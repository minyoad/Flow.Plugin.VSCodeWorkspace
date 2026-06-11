# Release script for Flow.Plugin.VSCodeWorkspaces
# This script builds the project and creates a versioned ZIP package

$ErrorActionPreference = "Continue"

# Read version from plugin.json
Write-Host "Reading plugin.json..." -ForegroundColor Cyan
$pluginJson = Get-Content 'plugin.json' -Raw | ConvertFrom-Json
$version = $pluginJson.Version
$pluginName = "VSCodeWorkspaces"
$outputDir = "Output\Release\$pluginName\net7.0-windows"
$zipFileName = "$pluginName-v$version.zip"

Write-Host "Plugin Name: $pluginName" -ForegroundColor Gray
Write-Host "Version: $version" -ForegroundColor Gray
Write-Host "Output Directory: $outputDir" -ForegroundColor Gray
Write-Host "ZIP Filename: $zipFileName" -ForegroundColor Gray

# Remove old ZIP if exists
if (Test-Path $zipFileName) {
    Write-Host "Removing old ZIP: $zipFileName" -ForegroundColor Yellow
    Remove-Item $zipFileName -Force
}

# Build Release version
Write-Host "`nBuilding Release version..." -ForegroundColor Yellow
dotnet build --configuration Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed! Exit code: $LASTEXITCODE" -ForegroundColor Red
    exit 1
}
Write-Host "Build succeeded!" -ForegroundColor Green

# Check if output directory exists
if (-not (Test-Path $outputDir)) {
    Write-Host "Output directory does not exist: $outputDir" -ForegroundColor Red
    exit 1
}

Write-Host "Output directory exists, starting packaging..." -ForegroundColor Gray

# Create ZIP package
Write-Host "Creating ZIP package: $zipFileName..." -ForegroundColor Yellow

# Enter output directory to ensure ZIP root contains plugin files directly
$currentLocation = Get-Location
Set-Location $outputDir
Compress-Archive -Path * -DestinationPath "$currentLocation\$zipFileName" -Force
Set-Location $currentLocation

if (Test-Path $zipFileName) {
    $zipItem = Get-Item $zipFileName
    $zipSize = $zipItem.Length / 1MB
    Write-Host "`n✅ Release succeeded!" -ForegroundColor Green
    Write-Host "📦 ZIP created: $zipFileName" -ForegroundColor Green
    Write-Host "📊 File size: $($zipSize.ToString('0.00')) MB" -ForegroundColor Green
    Write-Host "📍 Location: $($zipItem.FullName)" -ForegroundColor Green
} else {
    Write-Host "❌ Failed to create ZIP! File not generated" -ForegroundColor Red
    exit 1
}