@echo off
setlocal
chcp 65001 >nul
title OmokLan - Build and Package Setup

cd /d "%~dp0"

echo ======================================================================
echo    OmokLan - One-Click Build and Installer (.exe) Generator
echo ======================================================================
echo.

if exist "dist\OmokLan_Setup_v1.1.1.exe" del /f /q "dist\OmokLan_Setup_v1.1.1.exe"

echo [1/2] Building .NET Self-Contained Binary and Inno Setup Installer...
dotnet publish "src\OmokLan\OmokLan.csproj" -c Release -r win-x64 -o "publish\win-x64" --self-contained true

if errorlevel 1 (
    echo.
    echo ======================================================================
    echo  [ERROR] Build failed. Please check the logs above.
    echo ======================================================================
    echo.
    pause
    exit /b %errorlevel%
)

echo.
echo [2/2] Checking generated installer...
if exist "dist\OmokLan_Setup_v1.1.1.exe" (
    echo.
    echo ======================================================================
    echo  [SUCCESS] Installer has been successfully created!
    echo.
    echo  - Output: dist\OmokLan_Setup_v1.1.1.exe
    echo  - Just deliver this single file to other users.
    echo ======================================================================
    echo.
    explorer.exe /select,"dist\OmokLan_Setup_v1.1.1.exe"
) else (
    echo.
    echo [WARNING] dist\OmokLan_Setup_v1.1.1.exe was not found.
)

echo Done. Press any key to close this window.
pause >nul
