# 앱 정상 종료 교착 수정

## 목표와 완료 조건

트레이 `종료`와 terminal 오류 화면의 `종료` 요청이 앱 창만 닫거나 UI 스레드를
멈추지 않고, 작업공간 저장과 모든 terminal session 정리를 제한 시간 안에 마친 뒤
Starboard 및 소유한 자식 process를 종료하게 한다.

완료 조건은 다음과 같다.

- 종료 요청 직후 중복 입력을 차단하고 종료 절차를 한 번만 시작한다.
- 비동기 terminal 정리를 UI dispatcher의 동기 대기로 막지 않는다.
- 작업공간 저장, ConPTY·shell, WebView2, tray와 desktop hook을 정해진 순서로
  해제한 뒤 WPF `Application.Shutdown`을 호출한다.
- 종료 요청 후 8초 안에 Starboard와 소유한 shell process가 사라진다.
- 시작 실패, 단일 instance 조기 종료와 Windows session 종료 경로가 회귀하지 않는다.

## 현재 증상과 원인

- 2026-09-16 사용자 실행에서 앱이 제대로 종료되지 않는 증상이 다시 제보됐다.
  정확한 입력 경로와 남은 process 종류는 사용자 관찰을 추가로 확인한다.
- 과거 배포 기록에도 정상 종료 요청 후 15~20초 동안 process가 남아 강제 종료한
  사례가 여러 번 기록돼 있다.
- 현재 `AppCoordinator.HandleExitRequested`는 UI thread에서 곧바로
  `Application.Shutdown()`을 호출한다. 이어지는 `App.OnExit`는
  `ShutdownAsync().GetAwaiter().GetResult()`로 terminal 비동기 정리를 동기 대기한다.
- `TerminalModule.ShutdownCoreAsync`의 continuation이 WPF synchronization context를
  캡처할 수 있으므로, UI thread가 `OnExit`에서 기다리는 동안 정리 continuation도
  같은 UI thread를 기다리는 교착 위험이 있다.
- `AppCoordinator.ShutdownCoreAsync`가 마지막 window를 직접 닫는 현재 순서도 WPF의
  기본 `OnLastWindowClose` 정책과 겹치므로, 명시적 종료가 끝나기 전에 `OnExit`가
  재진입하지 않도록 수명 주기 정책을 함께 고정해야 한다.

## 범위와 영향 파일

- `src/Starboard.Windows/App.xaml`, `App.xaml.cs`
  - 명시적 shutdown 정책과 시작 실패·OS 종료 fallback
- `src/Starboard.Windows/Composition/AppCoordinator.cs`
  - 단일 비동기 종료 요청, 중복 요청 방지, 정리 완료 뒤 WPF shutdown
- `src/Modules/Starboard.Modules.Terminal/TerminalModule.cs`
  - UI context를 캡처하지 않는 module 정리 경계 검토
- 관련 integration/unit test와 `docs/test-plan.md`

ConPTY의 정상 종료 제한 시간과 작업공간 저장 format은 변경하지 않는다. 응답하지
않는 임의의 외부 process를 광범위하게 종료하는 기능도 추가하지 않는다.

## 판단과 위험

- 권장 구조는 `Application.Shutdown` 전에 coordinator의 비동기 정리를 끝내는
  2단계 종료다. 앱의 `ShutdownMode`는 `OnExplicitShutdown`으로 고정해 마지막 window
  close가 정리 중 `OnExit`를 먼저 발생시키지 않게 한다.
- WPF event handler는 종료 task를 coordinator가 소유하고 예외를 관찰해야 한다.
  추적되지 않는 fire-and-forget 호출은 사용하지 않는다.
- `OnExit`는 이미 완료됐거나 진행 중인 동일 shutdown task만 최후 방어로 기다린다.
  일반 사용자 종료에서 최초 비동기 정리를 여기서 시작하지 않는다.
- Windows session 종료는 운영체제 시간 제한이 있으므로 작업공간 저장 실패를
  숨기지 않되 앱 종료를 무기한 막지 않아야 한다.

## 구현 단계

- [x] 사용자 제보의 종료 입력 경로와 남는 process를 확인하고 재현 기준을 고정한다.
- [x] host 종료를 중복 안전한 비동기 2단계 흐름으로 변경한다.
- [x] terminal 정리 continuation의 synchronization context 캡처를 제거하거나 명시적으로
  안전한 thread 경계로 전환한다.
- [x] 정상·중복·시작 실패 종료 회귀 test를 추가한다.
- [x] 전체 restore/build/test를 수행하고 실제 배포본 종료 확인을 manual matrix에 남긴다.

## 검증 방법

- host integration test: 종료 요청 여러 번에도 terminal 정리와 WPF shutdown이 각각
  한 번만 수행되고 UI dispatcher가 교착되지 않는다.
- terminal test: 여러 session과 저장 대기 상태에서도 전체 cleanup deadline 안에
  반환하고 자식 PID가 종료된다.
- 실제 smoke: 별도 test instance에서 트레이 `종료` 후 8초 이내 Starboard, PowerShell,
  ConHost와 전용 WebView2 process가 사라지는지 확인한다.
- `dotnet restore`, Debug solution build, 전체 automated test를 실행한다.

## 진행 기록

- 2026-09-16: 사용자 제보를 접수하고 과거 강제 종료 기록 및 현재 WPF 종료 경로를
  대조했다. 제품 코드는 아직 수정하지 않았다.
- 2026-09-16: 트레이 `종료` 뒤 창은 사라지지만 `Starboard.exe`가 계속 남는 증상임을
  사용자에게 확인했다.
- 2026-09-16: WPF를 `OnExplicitShutdown`으로 전환하고 중복 요청을 하나로 합치는
  `ApplicationShutdownSequence`를 추가했다. terminal·workspace·desktop 정리가 완료된
  뒤에만 WPF shutdown을 요청하며, 정리 실패도 진단한 뒤 실패 exit code로 종료한다.
- 2026-09-16: 종료 순서·중복 요청·실패 관찰 회귀 test 3개가 통과했다. 지정 SDK의
  Debug restore와 solution build는 경고·오류 0개, 전체 532개 test는 실패·skip 0개로
  통과했다. 기존 바탕화면 v0.2.1 instance는 교체하지 않았으므로 실제 트레이 종료
  8초 smoke는 새 배포 후 확인 대상으로 남겼다.
- 2026-09-16: `44bc8a8`을 `origin/main`에 push하고 Release 532개 test, self-contained
  publish, 결정적 ZIP·SHA-256과 추출 smoke를 통과했다. SHA-256은
  `376823296e156c87bfd09627bab3fae145e0484d98805ca0ad1d8d5b303b2262`다. 기존 배포본은
  정상 종료 요청 후 8초 안에 종료되지 않아 강제 종료했고, 백업 없이 501개 파일을
  hash 일치 확인 후 바탕화면에 교체했다. 새 PID 61820의 응답 상태를 확인했으며 실제
  트레이 `종료` 재검증은 사용자 입력 확인 대상으로 유지한다.

## 완료 요약

기존에는 `Application.Shutdown`이 먼저 `OnExit`를 발생시키고 UI thread가 비동기
terminal 정리를 동기 대기했다. 수정 후에는 명시적 WPF shutdown 모드에서 정리 task를
먼저 await하고, window·tray·terminal 자원이 모두 해제된 뒤 application을 종료한다.
동일 시점의 중복 종료는 첫 요청의 exit code와 task를 공유한다. 자동 검증은 완료됐으며
실제 트레이 입력과 배포 process 종료 시간은 `docs/test-plan.md`의 `SMK-006`으로 관리한다.
