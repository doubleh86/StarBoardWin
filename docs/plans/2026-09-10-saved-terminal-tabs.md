# 저장한 탭 — 프로젝트 폴더에서 바로 시작

## 목표와 완료 조건

제품 방향은 [가벼운 터미널 기획](2026-09-10-lightweight-terminal-roadmap.md)을 따른다.
작업표시줄에 붙어 빠르고 간단하게 사용하는 도구로 유지하며, 분할 화면은 추가하지 않는다.
여러 세션은 탭 전환으로 다룬다.

사용자가 탭 이름·시작 폴더·셸을 즐겨찾기로 저장하고, 필요할 때 선택해 해당 폴더에서
새 터미널을 바로 열 수 있게 한다. 2026-09-10 통합 작업은 앞서 구현된 저장 정책과
renderer UI를 `TerminalModule`/`TerminalView` 수명 주기에 연결하고, package 개인정보
제외와 자동·수동 검증 문서를 완결한다. commit/push·배포는 포함하지 않는다.

- 저장한 항목을 선택하면 지정 폴더·셸·이름으로 새 탭 하나가 열린다.
- 저장·편집·삭제는 이미 실행 중인 탭이나 셸을 바꾸거나 종료하지 않는다.
- 기존 최대 8개 실행 탭, 안전 확인·새 출력 표시·비방해 focus 정책을 유지한다.
- 앱 재실행 후에도 저장한 목록은 유지되지만 목록 전체를 자동 실행하지 않는다.
- 탭 이름 편집 UI 회귀는 별도 TAB-UX-03으로 해결하고 새 메뉴와 통합 검증한다.

## 기준선과 기존 기능의 차이

2026-09-10 조사 기준은 `main`의 `a225da3`이다. 구현자는 실행 시작 시 최신 main과
배포 버전, 미커밋 문서와 진행 중 오케스트레이터의 파일 소유권을 다시 확인한다.

- 현재 탭 이름·순서·시작 폴더 설정과 opt-in 작업공간 복원은 구현돼 있다.
- `TerminalWorkspaceTabConfiguration.StartingDirectory`는 사용자가 설정한 시작 폴더다.
  실행 중 `cd`로 변경한 실제 working directory는 추적하지 않는다.
- 기존 작업공간 복원은 마지막 실행 탭 구성의 재생성이다. 이번 기능은 사용자가
  명시적으로 저장한 재사용 목록이며 둘의 저장·삭제 수명은 분리한다.
- 관련 정본: [작업공간 기획](2026-09-09-workspace-convenience.md),
  [탭 UI의 TAB-UX-03](2026-09-03-renderer-and-tab-ui.md),
  [안전 기능 기획](2026-09-09-terminal-safety-and-discoverability.md).

## 사용자 흐름

### 저장

1. 탭 우클릭 또는 Shift+F10 메뉴에서 `저장한 탭에 추가…`를 선택한다.
2. 이름·시작 폴더·셸을 확인하는 작은 편집 화면을 연다. 초기값은 해당 탭의 설정값이다.
   `현재 터미널에서 이동한 폴더와 다를 수 있습니다`를 표시하고 폴더를 직접 선택할 수 있게 한다.
3. `저장`하면 목록에 추가하고 현재 탭과 입력 상태는 그대로 둔다. 취소는 아무것도 저장하지 않는다.
   저장됐다는 이유로 새 탭을 추가하거나 셸에 명령을 보내지 않는다.

#### 다음 버전 UX 보완 — 현재 탭 메뉴에서 저장

- 사용자 확인(2026-09-11): 탭 context menu의 `이름 변경`, 이동, `시작 폴더 설정`과 같은
  위치에 `저장한 탭에 추가…`를 노출한다. 별도의 `저장한 탭` 관리 화면을 먼저 열어 새 항목을
  만드는 우회 동선은 요구하지 않는다.
- 선택하면 즉시 영속화하지 않고 기존 저장 항목 생성 dialog를 연다. 이름은 현재 탭 이름,
  시작 폴더는 탭에 설정된 시작 폴더, 셸은 해당 탭의 shell kind로 미리 채워 사용자가 확인한 뒤
  `저장`한다. 실행 중 `cd`로 이동한 현재 working directory를 추측하거나 자동 수집하지 않는다.
- 저장·취소·validation과 20개 한도는 기존 저장 service 계약을 재사용한다. 한도에 도달하면
  menu item을 비활성화하고 이유를 접근 가능한 이름이나 안내로 제공한다.
