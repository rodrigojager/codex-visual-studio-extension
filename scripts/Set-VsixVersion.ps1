param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$AssemblyVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "A versão deve estar no formato major.minor.patch, por exemplo 1.2.3."
}

if ([string]::IsNullOrWhiteSpace($AssemblyVersion)) {
    $AssemblyVersion = "$Version.0"
}

if ($AssemblyVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "A versão de assembly deve estar no formato major.minor.patch.revision, por exemplo 1.2.3.0."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Save-Utf8Xml {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [xml]$Xml
    )

    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Encoding = $utf8NoBom
    $settings.Indent = $true
    $settings.OmitXmlDeclaration = $false

    $writer = [System.Xml.XmlWriter]::Create($Path, $settings)
    try {
        $Xml.Save($writer)
    }
    finally {
        $writer.Dispose()
    }
}

$sourceManifestPath = Join-Path $repoRoot "CodexVsix\source.extension.vsixmanifest"
[xml]$sourceManifest = Get-Content -LiteralPath $sourceManifestPath -Raw -Encoding UTF8
$vsixNs = New-Object System.Xml.XmlNamespaceManager($sourceManifest.NameTable)
$vsixNs.AddNamespace("vsix", "http://schemas.microsoft.com/developer/vsx-schema/2011")
$sourceIdentity = $sourceManifest.SelectSingleNode("/vsix:PackageManifest/vsix:Metadata/vsix:Identity", $vsixNs)
if (-not $sourceIdentity) {
    throw "Não foi possível localizar o elemento Identity em source.extension.vsixmanifest."
}
$sourceIdentity.SetAttribute("Version", $Version)
Save-Utf8Xml -Path $sourceManifestPath -Xml $sourceManifest

$legacyManifestPath = Join-Path $repoRoot "CodexVsix\extension.vsixmanifest"
[xml]$legacyManifest = Get-Content -LiteralPath $legacyManifestPath -Raw -Encoding UTF8
$legacyNs = New-Object System.Xml.XmlNamespaceManager($legacyManifest.NameTable)
$legacyNs.AddNamespace("vsix", "http://schemas.microsoft.com/developer/vsx-schema/2011")
$legacyVersionNode = $legacyManifest.SelectSingleNode("/vsix:Vsix/vsix:Identifier/vsix:Version", $legacyNs)
if (-not $legacyVersionNode) {
    throw "Não foi possível localizar o elemento Version em extension.vsixmanifest."
}
$legacyVersionNode.InnerText = $Version
Save-Utf8Xml -Path $legacyManifestPath -Xml $legacyManifest

$assemblyInfoPath = Join-Path $repoRoot "CodexVsix\Properties\AssemblyInfo.cs"
$assemblyInfo = Get-Content -LiteralPath $assemblyInfoPath -Raw -Encoding UTF8
$assemblyInfo = [regex]::Replace($assemblyInfo, 'AssemblyVersion\("([^"]+)"\)', "AssemblyVersion(`"$AssemblyVersion`")")
$assemblyInfo = [regex]::Replace($assemblyInfo, 'AssemblyFileVersion\("([^"]+)"\)', "AssemblyFileVersion(`"$AssemblyVersion`")")
[System.IO.File]::WriteAllText($assemblyInfoPath, $assemblyInfo, $utf8NoBom)
