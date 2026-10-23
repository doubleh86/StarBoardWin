# 저장 순서·출력 흐름·문자 경계 안정화

## 목표와 완료 조건

2026-10-23 코드 검토에서 재현한 네 문제를 수정한다. 오래된 이벤트가 최신 탭 구성을
덮어쓰지 않고, 느린 renderer에서도 출력 대기량이 제한되며, 출력 경계의 보조 평면
문자와 허용된 크기의 붙여넣기가 손실 없이 전달되어야 한다.

## 범위와 영향 파일

- Terminal Application의 workspace persistence, renderer protocol, session coordinator.
- Terminal Infrastructure의 ConPTY 출력 읽기와 취소 가능한 읽기 gate.
- Terminal Presentation의 출력 buffer/ACK 연결, renderer source 및 배포 dist.
- 위 경계의 C#·renderer 회귀 테스트와 architecture/test-plan 문서.
- 같은 저장 경로에서 저장 성공 후 종료 시 동일 구성을 재저장하는 동작도 제거한다.
- 버전, 설정 schema, DesktopIntegration, UX, 의존성은 변경하지 않는다.
- 사용자 앱 종료·배포·Git commit/push는 이번 구현 요청에 포함하지 않는다.

## 판단과 위험

- workspace 이벤트를 state lock 밖에서 전달하므로 전달 순서는 상태 변경 순서와 다를 수
  있다. persistence lock 안에서 coordinator의 최신 snapshot을 다시 읽고, writer lock
  획득 후에도 저장 대상의 유효성을 검사한다. 새 schema나 외부 revision 계약은 불필요하다.
- 저장 성공은 해당 snapshot에만 적용한다. 저장 중 새 변경이 들어오면 dirty 상태를
  유지하고, 실패한 최신 구성은 종료 때 다시 저장할 수 있어야 한다.
- 출력은 session별 64 Ki UTF-16 단위 batch, 최대 256 Ki in-flight로 제한하고 xterm
  `write` callback의 ACK로 해제한다. host 대기량 상·하한으로 ConPTY 읽기를 비동기
  pause/resume하여 WebView 내부로 무제한 밀어 넣지 않는다. UI thread를 기다리게 하지 않는다.
- ACK는 renderer instance·session generation·batch sequence에 귀속한다. 중복·과거 ACK는
  무시한다. renderer 실패/재연결은 gate를 해제하고 기존 bounded backlog 정책을 유지하며,
  미확인 batch는 새 renderer에 순서대로 다시 보낸다. 종료 cancellation은 pause를 해제한다.
- UTF-16 high surrogate는 다음 fragment까지 보존한다. batch 및 overflow 경계에서
  surrogate pair를 분리하지 않는다. ANSI parser를 새로 만들지 않는다.
- clipboard 원문 1 Mi UTF-16 상한과 JSON envelope 상한을 분리하고 최악의 JSON escaping,
  xterm bracketed-paste wrapper 여유를 둔다. 승인한 paste는 기존 xterm paste 경계를 유지한다.
- 고정 지연 테스트 대신 조절 가능한 barrier/ACK를 사용해 역순 이벤트, 저장 중 변경,
  느린 parser·이전 세대 ACK·취소와 회복을 검증한다.

## 구현 단계

- [x] 최신 workspace 저장 및 저장 완료 dirty 정리
- [x] bounded 출력/ACK와 ConPTY backpressure 연결
- [x] UTF-16 경계 및 paste envelope 상한 회귀 방지
- [x] renderer dist 갱신, 전체 검증과 문서 인수 기록

## 검증 방법

- Terminal 집중 테스트: 역순 이벤트, 저장 경쟁, shutdown 중복/실패 재시도, Unicode,
  ACK budget·stale ACK·재연결·gate cancellation, 크기 상한.
- renderer production listener/harness: write callback 전 ACK 금지, callback 후 정확한 ACK,
  session 교체·제거 후 ACK 무시, 기존 paste/IME 회귀.
- renderer npm test/build, solution restore → Debug build → 전체 test, 정렬 및 diff 검사.
- 실제 Windows 10/PSReadLine, WebView2 고출력·RDP·IME는 자동 테스트와 구분해 수동 대기로 기록.

## 진행 기록

