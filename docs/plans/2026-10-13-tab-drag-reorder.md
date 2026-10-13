# 탭 마우스 드래그 정렬

## 현재 상태

**기획 완료·구현 미착수.** 2026-10-13 사용자 요청으로 추가한 독립 실행 계획이다.
이번 문서 커밋에서는 제품 코드, renderer 배포 asset, 앱 버전과 바탕화면 배포를 변경하지 않는다.
오케스트레이터는 아래 Task를 새 구현 대상으로 계획하되 기존 완료 기능을 다시 구현하지 않는다.

기준선은 `main`의 `1505ccb`다. 현재 탭 우클릭/`Shift+F10`의 `왼쪽으로 이동`·`오른쪽으로 이동`은
구현돼 있지만 마우스 드래그 정렬은 없다. `TerminalTabRegistry`의 순서와
`TerminalSessionCoordinator.WorkspaceChanged`를 정본으로 유지하며 renderer만의 별도 순서를 만들지 않는다.

## 목표와 완료 조건

작업표시줄 위의 작은 terminal에서 탭을 마우스로 끌어 원하는 위치로 옮긴다. 새 탭이나 shell을
만들지 않고 기존 탭 순서만 변경한다. 우클릭 좌우 이동은 접근 가능한 대체 경로로 유지한다.

1. 2~8개 탭에서 앞·뒤·중간 위치로 옮길 수 있고 1개 탭에서는 변경 요청을 만들지 않는다.
2. runtime session ID, shell PID, 이름·시작 폴더·generation, 입력·출력·scrollback을 보존한다.
   활성 탭은 같은 ID를 유지한다. 비활성 탭을 끌었다는 이유만으로 다른 terminal을 선택하지 않는다.
3. `×`·`+`·`▾`·이름 변경·높이 조절과 충돌하지 않고 일반 클릭, 더블클릭, 키보드 동작을 유지한다.
4. overflow에서도 정렬할 수 있고, 취소·오래된 요청·renderer 복구로 잘못된 순서를 저장하지 않는다.
5. workspace 복원이 켜져 있으면 기존 저장 경로로 새 순서를 저장한다. 꺼져 있으면 runtime 순서만
   변경하며 저장을 켜거나 새 파일/schema를 만들지 않는다. 기존 WSL 복원 지원 범위도 바꾸지 않는다.
6. 자동 검증과 실제 WebView2 검증을 나눠 기록한다. 빌드·문자열 검사만으로 실제 drag를 통과 처리하지 않는다.

## UX 계약

- 드래그 시작 영역은 탭 이름/탭 선택 영역이며 왼쪽 mouse button만 사용한다. 닫기 버튼, 메뉴,
  `+`, `▾`와 `+`/`▾` 사이 높이 조절 손잡이에는 드래그 handler를 적용하지 않는다.
- pointer 이동이 6 CSS px를 넘을 때 정렬 모드로 전환한다. 그 전의 mouse release는 기존 클릭이다.
  실제 drag 뒤의 click/double-click은 억제해 탭 선택·이름 편집·닫기를 우발적으로 실행하지 않는다.
- pointer 좌표와 DOM rect는 같은 CSS pixel 좌표계를 쓴다. WPF DIP나 screen physical pixel을
  섞지 않는다. 100/125/150/200%에서 이중 배율 계산 없이 동작해야 한다.
- 탭 바 높이와 현재 theme를 유지하고 삽입 위치를 얇은 표시로 안내한다. 노란 테두리, 상시 버튼,
  별도 창이나 panel 자동 확장은 추가하지 않는다. pointer preview는 shell 입력을 전달하지 않는다.
- 정렬 중에는 삽입 위치만 미리 보여주고 pointer release의 유효한 drop에서 한 번 확정한다.
  움직일 때마다 host에 이동 메시지를 보내거나 workspace 파일을 저장하지 않는다.
- 탭 목록의 좌우 가장자리에 머무르면 목록만 제한된 속도로 가로 스크롤한다. `+`와 오른쪽 끝 `▾`는
  움직이지 않으며 기존 `tablist → + → flexible spacer/높이 손잡이 → ▾` 배치를 보존한다.
- Escape, pointer cancel, panel/renderer 수명 종료 또는 유효한 대상 밖 release는 취소한다.
  capture를 해제하고 삽입 표시·자동 스크롤을 정리하며 아무 이동 요청도 보내지 않는다.
  정렬 취소용 Escape는 shell에 전달하지 않는다.
