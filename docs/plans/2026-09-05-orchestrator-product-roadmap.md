# Starboard 실사용 개선 기획 및 오케스트레이터 실행 명세

## 목표

현재 terminal panel을 매일 켜 두고 사용할 수 있도록 Windows 동작을 안정화하고,
사용자가 재빌드 없이 설정을 바꿀 수 있게 하며, 재현 가능한 portable 배포를 만든다.
실행 순서는 실사용 기준선 확인 → Windows 안정화 → 설정 창 → 배포 정리다.

이 문서는 오케스트레이터에 전달할 작업 명세다. 문서 작성 시점에는 구현을 시작하지
않았다. 구현자는 아래 작업 ID, 선행 조건, 파일 소유권과 인수 기준으로 실행한다.

## 참고 문서와 근거

- `AGENTS.md`: 제품 원칙과 모듈러 모놀리스 경계
- `docs/development-workflow.md`, `docs/code-style.md`: 작업·검증·코드 규칙
- `docs/plans/2026-09-01-windows-starboard-v01.md`: 기존 Phase 2~4 구현 계획
- `docs/plans/2026-09-03-renderer-and-tab-ui.md`: 탭과 schema 4 높이 변경
- `docs/architecture.md`, `docs/test-plan.md`, `README.md`: 설계와 검증 이력

기존 계획을 대체하지 않고 남은 작업을 실행 가능한 묶음으로 구체화한다.
이번 작업의 기능 범위와 인수 기준은 이 문서를 따른다. 진행 결과는 기존 계획의
관련 체크박스와 테스트 문서에도 반영한다. 기존 문서의 시간 예상은 완료 보장이
아니며 오케스트레이터는 기준선 조사 뒤 실제 작업량을 다시 판단한다.

## 현재 상태

기준일 2026-09-05, 확인한 기준 커밋은 `285f6d1`이다. 구현 시작 시 최신 HEAD와
변경 상태를 다시 확인하고 실제 기준 커밋을 기록한다.

- WPF host + Terminal / DesktopIntegration / Preferences class library 구조다.
- 하나의 WebView2에서 최대 8개 탭과 탭별 ConPTY shell을 유지한다.
- 기본 높이는 200 DIP이며 노란 terminal focus border는 제거했다.
- `Ctrl+Alt+E`는 확장/축소, `Ctrl+Alt+S`는 호출/숨김이다.
- `TaskbarCreated`, display/settings/DPI 메시지를 받아 geometry를 다시 조회한다.
  메시지가 연결돼 있다는 사실만으로 hot-plug와 mixed-DPI 복구가 검증된 것은 아니다.
- 설정 schema 4, JSON 저장, 테마 4종은 있다. 설정 창과 live apply 경로는 없다.
- DesktopIntegration의 단축키는 현재 코드에 고정돼 있다. `ExpandShortcut` 설정이
  존재한다는 사실과 실제 등록에 사용되는지는 구분해야 한다.
- 전체 테스트 86개 통과는 2026-09-04의 기록이다. 실제 UI 검증은 미수행 항목이 있다.
- `.ai-orchestrator/`는 기존 미추적 실행 데이터다. 제품 산출물에 포함하지 않는다.

## 구현 범위

### A. Windows 안정화

사용자 시나리오: 다른 프로그램에서 일하는 동안 panel이 focus를 빼앗지 않고,
작업표시줄·모니터 상태가 바뀌어도 입력하던 shell이 유지된다.

| ID | 요구사항 | 완료 판단 |
|---|---|---|
| WIN-01 | 작업표시줄 자동 숨김을 따른다 | 비활성·축소 panel은 작업표시줄 숨김/표시를 따르고 입력 중 또는 확장 중에는 유지 |
| WIN-02 | 사용자 숨김 의도를 기억한다 | 자동 숨김 해제, 전체화면 종료, Explorer 재시작이 사용자가 숨긴 panel을 다시 열지 않음 |
| WIN-03 | 같은 모니터 전체화면 앱을 방해하지 않는다 | 전체화면 동안 자동 노출 억제, 종료 후 focus 없이 정상 정책 복귀 |
| WIN-04 | 모니터 제거 후 화면 밖에 남지 않는다 | 연결된 모니터의 유효 work area로 복구하고 shell PID 유지 |
| WIN-05 | DPI 변경에 맞게 배치·재렌더링한다 | 100/125/150/200% geometry 자동 테스트, 가능한 mixed-DPI 실제 검증 |
| WIN-06 | 확장 후 축소 위치를 복원한다 | 모니터 구성이 같으면 직전 축소 rectangle 복원, 제거됐으면 안전한 위치 재계산 |
| WIN-07 | Explorer 재시작에서 복구한다 | taskbar 재조회와 tray 복구 후 중복 icon·timer·hook 없이 호출/종료 가능 |
| WIN-08 | 창 상태 변화가 세션을 끊지 않는다 | 숨김/표시·확장/축소·모니터 이동 뒤 탭별 PID, 경로와 입력 상태 유지 |

