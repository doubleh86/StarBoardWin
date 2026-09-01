# 개발 작업 규칙

## 목적

이 문서는 Starboard for Windows의 구현, 문서, 검증, Git 작업 흐름을 정의한다.
`C:/Work/TRK-Server`의 작업 범위 축소와 plan-first 원칙을 가져오되, 서버·DB
운영 규칙은 포함하지 않는다.

## 작업 시작 순서

1. 루트 `AGENTS.md`를 읽는다.
2. 현재 작업과 직접 관련된 파일과 문서를 `rg`로 찾는다.
3. Git 저장소라면 branch, worktree, 변경 상태를 확인한다.
4. 새 기능, 구조 변경, dependency 추가 또는 범위 있는 refactor라면
   `docs/plans/`의 기존 계획을 갱신하거나 새 계획을 작성한다.
5. 가장 작은 실행 가능한 vertical slice부터 구현하고 검증한다.

전체 코드와 모든 문서를 먼저 읽지 않는다. 관련 문서 사이 또는 문서와 코드
사이에 충돌이 있으면 임의로 하나를 선택하지 말고 계획에 차이와 판단 근거를
기록한다.

## 변경 범위

- 수정 줄 수가 아니라 요구사항을 완결하는 가장 작은 논리적 범위를 기준으로
  한다.
- 직접 영향받는 declaration, caller, contract, setting, test, documentation은
  함께 확인한다.
- 요청 범위 밖 기능, 미관 개선, 대규모 formatting, dependency 교체, 파일 이동을
  끼워 넣지 않는다.
- 작업과 관계없는 사용자 변경은 유지한다. 다른 변경을 reset, stash, 삭제 또는
  덮어쓰지 않는다.
- 관련 없는 기존 미사용 코드는 발견 사실만 보고한다.

## Plan-first

다음 작업은 제품 코드보다 계획 문서를 먼저 작성하거나 갱신한다.

- 새 사용자 기능
- Win32/ConPTY/WebView2 등 플랫폼 구조 추가 또는 변경
- dependency 추가·교체·제거
- 설정 schema나 저장 위치 변경
- 여러 계층 또는 여러 프로젝트에 걸친 refactor
- 기존 계획의 범위 확장

계획 문서는 `docs/plans/YYYY-MM-DD-topic.md` 형식으로 작성하며
`docs/plans/README.md`의 항목을 따른다. 작업 중 범위가 바뀌면 구현을 계속하기
전에 계획의 범위, 영향 파일, 위험 및 검증 방법을 먼저 갱신한다.

단순 문서 오탈자, 상태 확인, 이미 계획된 작업의 작은 후속 수정, build/test
실행만으로 끝나는 작업은 별도 계획 문서가 없어도 된다.

## 구조 전환 사전 확인

다음 상황에서는 구현을 밀어붙이지 않고 기존 계획을 갱신한 뒤 사용자에게
두 선택지와 영향 범위를 보고한다.

- 현재 구조를 유지하기 위한 compatibility 코드가 새 구현보다 커지는 경우
- 둘 이상의 외부 계약 또는 renderer/platform boundary를 함께 바꿔야 하는 경우
- 기존 구조 수정 비용이 새 adapter 또는 V2 구현보다 클 것으로 예상되는 경우
- 제품 UX 원칙과 기술적 제약이 직접 충돌하는 경우

보고에는 기존 구조 유지안과 새 구현안의 범위, migration/호환 부담, 위험,
검증 비용을 포함한다.

## 구현 흐름

- 각 phase가 끝날 때 solution을 build 가능한 상태로 유지한다.
- 먼저 pure logic과 interface boundary를 만들고, 실제 native integration은 얇게
  연결한다.
- 실패 시 같은 명령이나 같은 수정을 반복하지 말고 원인을 좁힌다.
- dependency를 추가하기 전에 platform 제공 기능과 이미 선택된 dependency로
  해결 가능한지 확인한다.
- runtime network가 필요한 dependency 또는 CDN asset은 사용하지 않는다.
- architecture와 실제 구현이 달라지면 같은 작업에서 문서를 갱신한다.

## 검증

변경 범위에 맞는 가장 작은 검증부터 수행한다.

1. 관련 unit test 또는 test filter
2. 변경한 project build
3. solution build와 전체 automated test
4. 필요한 integration/manual test

이 프로젝트를 완성해 달라는 사용자 요청에는 로컬 restore, build, test 실행이
포함된 것으로 본다. 관리자 권한, 외부 서비스, 설치, 시스템 설정 변경 또는
destructive data 변경이 필요한 검증은 별도 승인을 받는다.

검증 보고는 다음을 구분한다.

- 자동 테스트 통과
- 로컬 integration 검증 통과
- simulation 또는 mock으로만 확인
- 실제 hardware/display 환경에서 수동 확인 필요
- 환경 제약으로 미수행

실제로 수행하지 않은 멀티모니터, DPI, fullscreen, IME 검증을 완료로 보고하지
않는다.

## Git과 worktree

- 현재 디렉터리가 Git 저장소인지 먼저 확인한다. 저장소가 아니라면 branch나
  commit이 있다고 가정하지 않는다.
- 동시에 여러 write 작업을 수행하게 되면 작업별 branch/worktree와 파일
  ownership을 분리한다. 겹치는 파일을 동시에 수정하지 않는다.
- 다른 작업자의 dirty change를 이동, reset, stash, 삭제하지 않는다.
- 사용자가 요청하지 않으면 branch 생성, commit, push, merge를 자동으로 하지
  않는다.
- commit을 요청받으면 서로 관계없는 변경을 한 commit에 섞지 않는다.
- TRK 형식을 적용하는 commit message는 다음을 사용한다.

```text
[클라이언트, 실제 작성자] - 작업 내용
[문서, 실제 작성자] - 작업 내용
```

- 작성자에는 `AI`, `Codex` 같은 도구명을 사용하지 않는다. 실제 작성자 이름을
  알 수 없으면 첫 commit 전에 확인한다.
- commit 전에 `git diff --check`와 변경 파일 목록을 확인한다.

## 생성물과 로컬 파일

다음 파일은 source로 관리하지 않는다.

- `bin/`, `obj/`, `out/`, `artifacts/`, `TestResults/`
- `.vs/`, `.idea/`, 개인 `.vscode/` 설정
- `*.user`, `*.suo`, `*.log`
- WebView2 user data와 crash dump
- 로컬 shell path override와 machine-specific settings
- secret, token, credential, private environment file

bundled xterm.js처럼 제품이 runtime에 필요로 하는 검토된 정적 asset은 생성물이
아니며 source 또는 재현 가능한 dependency restore 대상으로 관리한다.

## 완료 보고

완료 시 다음을 짧게 정리한다.

- 변경한 동작과 주요 파일
- 선택한 구조와 이유
- 실행한 build/test와 결과
- 수행하지 못한 검증
- 알려진 제약과 다음 수정 위치
- 사용자의 실제 환경 확인이 필요한 항목