- drag 중 탭 추가·제거·재시작 또는 순서 snapshot 변경이 발생하면 취소한다. 배경 출력 같은 순서와
  무관한 갱신은 drag를 깨지 않는다. 취소 시 이전 배열로 새 host 상태를 덮어쓰지 않는다.
- 정렬은 focus를 강제로 shell로 넘기지 않는다. 기존 활성 탭과 키보드 focus 대상 ID를 보존하고,
  background 응답이 다른 foreground application의 focus를 빼앗지 않는다.

## 계약·구조 판단과 위험

- 기존 `move-session`은 `left`/`right` 한 칸 이동이다. drag를 여러 한 칸 메시지로 흉내 내면 탭
  추가·닫기와 경쟁할 수 있으므로 유효한 한 번의 위치 변경을 registry/coordinator에서 처리한다.
- 내부 renderer 요청은 이동할 session ID와 generation, 기준 탭 ID 및 앞/뒤 위치를 명시한다.
  renderer instance와 drag 시작 당시 순서가 여전히 유효한지 확인하는 정책을 계약 Task에서 확정한다.
  범위를 넘는 임의 배열, 없는/자기 자신 대상, 오래된 generation·renderer 요청은 거부한다.
- 순서 변경은 coordinator의 기존 동기화·WorkspaceChanged·workspace persistence 흐름에 연결한다.
  동일 위치는 no-op이고 거절 시 현재 host snapshot으로 UI를 맞춘다. module 간 참조, public API,
  settings schema와 protocol version 변경은 필요한 근거 없이 추가하지 않는다.
- 기존 session별 xterm/DOM 객체를 옮기거나 기존 정렬 갱신 경로를 사용한다. 탭 순서 때문에 terminal
  객체를 폐기·재생성하거나 ConPTY를 resize/restart하지 않는다. 소유한 pointer listener·capture와
  animation frame/자동 스크롤은 종료·취소 시 결정적으로 정리한다.
- Explorer 경로 드롭과 내부 탭 정렬을 구분한다. 외부 파일 drop, 터미널 본문 텍스트 선택, IME와
  clipboard shortcut handler를 가로채지 않는다. 추가 drag library나 runtime network는 사용하지 않는다.
- 저장 실패는 기존 로컬 상태 안내·재시도 정책을 따른다. 오류를 숨기거나 실행 중인 shell을 종료하지 않는다.

## 범위와 영향 파일

아래 경로 안에서 필요한 선언·호출부·테스트를 함께 수정한다. `dist`는 renderer source로 재생성한다.

- `src/Modules/Starboard.Modules.Terminal/Domain/TerminalTabRegistry.cs`
- `src/Modules/Starboard.Modules.Terminal/Application/TerminalSessionCoordinator.cs`
- `src/Modules/Starboard.Modules.Terminal/Application/RendererMessage.cs`, `RendererProtocol.cs`
- `src/Modules/Starboard.Modules.Terminal/Presentation/TerminalView.xaml.cs`
- `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/src/index.ts`, `styles.css`
- 필요하면 같은 renderer `src/`에 순수 drag/삽입 위치 정책 파일, `test/`에 Node 테스트
- `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/dist/**`
- `tests/Starboard.Modules.Terminal.Tests/TerminalTabRegistryTests.cs`, `TerminalSessionCoordinatorTests.cs`,
  `RendererProtocolTests.cs`, `RendererDistributionTests.cs`, 필요한 workspace 저장·복원 테스트
- 이 계획, `docs/plans/README.md`, `docs/test-plan.md`와 구현된 사용법을 설명하는 `README.md`

제외: 분할 화면, 탭 분리/별도 창, 창 위치 이동, 다른 앱으로 drag, shell 상태 복제, 저장한 탭 목록의
drag, WSL 구성 영속화, 새 설정·의존성, 다른 module 리팩터링, 버전 변경·release·배포.

## 오케스트레이터 Task와 병렬 경계

| Task ID | 제목 | 선행 | 파일 소유권과 완료 기준 |
|---|---|---|---|
| `tab-drag-reorder-contract` | 탭 드래그 정렬 계약 확정 | 없음 | 이 계획의 메시지 필드·오래된 요청·취소·순서 유지 규칙과 검증 fixture 확정. 제품 구현 전 계약 gate. |
| `tab-drag-reorder-runtime` | 세션을 유지한 위치 변경 구현 | contract | registry/coordinator/protocol/TerminalView 및 해당 C# 테스트만 소유. 한 번의 유효한 이동, no-op·경쟁 요청 거부, 기존 저장 흐름 확인. |
| `tab-drag-reorder-renderer` | 드래그 입력과 삽입 위치 UI 구현 | contract | renderer src/test/dist와 RendererDistributionTests만 소유. threshold·capture·취소·overflow·click 충돌 구현과 순수 정책 테스트. |
| `tab-drag-reorder-integration` | 통합 검증과 사용자 문서 갱신 | runtime, renderer | 메시지 왕복·workspace 순서·세션 보존 회귀 검증, 계획/테스트 계획/README 완료 상태 갱신. |

