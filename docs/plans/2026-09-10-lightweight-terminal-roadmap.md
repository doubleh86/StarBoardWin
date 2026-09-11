# 가벼운 터미널 제품 방향과 후속 기능 후보

## 제품 방향 — 2026-09-10 사용자 결정

Starboard는 작업표시줄에 붙어 빠르게 명령을 입력하고 결과를 확인하는 작고 간단한
터미널이다. 큰 작업공간이나 IDE를 대체하는 방향으로 확장하지 않는다.
실제 배치는 기존 제품 원칙대로 작업표시줄 바로 위이며, 작업표시줄을 덮거나 작업 영역을
예약하지 않는다. 사용자의 ‘아래 붙어서’라는 표현은 화면 하단 밀착이라는 의도로 반영한다.

- **분할 화면은 추가하지 않는다.** 좌우·상하 split pane, 분할 비율 조절과 pane별 focus
  관리 모두 제품 범위에서 제외한다. 좁은 기본 패널에서 읽기·입력이 복잡해지는 것을 피한다.
- 여러 세션은 기존 탭으로 전환한다. 현재 확장/축소 기능은 유지하지만 화면 분할은 하지 않는다.
- 우선순위는 UI 안정화 → 저장한 탭 → 출력 검색이다. 매일 반복하는 동작을 줄이는 기능을 우선한다.
- 기능을 추가해도 기본 화면에 상시 도구막대를 늘리지 않고 작은 메뉴나 필요할 때 여는 화면을 쓴다.
- 명령 자동 실행, 내장 AI와 대규모 작업공간 관리는 현재 후속 범위에서 보류한다.
  별도 사용자 결정 없이는 분할 화면을 구현 후보로 다시 포함하지 않는다.

## 현재 계획과 후속 후보

이 문서는 제품 방향과 후보 우선순위다. 기능 구현·의존성 추가·오케스트레이터 실행 승인이
아니며 실제 착수 전 각 기능의 계약, 영향 파일과 인수 기준을 별도 계획으로 구체화한다.

| 우선순위 | 기능 | 사용자 경험과 범위 |
|---|---|---|
| 먼저 | 탭 UI 안정화 | 좁은 이름 편집·닫기 영역 회귀를 해결하고 실제 WebView2에서 검증 |
| 다음 | 저장한 탭 | 이름·폴더·셸을 저장하고 골라 해당 폴더에 새 세션 시작 |
| 후속 1 | 출력 검색 | 현재 탭의 메모리 내 출력에서 검색, 이전/다음 결과 이동·강조, Escape 닫기 |
| 후속 2 | 파일·폴더 경로 드롭 | 탐색기에서 놓은 항목의 경로를 입력만 하고 자동 실행하지 않음 |
| 후속 3 | 출력 URL 열기 | Ctrl+클릭으로 허용된 웹 주소 열기, 일반 클릭·드래그는 텍스트 선택 유지 |
| 후순위 | 긴 작업 완료 알림 | 실제 명령 종료를 확인한 뒤 선택적으로 알림, 단순 출력 정적 상태로 추측하지 않음 |

- UI 정본: [TAB-UX-03 이름 편집 기획](2026-09-03-renderer-and-tab-ui.md).
- 저장한 탭 정본: [프로젝트 폴더에서 바로 시작](2026-09-10-saved-terminal-tabs.md).
- 출력 검색은 기존 scrollback 범위만 대상으로 한다. 로그 파일 저장, 전체 탭 통합 검색,
  renderer 재시작 이전 기록 복원은 초기 범위에서 제외한다. 검색 입력이 shell로 전달되면 안 된다.
- 경로 드롭은 셸별 quoting과 공백·한글·특수 문자 처리를 먼저 조사한다. shell prompt인지
  interactive application인지 불명확하면 입력 미리보기·확인 등 안전 정책을 확정한 뒤 구현한다.
  파일 내용을 읽거나 업로드하지 않으며 Enter·개행·명령 실행을 자동으로 붙이지 않는다.
- URL 열기는 사용자 명시적 동작에 한정한다. 초기 허용 scheme은 HTTP/HTTPS이며 command,
  file, custom protocol은 제외한다. 링크 자동 요청·미리보기·백그라운드 네트워크는 하지 않는다.
  링크가 보인다는 이유만으로 신뢰하지 않고 실제 대상 URL을 확인할 수 있어야 한다.
- 완료 알림은 기존 ‘새 출력’ 점과 별개다. 명령 시작·종료·exit code를 식별하는 셸 연동을
  조사하고 지원 셸·오탐 정책을 먼저 확정한다. 기본 꺼짐, focus 강탈 없음, 명령/출력 노출 없음이 기준이다.

