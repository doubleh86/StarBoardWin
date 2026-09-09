# 축소 패널과 하단 작업표시줄 간격

## 후속 기획 안내

아래 내용은 외부 6 DIP 간격을 적용·배포한 당시 기록이다. 후속 사용자 요청으로
외부 간격을 없애고 terminal 내부 여백으로 입력 줄을 보호하는 PANEL-UX-02를
[작업공간 기획](2026-09-09-workspace-convenience.md)과
[탭·패널 UI 계획](2026-09-03-renderer-and-tab-ui.md)에 추가했다.
후속안은 이후 구현됐으며, 축소 하단 panel은 외부 간격 0으로 taskbar에 밀착하고 renderer
내부 6 DIP padding으로 입력 줄 여유를 제공한다. 이 문서의 이전 배포 hash와 당시 검증
이력은 historical record로 유지한다.

## 목표와 범위

하단 입력 줄이 작업표시줄에 붙어 보인다는 피드백에 따라 축소 패널을 6 DIP 올린다.
높이, 터미널 행 수, 작업 영역, focus/z-order 정책은 변경하지 않는다.
상단·좌우 작업표시줄과 확장 모드 위치는 유지한다.

## 구현과 위험

- `PanelGeometryCalculator`에서 하단 패널 계산 시 monitor Y DPI로 간격을 변환한다.
- 높이를 유지할 공간이 부족하면 간격만 줄여 work area 밖으로 나가지 않게 한다.
- 기존 스타일 정리 변경은 보존한다. 후속 배포 요청에 따라 바탕화면 배포본을
  백업·교체하고 재실행한다. 재실행 시 기존 shell session은 종료된다.
- geometry 단위 테스트와 관련 settings/window integration 기대값을 갱신한다.

## 검증

- 100/125/150/200% 배율, 음수 monitor 좌표, auto-hide, 공간 부족/oversized 높이.
- 기존 확장/복원과 상단·좌우 geometry 테스트 유지, 전체 Debug build/test.
- 실제 사용자 화면의 가독성 확인은 배포 후 필요하다.

## 상태

구현 완료. geometry 집중 테스트 21개, 최종 Debug build 경고·오류 0개 및 전체
193개 테스트 통과. 설정 rollback의 기대 위치도 새 간격에 맞췄다.
TRK 정렬 검사를 통과했으며 기존 스타일 변경을 보존했다.

후속 요청에 따라 Release build 경고·오류 0개, 전체 193개 테스트와 portable package
검증을 통과한 작업 트리를 바탕화면에 배포하고 재실행했다. publish와 추출본 499개
파일 hash를 대조했고, 기존 instance 종료 후 smoke도 exit code 0으로 통과했다.

- 배포: `C:/Users/round1studio_14/Desktop/Starboard-win-x64`
- 백업: `C:/Users/round1studio_14/Desktop/Starboard-win-x64-backup-20260909-102608`
- package SHA-256: `e46d0cdf743209c1754b60c836a5aa2dc885bfa7acca0bb93826d5ba8c519017`
- 버전 metadata는 기반 commit `a0607c5`이며 미커밋 스타일·간격 수정을 포함한다.
- Explorer를 통해 실행한 배포본 PID 16876의 응답 상태와 PowerShell, ConHost,
  WebView2 자식 process를 확인했다. 시작 후 오류 log 갱신은 없었다.

실제 사용자 화면에서의 간격·입력과 hardware 시나리오는 아직 확인하지 않았다.
