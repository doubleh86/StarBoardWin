# 코드 스타일

## 목적

이 문서는 Starboard for Windows의 C#, XAML 및 테스트 코드에 적용하는 공통
규칙이다. `C:/Work/TRK-Server/Docs/CodeStyle.md`의 명시성과 작은 논리적 변경
원칙을 가져오되, WPF·Win32·ConPTY 데스크톱 앱에 맞게 조정했다.

프로젝트 또는 하위 디렉터리에 더 구체적인 규칙이 생기면 루트 `AGENTS.md`의
제품 제약과 module 경계 안에서 적용하고, 명시되지 않은 부분은 이 문서를 따른다.
지침 충돌과 확인이 필요한 경우의 처리는 `docs/development-workflow.md`를 따른다.

## 기본 원칙

- 최신 문법 자체보다 의도가 바로 읽히는 코드를 우선한다.
- 프로젝트에서 이미 쓰는 패턴이거나 중복 제거와 가독성 효과가 분명할 때만
  새로운 C# 문법을 도입한다.
- 요청과 직접 관련된 코드만 수정한다. 인접 코드 전체를 포맷하거나 임의로
  재배치하지 않는다.
- 디렉터리와 namespace를 일치시킨다.
- nullable reference types를 활성화하고 경고를 정상적인 코드로 해결한다.
- SDK analyzer와 `.editorconfig`를 코드 리뷰의 최소 자동 기준으로 사용한다.

## 이름과 파일 배치

- namespace는 file-scoped 형식을 기본으로 한다.
- 타입, public/internal 멤버, property, event는 `PascalCase`를 사용한다.
- 매개변수와 지역 변수는 `camelCase`를 사용한다.
- private instance field는 `_camelCase`를 사용한다.
- private constant는 TRK 코드 스타일과 동일하게 `_PascalCase`를 사용한다.
- interface는 `I` 접두사를 사용한다.
- 실제 비동기 작업을 반환하는 메서드만 `Async` 접미사를 사용한다. 이름만
  `Async`이고 동기 실행인 메서드를 만들지 않는다.
- 한 파일에는 주 책임 타입 하나를 둔다. 같은 구현에서만 사용하는 작은 보조
  모델을 같은 파일에 둘 경우 소비 클래스 내부에 중첩하지 말고 namespace 뒤,
  소비 클래스 앞에 최상위 타입으로 선언한다.
- WPF의 code-behind partial class와 생성 코드가 요구하는 구조는 예외다.
- `CommonHelper`, `Utils`처럼 책임이 넓은 새 이름을 피한다. 재사용 순수 함수가
  필요하면 `TaskbarGeometryCalculator`처럼 목적이 드러나는 이름을 사용한다.

## C# 문법

- `var`는 우변에서 타입이 명확하거나 타입명이 반복될 때 사용한다. 타입이
  동작 이해에 중요하면 명시한다.
- 소량의 단순 요소를 메서드 인자로 넘길 때 collection expression(`[...]`)을
  사용할 수 있다. 요소가 많거나 조건·계산이 섞이면 의미 있는 지역 변수로
  분리한다.
- 마지막 요소 접근에는 `^1`보다 명시적인 인덱스를 사용한다.

```csharp
return values[values.Count - 1];
```

- 축약 문법이 오류 경계나 소유권을 숨기면 풀어서 작성한다. 특히 native handle,
  process lifetime, cancellation 흐름은 명시적으로 보이게 한다.

## 메서드 호출과 선언 정렬

- 짧고 읽기 쉬운 호출과 선언은 한 줄로 작성한다.
- 여러 줄로 나눌 때 첫 번째 인자 또는 매개변수는 메서드명과 같은 줄에 둔다.
- 후속 줄의 첫 글자는 첫 번째 인자 또는 매개변수의 첫 글자와 같은 열에 둔다.
- 닫는 괄호는 마지막 인자 또는 매개변수 뒤에 둔다.
- 같은 줄에 자연스럽게 들어갈 내용을 기계적으로 한 줄에 하나씩 내리지 않는다.
- 메서드 인자인 여러 줄 객체 초기화는 `new`의 `n` 열에 중괄호와 다음 인자를
  맞추고, 초기화 멤버는 4칸 더 들여쓴다.

```csharp
await session.ResizeAsync(columns, rows,
                          cancellationToken);

private async Task StartShellAsync(ShellLaunchOptions options,
                                   CancellationToken cancellationToken)
{
}

await ExecuteAsync(new TerminalSize
                   {
                       Columns = columns,
                       Rows = rows
                   },
                   cancellationToken);
```

## 조건문과 제어 흐름

- boolean 반환 메서드와 boolean 변수는 명시 비교를 사용한다.

```csharp
if (windowState.IsExpanded == true)
{
}

if (TryResolveShell(out var shell) == false)
{
}

if (string.IsNullOrWhiteSpace(value) == true)
{
}
```

- 범위 조건은 relational pattern보다 명시적인 비교와 `||`/`&&`를 사용한다.

```csharp
if (columns < minimumColumns || columns > maximumColumns)
{
}
```

- `if`, `else`, `foreach`, `for`, `while`, `using` 본문은 한 줄이어도 중괄호를
  작성한다.
- 특정 케이스를 처리한 뒤 반환할 수 있으면 early return을 사용하고 불필요한
  `else`를 두지 않는다.
- 단순 방어 조건은 긴 `||` 하나로 합치기보다 오류 의미가 다르면 조건별로
  나눈다.
- 최종 `return` 앞에 실행 코드가 두 줄 이상 있으면 한 줄을 띄운다.

