[CmdletBinding()]
param(
    [string]$ManifestPath = "eng/packages.json",
    [string]$PackageDirectory = "artifacts/nuget",
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$manifestFile = Join-Path $repoRoot $ManifestPath
$outputDirectory = Join-Path $repoRoot $PackageDirectory
$manifest = Get-Content $manifestFile -Raw | ConvertFrom-Json

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

Get-ChildItem -Path $outputDirectory -Filter *.nupkg -File -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -Path $outputDirectory -Filter *.snupkg -File -ErrorAction SilentlyContinue | Remove-Item -Force

foreach ($package in @($manifest.packages)) {
    $projectPath = Join-Path $repoRoot ([string]$package.project)
    Write-Host "Packing $($package.id)"
    & dotnet pack $projectPath -c $Configuration --no-restore -o $outputDirectory -p:UseLocalProjectReferences=true -p:GeneratePackageOnBuild=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed for $($package.id)."
    }
}

Write-Host "NuGet packing completed."
