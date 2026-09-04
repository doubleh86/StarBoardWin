# Starboard for Windows

Starboard는 Windows 작업표시줄 바로 위에 계속 머무는 작은 terminal panel이다.
호출할 때만 나타나는 drop-down terminal과 달리, 탭별 실제 shell session을 앱
수명 동안 유지하면서 현재 사용 중인 창의 focus를 불필요하게 빼앗지 않는 것을
목표로 한다.

현재 구현은 v0.1의 Phase 1 vertical slice다. Windows 11 x64를 우선 지원하며
Windows 10 1809 이상은 best-effort 대상이다.

## 현재 동작

- taskbar monitor의 work area 하단에 borderless/tool-window panel 배치
- `pwsh.exe` → `powershell.exe` → `cmd.exe` 순서의 shell 탐색
- Windows ConPTY를 통한 실제 양방향 persistent session
- bundled xterm.js와 local-only WebView2 renderer
- 하나의 WebView2 안에서 탭별 xterm, scrollback과 shell 상태 유지
- 최대 8개 탭, 탭별 독립 ConPTY process·working directory·interactive state
- terminal resize를 ConPTY cell size로 전달
- shell/renderer 오류 surface와 shell restart
- `Ctrl+Alt+E` global hotkey로 work area 전체 확장/축소
- `Ctrl+Alt+S`로 가려졌거나 숨겨진 panel 호출, 활성 panel 숨김
- 32 DIP 탭 바 아래 terminal 본문 약 8행이 보이는 200 DIP 기본 높이
- notification area icon 왼쪽 클릭으로 panel 표시·활성화
- tray menu의 `터미널 표시/숨기기`와 `종료`
- 평소에는 다른 앱을 덮어두지 않는 normal z-order, tray 표시 요청 때만 활성화
- selection-aware `Ctrl+C`, `Ctrl+Shift+C` 복사와 `Ctrl+V`, `Ctrl+Shift+V` 붙여넣기
- single instance와 1초 taskbar geometry reconciliation
- 사용자별 JSON 설정 model과 Dark, Light, One Dark, Tokyo Night theme catalog

아직 Phase 2 이후인 auto-hide 동기화, fullscreen 억제, multi-monitor hot-plug,
settings UI, 자동 시작과 virtual desktop 보강은 구현되지 않았다. 자세한 범위는
[`docs/plans/2026-09-01-windows-starboard-v01.md`](docs/plans/2026-09-01-windows-starboard-v01.md)를
참고한다.

탭은 앱을 다시 시작하면 복원되지 않는다. 비활성 탭은 DOM에서 제거하지 않아
10,000줄 xterm scrollback과 shell 상태를 유지하지만, renderer process 자체가
재시작되면 과거 xterm scrollback은 복원하지 않고 살아 있는 ConPTY의 이후 output과
현재 탭 snapshot만 다시 연결한다. 실제 mixed-DPI, 한글 IME와 3-tab WebView2 조작
smoke는 아직 수동 검증이 필요하다.

## 요구 사항

- Windows 11 x64 권장
- .NET 10 SDK `10.0.301` 이상(빌드 시)
- Microsoft Edge WebView2 Evergreen Runtime
- PowerShell 7 권장; 없으면 Windows PowerShell 또는 `cmd.exe` 사용

일반 실행에 관리자 권한은 필요하지 않는다.

## 빌드와 실행

```powershell
dotnet restore Starboard.Windows.sln
dotnet build Starboard.Windows.sln --configuration Debug --no-restore
dotnet test Starboard.Windows.sln --configuration Debug --no-build --no-restore
dotnet run --project src/Starboard.Windows/Starboard.Windows.csproj
```

이 개발 PC에서는 system `dotnet` 대신 다음 SDK가 확인돼 있다.

```powershell
& 'C:/Users/round1studio_14/.dotnet/dotnet.exe' build Starboard.Windows.sln --configuration Debug
```

관리자 권한이나 별도 .NET Runtime 없이 실행할 수 있는 x64 폴더는 다음 명령으로
생성한다.

```powershell
dotnet publish src/Starboard.Windows/Starboard.Windows.csproj `
  --configuration Release --runtime win-x64 --self-contained true `
  --output artifacts/Starboard-win-x64 -p:PublishSingleFile=false
& ./artifacts/Starboard-win-x64/Starboard.exe
```

WebView2 Evergreen Runtime은 self-contained .NET publish와 별도로 필요하다.

## Renderer 갱신

일반 build와 runtime에는 Node.js나 network가 필요 없다. xterm.js source 또는
package version을 바꿀 때만 다음 명령으로 committed `dist`를 다시 만든다.

```powershell
Set-Location src/Modules/Starboard.Modules.Terminal/Presentation/Renderer
npm ci
npm run build
```

renderer는 runtime CDN, 외부 font, remote script를 사용하지 않는다.

## 입력 규칙

| 입력 | 동작 |
|---|---|
| terminal click | Starboard를 의도적으로 활성화하고 IME/키보드 입력 허용 |
| tray icon 왼쪽 클릭 | shell session을 유지한 채 panel 표시·활성화 |
| tray icon 오른쪽 클릭 | `터미널 표시/숨기기`, `종료` menu 표시 |
| `Ctrl+C` | 선택이 있으면 복사, 없으면 shell에 ETX 전달 |
| `Ctrl+Shift+C` | 선택 text 복사 |
| `Ctrl+V`, `Ctrl+Shift+V` | Windows clipboard text 붙여넣기 |
| `Ctrl+Shift+T` | 새 terminal 탭 열기 |
| `Ctrl+Tab`, `Ctrl+Shift+Tab` | 다음/이전 terminal 탭 선택 |
| `Ctrl+Shift+W` | 현재 terminal 탭 닫기 |
| `Ctrl+W` | shell에 그대로 전달 |
| `Ctrl+Alt+E` | collapsed/expanded geometry 전환 |
| `Ctrl+Alt+S` | 숨김·비활성 panel 호출, 활성 panel 숨김 |

`Ctrl+Alt+E` 또는 `Ctrl+Alt+S`가 다른 프로그램에 이미 등록돼 있으면 앱은 계속
실행되지만 해당 global shortcut은 사용할 수 없다. 충돌은 로컬 진단 로그에
기록한다.

## 로컬 데이터와 개인정보

```text
%LOCALAPPDATA%/Starboard/
  settings.json
  settings.json.bak
  Logs/starboard.log
  WebView2/
```

Starboard에는 analytics, telemetry, crash upload, remote configuration이 없다.
terminal command, output, clipboard 내용과 environment 값은 로그에 남기지 않는다.
로그는 subsystem, operation, 복구 가능성에 필요한 오류 종류만 기록한다.

## 구조

Starboard는 단일 프로세스와 단일 배포 단위를 유지하는 모듈러 모놀리스다.

```text
Starboard.Windows
  ├─ Starboard.Modules.Terminal
  ├─ Starboard.Modules.DesktopIntegration
  ├─ Starboard.Modules.Preferences
  └─ Starboard.SharedKernel
```

기능 module끼리는 직접 참조하지 않는다. Host만 module을 조합하며 architecture
test가 reference 방향과 public surface를 검사한다. 설계 근거는
[`docs/architecture.md`](docs/architecture.md), 원본 프로젝트 분석은
[`docs/architecture-reference.md`](docs/architecture-reference.md)에 있다.

## 라이선스

third-party package와 배포 고지는 [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)에
정리돼 있다. 제품 참고 대상은 [palamim/starboard](https://github.com/palamim/starboard)이며,
원본 Swift 코드나 asset을 기계적으로 복사하지 않았다.
