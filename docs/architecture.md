# Windows용 Starboard 아키텍처

## 상태

- 작성일: 2026-09-01
- 상태: v0.1 architecture lock
- 대상: Windows 11 x64, Windows 10 1809 이상 best-effort
- 제품 원칙: 작업표시줄 위에 계속 존재하지만 현재 작업을 방해하지 않는 실제
  persistent terminal

## 결정 요약

| 영역 | 결정 |
|---|---|
| application | .NET 10 WPF, 단일 process와 단일 executable |
| architecture | 세 기능 module을 가진 modular monolith |
| shell transport | Windows ConPTY 직접 interop |
| terminal renderer | standard WPF WebView2 + bundled xterm.js 6.0.0 |
| taskbar | 조회 전용 Shell/monitor API, appbar 등록 안 함 |
| focus | background show/move는 no-activate, 의도적 click은 activation 허용 |
| z-order | normal window band 유지, 명시적 호출 때만 foreground activation |
| tray | DesktopIntegration이 WinForms notification icon과 menu 수명 소유 |
| DPI | per-monitor v2, native geometry는 physical pixel로 계산 |
| virtual desktop | 공식 `IVirtualDesktopManager` 범위만 기본 제공 |
| settings | `%LOCALAPPDATA%/Starboard/settings.json`, versioned atomic write |
| startup | per-user `HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run` |
| privacy | telemetry와 runtime network 없음, command/output logging 없음 |
| portable release | central product version + Git build commit, self-contained win-x64 ZIP + SHA-256 |

## 모듈러 모놀리스

```text
Starboard.Windows
  ├─ Starboard.Modules.Terminal
  ├─ Starboard.Modules.DesktopIntegration
  ├─ Starboard.Modules.Preferences
  └─ Starboard.SharedKernel

각 기능 module ──> Starboard.SharedKernel ──> BCL
```

`Starboard.Windows`만 모든 module을 안다. module은 다른 기능 module을 직접
참조하지 않는다. host가 Preferences snapshot을 module별 option으로 변환하고
명시적 facade 호출과 좁은 event로 협력을 조정한다.

### Host

소유 범위:

- WPF `Application`, main/settings window shell
- single-instance guard
- module composition과 startup/shutdown 순서
- Preferences 값을 Terminal/DesktopIntegration option으로 매핑
- module failure를 사용자 상태로 연결

금지 범위:

- ConPTY, WebView2 bridge, taskbar, monitor, registry settings 구현
- 기능 module의 internal concrete type 참조
- service locator 또는 reflection module discovery

### Terminal module

소유 범위:

- shell 탐색과 launch option validation
- ConPTY 생성, input/output, resize와 process lifecycle
- xterm.js renderer asset, WebView2 host와 versioned bridge
- terminal surface와 renderer/shell failure state
- UTF-8 decoder와 출력 batching

외부에는 module entry point, session state, terminal option과 사용자 조작에 필요한
최소 event만 공개한다.

### DesktopIntegration module

소유 범위:

- system taskbar, monitor와 work area snapshot
- physical-pixel geometry와 window policy reducer
- WPF HWND style, position, z-order와 activation adapter
- display/settings/DPI/taskbar recreation message 감시
- fullscreen detection, global hotkey, startup와 virtual desktop adapter

ConPTY interop를 참조하지 않으며 terminal content를 알지 않는다.

### Preferences module

소유 범위:

- schema-versioned settings model과 validation
- default/partial/old/corrupt settings 복구
- atomic JSON persistence
- Dark, Light, One Dark와 Tokyo Night theme definition

OS startup registry 적용은 DesktopIntegration 책임이다. Preferences는 사용자가
선택한 boolean만 저장하고 host가 적용을 조정한다.

### 설정 적용과 복구 조정

tray의 `SettingsRequested`는 host가 소유한 단일 설정 window controller로 전달된다.
Preferences는 content와 validation을 소유하고, host window는 그 content를 담는 수명
경계만 제공한다. 이미 열린 창에 대한 tray 요청만 명시적으로 활성화하며 닫기에서는
terminal panel이나 이전 foreground application을 활성화하는 API를 호출하지 않는다.

Preferences가 유효성을 확인한 draft의 저장 callback은 host composition root에서 다음
순서로 처리한다.

1. Preferences snapshot을 Terminal과 DesktopIntegration의 public settings contract로
   변환한다.
2. Terminal appearance와 이후 생성할 tab의 기본 shell을 적용한다. 기존 tab/session을
   재생성하지 않는다.
3. DesktopIntegration 높이·opacity, global hotkey와 per-user startup 설정을 적용한다.
4. WPF host surface의 theme와 geometry 속성을 같은 snapshot으로 맞춘다.
5. 모든 live apply가 성공한 뒤 Preferences atomic store에 영속화한다.

DesktopIntegration의 opacity 적용은 composition root가 전달한 동기 callback을 통해
WPF `Window.Opacity`에 반영한다. 실패는 module의 기존 settings rollback 경로로 전파한다.
`AllowsTransparency=false`를 유지하며 `SetLayeredWindowAttributes`로 HWND style을
강제하지 않는다. WPF는 opaque HwndTarget에서 `WS_EX_LAYERED`를 제거하므로 이 조합은
실제 WPF integration test와 배포 실행에서 Win32 error 87을 발생시켰다(2026-09-09).
[WPF HwndTarget 구현](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/InterOp/HwndTarget.cs)과
[Win32 API 요구사항](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes)을
근거로 native opacity setter를 제거했다. 현재 속성 적용은 WPF surface에 한정되며,
opaque 창과 child-HWND WebView2 전체의 바탕화면 투과 효과를 보장하지 않는다.

Terminal이나 DesktopIntegration은 자체 operation의 실패와 local rollback 결과를
`EffectiveSettings`로 보고한다. 뒤 단계 또는 persistence가 실패하면 host는 마지막
persisted snapshot을 DesktopIntegration, Terminal 역순으로 다시 적용한다. host는
마지막 persisted snapshot과 두 module result에서 합성한 실제 effective snapshot을
별도로 유지한다. 둘이 다르면 성공으로 보고하지 않고 설정 창에 두 상태와 재시도
가능성을 표시한다. 편집기는 save callback 예외 시 draft와 창을 유지한다.

