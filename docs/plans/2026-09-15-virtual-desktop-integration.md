# 가상 데스크톱 연동과 안전한 fallback

## 목표와 완료 조건

DesktopIntegration이 Windows의 공식 `IVirtualDesktopManager`만 사용해 panel HWND의
현재 가상 데스크톱 상태를 조회하고, 명시적으로 받은 desktop ID로 panel을 이동할
수 있게 한다. COM 초기화나 호출 실패는 terminal/window 정책에 전파하지 않고
no-op 구현으로 전환한다. 모든 데스크톱 pinning은 지원하지 않는 capability로
노출한다.

## 범위와 영향 파일

- `src/Modules/Starboard.Modules.DesktopIntegration/Contracts/`: capability, 조회와 이동 결과 계약
- `src/Modules/Starboard.Modules.DesktopIntegration/Infrastructure/`: supported adapter, failover와 no-op 서비스
- `src/Modules/Starboard.Modules.DesktopIntegration/Infrastructure/Interop/`: 공식 COM interface 선언과 RCW 수명
- `src/Modules/Starboard.Modules.DesktopIntegration/DesktopIntegrationModule.cs`: panel HWND에 한정한 facade와 수명 조정
- `tests/Starboard.Modules.DesktopIntegration.Tests/`: supported/fallback/failover 및 module facade 테스트
- `tests/Starboard.ArchitectureTests/`: public contract와 module entry-point 경계 검사
- `docs/architecture.md`, `docs/test-plan.md`: capability, 제한, Windows 업데이트 위험과 검증 결과

Terminal과 Preferences module, host composition, undocumented virtual-desktop COM API,
desktop 열거·생성·전환·pinning은 범위에서 제외한다.

## 판단과 위험

- 공식 interface가 보장하는 세 메서드(`IsWindowOnCurrentVirtualDesktop`,
  `GetWindowDesktopId`, `MoveWindowToDesktop`)만 사용한다. 사용자의 desktop을 자동
  전환하지 않는다.
- pinning과 desktop switch notification은 공식 interface에 없으므로 capability는
  항상 false다. Windows 내부 COM interface GUID를 추측하거나 호출하지 않는다.
- HRESULT, COM activation, 잘못된 RCW cast와 release 실패를 non-fatal platform
  failure로 격리한다. 첫 초기화/호출 실패 뒤 adapter를 결정적으로 정리하고 no-op
  서비스로 원자적으로 교체한다.
- COM public contract 자체는 Windows 10 desktop apps부터 문서화되어 상대적으로
  안정적이지만 Windows shell 구현과 COM 등록 상태는 업데이트·정책·손상으로 실패할
  수 있다. 실패 시 panel 배치, terminal session, 앱 종료 순서가 계속 동작해야 한다.

## 구현 단계

- [x] 공식 COM interop와 supported/no-op/failover 구현
- [x] module facade, capability와 deterministic disposal 연결
- [x] adapter·fallback·public surface 자동 테스트 추가
- [x] 아키텍처와 수동/자동 테스트 문서 갱신
- [x] 지정 SDK restore/build/test 완료

## 검증 방법

- DesktopIntegration focused tests로 정상 query/move, 초기화 실패, 호출 중 실패 후
  단일 fallback 전환, adapter dispose와 module HWND 전달을 검증한다.
- ArchitectureTests로 새 public type이 Contracts 또는 module entry point에만 있고
  pinning operation이 public API로 노출되지 않는지 검사한다.
- 지정된 .NET executable로 solution restore, Debug build, 전체 test를 순서대로 실행한다.
- 실제 virtual desktop 전환 UI와 실제 shell COM 호출은 자동화하지 않았으면
  `docs/test-plan.md`의 manual 항목을 미수행으로 유지한다.

## 진행 기록

- 2026-09-15: Microsoft Learn의 공식 interface와 세 메서드 범위, Windows 10 desktop
  app 최소 지원 조건을 확인했다. 공식 surface에 pinning이나 switch event가 없음을
  기준으로 no-op capability를 설계했다.
- 2026-09-15: supported/fallback/failover와 module facade unit test, public surface
  architecture test를 추가했다. 지정 SDK restore, Debug build와 전체 504개 자동
  test가 통과했다.

## 완료 요약

`IVirtualDesktopManager`의 공식 세 메서드만 사용하는 adapter와 no-op fallback을
DesktopIntegration module에 연결했다. panel HWND의 current-desktop 여부와 ID를
조회하고 명시적 ID로 이동할 수 있으며, 초기화·호출 실패 뒤에는 adapter를 한 번
해제하고 unavailable/unsupported capability로 유지한다. pinning operation은
노출하지 않고 capability도 항상 false다.

지정 SDK solution restore와 Debug build(경고·오류 0), 전체 504개 test가 통과했다.
실제 interactive virtual desktop 생성·전환·panel 이동은 수행하지 않았으며
`docs/test-plan.md`의 VD-006과 MAN-021에 남겼다.
