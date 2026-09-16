# 새 탭 추가 버튼 위치 회귀 수정

## 목표와 완료 조건

탭 수가 가용 폭보다 적을 때 `+`를 탭 바 오른쪽 끝에 두지 않고 마지막 탭 바로 뒤에
배치한다. 탭이 넘칠 때는 탭 목록만 가로로 스크롤하고 `+`와 바로 뒤의 `▾` 메뉴는 계속
보이게 한다.

이 문서는 다른 편의 기능의 참고 항목이 아니라 **제품 코드 수정이 필요한 독립 실행
계획**이다. 오케스트레이터는 아래 `tab-add-button-position` Task를 생략하거나 이미 완료된
작업으로 해석하지 않는다.

## 현재 상태와 원인

- 기준 커밋: `1c52d68` (`main`, 2026-09-15 확인).
- 현재 renderer의 `.tab-list`는 `flex: 1 1 auto`라 탭 수와 관계없이 남은 폭을 모두 차지한다.
  DOM에서 `+`가 탭 목록 바로 뒤에 있어도 flex item의 빈 영역 때문에 화면에서는 오른쪽 끝으로
  밀려난다.
- [기존 탭 UI 계획](2026-09-03-renderer-and-tab-ui.md)에는 같은 UX가 과거 구현 완료와 현재
  회귀 수정 대기로 나뉘어 기록돼 있었다. 이번 독립 Task가 현재 구현 상태를 우선한다.
- WSL/profile menu, 높이 drag와 탭 구성 복제는 이미 `main`에 들어왔으며 이번 범위에 포함하지
  않는다.

## UX와 동작 계약

- 일반 폭에서 `+`의 왼쪽 가장자리는 마지막 탭의 오른쪽 가장자리로부터 0~8px 안에 있어야 한다.
  탭 목록 뒤의 flexible spacer, `auto` margin 또는 빈 hit area로 둘을 분리하지 않는다.
- 순서는 `마지막 탭 → + → ▾ → 남은 빈 공간`이다. 탭이 하나이거나 이름이 짧아도 동일하다.
- 탭들이 가용 폭을 넘으면 탭 목록만 남은 폭까지 줄어 가로 스크롤한다. `+`와 `▾`는 스크롤
  영역 밖에서 항상 보여야 하며 서로 겹치거나 잘리지 않는다.
- `+`는 설정된 기본 profile로 새 탭 하나를 만드는 기존 동작만 수행한다. `▾`의 profile/saved-tab
  menu, `×`의 닫기 확인, 최대 8개 탭과 session focus 정책은 변경하지 않는다.
- 기존 32 DIP 탭 바와 두 control의 최소 32×31px hit target, tooltip, 접근성 이름,
  keyboard focus 표시를 유지한다. 색상만으로 `+`와 `×`를 구분하지 않는다.
- Dark, Light, One Dark와 Tokyo Night에서 구조와 간격은 같아야 한다.

## 오케스트레이터 작업 정의

### Task: `tab-add-button-position`

- 제목: 새 탭 추가 버튼을 마지막 탭 옆으로 이동
- capabilities: `frontend`, `qa`
- dependencies: 없음
- 위험도: 낮음
- 설명: renderer 탭 목록의 flex sizing을 수정해 여유 폭에서는 내용 너비만 차지하고,
  overflow에서는 필요한 만큼 줄어들게 한다. source와 committed `dist`를 함께 갱신하고 DOM 순서,
  layout contract와 새 탭 동작의 회귀 검증을 추가한다.
- 예상 변경 경로:
  - `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/src/styles.css`
  - 필요할 때만 `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/src/index.html`
  - 필요할 때만 `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/src/index.ts`
  - `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/dist/**`
  - `tests/Starboard.Modules.Terminal.Tests/RendererDistributionTests.cs`
  - `docs/plans/2026-09-15-tab-add-button-position.md`
  - `docs/test-plan.md`
- 금지 경로:
  - `src/Modules/Starboard.Modules.Terminal/Contracts/**`
  - `src/Modules/Starboard.Modules.Terminal/Application/**`
  - `src/Modules/Starboard.Modules.DesktopIntegration/**`
  - `src/Modules/Starboard.Modules.Preferences/**`
  - `src/Starboard.Windows/**`

