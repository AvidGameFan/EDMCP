# Description: This script builds and packages the EDMCP project for Linux.
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
Write-Host "  EDMCP Release Build Script (Linux)" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Clean previous builds
Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
dotnet clean -c Release
Remove-Item -Recurse -Force -Path ".\EDMCP\bin" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force -Path ".\EDMCP\obj" -ErrorAction SilentlyContinue
Write-Host "? Cleaned`n" -ForegroundColor Green

# Publish the executable
Write-Host "Publishing self-contained executable for Linux..." -ForegroundColor Yellow
Write-Host "  Framework: .NET 10.0" -ForegroundColor Gray
Write-Host "  Configuration: Release" -ForegroundColor Gray
Write-Host "  Runtime: linux-x64" -ForegroundColor Gray
dotnet publish .\EDMCP\EDMCP.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true
Write-Host ""

# Verify the build succeeded
$exePath = ".\EDMCP\bin\Release\net10.0\linux-x64\publish\EDMCP"
if (!(Test-Path -Path $exePath)) {
    Write-Error "? Build failed - EDMCP executable not found at: $exePath`n"
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
$publishPath = ".\EDMCP\bin\Release\net10.0\linux-x64\publish"

# Remove old tar.gz if it exists
if (Test-Path ".\EDMCP_server_linux.tar.gz") {
    Remove-Item ".\EDMCP_server_linux.tar.gz" -Force
}

# Create temporary directory for packaging
$tempDir = ".\temp_linux_package"
if (Test-Path $tempDir) {
    Remove-Item -Recurse -Force $tempDir
}
New-Item -ItemType Directory -Path $tempDir | Out-Null

# Copy executable
Copy-Item "$publishPath\EDMCP" -Destination $tempDir

# Create startup script in temp directory
$startupScript = @"
#!/bin/bash
# EDMCP Server Startup Script
# This script is designed to run from the EDMCP release package root directory

export ASPNETCORE_URLS="http://0.0.0.0:5242"
export EASY_DIFFUSION_ADDRESS="`${EASY_DIFFUSION_ADDRESS:-http://localhost:9000}"

# Uncomment and modify the line below if Easy Diffusion is on a different machine
# export EASY_DIFFUSION_ADDRESS="http://192.168.1.100:9000"

echo ""
echo "======================================"
echo "   EDMCP MCP Server"
echo "======================================"
echo "Listening on: `$ASPNETCORE_URLS"
echo "Easy Diffusion: `$EASY_DIFFUSION_ADDRESS"
echo ""

if [ ! -f "EDMCP" ]; then
    echo "ERROR: EDMCP executable not found in current directory!"
    echo ""
    echo "This script should be run from the EDMCP release package directory."
    echo "If you extracted EDMCP_server_linux.tar.gz, run this script from that directory."
    echo ""
    read -p "Press Enter to exit..."
    exit 1
fi

# Make sure EDMCP is executable
chmod +x EDMCP

echo "Starting EDMCP..."
echo ""

./EDMCP

if [ `$? -ne 0 ]; then
    echo ""
    echo "ERROR: EDMCP exited with code `$?"
    echo ""
    read -p "Press Enter to exit..."
fi
"@

$startupScript | Out-File -FilePath "$tempDir\start-edmcp.sh" -Encoding UTF8 -NoNewline

# Create README for Linux package
$readmeContent = @"
# EDMCP Server - Linux Package

## Quick Start

1. Extract this archive:
   ``````bash
   tar -xzf EDMCP_server_linux.tar.gz
   cd edmcp
   ``````

2. Make the startup script executable:
   ``````bash
   chmod +x start-edmcp.sh
   ``````

3. Run the server:
   ``````bash
   ./start-edmcp.sh
   ``````

## Configuration

### Easy Diffusion on Different Machine

Edit `start-edmcp.sh` or set environment variable:

``````bash
export EASY_DIFFUSION_ADDRESS=http://192.168.1.100:9000
./start-edmcp.sh
``````

### Change Port

Edit `start-edmcp.sh` and modify:
``````bash
export ASPNETCORE_URLS="http://0.0.0.0:8080"
``````

## Testing

``````bash
# Health check
curl http://localhost:5242/health

# Should respond: {"status":"ok"}
``````

## Run as systemd Service

Create `/etc/systemd/system/edmcp.service`:

``````ini
[Unit]
Description=EDMCP MCP Server
After=network.target

[Service]
Type=simple
User=your-username
WorkingDirectory=/opt/edmcp
ExecStart=/opt/edmcp/EDMCP
Restart=on-failure
RestartSec=10
Environment="ASPNETCORE_URLS=http://0.0.0.0:5242"
Environment="EASY_DIFFUSION_ADDRESS=http://localhost:9000"

[Install]
WantedBy=multi-user.target
``````

Enable and start:
``````bash
sudo systemctl daemon-reload
sudo systemctl enable edmcp
sudo systemctl start edmcp
sudo systemctl status edmcp
``````

## Troubleshooting

### Permission Denied
``````bash
chmod +x EDMCP
chmod +x start-edmcp.sh
``````

### Port Already in Use
``````bash
# Find what's using port 5242
sudo lsof -i :5242
# Kill the process
sudo kill -9 <PID>
``````

### libicu Missing (Arch Linux)
``````bash
sudo pacman -S icu
``````

## Requirements

- Linux x64 (Arch, Ubuntu, Debian, Fedora, etc.)
- No .NET runtime required (self-contained)
- Easy Diffusion running (default: http://localhost:9000)

## Support

For more information, visit: https://github.com/AvidGameFan/EDMCP
"@

$readmeContent | Out-File -FilePath "$tempDir\README_LINUX.md" -Encoding UTF8

# Use tar.exe (built into Windows 10+) to create tar.gz
Write-Host "Creating tar.gz archive..." -ForegroundColor Yellow
$tarPath = Resolve-Path $tempDir
Push-Location $tempDir
tar -czf "..\EDMCP_server_linux.tar.gz" *
Pop-Location

# Clean up temp directory
Remove-Item -Recurse -Force $tempDir

$tarSize = (Get-Item ".\EDMCP_server_linux.tar.gz").Length
$tarSizeMB = [math]::Round($tarSize / 1MB, 2)

Write-Host "? Package created: EDMCP_server_linux.tar.gz" -ForegroundColor Green
Write-Host "  Size: $tarSizeMB MB`n" -ForegroundColor Gray

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "========================================`n" -ForegroundColor Cyan

Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Transfer EDMCP_server_linux.tar.gz to your Linux machine" -ForegroundColor Gray
Write-Host "  2. Extract: tar -xzf EDMCP_server_linux.tar.gz" -ForegroundColor Gray
Write-Host "  3. Run: ./start-edmcp.sh" -ForegroundColor Gray
Write-Host "  4. Test: curl http://localhost:5242/health`n" -ForegroundColor Gray

Write-Host "Package contents:" -ForegroundColor Cyan
Write-Host "  - EDMCP (executable)" -ForegroundColor Gray
Write-Host "  - start-edmcp.sh (startup script)" -ForegroundColor Gray
Write-Host "  - README_LINUX.md (documentation)`n" -ForegroundColor Gray
