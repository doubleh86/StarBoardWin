# Starboard 다음 기획 — 가벼운 작업공간

## 목표와 완료 조건

자주 쓰는 프로젝트 터미널을 이름으로 구분하고, 앱을 다시 켰을 때 같은 탭 구성으로
작업을 시작할 수 있게 한다. 기존의 얇은 패널과 최대 8개 탭을 유지한다.

이 문서는 [이전 실사용 개선 기획](2026-09-05-orchestrator-product-roadmap.md)의 후속
후보인 탭 이름·순서 변경과 구성 복원을 구체화한 제안이다. 이번 요청은 **기획서 작성만**
대상이며 구현·에이전트 실행·Git 작업·배포를 시작하지 않는다.

핵심 완료 조건은 다음과 같다.

- 탭 이름과 순서를 바꿔도 기존 shell PID, 입력 상태와 scrollback이 유지된다.
- 탭별 시작 폴더를 명시적으로 지정할 수 있다.
- 사용자가 복원을 켰을 때만 이름·순서·시작 폴더·셸 종류·선택 탭을 로컬에 저장한다.
- 재실행은 저장된 구성으로 **새 shell**을 만든다. 이전 명령·프로세스·출력은 복원하지 않는다.
- 한 탭의 복원 실패가 다른 탭이나 앱 전체의 실행을 막지 않는다.

## 현재 상태와 근거

2026-09-09 로컬 `main`, 기반 HEAD `a0607c5`와 미커밋 작업 트리를 확인했다.
코드 스타일 동기화와 하단 간격 변경을 포함한 배포본은 Release 193개 테스트를
통과했다. 실제 화면·IME·mixed-DPI 등 남은 수동 항목은
[테스트 계획](../test-plan.md)을 따른다. 이 문서 작성 중 테스트를 재실행한 것은 아니다.

- `TerminalTabRegistry`는 최대 8개 탭의 생성·선택·닫기와 기본 이름을 관리한다.
  마지막 탭을 닫으면 대체 탭 하나를 만드는 기존 정책을 유지한다.
- `TerminalSessionCoordinator`가 탭별 세션과 생성 당시 셸을 관리한다.
- `ShellResolver`의 현재 시작 폴더는 사용자 홈이다. 실행 중인 셸의 현재 경로를
  추적하는 계약은 없다.
- renderer에 탭 UI와 생성·전환·닫기 단축키가 있고, 이름·순서 편집 기능은 없다.
- Preferences 설정은 현재 schema 5이며 작업 구성 복원 옵션은 없다.
- 탭 구성은 앱 재시작 후 복원되지 않는다. renderer만 재생성되는 복구와 앱을
  종료했다 켜는 구성 복원은 서로 다른 경로다.

현재 미커밋 변경과 `.ai-orchestrator/` 데이터는 보존한다. 구현 착수 시 최신 HEAD와
필요한 변경의 포함 여부를 다시 확인하며, HEAD만으로 현재 배포본과 같다고 가정하지 않는다.

## 사용자 경험

예: 서버 탭은 `서버`, 프런트엔드 탭은 `웹`, 임시 작업은 `메모`로 이름을 붙인다.
각 탭에 시작 폴더를 지정하고 복원을 켜면 다음 실행에서도 이 순서로 새 셸이 열린다.
서버 실행 명령은 사용자가 직접 입력한다.

### 1. 탭 정리

- 탭의 우클릭 메뉴에 `이름 변경`, `왼쪽으로 이동`, `오른쪽으로 이동`,
  `시작 폴더 설정`을 제공한다. 키보드 사용자는 탭에 focus를 둔 상태에서
  `Shift+F10`으로 같은 메뉴를 연다. terminal 본문의 shell 입력과 구분한다.
- 이름 편집은 작은 인라인 편집기로 한다. Enter로 확정, Escape로 취소한다.
  한글 IME 조합 중 Enter는 확정 동작으로 오인하지 않는다.
- 이름은 앞뒤 공백을 제거하고 1~32개의 사용자 인식 문자로 제한한다.
  제어 문자와 줄바꿈은 거부한다. 중복 이름은 허용하며 내부 식별자는 이름과 분리한다.
- 빈 이름은 오류를 표시하며 기존 이름을 유지한다. 긴 이름은 말줄임하고 tooltip과
  접근성 이름으로 전체 문자열을 제공한다. HTML로 해석하지 않는다.
- 자동 이름은 실제 셸 종류와 번호를 사용한다. cmd 탭이 PowerShell로 표시되지 않게 한다.
- 이동은 좌우 한 칸씩이며 양끝에서는 해당 메뉴를 비활성화한다. 활성 탭은 식별자로
  유지하고 DOM 재생성·shell 재시작 없이 순서만 바꾼다. 드래그 정렬은 제외한다.

