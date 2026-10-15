# Starboard 후속 기획 — 실수 방지와 사용 편의

## 목표와 완료 조건

터미널을 잘못 닫거나 복사한 명령을 의도치 않게 실행하는 실수를 줄이고, 다른 탭의
출력과 기존 단축키를 쉽게 확인하게 한다. 이번 요청은 **기획 문서 작성만**이며 코드
구현·worker 실행·커밋·푸시·배포를 시작하지 않는다.

| 우선순위 | ID | 기능 | 완료 판단 |
|---|---|---|---|
| 1 | SAFE-01 | 탭 닫기 확인 | 확인 전에는 대상 세션을 종료하지 않고 취소하면 그대로 유지 |
| 2 | SAFE-02 | 여러 줄 붙여넣기 경고 | 줄바꿈을 포함한 clipboard 내용은 미리보기·동의 후에만 대상 탭에 전달 |
| 3 | NOTICE-01 | 비활성 탭의 새 출력 표시 | 출력 도착을 조용히 표시하고 탭 전환·완료 알림으로 오인하지 않음 |
| 4 | HELP-01 | 단축키 도움말 | 트레이에서 현재 설정과 일치하는 단축키·충돌 상태를 확인 |

확인창을 위한 새 전역 단축키, 원격 서비스나 외부 의존성은 추가하지 않는다.

## 현재 상태와 관련 기획

2026-09-09, 로컬 `main`의 관련 코드를 확인했다.

- renderer의 `×`와 `Ctrl+Shift+W`는 `close-session`을 보내고, `TerminalView`가
  coordinator의 닫기 경로로 연결한다. 닫기 확인 절차는 없다.
- `PasteFromClipboard`는 clipboard text를 읽어 크기 제한 안이면 `paste`로 보낸다.
  여러 줄 미리보기·확인 경로는 없다. 실제 전달은 기존 xterm paste 경로를 따른다.
- session별 output event와 renderer의 세션별 상태가 이미 있다. 작업 완료를 정확히
  판정하는 shell integration 계약은 없으므로 출력 표시는 완료 판정으로 확장하지 않는다.
- 기존 트레이 메뉴와 사용자 설정 단축키 계약을 재사용한다. 도움말용으로 별도
  단축키 등록이나 같은 설정의 이중 저장소를 만들지 않는다.

관련 정본:

- [저장소 작업 규칙](../../AGENTS.md), [개발 흐름](../development-workflow.md),
  [코드 스타일](../code-style.md)
- [작업공간·탭·패널 기획](2026-09-09-workspace-convenience.md)
- [탭 UI 계획](2026-09-03-renderer-and-tab-ui.md), [기존 검증 기록](../test-plan.md)

기존 `TAB-UX-01`은 `×`와 `+`를 배치로 구분하는 예방책이고 SAFE-01은 잘못 누른
뒤 실제 종료를 막는 확인 절차다. 둘 중 하나로 다른 요구사항을 대체하지 않는다.
이 문서는 별도 후속 범위이며 기존 작업공간 기획의 완료 조건을 소급해 늘리지 않는다.

## SAFE-01 — 탭 닫기 확인

### 사용자 흐름

1. `×`, `Ctrl+Shift+W` 또는 향후 탭 닫기 메뉴를 선택한다.
2. 살아 있는 세션이면 대상 이름과 `이 탭의 터미널과 실행 중인 작업이 종료됩니다.`를
   표시한다. 버튼은 `취소`, `탭 닫기`이며 기본 focus는 취소다.
3. `탭 닫기`를 명시적으로 선택한 경우에만 해당 세션을 닫는다.

### 동작 규칙

- busy 여부를 출력량·idle 시간·prompt 모양으로 추측하지 않는다. 살아 있는 셸은
  작업 실행 여부와 무관하게 확인한다. Starting/Restarting도 확인 대상으로 본다.
- 종료·실패 표시만 믿고 확인을 생략하지 않는다. 소유한 세션 자원이 종료된 것이
  확인된 탭만 추가 확인 없이 닫는다. 불명확하면 안전하게 확인한다.
- 취소, Escape, 확인 UI 닫기는 원래 탭·PID·입력·scrollback을 유지한다.
  확인 UI를 여는 단축키의 Enter/key repeat가 동의로 이어지지 않게 한다.
- 대기 중에는 대상 session ID와 세대 정보를 고정한다. 표시 이름이 바뀌거나 탭이
  이동해도 다른 탭을 닫지 않는다. 대상이 제거·재시작됐으면 오래된 확인을 폐기한다.
- 확인 요청은 중복으로 쌓지 않는다. 확인 중 output은 정상 처리하되 관련 없는
  keyboard input이 셸로 새어 들어가지 않게 한다. 긴 대기 동안 coordinator lock을 잡지 않는다.
- 마지막 탭을 실제로 닫은 뒤 기본 탭을 만드는 기존 동작은 유지한다. 취소했는데
  대체 탭이 생기거나 기존 탭이 먼저 사라지면 실패다.
- 기본적으로 확인을 켜고 이번 범위에서는 `다시 묻지 않기`나 우회 단축키를 제공하지 않는다.
- 앱 전체 종료·Windows 종료·shell 자체 `exit`는 이 탭 닫기 요청과 구분한다.
  이번 기능 때문에 OS 종료를 막거나 shutdown 과정에서 탭마다 확인창을 띄우지 않는다.

## SAFE-02 — 여러 줄 붙여넣기 경고

### 사용자 흐름

- clipboard에 CR 또는 LF가 하나라도 있으면 확인한다. `명령 한 줄 + 끝 줄바꿈`도
  실행될 수 있으므로 포함한다. CRLF는 표시할 때 하나의 줄바꿈으로 취급한다.
