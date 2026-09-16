# PowerShell bootstrap 준비 상태 회귀 수정

## 목표와 완료 조건

PowerShell 7과 Windows PowerShell 새 탭이 내부 command-lifecycle bootstrap을 사용자 화면에
노출하거나 `>>` 연속 입력 상태에 머물지 않고, 정상 prompt 하나에서 즉시 첫 명령을 받을 수 있게
한다. 완료 알림의 명시적 prompt/exit 신호는 유지하되 초기화가 실패해도 shell 입력은 사용할 수
있어야 한다.

이 문서는 완료 알림 구현의 부가 메모가 아니라 **제품 코드 수정이 필요한 독립 P0 버그 계획**이다.
오케스트레이터는 `powershell-bootstrap-readiness` Task를 이미 끝난 완료 알림 작업으로 간주하거나
생략하지 않는다.

## 관찰과 현재 근거

- 2026-09-15 바탕화면 배포본 `0.2.0+1c52d68`에서 새 PowerShell 7 탭을 만들면 정상 prompt 아래에
  `>>`가 표시됐다. `>>`는 PowerShell parser/PSReadLine이 현재 입력을 완결되지 않은 여러 줄 명령으로
  판단한 상태이므로 단순 렌더링 흔적이 아니다.
- 같은 실행 시간대의 로컬 진단 로그에 `Terminal / WriteInput / Terminal input could not be delivered`
  경고가 반복됐다. 로그에는 command 또는 output 원문이 없으므로 이 경고만으로 원인을 확정하지 않고
  screenshot의 shell 상태와 함께 재현 근거로 사용한다.
- `PowerShellCommandLifecycleIntegration.CreateBootstrapInput`은 Base64 bootstrap을 stdin에 쓴 뒤
  `\r\n`으로 끝낸다. 일반 xterm keyboard Enter는 `\r`이며, 뒤의 `\n`이 interactive PSReadLine에서
  별도 line-feed로 처리되는지 먼저 검증한다.
- `ConPtySession.InitializeCommandLifecycleAsync`는 최초 shell output만 기다린 후 bootstrap을 보낸다.
  최초 output은 prompt 입력 준비 완료를 보장하지 않으므로 PowerShell profile/banner와 주입이 경쟁할
  가능성도 함께 검증한다.
- 기존 integration test는 bootstrap 문자열을 `pwsh -Command` 인자로 실행해 named-pipe 신호를
  확인한다. 실제 ConPTY의 대화형 stdin, echo, PSReadLine prompt 전환과 첫 사용자 입력은 포함하지 않아
  이번 회귀를 잡지 못했다.

## UX와 안전 계약

- 새 PowerShell 탭에는 사용자의 profile이 만든 정상 prompt만 한 번 보인다. `$starboardBootstrap`,
  Base64 문자열, `Invoke-Expression`과 내부 pipe 이름을 화면이나 scrollback에 표시하지 않는다.
- 초기화가 끝난 직후 첫 일반 명령, 한글/IME 입력, 붙여넣기와 `Ctrl+C`가 추가 복구 조작 없이 동작한다.
- command lifecycle channel은 prompt depth와 성공/실패 결과만 전달한다. command, output, working
  directory, tab 이름과 clipboard를 pipe나 로그에 추가하지 않는다.
- bootstrap 연결 timeout, PowerShell profile 오류 또는 pipe 손실 시 완료 알림만 해당 session에서
  비활성화하고 shell은 정상 prompt와 입력 상태로 계속 사용한다.
- 사용자의 PowerShell profile 파일과 전역 설정을 수정하지 않는다. 임시 script를 사용자 폴더에
  남기거나 관리자 권한·network를 요구하지 않는다.
- WSL, CMD와 custom shell 경로는 이번 수정으로 초기화 command를 받지 않으며 기존 동작을 유지한다.

## 오케스트레이터 작업 정의

### Task: `powershell-bootstrap-readiness`

- 제목: PowerShell 새 탭 bootstrap과 첫 prompt 복구
- capabilities: `backend`, `qa`
- dependencies: 없음
- 위험도: 높음
- 설명: 실제 ConPTY 대화형 session에서 bootstrap stdin 경계와 prompt readiness를 재현한 뒤,
  내부 스크립트가 보이지 않고 `>>`가 남지 않는 초기화 방식으로 교체한다. PowerShell 7/Windows
  PowerShell의 profile·prompt 보존, named-pipe 완료 신호와 실패 fallback을 함께 검증한다.
