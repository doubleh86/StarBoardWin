# 잠금 해제 후 패널 너비 복구

## 현재 상태

2026-09-29의 stale collapsed frame 보정은 구현·자동 검증 완료다. 실제 잠금/해제 검증은
MAN-056의 미수행 상태를 유지한다. 2026-10-14 RDP 해제 후 로컬 PC 복귀 때 발생한다는 같은 형태의
재발 제보를 아래 `panel-rdp-width-recovery` 후속 Task로 추가했다. **부분 구현·호스트 연결 범위 확인 대기**이며,
기존 외곽 geometry 보정만으로 WebView2 내부 너비까지 복구됐다고 해석하지 않는다.

### 2026-10-14 실행 계획

실제 RDP 조작은 이 실행 환경에서 제공되지 않는다. 원인은 미확정이며, 확인 가능한
경계에 크기 관측과 조건부 복구를 추가한다. 현재 코드에는 동일 HWND 크기의 내부
불일치 검사와 WTS 알림 수명이 없고, xterm fit은 workspace ResizeObserver에 의존한다.
DesktopIntegration에서 현재 세션 WTS 등록/해제와 client pixel/window DPI 관측을 소유한다.
중복 알림은 5초 창으로 병합하며 기존 1초 재조정을 fallback으로 유지한다.
WPF DPI가 최신 HWND DPI와 일치할 때만 client pixel을 DIP로 변환한다.
Terminal presentation은 WPF host/control와 DOM CSS 폭을 비교하고 불일치에만 layout/fit을
복구한다. 동일 크기/DPI의 보정은 최대 5회이며 renderer와 shell을 재시작하지 않는다.
DOM 진단은 숫자 bounds/DPR만 수집한다. 실제 RDP 및 배율 관찰은 별도 수동 matrix에 남긴다.
영향: DesktopIntegration contract/runtime/interop, Terminal presentation, 해당 회귀 테스트와
architecture/test-plan/index. 실제 host 경로가 지정 목록과 달라 Shell/MainWindow와
Composition/AppCoordinator의 범위 포함 여부를 질의했으며 응답 전 이 파일은 수정하지 않는다.
검증: Task의 집중 .NET 테스트, npm ci/test/build, 전체 restore/build/test, 정렬과 diff 검사.

## 목표와 완료 조건

PC를 잠갔다가 해제한 뒤 가끔 패널의 탭 바와 terminal viewport/세로 scrollbar가
화면의 왼쪽 일부 폭에서 끝나고 오른쪽이 비는 제보를 해결한다. 정상적인
작업표시줄 monitor geometry가 돌아오면 collapsed 패널은 현재 work area의
전체 가로 폭을 다시 채운다. 현재 높이·실행 중인 셸·focus는 유지한다.

이 작업은 완료된 [탭 UI의 renderer 폭 수정](2026-09-16-tab-strip-menu-correction.md)
후에도 나타난 별도 재발이다. 당시 renderer CSS 수정의 성공이나 실패를
이번 잠금 해제 증상만으로 단정하지 않는다.

## 근거와 위험

- 사용자 제보(2026-09-29): 잠금/해제 후 간헐적으로 오른쪽 빈 영역이 생긴다.
  앞선 화면에서는 약 1900px 화면 중 탭/viewport가 약 1040px에서 끝났다.
  잠금 해제 시점의 실제 HWND·WebView2·DOM bounds는 아직 측정하지 못했다.
- `DesktopIntegrationModule.Reconcile`은 최신 geometry를 조회하지만
  engagement hold에서는 `LastSafeCollapsedBounds`를 복원한다.
  `SelectRestoredOrLatestBounds`의 유효성 검사는 예전 사각형이 현재 monitor
  work area **안에 있는지**만 검사한다. 예전 폭이 좁아졌어도 새 work area 안에
  들어오면 그대로 유지될 수 있다. 이는 코드상 가능한 원인이지, 아직
  현장 재현으로 확정된 원인은 아니다.
- display/setting 변경과 1초 주기 재조정은 존재하나 세션 unlock 전용
  경로는 현재 확인되지 않았다. 일시적인 taskbar unknown, monitor 전환,
  DPI/작업 영역 변경과 renderer resize 지연을 구별해야 한다.
- 안전한 geometry 재계산에도 창 활성화·최상단 고정·desktop work area 예약,
  shell 재시작을 사용하지 않는다. 작업표시줄이 확인되지 않는 동안에는
  기존 안전 frame을 유지하되 정상 snapshot이 돌아오면 폭을 복구한다.
- 코드 경계 확인(2026-09-29): WPF `RendererHost`는 별도 폭을 지정하지 않고
  WebView2 control도 고정 폭이 없다. renderer는 `html/body`와 workspace를
  가로 100%로 구성하고 workspace의 `ResizeObserver`가 xterm fit을 호출한다.
  반면 DesktopIntegration의 축소 frame 복원은 좁은 사각형을 그대로 HWND에
  전달할 수 있다. 실제 HWND·WebView2·DOM 측정 전에는 renderer 결함으로
  확정하지 않고 Terminal 변경을 보류한다.

## 범위와 영향 파일

- 우선 `DesktopIntegrationModule`, panel geometry/state/policy 및 해당
  module 테스트를 조사한다. 필요 시 display/session 알림 adapter를
  DesktopIntegration의 `Infrastructure`에 둔다.