### 2. 시작 폴더

- 시작 폴더는 사용자가 입력하거나 로컬 폴더 선택기로 지정한 경로다.
  초기값은 사용자 홈이며, 편집 UI에 `다음 셸 시작부터 적용`을 표시한다.
- 현재 탭에서 수행한 `cd`를 추적하거나 저장하지 않는다. 설정 변경 때문에 실행 중인
  셸에 `cd`를 주입하거나 자동 재시작하지 않는다. 기존 재시작 경로에는 새 시작 폴더를 적용한다.
- 입력은 존재하는 로컬 절대 디렉터리로 제한한다. 상대 경로, UNC·장치 경로,
  환경 변수·명령 치환은 이번 범위에서 지원하지 않는다. 공백·한글 경로는 지원한다.
- 폴더는 process의 working-directory 인자로 전달한다. shell command 문자열로 조합하지 않는다.
- 저장 후 폴더가 삭제되거나 접근할 수 없으면 해당 탭만 오류 상태로 두고,
  `폴더 변경 후 다시 시도` 또는 명시적인 `홈 폴더에서 시작` 경로를 제공한다.
  사용자가 정한 경로를 오류 때문에 조용히 덮어쓰지 않는다.

### 3. 다음 실행 시 작업 구성 복원

- 설정 창 `동작`에 `다음 실행 시 탭 구성 복원`을 추가한다. 기본값은 꺼짐이다.
  `이름·순서·시작 폴더를 저장합니다. 실행 중인 명령과 출력은 복원하지 않습니다`를 안내한다.
- 켜면 현재 구성부터 저장하고, 이후 생성·닫기·이름·순서·폴더·선택 변경을 저장한다.
  출력, resize, 상태 알림마다 파일을 쓰지는 않는다.
- 저장 항목은 schema version, 안정적인 구성 ID, 탭 순서, 이름, 시작 폴더,
  지원 셸 종류와 활성 구성 ID뿐이다. runtime session ID와 구성 ID는 분리한다.
- 지원 셸 종류는 `자동`, `pwsh`, `powershell`, `cmd`다. 자동은 다음 실행 시 다시 탐색하고,
  명시된 셸을 찾지 못하면 그 탭에 오류와 셸 재선택 경로를 제공한다.
  임의 executable·arguments 저장은 제외하며 지원 밖 셸은 저장 전에 알려 준다.
- 시작 시 구성을 읽어 최대 8개 탭을 순서대로 만들고 저장된 선택 탭을 선택한다.
  창을 foreground로 가져오지는 않는다. 한 탭 실패 후에도 나머지를 계속 복원한다.
- 새 탭과 복원 탭에는 새 session ID와 process가 부여된다. 이전 명령, environment,
  history, scrollback, 실행 중 job은 저장하거나 재실행하지 않는다.
  일반 셸의 기존 profile/history 동작까지 차단하거나 복원한다고 주장하지 않는다.
- 복원을 끄고 저장하면 현재 세션은 유지하되 보관된 작업 구성과 그 백업을 제거한다.
  삭제 실패는 사용자에게 알리고 저장 데이터가 남아 있음을 표시한다. 다음 시작은
  복원 옵션이 꺼진 상태를 우선해 기본 탭 하나만 생성한다.

## 저장과 실패 정책

- Terminal이 `%LOCALAPPDATA%/Starboard/workspace.json`의 읽기·검증·원자적 저장과
  수명 정리를 소유한다. Preferences는 복원 옵션만 저장한다.
- 쓰기는 단일 writer로 직렬화하고 짧은 debounce로 묶는다. 종료 시 제한 시간 내
  최신 변경을 flush한다. UI thread에서 파일 I/O를 기다리지 않는다.
- 임시 파일 작성 후 원자적 교체 및 마지막 정상 백업을 사용한다. 손상 파일은 정상
  백업으로 복구하고 둘 다 유효하지 않으면 기본 탭으로 시작하며 이유를 표시한다.
- 미래 schema는 덮어쓰지 않고 그 실행에서 복원·자동 저장을 중단한다. 기본 탭으로
  시작하고 호환 불가 상태를 알린다. 기존 파일을 보존한다.
- 복원 입력은 최대 파일 크기 64 KiB, 탭 1~8개, 고유 구성 ID, 유효한 활성 ID와
  각 필드 범위를 검증한다. 구문·구조가 잘못되면 파일 전체를 복구 대상으로 취급한다.
  정상 구조 안에서 사라진 폴더·셸은 개별 탭의 실행 오류로 처리한다.
- 저장 실패는 실행 중 세션을 종료하거나 이전 구성으로 되돌리지 않는다.
  `구성을 저장하지 못했습니다`와 재시도를 제공하고 마지막 정상 파일을 보존한다.
