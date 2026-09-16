# 다음 버전 경량 터미널 편의 기능

## 목표와 완료 조건

Starboard의 작업표시줄 위 경량 terminal 성격을 유지하면서 다음 세 기능을 추가한다.

1. `+` 옆 메뉴에서 PowerShell 계열, CMD와 설치된 WSL 배포판을 골라 새 탭을 연다.
2. 축소 패널의 위쪽 경계를 직접 끌어 96~720 DIP 안에서 높이를 바꾸고 저장한다.
3. 탭 메뉴에서 현재 탭의 실행 구성을 복제해 새 독립 session을 연다.

분할 화면, 명령 palette, shell 상태 복제와 runtime network 요청은 포함하지 않는다. 각 기능은
기존 session·focus·taskbar 밀착·DPI·개인정보 계약을 보존하고 실패가 다른 탭이나 앱 전체를
종료시키지 않을 때 완료로 본다.

## 기준선과 UX 연결

- `+`는 기본 shell로 즉시 새 탭을 만드는 빠른 경로로 유지한다. 위치 회귀 수정은
  [탭 UI 기획](2026-09-03-renderer-and-tab-ui.md)의 2026-09-14 항목을 따른다.
- 현재 `▾`는 저장한 탭만 연다. 다음 버전에는 이 control을 `새 탭 및 저장한 탭` 메뉴로
  확장하되, `+`의 한 번 클릭 동작을 메뉴로 바꾸지 않는다.
- 설정에는 96~720 DIP의 축소 높이 값이 이미 있고 적용·저장 경로도 존재한다. 새 기능은 같은
  값을 직접 조절하는 진입점이며 별도의 높이 상태나 설정 key를 만들지 않는다.
- 탭 이름·순서·시작 폴더와 저장한 탭은 이미 지원한다. 복제는 이 구성을 새 session 생성 입력으로
  재사용할 뿐 기존 process, command, output, environment 또는 scrollback을 복사하지 않는다.
- 현재 탭 메뉴의 `저장한 탭에 추가…` 연결은
  [저장한 탭 기획](2026-09-10-saved-terminal-tabs.md)의 선행 UI 보완이다. 같은 renderer menu를
  수정하므로 복제 항목과 동시에 별도 worktree에서 편집하지 않는다.

## 기능 A — 셸 프로필과 WSL 선택 실행

### 사용자 흐름

- `+`를 누르면 지금처럼 설정된 기본 shell로 즉시 새 탭을 연다.
- 바로 옆 `▾` 메뉴의 첫 구역 `새 탭`에는 사용 가능한 `PowerShell 7`, `Windows PowerShell`,
  `명령 프롬프트`와 설치된 WSL 배포판을 표시한다. 두 번째 구역 `저장한 탭`은 기존 목록과
  관리 진입점을 유지한다.
- 실행 파일이나 배포판이 없으면 해당 항목을 만들지 않는다. WSL 실행기는 있지만 목록 조회가
  실패한 경우 빈 목록으로 가장하지 않고 `WSL 목록을 불러오지 못했습니다`와 재시도 경로를
  메뉴 안에 표시한다.
- WSL 배포판을 선택하면 해당 배포판의 기본 Linux home에서 새 session을 시작한다. Windows
  시작 폴더를 Linux 경로로 조용히 변환하지 않는다. WSL 시작 폴더 저장은 별도 경로 모델이
  필요하므로 첫 범위에서는 제외한다.
- 새 탭 한도 8개, 중복 클릭 억제, 선택된 새 탭 focus와 실패한 탭의 재시작 정책은 기존 새 탭과
  동일하다.

### 계약과 위험

- Terminal module이 builtin shell과 WSL 배포판을 공통으로 표현하는 immutable launch profile
  contract를 소유한다. 동적인 배포판 이름을 기존 `TerminalShellKind` enum에 값으로 끼워 넣지
  않는다.
- WSL 탐색은 로컬 `wsl.exe`의 문서화된 목록 명령을 인자 배열로 실행하고 timeout과 cancellation을
  둔다. 출력은 UTF-16/UTF-8 차이를 실제 환경에서 확인한 뒤 parser 정책을 고정하며 배포판 이름을
  command 문자열로 이어 붙이지 않는다.
- 탐색 결과는 실행 중 짧게 cache할 수 있지만 앱 시작을 막지 않는다. WSL 미설치·기능 비활성화·
  조회 timeout은 PowerShell/CMD 사용을 방해하지 않는다.
- WSL 탭도 ConPTY가 소유한 하나의 지속 `wsl.exe` process로 다룬다. 배포판 내부 shell을 명령마다
  다시 실행하지 않으며 앱이 Linux 배포판 설치나 업데이트를 시도하지 않는다.
