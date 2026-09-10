# Windows용 Starboard 테스트 계획

## 목적

persistent terminal, taskbar geometry와 focus 정책을 반복 가능한 방식으로
검증한다. 자동화로 증명한 항목과 실제 Windows desktop에서만 확인할 수 있는
항목을 구분한다.

## 상태 표기

| 상태 | 의미 |
|---|---|
| `Planned` | 구현 또는 실행 전 |
| `Passed` | 명시한 환경과 절차에서 통과 |
| `Failed` | 재현 가능한 실패가 남아 있음 |
| `Blocked` | 필요한 환경·권한·장비가 없음 |
| `Not run` | 실행하지 않음 |

mock/simulation 결과를 실제 monitor, taskbar, IME 또는 fullscreen 검증으로
보고하지 않는다.

## 현재 개발 환경

| 항목 | 값 |
|---|---|
| OS API version | Windows `10.0.26200` |
| architecture | x64 |
| .NET SDK | `10.0.301` at `C:/Users/round1studio_14/.dotnet/dotnet.exe` |
| Node.js/npm | 설치됨 |
| PowerShell | PowerShell 7과 Windows PowerShell 설치됨 |
| WebView2 Runtime | `151.0.4129.107` 발견 |
| Git 상태 | orchestrator가 준비한 격리 Git worktree |

위 환경 값은 2026-09-01에 확인했다. 실제 monitor 수, taskbar 위치, scaling과
fullscreen application 종류는 실행할 때 별도로 기록한다.

## 최근 자동 검증 결과

- 실행일: 2026-09-10 (단축키 안내 창 닫기 수정)
- `ShortcutGuideWindowIntegrationTests`에서 modeless WPF 창의 버튼·Escape 실패를
  수정 전에 재현했다. 명시적 Close 연결 후 버튼·Escape·일반 Window.Close 세 경우가
  통과했으며 다른 창 유지와 일반 키 미소비도 검사했다.
- Debug solution restore/build와 filter/skip 없는 전체 304개 test가 통과했다
  (Architecture 7, Terminal 153, DesktopIntegration 75, Preferences 31, Integration 38).
  build 경고·오류 0개이며 build-server 비활성·단일 MSBuild node를 사용했다.
- 실제 STA WPF 창에 routed event를 전달한 자동 integration 검증이다. 물리 입력,
  tray 재열기·focus와 앱 전체 종료 지연은 검증하지 않았으며 바탕화면 재배포도 하지 않았다.

- 실행일: 2026-09-10 (안전 기능·패널/탭 UI 바탕화면 재배포)
- `becbd18cb170920757df4f73373e3ecc7d731b22`의 Release restore/build와 전체 301개
  test가 통과했다(Architecture 7, Terminal 153, DesktopIntegration 75,
  Preferences 31, Integration 35). build 경고·오류와 test skip은 0개다.
- self-contained publish, ZIP 재현성·SHA-256·추출 검증을 통과했다. package SHA-256은
  `3b5b5eac807e3badc16dee18e3e975b5c6e694778defdca457a86cc7f3f41ea9`다.
- 기존 바탕화면 instance는 정상 종료 요청 후 20초 내 종료되지 않아 사용자 지시에
  따라 강제 종료했다. 이후 단일 instance guard로 생략되지 않는 별도 portable smoke를
  실행해 exit code 0과 version/commit 검증을 통과했다.
- 사용자 요청대로 새 backup 없이 `C:/Users/round1studio_14/Desktop/Starboard-win-x64`를
  덮어썼고 publish 원본과 배포 파일 499개의 hash 일치를 확인했다. 사용자 설정은
  삭제하지 않았다. Explorer를 통해 재실행한 PID 88760의 응답 상태와
  PowerShell·ConHost·WebView2 자식 process를 확인했다.
- 재배포 후 사용자 요청으로 이전 backup 폴더 `Starboard-win-x64-backup-20260909-102608`과
  `Starboard-win-x64-backup-20260909-175512`를 휴지통으로 옮겼다. 최신 배포 폴더와
  실행 중인 앱은 보존했으며 이전 backup은 휴지통에서 복원할 수 있다.
- 실제 확인 dialog·clipboard·IME·focus·다중 monitor/DPI 조작은 수행하지 않았다.
  프로세스 기동 확인을 아래 수동 UI 시나리오의 통과로 취급하지 않는다.

- 실행일: 2026-09-09 (안전 기능·패널/탭 UI 최종 검증)
- 지정 SDK의 Debug restore와 build는 경고·오류 0개로 통과했다. filter/skip 없는 전체
  test는 `--blame-hang-timeout 3m`으로 Architecture 7, Preferences 31, Terminal 153,
  DesktopIntegration 75, Integration 35, 총 301개를 통과했고 실제 종료까지 약 19초였다.
- `npm --prefix src/Modules/Starboard.Modules.Terminal/Presentation/Renderer run build`로
  renderer source에서 offline dist를 재생성했다. sandbox에서는 esbuild child process가
  `EPERM`으로 차단됐으나 승인된 동일 로컬 build에서는 성공했고, rebuild 뒤 source/dist
  변경은 없었다. RendererDistributionTests가 CSP, local asset, confirmation/new-output,
  panel bottom padding 계약을 자동 검사한다.
- 이 결과는 unit/contract, Windows ConPTY GUI-host integration 및 simulation 범위다.
  실제 WebView2 화면, clipboard, Korean IME, focus와 multi-monitor/DPI hardware 검증은
  수행하지 않았으므로 아래 MAN 항목은 `Not run`으로 유지한다.

