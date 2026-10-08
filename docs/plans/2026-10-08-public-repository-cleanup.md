# 공개 저장소 코드·개발 흔적 정리

## 목표와 완료 조건

기존 터미널·창 동작과 모듈 경계를 유지하면서 공개 저장소에 불필요한 개발 기록,
중복 코드와 배포 고지 누락을 정리한다. 코드·배포 검증을 통과한 변경만 main에
커밋·푸시한다. 바탕화면 앱 교체나 Git 이력 재작성은 하지 않는다.

## 범위와 영향 파일

- `.hallmark/*.json`: 앞선 요청에서 삭제한 초기 디자인 점검 기록을 반영한다.
- `.gitignore`: 로컬 `.ai-orchestrator/`, `.hallmark/`의 재등록을 방지한다.
- `tokens.css`, renderer `src/styles.css`, `build.mjs`, `dist/`: 점검 점수·생성 도구
  주석만 제거하고 실제 사용되는 테마 토큰과 third-party 저작권 고지는 유지한다.
- renderer `src/index.ts`: 저장한 탭 UI의 한 줄에 몰린 선언·대입·이벤트 처리를
  기존 스타일에 맞춰 구분한다. 실행 순서와 로직은 바꾸지 않는다.
- `scripts/package-portable.ps1`: 제품 MIT 본문을 중복 생성하지 않고 루트
  `LICENSE`를 복사한다. 검색·링크 애드온 라이선스의 포함을 검사한다.
- `THIRD-PARTY-NOTICES.md`, renderer asset test: 실제 사용하는 xterm 애드온의
  고지와 파일 포함을 확인한다.
- C# 미사용 멤버·using은 analyzer 근거와 실제 호출·XAML·interop 사용 여부가
  확인된 경우에만 제거한다. 전체 포맷 변경이나 책임 재배치는 하지 않는다.
- 아키텍처·관련 과거 계획의 점검 주석과 삭제된 파일 표기, 테스트 결과를 갱신한다.

## 판단과 위험

- 루트 `tokens.css`는 renderer가 직접 import하는 빌드 입력이므로 삭제하지 않는다.
- 로컬 오케스트레이터 파일은 읽거나 삭제·커밋하지 않는다. ignore만 추가한다.
- 생성된 renderer bundle은 소스를 수정한 뒤 기존 npm 빌드로 재생성한다.
  동작 코드와 third-party license comment는 수정하지 않는다.
- 저장소 파일 점검은 과거 커밋 전체의 비밀 정보 감사를 대신하지 않는다.
- 멀티모니터·DPI·IME 등 실제 장비 검증을 자동 검사 결과로 대체하지 않는다.

## 구현 단계

- [x] Git 상태, 사용 중인 파일과 개발 메타데이터, 라이선스 누락을 확인했다.
- [x] 정적 검사 근거에 따라 불필요한 코드·주석·중복과 배포 고지를 정리했다.
- [x] renderer 재생성, 관련 테스트와 solution 전체 검증, 경량 배포 검증을 마쳤다.

Git 전달은 사용자 요청에 따라 main 커밋·푸시로 진행하며 원격 SHA 확인 결과는
최종 작업 보고에 남긴다.

## 검증 방법

- C# 미사용 멤버/using analyzer 및 기존 정렬 검사로 삭제·형식 변경 후보를 확인한다.
- renderer 빌드, 기존 Node 정책 테스트, asset/license 회귀 테스트를 수행한다.
- solution Debug restore/build/test를 순서대로 수행한다.
- 경량 portable 생성 명령으로 Release restore/build/test/publish, license 포함,
  ZIP·SHA-256·추출 smoke를 검증한다. 포함판 분기는 변경 영향에 따라 검증한다.
- `git diff --check`, 실제 runtime 코드·테마 변화 여부, 로컬 기록 제외와 원격 동기화를 확인한다.

## 진행 기록

- 2026-10-08: 사용자 요청으로 공개 저장소 정리를 시작했다. 변경 전 main은
  `2bdcee4`이며 기존 변경은 `.hallmark` JSON 두 개 삭제와 로컬 오케스트레이터
  미추적 디렉터리뿐이다.
- C# analyzer의 미사용 멤버·using 결과는 0건이다. 신규 asset test 이름은 SDK의
  CA1707과 기존 테스트에 맞춰 PascalCase를 사용하며 analyzer를 완화하지 않는다.

## 완료 요약

- 초기 `.hallmark` JSON 두 개와 source·CSS 생성물의 디자인 점검 주석을 제거했다.
  과거 계획에는 삭제 시점을 표시하고 실제 테마 토큰은 유지했다.
- 저장한 탭 UI의 압축된 선언·이벤트 처리를 정리했다. renderer 재빌드의 JavaScript와
  CSS 규칙 비교로 동작 코드·색상·레이아웃이 바뀌지 않았음을 확인했다.
- 제품 라이선스 중복 생성을 루트 파일 복사로 통일했다. search 애드온의 라이선스와
  search/web-links 고지를 보완하고 두 애드온의 배포 필수 파일 검사, 4개 라이선스
  회귀 사례를 추가했다. 라이선스 내용이나 사용 중인 의존성은 변경하지 않았다.
- C# 미사용 analyzer 결과 0건, 정렬 self-test/WorkingTree 통과다. 안전하게 삭제할
  기존 C# 멤버가 확인되지 않아 제품 C# 구현은 그대로 유지했다.
- Node 테스트 3개, 관련 renderer 테스트 22개, Debug·Release 전체 테스트 각 549개가
  실패·skip 없이 통과했다. 두 build의 경고·오류는 0개다.
- 기본 경량 ZIP의 재현성·checksum·추출 smoke와 원본/배포/추출 라이선스 SHA-256
  일치를 확인했다. 검증 기준은 `2bdcee4` 위의 미커밋 작업 트리이며 ZIP SHA-256은
  `515724c3fd7103e47658787686c76e5c840530dac8f37746757be45ea1aab67c`다.
- 포함판 모드 분기는 변경하지 않아 이번에는 경량판만 검증했다. 바탕화면 교체,
  수동 UI·장비 검증과 Git 이력 재작성은 하지 않았다. 로컬 오케스트레이터 파일은
  보존했으며 ignore로 공개 대상에서 제외했다.
