# Windows용 Starboard 테스트 계획

## 목적

persistent terminal, taskbar geometry와 focus 정책을 반복 가능한 방식으로
검증한다. 자동화로 증명한 항목과 실제 Windows desktop에서만 확인할 수 있는
항목을 구분한다.

실제 desktop 수동 검증 범위에서는 오프라인 WebView2 실행, 업데이트와 rollback을
배포 검증 대상으로 삼지 않는다. portable 생성·checksum·추출 smoke의 자동/package
결과는 별도로 판정하며 실제 renderer 조작을 통과한 것으로 확대하지 않는다. 수동
복구 결과는 셸 선택·지속 세션과 renderer·셸·ConPTY 실패 후 host/session 유지 및
재시작 관찰로 제한한다.

## 상태 표기

| 상태 | 의미 |
|---|---|
| `Planned` | 구현 또는 실행 전 |
| `Passed` | 명시한 환경과 절차에서 통과 |
| `Failed` | 재현 가능한 실패가 남아 있음 |
| `Blocked` | 필요한 환경·권한·장비가 없음 |
| `Not run` | 실행하지 않음 |
| `Out of scope` | 이번 작업의 필수 검증 범위와 완료 판단에서 제외 |

mock/simulation 결과를 실제 monitor, taskbar, IME 또는 fullscreen 검증으로
보고하지 않는다.

## ICON-01 앱·트레이 아이콘 검증

| ID | 범위 | 방법 | 상태 |
|---|---|---|---|
| ICON-001 | 16/20/24/32/48/64/256px ICO entry, alpha, 제품 resource 일치 | `pwsh -NoProfile -File scripts/Test-StarboardIcon.ps1` | Passed (automated) |
| ICON-002 | package의 `Starboard.exe`, `Assets/Starboard.ico` 및 사용자별 data 제외 | `package-portable.ps1`의 필수 파일/제외 경로 검사와 추출 smoke | Passed (automated) |
| ICON-003 | Explorer 재시작 뒤 tray icon 재생성 요청 | `DesktopWindowIntegrationTests` fake runtime | Passed (simulated) |
| ICON-004 | 밝은 작업표시줄, notification area와 숨겨진 아이콘 영역의 식별성 | 실제 Windows에서 tray를 열어 확인 | Not run — interactive desktop 시각 관찰 필요 |
| ICON-005 | 100%, 125%, 150%, 200% 배율의 tray 선명도와 투명 가장자리 | 각 배율을 설정하고 Explorer/tray를 다시 열어 확인 | Not run — 100%도 아이콘 시각 관찰을 수행하지 않았고 나머지 배율 변경 권한 없음 |
| ICON-006 | 실제 Explorer 재시작 뒤 동일 아이콘, 클릭과 menu 유지 | 사용자 desktop에서 Explorer 재시작 후 확인 | Not run — Explorer 재시작은 사용자 shell에 영향을 주므로 이 작업에서 수행하지 않음 |

## COMMAND-NOTIFY-01 완료 알림 통합 검증

| ID | 범위 | 방법 | 상태 |
|---|---|---|---|
| NOTIFY-001 | 명시적 start/finish, exit result와 중복·역순 signal 억제 | Terminal lifecycle unit/integration tests | Passed (automated) |
| NOTIFY-002 | restart·remove·shell exit·dispose 뒤 이전 session generation 거부 | coordinator lifetime tests | Passed (automated) |
| NOTIFY-003 | host 공개 Terminal event/재검증 경계와 metadata-only module 계약 | Architecture 및 host integration tests | Passed (automated) |
| NOTIFY-004 | opt-in off/on 전달, stale generation과 host stop 뒤 전달 억제 | `CommandCompletionNotificationCoordinatorTests`와 DesktopIntegration tests | Passed (simulated) |
| NOTIFY-005 | package에서 settings/workspace/saved-tabs/log/WebView2 data와 command/history/output capture 제외 | portable publish·ZIP·추출 content 검사 | Passed (automated) |
| NOTIFY-006 | 실제 Windows 알림이 명령 종료당 한 번 표시되고 panel activation/focus 변화가 없음 | 사용자 interactive desktop에서 PowerShell 긴 명령과 foreground app을 함께 관찰 | Not run — terminal/tray UI 입력·알림 시각 관찰을 수행하지 않음 |
| NOTIFY-007 | 100/125/150/200%에서 알림과 panel geometry/focus 회귀 없음 | 각 DPI에서 실제 notification과 panel을 관찰 | Not run — 이번 실행은 DPI 설정이나 실제 화면을 조작하지 않음 |
| NOTIFY-008 | PowerShell startup bootstrap 준비·fallback과 control payload privacy | lifecycle unit/runtime 및 PowerShell 7/Windows PowerShell 실제 ConPTY GUI-host test | Passed (automated, 2026-09-16) — 내부 bootstrap/`>>` 부재, 격리된 느린 profile output/custom prompt/nested prompt, 첫 성공·다음 실패 1회 신호, nonce·depth·exit-only channel과 pipe 실패/timeout 후 lifecycle 비활성화·shell 입력을 검증 |

## DIAGNOSTIC-LOG-01 진단 로그 개인정보와 보관 검증

| ID | 범위 | 방법 | 상태 |
|---|---|---|---|
| LOG-001 | message, command/output/prompt/path 및 안전하지 않은 metadata가 disk에 남지 않음 | `FileDiagnosticLogTests.WriteDoesNotPersistMessagesOrUnsafeMetadata` | Passed (automated) |
| LOG-002 | 512 KiB 회전과 active+4 archive의 2.5 MiB 상한 | `FileDiagnosticLogTests.WriteRotatesAtBoundedSizeAndKeepsOnlyConfiguredFiles` | Passed (automated) |
| LOG-003 | 병렬 write, 파일 잠금, 손상 파일과 종료 직전 경로 손실이 예외를 전파하지 않음 | `FileDiagnosticLogTests.ConcurrentAndLockedWritesAreIsolated`, `CorruptExistingLogAndUnavailableDirectoryDoNotThrow` | Passed (automated) |
| LOG-004 | publish/ZIP/추출본에서 logs(회전본 포함), user settings/workspace/saved tabs와 WebView2 data 제외 | `package-portable.ps1` content deny-list와 추출 smoke | Passed (automated) |

## 현재 개발 환경

| 항목 | 값 |
|---|---|
| OS API version | Windows `10.0.26200` |
| architecture | x64 |
| .NET SDK | `10.0.301` at `C:/Users/round1studio_14/.dotnet/dotnet.exe` |
| Node.js/npm | 설치됨 |
| PowerShell | PowerShell 7과 Windows PowerShell 설치됨 |
| WebView2 Runtime | `152.0.4191.66` 실행 process에서 확인 |
| Git 상태 | orchestrator가 준비한 격리 Git worktree |
| 2026-09-10 display probe | NVIDIA GeForce RTX 3060, LG ULTRAGEAR 2대, 각각 1920×1080 @ 143Hz |
| 2026-09-10 topology | `DISPLAY1=(-1920,0,1920,1080)` 보조, `DISPLAY2=(0,0,1920,1080)` primary |
| 2026-09-10 work area / scale | 두 화면 모두 `(Y=0,height=1032)`; per-monitor DPI 각각 96(100%) |

위 환경 값은 2026-09-01에 확인했다. 실제 monitor 수, taskbar 위치, scaling과
fullscreen application 종류는 실행할 때 별도로 기록한다.

## VIRTUAL-DESKTOP-01 공식 API와 fallback 검증

| ID | 범위 | 방법 | 상태 |
|---|---|---|---|
| VD-001 | 공식 조회/이동 adapter가 panel HWND와 desktop ID를 보존 | `VirtualDesktopServiceTests`의 fake manager | Passed (simulated) |
| VD-002 | COM 초기화 실패 시 no-op capability와 unavailable/unsupported 결과 | factory failure unit test | Passed (automated) |
| VD-003 | 조회 또는 이동 호출 실패 뒤 adapter 단일 해제와 영구 fallback | failover service unit tests | Passed (automated) |
| VD-004 | module entry point가 attached panel HWND만 사용하고 종료 시 service dispose | module facade unit test | Passed (simulated) |
| VD-005 | public type은 Contracts/module 경계만 사용하고 pinning operation 미노출 | `ModuleBoundaryTests` reflection 검사 | Passed (automated) |
| VD-006 | 실제 Windows virtual desktop에서 current 상태/ID와 명시적 이동 결과 일치 | 두 desktop을 만들고 panel 상태 조회 후 대상 ID 이동, terminal session/focus 유지 관찰 | Not run — interactive virtual desktop 생성·전환과 panel UI 관찰을 수행하지 않음 |

