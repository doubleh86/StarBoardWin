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

기본 collapsed 높이는 schema 3의 148 DIP다. 32 DIP tab strip과 기존 약 5행
terminal body 116 DIP를 합친 값이며, schema 2 이하의 이전 기본값 116 DIP만
migration한다. 다른 설정 높이는 사용자 지정으로 보존한다.

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

다음 signal에서 snapshot 전체를 다시 조회한다.

- `TaskbarCreated` registered message
- `WM_DISPLAYCHANGE`
- `WM_SETTINGCHANGE`
- `WM_DPICHANGED`
- `SystemEvents.DisplaySettingsChanged` fallback

event 누락과 Explorer transition을 보완하기 위해 1초 reconciliation을 둔다.
auto-hide transition 중에만 짧은 fast sampling을 쓰고 안정 상태에서 중단한다.

## Geometry와 DPI

native boundary는 physical pixel을 사용한다. WPF layout에 넘길 때만 해당 window
DPI로 DIP를 계산한다.

collapsed bottom-taskbar 기본값:

```text
left   = workArea.left
right  = workArea.right
bottom = taskbarRect.top
height = clamp(settings.heightDip * dpi / 96, minHeightPx, workArea.height)
top    = bottom - height
```

top/left/right taskbar도 edge별로 work area 안쪽에 붙인다. 사용자가 지정한 높이는
taskbar에 평행한 panel의 두께 의미로 사용한다.

expanded frame은 현재 panel monitor의 work area 전체다. 축소 시 마지막 valid
collapsed frame이 현재 monitor들과 교차하면 복원하고, 아니면 최신 snapshot으로
재계산한다.

manifest에서 per-monitor v2를 선언한다. `WM_DPICHANGED`에서는 `wParam`의 새 DPI와
`lParam` suggested rectangle을 사용하고 cached system DPI를 적용하지 않는다.

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

notification icon 왼쪽 클릭이나 호출 단축키처럼 사용자가 명시적으로 panel을
요청한 경우에만 host가 panel을 `Show`하고 normal window band의 앞으로
`Activate`한다. foreground 제한이 있는 경우 현재 foreground thread의 input
queue를 활성화 호출 동안만 연결하고 즉시 해제한다. 이후의 1초 geometry
reconciliation은 해당 z-order를 다시 끌어올리지 않는다. 숨김 상태는
DesktopIntegration state에 포함해 timer가 `SWP_SHOWWINDOW`로 panel을 임의
복원하지 못하게 한다.

foreground window가 panel monitor를 사실상 덮는 fullscreen이면 향후 정책에서
panel을 conceal할 수 있다. fullscreen 종료 뒤에는 geometry만 복구하며 topmost로
승격하지 않는다.

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
- analytics, crash upload, remote config와 runtime asset fetch를 사용하지 않는다.
- renderer content security policy는 local asset과 필요한 inline bootstrapping만
  허용하도록 최소화한다.

## 알려진 위험과 fallback

| 위험 | 기본 대응 | fallback |
|---|---|---|
| WebView2 Runtime 없음 | startup availability check | WPF error surface와 offline installer 안내 |
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
- [xterm.js 6.0.0](https://github.com/xtermjs/xterm.js/releases/tag/6.0.0)
