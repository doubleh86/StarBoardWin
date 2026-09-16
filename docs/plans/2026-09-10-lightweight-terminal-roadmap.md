# 가벼운 터미널 제품 방향과 후속 기능 후보

## 제품 방향 — 2026-09-10 사용자 결정

Starboard는 작업표시줄에 붙어 빠르게 명령을 입력하고 결과를 확인하는 작고 간단한
터미널이다. 큰 작업공간이나 IDE를 대체하는 방향으로 확장하지 않는다.
실제 배치는 기존 제품 원칙대로 작업표시줄 바로 위이며, 작업표시줄을 덮거나 작업 영역을
예약하지 않는다. 사용자의 ‘아래 붙어서’라는 표현은 화면 하단 밀착이라는 의도로 반영한다.

- **분할 화면은 추가하지 않는다.** 좌우·상하 split pane, 분할 비율 조절과 pane별 focus
  관리 모두 제품 범위에서 제외한다. 좁은 기본 패널에서 읽기·입력이 복잡해지는 것을 피한다.
- 여러 세션은 기존 탭으로 전환한다. 현재 확장/축소 기능은 유지하지만 화면 분할은 하지 않는다.
- 우선순위는 UI 안정화 → 저장한 탭 → 출력 검색이다. 매일 반복하는 동작을 줄이는 기능을 우선한다.
- 기능을 추가해도 기본 화면에 상시 도구막대를 늘리지 않고 작은 메뉴나 필요할 때 여는 화면을 쓴다.
- 명령 자동 실행, 내장 AI와 대규모 작업공간 관리는 현재 후속 범위에서 보류한다.
  별도 사용자 결정 없이는 분할 화면을 구현 후보로 다시 포함하지 않는다.

## 현재 계획과 후속 후보

이 문서는 제품 방향과 후보 우선순위다. 기능 구현·의존성 추가·오케스트레이터 실행 승인이
아니며 실제 착수 전 각 기능의 계약, 영향 파일과 인수 기준을 별도 계획으로 구체화한다.

| 우선순위 | 기능 | 사용자 경험과 범위 |
|---|---|---|
| UI 보정 | 설정 창 글자 대비 | 테마 배경과 기본 WPF control 상태에서 label·선택값이 흐리거나 검게 표시되지 않도록 foreground를 통일 |
| 먼저 | 탭 UI 안정화 | 좁은 이름 편집·닫기 영역 회귀를 해결하고 실제 WebView2에서 검증 |
| 다음 | 저장한 탭 | 이름·폴더·셸을 저장하고 골라 해당 폴더에 새 세션 시작 |
| 후속 1 | 출력 검색 | 현재 탭의 메모리 내 출력에서 검색, 이전/다음 결과 이동·강조, Escape 닫기 |
| 후속 2 | 파일·폴더 경로 드롭 | 탐색기에서 놓은 항목의 경로를 입력만 하고 자동 실행하지 않음 |
| 후속 3 | 출력 URL 열기 | Ctrl+클릭으로 허용된 웹 주소 열기, 일반 클릭·드래그는 텍스트 선택 유지 |
| 후순위 | 긴 작업 완료 알림 | 실제 명령 종료를 확인한 뒤 선택적으로 알림, 단순 출력 정적 상태로 추측하지 않음 |

- UI 정본: [TAB-UX-03 이름 편집 기획](2026-09-03-renderer-and-tab-ui.md).
- 저장한 탭 정본: [프로젝트 폴더에서 바로 시작](2026-09-10-saved-terminal-tabs.md).
- 출력 검색은 기존 scrollback 범위만 대상으로 한다. 로그 파일 저장, 전체 탭 통합 검색,
  renderer 재시작 이전 기록 복원은 초기 범위에서 제외한다. 검색 입력이 shell로 전달되면 안 된다.
- 경로 드롭은 셸별 quoting과 공백·한글·특수 문자 처리를 먼저 조사한다. shell prompt인지
  interactive application인지 불명확하면 입력 미리보기·확인 등 안전 정책을 확정한 뒤 구현한다.
  파일 내용을 읽거나 업로드하지 않으며 Enter·개행·명령 실행을 자동으로 붙이지 않는다.
