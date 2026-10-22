@echo off
chcp 65001 >nul
title LAN 오목 - 바로가기 및 파일 삭제

echo ========================================================
echo          LAN 오목 (OmokLan) 제거 도구
echo ========================================================
echo.

set "INSTALL_DIR=%LOCALAPPDATA%\OmokLan"
set "DESKTOP_SHORTCUT=%USERPROFILE%\Desktop\LAN 오목.lnk"
set "START_SHORTCUT=%APPDATA%\Microsoft\Windows\Start Menu\Programs\LAN 오목.lnk"

echo [1/3] 실행 중인 LAN 오목 프로세스 종료 중...
taskkill /F /IM OmokLan.exe >nul 2>&1

echo [2/3] 바탕화면 및 시작메뉴 바로가기 삭제 중...
if exist "%DESKTOP_SHORTCUT%" del /f /q "%DESKTOP_SHORTCUT%"
if exist "%START_SHORTCUT%" del /f /q "%START_SHORTCUT%"

echo [3/3] 설치 프로그램 폴더 삭제 중...
if exist "%INSTALL_DIR%" (
    rmdir /s /q "%INSTALL_DIR%"
    echo   - %INSTALL_DIR% 폴더가 정상적으로 삭제되었습니다.
)

echo.
echo ========================================================
echo   LAN 오목 프로그램이 시스템에서 완전히 제거되었습니다.
echo   (사용자 로컬 프로필 캐시는 %LOCALAPPDATA%\OmokLan에 저장되어 있었습니다)
echo ========================================================
echo.
pause
