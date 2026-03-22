[CmdletBinding()]
param(
    [string]$BaseRef,
    [string]$HeadRef = "HEAD",
    [string]$ManifestPath = "eng/packages.json",
    [string]$VersionFilePath = "eng/package-versions.props"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$graphScript = Join-Path $PSScriptRoot "Get-PackageGraph.ps1"
$graph = & $graphScript -BaseRef $BaseRef -HeadRef $HeadRef -ManifestPath $ManifestPath -VersionFilePath $VersionFilePath | ConvertFrom-Json

if ($null -eq $graph.versionBumps -or @($graph.versionBumps).Count -eq 0) {
    Write-Host "No package version bumps detected. Release metadata validation skipped."
    exit 0
}

$changedFiles = @($graph.changedFiles)
if (-not ($changedFiles -contains "CHANGELOG.md")) {
    throw "Package versions changed, but CHANGELOG.md was not updated in the same revision."
}

foreach ($package in @($graph.versionBumps)) {
    $packageId = [string]$package.id
    $version = [string]$package.version
    $notePath = "eng/release-notes/$packageId/$version.md"
    $fullNotePath = Join-Path $repoRoot $notePath

    if (-not (Test-Path $fullNotePath)) {
        throw "Missing package release note file '$notePath' for $packageId $version."
    }

    $normalizedNotePath = $notePath.Replace("\", "/")
    if (-not ($changedFiles -contains $normalizedNotePath)) {
        throw "Package release note file '$normalizedNotePath' must be part of the same change set."
    }
}

Write-Host "Release metadata validation completed successfully."