표시 정책은 다음 우선순위로 하나의 상태 결정 경로에서 처리한다.

1. 사용자 숨김 상태는 사용자 호출 전까지 유지한다.
2. 같은 모니터의 다른 전체화면 앱이 foreground이면 자동 표시를 억제한다.
3. terminal 입력 중 또는 panel 확장 중에는 taskbar auto-hide로 숨기지 않는다.
4. 비활성·축소 상태에서는 taskbar visibility를 따른다.
5. 일반 상태에서는 normal z-order를 유지하고 자동 갱신은 활성화하지 않는다.

트레이나 호출 단축키는 사용자의 명시적인 활성화 요청이다. 이를 통해 사용자 숨김을
해제하고 일반적인 창 활성화를 요청할 수 있으나, exclusive fullscreen 위로
topmost를 강제하지 않는다. 다른 모니터의 전체화면과 일반 최대화 창은 WIN-03의
억제 대상으로 보지 않는다. 설정 창에서 편집 중일 때도 입력 중 상태로 취급한다.

### B. 설정 창과 자동 실행

사용자 시나리오: 트레이의 `설정`을 열어 높이·글꼴·테마를 바꾸고, 실행 중인
터미널 작업을 유지한 채 저장한다.

- 한국어 별도 WPF 설정 창 하나를 사용한다. 다시 열면 기존 창을 활성화한다.
- 섹션은 `화면`, `터미널`, `동작`으로 구성한다. 각 필드의 오류는 해당 필드 옆에
  표시하고 저장 실패 시 창과 편집 값을 유지한다.
- `저장`으로 검증·적용·영속화를 수행하고 성공 시 닫는다. `취소`나 창 닫기는
  편집 값을 폐기한다. `기본값 복원`은 편집 값만 바꾸고 저장 전에는 적용하지 않는다.
- live preview는 이번 범위에서 제외한다. 저장된 값과 현재 적용된 값이 달라지면
  성공으로 숨기지 말고 실패 항목과 복구 상태를 표시한다.

| 설정 | 기본값 / 범위 | 저장 후 동작 |
|---|---|---|
| 높이 | 200 DIP / 96~720 | 축소 높이를 갱신; 확장 상태라면 다음 축소에 적용 |
| 글꼴 | Cascadia Mono / fallback 유지 | 모든 탭 갱신, 활성 탭 fit·ConPTY resize; 숨긴 탭은 활성화 시 fit |
| 글자 크기 | 13 / 8~32 | shell 재시작 없이 적용 |
| 테마 | Tokyo Night / Dark, Light, One Dark, Tokyo Night | WPF 주변 색상과 모든 xterm ANSI palette를 함께 적용 |
| 투명도 | 0.97 / 0.72~1 | 현재 전체 창 투명도 방식 유지; 글자에도 적용됨을 UI에서 설명 |
| 기본 셸 | 자동 탐색 / 설치된 pwsh, powershell, cmd | 이후 생성한 탭에 적용; 현재 탭 및 현재 탭 재시작은 해당 탭의 셸 유지 |
| 확장 단축키 | Ctrl+Alt+E | 유효성·등록 충돌 검사 후 변경 |
| 호출 단축키 | Ctrl+Alt+S | 설정 model에 필드를 추가하고 기존 JSON은 기본값으로 읽음 |
| 로그인 시 자동 실행 | 꺼짐 | 사용자별 등록/해제; 현재 실행 파일의 절대 경로 사용 |
| 표시 모니터 | 작업표시줄 모니터 | 이번 범위에서는 지원 정책 안내만 제공; 임의 모니터 picker는 제외 |

상세 동작:

- 높이를 행 수로 표기할 경우 글꼴과 DPI에 따른 근사치임을 표시한다.
- 설정 UI 입력 오류는 조용히 clamp하지 않고 알려준다. 기존 JSON 복구용 정규화는
  유지한다. 취소·부분 JSON·이전 schema·손상 파일·저장 권한 오류를 처리한다.
- 두 단축키가 같으면 저장하지 않는다. 새 단축키 등록 실패 시 이전에 동작하던
  등록을 보존하거나 복구하고 어떤 키가 적용됐는지 명시한다.
- 설정 저장·단축키·자동 실행·renderer 적용은 서로 다른 실패 지점을 가진다.
  공통 계약 단계에서 적용 순서와 rollback을 정의한다. rollback까지 실패하면
  마지막 저장 값과 실제 적용 상태를 함께 보여 주고 다시 시도할 수 있게 한다.
- 자동 실행은 앱 소유 HKCU 항목만 변경하고 공백·한글 경로를 올바르게 인용한다.
  테스트는 adapter와 격리 경로를 우선 사용한다. 구현만으로 개발 PC의 로그인
  자동 실행을 켜지 않는다.
- 설정 창을 닫았다는 이유로 terminal에 focus를 강제하지 않는다.

### C. Portable 배포 정리

사용자 시나리오: 버전이 표시된 ZIP을 받아 압축을 풀고 실행한다. 업데이트 시
기존 사용자 설정을 유지하면서 새 폴더로 전환할 수 있다.

