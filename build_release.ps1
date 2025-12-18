# Description: This script builds and packages the EDMCP project for release.
# This script should be run from the root folder containing EDMCP.sln

param(
    [switch]$SkipTest = $false
)

# Check we're in the right directory
if (!(Test-Path -Path ".\EDMCP.sln")) {
    Write-Error "This script must be run from the root EDMCP folder containing EDMCP.sln"
    exit 1
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  EDMCP Release Build Script" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Clean previous builds
Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
dotnet clean -c Release
Remove-Item -Recurse -Force -Path ".\EDMCP\bin" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force -Path ".\EDMCP\obj" -ErrorAction SilentlyContinue
Write-Host "? Cleaned`n" -ForegroundColor Green

# Publish the executable
Write-Host "Publishing self-contained executable..." -ForegroundColor Yellow
Write-Host "  Framework: .NET 10.0" -ForegroundColor Gray
Write-Host "  Configuration: Release" -ForegroundColor Gray
Write-Host "  Runtime: win-x64" -ForegroundColor Gray
dotnet publish .\EDMCP\EDMCP.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true
Write-Host ""

# Verify the build succeeded
$exePath = ".\EDMCP\bin\Release\net10.0\win-x64\publish\EDMCP.exe"
if (!(Test-Path -Path $exePath)) {
    Write-Error "? Build failed - EDMCP.exe not found at: $exePath`n"
    exit 1
}
Write-Host "? Build successful`n" -ForegroundColor Green

# Get file size
$fileSize = (Get-Item $exePath).Length
$fileSizeMB = [math]::Round($fileSize / 1MB, 2)
Write-Host "Executable Details:" -ForegroundColor Cyan
Write-Host "  Path: $exePath" -ForegroundColor Gray
Write-Host "  Size: $fileSizeMB MB ($([int]$fileSize) bytes)" -ForegroundColor Gray
Write-Host ""

# Create the release package
Write-Host "Creating release package..." -ForegroundColor Yellow
$publishPath = ".\EDMCP\bin\Release\net10.0\win-x64\publish"

# Remove old zip if it exists
if (Test-Path ".\EDMCP_server.zip") {
    Remove-Item ".\EDMCP_server.zip" -Force
}

# Create zip with the published files
Compress-Archive -Path "$publishPath\*" -DestinationPath ".\EDMCP_server.zip" -Force

# Add the startup script
Compress-Archive -Update -Path ".\start-edmcp.bat" -DestinationPath ".\EDMCP_server.zip"

$zipSize = (Get-Item ".\EDMCP_server.zip").Length
$zipSizeMB = [math]::Round($zipSize / 1MB, 2)

Write-Host "? Package created: EDMCP_server.zip" -ForegroundColor Green
Write-Host "  Size: $zipSizeMB MB`n" -ForegroundColor Gray

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "========================================`n" -ForegroundColor Cyan

Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Extract EDMCP_server.zip to your deployment folder" -ForegroundColor Gray
Write-Host "  2. Run: start-edmcp.bat" -ForegroundColor Gray
Write-Host "  3. Test: Invoke-WebRequest http://localhost:5000/health`n" -ForegroundColor Gray

if (!$SkipTest) {
    Write-Host "Testing executable..." -ForegroundColor Yellow
    #set ASPNETCORE_URLS=http://0.0.0.0:5242
    #default port is 5000
    $exeProcess = Start-Process -FilePath ".\EDMCP\bin\Release\net10.0\win-x64\publish\EDMCP.exe" -PassThru -NoNewWindow
    Start-Sleep -Seconds 3
    
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:5000/health" -ErrorAction Stop
        Write-Host "? Server is running and responding!" -ForegroundColor Green
        Write-Host "  Response: $($response.Content)`n" -ForegroundColor Gray
        
        # Kill the test instance
        $exeProcess | Stop-Process -Force -ErrorAction SilentlyContinue
    } catch {
        Write-Warning "? Could not test server - that's ok, the build succeeded anyway"
        $exeProcess | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}