- context menu를 keyboard로 연 경우에도 항목에 도달할 수 있어야 하며 Enter로 dialog를 열고,
  dialog 취소 후에는 원래 탭으로 focus를 돌린다. 저장 과정에서 셸 입력, PID, scrollback과
  현재 탭 선택을 변경하지 않는다.
- 2026-09-11 main 구현 확인 결과 context menu에는 이름 변경·좌우 이동·시작 폴더 설정만 있고
  이 항목이 누락돼 있다. 저장 backend와 관리 dialog는 이미 있으므로 다음 버전에서는 renderer
  context-menu 연결과 source/dist 회귀 검증을 최소 변경 범위로 삼는다.

### 저장한 항목에서 시작

- 기존 `+` 클릭은 기본 새 탭으로 유지한다. 바로 옆 독립 `⌄` 메뉴의 이름은 `저장한 탭`으로 한다.
- 목록은 저장 이름과 시작 폴더를 보여 준다. 항목을 선택하면 새 탭을 만들고 그 탭을 선택한다.
  이 선택은 사용자의 명시적 동작이며 background 저장/로드는 focus를 빼앗지 않는다.
- 저장 항목은 예를 들어 `서버 · C:\Work\TRK-Server`처럼 구분한다. 경로는 사용자 입력값이며
  예시를 제품 기본값이나 자동 스캔 대상으로 넣지 않는다.
- 빈 목록에는 `저장한 탭이 없습니다`와 추가 안내를 제공한다. `저장한 탭 관리…`에서
  추가·편집·삭제할 수 있게 한다. 저장 이름·폴더는 HTML이 아닌 텍스트로 표시한다.
- 메뉴는 32px 탭 바를 늘리지 않고 사용 가능 영역 안으로 펼친다. 작은 패널에서는 목록을
  스크롤하며 작업표시줄을 덮거나 메뉴 때문에 패널을 자동 확장하지 않는다.
- 키보드로 메뉴 진입·이동·선택·Escape 취소가 가능해야 한다. 닫을 때 기존 focus 대상으로
  복귀하고 terminal 명령 입력으로 메뉴 키가 새지 않아야 한다.
- 여러 실행 탭을 한 번에 저장하는 그룹이나 기본 시작 그룹 선택은 이번 범위에서 제외한다.
  앱을 켤 때 같은 탭들이 열리길 원하면 기존 작업공간 복원 옵션을 사용한다.

## 동작·저장·실패 정책

- 저장 필드는 schema, 독립 preset ID, 이름, 명시적인 로컬 절대 시작 폴더와 지원 셸 종류다.
  지원 셸은 기존 `자동`, `pwsh`, `powershell`, `cmd`를 재사용한다. 초기 상한은 저장 항목 20개,
  이름 길이·경로 허용 기준은 기존 탭 검증 정책을 재사용한다. 상한은 실행 시 검증한다.
- 동일 이름은 허용하되 경로·셸로 구분하며 ID로 편집·삭제한다. 같은 항목을 다시 고르면
  별도 새 세션을 연다. 단, 생성 응답을 기다리는 중의 중복 클릭은 하나의 요청으로 처리한다.
- 실행 탭이 8개면 새 탭을 만들지 않고 한도를 안내한다. 저장 목록을 읽고 편집·삭제하는
  기능은 계속 사용할 수 있다. 기존 탭을 자동으로 닫거나 교체하지 않는다.
- 저장 및 실행 시 폴더·셸을 각각 검증한다. 저장 후 삭제된 폴더나 찾을 수 없는 셸은
  해당 실행만 오류로 표시하고 수정·재시도 경로를 제공한다. 홈 폴더로 조용히 대체하지 않는다.
- 폴더는 ConPTY 셸 시작의 working-directory 인자로 전달한다. `cd` 문자열이나 시작 명령을
  만들어 입력하지 않는다. 임의 executable·arguments·명령 자동 실행은 제외한다.
- 저장 목록은 Terminal이 소유하는 별도 per-user JSON으로 관리한다(후보 `saved-tabs.json`).
  기존 workspace JSON을 재사용하거나 복원 옵션 해제 시 즐겨찾기를 함께 삭제하지 않는다.
- schema 검증·파일 크기 제한(초기 64 KiB)·원자적 쓰기·단일 writer·종료 시 bounded flush를
  적용한다. 파일 부재는 빈 목록, 손상은 복구 안내, 미래 schema는 덮어쓰기 금지로 처리한다.
  읽기/쓰기 실패가 terminal 시작이나 앱 종료를 무한 대기시키지 않아야 한다.