- 코드에서 일관된 버전을 사용하고 설정 창에 버전·빌드 커밋을 표시한다.
- Windows에서 restore/build/test/publish/package를 재현할 스크립트를 제공한다.
- 버전별 깨끗한 staging 폴더에 self-contained win-x64 Release를 publish하고,
  `Starboard-<version>-win-x64.zip` 및 SHA-256 파일을 만든다.
- local renderer asset, license, third-party notice와 실행 파일의 포함 여부를 검사한다.
- 개발 PC 경로를 하드코딩하지 않는다. 출력 폴더 안전성 및 기존 출력 덮어쓰기
  정책을 명시하고 다른 저장소·사용자 데이터를 정리 대상으로 삼지 않는다.
- 설정·로그·WebView2 사용자 데이터는 ZIP에 포함하지 않는다.
- 업데이트 문서는 앱 종료(실행 중 shell 작업 종료), 새 폴더 압축 해제, 실행,
  문제가 있을 때 이전 폴더로 되돌리기를 설명한다. 자동 실행이 켜져 있었다면
  실행 경로를 새 폴더로 갱신해야 한다는 절차도 포함한다.
- WebView2 Runtime은 별도 요구사항이다. .NET self-contained를 완전 무의존 실행으로
  설명하지 않는다. runtime 다운로드나 설치를 앱이 자동으로 실행하지 않는다.

## 제외 범위

2026-09-09 후속 후보를 [가벼운 작업공간 기획](2026-09-09-workspace-convenience.md)으로
구체화했다. 해당 문서는 별도 제안이며 아래 A~C의 완료 범위를 소급해서 늘리지 않는다.

이번 필수 범위는 A~C다. 탭 이름·순서 변경, 마지막 경로/탭 구성 복원은 후속 후보다.
세션 복원은 실행 중 프로세스나 명령을 자동으로 복구하는 기능과 구분해 별도 기획한다.
분할 화면, 탭 드래그, WSL/Git Bash/custom arguments, always-on-top 옵션, 가상
데스크톱 전체 pin, 설치 관리자, 코드 서명, 온라인 자동 업데이트는 포함하지 않는다.
CI workflow는 기존 환경을 확인한 후 별도로 제안할 수 있으나 이번 필수 배포는
로컬 Windows 스크립트다. 앱 runtime network·telemetry는 계속 사용하지 않는다.

이번 수동 검증 작업의 필수 범위와 완료 판단에서는 portable 패키징, 오프라인
Release 실행, 업데이트 및 rollback을 제외한다. 기존 자동 package/Release와
격리 smoke 기록은 삭제하지 않고 참고 자료로 보존하며, 배포 검증을 수행하지
않았다는 이유로 `Passed`로 승격하지 않는다. 셸 선택·지속 세션과
renderer·셸·ConPTY 실패 복구 및 재시작 결과만 유효한 recovery 검증 결과로
취급한다.

## 영향 파일과 아키텍처 결정

단일 애플리케이션 host와 기존 세 module을 유지한다. shell과 WebView2의 자식
프로세스는 이 배포 구조와 별개다. 별도 서버나 module 간 직접 참조를 추가하지 않는다.

| 소유 영역 | 주요 기존 파일 / 추가 위치 | 책임 |
|---|---|---|
| DesktopIntegration | `DesktopIntegrationModule.cs`, `Contracts/`, `Domain/PanelGeometryCalculator.cs`, `Infrastructure/TaskbarService.cs`, `WindowPlacementService.cs`, `TrayIconService.cs`, `Interop/` | native 관찰·창 정책·geometry·단축키·startup |
| Preferences | `Contracts/AppSettings.cs`, `Application/SettingsValidator.cs`, `Infrastructure/JsonSettingsStore.cs`, `PreferencesModule.cs`, 필요 시 `Presentation/` | 편집 model·유효성·저장·설정 content |
| Terminal | `TerminalModule.cs`, `Contracts/`, `Application/TerminalSessionCoordinator.cs`, `Presentation/TerminalView.xaml.cs`, `Presentation/Renderer/src/` 및 `dist/` | appearance 갱신·새 탭의 기본 셸 적용·세션 보존 |
| Host | `Composition/AppCoordinator.cs`, `Shell/MainWindow.xaml.cs`, 신규 최상위 설정 window | module 조정·최상위 window 수명·설정 적용/복구 순서 |
| 검증·배포 | `tests/Starboard.*`, `scripts/` 신규 package 스크립트, build metadata | 자동 검증·isolated smoke·ZIP 생성 |

경로는 `src/Modules/Starboard.Modules.<이름>/` 기준이며 실제 존재 여부를 확인해 수정한다.
최상위 설정 window는 host에 두고 편집 UI/검증은 Preferences가 소유한다. Preferences가
DesktopIntegration이나 Terminal을 직접 호출하지 않게 한다.

계약 확정 시 문서에 남길 내용:

