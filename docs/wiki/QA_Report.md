# 🧪 QA 검증 보고서 (QA Report)

**검증 일시**: 2026-10-22  
**대상 버전**: v1.1 (단일 실행 파일 배포판)  
**테스트 환경**: Windows 11 x64, .NET 9.0 SDK, xUnit 2.9.2  

---

## 1. 테스트 요약

| 테스트 영역 | 대상 모듈 | 테스트 케이스 수 | 결과 | 비고 |
|---|---|:---:|:---:|---|
| **오목 규칙 엔진** | `OmokBoard` | 7건 | **PASS** | 가로/세로/대각선 5목, 6목(자유룰), 경계 검사, 기권 등 |
| **네트워크 프로토콜** | `FrameCodec`, `BeaconPacket` | 3건 | **PASS** | 4바이트 길이 접두 프레이밍, 비컨 패킷 직렬화/파싱 |
| **TCP 대결 핸드셰이크** | `DuelHost`, `DuelSession` | 3건 | **PASS** | 대결 신청/수락, DND 자동거절, Busy(게임중) 자동거절 |
| **대국 동기화 & 기능** | `OmokMatch`, `WireModels` | 1건 | **PASS** | 코인 토스 흑백 결정, 착수 동기화, 실시간 채팅 송수신 |
| **배포 패키징** | `OmokLan.exe` (Single-File) | 1건 | **PASS** | Self-contained x64 단일 exe 정상 생성 (62.8MB) |
| **합계** | | **15건** | **100% 통과** | |

---

## 2. 상세 검증 내용

### 2.1 오목 규칙 검증 (`OmokBoardTests`)
- `HorizontalFiveInRow_DeclaresWinner`: 가로 5목 연속 착수 시 즉시 `PlaceOutcome.Win` 반환 및 승리 라인(5개 좌표) 반환 확인.
- `VerticalFiveInRow_DeclaresWinner`: 세로 5목 연속 착수 시 승패 판정 정상 동작 확인.
- `DiagonalFiveInRow_DeclaresWinner`: ↘ 방향 대각선 5목 착수 시 정상 승리 판정 확인.
- `SixInRow_FreeRuleAllowsWin`: 자유룰(금수 없음) 적용 — 6목 이상 연속 착수 시 승리로 정상 인정 확인.
- `OutOfRangeAndOccupied_Rejected`: 보드 좌표 범위 밖 및 이미 돌이 놓인 자리에 착수 거절 확인.
- `WrongTurn_Rejected`: 상대 턴일 때 착수 시도 시 즉시 거절 확인.
- `Forfeit_SetsOpponentWinner`: 기권 시 상대방이 즉시 승자로 지정되고 게임 종료 처리 확인.

### 2.2 네트워크 프레이밍 및 비컨 검증 (`FramingTests`)
- `WriteAndRead_RoundtripPreservesEnvelope`: Big-Endian 4바이트 길이 접두 프레임 직렬화/역직렬화 데이터 보존 확인.
- `BeaconPacket_SerializationAndParsing_Succeeds`: UDP 브로드캐스트용 JSON 비컨 패킷 파싱 및 유효성 검증 성공 확인.
- `BeaconPacket_InvalidAppOrVersion_Rejected`: 타 애플리케이션 및 구버전 패킷 필터링 확인.

### 2.3 TCP 대결 핸드셰이크 검증 (`DuelIntegrationTests`)
- `ChallengeAndAccept_EstablishesSessions`: 신청 및 수락 후 양방향 P2P 세션 정상 연결 확인.
- `ChallengeToDndHost_AutomaticallyRefused`: 방해금지(DND) 상태인 사용자에게 신청 시 자동 거절 확인.
- `ChallengeToBusyHost_AutomaticallyRefused`: 게임 진행 중인 사용자에게 신청 시 자동 거절 확인.

### 2.4 인게임 동기화 및 부가 기능 검증 (`MatchTests`)
- `TwoPlayers_CoinTossAndMoves_SyncProperly`:
  - 대결 시작 시 코인 토스로 흑/백 선후공이 50% 확률로 공정하게 분배됨 확인.
  - 착수 수순 및 보드 상태가 양측 화면에 동일하게 실시간 반영됨 확인.
  - 인게임 실시간 채팅 송수신 확인.
  - 30초 턴 타이머 만료 시 시간패 처리 로직 검증.
