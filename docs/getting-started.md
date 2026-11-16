# 실행 방법과 2인 접속

## 준비

| 구성 요소 | 설정 |
| --- | --- |
| Unity | 6000.6.9f1 / revision 8c45277fb9c5 |
| 렌더링 | 3D, Built-in Render Pipeline |
| 클라이언트 대상 | Windows 64-bit / macOS / Linux 64-bit |
| 서버 | Python 3.12 이상, websockets 15.0.1 |
| 기본 주소 | ws://127.0.0.1:7777/world |
| 기본 정원 | 64명 |

프로젝트 자체에 Assets, Packages, ProjectSettings가 포함되어 있습니다. 별도의 빈 Unity 프로젝트를 만들거나 템플릿 위에 덮어쓸 필요가 없습니다. Unity Hub에서 **기존 프로젝트 추가**로 저장소 루트를 선택하세요. 첫 임포트 때 Unity가 Library와 추가 기본 설정, 패키지 잠금 파일을 생성합니다.

[Unity 공식 릴리스 목록](https://unity.com/releases/editor/archive)에서 해당 버전을 설치하세요. Hub가 설치되어 있다면 `unityhub://6000.6.9f1/8c45277fb9c5`를 사용할 수도 있습니다. 빌드할 운영체제의 Build Support 모듈도 설치합니다.

현재 클라우드에서는 Unity Editor가 없어 실제 임포트와 빌드를 검증하지 못했습니다. [Unity 검증 체크리스트](testing.md)를 따라 확인한 뒤 팀의 기준 프로젝트로 사용하세요.

## 1. 소스 받기

```bash
git clone https://github.com/Q-holi/kjs-Project.git
cd kjs-Project
```

## 2. 서버 실행

Windows PowerShell:

```powershell
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r Server\requirements.txt
.\.venv\Scripts\python.exe Server\server.py
```

macOS / Linux:

```bash
python3 -m venv .venv
.venv/bin/python -m pip install -r Server/requirements.txt
.venv/bin/python Server/server.py
```

로그에 `kjs world ws://127.0.0.1:7777/world max_players=64`가 나오면 접속을 받을 준비가 된 상태입니다. 서버는 접속한 동안 켜 두며, 종료는 Ctrl+C입니다. 서버를 다시 시작하면 캐릭터 상태는 초기화됩니다.

다른 PC에서 접속하려면 서버를 아래처럼 실행합니다. 같은 PC에서 테스트할 때는 기본 명령을 사용하면 됩니다.

```bash
python Server/server.py --host 0.0.0.0 --port 7777 --max-players 64
```

다른 PC의 Unity 주소에는 서버 PC의 LAN IP를 넣습니다. 예: `ws://192.168.0.10:7777/world`. 서버 PC의 TCP 7777 포트가 테스트 상대에게 열려 있어야 합니다. `0.0.0.0`은 수신 주소이므로 클라이언트 접속 주소로 입력하지 않습니다. 서로 다른 PC에서 `127.0.0.1`을 사용하면 각자의 PC로 접속하게 됩니다.

## 3. Unity 클라이언트 실행

1. Unity Hub에서 저장소 루트를 6000.6.9f1로 엽니다.
2. 패키지 임포트가 끝날 때까지 기다립니다.
3. **KJS → Open Shared World**, 또는 `Assets/Scenes/SharedWorld.unity`를 엽니다.
4. **Play**를 누릅니다. Play 시작 시 필드·카메라·조명이 생성됩니다.
5. Server 주소와 Name을 입력하고 **Connect**를 누릅니다.
6. 필드의 빈 곳을 클릭하고 WASD 또는 방향키로 이동합니다.
7. 채팅 입력창에 글을 쓰고 **Send** 또는 Enter를 누릅니다.

자신의 캐릭터는 민트색이며 이름 옆에 `(You)`가 표시됩니다. 다른 캐릭터는 ID별 색으로 표시됩니다. 키 입력 중 텍스트 필드에 포커스가 있거나 창이 비활성화되면 이동을 멈춥니다. 접속이 끊기면 캐릭터 표시를 정리하며, 다시 Connect를 눌러 새 세션으로 접속할 수 있습니다.

## 4. 두 클라이언트로 확인

같은 프로젝트 폴더를 두 Unity Editor에서 동시에 열면 프로젝트 잠금이 발생합니다. **Editor + 데스크톱 빌드** 또는 **데스크톱 빌드 2개**로 테스트하세요.

1. **File → Build Profiles**에서 데스크톱 플랫폼을 활성화합니다.
2. **KJS → Build Current Desktop Target**을 실행합니다.
3. 생성된 실행 파일을 두 번 실행하고 이름을 Alice와 Bob으로 설정합니다.
4. 두 창에서 Connect를 누르고 서로의 캐릭터를 확인합니다.
5. Alice를 움직여 Bob 화면에서도 움직이는지 확인합니다.
6. 한글 채팅, Disconnect, 재접속, 서버 종료 때 화면 정리를 확인합니다.

기본 빌드 경로는 `Builds/Windows/kjs.exe`, `Builds/macOS/kjs.app`, `Builds/Linux/kjs.x86_64`입니다.

Windows에서 자동 접속 옵션 사용:

```powershell
Start-Process .\Builds\Windows\kjs.exe -ArgumentList '--server ws://127.0.0.1:7777/world --name Alice --connect'
Start-Process .\Builds\Windows\kjs.exe -ArgumentList '--server ws://127.0.0.1:7777/world --name Bob --connect'
```

macOS:

```bash
open -n Builds/macOS/kjs.app --args --server ws://127.0.0.1:7777/world --name Alice --connect
open -n Builds/macOS/kjs.app --args --server ws://127.0.0.1:7777/world --name Bob --connect
```

Linux에서는 실행 파일에 같은 `--server`, `--name`, `--connect` 옵션을 전달합니다.

## 문제 확인

| 증상 | 확인할 내용 |
| --- | --- |
| Connection failed | 서버 실행 여부, 주소의 ws/wss 형식, /world 경로, TCP 포트 |
| server_full | 현재 접속 수와 서버의 --max-players 값 |
| 이동하지 않음 | 필드 빈 곳 클릭, Game 창 포커스, Input Handling이 Input Manager(Old) 또는 Both인지 |
| 다른 PC 접속 실패 | 접속 주소가 서버 LAN IP인지, 서버 --host와 방화벽 설정 |
| 분홍색 오브젝트 | Built-in 프로젝트인지, Resources/KjsSurface.mat가 존재하는지, URP로 바꾸지 않았는지 |
| 한글 글리프 누락 | 맑은 고딕, Apple SD Gothic Neo 또는 Noto Sans CJK KR 설치 여부 |
| No world update | 서버 중단 여부; 접속을 종료하고 다시 Connect |

이 예제의 필드는 X/Z 평면이며 장애물과 플레이어 사이의 충돌 판정은 구현하지 않았습니다. 공개 인터넷 서비스로 전환하려면 인증, TLS(wss), 저장소와 배포 구성이 추가로 필요합니다.