- WPF 창 client bounds와 WebView2 child/DOM viewport를 실제로 비교해
  외부 창만 좁은지, renderer만 좁은지 분리한다. 후자인 경우에만 Terminal
  view/renderer fit 경로를 이 계획에 추가하고 먼저 범위를 갱신한다.
- 구현 영향 파일: `src/Modules/Starboard.Modules.DesktopIntegration/DesktopIntegrationModule.cs`,
  `tests/Starboard.IntegrationTests/DesktopWindowIntegrationTests.cs`,
  `docs/architecture.md`, `docs/test-plan.md`. 2026-09-29 구현은 기존 1초 재조정과 display,
  settings, DPI, Explorer 알림으로 snapshot 복구를 확인하므로 session unlock
  전용 알림을 추가하지 않았다. RDP 후속의 알림 보강 여부는 아래 별도 Task에서 판단한다.
- auto-hide로 taskbar가 concealed여도 snapshot 자체가 정상적으로 추적되면
  입력 중인 frame의 taskbar 쪽 anchor와 두께를 유지하면서 평행축을 현재
  work area 전체로 늘린다. 이 방향의 edge·DPI가 이전 frame과 맞지 않으면
  현재 설정 높이로 재계산한다. unknown/fallback에서는 현재 work area 안의
  안전 frame만 그대로 유지한다.
- 저장 탭 현재 디렉터리 변경은 [별도 계획](2026-09-10-saved-terminal-tabs.md)에
  맡긴다. 파일 소유권이 겹치지 않는 한 두 작업은 독립적으로 진행할 수 있다.

## 구현 단계

- [x] WPF/WebView2/DOM 크기 전달 경로와 DesktopIntegration의 저장 frame
  복원 경로를 코드로 비교했다. 실제 잠금 전후 HWND·WebView2·DOM/scrollbar
  bounds 비교는 수행하지 못했으며 MAN-056에 남겼다.
- [x] stale collapsed frame의 유효성을 현재 taskbar monitor/edge/work area와
  비교하도록 보강한다. 유효한 높이 조절값은 유지하면서 잘못된 가로 폭은
  fresh geometry로 회복하고, unknown 상태의 안전 fallback은 보존한다.
- [x] `좁은 안전 frame → 넓은 정상 work area`와 engagement hold,
  taskbar unknown→tracked, 다중 monitor·DPI, lock/unlock 반복의 회귀
  테스트를 추가한다. 실제 잠금/해제 관찰은 자동 테스트와 별개로 기록한다.

## 검증 방법과 진행 기록

- DesktopIntegration 집중 테스트 뒤 같은 변경의 solution restore/build/test를
  수행한다. 실제 Windows에서 잠금/해제 전후 panel/client/WebView2/viewport
  너비·scrollbar 끝, taskbar 비겹침, foreground HWND와 shell PID를 확인한다.
  재현되지 않거나 interactive desktop 조작이 불가하면 [MAN-056](../test-plan.md)에
  `Not run`으로 남기고 수정 완료의 수동 근거로 주장하지 않는다.
- [x] 2026-09-29: 제보 조건과 코드상 stale frame 경로를 확인해 계획을 작성했다.
  원인 확정·제품 수정·실제 잠금/해제 검증은 아직 수행하지 않았다.
- [x] 2026-09-29: 마지막 안전 frame이 현재 work area 안에 들어온다는 사실만으로
  전체 폭이 유효하다고 판단하던 경로를 수정했다. visible/tracked에서는 현재
  monitor·edge·work area·DPI와 설정된 DIP 높이를 기준으로 재계산한다.
  concealed/tracked에서는 안전한 taskbar 쪽 위치와 두께를 지키며 평행축을
  전체 work area로 확장한다. unknown/fallback에서는 현재 work area 안의
  마지막 안전 frame을 유지하고, monitor 제거로 벗어나면 연결된 monitor의
  fallback frame을 사용한다. renderer source와 재생성 dist에는 내용 변경이 없다.
- [x] 2026-09-29: 관련 integration 11개, DesktopIntegration 112개,
  솔루션 자동 테스트 537개 통과. 솔루션 restore, 단일 MSBuild 작업자 Debug
  build(경고·오류 0개), renderer build, C# 정렬, `git diff --check`를 확인했다.
  기본 병렬 solution build는 MSB3491 출력 경로 `Access denied`로 실패했고
  단일 작업자로 같은 solution을 검증했다. 실패 경로의 파일 잠금 또는 권한 원인은
  확인되지 않았다. `-m:1`은 작업자 경합을 줄이는 검증 방법이며 근본 원인
  해결로 단정하지 않는다. 실제 잠금·해제, panel/WebView2/DOM
  rect, scrollbar 위치, foreground HWND와 shell PID 관찰은 MAN-056 `Not run`이다.
- [x] 2026-09-29 재개 검증: 기존 DesktopIntegration 구현과 GEO-009/010 회귀
  테스트를 보존했다. 집중 테스트는 기본 병렬 실행이 복원 후 진행되지 않아
  중단했으며 `-m:1` 재실행에서 112개 통과했다. renderer build는 sandbox의
  `esbuild` 실행이 `spawn EPERM`으로 막혀 같은 명령을 제한 밖에서 실행해
  통과했다. 솔루션 restore는 sandbox에서 출력 없이 종료 코드 1이었고 제한
  밖의 동일 명령은 통과했다. 이어서
  `dotnet build Starboard.Windows.sln --configuration Debug --no-restore -m:1`은
  경고·오류 0개, 전체 테스트 537개 통과했다. 이번 실행에서 MSB3491은
  재발하지 않았으므로 기존 접근 거부의 정확한 실패 경로와 파일 잠금·권한 원인은
  여전히 미확정이다. 재발 시 경로와 점유 프로세스·ACL을 먼저 확인한다.
  실제 잠금 해제 및 HWND·WebView2·DOM 경계 측정은 MAN-056 `Not run`이다.