- 안내는 `여러 줄을 붙여넣으면 명령이 바로 실행될 수 있습니다. 내용을 확인하세요.`로 한다.
- 대상 탭 이름, 줄바꿈 표시와 읽기 전용 내용 미리보기, `취소`·`붙여넣기`를 제공한다.
  기본 focus는 취소다. 마지막 줄바꿈과 화면 밖 내용이 있다는 사실을 숨기지 않는다.
- CR/LF가 없는 내용은 기존 경로로 전달한다. 이 기능은 모든 위험 명령을 탐지하거나
  단일 줄 붙여넣기의 안전을 보장하는 기능이 아니다.

### 동작·개인정보 규칙

- keyboard shortcut, DOM paste, native context-menu 등 실제 가능한 모든 사용자
  붙여넣기 진입점을 조사하고 같은 보호 경로로 모은다. 확인되지 않은 우회 경로를
  지원 완료로 보고하지 않는다. 기존 `Ctrl+V`·`Ctrl+Shift+V`와 복사·interrupt 정책은 유지한다.
- 미리보기 시작 시 clipboard 내용을 한 번 읽어 메모리에 보관한다. 동의 뒤 clipboard를
  다시 읽지 않고 사용자가 확인한 동일 snapshot만 기존 xterm paste 경로로 전달한다.
- 별도 Enter를 추가하거나 줄별 실행, 공백 제거, 명령 재작성은 하지 않는다.
  기존 xterm의 줄바꿈·bracketed paste 처리를 보존하며 shell별 즉시 실행 여부를 단정하지 않는다.
- 대상 탭 전환·제거·재시작, renderer 재생성, 앱 종료 또는 사용자의 패널 숨김이 발생하면
  대기 요청을 취소한다. 늦은 동의로 다른 탭이나 새 세션에 전달하지 않는다.
- 단일 대기 요청만 허용하고 반복 paste로 중복 전송하지 않는다. 미리보기는 스크롤로
  확인할 수 있게 하며 크기 제한을 초과하면 안내 후 거부한다. 일부만 잘라 전송하지 않는다.
- clipboard 조회 실패는 사용자에게 알리고 입력을 전달하지 않는다. clipboard 원문은
  로그·설정·workspace 구성·디스크·telemetry에 저장하지 않는다. 완료·취소 시 참조를 해제한다.
- Enter/Escape와 IME 조합, 확인창을 연 단축키의 key repeat를 구분한다. 확인 UI는
  의도적인 paste 요청으로만 열리고 background event 때문에 focus를 가져오지 않는다.

## NOTICE-01 — 비활성 탭의 새 출력 표시

- 선택되지 않은 탭에 새 non-empty output이 도착하면 작은 `새 출력` 점을 표시한다.
  ANSI 내용·prompt를 분석하지 않으며 제어 sequence만 포함한 출력도 도착으로 취급한다.
- 기존 실행/실패 상태 점과 별도 의미로 표시하고 tooltip·접근성 이름에 `새 출력 있음`을
  제공한다. 색상만으로 구분하거나 탭 폭을 계속 변경하지 않는다.
- 여러 번 출력돼도 점 하나를 유지한다. 글자 수·줄 수·완료율로 표시하지 않는다.
  점멸·소리·토스트·자동 탭 전환·앱 활성화는 하지 않는다.
- 사용자가 해당 탭을 선택하면 표시를 지운다. 초기의 선택된 탭은 대상이 아니다.
  패널이 다른 창에 가려졌다는 이유만으로 선택된 탭을 비활성 탭으로 취급하지 않는다.
- 이름·순서 변경에도 session ID로 상태를 유지한다. 탭 제거·세션 재시작 때 초기화하고,
  renderer 재생성 때는 살아 있는 세션의 최신 표시 상태를 복원한다. 앱 재실행 간에는 저장하지 않는다.
- 출력 처리와 선택 처리의 순서를 정의한다. 선택 처리 이후 도착한 출력은 활성 탭에
  표시만 하고 새 출력 점을 다시 켜지 않는다. 이전 세션의 늦은 output은 무시한다.
- `명령 완료`, `성공`, `실패` 알림은 아니다. 해당 기능은 별도 shell integration 기획이 필요하다.

## HELP-01 — 단축키 도움말

- 트레이에 `단축키 안내`를 추가하고 한국어 읽기 전용 창 하나를 연다. 이미 열렸다면
  그 창을 재사용한다. 사용자가 명시적으로 열 때만 활성화한다.
- 전역 호출·확장 키는 설정의 기본 문자열이 아니라 **현재 적용된 값과 등록 상태**를
  표시한다. 충돌로 등록되지 않았거나 상태를 확인할 수 없으면 그대로 안내한다.
- 새 탭, 다음/이전 탭, 탭 닫기, 복사, 붙여넣기, interrupt 동작을 나누어 보여준다.
  `Ctrl+C`는 선택 시 복사·선택 없으면 interrupt, `Ctrl+W`는 셸 전달임을 설명한다.
- SAFE-01/02가 구현된 상태에서는 탭 닫기·여러 줄 paste의 확인 동작도 설명한다.
  현재 실행 중인 버전에 없는 계획 기능을 이미 쓸 수 있는 것처럼 표시하지 않는다.
- 도움말은 새 전역 키를 등록하지 않고 설정 편집기를 복제하지 않는다. 키 변경은 기존
  설정 화면으로 안내한다. 설정 저장·rollback 후 도움말이 열려 있으면 실제 적용 상태를 갱신한다.
- 창 닫기는 터미널 focus를 강제하지 않는다. Escape와 keyboard 탐색을 지원한다.

## 범위와 영향 파일

기존 모듈러 모놀리스와 세 모듈을 유지한다. 상위 WPF 창은 host, terminal 확인 정책과
pending 요청·output 상태는 Terminal, tray·단축키 등록 상태는 DesktopIntegration이 소유한다.
Preferences에 새 저장 설정을 추가하지 않는다.