- 창 표시 정책 입력 snapshot과 출력 결정, 사용자 숨김과 일시 억제의 구분.
- DPI 메시지에 필요한 매개변수 전달. 현재 `HandleWindowMessage(int, nint)`만으로
  필요한 정보가 충분한지 확인하고 host/native adapter 경계를 명확히 한다.
- 설정 변경 요청/결과, 설정 UI 열기 event, Desktop 옵션 적용/복구 계약.
- Terminal appearance와 새 탭 기본 셸 변경 계약. 기존 `StartAsync` 호출로
  live apply를 대신해 session을 재생성하지 않는다.
- 모든 탭의 theme/font 일관성 및 renderer ready 이전 변경의 최신 snapshot 보존.
- 동기화 방식과 Dispatcher 책임, native callback·timer·subscription 종료 순서.

## 오케스트레이터 작업 분해

각 작업은 자기 구현·직접 테스트를 함께 소유한다. 아래 ID를 task key로 사용한다.
공용 계약을 먼저 통합한 기준에서 worker worktree를 만든다. 문서 통합과 host 수정은
integration 담당자 한 명이 소유하며 worker는 변경 요청과 인수인계로 전달한다.

| 작업 ID | 선행 조건 | 독점 쓰기 범위 | 산출물 / 통과 기준 |
|---|---|---|---|
| P0-baseline | 없음 | 이 문서 진행 기록, 테스트 기록 | 최신 코드·실제 UI 기준선, API 조사 목록, 미수행 장비 항목 |
| P1-window-contracts | P0 | Desktop `Contracts/`, 정책 입력/출력 타입 | WIN 상태 우선순위, DPI/monitor 전달 계약, host 변경 명세 |
| P2-window-policy | P1 | Desktop 정책 Application/Domain 신규 파일 및 해당 tests | auto-hide/전체화면/명시 숨김 조합의 순수 상태 결정과 단위 검증 |
| P3-window-adapters | P1 | Desktop Infrastructure/Interop, geometry 및 해당 tests | monitor/DPI/taskbar/foreground 관찰, native adapter와 복구 검증 |
| P4-window-integration | P2, P3 | Desktop module 진입점, host, integration tests, 공용 문서 | policy+adapter+tray 연결, WIN-01~08 회귀 및 A gate |
| P5-settings-contracts | P4 A gate | module public contract, AppSettings schema, 이 문서 | live apply/default shell/startup/hotkey와 rollback 계약 확정 |
| P6-settings-editor | P5 | Preferences 구현·Presentation·tests | 한국어 설정 content, validation, migration, 안전한 저장 |
| P7-terminal-settings | P5 | Terminal 구현·renderer source/dist·tests | session을 유지하는 전체 탭 appearance 및 이후 새 탭 셸 적용 |
| P8-desktop-settings | P5 | Desktop 구현·tests | 높이·단축키 교체/복구·자동 실행 adapter·트레이 설정 요청 |
| P9-settings-integration | P6, P7, P8 | host, integration tests, 공용 문서 | 설정 창 수명, 저장/적용/복구 연결, B gate |
| P10-package | P9 | scripts, 버전 build metadata, packaging docs | 재현 가능한 ZIP·해시·버전 표시 연결 및 package 검증 |
| P11-final-verification | P10 | 공용 문서와 필요한 회귀 수정 | 통합 build/test/publish, release smoke, A~C 인수 보고 |

실행 wave는 `P0 → P1 → (P2 ∥ P3) → P4 → P5 → (P6 ∥ P7 ∥ P8) → P9 → P10 → P11`이다.
괄호 안 작업만 병렬 실행한다. P2/P3의 테스트 파일도 서로 구분하고 공용 파일은
미리 담당자를 지정한다. 겹침이 생기면 동시에 편집하지 말고 직렬 통합한다.

worker 인수인계에는 작업 ID, 실제 기준 커밋, 변경 파일, 계약 변경 여부, 실행한
검증과 결과, 미수행 항목, 통합 담당자에게 필요한 변경을 포함한다. 다른 worker가
아직 통합하지 않은 파일이나 공용 dirty tree를 자신의 전제 조건으로 삼지 않는다.

integration 담당자는 worker 결과를 해당 wave가 시작한 기준 위에 순서대로 통합하고
전체 검증을 수행한다. 공용 계약 수정이 필요하면 영향 worker와 계획부터 갱신한다.
원래 작업 트리의 기존 변경을 stash/reset/삭제하지 않는다.

### P9 설정 통합 실행 범위

P6~P8의 편집기와 module별 적용·복구 구현을 기준선으로 유지하고, P9에서는
`Starboard.Windows` composition root만 세 공개 계약을 연결한다. Preferences가
검증한 snapshot을 Terminal, DesktopIntegration, 영속화 순서로 적용하며 뒤 단계가
실패하면 DesktopIntegration, Terminal 역순으로 이전 snapshot을 요청한다. 각 module이
보고한 effective snapshot과 마지막 persisted snapshot은 host가 별도로 보관해 복구
불완전 상태를 설정 창에 표시한다.