- 삭제는 해당 저장 항목만 제거한다. 이미 연 탭과 작업공간 복원 snapshot을 소급 변경하지 않는다.
- command/output/history/clipboard/environment/PID/scrollback은 저장하지 않는다.
  저장 파일·backup·임시 파일은 portable package에서 제외하고 목록·경로 원문을 로그에 남기지 않는다.

## 범위와 영향 파일

기존 모듈러 모놀리스와 세 모듈을 유지한다. 새 모듈이나 범용 event bus는 추가하지 않는다.

| 소유 영역 | 예상 영향 파일 | 책임 |
|---|---|---|
| Terminal 계약 | `Contracts/`의 저장 항목·요청/결과, `Application/RendererProtocol.cs`, `RendererMessage.cs` | 저장 목록 snapshot, 대상 ID, 실패·중복 요청 계약 |
| Terminal 정책·저장 | `Application/TerminalSessionCoordinator.cs`, 새 preset service/store, `Domain/`, `Infrastructure/` | 목록 CRUD, 검증·영속화, 기존 세션 생성 경로 재사용 |
| Terminal 화면 | `Presentation/Renderer/src/index.ts`, `styles.css`, 필요한 `index.html`, 재생성 `dist/` | 저장·관리·선택 메뉴와 편집 화면 |
| Terminal 연결 | `Presentation/TerminalView.xaml.cs`, `TerminalModule.cs` | protocol 연결·취소·복구·종료 수명 |
| 검증·배포 | Terminal/Integration/Architecture tests, `scripts/package-portable.ps1` | 계약·상태·실제 셸 시작 폴더·package 개인정보 제외 |
| 문서 | 이 계획, README, architecture, test-plan | 사용법, 기존 복원과의 구분, 검증 증거 |

Terminal 경로는 `src/Modules/Starboard.Modules.Terminal/` 기준이다. public surface는 외부에
필요한 최소 계약으로 제한한다. Preferences schema 변경이나 DesktopIntegration/host 수정은
기본 범위에 포함하지 않으며 필요성이 생기면 먼저 계획·소유권을 갱신한다.

## 병렬 작업과 합류 순서

| 작업 | 선행 | 독점 파일·완료 조건 |
|---|---|---|
| P0 기준선·계약 | 없음 | 통합 담당: 최신 작업과 충돌 확인, 계약/protocol·fixture·저장 schema·요청 중복 정책 확정 |
| U1 TAB-UX-03 재현·수정 | 없음 | UI 담당: renderer src/dist와 UI 전용 tests. 좁은 인라인 편집을 별도 이름 편집창으로 변경, 탭 한 줄 배치·저장/취소·IME 검증 |
| P1 저장·실행 정책 | P0 | backend 담당: Terminal application/domain/infrastructure와 별도 preset tests. CRUD·원자 저장·실패 복구·기존 탭 유지 |
| P2 저장한 탭 UI | P0, U1 | UI 담당: renderer src/dist와 UI 전용 tests. 확정 fixture로 저장·관리·선택, 8개 제한·빈 목록·오류·키보드 검증 |
| P3 연결·수명 통합 | P1, P2 | 통합 담당: TerminalView/TerminalModule·IntegrationTests·package 검사. 실제 새 셸의 지정 폴더와 세션 독립성 확인 |
| P4 최종 검증·문서 | P3 | 검증 담당: 전체 suite·renderer 재빌드·package·수동 UI 증거 및 공용 문서 갱신 |

- P0와 U1은 파일이 겹치지 않으므로 병렬 가능하다. P1과 U1/P2도 소유권 분리 후 병렬 가능하다.
- U1과 P2는 같은 renderer 파일을 쓰므로 순차로 수행한다. `dist/` 생성과 같은 출력 경로도
  동시에 쓰지 않는다. 공용 test 파일과 문서는 통합 담당만 수정하고 worker는 전용 파일을 사용한다.
- 별도 checkout/빌드 출력에서 작업하고 기준 commit·변경 파일·계약 변경·검증 결과를 인계한다.
  계약 변경이 필요하면 통합 담당이 P0를 갱신하고 관련 worker를 새 기준으로 맞춘다.
- 진행 중 오케스트레이터 작업이 소유한 파일은 인수 전 편집하지 않는다. 이 문서는 작업 분해일
  뿐이며 계획 작성자가 자동으로 에이전트를 실행하거나 main에 구현을 추가하지 않는다.

## 검증과 위험

- 단위/계약: 목록 CRUD·독립 ID·중복 이름·20개 제한, 한글·공백·잘못된 경로, 파일 부재·손상·
  미래 schema·쓰기 실패, 8개 실행 제한·중복 요청·취소·늦은 응답을 검사한다.
