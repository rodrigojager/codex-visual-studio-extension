[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$VsixPath,

    [string]$ExpectedVersion,

    [switch]$RequireSignature
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resolvedVsix = (Resolve-Path -LiteralPath $VsixPath).Path
$workDirectory = Join-Path ([IO.Path]::GetTempPath()) ("codex-vsix-verify-" + [Guid]::NewGuid().ToString("N"))
$zipPath = Join-Path $workDirectory "package.zip"
$extractPath = Join-Path $workDirectory "package"

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][System.IO.Stream]$Stream)

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha256.ComputeHash($Stream))).Replace("-", "")
    }
    finally {
        $sha256.Dispose()
    }
}

try {
    New-Item -ItemType Directory -Path $workDirectory | Out-Null
    Copy-Item -LiteralPath $resolvedVsix -Destination $zipPath
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extractPath

    $requiredFiles = @(
        "[Content_Types].xml",
        "extension.vsixmanifest",
        "CodexVsix.dll",
        "CodexVsix.pkgdef",
        "Menus.ctmenu",
        "Newtonsoft.Json.dll",
        "Microsoft.Web.WebView2.Core.dll",
        "Microsoft.Web.WebView2.Wpf.dll",
        "LICENSE",
        "THIRD-PARTY-NOTICES.md",
        "CONTRIBUTORS.md",
        "Resources\MarketplaceIcon.png",
        "UI\CodexWebview\webview\index.html",
        "UI\CodexWebview\codex-acquire-vscode-api-shim.js",
        "UI\CodexWebview\codex-visual-studio-history-guard.js",
        "UI\CodexWebview\codex-visual-studio-diagnostics.js"
    )

    foreach ($relativePath in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $extractPath $relativePath))) {
            throw "Required VSIX entry is missing: $relativePath"
        }
    }

    $sensitiveFileNames = @(".env", "auth.json", "credentials.json", "secrets.json")
    $sensitiveExtensions = @(".pfx", ".p12", ".pem", ".key", ".snk", ".jks", ".keystore")
    $sensitiveEntries = @(Get-ChildItem -LiteralPath $extractPath -Recurse -File | Where-Object {
        $lowerName = $_.Name.ToLowerInvariant()
        $lowerExtension = $_.Extension.ToLowerInvariant()
        $sensitiveFileNames -contains $lowerName -or
            ($lowerName.StartsWith(".env.", [StringComparison]::Ordinal) -and $lowerName -ne ".env.example") -or
            $sensitiveExtensions -contains $lowerExtension
    })
    if ($sensitiveEntries.Count -gt 0) {
        $entryNames = ($sensitiveEntries | ForEach-Object { $_.FullName.Substring($extractPath.Length).TrimStart("\", "/") }) -join ", "
        throw "The VSIX contains credential or signing-material files: $entryNames"
    }

    $webviewFiles = @(Get-ChildItem -LiteralPath (Join-Path $extractPath "UI\CodexWebview") -Recurse -File)
    if ($webviewFiles.Count -lt 1304) {
        throw "The VSIX contains only $($webviewFiles.Count) official Codex webview files; at least 1304 are required."
    }

    $duplicatedWebviewPaths = @($webviewFiles | Where-Object {
        $_.FullName.Replace("/", "\") -match "UI\\CodexWebview\\.*UI\\CodexWebview\\"
    })
    if ($duplicatedWebviewPaths.Count -gt 0) {
        throw "The official Codex webview was packaged under a duplicated UI/CodexWebview path."
    }

    $localeBundles = @($webviewFiles | Where-Object {
        $_.DirectoryName -like "*UI\CodexWebview\webview\assets" -and
        $_.Length -gt 300000 -and
        $_.BaseName -match "^[a-z]{2}(?:-[A-Z0-9]{2,3})?-"
    })
    if ($localeBundles.Count -lt 60) {
        throw "The VSIX contains only $($localeBundles.Count) official locale bundles; at least 60 are required."
    }

    foreach ($localePrefix in @("pt-BR-", "pt-PT-")) {
        if (-not ($localeBundles | Where-Object { $_.Name.StartsWith($localePrefix, [StringComparison]::Ordinal) })) {
            throw "Required official locale bundle is missing: $localePrefix"
        }
    }

    if (Test-Path -LiteralPath (Join-Path $extractPath "MessagePack.dll")) {
        throw "MessagePack.dll must not be shipped; it is a build-time transitive override only."
    }

    $newtonsoftPath = Join-Path $extractPath "Newtonsoft.Json.dll"
    $newtonsoftFileVersion = (Get-Item -LiteralPath $newtonsoftPath).VersionInfo.FileVersion
    if (-not $newtonsoftFileVersion.StartsWith("13.0.3.", [StringComparison]::Ordinal)) {
        throw "Packaged Newtonsoft.Json version '$newtonsoftFileVersion' is not compatible with the 13.0.3 API surface hosted by supported Visual Studio versions."
    }

    $manifestPath = Join-Path $extractPath "extension.vsixmanifest"
    [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8
    $namespaceManager = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
    $namespaceManager.AddNamespace("vsix", "http://schemas.microsoft.com/developer/vsx-schema/2011")
    $identity = $manifest.SelectSingleNode("/vsix:PackageManifest/vsix:Metadata/vsix:Identity", $namespaceManager)
    if (-not $identity) {
        throw "The packaged VSIX manifest does not contain Metadata/Identity."
    }

    $releaseNotes = $manifest.SelectSingleNode("/vsix:PackageManifest/vsix:Metadata/vsix:ReleaseNotes", $namespaceManager)
    if ($releaseNotes -and -not [string]::IsNullOrWhiteSpace($releaseNotes.InnerText)) {
        $releaseNotesPath = $releaseNotes.InnerText.Trim()
        if (-not [Uri]::IsWellFormedUriString($releaseNotesPath, [UriKind]::Absolute) -and
            -not (Test-Path -LiteralPath (Join-Path $extractPath $releaseNotesPath) -PathType Leaf)) {
            throw "The manifest references a release notes file that is missing from the VSIX: $releaseNotesPath"
        }
    }

    $manifestVersion = [string]$identity.Version
    if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and $manifestVersion -ne $ExpectedVersion) {
        throw "Packaged manifest version '$manifestVersion' does not match '$ExpectedVersion'."
    }

    $assemblyPath = Join-Path $extractPath "CodexVsix.dll"
    $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version.ToString()
    if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and $assemblyVersion -ne "$ExpectedVersion.0") {
        throw "Packaged assembly version '$assemblyVersion' does not match '$ExpectedVersion.0'."
    }

    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($assemblyPath))
    $resource = $assembly.GetManifestResourceStream("CodexVsix.UI.Assets.mermaid.min.js")
    if (-not $resource) {
        throw "The embedded Mermaid resource is missing from CodexVsix.dll."
    }

    try {
        $mermaidHash = Get-Sha256Hex -Stream $resource
    }
    finally {
        $resource.Dispose()
    }

    $expectedMermaidHash = "70137E77BB273BB2EF972B86E8B0400CCA8BE53CB25BFC45911A186DC98665DE"
    if ($mermaidHash -ne $expectedMermaidHash) {
        throw "The packaged Mermaid resource hash '$mermaidHash' does not match the reviewed 11.15.0 bundle."
    }

    $signatureParts = Get-ChildItem -LiteralPath $extractPath -Recurse -File |
        Where-Object { $_.FullName -match '[\\/]package[\\/]services[\\/]digital-signature[\\/]' }
    $isSigned = @($signatureParts).Count -gt 0
    if ($RequireSignature -and -not $isSigned) {
        throw "The VSIX does not contain an OPC digital signature."
    }

    $packageHash = (Get-FileHash -LiteralPath $resolvedVsix -Algorithm SHA256).Hash
    Write-Output "VSIX=$resolvedVsix"
    Write-Output "VERSION=$manifestVersion"
    Write-Output "ASSEMBLY_VERSION=$assemblyVersion"
    Write-Output "NEWTONSOFT_FILE_VERSION=$newtonsoftFileVersion"
    Write-Output "WEBVIEW_FILES=$($webviewFiles.Count)"
    Write-Output "LOCALE_BUNDLES=$($localeBundles.Count)"
    Write-Output "SHA256=$packageHash"
    Write-Output "SIGNED=$($isSigned.ToString().ToLowerInvariant())"
}
finally {
    if (Test-Path -LiteralPath $workDirectory) {
        $resolvedWorkDirectory = [IO.Path]::GetFullPath($workDirectory)
        $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if ($resolvedWorkDirectory.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedWorkDirectory -Recurse -Force
        }
    }
}
