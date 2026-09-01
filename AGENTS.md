# Windows용 Starboard 저장소 작업 지침

## 적용 범위

이 지침은 저장소 전체에 적용한다.

이 저장소는 [palamim/starboard](https://github.com/palamim/starboard)의 제품
콘셉트와 사용 경험을 Windows에 맞게 재구현한다. Swift/AppKit 코드를 C#으로
기계적으로 포팅하지 않는다. 원본의 제품 의도와 상호작용 원칙을 보존하되,
문서화된 Windows API와 Windows다운 UX로 구현한다.

사용자는 코드를 직접 작성하지 않는다. 에이전트가 분석, 구현, 의존성 선정,
문서화, 빌드, 테스트와 합리적인 범위의 디버깅을 책임진다. 사소한 선택을
반복해서 묻지 말고 적절한 기본값을 선택한다. 제품 방향을 크게 바꾸거나,
되돌리기 어려운 작업이거나, 사용할 수 없는 장비·사용자 권한이 필요하거나,
요청 범위를 확장해야 할 때만 사용자에게 확인한다.

## 작업 규칙과 문서 확인 순서

이 저장소의 작업 흐름과 C# 규약은 `C:/Work/TRK-Server`의 검증된 규칙을
Starboard에 맞게 조정한 것이다. 이 저장소에 복사한 규약을 정본으로 사용하며,
외부 저장소가 계속 존재한다고 가정하지 않는다.

문서는 다음 순서로 확인하고, 현재 작업에 필요한 문맥을 확보하면 더 읽지 않는다.

1. 루트 `AGENTS.md`
2. 구현, 계획, Git, 검증 규칙이 필요하면 `docs/development-workflow.md`
3. C# 또는 XAML을 수정하기 전에 `docs/code-style.md`
4. 현재 작업과 직접 관련된 아키텍처, 테스트 또는 계획 문서

공통 작업 규칙은 다음과 같다.

- 불확실한 동작을 추측해서 구현하지 않는다. 로컬 코드, 원본 소스 또는 문서화된
  플랫폼 동작을 먼저 조사하고 중요한 가정은 문서에 남긴다.
- 수정 줄 수가 아니라 요구사항을 완결하는 가장 작은 논리적 범위를 기준으로
  변경한다. 관련 없는 리팩터링, 포맷팅, 파일 이동 또는 의존성 추가를 하지 않는다.
- 직접 영향받는 선언, 호출부, 계약과 테스트를 함께 확인한다. 필요한 연쇄 변경은
  범위에 포함하되 관련 없는 정리는 포함하지 않는다.
- 사용자의 기존 변경을 보존한다. 인접한 기존 미사용 코드를 발견했다는 이유만으로
  삭제하지 않는다.
- 관련 파일과 symbol은 `rg`로 먼저 찾는다. 작업 전에 저장소 전체나 모든 문서를
  읽지 않는다.
- 새 기능, 아키텍처 변경, 의존성 변경, 여러 파일에 걸친 리팩터링은 제품 코드를
  수정하기 전에 `docs/plans/`의 계획 문서를 작성하거나 갱신한다.
- 구현 도중 계획이나 아키텍처를 크게 바꿔야 한다는 사실을 발견하면 먼저 계획을
  갱신한 뒤 작업을 계속한다.

## 제품 정의

Windows용 Starboard는 Windows 작업표시줄 바로 위에 항상 존재하는 실제
persistent terminal panel이다. 호출했다가 숨기는 Quake 스타일 terminal이 아니라
바탕화면의 고정 구성 요소다.

다음 우선순위로 최적화한다.

1. 사용 중인 애플리케이션과 focus를 방해하지 않는다.
2. 실제 interactive shell session을 계속 유지한다.
3. 작업표시줄, monitor, DPI와 Explorer 변경에 Windows답게 대응한다.
4. renderer, shell과 platform 실패에서 정상적으로 복구한다.
5. Windows가 허용하는 범위에서 원본 프로젝트의 정신을 유지한다.

## UX 불변 조건

- 작업표시줄이 있는 monitor에서 작업표시줄 바로 위에 얇은 borderless terminal
  panel을 배치한다.
- 작업표시줄을 덮지 않는다. panel이 보인다는 이유만으로 desktop work area를
  예약하거나 변경하지 않는다.
- 창 재배치, display 변경과 background event가 현재 foreground application의
  focus를 빼앗지 않아야 한다.
- 사용자가 terminal 내부를 의도적으로 클릭하면 정상적으로 활성화되고 입력을
  받을 수 있어야 한다.
- exclusive fullscreen application 위에 panel을 강제로 덮지 않는다. 게임과
  영상에서 불쾌한 always-on-top UX를 만들지 않는다.
- 하나의 shell process를 계속 유지해 working directory, environment, history,
  job과 interactive state가 명령 사이에 보존되게 한다.
- expand 단축키를 누르면 현재 monitor의 usable work area까지 확장한다. 다시
  누르면 이전 collapsed geometry를 정확히 복원한다.
- 사용자가 입력 중이거나 panel이 확장된 동안에는 taskbar auto-hide 때문에
  panel이 사라지지 않아야 한다.
- pixel과 DIP 변환을 명시적으로 처리한다. 100%, 125%, 150%, 200% 배율과
  mixed-DPI monitor 조합에서 geometry가 정확해야 한다.
- Windows 11을 우선 지원한다. 정확성을 해치거나 복잡도가 지나치게 증가하지 않는
  범위에서 Windows 10도 호환한다.

## 기본 기술

- .NET 10
- nullable reference types를 활성화한 C#
- WPF
- x64 우선 지원
- managed API만으로 부족한 부분에 한정한 Win32 interop
- terminal backend는 Windows ConPTY
- 기본 shell 탐색 순서는 `pwsh.exe`, `powershell.exe`, `cmd.exe`

새 ANSI parser나 terminal emulator를 직접 구현하지 않는다. 유지보수되는 terminal
component를 비교하고 선택 근거를 `docs/architecture.md`에 기록한다. terminal,
Unicode, IME와 유지보수 요구를 만족하는 성숙한 .NET native component가 없으면
로컬에 asset을 포함한 WebView2와 xterm.js 조합을 우선 대안으로 사용한다.
renderer asset은 완전한 offline 실행을 지원해야 하며 runtime에 CDN에서 script,
font 또는 style을 불러오지 않는다.

의존성은 적고, 유지보수되고, license가 호환되며, 도입 근거가 있어야 한다.
third-party asset과 license를 기록한다. 원본 프로젝트는 MIT license지만 구현을
기계적으로 복사하지 않는다. 실제로 재사용한 코드나 asset에는 attribution을
보존한다.

## 아키텍처 경계

Starboard는 **단일 프로세스·단일 실행 파일·단일 배포 단위**를 유지하는
모듈러 모놀리스로 구현한다. 폴더와 namespace만 나눈 형태가 아니라, module을
별도 class library project로 분리해 project reference 단계에서 경계를 강제한다.

초기 module은 `Terminal`, `DesktopIntegration`, `Preferences` 세 개로 제한한다.
새 module은 독립적인 책임·수명·public contract가 있고 기존 module에 넣는 것보다
경계가 명확할 때만 추가한다.

```text
src/
  Starboard.Windows/                         # executable, composition root와 WPF shell
  Starboard.SharedKernel/                    # 최소 공통 contract만 허용
  Modules/
    Starboard.Modules.Terminal/
      Contracts/
      Application/
      Domain/
      Infrastructure/
      Presentation/
    Starboard.Modules.DesktopIntegration/
      Contracts/
      Application/
      Domain/
      Infrastructure/
        Interop/
    Starboard.Modules.Preferences/
      Contracts/
      Application/
      Domain/
      Infrastructure/
tests/
  Starboard.ArchitectureTests/
  Starboard.Modules.Terminal.Tests/
  Starboard.Modules.DesktopIntegration.Tests/
  Starboard.Modules.Preferences.Tests/
  Starboard.IntegrationTests/
docs/
```

다음 경계를 지킨다.

- `Starboard.Windows`는 유일한 composition root다. module 생성, application
  lifecycle 순서, 최상위 WPF window와 module 간 조정만 소유하며 terminal,
  Win32 또는 settings 구현을 두지 않는다.
- production 참조 방향은 `Starboard.Windows -> Modules -> SharedKernel` 한
  방향이다. module끼리 직접 참조하지 않고 순환 참조를 허용하지 않는다.
- module 간 협력은 host가 각 module의 public contract를 조정하는 명시적 호출과
  event로 처리한다. service locator, 전역 mutable singleton 또는 범용 in-process
  event bus를 기본 구조로 도입하지 않는다.
- 각 module은 contract, 구현, native adapter, asset과 수명 정리를 스스로
  소유한다. 외부에 필요한 contract와 module 진입점만 `public`으로 두고 구현은
  기본적으로 `internal`로 둔다. 다른 module의 내부 namespace나 concrete type을
  참조하지 않는다.
- `SharedKernel`은 둘 이상의 module이 같은 의미로 사용하는 작고 안정적인
  primitive와 cross-cutting contract만 둔다. 편의를 위한 `Utils`, module DTO,
  business rule 또는 platform 구현을 넣는 dumping ground로 사용하지 않는다.
- `Contracts`, `Application`, `Domain`, `Infrastructure`, `Presentation` 폴더는
  실제 책임이 있을 때만 만든다. 구조를 맞추기 위한 빈 layer나 forwarding
  abstraction은 만들지 않는다.
- architecture test에서 project reference 방향, 금지된 cross-module 참조와
  module public surface를 검증한다.
- desktop/window P/Invoke 선언, window message, native handle과 constant는
  `DesktopIntegration/Infrastructure/Interop`에 둔다. ConPTY interop는 Terminal
  module의 `Infrastructure/Interop`에 둔다. view와 view model에 Win32 호출을
  흩뿌리지 않는다.
- UI 코드는 service와 상태를 소비한다. ConPTY 수명이나 shell process 배관을
  직접 소유하지 않는다.
- terminal transport, terminal rendering과 shell selection을 각각 분리한다.
- taskbar와 monitor geometry 계산은 가능한 한 순수 함수로 만들어 테스트한다.
- virtual desktop 연동은 interface 뒤에 격리하고 supported implementation과
  fallback implementation을 분리한다.
- 문서화된 Windows API를 우선한다. undocumented COM API가 불가피하면 별도
  adapter로 격리하고, feature detection과 안전한 fallback을 제공하며,
  Windows update 위험을 문서화한다.
- 호출자가 하나뿐이고 platform/test boundary도 없는 사소한 추상화는 만들지
  않는다.

## Windows 플랫폼 규칙

focus, activation, z-order, taskbar, fullscreen window, virtual desktop과 ConPTY
정책을 확정하기 전에 실제 동작을 조사하고 검증한다.

- `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`, topmost 상태와 activation을 서로
  독립적인 영구 flag가 아닌 하나의 정책으로 다룬다.
- 창을 활성화하지 않고 재배치한다. 사용자가 terminal을 의도적으로 클릭했을 때
  활성화할 수 있도록 필요한 경우에만 style을 전환한다.
- 문서화된 shell/window notification을 받고 항상 최신 geometry를 다시 조회한다.
  `explorer.exe`가 작업표시줄을 다시 생성한 뒤에도 복구한다.
- 초기 UI가 일반적인 bottom taskbar에 최적화되더라도 bottom, top, left, right
  taskbar geometry를 모두 고려한다.
- primary monitor가 작업표시줄을 소유한다고 가정하지 않는다.
- per-monitor DPI 변경은 cached system DPI가 아니라 해당 window message가
  제공하는 DPI를 사용한다.
- 공식 virtual desktop API가 지원 기준이다. 모든 desktop에 pin하기 위해
  깨지기 쉬운 undocumented interface가 필요하다면 pinning은 optional 기능으로
  둔다.
- fullscreen 정책은 macOS와의 완전한 동작 일치보다 사용자 비방해를 우선한다.

관찰한 동작, 선택한 정책과 fallback을 `docs/architecture.md`에 기록한다.
검증하지 않은 가정을 사실처럼 문서화하지 않는다.

## Terminal 규칙

- ConPTY는 명령마다 process를 만드는 방식이 아니라 계속 실행되는 shell
  process를 host해야 한다.
- 양방향 stream, resize event, cancellation, 정상 종료와 shell failure 이후
  restart를 지원한다.
- UTF-8을 명시적으로 사용하고 한글 입출력을 테스트한다. renderer 선택과 연동에서
  IME composition을 핵심 요구사항으로 다룬다.
- interactive terminal application에 필요한 control sequence를 손실 없이
  전달한다.
- copy/paste와 interrupt shortcut을 명확히 정의하고 테스트한다. `Ctrl+C`,
  `Ctrl+V`, `Ctrl+Shift+C` 충돌을 의도적으로 해결한다.
- 기본 로그에 command 내용이나 terminal output을 기록하지 않는다.
- ConPTY 또는 renderer가 실패해도 앱 전체를 종료하지 않는다. 유용한 로컬 오류를
  보여주고 restart 경로를 제공한다.
- 기본 shell 동작을 먼저 안정화한다. WSL, Git Bash와 custom executable은 MVP를
  복잡하게 만들지 않으면서 추가할 수 있는 구조로 설계한다.

## 설정과 자동 시작

초기 설정 model은 다음 항목을 포함한다.

- shell
- theme
- collapsed terminal height
- font family와 size
- opacity
- 로그인 시 자동 실행
- preferred monitor behavior
- 기본값이 `Ctrl+Alt+E`인 expand shortcut

간단한 local JSON 또는 가벼운 per-user 저장소를 사용한다. 설정 파일이 없거나,
일부 값만 있거나, 이전 schema여도 읽을 수 있어야 한다. 쓰기 도중 중단돼도
손상된 파일을 남기지 않는다. 자동 시작은 일반 사용자 권한 범위의 HKCU 또는
지원되는 per-user startup 방식을 사용하며 일상적인 실행에 관리자 권한을
요구하지 않는다.

최소 Dark, Light, One Dark와 Tokyo Night theme를 제공한다. terminal ANSI
palette와 WPF 주변 색상을 하나의 일관된 theme definition으로 관리한다.

## 구현 순서

각 phase 종료 시 애플리케이션을 build하고 실행할 수 있는 상태로 유지한다.
제품 코드를 수정하기 전에 `docs/plans/README.md`에 정의된 구현 계획을 만들고
범위, 영향 파일, 위험, 검증 방법과 진행 상태를 기록한다.

### Phase 0 — 원본 분석과 결정

- 원본 저장소를 분석하고 `docs/architecture-reference.md`를 작성한다.
- platform-independent 제품 개념과 macOS 전용 구현을 분리한다.
- renderer 후보를 비교하고 Windows architecture 결정을 기록한다.

### Phase 1 — Vertical slice

- solution과 WPF application을 만든다.
- single monitor에서 작업표시줄 위에 borderless panel을 표시한다.
- ConPTY를 통해 실제 PowerShell session을 시작하고 유지한다.
- interactive terminal output을 render하고 resize를 지원한다.

### Phase 2 — Windows 동작

- per-monitor DPI와 mixed scaling을 정확히 처리한다.
- taskbar/monitor 변경과 hot-plug event를 따른다.
- taskbar auto-hide와 Explorer restart에 대응한다.
- expand/collapse와 의도적인 focus/activation 정책을 구현한다.

### Phase 3 — 제품 기능

- settings, theme, shell selection, startup과 shortcut 설정을 추가한다.
- 기본 shell 경로가 안정화된 다음에만 WSL/custom shell 지원을 추가한다.

### Phase 4 — 안정화

- virtual desktop best-effort 지원과 fullscreen 정책을 추가한다.
- 모든 복구 경로를 검증하고 문서, packaging과 테스트를 완료한다.

핵심 terminal과 windowing vertical slice가 완성되기 전에 선택 사항인 Phase 3과
Phase 4 작업을 앞당기지 않는다.

## 코드 작성 규칙

- `docs/code-style.md`를 코드 스타일 정본으로 사용한다. 명시적 boolean 비교,
  중괄호, early return, 여러 줄 인자 정렬, naming과 파일 배치 규칙을 따른다.
- 짧은 최신 문법보다 의도가 즉시 읽히는 명시적인 C#을 사용한다. .NET SDK
  analyzer를 활성화한다.
- UI thread에서 blocking process I/O나 오래 걸리는 native 호출을 실행하지 않는다.
- background loop에 cancellation을 전달하고 종료 시간을 제한한다.
- process, pseudoconsole handle, pipe, event subscription과 WebView resource를
  결정적으로 해제한다. 소유한 native handle은 가능한 경우 `SafeHandle`을
  사용한다.
- native 반환값을 검사하고 문제 해결에 유용한 Win32 오류 정보를 보존한다.
- 빈 `catch`를 사용하지 않는다. 예상 가능한 복구 오류는 local log에 남기고
  적절한 경우 사용자에게 상태로 표시한다.
- monitor/taskbar geometry와 settings snapshot에는 immutable record 또는 작은
  value type을 우선 사용한다.
- 명확하지 않은 Windows 동작과 선택 이유를 주석으로 남긴다. 코드 한 줄씩
  설명하는 주석은 작성하지 않는다.
- runtime 동작은 기본적으로 deterministic하고 offline이어야 한다.
- 현재 변경으로 미사용 상태가 된 private helper, field, using과 test는 같은
  작업에서 제거한다. 기존의 관련 없는 코드는 별도 요청 없이 삭제하지 않는다.

## 빌드와 검증

solution 생성 후 의미 있는 변경마다 최소한 다음을 검증한다.

```powershell
dotnet restore
dotnet build --configuration Debug
dotnet test --configuration Debug
```

먼저 영향받는 가장 작은 project 또는 test target을 검증하고, 완료 보고 전에는
적용 가능한 전체 suite를 실행한다. 실패한 명령은 원인을 확인하지 않은 채
반복하지 않는다. geometry, settings, shell resolution, theme parsing과 state
transition은 자동화된 테스트를 추가한다. CI에서 안정적으로 실행할 수 있다면
ConPTY lifecycle integration test도 추가한다.

`docs/test-plan.md`를 유지하고 최소한 다음 항목을 다룬다.

- bottom taskbar와 taskbar auto-hide
- primary/secondary taskbar monitor
- monitor disconnect/reconnect
- 100%, 150% DPI와 가능한 경우 mixed-DPI
- maximized/fullscreen application
- virtual desktop switching
- Explorer restart
- terminal resize와 persistent working directory
- long-running command와 `Ctrl+C`
- clipboard shortcut
- Unicode와 한글, IME composition
- expand/collapse와 focus 보존
- shell, renderer와 ConPTY failure/restart

실제 hardware 시나리오를 수행하지 않았다면 테스트했다고 보고하지 않는다.
pending으로 표시하고 실행 방법을 설명하며 automated, simulated, manual 결과를
구분한다.

## 필수 문서

구현과 함께 다음 문서를 최신 상태로 유지한다.

- `docs/development-workflow.md`: 작업 범위, plan-first 흐름, 검증, 생성물과 Git
  규칙
- `docs/code-style.md`: C#, XAML, async, error handling, interop과 test style
- `docs/plans/*.md`: 날짜별 구현 계획과 완료 인수인계
- `docs/architecture-reference.md`: 원본의 목적, lifecycle, terminal, windowing,
  Dock/Space 동작, settings, theme, permission과 macOS 전용/공통 개념 구분
- `docs/architecture.md`: Windows architecture, dependency 결정, focus/z-order
  정책, taskbar/DPI 전략, virtual desktop fallback, security와 알려진 위험
- `docs/test-plan.md`: 반복 가능한 automated/manual 검증 matrix
- `README.md`: 사전 요구사항, build, run, install, settings, 제약과 privacy 동작

## 보안과 개인정보

- analytics, telemetry, crash upload, remote configuration과 runtime network
  request를 사용하지 않는다.
- 사용자가 향후 명시적으로 기능을 활성화하지 않는 한 terminal command와 output을
  외부로 전송하거나 저장하지 않는다.
- third-party renderer와 library가 telemetry나 remote asset loading을 수행하는지
  검토한다.
- 일반 실행에 관리자 권한, Accessibility에 준하는 권한 또는 광범위한 filesystem
  권한을 요구하지 않는다.

## 완료 조건

release는 다음 조건을 모두 만족해야 완료로 본다.

- 지원 Windows 환경에서 build되고 실행된다.
- ConPTY를 통해 실제 persistent shell이 동작한다.
- panel이 taskbar를 덮거나 불필요하게 focus를 빼앗지 않으면서 taskbar를 따른다.
- expand/collapse가 동작하고 multi-monitor/DPI의 기본 동작을 검증했다.
- shell과 renderer failure가 애플리케이션 전체를 종료하지 않는다.
- architecture, test plan, build/run/install, third-party notice, 알려진 제약과
  Windows API 위험 문서가 실제 구현과 일치한다.
- 완료한 테스트와 수행하지 못한 manual scenario를 구분해서 보고한다.
