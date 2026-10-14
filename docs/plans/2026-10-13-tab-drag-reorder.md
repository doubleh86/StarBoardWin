# 탭 마우스 드래그 정렬

## 현재 상태

**계약·공통 fixture, runtime·workspace 동기화 및 renderer 구현, integration 자동 검증 완료.**
2026-10-13 사용자 요청으로 추가한 독립 실행 계획이다. `bd6e89f`의 runtime 구현 위에
동기화 경계와 포커스 보존을 보완했고, renderer Task에서 마우스 drag UI를 구현했다.
integration에서 메시지 왕복·저장/복원 회귀와 사용 문서를 보완했다. 실제 WebView2·IME·focus·DPI
화면 인수는 수동 검증 대기이며 자동·모의 결과로 통과 처리하지 않는다.
드래그 표시가 약하다는 후속 사용자 제보에 따라 `tab-drag-visual-feedback`을 아래에 추가했다.
이 후속 Task는 **기획 완료·구현 미착수**이며 기존 정렬 구현·자동 검증 완료와 구분한다.
계약 Task에서는 이 계획과 테스트 fixture만 변경하며 제품 코드, renderer 배포 asset, 앱 버전과 바탕화면 배포를 변경하지 않는다.
오케스트레이터는 미완료 후속 Task만 새 구현 대상으로 계획하고 기존 완료 기능을 다시 구현하지 않는다.

초기 기획의 기준선은 `main`의 `1505ccb`였다. 당시 탭 우클릭/`Shift+F10`의 `왼쪽으로 이동`·`오른쪽으로 이동`은
구현돼 있지만 마우스 드래그 정렬은 없다. `TerminalTabRegistry`의 순서와
`TerminalSessionCoordinator.WorkspaceChanged`를 정본으로 유지하며 renderer만의 별도 순서를 만들지 않는다.

## 목표와 완료 조건

작업표시줄 위의 작은 terminal에서 탭을 마우스로 끌어 원하는 위치로 옮긴다. 새 탭이나 shell을
만들지 않고 기존 탭 순서만 변경한다. 우클릭 좌우 이동은 접근 가능한 대체 경로로 유지한다.

1. 2~8개 탭에서 앞·뒤·중간 위치로 옮길 수 있고 1개 탭에서는 변경 요청을 만들지 않는다.
2. runtime session ID, shell PID, 이름·시작 폴더·generation, 입력·출력·scrollback을 보존한다.
   활성 탭은 같은 ID를 유지한다. 비활성 탭을 끌었다는 이유만으로 다른 terminal을 선택하지 않는다.
3. `×`·`+`·`▾`·이름 변경·높이 조절과 충돌하지 않고 일반 클릭, 더블클릭, 키보드 동작을 유지한다.
4. overflow에서도 정렬할 수 있고, 취소·오래된 요청·renderer 복구로 잘못된 순서를 저장하지 않는다.
5. workspace 복원이 켜져 있으면 기존 저장 경로로 새 순서를 저장한다. 꺼져 있으면 runtime 순서만
   변경하며 저장을 켜거나 새 파일/schema를 만들지 않는다. 기존 WSL 복원 지원 범위도 바꾸지 않는다.
6. 자동 검증과 실제 WebView2 검증을 나눠 기록한다. 빌드·문자열 검사만으로 실제 drag를 통과 처리하지 않는다.

## UX 계약

- 드래그 시작 영역은 탭 이름/탭 선택 영역이며 왼쪽 mouse button만 사용한다. 닫기 버튼, 메뉴,
  `+`, `▾`와 `+`/`▾` 사이 높이 조절 손잡이에는 드래그 handler를 적용하지 않는다.
- pointer 이동이 6 CSS px를 넘을 때 정렬 모드로 전환한다. 그 전의 mouse release는 기존 클릭이다.
  실제 drag 뒤의 click/double-click은 억제해 탭 선택·이름 편집·닫기를 우발적으로 실행하지 않는다.
- pointer 좌표와 DOM rect는 같은 CSS pixel 좌표계를 쓴다. WPF DIP나 screen physical pixel을
  섞지 않는다. 100/125/150/200%에서 이중 배율 계산 없이 동작해야 한다.
- 탭 바 높이와 현재 theme를 유지하고 삽입 위치를 얇은 표시로 안내한다. 노란 테두리, 상시 버튼,
  별도 창이나 panel 자동 확장은 추가하지 않는다. pointer preview는 shell 입력을 전달하지 않는다.
  얇은 표시는 초기 구현 기준이며, 후속 시각 보강은 아래 `tab-drag-visual-feedback`을 따른다.
- 정렬 중에는 삽입 위치만 미리 보여주고 pointer release의 유효한 drop에서 한 번 확정한다.
  움직일 때마다 host에 이동 메시지를 보내거나 workspace 파일을 저장하지 않는다.
- 탭 목록의 좌우 가장자리에 머무르면 목록만 제한된 속도로 가로 스크롤한다. `+`와 오른쪽 끝 `▾`는
  움직이지 않으며 기존 `tablist → + → flexible spacer/높이 손잡이 → ▾` 배치를 보존한다.
- Escape, pointer cancel, panel/renderer 수명 종료 또는 유효한 대상 밖 release는 취소한다.
  capture를 해제하고 삽입 표시·자동 스크롤을 정리하며 아무 이동 요청도 보내지 않는다.
  정렬 취소용 Escape는 shell에 전달하지 않는다.
- drag 중 탭 추가·제거·재시작 또는 순서 snapshot 변경이 발생하면 취소한다. 배경 출력 같은 순서와
  무관한 갱신은 drag를 깨지 않는다. 취소 시 이전 배열로 새 host 상태를 덮어쓰지 않는다.
- 정렬은 focus를 강제로 shell로 넘기지 않는다. 기존 활성 탭과 키보드 focus 대상 ID를 보존하고,
  background 응답이 다른 foreground application의 focus를 빼앗지 않는다.

## 계약·구조 판단과 위험

- 기존 `move-session`은 `left`/`right` 한 칸 이동이다. drag를 여러 한 칸 메시지로 흉내 내면 탭
  추가·닫기와 경쟁할 수 있으므로 유효한 한 번의 위치 변경을 registry/coordinator에서 처리한다.
- 내부 renderer 요청은 이동할 session ID와 generation, 기준 탭 ID 및 앞/뒤 위치를 명시한다.
  renderer instance와 drag 시작 당시 순서의 유효성은 아래 확정 계약을 따른다.
  범위를 넘는 임의 배열, 없는/자기 자신 대상, 오래된 generation·renderer 요청은 거부한다.
- 순서 변경은 coordinator의 기존 동기화·WorkspaceChanged·workspace persistence 흐름에 연결한다.
  동일 위치는 no-op이고 거절 시 현재 host snapshot으로 UI를 맞춘다. module 간 참조, public API,
  settings schema와 protocol version 변경은 필요한 근거 없이 추가하지 않는다.
- 기존 session별 xterm/DOM 객체를 옮기거나 기존 정렬 갱신 경로를 사용한다. 탭 순서 때문에 terminal
  객체를 폐기·재생성하거나 ConPTY를 resize/restart하지 않는다. 소유한 pointer listener·capture와
  animation frame/자동 스크롤은 종료·취소 시 결정적으로 정리한다.
