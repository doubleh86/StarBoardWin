# Starboard for Windows

Starboard는 Windows 작업표시줄 바로 위에 계속 머무는 작은 terminal panel이다.
호출할 때만 나타나는 drop-down terminal과 달리, 탭별 실제 shell session을 앱
수명 동안 유지하면서 현재 사용 중인 창의 focus를 불필요하게 빼앗지 않는 것을
목표로 한다.

현재 구현은 v0.1 portable release 기준이다. Windows 11 x64를 우선 지원하며
Windows 10 1809 이상은 best-effort 대상이다.

## 현재 동작

- 축소된 하단 panel은 작업표시줄 안쪽 경계에 외부 간격 없이 밀착하고, terminal 본문 안쪽의 6 DIP 하단 여백으로 입력 줄을 보호
- `pwsh.exe` → `powershell.exe` → `cmd.exe` 순서의 shell 탐색
- Windows ConPTY를 통한 실제 양방향 persistent session
- bundled xterm.js와 local-only WebView2 renderer
- 하나의 WebView2 안에서 탭별 xterm, scrollback과 shell 상태 유지
- 최대 8개 탭, 탭별 독립 ConPTY process·working directory·interactive state와 이름·순서 변경
- `저장한 탭` 메뉴에서 이름·시작 폴더·셸을 최대 20개 저장하고 새 독립 탭으로 실행
- terminal resize를 ConPTY cell size로 전달
- shell/renderer 오류 surface와 shell restart
- 실행 중인 탭 닫기는 기본적으로 확인하며, 여러 줄 clipboard 붙여넣기는 확인한 동일 미리보기만 전달
- 비활성 탭의 non-empty output은 조용한 `새 출력` 점으로만 표시
- `Ctrl+Alt+E` global hotkey로 work area 전체 확장/축소
- `Ctrl+Alt+S`로 가려졌거나 숨겨진 panel 호출, 활성 panel 숨김
- 32 DIP 탭 바 아래 terminal 본문 약 8행이 보이는 200 DIP 기본 높이
- notification area icon 왼쪽 클릭으로 panel 표시·활성화
- tray menu의 `터미널 표시/숨기기`와 `종료`
- tray의 `단축키 안내`에서 현재 적용·등록된 전역 키와 terminal 입력 규칙 확인
- 평소에는 다른 앱을 덮어두지 않는 normal z-order, tray 표시 요청 때만 활성화
- selection-aware `Ctrl+C`, `Ctrl+Shift+C` 복사와 `Ctrl+V`, `Ctrl+Shift+V` 붙여넣기
- single instance와 1초 taskbar geometry reconciliation
- tray에서 여는 설정 창, 사용자별 JSON 설정과 Dark, Light, One Dark, Tokyo Night theme
- 설정 화면의 제품 버전과 package build commit 표시

실제 multi-monitor/mixed-DPI, taskbar auto-hide, fullscreen, IME와 로그인 자동 시작
장비 검증은 아직 남아 있다. 구현 범위와 미수행 matrix는
[`docs/test-plan.md`](docs/test-plan.md)를 참고한다.

`설정 > 작업공간 복원`은 기본적으로 꺼져 있다. 켜면 다음 시작에 탭 이름·순서,
선택된 탭, 시작 폴더와 기본 shell 종류를 새 ConPTY process로 복원한다. 실행 중인
shell, PID, 현재 working directory, history, command, output, scrollback, clipboard와
environment는 저장하거나 복원하지 않는다. 복원 탭은 항상 새 PID를 얻으며, 폴더나
shell 시작 실패는 해당 탭만 오류 상태로 남기고 나머지 탭 복원을 계속한다.

옵션을 끄고 설정 저장에 성공하면 저장된 작업공간을 삭제하고 다음 시작에는 기본 탭
하나로 시작한다. 비활성 탭은 DOM에서 제거하지 않아 10,000줄 xterm scrollback과
shell 상태를 유지하지만, renderer process 자체가 재시작되면 과거 scrollback은
복원하지 않고 살아 있는 ConPTY의 이후 output과 현재 탭 snapshot만 다시 연결한다.
실제 mixed-DPI, 한글 IME와 작업공간 재시작 UI smoke는 아직 수동 검증이 필요하다.

