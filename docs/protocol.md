# 통신 프로토콜

## 접속 규칙

- 전송: WebSocket 텍스트 메시지, UTF-8 JSON 객체
- 엔드포인트: `/world`, 기본 TCP 포트 7777
- 프로토콜 버전: 1
- 좌표: Unity X/Z 지면 좌표, Y는 클라이언트 표시용으로 고정
- 모든 플레이어 ID와 위치는 서버가 생성
- 클라이언트는 접속 후 5초 안에 join을 전송

클라이언트 메시지는 최대 4 KiB입니다. 이진 프레임, 잘못된 JSON, 알 수 없는 종류, 비정상 숫자 등은 연결 종료 대상입니다.

## 클라이언트 → 서버

### join

첫 메시지입니다. 이름은 제어 문자를 제거한 뒤 최대 20자로 제한합니다. 빈 이름은 거절합니다. 이름은 계정 인증 수단이 아닙니다.

```json
{"type":"join","version":1,"name":"Alice"}
```

### input

접속 확인 후 초당 10회 보냅니다. `seq`는 0 이상 2147483647 이하의 정수이며 새 입력마다 증가합니다. `dx`와 `dz`는 각각 -1부터 1 사이의 유한한 수입니다. 벡터 크기가 1을 넘으면 서버가 정규화합니다. seq가 이전 값 이하이면 무시합니다.

```json
{"type":"input","seq":1,"dx":1.0,"dz":0.0}
```

위치, 속도 또는 클라이언트 시간 간격을 보내 서버 이동을 바꿀 수 없습니다. 입력이 끊기면 서버가 0.30초 뒤 정지시킵니다.

### chat

내용은 최대 140자입니다. 제어 문자를 제거하며 빈 메시지는 무시합니다. 같은 사용자의 채팅 간격은 최소 0.5초입니다. 초과 채팅은 전달하지 않습니다.

```json
{"type":"chat","text":"안녕하세요!"}
```

### ping

연결 점검용 응용 메시지입니다. WebSocket 자체 ping/pong과는 별개입니다.

```json
{"type":"ping"}
```

## 서버 → 클라이언트

### welcome

접속 등록에 성공하면 가장 먼저 전달합니다. 이후 input과 chat을 보낼 수 있습니다.

```json
{"type":"welcome","id":"server_generated_uuid","version":1,"tickRate":20,"speed":5.0,"worldHalfExtent":24.0,"maxPlayers":64}
```

### snapshot

서버 틱과 전체 플레이어 상태입니다. 새 접속과 퇴장도 이 전체 목록에 반영됩니다. `seq`는 서버가 마지막으로 수락한 해당 플레이어의 입력 번호이며, 아직 입력이 없으면 -1입니다. `yaw`의 단위는 도입니다. 현재 클라이언트는 이 상태를 보간해 표시합니다.

```json
{"type":"snapshot","tick":20,"players":[{"id":"server_generated_uuid","name":"Alice","x":3.25,"z":0.0,"yaw":90.0,"seq":1}]}
```

### chat / left / pong / error

```json
{"type":"chat","id":"server_generated_uuid","name":"Alice","text":"안녕하세요!"}
```

```json
{"type":"left","id":"server_generated_uuid"}
```

```json
{"type":"pong","tick":20}
```

```json
{"type":"error","message":"server_full"}
```

left를 받으면 즉시 캐릭터를 제거할 수 있습니다. 이후 snapshot에서도 제외됩니다. server_full 이후에는 연결을 종료합니다.

## 제한과 연결 종료

| 항목 | 동작 |
| --- | --- |
| 잘못된 프로토콜, 잘못된 경로, 입력 오류 | close code 1008 |
| 초당 메시지 제한 | 초당 60개 보충, 초기 버스트 120개; 초과 시 1008 |
| 클라이언트 메시지 크기 초과 | 1009 |
| 정원 초과 | server_full 및 1013 |
| 서버 송신 큐 지연 | 연결당 큐 8개, 초과 또는 송신 지연 시 1013 |
| Unity 수신 큐 | 최대 64개; 넘으면 접속 정리 후 재접속 필요 |
| Unity 수신 메시지 | 최대 256 KiB, 분할 프레임 조립 지원 |
| Unity snapshot 없음 | 5초 뒤 접속 정리 |

이 제한은 초기 예제의 메모리와 입력 검증을 위한 것입니다. 계정별 인증·권한·외부 트래픽 제어는 아직 구현하지 않았습니다.