기본 shell 변경은 Terminal의 새-tab default만 바꾸므로 기존 tab의 PID, working
directory, environment와 restart shell capture는 유지된다. hotkey 교체와 startup
등록/복구는 DesktopIntegration 내부 adapter가 소유하며, startup은 앱 소유 HKCU Run
value 이외의 항목을 수정하지 않는다.

### SharedKernel

둘 이상의 module이 같은 의미로 쓰는 diagnostics contract처럼 작고 안정적인
cross-cutting contract만 둔다. geometry, settings DTO, terminal state나 Win32
constant는 해당 module에 남긴다.

### Boundary enforcement

`Starboard.ArchitectureTests`가 다음을 확인한다.

1. 기능 module assembly가 다른 기능 module을 참조하지 않는다.
2. SharedKernel이 host 또는 module을 참조하지 않는다.
3. production project가 test project를 참조하지 않는다.
4. module의 public type이 `Contracts` namespace 또는 승인된 module entry point다.
5. host에 `Starboard.Modules.*.Infrastructure` namespace 참조가 없다.

별도 architecture framework 없이 MSTest, reflection과 project metadata로 먼저
검사한다.

## Terminal renderer 결정

### 후보 비교

| 후보 | 장점 | 문제 | 결정 |
|---|---|---|---|
| ANSI/VT 직접 구현 | dependency 없음 | parser, reflow, IME, accessibility와 interactive app 호환을 새로 책임져야 함 | 제외 |
| .NET native terminal control | WPF와 직접 통합 가능 | v0.1 요구를 충족한다고 검증된 유지보수 후보를 확정하지 못함 | 보류 |
| WebView2 + xterm.js | VS Code 계열에서 검증된 terminal model, Unicode/IME, selection과 addon 생태계 | renderer process와 bridge lifecycle 필요 | 선택 |

선택 package는 구현 시점의 stable version으로 고정한다.

- `Microsoft.Web.WebView2` `1.0.4191.47`
- `@xterm/xterm` `6.0.0`
- `@xterm/addon-fit` `0.11.0`
- build-only `esbuild` `0.28.2`

`package-lock.json`과 review한 `dist`를 함께 관리한다. 일반 .NET build와 runtime은
Node.js나 network를 요구하지 않는다. npm은 renderer asset을 갱신할 때만 쓴다.

### WebView2 policy

- WPF standard WebView2 한 개만 생성한다.
- `%LOCALAPPDATA%/Starboard/WebView2`를 user data folder로 사용한다.
- application asset directory만 virtual host로 매핑한다.
- runtime HTTP/HTTPS navigation, popup과 download를 거부한다.
- Release에서는 DevTools, browser context menu, status bar와 accelerator를 끈다.
- host object를 광범위하게 노출하지 않고 JSON web message만 사용한다.
- renderer process failure를 관찰하고 session을 즉시 폐기하지 않은 채 surface를
  다시 연결한다.
- WebView2 Runtime이 없으면 WPF error surface와 offline installer 안내를 표시한다.

Windows 11에는 Evergreen Runtime이 일반적으로 포함되지만 application은 설치를
가정하지 않고 availability를 확인한다. Evergreen update는 앱을 재시작할 때 새
runtime을 사용하므로 장시간 실행 앱의 update 안내는 후속 배포 단계에서 다룬다.

### Renderer bridge

모든 message는 다음 envelope을 사용한다.

```json
{
  "version": 2,
  "type": "input|resize|copy|paste-request|select-session|close-session|restart-session",
  "sessionId": "32-character-guid",
  "payload": {}
}
```

- `ready`, `new-tab`, next/previous 선택과 renderer 자체 오류만 global message이며
  `sessionId`를 갖지 않는다. input/output, resize, clipboard, 선택, 닫기, restart,
  remove와 session 오류처럼 session을 대상으로 하는 message는 비어 있지 않은
  `N` 형식 GUID를 반드시 포함한다.
- 알 수 없는 version/type, global message의 session ID, session message의 누락되거나
  잘못된 ID는 무시하고 diagnostics에 metadata만 남긴다.
- input payload는 UTF-8로 encoding해 ConPTY input queue로 보낸다.
- output은 host가 session별 bounded buffer와 최대 batch 크기로 묶어 대상 xterm에
  보낸다. 느린 session의 backlog는 다른 session과 공유하지 않는다.
- host는 global `initialize`와 session별 `session-upsert`, `activate-session`,
  `output`, `paste`, `reset`, `remove-session`, `session-error`를 보내며 renderer
  source는 bundled local asset으로만 제공한다.
- resize는 양의 column/row와 상한을 검증한 뒤 해당 session의
  `ResizePseudoConsole`에만 전달한다.
- bridge log에는 payload text, command 또는 terminal output을 기록하지 않는다.
- oversized/malformed message는 session을 종료하지 않고 거부한다.

### 안전 확인과 새 출력 상태

살아 있는 tab의 `close-session` 요청은 coordinator가 발급한 request ID, session ID,
generation을 포함한 확인 요청으로 바꾼다. renderer가 같은 세대의 `confirmed` 응답을
돌려줄 때만 transport를 닫는다. 취소·Escape·dialog close, renderer reconnect, tab
제거·restart와 늦은 응답은 취소로 처리하므로 다른 tab 또는 새 session을 닫을 수 없다.

clipboard text에 CR 또는 LF가 있으면 host는 한 번 읽은 snapshot만 같은 확인 경계에
넣는다. 승인 후 clipboard를 다시 읽지 않고 기존 xterm paste path로 한 번 전달하며,
preview와 token은 log, settings, workspace 또는 package에 쓰지 않는다. 비활성 tab의
non-empty output은 session generation별 in-memory `new output` state로 표시한다. 이는
command completion 판단이나 activation 요청이 아니며, tab 선택·restart·remove에서 지운다.

### 명령 수명과 완료 알림 계약

