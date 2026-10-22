@echo off
chcp 65001 > nul
setlocal enabledelayedexpansion
title LAN 오목 (OmokLan) 사내망 원클릭 설치기

echo ========================================================
echo       🎯 LAN 오목 (OmokLan) 원클릭 설치 프로그램
echo ========================================================
echo.

:: 1. 관리자 권한 확인 및 자동 승격
openfiles >nul 2>&1
if '%errorlevel%' NEQ '0' (
    echo [안내] 방화벽 등록 및 설치를 위해 관리자 권한으로 승격합니다...
    powershell -Command "Start-Process '%~0' -Verb RunAs"
    exit /b
)

set "TARGET_DIR=%LOCALAPPDATA%\Programs\OmokLan"
set "EXE_NAME=OmokLan.exe"
set "SRC_EXE=%~dp0%EXE_NAME%"

if not exist "%SRC_EXE%" (
    echo [오류] 설치 파일(%EXE_NAME%)을 찾을 수 없습니다.
    echo 같은 폴더에 %EXE_NAME% 파일이 있는지 확인해 주세요.
    pause
    exit /b
)

echo [1/4] 프로그램 파일 복사 중...
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"
copy /Y "%SRC_EXE%" "%TARGET_DIR%\%EXE_NAME%" > nul
echo     - 복사 완료: %TARGET_DIR%\%EXE_NAME%

echo.
echo [2/4] Windows 방화벽 규칙 자동 등록 중...
netsh advfirewall firewall delete rule name="OmokLan App" > nul 2>&1
netsh advfirewall firewall delete rule name="OmokLan Discovery (UDP-In)" > nul 2>&1
netsh advfirewall firewall delete rule name="OmokLan Duel (TCP-In)" > nul 2>&1

netsh advfirewall firewall add rule name="OmokLan App" dir=in action=allow program="%TARGET_DIR%\%EXE_NAME%" enable=yes > nul 2>&1
netsh advfirewall firewall add rule name="OmokLan Discovery (UDP-In)" dir=in action=allow protocol=UDP localport=47800 > nul 2>&1
netsh advfirewall firewall add rule name="OmokLan Duel (TCP-In)" dir=in action=allow protocol=TCP localport=47801-47810 > nul 2>&1
echo     - 방화벽 허용 규칙 등록 완료 (UDP 47800, TCP 47801~47810)

echo.
echo [3/4] 바탕화면 및 시작메뉴 바로가기 생성 중...
set "DESKTOP_LNK=%USERPROFILE%\Desktop\LAN 오목.lnk"
set "START_LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\LAN 오목.lnk"

powershell -NoProfile -ExecutionPolicy Bypass -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('%DESKTOP_LNK%'); $s.TargetPath = '%TARGET_DIR%\%EXE_NAME%'; $s.WorkingDirectory = '%TARGET_DIR%'; $s.Description = 'LAN 오목 P2P'; $s.Save();"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('%START_LNK%'); $s.TargetPath = '%TARGET_DIR%\%EXE_NAME%'; $s.WorkingDirectory = '%TARGET_DIR%'; $s.Description = 'LAN 오목 P2P'; $s.Save();"

echo     - 바로가기 생성 완료: 바탕화면 및 시작메뉴

echo.
echo [4/4] 설치 완료!
echo ========================================================
echo   🎉 LAN 오목 설치가 성공적으로 완료되었습니다!
echo   바탕화면의 [LAN 오목] 아이콘으로 언제든 실행하실 수 있습니다.
echo ========================================================
echo.

set /p RUN_NOW="지금 바로 LAN 오목을 실행하시겠습니까? (Y/N) [기본값: Y]: "
if /i "%RUN_NOW%"=="" set RUN_NOW=Y
if /i "%RUN_NOW%"=="Y" (
    start "" "%TARGET_DIR%\%EXE_NAME%"
)

exit /b
