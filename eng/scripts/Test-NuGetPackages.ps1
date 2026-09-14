[CmdletBinding()]
param(
    [string]$ManifestPath = "eng/packages.json",
    [string]$VersionFilePath = "eng/package-versions.props",
    [string]$PackageDirectory = "artifacts/nuget",
    [string]$SmokeProject = "eng/package-smoke/CanKit.PackageSmoke.csproj",
    [string]$AotSmokeProject = "eng/aot-smoke/CanKit.AotSmoke.csproj"
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
$manifest = Get-Content (Join-Path $repoRoot $ManifestPath) -Raw -Encoding UTF8 | ConvertFrom-Json
$packages = @($manifest.packages)
$versionMap = Get-VersionMap -XmlContent (Get-Content (Join-Path $repoRoot $VersionFilePath) -Raw -Encoding UTF8) -Packages $packages
$packageDirectoryPath = Join-Path $repoRoot $PackageDirectory
$smokeProjectPath = Join-Path $repoRoot $SmokeProject
$aotSmokeProjectPath = Join-Path $repoRoot $AotSmokeProject

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

$temporaryRootPath = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
$tempDirectory = Join-Path $temporaryRootPath ("CanKit.PackageValidation." + [System.Guid]::NewGuid().ToString("N"))
$null = New-Item -ItemType Directory -Path $tempDirectory -Force
$configPath = Join-Path $tempDirectory "NuGet.Config"
$packagesPath = Join-Path $tempDirectory "packages"
$smokeArtifactsPath = Join-Path $tempDirectory "package-smoke"
$escapedPackageDirectory = [System.Security.SecurityElement]::Escape($packageDirectoryPath)
$configContent = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$escapedPackageDirectory" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="CanKit.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
Set-Content -Path $configPath -Value $configContent -Encoding UTF8

try {
    & dotnet restore $smokeProjectPath --configfile $configPath --packages $packagesPath --artifacts-path $smokeArtifactsPath -p:UseLocalProjectReferences=false -p:GeneratePackageOnBuild=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed for the package smoke project."
    }

    & dotnet build $smokeProjectPath -c Release --no-restore --artifacts-path $smokeArtifactsPath -p:UseLocalProjectReferences=false -p:GeneratePackageOnBuild=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for the package smoke project."
    }

    & dotnet run --project $smokeProjectPath -c Release -f net10.0 --no-build --artifacts-path $smokeArtifactsPath -p:UseLocalProjectReferences=false -p:GeneratePackageOnBuild=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet run failed for the package smoke project."
    }

    foreach ($mode in @("aot", "trimmed")) {
        $publishAot = ($mode -eq "aot").ToString().ToLowerInvariant()
        $modeArtifactsPath = Join-Path $tempDirectory $mode
        $publishPath = Join-Path $modeArtifactsPath "publish"
        $publishProperties = @("-p:PublishAot=$publishAot", "-p:GeneratePackageOnBuild=false")
        Write-Host "Validating $mode publish (win-x64)"

        & dotnet restore $aotSmokeProjectPath --configfile $configPath --packages $packagesPath --artifacts-path $modeArtifactsPath -r win-x64 -p:Configuration=Release @publishProperties
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed for the $mode smoke project."
        }

        & dotnet publish $aotSmokeProjectPath -c Release -r win-x64 --no-restore --artifacts-path $modeArtifactsPath -o $publishPath @publishProperties
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed for the $mode smoke project."
        }

        & (Join-Path $publishPath "CanKit.AotSmoke.exe")
        if ($LASTEXITCODE -ne 0) {
            throw "The $mode smoke executable failed."
        }
    }
}
finally {
    $cleanupPath = [System.IO.Path]::GetFullPath($tempDirectory)
    if ([System.IO.Path]::GetDirectoryName($cleanupPath) -ne $temporaryRootPath -or
        -not [System.IO.Path]::GetFileName($cleanupPath).StartsWith("CanKit.PackageValidation.", [System.StringComparison]::Ordinal)) {
        throw "Unexpected package validation cleanup path '$cleanupPath'."
    }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "NuGet package validation completed successfully."