- 설정 취소는 복원 상태나 파일에 영향을 주지 않는다. 옵션 저장 성공 후 활성화/비활성화를
  처리하며, 구성 저장·삭제의 실패는 일반 설정 성공과 분리해 알린다. 구체적인 결과 계약과
  재시도 순서는 구현 첫 단계에서 기존 설정 apply/rollback 흐름에 맞춰 확정한다.
- 경로·탭 이름은 로컬 파일에만 저장한다. command/output/clipboard/environment는
  파일·로그·package에 포함하지 않는다. 진단 로그에 구성 파일 원문을 남기지 않는다.

## 범위와 영향 파일

기존 세 모듈을 유지한다. 새 module, 서버, 범용 event bus, 직접 cross-module 참조는 없다.

| 소유 영역 | 예상 영향 파일·경로 | 책임 |
|---|---|---|
| Terminal | `Domain/TerminalTab*`, `Application/TerminalSessionCoordinator.cs`, `ShellResolver.cs` | 이름·순서·시작 폴더·구성 복원과 세션 유지 |
| Terminal | `Contracts/`, `TerminalModule.cs`, 신규 `Infrastructure/` 구성 저장소 | 최소 public 계약, 파일 저장·복구·종료 |
| Terminal | `Presentation/TerminalView.xaml.cs`, `Presentation/Renderer/src/` 및 `dist/` | 편집 메뉴, protocol, 오류·저장 상태 UI |
| Preferences | `Contracts/AppSettings.cs`, 검증·migration, 설정 editor와 tests | 복원 옵션; schema 5에서 다음 schema로 migration |
| Host | `Composition/AppCoordinator.cs`, `SettingsApplicationService.cs`와 관련 tests | 시작·설정·종료 계약의 명시적 조정 |
| 검증·문서 | Terminal/Preferences/Integration/Architecture tests, `README.md`, `docs/test-plan.md`, `docs/architecture.md`, package 검증 | 회귀·개인정보·배포 검증 |

모듈 경로는 `src/Modules/Starboard.Modules.<이름>/` 기준이다. Terminal 전용 구성 DTO를
SharedKernel이나 Preferences로 이동하지 않는다. DesktopIntegration geometry·focus 정책은
수정하지 않으며 기본 높이 200 DIP와 하단 간격 6 DIP를 보존한다.

## 구현 단계와 인수 기준

아래 단계는 후속 구현 또는 오케스트레이터 전달용 분해다. 이번 문서 작성은 실행 승인이 아니다.

| ID | 선행 | 산출물·완료 판단 |
|---|---|---|
| W0 기준선 | 없음 | 실제 작업 트리 확인, 기존 탭/입력/focus smoke, 미수행 수동 항목 기록 |
| W1 계약 | W0 | 구성 schema, session/config ID 구분, protocol, 옵션 전환·오류 결과·종료 순서 확정 |
| W2 탭 정리 | W1 | 이름·이동 UI와 상태 변경, 기존 PID·입력·scrollback 보존 |
| W3 시작 폴더 | W2 | 명시적 폴더 설정, 안전한 process 전달, 삭제·접근 실패 복구 |
| W4 저장·복원 | W3 | 원자적 저장, 새 session 복원, 손상/미래 schema/부분 실행 실패 검증 |
| W5 설정 통합 | W4 | 기본 꺼짐, 켜기·취소·끄기·삭제 실패 처리, 기존 apply 회귀 없음 |
| W6 최종 검증 | W5 | 전체 build/test, renderer dist, package 개인정보 검사와 수동 결과 인수인계 |

기본 실행 순서는 직렬이다. 후속으로 병렬 작업을 요청받으면 W1 통합 후 Preferences
옵션 편집과 Terminal 내부 작업처럼 쓰기 범위가 분리되는 부분만 나눈다. host·공용 문서·
renderer source/dist는 각각 담당자 한 명이 소유한다. branch/commit/push와 바탕화면
재배포는 해당 실행 요청의 승인 범위를 별도로 따른다.

## 검증 방법

- 단위: 이름 길이·한글·emoji·빈 값·제어 문자, 양끝 이동, 선택 유지, 8개 제한,
  마지막 탭 대체, stale session 대상 메시지 무시, 순서·설정 직렬화 round trip.
- 저장: 없음·부분/손상 JSON·중복 ID·미래 schema·크기 초과·쓰기 실패·복구 실패,
  연속 변경의 마지막 값 저장, 종료 중 write, 복원 끄기와 삭제 실패.
- 셸: 공백·한글 시작 폴더, 없는 폴더, 사라진 셸, 8개 중 1개 실패,
  복원 전후 새로운 PID, 이름·정렬 변경 전후 동일 PID. 테스트 소유 세션만 사용한다.