host의 설정 window는 하나만 소유한다. tray의 명시적 설정 요청은 기존 window가
있으면 그 창만 활성화하며, 닫기 경로에서는 panel이나 다른 foreground window를
명시적으로 활성화하지 않는다. Terminal 설정 변환은 live appearance와 이후 새 tab의
기본 shell만 전달하므로 기존 session의 PID와 working directory 수명은 P7 계약을
그대로 따른다.

P9 영향 파일은 `src/Starboard.Windows/Composition/`, `src/Starboard.Windows/Shell/`,
host project 설정, integration/architecture tests와 이 roadmap·architecture·test plan이다.
module 내부, renderer asset, 설정 schema와 저장 위치는 변경하지 않는다. 저장 실패나
module apply 실패를 재현하는 host seam은 production module public result type만 사용하며
native/renderer 구현을 host로 끌어올리지 않는다.

## 위험 영역과 fallback

| 위험 | 대응 |
|---|---|
| 자동 숨김 설정과 실제 taskbar visibility는 다름 | 문서화된 API와 실제 관찰을 대조; 모호하면 마지막 안전 위치 유지, 짧은 sampling은 상태 변화 동안만 |
| 전체화면 오탐과 focus 변화 | 최대화/다른 monitor/자기 창 제외 검증; 탐지 실패 시 normal z-order를 유지하고 focus를 요청하지 않음 |
| 모니터 사라짐과 cached rectangle | 연결된 monitor와 교차 확인 후 복원, 그렇지 않으면 최신 snapshot으로 배치 |
| 저장과 OS 적용의 부분 실패 | 이전 snapshot 보관, rollback 가능한 operation으로 구분, 실패 UI와 재시도 경로 |
| shell 설정으로 기존 작업 종료 | 기본 셸 변경은 새 탭만 대상으로 하고 기존 PID 보존을 검증 |
| 실패 시 반복 로그·남은 timer | 같은 오류 로그 제한, cancellation과 dispose 이후 callback 차단 |
| renderer 재빌드 누락 | source 변경 시 dist 재생성, 실제 publish 디렉터리의 CSS/JS로 검증 |
| portable 자동 실행 경로 변경 | 업데이트 절차에서 새 실행 경로 등록 확인, 이전 경로로 자동 실행되는지 점검 |

Windows API 동작은 이 기획만으로 검증 완료된 것으로 취급하지 않는다. P0/P1에서
공식 문서와 로컬 코드를 조사하고 구현자가 실제 관찰·fallback을 architecture에 남긴다.

## 구현 단계

- [ ] P0 기준선·환경·실제 UI 확인
- [x] P1~P4 Windows 안정화 코드와 자동 A gate (hardware matrix는 pending)
- [x] P5~P9 설정 기능과 자동 B gate (실제 tray/focus·실패 UI 수동 matrix는 pending)
- [x] P10~P11 portable 배포와 자동 C gate (기존 자동 결과 기록; 이번 수동
  배포 완료 판단에서는 제외)

각 gate는 기능 구현과 자동 검증이 완료되고, 실제 실행으로 확인 가능한 핵심 동작이
확인돼야 통과한다. 장비가 없어 수행하지 못한 항목은 예상 결과·실행 절차와 함께
pending으로 남긴다. 해당 플랫폼의 release 검증 완료로 표시하지 않는다.
핵심 입력·focus·session 유지 회귀가 있으면 다음 wave로 진행하지 않고 수정한다.

## 검증 방법

1. 기존 기준선: 탭 3개, 한글 입력, copy/paste, Ctrl+C, 호출/숨김, 확장/축소,
   200 DIP 높이와 노란 테두리 제거를 확인한다. 기존 테스트 통과 숫자를 재사용하지 않는다.
2. Windows: WIN-01~08을 자동 상태 전이/geometry 테스트와 실제 환경 검증으로 나눈다.
   사용자 숨김 → 전체화면 진입/종료 → Explorer 복구 뒤에도 숨김 유지 사례를 포함한다.
3. Settings: 취소, 기본값 복원, invalid 값, 이전 JSON, 저장 실패, hotkey 충돌,
   적용 중 실패/rollback, startup adapter 실패와 공백 경로를 검증한다.
4. Terminal: 3개 탭의 PID·경로를 보존한 채 theme/font/height를 변경한다. 비활성
   탭 활성화 시 올바른 geometry와 색상, 새 탭에만 새 셸 적용을 확인한다.
5. Packaging: 깨끗한 출력에서 ZIP 생성, 필수 asset/license 포함, 사용자 데이터
   미포함, SHA-256 일치 및 추출된 실행 파일의 실제 renderer/shell 동작을 확인한다.
   이 packaging 검증과 오프라인 Release, update/rollback은 이번 수동 검증
   작업의 범위 밖이며 완료 판단에 포함하지 않는다.

표준 전체 검증 명령(저장소 루트):

