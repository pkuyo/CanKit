[CmdletBinding()]
param(
    [string]$ManifestPath = "eng/packages.json",
    [string]$VersionFilePath = "eng/package-versions.props",
    [string]$PackageDirectory = "artifacts/nuget",
    [string]$SmokeProject = "eng/package-smoke/CanKit.PackageSmoke.csproj"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression.FileSystem

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

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$manifest = Get-Content (Join-Path $repoRoot $ManifestPath) -Raw | ConvertFrom-Json
$packages = @($manifest.packages)
$versionMap = Get-VersionMap -XmlContent (Get-Content (Join-Path $repoRoot $VersionFilePath) -Raw) -Packages $packages
$packageDirectoryPath = Join-Path $repoRoot $PackageDirectory
$smokeProjectPath = Join-Path $repoRoot $SmokeProject

foreach ($package in $packages) {
    $packageId = [string]$package.id
    $version = [string]$versionMap[$packageId]
    $nupkgPath = Join-Path $packageDirectoryPath "$packageId.$version.nupkg"
    $snupkgPath = Join-Path $packageDirectoryPath "$packageId.$version.snupkg"

    if (-not (Test-Path $nupkgPath)) {
        throw "Missing package artifact '$nupkgPath'."
    }

    if (-not (Test-Path $snupkgPath)) {
        throw "Missing symbol package '$snupkgPath'."
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($nupkgPath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName })
        if (-not ($entries | Where-Object { $_ -eq "README.md" })) {
            throw "Package '$packageId' does not contain README.md."
        }

        if (-not ($entries | Where-Object { $_.StartsWith("lib/", [System.StringComparison]::OrdinalIgnoreCase) -or $_.StartsWith("ref/", [System.StringComparison]::OrdinalIgnoreCase) })) {
            throw "Package '$packageId' does not contain lib/ or ref/ assets."
        }
    }
    finally {
        $archive.Dispose()
    }
}

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("CanKit.PackageValidation." + [System.Guid]::NewGuid().ToString("N"))
$null = New-Item -ItemType Directory -Path $tempDirectory -Force
$configPath = Join-Path $tempDirectory "NuGet.Config"
$configContent = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$packageDirectoryPath" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@
Set-Content -Path $configPath -Value $configContent -Encoding UTF8

try {
    & dotnet restore $smokeProjectPath --configfile $configPath -p:UseLocalProjectReferences=false -p:GeneratePackageOnBuild=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed for the package smoke project."
    }

    & dotnet build $smokeProjectPath -c Release --no-restore -p:UseLocalProjectReferences=false -p:GeneratePackageOnBuild=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for the package smoke project."
    }
}
finally {
    Remove-Item -Path $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "NuGet package validation completed successfully."
