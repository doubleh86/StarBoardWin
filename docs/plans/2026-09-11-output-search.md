# 현재 탭 출력 검색 구현 계획

## 목표와 완료 조건

현재 활성 terminal 탭의 메모리 내 xterm scrollback만 검색한다. 검색 UI는 shell 입력과
분리하고, 이전/다음 이동·현재 일치 강조·결과 없음·Escape 닫기와 terminal focus 복귀를
제공한다.

## 범위와 영향 파일

- `Terminal/Presentation/Renderer`의 고정된 xterm search addon, 검색 상태와 overlay,
  재생성 `dist/`
- renderer 검색 동작을 확인하는 focused source/dist test와 architecture, test plan,
  README 문서
- shell process, renderer protocol, DesktopIntegration 및 Preferences는 변경하지 않는다.

## 판단과 위험

- 공식 `@xterm/addon-search`를 각 기존 `Terminal` 인스턴스에 load한다. addon은 해당
  terminal buffer만 탐색하므로 탭 간·renderer 재시작 전 기록을 결합하지 않는다.
- 검색 입력은 overlay의 HTML input이 소유한다. terminal custom key handler는 Ctrl+F를
  가로채며 overlay가 열려 있는 동안 탐색 키를 capture 단계에서 차단한다.
- session 선택, 제거, reset과 renderer document 재생성에서 검색 상태와 addon highlight를
  정리한다. xterm terminal/session/PID/output에는 host message를 보내지 않는다.
- 200 DIP collapsed panel에서는 tab strip 아래에 겹치는 한 줄 compact overlay를 사용하고,
  폭이 좁으면 입력과 아이콘 버튼이 shrink한다. 실제 WebView2, 키 이벤트 순서, 한글 IME와
  네 테마의 시각 검증은 수동 항목으로 남긴다.

## 구현 단계

- [x] 기존 renderer 탭·scrollback·shortcut 경계를 조사했다.
- [x] search addon 의존성을 고정하고 각 session에 연결한다.
- [x] 검색 overlay와 상태 전환, 탭/renderer lifecycle 정리를 구현한다.
- [x] focused regression test, source/dist, 문서를 갱신하고 검증한다.

## 검증 방법

- renderer build로 고정 dependency와 offline dist 동기화를 확인한다.
- renderer source/dist focused test, C# alignment, 전체 solution restore/build/test,
  `git diff --check`를 실행한다.
- 실제 WebView2에서 200 DIP·좁은 폭·IME·네 theme와 shell 미전달을 수동으로 확인한다.

## 진행 기록

- 2026-09-11: renderer는 한 WebView2 document에서 session별 xterm buffer를 유지하며,
  `TerminalSessionCoordinator`나 renderer host protocol을 검색에 필요로 하지 않음을 확인했다.
- 2026-09-11: `@xterm/addon-search` 0.16.0을 package/lock에 고정하고 compact overlay,
  lifecycle cleanup과 dist contract test를 추가했다. renderer build와 solution
  restore/build/test는 통과했다. alignment script는 이번 범위 밖 DesktopIntegration의 기존
  continuation alignment 6건으로 실패했다.

## 완료 요약

검색은 활성 xterm instance의 scrollback만 탐색하며, `Ctrl+F`와 overlay key를 shell로
보내지 않는다. 탭 전환·제거·reset은 decorations와 overlay를 안전하게 정리하고 Escape는
동일 terminal focus를 복원한다. 실제 WebView2 200 DIP·좁은 폭·한글 IME·네 theme 검증은
`MAN-046`으로 Not run을 명시했다.