| 소유 영역 | 예상 영향 파일·경로 | 책임 |
|---|---|---|
| Terminal | `Application/TerminalSessionCoordinator.cs`, 필요한 pending 요청·output 상태 타입과 tests | 대상 세션 검증, 중복/늦은 응답 차단, 새 출력 상태 |
| Terminal | `Application/RendererProtocol.cs`, `RendererMessage.cs`, `Presentation/TerminalView.xaml.cs` | clipboard 경계, 확인 request/result와 수명 연결 |
| Terminal | `Presentation/Renderer/src/` 및 `dist/` | 확인·미리보기 UI, 새 출력 점, 입력 routing |
| DesktopIntegration | `Infrastructure/TrayIconService.cs`, module 진입점·필요한 최소 `Contracts/` | 도움말 요청 event와 실제 hotkey 상태 제공 |
| Host | `Composition/AppCoordinator.cs`, `Shell/`의 도움말 창 | module 조정, 단일 도움말 창 수명 |
| 검증·문서 | 관련 module/integration/architecture tests, README·architecture·test-plan | 집중/통합 검증과 실제 결과 기록 |

모듈 경로는 `src/Modules/Starboard.Modules.<이름>/` 기준이다. 구현 직전에 기존 계약과
모든 paste entry point를 확인해 실제 파일 소유권을 고정한다. 새 정책을 view에 몰아넣거나
host가 ConPTY를 직접 제어하지 않는다. Terminal과 DesktopIntegration은 직접 참조하지 않는다.

## 구현 순서·병렬 처리

| 작업 | 선행 | 독점 범위·인수 기준 |
|---|---|---|
| S0 기준선·공통 계약 | 없음 | 통합 담당: 최신 코드 확인, 확인 요청 ID/세션 세대/취소 결과, badge snapshot, 도움말 계약·공통 fixture 확정 |
| S1-A Terminal 정책 | S0 | Terminal Application의 확인·출력 상태와 직접 tests; view/protocol 동결 파일 제외. SAFE-01 → SAFE-02 → NOTICE-01 순으로 완결 |
| S1-B Terminal 화면 | S0 | renderer source/dist와 전용 tests. 공통 fixture로 닫기 확인 → paste 미리보기 → 새 출력 표시 구현. TerminalView는 직접 수정하지 않음 |
| S1-C 도움말 | S0 | Desktop tray·hotkey 상태 제공, 독립 도움말 창과 직접 tests. host coordinator·공용 문서는 통합 담당에게 연결 요청 |
| S2 통합 | S1-A, S1-B, S1-C | 통합 담당: TerminalView·module 진입점·AppCoordinator와 integration tests, 공용 문서. 실제 clipboard/세션/도움말 연결 |
| S3 최종 검증 | S2 | 전체 build/test, renderer/package 검증, manual 결과와 미수행 항목 인계 |

S1-A/B/C는 같은 S0 기준에서 병렬 실행할 수 있다. 공통 계약을 먼저 build 가능한 상태로
확정하고, worker별 checkout·출력을 분리한다. 같은 파일을 동시에 수정하지 않는다.
공통 계약 변경은 통합 담당이 조정하고 필요 worker만 새 기준으로 갱신한다.

기존 작업공간 기획의 W2-A/W2-C/W3와 coordinator·renderer·host 파일이 겹친다.
그 작업이 진행 중이면 소유 파일 인수 후 시작하거나 통합 담당이 기존 작업에 요구사항을
명시적으로 병합한다. 두 계획의 worker를 같은 파일에 동시에 투입하지 않는다.
W4 등 전체 선행 기획의 완료를 무조건 기다릴 필요는 없지만 계약과 기준 코드는 일치해야 한다.

worker 인수인계에는 기준 커밋, 변경 파일, 계약 변경, 검증 결과, 미수행 항목과 연결 요청을
포함한다. S2는 A → B → C 순으로 통합한다. 각 기능의 단위/mock 통과를 실제 UI 완료로
대체하지 않는다. 실제 구현 요청의 branch·commit·push·배포 권한을 별도로 따른다.

## 검증 방법과 위험

- SAFE-01: 모든 닫기 entry point, 취소/동의, 마지막 탭, Starting/Running/Restarting,
  종료된 탭, 중복 입력, 확인 중 대상 변경·재시작, 탭 이름·순서 변경, PID 보존.
- SAFE-02: 단일 줄, 끝 CR/LF, CRLF 여러 줄, 빈 clipboard, 크기 초과, 한글·공백,
  조회 실패, 표시 후 clipboard 변경, 탭 전환·제거·재시작, 늦은 확인, 중복 paste.
  취소 전후 셸에 전송된 입력은 0이고 동의한 snapshot은 한 번만 전달되는지 검증한다.
- NOTICE-01: 3개 탭에서 활성/비활성 출력, 선택과 output 경합, 제거·재시작·renderer 복구,
  빠른 연속 출력과 상태 점 구분, focus와 탭 폭 보존.
- HELP-01: 기본·사용자 변경 키, 등록 실패·rollback·상태 미확인, 단일 창 재사용,
  keyboard 탐색·닫기, 안내와 실제 동작 일치.
- UI: 작은 200 DIP 패널에서도 확인 메시지·취소·동의가 접근 가능해야 한다.
  긴 paste는 제한된 미리보기 스크롤로 처리하고 작업표시줄을 덮거나 자동 확장하지 않는다.
  4개 테마, 100/125/150/200%와 한글 IME, Tab/Enter/Escape를 검증한다.
- 입력 보호를 우선한다. 확인 UI 실패·renderer 재연결 시 대기 중인 닫기/paste는
  자동 승인하지 않고 취소한다. 사용자 세션이 아닌 테스트 소유 세션으로 검증한다.
