# 진단 로그 회전과 개인정보 보호

## 목표와 완료 조건

로컬 진단 로그의 보관 크기를 제한하고, 로그 쓰기 실패가 앱 수명에 영향을 주지 않게 한다. command, terminal output, prompt, 경로와 임의 사용자 문자열은 로그 또는 portable package에 남지 않아야 한다.

## 범위와 영향 파일

- `src/Starboard.Windows/Infrastructure/FileDiagnosticLog.cs`: bounded, failure-isolated diagnostic sink
- `tests/Starboard.IntegrationTests`: rotation, concurrent write, privacy 회귀 검사
- `scripts/package-portable.ps1`: portable content deny-list 보강
- `docs/architecture.md`, `docs/test-plan.md`, `README.md`: 실제 retention/privacy 계약

## 판단과 위험

기존 `IDiagnosticLog`의 message는 호출부의 고정 상태 설명에 쓰이지만, 실수로 사용자 문자열을 넘겨도 file sink가 이를 영속화하지 않는다. 파일에는 허용 문자·길이의 subsystem/operation, level, native error code만 기록한다. 파일 잠금·I/O·회전 실패는 모두 best-effort로 무시한다. retention은 active log 512 KiB와 `.1`~`.4` 보관본(최대 2.5 MiB)이다.

## 구현 단계

- [x] 현재 diagnostic 및 portable 검사 구조 확인
- [x] bounded privacy-preserving file sink와 focused tests 구현
- [x] portable deny-list 및 문서 갱신
- [x] restore/build/test/package 검증

## 검증 방법

- Integration test로 개인정보 차단, 회전·보관 수, 병렬 write와 lock/corrupt file failure isolation 확인
- 지정된 Debug restore/build/test와 portable packaging 수행

## 진행 기록

- 2026-09-15: 구현 시작. 기존 file sink가 message를 그대로 append하는 것을 확인했다.
- 2026-09-15: 지정 SDK Debug restore/build와 전체 516 test가 통과했다. portable Release build/test(516 test), publish, content deny-list, deterministic archive/hash와 extracted smoke가 통과했다.

## 완료 요약

`FileDiagnosticLog`는 message/exception 본문을 버리고 제한된 metadata만 기록한다. active 512 KiB log와 4개 archive로 보관 상한을 2.5 MiB로 정했고 I/O·lock·rotation failures를 application path에서 격리했다. portable 검사는 rotating log, WebView2 data directory와 database file까지 명시적으로 거부한다. package ZIP SHA-256은 `e62cf4669adb4dc69e1957dd056f0575e24527e8132070f819e382f6e71d5435`다.