## 후속 — RDP 전환 후 창과 내부 viewport 너비 불일치 (2026-10-14)

### 제보와 확인 범위

- 사용자 제보: RDP 연결 후 발생하는 것 같다는 관찰이다. 첨부
  `codex-clipboard-45c41ff2-38be-4b76-a869-cd9975248721.png`에서는 패널 배경은 약 1900px 폭으로
  보이지만 탭 바의 `▾`와 terminal 세로 scrollbar는 약 1040px에서 끝나고 오른쪽이 비어 있다.
  추가 사용자 확인(2026-10-14): **RDP를 끊고 원래 로컬 PC로 돌아온 뒤**에 이 화면이 발생한다.
  원격 접속 직후·접속 중에도 발생하는지는 미확인이므로 정상이라고 단정하지 않는다.
  임시 이미지 파일의 영구 존재를 전제하지 않고 이 관찰을 텍스트 근거로 보존한다.
- 탭 바와 terminal이 함께 좁다는 점은 단순 shell 출력·ConPTY 열 수 문제가 아니라 상위
  WPF/WebView2 viewport 크기 전달 문제도 조사해야 한다는 단서다. 실제 HWND/child/DOM rect와
  DPI를 측정한 결과는 아니며 스크린샷만으로 원인 또는 실패 계층을 확정하지 않는다.
- 현재 코드: `WM_DISPLAYCHANGE`·`WM_DPICHANGED`·설정 변경과 1초 재조정은 처리한다.
  DPI 메시지 handler는 `false`를 반환해 WPF 기본 처리를 막지 않고 MainWindow에서도 지연 refresh를
  예약한다. 메시지를 삼킨다는 가정을 원인으로 사용하지 않는다.
- 2026-09-29 보정은 DesktopIntegration의 안전 frame 복원 경로다. WPF/WebView2에는 고정 child 폭을
  지정하지 않지만, RDP 연결/해제 전용 WTS 등록·알림과 내부 bounds 불일치 복구 경로는 현재 없다.
  renderer의 ResizeObserver는 받은 workspace 크기를 기준으로 xterm fit을 호출하므로 상위 viewport가
  이전 폭이면 fit만 반복해도 전체 화면 폭으로 늘어나지 않을 수 있다.
- 후보: 전환 중 monitor/work area snapshot 또는 DPI가 임시 값인 경우, native 창과 WPF arrange가
  서로 다른 크기를 갖는 경우, WebView2 child/controller 또는 DOM viewport가 이전 크기로 남는 경우.
  우선 원격 화면에서 로컬 monitor로 복귀할 때 이전 원격 크기·배율이 내부에 남는지 비교한다.
  실제 측정으로 구분하고 기존 geometry 수정을 되돌리거나 renderer를 무조건 재생성하지 않는다.

### 공식 API 근거와 복구 방향