- URL 열기는 사용자 명시적 동작에 한정한다. 초기 허용 scheme은 HTTP/HTTPS이며 command,
  file, custom protocol은 제외한다. 링크 자동 요청·미리보기·백그라운드 네트워크는 하지 않는다.
  링크가 보인다는 이유만으로 신뢰하지 않고 실제 대상 URL을 확인할 수 있어야 한다.
- 완료 알림은 기존 ‘새 출력’ 점과 별개다. 명령 시작·종료·exit code를 식별하는 셸 연동을
  조사하고 지원 셸·오탐 정책을 먼저 확정한다. 기본 꺼짐, focus 강탈 없음, 명령/출력 노출 없음이 기준이다.

## COMMAND-NOTIFY-01 — 명시적 명령 완료 신호와 알림 계약

### 목표와 범위

- 완료 판단은 출력 도착·정지 시간, prompt 문자열이나 shell process 종료를 사용하지 않는다. 초기 지원
  범위는 Starboard가 주입한 integration hook에서 별도 control channel로 시작과 종료/exit code를 모두
  보낼 수 있는 PowerShell 7과 Windows PowerShell이다. `cmd.exe`와 custom shell은 명시적 연동이
  확보되기 전까지 알림 미지원으로 남겨 오탐보다 누락을 선택한다.
- Terminal은 command execution ID와 runtime session ID/generation을 결합해 `Ready → Executing → Ready`
  수명을 추적한다. 같은 execution의 명시적 종료 신호에만 완료 event를 만들며 restart 이전 세대,
  중복·역순·불일치 신호는 알림을 만들지 않는다.
- Preferences의 `commandCompletionNotificationsEnabled`는 opt-in이고 기본값은 `false`다. Terminal의 완료
  event와 DesktopIntegration의 표시 요청은 host가 public contract로 변환한다. 어느 계약에도 command,
  output, prompt, working directory 또는 임의 사용자 문자열을 넣지 않는다.

### 영향 파일과 위험

- Terminal contract에는 민감한 본문 없는 execution ID, session reference, exit result와 완료 event를,
  Domain에는 명시적 integration signal만 소비하는 세대별 state machine을 둔다.
- DesktopIntegration contract에는 host가 현재 session generation을 재검증한 뒤 넘길 generic notification
  request를 둔다. 요청은 focus/activation 지시나 사용자 제공 표시 문자열을 받지 않는다.
- Preferences schema에는 기본 비활성화 값을 추가하고 missing/partial/이전 JSON을 defaults와 병합한다.
  설정 적용 중 실패하면 현재 유효 snapshot을 유지하며, 미확정 완료는 재시도하거나 추측하지 않는다.
- control channel 손실, 잘못된 순서, execution ID 불일치는 해당 generation의 tracker를 unavailable로
  만들고 진행 중 command를 폐기한다. 새 session generation만 깨끗한 tracker를 만든다. 핵심 위험은
  terminal output에 섞인 escape sequence를 신뢰해 생기는 오탐과 restart 후 늦은 event이므로 renderer
  output parser를 완료 판정 경계로 사용하지 않는다.

### 구현 및 검증

- [x] Terminal 완료 계약과 명시적 신호 state machine을 추가하고 정상/실패 exit code, 늦은 세대,
  중복·역순·연동 손실을 단위 테스트한다.
- [x] Preferences 기본값·이전/부분 JSON 호환과 opt-in transition, DesktopIntegration 표시 요청 계약,
  module 소유권 및 민감 문자열 부재를 테스트한다.
- [x] 관련 Terminal, Preferences, Architecture test project와 `git diff --check`를 통과시킨다. 실제 shell
  hook/control channel과 Windows notification UI 연결은 이 계약 확정 다음 구현 단계로 남긴다.

### 통합 단계 — completion-notification-integration

- [x] `TerminalModule`이 metadata-only 완료 event를 공개하고, module 내부 coordinator가 session ID와
  generation이 현재 수명인지 lock 아래 재검증하는 공개 진입점을 제공한다. restart·remove·shutdown으로
  폐기된 수명은 `false`이며 host는 reflection이나 renderer/output 분석을 사용하지 않는다.
- [x] composition root는 시작 시 적용 설정을 DesktopIntegration에 전달하고 Terminal 공개 event를
  구독한다. 완료 시 현재 generation을 다시 확인한 뒤에만 generic notification request로 변환하며,
  종료 시작 시 구독과 전달을 먼저 차단해 늦은 callback이 tray 수명보다 오래 남지 않게 한다.
