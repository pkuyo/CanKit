[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$VersionBumpMatrixJson,
    [Parameter(Mandatory = $true)]
    [string]$Source,
    [Parameter(Mandatory = $true)]
    [string]$ApiKey,
    [string]$PackageDirectory = "artifacts/nuget"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$packageDirectoryPath = Join-Path $repoRoot $PackageDirectory
$packages = @($VersionBumpMatrixJson | ConvertFrom-Json)

if (@($packages).Count -eq 0) {
    Write-Host "No package version bumps supplied. Nothing to publish."
    exit 0
}

foreach ($package in $packages) {
    $packageId = [string]$package.id
    $version = [string]$package.version
    $nupkgPath = Join-Path $packageDirectoryPath "$packageId.$version.nupkg"
    $snupkgPath = Join-Path $packageDirectoryPath "$packageId.$version.snupkg"

    foreach ($artifactPath in @($nupkgPath, $snupkgPath)) {
        if (-not (Test-Path $artifactPath)) {
            throw "Publish artifact '$artifactPath' was not found."
        }

        Write-Host "Publishing $(Split-Path $artifactPath -Leaf)"
        & dotnet nuget push $artifactPath --source $Source --api-key $ApiKey --skip-duplicate
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet nuget push failed for '$artifactPath'."
        }
    }
}

Write-Host "NuGet publish completed successfully."