- 구현 시 관련 집중 tests부터 실행하고 완료 전 `AGENTS.md`의 전체 restore/build/test,
  renderer 변경 시 `npm ci`·`npm run build`, local asset/package 검증을 수행한다.
  실제 Windows clipboard·WebView2·IME·focus 검증과 mock/browser 결과를 구분한다.

## 진행 기록

- [x] 2026-09-09: 사용자 동의에 따라 네 기능의 우선순위·실패 정책·영향 파일과
  병렬 작업 분해를 작성했다. 기존 코드와 기획을 참고했고 제품 코드는 수정하지 않았다.
- [x] 2026-09-09: S0 공통 계약 범위를 확정했다. Terminal은 비어 있지 않은 요청 ID와
  세션 ID, 1부터 증가하는 세션 세대를 묶은 대상을 확인 요청·응답에 함께 싣고, 응답
  적용 전에 현재 대상과의 완전 일치를 검사한다. 취소·승인 결과는 별도 enum으로 두며
  새 출력 표시는 세대별 immutable snapshot으로 전달한다. DesktopIntegration은 도움말
  요청과 각 전역 단축키의 configured gesture, effective gesture, 실제 등록 상태를 공개
  계약으로 제공한다. 두 module 계약은 서로의 타입을 참조하지 않고 host가 조정한다.
- [x] 2026-09-09: S0 집중 테스트(Terminal 140, DesktopIntegration 74), architecture
  tests 6개, Debug solution build와 전체 automated tests 285개가 통과했다. 실제 확인 UI,
  clipboard, renderer 복구와 Windows 단축키 충돌 시나리오는 S1~S3 구현·manual 검증 대상이다.
- [x] S1~S3 구현·검증.
- [x] 2026-09-09: SAFE-01 통합 뒤 `GuiHostKeepsConPtyTabsIndependentThroughExitRestartAndClose`가
  살아 있는 재시작 세션을 이전의 직접 `CloseAsync` 경로로 닫아 새 안전 계약과 불일치하는
  회귀를 안정화한다. GUI test host는 재시작 세대의 확인 token으로 닫기를 승인하고, terminal
  입출력을 진단에 남기지 않은 채 host/PID identity, session ID·generation, tab 제거와 process
  종료 신호를 제한 시간·cancellation 안에서 각각 관찰한다. 단순 재시도나 고정 지연은 사용하지
  않으며 대상 반복 실행과 filter/skip 없는 IntegrationTests 전체 실행으로 검증한다. 실제 GUI,
  WebView2와 수동 ConPTY 입력 검증은 이번 자동화 범위와 구분해 pending으로 유지한다.
  제품 결함이 아닌 테스트 계약 불일치로 판별해 같은 generation의 확인 응답과 명시적
  tab 제거/PID 종료 signal로 수정했다. 집중 test 10회 반복과 최종 변경 뒤 집중 test,
  IntegrationTests 34개 및 단축키 안내 집중 test 4개가 filter/skip 없이 통과했다. 이
  machine의 기본 병렬 SDK 실행은
  테스트 시작 전 기존 MSBuild 정체가 재현되어 중단했고 build-server 비활성·단일 node로
  검증했다.

- [x] 2026-09-09: SAFE-01/02, NOTICE-01과 HELP-01을 통합했다. 살아 있는 탭은
  generation-bound confirmation 없이는 닫히지 않고, CR/LF clipboard는 one-shot snapshot
  preview 뒤에만 전달된다. 비활성 탭 output은 새 출력 점으로만 표시하며 tray 도움말은
  effective global shortcut 상태와 terminal 규칙을 보여 준다.
- [x] 2026-09-09: 최종 Debug restore/build와 filter/skip 없는 전체 301개 test, renderer
  offline rebuild, `git diff --check`를 통과했다. 실제 WebView2 dialog, clipboard, IME,
  focus와 DPI/hardware 시나리오는 `docs/test-plan.md`의 `Not run` 수동 항목으로 남겼다.
- [x] 2026-09-10: SAFE-01/02와 NOTICE-01의 실제 WebView2 수동 검증을 시도했으나,
  이 실행 환경의 Windows UI 자동화 정책이 terminal application의 click·입력·clipboard·shortcut
  조작을 금지했다. 실제 dialog, preview, clipboard 또는 shell 입력은 조작하지 않았고
  MAN-029a, MAN-033, MAN-034 및 MAN-034a를 포함한 관련 항목은
  `docs/test-plan.md`에서 `Blocked`로 갱신했다. 자동 test 결과를 실제 UI 통과로
  대체하지 않았으며 terminal command, output 또는 clipboard 원문을 문서화하지 않았다.

### 2026-09-10 — HELP-01 안내 창 닫기 회귀 수정

- 사용자 제보: 단축키 안내 창의 하단 `닫기` 버튼이 동작하지 않는다.
- 원인: host는 `Show()`로 modeless 창을 열지만 버튼은 `IsCancel`에만 의존했고
  `Close()` 호출이 없었다. [WPF 닫기 동작 문서](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/how-to-close-window-dialog-box)에
  따른 자동 취소 닫기는 `ShowDialog()`로 연 대화상자용이다.
- 안내 창의 XAML과 code-behind에 버튼 Click·Escape 처리를 명시적으로 연결했다.
  modeless 정책을 유지하며 다른 키는 소비하지 않고 terminal·host 종료 경로는 바꾸지 않는다.
- `ShortcutGuideWindowIntegrationTests`는 실제 STA WPF 창에 routed event를 전달한다.
  수정 전 버튼·Escape 두 경우의 실패를 재현했고 수정 후 세 경우(버튼·Escape·일반
  Window.Close)가 통과했다. 별도 창이 계속 열려 있는지도 검증한다. 물리 마우스·키보드
  조작과 tray 재열기·focus 검증은 이 자동화 결과에 포함하지 않는다.
