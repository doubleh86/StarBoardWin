# TRK CodeStyle 동기화와 C# 스타일 정리

## 목표와 완료 조건

`C:/Work/TRK-Server/Docs/CodeStyle.md`의 최신 공통 규칙을 저장소 정본에 반영하고,
직접 관리하는 C# 코드를 동작 변경 없이 정리한다. 전체 build/test와 diff 검증을 통과한다.

## 범위와 영향 파일

- `docs/code-style.md`: 원문 출처·적용/제외 기준, 120열, 인자 묶음/정렬, 단락 공백,
  중첩 await 금지와 예외 메시지 배치 보강.
- `.editorconfig`: IDE에서 표현 가능한 줄 길이·정렬 규칙 반영.
- `scripts/Test-CSharpAlignment.ps1`: TRK의 기존 정렬 검사기를 가져와 `-WorkingTree`
  읽기 전용 검사만 추가한다. Git hook 설치나 build dependency 추가 없이 직접 실행한다.
- `src/**/*.cs`, `tests/**/*.cs`: 생성물 제외. 인자/파라미터 정렬, 조건식과 단락 공백,
  테스트의 중첩 await를 지역 변수로 분리한다.
- 파일·namespace·기존 멤버 이름·공개 계약, 기존 static/helper 구조는 유지한다.
  서버 전용 TimeZoneHelper, DB/패킷/게임 데이터 규칙과 Git hook은 도입하지 않는다.
- XAML 및 offline third-party renderer asset은 원문 C# 스타일 적용 대상이 아니므로 유지한다.

## 판단과 위험

- 원문은 2026-09-09에 확인했다. SHA-256:
  `9245426EEFE150BAC859643BDCE9A972A33EAAA9F4C6A9A377F8BB94EF89AAD4`.
- 일괄 정규식으로 문자열·주석·실행 순서를 바꾸지 않는다. Roslyn syntax/token 기반의
  임시 formatting 도구를 `out/`에서 사용하고 문자열/토큰 동일성을 검사한다.
- 중첩 await 분리는 평가 순서가 보존되는 테스트 단정문만 수동 수정한다.
- JSON raw string fixture 1곳은 내용/들여쓰기 토큰을 보존한 채 지역 상수로 분리해
  호출 인자에 긴 literal을 직접 중첩하지 않는다.
- 첫 build에서 Roslyn IDE0055가 TRK의 `new` 열 기준 initializer 정렬을 거부했다.
  이 진단은 C# 언어 정확성이 아니라 기본 formatter와의 공백 차이 검사이므로 비활성화하고,
  원본 TRK 정렬 검사기와 diff/token 검사를 사용한다. 나머지 analyzer와 warnings-as-errors는 유지한다.
- 기존 private naming 차이나 타입 이동·추상화 변경은 스타일 작업에 섞지 않는다.
- 실행 중인 바탕화면 앱, `.ai-orchestrator/`, 원본 TRK 저장소를 수정하지 않는다.
  commit/push 또는 재배포는 이번 요청에 포함하지 않는다.

## 구현 단계

- [x] 원문과 현재 규약 비교 및 범위 결정
- [x] 저장소 규약과 IDE 설정 갱신
- [x] C# 스타일 정리 및 token/diff 검사
- [x] 전체 Debug build/test, 완료 기록

## 검증 방법

원문 공통 규칙과 변경 diff를 대조하고 공백 변경은 Roslyn token 동일성을 확인한다.
중첩 await 분리는 해당 테스트와 전체 suite로 검증한다. 같은 formatting 변환을 다시
적용해 추가 변경이 없는지도 확인한다. 실제 UI 동작이나 배포 검증은 수행하지 않는다.

## 완료 요약

- 직접 관리하는 C# 128개 파일을 점검하고 98개 파일을 정리했다. production의 실행
  token/구문은 변경하지 않았다. Git LF와 Windows CRLF 차이를 정규화한 HEAD 비교에서
  126개 파일의 token/구문이 같았고, 나머지 2개는 테스트의 중첩 await 4곳과 raw JSON
  fixture 1곳을 지역 변수/상수로 분리한 변경만 포함한다.
- 인자·파라미터 묶음/정렬, 객체·record initializer 정렬, 조건식 정렬, return/throw
  앞 단락 공백을 맞췄다. 같은 변환을 다시 실행했을 때 추가 변경은 0개였다.
- TRK 정렬 검사기 `-SelfTest`, `-WorkingTree` 및 `git diff --check` 통과.
  SDK 10.0.301로 solution restore와 Debug build 경고 0개·오류 0개,
  전체 188개 test 통과. TRX는 `out/code-style-tests/`에 보관한다.
- IDE0055만 TRK initializer 규칙과 충돌하여 비활성화했다. 그 외 analyzer,
  warnings-as-errors와 modular-monolith architecture test는 유지했다.
- 명명 변경, 타입 이동, 기존 static 구조와 time provider 교체는 하지 않았다.
  원본 TRK 저장소, 실행 중인 앱과 `.ai-orchestrator/`는 보존했다.
- 스타일 작업이므로 Release 재배포나 UI/hardware 수동 검증은 수행하지 않았다.
  commit/push도 별도 요청 전에는 수행하지 않는다.
