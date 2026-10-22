@echo off
setlocal
chcp 65001 >nul
title LAN Omok - One-Click Build ^& Installer Generator

echo ======================================================================
echo    LAN Omok (OmokLan) - One-Click Build ^& Installer Generator
echo ======================================================================
echo.

cd /d "%~dp0"

echo [1/3] Cleaning previous build outputs...
if exist "dist\OmokLan_Setup_v1.1.1.exe" del /f /q "dist\OmokLan_Setup_v1.1.1.exe"

echo.
echo [2/3] Building .NET Single-File Binary ^& Packaging Inno Setup Installer...
echo       (Please wait 10-15 seconds)
echo.
dotnet publish "src\OmokLan\OmokLan.csproj" -c Release -r win-x64 -o "publish\win-x64" --self-contained true

if errorlevel 1 (
    echo.
    echo ======================================================================
    echo  [ERROR] Build failed! Check the log messages above.
    echo ======================================================================
    echo.
    pause
    exit /b %errorlevel%
)

echo.
echo [3/3] Verifying generated installer...
if exist "dist\OmokLan_Setup_v1.1.1.exe" (
    echo.
    echo ======================================================================
    echo  [SUCCESS] Installer generated successfully!
    echo.
    echo  - Output: dist\OmokLan_Setup_v1.1.1.exe
    echo  - Pass this single file to your corporate users.
    echo ======================================================================
    echo.
    explorer.exe /select,"dist\OmokLan_Setup_v1.1.1.exe"
) else (
    echo.
    echo [WARNING] Please check if dist\OmokLan_Setup_v1.1.1.exe was created.
)

echo Done. Press any key to exit.
pause >nul