- 앱 전체의 종료 지연은 이 버튼 연결 누락과 같은 원인이라고 확인되지 않았다.
  별도 수명 주기 진단 대상으로 남기며 이번 수정 범위를 확장하지 않는다.
- Debug solution restore/build 경고·오류 0개, 전체 304개 test와 `git diff --check`를
  통과했다. 후속 사용자 요청으로 수정분을 commit/push 대상에 포함한다.
  바탕화면 재배포와 오케스트레이터 상태·작업 브랜치 변경은 하지 않는다.

### 2026-09-21 — SAFE-02 붙여넣기 중복 전달 회귀 수정

- 사용자 제보: 한 번 복사한 내용을 terminal에 붙여넣으면 동일 내용이 두 번 입력된다.
- 원인은 `Ctrl+V` key handler가 host에 `paste-request`를 보내는 동시에, 취소되지 않은
  browser paste 기본 동작을 xterm도 처리해 두 경로가 각각 shell input을 만든 것이다.
- keyboard paste는 기본 동작을 명시적으로 취소하고 host clipboard 경계로 한 번만
  요청한다. context-menu 등 native paste event도 capture 단계에서 같은 경계로 모아
  xterm의 직접 clipboard 전달과 여러 줄 확인 우회를 차단한다.
- renderer source 계약은 gesture당 `paste-request` 호출부가 하나뿐인지, keyboard와
  native paste 진입점 모두 기본 동작을 차단하는지 자동 검증한다. 실제 WebView2의
  `Ctrl+V`, `Ctrl+Shift+V`, context-menu와 한글 IME 조작은 수동 확인 대상으로 남긴다.

## 2026-10-14 복사·붙여넣기 불능과 앱 자동 종료

### 현재 상태와 사용자 확인

**코드 경계 수정·자동 검증 완료 (2026-10-14), 실제 제보 재현·장기 실행 인수는 미수행.** 기존 SAFE-02 중복 전달 수정과 완료 테스트는 보존한다.
사용자는 처음 `Ctrl+S`·`Ctrl+V`로 제보했으나 추가 확인으로 **`Ctrl+C` 복사·`Ctrl+V` 붙여넣기**라고
정정했다. 단순 `Ctrl+S` 또는 전역 `Ctrl+Alt+S` 문제로 잘못 계획하지 않는다.

- 본문을 다시 클릭해도 복사·붙여넣기가 모두 되지 않았다.
- 앱 재시작 뒤에는 정상 동작으로 돌아왔다. 일시 회복을 수정 완료로 해석하지 않는다.
- 앱이 한 번 저절로 종료됐다고 확인했다. 사용자 수동 종료나 정상 tray 종료가 아니다.
- 제보 시 바탕화면 metadata의 버전은 `0.2.2`, 빌드는 `2dbb6c9`였다.
  실패한 순간의 선택 상태·focused element·대화상자·clipboard API 결과와 재현 순서는 아직 미확인이다.

### 수정 전 읽기 전용 조사 근거와 한계

- Windows Application Error **1000**에서 2026-10-14 **15:46:41 / 15:47:19 KST**에
  바탕화면 `Starboard.exe`의 오류 기록을 확인했다. version `0.2.2.0`, 같은 PID `53316` (`0xd044`),
  exception code `e0434352` / `c000041d`, fault module `KERNELBASE.dll`이다.
  대응하는 Windows Error Reporting **1001** APPCRASH 기록도 있다. 동일 process의 여러 기록을
  여러 번의 앱 종료로 계산하지 않으며, fault module 이름을 root cause로 단정하지 않는다.
- 같은 시각 주변(15:43~15:50 KST)의 .NET Runtime **1026** 조회에는 이벤트가 없었다.
  따라서 예외 stack·throw 지점은 현재 확보하지 못했다. 덤프를 열거나 외부로 전송하지 않았다.
- `starboard.log`의 읽은 tail 100행에는 12:00:46~12:00:50 KST의 `RecoverRendererLayout` 및
  `ObserveRendererLayout` 경고와 후속 session recovery 기록이 있지만 Copy/Paste 실패 기록은 없었다.
  로그는 성공한 clipboard 요청을 기록하지 않으므로, 이 부재로 요청 미수신·clipboard 정상 또는
  layout 경고와 오후 충돌의 인과관계를 증명하지 않는다.
- `FileDiagnosticLog`는 message 문자열을 저장하지 않고 operation/native Win32 code만 남긴다.
  숫자 bounds나 HRESULT를 message에 넣어 호출해도 현재 파일에는 남지 않는다. 이 개인정보 보호
  정책을 유지하면서 필요한 **허용된 숫자·상태 메타데이터**를 실제로 관측할 경로를 마련해야 한다.
- renderer의 xterm key handler는 selection이 있는 `Ctrl+C`를 host `copy`로, `Ctrl+V`를
  `paste-request`로 보낸다. selection 없는 `Ctrl+C`는 기존 shell interrupt다. 이 handler와 host
  clipboard 함수는 `431deed` 이후의 두 기능 커밋에서 직접 수정되지 않았다.
- 전역 capture handler는 확인창/저장 탭 dialog가 있을 때 Ctrl 조합을 막는다. 최근 드래그의
  focus 보존·capture 해제, renderer 복구·layout 관측과 pending UI 상태를 함께 조사하되
  특정 기능을 원인으로 단정하지 않는다. 재시작이 복구한다는 이유로 clipboard 실패를 배제하지 않는다.
