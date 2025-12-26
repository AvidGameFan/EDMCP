@echo off
REM EDMCP Server Startup Script
REM This script is designed to run from the EDMCP release package root directory

set ASPNETCORE_URLS=http://0.0.0.0:5242
set EASY_DIFFUSION_ADDRESS=http://localhost:9000
set DEFAULT_MODEL=sd-v1-5

REM Uncomment and modify the line below if Easy Diffusion is on a different machine
REM set EASY_DIFFUSION_ADDRESS=http://192.168.1.100:9000

echo.
echo ======================================
echo   EDMCP MCP Server
echo ======================================
echo Listening on: %ASPNETCORE_URLS%
echo Easy Diffusion: %EASY_DIFFUSION_ADDRESS%
echo.

if not exist "EDMCP.exe" (
    echo ERROR: EDMCP.exe not found in current directory!
    echo.
    echo This script should be run from the EDMCP release package directory.
    echo If you extracted EDMCP_server.zip, run this script from that directory.
    echo.
    pause
    exit /b 1
)

echo Starting EDMCP...
echo.

EDMCP.exe

if errorlevel 1 (
    echo.
    echo ERROR: EDMCP.exe exited with code %errorlevel%
    echo.
    pause
)