- [x] 정상·실패·중복·늦은 완료, 설정 on/off, restart/remove/renderer recovery/shutdown 경계와 기존
  new-output 상태를 자동 검증한다. portable 검사는 사용자 설정·workspace·saved tabs·log·WebView2 data와
  command/output capture artifact가 포함되지 않는지 확인한다.
- [x] 지정된 Terminal test, solution restore/build/test, C# 정렬 및 diff 검사를 실행하고 실제 Windows
  notification, focus, 100/125/150/200% DPI는 수행 여부를 `docs/test-plan.md`에 자동 검증과 구분해 기록한다.

### 2026-09-15 PowerShell 초기화 입력 회귀

- 실제 배포본에서 새 PowerShell 7 탭이 내부 bootstrap 입력 뒤 `>>` 연속 입력 prompt에 머무는
  현상을 확인했다. 이는 단순한 내부 명령 노출이 아니라 첫 사용자 명령을 정상적으로 받을 수 없는
  P0 사용성 버그다.
- 현재 stdin bootstrap이 PowerShell 대화형 입력에 `\r\n`을 보내는 점과 shell이 prompt 입력 준비를
  마치기 전에 긴 encoded command를 주입하는 경계를 우선 조사한다. 원인은 추측으로 확정하지 않고
  실제 ConPTY/PSReadLine 재현 test로 고정한다.
- 수정 범위와 단일 오케스트레이터 Task는
  [PowerShell bootstrap 준비 상태 회귀 계획](2026-09-15-powershell-bootstrap-readiness.md)을 정본으로
  삼는다. 기존 완료 알림 계약을 완료 상태로만 보고 이 회귀를 누락하면 안 된다.

## PATH-DROP-01 — 파일·폴더 경로를 안전하게 입력

### 목표와 범위

- Explorer에서 renderer의 현재 터미널에 놓은 파일과 폴더는 내용을 읽지 않고 WebView2가 제공하는
  절대 경로만 받는다. 드롭 당시 활성 session id, shell generation과 renderer instance id를 함께
  고정하며 하나라도 현재 상태와 다르면 요청을 폐기한다.
- PowerShell 계열은 작은따옴표 문자열과 작은따옴표 이중화, `cmd.exe`는 큰따옴표를 사용한다.
  셸 종류를 확정할 수 없는 custom shell과 `cmd.exe`에서 실행 시 재해석될 수 있는 `%`/`!`, 제어 문자,
  상대·device 경로는 지원하지 않는다.
- 인용 결과는 항상 읽기 전용 미리보기와 취소 우선 확인 화면을 거친 뒤 동일 session lifetime에만
  전달한다. Enter·개행은 만들지 않으며 renderer에는 terminal buffer 삽입만 지시하고 shell에는
  확인된 문자열만 한 번 전달한다.

### 영향 파일과 위험

- Terminal contract/application에는 경로 검증·셸별 인용 결과와 session lifetime에 결합된 확인
  요청을 둔다. `TerminalView.xaml.cs`는 `CoreWebView2File.Path`만 추출하고 파일 stream이나 metadata를
  열지 않는다.
- renderer `src/`는 외부 file drop을 가로채 additional objects로 host에 전달하고 확인 UI를 표시한다.
  `dist/`는 build script로만 재생성한다. Preferences와 DesktopIntegration은 변경하지 않는다.
- 핵심 위험은 WebView2 navigation 전후의 늦은 메시지, 탭 전환·종료·restart 경쟁, `cmd.exe`의
  환경 변수/delayed expansion이다. request/session/renderer identity 검증과 보수적 거부로 차단한다.

### 구현 및 검증

- [x] 순수 경로 검증·인용 정책과 session-bound 확인 계약을 구현하고 공백·한글·특수 문자,
  unsupported shell/path, 전환·restart·종료 경쟁 테스트를 추가한다.
- [x] WebView2 additional-object bridge와 renderer drop/미리보기/오류 UI를 연결하고 source/dist 일치
  테스트를 보강한다.
- [x] renderer `npm ci`/build, Terminal focused tests, solution restore/build/test, 정렬 및 diff 검사를
  수행한다. 실제 Explorer·WebView2 drop과 IME/focus 회귀는 자동 검증과 구분해 수동 항목으로 남긴다.