탭의 우클릭 또는 `Shift+F10` 메뉴에서 `저장한 탭에 추가…`를 선택하면 현재 탭의
이름·설정된 시작 폴더·셸 종류를 편집해 저장할 수 있다. `+` 옆의 `저장한 탭` 메뉴에서
항목을 선택하면 기존 탭을 바꾸지 않고 지정 폴더에서 새 셸을 시작한다. `저장한 탭
관리…`에서는 추가·편집·삭제할 수 있으며, 저장 실패 시 메모리 목록도 바뀌지 않아
편집 화면을 다시 열어 재시도할 수 있다. 실행 중 탭은 계속 최대 8개다.

저장 목록은 작업공간 복원 옵션과 별개로 항상 `%LOCALAPPDATA%/Starboard/saved-tabs.json`에
유지된다. 작업공간 복원을 꺼도 저장 목록은 삭제되지 않고, 저장 항목은 PID, 입력 상태,
현재 shell working directory, history, output 또는 scrollback을 보관하지 않는다. 따라서
저장 항목을 열 때마다 새 PID와 빈 interactive state를 얻는다. 현재 탭을 저장할 때 폴더는
shell prompt의 실시간 현재 폴더가 아니라 탭에 설정된 시작 폴더이므로 필요하면 저장 창에서
경로를 고쳐야 한다.

## 요구 사항

- Windows 11 x64 권장
- .NET 10 SDK `10.0.301` 이상(빌드 시)
- Microsoft Edge WebView2 Evergreen Runtime
- PowerShell 7 권장; 없으면 Windows PowerShell 또는 `cmd.exe` 사용

일반 실행에 관리자 권한은 필요하지 않는다.

## 빌드와 실행

```powershell
dotnet restore Starboard.Windows.sln
dotnet build Starboard.Windows.sln --configuration Debug --no-restore
dotnet test Starboard.Windows.sln --configuration Debug --no-build --no-restore
dotnet run --project src/Starboard.Windows/Starboard.Windows.csproj
```

## Portable 배포 만들기

다음 한 명령은 build server를 정상 종료한 뒤 restore, Release build/test,
self-contained `win-x64` publish, ZIP과 SHA-256 생성, 추출 smoke를 순서대로 수행한다.

```powershell
powershell -NoProfile -File scripts/package-portable.ps1
```

Windows PowerShell 5.1과 PowerShell 7(`pwsh`)을 모두 지원한다. package 흐름은
Release restore도 수행하므로 NuGet package source와 vulnerability metadata source에
접근할 수 있어야 한다. 격리된 환경에서는 `api.nuget.org` 또는 조직 mirror 접근을
허용한 뒤 같은 명령을 다시 실행한다. runtime과 이미 생성된 portable package는
network를 요구하지 않는다. `Get-FileHash`가 없는 PowerShell host도 지원하며, 이 경우
스크립트가 .NET SHA-256 API로 같은 소문자 checksum을 생성한다. 별도 hash 도구나
network dependency는 필요하지 않다.

`dotnet`이 PATH에 없으면 절대 경로를 저장소에 기록하지 않고 실행 시에만 넘긴다.

```powershell
powershell -NoProfile -File scripts/package-portable.ps1 -DotNetPath '<dotnet.exe 경로>'
```

결과는 `out/portable/<version>/Starboard-<version>-win-x64.zip`과 같은 이름의
`.sha256` 파일이다. 스크립트는 Git이 확인한 저장소 루트 아래의 해당 버전
`staging`만 정리하며, reparse point나 범위를 벗어난 경로는 거부한다. ZIP은 실행
파일, local renderer, 제품 `LICENSE`, third-party notice와 release metadata를 포함하고
사용자 설정, 작업공간·저장한 탭 JSON과 backup/temporary 파일, 로그, WebView2 user data와
PDB는 거부한다. 같은 source commit과 SDK/
dependency 입력에서 파일 순서와 ZIP entry 시각을 고정해 다시 만들 수 있다.