```powershell
dotnet restore Starboard.Windows.sln --disable-parallel --disable-build-servers -maxcpucount:1
dotnet build Starboard.Windows.sln -c Debug --no-restore --disable-build-servers -maxcpucount:1
dotnet test Starboard.Windows.sln -c Debug --no-build --no-restore --disable-build-servers -maxcpucount:1
dotnet restore src/Starboard.Windows/Starboard.Windows.csproj -r win-x64 --disable-parallel
dotnet publish src/Starboard.Windows/Starboard.Windows.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=false -o artifacts/Starboard-win-x64
git diff --check
```

로컬 `dotnet`이 없으면 README의 user-local SDK 경로를 확인한다. renderer 변경 시
해당 renderer 디렉터리에서 `npm ci`, `npm run build`를 실행한다. 의존성 다운로드나
native tool 실행이 sandbox에서 차단되면 해당 권한 절차로 해결한다.

실제 UI와 window rectangle을 관찰한다. `Get-Process`의 Responding만으로 renderer,
ConPTY, visible window가 정상이라고 보고하지 않는다. Explorer 재시작·로그인 변경·
앱 교체처럼 사용자 환경에 영향을 주는 검증은 실행 시 확보된 권한 범위에서 수행한다.
진행 중인 사용자 shell을 임의 종료하지 말고 테스트 instance의 소유 경로와 PID를 확인한다.

## 진행 기록

- 2026-09-05: 기준 코드와 기존 계획을 확인하고 이 문서를 작성했다. 제품 코드는
  수정하지 않았고 오케스트레이터 실행·build/test는 이번 문서 작업에서 수행하지 않았다.
- 2026-09-07 (P4-window-integration 착수): P2 정책 reducer와 P3의 monitor/taskbar/DPI,
  foreground/fullscreen 관찰 adapter가 존재하지만 기존 module 진입점은 단순 geometry
  timer만 사용하고 host는 축약된 window message만 전달하는 상태임을 확인했다. P4는
  `DesktopIntegrationModule`이 정책 상태와 observer 수명을 소유하고, host가 정책의
  표시 요청만 WPF `Show`/`Hide`로 반영하도록 연결한다. background reconciliation은
  activation과 z-order를 보존하며, tray/호출 단축키만 사용자 표시·활성화 요청으로
  처리한다. 자동 검증은 가짜 native/placement/observer seam을 사용한 상태 전이와
  전체 solution test로 수행하고, 실제 Explorer 재시작·auto-hide·exclusive fullscreen·
  multi-monitor/mixed-DPI는 별도 hardware/manual 결과로 남긴다.
- 2026-09-07 (P4-window-integration 완료): module 진입점이 user visibility, mode,
  engagement와 last-safe frame을 reducer 입력으로 관리하고 display/foreground observer,
  1초 fallback, tray 재생성과 전체 window message를 연결했다. host는 policy presentation을
  dispatcher에서 반영하고 `Activated`/`Deactivated`만 engagement로 전달한다. simulated
  integration 6개와 실제 WPF HWND smoke 1개가 통과했으며, smoke에서 실제 bottom
  taskbar work area 안 배치, focus 보존과 expand/collapse 복원을 확인했다. 실행 중인
  별도 설치본의 single-instance guard 때문에 이번 build의 전체 tray/terminal executable
  smoke는 보류했고 Explorer 재시작, auto-hide, fullscreen, multi-monitor/mixed-DPI 실제
  장비 항목도 `docs/test-plan.md`에 `Not run` 또는 `Blocked`로 유지했다. 지정된 restore,
  Debug solution build와 전체 137개 automated test가 모두 통과했다.
- 2026-09-08 (P9-settings-integration 완료): Preferences editor의 validated save callback을
  host가 Terminal, DesktopIntegration, WPF surface, persistence 순서로 조정하고 persistence
  또는 뒤 단계 실패 시 DesktopIntegration, Terminal 역순으로 마지막 persisted snapshot을
  적용하게 했다. persisted/effective 상태가 갈라지면 설정 창에 두 값을 구분해 표시하며
  editor draft를 유지한다. tray 설정 요청은 단일 창을 재사용하고 닫기에서는 다른 창을
  활성화하지 않는다. build server 종료 후 지정 SDK로 restore, integration build/test와
  Debug solution build 및 전체 180개 automated test가 통과했다. 실제 tray focus, 실제
  renderer failure UI와 로그인 startup은 manual matrix에 `Not run`으로 유지했다.
- 2026-09-08 (P10-package 착수): 제품 버전은 `Directory.Build.props`의 단일 값으로
  관리하고 package 시 확인한 Git HEAD를 host assembly metadata와 배포 metadata에 함께
  주입한다. `scripts/package-portable.ps1`은 저장소 루트를 Git으로 재확인한 뒤
  `out/portable/<version>/staging`만 정리하고 Release restore/build/test,
  self-contained win-x64 publish, 결정적 파일 순서·시각의 ZIP, SHA-256 및 추출 smoke를
  한 흐름으로 수행한다. smoke mode는 실제 Preferences·startup adapter를 시작하지 않아
  기존 사용자 설정과 자동 실행 경로를 변경하지 않으며 renderer 파일, 기본 shell,
  제품 버전과 build commit 일치를 검사한다. 기능 module 구현과 renderer asset은 변경하지
  않는다.