`IVirtualDesktopManager`에는 desktop 열거·생성·전환과 모든-desktop pinning API가
없으므로 VD-006의 대상 desktop ID 확보는 진단용 helper 또는 별도 test window로
수동 준비해야 한다. 제품 API가 pinning을 지원하는 것으로 표시하는 검증은 하지
않으며 `CanPinWindowToAllDesktops=false`가 기대 결과다.

## 최근 자동 검증 결과

- 실행일: 2026-09-16 (앱 정상 종료 교착 수정 및 바탕화면 재배포)
- commit `44bc8a82a787c9f2007a0787e5a4f4f0fb064f6b`의 Debug/Release solution build는
  경고·오류 0개였고 전체 532개 test가 실패·skip 없이 통과했다. 중복 종료 병합,
  cleanup 완료 전 WPF shutdown 금지와 cleanup 실패 exit code를 전용 test 3개로 확인했다.
- self-contained publish, 501-file content 검사, 결정적 ZIP 재생성과 checksum 및 추출
  smoke가 통과했다. package SHA-256은
  `376823296e156c87bfd09627bab3fae145e0484d98805ca0ad1d8d5b303b2262`다.
- 기존 바탕화면 instance는 정상 종료 요청 후 8초 안에 종료되지 않아 강제 종료했다.
  사용자 요청대로 backup 없이 `C:/Users/round1studio_14/Desktop/Starboard-win-x64`를
  publish와 파일별 hash가 같은 501개 파일로 교체하고 새 PID 61820의 응답 상태를 확인했다.
- 새 배포본에서 실제 tray `종료`를 다시 누르는 검증은 자동화하지 않았으므로 `SMK-006`은
  사용자 확인 전까지 `Pending recheck`를 유지한다.

- 실행일: 2026-09-15 (run `20260915-073031-990510-914d5fc7`, integration build repair)
- 지정 SDK restore는 성공했다. 첫 Debug build의 최초 오류는 Terminal WPF markup cache
  삭제 `Access denied`였고 DesktopIntegration test cache/coverage 쓰기에도 같은 증상이
  나타났다. 파일은 read-only가 아니며 현재 사용자 Modify ACL이 있었다. build server
  정상 종료 뒤 동일 Debug build는 경고·오류 0개로 통과해 source/project 충돌이 아닌
  ignored `bin/obj` 생성 입력의 일시적 경쟁으로 판정했다.
- 전체 Debug test 518개가 통과했다(Architecture 13, Terminal 300,
  DesktopIntegration 110, Preferences 37, Integration 58). virtual desktop fallback,
  renderer/shell failure recovery, diagnostic log privacy/rotation과 portable deny-list의
  기존 focused regression test와 package build 격리 검사가 포함된다. 최종 suite 전 한 번
  실패한 기존 ConPTY tab 검사는 첫 shell marker 시작이 10초를 초과한 건이며, 해당 test
  단독 재실행(7초)과 이어진 전체 suite에서 통과해 일시적 host 시작 지연으로 구분했다.
- portable Release build/test 518개, self-contained publish, deterministic ZIP/checksum,
  추출 smoke가 통과했다. ZIP 501개 entry의 별도 검사에서도 settings/workspace/saved-tabs,
  logs, WebView2 user data, database/dump/PDB/temp/backup 패턴은 0건이었다. ZIP SHA-256은
  `93edf4cac885380442da24f564d3d1c0b80c94042c6b71dc4e73d919adcbc23a`다.
- 실제 virtual desktop/Explorer, renderer process kill, WebView2 Runtime 제거,
  multi-monitor/DPI와 IME는 수행하지 않았다. 관련 VD-006 및 manual matrix는 `Not run` 또는
  기존 `Blocked` 상태를 유지한다.
- 실행일: 2026-09-15 (run `20260915-073031-990510-914d5fc7`, virtual desktop integration)
- 지정 SDK solution restore와 Debug build는 경고·오류 0개로 통과했다. 전체 자동 test
  504개가 통과했다(Architecture 13, Terminal 294, DesktopIntegration 110,
  Preferences 37, Integration 50). 실제 virtual desktop UI 전환·이동은 수행하지 않았다.
- 실행일: 2026-09-15 (run `20260915-022255-752257-23c5de81`, terminal convenience release gate)
- renderer `npm run build`가 통과했고 source 재생성 뒤 committed `dist`에 diff가 없었다. sandbox의
  esbuild child-process 실행은 `EPERM`이었으며, 허용된 동일 명령으로만 build를 완료했다.
- 지정 SDK Debug restore와 build는 경고·오류 0개로 통과했다. 전체 자동 test 496개가
  통과했다(Architecture 12, Terminal 294, DesktopIntegration 103, Preferences 37,
  Integration 50). 여기에는 WSL profile discovery/UTF-16·UTF-8 parsing, timeout/failure
  fallback, profile launch argument 및 duplicate/height renderer protocol simulation이 포함된다.
- `package-portable.ps1`은 self-contained Release build/test(동일 496개), publish, deterministic
  ZIP/checksum 재생성, 추출 smoke를 통과했다. ZIP SHA-256은
  `b1b1e72bab774fdcf8e51a275e33f4826725abd0b6ac069b3bd43ecaf300b2db`다. publish·ZIP·추출본에
  settings/workspace/saved-tabs primary·backup·temporary, logs, WebView2 data, PDB와
  command/history/output capture가 없음을 자동으로 검사했다.
- 실제 WebView2 mouse·keyboard·Korean IME, 설치된 WSL distribution, DPI·monitor·taskbar 및
  focus는 실행하지 않았다. 자동/simulation 통과로 대체하지 않고 MAN-050~052를 `Not run`으로
  유지한다.

- 실행일: 2026-09-14 (run `20260911-112715-513680-257ccf40`, 완료 알림 통합)
- 지정 SDK의 exact Terminal test 260개와 Debug solution restore/build(경고·오류 0), 전체 436개
  test가 sandbox 밖에서 통과했다(Architecture 10, Terminal 260, DesktopIntegration 85,
  Preferences 36, Integration 45). sandbox 안의 exact SDK 명령은 기본 병렬 worker/IPC 제약으로
  출력 없는 exit 1 또는 정체, PowerShell named-pipe test timeout을 보였으며 단일-node fallback과
  sandbox 밖 동일 argv 결과를 구분했다.
- current session generation 재검증, restart/remove/dispose 이후 stale 수명, 중복 finish, opt-in
  off/on, host stop 이후 late callback, metadata-only public surface와 기존 new-output/restart/renderer
  recovery·expand/focus 계약을 자동 또는 simulation으로 확인했다. 실제 Windows 알림 풍선과 foreground
  focus, 100/125/150/200% DPI 화면은 수행하지 않아 NOTIFY-006~007을 `Not run`으로 유지한다.
- portable Release build/test 436개와 self-contained publish, 사용자 data 및 command/history/output
  capture 제외, 재현 ZIP·추출 smoke가 통과했다. ZIP SHA-256은
  `d824849c3c5fbef99e659393f31f45b94feae70ff9292e042d3c71c08660c6db`다.

- 실행일: 2026-09-11 (run `20260911-073617-996439-56532a1d`, terminal URL open)
- renderer `npm ci`와 source→dist build가 통과했다. sandbox에서 esbuild child spawn이
  `EPERM`으로 차단된 후 허용된 동일 build로 재생성했다.
- 지정 SDK restore와 Debug build(경고/오류 0)가 통과했다. 전체 automated test 403개가
  통과했다(Architecture 8, Terminal 239, DesktopIntegration 80, Preferences 33,
  Integration 43).
- HTTP/HTTPS·길이·user-info 검증, 일반 click/selection guard, Ctrl+click marker, 실제 실행
  target 확인, renderer/session generation과 외부 실행 실패 격리, no-fetch/source-dist 계약을
  자동화로 확인했다. 실제 WebView2 pointer selection과 기본 브라우저 실행은 수행하지 않아
  MAN-048을 `Not run`으로 유지한다.

- 실행일: 2026-09-11 (run `20260911-073617-996439-56532a1d`, path drop)
- renderer `npm ci`와 source→dist build가 통과했다. sandbox에서 esbuild child spawn이
  `EPERM`으로 차단된 후 허용된 동일 build로 재생성했다.