## ICON-01 — Starboard 아이콘을 앱과 트레이에 적용

### 목표와 현재 상태

- 사용자 요청(2026-09-10): 제작한 아이콘을 앱뿐 아니라 notification area의 트레이에도 사용한다.
  원본은 [starboard-icon-v1.png](../assets/starboard-icon-v1.png), 제작 기록과 프롬프트는
  [아이콘 시안 기록](../assets/starboard-icon-v1.md)을 따른다. 현재 원본은 1254×1254 투명 PNG다.
- `TrayIconService`는 `Starboard.Modules.DesktopIntegration.Assets.Starboard.ico` embedded
  resource를 독립적으로 로드하고, 실패 시에만 `SystemIcons.Application`으로 fallback한다.
  새 PNG를 저장한 것만으로 트레이에 반영되지 않으므로 명시적인 asset 포함·로드·수명 관리가 필요했다.
- 상태는 **I0·I1·I2·I3 구현 및 자동 검증 완료, 실제 Windows 표시 수동 검증 대기**다.

### 적용 범위와 정책

- terminal `>_`, 별, 하단 panel 선이라는 동일한 시각 정체성을 앱과 트레이에 사용한다.
  단순 PNG 확장자 변경이 아니라 실제 multi-resolution ICO를 만들고 alpha를 보존한다.
- 초기 제작 대상 크기는 16/20/24/32/48/64/256px다. 작은 크기의 선·별·내부 여백이
  뭉개지는지 실제 픽셀 크기로 확인한다. 필요하면 같은 모티프의 소형 전용 이미지를 제작하며
  큰 원본을 기계적으로 축소한 결과만으로 완료 처리하지 않는다.
- EXE/Explorer와 WPF 창 아이콘, notification area 및 숨겨진 아이콘 영역에 적용한다.
  아이콘 표시를 위해 기존 panel의 ShowInTaskbar, focus, z-order 정책을 변경하지 않는다.
- 트레이의 왼쪽 클릭 호출, 표시/숨기기·단축키 안내·설정·종료 메뉴와 tooltip을 유지한다.
  새 출력·작업 상태에 따른 애니메이션·badge·아이콘 교체는 이번 범위에 포함하지 않는다.
- asset은 배포물에 로컬로 포함한다. 사용자 기기의 PNG 경로나 네트워크에 의존하지 않는다.
  트레이 asset은 DesktopIntegration이 소유하며 다른 module의 내부 asset을 직접 참조하지 않는다.
  앱용·트레이용 파일은 같은 확정 원본에서 만들고 실제 파일 배치·resource 이름을 구현 시 기록한다.
- 소유한 Icon과 stream은 적절히 해제한다. Explorer 재시작 후에도 동일 아이콘으로 재생성하며
  로딩 실패 시 기존 기본 아이콘으로 fallback하고 민감 정보 없는 진단을 남긴다.

### 영향 파일·병렬 작업

| 작업 | 선행 | 소유 범위와 완료 조건 |
|---|---|---|
| I0 아이콘 asset 확정 | 없음 | 디자인 담당: 원본·소형 보정·ICO 및 크기별 preview, asset 경로·명세 동결 |
| I1 앱 적용 | I0 | host 담당: `Starboard.Windows.csproj`, 필요한 WPF 창 icon resource/참조, 관련 tests |
| I2 트레이 적용 | I0 | Desktop 담당: module asset/project resource, `Infrastructure/TrayIconService.cs`, 전용 tests; 로드·해제·Explorer 복구 |
| I3 통합 검증·문서 | I1, I2 | 통합 담당: package 검사·전체 검증·사용자 문서와 실제 Windows 표시 증거 |

I1과 I2는 파일 소유권을 나눠 병렬 가능하다. I0 원본과 공용 문서는 통합 담당만 갱신한다.
I0의 canonical ICO는 `docs/assets/starboard-icon.ico`이며 16/20/24/32/48/64/256px RGBA PNG
entry를 가진다. I1은 `src/Starboard.Windows/Assets/Starboard.ico`를 executable/WPF resource로,
I2는 `Starboard.Modules.DesktopIntegration.Assets.Starboard.ico` embedded resource로 사용한다.
I3는 portable archive의 `Assets/Starboard.ico` 사본을 필수 파일로 검사하며 user settings,
workspace/saved-tabs, logs와 WebView2 data는 거부한다.

### 인수 기준

- ICO에 의도한 크기·alpha가 포함되는지, package에 asset이 누락되지 않았는지 자동 검증한다.
- 실제 Windows 밝은/어두운 작업표시줄과 숨겨진 아이콘 영역, 100/125/150/200% 배율에서
  식별성과 투명 가장자리를 확인한다. 축소 preview와 실제 트레이 검증은 구분한다.