- 2026-09-08 (P10~P11 완료): build server를 정상 종료한 뒤 지정 SDK의 Debug
  restore/build와 전체 186개 test가 통과했다. package 흐름에서도 Release build/test,
  self-contained win-x64 publish, 499-entry ZIP과 SHA-256 일치, 동일 staging의 ZIP 재생성 및
  clean staging 전체 재실행 hash, 필수 renderer/license/notice, executable과 release metadata의 version/full commit
  일치 및 추출 smoke가 통과했다. 설정·로그·WebView2 user data·PDB·개발 PC 절대 경로는
  package에서 거부된다. 실제 WebView2 UI/interactive shell, multi-monitor/mixed-DPI,
  fullscreen/auto-hide/IME와 portable update/startup/rollback은 수행하지 않았고 test plan에
  `Not run`으로 유지했다.
- 2026-09-10 (window-platform-manual-validation): 실제 Windows 11 Pro build 26200
  장비에서 1920×1080 LG monitor 2대, 왼쪽 secondary의 X=-1920 음수 좌표, 두 화면의
  Y=0부터 높이 1032px인 work area와 각 monitor의 100% 배율을 확인했다. 그러나 기준
  app UI 제어가 승인되지 않았고 terminal application인 Starboard의
  launch/input/hotkey 자동화도 정책상 금지됐다.
  명령 세션은 interactive Explorer taskbar HWND를 볼 수 없어 focus/rectangle/z-order를
  별도 측정할 수도 없었다. MAN-001과 MAN-003~MAN-022는 항목별 장비·권한 제약 및 재개
  조건을 적어 `Blocked`로 갱신했으며, 실제 taskbar 비겹침, background focus 보존과
  exclusive fullscreen 억제를 통과로 판단하지 않았다. P0의 실제 UI 기준선과 Windows
  hardware matrix는 계속 미완료다.

## 미결정 사항

제품 범위와 기본값은 위와 같이 정했다. 제품 버전 `0.1.0`은 중앙 build property로
확정했고 향후 release에서 이 값만 변경한다. 외부 서비스나 새로운 대형 의존성이
필요하면 기존 구조로 가능한 대안과 함께 별도 판단 사항으로 기록한다.

## 완료 요약

### 2026-09-09 바탕화면 배포 시작 오류 후속

- 실제 배포본과 최소 WPF integration test에서 `SetLayeredWindowAttributes`가
  Win32 error 87로 실패함을 재현했다. `AllowsTransparency=false`인 WPF가
  `WS_EX_LAYERED`를 제거하므로 외부 native opacity 설정과 호환되지 않는다.
- 변경 범위: DesktopIntegration의 opacity 적용을 host가 전달하는 동기 callback으로
  연결한다. host는 기존 WPF `Opacity` 속성을 사용하며 module 간 직접 참조는 추가하지
  않는다. 실패/rollback 순서는 유지하고 미사용 native opacity API를 제거한다.
- 영향 파일: `DesktopIntegrationModule`, `WindowPlacementService`, `NativeMethods`,
  `AppCoordinator`, 신규 `PanelOpacityIntegrationTests`, architecture/test-plan 문서.
- opaque WPF 및 child-HWND WebView2 제약 때문에 desktop 전체에 대한 진짜 반투명 효과는
  보장하지 않는다. `AllowsTransparency` 변경이나 renderer 교체는 이번 범위가 아니다.
- 검증: 재현 test의 실패→통과, 실제 WPF에서 opacity 반복 적용과 focus/geometry 보존,
  전체 Debug/Release suite, portable package 검사, 바탕화면 교체 및 실제 재실행 확인.
- 상태: 수정, Debug/Release 각각 전체 188개 test 및 package 검증 완료.
  실제 WPF 회귀 test에서 native setter 오류를 먼저 재현했고, 수정 후 opacity 반복
  적용의 bounds/focus 보존과 실패 전파를 확인했다. 바탕화면 기존 폴더를 백업한 뒤
  499개 파일 hash를 검증해 교체하고 재실행했다. 앱 응답과 PowerShell/ConPTY/WebView2
  process를 확인했다. computer-use에서 panel HWND를 찾지 못해 실제 화면·입력과
  전체 hardware manual matrix는 미검증이다. 상세 결과는 test-plan을 따른다.

P4 Windows 창 통합, P5~P9 설정 적용·복구와 P10~P11 portable package 자동 C gate를
완료했다. 중앙 version과 build commit이 executable, 설정 화면과 release metadata에서
일치하며 version staging 밖을 정리하지 않는 self-contained ZIP/SHA-256 흐름과 추출 smoke를
제공한다. 실제 display/fullscreen, tray/focus/settings failure, WebView2/terminal UI와
portable update/rollback hardware matrix는 이번 수동 검증 범위에서 제외한다.
portable 패키징과 오프라인 Release 실행도 같은 범위에서 제외하며, 기존 자동·
격리 결과는 배포 검증 완료가 아닌 보존 기록으로 취급한다.