- 지정 SDK restore·Debug build(경고/오류 0)·전체 377개 test가 통과했다
  (Architecture 8, Terminal 214, DesktopIntegration 80, Preferences 33, Integration 42).
  경로 인용·거부, 탭 전환·restart·remove 후 입력 0건, renderer instance/session
  generation 프로토콜과 file read API 미사용은 자동화로 확인했다.
- 실제 Explorer·WebView2 drop, 드롭 중 renderer reconnect, 한글 IME·focus·scrollback
  회귀는 수행하지 않아 MAN-047을 `Not run`으로 유지한다.

- 실행일: 2026-09-11 (run `20260910-073231-827228-6ed3d9e7`, 저장한 탭 수명 통합 최종 재검증)
- 지정 SDK restore는 NuGet vulnerability metadata 접근이 필요한 sandbox 시도에서 `NU1900` 또는
  출력 없는 exit 1로 실패했고 network가 허용된 같은 argv로 통과했다. sandbox의 기본 병렬 Debug
  build도 첫 project 뒤 경고·오류 0개인 채 exit 1, 기본 전체 test는 test 시작 전 출력 없이
  정체되어 중단했다. 같은 지정 argv를 외부에서 실행한 Debug solution은 build 경고·오류 0개,
  전체 348개 test(Architecture 8, Terminal 193, DesktopIntegration 75, Preferences 31,
  Integration 41)가 통과했다.
- 저장 store/service 집중 89개, 새 integration contract 3개와 architecture 8개가 통과했다.
  저장 정의의 지정 셸·폴더와 기존 session instance 보존은 module test, 실제 서로 다른 PID와
  session interactive-state 격리는 기존 ConPTY GUI-host integration을 함께 근거로 삼았다. 실제
  WebView2에서 저장 메뉴를 조작해 PID와 scrollback을 관찰한 결과는 아니며 MAN-044~045로 남겼다.
- 기존 41개 IntegrationTests와 499-file publish까지 통과한 package 실행이 checksum 단계에서
  실패한 원인은 해당 PowerShell host에 `Get-FileHash` cmdlet이 없었기 때문이다. 저장 탭 기능,
  Release build/test 또는 publish 실패가 아니다. 스크립트는 이제 cmdlet availability를 확인하고
  없으면 disposable file stream과 .NET `SHA256` 객체로 같은 소문자 hash를 계산한다.
- 수정 후 과제에 지정된 `powershell -NoProfile -File scripts/package-portable.ps1`의 sandbox
  첫 실행은 publish 전 Release restore에서 NuGet audit source를 읽지 못한 `NU1900`으로 중단됐고,
  network가 허용된 동일 argv는 Release build 경고·오류 0개와 전체 348개 test(Integration 41),
  499-entry self-contained ZIP 재현성 및 추출 executable smoke를 통과했다. 최종 ZIP SHA-256은
  `13c491097c49d1b1724b845c18ccd5b1c9f3f83c9906a3c7fc0b14f5f01c1c8a`이며 checksum 파일과
  독립 `Get-FileHash` 재계산 및 cmdlet 부재 simulation 결과가 모두 일치했다. stream 해제 후
  ZIP의 배타적 재개방도 통과했다. saved-tabs/settings/workspace primary·backup·temporary, log,
  WebView2 user data와 개발 PC 절대 경로 검사는 publish·ZIP·추출본에서 0건이었다.
- renderer source rebuild는 sandbox의 esbuild child spawn `EPERM` 뒤 허용된 로컬 실행에서
  통과했고 source/dist diff가 없었다. 실제 WebView2 화면, Korean IME, 125/150/200% 및 mixed-DPI
  장비 조작은 수행하지 않았다.
- 실행일: 2026-09-10 (최신 main `e252bd1` 사용자 요청 배포)
- 이번 배포는 앞선 수동 검증 작업의 범위와 별개인 사용자 요청이다. Release restore/build와
  전체 304개 test(7/153/75/31/38)가 통과했으며 경고·오류·실패·skip은 0개다.
- 실행 중 오케스트레이터를 보호하기 위해 공용 build-server shutdown 없이 별도
  `out/deploy-e252bd1` 출력에서 검증·self-contained publish했다. 기존 package content
  검사 함수를 재사용해 필수 asset·고지와 개인정보/개발 파일 제외를 확인했다.
- 종료 전 Starboard 하위에 ai_auto_work Electron·Python·작업 프로세스가 있음을 확인해
  사용자에게 알렸고, 작업 중단을 감수하고 교체하라는 명시적 승인을 받았다. 기존 앱은
  정상 종료 요청 후 15초 내 종료되지 않아 process tree를 강제 종료했다.
- 기존 instance 종료 뒤 portable smoke exit code 0, version/commit 일치를 확인했다.
  백업 없이 바탕화면 기존 폴더를 교체하고 원본과 배포 파일 499개의 hash를 대조했다.
  이번에는 directory 배포이며 ZIP은 새로 생성하지 않았다.
- Explorer로 재실행한 PID 89036의 응답과 PowerShell·ConHost·WebView2 자식 process를
  확인했다. 기존 오케스트레이터와 작업 PID는 종료됐으며 자동 재시작하지 않았다.
- 제품 코드는 기존 `a225da3`과 같고 새 아이콘 적용·저장한 탭·이름 편집 UI는 아직 기획이다.
  실제 배포본 UI·IME·focus·DPI 조작 검증은 미수행이며 이번 기록은 기획 worktree에만 추가했다.

- 실행일: 2026-09-10 (run `20260910-055029-889715-ee5de9ac`, Windows platform 수동 검증 시도)
- 실제 장비의 OS, GPU, 연결된 monitor와 현재 topology를 읽기 전용으로 조회했다.
  Windows 11 Pro `10.0.26200` x64, RTX 3060, 동일한 LG ULTRAGEAR 2대이며, 1920×1080
  화면을 좌우로 배치해 왼쪽 보조 화면은 X=-1920, 오른쪽 primary는 X=0이다. 두 화면의
  work area는 Y=0부터 높이 1032px라 48px bottom taskbar 영역을 남긴다. per-monitor
  DPI API는 두 화면 모두 96(100%)를 반환했다. 이 값은 실제 장비·배치
  근거지만 Starboard panel의 표시, text 선명도 또는 focus 정책 통과 근거는 아니다.
- 현재 worktree HEAD는 `8c172dc870f4da53947d8fd8a839d398103b0a8a`다. 바탕화면 portable
  배포 metadata는 제품 `0.1.0`, build commit
  `a225da3b9c34ea0a264c095dbb73404efeb20b6e`이며, 실행 중인 Starboard process는 없었다.
  두 commit의 제품 차이는 shortcut 안내 window 수정뿐이지만, 이번 실행에서 portable
  executable을 시작하거나 panel을 실제 관찰하지 않았으므로 현재 제품 동작으로 간주하지
  않는다.
- UI 자동화는 안전한 foreground 기준 창인 Rider와 Calculator에 대해서도 각각
  `Computer Use was not approved`로 거부됐다. 또한 Starboard는 terminal application이므로
  이 환경의 Windows UI 자동화 정책상 launch/click/key input/hotkey 조작 대상이 될 수 없다.
  명령 세션에서 읽기 전용으로 조회한 `Shell_TrayWnd`도 HWND `0x0`으로 반환돼 interactive
  desktop의 foreground, panel rectangle과 z-order를 별도 probe로 관찰할 수 없었다.
- 이 때문에 MAN-001, MAN-003~MAN-022는 아래 실행 기록대로 `Blocked`다. monitor 2대와
  음수 좌표, bottom work area 및 100% 환경이 실제로 존재한다는 것만 확인했으며, panel이
  taskbar를 덮지 않는지, background event가 focus를 빼앗지 않는지, exclusive fullscreen
  위에 강제로 나타나지 않는지는 확인하지 못했다. 재개하려면 사용자 소유 interactive
  desktop에서 Starboard와 기준 app에 입력·화면 관찰 권한이 있어야 하며, 125/150/200%와
  mixed-DPI 변경, taskbar 위치/auto-hide 변경, monitor 물리 분리, Explorer 재시작 및
  controlled exclusive-fullscreen app 실행 권한이 추가로 필요하다.

- 실행일: 2026-09-10 (터미널 UI 수동 검증 시도)
- 대상은 실제 WPF/WebView2 화면에서 terminal click, Korean IME, clipboard/interrupt,
  안전 확인창, 다중 탭·접근성·단축키와 축소 panel 본문 여백을 검증하는 것이었다.
  이 실행 환경의 Windows UI 자동화 정책은 terminal application의 click, text input,
  clipboard 및 keyboard shortcut 조작을 금지하므로 실제 화면에 입력을 전달하지
  않았다. 자동 test, process 기동 또는 renderer source 검사는 이 수동 검증을 대체하지
  않는다.
