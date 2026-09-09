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

## 병렬 구현 계획과 인수 기준

아래 단계는 후속 구현 또는 오케스트레이터 전달용 분해다. 이번 요청에서는 병렬 실행이
가능하도록 기획만 수정한다. 실제 구현·worker 실행·branch 생성·배포는 시작하지 않는다.
기존 직렬 W2~W6는 착수 전이므로 아래 작업 ID와 의존성으로 대체한다.

```text
W0 기준선 → W1 공통 계약 확정
                ├─ W2-A 탭·시작 폴더 로직 ─┐
                ├─ W2-B 구성 저장소 ──────┤
                ├─ W2-C 탭 편집 화면 ─────┼→ W3 통합·복원 연결 → W4 최종 검증
                └─ W2-D 설정 편집 ────────┘
```

W2-A~D 사이에는 선행 의존성을 두지 않는다. W1에서 합의한 계약과 테스트 대역으로
각자 구현하며, 실제 module 연결과 end-to-end 검증은 네 결과가 모두 준비된 뒤 W3에서 한다.
실행 환경의 worker 한도를 넘기지 않는다. 예를 들어 통합 담당 1명과 worker 3명까지
가능하면 A/B/C를 먼저 시작하고 빈 자리가 생기는 즉시 D를 시작한다.

### 작업별 소유권과 완료 조건

아래 경로는 앞 절의 module 경로를 기준으로 한다. W1에서 신규 파일의 실제 이름과
공유 테스트 fixture 담당까지 확정해 기록한다. 폴더가 같아도 같은 파일의 동시 편집은 금지한다.

| ID | 선행 | 독점 쓰기 범위 | 산출물·통과 기준 |
|---|---|---|---|
| W0 기준선 | 없음 | 통합 담당: 이 기획서·검증 기록 | 최신 HEAD/dirty 상태, 기존 탭·입력·focus 기준선과 미수행 환경 기록 |
| W1 공통 계약 | W0 | 통합 담당: Terminal 계약·구성 schema·저장소 경계, C#/TS protocol, Preferences 옵션 선언, 공통 fixture·architecture 허용 목록 | 아래 계약 gate 통과, build 가능한 공통 기준 확정 |
| W2-A 탭·시작 폴더 로직 | W1 | Terminal의 기존 `TerminalTab*`, `TerminalSessionCoordinator.cs`, `ShellResolver.cs`와 해당 로직 tests; W1의 확정 타입 제외 | 이름·순서·시작 폴더 변경과 새 세션 복원 로직; 저장소 대역으로 부분 실패·PID 보존 검증 |
| W2-B 구성 저장소 | W1 | Terminal 신규 구성 저장소 구현·저장 큐 파일과 전용 tests; coordinator·기존 ConPTY 파일 제외 | 원자적 저장, debounce·flush·삭제, 손상/미래 schema/쓰기 실패 검증; 실제 사용자 파일은 사용하지 않음 |
| W2-C 탭 편집 화면 | W1 | Terminal `Presentation/Renderer/src/` 및 `dist/`, renderer 전용 검증 파일 | 이름·순서·폴더 편집과 상태 UI, protocol fixture 기반 입력·오류 검증, source/dist 일치; 실제 셸 연결 검증은 W3에 인계 |
| W2-D 설정 편집 | W1 | Preferences validation·migration·editor 구현과 해당 tests; 확정 계약 선언 제외 | 기본 꺼짐, 이전 설정 migration, 편집·취소 및 저장/삭제 결과 표시를 callback 대역으로 검증 |
| W3 통합·복원 연결 | W2-A, W2-B, W2-C, W2-D | 통합 담당: `TerminalModule.cs`, `TerminalView.xaml.cs`, host composition, integration/architecture tests, 공용 문서 | 저장소·coordinator·renderer 연결, 시작·옵션 전환·종료 순서, 실제 세션 복원과 실패 경로 통합 |
| W4 최종 검증 | W3 | 통합 담당: package 검증, 공용 문서와 필요한 회귀 수정 | 전체 build/test, renderer/package 검사, 실제 UI 결과와 미수행 항목 인수인계 |

### W1 계약 gate

worker가 서로의 미완성 구현을 기다리지 않도록 다음을 먼저 확정한다.

- 저장 schema와 불변 snapshot: 구성 ID/session ID 구분, 순서·이름·폴더·셸·선택 필드,
  validation 규칙과 오류 코드. 파일 DTO는 Terminal 내부에 유지한다.
- 탭 이름·이동·폴더 변경·복원 요청과 결과: 새 API의 signature, 취소·실패·중복 요청 정책,
  기존 session 유지 조건. runtime 상태 변경과 저장 대상 구성 변경 event를 구분한다.
- 저장소 경계: 읽기·저장·삭제·flush 결과, 미래 schema·백업 복구·부분 삭제 실패 의미,
  debounce 및 종료 cancellation 소유자. W2-A가 사용할 대역을 준비한다.