- 2026-10-23: 사용자 직접 수정 승인. main `31ae384`의 깨끗한 작업 트리에서 시작했다.
  별도 orchestrator worktree는 변경하지 않는다. 이전 재현 probe는 ignored `out/`에 있으며
  제품 회귀 테스트로 옮겨 현재 source를 검증한다.
- persistence는 이벤트의 옛 snapshot 대신 lock 안에서 최신 구성을 만든다. writer 대기 중
  폐기된 snapshot은 건너뛰며 성공한 현재 구성만 dirty를 해제한다. 저장 실패·저장 중 변경은
  shutdown flush로 남기고, 변경 없는 종료는 마지막 정상 backup을 불필요하게 교체하지 않는다.
- `TerminalOutputBuffer`가 Unicode-safe batch·in-flight ACK·bounded 장애 replay를 소유한다.
  `TerminalOutputReadGate`는 ConPTY의 다음 read만 취소 가능한 async wait로 제어한다.
  session generation 교체 시 늦은 이전 세대 정리가 새 buffer를 지우지 않게 제한했다.
- source/dist와 host protocol을 함께 갱신했다. 작은 입력의 UX/기존 paste 승인 경로와
  프로필·설정 schema는 유지한다. 새 의존성이나 module/public API는 추가하지 않았다.
- 집중 자동 테스트 190개 통과 후 writer 대기 경쟁과 실제 ConPTY/renderer 연결 검증을 추가했다.
- 제한 안 restore는 NU1900(보안 정보 조회 불가), test의 MSBuild 하위 프로젝트 처리는 컴파일 전
  오류 0개의 실패였다. 승인된 제한 밖 동일 명령은 진행했다. 검사·취약성 조회를 비활성화하거나
  timeout을 늘리지 않았다. 컴파일의 새 테스트 helper CA1822, 정렬 한 열과 기존 source-shape
  assertion을 수정한 뒤 최종 전체 검증을 다시 통과했다.

### 최종 검증

- SDK `10.0.301`, artifacts `out/verification/reliability-20261023`.
- `dotnet restore Starboard.Windows.sln --artifacts-path ... --disable-build-servers`: 통과.
- `dotnet build Starboard.Windows.sln --configuration Debug --no-restore --artifacts-path ... --disable-build-servers`:
  통과, 경고 0·오류 0.
- `dotnet test Starboard.Windows.sln --configuration Debug --no-build --no-restore --artifacts-path ... --disable-build-servers`:
  **721/721 통과** (Architecture 16, Terminal 455, DesktopIntegration 120, Preferences 37, Integration 93).
- renderer `npm test`: **120/120 통과**. `npm run build`: 통과, 실제 배포 dist 갱신.
- `scripts/Test-CSharpAlignment.ps1 -WorkingTree`, `git diff --check`: 통과.
- 실제 isolated GUI ConPTY pause/resume/paused dispose는 local integration으로 통과했다.
  xterm의 52,428,800 code unit 처리는 scheduling/host window를 통제한 simulation이며 parser 대기량의
  관측 최대치는 262,144였다. 실제 WebView2 화면 latency/메모리 benchmark로 보고하지 않는다.
- 실제 Windows 10·WSL·RDP·IME·재부팅 인수는
  [REL-MAN-01~02](../test-plan.md#저장출력-흐름문자-경계-회귀-2026-10-23)에 수동 대기로 남겼다.

## 완료 요약

**구현·자동 검증 완료 / 실제 화면 인수 대기**. 검토한 네 문제와 동일 저장 경로의 중복 쓰기를
수정했다. 출력 read/renderer/저장 수명 경계를 유지하면서 순서와 최대 대기량을 테스트 가능한
작은 internal 타입으로 분리했다. 후속 수정은 Application의 `TerminalOutputBuffer`,
`TerminalWorkspacePersistence`, `RendererProtocol` 및 Presentation의 ACK 연결을 함께 확인한다.
구현 완료 시점에는 사용자 앱 종료·재배포, commit/push, 버전 변경을 수행하지 않았다.

### 후속 배포 요청 — 2026-10-23

사용자가 커밋·푸시와 재배포를 요청했다. 위 검증 완료 수정분을 main에 게시하고 기존
0.2.2 framework-dependent 경량판을 새 commit metadata로 패키징한다. 바탕화면의 기존
경량판 폴더와 ZIP을 백업 없이 교체하며 사용자 설정·workspace 저장소는 보존한다.
Release 패키징·교체·실행 결과는 후속 작업 완료 보고에서 별도로 확인한다.