- Explorer 경로 드롭과 내부 탭 정렬을 구분한다. 외부 파일 drop, 터미널 본문 텍스트 선택, IME와
  clipboard shortcut handler를 가로채지 않는다. 추가 drag library나 runtime network는 사용하지 않는다.
- 저장 실패는 기존 로컬 상태 안내·재시도 정책을 따른다. 오류를 숨기거나 실행 중인 shell을 종료하지 않는다.

## 확정된 내부 메시지·동기화 계약

### 현재 코드에서 확인한 경로와 변경 경계

- `Application/RendererProtocol.cs`는 protocol version **2**, root의 `sessionId`, object `payload`를
  사용한다. `move-session`은 `payload.direction` 문자열을 파싱하고 `TerminalView.MoveSession`이
  coordinator의 `MoveLeft`/`MoveRight`에 연결한다. 기존 요청 형태와 한 칸 이동 의미를 유지한다.
- `Domain/TerminalTabRegistry.cs`의 배열이 순서 정본이다. coordinator의 `UpdateWorkspace`는
  `stateLock` 안에서 변경·snapshot 생성을 하고 lock 밖에서 `WorkspaceChanged`를 발행한다.
  renderer `orderedSessions`/`renderTabs`는 `session-upsert.payload.order`로 기존 DOM을 재배치한다.
- `TerminalView.Session_WorkspaceChanged`는 event에 실린 과거 snapshot 대신 처리 시점의
  `sessionCoordinator.Snapshot`을 다시 읽어 `SyncWorkspace`한다. 이 경로는 제거된 ID의
  `remove-session`, 모든 현재 탭의 `session-upsert`(order/generation), 현재 ID의 `activate-session`을 보낸다.
- `TerminalWorkspacePersistence`는 같은 event를 소비하며 opt-in, restore/future schema/종료 guard와
  기존 debounce·동일 구성 건너뛰기를 적용한다. 저장은 runtime ID 대신 configuration ID와 배열 순서에서
  계산한 `Order`를 사용한다. 드래그 요청이나 시작 snapshot을 저장 입력으로 사용하지 않는다.
- 현재 `SyncWorkspace`는 탭 snapshot과 generation을 각각 조회한다. runtime 담당자는 드래그의
  비교·변경을 **동일 coordinator lock**에서 수행하고 resync의 ID/order/generation도 일관된 조회로
  얻어야 한다. renderer generation/instance 수명 guard는 View가 소유한다. public contract나
  SharedKernel type을 추가할 이유가 없으며 필요한 DTO와 진입점은 Terminal 내부에 둔다.

### 요청 필드

새 내부 요청 이름은 **`reorder-session`**이다. `move-session`을 확장하거나 여러 번 전송하지 않는다.
host와 bundled renderer를 함께 배포하므로 기존 version 2 안에 이 내부 메시지를 추가한다.
fixture의 `fixtureVersion`은 테스트 자료 형식이며 제품 protocol/settings version이 아니다.

```json
{
  "version": 2,
  "type": "reorder-session",
  "sessionId": "00000000000000000000000000000001",
  "payload": {
    "rendererInstanceId": "10000000000000000000000000000001",
    "sessionGeneration": 1,
    "targetSessionId": "00000000000000000000000000000003",
    "position": "after",
    "startOrder": [
      { "sessionId": "00000000000000000000000000000001", "sessionGeneration": 1 },
      { "sessionId": "00000000000000000000000000000002", "sessionGeneration": 2 },
      { "sessionId": "00000000000000000000000000000003", "sessionGeneration": 1 }
    ]
  }
}
```

| 필드 | 파싱·의미 |
|---|---|
| `version`, `type` | 숫자 2, 정확한 `reorder-session` 문자열. 기존 envelope와 최대 메시지 길이 제한 유지 |
| root `sessionId` | 이동할 runtime session ID. configuration ID가 아님 |
| `payload.rendererInstanceId` | 현재 페이지가 `ready`에 보낸 instance ID. drag 시작 시 캡처한 값 |
| `payload.sessionGeneration` | 이동할 세션의 시작 시 generation |
| `payload.targetSessionId` | 삽입 기준이 되는 현재 runtime 탭 ID. 첫/끝 위치도 첫/끝 **다른** 탭을 기준으로 표현 |
| `payload.position` | 정확히 `before` 또는 `after`. index, 방향 반복, 임의 최종 배열은 받지 않음 |
| `payload.startOrder` | pointer down 시 캡처한 전체 순서. 1~8개 `{sessionId, sessionGeneration}` 배열. ID 중복 금지 |

모든 ID는 기존 parser처럼 non-empty GUID `N` 형식(32 hex, 대소문자 허용)이다. 비교는 GUID 의미로
하고 renderer가 보낼 때는 소문자로 정규화한다. 모든 generation은 JSON 숫자 safe integer
`1..9007199254740991`이며 문자열·소수·null을 허용하지 않는다. root ID가 `startOrder`에 정확히 한 번
있고 그 generation이 payload 값과 같아야 파싱 성공이다. 모든 필드는 필수다. 기존 parser처럼 알 수
없는 추가 필드는 무시하되 정렬의 근거로 사용하지 않는다. 잘못된 필수 필드는 기본값으로 보정하지 않는다.
target의 존재·자기 자신 여부는 파싱 이후 host 판정이다(닫힌 탭 요청도 정확히 구분한다).

### 판정 순서와 원자적 이동

아래 reason은 fixture의 테스트 기대값이며 새로운 공개 status API나 응답 메시지가 아니다.

1. envelope/필드 조건을 파싱한다. 실패는 `invalid-message`, 순서 변경 없음.
2. View가 disposed/renderer failed/not ready/CoreWebView2 부재 또는 현재 sender와 다른 경우
   `renderer-unavailable`. 현재 `ready` instance와 다른 경우 `stale-renderer`. 지연 실행 callback은
   캡처한 host의 `rendererGeneration`도 재확인한다. 이 host 값은 wire 필드로 노출하지 않는다.
3. coordinator lock 안에서 이동 ID가 없으면 `missing-session`, payload generation이 현재와
   다르면 `stale-generation`, target이 없으면 `missing-target`, target이 자신이면 `self-target`.
4. 같은 lock 안에서 현재 전체 `(ID, generation)` 순서와 `startOrder`를 길이·위치·generation까지
   비교한다. 하나라도 다르면 `stale-order`. 탭 추가/닫기, 좌우 이동, 다른 드래그와 **다른 탭의 재시작**도
   여기서 거절한다. 배경 output/이름/state만 바뀌고 ID·generation·순서가 같으면 유효하다.
5. source를 제거한 임시 목록에서 target을 찾아 그 직전/직후에 source를 삽입한다. source 제거 전
   index를 재사용하지 않는다. 최종 순서가 현재와 같으면 `no-op`/`same-position`; 다르면 `applied`/`moved`.
   전체를 한 번에 적용하며 active ID, generation, tab metadata, session 객체/PID와 terminal 상태를 보존한다.