- 실행일: 2026-09-09 (ConPTY GUI-host 수명주기 회귀 안정화)
- 원인은 제품의 session 정리 결함이 아니라 SAFE-01 뒤에도 GUI test host가 살아 있는
  재시작 session을 확인 token 없이 `CloseAsync`로 닫던 테스트 계약 불일치였다. 제품은
  이를 의도대로 거부했으며 기존 generation/instance 검증과 다른 tab 격리는 유지했다.
- host는 재시작된 session generation의 close confirmation을 적용하고 workspace의 명시적
  tab 제거 event와 실제 shell PID 종료를 각각 제한 시간·cancellation으로 기다린다. 진행
  결과에는 terminal command/output 없이 expected/observed host path·PID, session ID,
  generation, tab 제거와 process 종료 여부만 원자적으로 기록한다. host timeout과 bounded
  cleanup 실패는 재시도·skip 없이 이 상태를 포함해 실패한다.
- 대상 test는 build-server 비활성·단일 MSBuild node에서 10회 연속 통과했고 최종 변경 뒤
  집중 test, filter/skip 없는 IntegrationTests 34개와 단축키 안내 집중 test 4개도 통과했다.
  기본 병렬 SDK 명령은 이
  machine의 기존 MSBuild 정체가 테스트 시작 전 다시 발생해 중단했으며, 이는 2026-09-03에
  기록한 환경 제약과 같은 양상이다.
- 실제 WPF/WebView2 GUI 조작과 사용자가 입력하는 ConPTY 수동 검증은 수행하지 않았으므로
  MAN-033~034와 관련 수동 항목은 `Not run`으로 유지한다.

- 실행일: 2026-09-09 (작업공간 기능 바탕화면 재배포)
- `d40f826b69e12e77bfe3026cb419b5f7ed6901af`의 Release restore/build/test를 수행했다.
  경고·오류 0개, 전체 262개 통과(Architecture 5, Terminal 121, DesktopIntegration 71,
  Preferences 31, Integration 34). self-contained publish, ZIP/SHA-256·추출 검증 통과.
- package SHA-256: `e76ee80039a5230bac59d16ee2060bfbaf8c8b7f3acd0b08d808370b1ec7b03c`.
  publish와 배포 후보 499개 파일의 hash를 대조했다. 기존 instance 종료 후 별도 smoke를
  실행해 single-instance 조기 종료 없이 exit code 0과 version/commit 일치를 확인했다.
- `C:/Users/round1studio_14/Desktop/Starboard-win-x64`를 교체하고 Explorer로 재실행했다.
  이전 배포본은 `Starboard-win-x64-backup-20260909-175512`에 보존했다.
  PID 71664의 응답 상태와 PowerShell·ConHost·WebView2 자식 process를 확인했다.
- settings/workspace JSON은 배포 전후 존재하지 않으며 복원 옵션 기본 꺼짐을 유지한다.
  이번 실행 시점의 새 로그는 smoke 종료의 정보 수준 ShutdownFlush뿐이며 시작 오류는 없다.
- 실제 탭 편집·옵션 저장·앱 재실행 구성 복원과 IME/다중 monitor 수동 검증은 수행하지
  않았다. 프로세스 실행 확인을 해당 UI 시나리오 통과로 취급하지 않는다.

- 실행일: 2026-09-09 (workspace release verification)
- 지정된 user-local .NET SDK로 Debug restore/build/test를 다시 실행해 경고·오류 0개와
  전체 262개 test 통과를 확인했다(Architecture 5, Preferences 31, Terminal 121,
  DesktopIntegration 71, Integration 34). renderer는 `npm run build`로 source에서
  재생성한 뒤 committed `dist` diff가 없음을 확인했다.
- portable package 검사는 workspace JSON, 모든 `.bak`/`.tmp`, logs와 WebView2 user
  data를 publish, ZIP, 추출 smoke directory에서 거부하도록 보강했다. 실제 package
  생성과 workspace 재시작 UI, 한글 IME, 권한 제한 folder 및 multi-monitor/DPI hardware
  실행은 이 작업에서 하지 않았으므로 MAN-041~043과 기존 관련 MAN 항목은 `Not run`이다.

- 실행일: 2026-09-09 (하단 패널 간격 수정본 바탕화면 배포)
- Release build 경고·오류 0개, 전체 193개 테스트 통과. portable ZIP 재생성 hash,
  SHA-256 및 추출 검증 통과. publish와 바탕화면 교체본 499개 파일 hash가 일치한다.
- 기존 instance를 종료한 뒤 배포 후보 smoke를 별도 실행해 exit code 0을 확인했다.
  기존 폴더를 `Starboard-win-x64-backup-20260909-102608`로 보존하고 교체했다.
- Explorer를 통해 재실행한 바탕화면 배포본의 응답 상태와 PowerShell, ConHost,
  WebView2 자식 process를 확인했다. 오류 log는 이전 2026-09-05 이후 갱신되지 않았다.
- package는 `a0607c5` 기반 미커밋 스타일·간격 수정을 포함한다. SHA-256:
  `e46d0cdf743209c1754b60c836a5aa2dc885bfa7acca0bb93826d5ba8c519017`.
  실제 사용자 화면의 6 DIP 간격, 입력 및 hardware 시나리오는 미수행이다.

