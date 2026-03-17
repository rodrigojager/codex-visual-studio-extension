param(
    [Parameter(Mandatory = $true)]
    [string]$PublisherName,

    [string]$InternalName = "codex-for-visual-studio",

    [string]$VsixPath = "CodexVsix\CodexVsix.vsix",

    [string]$OverviewFile = "marketplace\overview.md",

    [string]$RepositoryUrl,

    [string]$PersonalAccessToken,

    [string[]]$IgnoreWarnings = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $candidate = $Path
    if (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path $repoRoot $candidate
    }

    return (Resolve-Path $candidate).Path
}

if ([string]::IsNullOrWhiteSpace($RepositoryUrl)) {
    try {
        $RepositoryUrl = (git remote get-url origin).Trim()
    }
    catch {
        $RepositoryUrl = "https://github.com/rodrigojager/codex-visual-studio-extension"
    }
}

$vsixFullPath = Resolve-RepoPath -Path $VsixPath
$overviewFullPath = Resolve-RepoPath -Path $OverviewFile

$vsixPublisherRoot = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.vssdk.buildtools"
$vsixPublisherExe = Get-ChildItem $vsixPublisherRoot -Directory |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName "tools\vssdk\bin\lib\VsixPublisher.exe" } |
    Where-Object { Test-Path $_ } |
    Select-Object -First 1

if (-not $vsixPublisherExe) {
    throw "VsixPublisher.exe não foi encontrado em $vsixPublisherRoot."
}

$publishManifest = [ordered]@{
    identity = @{
        internalName = $InternalName
    }
    assetFiles = @()
    categories = @("Coding")
    overview = $overviewFullPath
    publisher = $PublisherName
    qna = $true
    repo = $RepositoryUrl
}

if ($IgnoreWarnings.Count -gt 0) {
    $publishManifest.ignoreWarnings = $IgnoreWarnings
}

$publishManifestPath = Join-Path ([System.IO.Path]::GetTempPath()) "codex-marketplace-publish.json"
$publishManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $publishManifestPath -Encoding UTF8

$arguments = @(
    "publish"
    "-payload", $vsixFullPath
    "-publishManifest", $publishManifestPath
)

if (-not [string]::IsNullOrWhiteSpace($PersonalAccessToken)) {
    $arguments += @("-personalAccessToken", $PersonalAccessToken)
}

if ($IgnoreWarnings.Count -gt 0) {
    $arguments += @("-ignoreWarnings", ($IgnoreWarnings -join ","))
}

Write-Host "Publishing $vsixFullPath to the Visual Studio Marketplace..."
Write-Host "Publisher: $PublisherName"
Write-Host "Internal name: $InternalName"

& $vsixPublisherExe @arguments

if ($LASTEXITCODE -ne 0) {
    throw "VsixPublisher falhou com código $LASTEXITCODE."
}