- UI 수동: 3개 탭에서 서로 다른 폴더·입력·출력을 유지하며 이름·순서 변경,
  IME Enter/Escape, 우클릭/Shift+F10, tab overflow와 screen reader 접근성 이름 확인.
- 복원 수동: 복원을 켠 뒤 정상 종료·재실행 → 이름·순서·폴더·선택 확인 → 이전 명령이
  자동으로 실행되지 않음 확인 → 옵션 끄기 후 기본 탭 하나와 저장 구성 삭제 확인.
- 회귀: 호출/숨김·확장/축소·6 DIP 하단 간격, 100/125/150/200% geometry,
  shell/renderer failure, settings 저장/rollback. 실제 장비 항목은 자동 검사와 구분한다.
- `AGENTS.md`의 solution restore/build/test와 renderer 변경 시 `npm ci`, `npm run build`를
  수행한다. package에 workspace 파일·백업·임시 파일이 들어가면 검증을 실패시킨다.

기존 미수행 핵심 입력·focus 항목은 W0에서 우선 확인한다. 장비나 UI 접근 제약은
근거와 절차를 남기고, 프로세스 응답이나 테스트 개수만으로 수동 gate를 통과시키지 않는다.

## 제외 범위와 위험

- Git worktree 생성·삭제·branch checkout은 하지 않는다. 여기서 작업공간은 탭 구성이다.
- 여러 개의 저장 workspace preset, 분할 화면, 탭 드래그, 명령 launcher는 제외한다.
- 현재 경로 자동 추적, shell integration script/OSC 경로 수집, 프로세스 재연결은 제외한다.
- always-on-top, 전역 단축키 추가, 온라인 동기화·자동 업데이트, 새 renderer는 제외한다.
- 복원은 셸 초기화 비용과 profile 실행을 동반한다. 순차 시작과 cancellation을 적용하고
  종료 뒤 남은 callback이 새 탭을 생성하지 않도록 검증한다.
- 저장 파일에는 프로젝트 경로가 포함된다. 기본 꺼짐, 로컬 저장 안내와 끄기 시 삭제로
  사용자가 저장 여부를 통제한다. 일반 로컬 JSON이며 암호화 저장을 약속하지 않는다.

## 진행 기록과 인수인계

- [x] 2026-09-09: 기존 후속 후보와 실제 코드 경계를 확인하고 기획 초안 작성.
- [ ] W0 기준선: 기존 수동 입력·focus 항목은 후속 UI 통합에서 수행한다.
- [x] W1 계약: Terminal 공개 구성은 configuration ID, 이름, 순서, 시작 폴더와 제한된 shell kind만
  표현하고 runtime session ID는 renderer/실행 수명 내부에만 남긴다. Preferences schema 6은
  `restoreWorkspaceOnLaunch`를 기본 `false`로 추가하며 schema 5와 부분 JSON은 이 값이 꺼진
  상태로 migration한다. 설정 apply 결과는 옵션 전환을 계산할 수 있게 하고, Terminal의
  저장·삭제·종료 flush 실패는 별도 결과 계약으로 host가 표시·재시도할 수 있게 한다.
- [x] W2 탭 정리: 이름 변경과 좌우 이동은 renderer의 우클릭/Shift+F10 메뉴와 IME 안전 인라인 편집으로 연결했다. 변경은 runtime session ID를 유지한 채 registry 순서와 이름만 바꾼다. `npm ci`, `npm run build`, Terminal module tests 77개를 통과했다.
- [ ] W3~W6 구현·검증.

W1은 저장소 구현이나 실제 복원 실행을 시작하지 않는다. renderer protocol은 이후 UI가 이름 변경,
순서 이동, 시작 폴더 변경과 workspace 저장 상태를 명시적으로 교환할 수 있도록만 확장한다.

### W1 완료 요약

- Terminal 구성 DTO와 shell kind는 Terminal module의 공개 Contracts에만 두고, runtime session ID는
  internal tab/renderer 경로에 유지했다. 구성 ID는 새 탭마다 별도로 생성한다.
- Preferences schema 6은 기존 schema 5와 부분 JSON을 `restoreWorkspaceOnLaunch: false`로
  정규화한다. apply 결과는 enabled/disabled/unchanged 전환을 계산하므로 host가 설정 저장 성공 뒤에만
  Terminal 저장·삭제를 조정할 수 있다.
- `dotnet test tests/Starboard.Modules.Preferences.Tests/Starboard.Modules.Preferences.Tests.csproj --configuration Debug`
  30개와 `dotnet test tests/Starboard.Modules.Terminal.Tests/Starboard.Modules.Terminal.Tests.csproj --configuration Debug`
  70개를 통과했다. renderer bundle 재생성은 W1의 source-only protocol 변경과 배정된 경로 제한 때문에
  후속 UI 통합 작업에서 수행한다.