- 아래 MAN-002, MAN-028, MAN-029, MAN-029a, MAN-033, MAN-034, MAN-034a,
  MAN-035 및 MAN-036은 이 정책 제약 때문에 `Blocked`다. 테스트 재개에는 실제
  사용자 소유 desktop에서 각 절차를 수동 수행할 수 있는 입력 권한이 필요하다.
  terminal command, output 및 clipboard 원문은 기록하지 않았다.

- 실행일: 2026-09-10 (단축키 안내 창 닫기 수정본 바탕화면 배포)
- `a225da3b9c34ea0a264c095dbb73404efeb20b6e`의 Release restore/build와 전체 304개
  test가 통과했다. build 경고·오류, test 실패·skip은 0개다.
- self-contained publish, ZIP 재현성·SHA-256·추출 smoke가 통과했다. package SHA-256은
  `44bf5decfbc6a7f5d1b778e64ac3b9d6819f4b562c3e73c587d3210e1661b8bb`다.
- 기존 PID 88760은 정상 종료 요청 후 15초 내 종료되지 않아 해당 process tree를
  강제 종료했다. 기존 instance가 없는 상태에서 package smoke가 완료됐다.
- 사용자 요청대로 backup 없이 바탕화면 `Starboard-win-x64`에 덮어쓰고 원본과 499개
  파일 hash 일치를 확인했다. Explorer로 재실행한 PID 102920의 응답과 PowerShell,
  ConHost, WebView2 자식 process를 확인했다. 새 backup 폴더는 만들지 않았다.
- 닫기·Escape의 WPF routed-event 자동 검증은 포함되지만 배포본의 물리 입력·tray
  재열기·focus 수동 검증은 미수행이다. 앱 전체 종료 지연의 원인과 수정은 별도 과제다.

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
| SET-UI-001 | settings control state resources | CheckBox label, TextBox, ComboBox 본문·popup에 일반·비활성·오류 전경색과 배경 계약 존재 | Passed (automated) |
| SET-UI-002 | settings built-in themes | Dark, Light, One Dark, Tokyo Night 선택 계약 유지 | Passed (automated) |
| SET-UI-003 | settings WPF screenshot | 네 테마의 label·checkbox·입력·콤보 일반/비활성/오류 상태가 배경과 구분되고 focus 표시 유지 | Pending — 실제 WPF 화면 및 100/125/150/200% DPI 수동 확인 필요 |

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
| INT-001 | ConPTY 생성과 `pwsh` prompt | prompt output 수신 | Passed (automated, PowerShell 7/Windows PowerShell, 2026-09-16) |
| INT-002 | `Set-Location` 후 다음 command | working directory 유지 | Passed |
| INT-003 | environment variable set/get | 같은 shell process에서 값 유지 | Planned |
| INT-004 | ConPTY resize | shell이 새 column/row를 보고 | Planned |
| INT-005 | Unicode/한글 round trip | UTF-8 text 손실 없음 | Planned |
| INT-006 | long-running command + interrupt | `Ctrl+C`로 prompt 복귀 | Planned |
| INT-007 | shell explicit exit | restart surface state와 새 session 생성 | Passed (automated coordinator); 실제 renderer UI는 MAN-033 |
| INT-008 | output drain + dispose | hang 없이 timeout 내 종료 | Passed |
| INT-009 | bundled renderer load | network request 없이 ready message | Planned |
| INT-010 | WebView2 process failure simulation | app 유지, surface recovery 또는 오류 표시 | Partial — control 교체·live-session resync·bounded backlog·old generation 거부 계약은 automated; 실제 process kill/reconnect UI는 MAN-033 |
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
| INT-031 | terminal failure generation race | restart/remove/shutdown 뒤 이전 output/exit/renderer callback이 현재 generation을 변경하지 않음 | Passed (automated unit/source contract, 2026-09-15) |
| INT-032 | WebView2 Runtime missing recovery contract | 고정된 offline 안내에 exception detail·command·output을 포함하지 않고 재시도와 host-owned 종료 제공 | Passed (automated unit/source contract, 2026-09-15); 실제 Runtime 제거 화면은 MAN-030 |
| INT-024 | tray 설정 창 단일 수명 | 중복 창 없이 기존 창 활성화, 닫을 때 panel 활성화 호출 없음 | Passed (simulated) |
| INT-025 | live appearance와 새-tab shell | 기존 session PID/cwd 유지, 변경 shell은 이후 tab만 사용 | Passed (module automated) |
| INT-026 | build metadata 표시 | 설정 표시용 version/short commit이 assembly metadata에서 일관되게 생성됨 | Passed (automated) |
| INT-027 | portable smoke guard | metadata mismatch, renderer 누락과 shell 누락을 non-zero로 거부하고 normal startup은 변경하지 않음 | Passed (automated) |
| INT-028 | restored workspace process identity | 저장 구성마다 새 PID를 시작하고 runtime session ID/command/output은 재사용하지 않음 | Passed |
| INT-029 | renderer source/dist offline contract | committed bundle이 source와 동기화되고 CDN·remote font/script 없이 local asset만 사용 | Passed |
| INT-030 | portable user-data exclusion | publish/ZIP/추출본에 workspace JSON, `.bak`, `.tmp`, log와 WebView2 data가 없음을 package 검사로 거부 | Passed (automated package) |
| INT-031 | ConPTY GUI-host close synchronization | restart generation confirmation, tab removal과 shell PID exit를 timeout/cancellation 안에서 관찰 | Passed |
| INT-032 | 저장한 탭 CRUD와 재실행 유지 | 별도 strict JSON의 create/update/delete, 원자 저장, 재로드와 workspace 계약 독립성 | Passed (module automated) |
| INT-033 | 저장 항목 새 shell 실행 | 지정 shell·폴더·이름으로 새 session을 만들고 기존 session instance/PID/state를 교체하지 않음 | Passed (module automated + ConPTY GUI-host process isolation) |
| INT-034 | renderer 저장 탭 수명 연결 | CRUD/launch/cancel routing, request ID 중복 억제, renderer generation 변경 뒤 snapshot 재동기화와 shutdown 순서 | Passed (automated integration contract) |
| INT-035 | portable saved-tabs exclusion | publish/ZIP/추출본에서 saved-tabs primary, backup, temporary와 개발 PC 절대 경로를 package 검사로 거부 | Passed (automated package) |
| INT-036 | shell별 path-drop 인용 | PowerShell 작은따옴표/한글/특수 문자와 cmd 큰따옴표를 검증하고 cmd `%`/`!`, custom shell, 상대·device·개행 경로를 거부 | Passed (module automated) |
| INT-037 | path-drop lifetime·renderer contract | drop/confirmation을 renderer instance, active session ID·generation에 결합하고 전환·restart·remove 후 입력 0건, additional object 경로만 사용·file read API 미사용·source/dist 일치 | Passed (module automated + renderer distribution contract) |
| INT-038 | terminal URL open safety contract | HTTP/HTTPS·길이·user-info 검증, Ctrl+click marker, 실제 target 확인, renderer/session 세대 결합, shell execute 실패 격리, no-fetch와 source/dist 일치 | Passed (module automated + renderer/host integration contract) |
| INT-039 | PowerShell 새 session bootstrap readiness | profile을 건너뛰지 않는 startup bootstrap, 내부 문자열/`>>` 부재, 느린 profile output/custom·nested prompt, 첫 성공·다음 실패 start/finish 각 1회와 bounded fallback | Passed (PowerShell 7/Windows PowerShell actual ConPTY + unit/runtime, 2026-09-16) |
| INT-040 | 앱 비동기 정상 종료 순서 | 중복 종료 요청을 하나로 합치고 terminal 정리 완료 뒤 WPF shutdown, 정리 실패는 진단 후 exit code 1 | Passed (automated sequence/wiring contract, 2026-09-16) |

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
| SMK-006 | tray 종료 | 접근 가능한 `Starboard 종료` menu 실행 뒤 8초 이내 process 종료 | Pending recheck — 배포 v0.2.1에서 창이 사라진 뒤 `Starboard.exe`가 남는 사용자 제보를 2026-09-16 확인; 비동기 선행 정리 수정과 자동 회귀 test는 통과했으며 새 배포본 실제 tray 입력 재검증 필요 |
| SMK-007 | normal z-order | `WS_EX_TOPMOST` 없음, background expand/collapse focus 유지 | Passed |
| SMK-008 | global panel 호출 | 비활성→표시·foreground, 활성→숨김 | Passed |
| SMK-009 | 실제 tray icon 왼쪽 클릭 | 숨긴 panel 표시·foreground, normal z-order 유지 | Passed |
| SMK-010 | P4 build 전체 executable/tray 재검증 | 이번 build로 tray·호출·terminal 수명 확인 | Blocked (existing instance) |
| SMK-011 | versioned portable package | ZIP/SHA-256 일치, 필수 renderer/license/metadata 포함, runtime/user/developer data 제외 | Passed (automated package, 2026-09-11) |
| SMK-012 | extracted portable smoke mode | 추출 executable 시작, version/commit·renderer·기본 shell 확인 후 user settings 적용 없이 종료 | Passed (automated package, 2026-09-11; WebView2 UI 초기화는 MAN-039와 별도) |

