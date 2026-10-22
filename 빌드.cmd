@echo off
setlocal
chcp 65001 >nul
title LAN 오목 - 원클릭 빌드 및 인스톨러 생성기

echo ======================================================================
echo    LAN 오목 (OmokLan) - 원클릭 자동 빌드 ^& 인스톨러(.exe) 생성기
echo ======================================================================
echo.

cd /d "%~dp0"

echo [1/3] 기존 빌드 산출물 정리 중...
if exist "dist\OmokLan_Setup_v1.1.exe" del /f /q "dist\OmokLan_Setup_v1.1.exe"

echo.
echo [2/3] .NET 단일 바이너리 빌드 및 Inno Setup 인스톨러 자동 패키징 중...
echo       (잠시만 기다려 주세요. 약 10~15초 소요됩니다)
echo.
dotnet publish "src\OmokLan\OmokLan.csproj" -c Release -r win-x64 -o "publish\win-x64" --self-contained true

if errorlevel 1 (
    echo.
    echo ======================================================================
    echo  [오류] 빌드 중 오류가 발생했습니다. 상단 에러 로그를 확인해 주세요.
    echo ======================================================================
    echo.
    pause
    exit /b %errorlevel%
)

echo.
echo [3/3] 최종 인스톨러 생성 확인...
if exist "dist\OmokLan_Setup_v1.1.exe" (
    echo.
    echo ======================================================================
    echo  [성공!] 단일 인스톨러가 성공적으로 생성되었습니다!
    echo.
    echo  - 산출물: dist\OmokLan_Setup_v1.1.exe
    echo  - 사내망 사용자에게 이 파일 하나만 전달하시면 됩니다.
    echo ======================================================================
    echo.
    explorer.exe /select,"dist\OmokLan_Setup_v1.1.exe"
) else (
    echo.
    echo [경고] dist\OmokLan_Setup_v1.1.exe 생성 여부를 확인해 주세요.
)

echo 작업이 완료되었습니다. 창을 닫으려면 아무 키나 누르세요.
pause >nul