## URL-OPEN-01 — 출력 웹 주소를 확인 후 외부 브라우저로 열기

### 목표와 범위

- xterm 출력의 HTTP/HTTPS 절대 주소만 링크 후보로 만들며 일반 클릭과 drag selection은 유지한다.
  renderer는 Ctrl+클릭에만 session/renderer 세대와 URL 후보를 host로 보내고 자체 navigation,
  새 창, 미리보기 또는 fetch를 수행하지 않는다.
- host는 URL 길이, 절대 형식과 scheme을 다시 검증한 뒤 실제 대상 문자열을 읽기 전용 확인 dialog에
  표시한다. 사용자가 같은 renderer 수명에서 확인한 경우에만 Windows 기본 브라우저에 전달한다.
- file, command, custom protocol, 사용자 정보가 든 URL, 잘못되거나 과도하게 긴 값은 거부한다.
  원문 URL은 설정이나 진단 로그에 저장하지 않으며 실패는 terminal session과 분리해 복구 가능한
  renderer 상태 메시지로만 알린다.

### 영향 파일과 위험

- Terminal contract/application에는 URL 검증 결과와 renderer message parsing을 둔다.
  `TerminalView.xaml.cs`는 pending URL 확인 수명과 외부 실행 실패 경계를 소유한다.
- renderer `src/`는 xterm link provider와 확인 dialog를 연결하고 `dist/`는 build로 재생성한다.
  Preferences와 DesktopIntegration은 변경하지 않는다.
- 핵심 위험은 표시 문자열과 실행 대상의 불일치, 일반 selection 회귀, renderer reconnect 이후 늦은
  확인, shell execute 실패다. host 재검증, immutable 확인 대상, renderer instance/generation 검사와
  예외 격리로 차단한다.

### 구현 및 검증

- [x] HTTP/HTTPS 전용 URL 검증 계약과 renderer protocol/lifetime 검증을 구현하고 malformed,
  unsupported scheme, user-info, 과도한 길이와 외부 실행 실패 경계를 테스트한다.
- [x] renderer Ctrl+클릭 링크 provider와 실제 대상 확인 UI를 연결하고 일반 click/drag, no-fetch,
  source/dist 일치 계약을 보강한다.
- [x] renderer `npm ci`/build, Terminal focused tests, solution restore/build/test, 정렬 및 diff 검사를
  수행한다. 실제 기본 브라우저 실행과 WebView2 selection/focus는 자동 검증과 구분해 수동 항목으로 남긴다.

## SETTINGS-UI-01 — 설정 창 글자 대비 보정

### 관찰과 범위

- 사용자 제보(2026-09-11): Tokyo Night 설정 창의 구조와 기능은 유지하되 글자 색상과 대비를
  보정한다. 현재 화면에서 `Windows 로그인 시 시작`, `다음 실행 시 탭 구성 복원`의 checkbox
  label은 어두운 배경에서 검게 보이고, `테마`와 `표시 모니터`의 선택값은 밝은 system control
  배경 위에서 지나치게 옅게 보인다.
- `SettingsEditorView.xaml`은 TextBox와 ComboBox foreground/background를 선언하지만 WPF 기본
  control template의 실제 표시 상태가 모든 색을 일관되게 사용한다고 가정할 수 없다. 정확한 원인은
  normal·hover·focus·disabled·dropdown open 상태를 실제 창에서 관찰한 뒤 확정한다.
- 이번 보정은 색상 token과 control style/template 범위다. 창 크기, section 배치, 문구, 설정 계약,
  저장 동작과 terminal theme palette는 바꾸지 않는다.

### 수정 기준과 인수 조건

- 일반 label, GroupBox header, checkbox content, 도움말, validation message, TextBox·ComboBox의
  선택값과 dropdown item에 용도별 foreground를 명시한다. 단일 색상을 모든 상태에 강제하지 않고
  enabled/disabled와 error 의미는 구분한다.
- Tokyo Night뿐 아니라 Dark, Light, One Dark 네 테마에서 텍스트와 배경의 대비를 확인한다.
  system theme나 high contrast가 control template을 바꾸는 경우 가독성을 해치지 않으며,
  hover·focus·선택·비활성 상태에서도 글자가 사라지거나 같은 계열색에 묻히지 않아야 한다.