- EXE/창/트레이의 디자인 일치, 클릭·메뉴·종료, Explorer 재시작 복구와 아이콘 로드 실패
  fallback을 검사한다. 아이콘 캐시 때문에 이전 그림이 보이는 경우와 asset 누락을 구분한다.
- build/resource 입력 변경이므로 solution restore/build/test와 portable 검증을 수행한다.
  실제 장비에서 수행하지 않은 항목은 `Not run`으로 남긴다. commit/push·배포는 별도 요청을 따른다.

## 구현 계획으로 구체화할 때의 경계

- 기존 단일 배포 단위·Terminal/DesktopIntegration/Preferences 모듈 경계를 유지한다.
- renderer 기능은 `Terminal/Presentation/Renderer/src/`와 재생성 `dist/`, 관련 protocol과
  Terminal tests가 주요 영향 범위다. 링크의 외부 실행·clipboard/drop 전달은 host 경계를 먼저 확인한다.
- 새 dependency는 도입 전 유지보수·license·offline asset 포함 여부를 조사한다. 이 문서는
  특정 dependency를 확정하지 않으며 새 terminal parser를 구현하지 않는다.
- 출력 검색·경로 드롭·URL 열기는 renderer 파일이 겹치므로 같은 checkout에서 병렬 편집하지 않는다.
  공통 계약과 파일 소유권을 확정하면 정책/검증 로직과 UI 작업을 독립 worktree에서 병렬화할 수 있다.
- 신규 후보는 기존 UI 안정화·저장한 탭 구현 중인 worker에게 임의로 끼워 넣지 않는다.

## 검증 원칙

- 작은 200 DIP 패널에서 입력·취소·검색·메뉴가 접근 가능해야 하며 기능 때문에 자동 확장하지 않는다.
- keyboard focus, 한글 IME, 4개 테마, 긴 텍스트와 좁은 폭을 검증한다. 실제 WebView2 검증과
  browser simulation을 구분하고 screenshot/입력 경로 확인 없이 UI 완료로 처리하지 않는다.
- shell PID·입력 상태·scrollback 유지, 다른 앱 focus 비방해, taskbar 비침범을 공통 회귀 기준으로 한다.
- 원래 명령과 사용자 데이터가 로그·저장 파일·배포물에 새로 포함되지 않는지 검증한다.
- 문서 단계에서는 내용·링크와 `git diff --check`만 확인한다. 제품 코드나 빌드 입력은 바꾸지 않는다.

## 작업 및 Git 범위

- 기획은 `codex/planning` 전용 worktree에서 진행한다. 초기 요청은 해당 브랜치 commit/push만
  허용했으며, 2026-09-10 후속 사용자 요청으로 현재 기획과 아이콘 원본의 `main` merge도 승인됐다.
- 이번 후속 반영은 로컬 `main` merge까지다. 원격 push, 오케스트레이터 상태·실행 브랜치 변경,
  제품 구현·배포는 별도 요청 없이 하지 않는다.
- 기획 브랜치의 원격 push만으로 main이나 이미 시작된 실행의 계획 snapshot이 바뀐다고 가정하지 않는다.
  구현자가 사용할 문서 버전의 인수와 main 반영은 별도 요청·절차로 수행한다.

## 진행 기록

- [x] 2026-09-10: 분할 화면 제외와 빠르고 간단한 작업표시줄 터미널 방향을 사용자 결정으로 기록.
- [x] 출력 검색·경로 드롭·URL 열기·완료 알림을 후속 후보로 정리하고 기존 계획과 연결.
- [x] 2026-09-10: 제작 아이콘의 앱·트레이 적용을 ICON-01로 기획. 소형 크기 검증,
  resource 수명·fallback, 병렬 작업과 실제 Windows 인수 기준을 추가했다.
- [x] 2026-09-11: I0 canonical ICO와 두 제품 resource의 hash·PNG entry를
  `Test-StarboardIcon.ps1`로 검사하고, I1 executable/WPF resource와 I2 embedded tray resource를
  적용했다. portable package에는 `Assets/Starboard.ico`를 포함하고 사용자별 data를 거부한다.
- [ ] 2026-09-11: 밝고 어두운 작업표시줄 및 숨겨진 아이콘 영역에서 100/125/150/200% 배율,
  Explorer 재시작 뒤의 실제 tray 표시를 수동으로 확인한다.
- [ ] 각 후속 후보의 상세 기획·구현·검증. 현재는 시작하지 않는다.