6. `applied`만 `WorkspaceChanged`를 한 번 발행한다. `no-op`/거절/취소는 발행하지 않는다.
   persistence opt-in이면 기존 경로가 저장을 예약하고, off면 runtime 순서만 바뀐다.
   opt-in이 켜져 있어도 기존 unsupported shell/future schema guard를 우회하지 않는다.

`startOrder`는 optimistic 비교용이며 교체할 정본 배열이 아니다. 새 order revision/settings schema는
도입하지 않는다. 처리 시 전체 ID·generation 순서가 시작값으로 돌아온 경우(순서만 변경 후 복귀)는
동등한 상태로 수락한다. 한 번 처리된 메시지의 재전송은 순서가 달라졌으면 `stale-order`다. 따라서
renderer는 유효한 drop당 한 번만 전송하고 자동 재시도하지 않는다. lock 밖에서 검증 후 lock 안에서
적용하는 경로는 허용하지 않는다. 1개 탭은 renderer가 요청을 만들지 않는다; 강제로 보낸 자기 대상은 거절한다.

### 취소·응답·재동기화

- threshold 전 release는 기존 클릭이다. 정렬 이후 Escape/pointercancel/lost capture/대상 밖 release,
  panel·renderer 수명 종료, drag 시작 목록과 다른 ID·generation·순서 수신은 `cancelled`다.
  이동 wire 메시지를 보내지 않고 capture/삽입 표시/auto-scroll을 정리한다. 취소 Escape는 shell로 보내지 않는다.
- renderer는 실제 배열/`entry.order`를 낙관적으로 바꾸지 않고 삽입 표시만 보여준다. 취소 시 마지막으로
  받은 host 상태를 그대로 렌더한다(`local-current`). 과거 `startOrder`로 되돌리거나 host에 취소 메시지를
  보내지 않는다. 수명 종료 후에는 새 페이지의 초기 동기화가 담당한다(`none`).
- applied/no-op/파싱 가능한 거절 뒤에는 기존 `SyncWorkspace` 경로로 **처리 시점의 현재** 목록을
  현재 살아 있는 renderer에 다시 보낸다(`current`). 거절 때문에 `WorkspaceChanged`를 발행하지 않는다.
  오래된 renderer 요청을 받았어도 응답은 과거 instance에 보내지 않는다. 현재 instance가 ready인 경우
  그 instance에만 동기화하고, 사용할 renderer가 없으면 다음 ready/initialize까지 보류한다.
- 기존 parser가 모든 malformed 요청을 버리는 경로는 유지한다. 다만 정상 version/type envelope로
  식별되는 `reorder-session`의 payload 파싱 실패는 ready인 현재 renderer를 동기화한다.
  틀린 version/type/root 형태는 기존처럼 무시한다(`none`). command/output 내용을 log에 넣지 않는다.
- `session-upsert`의 중간 상태는 drag 시작 목록으로 사용하지 않는다. 기존 `activate-session`과
  최신 order를 batch 완료 시 함께 적용한다. 아래 workspace-sync 보충 계약에 따라 host의 완전한
  목록을 검증하며, 별도 ack/request ID나 자동 재시도는 추가하지 않는다.
- 같은 active ID의 순서 동기화는 탭/terminal에 focus를 요청하지 않으며 ConPTY resize/reset을 유발하지
  않아야 한다. 현재 renderer `activateSession`의 `fitSession(nextEntry, true)` 경로에 그대로 연결하면
  이 조건을 깨뜨릴 수 있으므로 renderer 담당자가 같은 ID의 정렬 갱신 경로를 구분한다.
  실제 크기 변경과 기존 사용자 탭 선택의 fit/focus 정책은 유지한다. 취소 중 탭이 닫혔다면 host의
  현재 active/focus 대상을 따른다; 이미 사라진 ID로 focus를 복원하지 않는다.

## 공통 검증 자료와 소비 방법

### workspace-sync 보충 계약 — 2026-10-13

- `tab-drag-reorder-workspace-sync`는 기존 coordinator의 `RendererWorkspace` snapshot과
  `SyncWorkspace`를 재사용한다. version 2 global `workspace-sync-begin` / `workspace-sync-complete`를
  기존 remove/upsert/activate 메시지 앞뒤에 보낸다. 두 payload는 같은 snapshot의 전체
  `order: [{sessionId, sessionGeneration}]`와 `activeSessionId`(빈 workspace는 null)를 가진다.
- renderer는 begin부터 complete까지 드래그 시작 목록을 제공하지 않는다. complete의 목록·활성 ID가
  begin과 같고 live entry의 ID·generation·순서가 정확히 일치해야 방어 복사한 전체 시작 목록을 제공한다.
  중간 DOM 재배치는 보류한다. batch 밖의 추가·삭제·generation·순서 변경도 확정 목록을 무효화하지만
  출력·이름·상태만 바뀐 메시지는 무효화하지 않는다. 불완전하거나 잘못된 완료는 사용 가능한 목록을 만들지 않는다.
- 같은 active ID의 정렬·거절 재동기화는 fit/resize/focus를 요청하지 않는다. 재배치로 browser가 탭
  내부의 기존 키보드 focus를 잃으면 살아 있는 같은 element만 복원한다. terminal pane은 이동하지 않는다.
  명시적인 사용자 선택·키보드 선택·새 탭·닫기 후 활성화에 대한 기존 focus/fit 정책은 유지한다.
- 영향 범위는 RendererProtocol, TerminalView, renderer src/test/package.json/dist,
  RendererProtocolTests/RendererDistributionTests와 이 계획으로 한정한다. 새 public API·설정 schema·의존성은
  추가하지 않고 완료된 계약 Task와 공통 fixture는 보존한다. Node 동작 테스트로 실제 메시지 처리 및
  DOM/terminal 모형의 focus·resize를 검증하고, 실제 WebView2 focus/IME는 미검증으로 구분한다.
- 검증: 지정 npm ci/test/build, solution restore/build/test, C# 정렬 검사와 diff check.
  이 보충 구현의 완료 근거는 아래 workspace-sync 완료 요약에 기록한다.

정본은 [cases.json](../../tests/Starboard.Modules.Terminal.Tests/Fixtures/tab-drag-reorder/cases.json)이다.
`states`의 각 상태는 전체 ordered `sessions`(ID/generation), active ID, renderer ready/instance를 가진다.
각 case의 `hostState`는 **처리 시점** 상태, `dragState`는 renderer의 **시작 시점** 상태다.
`message`는 보정 없이 그대로 직렬화할 wire 입력이며 취소/클릭에는 null이다. `rendererAction`은
`drop`, `click`, `cancel:<원인>`이며 malformed host 입력은 `not-generated`(renderer가 생성하지 않음)다.
runtime 기대값은 `expected.parseAccepted/result/reason/order`,
`workspaceChangedCount/resync`; renderer 기대값은 `rendererOutcome/sendCount`다. 모든 case에서 현재
active ID와 host의 ID별 generation·metadata·세션 객체는 보존한다. `persistenceModes: [false, true]`는
두 설정에서 실행하라는 뜻이며 새 설정 항목이 아니다.
`legacyMoves`는 기존 version 2 `move-session` 좌우 요청과 끝 위치의 무변경 기대값이며 runtime 회귀
테스트에서 함께 읽는다. 기존 좌우 이동에 드래그의 필수 필드나 새로운 거절 재동기화 정책을 강제하지 않는다.