- [WM_WTSSESSION_CHANGE](https://learn.microsoft.com/en-us/windows/win32/termserv/wm-wtssession-change)는
  remote/console 연결·해제, unlock 등 세션 전환을 구분한다. 필요 시 DesktopIntegration에서
  [WTSRegisterSessionNotification](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsregistersessionnotification)의
  `NOTIFY_FOR_THIS_SESSION`으로 현재 세션만 등록하고, detach/dispose 때 대응하는 해제를 수행한다.
  등록 실패는 local 진단과 기존 display/주기 재조정 fallback으로 처리하며 관리자 권한을 요구하지 않는다.
- 알림 도착이 geometry 안정화를 보장한다고 가정하지 않는다. 현재 monitor/work area/taskbar/DPI를
  다시 조회하고 UI dispatcher 적용 뒤의 크기를 확인한다. 중복 알림은 합치고 재시도는 횟수·시간을
  제한한다. 예전 크기를 강제로 적용하거나 무한 layout/resize loop를 만들지 않는다.
- [WM_DPICHANGED](https://learn.microsoft.com/en-us/windows/win32/hidpi/wm-dpichanged)의 새 window DPI,
  WPF DIP·native pixel·DOM CSS pixel·WebView2 scale을 명시적으로 비교한다. 과거 system DPI 또는
  RDP/로컬의 이전 monitor 값으로 이중 scaling하지 않는다.
- 측정 결과 외곽 HWND가 좁으면 DesktopIntegration geometry를, WPF/child bounds가 좁으면 host→
  Terminal presentation의 resize 전달을, DOM만 정상인데 xterm만 좁으면 renderer fit을 보정한다.
  필요한 경계만 수정하고 module 간 직접 참조·UI의 새 Win32 호출은 추가하지 않는다.
- 복구는 활성화·최상단 고정·panel expand/collapse·앱 재시작 없이 수행한다. 높이·taskbar anchor,
  숨김/확장 상태, active ID·shell PID·실행 중 작업·입력·scrollback을 보존한다. 사용자가 패널을 숨긴
  상태에서 RDP 알림 때문에 다시 보여주지 않는다. renderer 교체와 terminal reset은 기본 해법이 아니다.

### 독립 Task: `panel-rdp-width-recovery`

- 제목: RDP 해제 후 로컬 복귀 시 패널과 WebView2 가로 폭 복구
- 상태: **부분 구현·호스트 연결 범위 확인 대기** (2026-10-14 실행). 실제 RDP 원인은 미확정이다.
- capabilities: `desktop`, `frontend`, `qa`; 위험도: 중간.
- 선행: 완료된 잠금 해제 geometry 보정 및 현재 renderer 가로 fill 계약을 보존한다.
- 우선 조사/예상 영향: DesktopIntegrationModule, `Infrastructure/Interop`의 세션 notification adapter와
  수명 처리, DesktopWindowIntegrationTests 및 해당 module 테스트. 측정으로 필요함을 확인한 경우에만
  host 조정(MainWindow/AppCoordinator), TerminalView의 WebView2 크기 전달, renderer src/dist와 UI 테스트를
  포함한다. 변경 경계를 확정한 뒤 코드를 수정한다.
- 문서: 이 계획, 계획 index, `docs/test-plan.md`, 필요한 architecture 복구 정책.
- 제외: 다른 기능·drag controller 수정, 저장 schema·shell bootstrap 변경, 새 dependency,
  원격 PC/RDP 설정 변경, 강제 로그오프, commit/push·release·바탕화면 재배포.
- 파일 소유권: 진단과 계층 구분을 먼저 수행한다. 그 뒤 수정 파일이 독립적일 때만 native 알림·geometry와
  Terminal layout을 병렬로 나눈다. MainWindow/TerminalView/renderer 공유 파일은 담당자를 하나로 고정한다.
- [x] 로컬 코드와 공식 API에서 크기 전달 경계 및 지원되는 관측/보정 수단을 조사했다.
  실제 RDP 재현·측정과 원인 확정은 수행하지 않았으며 RDP-MAN-01~07에 남겼다.
- [x] 현재 세션 WTS 등록/해제·5초 병합·기존 fallback, native client/window DPI contract,
  Terminal presentation의 숫자 관측·조건부 measure/UpdateWindowPos/fit과 5회/5초 제한을 구현했다.
- [ ] HWND→WPF 연결: 지정된 `src/Starboard.Windows/MainWindow.xaml.cs`가 존재하지 않고 실제 파일은
  `src/Starboard.Windows/Shell/MainWindow.xaml.cs`다. AppCoordinator도 `Composition/`에 있다.
  Task의 "Change only expected paths" 규칙 때문에 실제 경로 수정 범위를 질의했으며 응답 대기다.
  현재 `PanelLayoutCheckRequested` 소비자가 없어 native client→WPF root 보정은 아직 연결되지 않았다.
- [x] 중복/역순/지연 알림, 임시 geometry, 동일 외곽 폭의 내부 mismatch, DPI·zoom·focus·renderer
  객체 보존의 집중 회귀를 추가했다. 전체 검증 결과는 아래 실행 기록에 별도로 기록한다.

### 검증과 미수행 항목

- 제어된 재현: 로컬 정상 상태 → 다른 해상도/배율 RDP 연결 → 원격 세션 크기 변경 → 해제 → 로컬 복귀,
  **해제 후 로컬 화면의 폭 복구**를 우선 확인한다. 같은 해상도/배율 재연결 및 반복도 나눠 확인한다.
  로컬 복귀 전후 세션 알림·display/DPI 변경·layout 적용의 실제 순서를 기록하되 항상 같은 순서로
  도착한다고 가정하지 않는다. 단일/다중 monitor, engaged/idle, 확장/축소,
  사용자 숨김과 auto-hide를 구분한다. 연결과 복귀를 같은 event로 뭉뚱그리지 않는다.
- 각 단계에서 native window/client rect, WPF ActualWidth/ActualHeight와 해당 window DPI,
  RendererHost/WebView2 크기·scale, DOM window.innerWidth/workspace/viewport rect·scrollbar 끝을 비교한다.
  진단은 크기·DPI·event 종류·제한된 timing만 기록하고 명령·출력·탭 이름·경로·원격 계정은 기록하지 않는다.
- 인수: 정상 geometry가 돌아온 뒤 탭 바와 viewport가 패널 가용 폭을 채우고 scrollbar가 오른쪽 끝에
  위치한다. 100/125/150/200%에서 clipping·taskbar 겹침 없이 높이·foreground·PID·작업 상태를 보존한다.
  실제 RDP를 수행하지 못한 자동/mock 테스트는 실제 화면 통과 근거가 아니다.
- **Not run (manual):** 이번에는 RDP 연결/해제나 HWND/WebView2/DOM 경계 측정을 수행하지 않았다.
  [MAN-056](../test-plan.md)의 기존 잠금 해제 검증을 유지하고 별도 RDP-MAN-01~07 matrix를 추가했다.
  제보를 실제 재현·원인 확정·수정 완료로 표시하지 않는다.

### 2026-10-14 실행 검증과 인수인계

- DesktopIntegration 집중 테스트 120개, DesktopWindowIntegrationTests 집중 13개 통과.
  최초 기본 병렬 .NET 실행이 restore 이후 진행하지 않아 취소하고 `-m:1 -nr:false`를 사용했다.
  기존 SDK analyzer가 test 이름의 밑줄을 CA1707로 거부해 기존 테스트의 PascalCase 이름을 따랐다.
- npm ci 성공 (새 의존성 없음), renderer 테스트 76개와 renderer build 통과. sandbox에서는
  esbuild spawn EPERM이므로 동일 npm test/build를 제한 밖에서 재실행했다.
  새 recover-layout parser는 빈 global payload만 허용한다. 기존 drag source/test와 fixture는 수정하지 않았다.
- 지정 .NET executable로 solution restore, Debug build (경고/오류 0), 전체 테스트 585개 통과.
  restore는 sandbox에서 출력 없이 코드 1로 종료되어 동일 명령을 제한 밖에서 재실행했다.
  전체 test도 renderer bridge의 esbuild EPERM 한 건으로 실패한 뒤 동일 suite를 제한 밖에서 통과했다.
  build/test는 `-m:1 -nr:false`를 사용했고 `--no-restore`/`--no-build`는 선행 성공 뒤에만 사용했다.
- C# WorkingTree 정렬 검사와 git diff --check 통과. 실제 host 경로는 수정하지 않았으며
  Preferences, Terminal Infrastructure, drag controller/test, fixture에 content diff가 없다.
  초기 license 파일의 line-ending dirty 상태를 보존했다. commit/merge/push/deploy는 수행하지 않았다.
- **남은 구현:** 실제 host 경로를 Task scope에 포함한 뒤 MainWindow에서 layout check를 UI dispatcher로
  병합하고 최신 client/window DPI와 WPF DPI를 비교한다. root 크기 불일치만 최대 5회/5초 보정한다.
  close/dispose에서 구독·예약 작업을 해제하고 root 불일치/동일 외곽 크기 회귀를 추가한다.
  `LayoutRecoveryRevision`은 5초 이내 session burst에서 동일하며 기존 주기 check는 활성화하지 않는다.
  해당 변경 후 영향받는 focused 테스트와 전체 build/test를 다시 실행한다.
  현재 Task는 호스트 연결을 기다리는 **부분 구현**으로, 전체 acceptance 완료가 아니다.

### Terminal 공개 점검 진입점 (2026-10-15)

- Task `terminal-layout-recovery-entrypoint`: TerminalModule에 revision만 받는 공개 요청 계약을
  추가하고 기존 TerminalView 숫자 관측·조건부 measure/UpdateWindowPos/`recover-layout`에 연결한다.
  host 연결, DesktopIntegration, shell transport, renderer protocol/asset은 이번 범위에서 변경하지 않는다.
- 영향: TerminalModule, Contracts, TerminalView와 LayoutRecovery, RendererLayoutRecovery,
  Terminal/Integration/Architecture 테스트 및 architecture/test-plan/계획 index.
- 양수의 단조 증가 revision은 module 수명 안의 복구 묶음이다. 같은 값과 낮은 값은 무시한다.
  정상 관측·target 변경·주기 관측은 예산을 새로 열지 않는다. 새 revision만 최대 5회/5초를 연다.
  dispatcher에는 최신 요청 하나, 관측 중에는 최신 후속 revision 하나만 남긴다.
- 위험: 숨김/Unloaded/실패/교체/종료 전 관측 결과가 뒤늦게 적용되는 경우. 수명 epoch와
  renderer identity/generation, visibility 재검사로 차단한다. WebView2 script timeout은 실제 script를
  취소하지 못하므로 완료 전 새 script를 누적하지 않는다. 무시한 revision은 다시 재생하지 않는다.
- [x] 공개 계약·dispatcher 병합·관측 수명·revision 예산 구현과 집중 회귀 테스트.
- [x] 지정 Terminal/Architecture 테스트, solution restore/build/test, C# 정렬 및 diff 검사.
- 실제 RDP·화면/foreground/PID 검증은 RDP-MAN-01~07 `Not run (manual)`을 유지한다.

#### 완료·검증 기록

- 공개 호출 예: `terminalModule.RequestLayoutCheck(new TerminalLayoutCheckRequest(revision));`
  TerminalView 구현 타입이나 다른 module 참조 없이 worker thread에서 호출할 수 있다.
  관측 중 새 revision은 이전 결과를 버리고 후속 관측 하나로 합친다. 기존 숫자 script와
  조건부 보정·빈 `recover-layout` 메시지를 재사용하며 renderer/shell 재생성은 추가하지 않았다.
- 지정 .NET executable로 Terminal 376개, Architecture 14개 통과. solution restore 성공,
  Debug build `-m:1 -nr:false` 경고·오류 0개, 최종 지정 전체 test argv에서 **620개 통과**
  (Terminal 376, Architecture 14, DesktopIntegration 120, Preferences 37, Integration 73).
- 첫 sandbox Terminal 실행은 restore 뒤 진행하지 않아 중단했고, 단일 작업자 실행은 기존 renderer
  bridge의 esbuild `spawn EPERM` 한 건으로 실패했다(375개 통과). sandbox 밖 동일 지정 argv는
  376개 통과했다. 기본 병렬 solution build는 Terminal/Preferences WPF markup cache와 기존
  DesktopIntegration test coverage 생성물의 접근 거부로 실패했다. ACL에는 현재 계정 소유 및
  Authenticated Users Modify가 있었으며 정확한 점유/worker 원인은 미확정이다. 단일 작업자에서
  통과한 사실을 원인 해결로 단정하지 않는다.
- 첫 전체 suite는 기존 `powershell-bootstrap-windows`가 `powershell-first-input-written` 단계에서
  TaskCanceledException으로 실패했다(619/620). 관련 PowerShell 두 사례의 분리 재실행과 지정
  전체 suite 재실행은 통과했다. bootstrap 코드를 변경하지 않았고 최초 timeout 원인은 미확정이다.
- C# WorkingTree 정렬 및 `git diff --check` 통과. 지정 경로만 변경했으며 Preferences,
  Terminal Infrastructure, renderer source/dist/protocol에는 content diff가 없다.
  commit/merge/push/deploy는 수행하지 않았다. 실제 host 연결과 수동 RDP 인수는 별도 작업이다.

#### 재시도 점검 (2026-10-15)

- 기준 `a9f6c9d`와 현재 `7683092`를 대조했다. 기존 공개 계약·module 진입점과 구현을
  보존한다. 기존 완료 Task 및 위 성공·실패 기록은 당시 이력으로 유지한다.
- 누락된 검증: 기존 5회 제한 테스트는 여섯 번째 관측이 정확히 5초에 도달하므로 횟수 제한과
  시간 제한을 구분하지 못했다. 5초 이내의 반복으로 바꾸고, 관측 도중 취소 후 새 요청이
  기존 작업과 병합되는 수명 회귀를 보완한다. 제품 API/renderer/host 범위는 확장하지 않는다.
- 현재 Task 계정의 영향받는 bin/obj에서 생성·쓰기·삭제 probe와 기존 Terminal markup cache의
  배타적 read/write 열기가 성공했다. 읽기 전용 속성은 없고 ACL에는 Authenticated Users Modify가
  있다. 이 사실만으로 과거 접근 거부 원인이나 동시 빌드 충돌을 확정하지 않는다.
- 추가 권한 확인: Terminal bin/obj, Preferences obj, DesktopIntegration.Tests bin/obj의 파일에
  ReadOnly/reparse 항목이 없었다. 두 module의 markup cache 및 DesktopIntegration.Tests의
  AssemblyInfo/cache를 내용 변경 없이 배타적 read/write로 열었다. 별도 .NET compiler process도
  Terminal.Tests obj에 진단용 assembly를 작성했고 진단용 source/assembly 삭제가 성공했다.
  현재 검사로 일반적인 생성물 쓰기 제한이나 검사 시점의 해당 파일 잠금은 재현되지 않았다.
- WMI process 명령행 조회는 접근 거부로 실패했다. 읽기 전용 process snapshot에서는 다수의
  dotnet child와 이미 종료된 parent가 관측됐지만 해당 작업공간의 동시 빌드·worker 충돌을
  입증하지 못했다. 생성물 정리·ACL 변경·다른 process 종료·sandbox 밖 재실행을 하지 않았고,
  확인되지 않은 충돌을 근거로 `-m:1 -nr:false`를 적용하지 않았다.
- 이번 재시도는 **구현 보존·테스트 보완, 자동 검증 미완료**다. 실행 계정은
  `DESKTOP-BVS661A\\round1studio_14`이며 모든 SDK 명령에 지정 `.dotnet/dotnet.exe`를 사용했다.

| 필수 검증 | 이번 재시도 결과 |
|---|---|
| Terminal Debug test | restore 최신 상태 확인 뒤 build/test 출력 없이 대기하여 자체 실행을 중단. 통과 아님 |
| Architecture Debug test | exit 1, restore 단계 실패. diagnostic에서도 `_GetAllRestoreProjectPathItems` / `_GenerateRestoreProjectPathWalk`의 MSBuild task 실패, warning/error 0. 테스트 미실행 |
| solution restore | exit 1. diagnostic에서 `_FilterRestoreGraphProjectInputItems`의 MSBuild task 실패, warning/error 0. 생성물 접근 거부 메시지는 재현되지 않았으며 원인 미확정 |
| solution Debug build `--no-restore` | 이번 solution restore가 실패해 선행 성공 조건 불충족으로 미실행 |
| solution test `--no-build --no-restore` | 이번 Debug build가 없어 오래된 binary를 검증하지 않도록 미실행 |
| C# WorkingTree 정렬 | 통과 |
| `git diff --check` | 통과 |

- 진단 로그는 해당 테스트 project의 무시되는 obj 아래 `layout-terminal-verification.log`,
  `layout-retry-diagnostic.log`, `layout-restore-diagnostic.log`에 남겼다. 앞의 620개 성공 기록은
  이전 커밋의 이력이며 이번 변경의 검증 근거가 아니다. 변경 테스트의 실행 결과도 아직 없다.
  SDK 하위 작업 실패 원인을 확인한 뒤 지정 집중 테스트와 restore→Debug build→전체 test를
  다시 수행해야 한다. 실제 host 연결 및 RDP 수동 인수의 별도 범위는 유지한다.

#### 추가 재개 점검 (2026-10-15 14:35 KST 시작)

- 기준 `a9f6c9d` 이후 `7683092`의 공개 진입점·구현과 `af9aee5`의 테스트 보완 및 실패 기록을
  대조했다. 작업 트리는 clean이었다. 기존 제품 구현과 완료 Task 이력을 유지한다.
- 보완 범위는 Terminal 테스트에서 실제 dispatcher→revision 예산→진행 중 관측을 함께 연결해
  최신 요청만 후속 관측으로 합쳐지고 중복·지연 revision이 예산을 갱신하지 않는지 확인하는
  회귀다. 공개 계약·module 경계·renderer protocol·host 연결 범위를 바꾸지 않는다.
- 이번 Task 계정/환경에서 생성물 속성·ACL, 생성/쓰기/삭제, 기존 cache 배타적 열기와 읽기 전용
  process 조사를 별도로 수행한다. 과거 로그 시각의 실패를 이번 실패로 계산하지 않는다.
  일반 쓰기가 허용되지 않는 실행 환경이면 코드 수정·반복 build로 해결하지 않고 정상적인
  생성물 쓰기가 허용되는 실행 환경에서 재개해야 한다. 기존 생성물 정리, 다른 process 종료, 권한 변경,
  제한 우회는 수행하지 않는다. 동시 MSBuild 충돌이 입증되지 않으면 worker 옵션을 바꾸지 않는다.
- 검증은 지정 Terminal/Architecture 테스트 후 restore→Debug build→전체 test, 정렬·diff 검사다.
  선행 restore/build 실패 시 `--no-restore`/`--no-build` 단계는 미실행으로 명시한다.

이번 실행 결과 (KST, `af9aee5` 이후 변경에 대한 기록):

- 실행 계정은 `desktop-bvs661a\round1studio_14`다. Terminal module 및 Terminal.Tests의 네 bin/obj
  경로가 할당 작업공간 안임을 해석 후 확인했다. 디렉터리 ReadOnly 속성은 없고 소유자는 현재
  계정, ACL은 Authenticated Users Modify를 포함한다. 네 경로의 새 파일 생성·쓰기·배타적 열기·삭제와
  기존 Terminal markup cache의 내용 변경 없는 배타적 read/write 열기가 모두 성공했다.
  현재 일반 쓰기/삭제 제한과 해당 cache 잠금은 재현되지 않았다. 과거 거부 원인의 해소 증거는 아니다.
- process snapshot에는 dotnet 446개와 VBCSCompiler 1개가 있었다. WMI 명령행 조회는 접근 거부라
  해당 workspace의 동시 MSBuild 충돌을 확인하지 못했다. process 수만으로 원인을 확정하지 않았으며
  `-m:1 -nr:false`로 바꾸거나 다른 process를 종료하지 않았다. 생성물 정리·ACL 변경도 하지 않았다.
- 제품 구현은 추가 변경이 필요하지 않았다. 기존 contract/public surface·조건부 복구·수명 테스트를
  유지하고 `RendererLayoutRequestDispatcherTests`에 실제 STA dispatcher와 제어된 관측을 연결한
  회귀를 추가했다. 진행 중 revision 1을 100으로 대체하면 이전 결과는 적용되지 않고 후속 하나만
  관측하며, 중복/역순 요청과 주기 관측은 예산을 갱신하지 않고 새 revision만 예산을 여는지 확인한다.

| 필수 검증 | 이번 실행 시각과 결과 (2026-10-15 KST) |
|---|---|
| 지정 Terminal Debug test | 14:36:41 시작. 14:36:42 restore 최신 상태 출력 이후 진행 출력이 없어 14:37:43 자체 실행 취소(exit 1). 테스트 통과 아님 |
| 지정 Architecture Debug test | 14:37:56 시작. restore 확인 출력 이후 진행 출력이 없어 14:39:04 직전 자체 실행 취소(exit 1). 테스트 미실행 |
| 지정 solution restore | 14:39:04~14:39:12 exit 1, 상세 오류 출력 없음 |
| restore diagnostic 추가 조사 | 14:39:17~14:39:22 exit 1. `_FilterRestoreGraphProjectInputItems`의 MSBuild 하위 작업 실패, warning/error 0. 생성물 접근 거부나 동시 빌드 충돌 메시지는 없음 |
| solution Debug build `--no-restore` | 현재 solution restore 실패로 선행 조건 불충족, 미실행 |
| solution test `--no-build --no-restore` | 현재 Debug build 미성공으로 미실행. 과거 binary를 이번 변경 검증에 사용하지 않음 |
| C# WorkingTree 정렬 | 새 테스트의 인자 정렬 한 칸 수정 후 14:38 재실행 통과 |
| `git diff --check` | 통과 |

- 로그는 각각 Terminal.Tests obj의 `layout-terminal-20261015-1440.log`, ArchitectureTests obj의
  `layout-architecture-20261015-1438.log`, `layout-restore-20261015-1439.log`,
  `layout-restore-diagnostic-20261015-1439.log`다. 파일명은 이번 실행의 고유 구분용이며 실제 시각은
  위 표를 따른다. 이전 14:28 로그와 앞선 커밋의 성공/실패를 이번 실행 결과로 재사용하지 않았다.
- **구현 보존·회귀 보완, 자동 검증 미완료**다. 이번 조사로 실행 환경이 읽기 전용이라고 확정할
  수는 없지만 SDK 하위 작업 실패 원인도 해소되지 않았다. 정상적인 생성물 쓰기와 SDK 하위 작업
  실행이 허용되는 환경이 필요하다. 실행 환경 담당자가 원인을 확인·해소한 뒤 지정 집중 테스트 및
  restore→Debug build→전체 test를 재개한다. 같은 실패를 반복 build나 제품 코드 변경으로 해결하지
  않는다. 실제 host 이벤트 연결 및 RDP 수동 검증은 계속 별도 범위다.

#### 추가 실패 조사 (2026-10-15 17:58 KST 시작)

- 기준 `a9f6c9d61365c46d66ccc3faf0322491ecadc97c` 이후 `7683092`, `af9aee5`,
  `e2def41`의 diff·검증 기록을 대조했다. 시작 시 작업 트리는 clean이었다. 공개 revision 계약,
  TerminalModule 진입점, UI dispatcher, 조건부 보정과 취소 수명 및 기존 회귀 테스트를 보존했다.
  이번 변경은 실패 증거와 검증 상태 문서만 갱신한다. 확인되지 않은 원인을 제품 수정이나
  assertion 완화·테스트 제외로 처리하지 않는다.
- 이전 Terminal 로그의 수정 시각은 각각 14:28:26과 14:36:42 KST였으며 둘 다 restore 최신 상태
  출력까지만 있었다. 과거 접근 거부나 assertion을 이번 원인으로 사용할 상세 증거는 없었다.
  작업공간 쓰기 확인 후 지정 Terminal 명령을 한 번 실행하고, 상세 출력 부재를 보완하기 위해
  같은 명령에 `--verbosity diagnostic`만 추가해 조사했다.

| 필수 검증·조사 | 이번 실행 시각과 결과 (2026-10-15 KST) |
|---|---|
| 지정 Terminal Debug test | 17:58:04 시작. restore 최신 상태 이후 출력이 없어 17:58:47 자체 실행 취소(exit 1). 실패 테스트 이름·assertion·테스트 호스트 시작은 관측되지 않음 |
| Terminal diagnostic | 17:59:02 시작. 17:59:06.182 최초 실패 상태: MSBuild `BuildProject` / `_GetProjectReferenceTargetFrameworkProperties` 하위 작업. SharedKernel `GetTargetFrameworks`도 실패 상태이며 compiler 오류·호스트 시작·assertion 출력 없음. 요약 warning/error 0 이후 process가 종료하지 않아 18:00:01 자체 실행 취소. SDK 자체 종료 코드로 오인하지 않음 |
| 지정 Architecture Debug test | 18:00:01 시작. restore 프로젝트 확인 이후 진행 출력이 없어 18:01:00 자체 실행 취소(exit 1). 테스트 미실행 |
| 지정 solution restore | 18:01:00~18:01:08 exit 1. stdout/stderr에 상세 오류 없음 |
| restore diagnostic | 18:01:29~18:01:36 exit 1. 18:01:36.184 최초 실패 상태는 MSBuild `_FilterRestoreGraphProjectInputItems` 하위 작업, warning/error 0 |
| solution Debug build `--no-restore` | 현재 solution restore 실패로 선행 성공 조건 불충족, 미실행 |
| solution test `--no-build --no-restore` | 현재 Debug build 미성공으로 미실행. 이전 binary나 과거 620개 통과를 이번 검증으로 사용하지 않음 |
| C# WorkingTree 정렬 | 지정 `pwsh -NoProfile -File scripts/Test-CSharpAlignment.ps1 -WorkingTree` 통과 |
| `git diff --check` | 통과. 변경 경로는 지정 문서 3개뿐이며 제품·테스트와 금지 경로의 content 변경 없음 |

- 진단 로그의 앞선 `MSB4276` 문자열은 기본 SDK resolver의 workload locator 조회 결과이며,
  바로 뒤 workload SDK resolver에서 resolve된 기록이 있다. 이를 치명적 오류나 SDK 설치 손상
  근거로 단정하지 않는다. 실패 상태는 위 표까지 확인했으며 최초 치명적 오류 메시지와 근본 원인은 미확정이다.
- 동일 계정 `desktop-bvs661a\round1studio_14`에서 Terminal module/Terminal.Tests bin·obj
  네 절대 경로가 할당 작업공간 하위임을 확인했다. 생성·쓰기·배타적 read/write 열기·삭제가 모두
  성공했고 ReadOnly/reparse 파일은 0개였다. 기존 Terminal markup cache도 내용 변경 없이 배타적
  read/write로 열었다. 확인한 obj ACL의 소유자는 현재 계정이며 Authenticated Users Modify가 있다.
  검사 시점의 일반 쓰기와 해당 cache 잠금만 확인했으며 과거 거부 원인 해소를 뜻하지 않는다.
- process snapshot에는 dotnet 15개가 있었으나 WMI 명령행 조회가 접근 거부여서 이 작업공간의
  동시 빌드 충돌은 입증하지 못했다. 조사 API의 거부를 생성물 쓰기 거부로 해석하지 않는다.
  `-m:1 -nr:false`, 생성물 정리, 다른 process 종료, ACL/시스템 권한 변경이나 제한 밖 재실행은
  하지 않았다. restore 진단 출력 위치만 obj로 지정했으며 추가 failure 파일은 없었다.
- 이번 로그는 Terminal.Tests obj의 `layout-terminal-current.log`,
  `layout-terminal-current-diagnostic.log`와 ArchitectureTests obj의 `layout-architecture-current.log`,
  `layout-restore-current-diagnostic.log`다. 지정 restore는 상세 출력이 없어 실행 시각·exit만 위 표에
  기록했다. 이전 로그와 완료 Task 이력은 덮어쓰지 않았다.
- 상태는 **기존 구현·회귀 보존, 자동 검증 미완료**다. MSBuild 하위 작업 실패와 종료 대기의
  원인을 확인·해소한 뒤 지정 집중 테스트와 restore→Debug build→전체 test가 필요하다.
  제품 코드 결함이나 읽기 전용 환경을 확정할 수 없어 추가 테스트 변경도 하지 않았다.
  실제 host 연결 및 RDP-MAN-01~07의 `Not run (manual)` 상태는 유지한다.