```csharp
if (requestedHeight <= 0)
{
    return defaultHeight;
}

var scaledHeight = requestedHeight * dpiScale;
var clampedHeight = Math.Min(scaledHeight, workAreaHeight);

return clampedHeight;
```

## 클래스와 helper

- 상태 또는 실행 흐름을 가진 service, host, view model, controller 클래스에 단지
  호출 편의를 위한 새 static 메서드를 만들지 않는다.
- 한 클래스 안에서만 쓰는 보조 로직은 private instance 메서드로 둔다.
- 여러 곳에서 재사용하는 순수 함수만 책임이 분명한 `*Calculator`, `*Parser`,
  `*Resolver`, `*Helper` 타입의 static 메서드로 분리한다.
- 추상화는 실제 플랫폼 경계, 수명 경계, 교체 가능한 구현 또는 테스트 seam이
  있을 때만 추가한다.

## 비동기와 수명

- UI thread에서 동기 process I/O, pipe read/write, 대기 또는 긴 native 호출을
  실행하지 않는다.
- background loop에는 `CancellationToken`을 전달하고 종료 시간을 제한한다.
- fire-and-forget 작업은 금지한다. 불가피한 UI event 진입점은 예외를 관찰하고
  소유 객체가 수명을 추적해야 한다.
- `Process`, pipe, stream, pseudoconsole, native handle, event subscription,
  timer, WebView2 resource를 소유한 타입은 해제 책임을 명확히 가진다.
- native handle은 가능한 경우 `SafeHandle` 파생 타입으로 감싼다.
- async dispose가 필요한 객체는 `IAsyncDisposable`을 구현한다.
- 현재 시각에 의존하는 정책은 직접 `DateTime.Now`를 흩뿌리지 말고
  `TimeProvider`를 주입해 테스트 가능하게 한다.

## 오류 처리와 로깅

- native 호출의 반환값을 확인하고 `Marshal.GetLastWin32Error()` 등 원래 오류
  정보를 잃지 않는다.
- 예상되는 실패를 빈 모델, 기본값 또는 성공 상태로 숨기지 않는다.
- 복구 가능한 renderer/shell/platform 실패는 상태로 전환하고 사용자가 재시작할
  수 있게 한다.
- 빈 `catch`를 사용하지 않는다. best-effort 정리나 최후의 오류 UI 경계에서도
  사용할 수 있는 로컬 진단 또는 실패 상태로 오류를 관찰 가능하게 처리한다.
  예외를 다시 전달할 수 없는 이유와 복구 정책은 주석으로 남긴다.
- 예외를 변환할 때 원래 예외를 inner exception으로 유지한다.
- terminal command와 terminal output은 기본 로그에 기록하지 않는다.
- 로그에는 subsystem, operation, recoverability처럼 문제 해결에 필요한 문맥을
  넣되 비밀 정보와 사용자 입력은 넣지 않는다.

## Win32 interop

- desktop/window P/Invoke 선언, constant, struct, enum과 native handle은
  `src/Modules/Starboard.Modules.DesktopIntegration/Infrastructure/Interop`에 둔다.
  ConPTY interop는 `src/Modules/Starboard.Modules.Terminal/Infrastructure/Interop`에
  둔다. host와 view에는 Win32 구현을 두지 않는다.
- native struct layout, character set, calling convention, ownership을 명시한다.
- HWND와 좌표가 physical pixel인지 WPF DIP인지 이름 또는 타입에서 구분한다.
- message handler에서는 최소한의 상태 수집만 하고 무거운 작업은 서비스로
  넘긴다.
- 문서화되지 않은 API를 일반 interop 코드와 섞지 않는다.

## XAML과 UI

- 화면 동작, terminal/session lifetime, platform policy를 code-behind에 넣지
  않는다. code-behind는 WPF 수명 이벤트와 native hook 연결처럼 view에 묶인
  작업으로 제한한다.
- 색상, font, padding, opacity는 resource 또는 theme model에서 가져온다.
- 고정 pixel 값은 제품 의도가 있는 최소 크기 등에만 사용하고 DPI 변환 책임을
  명시한다.
- element 이름은 역할이 드러나는 `PascalCase`를 사용한다.
- focus 관련 동작은 XAML 속성 우연에 기대지 말고 정책과 테스트 항목으로 남긴다.

## 테스트

- 기능 변경이나 버그 수정은 기존 테스트로 해당 동작과 회귀 위험을 검증할 수
  있는지 먼저 확인하고, 부족한 동작·경계 조건에 테스트를 추가하거나 갱신한다.
  낮은 영향의 변경에 구현을 그대로 재현하는 테스트를 추가하지 않는다.
- 실행할 검증 범위와 종료 기준은 `docs/development-workflow.md`를 따른다.
- test 이름은 `Method_Scenario_ExpectedResult` 형식을 기본으로 한다.
- geometry, state transition, shell resolution, settings migration은 platform 없이
  실행 가능한 순수 테스트로 만든다.
- 실제 Windows message, ConPTY, WebView2가 필요한 검증은 integration/manual로
  명확히 구분한다.
- 변경 감지를 위한 기대값에는 production constant를 그대로 참조하지 말고 의미
  있는 literal을 사용할 수 있다.

## 변경 후 정리

- 이번 변경으로 새로 미사용 상태가 된 private helper, field, using, test fixture는
  같은 작업에서 제거한다.
- 기존에 이미 있던 미사용 코드나 public API는 임의로 삭제하지 않는다.
- 관련 없는 줄바꿈, using 정렬, 이름 변경을 변경 묶음에 섞지 않는다.