- 실행일: 2026-09-09 (하단 패널 간격)
- 축소 패널을 하단 작업표시줄에서 6 DIP 띄우되 높이를 보존하도록 수정했다.
  100/125/150/200% DPI와 공간 부족에서의 간격 축소를 추가 검증하고, 음수 monitor 좌표,
  auto-hide, 상단·좌우 배치 및 확장/복원 테스트를 유지했다.
- geometry 집중 테스트 21개와 전체 Debug build/test 193개 통과, 빌드 경고·오류 0개.
  실제 바탕화면 배포본은 교체하지 않았으므로 사용자 화면에서의 간격 확인은 미수행이다.

- 실행일: 2026-09-09 (코드 스타일 동기화)
- TRK 공통 CodeStyle을 반영한 C# 98개 파일 정리 후 Debug restore/build가 경고·오류
  없이 통과했고 전체 188개 test가 통과했다. 결과는 `out/code-style-tests/`에 보관한다.
- production은 공백·줄바꿈 변경만 포함한다. token/구문 비교와 formatter 재실행
  추가 변경 0개, TRK 정렬 검사기 self-test/working-tree 검사 및 diff 검사를 통과했다.
- IDE0055의 기본 initializer 정렬은 TRK 규칙과 충돌하므로 해당 진단만 비활성화하고
  `scripts/Test-CSharpAlignment.ps1` 검사로 보완했다. 나머지 analyzer는 유지한다.
- 테스트 2개 파일의 await 결과/JSON fixture 지역 변수 분리 외 runtime 로직 변경은 없다.
  이번 작업에서 Release 배포나 UI/hardware 시나리오는 재실행하지 않았다.

- 실행일: 2026-09-09
- 범위: 바탕화면 시작 오류(Win32 error 87) 수정 및 재배포
- 수정 전 실제 opaque WPF 창의 native opacity 적용에서 같은 오류와
  `WindowPlacementService.SetOpacity` stack을 재현했다. host callback을 통한 WPF
  속성 적용으로 변경한 뒤 신규 integration test 2개가 통과했다. 0.97 → 0.8 → 1.0 →
  0.97 반복 적용 시 bounds와 foreground 보존, callback 실패의 rollback 경로 전파를
  검증했다. 실제 desktop 투과 효과를 검증한 것은 아니다.
- Debug restore/build/test와 Release package build/test 모두 경고·오류 0개,
  전체 188개 통과(Architecture 5, Terminal 61, DesktopIntegration 66, Preferences 27,
  Integration 29). `out/startup-fix-tests`에 Debug TRX를 보관했다.
- self-contained ZIP/SHA-256, 재생성 hash, 추출 smoke가 통과했다. 기존 instance를
  종료한 뒤 smoke를 수행해 single-instance 조기 종료를 피했다. 499개 파일을
  publish 파일별 hash와 대조해 바탕화면에 교체했다. 기존 폴더는 백업했고 사용자
  settings 파일은 배포 전후 없으므로 기본값을 유지한다.
- 배포본은 `b014901` 기반 미커밋 시작 오류 수정본이다. package SHA-256:
  `36a1e500da0df232a8a0b7483c10e370740f1a6cda0172b2591e41599dab689c`.
  commit 표기만으로 수정 전/후를 구별할 수 없으므로 이 hash를 기준으로 식별한다.
- 탐색기를 통해 재실행한 배포 경로의 앱 응답과 `pwsh.exe`, `conhost.exe`,
  `msedgewebview2.exe` 자식 process를 확인했고 새로운 시작 오류나 진단 로그는 없었다.
  computer-use의 앱/창 목록에는 panel이 노출되지 않아 실제 renderer 화면·입력,
  tray/settings 조작까지 통과했다고 판단하지 않는다. MAN-039/040의 전체 시나리오,
  실제 multi-monitor/DPI/fullscreen/IME 검증은 여전히 미완료다.

- 실행일: 2026-09-08
- 범위: P10~P11 portable 배포와 C gate 자동 검증
- 결과: build server 정상 종료 후 지정 SDK로 restore, Debug solution build 경고
  0개·오류 0개와 전체 automated test 186개가 통과했다. package 단일 흐름의 Release
  build와 동일한 186개 test도 통과했다.
- self-contained `win-x64` publish에서 499-entry ZIP과 SHA-256을 생성했다. 실제 hash
  재계산, 같은 staging의 결정적 ZIP 재생성 및 clean staging 전체 재실행 hash, executable version/build commit,
  local renderer, 제품/third-party license와 notice 포함 검사가 통과했다. settings, log,
  WebView2 user data, dump, PDB와 개발 PC 절대 경로는 없었다.
- ZIP을 버전 staging에 다시 풀고 전용 executable smoke를 실행해 metadata, renderer와
  기본 shell 경로를 확인했다. smoke는 Preferences/startup 적용 전에 종료해 기존 사용자
  설정이나 자동 실행 경로를 바꾸지 않았다.
- 실제 WebView2 Runtime 초기화, interactive terminal UI, multi-monitor/mixed-DPI,
  fullscreen, auto-hide, IME와 실제 portable update/startup/rollback은 수행하지 않아
  `MAN-*`에 `Not run`으로 유지한다.

- 실행일: 2026-09-07
- 범위: P4 Windows 창 안정화 통합
- 결과: 지정된 clean restore와 Debug solution build가 경고 0개·오류 0개로 통과했고,
  전체 automated test 137개가 통과했다. 이 중 DesktopIntegration unit test 55개,
  architecture test 5개, 창 정책·host integration test 7개다.
