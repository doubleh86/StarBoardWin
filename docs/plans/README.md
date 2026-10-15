# 구현 계획 문서 규칙

`docs/plans/`는 기능 구현 전에 범위와 검증 기준을 고정하고, 진행 중 판단과
완료 후 인수인계를 남기는 디렉터리다. 계획 작성은 사용자 승인을 기다리는
단계가 아니다. 작성 대상과 질문 기준은 [개발 작업 규칙](../development-workflow.md)을
따른다.

## 현재 작업 상태 — 2026-10-15

2026-10-15 `terminal-clipboard-shortcut-regression` 입력 진단 후속은 **구현 보완·자동 검증 미완료**다.
기준 `a9f6c9d`의 clipboard/키 복구를 보존하고 23자 고정 진단 코드, parser→실제 파일 privacy 회귀와
STA dispatcher await 중 무효화 검사를 보완했다. 기존 operation도 63자로 제한 안에 있었으므로
64자 redaction 발생을 확정하지 않는다. npm ci만 통과, test/build는 esbuild spawn EPERM이며
C# 집중 검증은 정체/MSBuild 하위 작업 실패, solution restore exit 1로 build/전체 test 미실행이다.
과거 610개 통과·별도 layout Task 이력은 당시 기록이며 이번 성공으로 간주하지 않는다.
[이번 상세 기록](../test-plan.md#입력-진단-dispatcher-재검증-2026-10-15)을 따른다.
CLIP-MAN-01~05와 실제 자발 종료 원인 미확정 상태를 유지한다.

`terminal-layout-recovery-entrypoint` 최신 후속: 2026-10-15 09:04:25 UTC 시작 검증은 restore와
SharedKernel·Terminal build 성공 후 테스트 프로젝트 `RendererLayoutRequestDispatcherTests.cs:34`
CA1707로 중단돼 테스트가 실행되지 않았다. 밑줄 한 개를 제거하는 이름 수정과 재검증을
[진행 기록](2026-09-29-unlock-panel-width-recovery.md#ca1707-이름-수정검증-재개-2026-10-15)에 추가한다.
아래 완료 Task와 과거 접근 거부·MSBuild 조사 이력은 보존하며 현재 검증 결과와 구분한다.

이름 수정 후 현재 상태는 **자동 검증 미완료**다. Terminal/Architecture 집중 검증은
restore 단계 출력 뒤 실행 결과가 없고, 18:12 KST solution restore는 exit 1이다.
18:13 diagnostic은 `_FilterRestoreGraphProjectInputItems` 하위 작업 실패(오류 0개)를
기록했다. 선행 조건 미충족으로 Debug build/전체 test는 미실행이며 정렬·diff 검사는 통과했다.
새 컴파일·테스트 성공이 확인되기 전에는 이름 수정만으로 해결 완료를 선언하지 않는다.

초기에는 `main`의 `1505ccb` 코드를 기준으로 기존 계획과 구현을 대조했고, 이후 Task 결과를
아래 표에 반영했다. 아래 표는 현재 상태의
진입점이며, 과거 제보·초기 UX·검증 횟수는 각 문서의 당시 기록으로 보존한다.

| 구분 | 현재 상태와 정본 |
|---|---|
| 긴급 입력·종료 회귀 (2026-10-14) | [Ctrl+C/Ctrl+V 불능·자발 종료](2026-09-09-terminal-safety-and-discoverability.md#2026-10-14-복사붙여넣기-불능과-앱-자동-종료): `terminal-clipboard-shortcut-regression` P0 재개 수정·자동 검증 완료. 기존 local 편집/STA 재시도/원문 없는 진단을 보존하고 OLE 실패 뒤 수명 검사, 편집 종료 키 억제·IME 보호와 source/dist 동기화를 보강했다. Node 103·.NET 610 통과. 기본 build 접근 거부 후 실행별 worker pool에서 동일 argv의 restore/build/test가 통과했으며 최초 거부 원인은 미확정이다. 실제 사용자 증상·자발 종료의 공통 원인은 미확정이며 CLIP-MAN-01~05 장기 실행·IME·RDP는 Not run (manual)이다. |
| 재부팅 후 탭 복원 회귀 (2026-10-14) | [작업공간 복원 계획](2026-09-09-workspace-convenience.md#후속--windows-재부팅-후-탭-구성-복원-실패-2026-10-14): 설정은 켜져 있고 저장된 3탭 구성도 남아 있지만 사용자는 재부팅 뒤 탭 이름·개수가 초기화됐다고 확인했다. 원인 미확정·제품 수정 미착수. Windows 로그인 자동 실행과 화면 복원은 별도 인수 대상이다. |
| 탭 드래그 정렬 | [계획과 인수 기록](2026-10-13-tab-drag-reorder.md): contract/runtime/workspace-sync/renderer 구현 및 integration 자동 검증 완료. 실제 WebView2·IME·focus·DPI 화면 인수는 수동 검증 대기다. |
| 탭 드래그 시각 피드백 | [계획과 완료 기록](2026-10-13-tab-drag-reorder.md#후속--탭-드래그-시각-피드백-보강-2026-10-13): `tab-drag-visual-feedback` 구현·자동 검증 완료 (2026-10-14). 이동 탭 강조, 3px 삽입선·끝 표시와 이름 미리보기 및 source/dist 동기화를 완료했다. 실제 WebView2·네 theme·DPI 화면 인수는 수동 검증 대기다. |
| RDP 호스트 복구 연결 (2026-10-15) | [실제 호스트 인수](2026-09-29-unlock-panel-width-recovery.md#실제-호스트-연결-완료-인수-2026-10-15): `panel-rdp-width-recovery` 구현·자동 검증 완료. MainWindow 이벤트/dispatcher 병합, 최신 client/window DPI↔WPF root 보정과 AppCoordinator→Terminal 공개 점검을 같은 revision으로 연결했다. root 5회/5초, 중복·역순·주기 요청의 예산 재시작 금지, 숨김·종료 정리를 검증했다. DesktopWindow 18, DesktopIntegration 120, Terminal 379, 전체 .NET 630 통과. 기본 SDK 병렬 exit 1 뒤 단일 worker, esbuild EPERM 뒤 승인된 제한 밖 suite로 통과했다. 기존 WTS/Terminal 내부·실행 이력은 보존하며 실제 RDP-MAN-01~07은 Not run (manual)이다. |
| Terminal 공개 레이아웃 점검 (2026-10-15) | [공개 진입점 계획·인수](2026-09-29-unlock-panel-width-recovery.md#terminal-공개-점검-진입점-2026-10-15): `terminal-layout-recovery-entrypoint` 구현·자동 검증 완료. revision-only 계약과 module 진입점, UI dispatcher/관측 병합, revision별 5회/5초 및 숨김·renderer·종료 수명을 연결했다. Terminal 376·Architecture 14·전체 .NET 620개 통과. 기본 병렬 build 접근 거부 뒤 단일 작업자 build가 통과했다. 실제 host 이벤트 연결과 RDP 화면 검증은 위 별도 범위로 남는다. |
| 탭 UI | 이름 변경 창, 좌우 이동 메뉴와 `+` 위치는 구현·자동 검증 완료. 현재 `+`는 profile 선택, 오른쪽 끝 `▾`는 저장한 탭 메뉴다. [탭 UI 기록](2026-09-03-renderer-and-tab-ui.md), [현재 메뉴 정본](2026-09-16-tab-strip-menu-correction.md)을 따른다. |
| 기존 편의 기능 | 작업공간 복원, 저장한 탭·현재 디렉터리 prefill, 출력 검색, 경로 드롭, URL 열기, 완료 알림, 셸/WSL 선택, 높이 조절과 탭 구성 복제는 구현·자동 검증 완료. [경량 기능 목록](2026-09-10-lightweight-terminal-roadmap.md), [편의 기능 구현 기록](2026-09-14-next-terminal-convenience.md)을 따른다. |
| 남은 실제 검증 | WebView2·IME·focus·DPI·잠금 해제 등은 [테스트 계획](../test-plan.md)의 `Not run`/`Blocked`를 유지한다. 수동 미수행을 미구현 Task나 통과로 해석하지 않는다. |

오케스트레이터는 기존 완료 기능을 새 구현으로 다시 생성하지 않는다. 탭 드래그 정렬은
별도 Task로 구현·통합 검증했으며 기존 완료 Task와 과거 실행 기록은 보존한다.
이번 integration은 테스트·문서만 변경했고 앱 버전·배포물은 갱신하지 않았다. 이후 작업 시
이 표와 해당 계획의 진행 기록을 함께 갱신한다.

2026-10-15 `terminal-layout-recovery-entrypoint` 재시도는 **검증 미완료**다. 위 완료 기록은
`7683092`의 과거 이력으로 보존한다. 공개 구현을 유지하고 횟수 제한·관측 취소 후 요청 테스트를
보완했으나 이번 집중 테스트/solution restore는 SDK 하위 작업 실패 또는 대기로 완료하지 못했다.
기본 build 접근 거부의 원인 해소나 동시 MSBuild 충돌을 확인하지 못해 단일 worker 재실행으로
대체하지 않았다. 이번 restore→build→전체 test 통과 전에는 현재 재시도를 완료로 처리하지 않는다.
검사 근거와 재개 조건은 [재시도 점검](2026-09-29-unlock-panel-width-recovery.md#재시도-점검-2026-10-15)을 따른다.

같은 날 14:35 KST의 추가 재개도 **자동 검증 미완료**다. `af9aee5`까지의 구현과 실행 이력을
보존하고 dispatcher·진행 중 관측·revision 예산의 조합 회귀만 보완했다. Terminal/Architecture
테스트는 restore 이후 또는 단계에서 진행하지 않아 자체 실행을 취소했고, 14:39 solution restore는 exit 1이다.
현재 계정의 생성물 쓰기·삭제·cache 배타적 열기는 성공했으나 SDK 하위 작업 실패 원인은 미확정이다.
선행 조건이 없어 build/전체 test는 실행하지 않았다. 정상적인 SDK 하위 작업 실행이 가능한 환경에서
재검증해야 하며 기존 완료 Task를 취소하거나 과거 통과를 이번 통과로 간주하지 않는다.
[이번 시각별 결과와 재개 조건](2026-09-29-unlock-panel-width-recovery.md#추가-재개-점검-2026-10-15-1435-kst-시작)을 따른다.

같은 날 17:58 KST 재조사도 **자동 검증 미완료**다. `e2def41`까지의 구현·회귀를 보존했다.
이번 Terminal 상세 로그는 restore 이후 `BuildProject` / `_GetProjectReferenceTargetFrameworkProperties`의
MSBuild 하위 작업 실패 상태를 보여 주며 compiler 오류·테스트 호스트 시작·assertion은 관측되지 않았다.
solution restore는 18:01 exit 1이며 원인은 미확정이다. 생성물 쓰기·cache 열기는 성공했으나 동시 빌드
충돌은 입증하지 못했다. 이번 실패의 단계·시각과 미실행 build/전체 test를
[별도 기록](2026-09-29-unlock-panel-width-recovery.md#추가-실패-조사-2026-10-15-1758-kst-시작)에 남겼다.

## 파일 이름

`YYYY-MM-DD-topic.md` 형식을 사용한다.

예:

```text
2026-09-01-windows-starboard-v01.md
```

같은 기능의 계획이 이미 있으면 기존 파일을 갱신한다. 새 대화나 후속 수정마다
파일을 만들거나 완료된 조사를 다시 수행하지 않는다.

## 기본 양식

작업 규모에 맞춰 아래 항목을 짧게 작성한다. 범위에는 영향 파일을, 판단에는
중요한 가정과 위험을, 검증에는 완료 조건을 확인할 방법을 포함한다.

```markdown
# 제목

## 목표와 완료 조건

## 범위와 영향 파일

## 판단과 위험

## 구현 단계

- [ ] 단계 1
- [ ] 단계 2

## 검증 방법

## 진행 기록

## 완료 요약
```

현재 상태, 참고 근거, 제외 범위, 아키텍처 결정, fallback, 미결정 사항은 판단에
필요할 때 추가한다. 해당하지 않는 제목을 채우려고 조사나 설계를 늘리지 않는다.
기존 계획을 이 양식에 맞추기 위한 일괄 수정은 하지 않는다.

## 운영 규칙

- 범위가 바뀌면 코드보다 계획을 먼저 갱신한다.
- 완료한 단계와 중요한 실패 원인·결정을 기록한다. 일상적인 도구 호출마다
  기록을 추가하지 않는다.
- 체크박스는 구현하거나 문서화해서 닫을 수 있는 작업에만 사용한다. 제품 코드와 자동
  검증이 완료됐고 실제 화면·장비 검증만 남았다면 구현 항목은 `[x]`로 닫고, 남은 검증은
  `Blocked (manual)` 또는 `Not run (manual)`로 [테스트 계획](../test-plan.md)에 연결한다.
  환경 제약으로 수행할 수 없는 수동 검증을 미구현 Task처럼 `[ ]`로 남기지 않는다.
- 후속 구현이 완료되면 과거의 `[ ]`와 "다음 버전" 표현을 같은 문서에서 정정한다. 역사적
  제보나 당시 판단은 보존하되 현재 정본과 완료 근거를 바로 옆에 표시해 오케스트레이터가
  이미 구현된 항목을 다시 계획하지 않게 한다.
- 중간 요구 변경이나 대화 재개 시 원래 목표, 반영된 제약, 완료한 검증과 남은
  작업을 기준으로 이어 간다. 확인되지 않은 항목을 완료로 표시하지 않는다.
- 완료 시 변경 전/후 동작, 구조 선택 이유, 경계 조건, 실제 검증 결과, 후속
  수정 위치를 완료 요약에 기록한다.
- 코드 및 문서 작업이 끝났고 추가 구현 판단이 남지 않으면 파일을
  `docs/plans/completed/`로 옮길 수 있다.
- 폐기된 초안은 삭제하지 말고 `docs/plans/archive/`로 옮기고 대체 계획을
  링크한다.
- secret, local path credential, 개인 설정은 계획에 기록하지 않는다.