- 사용 중인 WebView2 SDK 문서상 `AreBrowserAcceleratorKeysEnabled=false`는 `Ctrl+C`·`Ctrl+V` 같은
  텍스트 편집 accelerator를 비활성화하지 않는다. browser accelerator를 무조건 켜는 처방을 사용하지 않는다.
- 현재 Node renderer harness는 custom key callback을 보관하지만 selection 모형과 실제 Windows
  clipboard 왕복을 검증하지 않는다. 기존 76개 renderer/585개 .NET 통과는 이번 사용자 증상의
  실제 재현이나 clipboard·native keyboard·앱 장기 실행 통과를 대신하지 않는다.

### 독립 Task: `terminal-clipboard-shortcut-regression`

- 제목: 복사·붙여넣기 간헐 불능과 자동 종료 회귀 진단·복구
- 우선순위: **긴급(P0)**. 화면 미관 보강보다 입력 기본 기능과 앱 수명 안정화를 우선한다.
- 상태: **지정 worktree에서 구현·자동 검증 완료**. 실제 사용자 증상과 충돌의 공통 원인은 미확정이다.
- 선행: 기존 paste 단일 전달·여러 줄 확인·selection 없는 interrupt·세션 보존 정책을 유지한다.
- 조사 순서: renderer key 도착 → xterm selection/focus·capture/pending UI 상태 → host message 수신 →
  clipboard API 성공/실패 → 대상 session 전달을 분리한다. 자동 종료의 예외 경로는 별도로 좁히고
  같은 원인인지 여부를 증거로 판정한다. clipboard 원문을 진단에 포함하지 않는다.
- 실제 영향 경로: Terminal `Presentation/TerminalView.xaml.cs`, `TerminalView.LayoutRecovery.cs`,
  renderer `src/index.ts`, 필요한 경우에만 `src/tabDragReorder.ts`·`workspaceSync.ts`, source로 재생성한
  `dist/**`, renderer `test/rendererHarness.mjs` 및 clipboard/key 회귀 테스트, 관련 Terminal C# 테스트.
  예외/diagnostic 경계의 근거가 있을 때만 host **`src/Starboard.Windows/App.xaml.cs`**, 실제
  **`src/Starboard.Windows/Shell/MainWindow.xaml.cs`**, **`Composition/AppCoordinator.cs`** 및
  **`Infrastructure/FileDiagnosticLog.cs`**와 해당 테스트를 포함한다. 잘못된 host 상대 경로 때문에
  연결 조사 자체를 생략하지 않는다. architecture/test-plan/이 계획·index를 실제 결과와 맞춘다.
- 제외: 의존성/설정 schema 추가, 저장 탭 삭제, 사용자 clipboard 내용 수집·저장·전송,
  사용자 앱 강제 종료·자동 재실행, 원격 서비스/Windows 설정 변경, 다른 기능 재구현, commit/push·배포.
- 파일 소유권: clipboard·focus·UI 수명 조사는 하나의 runtime/renderer 경계로 먼저 고정한다.
  같은 TerminalView·controller를 여러 worker가 동시에 수정하지 않는다. 분리 가능한 순수 검증만
  계약 확정 후 병렬로 진행하고, 실제 입력·예외 통합 검증은 합류 뒤 수행한다.
- [x] key 도착·selection/focus/capture/modal·host clipboard·renderer 전달과 예외 경계를
  원문 없는 상태 관측으로 분리했다. 실제 사용자 실패 직전 상태와 종료 throw 지점은 미확정이다.
- [x] 확인된 코드 경로의 수명·focus·pending 상태와 clipboard 오류 처리를 수정했다.
  복구 가능한 clipboard 접근 실패는 제한된 비동기 재시도와 사용자 상태 안내로 처리하되
  clipboard STA/UI thread 요구를 지킨다. 재시작을 기본 복구 수단으로 사용하지 않는다.
- [x] production custom key handler/host clipboard 경계의 동작 테스트와 전체 검증을 실행하고,
  실제 Windows/WebView2 장기 실행·RDP 전환/복구 검증의 인수 상태를 갱신한다.

### 완료 기준과 안전 경계

이번 구현 범위는 renderer production 키/DOM 입력 경계, Terminal Presentation의 STA clipboard
재시도·요청 수명 경계, WebView callback 예외 관측과 host 파일 진단이다. Clipboard는 최대
3회 시도하며 await 이후 활성 탭·세대·renderer 수명과 확인창을 다시 검사한다. Clipboard
원문·입력·출력·예외 message/stack을 기록하지 않는다. 전역 예외는 진단만 추가하고
무조건 Handled 처리하지 않는다. 위험은 지연된 paste의 잘못된 대상 전달과 확인창 우회이며,
production handler를 실행하는 Node 모의 테스트와 C# 주입 경계 테스트로 검사한다.
영향 파일은 지정 Presentation/source/dist, Terminal/IntegrationTests와 App.xaml.cs,
FileDiagnosticLog.cs 및 지정 문서로 제한한다. 실제 Windows 장기 실행·IME·RDP는 별도 미수행으로 남긴다.

- synthetic selection으로 `Ctrl+C`·`Ctrl+Shift+C`가 정확히 한 번 copy 요청을 만들고,
  selection 없는 `Ctrl+C`는 shell interrupt를 유지한다. 반복 입력·IME/AltGr 조합도 구분한다.
- `Ctrl+V`·`Ctrl+Shift+V`·native paste는 gesture당 한 번만 전달된다. 단일 줄·CR/LF 확인,
  취소·탭 변경·session generation 변경·clipboard busy/empty/비텍스트·renderer recovery를 검증한다.
  지난 중복 붙여넣기 수정을 되돌리거나 확인 없이 줄바꿈을 전달하지 않는다.
- 확인/이름 편집/저장 탭/search의 input에서는 해당 입력칸의 편집 동작을 허용하고 shell로
  전송하지 않는다. dialog 닫힘·Escape·blur·keyup 유실 뒤 억제 상태가 영구적으로 남지 않는다.
