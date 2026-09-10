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
