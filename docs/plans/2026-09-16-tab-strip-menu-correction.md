# 탭 추가 메뉴와 오른쪽 메뉴 위치 회귀 수정

## 목표와 완료 조건

`+`는 마지막 탭 바로 옆에 유지하되 클릭하면 PowerShell 7, Windows PowerShell,
Command Prompt와 발견된 WSL 배포판 중 새 탭 실행 profile을 선택하게 한다. `▾`는
마지막 탭 옆으로 따라오지 않고 기존 위치인 탭 바 오른쪽 끝에 고정해 저장한 탭 메뉴를
연다.

실제 배포 화면에서 terminal viewport와 세로 scrollbar가 패널 왼쪽 일부 폭으로 줄어든
회귀도 같은 작업에서 수정한다. 탭 바 control 배치와 관계없이 terminal 본문은 항상 패널의
사용 가능 폭 전체를 채워야 완료로 본다.

이 문서는 완료된 [새 탭 추가 버튼 위치 회귀 수정](2026-09-15-tab-add-button-position.md)의
사용자 확인 후속 정본이다. 오케스트레이터는 아래 `tab-strip-menu-correction` Task를
독립적인 미완료 작업으로 실행한다.

## 현재 상태와 확인된 문제

- 기준 커밋: `9e7cee2` (`main`, 2026-09-16 확인).
- 현재 DOM 순서는 `tablist → + → ▾`이고 `.tab-list`가 `flex: 0 1 auto`라 `+`뿐 아니라
  `▾`도 마지막 탭 바로 뒤에 배치된다. 이전 계획이 두 control을 함께 이동하도록 잘못
  지정한 결과이며 이번 계획에서 계약을 정정한다.
- 현재 `+`는 활성 탭과 같은 builtin shell 또는 첫 builtin profile을 즉시 실행한다.
  profile/WSL 목록은 `▾`가 여는 저장 탭 메뉴 앞부분에 섞여 있다.
- 사용자 화면에서 xterm viewport의 오른쪽 경계와 scrollbar가 패널 오른쪽 끝보다 훨씬
  왼쪽에 나타났다. 탭 strip 변경과 같은 배포에서 관찰됐지만 CSS flex 자체를 원인으로
  단정하지 않는다. WebView host, `.terminal-workspace`, `.session-pane`, `.terminal-mount`,
  `.xterm`의 실제 너비와 fit/resize 호출 순서를 확인해 원인을 좁힌다.

## UX와 동작 계약

- 여유 폭의 시각 순서는 `마지막 탭 → + → 유동 빈 공간 → ▾`다. 마지막 탭과 `+` 사이
  간격은 0~8px이고 `▾`의 오른쪽 가장자리는 탭 바 오른쪽 가장자리에 붙는다.
- 탭이 가용 폭을 넘으면 탭 목록만 줄어 가로로 스크롤한다. `+`와 `▾`는 스크롤 영역
  밖에서 항상 보이고 서로 겹치거나 잘리지 않는다. 유동 빈 공간은 먼저 0까지 줄어든다.
- `+`를 mouse로 클릭하거나 keyboard의 Enter/Space로 활성화하면 `+`에 고정된 profile
  선택 메뉴를 연다. 목록에는 사용 가능한 builtin shell과 발견된 WSL 배포판을 표시하고,
  하나를 선택한 뒤에만 새 탭을 만든다.
- WSL 조회 실패 메시지와 재시도 동작은 `+` profile 메뉴로 이동한다. WSL이 없거나 조회에
  실패해도 builtin shell 선택은 계속 가능해야 한다.
- `▾`는 오른쪽 끝에서 저장한 탭 목록과 관리 진입점만 제공한다. profile 목록을 중복 표시하지
  않는다. 두 메뉴는 동시에 열리지 않으며 바깥 클릭과 Escape로 닫힌 뒤 각 trigger로 focus가
  돌아간다.
- 기존 `Ctrl+Shift+T`는 빠른 실행 경로로 유지해 현재 기본 profile 탭을 즉시 하나 만든다.
  profile 선택이 필요한 경우 `+` 메뉴를 사용한다.
- 최대 8개 탭, profile별 session 생성, 저장 탭 schema, 탭 복제, 닫기 확인과 session focus
  정책은 변경하지 않는다.
- terminal 본문과 xterm viewport는 탭 수, 탭 이름, 메뉴 open 여부와 무관하게 사용 가능한
  panel client width를 채운다. xterm 내부 scrollbar만 오른쪽 끝에 나타나며 별도 빈 세로 영역을
  남기지 않는다.
- 32 DIP 탭 바, 최소 32×31px hit target, tooltip, 접근성 이름, focus 표시와 네 theme의
  동일한 구조를 유지한다.

## 오케스트레이터 작업 정의

### Task: `tab-strip-menu-correction`

- 제목: 새 탭 profile 메뉴 분리와 오른쪽 메뉴·terminal 폭 회귀 수정
- capabilities: `frontend`, `qa`
- dependencies: 없음
- 위험도: 보통
- 설명: renderer의 탭 바 flex 계약을 `tablist + add + flexible space + saved menu`로 정정하고,
  기존 결합 메뉴에서 launch profile 구역을 `+` 전용 메뉴로 분리한다. terminal host와 xterm
  실제 너비 및 fit 호출을 조사해 좁아진 viewport 회귀를 함께 수정한다. source와 committed
  `dist`, renderer 계약 테스트 및 test plan을 갱신한다.
- 예상 변경 경로:
  - `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/src/index.html`
  - `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/src/styles.css`
  - `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/src/index.ts`
  - `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/dist/**`
  - 필요할 때만 `src/Modules/Starboard.Modules.Terminal/Presentation/TerminalView.xaml.cs`
  - `tests/Starboard.Modules.Terminal.Tests/RendererDistributionTests.cs`
  - 필요할 때만 `tests/Starboard.Modules.Terminal.Tests/TerminalView*Tests.cs`
  - `docs/plans/2026-09-16-tab-strip-menu-correction.md`
  - `docs/test-plan.md`