contract 완료 후 runtime과 renderer는 확정된 메시지 fixture를 공유하고 서로 다른 파일에서 병렬로
진행할 수 있다. `index.ts`·`styles.css`·`dist`와 RendererDistributionTests는 한 담당자만 수정한다.
계약 변경은 두 구현에 먼저 알리고 반영한 뒤 합류한다. 통합 전 두 작업이 각각 검증돼야 한다.
이 문서 작성만으로 다른 채팅을 생성·메시지 전송하거나 진행 중인 오케스트레이터를 중단하지 않는다.

## 구현 단계

- [ ] `tab-drag-reorder-contract`: 최신 main/dirty 상태를 확인하고 내부 요청·취소 계약과 fixture를 고정한다.
- [ ] `tab-drag-reorder-runtime`: 순서 변경 및 stale/no-op 처리, 저장·복원 연결과 C# 자동 검증을 구현한다.
- [ ] `tab-drag-reorder-renderer`: drag UI·자동 스크롤·수명 정리, Node 정책 검증과 source/dist를 구현한다.
- [ ] `tab-drag-reorder-integration`: 전체 자동 검증, 구현 상태·사용법·수동 검증 matrix를 갱신한다.

## 검증 방법

다음은 향후 제품 구현 시의 검증이며 이번 기획 커밋에서 실행한 결과가 아니다.

- 자동: 앞/뒤/중간 이동, 동일 위치, 없는/자기 자신 대상, 순서 변경 경쟁과 stale generation/renderer,
  활성 ID·세션 객체 보존, 저장 opt-in/off와 복원 순서, 기존 좌우 이동·닫기·선택 동작을 테스트한다.
- renderer 정책 자동: threshold 전 클릭/후 click 억제, 삽입 위치, overflow 스크롤, 취소 및 listener/
  capture 정리, 배경 출력 중 유지와 순서 변경 시 취소를 검증한다. 소스 문자열 검사만 하지 않는다.
- DOM/layout 자동 또는 simulated: 1/3/8개 탭, 짧은/긴/한글 이름, 좁은 폭과 네 theme에서 탭·닫기·
  `+`·손잡이·`▾` hit target과 삽입 표시가 겹치지 않는지 확인한다. 실제 WebView2 결과와 구분한다.
- 실제 WebView2: mouse drag와 취소, overflow 양방향, 비활성 탭, 좌우 이동 대체 경로, 이름 편집·IME,
  `×` 닫기 확인, `+` profile 선택, `▾` 저장 탭, panel 높이 조절·축소/확장과 focus를 확인한다.
  장기 실행·입력·scrollback을 가진 탭의 PID/상태 유지 및 opt-in 재실행 순서도 확인한다.
- 실제 DPI 100/125/150/200%와 잠금 해제·renderer 재연결을 구분해 기록한다. 수행하지 못한 시나리오는
  `docs/test-plan.md`에 `Not run (manual)` 또는 `Blocked (manual)`로 남긴다.

```powershell
npm --prefix src/Modules/Starboard.Modules.Terminal/Presentation/Renderer ci
npm --prefix src/Modules/Starboard.Modules.Terminal/Presentation/Renderer run build
dotnet restore Starboard.Windows.sln
dotnet build Starboard.Windows.sln --configuration Debug --no-restore
dotnet test Starboard.Windows.sln --configuration Debug --no-build --no-restore
pwsh -NoProfile -File scripts/Test-CSharpAlignment.ps1 -WorkingTree
git diff --check
```

새 Node 정책 테스트는 기존 renderer test 방식과 연결해 실행 명령을 구현 기록에 남긴다.
실제 drag 화면을 수행하지 못했다면 자동 검증 완료와 제품 수동 인수 미완료를 분리해 보고한다.

## 진행 기록

- 2026-10-13: 현재 코드의 좌우 이동 경로와 드래그 미지원 상태를 확인하고 기획·Task·병렬 소유권을 작성했다.
- 구현·자동 테스트·실제 WebView2 검증은 아직 착수하지 않았다. 완료 요약은 구현 후 추가한다.
