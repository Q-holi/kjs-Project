# 테스트 결과와 재현 방법

## 검증 상태

기준일: 2026-11-16 (Asia/Seoul). 환경: Debian 13, Python 3.12.14, websockets 15.0.1, .NET SDK 8.0.428 / runtime 8.0.34.

| 대상 | 결과 | 검증 범위 |
| --- | --- | --- |
| 서버 회귀·통합 테스트 | 13개 통과 | 서버 이동 규칙 및 실제 로컬 WebSocket 통신 |
| 32개 클라이언트 | 통과 | 고유 ID, 전체 사용자 상태 수신, 한글 채팅 |
| 64개 클라이언트 | 통과 | 약 5초 동시 입력·채팅·snapshot 수신 및 퇴장 정리 |
| C# 통신 코드 컴파일 | 통과 | 실제 WorldClient.cs와 Protocol.cs를 .NET Standard 2.1 참조로 컴파일 |
| C# 클라이언트 2개 | 통과 | 실제 서버 접속, 상대 이동, 한글 채팅, 퇴장 이벤트; .NET 런타임에서 실행 |
| Unity 파일 구조 | 통과 | .meta/GUID, 씬 스크립트 연결, 빌드 씬, 3D 모드와 입력 축의 정적 검사 |
| Unity Editor 임포트·전체 C# 컴파일 | 대기 | 현재 환경에 Unity Editor가 없음 |
| Unity PlayMode 테스트 | 대기 | 테스트 소스만 포함; Unity에서 실행 필요 |
| Unity 화면·데스크톱 빌드·2인 플레이 | 대기 | Unity Editor와 대상 플랫폼 빌드 모듈에서 확인 필요 |

C# 통신 코드의 .NET 실행 성공은 Unity의 JsonUtility, Mono/IL2CPP, 셰이더, 씬 임포트와 UI를 검증한 결과가 아닙니다. Unity가 필요한 항목은 미검증 상태로 남겨 두었습니다.

## 64개 클라이언트 실제 결과

| 측정 | 값 |
| --- | --- |
| 클라이언트 수 | 64 |
| 실행 시간 | 5.106초 |
| 서버 설정 | 20 Hz |
| 입력 라운드 | 48 |
| 각 클라이언트의 snapshot 수 | 모두 100회 |
| 전체 사용자 목록 수신 | 모든 클라이언트 성공 |
| 한글 채팅 수신 | 모든 클라이언트 성공 |
| snapshot 수신 간격 중앙값 | 50.883 ms |
| snapshot 수신 간격 p95 | 60.371 ms |
| 종료 후 서버 플레이어 수 | 0 |

위 값은 같은 클라우드 머신의 loopback에서 실행한 Python 소켓 봇 시험 결과입니다. 네트워크 왕복 지연, 원격 인터넷, Unity 렌더링 성능 또는 장시간 안정성을 측정하지 않았습니다. 대규모 MMORPG 수용 인원으로 일반화하지 않습니다.

원본 근거는 `docs/evidence/server-tests.txt`, `load-64.json`, `transport-profile.txt`, `csharp-transport.txt`, `project-structure.json`에 있습니다. Unity 버전 선택 근거는 `unity-release.json`에 있습니다.

## 서버 테스트 재현

저장소 루트에서 실행합니다. Windows에서는 아래 `python` 대신 `.\.venv\Scripts\python.exe`, macOS/Linux에서는 `.venv/bin/python`을 사용할 수 있습니다.

```bash
python -m pip install -r Server/requirements.txt
python -m unittest discover -s Server/tests -v
python Tools/load_test.py --clients 64 --seconds 5
python Tools/validate_project.py
```

테스트는 임의의 비어 있는 로컬 포트에서 서버를 띄우므로 수동 서버가 필요하지 않습니다. `load_test.py`는 1~256개 클라이언트와 1초 이상의 시간을 받지만, 더 많은 클라이언트의 통과는 별도로 기록해야 합니다.