- 금지 경로:
  - `src/Modules/Starboard.Modules.Terminal/Contracts/**`
  - `src/Modules/Starboard.Modules.Terminal/Application/**`
  - `src/Modules/Starboard.Modules.DesktopIntegration/**`
  - `src/Modules/Starboard.Modules.Preferences/**`
  - `src/Starboard.Windows/**`

### 인수 기준

1. 1개와 3개 탭에서 마지막 탭과 `+` 사이 간격은 0~8px이고 `▾`는 탭 바 오른쪽 끝에 있다.
2. 좁은 폭과 8개 탭에서는 탭 목록만 가로 스크롤하며 `+`와 `▾`가 계속 보인다.
3. `+` 메뉴에서 PowerShell 7, Windows PowerShell, Command Prompt와 발견된 WSL profile을
   선택할 수 있고 선택 전에는 새 탭이 생성되지 않는다.
4. `▾` 메뉴에는 저장 탭 목록과 관리 기능만 남으며 profile 항목이 중복되지 않는다.
5. `Ctrl+Shift+T`는 기본 profile을 즉시 실행하고 `+`의 Enter/Space, 메뉴 keyboard 이동,
   Escape와 focus 복귀가 동작한다.
6. 탭 수·이름, 두 메뉴의 open/close와 창 resize 전후에 terminal mount 및 xterm viewport가
   panel client width를 채우고 scrollbar가 오른쪽 끝에 있다.
7. 네 theme에서 32 DIP 높이, hit target, hover/focus/disabled 상태와 menu clipping이 정상이다.
8. renderer source로 빌드한 `dist`가 commit된 asset과 일치하고 전체 자동 검증이 통과한다.

## 구현 판단과 위험

- 우선 CSS flex로 `tab-list: 0 1 auto`, `+`: 고정 폭, `▾`: `margin-left: auto`와 고정 폭을
  구성한다. 탭 개수에 따른 JavaScript 좌표 계산은 도입하지 않는다.
- profile menu와 saved-tabs menu는 표시 내용과 focus return target이 다르므로 상태와 dismiss
  함수를 분리한다. 공통 DOM 생성 helper는 사용할 수 있지만 하나의 전역 menu 상태를 두 trigger가
  암묵적으로 공유하지 않는다.
- 기존 host protocol과 `TerminalLaunchProfile` 계약으로 요구사항을 충족하므로 protocol version,
  C# application contract와 settings schema는 변경하지 않는다.
- 폭 회귀는 정적 stylesheet 문자열만으로 해결됐다고 판단하지 않는다. 실제 element rect와
  xterm fit/resize 결과를 확인하며, TerminalView 변경은 WebView client size 전달이나 fit 순서가
  원인으로 확인된 경우에만 허용한다.
- `+`에 popup 의미가 생기므로 `aria-haspopup`, `aria-expanded`, menu label과 focus return을
  각각 `+` 기준으로 갱신한다. 기존 saved-tabs dialog의 focus 복귀는 `▾`를 유지한다.

## 검증 방법

```powershell
npm --prefix src/Modules/Starboard.Modules.Terminal/Presentation/Renderer run build
dotnet restore Starboard.Windows.sln
dotnet build Starboard.Windows.sln --configuration Debug --no-restore
dotnet test Starboard.Windows.sln --configuration Debug --no-build --no-restore
pwsh -NoProfile -File scripts/Test-CSharpAlignment.ps1 -WorkingTree
git diff --check
```

- renderer test에서 control 배치 계약, 분리된 menu 내용, ARIA/focus, shortcut과 source/dist 일치를
  확인한다.
- 실제 WebView2에서 1/3/8개 탭, 짧고 긴 이름, 좁고 넓은 panel, 두 menu와 네 theme를 확인한다.
- terminal 본문 폭은 화면 캡처만 보지 말고 panel/workspace/mount/xterm viewport의 client rect와
  resize 후 오른쪽 scrollbar 위치를 함께 확인한다.
- 실제 WSL 설치 환경과 100/125/150/200% DPI를 실행하지 못하면 자동 결과로 대체하지 않고
  `Not run`으로 기록한다.
- 바탕화면 배포와 앱 재실행은 별도 요청이며 이 Task의 필수 범위가 아니다.

## 진행 기록

- [x] 2026-09-16: 배포 화면에서 `▾` 위치와 terminal 본문 폭 회귀를 확인했다.
- [x] 2026-09-16: `+`는 profile 선택, `▾`는 오른쪽 끝의 저장 탭 메뉴로 역할을 분리하는 계약을 확정했다.
- [x] 2026-09-16: `tab-strip-menu-correction` 구현과 자동 검증을 완료했다. `tablist → + → flexible spacer → ▾`를 고정했고 profile/WSL UI를 `+` menu로 분리했다. 숨겨진 pane의 활성화 뒤 fit 흐름은 유지하면서 terminal mount의 xterm에 명시적인 100% width/min-width 계약을 추가했으며, source로 rebuilt `dist`와 renderer 계약 test를 갱신했다.
- [ ] 실제 WebView2, WSL 설치 환경과 DPI별 수동 검증.

## 완료 요약

2026-09-16 자동 검증: renderer `npm run build`, Debug restore/build, 전체 `dotnet test` (524 passed), C# alignment와 `git diff --check`를 통과했다. 실제 WebView2·WSL 설치 환경·DPI별 rect/scrollbar 수동 검증은 `docs/test-plan.md`의 MAN-050b에 Not run으로 남겼다.