명령 완료는 terminal byte stream의 양, 마지막 출력 이후 경과 시간, prompt 문자열, shell process
종료로 추측하지 않는다. xterm/renderer가 소비하는 output과 명령 수명 control signal은 별도 경계다.
VS Code가 문서화한 OSC 633/133의 `C`(pre-execution), `D;<exitcode>`(finished) 순서는 선행 사례지만,
Windows ConPTY에서 marker 위치에 보정 heuristic이 필요하고 command line을 운반하는 `E` sequence도
존재한다. Starboard는 command/output을 contract 밖에 유지하기 위해 renderer 출력에서 이 sequence를
해석하지 않고, 제품이 주입한 shell hook의 metadata-only control channel만 신뢰한다.

초기 notification-capable shell은 `pwsh.exe`와 `powershell.exe`다. PowerShell의
`PSConsoleHostReadLine` hook에서 실행 직전 start를 보내고 다음 `prompt` 진입에서 종료와 exit code를
보내는 adapter를 private named-pipe control channel에 연결한다. integration을 주입할 수 없거나 restricted language/profile
충돌로 완전한 start/finish 쌍을 보장하지 못하면 해당 generation은 미지원이다. `cmd.exe`와 custom
shell도 명시적인 양방향 hook이 확보되기 전에는 완료 알림을 만들지 않는다. 지원 범위를 넓히기 위해
prompt 모양이나 output 정지 fallback을 추가하지 않는다.

Terminal의 수명 tracker는 다음 metadata만 소비한다.

- host가 발급하고 재사용하지 않는 command execution ID
- opaque runtime session ID와 shell 교체마다 증가하는 generation
- `CommandStarted`, `CommandFinished(exitCode)`, `IntegrationLost` control signal

정상 순서는 generation별 `Ready → Executing → Ready`다. 동일 execution ID의 명시적인 finish만
`TerminalCommandCompletion`을 만들며 결과는 integration이 보고한 정수 exit code와 `0 == success`
판정이다. 계약은 모든 `int` 값을 손실 없이 보존한다. PowerShell cmdlet/pipeline처럼 native process
exit code가 없는 경우 adapter는 shell 성공 상태를 0/1로 정규화하고, 명시적 결과를 얻지 못하면 finish를
만들지 않는다. restart
이전 generation의 늦은 signal은 현재 tracker를 바꾸지 않고 거부한다. 현재 generation에서 finish 선행,
중복 start, execution ID 불일치 또는 control channel 손실이 발생하면 active command를 폐기하고
tracker를 `Unavailable`로 만든다. 그 세대에서는 누락을 허용하되 오탐을 만들지 않으며 새 generation만
새 tracker를 시작한다. shell/ConPTY 자체 종료는 terminal session 상태일 뿐 command finish 대체 신호가
아니다.

Preferences schema 7의 `commandCompletionNotificationsEnabled`는 기본 `false`인 opt-in이다. missing,
partial 및 schema 6 이하 JSON은 값이 없으면 계속 `false`로 normalize된다. 적용 결과의 transition을
composition root가 받아 DesktopIntegration 표시 정책을 조정한다. `TerminalModule.CommandCompleted`는
module 내부 tracker가 만든 metadata-only event만 공개하고 `TerminalModule.IsCurrentSession`은 coordinator의
lock 아래에서 해당 session ID/generation이 아직 등록된 같은 shell lifetime인지 확인한다. Terminal
completion event를 받을 때 host는 이 공개 경계로 현재 generation을 다시 확인한 뒤 동일 metadata를
`CommandCompletionNotificationRequest`로 변환한다. DesktopIntegration은 고정된 제품 문구와 성공/실패
상태만 표시하며 panel activation, foreground 전환이나 focus 이동을 요청하지 않는다.

host는 종료 시작 시 Terminal event 구독을 제거하고 notification coordinator를 먼저 stop한 다음 Terminal
session을 정리하고 마지막에 DesktopIntegration tray를 해제한다. 따라서 이미 제거·restart된 session,
새 generation으로 교체된 shell, 종료 뒤 도착한 callback은 새 탭이나 살아 있는 tray로 전달되지 않는다.
renderer 복구는 shell session generation을 바꾸거나 completion을 합성하지 않으므로 같은 공개 검증을
우회하지 않는다.

Terminal completion event와 DesktopIntegration request에는 session ID/generation, execution ID와 exit
result만 있으며 command, output, prompt, working directory, tab 이름 또는 사용자 제공 문자열이 없다.
설정 파일에는 opt-in boolean만 저장한다. diagnostics는 integration availability, signal kind, rejection
reason과 복구 가능 여부 같은 bounded metadata만 기록하고 ID, exit code를 포함한 상관 정보가 필요해도
command/output을 기록하지 않는다.

Explorer 경로 drop은 WebView2의 `postMessageWithAdditionalObjects`/`CoreWebView2File.Path`
경계를 사용한다. renderer는 `File` 객체만 host로 넘기고 `text()`, `arrayBuffer()`
또는 stream을 통해 내용을 읽지 않는다. host도 `Path`만 자료로 사용하며 존재 확인,
metadata 조회나 file open을 하지 않는다. drop 당시 renderer instance GUID, active session ID와
session generation을 함께 보내고 host/coordinator가 모두 현재값과 비교한다. navigation,
tab 선택·종료·restart 후의 느린 request나 confirmation은 입력을 만들지 않는다.

PowerShell 7/Windows PowerShell은 작은따옴표로 감싸고 경로 내 작은따옴표를
두 번 쓴다. `cmd.exe`는 큰따옴표로 감싸되 명령 실행 시 환경 변수나 delayed
expansion으로 재해석될 수 있는 `%`/`!`가 있으면 거부한다. custom shell,
상대·device 경로, 제어 문자와 지나치게 큰 입력도 거부한다. 인용 결과는
취소가 기본 focus인 읽기 전용 확인 화면에 보이며, 승인해도 Enter·CR·LF를
추가하지 않아 shell prompt를 자동 실행하지 않는다.

## ConPTY 설계

Windows 10 1809부터 제공되는 documented API만 쓴다.

1. host-to-ConPTY input과 ConPTY-to-host output pipe를 만든다.
2. `CreatePseudoConsole`에 initial character size와 pipe ends를 전달한다.
3. `InitializeProcThreadAttributeList`의 two-call pattern으로 buffer를 할당한다.
4. `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE` attribute에 HPCON을 연결한다.
5. `STARTUPINFOEX`, `EXTENDED_STARTUPINFO_PRESENT`와
   `CREATE_UNICODE_ENVIRONMENT`로 shell process를 생성한다.
