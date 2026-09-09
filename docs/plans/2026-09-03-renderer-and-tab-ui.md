# 다중 renderer와 terminal 탭 UI 구현 계획

## 목표

하나의 WebView2 안에서 session별 xterm 인스턴스를 유지하고, 접근 가능한 탭 UI로
여러 persistent shell session을 선택·생성·닫기·재시작할 수 있게 한다. renderer와
host 사이의 session 대상 메시지는 항상 유효한 session identifier를 포함하며,
탭 바를 추가한 뒤에도 collapsed panel에서 약 5줄의 terminal 본문을 유지한다.

## 참고 문서와 근거

- `AGENTS.md`
- `docs/development-workflow.md`
- `docs/code-style.md`
- `docs/plans/2026-09-01-windows-starboard-v01.md`
- 기존 `TerminalSessionCoordinator`, `TerminalTabRegistry`, renderer protocol 및
  `TerminalView`

현재 domain/application 계층에는 최대 8개 session을 관리하는 registry와
coordinator가 있으나, presentation은 active session의 output만 단일 xterm으로
전달한다. 따라서 새 session domain을 다시 만들지 않고 해당 계약을 renderer와
view에 끝까지 연결한다.

## 현재 상태

### 2026-09-09 패널 밀착과 터미널 내부 여백 후속

- **PANEL-UX-02:** 패널 바깥에 바탕화면이 띠처럼 보이지 않도록, 하단 작업표시줄의
  안쪽 경계에 축소 패널을 외부 간격 0으로 붙인다. 입력 줄의 여유는 패널 전체를
  띄우는 대신 terminal 본문 내부의 아래 여백으로 확보한다.
- 기존 본문 padding에 아래쪽 6 DIP 상당의 여백을 추가하는 것을 초기안으로 한다.
  여백도 terminal과 같은 배경색으로 채운다. xterm 화면을 덮는 overlay나 음수 margin은
  사용하지 않고, 실제 사용 가능 높이를 줄인 뒤 fit/ConPTY resize에 반영한다.
- WPF DIP·WebView CSS pixel·physical pixel의 배율 전달을 확인해 이중 scaling을
  피한다. 기본 패널 높이 200 DIP와 탭 바 높이는 유지하며 글자·커서가 잘리지 않아야 한다.
  행 수는 남은 실제 높이로 계산한다. 높이를 보존하면서 행 수도 무조건 같다고 약속하지 않는다.
- 이번 변경은 하단 작업표시줄의 축소 상태에만 적용한다. 확장 상태와 상단·좌우
  작업표시줄의 기존 geometry/padding은 유지한다. auto-hide 시에는 기존 안전 경계를 따른다.
- 자석처럼 드래그한 창을 붙이는 기능은 추가하지 않는다. 작업표시줄을 덮거나
  desktop work area를 예약하지 않으며 focus·z-order·세션 수명 정책은 유지한다.
- 영향: DesktopIntegration geometry와 관련 tests, Terminal renderer padding/fit과
  source/dist, 필요한 최소 표시 상태 계약 및 host 연결. 계약은 W1, renderer는 W2-C,
  geometry는 W2-E, 연결·통합 검증은 W3이 소유한다.
- 인수 기준: 100/125/150/200% geometry 자동 검증, 실제 화면에서 외부 틈·작업표시줄
  겹침 없음, 입력 줄·커서 표시, 작은 높이·확장/축소·auto-hide 회귀와 PID 유지 확인.
  실제 장비 결과와 브라우저/mock 결과는 구분한다.
- 상태: 기획만 반영, 구현 대기. [기존 6 DIP 외부 간격](2026-09-09-panel-taskbar-gap.md)은
  현재 배포 동작이며 위 내용은 이를 대체할 후속안이다. 코드·배포본은 변경하지 않는다.

### 2026-09-09 새 탭 버튼 위치 후속

- 사용자 화면에서 탭 바로 옆의 `×`를 새 탭 버튼으로 오인해 탭을 닫았다.
  `.tab-list`의 flex-grow 때문에 `+`가 패널 맨 오른쪽에 떨어져 있는 상태다.
- `.tab-list`가 남은 공간을 채우지 않게 하고 `+`를 마지막 탭 바로 뒤에 배치한다.
  추가 버튼과 마지막 탭 사이에 간격을 둔다. 색상보다 탭 안/밖의 배치로 역할을 구분한다.
