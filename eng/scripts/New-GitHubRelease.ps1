[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$VersionBumpMatrixJson,
    [Parameter(Mandatory = $true)]
    [string]$Tag,
    [string]$PackageDirectory = "artifacts/nuget",
    [string]$ReleaseDirectory = "artifacts/release",
    [string]$NotesFile = "artifacts/github-release-notes.md"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$packageDirectoryPath = Join-Path $repoRoot $PackageDirectory
$releaseDirectoryPath = Join-Path $repoRoot $ReleaseDirectory
$notesFilePath = Join-Path $repoRoot $NotesFile
$parsedPackages = $VersionBumpMatrixJson | ConvertFrom-Json
$packages = @($parsedPackages)

if ($packages.Count -eq 0) {
    throw "At least one publishable package is required to create a GitHub Release."
}

if (-not $Tag.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Release tag '$Tag' must start with 'v'."
}

if (Test-Path $releaseDirectoryPath) {
    Get-ChildItem -Path $releaseDirectoryPath -File | Remove-Item -Force
}
else {
    New-Item -ItemType Directory -Path $releaseDirectoryPath | Out-Null
}

$notes = [System.Collections.Generic.List[string]]::new()
$notes.Add("## Published packages")
$notes.Add("")

foreach ($package in $packages) {
    $packageId = [string]$package.id
    $version = [string]$package.version
    $notes.Add("- $packageId $version")
}

foreach ($package in $packages) {
    $packageId = [string]$package.id
    $version = [string]$package.version
    $releaseNotePath = Join-Path $repoRoot "eng/release-notes/$packageId/$version.md"

    if (-not (Test-Path $releaseNotePath -PathType Leaf)) {
        throw "Release note '$releaseNotePath' was not found."
    }

    foreach ($extension in @("nupkg", "snupkg")) {
        $artifactName = "$packageId.$version.$extension"
        $artifactPath = Join-Path $packageDirectoryPath $artifactName
        if (-not (Test-Path $artifactPath -PathType Leaf)) {
            throw "Release artifact '$artifactPath' was not found."
        }

        Copy-Item -LiteralPath $artifactPath -Destination $releaseDirectoryPath
    }

    $packageNotes = (Get-Content $releaseNotePath -Raw).Trim()
    $versionHeading = "^##\s+$([regex]::Escape($version))\s*(?:\r?\n)+"
    $packageNotes = ([regex]::Replace($packageNotes, $versionHeading, "")).Trim()

    $notes.Add("")
    $notes.Add("## $packageId $version")
    $notes.Add("")
    $notes.Add($packageNotes)
}

$notesDirectory = Split-Path $notesFilePath -Parent
New-Item -ItemType Directory -Path $notesDirectory -Force | Out-Null
$notes.Add("")
$notes.Add("[Full changelog](https://github.com/$env:GITHUB_REPOSITORY/blob/$Tag/CHANGELOG.md)")
$notes -join "`n" | Set-Content -Path $notesFilePath -Encoding utf8

Write-Host "Prepared $($packages.Count) package(s) for GitHub Release $Tag."
Write-Host "Release notes: $notesFilePath"
Write-Host "Release assets: $releaseDirectoryPath"