- 작업공간 복원과 저장한 탭 schema에 WSL profile을 영속화하는 것은 첫 범위에서 제외한다.
  종료 후에는 WSL 탭이 기존 builtin 기본 탭으로 대체될 수 있음을 설정과 복원 안내에 명시한다.

## 기능 B — 축소 패널 높이 직접 조절

### 사용자 흐름

- 축소 상태에서 panel 위쪽 경계에 resize cursor를 제공하고 위아래 drag로 높이를 조절한다.
  작업표시줄과 맞닿은 아래쪽 경계는 움직이지 않는다.
- 96~720 DIP와 현재 monitor work area 중 더 작은 범위로 제한한다. drag 중에는 실시간으로
  renderer/ConPTY 크기를 맞추되 저장은 pointer release 뒤 한 번만 수행한다.
- 위쪽 경계를 double-click하면 기본 200 DIP로 복원한다. 확장 상태에서는 resize hit target을
  비활성화하고 기존 expand/collapse geometry 복원을 우선한다.
- 설정 창에서 높이를 변경하면 기존처럼 즉시 반영되고, drag로 변경한 값도 설정 창을 다시 열면
  동일하게 표시된다. 저장 실패 시 화면은 마지막 저장값으로 복귀하고 짧은 로컬 오류를 제공한다.

### 계약과 위험

- borderless WPF window의 top-edge resize는 DesktopIntegration의 window policy와 interop 경계에서
  처리한다. view code에 Win32 constant나 message 처리를 흩뿌리지 않는다.
- pixel drag 결과를 해당 monitor DPI의 DIP로 변환한다. drag 중 monitor 또는 DPI가 바뀌면 최신
  window DPI와 work area로 다시 제한하고 taskbar를 덮지 않는다.
- 사용자가 경계를 누른 행위는 의도적 interaction이므로 활성화될 수 있지만 background geometry
  reconciliation이나 설정 저장 callback이 다른 foreground app의 focus를 가져오면 안 된다.
- auto-hide freeze, fullscreen demotion, expand/collapse와 Explorer 재시작 후 재배치 정책은 그대로
  유지한다. panel 높이 변경을 desktop work area 예약으로 구현하지 않는다.

## 기능 C — 현재 탭 구성 복제

### 사용자 흐름과 계약

- 탭 우클릭 또는 `Shift+F10` 메뉴에 `이 탭 구성 복제`를 추가한다.
- 선택하면 현재 탭의 표시 이름, 설정된 시작 폴더와 launch profile을 초기값으로 새 탭 하나를
  만들고 선택한다. 이름은 `기존 이름 (2)`처럼 충돌 없이 표시하되 사용자가 나중에 바꿀 수 있다.
- 실행 중 `cd`로 바뀐 실제 working directory는 추측하지 않는다. 원본 tab process, PID, 입력,
  history, output, scrollback, environment와 실행 중인 job은 복제하지 않는다.
- 원본 탭이 WSL이면 같은 배포판 profile로 새 session을 시작한다. 그 사이 배포판이나 executable을
  찾을 수 없으면 새 탭만 오류 상태로 남기고 원본 session은 유지한다.
- 실행 탭이 8개면 항목을 비활성화하고 이유를 접근 가능한 안내로 제공한다. 빠른 중복 입력은
  request ID와 session generation으로 한 번만 처리한다.

## 범위와 영향 파일

| 영역 | 예상 영향 | 책임 |
|---|---|---|
| Terminal contracts/application | launch profile, WSL discovery/resolve, duplicate request와 session 생성 | 동적 profile과 지속 session 수명 |
| Terminal infrastructure | bounded `wsl.exe` 목록 조회와 실행 adapter | process 인자·encoding·timeout·오류 격리 |
| Renderer source/dist | `▾` 구역, profile 선택, 탭 복제 menu와 접근성 | 32 DIP tab strip을 유지하는 입력 흐름 |
| DesktopIntegration | top-edge hit test, bottom anchor, DPI/work-area clamp | borderless resize와 focus 비방해 |
| Preferences/host | 기존 `CollapsedHeightDip` 저장·동기화 | 새 schema 없이 원자 저장 경로 재사용 |
| Tests/docs | module·integration·renderer·geometry test, README와 test plan | 자동/simulation/실화면 결과 구분 |

구체적인 type과 파일은 구현 전 symbol 검색으로 확정한다. `Starboard.Windows`는 module을 조정하는
composition root 역할만 유지하고 WSL process 탐색이나 resize 계산을 직접 소유하지 않는다.

## 병렬 구현과 합류 순서

1. **P0 계약 gate(선행):** launch profile 식별자, duplicate request/result, 높이 변경 callback과
   실패 정책을 확정한다.