- simulated integration에서 최신 display/DPI recapture, 사용자 숨김 → fullscreen →
  `TaskbarCreated` → fullscreen 종료, 같은 monitor fullscreen 억제/복원, auto-hide 중
  명시적 호출, 확장·축소와 monitor 제거 fallback을 검증했다.
- 로컬 WPF smoke는 실제 bottom taskbar와 `1920x1032` work area에서 HWND를 생성해
  panel이 work area 안에 있고, background 확장·축소가 foreground HWND를 바꾸지
  않으며 원래 collapsed rectangle을 복원함을 확인했다.
- 별도 바탕화면 배포본 Starboard가 실행 중이어서 single-instance guard가 이번
  worktree executable의 동시 실행을 막았다. 기존 사용 process를 종료하지 않았고
  이번 build의 tray/shortcut/terminal 전체 executable smoke는 수행하지 않았다.
  Explorer 재시작, 실제 auto-hide 전환, fullscreen application, multi-monitor와
  mixed-DPI hardware 검증도 `MAN-*`에서 `Not run`으로 유지한다.

- 실행일: 2026-09-04
- 범위: terminal focus border 제거와 schema 4 collapsed 높이 migration
- 결과: renderer build, Debug solution build 경고 0개·오류 0개, 전체 test 86개 통과
- renderer 배포 CSS에 `.xterm.focus::after`가 없고 schema 3의 148 DIP 기본값은
  200 DIP로 migration하며 다른 사용자 높이는 보존함을 자동 test로 확인
- self-contained `win-x64` Release 501개 파일을 바탕화면 배포 폴더에 덮어쓰고
  전체 source/destination SHA-256 일치 및 재실행 후 process 응답을 확인
- 실제 화면에서 노란 focus border 제거와 약 8행 표시 여부는 수행하지 않아
  `MAN-035`와 `MAN-036`을 `Not run`으로 유지

- 실행일: 2026-09-03
- 범위: terminal multi-session backend와 protocol v2 renderer
- 결과: Terminal module unit test 55개 통과, ConPTY integration test 2개 통과,
  architecture test 5개 통과
- 전체 Debug build는 경고 0개·오류 0개, 전체 test 83개 통과. 기본 병렬 build는
  오류 진단 없이 exit code 1, 기본 병렬 test와 architecture test는 출력 없이
  정체되어 중단했으며 `--disable-build-servers -maxcpucount:1`로 재검증해 통과
- 실제 PowerShell session 3개에서 서로 다른 environment, working directory와 history를
  만들고 첫 session의 exit/restart/close 뒤 두 번째와 세 번째 session 상태가
  유지됨을 hidden GUI test host로 확인
- 최초 세 shell과 restart shell의 PID가 모두 다르고 exit, tab close와 coordinator
  dispose 뒤 각 PID의 종료가 관찰됨을 확인
- blocked fake session 3개의 cleanup을 병렬 시작하고 주입한 deadline 안에
  coordinator가 반환함을 unit test로 확인
- 선택을 세 tab 사이에서 바꾼 입력이 지정 transport 하나에만 기록되고, renderer
  배포 asset이 session별 xterm map에 output을 쓰며 비활성 pane을 제거하지 않고
  숨기는 구조와 10,000줄 scrollback 설정을 자동 test로 확인
- Terminal module의 다른 기능 module 직접 참조 금지와 module entry point/Contracts
  밖 public type 금지를 architecture test로 확인
- 실제 WebView2를 조작하는 3-tab UI, 비활성 tab 장기 output/scrollback, 단축키,
  expand/collapse, 숨김/복원과 tray 종료 smoke는 이번 실행에서 수행하지 않아
  `MAN-033`~`MAN-035` 및 관련 항목을 `Not run`으로 유지

- 실행일: 2026-09-01
- 명령: clean restore, Debug solution build, Debug solution test
- 결과: build 경고 0개·오류 0개, test 32개 통과
- 실제 executable smoke: foreground focus 유지, 116px collapsed → 1032px
  expanded → 116px 복원, `WS_EX_TOPMOST` 없음 확인
- tray smoke: notification icon callback으로 숨김 뒤 1.5초 유지, 숨김 중 동일
  PowerShell process 유지, 재표시와 `종료` 후 8초 이내 process 종료 확인
- summon smoke: 실제 `Ctrl+Alt+S` 입력으로 비활성 panel 호출, 활성 panel 숨김을
  확인하고 실제 notification-area icon 좌표 클릭으로 숨긴 panel의 표시·활성화를 확인
- WebView2·ConPTY·PowerShell 자식 process 확인
- self-contained Release: 501개 파일·196.5MiB, WPF·WinForms runtime, local
  renderer/notice 포함 및 실제 실행 smoke 통과
- 실제 화면 상호작용이 필요한 항목은 아래 manual matrix에 `Not run`으로 유지

## 공통 명령

system `dotnet` 대신 확인된 user-local SDK를 사용한다.

```powershell
$starboardDotnet = 'C:/Users/round1studio_14/.dotnet/dotnet.exe'
& $starboardDotnet restore Starboard.Windows.sln
& $starboardDotnet build Starboard.Windows.sln --configuration Debug --no-restore
& $starboardDotnet test Starboard.Windows.sln --configuration Debug --no-build
```

renderer asset을 변경한 경우에만 Terminal module의 renderer source directory에서
다음을 실행한다.

```powershell
npm ci
npm run build
```

일반 build와 runtime은 npm이나 network를 사용하지 않아야 한다.

## Automated test matrix

### Architecture