- 후속으로 제공된 Windows Terminal 스크린샷처럼 `×`를 각 탭의 배경·경계 안에
  포함한다. 활성 탭은 이름과 닫기 영역 전체에 같은 배경을 적용하고 terminal 본문과
  이어지게 하며, 비활성 탭은 배경 명도로 구분한다. 상단 모서리는 가볍게 둥글린다.
  `+`는 마지막 탭 밖의 독립 버튼이다. 닫기 영역을 별도 탭처럼 보이게 나누지 않는다.
- 참고 이미지의 높이·여백을 그대로 복제하지 않고 기존 32px 탭 바와 테마를 유지한다.
  색상만으로 선택 상태를 전달하지 않으며 기존 접근성·키보드 focus 표시를 보존한다.
- 오른쪽 `∨` 셸 선택 메뉴는 후속 후보로만 기록한다. 현재 범위에서는 새 메뉴를
  추가하지 않고 `+`가 기존 기본 셸로 새 탭을 만드는 동작을 유지한다.
- 탭 overflow는 기존 가로 스크롤로 처리하며 `+`는 스크롤 영역 밖에서 유지한다.
  닫기 기능·키보드 단축키·세션 수명과 32px 탭 바 높이는 변경하지 않는다.
- 영향: renderer `src/styles.css`, 필요 시 탭 DOM을 구성하는 `src/index.ts`와
  `src/index.html`, 대응하는 재생성 `dist` asset, 이 계획과 테스트 기록.
- 검증: 1/3/8개 탭과 좁은 폭의 브라우저 렌더링, renderer build 및 전체 .NET suite.
  활성·비활성 탭의 닫기 영역, `+`와 `×`의 서로 겹치지 않는 클릭 영역, tooltip·
  접근성 이름, 4개 테마에서의 선택 구분과 키보드 focus를 확인한다.
  바탕화면 배포·앱 재실행은 이번 수정에 포함하지 않는다.
- 상태: 기획만 반영, 구현 대기. 사용자 요청 정정에 따라 시도한 CSS·배포 asset
  변경은 모두 원복했다. 바탕화면 앱은 변경하지 않았다.
- 후속 [작업공간 기획](2026-09-09-workspace-convenience.md)의 W2-C에서 함께 처리한다.
  실제 WebView2 수동 검증과 브라우저 검증은 구분한다.

- protocol v2, session별 xterm registry, tab UI와 schema 3 migration 구현 완료
- post-implementation 검토에서 확인한 TypeScript envelope scope와 tablist 방향키
  focus 공백 보완 완료
- renderer/Terminal/Preferences/DesktopIntegration 및 전체 solution 자동 검증 완료
- 실제 WebView2의 overflow, IME, focus와 200 DIP 본문 행 수는 수동 확인 대기
- 2026-09-04 후속 조정으로 terminal focus ring 제거와 collapsed 본문 약 3행 확대,
  schema 4 migration 및 배포 갱신 완료

## 구현 범위

- C# protocol parsing/serialization 경계와 TypeScript message contract를 session-aware로 변경
- 단일 WebView2 document 안에서 session별 xterm/fit addon/scrollback/state 보존
- 절제된 가로 overflow tab bar, 새 탭·선택·닫기·재시작 affordance와 접근성 상태
- `Ctrl+Shift+T`, `Ctrl+Tab`, `Ctrl+Shift+Tab`, `Ctrl+Shift+W` routing 및
  `Ctrl+W` shell 전달 유지
- input, resize, copy, paste, output, reset/remove/error를 정확한 session에 routing
- session별 loading, running, exited/error 상태와 해당 session만 restart하는 action
- renderer 측정값과 실제 tab bar 높이를 반영한 약 5행 collapsed 기본 높이 및
  이전 schema migration
- terminal 내부 focus를 나타내던 노란 외곽선 제거
- 기본 collapsed 높이를 200 DIP로 올리고 schema 3의 148 DIP 기본값만 migration
- source/dist 동기화와 관련 자동 테스트·문서 검증

## 제외 범위

- drag reorder, split pane, 별도 terminal window, 앱 재시작 간 tab 복원
- shell title escape sequence 기반 동적 tab 이름
- 새 renderer/runtime dependency 추가
- tray, global hotkey, focus/z-order 정책 변경
- 실제 mixed-DPI/IME/Explorer restart hardware 수동 검증

## 영향 파일