2. **P1-A Terminal backend:** WSL discovery/resolve와 profile 기반 새 session·복제 로직 및 tests.
3. **P1-B Desktop 높이:** geometry/DPI 순수 계산, top-edge adapter, 설정 저장 연결 및 tests.
4. **P1-C Renderer UX:** `▾` 구역과 탭 복제 UI. P0 뒤 P1-A와 병렬 가능하지만 기존 `+` 위치 회귀와
   `저장한 탭에 추가…`도 같은 파일을 수정하므로 세 renderer 변경은 한 작업자가 직렬 통합한다.
5. **P2 host 통합:** module event와 settings 수명을 composition root에서 연결하고 stale callback을
   차단한다.
6. **P3 통합 검증·문서:** renderer source/dist 일치, 전체 suite, portable 개인정보 제외와 실제 UI
   수동 항목을 갱신한다.

P1-A와 P1-B는 파일 소유권이 겹치지 않아 병렬 처리한다. P1-C는 Terminal contract를 소비하지만
backend concrete type을 참조하지 않는다. 합류는 P0 → P1-A/P1-B/P1-C → P2 → P3 순서다.

## 검증 방법

- WSL 미설치, 설치됐지만 배포판 없음, 1개/여러 배포판, Unicode 이름, timeout과 조회/실행 실패를
  fake process adapter로 자동 검증하고 실제 설치 환경 결과와 구분한다.
- builtin/WSL profile 선택, 8개 한도, 중복 요청, 실패 격리, restart/remove/dispose 이후 stale
  callback 거부와 원본 탭 PID 유지를 검증한다.
- 복제 전후 session ID와 PID가 다르고 이름·설정 시작 폴더·profile만 이어지는지 확인한다.
- 96/200/720 DIP, work area보다 작은 상한, 100/125/150/200%와 mixed-DPI 이동의 bottom anchor와
  px↔DIP round trip을 자동 또는 simulation으로 검증한다.
- 실제 WebView2에서 `+`/`▾`/tab menu의 mouse·keyboard·IME, 좁은 폭·1/3/8개 탭과 drag cursor,
  taskbar 겹침·focus 변화를 확인한다. 실행하지 않은 DPI/monitor/WSL 실화면은 `Not run`으로 남긴다.
- 제품 코드 구현 시 renderer rebuild 후 source/dist contract, Debug restore/build/전체 test와 portable
  Release 경로를 실행한다. 이번 문서 작업은 내용·링크와 `git diff --check`만 확인한다.

## 진행 기록

- [x] 2026-09-14: 셸 프로필/WSL 선택, 패널 높이 drag와 탭 구성 복제를 다음 버전 후보로 구체화했다.
- [x] 기존 `+` 위치 회귀와 현재 탭 저장 항목의 renderer 파일 충돌을 확인해 직렬 합류 조건으로 기록했다.
- [x] P0 계약 gate와 구현 작업 완료: immutable launch profile, WSL discovery/resolve,
  duplicate request generation 및 collapsed-height callback을 각 module contract에 고정했다.
- [x] P1-A/P1-B/P1-C와 P2 host 통합 완료: builtin/WSL profile menu, top-edge height drag와
  현재 탭 구성 복제가 기존 session, focus, taskbar/DPI 정책을 보존하는 경로로 합류했다.
- [x] P3 자동·portable gate 완료: renderer source/dist rebuild, Debug solution suite 및
  self-contained portable publish/ZIP/checksum/extraction smoke를 실행했고 package에 settings,
  workspace, saved tabs, logs, WebView2 data 및 command/output capture가 없음을 검사했다.
- [ ] 실제 WSL 설치 환경, 다중 DPI/monitor/taskbar/focus 및 WebView2 mouse·keyboard·IME 수동 검증. 2026-09-16 assigned run에서 `wsl.exe --status`와 `wsl.exe --list --verbose`가 모두 `Wsl/EnumerateDistros/Service/E_ACCESS_DENIED`로 실패했고 terminal UI automation은 정책상 수행할 수 없어, 결과를 `docs/test-plan.md`의 MAN-050~052에 Blocked로 기록했다.

## 완료 요약

구현과 자동 release gate를 완료했다. WSL profile은 목록 조회와 새 session 실행에만 사용하며,
첫 범위에서는 workspace 또는 saved-tabs schema에 저장하지 않는다. WSL을 선택한 tab은 앱을
다시 시작하거나 workspace를 복원할 때 builtin 기본 tab으로 대체될 수 있다. `wsl.exe` 또는
배포판 조회가 실패하면 builtin profile은 계속 제공되고 메뉴에 실패 상태와 재시도 경로를 보인다.
실제 WebView2 입력·IME, 설치된 WSL, DPI/monitor/taskbar/focus 시나리오는 자동 결과와 혼동하지
않도록 test plan에서 `Blocked` 또는 `Not run`으로 유지한다. 2026-09-16 assigned run은 WSL
enumeration access denial과 terminal UI automation 제한 때문에 MAN-050~052를 통과로 바꾸지
않았으며, 재개 절차는 test plan에 기록했다.
