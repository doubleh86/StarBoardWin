# 잠금 해제 후 패널 너비 복구

## 목표와 완료 조건

PC를 잠갔다가 해제한 뒤 가끔 패널의 탭 바와 terminal viewport/세로 scrollbar가
화면의 왼쪽 일부 폭에서 끝나고 오른쪽이 비는 제보를 해결한다. 정상적인
작업표시줄 monitor geometry가 돌아오면 collapsed 패널은 현재 work area의
전체 가로 폭을 다시 채운다. 현재 높이·실행 중인 셸·focus는 유지한다.

이 작업은 완료된 [탭 UI의 renderer 폭 수정](2026-09-16-tab-strip-menu-correction.md)
후에도 나타난 별도 재발이다. 당시 renderer CSS 수정의 성공이나 실패를
이번 잠금 해제 증상만으로 단정하지 않는다.

## 근거와 위험

- 사용자 제보(2026-09-29): 잠금/해제 후 간헐적으로 오른쪽 빈 영역이 생긴다.
  앞선 화면에서는 약 1900px 화면 중 탭/viewport가 약 1040px에서 끝났다.
  잠금 해제 시점의 실제 HWND·WebView2·DOM bounds는 아직 측정하지 못했다.
- `DesktopIntegrationModule.Reconcile`은 최신 geometry를 조회하지만
  engagement hold에서는 `LastSafeCollapsedBounds`를 복원한다.
  `SelectRestoredOrLatestBounds`의 유효성 검사는 예전 사각형이 현재 monitor
  work area **안에 있는지**만 검사한다. 예전 폭이 좁아졌어도 새 work area 안에
  들어오면 그대로 유지될 수 있다. 이는 코드상 가능한 원인이지, 아직
  현장 재현으로 확정된 원인은 아니다.
- display/setting 변경과 1초 주기 재조정은 존재하나 세션 unlock 전용
  경로는 현재 확인되지 않았다. 일시적인 taskbar unknown, monitor 전환,
  DPI/작업 영역 변경과 renderer resize 지연을 구별해야 한다.
- 안전한 geometry 재계산에도 창 활성화·최상단 고정·desktop work area 예약,
  shell 재시작을 사용하지 않는다. 작업표시줄이 확인되지 않는 동안에는
  기존 안전 frame을 유지하되 정상 snapshot이 돌아오면 폭을 복구한다.

## 범위와 영향 파일

- 우선 `DesktopIntegrationModule`, panel geometry/state/policy 및 해당
  module 테스트를 조사한다. 필요 시 display/session 알림 adapter를
  DesktopIntegration의 `Infrastructure`에 둔다.
- WPF 창 client bounds와 WebView2 child/DOM viewport를 실제로 비교해
  외부 창만 좁은지, renderer만 좁은지 분리한다. 후자인 경우에만 Terminal
  view/renderer fit 경로를 이 계획에 추가하고 먼저 범위를 갱신한다.
- 저장 탭 현재 디렉터리 변경은 [별도 계획](2026-09-10-saved-terminal-tabs.md)에
  맡긴다. 파일 소유권이 겹치지 않는 한 두 작업은 독립적으로 진행할 수 있다.

## 구현 단계

- [ ] 잠금→해제 및 monitor/work area 회복 과정에서 panel HWND, WebView2,
  workspace, xterm viewport와 scrollbar의 pixel/DIP bounds, taskbar 상태와
  현재 policy priority를 비교해 원인을 분리한다.
- [ ] stale collapsed frame의 유효성을 현재 taskbar monitor/edge/work area와
  비교하도록 보강한다. 유효한 높이 조절값은 유지하면서 잘못된 가로 폭은
  fresh geometry로 회복하고, unknown 상태의 안전 fallback은 보존한다.
- [ ] `좁은 안전 frame → 넓은 정상 work area`와 engagement hold,
  taskbar unknown→tracked, 다중 monitor·DPI, lock/unlock 반복의 회귀
  테스트를 추가한다. 실제 잠금/해제 관찰은 자동 테스트와 별개로 기록한다.

## 검증 방법과 진행 기록

- DesktopIntegration 집중 테스트 뒤 같은 변경의 solution restore/build/test를
  수행한다. 실제 Windows에서 잠금/해제 전후 panel/client/WebView2/viewport
  너비·scrollbar 끝, taskbar 비겹침, foreground HWND와 shell PID를 확인한다.
  재현되지 않거나 interactive desktop 조작이 불가하면 [MAN-056](../test-plan.md)에
  `Not run`으로 남기고 수정 완료의 수동 근거로 주장하지 않는다.
- [x] 2026-09-29: 제보 조건과 코드상 stale frame 경로를 확인해 계획을 작성했다.
  원인 확정·제품 수정·실제 잠금/해제 검증은 아직 수행하지 않았다.