- `src/Modules/Starboard.Modules.Terminal/Application/RendererMessage.cs`
- `src/Modules/Starboard.Modules.Terminal/Application/RendererProtocol.cs`
- `src/Modules/Starboard.Modules.Terminal/Presentation/TerminalView.xaml*`
- `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/src/**`
- `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/dist/**`
- `src/Modules/Starboard.Modules.Preferences/Contracts/AppSettings.cs`
- `src/Modules/Starboard.Modules.Preferences/Application/SettingsValidator.cs`
- `src/Modules/Starboard.Modules.DesktopIntegration/DesktopIntegrationModule.cs`
- `src/Starboard.Windows/Shell/MainWindow.xaml`
- 필요한 `src/Starboard.Windows/Composition/**` 호출부
- Terminal/Preferences의 집중 test와 관련 documentation

## 아키텍처 결정

- WebView2는 기존처럼 하나만 소유한다. renderer 내부 map이 session id를 key로
  독립 `Terminal`과 `FitAddon`을 소유하며, 비활성 terminal DOM도 숨기기만 해
  output buffer, scrollback과 emulator state를 보존한다.
- renderer 전역 메시지(`ready`, renderer 자체 오류)는 session id가 없고,
  session을 대상으로 하는 생성/선택/output/input/resize/clipboard/reset/remove
  메시지는 session id가 필수다.
- TypeScript envelope도 global/session message discriminated union으로 분리해
  session 대상 message를 session ID 없이 생성할 수 없게 한다.
- C# view는 coordinator snapshot을 tab UI state로 투영하며 shell 수명은 계속
  coordinator가 소유한다. module 간 새 참조나 event bus를 추가하지 않는다.
- output backlog는 session별 bounded buffer로 분리해 느린 한 session이 다른
  session output이나 routing을 오염시키지 않게 한다.
- collapsed 높이 migration은 schema version과 기존 기본값을 함께 판별한다.
  명시적으로 사용자 지정한 높이는 수치가 이전 기본값과 같지 않은 한 보존한다.
- 새 기본 높이는 기존 116 DIP terminal 본문에 32 DIP tab chrome을 더한 148 DIP로
  정의한다. schema 2 이하의 116 DIP만 새 기본값으로 migration하고 다른 높이는
  사용자 지정으로 간주한다.
- 후속 기본 높이는 13px font와 1.35 line-height에서 약 3행인 52.65 DIP를 더해
  200 DIP로 정한다. schema 3 이하에서 정확히 148 DIP인 값만 migration하며 다른
  값은 사용자 지정으로 보존한다.
- terminal 자체는 caret으로 입력 focus를 충분히 나타내므로 xterm 전체를 두르는
  focus ring은 제거한다. 탭과 버튼의 keyboard focus-visible 표시는 유지한다.

## 위험 영역과 fallback

- WebView2/xterm 실제 keyboard event ordering은 WPF unit test로 완전 검증할 수
  없다. protocol/parser와 pure routing state는 자동화하고 IME·clipboard·focus는
  수동 확인 항목으로 남긴다.
- session을 닫는 동안 output/exit event가 경합할 수 있다. registry snapshot에
  없는 id의 late event/message는 무시하고 xterm dispose/remove를 idempotent하게 한다.
- resize observer가 숨겨진 tab에서 0 크기를 낼 수 있다. active terminal만 fit하고,
  선택 시 다시 fit한 뒤 해당 session resize를 보고한다.
- renderer process 전체 실패 시 기존 전체 reconnect surface를 유지하고, shell
  exit는 tab-local error/restart UI로 제한한다.

## 구현 단계

- [x] session-aware protocol과 invalid message 거부 테스트
- [x] session별 xterm registry, routing, keyboard/clipboard 정책
- [x] WPF tab bar와 coordinator lifecycle/status 연결
- [x] collapsed 높이 schema migration과 테스트
- [x] renderer dist 재생성 및 문서 동기화
- [x] 지정된 build/test와 추가 관련 suite 실행
- [x] TypeScript contract scope와 tablist 방향키 focus 보완 후 재검증
- [x] terminal focus ring 제거와 renderer dist 재생성
- [x] schema 4의 200 DIP 기본 높이 및 migration 구현·테스트
- [x] 관련 문서, 전체 build/test와 실제 실행 파일 갱신

## 검증 방법

- `npm --prefix src/Modules/Starboard.Modules.Terminal/Presentation/Renderer run build`
- `tsc --noEmit --strict --target ES2022 --module ESNext --moduleResolution Bundler
  --lib ES2022,DOM --skipLibCheck src/index.ts src/style-imports.d.ts`