- runtime 담당자: 기존 `RendererProtocolTests`, registry/coordinator 테스트에서 `System.Text.Json`으로
  같은 JSON을 읽는다. invalid message는 실제 parser를, 나머지는 host lifetime guard와 coordinator의
  실제 이동 경로를 검증한다. source만 있는 세션 재시작뿐 아니라 전체 startOrder 비교를 검증한다.
  `workspaceChangedCount`를 관찰하고 applied의 ID별 session/PID 보존, no-op/거절의 무변경을 확인한다.
  opt-in/off 테스트는 기존 workspace 저장 mock에서 실제 debounce를 기다려 저장 off=0, on=지원되는
  구성의 최신 순서 저장을 검증한다. fixture의 event 수를 물리 파일 쓰기 횟수로 간주하지 않는다.
- C# fixture 탐색: `RendererDistributionTests.FindSolutionRoot`와 같은 solution-root 탐색 후
  `tests/Starboard.Modules.Terminal.Tests/Fixtures/tab-drag-reorder/cases.json`을 읽는다. 작업 디렉터리나
  개인 절대 경로에 의존하지 않는다. csproj copy 설정이나 package/public API 변경 없이 소비할 수 있다.
- renderer 담당자: 기존 `Renderer/test/*.test.mjs` 방식의 Node 정책 테스트에서
  `new URL("../../../../../../tests/Starboard.Modules.Terminal.Tests/Fixtures/tab-drag-reorder/cases.json", import.meta.url)`로
  동일 파일을 읽는다. test 파일은 기존 `Presentation/Renderer/test/`에 둔다. 시작 상태로 순수 정책을
  초기화하고 처리 상태를 입력한 후 click/cancel/drop을 실행해 실제 `sendCount`와 payload를 검증한다.
  stale host 사례에서 renderer는 취소하되 runtime 테스트는 이미 전송돼 늦게 도착한 `message`를 검증한다.
  JSON의 기대값이나 정렬 계산을 복사한 대체 구현으로 제품 테스트를 통과시키지 않는다.
- [validate-fixtures.test.mjs](../../tests/Starboard.Modules.Terminal.Tests/Fixtures/tab-drag-reorder/validate-fixtures.test.mjs)는
  fixture 자체의 구조·ID·순열·필수 사례·기대값 모순을 검사한다. **제품 구현 테스트가 아니다.**
  이 Task에서 실행할 좁은 검증은 다음이며 이후 두 구현은 같은 자료를 읽는 실제 테스트를 추가한다.

```powershell
node tests/Starboard.Modules.Terminal.Tests/Fixtures/tab-drag-reorder/validate-fixtures.test.mjs
git diff --check
```

fixture는 정상 앞/뒤/중간·2/8탭, 비활성 이동, 동일 위치, 취소/클릭/1탭, 순서 변경 경쟁·재전송,
source/target 소실·자기 대상, source/다른 탭 generation 변경, renderer 교체/부재, malformed 입력을 포함한다.
이 contract Task는 계획·fixture만 바꾸므로 solution build/test, WebView2 drag, persistence 실제 저장은
수행하지 않는다. 제품 구현과 실제 UI 인수 결과는 후속 Task에서 별도로 기록한다.

## 범위와 영향 파일

아래 경로 안에서 필요한 선언·호출부·테스트를 함께 수정한다. `dist`는 renderer source로 재생성한다.

- `src/Modules/Starboard.Modules.Terminal/Domain/TerminalTabRegistry.cs`
- `src/Modules/Starboard.Modules.Terminal/Application/TerminalSessionCoordinator.cs`
- `src/Modules/Starboard.Modules.Terminal/Application/RendererMessage.cs`, `RendererProtocol.cs`
- `src/Modules/Starboard.Modules.Terminal/Presentation/TerminalView.xaml.cs`
- `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/src/index.ts`, `styles.css`
- 필요하면 같은 renderer `src/`에 순수 drag/삽입 위치 정책 파일, `test/`에 Node 테스트
- `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/dist/**`
- `tests/Starboard.Modules.Terminal.Tests/TerminalTabRegistryTests.cs`, `TerminalSessionCoordinatorTests.cs`,
  `RendererProtocolTests.cs`, `RendererDistributionTests.cs`, 필요한 workspace 저장·복원 테스트
- `tests/Starboard.Modules.Terminal.Tests/Fixtures/tab-drag-reorder/**` (공통 계약 입력과 자료 검증)
- 이 계획, `docs/plans/README.md`, `docs/test-plan.md`와 구현된 사용법을 설명하는 `README.md`

제외: 분할 화면, 탭 분리/별도 창, 창 위치 이동, 다른 앱으로 drag, shell 상태 복제, 저장한 탭 목록의
drag, WSL 구성 영속화, 새 설정·의존성, 다른 module 리팩터링, 버전 변경·release·배포.

## 오케스트레이터 Task와 병렬 경계

| Task ID | 제목 | 선행 | 파일 소유권과 완료 기준 |
|---|---|---|---|
| `tab-drag-reorder-contract` | 탭 드래그 정렬 계약 확정 | 없음 | 이 계획의 메시지 필드·오래된 요청·취소·순서 유지 규칙과 검증 fixture 확정. 제품 구현 전 계약 gate. |
| `tab-drag-reorder-runtime` | 세션을 유지한 위치 변경 구현 | contract | registry/coordinator/protocol/TerminalView 및 해당 C# 테스트만 소유. 한 번의 유효한 이동, no-op·경쟁 요청 거부, 기존 저장 흐름 확인. |
| `tab-drag-reorder-renderer` | 드래그 입력과 삽입 위치 UI 구현 | contract | renderer src/test/dist와 RendererDistributionTests만 소유. threshold·capture·취소·overflow·click 충돌 구현과 순수 정책 테스트. |
| `tab-drag-reorder-integration` | 통합 검증과 사용자 문서 갱신 | runtime, renderer | 메시지 왕복·workspace 순서·세션 보존 회귀 검증, 계획/테스트 계획/README 완료 상태 갱신. |

contract 완료 후 runtime과 renderer는 확정된 메시지 fixture를 공유하고 서로 다른 파일에서 병렬로
진행할 수 있다. `index.ts`·`styles.css`·`dist`와 RendererDistributionTests는 한 담당자만 수정한다.
계약 변경은 두 구현에 먼저 알리고 반영한 뒤 합류한다. 통합 전 두 작업이 각각 검증돼야 한다.
이 문서 작성만으로 다른 채팅을 생성·메시지 전송하거나 진행 중인 오케스트레이터를 중단하지 않는다.

## 구현 단계