6. input writer와 output reader를 별도 background worker로 실행한다.
7. xterm fit 결과가 바뀌면 검증 후 `ResizePseudoConsole`을 호출한다.

ConPTY pipe는 공식 문서상 synchronous I/O이므로 UI thread에서 읽거나 쓰지 않는다.
output decoder는 byte chunk 사이에 걸친 UTF-8 sequence를 보존한다.

### Shell resolution

기본 순서:

1. `pwsh.exe`
2. `powershell.exe`
3. `cmd.exe`

`PATH`와 명시적 표준 설치 위치를 확인하되 존재하지 않는 executable을 성공으로
간주하지 않는다. 기본 launch argument는 shell 종류별로 별도 정의한다. custom
shell은 executable path와 argument array를 분리해 저장하고 한 문자열을 다시
shell parsing하지 않는다.

### Multi-session workspace

Phase 1.3A부터 `TerminalSessionCoordinator`가 ordered tab registry와 실제 terminal
transport의 수명을 함께 조정한다.

- 각 tab은 host가 발급한 opaque `Guid` session ID, 생성 순번을 재사용하지 않는
  `PowerShell N` 이름과 `Starting`, `Running`, `Restarting`, `Exited`, `Failed`
  상태를 가진다.
- 첫 tab과 새 tab은 즉시 active가 된다. 직접 선택과 next/previous는 registry
  순서를 따르고 끝에서 순환한다. active tab close는 오른쪽 이웃을 우선하고 끝이면
  왼쪽 이웃을 선택하며 inactive tab close는 active ID를 바꾸지 않는다.
- 마지막 tab close는 닫힌 ConPTY 정리를 기다리기 전에 새 기본 tab과 별도 shell을
  생성·활성화한다. 동시에 유지할 수 있는 tab은 8개이며 초과 요청은 shell process
  생성 전에 거부한다.
- tab마다 `ITerminalSession` 한 개와 별도 ConPTY, pipe, shell process를 소유한다.
  restart는 tab ID와 이름을 보존하면서 해당 transport만 교체하고 다른 tab registry
  entry나 transport는 건드리지 않는다.
- output/exit callback은 session ID뿐 아니라 coordinator가 보관한 실제 session
  instance와 일치할 때만 전달한다. close/restart 뒤 도착한 이전 instance callback은
  새 session 상태를 변경하지 않는다.

Phase 1.3B부터 WPF view와 renderer는 protocol v2를 사용한다. WebView2 document는
하나만 유지하고 session ID를 key로 xterm과 fit addon을 하나씩 보관한다. 비활성
tabpanel은 숨기기만 하므로 output buffer, scrollback과 emulator state가 유지되며,
선택할 때 다시 fit한 결과만 해당 ConPTY에 전달한다. 가로 overflow tablist는 최대
8개 session, session별 loading/error 상태와 restart action을 제공한다.

이 경계는 세 session을 오가는 targeted input unit test, session ID별 output probe를
사용한 실제 ConPTY hidden GUI integration test와 bundled renderer 구조 test로
검증한다. 통합 test는 서로 다른 PID와 working directory를 가진 세 PowerShell을
만들고 첫 session의 exit/restart/close 뒤 나머지 session의 상태 및 output routing을
다시 확인한다. 실제 WebView2 화면에서 비활성 xterm scrollback을 눈으로 확인하는
항목은 자동 검증과 구분해 수동 test로 남긴다.

### Workspace persistence and restore

`restoreWorkspaceOnLaunch`는 Preferences schema 6에서 추가된 opt-in 값이며 기본값은 `false`다.
꺼진 설정은 남아 있는 workspace 파일보다 우선한다. 시작 시 기본 탭 하나를 만들고,
설정 저장이 성공한 뒤에는 기존 workspace 파일을 삭제한다. 켜진 경우에만 Terminal이
`%LOCALAPPDATA%/Starboard/workspace.json`을 읽고, 1~8개 탭의 구성 ID, 이름, 연속된
순서, 시작 폴더, 제한된 shell kind와 활성 구성 ID를 복원한다.

workspace 저장은 64 KiB 상한과 strict validation을 거친 뒤 같은 directory의 flush된
`.tmp` file을 `File.Replace`(최초 저장은 move)로 교체한다. 유효한 primary만 `.bak`으로
승격한다. primary가 손상되면 valid backup으로 복구하고, 미래 schema가 발견되면 primary와
backup을 보존한 채 그 실행의 restore와 autosave를 중단한다. 복원은 각 구성 ID를 새 runtime
session ID와 새 ConPTY process에 매핑하므로 PID, shell의 현재 state, history, command,
output, scrollback, clipboard, environment는 복원하지 않는다. 개별 shell 또는 directory
실패는 failed tab으로 격리해 이후 tab 복원을 계속한다.

### Saved terminal tabs

저장한 탭은 workspace restore와 다른 Terminal 소유 수명이다. `TerminalModule`이
`%LOCALAPPDATA%/Starboard/saved-tabs.json`의 store와 `TerminalSavedTabService`를 만들고,
renderer 입력을 소유하는 `TerminalView`에 전달한다. renderer가 준비되면 저장 목록을 한 번
읽고 workspace restore on/off와 무관하게 snapshot을 전송한다. workspace 옵션을 끄거나
workspace 파일을 삭제해도 saved-tabs 파일은 변경하지 않는다.

저장 항목은 불변 ID, 32 text-element 이하 이름, 존재하는 local absolute 시작 폴더와
`Automatic`/`Pwsh`/`PowerShell`/`Cmd` shell kind만 담는다. 최대 20개이며 64 KiB strict
schema JSON을 같은 directory의 flush된 `.tmp`에서 원자 교체한다. 손상·누락·미래 schema와
I/O timeout은 shell startup을 막지 않고, 미래 schema는 덮어쓰지 않는다. 이름이나 경로
원문은 diagnostic log에 기록하지 않는다.

