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

- protocol v2, session별 xterm registry, tab UI와 schema 3 migration 구현 완료
- renderer/Terminal/Preferences/DesktopIntegration 및 전체 solution 자동 검증 완료
- 실제 WebView2의 overflow, IME, focus와 148 DIP 본문 행 수는 수동 확인 대기

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
- 필요한 `src/Starboard.Windows/Composition/**` 호출부
- Terminal/Preferences의 집중 test와 관련 documentation

## 아키텍처 결정

- WebView2는 기존처럼 하나만 소유한다. renderer 내부 map이 session id를 key로
  독립 `Terminal`과 `FitAddon`을 소유하며, 비활성 terminal DOM도 숨기기만 해
  output buffer, scrollback과 emulator state를 보존한다.
- renderer 전역 메시지(`ready`, renderer 자체 오류)는 session id가 없고,
  session을 대상으로 하는 생성/선택/output/input/resize/clipboard/reset/remove
  메시지는 session id가 필수다.
- C# view는 coordinator snapshot을 tab UI state로 투영하며 shell 수명은 계속
  coordinator가 소유한다. module 간 새 참조나 event bus를 추가하지 않는다.
- output backlog는 session별 bounded buffer로 분리해 느린 한 session이 다른
  session output이나 routing을 오염시키지 않게 한다.
- collapsed 높이 migration은 schema version과 기존 기본값을 함께 판별한다.
  명시적으로 사용자 지정한 높이는 수치가 이전 기본값과 같지 않은 한 보존한다.
- 새 기본 높이는 기존 116 DIP terminal 본문에 32 DIP tab chrome을 더한 148 DIP로
  정의한다. schema 2 이하의 116 DIP만 새 기본값으로 migration하고 다른 높이는
  사용자 지정으로 간주한다.

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

## 검증 방법

- `npm --prefix src/Modules/Starboard.Modules.Terminal/Presentation/Renderer run build`
- `C:\Users\round1studio_14\.dotnet\dotnet.exe build src/Modules/Starboard.Modules.Terminal/Starboard.Modules.Terminal.csproj --configuration Debug`
- `C:\Users\round1studio_14\.dotnet\dotnet.exe test tests/Starboard.Modules.Terminal.Tests/Starboard.Modules.Terminal.Tests.csproj --configuration Debug`
- `C:\Users\round1studio_14\.dotnet\dotnet.exe test tests/Starboard.Modules.Preferences.Tests/Starboard.Modules.Preferences.Tests.csproj --configuration Debug`
- 관련 DesktopIntegration test와 가능한 solution build/test
- `git diff --check`, renderer source/dist build 재실행 후 clean diff 확인
- 실제 WebView2에서 tab overflow, keyboard, focus-visible, IME/clipboard와 약 5행
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

## 미결정 사항

- 없음. schema 2 이하에서 정확히 116 DIP인 값은 이전 기본값으로 간주하고,
  그 외 사용자가 지정한 높이는 보존한다.

## 완료 요약

protocol v2는 global/session message scope를 분리하고 session message의 비어 있거나
잘못된 GUID를 거부한다. 하나의 WebView2 document가 최대 8개의 xterm과 fit addon을
session ID별로 유지하며, 숨긴 tab도 output과 emulator state를 계속 보존한다.
tablist는 가로 overflow, 상태별 시각/접근성 label, 새 탭·닫기·재시작 affordance와
요구 단축키를 제공한다. collapsed 기본 높이는 schema 3의 148 DIP로 올렸고 이전
기본값만 migration한다.

자동 검증은 renderer build와 strict TypeScript 검사, Terminal/Preferences/
DesktopIntegration 집중 test, 전체 solution build 및 총 83개 test를 통과했다.
실제 WebView2/monitor에서의 tab overflow, IME·clipboard·focus-visible과 약 5행
geometry는 `docs/test-plan.md`의 수동 항목으로 남겼다.