- `C:\Users\round1studio_14\.dotnet\dotnet.exe build src/Modules/Starboard.Modules.Terminal/Starboard.Modules.Terminal.csproj --configuration Debug`
- `C:\Users\round1studio_14\.dotnet\dotnet.exe test tests/Starboard.Modules.Terminal.Tests/Starboard.Modules.Terminal.Tests.csproj --configuration Debug`
- `C:\Users\round1studio_14\.dotnet\dotnet.exe test tests/Starboard.Modules.Preferences.Tests/Starboard.Modules.Preferences.Tests.csproj --configuration Debug`
- 관련 DesktopIntegration test와 가능한 solution build/test
- `git diff --check`, renderer source/dist build 재실행 후 clean diff 확인
- 실제 WebView2에서 tab overflow, keyboard, focus-visible, IME/clipboard와 약 8행
  geometry는 수동 확인 항목으로 구분

## 진행 기록

- 2026-09-03: session domain/coordinator는 이미 존재하며 presentation과 renderer가
  단일 active session에 묶여 있음을 확인했다. 기존 구조를 확장하기로 결정했다.
- 2026-09-03: tab strip은 WebView document 안에 두어 하나의 접근성 tree와
  keyboard routing 경계를 유지한다. 기존 약 5행 본문 116 DIP 위에 32 DIP strip을
  더한 148 DIP를 schema 3 기본값으로 정했다.
- 2026-09-03: session별 bounded output buffer와 targeted input/resize/clipboard,
  close/restart routing을 연결하고 protocol scope가 잘못된 serialization도 거부하게
  했다.
- 2026-09-03: renderer build, strict TypeScript 검사, 지정된 project build/test,
  DesktopIntegration test와 전체 solution build/test를 통과했다.
- 2026-09-03: 완료 후 재검토에서 TypeScript의 단일 optional `sessionId` envelope와
  tab 방향키 선택이 terminal focus를 요청하는 문제를 확인해 보완 범위에 추가했다.
- 2026-09-03: TypeScript envelope를 global/session discriminated union으로 분리하고
  허용된 host message type을 명시적으로 검증한다. tab 방향키는 새 active tab에
  focus를 유지하고 click과 application shortcut만 terminal focus를 요청한다.
- 2026-09-03: strict TypeScript, renderer deterministic rebuild, 지정된 Terminal/
  Preferences 명령, 전체 Debug build와 83개 전체 test가 통과했다.
- 2026-09-04: xterm surface의 노란 focus ring을 제거하고 button focus-visible은
  유지했다. 기본 높이는 약 3행을 더한 200 DIP로 올리고 schema 3 이하의 148 DIP만
  migration하도록 했다. renderer build, 집중 test, 전체 Debug build와 총 86개
  test가 통과했으며 self-contained Release를 바탕화면 배포 폴더에 교체했다.

## 미결정 사항

- 없음. schema 2 이하에서 정확히 116 DIP인 값은 이전 기본값으로 간주하고,
  그 외 사용자가 지정한 높이는 보존한다.

## 완료 요약

protocol v2는 global/session message scope를 분리하고 session message의 비어 있거나
잘못된 GUID를 거부하며 TypeScript 타입도 session message의 ID를 필수로 한다.
하나의 WebView2 document가 최대 8개의 xterm과 fit addon을 session ID별로 유지하며,
숨긴 tab도 output과 emulator state를 계속 보존한다. tablist는 가로 overflow,
상태별 시각/접근성 label, 새 탭·닫기·재시작 affordance와 요구 단축키를 제공한다.
방향키 tab 선택은 tablist focus를 유지한다. 후속 조정에서 terminal surface의 노란
focus ring을 제거하되 tab/button focus-visible은 유지했다. collapsed 기본 높이는
schema 4의 200 DIP로 올렸고 schema 3 이하의 148 DIP 기본값만 migration한다.

자동 검증은 renderer build와 strict TypeScript 검사, Terminal/Preferences/
DesktopIntegration 집중 test, 전체 solution build 및 후속 기준 총 86개 test를
통과했다. 실제 WebView2/monitor에서의 tab overflow, IME·clipboard·focus-visible,
노란 focus ring 제거 상태와 약 8행 geometry는 `docs/test-plan.md`의 수동 항목으로
남겼다.