## Manual desktop matrix

각 실행에서 OS build, monitor topology, scaling, taskbar edge/auto-hide, shell과 app
build hash를 함께 기록한다.

### 2026-09-15 terminal convenience release gate

아래 항목은 이 release gate에서 실제 UI/OS 환경을 조작하지 않았다는 기록이다. WSL query와
launch fallback, height/DPI geometry와 duplicate session lifetime은 fake process, pure geometry,
renderer protocol 및 ConPTY integration으로만 검증됐다.

| ID | Scenario | 확인 내용 | 상태 |
| --- | --- | --- | --- |
| MAN-050 | WebView2 terminal convenience UI | `+`/profile menu/tab menu와 top-edge drag의 mouse·keyboard, 좁은 폭, Korean IME, duplicate 뒤 기존 scrollback·input/focus 보존 | Not run — 실제 WebView2 terminal UI 입력 자동화를 수행하지 않았음 |
| MAN-051 | installed WSL profile | 실제 `wsl.exe`/distribution discovery, Linux home launch, resize, exit와 query/launch failure message·retry | Not run — 이 환경에서 WSL 설치 상태를 변경하거나 terminal UI를 조작하지 않았음 |
| MAN-052 | DPI, monitor, taskbar and focus | 100/125/150/200% 및 mixed DPI, monitor 이동, taskbar edge/auto-hide, fullscreen과 background focus preservation | Not run — display/taskbar/foreground를 변경하거나 panel을 시각적으로 관찰하지 않았음 |

### 2026-09-16 WSL·터미널 편의 실환경 검증 시도

이 실행은 assigned run `20260916-024929-604976-4bda31f4`의 문서 검증이다. Windows
interactive terminal UI를 조작하지 않고, WSL discovery는 읽기 전용 명령으로만 확인했다.
`wsl.exe --status`와 `wsl.exe --list --verbose`는 모두 exit code 1 및
`Wsl/EnumerateDistros/Service/E_ACCESS_DENIED`를 반환했다. 따라서 WSL이 미설치되었거나
배포판이 없다고 판정하지 않으며, 설치된 distribution 이름·기본 Linux home·실행 가능 여부도
미확인이다.

| ID | Scenario | 실제 관찰 또는 시도 | 상태 |
| --- | --- | --- | --- |
| MAN-050 | WebView2 terminal convenience UI | `+`/profile menu/tab menu, top-edge drag, mouse·keyboard, 좁은 폭, Korean IME와 duplicate 뒤 기존 scrollback·input/focus 보존을 실제 Starboard terminal UI에서 확인해야 한다. 이 실행 환경의 Windows automation 안전 정책은 terminal application 조작을 금지하므로 panel을 열거나 input/drag를 주입하지 않았다. | Blocked — 자동 renderer/protocol·ConPTY 결과를 실제 WebView2 상호작용 결과로 대체하지 않음 |
| MAN-051 | installed WSL profile | `wsl.exe --status`, `wsl.exe --list --verbose`를 실행했으나 둘 다 `Wsl/EnumerateDistros/Service/E_ACCESS_DENIED`로 실패했다. distribution discovery, 선택, Linux home 시작, resize/exit, query·launch 오류 화면과 retry를 실행하지 못했다. | Blocked — WSL service/distribution enumeration 권한 또는 상태를 이 session에서 읽을 수 없음 |
| MAN-052 | DPI, monitor, taskbar and focus | panel 높이 drag, expand/collapse 복원, foreground focus 보존과 100/125/150/200% 및 mixed-DPI/taskbar edge 조합은 terminal UI 및 display/taskbar 조작을 요구한다. 이 환경에서는 해당 조작을 수행하지 않았다. | Blocked — 실제 panel geometry·focus를 관찰하지 않았으며 자동 geometry test는 실화면 통과 근거가 아님 |

재개 절차는 targetable Starboard panel을 제공하는 interactive desktop에서 수행한다.

1. 동일한 일반 사용자 session에서 `wsl.exe --status`와 `wsl.exe --list --verbose`가 성공하는지 먼저 확인하고, 목록의 각 배포판 이름을 기록한다. 실패하면 Windows Features의 WSL/Virtual Machine Platform, `LxssManager` 상태와 사용자 WSL 권한을 복구한 뒤 다시 조회한다.
2. Starboard `▾`에서 각 WSL profile을 선택해 새 탭을 열고 Linux home의 `pwd`, resize, `exit`, 실패 안내와 retry를 확인한다. Windows 시작 폴더를 Linux 경로로 변환하지 않는 계약도 함께 확인한다.
3. WSL 탭에서 시작 폴더 marker를 만든 뒤 `이 탭 구성 복제`를 실행한다. 새 PID/session에서 같은 profile·설정 시작 폴더만 이어지고, 원본 PID·input·scrollback·focus가 보존되는지 확인한다.
4. 100/125/150/200%와 mixed-DPI, bottom/top/left/right 및 auto-hide taskbar에서 top-edge drag와 expand/collapse를 반복한다. bottom anchor와 저장된 collapsed height 복원, background reconciliation 중 foreground HWND 보존을 screenshot과 HWND 기록으로 남긴다.
5. WebView2에서 mouse selection, keyboard navigation, Microsoft Korean IME 조합/확정/backspace, `Ctrl+C` interrupt와 renderer/shell 오류 뒤 취소·retry 경로를 실제 화면에서 확인한다.

### 2026-09-10 release·recovery 격리 검증

- 대상: run `20260910-055029-889715-ee5de9ac`, worktree HEAD
  `456049b862b6bfdf60c58722a2c223fe680c1d62`. 기존 바탕화면 배포본
  `C:\Users\round1studio_14\Desktop\Starboard-win-x64\Starboard.exe` PID
  102920은 모든 단계 전후 같은 경로와 PID로 살아 있었고 종료·교체하지 않았다.
  테스트 파일은 ignored `out/manual-release-recovery/20260910-055029` 아래에만 만들었다.
- package 흐름은 최초 sandbox restore에서 NuGet vulnerability source에 접근하지 못해
  `NU1900`으로 중단됐고, 허용된 network에서 다시 실행해 Release build 경고·오류 0개,
  301개 test(Architecture 7, Terminal 153, DesktopIntegration 75, Preferences 31,
  Integration 35), 499-file ZIP, 추출 smoke를 통과했다. ZIP SHA-256은
  `29222a0d423870430cd3a2b1b67f668ba85cef80e51658fd085e96d27372b546`다.
- PowerShell 7 PID 86092와 Windows PowerShell PID 21984는 각각 같은 process에 여러
  명령을 보내 환경 값, `%TEMP%` working directory와 `Get-History` count 5가 유지됨을
  확인했다. cmd PID 92736도 환경 값과 `%TEMP%` working directory를 후속 명령에서
  유지했다. 이 probe는 renderer/ConPTY UI가 아닌 redirected process이므로 terminal
  resize, IME와 cmd Unicode 표시는 통과 근거가 아니다. Release suite의 실제 ConPTY
  GUI-host test는 PowerShell tab의 exit/restart/close와 다른 tab 격리를 별도로 통과했다.
- 실행 중인 사용자 instance와 `Local` mutex가 충돌하므로 현재·이전 배포 복사본의
  `Starboard.dll`에서 mutex 문자열 한 곳만 같은 길이의 test 전용 이름으로 바꾼 계측본을
  사용했다. 그 외 package 파일은 수정하지 않았다. 계측 current에서 실제 PowerShell 7,
  ConHost와 WebView2 renderer가 생성됐다. 새 renderer PID 92216만 종료한 뒤 test host
  PID 78416과 shell PID 96840이 계속 살아 있었고 log에는
  `RendererProcess / RenderProcessExited`가 기록됐다. 별도 실행에서 shell PID 99316만
  종료한 뒤에도 test host PID 97244가 유지되고 `ShellExit`가 기록됐다. 실제 오류 surface,
  `다시 시작` click과 다른 UI tab 상태는 terminal UI 자동화 금지로 관찰하지 못했다.