- C#/TS protocol: message type, target ID, payload, success/error 응답과 버전 처리.
  W2-C가 backend 없이 실행할 요청/응답 fixture와 host 연결 인수 항목을 제공한다.
- Preferences 복원 옵션의 schema/default, host의 옵션 적용 결과 계약,
  일반 설정 성공과 구성 저장·삭제 실패의 구분, 재시도 순서.
- 공통 타입·fixture는 모든 worker가 build할 수 있는 상태로 통합한다. 아직 없는 구현을
  호출해 build를 깨뜨리거나, 가짜 성공을 반환하는 runtime placeholder를 만들지 않는다.

gate 통과 후 공통 계약 파일은 동결한다. 변경이 필요하면 worker는 직접 고치지 않고
통합 담당에게 이유·영향을 전달한다. 통합 담당이 계획과 계약을 갱신·검증한 뒤 영향을
받는 worker를 같은 새 기준으로 맞춘다. 계약 변경에 의존하지 않는 작업은 계속할 수 있다.

### 실행·통합 규칙

- 구현 실행 범위에서 허용된 branch/worktree만 사용한다. 모든 W2 작업은 W1이 통합된
  동일 기준 커밋에서 출발하고, 각자 격리된 checkout과 build 출력 경로를 사용한다.
  기존 dirty 변경을 stash/reset하거나 worker 전제로 암묵적으로 가져오지 않는다.
- worker는 자기 코드와 직접 tests를 함께 완결한다. host, 공용 문서, project 설정,
  공통 fixture 변경은 통합 담당에게 요청한다. 병렬화를 위해 새 production module이나
  불필요한 public API를 만들지 않는다.
- renderer source와 dist는 W2-C 한 명이 함께 소유한다. 같은 checkout에서 npm build와
  asset 편집을 동시에 실행하지 않는다. 공유 package staging의 동시 실행도 금지한다.
- 각 worker는 작업 ID, 기준 커밋, 변경 파일, 계약 변경 여부, 검증 명령·결과,
  미수행 항목과 W3 연결 지점을 인계한다. mock 통과를 실제 UI 통과로 보고하지 않는다.
- W3에서는 결과를 A → B → C → D 순으로 적용하고 단계별 관련 build/test를 확인한다.
  이 순서는 통합 순서이며 W2 실행 의존성은 아니다. 충돌은 통합 담당이 의미를 확인해
  해결한다. worker 종료만으로 gate를 통과시키지 않는다.
- W3 중 재작업을 위임하면 수정 대상 파일 소유권을 일시적으로 돌려주고 해당 파일의
  통합 편집을 멈춘다. 다시 인수한 후 전체 연결 검증을 계속한다.
- 핵심 입력·focus·기존 세션 유지 회귀가 있으면 W4 배포 검증으로 넘기지 않는다.
  실제 장비가 없어 남은 시나리오는 근거와 절차를 기록하고 release 완료와 구분한다.
- 원격 push, main 반영, 바탕화면 앱 교체·종료는 병렬화 계획만으로 승인되지 않는다.
  해당 실행 요청의 권한 범위를 따른다.

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

- 제품 기능으로 Git worktree 생성·삭제·branch checkout을 제공하지 않는다.
  여기서 작업공간은 탭 구성이다. 개발용 격리 worktree는 위 실행 규칙과 별개다.
- 여러 개의 저장 workspace preset, 분할 화면, 탭 드래그, 명령 launcher는 제외한다.
- 현재 경로 자동 추적, shell integration script/OSC 경로 수집, 프로세스 재연결은 제외한다.
- always-on-top, 전역 단축키 추가, 온라인 동기화·자동 업데이트, 새 renderer는 제외한다.
- 복원은 셸 초기화 비용과 profile 실행을 동반한다. 순차 시작과 cancellation을 적용하고
  종료 뒤 남은 callback이 새 탭을 생성하지 않도록 검증한다.
- 저장 파일에는 프로젝트 경로가 포함된다. 기본 꺼짐, 로컬 저장 안내와 끄기 시 삭제로
  사용자가 저장 여부를 통제한다. 일반 로컬 JSON이며 암호화 저장을 약속하지 않는다.

## 진행 기록과 인수인계

- [x] 2026-09-09: 기존 후속 후보와 실제 코드 경계를 확인하고 기획 초안 작성.
- [x] 2026-09-09: 병렬 가능한 기획 요청에 따라 W1 계약 선행, W2-A~D 독립 작업,
  W3 직렬 통합·W4 최종 검증으로 재구성. 파일 소유권과 계약 변경·인수인계 규칙 추가.
- [ ] W0~W1 기준선·계약 확정.
- [ ] W2-A~D 독립 구현·검증.
- [ ] W3 통합·W4 최종 검증.

이번 산출물은 기획서와 이전 기획의 후속 링크뿐이다. 기존 제품 코드·미커밋 변경·
바탕화면 실행본은 수정하지 않는다. 문서 내용·경로·diff를 검증하고 .NET 빌드는 생략한다.