- 예상 변경 경로:
  - `src/Modules/Starboard.Modules.Terminal/Infrastructure/ShellCommandLifecycleIntegration.cs`
  - 필요할 때만 `src/Modules/Starboard.Modules.Terminal/Infrastructure/ConPtySession.cs`
  - 필요할 때만 `src/Modules/Starboard.Modules.Terminal/Domain/ShellLaunchSpec.cs`
  - `tests/Starboard.Modules.Terminal.Tests/ShellCommandLifecycleIntegrationTests.cs`
  - 실제 ConPTY 경계가 필요하면 `tests/Starboard.IntegrationTests/**`
  - 실제 ConPTY test host가 필요하면 `tests/Starboard.ConPtyTestHost/**`
  - `docs/plans/2026-09-15-powershell-bootstrap-readiness.md`
  - `docs/test-plan.md`
  - `README.md`
- 금지 경로:
  - `src/Modules/Starboard.Modules.Terminal/Presentation/Renderer/**`
  - `src/Modules/Starboard.Modules.DesktopIntegration/**`
  - `src/Modules/Starboard.Modules.Preferences/**`
  - 사용자 PowerShell profile과 `%LOCALAPPDATA%/Starboard`의 실제 설정 파일

### 인수 기준

1. PowerShell 7과 Windows PowerShell의 새 interactive ConPTY session에서 내부 bootstrap 문자열과
   `>>`가 나타나지 않고 정상 prompt 하나가 준비된다.
2. 초기화 직후 첫 명령이 정확히 한 번 실행되고 이후 명령의 성공·실패 완료 신호도 각각 한 번 온다.
3. 사용자 정의 prompt/profile을 보존하며 nested prompt, profile output과 느린 profile에서도 입력이
   bootstrap과 섞이지 않는다.
4. pipe 연결 실패·timeout·중도 종료 시 shell은 계속 입력 가능하고 완료 알림만 비활성화된다.
5. 사용자 command/output을 control channel, diagnostic log, test artifact에 기록하지 않는다.
6. CMD/WSL/custom shell과 기존 restart/remove/dispose generation 차단에 회귀가 없다.
7. 재현 test가 수정 전 실패하고 수정 후 통과하며 전체 자동 검증과 renderer source/dist 무변경 확인을
   통과한다.

## 구현 판단과 위험

- 먼저 bootstrap 끝의 `\r\n`을 대화형 Enter 하나인 `\r`로 제한했을 때의 실제 ConPTY 동작을
  검증한다. 문자열 단위 test만 추가하고 해결됐다고 판단하지 않는다.
- stdin 주입 자체가 prompt readiness/echo 경쟁을 제거하지 못하면 PowerShell process 시작 시
  `-NoExit`과 encoded initialization argument를 사용하는 방식을 비교한다. 이 경우에도 기존 profile을
  건너뛰는 `-NoProfile`을 기본으로 추가하지 않고 원래 prompt를 bootstrap 전에 확보한다.
- launch argument 방식은 PowerShell 7과 Windows PowerShell 모두에서 인자 길이, quoting, profile
  실행 순서와 종료 semantics를 확인한다. raw command 문자열 결합보다 기존 argument-list/command-line
  builder 경계를 사용한다.
- output 문자열에서 bootstrap 흔적을 광범위하게 삭제하는 방식은 정상 사용자 출력을 잃을 수 있으므로
  기본 해결책으로 사용하지 않는다. 제어 가능한 startup 단계와 handshake로 격리한다.
- 초기 prompt handshake를 기다리더라도 UI thread나 session 생성 전체를 무기한 막지 않는다. bounded
  timeout 뒤 shell-usable fallback으로 전환하고 늦은 callback은 generation으로 거부한다.

## 구현 결정 (2026-09-16)

- PowerShell bootstrap은 대화형 stdin 주입을 중단하고 기존 shell argument 뒤에 `-NoExit`과
  `-EncodedCommand`를 붙여 startup command로 실행한다. command line은 기존
  `WindowsCommandLineBuilder`가 argument list로 구성하므로 script quoting이나 사용자 화면 echo에
  의존하지 않는다.
- `-NoProfile`은 추가하지 않는다. PowerShell 자체 startup 순서에 따라 사용자의 profile과 profile
  output을 먼저 처리한 뒤 bootstrap command를 실행하고, bootstrap이 반환된 다음 원래 `prompt`
  함수를 정확히 한 번 호출한다.
