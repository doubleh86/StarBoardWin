
# Windows용 Starboard v0.1 구현 계획

- 작성일: 2026-09-01
- 상태: Phase 1.2 전역 호출 단축키·트레이 포커스 복구 구현 및 검증 완료
- 기준 문서: `AGENTS.md`, `docs/development-workflow.md`, `docs/code-style.md`
- 대상: Windows 11 x64 우선, Windows 10 1809 이상 best-effort
- 예상 작업량: 핵심 vertical slice 9~14시간, v0.1 완료 기준 총 21~33시간

## 목표

macOS Starboard를 직접 포팅하지 않고 다음 제품 경험을 Windows에 맞게 구현한다.

> Windows 작업표시줄 바로 위에 늘 존재하지만 현재 작업을 방해하지 않는,
> 실제 persistent shell terminal.

완료 시 사용자는 별도 코딩이나 설정 작업 없이 다음 동작을 사용할 수 있어야 한다.

- 앱 실행 시 현재 foreground application의 focus를 빼앗지 않는다.
- taskbar 위에 얇은 terminal panel이 표시된다.
- panel을 클릭하면 정상적으로 입력할 수 있다.
- `cd`, environment, history와 long-running process가 유지되는 실제 shell이 동작한다.
- `Ctrl+Alt+E`로 현재 monitor의 work area까지 확장하고 다시 축소할 수 있다.
- `Ctrl+Alt+S`로 다른 앱 위에 가려졌거나 숨겨진 panel을 즉시 호출하고,
  이미 활성화된 panel은 다시 숨길 수 있다.
- taskbar 이동, auto-hide, DPI, monitor 변경과 Explorer 재시작에 복구한다.
- theme, shell, 높이, font, opacity, startup과 monitor 정책을 설정할 수 있다.
- fullscreen application과 virtual desktop에서는 강제 노출보다 비방해를 우선한다.

## 참고 문서와 조사 근거

### 원본 Starboard