- [x] `tab-drag-reorder-contract`: 할당된 worktree/dirty 상태를 확인하고 내부 요청·취소 계약과 fixture를 고정한다.
- [x] `tab-drag-reorder-runtime`: 기존 순서 변경 및 stale/no-op·저장 경로를 보존하고 동기화·세션 보존 회귀 검증을 보완했다.
- [x] `tab-drag-reorder-renderer`: drag UI·자동 스크롤·수명 정리, Node 이벤트 검증과 source/dist를 구현한다.
- [x] `tab-drag-reorder-integration`: 메시지 왕복·저장/복원과 전체 자동 검증, 구현 상태·사용법·수동 검증 matrix 갱신 완료. 실제 화면 인수는 대기다.

## 검증 방법

다음은 향후 제품 구현 시의 검증이며 이번 기획 커밋에서 실행한 결과가 아니다.

- 자동: 앞/뒤/중간 이동, 동일 위치, 없는/자기 자신 대상, 순서 변경 경쟁과 stale generation/renderer,
  활성 ID·세션 객체 보존, 저장 opt-in/off와 복원 순서, 기존 좌우 이동·닫기·선택 동작을 테스트한다.
- renderer 정책 자동: threshold 전 클릭/후 click 억제, 삽입 위치, overflow 스크롤, 취소 및 listener/
  capture 정리, 배경 출력 중 유지와 순서 변경 시 취소를 검증한다. 소스 문자열 검사만 하지 않는다.
- DOM/layout 자동 또는 simulated: 1/3/8개 탭, 짧은/긴/한글 이름, 좁은 폭과 네 theme에서 탭·닫기·
  `+`·손잡이·`▾` hit target과 삽입 표시가 겹치지 않는지 확인한다. 실제 WebView2 결과와 구분한다.
- 실제 WebView2: mouse drag와 취소, overflow 양방향, 비활성 탭, 좌우 이동 대체 경로, 이름 편집·IME,
  `×` 닫기 확인, `+` profile 선택, `▾` 저장 탭, panel 높이 조절·축소/확장과 focus를 확인한다.
  장기 실행·입력·scrollback을 가진 탭의 PID/상태 유지 및 opt-in 재실행 순서도 확인한다.
- 실제 DPI 100/125/150/200%와 잠금 해제·renderer 재연결을 구분해 기록한다. 수행하지 못한 시나리오는
  `docs/test-plan.md`에 `Not run (manual)` 또는 `Blocked (manual)`로 남긴다.

```powershell
npm --prefix src/Modules/Starboard.Modules.Terminal/Presentation/Renderer ci
npm --prefix src/Modules/Starboard.Modules.Terminal/Presentation/Renderer run build
dotnet restore Starboard.Windows.sln
dotnet build Starboard.Windows.sln --configuration Debug --no-restore
dotnet test Starboard.Windows.sln --configuration Debug --no-build --no-restore
pwsh -NoProfile -File scripts/Test-CSharpAlignment.ps1 -WorkingTree
git diff --check
```

새 Node 정책 테스트는 기존 renderer test 방식과 연결해 실행 명령을 구현 기록에 남긴다.
실제 drag 화면을 수행하지 못했다면 자동 검증 완료와 제품 수동 인수 미완료를 분리해 보고한다.

## 진행 기록

- 2026-10-13 renderer 착수: 선행 `WorkspaceSync.getDragStartOrder()`와 기존 Node renderer harness를
  재사용한다. tab button의 왼쪽 mouse pointer만 후보로 받고 6 CSS px 초과 시 삽입 표시를 켠다.
  capture·document listener·제한된 tab-list RAF scroll을 controller가 소유하고 종료 시 정리한다.
  동기화 begin 목록과 개별 upsert/remove의 ID·순서·generation 변경에 취소를 연결한다.
  기존 활성화·focus 보존·workspace 동기화는 재구현하지 않는다. 실제 WebView2/IME/DPI는 manual로 남긴다.

- 2026-10-13: 현재 코드의 좌우 이동 경로와 드래그 미지원 상태를 확인하고 기획·Task·병렬 소유권을 작성했다.
- 2026-10-13 contract: 할당된 clean worktree(`4ea3727`)에서 기존 parser/registry/coordinator/View/renderer와
  persistence 경로를 조사했다. version 2 `reorder-session`, 전체 ID·generation startOrder 비교,
  취소·거절의 현재 상태 동기화 및 공통 fixture 소비 방법을 확정했다. 제품 구현·실제 WebView2 검증은 미착수다.
- 계약 자료 검증: Node v24.18.0에서 fixture 검사를 직접 실행했다. 기본 `node --test`는 이 sandbox의
  child process `spawn EPERM`으로 시작하지 못해, child process를 만들지 않는 위 직접 실행 명령을 사용한다.
  이는 자료 정합성 검증이며 C# parser/coordinator, renderer 정책 또는 실제 WebView2의 통과 결과가 아니다.

## 계약 Task 완료 요약

- 메시지 필드·검증 우선순위·원자적 이동·취소/거절 resync와 기존 좌우 이동/opt-in 저장 보존 규칙을 확정했다.
- `cases.json`에 공통 사례 50건과 좌우 이동 회귀 4건을 추가하고 C#/Node의 같은 파일 소비 경로를 확인했다.
- fixture 정합성 검사 **56 passed**, 문서 링크·renderer 상대 경로 확인과 `git diff --check` 통과.
  지정된 계획·fixture 경로만 변경했다. 제품 구현·solution build/test·실제 WebView2 검증은 후속 Task 범위다.

## workspace-sync Task 완료 요약 — 2026-10-13

- 기존에는 개별 upsert마다 탭을 이동하고 같은 active ID에도 강제 resize를 전송했다. 이제 같은
  coordinator snapshot의 begin/complete로 전체 ID·generation·order와 active ID를 검증한 뒤
  탭 DOM과 활성 상태를 확정한다. 초기·부분·잘못된 완료·batch 밖의 구조 변경에서는
  `WorkspaceSync.getDragStartOrder()`가 undefined를 반환하고, 완료된 목록은 방어 복사한다.
- 이름·상태·일반 출력 갱신은 구조 변경과 구분한다. 같은 active ID의 정렬/거절 재동기화는
  terminal 객체·pane·키보드 focus를 유지하며 fit/resize/reset을 요청하지 않는다. 탭 DOM 이동 시
  기존의 살아 있는 focused element만 복원한다. 사용자 선택·닫기 뒤의 활성화는 기존 정책을 유지한다.
- source에서 dist를 재생성했다. 새 global 메시지의 C# 직렬화·방향/범위 검증과 host batch 연결을
  검사하고, production renderer의 WebView message listener를 실제 실행하는 Node 테스트를 추가했다.
  browser DOM·layout·xterm만 모형이며, 공통 fixture의 상태를 그대로 사용하고 fixture는 변경하지 않았다.
- 검증 PASS: npm ci (추가 dependency 없음), npm test **18 passed**, npm run build,
  지정 .NET 10 SDK의 solution restore, Debug build (**0 warnings / 0 errors**),
  solution test **561 passed / 0 skipped** (Terminal 331, DesktopIntegration 112, Preferences 37,
  Architecture 13, Integration 68), C# WorkingTree 정렬 검사와 `git diff --check`.
  먼저 영향받은 Terminal project도 331 passed로 검증했다. 초기 sandbox .NET 실행 및 esbuild spawn
  EPERM은 승인된 같은 로컬 검증을 권한 확보 후 재실행해 해결했다. analyzer의 테스트 이름 제한과
  C# continuation 정렬 실패는 기존 규약에 맞게 수정한 뒤 검증했다.
