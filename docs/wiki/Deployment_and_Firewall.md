# 🚀 배포 및 방화벽 설정 가이드

## 1. 폐쇄망 배포 안내

LAN 오목(OmokLan)은 폐쇄망(Air-Gapped Network) 배포를 위해 **완전 독립형(Self-Contained) 단일 실행 파일**로 빌드되었습니다.

### 배포 파일 정보
- **파일명**: `OmokLan.exe`
- **위치**: `publish/win-x64/OmokLan.exe`
- **파일 크기**: 약 62.8 MB
- **요구사항**: Windows 10 / 11 (64-bit)
- **설치 여부**: **무설치 포터블 (Portable)** — USB나 망연계 솔루션을 통해 파일을 복사한 뒤 더블 클릭하면 즉시 실행됩니다 (.NET 별도 설치 불필요).

---

## 2. Windows 방화벽 설정

폐쇄망 환경의 PC에서 UDP 브로드캐스트 패킷과 인바운드 TCP 연결이 차단되어 있으면 상대방이 로비에 나타나지 않거나 대결 신청이 도달하지 않을 수 있습니다.

### 방법 1. 최초 실행 시 방화벽 팝업에서 허용
앱을 처음 실행할 때 Windows Defender 방화벽 안내 창이 나타나면 **[액세스 허용]**을 클릭합니다 (개인 네트워크 및 공용 네트워크 모두 체크 권장).

### 방법 2. 관리자 권한 PowerShell 명령어로 일괄 등록
사내 정책으로 팝업이 차단되어 있거나 관리자가 사전 구성할 경우, 관리자 권한 PowerShell에서 아래 명령어를 실행합니다:

```powershell
# 1. UDP 브로드캐스트 탐색 포트 인바운드 허용
netsh advfirewall firewall add rule name="OmokLan Discovery (UDP-In)" dir=in action=allow protocol=UDP localport=47800

# 2. TCP 1:1 대결 세션 포트 대역 인바운드 허용
netsh advfirewall firewall add rule name="OmokLan Duel (TCP-In)" dir=in action=allow protocol=TCP localport=47801-47810

# 또는 OmokLan.exe 프로그램 전체 인바운드 허용 (권장)
netsh advfirewall firewall add rule name="OmokLan App" dir=in action=allow program="C:\경로\OmokLan.exe" enable=yes
```

---

## 3. 트러블슈팅

| 증상 | 원인 | 해결 방법 |
|---|---|---|
| 로비 목록에 다른 사용자가 안 보임 | 다른 서브넷(VLAN)에 위치하거나 UDP 브로드캐스트 차단 | 같은 공유기/스위치 망(동일 C클래스 IP 대역)인지 확인하고 방화벽 UDP 47800 인바운드 허용 |
| 대결 신청 시 `연결할 수 없습니다` 오류 | 대상 PC의 방화벽에서 TCP 47801 포트 차단 | 상대방 PC에서 Windows 방화벽의 TCP 47801 인바운드 규칙 등록 |
| 상대방에게 대결 신청 버튼이 비활성화됨 | 상대방이 방해금지(DND) 상태이거나 게임 중 | 상대방이 로비 상단의 방해금지 토글을 해제했는지 확인 |
| 30초 내에 돌을 못 둠 | 턴 타임아웃 규칙 적용 | 30초 제한시간 내에 착수해야 하며, 시간 초과 시 자동으로 시간패 처리됨 |