- 탭 선택·드래그/취소·레이아웃 복구/RDP 로컬 복귀 뒤 다시 terminal을 클릭하면 입력과
  복사·붙여넣기가 정상이어야 한다. 복구가 active ID/PID·cwd·입력·output·scrollback을 바꾸지 않는다.
- 모든 callback exception을 무조건 `Handled=true`로 삼키거나 clipboard 정상 실패를 앱 전체 종료로
  확대하지 않는다. throw 지점과 native/managed 수명을 판정한 뒤 회복 가능한 오류만 경계에서 처리한다.
  진단은 route 종류·선택 유무·일반 focus 역할·generation 유효성·예외 type/숫자 HRESULT 등
  허용된 최소 metadata로 제한하고 exception message/clipboard/terminal text를 저장하지 않는다.
- **Not run (manual):** 이번 조사에서는 현재 앱에 키를 보내거나 clipboard를 읽고 쓰지 않았고,
  충돌을 의도적으로 재현하거나 덤프를 수집하지 않았다. 구현 시
  [테스트 계획](../test-plan.md)의 MAN-029/029a와 RDP matrix에 장기 실행·재시작 회복·자발 종료 관찰을
  추가하며, 수행하지 못한 실제 항목은 미수행으로 남긴다. 이 읽기 전용 조사에서는 제품 수정·충돌 재현·배포를 수행하지 않았다.

### 2026-10-14 재개 범위 — terminal-clipboard-shortcut-regression

기존 완료 기록과 구현은 보존한다. 이번 할당 작업 폴더에서는 마지막 clipboard 실패가 OLE
재진입으로 탭·세대·확인창 수명을 바꾼 뒤에도 Unavailable을 반환하는 경계와, 검색 Escape로
focus가 terminal에 돌아온 뒤 held key가 전달되는 경계를 보강한다. 이름/저장 탭 편집 종료 키와
IME 중 application shortcut도 같은 production 입력 경계에서 확인한다. 영향 파일은 지정
Presentation, Terminal 테스트와 문서로 제한하며 dependency/transport/settings를 바꾸지 않는다.
최종 실패 후 수명 재검사, 편집 종료 키 억제와 fresh keydown/blur/keyup 복구를 동작 테스트한다.
이름 편집의 버튼 focus에서 발생하는 native dialog cancel도 입력칸 Escape와 같은 억제 경계로
연결한다. 이름 저장 Enter와 native cancel 뒤 held key/keypress 차단 및 다음 복사·paste 복구를
production renderer 모의 테스트로 추가한다. 기존 입력칸·확인창 계약과 원문 없는 진단은 유지한다.

검증 전에 생성 파일의 독점 ReadWrite 열기는 모두 성공했다. WPF MarkupCompile.cache와
sourcelink.json은 Archive이며 ReadOnly가 아니다. 실행 계정과 소유자가 일치하고 ACL은
Authenticated Users Modify를 허용한다. sandbox CIM 조회는 access denied였고 허용된 읽기 전용
조회에서는 nodeReuse MSBuild worker와 다른 도구의 프로세스가 보였다. worker 명령줄에 작업
폴더가 없어 이 폴더의 동시 빌드 충돌을 확정하지 않는다. 프로세스 종료·ACL/속성 변경·캐시
삭제는 하지 않는다. 지정 명령을 그대로 순차 실행하고, 실패 시 실제 파일/프로세스 근거를
추가한다. 이전 검증 수치는 이번 변경의 성공 근거로 재사용하지 않는다.

### 2026-10-14 재개 완료 기록 — Run 20261014-072003-981783-1949fb3a

- 기존 수정과 SAFE-02 계약을 보존했고, OLE 실패 catch 뒤 수명 재검사, 이름 저장/취소·저장 탭
  cancel·검색 Escape의 종료 키 억제 및 IME flag 보호를 완료했다. held key/keypress와
  keyup 유실 뒤 fresh press·blur 복구도 production handler로 검증했다. 새 전역 catch나
  terminal transport/settings/dependency 변경은 없다. renderer asset은 source로 재생성했다.
- 현재 변경의 npm ci/test/build **Passed**, Node **103/103**, Terminal **368/368**,
  최종 solution restore → Debug build → 전체 tests **610/610** (skip 0, build 경고·오류 0),
  C# 정렬 및 diff check **Passed**다. 과거 실행 수치를 이번 성공 근거로 재사용하지 않았다.
- 최초 기본 옵션 Debug build는 MarkupCompile.cache 삭제·sourcelink.json 쓰기 등 접근 거부로
  경고 2·오류 5였다. 실패 전후 직접 독점 열기/속성/소유자/ACL 검사는 정상이며,
  해당 폴더의 동시 build나 잠금은 미확인이다. 허용된 계정의 별도 handshake worker pool에서
  동일 검증 argv를 다시 실행해 통과했다. worker 재사용/실행 환경 문제는 추정이며,
  파일 권한 해결로 보고하지 않는다. `-m:1`·`-nr:false`, 다른 프로세스 종료, ACL/속성 변경과
  수동 캐시 삭제는 하지 않았다. sandbox esbuild EPERM과 focused .NET 정체도 보존한다.