- integration: 테스트 소유 폴더/세션으로 저장 후 실행 위치와 실제 셸 종류·새 PID를 확인하고,
  기존 탭 PID가 바뀌지 않는지 검사한다. 사용자 터미널에 검증 명령을 보내지 않는다.
- 앱 재실행 후 저장 목록 유지, 작업공간 복원 on/off와 독립성, 누락 폴더·셸 실패 후 재시도,
  renderer 복구 중 메뉴·편집 취소와 종료 시 저장 실패를 확인한다.
- UI: U1 제보 재현 전후 screenshot과 P2의 빈 목록/1개/20개·긴 이름/경로·좁은 폭·1/3/8개
  실행 탭을 검증한다. 4개 테마·100/125/150/200%·키보드/IME·터치 타깃과 닫기 동작을 포함한다.
- 실제 WebView2 검증과 browser simulation/단위 검증은 분리한다. 자동 test가 통과해도
  제보 화면과 저장→선택→새 셸 시작의 실제 UX가 미확인이라면 수동 항목은 `Not run`이다.
- `AGENTS.md`의 전체 restore/build/test, renderer rebuild와 source/dist 일치, portable
  저장 파일 제외 검증, `git diff --check`를 완료한다. Git·바탕화면 배포는 별도 요청을 따른다.

## 진행 기록

- [x] 2026-09-10: 사용자 제보와 저장한 탭 제안을 문서화하고 기존 복원과의 차이,
  현재 폴더 추적 한계, 파일 소유권과 병렬 가능한 순서를 정리했다.
- [x] P0/U1/P1/P2: 저장 계약·원자 저장 정책·새 세션 실행 정책과 renderer 저장/관리 UI를
  구현하고 해당 Terminal module 테스트를 추가했다.
- [x] P3/P4: `TerminalModule`/`TerminalView` 수명 연결, renderer generation·request ID 기반
  늦은 callback/중복 launch 억제, 종료 취소 순서와 portable 제외 검사를 구현했다. 저장 관련
  집중 test 89개, 통합 contract 3개, architecture 8개와 단일 노드 Debug/Release 전체 348개,
  renderer rebuild, 499-file portable ZIP·추출 smoke를 통과했다.
- [x] 자동/수동 결과를 분리했다. 실제 WebView2 저장 메뉴·scrollback/PID 관찰과 DPI·IME는
  실행 환경에서 수행하지 않아 `docs/test-plan.md`의 MAN-044~045 및 기존 matrix에 남겼다.
- [x] 2026-09-11 최종 재검증: 최초 portable 실행은 `staging/publish`·`staging/smoke`
  directory를 초기화한 뒤, publish·archive를 만들기 전 Release restore의 `NU1900`으로
  중단됐다. 모든 project가 `https://api.nuget.org/v3/index.json` vulnerability metadata를
  읽지 못한 동일 오류였으므로 원인은 publish 산출물·파일 잠금·archive 논리가 아니라 격리
  환경의 network 의존성으로 특정했다. network를 허용한 같은 Windows PowerShell 5.1 명령은
  스크립트 수정이나 검증 제거 없이 Release 348개 test, 499-file publish, ZIP 재현성·해시와
  추출 smoke를 통과했다. 완성된 `staging/build`, `publish`, `smoke`를 확인했고 package의
  saved-tabs/settings/workspace primary·backup·temporary, log, WebView2 user data와 개발 PC
  절대 경로 검사는 0건이었다.
- [x] 2026-09-11 portable checksum 호환성 복구: 41개 IntegrationTests와 499-file publish 뒤
  발생한 package 실패는 제품이나 publish가 아니라 실행 PowerShell의 `Get-FileHash` 부재로
  특정했다. cmdlet availability 확인과 disposable .NET SHA-256 fallback을 추가하고 cmdlet 부재
  simulation 및 file 재개방으로 동일 hash와 수명 해제를 검증했다. 지정 package argv는 최종
  Release 348개 test, 499-entry ZIP 재현성·추출 smoke를 통과했다. checksum과 독립 재계산 값은
  `13c491097c49d1b1724b845c18ccd5b1c9f3f83c9906a3c7fc0b14f5f01c1c8a`로 일치했고 per-user
  data·saved-tabs primary/backup/temporary·개발 경로 검사는 0건이었다. 실제 WebView2 저장 UI,
  DPI와 IME는 재실행하지 않았으며 MAN-044~045와 기존 manual matrix 상태를 유지한다.
- [ ] 다음 버전: 실제 구현에서 빠진 탭 context menu의 `저장한 탭에 추가…` 진입점을 연결하고,
  현재 탭 값 prefill·20개 한도·저장/취소 focus 복귀를 검증한다.
