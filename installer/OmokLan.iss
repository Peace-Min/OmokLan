; =====================================================================
; LAN 오목 (OmokLan) - Inno Setup 배포 스크립트
; 사내망/폐쇄망 환경 맞춤형 단일 실행 설치 패키지 (Single-File Installer)
; =====================================================================

#define MyAppName "LAN 오목"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "LAN Omok"
#define MyAppURL "https://github.com/Peace-Min/OmokLan"
#define MyAppExeName "OmokLan.exe"

[Setup]
; 고유 애플리케이션 ID (업그레이드 및 제거 시 식별용)
AppId={{5D27E7B0-9A1F-4384-B473-8C6EA53B25A1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; 관리자 권한 없는 일반 사내 PC에서도 즉시 설치 가능하도록 lowest 지정
; (필요 시 관리자 권한으로 시스템 전체 설치도 선택 가능)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; 기본 설치 경로: 비관리자 모드 시 %localappdata%\Programs\OmokLan, 관리자 모드 시 Program Files\OmokLan
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; 설치 패키지 출력 설정 (초고압축 단일 exe 생성)
OutputDir=..\dist
OutputBaseFilename=OmokLan_Setup_v1.1
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible

; 마법사 테마 및 스타일
WizardStyle=modern
DisableWelcomePage=no

; 설치 전 안내 텍스트 (방화벽 및 폐쇄망 안내)
InfoBeforeFile=사내망_배포안내.txt

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkablealone

[Files]
; 독립 실행형 단일 실행 바이너리 및 안내 파일
Source: "..\publish\win-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "사내망_배포안내.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; 시작 메뉴 및 바탕화면 바로가기 등록
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\사내망 배포 및 방화벽 안내"; Filename: "{app}\사내망_배포안내.txt"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 설치 완료 후 즉시 게임 실행 옵션
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