| ID | Case | 기대 결과 | 상태 |
|---|---|---|---|
| ARC-001 | 기능 module assembly reference 검사 | 다른 기능 module을 직접 참조하지 않음 | Passed |
| ARC-002 | SharedKernel reference 검사 | host/module/test를 참조하지 않음 | Passed |
| ARC-003 | module public type 검사 | Contracts 또는 승인 entry point만 public | Passed |
| ARC-004 | production project reference 검사 | test project로 역참조 없음 | Passed |
| ARC-005 | host namespace 검사 | module Infrastructure namespace 사용 없음 | Passed |

### Desktop geometry와 policy

| ID | Case | 기대 결과 | 상태 |
|---|---|---|---|
| GEO-001 | bottom taskbar, 100% DPI | panel이 work area 하단에 붙고 taskbar와 겹치지 않음 | Passed |
| GEO-002 | top taskbar | panel이 work area 상단 안쪽에 붙음 | Passed |
| GEO-003 | left/right taskbar | edge에 평행한 두께와 work area clamp 적용 | Passed |
| GEO-004 | negative monitor coordinate | 음수 좌표를 보존하고 primary origin으로 clamp하지 않음 | Passed |
| GEO-005 | 125/150/200% DPI | DIP height가 올바른 physical pixel로 변환됨 | Passed (automated) |
| GEO-006 | 높이가 work area보다 큼 | 최소 여백을 보존하도록 clamp됨 | Passed |
| GEO-007 | collapsed → expanded → collapsed | 원래 valid frame을 정확히 복원 | Passed |
| GEO-008 | 기억한 monitor 제거 | 최신 taskbar monitor의 안전 frame으로 복구 | Passed (simulated) |
| POL-001 | idle + taskbar concealed | panel conceal | Passed (automated) |
| POL-002 | active + taskbar concealed | 마지막 안전 frame 유지 | Passed (automated) |
| POL-003 | expanded + taskbar concealed | expanded frame 유지 | Passed (automated) |
| POL-004 | fullscreen on same monitor | panel demote/conceal | Passed (simulated) |
| POL-005 | fullscreen on other monitor | panel normal policy 유지 | Passed (automated) |
| POL-006 | unknown taskbar presence | off-screen 이동 없이 last safe frame 유지 | Passed (automated) |

### Terminal

| ID | Case | 기대 결과 | 상태 |
|---|---|---|---|
| TRM-001 | shell discovery | pwsh → powershell → cmd 순서 | Planned |
| TRM-002 | executable 없음 | recoverable missing-shell state | Planned |
| TRM-003 | custom executable/argument validation | path와 argument array를 안전하게 분리 | Planned |
| TRM-004 | resize validation | 1 이상의 bounded column/row만 native call로 전달 | Passed |
| TRM-005 | split UTF-8 sequence | chunk 경계에서도 문자 손실 없음 | Planned |
| TRM-006 | output batching | 순서 보존, 최대 batch와 flush interval 준수 | Planned |
| TRM-007 | renderer message version/type | unknown/malformed/oversized message 거부 | Passed |
| TRM-008 | shell exit | app은 유지되고 restart 가능 state로 전환 | Passed |
| TRM-009 | bounded shutdown | timeout 안에 resource 정리 완료 | Passed |
| TRM-010 | tab add/direct/next/previous 선택 | ordered registry와 끝 순환, `PowerShell N` 이름 및 opaque ID 유지 | Passed |
| TRM-011 | active/inactive tab close | 오른쪽 우선 인접 선택, inactive close 시 active 유지 | Passed |
| TRM-012 | 마지막 tab close와 8개 상한 | 새 기본 tab 즉시 생성, 상한 초과는 process 생성 전 거부 | Passed |
| TRM-013 | session exit/failure/restart 격리 | 대상 tab 상태/transport만 변경하고 stale callback 무시 | Passed |
| TRM-014 | multi-session bounded shutdown | 모든 session cleanup 병렬 시작, 전체 deadline 안에 반환 | Passed |
| TRM-015 | renderer protocol v2 session ID | session message serialize/parse 및 missing/empty/malformed ID 거부 | Passed |
| TRM-016 | targeted input/resize routing | 선택 변경 없이 지정 session transport 하나만 호출 | Passed |
| TRM-017 | workspace tab name/order round trip | 한글 이름과 0-based 순서, 활성 tab 구성이 보존됨 | Passed |
| TRM-018 | workspace starting directory/shell restore | 허용된 절대 폴더와 shell kind만 저장·복원함 | Passed |
| TRM-019 | workspace corrupt/future schema recovery | 손상 primary는 valid backup으로, 미래 schema는 보존·중단함 | Passed |
| TRM-020 | workspace partial restore failure | 한 tab의 shell/folder 실패가 다른 tab 복원을 막지 않음 | Passed |
| TRM-021 | workspace opt-out/delete | 기본 꺼짐, 옵션 해제 뒤 파일 삭제와 기본 tab 시작 | Passed |
| TRM-022 | close confirmation | 같은 session generation의 승인만 tab close에 적용하고 취소/늦은 응답은 무시 | Passed |
| TRM-023 | multiline paste confirmation | CR/LF snapshot만 확인 뒤 한 번 전달하고 cancel·restart·late response는 전달하지 않음 | Passed |
| TRM-024 | inactive-tab new output | 비활성 non-empty output만 표시하고 selection/restart/remove에서 초기화 | Passed |

### Preferences와 theme

