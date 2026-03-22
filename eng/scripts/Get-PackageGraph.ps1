[CmdletBinding()]
param(
    [string]$BaseRef,
    [string]$HeadRef = "HEAD",
    [string]$ManifestPath = "eng/packages.json",
    [string]$VersionFilePath = "eng/package-versions.props",
    [string]$GitHubOutputFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}

function Normalize-RelativePath {
    param([string]$Path)

    return $Path.Replace("\", "/").TrimStart([char[]]("./"))
}

function Get-VersionMap {
    param(
        [string]$XmlContent,
        [object[]]$Packages
    )

    $document = New-Object System.Xml.XmlDocument
    $document.LoadXml($XmlContent)

    $map = @{}
    foreach ($package in $Packages) {
        $propertyName = [string]$package.versionProperty
        $propertyNode = $document.SelectSingleNode("//$propertyName")
        if ($null -eq $propertyNode) {
            throw "Version property '$propertyName' was not found in package version file."
        }

        $map[[string]$package.id] = $propertyNode.InnerText.Trim()
    }

    return $map
}

function Get-ChangedFiles {
    param(
        [string]$RepoRoot,
        [string]$Base,
        [string]$Head
    )

    if ([string]::IsNullOrWhiteSpace($Base) -or $Base -match "^0+$") {
        return [pscustomobject]@{
            AssumeAllChanged = $true
            Files = @()
        }
    }

    $files = @(git -C $RepoRoot diff --name-only $Base $Head)
    return [pscustomobject]@{
        AssumeAllChanged = $false
        Files = @($files | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { Normalize-RelativePath $_ })
    }
}

function Get-ImpactedPackages {
    param(
        [object[]]$Packages,
        [string[]]$DirectPackageIds
    )

    $dependents = @{}
    foreach ($package in $Packages) {
        $packageId = [string]$package.id
        if (-not $dependents.ContainsKey($packageId)) {
            $dependents[$packageId] = [System.Collections.Generic.List[string]]::new()
        }
    }

    foreach ($package in $Packages) {
        foreach ($dependency in @($package.dependsOn)) {
            $dependencyId = [string]$dependency
            if (-not $dependents.ContainsKey($dependencyId)) {
                $dependents[$dependencyId] = [System.Collections.Generic.List[string]]::new()
            }

            $dependents[$dependencyId].Add([string]$package.id)
        }
    }

    $visited = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $queue = [System.Collections.Generic.Queue[string]]::new()
    foreach ($packageId in $DirectPackageIds) {
        if ($visited.Add($packageId)) {
            $queue.Enqueue($packageId)
        }
    }

    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        foreach ($dependent in $dependents[$current]) {
            if ($visited.Add($dependent)) {
                $queue.Enqueue($dependent)
            }
        }
    }

    return @($visited | Sort-Object)
}

$repoRoot = Resolve-RepoRoot
$manifestFile = Join-Path $repoRoot $ManifestPath
$versionFile = Join-Path $repoRoot $VersionFilePath

$manifest = Get-Content $manifestFile -Raw | ConvertFrom-Json
$packages = @($manifest.packages)
$sharedPathPrefixes = @($manifest.sharedPathPrefixes | ForEach-Object { Normalize-RelativePath ([string]$_) })
$currentVersionMap = Get-VersionMap -XmlContent (Get-Content $versionFile -Raw) -Packages $packages

$relativeVersionFilePath = Normalize-RelativePath $VersionFilePath
$baseVersionContent = $null
if (-not [string]::IsNullOrWhiteSpace($BaseRef) -and $BaseRef -notmatch "^0+$") {
    try {
        $baseVersionContent = git -C $repoRoot show "$BaseRef`:$relativeVersionFilePath" 2>$null
        if ([string]::IsNullOrWhiteSpace($baseVersionContent)) {
            $baseVersionContent = $null
        }
    }
    catch {
        $baseVersionContent = $null
    }
}

$baseVersionMap = if ($null -ne $baseVersionContent) {
    Get-VersionMap -XmlContent $baseVersionContent -Packages $packages
}
else {
    @{}
}

$versionBumps = @(
    foreach ($package in $packages) {
    if ($package.publish -eq $false) { continue }
    $packageId = [string]$package.id
    $currentVersion = [string]$currentVersionMap[$packageId]
    $baseVersion = if ($baseVersionMap.ContainsKey($packageId)) { [string]$baseVersionMap[$packageId] } else { "" }
    if ($baseVersion -ne $currentVersion) {
        [pscustomobject]@{
            id = $packageId
            project = [string]$package.project
            version = $currentVersion
        }
    }
}
)

$changeSet = Get-ChangedFiles -RepoRoot $repoRoot -Base $BaseRef -Head $HeadRef
$changedFiles = @($changeSet.Files)
$allPackageIds = @($packages | ForEach-Object { [string]$_.id } | Sort-Object)

$sharedChanged = $changeSet.AssumeAllChanged
if (-not $sharedChanged) {
    foreach ($changedFile in $changedFiles) {
        foreach ($sharedPathPrefix in $sharedPathPrefixes) {
            if ($changedFile.StartsWith($sharedPathPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                $sharedChanged = $true
                break
            }
        }

        if ($sharedChanged) {
            break
        }
    }
}

$directPackageIds = if ($sharedChanged) {
    $allPackageIds
}
else {
    $directHits = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($changedFile in $changedFiles) {
        foreach ($package in $packages) {
            foreach ($pathPrefix in @($package.pathPrefixes)) {
                $normalizedPrefix = Normalize-RelativePath ([string]$pathPrefix)
                if ($changedFile.StartsWith($normalizedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $null = $directHits.Add([string]$package.id)
                }
            }
        }
    }

    @($directHits | Sort-Object)
}

$impactedPackageIds = if ($sharedChanged) {
    $allPackageIds
}
else {
    Get-ImpactedPackages -Packages $packages -DirectPackageIds $directPackageIds
}

$versionBumpIds = @($versionBumps | ForEach-Object { $_.id })
$impactedMatrix = @(
    foreach ($packageId in $impactedPackageIds) {
    $package = $packages | Where-Object { [string]$_.id -eq $packageId } | Select-Object -First 1
    [pscustomobject]@{
        id = $packageId
        project = [string]$package.project
        version = [string]$currentVersionMap[$packageId]
    }
}
)

$result = [pscustomobject]@{
    changedFiles = $changedFiles
    sharedChanged = $sharedChanged
    directPackageIds = $directPackageIds
    impactedPackageIds = $impactedPackageIds
    impactedMatrix = $impactedMatrix
    versionBumpIds = $versionBumpIds
    versionBumps = $versionBumps
}

if (-not [string]::IsNullOrWhiteSpace($GitHubOutputFile)) {
    $versionBumpJson = $versionBumps | ConvertTo-Json -Compress
    $impactedMatrixJson = $impactedMatrix | ConvertTo-Json -Compress

    Add-Content -Path $GitHubOutputFile -Value "has_version_bumps=$([string]($versionBumps.Count -gt 0).ToLowerInvariant())"
    Add-Content -Path $GitHubOutputFile -Value "version_bump_ids=$($versionBumpIds -join ',')"
    Add-Content -Path $GitHubOutputFile -Value "version_bump_matrix=$versionBumpJson"
    Add-Content -Path $GitHubOutputFile -Value "impacted_package_ids=$($impactedPackageIds -join ',')"
    Add-Content -Path $GitHubOutputFile -Value "impacted_package_matrix=$impactedMatrixJson"
}

$result | ConvertTo-Json -Depth 10