- 실제 WebView2 mouse drag·focus·IME·DPI는 **Not run (manual)**이다. 이번 결과는 자동 메시지·정책과
  simulated DOM 검증이며 실제 화면 인수를 대신하지 않는다. 새 public API·설정 schema·shell 생성/재시작
  경로와 다른 Task의 완료 상태는 변경하지 않았다.

## runtime Task 보완 완료 요약 — 2026-10-13

- 할당된 clean worktree의 `8d14ab9`에서 선행 workspace-sync와 `bd6e89f`의 registry/coordinator,
  reorder-session parser, TerminalView 처리 및 기존 테스트를 확인했다. 기존 구현으로 요청 범위의
  runtime 동작이 충족되어 제품 코드는 변경하지 않고 `TerminalTabReorderTests.cs`만 보완했다.
  renderer, 배포 asset, 공통 fixture, public API와 설정·저장 schema는 변경하지 않았다.
- 공통 fixture의 재동기화 사례에 실제 `workspace-sync-begin`/`workspace-sync-complete` 직렬화를
  연결했다. 두 메시지의 payload 일치, 현재 활성 ID, 전체 순서 및 각 generation을 검증하며 기존
  opt-in/off 저장·저장 실패·좌우 이동·선택·닫기 검증과 모든 shell 실행 구성 비교를 유지한다.
- 추가 회귀 4건: drag 시작 뒤 탭 추가(`stale-order`), 종료된 대상 닫기(`missing-target`), source
  재시작(`stale-generation`), target 재시작(`stale-order`). 거절은 WorkspaceChanged를 발생시키지
  않으며 최신 snapshot으로 재요청하면 정확히 한 번 이동한다. 거절/재요청 사이 활성 ID, 탭 객체,
  실제 테스트 session 객체, generation, 실행 구성, resize/dispose 횟수와 입력 경로를 보존한다.
  shell 종료·재시작 자체의 수명 변경은 기준 snapshot 전에 수행해 정렬에 의한 변경과 구분했다.
- 접근 조사: 작업 root·직접 상위 폴더·대상 obj는 Directory 속성이고 App.g.cs와
  Starboard_MarkupCompile.cache는 Archive 속성으로 읽기 전용이 아니었다. 조사한 root/상위 폴더/
  두 파일/테스트 obj ACL에 Deny 항목이 없었다. 작업 root, 해당 WPF obj 및 테스트 obj의 임시
  파일 생성·삭제, 두 캐시 파일의 배타적 ReadWrite 열기가 통과했다. 테스트 obj에 남은 `*.tmp`는
  없었다. probe는 지정 작업 디렉터리 내부에만 생성했고 제거했다.
- sandbox 프로세스 명령줄 조회는 실제 접근 거부였으며 승인된 읽기 조회로 재실행했다. 다른
  프로젝트의 .NET 프로세스는 종료하지 않았다. 현재 작업을 가리키는 중복 build/test 명령은
  조회에서 확인되지 않았고 두 WPF 파일의 점유도 재현되지 않았다. 최초 sandbox Terminal test는
  restore 이후 진행 출력이 없어 중단 신호를 보냈고 성공 결과로 계산하지 않았다. 허용된 실행
  권한으로 같은 SDK·Debug 명령을 순차 실행하자 완료됐다. 과거 App.g.cs/MarkupCompile/테스트
  임시 파일 접근 거부의 정확한 원인은 현재 증거로 확정할 수 없다. ACL/읽기 전용 변경이나
  bin/obj 정리는 하지 않았고 제품 코드·검증 기준으로 실행 제한을 우회하지 않았다.
- 최종 검증 PASS: 지정 SDK `C:\Users\round1studio_14\.dotnet\dotnet.exe`의 Terminal Debug test
  **335 passed / 0 skipped**, solution restore, Debug solution build **0 warnings / 0 errors**,
  같은 Debug solution test **565 passed / 0 skipped** (Terminal 335, DesktopIntegration 112,
  Preferences 37, Architecture 13, Integration 68), C# WorkingTree 정렬 검사, `git diff --check`.
  보완 중 internal enum의 public test 매개변수 노출, CA1826, 실행 중 탭 닫기 보호 정책과
  continuation 정렬 실패를 수정한 뒤 검증했다. 검증 산출물 쓰기 차단은 최종 실행에서 재현되지 않았다.
- 이번 검증은 C# coordinator·protocol 및 fake shell/store 회귀 검증이다. 실제 TerminalView/WebView2
  메시지 dispatch, mouse drag·focus·IME·DPI와 실제 shell PID/scrollback 인수는 **Not run (manual)**로
  유지한다. renderer/integration Task 완료 상태는 변경하지 않으며 commit/push/배포는 수행하지 않았다.

## renderer Task 완료 요약 — 2026-10-13

- 기존 클릭·좌우 이동 메뉴만 있던 renderer에 `src/tabDragReorder.ts` controller를 추가했다.
  2~8개 탭의 왼쪽 primary mouse button에서 완전한 시작 순서를 받아 움직임이 **6 CSS px 초과**일
  때 정렬을 시작한다. 탭 전체 영역의 중점을 기준으로 앞/뒤 삽입 표시를 하고 유효한 release에서
  기존 version 2 `reorder-session`을 한 번 보낸다. 자체 순서·active ID·terminal 객체를 변경하지 않는다.
- pointerdown의 기본 focus 이동을 막아 비활성 탭을 끌어도 keyboard focus가 유지된다.
  실제 drag 뒤 tab button의 mouse click/dblclick은 500 ms 동안 억제한다. keyboard click(detail 0),
  close/new/saved menu/높이 손잡이는 이 억제 범위 밖이다. 임계값 전 클릭과 일반 더블클릭·이름 편집
  IME composition 경로는 유지한다. 터미널 본문 pointer/selection, 외부 경로 drop handler는 변경하지 않았다.
- tab-list의 안쪽 양끝 최대 24 CSS px에서 list.scrollLeft만 RAF당 최대 8 CSS px 이동한다.
  스크롤 범위를 clamp하고 target/표시 좌표를 다시 계산한다. list 밖에서는 자동 스크롤하지 않는다.
- Escape, pointercancel/lost capture, 밖 release, 왼쪽 button 해제, blur/hidden/pagehide와 ID·순서·
  generation 변경은 요청 없이 capture·임시 listener·marker·RAF를 정리한다. pagehide는 controller의
  상시 listener와 marker도 해제한다. 같은 목록의 sync begin/complete, 이름·상태·배경 출력은 유지한다.
  선행 WorkspaceSync와 focus 보존을 그대로 재사용하고 구조 변경 관찰만 연결했다.