### 2026-09-10 설정 수동 검증 인수인계

- 실행 중인 portable `Starboard.exe`(PID 102920)와 96 DPI 환경은 확인했지만,
  Computer Use에 panel/tray window가 targetable window로 노출되지 않았다. tray 설정 창,
  저장/취소 및 global hotkey conflict UI를 조작할 수 없어 MAN-023, MAN-031,
  MAN-037 및 MAN-038은 `Blocked`로 갱신했다.
- `%LOCALAPPDATA%\Starboard`에 `settings.json`이 없고 HKCU Run `Starboard` 값도 없는
  상태만 읽기 전용으로 확인했다. theme·font·height apply 또는 rollback 중 terminal PID와
  working directory가 보존되는지는 아직 실측하지 못했다. process CIM 조회도 access
  denied였다.
- 다음 interactive desktop 검증은 설정 창을 두 번 열어 단일 instance와 foreground
  보존을 확인하고, tab shell PID/cwd를 기록한 뒤 theme·font·height save 및 실패 rollback,
  hotkey conflict, startup 등록/해제 순으로 수행해야 한다. 자세한 matrix와 환경 제약은
  [테스트 계획](../test-plan.md)을 따른다.

### 2026-09-10 release recovery 수동 검증 인수인계

- current HEAD package는 Release 경고·오류 0개, 301개 test, 499-file ZIP과 추출 smoke를
  통과했고 SHA-256은
  `29222a0d423870430cd3a2b1b67f668ba85cef80e51658fd085e96d27372b546`다. 최초 restore는
  sandbox의 NuGet vulnerability source 차단으로 실패했고 허용된 network에서 재실행했다.
- 바탕화면 PID 102920을 유지하기 위해 이전/current 복사본의 mutex 문자열 한 곳만
  같은 길이의 test 이름으로 바꿨다. 계측 current의 renderer PID만 종료해도 host와 shell이
  유지되고 `RenderProcessExited`가 기록됐고, shell PID만 종료해도 host와 `ShellExit`가
  유지됐다. UI 오류 표시·restart click과 다른 tab 상태는 미확인이다.
- 이전 build commit `a225da3b9c34ea0a264c095dbb73404efeb20b6e` → current
   `456049b862b6bfdf60c58722a2c223fe680c1d62` → 이전 smoke는 모두 exit code 0이었다.
  settings/workspace와 HKCU Run 값은 원래부터 없고 전후 그대로여서 실제 설정 보존,
  startup 경로 갱신/복귀와 schema downgrade는 검증하지 못했다.
- 세 기본 shell은 별도 지속 process probe에서 환경/cwd를 유지했고 두 PowerShell은
  history도 유지했다. 제품 UI resize·cmd Unicode·WSL/custom은 통과로 판단하지 않았다.
  Runtime 누락 override와 실제 network-disabled WebView2도 미완료다. 항목별 Partial,
  Blocked, Not run 상태와 재개 조건은 [테스트 계획](../test-plan.md)에 기록했다.

  portable 패키징·오프라인 Release·update/rollback 항목은 미완료 배포 검증으로
  해석하지 않고 `Out of scope`로 관리한다. 이 기록에서 셸 선택·지속 세션과
  renderer·셸·ConPTY 실패 후 host/session 유지 및 재시작만 이번 작업의 유효한
  recovery 검증 결과다.

## 오케스트레이터 전달 프롬프트

```text
Starboard 저장소의 docs/plans/2026-09-05-orchestrator-product-roadmap.md를 실행 명세로
사용해 Windows 안정화, 설정 창, portable 배포 정리를 구현해줘.

먼저 AGENTS.md와 개발 규약을 읽고 현재 HEAD/dirty 상태를 확인해.
P0~P11 의존성 순서와 gate를 지키고, 공용 계약을 통합한 기준에서 작업별 격리
worktree를 사용해. 병렬 실행은 P2/P3 및 P6/P7/P8에 한정하고 host·공용 문서는
integration 담당자 한 명이 소유하게 해. 작업별 산출물을 순서대로 통합하고
관련 테스트와 최종 전체 build/test/publish를 수행해.

기존 세 module의 모듈러 모놀리스 경계, 200 DIP 기본 높이, 노란 terminal 테두리
제거, 탭별 persistent shell과 사용자 focus 보존을 유지해.
API 가정은 공식 문서·로컬 실험으로 확인하고 실제 UI 미검증은 pending으로 기록해.
테스트 process가 살아 있다는 사실만으로 UI 정상 동작을 단정하지 마.

이번 실행은 문서의 A~C를 완료하고 검토 가능한 통합 결과와 ZIP을 준비하는 범위야.
작업 branch/worktree와 작업 커밋은 사용할 수 있어. main 반영·원격 push·GitHub
Release 게시·사용 중인 바탕화면 앱 교체는 이번 실행에 포함하지 마.
최종 보고에 기능별 완료 상태, 검증 증거, 남은 수동 시나리오, 변경 파일과
통합 branch/커밋, ZIP 경로를 남겨줘.
```
