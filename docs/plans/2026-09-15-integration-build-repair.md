# 통합 빌드 실패 진단과 회귀 검증

## 목표와 완료 조건

가상 데스크톱 fallback, 터미널·renderer 복구와 진단 로그 강화가 합쳐진 상태에서
Debug 통합 빌드의 최초 오류를 확인한다. 소스 또는 프로젝트 입력 충돌이면 가장 작은
범위로 수정하고, 그렇지 않으면 생성 입력·실행 환경 원인을 분리해 불필요한 제품 변경을
피한다. 지정 SDK restore, Debug build, 전체 test와 portable 패키징을 순서대로 통과하고
사용자별 runtime data가 패키지에 없음을 확인한다.

## 범위와 영향 파일

- 세 선행 작업의 직접 선언, 테스트와 프로젝트 입력
- `scripts/package-portable.ps1`의 빌드 격리 및 content deny-list
- `docs/test-plan.md`의 자동 검증 결과와 미수행 수동 항목
- 이 계획의 원인, 판단과 완료 기록

가상 데스크톱 API 범위, terminal session/renderer 복구 정책, 진단 로그 schema와
사용자 설정 저장 위치는 변경하지 않는다.

## 판단과 위험

- 첫 Debug 빌드의 최초 오류는 Terminal WPF 프로젝트의
  `Starboard.Modules.Terminal_MarkupCompile.cache` 삭제가 거부된 `MC1000`이었다.
  같은 빌드에서 DesktopIntegration test의 assembly reference cache 및 coverage 임시
  파일 쓰기도 거부됐다. 대상은 모두 ignored `bin/obj` 생성 입력이며 source 선언,
  project reference 또는 package 설정 오류는 보고되지 않았다.
- 대상 파일은 read-only가 아니고 현재 사용자에게 Modify ACL이 있었다. 빌드 서버를
  정상 종료한 뒤 동일한 `--no-restore` Debug build가 경고·오류 없이 성공했으므로,
  관찰 근거는 동일 worktree 생성물을 사용하던 잔존/병렬 빌드 프로세스의 일시적 파일
  경쟁이다. 특정 점유 PID는 확인되지 않았으므로 그 이상으로 원인을 확대하지 않는다.
- repository source나 project setting을 바꾸면 검증된 세 기능을 후퇴시키거나 실제
  원인과 무관한 복잡도를 만들 수 있다. 따라서 생성 파일을 source로 다루는 변경은 하지
  않고 build server shutdown 뒤 동일 명령 재실행을 최소 복구로 사용한다.
- portable 흐름은 이미 build server shutdown, `-maxcpucount:1`, 별도 staging
  `ArtifactsPath`를 사용한다. 이 격리 정책과 사용자 data deny-list를 실제 패키지와
  기존 회귀 테스트로 재검증한다.

## 구현 단계

- [x] 세 선행 계획, 직접 참조와 package 입력 확인
- [x] 지정 SDK restore와 Debug build로 최초 오류 재현 및 생성 입력 영향 범위 확인
- [x] build server 정상 종료 후 동일 Debug build로 소스/프로젝트 충돌 부재 확인
- [x] 세 기능과 package deny-list의 기존 회귀 테스트 및 package build 격리 테스트 확인
- [x] 전체 Debug test와 portable Release package 검증

## 검증 방법

- 지정된 `C:/Users/round1studio_14/.dotnet/dotnet.exe`로 solution restore, Debug
  build와 전체 test를 선행 성공 순서대로 실행한다.
- virtual desktop supported/no-op/failover tests, terminal failure recovery integration
  tests, `FileDiagnosticLogTests`와 portable policy tests가 전체 suite에 포함돼 통과하는지
  확인한다. package build가 build server를 종료하고 단일 node와 별도 artifacts 경로를
  사용하는지는 작은 source-contract 회귀 테스트로 고정한다.
- `scripts/package-portable.ps1`로 Release restore/build/test, self-contained publish,
  deterministic archive/hash와 추출 smoke를 실행한다. ZIP entry를 별도로 열어 log,
  settings, workspace, saved tabs, WebView2 user data 및 개발 산출물 패턴이 0건인지 확인한다.
- 실제 virtual desktop/Explorer, renderer process kill, WebView2 Runtime 제거, IME와
  multi-monitor/DPI 시나리오는 자동 결과로 대체하지 않고 수동 미수행 상태를 유지한다.

## 진행 기록

- 2026-09-15: 지정 SDK restore는 성공했다. 첫 Debug build는 Terminal markup cache의
  `MC1000` access denied가 최초 오류였고 DesktopIntegration test cache/coverage에도
  같은 종류의 쓰기 거부가 이어졌다.
- 2026-09-15: 파일 속성·ACL을 확인하고 build server를 정상 종료했다. 동일 Debug
  build는 경고·오류 0개로 성공했고 당시 전체 517개 test가 통과했다.
- 2026-09-15: 같은 생성 입력 경쟁을 portable build가 피하도록 build-server shutdown,
  단일 MSBuild node와 staging artifacts 경로를 사용하는 회귀 테스트를 추가했다.
- 2026-09-15: 테스트 추가 후 첫 전체 실행에서 기존 ConPTY GUI-host tab 검사가 첫 shell
  marker의 10초 제한을 한 번 넘겼다. host identity와 경로는 정상이고 변경은 package
  source-contract test뿐이었다. 실패한 test는 7초에 통과했고 이어진 전체 Debug suite도
  518개 모두 통과해 일시적 host 시작 지연으로 분리했다.
- 2026-09-15: portable 스크립트가 Release build와 동일 518개 test, publish,
  deterministic ZIP/hash, 추출 smoke를 통과했다. ZIP 501개 entry의 독립 민감 경로
  검사 결과는 0건이며 SHA-256은
  `93edf4cac885380442da24f564d3d1c0b80c94042c6b71dc4e73d919adcbc23a`다.

## 완료 요약

통합 실패는 선언·참조·프로젝트 설정 충돌이 아니라 공유 생성 입력의 일시적 파일
경쟁이었다. build server를 정상 종료해 점유를 해제한 뒤 정확히 같은 Debug build가
성공했으므로 제품 코드와 세 선행 기능은 변경하지 않았다. 새 package build 격리
테스트와 전체 suite로 가상 데스크톱 영구 fallback, session generation 기반 shell/renderer 복구,
metadata-only bounded 로그와 portable deny-list를 확인했다.

portable ZIP에는 로그, 사용자 설정, workspace, saved tabs, WebView2 사용자 데이터,
database/dump/PDB/temp/backup이 없었다. 실제 Windows virtual desktop·Explorer·WebView2
복구 및 display/IME 조작은 수행하지 않았으며 `docs/test-plan.md`의 수동 항목대로
미수행 상태다.