- WebView2 Runtime 누락은 계측 child에만 빈
  `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER`를 주입했다. 첫 실행은 6초 동안 host만 살아 있고
  renderer/shell과 terminal 오류 log가 없었으며, 반복 실행은 20초 안에 종료되고
  `ShutdownFlush`만 기록해 기대한 local 오류·설치 안내를 확인하지 못했다. system Runtime을
  제거하거나 기존 WebView2 user data를 이동하지 않았으며 MAN-030은 통과가 아니다.
- 바탕화면 배포 commit `a225da3b9c34ea0a264c095dbb73404efeb20b6e`와 current commit을
  각각 499-file 격리 폴더로 복사했다. test 전용 mutex를 적용한 `이전 → 현재 → 이전`
  smoke는 서로 다른 PID 67912, 45944, 65900에서 모두 exit code 0이었다. 전후
  `settings.json`, backup, workspace primary/backup과 HKCU Run `Starboard`는 모두
  absent로 동일했다. 따라서 side-by-side 교체·복귀와 user data 비생성만 확인했으며,
  실제 설정 보존, enabled startup 경로 재등록과 schema downgrade는 미검증이다.
- runtime network를 system 수준에서 차단하지 않았고 계측본은 기존 user data folder를
  공유했다. package의 local renderer/CSP/no-remote-asset 자동 계약 기록은 보존하지만,
  실제 network-disabled WebView2 화면은 이번 수동 배포 검증 범위에서 제외했으므로
  MAN-032는 `Out of scope`다.

### 2026-09-10 설정·작업공간 실환경 시도

- 환경: Windows interactive session, 96 DPI(100%)로 보고됨. 바탕화면 portable
  `C:\Users\round1studio_14\Desktop\Starboard-win-x64\Starboard.exe`가 PID 102920로
  실행 중이었다. `%LOCALAPPDATA%\Starboard`에는 Logs와 WebView2 user data만 있었고
  `settings.json`, `workspace.json`, `workspace.json.bak` 및 HKCU Run `Starboard` 값은
  모두 없었다.
- 실제 UI 제어 결과: Computer Use의 targetable window 목록과 app 목록에 Starboard
  window가 나타나지 않았다. 따라서 tray 메뉴와 설정 창을 열거나 terminal UI를 입력할
  수 없었다. OS/process CIM 조회도 access denied여서 child shell PID와 working
  directory를 독립적으로 읽지 못했다.
- 이 결과는 현재 실행본이 설정·복원 데이터를 아직 만들지 않았다는 읽기 전용 관찰일
  뿐이다. 설정을 변경하거나 Run 값을 등록/해제하지 않았고, 기존 terminal session과
  사용자 파일을 변경하지 않았다. 아래 항목은 UI 접근 가능한 interactive desktop에서
  재수행해야 한다.