실행은 `TerminalSessionCoordinator.AddSavedTabAsync`의 기존 8-tab gate와 세션 생성 경로를
사용한다. 선택한 정의마다 새 session ID와 ConPTY/shell PID를 만들고 해당 이름·폴더·셸만
적용하므로 기존 탭의 process, input, output buffer와 renderer scrollback을 교체하지 않는다.
동일 renderer request ID는 `TerminalView` 수명 동안 한 번만 launch에 전달한다. renderer
document generation이 바뀐 뒤 완료된 callback은 새 document에 과거 결과로 적용하지 않고
최신 저장 목록과 실행 탭 수만 다시 동기화한다. renderer 실패 중에도 저장 변경 자체가
성공했다면 재연결 snapshot에 반영된다.

종료는 renderer 입력과 view callback을 먼저 취소하고 saved-tab service의 pending launch와
I/O를 제한 시간 안에서 중단한 뒤 workspace persistence와 terminal sessions를 정리한다.
따라서 늦은 저장/launch callback은 renderer에 새 탭을 추가하지 않으며 저장 I/O가 앱 종료를
무한히 지연하지 않는다.

기본 collapsed 높이는 schema 4의 200 DIP다. 32 DIP tab strip 아래에 13px font와
1.35 line-height 기준 약 8행의 terminal body를 표시한다. schema 3 이하에서 이전
기본값인 148 DIP만 200 DIP로 migration하고 다른 설정 높이는 사용자 지정으로
보존한다. terminal focus는 caret으로 나타내며 xterm surface 전체를 두르는 별도
focus border는 그리지 않는다. 탭과 버튼의 keyboard focus-visible 표시는 유지한다.

### Shutdown order

`ClosePseudoConsole`은 Windows 11 24H2 이전 환경에서 client/output 상태에 따라
오래 기다릴 수 있다. UI thread에서 호출하지 않고 다음 bounded 순서를 사용한다.

1. 새 renderer input을 막고 input writer를 완료한다.
2. shell에 정상 종료 기회를 짧게 제공한다.
3. output reader가 EOF까지 drain할 시간을 제한해 기다린다.
4. 남은 child process를 종료한다.
5. pipe end와 process/thread handle을 닫는다.
6. 별도 worker에서 HPCON을 닫고 timeout 이후 shutdown을 계속한다.
7. renderer를 dispose한다.

SafeHandle이 handle 소유권을 표현하며 같은 native handle을 두 객체가 소유하지
않는다.

단일 tab close/restart 정리는 6초를 상한으로 삼고, 앱 종료에서는 모든 session
정리를 병렬로 시작한 뒤 workspace 전체 8초 deadline을 적용한다. deadline을 넘긴
session은 late completion의 예외를 계속 관찰하되 다른 tab 정리나 앱 종료를 막지
않는다.

Windows ConPTY integration은 별도 `Starboard.ConPtyTestHost` GUI-host executable에서
실행한다. 이 host는 제품 UI/WebView2를 띄우지 않으며 실제 ConPTY와 coordinator 수명만
검증한다. SAFE-01 뒤 발생한 tab test 실패는 제품 deadlock이 아니라, 재시작된 session을
확인 없이 직접 `CloseAsync`로 닫던 오래된 test 계약 때문이었다. host는 이제 같은
generation의 confirmation을 적용하고 tab 제거 event와 shell PID 종료를 각각 bounded
cancellation 안에서 기다린다. 결과 파일에는 host path/PID, session ID·generation,
tab removal 및 process-exit boolean만 기록하고 command/output은 남기지 않는다.

## Taskbar와 monitor

### Snapshot

`TaskbarSnapshot`은 다음 physical-pixel 값을 가진 immutable record다.

- taskbar edge와 bounding rectangle
- taskbar monitor bounds와 work area
- auto-hide configured 여부
- taskbar visible/concealed/unknown presence
- monitor DPI
- snapshot source와 confidence

system taskbar는 `SHAppBarMessage(ABM_GETTASKBARPOS)`를 정본으로 찾고
`MonitorFromRect`/`GetMonitorInfo`로 monitor와 work area를 얻는다.
`ABM_GETSTATE`는 auto-hide 설정 여부만 제공하며 현재 animation visibility를 직접
보장하지 않으므로 별도 presence 판단과 reconciliation이 필요하다.

v0.1은 system taskbar를 우선한다. `ABM_GETTASKBARPOS`가 secondary taskbar를 모두
열거하지 못하므로 임의 class-name 탐색은 optional adapter 뒤에 격리하고 검증되지
않으면 preferred monitor 기능을 system taskbar monitor로 제한한다.

### Appbar를 등록하지 않는 이유

`ABM_NEW`로 Starboard를 appbar로 등록하면 work area를 예약해 일반 application
layout을 바꿀 수 있다. 제품 요구사항은 overlay surface이므로 taskbar 정보만
조회하고 appbar로 등록하지 않는다.

### Event와 reconciliation

`DesktopIntegrationModule`이 display/foreground observer, tray icon, hotkey와 timer의
수명을 함께 소유한다. 다음 signal에서 이전 monitor handle이나 rectangle을 재사용하지
않고 snapshot 전체를 다시 조회한다.

- `TaskbarCreated` registered message
- `WM_DISPLAYCHANGE`
- `WM_SETTINGCHANGE`
- `WM_DPICHANGED`
- `SystemEvents.DisplaySettingsChanged` fallback
- foreground `EVENT_SYSTEM_FOREGROUND`

event 누락, auto-hide transition과 Explorer transition을 보완하기 위해 1초
reconciliation을 둔다. `TaskbarCreated`에서는 기존 tray icon을 해제한 뒤 하나만 다시
만들고 같은 정책 reconcile을 실행한다. observer 등록이 실패해도 timer fallback과
등록 가능한 shortcut은 유지하며, 종료 시 callback 구독과 native hook을 해제한다.

## Geometry와 DPI

native boundary는 physical pixel을 사용한다. WPF layout에 넘길 때만 해당 window
DPI로 DIP를 계산한다.

collapsed bottom-taskbar 기본값:

```text
left   = workArea.left
right  = workArea.right
inner  = clamp(taskbarRect.top, workArea.top, workArea.bottom)
height = clamp(round(settings.heightDip * dpiY / 96), 1, inner - workArea.top)
bottom = inner
top    = bottom - height
```