- 상세 실패·조치·실행 환경과 결과는 [재개 검증 기록](../test-plan.md#할당-worktree-재개-검증--run-20261014-072003-981783-1949fb3a)에
  남겼다. 실제 자발 종료 throw 지점/사용자 증상 공통 원인은 미확정이고,
  Windows/WebView2 장기 실행·실제 clipboard·IME·RDP의 CLIP-MAN-01~05는 Not run (manual)이다.
  commit/push·앱 배포는 수행하지 않았다.

### 2026-10-14 Task 완료 기록 (이전 실행)

- 확인된 코드 문제: dialog의 전역 Ctrl 차단이 local 편집을 막음, 확인 키 억제가 keyup 유실 시
  남음, 선택 copy가 browser 기본 동작을 명시적으로 취소하지 않음, clipboard의 COMException
  catch가 WPF ExternalException을 놓침, renderer 세대 map이 지워진 뒤 늦은 요청이 generation 0
  session reference를 만들 수 있음. 실제 사용자 증상과 동일 원인임은 확인하지 못했다.
- production renderer의 copy/paste와 editor/dialog input 보호·fresh keydown/blur 복구를 수정했다.
  host clipboard는 3회 adapter 시도, 80ms 비동기 지연, STA 유지와 OLE 재진입/await 뒤 수명
  검사를 사용한다. 탭 왕복·세대/renderer 변경은 snapshot을 폐기하고 CR/LF 확인과 단일 전달을
  유지한다. 실패 안내는 theme 색상으로 표시하며 다음 성공 요청에서 지운다.
- WebView callback의 알려진 COM/disposed 오류, 실패 renderer drop 해제와 layout 조회의
  예상 오류를 해당 경계에서 관측했다. CTS 해제 전에 clipboard/layout 작업을 취소·관찰한다.
  App 전역 관측은 고정 예외 분류/HRESULT만 기록하며 Handled로 모든 예외를 삼키지 않는다.
- `npm ci`, production Node tests **87/87**, renderer offline build, Terminal tests **364/364**,
  solution restore/Debug build **경고·오류 0개**, 최종 전체 tests **606/606** (Architecture 13,
  Terminal 364, DesktopIntegration 120, Preferences 37, Integration 72), C# 정렬 검사와
  `git diff --check`를 통과했다. build/test는 초기 sandbox 하위 프로세스 EPERM·기존 병렬
  MSBuild worker 접근 오류 뒤 build-server 비활성·단일 노드로 실행했다. package manifest/lock과
  금지된 source 경로는 수정하지 않았다.
- 첫 전체 병렬 suite에서 `LocalWpfPanelPlacementPreservesForegroundAndRestoresCollapsedBounds`의
  실제 OS foreground HWND 비교가 한 번 실패했다. 해당 테스트를 수정하거나 skip하지 않았으며
  최종 단일 노드 전체 실행에서 통과했다. 외부 focus 경합 여부와 원인은 미확정이고 단순 통과로
  첫 실패 기록을 지우지 않는다.
- source에서 dist/app.js·app.css를 재생성했으며 license 내용은 보존했다. commit/push·앱 버전·
  바탕화면 배포는 수행하지 않았다. 실제 Windows clipboard, 장기 실행·자발 종료 재현·한글 IME·
  RDP는 CLIP-MAN-01~05 **Not run (manual)**이다.

### 2026-10-15 입력 진단·dispatcher 회귀 보완 계획

기준 `a9f6c9d61365c46d66ccc3faf0322491ecadc97c` 이후 clipboard adapter/lifetime은 변경되지
않았고 TerminalView 변경은 별도 layout 진입점 연결이다. 기존 키 억제·STA 3회 재시도·OLE
재진입 검사·callback 관측을 보존한다. 현재 입력 operation은 실제 63자로 64자 제한 이내이나,
parser에서 실제 파일까지의 회귀 검증이 없다. 짧은 고정 형식 `Input.Rr.Ss.Ff.Mm.Uu.Cc`로
변경하고 288개 허용 조합의 parser→FileDiagnosticLog 기록과 원문 배제를 검사한다.
FileDiagnosticLog의 64자/문자 제한과 임의 문자열 허용 범위는 변경하지 않는다.
영향 범위는 RendererInputDiagnostics, Terminal Presentation 테스트, FileDiagnosticLogTests와
지정 문서다. 기존 실제 dispatcher STA 테스트는 보존하고 await 중 수명 무효화만 보완한다.
지정 npm 검증, 집중 .NET 검사, solution restore→Debug build→전체 test와 정렬/diff를 실행한다.
실패 시 최신 최초 오류를 확인하며 이전 610개 성공과 이번 실행을 구분한다. 별도 worker pool,
권한/속성 변경, 프로세스 종료 또는 제한 우회는 하지 않는다. CLIP-MAN-01~05는 미수행 유지다.

### 2026-10-15 입력 진단 보완 결과

입력 operation을 `Input.Rr.Ss.Ff.Mm.Uu.Cc` 23자로 축약했다. 기존 63자 값도 현재 64자
제한 안에 있으므로 redaction 발생 사실은 확정하지 않는다. parser→실제 파일의 288개 조합,
원문/예외 privacy와 sanitizer 유지 테스트, 실제 dispatcher await 중 무효화 case를 보완했다.
ClipboardAccess/ClipboardRequestLifetime/TerminalView 및 renderer 동작은 변경하지 않았다.
이번 npm ci는 통과했으나 npm test/build는 esbuild spawn EPERM이고, C# 집중 검증은 정체/SDK
하위 작업 실패로 새 테스트가 실행되지 않았다. solution restore exit 1로 build/전체 test는
선행 조건 미충족 미실행이다. 동시 build 충돌을 확인하지 못해 단일 worker 옵션을 쓰지 않았고,
권한/프로세스/실행 정책을 바꾸지 않았다. 상세 최초 실패와 동일 계정 쓰기·ACL·잠금 조사 결과는
[이번 검증 기록](../test-plan.md#입력-진단-dispatcher-재검증-2026-10-15)에 남겼다.
기존 완료 구현·610개 성공은 보존하며 **이번 자동 검증은 미완료**다. 실제 자발 종료 원인과
CLIP-MAN-01~05의 미수행 상태는 유지한다.