| ID | Case | 기대 결과 | 상태 |
|---|---|---|---|
| SET-001 | settings 없음 | defaults 생성 | Passed |
| SET-002 | partial document | 누락값만 defaults로 merge | Passed |
| SET-003 | old schema | 지원 migration 후 current schema | Passed |
| SET-004 | invalid JSON | backup 후 defaults, app 유지 | Passed |
| SET-005 | atomic write failure | 마지막 정상 파일 유지 | Passed |
| SET-006 | out-of-range value | validator가 안전 범위로 교정하고 이유 반환 | Passed |
| THM-001 | 네 built-in theme | 모든 필수 WPF/xterm/ANSI token 존재 | Passed |
| THM-002 | ANSI palette | 각 theme가 정확히 16색 제공 | Passed |
| THM-003 | foreground/background | 기본 terminal text contrast 기준 충족 | Passed |

### OS adapter

| ID | Case | 기대 결과 | 상태 |
|---|---|---|---|
| HOT-001 | 호출 shortcut message | `Ctrl+Alt+S` message당 activation toggle event 한 번 | Passed |
| HOT-002 | shortcut 충돌 | recoverable error, 앱 계속 실행 | Passed (simulated) |
| RUN-001 | startup command quoting | 공백이 있는 executable path가 정확히 quote됨 | Passed |
| RUN-002 | enable/disable | 현재 user Run value만 생성/제거 | Passed (simulated) |
| VDT-001 | official API available | capability와 current-desktop query 반환 | Planned |
| VDT-002 | COM unavailable | no-op fallback과 unsupported capability | Planned |

## Integration test matrix

integration test는 Windows에서 실행하며 다른 앱의 focus나 실제 display topology를
변경하지 않는 범위만 자동화한다.

| ID | Case | 기대 결과 | 상태 |
|---|---|---|---|
| INT-001 | ConPTY 생성과 `pwsh` prompt | prompt output 수신 | Planned |
| INT-002 | `Set-Location` 후 다음 command | working directory 유지 | Passed |
| INT-003 | environment variable set/get | 같은 shell process에서 값 유지 | Planned |
| INT-004 | ConPTY resize | shell이 새 column/row를 보고 | Planned |
| INT-005 | Unicode/한글 round trip | UTF-8 text 손실 없음 | Planned |
| INT-006 | long-running command + interrupt | `Ctrl+C`로 prompt 복귀 | Planned |
| INT-007 | shell explicit exit | restart surface state와 새 session 생성 | Planned |
| INT-008 | output drain + dispose | hang 없이 timeout 내 종료 | Passed |
| INT-009 | bundled renderer load | network request 없이 ready message | Planned |
| INT-010 | WebView2 process failure simulation | app 유지, surface recovery 또는 오류 표시 | Planned |
| INT-011 | 두 PowerShell session의 독립 상태 | environment, cwd, history와 background job이 서로 섞이지 않음 | Passed |
| INT-012 | 한 session exit/restart/confirmed close | 같은 generation의 확인만 적용되고 tab 제거·PID 종료 뒤에도 다른 session의 interactive state가 그대로 유지됨 | Passed |
| INT-013 | multi-session output drain + dispose | hidden GUI host가 8초 cleanup deadline 안에 정상 종료 | Passed |
| INT-014 | display/DPI message와 observer recapture | 최신 monitor/DPI frame 적용, activation 없음 | Passed (simulated) |
| INT-015 | 사용자 숨김 + fullscreen + Explorer 복구 | 사용자 호출 전까지 숨김 유지, tray 한 번 재생성 | Passed (simulated) |
| INT-016 | same-monitor fullscreen 진입/종료 | 임시 conceal 뒤 foreground를 바꾸지 않고 복원 | Passed (simulated) |
| INT-017 | auto-hide 중 명시적 호출 | fullscreen이 아닐 때만 표시·활성화 요청 | Passed (simulated) |
| INT-018 | 실제 WPF HWND background 배치 | taskbar 비겹침, focus 보존, 확장·축소 복원 | Passed (local Windows) |
| INT-019 | 설정 apply 성공 순서 | Terminal → DesktopIntegration → host surface → persistence | Passed (simulated) |
| INT-020 | 취소·validation 오류 | live module과 저장소를 변경하지 않고 편집 draft 유지 | Passed (automated contracts) |
| INT-021 | renderer/shortcut/startup 적용 실패 | 저장하지 않고 이전 Terminal/Desktop snapshot으로 복구 | Passed (simulated) |
| INT-022 | persistence 실패 | DesktopIntegration → Terminal 역순 rollback, 마지막 파일 유지 | Passed (simulated) |
| INT-023 | rollback 실패 | persisted/effective 상태 분리 표시, 같은 draft로 재시도 성공 | Passed (simulated) |
| INT-024 | tray 설정 창 단일 수명 | 중복 창 없이 기존 창 활성화, 닫을 때 panel 활성화 호출 없음 | Passed (simulated) |
| INT-025 | live appearance와 새-tab shell | 기존 session PID/cwd 유지, 변경 shell은 이후 tab만 사용 | Passed (module automated) |
| INT-026 | build metadata 표시 | 설정 표시용 version/short commit이 assembly metadata에서 일관되게 생성됨 | Passed (automated) |
| INT-027 | portable smoke guard | metadata mismatch, renderer 누락과 shell 누락을 non-zero로 거부하고 normal startup은 변경하지 않음 | Passed (automated) |
| INT-028 | restored workspace process identity | 저장 구성마다 새 PID를 시작하고 runtime session ID/command/output은 재사용하지 않음 | Passed |
| INT-029 | renderer source/dist offline contract | committed bundle이 source와 동기화되고 CDN·remote font/script 없이 local asset만 사용 | Passed |
| INT-030 | portable user-data exclusion | publish/ZIP/추출본에 workspace JSON, `.bak`, `.tmp`, log와 WebView2 data가 없음을 package 검사로 거부 | Passed (automated package) |
| INT-031 | ConPTY GUI-host close synchronization | restart generation confirmation, tab removal과 shell PID exit를 timeout/cancellation 안에서 관찰 | Passed |