축소 하단 패널은 작업표시줄의 안쪽 경계에 외부 간격 0으로 붙인다. 입력 줄 보호는
renderer의 같은 배경색 6 DIP 하단 padding이 담당하며 fit/ConPTY resize는 이 padding을
제외한 실제 본문 높이로 계산한다. 작업표시줄 bounds가 없으면 기존 work-area edge
fallback을 사용한다.

top/left/right taskbar도 edge별로 work area 안쪽에 붙인다. 사용자가 지정한 높이는
taskbar에 평행한 panel의 두께 의미로 사용한다.

expanded frame은 현재 panel monitor의 work area 전체다. 축소 시 마지막 valid
collapsed frame이 현재 monitor들과 교차하면 복원하고, 아니면 최신 snapshot으로
재계산한다.

manifest에서 per-monitor v2를 선언한다. host는 전체 `HWND`, `wParam`, `lParam`을
module에 동기 전달한다. `WM_DPICHANGED`의 새 DPI와 suggested rectangle은 window
procedure가 반환하기 전에 lifetime-safe 값으로 복사하고, WPF가 자체 DPI message를
처리한 다음 dispatcher에서 최신 taskbar/monitor snapshot으로 한 번 더 배치한다.
cached system DPI는 적용하지 않는다.

## Focus와 activation

핵심 원칙은 창을 영구적으로 nonactivatable하게 만드는 것이 아니다.

- WPF initial show는 `ShowActivated=false`다.
- HWND에 `WS_EX_TOOLWINDOW`를 적용해 taskbar와 Alt+Tab 목록에서 제외한다.
- `WS_EX_NOACTIVATE`는 permanent style로 적용하지 않는다.
- background show/move/resize는 `SetWindowPos`의 `SWP_NOACTIVATE`를 사용한다.
- 사용자가 WebView2 terminal을 클릭하면 일반 activation과 IME focus를 허용한다.
- geometry watcher는 `Activate`, `Focus`, `SetForegroundWindow`를 호출하지 않는다.

focus regression test는 reposition 전후 `GetForegroundWindow` 값을 비교한다.

## Z-order와 fullscreen

panel은 normal/maximized desktop에서도 topmost group에 올리지 않는다.
`ConfigureToolWindow`에서 기존 topmost 상태를 한 번 제거하고, background
reconciliation은 `SWP_NOACTIVATE | SWP_NOZORDER`로 위치와 크기만 갱신한다. 따라서
다른 일반 앱을 활성화하면 그 앱이 Starboard를 자연스럽게 덮는다.

notification icon 왼쪽 클릭, tray의 표시 요청이나 호출 단축키처럼 사용자가
명시적으로 panel을 요청한 경우에만 module이 engagement를 갱신하고 host 표시 뒤
normal window band의 앞으로 활성화를 요청한다. foreground 제한이 있는 경우 현재
foreground thread의 input queue를 활성화 호출 동안만 연결하고 즉시 해제한다.
이후의 observer/timer reconciliation은 activation이나 z-order를 변경하지 않는다.
영구 사용자 숨김과 환경에 의한 임시 억제를 분리하며, WPF host는 module의 최종
presentation 요청만 `Show`/`Hide`로 반영한다.

foreground window가 panel monitor를 사실상 덮는 fullscreen이면 panel을 임시
conceal한다. 이 상태에서는 명시적 호출도 fullscreen 위로 강제 활성화하지 않는다.
fullscreen 종료 뒤에는 사용자 숨김 의도를 다시 확인한 후 geometry만 복구하며
topmost로 승격하거나 foreground를 되찾지 않는다.

fullscreen 판단은 foreground top-level window rectangle과 monitor bounds,
visibility, cloaking과 shell window 제외 조건을 조합한다. 단순 maximized window는
work area만 차지하므로 fullscreen으로 분류하지 않는다.

## Auto-hide state

상태는 UI event에 흩어진 boolean 대신 reducer로 계산한다.

- `PanelMode`: Collapsed / Expanded
- `Engagement`: Idle / Active
- `TaskbarPresence`: Visible / Concealed / Unknown
- `Fullscreen`: Normal / FullscreenOnPanelMonitor
- `Tracking`: Tracked / Fallback

사용자가 입력 중이거나 expanded이면 taskbar conceal 중에도 마지막 안전 frame을
유지한다. idle/collapsed이면 taskbar visibility를 따라 panel을 conceal한다.
`Unknown`에서는 창을 화면 밖으로 이동하지 않고 마지막 안전 frame을 사용한다.

## Global hotkey

`Ctrl+Alt+E`는 확장/축소, `Ctrl+Alt+S`는 호출/숨김에 사용한다. 호출 단축키는
숨김 또는 비활성 panel을 표시·활성화하고, 이미 활성 상태면 숨긴다. 둘 다
`RegisterHotKey`에 `MOD_NOREPEAT`를 사용한다. 등록 충돌은 예상 가능한 오류로
처리해 앱은 계속 실행하고 진단 로그에 충돌 상태를 기록한다. 종료 또는 shortcut
변경 시 반드시 `UnregisterHotKey`한다.

## Virtual desktop

공식 `IVirtualDesktopManager`는 다음 범위만 제공한다.

- window가 현재 desktop에 있는지 확인
- window가 속한 desktop ID 조회
- window를 특정 desktop으로 이동

모든 desktop pinning과 desktop switch event는 공식 public contract에 없다.
v0.1 기본 구현은 공식 API availability와 current-desktop 상태만 보고하며 pinning을
지원한다고 가장하지 않는다. fallback은 no-op + capability result다. undocumented
COM adapter는 기본 범위에 포함하지 않는다.

## Settings와 theme

저장 위치:

```text
%LOCALAPPDATA%/Starboard/
  settings.json
  settings.json.bak
  Logs/
  WebView2/
```

`schemaVersion`을 포함하고 missing/partial/old document를 defaults와 merge한다.
write는 같은 directory의 temp file을 flush한 뒤 atomic replace한다. corrupt file은
사용자 command를 포함하지 않는 diagnostics를 남기고 backup 후 defaults로
복구한다.

### Visual system

Hallmark 적용값은 desktop application에 맞게 다음처럼 제한한다.

- genre: atmospheric
- application-shell 구조: Workbench
- theme anchor: Terminal
- tone: technical, austere
- enrichment: 없음, 실제 terminal content가 중심
- motion: geometry·상태 transition 없음, terminal caret blink만 유지