portable package는 .NET Runtime을 포함하므로 별도 .NET 설치가 필요 없지만,
**Microsoft Edge WebView2 Evergreen Runtime은 별도 필수 요구사항**이다. WebView2가
없으면 Microsoft의 Evergreen Runtime을 설치한 뒤 다시 실행한다. 현재 앱 내부 오류
surface는 Runtime 시작 실패와 재시도만 안내하며 offline installer를 직접 포함하거나
실행하지 않는다.

## Portable 설치·업데이트·복귀

Starboard는 설치 프로그램 없이 버전별 새 폴더에 압축을 풀어 사용한다. 업데이트
전에 terminal의 foreground/background 작업을 모두 끝내고 shell을 종료한 다음 tray의
`종료`를 선택한다. 앱 process가 남아 있는 상태에서 파일을 덮어쓰지 않는다.

1. 새 ZIP을 이전 버전과 다른 새 폴더에 압축 해제한다.
2. `.sha256`의 값과 ZIP의 `Get-FileHash -Algorithm SHA256` 결과가 같은지 확인한다.
3. 새 폴더의 `Starboard.exe`를 실행하고 terminal과 설정 화면의 버전·build commit을
   `release-metadata.json`과 대조한다.
4. `%LOCALAPPDATA%/Starboard/settings.json`은 배포 폴더 밖에 있으므로 기존 사용자
   설정이 유지된다.
5. `로그인 시 자동 실행`이 켜져 있었다면 설정에서 한 번 끈 뒤 다시 켜 새 폴더의
   실행 경로로 갱신한다. 재로그인 전에 새 경로가 적용됐는지 확인한다.

문제가 생기면 새 앱을 tray에서 종료하고 이전 버전 폴더의 `Starboard.exe`를 다시
실행한다. 자동 실행을 사용하면 이전 버전 설정에서 껐다 켜 경로를 되돌린다. 사용자
설정 schema가 이전 버전에서 지원되지 않는 경우에는 `%LOCALAPPDATA%/Starboard`를
먼저 백업하고, 필요할 때 `settings.json.bak`을 복원한다. 두 버전 폴더는 복귀 확인이
끝날 때까지 유지한다.

2026-09-10 격리 검증에서는 서로 다른 build commit의 499-file 폴더로
`이전 → 현재 → 이전` smoke를 통과했고 기존 배포 PID를 종료하거나 폴더를 덮어쓰지
않았다. 다만 해당 계정에는 settings/workspace와 HKCU Run `Starboard` 값이 없었으므로
실제 설정 migration 및 활성화된 자동 실행 경로의 변경·복귀는 아직 수동 확인이
필요하다. 업데이트는 시작 프로그램 경로를 자동 이동하지 않으므로 위 5단계를
생략하면 로그인 시 이전 폴더가 계속 실행될 수 있다.

## Renderer 갱신

일반 build와 runtime에는 Node.js나 network가 필요 없다. xterm.js source 또는
package version을 바꿀 때만 다음 명령으로 committed `dist`를 다시 만든다.

```powershell
Set-Location src/Modules/Starboard.Modules.Terminal/Presentation/Renderer
npm ci
npm run build
```

renderer는 runtime CDN, 외부 font, remote script를 사용하지 않는다.
package의 local asset/CSP 계약은 자동 검증됐지만, system network를 끈 실제 WebView2
화면 실행은 아직 수동 검증 대상이다.

## 입력 규칙