- 기존 npm test 실행 경로와 rendererHarness를 확장했다. 공통 `cases.json`에서 renderer gesture가
  있는 **32개 사례**를 읽어 production listener의 pointer/keyboard/click 이벤트와 실제 전송 payload를
  검증한다. malformed host-only 입력은 기존 계약/runtime suite가 소유한다. 추가 회귀는 모든 2~8개
  탭 수, 임계값, keyboard/terminal focus, 일반 doubleclick·IME, 클릭 억제 만료와 close 보존,
  양방향 overflow, 취소·수명 listener/capture/RAF 정리를 검증한다. layout/xterm/clock만 simulation이다.
  fixture, package.json 및 dependency는 수정하지 않았다.
- 검증 **PASS**: 지정 `npm ci`, `npm test` **59 passed / 0 skipped**, `npm run build`;
  재생성한 app.js/app.css와 동일 esbuild 옵션의 메모리 출력을 바이트 단위로 비교해 source/dist 일치를 확인했다.
  지정 .NET SDK(`C:\Users\round1studio_14\.dotnet\dotnet.exe`)의 solution restore/Debug build
  **경고 0 / 오류 0**, RendererDistributionTests **24 passed**, 전체 Debug solution test
  **566 passed / 0 skipped**(Terminal 336, DesktopIntegration 112, Preferences 37, Architecture 13,
  Integration 68), C# WorkingTree 정렬 검사와 `git diff --check`를 완료했다.
- 검증 중 기본 sandbox의 esbuild spawn EPERM과 출력 없는 SDK restore 실패는 같은 명령을 허용된
  환경에서 실행해 통과했다. 첫 전체 suite의 WPF foreground 보존 테스트 한 건은 renderer를 로드하지
  않는 test에서 foreground HWND 불일치로 실패했고, 코드 변경 없이 단독 및 전체 재실행은 통과했다.
  데스크톱 focus 변동 가능성은 남기며 첫 실패를 성공 횟수로 계산하지 않는다.
- 실제 WebView2의 native pointer capture·mouse click 타이밍·overflow 시각 결과, 외부 파일 drop,
  본문 selection·한글 IME와 mixed DPI/잠금 해제는 **Not run (manual)**이다.
  후속 integration에서 비활성 탭을 양방향으로 끌고 Escape/밖 release/재시작·탭 추가로 취소한 뒤,
  close/+/▾/손잡이, 이름 편집 및 본문 IME/selection/파일 drop을 함께 확인한다.
  이 Task는 앱 실행·설치·배포·commit을 수행하지 않았다.

## integration Task 완료 요약 — 2026-10-13

- 할당된 worktree에서 기존 fixture·경쟁·저장 실패·저장 옵션·세션 보존 테스트를 재사용했다.
  공통 fixture, 제품 코드, renderer dist, 앱 버전과 배포 경로는 변경하지 않았다.
- TerminalTabReorderTests에 production renderer listener와 C# parser/context/coordinator의
  JSON 왕복 테스트를 추가했다. Escape 취소 후 유효 drop 한 번, 전체 batch 완료 전후 DOM/startOrder,
  다음 drag의 최신 순서, 이전 요청 거절과 저장 파일 bytes 보존을 연결한다. 실제 파일 store load와
  새 coordinator restore에서 configuration 순서·활성 configuration·한글 이름 및 새 runtime ID를
  검사한다. DOM/xterm과 shell은 모형이며 native TerminalView dispatch는 실행하지 않는다.
- TerminalView dispatch·sender/generation guard·최신 snapshot batch 순서는 별도 정적 wiring guard로
  확인했다. renderer에 1/3/8개 탭×네 제품 theme, 긴 한글 이름·180 CSS px 폭·overflow 모의 matrix
  12건을 추가했다. 실제 marker 색·글자 clipping·WebView2 layout 결과로 확대하지 않는다.
- 검증 PASS: npm ci, npm test **71 passed / 0 skipped**, npm run build;
  지정 SDK `C:\Users\round1studio_14\.dotnet\dotnet.exe` solution restore, Debug build **경고 0 / 오류 0**,
  Debug solution test **568 passed / 0 skipped**(Terminal 337, DesktopIntegration 112, Preferences 37,
  Architecture 13, Integration 69), C# WorkingTree 정렬 검사와 `git diff --check`.
  새 연결 테스트는 먼저 focused target **1 passed**로 검증했다. npm ci가 선행돼야 C# bridge도 실행된다.
- 기본 sandbox의 esbuild spawn EPERM 및 진행 없는 focused SDK 실행은 통과로 계산하지 않았다.
  이번 테스트의 이름 길이·VM 비교·표준 stream UTF-8/BOM 문제를 수정한 후 허용된 실행 환경에서
  최종 검증했다. 과거 Task의 접근 거부 원인 해결을 추정하지 않고 기존 실패·완료 기록을 유지한다.
- README에 drag·overflow·Escape/밖 release·좌우 이동 대체 경로와 복원 ON/OFF 사용법을 추가했다.
  전체 C# 테스트의 Node dependency를 명시하고 계획 index를 구현·자동 검증 완료 상태로 갱신했다.