| ID | Scenario | 확인 내용 | 상태 |
|---|---|---|---|
| MAN-001 | cold launch while editor focused | editor focus 유지, panel 표시 | Blocked — 기준 editor UI 제어 미승인 및 terminal app 자동화 금지; cold launch/화면 관찰 불가 |
| MAN-002 | terminal click | 한 번의 click으로 caret/IME 입력 가능 | Blocked — terminal UI input automation policy |
| MAN-003 | background reposition | foreground HWND 변화 없음 | Blocked — interactive foreground/HWND 관찰 불가; `Shell_TrayWnd=0x0` |
| MAN-004 | bottom taskbar | 겹침과 1px gap/overlap 오류 없음 | Blocked — 1080px 화면/1032px work area는 확인; panel 시각 관찰 불가 |
| MAN-005 | top taskbar | 올바른 edge에 표시 | Blocked — 현재 bottom 환경뿐이며 taskbar 위치 변경/관찰 권한 없음 |
| MAN-006 | left taskbar | geometry와 usable content 확인 | Blocked — 현재 bottom 환경뿐이며 taskbar 위치 변경/관찰 권한 없음 |
| MAN-007 | right taskbar | geometry와 usable content 확인 | Blocked — 현재 bottom 환경뿐이며 taskbar 위치 변경/관찰 권한 없음 |
| MAN-008 | taskbar auto-hide reveal/conceal | idle은 따라가고 active는 유지 | Blocked — auto-hide 설정 변경과 terminal active/idle 입력·관찰 불가 |
| MAN-009 | taskbar monitor 이동 | panel이 새 monitor로 이동 | Blocked — monitor 2대는 확인; taskbar 이동과 panel 관찰 권한 없음 |
| MAN-010 | secondary monitor negative coordinate | 잘못된 primary clamp 없음 | Blocked — `DISPLAY1` X=-1920은 확인; panel을 보조 화면에 배치·관찰 불가 |
| MAN-011 | monitor disconnect/reconnect | visible monitor의 안전 frame으로 복구 | Blocked — monitor 2대는 확인; 원격 실행에서 물리 분리·재연결 불가 |
| MAN-012 | 100% scaling | 선명한 text와 정확한 geometry | Blocked — 현재 DPI 96(100%)은 확인; panel/text 시각 관찰 불가 |
| MAN-013 | 125% scaling | 선명한 text와 정확한 geometry | Blocked — 125%로 변경 가능한 interactive display 권한 없음 |
| MAN-014 | 150% scaling | 선명한 text와 정확한 geometry | Blocked — 150%로 변경 가능한 interactive display 권한 없음 |
| MAN-015 | 200% scaling | 최소 terminal row와 geometry 유지 | Blocked — 200%로 변경 가능한 interactive display 권한 없음 |
| MAN-016 | mixed-DPI monitor 이동 | DPI 변경 후 즉시 선명하게 reflow | Blocked — 두 화면 모두 현재 100%; per-monitor 변경과 panel 이동 불가 |
| MAN-017 | Explorer restart | taskbar tracking과 z-order 복구 | Blocked — interactive taskbar HWND/Starboard 관찰 불가 상태에서 사용자 shell 재시작 미수행 |
| MAN-018 | maximized application | panel visible, app focus 유지 | Blocked — 기준 app UI 제어 미승인 및 foreground/panel 관찰 불가 |
| MAN-019 | borderless fullscreen | 같은 monitor에서 panel이 방해하지 않음 | Blocked — 제어 가능한 borderless-fullscreen app과 화면 관찰 권한 없음 |
| MAN-020 | exclusive fullscreen game/video | 강제 overlay 없음 | Blocked — 제어 가능한 exclusive-fullscreen app과 화면 관찰 권한 없음 |
| MAN-021 | virtual desktop switch | 지원 capability와 실제 visibility 일치 | Blocked — virtual desktop UI 제어 권한 없음; Windows-key 자동화 금지 |
| MAN-022 | expand/collapse hotkey | 현재 monitor work area 확장과 정확한 복원 | Blocked — terminal application 대상 keyboard shortcut 자동화 금지 |
| MAN-023 | hotkey conflict | settings에서 실패가 설명되고 app 유지 | Blocked — 2026-09-10 실행 중인 portable app은 Computer Use targetable window/app 목록에 노출되지 않아 tray 설정 창을 열고 충돌 등록을 유발할 수 없었음; app PID 102920는 관찰했으나 충돌 UI·session 유지 여부는 미확인 |
| MAN-024 | PowerShell 7 | persistent prompt, history, resize | Partial — PID 86092의 redirected 단일 process에서 환경·cwd·history 유지, Release ConPTY GUI-host에서 PowerShell tab restart/격리 통과; 실제 renderer prompt와 resize 미확인 |
| MAN-025 | Windows PowerShell | persistent prompt, history, resize | Partial — PID 21984의 redirected 단일 process에서 환경·cwd·history 유지; Starboard renderer/ConPTY resize 미확인 |
| MAN-026 | cmd | persistent prompt, Unicode 한계가 명시됨 | Partial — PID 92736의 redirected 단일 process에서 환경·cwd 유지; Starboard renderer/resize와 Unicode 표시는 미확인 |
| MAN-027 | WSL/custom shell | 선택한 경우 argument/resize/exit 검증 | Not run — v0.1 public 설정·workspace contract는 Automatic/Pwsh/PowerShell/Cmd만 지원하며 WSL/custom executable은 후속 범위로 deferred |
| MAN-028 | 한글 IME composition | 조합 중복·누락·caret 이탈 없음 | Blocked — terminal UI input automation policy |
| MAN-029 | clipboard shortcuts | selection copy, paste와 Ctrl+C interrupt 구분 | Blocked — terminal UI input automation policy |
| MAN-029a | safety confirmation UI | 살아 있는 tab close 취소/승인, multiline paste preview·Escape·clipboard 변경 뒤 승인 확인 | Blocked — terminal UI input automation policy |
| MAN-030 | WebView2 Runtime missing simulation | local error와 설치 안내 | Blocked — 계측 child의 빈 browser folder override에서 renderer/shell이 시작되지 않았지만 오류 surface를 관찰하지 못했고 결과도 6초 alive/20초 내 정상 종료로 일관되지 않음; system Runtime 제거는 기존 session 보호를 위해 미수행 |
| MAN-031 | login startup | 일반 user 권한으로 한 instance만 실행 | Blocked — 현재 HKCU Run `Starboard` 값은 없음을 읽기 전용으로 확인했으나, 설정 UI가 노출되지 않아 등록·재로그인·single-instance 결과를 검증할 수 없었음 |
| MAN-032 | Release folder offline | renderer가 network 없이 로드 | Out of scope — 오프라인 Release 실행은 이번 수동 배포 검증 범위에서 제외; 기존 local renderer/CSP/no-remote-asset 자동 계약 기록은 보존 |
| MAN-033 | multi-tab renderer | 비활성 탭 output/scrollback/state 유지, 대상 session routing | Blocked — 실제 renderer PID 종료 뒤 host·shell 유지와 automated ConPTY tab 격리는 확인; 오류 surface, reconnect와 다른 UI tab 상태는 terminal UI input automation policy로 미확인 |
| MAN-034 | tab 접근성·overflow·shortcut | 상태/이름/focus-visible, 8개 overflow, 탭 단축키와 Ctrl+W 전달 | Blocked — terminal UI input automation policy |
| MAN-034a | inactive-tab new output | 비활성 tab의 점, 접근성 이름, 선택 시 해제와 자동 activation 없음 | Blocked — terminal UI input automation policy |
| MAN-035 | schema 4 collapsed height | 200 DIP에서 tab strip 아래 약 8행, 사용자 높이 보존 | Blocked — terminal UI input automation policy |
| MAN-036 | terminal focus surface | terminal click 후 노란 외곽선 없이 caret과 입력 동작 유지 | Blocked — terminal UI input automation policy |
| MAN-037 | tray 설정 창 수명·focus | 연속 요청 시 한 창, 닫은 뒤 이전 foreground를 강제 변경하지 않음 | Blocked — Starboard tray/panel window를 자동화 대상에서 찾지 못해 연속 tray 요청·창 닫기·foreground 보존을 조작/관찰할 수 없었음 |
| MAN-038 | 실제 설정 live apply/rollback | theme/font/높이 적용 중 PID·cwd 유지, hotkey 충돌과 저장 실패 UI 확인 | Blocked — theme/font/height save 및 rollback UI를 열 수 없었음. 실행 중 app PID만 관찰했고 CIM process 조회가 access denied여서 기존 terminal PID·cwd 보존은 측정하지 못했음 |
| MAN-039 | portable WebView2/terminal UI | 새 폴더에서 실제 WebView2 Runtime 초기화, local renderer와 interactive shell 확인 | Out of scope — portable Release 실행은 이번 수동 배포 검증 범위에서 제외; 기존 renderer/ConHost/pwsh 및 실패 격리 관찰 기록은 보존 |
| MAN-040 | portable update/rollback | 기존 설정 유지, startup 경로 변경과 이전 폴더 복귀 확인 | Out of scope — 업데이트 및 rollback은 이번 수동 배포 검증 범위에서 제외; 기존 이전→현재→이전 smoke 기록은 보존 |
| MAN-041 | workspace restore UI | 복원 opt-in 뒤 재시작에서 탭 이름·순서·선택·시작 폴더가 보존되고 각 tab이 새 PID인지 확인 | Blocked — 현재 `workspace.json`/`.bak`은 없었고 restore opt-in 설정 UI를 조작할 수 없었음; 따라서 재시작 뒤 새 shell PID와 tab metadata 복원을 확인하지 못했음 |
| MAN-042 | workspace IME and partial failure | 한글 IME 이름 편집, 없는/권한 없는 폴더 또는 shell 한 tab 실패가 다른 tab을 막지 않는지 확인 | Blocked — terminal UI 자동화 대상이 없어 한글 이름 편집 및 실패 tab을 만든 뒤 다른 tab의 계속 시작을 관찰할 수 없었음 |
| MAN-043 | workspace opt-out | 옵션 해제 뒤 구성 파일 삭제와 다음 시작의 기본 tab 하나를 확인 | Blocked — 초기 workspace primary/backup 부재만 읽기 전용으로 확인했음; opt-out 저장으로 파일을 삭제하고 다음 시작 기본 tab을 확인하는 destructive UI 시나리오는 수행하지 못했음 |
| MAN-044 | 저장한 탭 전체 UI 흐름 | 실제 WebView2에서 저장·취소·편집·삭제·선택·실패 재시도, 앱 재실행 유지와 workspace on/off 독립성 확인 | Not run — terminal UI 입력 자동화가 허용되지 않아 실제 menu/dialog 조작과 재실행을 수행하지 않았음 |
| MAN-045 | 저장 항목 shell/폴더와 기존 xterm 보존 | 저장 항목 실행 전후 새 PID·지정 폴더/셸 및 기존 탭 PID·입력·scrollback 보존을 실제 화면에서 확인 | Not run — module/ConPTY 자동 검증은 통과했지만 실제 WebView2 scrollback과 사용자 입력 상태는 관찰하지 않았음 |
| MAN-046 | 현재 탭 출력 검색 | 200 DIP/좁은 폭에서 Ctrl+F, 한글 IME, Enter/Shift+Enter, 결과 강조/없음, Escape focus 복귀와 네 theme를 확인하고 검색 key가 shell에 전달되지 않는지 확인 | Not run — renderer distribution test는 addon·lifecycle·compact CSS를 확인했지만 실제 WebView2 keyboard/IME/theme 화면 및 shell input 관찰은 수행하지 않았음 |

