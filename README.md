# kjs-Project

Unity **6000.6.9f1** 기반의 **3D 멀티플레이 시작 프로젝트**입니다. 여러 사용자가 하나의 필드에 접속해 이동하고 채팅합니다. 서버가 위치를 계산하며, Unity 클라이언트는 입력을 전송하고 받은 상태를 표시합니다.

**현재 검증:** 서버 테스트 13개, 실제 WebSocket 64개 동시 접속 시험 통과. Unity Editor에서의 임포트·화면·빌드는 아직 실행하지 않았습니다. 자세한 검증 범위는 [테스트 기록](docs/testing.md)에 있습니다.

## 프로젝트 열기

1. 저장소를 복제합니다: `git clone https://github.com/Q-holi/kjs-Project.git`
2. Unity Hub에서 **6000.6.9f1**과 사용할 데스크톱 빌드 모듈을 설치합니다.
3. **Assets / Packages / ProjectSettings가 들어 있는 저장소 루트**를 Hub의 기존 프로젝트로 추가합니다.
4. Python 3.12로 아래 서버를 실행합니다.
5. Unity에서 **KJS → Open Shared World**를 선택하고 Play를 누릅니다.
6. 이름을 입력하고 **Connect**를 누릅니다. 주소 기본값은 `ws://127.0.0.1:7777/world`입니다.

```bash
python -m venv .venv
# macOS / Linux
.venv/bin/python -m pip install -r Server/requirements.txt
.venv/bin/python Server/server.py
```

Windows PowerShell에서는 가상환경 실행 파일을 `.\.venv\Scripts\python.exe`로 바꿉니다. [운영체제별 복사 가능한 명령과 2인 접속 절차](docs/getting-started.md)를 확인하세요.

## 포함된 기능

- 3D 필드 씬, 카메라, 플레이어 캡슐과 이름표, 접속 UI, 채팅 UI
- 별도 Python WebSocket 서버: 기본 64명 제한, 20 Hz 시뮬레이션·상태 전송
- 서버 권한 이동: 입력 검증, 대각선 속도 제한, 필드 경계, 오래된 입력 무시
- 접속·퇴장 동기화, 한글 채팅, 정원 초과·잘못된 메시지 처리
- Windows / macOS / Linux 데스크톱 클라이언트용 빌드 메뉴
- 서버 통합 테스트, 동시 접속 봇, 실제 C# 통신 코드 시험, Unity PlayMode 테스트 소스

## 문서와 작업 관리

- [실행 방법 / 2인 접속](docs/getting-started.md)
- [구조 / 코드 설명](docs/architecture.md)
- [통신 프로토콜](docs/protocol.md)
- [테스트 결과 / Unity 검증 절차](docs/testing.md)
- [Notion 프로젝트 홈](https://app.notion.com/p/41a20b94753f813a9663f16a277f59cd)
- [Confluence 프로젝트 홈](https://ybs6863.atlassian.net/wiki/spaces/~5f842334e357dd006e66b4c3/pages/1769473/kjs)
- [Trello 보드](https://trello.com/b/pcZgjENn/kjs)

문서 두 곳은 초기 전달 시 동일 내용으로 작성하며 자동 동기화 기능은 없습니다. 코드의 기준은 이 저장소이며, 작업 상태는 Trello에서 관리합니다.

## MMORPG 개발의 시작 범위

이 예제는 단일 프로세스·단일 필드에서 접속과 이동을 확인하는 출발점입니다. 계정 인증, 캐릭터 저장, 전투, 인벤토리, 물리 충돌, 관심 영역 관리, 지역/서버 분할은 아직 구현하지 않았습니다. WebGL은 `ClientWebSocket`을 지원하지 않아 대상에서 제외합니다. 64개 봇의 로컬 통신 성공이 인터넷 환경의 대규모 MMORPG 수용 인원을 보장하지는 않습니다.

Unity 버전은 [공식 Editor Release API](https://services.api.unity.com/unity/editor/release/v1/releases?limit=10)에서 확인한 최신 정식 버전으로 고정했습니다. 알파·베타 릴리스는 선택하지 않았습니다.