ConPTY test는 각 case와 host cleanup에 timeout을 두고 실패 시 orphan child process를 남기지
않는다. GUI host는 재시작 close의 session ID·generation, tab 제거와 process 종료 상태를
입출력 payload 없이 결과 파일에 기록해 timeout과 cleanup 실패 단계를 구분한다.

## 실제 executable smoke matrix

| ID | Case | 확인 결과 | 상태 |
|---|---|---|---|
| SMK-001 | hidden process launch | foreground HWND가 시작 전후 동일 | Passed |
| SMK-002 | background expand/collapse | 116px → 1032px → 116px, 원래 rectangle 정확히 복원 | Passed |
| SMK-003 | renderer/shell process tree | `msedgewebview2.exe`, `conhost.exe`, `pwsh.exe` 확인 | Passed |
| SMK-004 | self-contained Release 실행 | 외부 .NET Runtime 없이 host·renderer·shell 시작 | Passed |
| SMK-005 | tray 표시/숨김 | 1.5초 timer 이후에도 hidden, shell PID 유지, 재표시 성공 | Passed |
| SMK-006 | tray 종료 | 접근 가능한 `Starboard 종료` menu 실행 뒤 8초 이내 process 종료 | Passed |
| SMK-007 | normal z-order | `WS_EX_TOPMOST` 없음, background expand/collapse focus 유지 | Passed |
| SMK-008 | global panel 호출 | 비활성→표시·foreground, 활성→숨김 | Passed |
| SMK-009 | 실제 tray icon 왼쪽 클릭 | 숨긴 panel 표시·foreground, normal z-order 유지 | Passed |
| SMK-010 | P4 build 전체 executable/tray 재검증 | 이번 build로 tray·호출·terminal 수명 확인 | Blocked (existing instance) |
| SMK-011 | versioned portable package | ZIP/SHA-256 일치, 필수 renderer/license/metadata 포함, runtime/user/developer data 제외 | Passed (automated package) |
| SMK-012 | extracted portable smoke mode | 추출 executable 시작, version/commit·renderer·기본 shell 확인 후 user settings 적용 없이 종료 | Passed (automated package) |

## Manual desktop matrix

각 실행에서 OS build, monitor topology, scaling, taskbar edge/auto-hide, shell과 app
build hash를 함께 기록한다.