| MAN-047 | Explorer file/folder path drop | PowerShell/Windows PowerShell/cmd 현재 탭에 공백·한글·특수 문자 경로를 놓고 미리보기·취소·승인, Enter 미생성, tab 전환·restart·renderer reconnect 경쟁, IME·붙여넣기·focus·scrollback 회귀를 실제 WebView2에서 확인 | Not run — additional-object/protocol/unit 검증은 통과했으나 실제 Explorer·WebView2 드래그 입력은 수행하지 않음 |
| MAN-048 | terminal 출력 URL Ctrl+click | 일반 click/drag selection 유지, Ctrl+click 실제 URL 확인·취소·기본 브라우저 열기, HTTP/HTTPS 및 OSC 8, 긴/잘못된/custom scheme 거부, 외부 실행 실패와 tab/restart/reconnect 경쟁을 실제 WebView2에서 확인 | Not run — protocol/validation/distribution 자동 검증은 통과했으나 실제 WebView2 pointer selection과 기본 브라우저 실행은 수행하지 않음 |
| MAN-049 | 현재 탭을 저장한 탭에 추가 | tab 우클릭과 Shift+F10에서 메뉴 항목에 도달하고, 이름·설정 시작 폴더·셸 prefill, 20개 한도 안내, 저장/취소 뒤 원래 tab focus와 shell PID·입력·scrollback·선택 보존을 WebView2에서 확인 | Not run — renderer distribution과 protocol/host source 자동 검증만 수행했으며 실제 WebView2, DPI 및 Korean IME 조작은 수행하지 않음 |
| MAN-050a | 새 탭 버튼 위치와 overflow | 실제 WebView2에서 1/3/8개 탭, 짧고 긴 이름, 좁은 패널과 네 theme를 확인해 마지막 탭 옆 `+`, 탭 목록만 가로 스크롤, `+`/`▾` 고정 표시와 기본 프로필 새 탭 생성을 검증 | Blocked (2026-09-16) — 이 worktree의 Debug WPF build는 성공했지만 interactive UI automation이 새로 빌드한 `Starboard.exe`를 target app으로 승인하지 않아 창을 열거나 mouse/keyboard/DPI별 화면을 관찰할 수 없었음. 자동 layout 계약을 수동 결과로 대체하지 않음 |
| MAN-050b | 새 탭 profile menu와 viewport 폭 | 실제 WebView2에서 `+` mouse/Enter/Space로 PowerShell 7, Windows PowerShell, Command Prompt 및 설치된 WSL profile을 선택해 새 탭이 선택 뒤에만 생기는지 확인한다. `▾`에는 저장/관리만 있는지, Escape·바깥 click focus 복귀, `Ctrl+Shift+T` 기본 profile 즉시 생성, 1/3/8개 탭과 menu/resize 전후 panel·workspace·mount·xterm viewport 폭 및 scrollbar 오른쪽 끝을 네 theme와 100/125/150/200% DPI에서 확인한다. | Not run (2026-09-16) — renderer distribution test는 두 독립 menu 상태, clipping CSS와 xterm 가로 fill/fit 경로를 확인하지만, targetable WebView2 window·WSL 설치 상태·DPI별 실제 rect와 scrollbar는 이 환경에서 관찰하지 못했음. |
| MAN-053 | PowerShell bootstrap 화면·입력 | WebView2에서 PowerShell 7/Windows PowerShell 새 탭 5회, profile 출력/custom prompt, 즉시 첫 명령, 한글 IME, `Ctrl+C`, nested prompt와 알림 on/off를 관찰 | Blocked (2026-09-16) — interactive UI automation이 새로 빌드한 `Starboard.exe` 실행을 허용하지 않아 두 PowerShell의 5회 새 탭, 즉시 입력, Korean IME, `Ctrl+C`, 동일 session generation 완료 알림을 관찰하지 못했음. 기존 실제 ConPTY 자동 검증은 대체 근거가 아님 |
| MAN-054 | 넓은 패널 높이 조절 영역 | 1/3개 탭에서 `+`와 `▾` 사이 중립색 손잡이, `ns-resize`, 빈 영역 drag와 bottom anchor를 확인하고, 좁은 폭·8개 탭에서는 손잡이가 사라지며 탭·`×`·`+`·`▾` 입력과 기존 6 DIP top-edge fallback이 정상인지 네 theme와 100/125/150/200% DPI에서 확인 | Not run (2026-09-16) — renderer build와 source/dist 계약, global protocol, collapsed/expanded native 시작 정책 및 전체 529개 자동 test는 통과했다. Computer Use 안전 규칙이 terminal 앱 자동 조작을 금지하므로 실제 WebView2 pointer drag, cursor, focus와 화면 상태는 수동 확인으로 남김 |

### 2026-09-16 Windows 플랫폼·설정 UI 재검증 시도

실행 환경은 Windows 10 Pro 25H2 (build `26200.9457`), AMD64, interactive session 1이다.
읽기 전용 probe에서 Explorer (PID 9980)와 Starboard (PID 31732)가 같은 session에서 실행 중인 것,
HKCU Run `Starboard` 값이 `C:\Users\round1studio_14\Desktop\Starboard-win-x64\Starboard.exe`를
가리키는 것, `%LOCALAPPDATA%\Starboard`의 `settings.json`/`.bak` 및
`workspace.json`/`.bak`가 존재하는 것을 확인했다. `Win32_VideoController` 조회는 access denied로
실패했다. 따라서 이 기록은 실행·저장 상태의 실측일 뿐 화면, tray, panel 또는 terminal UI의
시각·입력 검증이 아니다.

| 범주 | 실제 관찰 또는 시도 | 결과 |
|---|---|---|
| panel/taskbar/focus | Starboard와 Explorer가 interactive session에서 실행 중임을 확인했으나, 이 실행 컨텍스트에는 Starboard window를 대상으로 한 UI automation/runtime이 노출되지 않았다. | Blocked — MAN-001, MAN-003~004, MAN-018은 기존 상태 유지; taskbar 비겹침·foreground 보존을 통과로 표시하지 않음 |
| auto-hide, taskbar edge, Explorer restart | 현재 taskbar 위치·auto-hide 상태를 시각적으로 읽을 수 없고, auto-hide 변경 및 Explorer restart는 사용자 shell을 변경한다. | Blocked — MAN-005~009, MAN-017; 해당 설정 변경·restart 미수행 |
| DPI/multi-monitor/fullscreen | 해상도·video controller probe가 access denied였고 display settings, monitor 이동·분리, fullscreen app을 제어할 수 없었다. | Blocked — MAN-010~016, MAN-019~020; 100/125/150/200%와 mixed-DPI 모두 미통과 |
| tray icon | 실행 중 process와 자동 시작 경로는 확인했지만 notification area, hidden-icons flyout, 밝고 어두운 taskbar는 보거나 열 수 없었다. | Blocked — ICON-004~006; 네 DPI의 icon 선명도와 Explorer 복구는 미확인 |
| 네 theme 설정 UI | `settings.json`과 backup은 존재하지만 editor를 열어 Dark, Light, One Dark, Tokyo Night를 save/cancel하거나 각 control 상태를 관찰할 수 없었다. | Blocked — MAN-038 및 SETTINGS-UI-01 실제 screenshot gate; 네 theme 모두 `Not run` |
| 저장·취소·rollback, shortcut, 로그인 자동 시작 | 자동 시작 registry 값은 실측했으나 이를 toggle·재로그인하지 않았고, 저장/취소/rollback UI와 global-hotkey 충돌 UI를 열 수 없었다. | Blocked — MAN-023, MAN-031, MAN-038; registry 존재는 설정 적용·복구 통과가 아님 |

재개 시에는 targetable Starboard panel과 tray를 제공하는 interactive desktop에서 다음 순서로 수행한다:
baseline foreground HWND 기록 → 네 theme를 각각 save/cancel하고 restart 전후 설정/backup 비교 →
이미 점유한 global shortcut으로 conflict UI 및 기존 등록 유지 확인 → startup toggle 뒤 HKCU Run과
재로그인 single-instance 확인 → 100/125/150/200%와 mixed-DPI에서 tray/panel screenshot → auto-hide,
각 taskbar edge, Explorer restart, maximized/borderless/exclusive fullscreen을 차례로 확인한다.

### 2026-09-16 terminal UI 수동 검증 시도

이 task 전용 worktree에서 `Starboard.Windows` Debug restore와 build는 성공했다. 그러나 Windows
UI automation provider가 해당 새 `Starboard.exe`를 승인하지 않아 targetable window를 생성할 수
없었다. 이 제한을 다른 automation 또는 사용자 shell 변경으로 우회하지 않았다.

따라서 다음 실제 UI 시나리오는 모두 **Blocked**이며 자동 test 결과로 판정하지 않는다.

- PowerShell 7, Windows PowerShell, Command Prompt와 설치된 WSL 중 하나를 `+` profile menu에서 선택해
  새 탭이 선택 뒤에만 생기는지, `Ctrl+Shift+T`는 기본 profile을 즉시 만드는지 확인
- 좁은 panel의 1/3/8개 탭에서 마지막 tab 바로 뒤 `+`, 오른쪽 끝 `▾`, 두 menu의 Escape/바깥 click
  focus 복귀와 저장 탭 menu에 profile/WSL 상태가 없는지 확인
- panel/workspace/mount/xterm viewport client rect와 scrollbar 오른쪽 끝을 menu open/close 및 resize 전후에 비교
- Korean IME 조합·확정, 선택 없는 `Ctrl+C` interrupt, 같은 session generation의 한 번뿐인 완료 알림을
  실제 terminal/Windows notification으로 확인

재개 조건은 PowerShell 7, Windows PowerShell, Microsoft Korean IME와 WebView2 Runtime이 설치된
interactive desktop에서 Starboard panel을 targetable window로 열 수 있는 것이다. 그 환경에서 각 shell의
5회 반복 결과와 명령/interrupt/notification generation, 그리고 100/125/150/200% DPI별 tab-strip
screenshot을 별도로 기록해야 한다.

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

portable 생성·checksum·추출 smoke에는 아래 자동 배포 gate를 적용한다. 실제
WebView2/terminal UI, 오프라인 Release 화면, 업데이트와 rollback은 별도 수동 gate이며,
자동 package 통과만으로 완료했다고 판정하지 않는다. 셸 선택·지속 세션 및
renderer·셸·ConPTY 실패 복구/재시작의 실제 관찰 결과도 자동 package 결과와 구분한다.

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
- 실제로 실행한 manual case만 `Passed`로 표시하며, 배포 제외 항목은
  `Out of scope`로 표시
- 미실행 DPI, multi-monitor, fullscreen과 IME case가 숨김없이 남아 있음