### 인수 기준

1. 1개와 3개 탭의 여유 폭에서 마지막 탭과 `+` 사이의 시각 간격이 0~8px다.
2. 좁은 폭과 8개 탭에서 탭 목록만 가로 스크롤하며 `+`와 `▾`가 계속 보인다.
3. DOM과 keyboard 순서가 `tablist → + → ▾`로 유지된다.
4. `+` 클릭은 기존 session을 닫거나 menu를 열지 않고 기본 profile 새 탭만 하나 만든다.
5. 네 테마에서 32 DIP 높이, hit target, hover/focus/disabled 상태가 잘리거나 겹치지 않는다.
6. renderer source를 빌드한 `dist`가 commit된 asset과 일치하고 전체 자동 검증이 통과한다.

## 구현 판단과 위험

- `.tab-list`의 grow를 제거하고 shrink/overflow를 허용하는 가장 작은 CSS 변경을 우선한다.
  고정 폭 계산이나 탭 개수별 JavaScript 위치 계산은 도입하지 않는다.
- `overflow-x: auto`, tab item의 고정 shrink 정책과 `+`/`▾`의 `flex: 0 0 32px`는 유지한다.
- stylesheet 문자열 검사만으로 실제 픽셀 배치를 통과했다고 보고하지 않는다. 자동화 가능한
  DOM/CSS 계약과 실제 WebView2 화면 관찰을 구분한다.
- 새 dependency, protocol version, settings schema와 session lifecycle 변경은 필요하지 않다.
  CSS만으로 해결되지 않는 사실을 확인한 경우에만 같은 renderer source 범위에서 DOM을 조정한다.

## 검증 방법

```powershell
npm --prefix src/Modules/Starboard.Modules.Terminal/Presentation/Renderer run build
dotnet restore Starboard.Windows.sln
dotnet build Starboard.Windows.sln --configuration Debug --no-restore
dotnet test Starboard.Windows.sln --configuration Debug --no-build --no-restore
pwsh -NoProfile -File scripts/Test-CSharpAlignment.ps1 -WorkingTree
git diff --check
```

- renderer test에서 tablist, `+`, `▾` DOM 순서와 non-growing tab-list/고정 control 계약을 확인한다.
- 실제 WebView2에서 1/3/8개 탭, 짧고 긴 이름, 좁은 패널과 네 테마를 확인한다.
- 100/125/150/200% DPI 실제 화면을 실행하지 못하면 자동 결과로 대체하지 않고 `Not run`으로
  기록한다.
- 바탕화면 배포와 앱 재실행은 별도 요청이며 이 구현 Task의 필수 범위가 아니다.

## 진행 기록

- [x] 2026-09-15: 현재 `main` CSS에서 회귀 원인을 다시 확인하고 독립 Task와 경로를 확정했다.
- [x] 2026-09-15: `.tab-list`를 `flex: 0 1 auto`로 조정하고 고정 control·DOM 순서·기본 프로필 새 탭 회귀 계약을 추가했다. renderer source/dist와 자동 검증을 갱신했다.
- [ ] 실제 WebView2와 DPI별 위치 수동 검증.
- [ ] 2026-09-16: 이 worktree의 Debug WPF build는 성공했으나 Windows UI automation provider가 새
  `Starboard.exe`를 target app으로 승인하지 않아 panel을 열거나 조작하지 못했다. 좁은 panel,
  1/3/8개 tab, mouse/keyboard 새 탭 및 100/125/150/200% DPI의 `마지막 tab → + → ▾` 위치는
  **Blocked**로 남겼다. renderer layout 계약은 실제 픽셀 관찰을 대체하지 않는다.

## 완료 요약

자동 구현과 계약 검증은 완료했다. 실제 WebView2의 1/3/8개 탭 및 DPI별 시각 검증은 이 환경에서
수행하지 못했다. 2026-09-16 시도는 targetable window 권한이 없어 Blocked였으며, targetable
interactive desktop에서 네 DPI와 mouse/keyboard 상호작용을 재개해야 한다.