marketing page용 hero/nav/footer 규칙은 application shell에 적용하지 않는다.
대신 4pt spacing, named color/font token, 뚜렷한 focus ring과 interactive control의
default/hover/focus/active/disabled/loading/error/success 상태를 사용한다.

WebView2 transparent composition 위험 때문에 glassmorphism과 blur를 쓰지 않는다.
첫 surface는 불투명에 가까운 dark canvas, 1px rule, system monospace와 한 개의
차분한 cyan accent로 구성한다. WPF resource와 xterm CSS는 같은 theme model에서
생성한다.

## Diagnostics와 privacy

- rolling local log만 사용하며 기본 retention을 제한한다.
- subsystem, operation, native error code, recoverability만 기록한다.
- command, terminal output, clipboard, environment value와 full custom arguments는
  기록하지 않는다.
- 닫기 확인 correlation token, 여러 줄 paste/path-drop preview와 새 출력 badge도 disk 또는 log에
  기록하지 않으며 현재 session generation이 바뀌면 폐기한다.
- opt-in workspace JSON은 탭 이름·순서·시작 폴더처럼 사용자가 선택한 구성만 담으며,
  command, output, clipboard, environment, runtime session ID와 PID는 담지 않는다.
- saved-tabs JSON은 저장 ID·이름·시작 폴더·shell kind만 담으며 workspace opt-in과 독립적이다.
  저장 목록과 경로 원문은 log 또는 portable package에 넣지 않는다.
- analytics, crash upload, remote config와 runtime asset fetch를 사용하지 않는다.

## Portable release와 build metadata

`Directory.Build.props`의 `VersionPrefix`가 제품 버전의 단일 정본이다. 일반 local
build는 commit을 `unknown`으로 표시할 수 있고, package 흐름은 확인한 전체 Git HEAD를
`StarboardBuildCommit`으로 전달한다. host assembly의 `ProductVersion`/`BuildCommit`,
설정 화면과 `release-metadata.json`은 이 두 값을 함께 사용한다.

`scripts/package-portable.ps1`은 Git이 보고한 root와 script의 root가 일치하는지 먼저
검사한다. 출력은 ignored `out/portable/<version>` 아래에 두고 재실행 때는 그 버전의
`staging`만 정리한다. staging이 reparse point이거나 계산한 경로가 root를 벗어나면
중단한다. publish는 self-contained `win-x64`, multi-file이고 WebView2 Runtime 자체는
포함하지 않는다. committed `Renderer` asset, 제품 MIT `LICENSE`, README, third-party
notice/license와 release metadata를 포함한 뒤 settings/workspace/saved-tabs JSON과 그
backup·temporary 파일, logs/WebView2 user data, dump, PDB와 개발 PC 절대 경로가 없는지
검사한다.

앱 executable과 WPF 창은 `src/Starboard.Windows/Assets/Starboard.ico`를 application/resource로
사용한다. notification icon은 DesktopIntegration assembly의
`Starboard.Modules.DesktopIntegration.Assets.Starboard.ico` embedded resource를 독립적으로
소유한다. 두 파일은 검증된 동일 ICO이며, portable package에는 확인 가능한
`Assets/Starboard.ico` 사본도 넣고 package 검사가 이를 요구한다. 따라서 실행 중 tray는
사용자별 경로나 network가 아니라 module 내부 resource를 계속 사용하고, archive의 asset은
검증·검사와 배포물 식별에 쓴다.

package 명령은 clean machine에서도 dependency와 vulnerability metadata를 확인하는 Release
restore를 먼저 수행한다. 따라서 build 시점에는 NuGet package source와 audit source에 대한
network 또는 동등한 조직 mirror가 필요하다. source 접근 실패를 package 산출물 오류로
오인하거나 audit을 끄지 않고, source 접근을 복구한 뒤 동일 명령을 다시 실행한다. 이 build
전제 조건은 생성된 portable package의 offline runtime 정책과 별개다.

ZIP entry는 ordinal 경로 순서와 source commit 시각을 사용한다. SHA-256 계산은
`Get-FileHash` availability를 먼저 확인하고, cmdlet이 없는 PowerShell host에서는 .NET
`SHA256` API와 read-only file stream을 사용한다. 두 disposable object는 `finally`에서
결정적으로 해제하며 외부 hash 도구나 network dependency를 추가하지 않는다. 같은 계산
경로로 checksum 파일을 만든 뒤 다시 계산해 일치 여부를 확인하고 별도 staging에 압축
해제한다. 추출본은 전용 smoke
인자로 시작하여 assembly와 package metadata, executable/local renderer 및 기본 shell
경로를 검사하고 즉시 종료한다. 이 mode에서는 Preferences load와 Desktop startup
적용을 시작하지 않으므로 기존 사용자 설정과 HKCU 자동 실행 경로를 변경하지 않는다.
WebView2 Runtime 실제 초기화와 terminal UI interaction은 별도 manual release matrix다.
- renderer content security policy는 local asset과 필요한 inline bootstrapping만
  허용하도록 최소화한다.

2026-09-10 release recovery 검증은 실행 중인 사용자 배포본을 보호하기 위해 이전·현재
복사본의 single-instance mutex 문자열 한 곳만 같은 길이의 test 전용 이름으로 바꾼
계측본을 사용했다. 실제 WebView2 renderer process만 종료했을 때 host와 PowerShell
process가 유지되고 `RenderProcessExited`가 기록됐으며, PowerShell process만 종료했을
때도 host가 유지되고 `ShellExit`가 기록됐다. 이는 process 수명 격리 근거지만 WPF 오류
surface 표시, renderer reconnect button과 다른 tab 화면 상태의 수동 통과 근거는 아니다.

Runtime 누락 child override는 renderer/shell 미생성까지만 관찰됐고 local 오류 surface와
installer 안내를 일관되게 확인하지 못했다. 현재 구현의 사용자 message는 Runtime 시작
실패와 재시도를 알리고, 설치 절차는 README에만 있다. system Runtime 제거와 실제
network-disabled 실행은 기존 사용자 WebView2/session 보호를 위해 수행하지 않았다.
따라서 local asset/CSP/package smoke가 통과했더라도 실제 offline WebView2와 Runtime 누락
fallback은 manual matrix에서 계속 부분 또는 blocked 상태로 관리한다.