- checkbox glyph와 label, ComboBox 본문·화살표·popup 항목이 한 세트로 읽혀야 한다. 글자색만
  바꾸어 밝은 기본 배경과 충돌하면 해당 control의 background/border까지 같은 style에서 조정한다.
- 100/125/150/200% DPI에서 잘림 없이 확인하고 keyboard focus 표시를 유지한다. 자동화 가능한
  resource/style 계약과 실제 WPF screenshot 검증을 구분하며 실제 화면 확인 전에는 완료로 표시하지 않는다.
- 예상 영향 파일은 Preferences의 `Presentation/SettingsEditorView.xaml`과 관련 UI test다.
  필요할 때만 host `Shell/SettingsWindow.xaml`을 함께 수정하며 설정 model이나 module 경계는 건드리지 않는다.

## ICON-01 — Starboard 아이콘을 앱과 트레이에 적용

### 목표와 현재 상태

- 사용자 요청(2026-09-10): 제작한 아이콘을 앱뿐 아니라 notification area의 트레이에도 사용한다.
  원본은 [starboard-icon-v1.png](../assets/starboard-icon-v1.png), 제작 기록과 프롬프트는
  [아이콘 시안 기록](../assets/starboard-icon-v1.md)을 따른다. 현재 원본은 1254×1254 투명 PNG다.
- `TrayIconService`는 `Starboard.Modules.DesktopIntegration.Assets.Starboard.ico` embedded
  resource를 독립적으로 로드하고, 실패 시에만 `SystemIcons.Application`으로 fallback한다.
  새 PNG를 저장한 것만으로 트레이에 반영되지 않으므로 명시적인 asset 포함·로드·수명 관리가 필요했다.
- 상태는 **I0·I1·I2·I3 구현 및 자동 검증 완료, 실제 Windows 표시 수동 검증 대기**다.

### 적용 범위와 정책

- terminal `>_`, 별, 하단 panel 선이라는 동일한 시각 정체성을 앱과 트레이에 사용한다.
  단순 PNG 확장자 변경이 아니라 실제 multi-resolution ICO를 만들고 alpha를 보존한다.
- 초기 제작 대상 크기는 16/20/24/32/48/64/256px다. 작은 크기의 선·별·내부 여백이
  뭉개지는지 실제 픽셀 크기로 확인한다. 필요하면 같은 모티프의 소형 전용 이미지를 제작하며
  큰 원본을 기계적으로 축소한 결과만으로 완료 처리하지 않는다.
- EXE/Explorer와 WPF 창 아이콘, notification area 및 숨겨진 아이콘 영역에 적용한다.
  아이콘 표시를 위해 기존 panel의 ShowInTaskbar, focus, z-order 정책을 변경하지 않는다.
- 트레이의 왼쪽 클릭 호출, 표시/숨기기·단축키 안내·설정·종료 메뉴와 tooltip을 유지한다.
  새 출력·작업 상태에 따른 애니메이션·badge·아이콘 교체는 이번 범위에 포함하지 않는다.
- asset은 배포물에 로컬로 포함한다. 사용자 기기의 PNG 경로나 네트워크에 의존하지 않는다.
  트레이 asset은 DesktopIntegration이 소유하며 다른 module의 내부 asset을 직접 참조하지 않는다.
  앱용·트레이용 파일은 같은 확정 원본에서 만들고 실제 파일 배치·resource 이름을 구현 시 기록한다.
- 소유한 Icon과 stream은 적절히 해제한다. Explorer 재시작 후에도 동일 아이콘으로 재생성하며
  로딩 실패 시 기존 기본 아이콘으로 fallback하고 민감 정보 없는 진단을 남긴다.

### 영향 파일·병렬 작업

| 작업 | 선행 | 소유 범위와 완료 조건 |
|---|---|---|
| I0 아이콘 asset 확정 | 없음 | 디자인 담당: 원본·소형 보정·ICO 및 크기별 preview, asset 경로·명세 동결 |
| I1 앱 적용 | I0 | host 담당: `Starboard.Windows.csproj`, 필요한 WPF 창 icon resource/참조, 관련 tests |
| I2 트레이 적용 | I0 | Desktop 담당: module asset/project resource, `Infrastructure/TrayIconService.cs`, 전용 tests; 로드·해제·Explorer 복구 |
| I3 통합 검증·문서 | I1, I2 | 통합 담당: package 검사·전체 검증·사용자 문서와 실제 Windows 표시 증거 |

