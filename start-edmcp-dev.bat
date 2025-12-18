@echo off
REM EDMCP Development Server Startup
REM Sets environment variables and runs the production executable

set ASPNETCORE_URLS=http://0.0.0.0:5242
set EASY_DIFFUSION_ADDRESS=http://localhost:9000

echo.
echo ======================================
echo   EDMCP Server
echo ======================================
echo Starting EDMCP on http://0.0.0.0:5242
echo Easy Diffusion: %EASY_DIFFUSION_ADDRESS%
echo.

REM Navigate to the correct publish folder (net10.0, not net8.0)
cd EDMCP\bin\Release\net10.0\win-x64\publish

if not exist "EDMCP.exe" (
    echo ERROR: EDMCP.exe not found!
    echo Expected location: %cd%\EDMCP.exe
    echo.
    echo Did you forget to run the release build first?
    echo Run: .\build_release.ps1
    pause
    exit /b 1
)

echo Running: %cd%\EDMCP.exe
echo.

EDMCP.exe

if errorlevel 1 (
    echo.
    echo ERROR: EDMCP.exe exited with error code %errorlevel%
    pause
)