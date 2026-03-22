[CmdletBinding()]
param(
    [string]$BaseRef,
    [string]$HeadRef = "HEAD",
    [string]$ManifestPath = "eng/packages.json",
    [string]$VersionFilePath = "eng/package-versions.props"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Normalize-RelativePath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    return $Path.Replace("\", "/").TrimStart([char[]]("./"))
}

function Test-FileIncludedInChangeSet {
    param(
        [string[]]$ChangedFiles,
        [string]$Path
    )

    $normalizedTarget = Normalize-RelativePath $Path
    $normalizedChangedFiles = @(
        $ChangedFiles |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { Normalize-RelativePath ([string]$_) }
    )

    return $normalizedChangedFiles -contains $normalizedTarget
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$graphScript = Join-Path $PSScriptRoot "Get-PackageGraph.ps1"

$graph = & $graphScript `
    -BaseRef $BaseRef `
    -HeadRef $HeadRef `
    -ManifestPath $ManifestPath `
    -VersionFilePath $VersionFilePath |
    ConvertFrom-Json

if ($null -eq $graph.versionBumps -or @($graph.versionBumps).Count -eq 0) {
    Write-Host "No package version bumps detected. Release metadata validation skipped."
    exit 0
}

$changedFiles = @($graph.changedFiles | ForEach-Object { Normalize-RelativePath ([string]$_) })

# 只有拿得到可靠 BaseRef 时，才强制要求文件必须在本次 diff 里
$hasReliableBaseRef = -not [string]::IsNullOrWhiteSpace($BaseRef) -and $BaseRef -notmatch "^0+$"
$canValidateChangeSet = $hasReliableBaseRef -and $changedFiles.Count -gt 0

if ($canValidateChangeSet) {
    if (-not (Test-FileIncludedInChangeSet -ChangedFiles $changedFiles -Path "CHANGELOG.md")) {
        throw "Package versions changed, but CHANGELOG.md was not updated in the compared range ($BaseRef..$HeadRef)."
    }
}
else {
    Write-Warning "BaseRef is unavailable or changed file list is empty. Skipping CHANGELOG change-set validation."
}

foreach ($package in @($graph.versionBumps)) {
    $packageId = [string]$package.id
    $version = [string]$package.version
    $notePath = "eng/release-notes/$packageId/$version.md"
    $fullNotePath = Join-Path $repoRoot $notePath
    $normalizedNotePath = Normalize-RelativePath $notePath

    if (-not (Test-Path $fullNotePath)) {
        throw "Missing package release note file '$normalizedNotePath' for $packageId $version."
    }

    if ($canValidateChangeSet) {
        if (-not (Test-FileIncludedInChangeSet -ChangedFiles $changedFiles -Path $normalizedNotePath)) {
            throw "Package release note file '$normalizedNotePath' must be part of the compared range ($BaseRef..$HeadRef)."
        }
    }
    else {
        Write-Warning "Skipping change-set validation for '$normalizedNotePath' because BaseRef is unavailable or changed file list is empty."
    }
}

Write-Host "Release metadata validation completed successfully."