- 실제 WebView2 mouse/capture·PID·입력·출력·scrollback·IME·focus·100/125/150/200% 및 mixed-DPI는
  **Not run (manual)**이다. 현재 세션은 native 앱 제어가 비활성화되어 화면 인수를 수행하지 않았다.
  [수동 matrix와 재현 절차](../test-plan.md#탭-드래그-정렬-통합-회귀--2026-10-13)의 DRAG-MAN-01~06을
  대기로 남겼다. 자동 Task 완료는 실제 제품 화면 인수 또는 release 완료를 의미하지 않는다.
  사용자 앱 재시작·설치·commit·push·배포는 수행하지 않았다.

## 후속 — 탭 드래그 시각 피드백 보강 (2026-10-13)

### 제보·코드 근거와 목표

- 사용자 제보: 탭을 마우스로 옮길 때 표시가 약해 이동 중인 탭과 놓을 위치를 파악하기 어렵다.
  실제 화면의 원인 재현·대비 측정은 아직 수행하지 않았다.
- 현재 `.tab-insertion-marker`는 theme accent 색의 폭 **2 CSS px** 선이다. 정렬 중
  `data-reordering`은 탭 커서를 `grabbing`으로 바꾸지만 source 탭을 별도로 강조하거나
  마우스 옆 이름 미리보기를 표시하지 않는다. 자기 탭 위/목록 밖에서는 삽입선도 숨긴다.
  이는 코드에서 확인한 피드백 구성이지 renderer 실패나 실제 화면 검증 결과가 아니다.
- 목표: 정렬 임계값을 넘으면 **무엇을 옮기는지·어디에 놓이는지**가 동시에 보여야 한다.
  shell 수명, 순서 메시지·판정 계약, focus와 저장 옵션은 변경하지 않는다.

### UX와 인수 기준

1. 6 CSS px 임계값을 넘은 뒤에만 source 탭의 배경과 테마 accent 표시를 강화한다.
   활성 탭 표시와 drag 상태를 분리하며 비활성 탭을 끌어도 활성 ID는 바뀌지 않는다.
   padding·폭·높이·닫기 버튼 hit target은 유지하고 노란 테두리는 추가하지 않는다.
2. 유효한 삽입 위치는 **3~4 CSS px 선과 위/아래 작은 끝 표시**로 강조한다.
   밝고 어두운 테마에서 배경에 묻히지 않게 기존 theme token을 사용한다. 표시가
   `×`·`+`·`▾`·높이 손잡이와 탭 글자를 가리거나 hit testing을 가로채면 안 된다.
3. 마우스 옆에는 탭 이름만 담은 작은 비상호작용 미리보기를 표시한다. viewport 안으로
   제한하고 긴 이름은 말줄임한다. 실제 탭/xterm을 복제하지 않으며 닫기 버튼·terminal 내용은
   포함하지 않는다. `pointer-events: none`, `aria-hidden`으로 중복 접근성·focus를 만들지 않는다.
   DOM text로 이름을 설정해 HTML로 해석하지 않고, 목록 밖에서는 잘못된 drop 가능 표시를 하지 않는다.
4. 목록 안의 유효 target이 없으면 source 표시만 유지하고 삽입 표시는 숨긴다.
   목록 밖이면 미리보기를 숨기거나 비활성 형태로 명확히 구분한다. 무효 release는 기존대로 취소한다.
5. preview는 host 순서·DOM 정본을 낙관적으로 변경하지 않는다. 기존 drop당 한 번 전송,
   자동 스크롤, click/doubleclick 억제, Escape·pointercancel·blur·수명 종료·경쟁 취소를 보존한다.
   drop·취소·source 제거·renderer 복구 뒤 표시 class/element가 남지 않아야 한다.
6. 지속 애니메이션·깜빡임·새 상시 control은 추가하지 않는다. 1/3/8개 탭, 좁은 폭/overflow,
   긴 한글 이름, 네 theme와 100/125/150/200%에서 표시와 클릭 영역을 구분해 검증한다.

### 독립 Task: `tab-drag-visual-feedback`

- 제목: 탭 드래그의 이동 대상과 삽입 위치 표시 강화
- 상태: **구현·자동 검증 완료 (2026-10-14), 실제 화면 검증 대기**. renderer source/dist와 관련 테스트·문서를 변경했다.
- capabilities: `frontend`, `qa`; 위험도: 낮음.
- dependencies: 완료된 `tab-drag-reorder-renderer`와 `tab-drag-reorder-integration`.
- 영향 파일: renderer `src/tabDragReorder.ts`, `src/styles.css`, 필요한 최소 `src/index.ts`,
  재생성 `dist/**`, `test/tabDragReorder.test.mjs`와 필요하면 기존 `test/rendererHarness.mjs`,
  `RendererDistributionTests.cs`, 이 계획·계획 index·`docs/test-plan.md`.
- 제외: Terminal runtime/protocol/fixture 재설계, 다른 module, 새 dependency/schema,
  새 단축키·순서 정책, session 재시작, commit/push·release·배포.
- 파일 소유권: 위 renderer source/dist와 UI 테스트는 한 담당자가 함께 수정한다.
  동일 controller/CSS를 여러 Task로 나눠 병렬 편집하지 않는다.
- [x] source 강조·삽입선·이름 미리보기와 취소/수명 정리를 구현하고 source/dist를 동기화한다.
- [x] Node 동작 검증·renderer build·관련 C# 테스트와 전체 restore/build/test를 실행하고 완료 상태를 갱신한다.
- 자동 검증: 임계값 전 표시 없음, 비활성 source 강조와 active/focus 보존, 미리보기 이름의
  안전한 text 처리·viewport 제한, 무효 target, overflow, drop/모든 취소의 표시 정리와 기존 전송 횟수를 확인한다.
  CSS 문자열 검사나 모의 DOM만으로 실제 시각 대비·clipping을 통과로 처리하지 않는다.
- **Not run (manual):** 현재 실제 WebView2 화면 확인은 미수행이다. 구현 후
  [기존 수동 matrix](../test-plan.md#실제-webview2-수동-인수-절차-모두-대기)의 DRAG-MAN-01/02/04/05에
  source 표시·삽입선·이름 미리보기 관찰을 보강하고, 수행하지 못한 항목은 그대로 미수행으로 남긴다.

### 구현 판단 — 2026-10-14

- 기존 controller의 6 CSS px 초과 시작과 cancel/dispose 수명을 재사용한다. source의 dataset으로
  배경·안쪽 accent 표시만 바꿔 layout/닫기 hit target과 active 표시를 보존한다.
- 삽입선은 3 CSS px와 7 CSS px 끝 표시를 tab-list 내부에 제한한다. 이름은 entry.name을
  textContent로 설정하는 고정 위치 preview로 표시하고, viewport 크기와 측정된 preview 크기로 clamp한다.
  무효 target에서는 preview와 marker를 숨긴다. 모든 overlay는 pointer-events:none/aria-hidden이다.
- 자동 DOM 검증은 임계값, 안전한 이름, viewport clamp와 취소 수명을 확인한다. 실제 WebView2 native
  화면/Windows DPI 조작은 이 환경에서 지원되지 않아 48조합 모두 Not run으로 기록한다.

### 완료 요약 — 2026-10-14

- 6 CSS px 초과 후 source 배경과 아래쪽 3px 안쪽 accent를 표시한다. 활성 탭의 위쪽 표시,
  크기·닫기 영역과 focus는 유지한다. 유효 target에 3px 삽입선과 7px 끝 표시 및 이름 preview를
  제공한다. 이름은 textContent로 설정하며 최대 220px/viewport 제한과 말줄임을 적용한다.
- 무효 target에서는 선·preview를 숨긴다. 기존 cancel/dispose에서 source dataset과 이름을 정리하고
  pagehide에서는 overlay와 resize listener를 제거한다. 이름 변경과 viewport resize에 표시를 갱신한다.
  정렬 메시지, host 저장/셸 수명, active ID와 자동 스크롤 판정은 유지한다.
- PASS: npm ci, npm test **74 passed / 0 skipped**, npm run build, 동일 esbuild 설정으로
  source/dist JS·CSS 바이트 일치, RendererDistributionTests **24 passed / 0 skipped**.
  지정 SDK의 solution restore/Debug build **0 warnings / 0 errors**, 전체 Debug test
  **568 passed / 0 skipped** (Terminal 337, DesktopIntegration 112, Preferences 37, Architecture 13,
  Integration 69), C# WorkingTree 정렬 검사와 `git diff --check`.
- 첫 sandbox npm test는 esbuild spawn EPERM, focused .NET test는 NuGet audit 접근 NU1900으로 실패했다.
  같은 명령을 허용된 실행 환경에서 통과했다. 선 두께 변경 후 기존 좌표 기대값 2건을 갱신했다.
  dependency·analyzer·audit 설정을 변경하지 않았다.
- 실제 WebView2 화면, native pointer/focus/IME, 실제 shell PID와 1/3/8 탭×네 theme×100/125/150/200%
  48조합은 **Not run (manual)**이다. [후속 수동 절차와 결과 표](../test-plan.md#탭-드래그-시각-피드백-검증--2026-10-14)를
  갱신했다. 실제 화면 인수/release 완료를 의미하지 않는다. commit/push/배포는 수행하지 않았다.