13개 테스트는 대각선 속도와 위치 위조 필드 무시, 경계와 오래된 seq, 입력 중단 시 정지, 비정상 숫자·자료형, 32인 사용자 목록과 채팅, 두 클라이언트의 원격 이동, 퇴장·재접속 ID, 정원 초과, 잘못된 패킷 뒤 다른 사용자 유지, 버전 오류, 이진·대용량 메시지, 채팅 제한, 메시지 폭주를 확인합니다.

## 실제 C# 통신 코드 재현

.NET 8 SDK 설치 후:

```bash
dotnet build Tools/TransportProfile/TransportProfile.csproj --configuration Release
python Tools/run_transport_smoke.py
```

두 번째 명령은 임시 Python 서버와 C# 클라이언트 두 개를 자동으로 실행합니다. Unity 프로젝트의 WorldClient.cs를 그대로 참조합니다. UnityEngine 코드의 전체 컴파일이나 Unity GUI 테스트는 포함하지 않습니다.

클라우드 검증에서는 .NET CLI 초기 설정의 이름 있는 mutex를 사용할 수 없어 SDK에 포함된 Roslyn 컴파일러를 직접 호출했습니다. 같은 .NET Standard 2.1 참조 어셈블리와 같은 소스를 사용했고, 생성된 C# 실행 파일을 실제 Python 서버에 연결했습니다. 컴파일 결과와 실행 결과는 원본 로그에 기록했습니다. 일반 개발 환경과 GitHub CI는 위의 dotnet 명령을 사용합니다.

## Unity에서 남은 검증

Unity Hub로 6000.6.9f1을 설치하고 프로젝트를 연 뒤 진행합니다.

- [ ] 패키지 임포트와 전체 C# 컴파일이 오류 없이 완료된다.
- [ ] KJS → Open Shared World로 씬을 열고 Play 시 필드·카메라·UI가 보인다.
- [ ] Window → General → Test Runner의 PlayMode에서 씬 로드 테스트가 통과한다.
- [ ] 아래 환경 변수와 서버를 준비한 뒤 Unity 네트워크 테스트가 통과한다.
- [ ] 대상 플랫폼으로 KJS 빌드 메뉴가 성공한다.
- [ ] Editor + 빌드 또는 빌드 2개에서 서로의 이동·이름·채팅을 확인한다.
- [ ] 텍스트 입력과 비활성 창에서 캐릭터가 멈춘다.
- [ ] Disconnect, 재접속, 서버 중단 후 캐릭터와 상태 표시가 정리된다.

Unity 네트워크 테스트는 `KJS_INTEGRATION_URL`이 없으면 **Ignored** 처리합니다. 이는 통과가 아닙니다. 환경 변수를 설정한 셸에서 Unity Editor를 새로 실행해야 합니다. 이미 열린 Unity Hub가 시작한 Editor에는 새 환경 변수가 전달되지 않을 수 있습니다.

macOS/Linux 예시:

```bash
export KJS_INTEGRATION_URL=ws://127.0.0.1:7777/world
# 다른 터미널에서 서버를 실행한 뒤, 위 환경 변수가 설정된 셸에서 Unity를 실행
"$UNITY_EDITOR" -batchmode -projectPath "$PWD" -runTests -testPlatform PlayMode -testResults TestResults/playmode.xml -logFile TestResults/playmode.log
```

`UNITY_EDITOR`는 본인의 Unity 실행 파일 절대 경로로 설정합니다. TestResults 폴더를 먼저 만드세요. `-runTests`에는 `-quit`을 추가하지 않습니다. 렌더링 확인에는 그래픽 장치가 필요하며 `-nographics` 결과만으로 화면을 검증했다고 표시하면 안 됩니다. PlayMode 테스트는 서버가 준비되어 있고 환경 변수가 설정되면 두 Unity 통신 클라이언트의 접속·이동·한글 채팅·퇴장을 확인합니다.

## 지속 검증

저장소에는 서버·정적 검사·C# 통신 시험을 실행하는 GitHub Actions 구성이 포함됩니다. 실제 실행 여부와 결과는 GitHub Actions에서 확인해야 하며, 워크플로 파일의 존재만으로 CI가 통과한 것으로 표시하지 않습니다. Unity 자동 빌드와 PlayMode CI는 Unity Editor와 적절한 활성화 환경을 구성한 뒤 추가할 수 있습니다.