| ID | Scenario | 확인 내용 | 상태 |
|---|---|---|---|
| MAN-001 | cold launch while editor focused | editor focus 유지, panel 표시 | Not run |
| MAN-002 | terminal click | 한 번의 click으로 caret/IME 입력 가능 | Not run |
| MAN-003 | background reposition | foreground HWND 변화 없음 | Not run |
| MAN-004 | bottom taskbar | 겹침과 1px gap/overlap 오류 없음 | Not run |
| MAN-005 | top taskbar | 올바른 edge에 표시 | Not run |
| MAN-006 | left taskbar | geometry와 usable content 확인 | Not run |
| MAN-007 | right taskbar | geometry와 usable content 확인 | Not run |
| MAN-008 | taskbar auto-hide reveal/conceal | idle은 따라가고 active는 유지 | Not run |
| MAN-009 | taskbar monitor 이동 | panel이 새 monitor로 이동 | Not run |
| MAN-010 | secondary monitor negative coordinate | 잘못된 primary clamp 없음 | Not run |
| MAN-011 | monitor disconnect/reconnect | visible monitor의 안전 frame으로 복구 | Not run |
| MAN-012 | 100% scaling | 선명한 text와 정확한 geometry | Not run |
| MAN-013 | 125% scaling | 선명한 text와 정확한 geometry | Not run |
| MAN-014 | 150% scaling | 선명한 text와 정확한 geometry | Not run |
| MAN-015 | 200% scaling | 최소 terminal row와 geometry 유지 | Not run |
| MAN-016 | mixed-DPI monitor 이동 | DPI 변경 후 즉시 선명하게 reflow | Not run |
| MAN-017 | Explorer restart | taskbar tracking과 z-order 복구 | Not run |
| MAN-018 | maximized application | panel visible, app focus 유지 | Not run |
| MAN-019 | borderless fullscreen | 같은 monitor에서 panel이 방해하지 않음 | Not run |
| MAN-020 | exclusive fullscreen game/video | 강제 overlay 없음 | Not run |
| MAN-021 | virtual desktop switch | 지원 capability와 실제 visibility 일치 | Not run |
| MAN-022 | expand/collapse hotkey | 현재 monitor work area 확장과 정확한 복원 | Not run |
| MAN-023 | hotkey conflict | settings에서 실패가 설명되고 app 유지 | Not run |
| MAN-024 | PowerShell 7 | persistent prompt, history, resize | Not run |
| MAN-025 | Windows PowerShell | persistent prompt, history, resize | Not run |
| MAN-026 | cmd | persistent prompt, Unicode 한계가 명시됨 | Not run |
| MAN-027 | WSL/custom shell | 선택한 경우 argument/resize/exit 검증 | Not run |
| MAN-028 | 한글 IME composition | 조합 중복·누락·caret 이탈 없음 | Not run |
| MAN-029 | clipboard shortcuts | selection copy, paste와 Ctrl+C interrupt 구분 | Not run |
| MAN-029a | safety confirmation UI | 살아 있는 tab close 취소/승인, multiline paste preview·Escape·clipboard 변경 뒤 승인 확인 | Not run |
| MAN-030 | WebView2 Runtime missing simulation | local error와 설치 안내 | Not run |
| MAN-031 | login startup | 일반 user 권한으로 한 instance만 실행 | Not run |
| MAN-032 | Release folder offline | renderer가 network 없이 로드 | Not run |
| MAN-033 | multi-tab renderer | 비활성 탭 output/scrollback/state 유지, 대상 session routing | Not run |
| MAN-034 | tab 접근성·overflow·shortcut | 상태/이름/focus-visible, 8개 overflow, 탭 단축키와 Ctrl+W 전달 | Not run |
| MAN-034a | inactive-tab new output | 비활성 tab의 점, 접근성 이름, 선택 시 해제와 자동 activation 없음 | Not run |
| MAN-035 | schema 4 collapsed height | 200 DIP에서 tab strip 아래 약 8행, 사용자 높이 보존 | Not run |
| MAN-036 | terminal focus surface | terminal click 후 노란 외곽선 없이 caret과 입력 동작 유지 | Not run |
| MAN-037 | tray 설정 창 수명·focus | 연속 요청 시 한 창, 닫은 뒤 이전 foreground를 강제 변경하지 않음 | Not run |
| MAN-038 | 실제 설정 live apply/rollback | theme/font/높이 적용 중 PID·cwd 유지, hotkey 충돌과 저장 실패 UI 확인 | Not run |
| MAN-039 | portable WebView2/terminal UI | 새 폴더에서 실제 WebView2 Runtime 초기화, local renderer와 interactive shell 확인 | Not run |
| MAN-040 | portable update/rollback | 기존 설정 유지, startup 경로 변경과 이전 폴더 복귀 확인 | Not run |
| MAN-041 | workspace restore UI | 복원 opt-in 뒤 재시작에서 탭 이름·순서·선택·시작 폴더가 보존되고 각 tab이 새 PID인지 확인 | Not run |
| MAN-042 | workspace IME and partial failure | 한글 IME 이름 편집, 없는/권한 없는 폴더 또는 shell 한 tab 실패가 다른 tab을 막지 않는지 확인 | Not run |
| MAN-043 | workspace opt-out | 옵션 해제 뒤 구성 파일 삭제와 다음 시작의 기본 tab 하나를 확인 | Not run |

## Focus 검증 절차

1. Notepad 또는 editor에 text caret를 둔다.
2. foreground HWND와 process를 기록한다.
3. Starboard를 launch하거나 taskbar/display refresh를 유발한다.
4. foreground HWND가 그대로인지 확인한다.
5. terminal을 click하고 입력이 되는지 확인한다.
6. editor를 다시 click한 뒤 background reconciliation이 focus를 되찾지 않는지
   확인한다.

launch focus 보존과 terminal click activation은 서로 다른 요구사항이며 하나의
`WS_EX_NOACTIVATE` 결과로 함께 통과했다고 판단하지 않는다.

## IME와 shortcut 검증 절차

1. Microsoft Korean IME를 활성화한다.
2. 한글 syllable, 자모 수정, backspace와 space 확정을 입력한다.
3. CR/LF가 있는 paste에서 읽기 전용 preview와 취소 기본 focus가 보이고, clipboard를 바꾼 뒤
   승인해도 처음 preview snapshot만 전달되는지 확인한다. emoji/CJK text도 확인한다.
4. selection이 없을 때 `Ctrl+C`가 interrupt를 보내는지 확인한다.
5. selection이 있을 때 copy shortcut과 interrupt 정책이 문서와 일치하는지
   확인한다.
6. `Ctrl+V`와 `Ctrl+Shift+V`를 각각 확인한다.

## Explorer와 display recovery 절차

1. terminal에서 작업 directory와 environment marker를 만든다.
2. Explorer를 정상적인 사용자 절차로 restart한다.
3. `TaskbarCreated` 이후 panel이 새 taskbar geometry로 돌아오는지 확인한다.
4. shell PID 또는 marker가 유지되는지 확인한다.
5. monitor를 제거했을 때 창이 off-screen에 남지 않는지 확인한다.

## Release gate

- clean restore/build/test 성공
- self-contained `win-x64` publish 성공
- committed renderer `dist`와 license notice 존재
- renderer source 재빌드 결과와 committed `dist`가 일치하고 runtime CDN/remote asset이 없음
- 제품 version/build commit과 `release-metadata.json` 일치
- versioned staging 밖의 출력이나 사용자 data를 정리하지 않음
- ZIP SHA-256 일치와 추출 smoke 성공
- runtime network request 없음
- workspace JSON, `.bak`, `.tmp`, command 또는 terminal output이 publish/ZIP/추출본에 없음
- confirmation token/paste preview/new-output runtime state가 log, workspace, publish/ZIP/추출본에 없음
- automated/integration 결과가 이 문서에 갱신됨
- 실제로 실행한 manual case만 `Passed`로 표시
- 미실행 DPI, multi-monitor, fullscreen과 IME case가 숨김없이 남아 있음