## 현재 탭 출력 검색

renderer는 고정된 `@xterm/addon-search` 0.16.0을 session별 xterm instance에만 load한다.
따라서 검색 대상은 활성 탭의 현재 memory scrollback이며, 다른 tab buffer, disk/log,
host protocol 또는 renderer process 재시작 전 buffer를 읽거나 복원하지 않는다. 검색
overlay는 renderer document의 상태이고 `Ctrl+F`, Enter/Shift+Enter, 이전/다음 button과
Escape를 renderer에서 처리한다. input과 탐색 key는 shell input message를 만들지 않으며,
닫을 때 highlight를 지우고 동일 terminal로 focus를 돌린다. tab 전환·제거·reset은 overlay와
decorations를 정리한다. 이 흐름은 PID, session, output, scrollback transport를 변경하지
않는다.

overlay는 terminal pane 위의 compact absolute surface이며 200 DIP collapsed panel에서 본문을
resize하거나 자동 확장하지 않는다. theme appearance의 canvas/accent/selection 색을 search
decorations에 사용하고, 예상 밖의 색 형식에는 안정된 cyan fallback을 사용한다.

## 터미널 출력 URL 열기

renderer는 xterm.js와 호환되는 공식 `@xterm/addon-web-links` 0.12.0을 session별로 load한다.
plain text 주소와 OSC 8 hyperlink 모두 HTTP/HTTPS만 후보로 두며, addon의 기본 `window.open`은
사용하지 않는다. custom handler는 modifier 없는 click과 drag를 가로채지 않고 왼쪽 Ctrl+click일
때만 active session ID·shell generation·renderer instance ID와 후보 주소를 host에 전달한다.
renderer의 CSP `connect-src 'none'`, host의 navigation/new-window/download 차단은 그대로 유지하므로
hover, detection과 confirmation 과정에서 대상 서버로 요청하지 않는다.

host protocol은 2,048자 이하의 well-formed absolute URI, HTTP/HTTPS scheme, 비어 있지 않은 host와
빈 user-info를 다시 검사한다. file, command, JavaScript와 custom scheme은 renderer message 단계에서
거부한다. host가 정규화해 실제 실행할 문자열을 읽기 전용 confirmation dialog에 다시 보내며,
사용자가 확인한 immutable target만 `UseShellExecute`로 Windows 기본 브라우저에 전달한다. tab 전환,
session restart/remove, renderer reconnect나 세대 불일치 뒤의 응답은 폐기한다. 외부 실행은 UI thread
밖에서 수행하고 실패는 shell/session을 변경하지 않는 local feedback으로 격리한다. URL 원문은
settings, workspace, saved-tab 또는 diagnostic log에 기록하지 않는다.

portable update는 파일을 제자리 교체하거나 시작 프로그램 경로를 자동 이동하지 않는다.
서로 다른 build commit의 격리 폴더에서 이전→현재→이전 metadata smoke와 absent user state
불변은 확인했지만, 설정이 존재하는 schema migration/downgrade와 enabled HKCU Run 경로는
검증하지 않았다. 사용자는 README 절차대로 새 폴더에서 자동 실행을 껐다 켜고 rollback
때 이전 폴더에서 반복해야 한다.

## 알려진 위험과 fallback

| 위험 | 기본 대응 | fallback |
|---|---|---|
| WebView2 Runtime 없음 | startup initialization error surface와 재시도 | README 설치 절차; in-app offline installer 안내는 미구현·수동 검증 필요 |
| standard WebView2 alpha 제약 | opaque/tinted background | composition control은 별도 검증 후만 고려 |
| ConPTY shutdown deadlock | 별도 worker와 bounded drain | child kill, pipe close 후 app shutdown 계속 |
| taskbar auto-hide event 누락 | event + reconciliation | last safe frame |
| secondary taskbar 공식 열거 부재 | system taskbar 정본 | optional isolated adapter |
| fullscreen 오탐 | work area와 monitor bounds 구분 | 기본 normal z-order 유지, 필요 시 conceal 정책 off |
| virtual desktop pin 부재 | capability를 명시 | no-op fallback |
| IME/WebView shortcut 충돌 | composition-aware input, selection-aware copy | shortcut remap |

## 근거

- [원본 Starboard 분석](architecture-reference.md)
- [CreatePseudoConsole](https://learn.microsoft.com/en-us/windows/console/createpseudoconsole)
- [Creating a Pseudoconsole session](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session)
- [ResizePseudoConsole](https://learn.microsoft.com/en-us/windows/console/resizepseudoconsole)
- [ClosePseudoConsole](https://learn.microsoft.com/en-us/windows/console/closepseudoconsole)
- [SHAppBarMessage](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shappbarmessage)
- [ABM_GETTASKBARPOS](https://learn.microsoft.com/en-us/windows/win32/shell/abm-gettaskbarpos)
- [ABM_GETSTATE](https://learn.microsoft.com/en-us/windows/win32/shell/abm-getstate)
- [GetMonitorInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getmonitorinfoa)
- [Taskbar creation notification](https://learn.microsoft.com/en-us/windows/win32/shell/taskbar#taskbar-creation-notification)
- [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)
- [Extended Window Styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)
- [WM_DPICHANGED](https://learn.microsoft.com/en-us/windows/win32/hidpi/wm-dpichanged)
- [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [IVirtualDesktopManager](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager)
- [WebView2 distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- [WebView2 development practices](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/developer-guide)
- [VS Code terminal shell integration protocol](https://github.com/microsoft/vscode-docs/blob/main/docs/terminal/shell-integration.md)
- [PowerShell PSConsoleHostReadLine](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_psconsolehostreadline)
- [PowerShell prompt function](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_prompts)
- [xterm.js 6.0.0](https://github.com/xtermjs/xterm.js/releases/tag/6.0.0)
- [xterm.js search addon 0.16.0](https://www.npmjs.com/package/@xterm/addon-search/v/0.16.0)
- [xterm.js web-links addon 0.12.0](https://www.npmjs.com/package/@xterm/addon-web-links/v/0.12.0)
