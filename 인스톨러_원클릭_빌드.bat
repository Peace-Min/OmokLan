@echo off
chcp 65001 >nul
title LAN 오목 - 빌드 및 인스톨러 원클릭 생성기

echo ========================================================
echo       LAN 오목 (OmokLan) - 빌드 및 인스톨러 생성기
echo ========================================================
echo.
echo [1/3] 기존 빌드 및 패키지 정리 중...
if exist "dist\OmokLan_Setup_v1.1.1.exe" del /f /q "dist\OmokLan_Setup_v1.1.1.exe"

echo.
echo [2/3] .NET 단일 실행 파일 게시(Publish) 중...
dotnet publish "src\OmokLan\OmokLan.csproj" -c Release -r win-x64 -o "publish\win-x64" --self-contained true

if errorlevel 1 (
    echo.
    echo [오류] dotnet publish 빌드 중 오류가 발생했습니다!
    pause
    exit /b %errorlevel%
)

echo.
echo [3/3] 최종 인스톨러 파일 확인...
if exist "dist\OmokLan_Setup_v1.1.1.exe" (
    echo.
    echo ========================================================
    echo  [성공] 인스톨러가 성공적으로 생성되었습니다!
    echo  - 파일 위치: dist\OmokLan_Setup_v1.1.1.exe
    echo  - 이 파일 하나만 사내망 사용자에게 전달하시면 됩니다.
    echo ========================================================
    echo.
    explorer.exe /select,"dist\OmokLan_Setup_v1.1.1.exe"
) else (
    echo.
    echo [경고] dist\OmokLan_Setup_v1.1.1.exe 생성 여부를 확인해 주세요.
)

pause