I1과 I2는 파일 소유권을 나눠 병렬 가능하다. I0 원본과 공용 문서는 통합 담당만 갱신한다.
I0의 canonical ICO는 `docs/assets/starboard-icon.ico`이며 16/20/24/32/48/64/256px RGBA PNG
entry를 가진다. I1은 `src/Starboard.Windows/Assets/Starboard.ico`를 executable/WPF resource로,
I2는 `Starboard.Modules.DesktopIntegration.Assets.Starboard.ico` embedded resource로 사용한다.
I3는 portable archive의 `Assets/Starboard.ico` 사본을 필수 파일로 검사하며 user settings,
workspace/saved-tabs, logs와 WebView2 data는 거부한다.

### 인수 기준

- ICO에 의도한 크기·alpha가 포함되는지, package에 asset이 누락되지 않았는지 자동 검증한다.
- 실제 Windows 밝은/어두운 작업표시줄과 숨겨진 아이콘 영역, 100/125/150/200% 배율에서
  식별성과 투명 가장자리를 확인한다. 축소 preview와 실제 트레이 검증은 구분한다.
- EXE/창/트레이의 디자인 일치, 클릭·메뉴·종료, Explorer 재시작 복구와 아이콘 로드 실패
  fallback을 검사한다. 아이콘 캐시 때문에 이전 그림이 보이는 경우와 asset 누락을 구분한다.
- build/resource 입력 변경이므로 solution restore/build/test와 portable 검증을 수행한다.
  실제 장비에서 수행하지 않은 항목은 `Not run`으로 남긴다. commit/push·배포는 별도 요청을 따른다.

## 구현 계획으로 구체화할 때의 경계

- 기존 단일 배포 단위·Terminal/DesktopIntegration/Preferences 모듈 경계를 유지한다.
- renderer 기능은 `Terminal/Presentation/Renderer/src/`와 재생성 `dist/`, 관련 protocol과
  Terminal tests가 주요 영향 범위다. 링크의 외부 실행·clipboard/drop 전달은 host 경계를 먼저 확인한다.
- 새 dependency는 도입 전 유지보수·license·offline asset 포함 여부를 조사한다. 이 문서는
  특정 dependency를 확정하지 않으며 새 terminal parser를 구현하지 않는다.
- 출력 검색·경로 드롭·URL 열기는 renderer 파일이 겹치므로 같은 checkout에서 병렬 편집하지 않는다.
  공통 계약과 파일 소유권을 확정하면 정책/검증 로직과 UI 작업을 독립 worktree에서 병렬화할 수 있다.
- 신규 후보는 기존 UI 안정화·저장한 탭 구현 중인 worker에게 임의로 끼워 넣지 않는다.

## 검증 원칙

- 작은 200 DIP 패널에서 입력·취소·검색·메뉴가 접근 가능해야 하며 기능 때문에 자동 확장하지 않는다.
- keyboard focus, 한글 IME, 4개 테마, 긴 텍스트와 좁은 폭을 검증한다. 실제 WebView2 검증과
  browser simulation을 구분하고 screenshot/입력 경로 확인 없이 UI 완료로 처리하지 않는다.
- shell PID·입력 상태·scrollback 유지, 다른 앱 focus 비방해, taskbar 비침범을 공통 회귀 기준으로 한다.
- 원래 명령과 사용자 데이터가 로그·저장 파일·배포물에 새로 포함되지 않는지 검증한다.
- 문서 단계에서는 내용·링크와 `git diff --check`만 확인한다. 제품 코드나 빌드 입력은 바꾸지 않는다.

## 작업 및 Git 범위

- 기획은 `codex/planning` 전용 worktree에서 진행한다. 초기 요청은 해당 브랜치 commit/push만
  허용했으며, 2026-09-10 후속 사용자 요청으로 현재 기획과 아이콘 원본의 `main` merge도 승인됐다.
- 이번 후속 반영은 로컬 `main` merge까지다. 원격 push, 오케스트레이터 상태·실행 브랜치 변경,
  제품 구현·배포는 별도 요청 없이 하지 않는다.