- host는 PowerShell process를 만들기 전에 무작위 `LOCAL\` named-pipe의 읽기 client 연결을 시작하고,
  PowerShell startup command가 출력 전용 server를 만든다. Windows의 `CurrentUserOnly`가 계정뿐 아니라
  elevation level도 검사해 ConPTY child 연결을 거부한 실제 재현 결과를 반영한 방향이다. 별도 무작위
  nonce로 prompt signal을 인증하며 command/output은 channel에 넣지 않는다.
- 양쪽 pipe 연결과 첫 level-0 prompt 준비는 5초로 제한한다. 실패하면 prompt wrapper는 writer 없이
  남고 input readiness gate를 해제해 shell 입력은 계속 허용하며 완료 알림만 비활성화한다.
- CMD의 기존 stdin bootstrap은 유지한다. 따라서 `ConPtySession`의 bounded stdin initialization은
  CMD에만 적용되고 PowerShell 첫 입력은 startup command와 섞일 수 없다.
- 실제 ConPTY test host에 PowerShell bootstrap mode를 추가해 화면 output에 encoded bootstrap,
  continuation prompt가 없고 첫 성공 명령과 다음 실패 명령이 각각 한 번 완료되는지 검사한다. test host
  process에만 임시 `USERPROFILE`/`HOME`을 지정하고 종료 시 삭제하는 격리 profile로 느린 startup,
  profile output, custom prompt와 nested prompt 복귀를 두 PowerShell에서 직접 검증한다. 실제 사용자
  profile 파일은 읽거나 수정하지 않는다.

## 구현과 검증 순서

1. **R0 재현:** PowerShell 7/Windows PowerShell을 실제 ConPTY로 열고 bootstrap 직후 prompt 상태와 첫
   입력을 관찰하는 회귀 test를 만든다.
2. **R1 최소 수정:** CR/LF 입력 경계와 readiness 조건을 수정하고 bootstrap text/`>>` 부재를 검증한다.
3. **R2 fallback:** pipe timeout·profile 지연·profile 오류·session 종료에서 shell usability와 bounded
   cleanup을 검증한다.
4. **R3 통합:** 완료 알림 on/off, 정상/실패 명령, nested prompt와 restart/remove/dispose를 확인한다.
5. **R4 전체 검증:** Debug 전체 suite와 portable Release 경로, 문서 및 실제 화면 수동 항목을 갱신한다.

단일 startup 경계를 수정하는 작업이므로 여러 작업자가 같은 파일을 병렬 편집하지 않는다. 실제
ConPTY 재현과 기존 코드·문서 조사는 병렬 조회할 수 있지만 수정과 최종 검증은 한 Task에서 수행한다.

## 검증 명령

```powershell
dotnet restore Starboard.Windows.sln
dotnet build Starboard.Windows.sln --configuration Debug --no-restore
dotnet test Starboard.Windows.sln --configuration Debug --no-build --no-restore
pwsh -NoProfile -File scripts/Test-CSharpAlignment.ps1 -WorkingTree
git diff --check
```

- PowerShell 7과 Windows PowerShell의 exact integration test를 먼저 실행한 뒤 전체 suite를 수행한다.
- 실제 배포 화면에서 탭 5회 연속 생성, 즉시 첫 명령, 한글 입력, `Ctrl+C`, 완료 알림 on/off를 확인한다.
- 실제 WebView2/ConPTY 화면을 실행하지 못하면 unit test 성공으로 대신하지 않고 `Not run`으로 남긴다.
- renderer를 수정하지 않았다면 source/dist hash 또는 working-tree diff가 없음을 확인한다.
- 바탕화면 재배포와 앱 재실행은 별도 요청이며 이 구현 Task의 필수 범위가 아니다.

## 진행 기록

- [x] 2026-09-15: 사용자 화면의 `>>` 상태, 현재 bootstrap 종결 문자와 readiness/test 공백을 조사해
  독립 P0 Task로 기록했다.
- [x] 2026-09-16: stdin bootstrap 경쟁을 확인하고 startup encoded command, process-start 전 pipe
  client, bounded fallback으로 구현 방향을 확정했다.
- [x] 2026-09-16: `powershell-bootstrap-readiness` 제품 수정과 unit/runtime 회귀 test를 구현했다.
- [x] 2026-09-16: 실제 PowerShell 7과 Windows PowerShell을 각각 ConPTY test host에서 실행해 내부
  bootstrap/`>>` 부재, 격리된 느린 profile output/custom prompt/nested prompt와 첫 성공·다음 실패
  completion을 검증했다.
- [ ] 실제 WebView2 terminal 화면에서 새 탭 5회, 즉시 입력, 한글 IME, `Ctrl+C`와 알림 UI를 수동 검증.

## 완료 요약

PowerShell bootstrap을 startup `-EncodedCommand`로 옮기고 첫 prompt 또는 제한 시간 fallback까지 입력을
gate했다. PowerShell 7/Windows PowerShell 실제 ConPTY 자동 검증은 통과했으며 profile을 건너뛰거나
사용자 profile 파일을 수정하지 않는다. pipe 생성 충돌과 readiness timeout에서도 shell 입력이 계속
동작하고 완료 알림만 비활성화되는 runtime 회귀 검증을 추가했다. 전체 Debug 자동 test 523건과 C#
정렬·diff 검사가 최종 변경 기준으로 통과했다.
실제 WebView2 화면 검증과 배포본 교체는 수행하지 않았다.
