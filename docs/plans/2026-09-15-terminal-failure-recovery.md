# 터미널 실패 복구 강화

## 목표와 완료 조건

- ConPTY 생성 실패와 셸 종료를 해당 탭에만 한정하고 다른 탭과 호스트 수명을 유지한다.
- WebView2 renderer process 실패 시 살아 있는 셸을 종료하지 않고 새 renderer에 현재 탭 상태와
  실패 중 쌓인 제한된 출력을 다시 연결한다.
- WebView2 Runtime이 없으면 앱을 종료시키지 않고 오프라인 설치 안내, 재시도와 앱 종료 동작을 제공한다.
- restart, remove, shutdown과 엇갈린 이전 세대 callback이 현재 세션이나 renderer 상태를 바꾸지 못하게 한다.
- 오류·진단·테스트 산출물에 명령이나 터미널 출력 본문을 기록하지 않는다.

## 범위와 영향 파일

- `src/Modules/Starboard.Modules.Terminal/Application`: 세대가 포함된 세션 event와 callback 검증
- `src/Modules/Starboard.Modules.Terminal/Presentation`: 교체 가능한 WebView2 수명, 복구 surface와 bounded output 재연결
- `src/Modules/Starboard.Modules.Terminal/TerminalModule.cs`: 사용자 종료 요청을 host에 전달하는 contract
- `src/Starboard.Windows/Composition/AppCoordinator.cs`: terminal 복구 surface의 명시적 종료 요청 조정
- Terminal unit/integration tests와 `README.md`, `docs/architecture.md`, `docs/test-plan.md`
- Preferences module과 설정 schema는 변경하지 않는다.

## 판단과 위험

- 셸 lifetime의 정본은 coordinator의 `TerminalSessionReference`다. 출력·종료 event에도 이 reference를
  전달하고 소비 시 현재 세대인지 다시 확인한다.
- dispatcher에 대기한 workspace callback은 전달 당시 snapshot을 적용하지 않고 실행 시점의 최신 coordinator
  snapshot을 읽는다.
- renderer process 실패 뒤 기존 WebView2 control은 재사용 가능하다고 가정하지 않는다. control을 폐기하고 새
  environment/control을 만들며 ConPTY session은 그대로 둔다.
- renderer 부재 중 출력은 세션별 4 MiB 한도로만 메모리에 보관한다. 넘친 경우 본문 없이 drop 사실만 진단한다.
- Runtime 설치 자체는 네트워크 요청이나 관리자 권한을 자동 수행하지 않는다. 앱은 README의 오프라인 standalone
  installer 절차를 안내하고 재시도 또는 종료만 제공한다.

## 구현 단계

- [x] 세대가 포함된 event와 늦은 callback 회귀 테스트를 추가한다.
- [x] WebView2 교체 재연결과 Runtime 누락 recovery surface를 구현한다.
- [x] host 종료 요청 배선과 집중 unit/integration test를 추가한다.
- [x] 아키텍처, 사용자 안내와 test matrix를 실제 구현에 맞춘다.
- [x] 지정된 restore/build/test와 diff 검증을 통과한다.

## 검증 방법

- `Starboard.Modules.Terminal.Tests`에서 start 실패 격리, restart/remove/dispose 이후 늦은 output/exit와
  세대 전달을 검증한다.
- `Starboard.IntegrationTests`에서 renderer control 교체, bounded backlog, Runtime 안내와 host 종료 배선을
  source contract로 검증한다.
- 프로젝트 지정 .NET SDK로 solution restore, Debug build와 전체 automated test를 실행한다.
- 실제 Runtime 제거와 renderer process 강제 종료 UI는 기존 사용자 환경을 훼손하지 않도록 수행하지 않고
  수동 항목으로 남긴다.

## 진행 기록

- 2026-09-15: 기존 coordinator가 native callback의 entry identity는 검사하지만 출력 event에 generation을
  전달하지 않아 재시작 경합 시 이전 출력이 새 세대 backlog로 분류될 수 있음을 확인했다.
- 2026-09-15: 기존 renderer 재시도는 실패한 WebView2 control에 다시 Navigate하므로 browser/renderer process
  실패 종류에 따라 복구가 불가능할 수 있음을 확인했다.
- 2026-09-15: 관련 Terminal 59개와 integration 6개 집중 검사를 통과했다. sandbox 내부 병렬 MSBuild와
  전체 test host 실행은 자식 process 제한 때문에 무진단 실패/정체됐고, 같은 필수 argv를 허용된 실행에서
  다시 수행해 restore, 경고·오류 없는 Debug build와 전체 512개 test 통과를 확인했다.

## 완료 요약

세션 output/exit event에 generation을 포함하고 coordinator와 renderer 양쪽에서 이전 generation을 거부한다.
restart/remove/shutdown과 엇갈린 native callback, 오래된 dispatcher workspace event와 교체 전 WebView2 event는
현재 상태에 적용되지 않는다. Renderer process 실패 시 실패한 WebView2 control을 폐기하고 새 control에 live
ConPTY snapshot과 실패 중 쌓인 세션별 최대 4 MiB 출력을 전달한다. 중단 전 renderer scrollback은 복원하지
않고 이 제한을 README에 명시했다.

Runtime 누락은 전용 예외를 local error surface로 변환하고 offline Evergreen Standalone Installer 절차,
`다시 시도`와 host-owned `앱 종료`를 제공한다. 사용자 오류와 진단에는 고정된 문구, session ID·exit code와
backlog drop 사실만 사용하며 command/output 본문은 포함하지 않는다. 실제 Runtime 제거와 process kill 뒤 UI
재연결은 사용자 환경 보호를 위해 수행하지 않았고 test plan의 manual 항목으로 유지했다.