- 기획 브랜치의 원격 push만으로 main이나 이미 시작된 실행의 계획 snapshot이 바뀐다고 가정하지 않는다.
  구현자가 사용할 문서 버전의 인수와 main 반영은 별도 요청·절차로 수행한다.

## 진행 기록

- [x] 2026-09-10: 분할 화면 제외와 빠르고 간단한 작업표시줄 터미널 방향을 사용자 결정으로 기록.
- [x] 출력 검색·경로 드롭·URL 열기·완료 알림을 후속 후보로 정리하고 기존 계획과 연결.
- [x] 2026-09-10: 제작 아이콘의 앱·트레이 적용을 ICON-01로 기획. 소형 크기 검증,
  resource 수명·fallback, 병렬 작업과 실제 Windows 인수 기준을 추가했다.
- [x] 2026-09-11: I0 canonical ICO와 두 제품 resource의 hash·PNG entry를
  `Test-StarboardIcon.ps1`로 검사하고, I1 executable/WPF resource와 I2 embedded tray resource를
  적용했다. portable package에는 `Assets/Starboard.ico`를 포함하고 사용자별 data를 거부한다.
- **Not run (manual) — 2026-09-11:** 밝고 어두운 작업표시줄 및 숨겨진 아이콘 영역에서
  100/125/150/200% 배율, Explorer 재시작 뒤의 실제 tray 표시를 확인해야 한다.
- [x] 2026-09-11: 설정 창 checkbox·ComboBox·입력 필드에 네 테마에서 재사용할 수 있는
  일반·비활성·오류 전경색 토큰과 popup 항목 스타일을 적용하고 UI 계약 테스트를 추가했다.
- **Not run (manual) — 2026-09-11:** Dark, Light, One Dark, Tokyo Night를 실제 WPF 창에서
  100/125/150/200% 배율로 screenshot 확인해야 한다. 자동 계약 검증과 실제 화면 확인은
  별도 결과로 기록한다.
- [x] 2026-09-11: COMMAND-NOTIFY-01의 metadata-only 완료 event, 세대별 명시적 signal state machine,
  기본 비활성화 schema 7 설정과 DesktopIntegration 조정 request를 확정했다. 지정된 Terminal 247개,
  Preferences 36개, Architecture 9개와 전체 415개 자동 테스트가 통과했다. 실제 PowerShell hook,
  control channel과 Windows 알림 표시는 다음 구현 단계다.
- [x] 2026-09-14: TerminalModule 공개 event와 current-generation 재검증, host opt-in 조정 및 종료 전
  전달 차단을 연결했다. exact Debug 전체 436개와 portable Release 전체 436개, 개인정보 제외·재현 ZIP·
  추출 smoke가 통과했다. 실제 Windows 알림/focus/DPI 관찰은 NOTIFY-006~007에 `Not run`으로 남겼다.
- [x] 2026-09-11: PATH-DROP-01의 shell별 인용, 확인 미리보기, renderer/session
  generation 경계와 WebView2 additional-object 연결을 구현했다. renderer build,
  Terminal 214개와 전체 377개 자동 테스트가 통과했고 실제 Explorer drop·IME·focus
  확인은 MAN-047에 `Not run`으로 남겼다.
- [x] 2026-09-11: URL-OPEN-01의 HTTP/HTTPS 전용 검증, Ctrl+click/selection guard, 실제 target
  확인과 renderer/session generation·외부 실행 실패 격리를 구현했다. renderer build와 전체
  403개 test는 통과했고 실제 WebView2 pointer selection과 기본 브라우저 실행은 MAN-048로 남겼다.
- **Blocked (manual) — 2026-09-16:** Windows interactive session에서 실행 중 Starboard,
  Explorer, HKCU Run 및 local
  settings/workspace primary·backup 파일은 재확인했지만 panel/tray UI automation과 video-controller
  access가 없었다. 밝고 어두운 taskbar의 tray icon, Dark/Light/One Dark/Tokyo Night 설정 화면,
  save/cancel/rollback, hotkey conflict, auto-hide, Explorer restart, 100/125/150/200%·mixed-DPI,
  focus와 fullscreen은 모두 `Blocked` 또는 `Not run`으로 유지한다. 실제 통과는 재개 절차와 함께
  [테스트 계획](../test-plan.md)의 2026-09-16 기록에만 판정한다.