| 입력 | 동작 |
|---|---|
| terminal click | Starboard를 의도적으로 활성화하고 IME/키보드 입력 허용 |
| tray icon 왼쪽 클릭 | shell session을 유지한 채 panel 표시·활성화 |
| tray icon 오른쪽 클릭 | `터미널 표시/숨기기`, `종료` menu 표시 |
| `Ctrl+C` | 선택이 있으면 복사, 없으면 shell에 ETX 전달 |
| `Ctrl+Shift+C` | 선택 text 복사 |
| `Ctrl+V`, `Ctrl+Shift+V` | Windows clipboard text 붙여넣기 |
| `Ctrl+Shift+T` | 새 terminal 탭 열기 |
| `Ctrl+Tab`, `Ctrl+Shift+Tab` | 다음/이전 terminal 탭 선택 |
| `Ctrl+Shift+W` | 현재 terminal 탭 닫기 요청; 살아 있는 session은 확인 뒤 종료 |
| `Ctrl+W` | shell에 그대로 전달 |
| `Ctrl+Alt+E` | collapsed/expanded geometry 전환 |
| `Ctrl+Alt+S` | 숨김·비활성 panel 호출, 활성 panel 숨김 |

`Ctrl+Alt+E` 또는 `Ctrl+Alt+S`가 다른 프로그램에 이미 등록돼 있으면 앱은 계속
실행되지만 해당 global shortcut은 사용할 수 없다. 충돌은 로컬 진단 로그에
기록한다. tray의 `단축키 안내`는 설정 문자열이 아니라 실제 적용·등록 상태를
표시한다.

여러 줄 붙여넣기는 CR 또는 LF를 포함한 경우에만 확인 창을 열며, 취소·Escape·창
닫기는 아무 입력도 전달하지 않는다. 확인 뒤에는 clipboard를 다시 읽지 않고 사용자가
확인한 snapshot만 한 번 전달한다. 새 출력 점은 완료·성공 알림이 아니며, 해당 탭을
선택하면 사라진다.

## 로컬 데이터와 개인정보

```text
%LOCALAPPDATA%/Starboard/
  settings.json
  settings.json.bak
  workspace.json                 # 복원 옵션을 켠 경우의 탭 구성만
  workspace.json.bak
  workspace.json.tmp             # 원자 저장 중에만 존재 가능
  saved-tabs.json                # 작업공간 복원 옵션과 독립적인 저장 탭 정의
  saved-tabs.json.tmp            # 원자 저장 중에만 존재 가능
  Logs/starboard.log
  WebView2/
```

Starboard에는 analytics, telemetry, crash upload, remote configuration이 없다.
작업공간과 저장한 탭 파일은 각각 최대 64 KiB의 일반 로컬 JSON이며 암호화되지 않는다. 작업공간에는 탭
구성 ID, 이름, 순서, 시작 폴더, shell 종류와 활성 탭만 들어간다. terminal command,
저장한 탭 파일에는 저장 ID, 이름, 시작 폴더와 shell 종류만 들어간다. terminal command,
output, clipboard 내용, environment 값, runtime PID/session ID는 로그나 두 구성
파일에 남기지 않는다. 닫기 확인 token, 여러 줄 붙여넣기 미리보기와 새 출력 표시는
메모리의 현재 session 세대에만 묶이며 disk·log·package에 저장하지 않는다. 로그는
subsystem, operation, 복구 가능성에 필요한 오류 종류만 기록한다. portable ZIP에는
이 사용자 데이터, backup, temporary 파일이 포함되지 않는다.

## 구조

Starboard는 단일 프로세스와 단일 배포 단위를 유지하는 모듈러 모놀리스다.

```text
Starboard.Windows
  ├─ Starboard.Modules.Terminal
  ├─ Starboard.Modules.DesktopIntegration
  ├─ Starboard.Modules.Preferences
  └─ Starboard.SharedKernel
```

기능 module끼리는 직접 참조하지 않는다. Host만 module을 조합하며 architecture
test가 reference 방향과 public surface를 검사한다. 설계 근거는
[`docs/architecture.md`](docs/architecture.md), 원본 프로젝트 분석은
[`docs/architecture-reference.md`](docs/architecture-reference.md)에 있다.

## 라이선스

third-party package와 배포 고지는 [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)에
정리돼 있다. 제품 참고 대상은 [palamim/starboard](https://github.com/palamim/starboard)이며,
원본 Swift 코드나 asset을 기계적으로 복사하지 않았다.