- [원본 저장소](https://github.com/palamim/starboard)
- [원본 README](https://github.com/palamim/starboard/blob/main/README.md)
- [원본 아키텍처 메모](https://github.com/palamim/starboard/blob/main/CLAUDE.md)
- [원본 source tree](https://github.com/palamim/starboard/tree/main/Sources/Starboard)

현재 원본은 SwiftTerm 하나를 의존하는 작은 AppKit/SwiftUI 애플리케이션이다.
핵심 동작은 다음과 같다.

- `NSPanel`을 borderless/non-activating panel로 만들고 Dock보다 한 단계 높은
  window level에 둔다.
- 모든 Space와 fullscreen에 참여하고 window cycle에서 제외한다.
- Dock Accessibility tree의 `AXList` frame을 읽어 icon tray의 위치와 크기를
  추적한다.
- 평상시 약 1초, auto-hide transition 중 약 60ms cadence로 상태를 갱신한다.
- Dock가 숨을 때 panel도 숨기되 terminal이 key window이거나 확장 상태면
  마지막으로 완전히 보이던 geometry를 동결한다.
- shell process는 앱 시작 시 한 번 만들고 앱 수명 동안 유지한다.
- settings는 `UserDefaults`에 저장한다.
- expand/collapse, theme picker와 settings는 terminal session과 분리한다.

Windows판에 보존할 개념은 persistent session, 비활성 시작, focus 중 geometry
동결, taskbar에 상대적인 배치, expand/collapse와 theme 일관성이다. Accessibility,
Space collection behavior, Dock window level과 `launchd`는 macOS 전용이므로
Windows 구현에 가져오지 않는다.

### Windows 공식 API 근거

- [ConPTY session 생성](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session)
- [ConPTY resize](https://learn.microsoft.com/en-us/windows/console/resizepseudoconsole)
- [Taskbar 위치 조회](https://learn.microsoft.com/en-us/windows/win32/shell/abm-gettaskbarpos)
- [Taskbar 상태와 재생성 notification](https://learn.microsoft.com/en-us/windows/win32/shell/taskbar)
- [Application desktop toolbar notification](https://learn.microsoft.com/en-us/windows/win32/shell/application-desktop-toolbars)
- [Per-Monitor DPI WPF](https://learn.microsoft.com/en-us/windows/win32/hidpi/declaring-managed-apps-dpi-aware)
- [공식 Virtual Desktop API](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager)
- [WPF WebView2와 composition control](https://learn.microsoft.com/en-us/microsoft-edge/webview2/platforms/wpf)
- [WebView2 runtime 배포](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/evergreen-vs-fixed-version)

공식 `IVirtualDesktopManager`는 현재 desktop 확인, desktop ID 조회와 특정
desktop으로 이동만 제공한다. 모든 desktop pinning과 desktop switch event는
공식 surface에 없으므로 v0.1의 필수 완료 조건으로 두지 않는다.

ConPTY channel은 UTF-8이며 synchronous pipe를 각각 별도 worker에서 계속
drain하지 않으면 deadlock 위험이 있다. input, output과 shutdown 수명을 분리한다.

### Renderer 조사

- [xterm.js](https://github.com/xtermjs/xterm.js)는 VS Code 등에 사용되고 CJK,
  emoji와 IME를 지원한다.
- 계획 작성 시점의 stable release인 `@xterm/xterm 6.0.0`을 기준으로 하되
  구현 시 lock file로 정확한 version을 고정한다.
- [Windows Terminal WPF control 제품화 이슈](https://github.com/microsoft/terminal/issues/6999)는
  아직 open 상태이며 control이 Windows Terminal 내부 build와 강하게 결합되어
  있으므로 v0.1 dependency로 사용하지 않는다.
- standard WPF `WebView2`는 HwndHost airspace 제약이 있지만 main panel의 모든
  overlay chrome을 같은 HTML surface에 두면 이 문제를 피할 수 있다.
- `WebView2CompositionControl`은 WPF overlay에는 유리하지만 DPI blur와 2026년
  monitor disconnect crash 사례가 열려 있다. multi-monitor 안정성이 핵심인
  이 제품에서는 기본 renderer host로 사용하지 않는다.

### 현재 로컬 환경

2026-09-01 조사 결과:

- Windows build: 10.0.26200
- user-local .NET SDK: 10.0.301
- `pwsh.exe`, Windows PowerShell과 `cmd.exe` 사용 가능
- Node.js와 npm 사용 가능
- WebView2 Evergreen Runtime 151.0.4129.107 설치됨
- system `dotnet.exe`에는 SDK가 없고
  `C:/Users/round1studio_14/.dotnet/dotnet.exe`에 SDK가 있음
- 현재 작업 폴더는 아직 Git 저장소가 아님
- 제품·test 코드가 생성됐고 Debug build와 실제 executable smoke test가 통과함

로컬 SDK 경로는 저장소 파일에 hard-code하지 않는다. 구현 명령을 실행할 때만
task-local 변수로 user-local `dotnet.exe`를 선택한다.

## 제품 및 UX 기본 결정

| 항목 | v0.1 기본값 | 이유 |
|---|---|---|
| 사용자 | Windows 개발자와 power user | terminal 상태 유지와 빠른 context 복귀가 핵심 |
| tone | technical, utilitarian, austere | 상시 표시되는 UI이므로 장식보다 정보 밀도와 절제가 중요 |
| collapsed geometry | taskbar monitor의 work area 전체 폭, 높이 116 DIP | 13px font와 상하 여백을 포함해 약 5행 terminal이 보이는 높이 |
| expanded geometry | 현재 panel monitor의 전체 work area | taskbar는 가리지 않고 terminal 작업 공간을 최대로 확보 |
| 시작 activation | 비활성 | 현재 사용 중인 앱의 focus 보존 |
| click activation | 활성 허용 | terminal 입력과 IME에 필요 |
| z-order | 기본은 일반 창 band, 트레이에서 표시할 때만 활성화 | 다른 앱을 계속 덮지 않으면서 필요할 때 즉시 복귀 |
| tray | 왼쪽 클릭은 항상 표시·활성화, 기본 메뉴에 표시/숨김과 종료 | 가려진 panel의 확실한 복귀와 명시적 숨김·종료 경로 제공 |
| Alt+Tab/taskbar button | 표시하지 않음 | desktop fixture이므로 일반 앱 window처럼 취급하지 않음 |
| taskbar auto-hide | idle이면 taskbar와 함께 conceal, active/expanded면 현재 frame 유지 | 원본의 hold/freeze UX를 Windows에 맞게 보존 |
| monitor 기본값 | system taskbar가 있는 monitor 추적 | taskbar 이동 시 자연스럽게 함께 이동 |
| expand shortcut | `Ctrl+Alt+E` global hotkey | foreground application focus를 바꾸지 않고 geometry만 변경 |
| summon shortcut | `Ctrl+Alt+S` global hotkey | 가려짐·숨김 상태에서 즉시 복귀하고 활성 상태에서는 다시 숨김 |
| main panel motion | 없음 | 정확성과 즉각 반응 우선, layout animation으로 인한 blur/focus 문제 회피 |
| 기본 shell | `pwsh` → `powershell` → `cmd` | 현대 PowerShell 우선, 기본 Windows fallback 보장 |
| 기본 font | Cascadia Mono → Cascadia Code → Consolas | Windows terminal 가독성과 설치 가능성 |
| renderer | standard WebView2 + locally bundled xterm.js | IME/ANSI 완성도와 multi-monitor 안정성 우선 |
| WebView2 runtime | Evergreen 사용, missing-runtime 오류와 offline 설치 안내 제공 | Windows 11 기본 설치, 250MB 이상 Fixed Runtime bundling 회피 |
| 배포 | self-contained win-x64 folder/zip | 관리자 권한 없이 실행하고 asset을 예측 가능하게 포함 |
| install | v0.1은 portable 배포, startup은 HKCU Run | MSIX/WiX 복잡도를 core UX 이후로 연기 |

### Hallmark 적용 범위

웹 landing page용 macrostructure 규칙은 적용하지 않는다. 다음 원칙만 desktop UI에
적용한다.

- WPF `ResourceDictionary`와 terminal theme object를 design token 정본으로
  사용하고 중간에 색상·font 값을 임의로 추가하지 않는다.
- main panel에는 card, gradient, fake window chrome과 불필요한 status badge를
  넣지 않는다.
- terminal은 실제 terminal 자체가 visual hierarchy의 중심이다.
- menu와 settings control은 default, hover, focus-visible, pressed, disabled,
  error 상태를 구분한다. loading/success가 의미 없는 control에 억지로 상태를
  만들지 않는다.
- focus ring은 즉시 보이고 충분한 contrast를 가진다.
- 색상과 background opacity가 바뀌어도 terminal text contrast를 유지한다.
- main panel animation은 생략하고 settings transition도 motion을 최소화한다.

## 범위

### v0.1 포함

- .NET 10 WPF x64 solution
- borderless persistent panel
- documented taskbar geometry와 monitor 추적
- taskbar auto-hide transition 대응
- per-monitor v2 DPI
- Explorer/taskbar 재생성 복구
- standard WebView2와 offline xterm.js asset
- 직접 구현한 최소 ConPTY host
- persistent PowerShell/cmd session
- 최대 8개의 독립적인 terminal tab과 tab별 persistent shell session
- UTF-8, ANSI/VT, 한글, IME와 clipboard
- terminal resize
- non-stealing start/reposition과 deliberate click activation
- normal/maximized/fullscreen z-order 정책
- global expand/collapse shortcut
- settings JSON과 atomic write
- Dark, Light, One Dark, Tokyo Night
- startup registration
- shell resolver와 WSL/custom executable 구조
- shell/renderer failure 상태와 restart
- unit/integration/manual test plan
- self-contained win-x64 publish와 README

### v0.1 제외 또는 후순위

- split pane, tab 분리 창, drag reorder와 여러 window
- tab/session의 앱 재시작 간 복원과 사용자 지정 tab 이름
- shell title escape sequence를 해석한 동적 tab 이름
- SSH profile manager
- command palette와 theme picker shortcut
- GPU/WebGL renderer
- shell session persistence across app process restart
- admin/elevated shell 전환
- Microsoft Store 제출
- full installer와 auto updater
- Windows on ARM
- undocumented Virtual Desktop COM API 기본 사용
- 모든 virtual desktop pinning 보장
- Windows Terminal settings/profile 자동 import
- telemetry, analytics와 network feature

## 아키텍처 결정

### Solution 구성

v0.1부터 단일 프로세스·단일 실행 파일·단일 배포를 유지하는 모듈러 모놀리스로
구성한다. WPF host는 composition root와 최상위 window만 소유하고, 제품 기능은
별도 class library module로 분리한다. 폴더 관례뿐 아니라 `ProjectReference`와
public/internal 접근 제한으로 경계를 강제한다.

```text
Starboard.Windows.sln
src/
  Starboard.Windows/
    Starboard.Windows.csproj
    App.xaml
    App.xaml.cs
    app.manifest
    Composition/
      AppCoordinator.cs
      SingleInstanceGuard.cs
      ModuleComposition.cs
    Shell/
      MainWindow.xaml
      MainWindow.xaml.cs
      SettingsWindow.xaml
      SettingsWindow.xaml.cs
    Infrastructure/
      DiagnosticLog.cs
  Starboard.SharedKernel/
    Starboard.SharedKernel.csproj
    Diagnostics/
  Modules/
    Starboard.Modules.Terminal/
      Starboard.Modules.Terminal.csproj
      Contracts/
        TerminalOptions.cs
        TerminalSessionState.cs
      Application/
        TerminalSession.cs
        ShellResolver.cs
      Infrastructure/
        ConPty/
          ConPtyHost.cs
          Interop/
            ConsoleNativeMethods.cs
            SafePseudoConsoleHandle.cs
        Renderer/
          TerminalRendererHost.cs
          TerminalBridgeMessage.cs
          Web/
            dist/
            src/
            package.json
            package-lock.json
      Presentation/
        TerminalView.xaml
        TerminalView.xaml.cs
    Starboard.Modules.DesktopIntegration/
      Starboard.Modules.DesktopIntegration.csproj
      Contracts/
      Domain/
        PanelWindowPolicy.cs
        PanelGeometryCalculator.cs
      Infrastructure/
        Interop/
          ShellNativeMethods.cs
          WindowNativeMethods.cs
          NativeTypes.cs
        Taskbar/
          TaskbarService.cs
          TaskbarWatcher.cs
        Display/
          MonitorService.cs
          DpiConverter.cs
        Windowing/
          FullscreenWatcher.cs
          GlobalHotKeyService.cs
        VirtualDesktop/
          SupportedVirtualDesktopService.cs
          FallbackVirtualDesktopService.cs
        Startup/
          StartupService.cs
    Starboard.Modules.Preferences/
      Starboard.Modules.Preferences.csproj
      Contracts/
      Domain/
        AppSettings.cs
        TerminalTheme.cs
      Application/
        SettingsValidator.cs
        ThemeCatalog.cs
      Infrastructure/
        SettingsStore.cs
        ThemeDefinitions/
tests/
  Starboard.ArchitectureTests/
  Starboard.Modules.Terminal.Tests/
  Starboard.Modules.DesktopIntegration.Tests/
  Starboard.Modules.Preferences.Tests/
  Starboard.IntegrationTests/
docs/
  architecture-reference.md
  architecture.md
  test-plan.md
  plans/
THIRD-PARTY-NOTICES.md
README.md
```

세 module의 책임은 다음과 같이 고정한다.

- `Terminal`: shell 선택, ConPTY transport/session 수명, renderer bridge와
  terminal surface
- `DesktopIntegration`: taskbar/display/DPI/window/focus/hotkey/fullscreen,
  virtual desktop와 startup OS 연동
- `Preferences`: schema-versioned settings 저장·검증, theme catalog와 사용자
  preference model

`Starboard.Windows`가 Preferences 값을 각 module 전용 option으로 변환하고 module
수명을 조정한다. module끼리는 직접 참조하지 않는다. 실제 구현에서 trivial type이
지나치게 늘어나면 같은 module 안의 인접 책임을 합치되 project boundary는
유지한다. 위 tree 자체를 목표로 빈 layer나 전달만 하는 abstraction을 만들지
않는다.

### 모듈 경계 규칙

production reference graph는 다음 한 방향만 허용한다.

```text
Starboard.Windows
  -> Starboard.Modules.Terminal
  -> Starboard.Modules.DesktopIntegration
  -> Starboard.Modules.Preferences

각 module -> Starboard.SharedKernel -> BCL
```

- host만 세 module을 모두 알고 object graph와 startup/shutdown 순서를 구성한다.
- module은 다른 module project를 참조하지 않는다. 협력이 필요하면 host가 명시적
  contract call 또는 좁은 event를 중재한다.
- module entry point, input/output DTO와 외부 소비 interface만 `public`으로 두고
  concrete implementation은 기본적으로 `internal`로 둔다.
- `SharedKernel`은 둘 이상의 module이 같은 의미로 사용하는 안정적인 primitive와
  diagnostics contract만 허용한다. module별 DTO와 business rule은 이동하지
  않는다.
- test project의 production module 참조는 허용하되 production graph로 역유입되지
  않는다.
- `Starboard.ArchitectureTests`에서 assembly reference, 금지 namespace와 public
  surface 규칙을 검사한다. 별도 architecture framework 없이 MSTest와 reflection,
  project metadata로 먼저 구현한다.

### Dependency 정책

production NuGet dependency는 우선 `Microsoft.Web.WebView2` 하나로 제한한다.
ConPTY는 wrapper package 없이 documented Win32 API를 직접 호출한다.

terminal web asset은 다음 package만 정확히 pin한다.

- `@xterm/xterm`
- `@xterm/addon-fit`
- bundling에 필요한 최소 build tool 하나

`package-lock.json`과 review된 `dist`를 함께 관리한다. 일반 .NET build는 Node.js나
network 없이 committed `dist`를 사용한다. xterm.js를 upgrade할 때만
`npm ci`와 asset build를 실행하고 결과 diff를 검토한다.

test framework는 생성 시점의 stable MSTest package를 pin한다. runtime logging,
dependency injection, module discovery와 settings를 위해 큰 framework를 추가하지
않는다. `Starboard.Windows`의 작은 `AppCoordinator`가 세 module facade와 shared
service를 명시적으로 생성하고 연결한다.

### Application lifecycle

1. host의 `SingleInstanceGuard`가 중복 실행을 막는다.
2. Preferences module이 settings를 기본값과 merge하고 validate한다.
3. host가 validated preference를 Terminal/DesktopIntegration option으로 매핑한다.
4. DesktopIntegration module이 taskbar/display snapshot을 조회한다.
5. host가 main window HWND를 만들되 활성화하지 않고 첫 geometry를 적용한다.
6. Terminal module이 하나의 WebView2 renderer와 terminal workspace를 초기화한다.
7. renderer의 protocol v2 `ready` 이후 Terminal module이 첫 tab과 그 tab 전용
   ConPTY shell을 시작한다.
8. DesktopIntegration module이 watcher, hotkey와 foreground hook을 연결한다.
9. 명시적 종료에서 host가 DesktopIntegration watcher를 먼저 중단하고 Terminal
   module은 모든 tab의 input 차단 → shell 종료 → ConPTY output drain → renderer
   순으로 bounded shutdown을 병렬 조정한다.
10. unexpected shell exit는 앱과 다른 tab을 유지하고 해당 tab에만 restart action을
    표시한다.
11. renderer crash는 모든 ConPTY session을 즉시 폐기하지 않고 짧은 recovery
    window 동안 renderer를 재생성한다. 재연결 실패 시에도 session 간 output이나
    input을 섞지 않고 전역 WPF 오류 surface에서 재시도를 제공한다.

### Window policy state

window policy는 UI event에 흩어진 boolean 대신 다음 상태를 조합해 계산한다.

- `PanelMode`: Collapsed, Expanded
- `EngagementState`: Idle, Active
- `TaskbarPresence`: Visible, Concealed, Unknown
- `FullscreenState`: Normal, FullscreenOnPanelMonitor
- `TrackingState`: Tracked, Fallback
- `RendererState`: Starting, Ready, Failed

입력 snapshot에서 `PanelWindowAction`을 계산하는 pure reducer를 두고 다음 action만
window service가 실행한다.

- ShowWithoutActivation
- Conceal
- MoveWithoutActivation
- PreserveNormalZOrder
- ActivateOnTrayRestore
- FreezeCurrentGeometry
- RestoreCollapsedGeometry
- ApplyExpandedGeometry

이 구조로 auto-hide transition, active hold와 fullscreen policy를 unit test한다.

### Taskbar와 display 추적

우선순위는 다음과 같다.

1. `SHAppBarMessage(ABM_GETTASKBARPOS)`로 system taskbar rectangle을 조회한다.
2. `MonitorFromRect`와 `GetMonitorInfo`로 monitor bounds와 work area를 얻는다.
3. `ABM_GETSTATE`로 auto-hide 상태를 얻는다.
4. `TaskbarCreated`, `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE`,
   `WM_DPICHANGED`와 foreground WinEvent를 받으면 snapshot을 다시 계산한다.
5. Explorer restart 뒤 taskbar HWND와 snapshot을 재획득한다.
6. auto-hide animation의 실제 visible/concealed 판정에 taskbar HWND geometry가
   필요하면 `Shell_TrayWnd` lookup을 격리된 fallback adapter에서만 사용한다.
7. event가 누락되는 환경을 위해 평상시 낮은 빈도의 reconciliation timer를 둔다.
   auto-hide edge transition이 감지된 짧은 구간에만 fast sampling을 사용한다.

Starboard를 appbar로 등록하거나 `ABM_SETPOS`를 호출하지 않는다. appbar 등록은
work area를 바꾸고 일반 application layout에 영향을 줄 수 있어 제품 원칙과
충돌한다.

secondary taskbar class lookup은 공식 API가 아니므로 기본 완료 조건은 system
taskbar monitor 추적이다. secondary taskbar 선택 기능은 adapter와 manual test를
통과한 뒤에만 활성화한다.

### Geometry 정책

geometry 계산은 physical pixel을 입력·출력으로 사용한다. WPF view setting만 DIP로
보관하고 해당 monitor DPI에서 한 번 변환한다.

bottom taskbar 기준:

- collapsed X = monitor work area left
- collapsed width = monitor work area width
- collapsed height = configured height DIP를 physical pixel로 변환한 값
- collapsed bottom = visible taskbar top
- expanded rectangle = monitor work area

top taskbar는 taskbar 아래쪽에 붙인다. left/right taskbar에서는 taskbar edge와
평행한 panel을 남은 work area 안쪽에 배치하고 두께를 안전 범위로 clamp한다.
실사용성이 떨어지면 향후 monitor별 수평 fallback을 별도 정책으로 추가한다.

monitor가 제거되면 마지막 frame을 사용하지 않고 새 taskbar snapshot으로 즉시
재계산한다. active/expanded 상태라도 frame 전체가 어떤 monitor와도 intersect하지
않으면 안전한 monitor로 이동한다.

### Focus와 z-order

- main window는 `ShowActivated=false`와 `ShowInTaskbar=false`로 시작한다.
- `WS_EX_TOOLWINDOW`를 적용한다.
- `WS_EX_NOACTIVATE`를 영구 적용하지 않는다. 영구 적용하면 WebView2, IME와
  deliberate click activation이 깨질 가능성이 높다.
- background reposition은 `SetWindowPos(..., SWP_NOACTIVATE)`로 실행한다.
- 사용자가 WebView surface를 클릭하면 일반 activation을 허용한다.
- global expand/collapse는 foreground window를 바꾸지 않는다.
- normal/maximized desktop에서도 topmost band를 사용하지 않는다.
- background geometry 갱신은 현재 normal z-order를 보존한다. 트레이에서 표시를
  요청한 경우에만 창을 활성화해 normal window band의 앞으로 가져온다.
- foreground window가 panel monitor를 실제 fullscreen으로 덮으면 panel을
  conceal할 수 있다. foreground가 바뀌어도 topmost로 승격하지 않는다.
- settings window는 사용자가 menu에서 연 별도 일반 WPF window로 정상 활성화한다.

### Renderer와 host bridge

terminal은 local HTML surface에서 그린다. loading/error/retry는 WebView2를 숨긴
동안만 보이는 WPF 대체 surface로 두어 airspace 위에 control을 겹치지 않는다.
main HTML surface의 settings 진입점은 Phase 3로 남긴다. notification icon의
표시/숨김·종료 menu는 Phase 1.1에서 제공한다.

WebView2는 local asset만 탐색할 수 있게 구성한다.

- DevTools, default context menu, status bar, external drop과 browser shortcut 비활성
- permission request 기본 거부
- 새 window와 외부 navigation 차단
- local virtual host 이외의 web request 차단
- runtime CDN/font/script request 없음
- renderer source와 build asset license 기록

현재 구현이 사용하는 JSON field 이름 `version`을 유지하되 다중 탭 bridge는
protocol version 2로 한 번에 전환한다. envelope는 `{ version, type, sessionId?,
payload }`이며 `sessionId`는 host가 생성하는 32자리 lowercase hex GUID를 opaque
identifier로 취급하고 process lifetime 동안 재사용하지 않는다. renderer가 임의로
만든 ID, registry에 없는 ID, 이미 닫히는 session의 ID와 version 1 message는 host가
side effect 없이 거부한다. version 1로 자동 downgrade하면 input/output을 잘못된
session에 전달할 수 있으므로 호환 shim을 두지 않는다.

Web → host:

- workspace scope: `ready`, `new-session`, `resize`, `renderer-error`
- session scope: `activate-session`, `close-session`, `restart-session`, `input`, `copy`,
  `paste-request`

Host → web:

- workspace scope: `initialize`
- session scope: `session-added`, `session-activated`, `session-state`, `session-removed`,
  `output`, `paste`, `reset`

`initialize` payload에는 font/theme, `maxSessions`, ordered tab snapshot과
`activeSessionId`를 포함한다. tab snapshot은 `sessionId`, 표시용 `title`과
`starting|running|exited|failed|closing` state만 노출하며 command, working directory와
terminal output은 포함하지 않는다. session scope message는 모두 envelope의
`sessionId`가 필수다. `resize`만 workspace scope이며 최신 cell size를 모든 running
session과 이후 생성되는 session에 적용한다. input/copy/paste는 host가 확인한 active
session에서만 처리한다.

host는 tab registry, 순서, active session과 state의 정본이다. renderer는
`new/activate/close/restart` 의도만 보내고 host의 응답 전에는 registry를 확정하지
않는다. close/restart 중 늦게 도착한 output callback은 controller가 현재 registry와
session instance를 다시 확인한 뒤 버린다. unknown/stale ID, 잘못된 state 전이,
누락된 payload, 범위를 벗어난 resize와 1MiB 초과 message를 protocol test로 거부한다.

renderer는 WebView2 하나 안에 session별 xterm.js `Terminal`과 `FitAddon`을 만들고
active terminal의 DOM surface만 표시한다. inactive terminal도 routed output을 받아
scrollback과 interactive state를 유지한다. WebView2를 tab마다 만들지 않아 browser
process, airspace, focus와 DPI 수명을 tab 수만큼 늘리지 않는다. renderer 재시작 시
host가 registry snapshot을 다시 보내고 끊긴 동안의 bounded pending output만
재전송한다. 이미 renderer에 전달된 과거 scrollback은 개인정보와 memory 상한 때문에
host transcript로 복제하지 않으며 renderer crash 뒤에는 복원되지 않는 제한을
명시한다.

output은 session별 ConPTY UTF-8 stream을 incremental decoder로 처리하고 최대
64KiB 단위로 batching한다. pending buffer는 session당 4MiB, 동시 session은 최대
8개로 제한해 host pending output 상한을 32MiB로 고정한다. renderer가 따라오지
못하면 해당 noisy session의 가장 오래된 output만 버리고 payload 없이 session ID와
overflow 횟수만 diagnostic에 기록한다.

### ConPTY session

- `CreatePipe`로 input/output channel을 만든다.
- `CreatePseudoConsole`과 `STARTUPINFOEX`의 pseudo console attribute를 사용해
  child shell을 생성한다.
- input writer와 output reader는 별도 background worker에서 동작한다.
- process tree와 owned handle 수명을 명확히 분리한다.
- terminal cell size가 바뀌면 debounce 후 `ResizePseudoConsole`을 호출한다.
- ConPTY output은 UTF-8 incremental decoder를 사용한다.
- renderer input은 UTF-8로 encode해 ConPTY input pipe에 쓴다.
- shell exit code를 관찰하고 Failed/Exited state와 restart action을 제공한다.
- shutdown 시 output drain이 막히지 않도록 child 종료, pipe close와 HPCON close
  순서를 integration test로 고정한다.
- command나 terminal output은 log에 기록하지 않는다.

### 다중 탭 책임과 session lifecycle

- 다중 탭은 새 module이나 host 책임으로 올리지 않고 Terminal module 내부의
  application/presentation 책임으로 둔다. `TerminalModule`은 계속 단일 `Surface`와
  시작/종료 facade만 공개하고 `AppCoordinator`, `MainWindow`, DesktopIntegration과
  Preferences는 tab collection이나 `ConPtySession`을 알지 않는다.
- application 계층의 workspace/controller가 ordered tab registry, active ID,
  최대 8개 제한과 state transition을 소유한다. native/test boundary가 있는 session
  factory만 좁은 internal seam으로 두고 실제 pipe/HPCON/process 소유권은 기존
  `ConPtySession`에 남긴다.
- backend state는 `starting → running|failed|exited`,
  `running → exited|failed|restarting`, `exited|failed → restarting → starting`으로
  전환한다. close는 registry와 session instance mapping을 같은 lock에서 제거해 이후
  input/resize와 stale callback을 거부한다.
- tab 하나는 정확히 하나의 `ConPtySession`을 소유한다. 새 tab은 현재 shell option과
  최신 rows/columns로 새 process를 시작하며 다른 tab의 working directory,
  environment, history, foreground job과 exit state를 공유하지 않는다.
- 최초 실행은 `PowerShell 1` tab 하나를 만들고 이후 생성 순번을 재사용하지 않는
  `PowerShell N` 이름을 붙인다. 마지막 tab을 닫으면 이전 transport cleanup보다 먼저
  새 기본 tab과 shell을 생성·활성화해 terminal surface를 항상 하나 유지한다. 새 tab
  button과 keyboard shortcut은 Phase 1.3B renderer 범위다.
- tab 전환은 process를 suspend/restart하지 않는다. active ID만 바꾸고 선택된 xterm에
  focus를 옮긴 뒤 latest resize를 모든 running session에 적용한다. panel hide,
  expand/collapse와 background geometry 변경도 session 수명에 영향을 주지 않는다.
- 새 shell 시작 실패나 shell exit는 해당 tab을 `failed` 또는 `exited`로 바꾸고 그
  tab에만 restart/close를 제공한다. restart는 같은 tab ID 아래 새 ConPTY instance를
  만들고 기존 instance의 event를 먼저 분리한다. renderer 전체 실패만 WPF 전역 오류
  surface를 사용하며 이 경우 기존 ConPTY session은 recovery 동안 계속 실행한다.
- close는 해당 tab의 registry entry와 instance mapping을 먼저 제거해 새 input/resize를
  거부하고, 오른쪽 이웃 우선·없으면 왼쪽 이웃을 active로 정한 뒤 그 session만
  비동기로 정리한다. 마지막 tab이면 새 기본 session을 cleanup 전에 시작한다.
- session 하나의 disposal은 input 차단, cancellation, input pipe close, child
  terminate/wait, HPCON close, output pump 관찰과 나머지 SafeHandle dispose 순서를
  유지하고 총 6초 이내의 bounded wait를 사용한다. 앱 종료에서는 최대 8개 session을
  순차 처리하지 않고 병렬 정리해 workspace 전체 deadline을 8초로 제한한다. 각 단계
  timeout 뒤에도 남은 owned handle dispose를 계속하고 command/output 없는 local
  diagnostic만 기록한다.
- session coordinator의 registry mutation은 lock과 operation gate로 직렬화하고
  process I/O와 disposal은 UI synchronization context를 캡처하지 않는다.
  close/restart 뒤 callback은 controller instance identity와 state를 확인해 다른
  tab으로 재사용되지 않게 한다.

### Shortcut와 clipboard

기본 정책:

- selection이 있으면 `Ctrl+C`는 copy
- selection이 없으면 `Ctrl+C`는 ETX를 shell에 전송
- `Ctrl+Shift+C`는 항상 copy
- `Ctrl+V`와 `Ctrl+Shift+V`는 clipboard paste
- paste는 Windows Clipboard를 host에서 읽고 renderer/ConPTY로 전달
- large paste에는 size limit과 bracketed paste 지원 여부를 적용
- `Ctrl+Alt+E`는 `RegisterHotKey`로 등록하고 충돌 시 settings에 오류를 표시
- `Ctrl+Alt+S`도 `RegisterHotKey`로 등록한다. panel이 숨겨졌거나 비활성
  상태면 표시·활성화하고, 이미 활성 상태면 숨긴다.
- notification icon 왼쪽 클릭은 단순 가시성 반전이 아니라 panel 호출로 처리한다.
  따라서 다른 창 뒤에 가려진 panel을 숨기지 않고 앞쪽으로 되돌린다.
- 항상 위 고정은 다른 앱의 하단 UI를 덮으므로 기본 정책으로 사용하지 않는다.

IME composition은 browser에 맡기고 `compositionstart/update/end` 중 host shortcut
처리를 억제한다.

### Settings와 local data

기본 경로:

```text
%LOCALAPPDATA%/Starboard/
  settings.json
  settings.json.bak
  Logs/
  WebView2/
```

settings에는 `schemaVersion`을 둔다. 읽을 때 missing/old/partial document를
기본값과 merge하고, 쓰기는 같은 directory의 temp file에 flush한 뒤 atomic
replace한다.

background opacity는 text opacity와 분리한다. standard WebView2에서 실제
desktop-through alpha가 안정적으로 동작하는지 Phase 1에서 검증한다. 실패하면
composition control로 즉시 전환하지 않고 다음 순서로 fallback한다.

1. opaque WebView surface에서 theme tint 강도만 조절
2. top-level window opacity가 input/DPI에 안전한지 검증
3. composition control은 monitor hot-plug 회귀 검증을 통과한 경우에만 opt-in

## 영향 파일

Phase 0에서 다음 문서를 수정·추가했다.

- `AGENTS.md`
- `.hallmark/preflight.json` (초기 디자인 점검 기록; 2026-10-08 공개 저장소 정리에서 삭제)
- `docs/architecture-reference.md`
- `docs/architecture.md`
- `docs/test-plan.md`
- `docs/plans/2026-09-01-windows-starboard-v01.md`

Phase 1에서 solution과 product/test 파일, renderer source/dist, third-party notice와
README를 생성했다. 이후 phase도 동일한 module 경계 안에서 확장한다.

다중 탭 Phase의 예상 변경 범위는 다음과 같다. host composition과 다른 module은
contract 회귀 확인 대상이지만 기본 변경 대상은 아니다.

- `src/Modules/Starboard.Modules.Terminal/Application/RendererMessage.cs`
- `src/Modules/Starboard.Modules.Terminal/Application/RendererProtocol.cs`
- `src/Modules/Starboard.Modules.Terminal/Application/`의 tab registry/session
  controller와 test seam 새 파일
- `src/Modules/Starboard.Modules.Terminal/Infrastructure/ConPtySession.cs`
- `src/Modules/Starboard.Modules.Terminal/Presentation/TerminalView.xaml`
- `src/Modules/Starboard.Modules.Terminal/Presentation/TerminalView.xaml.cs`
- `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/src/index.html`
- `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/src/index.ts`
- `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/src/styles.css`
- renderer build가 재생성하는 `Presentation/Renderer/dist/*`
- `tests/Starboard.Modules.Terminal.Tests/RendererProtocolTests.cs`
- `tests/Starboard.Modules.Terminal.Tests/`의 workspace/session lifecycle test 새 파일
- `tests/Starboard.IntegrationTests/ConPtyLifecycleTests.cs`와 필요 시 test host
- 실제 동작과 검증 결과를 반영할 `docs/architecture.md`, `docs/test-plan.md`,
  `README.md`

## 구현 단계

### Phase 0 — 분석과 architecture lock

예상: 1~2시간

- [x] 원본 source를 파일 단위로 분석한다.
- [x] `docs/architecture-reference.md`를 작성한다.
- [x] macOS 전용 mechanism과 portable product concept를 표로 분리한다.
- [x] Windows focus/z-order/taskbar/DPI/virtual desktop API 근거를 정리한다.
- [x] renderer와 ConPTY 설계를 `docs/architecture.md`에 확정한다.
- [x] 자동/manual matrix를 `docs/test-plan.md`에 작성한다.
- [x] local .NET SDK를 사용하는 build command를 확인한다.
- [x] 모듈러 모놀리스의 module 책임, project 이름, reference 방향과 public
  boundary를 확정한다.

완료 gate:

- architecture 문서가 원본 분석 질문을 모두 답한다.
- 추측 상태의 Windows API 동작은 `검증 필요`로 표시돼 있다.
- product code를 만들기 전 주요 fallback이 정해져 있다.
- module dependency graph와 architecture test 항목이 문서에 일치한다.

### Phase 1 — 실행 가능한 terminal vertical slice

예상: 7~11시간

#### 1A. Scaffold와 window

- [x] .NET 10 WPF host, SharedKernel, 세 module과 module별 test project를
  생성한다.
- [x] `global.json`으로 .NET 10 feature band를 고정한다.
- [x] 허용된 단방향 `ProjectReference`만 연결한다.
- [x] assembly reference와 public surface를 검증하는 architecture test를 만든다.
- [x] per-monitor v2, asInvoker manifest를 추가한다.
- [x] module facade, single-instance guard와 host app coordinator를 만든다.
- [x] borderless, tool-window main panel을 taskbar 위에 표시한다.
- [x] launch/reposition이 foreground focus를 유지하는지 실제 process smoke로 확인한다.

#### 1B. Renderer

- [x] xterm.js package와 lock file을 고정한다.
- [x] local-only terminal HTML/CSS/TypeScript를 만든다.
- [x] standard WebView2를 초기화하고 local virtual host만 허용한다.
- [x] renderer bridge protocol과 validation을 구현한다.
- [x] fit addon이 계산한 rows/columns를 host에 전달한다.
- [x] renderer failure UI와 restart surface를 만든다.
- [x] opacity 적용과 renderer 입력 경계를 구현·자동 검증했다. 실제 한글 IME composition은
  `docs/test-plan.md`의 MAN-028 `Blocked (manual)`로 분리한다.

#### 1C. ConPTY

- [x] SafeHandle과 최소 P/Invoke를 구현한다.
- [x] shell resolver를 구현한다.
- [x] persistent `pwsh.exe` session을 시작한다.
- [x] input/output stream과 UTF-8 decoder를 연결한다.
- [x] resize를 ConPTY에 전달한다.
- [x] shell exit와 restart를 구현한다.
- [x] bounded shutdown을 구현한다.

완료 gate:

- 앱이 build되고 실행된다.
- architecture test가 module 간 직접·순환 참조와 내부 구현 노출을 차단한다.
- 실행 시 기존 foreground window가 유지된다.
- `cd` 이후 다음 명령에서 working directory가 유지된다.
- PowerShell history, Unicode/한글, long-running command와 `Ctrl+C`가 동작한다.
- panel resize가 terminal column/row와 ConPTY size에 반영된다.
- runtime network request 없이 renderer가 로드된다.

### Phase 1.1 — 트레이 접근성과 비방해 z-order 후속

예상: 1~2시간

- [x] DesktopIntegration module이 Windows notification-area icon과 native context
  menu의 수명을 소유한다.
- [x] 트레이 왼쪽 클릭과 `터미널 표시/숨기기`가 shell session을 종료하지 않고
  panel visibility를 제어한다. Phase 1.2에서 왼쪽 클릭은 호출, menu는 명시적
  표시/숨김으로 역할을 분리했다.
- [x] 트레이 menu에 `종료`를 제공하고 host lifecycle을 정상 종료한다.
- [x] tray/OS 종료 시 terminal async 정리가 UI synchronization context를 캡처해
  교착되지 않도록 renderer와 ConPTY 정리 순서를 분리한다.
- [x] panel의 기본 높이를 116 DIP로 올리고 schema 1의 96 DIP 기본값을 migration한다.
- [x] WPF `Topmost`와 반복 `HWND_TOPMOST` 배치를 제거한다.
- [x] 숨긴 panel을 reconciliation timer가 다시 표시하지 않게 visibility state를
  window placement state와 함께 관리한다.
- [x] 관련 unit test, solution build/test, self-contained publish와 실제 process
  smoke를 다시 수행한다.

완료 gate:

- 트레이 icon에서 panel 표시/숨김과 종료를 수행할 수 있다.
- panel을 숨겨도 shell process가 유지되고 timer 때문에 임의로 다시 나타나지 않는다.
- 다른 일반 앱을 활성화하면 Starboard가 그 앱 위에 계속 남지 않는다.
- 트레이에서 panel을 표시하면 해당 사용자 동작에 한해 panel이 앞으로 온다.
- 기본 collapsed geometry가 약 5행을 표시하는 116 DIP로 계산된다.

### Phase 1.2 — 전역 호출과 트레이 포커스 복구

예상: 1~2시간

- [x] `Ctrl+Alt+S`를 `MOD_NOREPEAT` global hotkey로 등록한다.
- [x] 비활성·숨김 상태에서는 panel을 표시·활성화하고, 이미 활성 상태면 숨긴다.
- [x] 트레이 왼쪽 클릭은 visibility toggle과 분리해 항상 panel을 호출한다.
- [x] 명시적 호출에서만 foreground input queue를 잠깐 연결해 normal window band의
  앞으로 복구하고 즉시 해제한다.
- [x] `WS_EX_TOPMOST`를 설정하지 않고 1초 reconciliation도 z-order를 바꾸지 않는다.
- [x] hotkey message unit test 2개, 전체 test 32개와 실제 key/tray click smoke를
  통과한다.

완료 gate:

- `Ctrl+Alt+S`로 비활성 panel을 foreground로 호출하고 활성 panel을 숨길 수 있다.
- 숨긴 panel을 실제 notification-area icon 왼쪽 클릭으로 표시·활성화할 수 있다.
- 호출 후에도 `WS_EX_TOPMOST`가 없고 다른 앱이 다시 자연스럽게 panel을 덮을 수 있다.

### Phase 1.3 — 다중 terminal tab

예상: 5~8시간

이번 `terminal-session-tabs` 작업은 1.3A의 backend/session 수명 경계와 그 자동
검증까지만 구현한다. renderer protocol v2, tab chrome과 shortcut은 1.3B에 남긴다.
직접 할당된 동작에 맞춰 마지막 tab close는 거부하지 않고 기존 session을 정리한
뒤 새 기본 tab을 같은 전환에서 즉시 생성·활성화한다. tab 이름은 생성 순번을
재사용하지 않는 `PowerShell N` 형식이며 restart는 tab ID와 이름을 보존한다.

사전 조건: `feature/terminal-tabs`의 전용 worktree에서만 제품 수정, renderer build와
.NET 검증을 수행한다. 기존 worktree나 branch가 있으면 삭제/reset하지 않고 clean
상태와 기준 commit을 확인한 뒤 재사용한다.

#### 1.3A. Workspace와 독립 session

- [x] Terminal module 내부에 ordered tab registry, active ID, state transition과
  최대 8개 제한을 구현한다.
- [x] tab마다 별도 `ConPtySession`을 생성하고 working directory, environment,
  history, job과 exit/restart 수명이 섞이지 않게 한다.
- [x] create/activate/close/restart race와 stale callback을 instance identity로
  차단한다.
- [x] 한 session의 launch/resize/input/exit 실패가 다른 session을 중단하거나 전역
  error surface를 열지 않게 한다.
- [x] single close 6초, 최대 8개 병렬 app shutdown 8초 deadline과 handle 정리 순서를
  자동 test로 고정한다.

#### 1.3B. Renderer protocol v2와 tab UI

- [x] bridge를 protocol version 2와 host 발급 `sessionId` schema로 전환하고 v1,
  unknown/stale ID, malformed/oversized payload를 거부한다.
- [x] 하나의 WebView2 안에 tab별 xterm instance를 만들고 inactive session의
  scrollback/output을 유지한다.
- [x] new/activate/close/restart intent와 host-confirmed state rendering을 구현한다.
- [x] 새 tab, tab 전환, tab 닫기 button에 hover, focus-visible, pressed, disabled,
  error/closing state와 접근 가능한 이름을 제공한다.
- [x] `Ctrl+Shift+T`, `Ctrl+Tab`, `Ctrl+Shift+Tab`, `Ctrl+Shift+W`를 IME composition과
  기존 copy/paste/interrupt shortcut을 깨지 않도록 연결한다.
- [x] renderer crash 재연결 시 tab snapshot과 bounded pending output만 복원하고,
  protocol mismatch에서는 input을 차단한 전역 오류 fallback을 표시한다.

#### 1.3C. 통합과 문서

- [x] renderer source를 build해 `dist`를 재생성하고 runtime network request가 없는지
  확인한다.
- [x] unit/integration test와 hidden GUI host의 실제 세 session 검증을 수행하고 session별 PID,
  working directory 유지, failure isolation과 bounded shutdown을 확인한다.
- [x] `docs/architecture.md`, `docs/test-plan.md`와 README의 multi-tab 동작, shortcut,
  crash 시 scrollback 제한과 수행 결과를 실제 구현에 맞게 갱신한다.

완료 gate:

- 두 개 이상의 tab이 서로 다른 shell process와 interactive state를 유지한다.
- inactive tab의 long-running command와 output이 계속 진행되며 tab 전환 뒤 확인된다.
- 한 tab을 종료/restart/close해도 나머지 tab의 PID와 working directory가 유지된다.
- renderer는 잘못된 session routing을 거부하고 tab별 output을 다른 xterm에 쓰지 않는다.
- 마지막 tab close는 새 기본 tab을 즉시 생성·활성화하며, 최대 8개를 넘는 생성은
  process를 만들기 전에 명확히 거부된다.
- renderer crash/retry 동안 shell session은 유지되고 과거 scrollback 비복원 제한이
  사용자에게 숨겨지지 않는다.
- close와 app shutdown이 정한 deadline 안에 끝나고 남은 Starboard 소유 shell,
  pipe와 HPCON handle이 없다.
- 기존 panel focus, tray summon, expand/collapse와 offline renderer 동작이 회귀하지
  않는다.

### Phase 2 — Windows windowing 완성

2026-09-05 이후 Phase 2~4의 실사용 개선 작업은
[오케스트레이터 실행 명세](2026-09-05-orchestrator-product-roadmap.md)의 작업 분해와
인수 기준을 함께 따른다. 당시 실행의 필수 범위는 Windows 안정화, 설정 창과 portable
배포였고 가상 데스크톱과 WSL은 후속으로 두었다. 이후 공식 virtual desktop adapter/fallback과
WSL launch profile을 구현했으며, 문서화되지 않은 pinning과 임의 custom executable은 지원 범위에서
제외한다.

예상: 5~8시간

- [x] taskbar/display snapshot model과 pure geometry calculator를 만든다.
- [x] `ABM_GETTASKBARPOS`, `ABM_GETSTATE`, `GetMonitorInfo`를 연결한다.
- [x] `TaskbarCreated`, display, setting, DPI message를 처리한다.
- [x] `TaskbarCreated` 뒤 taskbar geometry를 다시 조회하는 복구 경로를 구현한다.
- [x] display change에서 monitor/taskbar snapshot을 다시 조회하고 제거된 monitor를 안전하게
  재선택하는 경로를 구현·자동 검증했다. 실제 cable hot-plug은 MAN-009~011에 남긴다.
- [x] 100/125/150/200%와 mixed-DPI 변환을 pure geometry test로 검증했다. 실제 화면 이동과
  선명도 관찰은 MAN-012~016 `Blocked (manual)`다.
- [x] auto-hide 상태와 active/expanded freeze 정책을 구현·자동 검증했다.
- [x] `Ctrl+Alt+E` expand/collapse를 구현한다.
- [x] foreground fullscreen watcher와 z-order demotion 정책을 구현·자동 검증했다.
- [x] normal/maximized/fullscreen focus 정책의 reducer·integration 회귀를 검증했다. 실제 foreground
  응용프로그램을 사용한 화면 관찰은 MAN-018~020 `Blocked (manual)`다.
- [x] bottom/top/left/right taskbar geometry test를 추가한다.

완료 gate:

- taskbar를 덮지 않고 지정 monitor를 따라간다.
- 위치 변경과 expand/collapse가 foreground focus를 바꾸지 않는다.
- terminal click과 IME 입력은 정상 활성화된다.
- monitor 제거와 Explorer restart 뒤 안전한 geometry로 복구한다.
- fullscreen 앱 위에 panel을 강제로 유지하지 않는다.

### Phase 3 — Settings, theme와 startup

예상: 4~6시간

- [x] schema-versioned settings model과 atomic store를 구현한다.
- [x] terminal height, font, size와 top-level opacity를 적용한다.
- [x] Dark, Light, One Dark, Tokyo Night token을 구현한다.
- [x] theme token이 WPF와 xterm ANSI palette에 함께 적용되게 한다.
- [x] renderer 탭·저장 탭·context menu를 구현했다.
- [x] 별도 WPF settings window와 단일 창 수명·저장/취소 경계를 구현했다.
- [x] JSON 기반 shell selection과 executable validation을 구현한다.
- [x] builtin shell과 발견된 WSL 배포판의 profile 기반 session 실행을 지원한다. 임의 custom
  executable은 settings/workspace 영속 계약에서 명시적으로 제외하며 v0.2 완료 조건이 아니다.
- [x] 일반 사용자 범위의 HKCU Run startup registration을 구현·자동 검증했다.
- [x] global shortcut 변경, 재등록과 registration failure 복구를 구현·자동 검증했다.
- [x] settings corruption, backup 복구와 missing shell fallback을 자동 검증했다.

완료 gate:

- 재시작 후 settings가 보존된다.
- theme 변경이 terminal session을 재시작하지 않는다.
- 잘못된 settings와 shell path 때문에 앱 전체가 종료되지 않는다.
- startup을 켜고 끌 때 관리자 권한이 필요하지 않다.

### Phase 4 — 안정화, virtual desktop와 배포

예상: 4~6시간 + 실제 환경 확인

- [x] 공식 `IVirtualDesktopManager` adapter와 no-op fallback을 구현한다.
- [x] 공식 API로 불가능한 pinning을 capability로 명확히 보고한다.
- [x] renderer process failure, shell crash와 ConPTY creation failure의 격리·재시작 경로를
  구현하고 자동 검증했다.
- [x] WebView2 Runtime 누락 error surface, offline standalone installer 안내와 재시도/종료
  경로를 구현·문서화했다.
- [x] DiagnosticLog rotation, bounded file과 command/output 개인정보 제외를 검증했다.
- [x] self-contained win-x64 Release publish를 만든다.
- [x] `THIRD-PARTY-NOTICES.md`와 license를 작성한다.
- [x] README에 현재 build/run과 제한을 작성한다.
- [x] `docs/test-plan.md` 결과를 실제 수행 상태로 갱신한다.
- **Blocked (manual):** hardware/display, IME, tray와 실제 WebView2 상호작용은 현재 automation
  권한으로 수행하지 못했다. 항목별 상태는 `docs/test-plan.md`의 MAN matrix를 따른다.
- [x] 미수행 manual test의 재현 절차와 `Blocked`/`Not run` 근거를 `docs/test-plan.md`에 기록했다.

완료 gate:

- clean restore/build/test/publish가 성공한다.
- release folder에서 renderer asset과 shell이 정상 동작한다. (기존 자동·격리
  기록; 이번 수동 배포 검증 완료 판단에서는 제외)
- portable 패키징, 오프라인 Release 실행, 업데이트와 rollback은 이번 수동
  검증 작업의 필수 범위와 완료 판단에서 제외한다. 기존 자동·격리 결과는
  보존 자료로만 남긴다.
- runtime network/telemetry가 없다.
- 자동 테스트와 실제 수행한 manual test 결과가 문서와 일치한다.
- 알려진 Windows API 위험과 virtual desktop 제약이 숨김없이 기록돼 있다.

## 검증 방법

### 자동 테스트

| 대상 | 주요 case |
|---|---|
| architecture boundary | production reference 방향, module 간 직접 참조 금지, public surface 제한 |
| `PanelGeometryCalculator` | taskbar 4방향, negative monitor coordinate, DPI 4종, height clamp |
| window policy reducer | idle/active, collapsed/expanded, auto-hide visible/concealed, fullscreen |
| `ShellResolver` | pwsh 우선순위, fallback, missing executable, custom arguments |
| `SettingsStore` | missing, partial, old schema, invalid JSON, atomic replace |
| theme | 필수 token, ANSI 16색, contrast와 JSON/resource validation |
| renderer bridge | v2 version/type/session ID validation, stale/unknown ID, malformed/oversized message, tab별 routing과 batching |
| terminal workspace | create/activate/close/restart state, 8개 상한, 마지막 tab 대체, 실패 격리와 stale callback |
| session cleanup | single 6초/전체 8초 deadline, 병렬 종료, timeout 뒤 handle 정리 |
| startup command | quoting, enable/disable와 executable path |
| virtual desktop | supported/fallback capability 반환 |

### Integration test

- ConPTY creation과 정상 종료
- `pwsh` prompt 출력
- `Set-Location` 뒤 위치 유지
- environment variable 유지
- resize
- Unicode/한글 round trip
- long-running command interrupt
- shell exit와 restart
- output drain과 bounded shutdown
- 두 ConPTY session의 PID, working directory, environment와 output 격리
- 한 session close/restart 뒤 다른 session round trip 유지
- 한 session launch/exit 실패 뒤 다른 session과 renderer 유지
- 최대 8개 session의 병렬 shutdown과 child process/handle 잔존 여부

UI/Explorer/display에 의존하는 test는 CI에서 불안정하면 자동 test로 위장하지 않고
manual test로 분리한다.

### Manual test

이번 작업의 수동 검증 완료 판단에서는 portable 패키징, 오프라인 Release 실행,
업데이트와 rollback을 제외한다. 기존 package·Release·격리 smoke 결과는 진행
기록으로 보존하되 배포 검증의 `Passed` 근거로 사용하지 않는다. 셸 선택·지속
세션과 renderer·셸·ConPTY 실패 후 host/session 유지 및 재시작 결과만 이번
범위에서 유효한 recovery 검증 결과다.

- launch focus preservation
- terminal click activation
- taskbar bottom와 auto-hide
- taskbar monitor 이동
- secondary monitor와 negative coordinate
- monitor disconnect/reconnect
- 100%, 125%, 150%, 200% scaling
- mixed-DPI 이동
- Explorer restart
- maximized application
- borderless fullscreen과 exclusive fullscreen
- virtual desktop switch
- PowerShell, Windows PowerShell, cmd와 WSL
- 한글 IME composition
- clipboard shortcut와 selection
- mouse/keyboard tab 생성, 순환, 전환, 닫기와 마지막 tab 대체
- tab 전환 뒤 cwd, history, foreground job, scrollback과 한글 IME 유지
- inactive tab의 long-running output 중 active tab 입력 응답성
- 한 tab shell crash/restart와 다른 tab PID/state 보존
- 8개 tab 상한의 disabled/error feedback
- renderer crash/retry 뒤 session 유지와 scrollback 비복원 안내
- expand/collapse hotkey conflict
- WebView2 renderer process failure
- startup login simulation
- self-contained release folder 실행

## 위험 영역과 fallback

| 위험 | 영향 | 기본 대응 | fallback |
|---|---|---|---|
| standard WebView2 opacity/transparent background 제약 | opacity와 rounded visual 저하 | Phase 1에서 early spike | opaque/tint opacity → 검증된 top-level opacity, composition은 opt-in |
| composition control monitor hot-plug crash | 앱 freeze/crash | default로 사용하지 않음 | standard WebView2 유지 |
| ConPTY synchronous pipe deadlock | terminal/app 종료 hang | input/output 별도 worker, bounded shutdown | child kill 후 pipe/HPCON 순차 close |
| 여러 ConPTY의 순차 종료 | tab 수에 비례한 app 종료 지연 | session별 6초 bounded cleanup을 병렬 실행하고 전체 8초 deadline 적용 | timeout session의 남은 handle을 강제 순서로 dispose하고 payload 없는 diagnostic 기록 |
| stale session message/callback | 닫거나 restart한 tab의 input/output이 다른 tab으로 오염 | host 발급 opaque ID, registry/state/instance identity 재검증 | message 폐기 후 해당 tab state snapshot 재전송 |
| 한 tab의 output 폭주 | 다른 tab 지연과 memory 증가 | session별 4MiB buffer, 64KiB batch, 최대 8 session | noisy session의 oldest output만 폐기하고 1회 diagnostic |
| renderer crash 또는 v1/v2 mismatch | 모든 tab UI 소실 또는 잘못된 routing | ConPTY session 유지, v2 snapshot으로 renderer만 재연결 | input 차단 WPF 오류 surface; 자동 v1 downgrade 금지 |
| permanent NOACTIVATE | click/IME 입력 불가 | 적용하지 않고 no-activate move만 사용 | `WM_MOUSEACTIVATE` 정책을 격리해 검토 |
| panel과 fullscreen 충돌 | 게임/영상 방해 | normal z-order 유지 후 필요 시 conceal | 사용자가 fullscreen conceal policy를 끌 수 있게 설정 |
| taskbar auto-hide animation event 누락 | panel이 stranded | event + reconciliation + 짧은 fast sampling | unknown 상태에서 last safe frame 사용 |
| secondary taskbar가 공식 API에 없음 | 특정 monitor 선택 제한 | system taskbar를 정본으로 사용 | class lookup adapter를 별도 capability로 제공 |
| virtual desktop pin 공식 API 부재 | 모든 desktop 표시 불가 | supported abstraction과 명시적 limitation | 향후 isolated undocumented adapter 검토 |
| WebView2 runtime 없음 | renderer 시작 실패 | runtime check와 local error | offline standalone installer 안내 |
| IME/shortcut 충돌 | 한글 입력 또는 Ctrl+C 오동작 | browser composition event와 selection-aware key policy | shortcut remap 설정 |
| strict analyzer/warnings-as-errors | 초기 build 마찰 | scaffold부터 warning 0 유지 | 실제 SDK bug만 좁은 suppression과 근거 기록 |
| local system dotnet에 SDK 없음 | build command 실패 | user-local SDK를 task 변수로 사용 | README에는 일반 SDK 설치 절차 제공 |
| module 과분할 또는 SharedKernel 오염 | 변경 추적과 경계가 오히려 복잡해짐 | 세 module만 시작하고 구현은 internal 기본 | 독립 수명·contract 없는 책임은 기존 module 안에서 통합 |

## 진행 기록

### 2026-09-01

- 원본 README, CLAUDE.md와 핵심 window/tracking/geometry source를 조사했다.
- ConPTY, taskbar, DPI, WebView2와 virtual desktop 공식 API 범위를 확인했다.
- local Windows/.NET/Node/WebView2/shell 환경을 확인했다.
- renderer 기본안을 standard WebView2 + xterm.js로 정했다.
- 제품 코드 구현 전 이 계획을 작성했다.
- 사용자 요구에 따라 단일 project/folder 경계안을 세 기능 module 기반의
  모듈러 모놀리스로 변경하고 `AGENTS.md`와 본 계획에 강제 규칙을 반영했다.
- 원본 `main` commit `f4136df`의 Swift source 전체를 분석해
  `docs/architecture-reference.md`에 portable UX와 macOS mechanism을 분리했다.
- 공식 Windows API와 renderer 결정을 `docs/architecture.md`에 고정하고
  automated/integration/manual matrix를 `docs/test-plan.md`에 작성했다.
- 모듈러 모놀리스 solution, 세 기능 module과 WPF composition root를 만들었다.
- WebView2 + bundled xterm.js renderer와 Windows ConPTY persistent shell을 연결했다.
- taskbar geometry, no-activate placement와 `Ctrl+Alt+E` 확장/복원을 구현했다.
- architecture/unit/integration test 32개와 실제 executable smoke test를 통과했다.
- 실제 executable에서 foreground focus 유지, 116px → work area 확장 → 116px 복원,
  `msedgewebview2.exe`, `conhost.exe`, `pwsh.exe` 자식 process 생성을 확인했다.
- `artifacts/Starboard-win-x64`에 196.5MiB self-contained Release를 만들고 같은
  renderer/ConPTY process tree로 실제 실행되는지 확인했다.
- notification icon의 표시/숨김에서 shell PID가 유지되고, `종료` 뒤 process가
  8초 이내 종료되는지 확인했다.
- 실제 `Ctrl+Alt+S`로 비활성 panel 호출과 활성 panel 숨김을 확인하고, 실제
  notification-area icon 좌표 클릭으로 숨긴 panel의 표시·foreground 복구를 확인했다.
- WPF host에도 WinForms framework 사용을 명시해 self-contained 배포에 tray
  runtime assembly가 누락되지 않게 했다.
- terminal shutdown continuation이 UI synchronization context를 캡처해 교착되던
  문제를 실제 tray 종료 smoke에서 발견하고 non-capturing 정리 경로로 수정했다.
- 한글 IME, taskbar auto-hide, mixed-DPI/multi-monitor, fullscreen과 settings UI는
  다음 phase의 구현·수동 검증으로 남겼다.

### 2026-09-03

- 다중 terminal tab을 v0.1 Phase 1.3으로 추가하고 기존 single-session 구현의
  `TerminalView`/`ConPtySession`/renderer bridge 책임을 실제 코드와 대조했다.
- tab collection은 Terminal module 내부, tab별 ConPTY는 infrastructure 내부,
  renderer는 view라는 경계를 확정하고 host/다른 module 변경을 기본 범위에서 뺐다.
- protocol v2 session routing, 오류 격리, 최대 8개, session별 6초 및 전체 8초
  bounded cleanup과 renderer crash fallback을 제품 코드보다 먼저 고정했다.
- `git worktree list --porcelain`에서 `feature/terminal-tabs`와
  `C:/PrivateProject/StarBoardWin-tabs`가 같은 기준 commit에 이미 연결된 것을 확인했다.
  기존 branch/worktree를 생성, 삭제, reset 또는 checkout하지 않았고 이후 제품 수정과
  build/test는 그 전용 worktree에서 수행하는 인수인계 경계를 유지한다.
- `terminal-session-tabs` 구현에서 ordered registry와 session coordinator를 추가하고
  `TerminalModule`이 coordinator, view가 renderer subscription을 소유하도록 기존
  단일 `TerminalView -> ConPtySession` 수명 경계를 분리했다.
- 마지막 tab은 close를 거부하는 이전 초안 대신 직접 할당 기준에 맞춰 새 기본
  `PowerShell N` tab을 즉시 생성·활성화하고 닫힌 transport만 bounded cleanup한다.
- unit test 25개와 ConPTY integration test 2개에서 선택/순환/인접 close, 8개 상한,
  launch/exit/restart 격리, stale callback, 병렬 deadline과 두 실제 PowerShell의
  서로 다른 PID 및 environment/cwd/history/background job 독립성을 검증했다.
- Debug solution restore/build와 전체 test 51개를 통과했고 build warning과 error는
  없었다. task에 지정된 Terminal unit/ConPTY integration 명령도 추가 옵션 없이
  각각 25개와 2개 통과했다.
- 최종 통합 검증에서는 실제 ConPTY session을 3개로 확장해 서로 다른 PID,
  environment, working directory와 history를 확인하고 첫 session의 exit/restart/close
  뒤 두 활성 session의 상태가 유지되는지 재검증했다. targeted input, 비활성 renderer
  pane/scrollback 보존 구조, 3-session bounded shutdown과 module reference/public
  surface도 자동 test에 포함했다.
- 2026-09-03 최종 Debug build는 경고 0개·오류 0개, 전체 83개 test가 통과했다.
  기본 병렬 MSBuild/test가 진단 없이 실패하거나 정체되는 환경 문제는 build server를
  끄고 `-maxcpucount:1`로 제한해 재검증했다.
- 검증 worktree는
  `C:/PrivateProject/.ai-worktrees/20260902-054931-690910-444810d6/verification-docs-and-commit`,
  검증 시작 HEAD는 `16a3699`다. orchestrator 계약에 따라 이 worktree에서는 commit,
  merge와 push를 수행하지 않는다. orchestrator가 최종 diff를
  `[클라이언트, doubleh86] - 다중 터미널 탭 구현`으로 commit한 뒤 hash를 기록하고,
  별도 후속 단계에서만 `feature/terminal-tabs`를 main에 merge해야 한다.

### 2026-09-10 — Windows platform 수동 검증 시도

- Windows 11 Pro build 26200 x64, RTX 3060과 동일한 LG ULTRAGEAR 1920×1080
  monitor 2대를 확인했다. primary는 X=0, 왼쪽 secondary는 X=-1920이며 두 화면의
  work area는 Y=0부터 높이 1032px로 48px bottom taskbar 영역을 남기며 per-monitor
  DPI는 각각 96(100%)다.
- foreground 기준 app인 Rider와 Calculator의 Windows UI 제어가 승인되지 않았고,
  Starboard는 terminal application 자동화 금지 대상이다. 명령 세션에서도 interactive
  Explorer의 `Shell_TrayWnd`가 HWND `0x0`으로 반환돼 panel rectangle, focus와 z-order를
  우회 관찰할 수 없었다.
- 따라서 실제 bottom/negative-coordinate/100% 장비 존재만 근거로 기록하고 MAN-001,
  MAN-003~MAN-022를 통과로 올리지 않았다. 각 항목은 장비 또는 권한 제약과 재개 조건을
  `docs/test-plan.md`에 `Blocked`로 기록했다. 특히 taskbar 비겹침, background focus 보존,
  exclusive fullscreen 비강제 표시는 이번 실행에서 검증되지 않았다.

### 2026-09-10 — release recovery 수동 검증

- 기존 바탕화면 배포본 PID 102920과 폴더를 유지한 채 ignored worktree 출력에 이전
  commit `a225da3b9c34ea0a264c095dbb73404efeb20b6e`와 current
  `456049b862b6bfdf60c58722a2c223fe680c1d62`를 각각 499-file 격리본으로 준비했다.
  single-instance mutex 문자열만 같은 길이의 test 이름으로 계측해 이전→현재→이전
  smoke를 서로 다른 PID에서 모두 exit code 0으로 실행했고, absent settings/workspace와
  HKCU Run 값 및 기존 배포 PID가 전후 동일함을 확인했다.
- current package는 Release build 경고·오류 0개, 전체 301개 test, ZIP/SHA-256과 추출
  smoke를 통과했다. hash는
  `29222a0d423870430cd3a2b1b67f668ba85cef80e51658fd085e96d27372b546`다.
- 실제 renderer PID만 종료한 뒤 계측 host와 PowerShell PID가 유지되고
  `RenderProcessExited`가 기록됐으며, shell PID만 종료한 뒤에도 host와 `ShellExit`가
  유지됐다. 실제 오류 UI, retry와 다른 tab 상태는 terminal UI 자동화 제한으로
  확인하지 못했다.
- PowerShell 7, Windows PowerShell과 cmd는 각각 별도 단일 process probe에서 환경과
  working directory를 명령 사이에 유지했고 두 PowerShell은 history도 유지했다.
  Starboard 화면 resize와 cmd Unicode는 미검증이다. WSL/custom shell은 v0.1 선택 계약에
  포함되지 않는다.
- WebView2 Runtime 누락 child override는 기대한 오류/설치 안내를 일관되게 관찰하지
  못했고 실제 network-disabled UI, 설정이 존재하는 update와 enabled startup 경로 복귀도
  남았다. 상세 상태와 재개 조건은 `docs/test-plan.md` MAN-024~027, 030, 032, 039, 040을
  정본으로 따른다.

## 미결정 사항

다음 항목은 사용자 결정을 요구하는 blocker가 아니라 Phase 1/2에서 실제 검증해
확정할 기술 항목이다.

1. standard WebView2에서 background-only opacity가 안정적으로 가능한가?
2. borderless standard WebView2를 mixed-DPI monitor 사이에서 옮길 때 text가
   즉시 선명하게 다시 render되는가?
3. Windows 11 auto-hide taskbar의 실제 reveal/conceal geometry event를
   WinEvent hook만으로 충분히 받을 수 있는가?
4. secondary taskbar adapter를 v0.1에서 노출할 만큼 안정적으로 검증할 수 있는가?
5. fullscreen 감지 시 demote와 conceal 중 어느 정책이 게임/영상에서 더 자연스러운가?

각 항목은 architecture를 무단 변경하지 않는 범위에서 더 안전한 fallback을
기본값으로 선택한다. composition control 또는 undocumented virtual desktop API처럼
위험도가 크게 바뀌는 선택이 필요하면 비교안을 먼저 보고한다.

## 완료 요약

v0.1의 제품 구현과 portable 배포 경로는 완료됐고 이후 v0.2 작업에서 settings, workspace,
saved tabs, WSL launch profile, failure recovery, virtual desktop fallback과 진단 로그를 보강했다.
과거 Phase 2~4의 미체크 항목 중 코드·자동 검증이 존재하는 항목은 2026-09-16 현재 상태로
정정했다.

실제 WebView2 입력, 한글 IME, tray/settings 화면, multi-monitor·mixed-DPI, taskbar edge/auto-hide,
fullscreen/focus, Explorer 재시작과 virtual desktop UI는 구현 미완료가 아니라 환경 제약으로
검증하지 못한 manual matrix다. 통과로 간주하지 않으며 최신 상태와 재개 절차는
[테스트 계획](../test-plan.md)을 정본으로 사용한다. 현재 사용자 확인 후 남은 제품 수정은
[탭 추가 메뉴와 오른쪽 메뉴 위치 회귀 수정](2026-09-16-tab-strip-menu-correction.md)에서 관리한다.
