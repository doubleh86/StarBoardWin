# Starboard for Windows

Windows 작업표시줄 바로 위에 붙어 있는 작고 지속적인 터미널.

별도의 터미널 창을 찾아다니지 않고, 작업 중인 화면 아래에서 빠르게 명령을 실행할 수
있습니다. 평소에는 얇은 패널로 두고, 필요할 때 확장하거나 숨겨도 각 탭의 셸 세션은
그대로 유지됩니다. 다른 앱의 포커스를 불필요하게 빼앗지 않는 것을 우선합니다.

[palamim/starboard](https://github.com/palamim/starboard)의 macOS용 상시 터미널
콘셉트와 UX에서 영감을 받아 Windows에 맞게 독립적으로 재구현한 프로젝트입니다.
원본 프로젝트의 공식 Windows 버전은 아니며, Swift/AppKit 코드를 기계적으로
포팅하거나 원본 소스·에셋을 복사한 구현이 아닙니다.

Built with Codex.

## 다운로드

[최신 GitHub 릴리즈](https://github.com/doubleh86/StarBoardWin/releases/latest)에서
Windows x64용 ZIP을 받을 수 있습니다. 릴리즈의 `Assets`에서 다음 중 하나를
선택하세요. GitHub가 자동으로 제공하는 `Source code`는 실행용 배포물이 아닙니다.

- `Starboard-<version>-win-x64-framework-dependent.zip`: 경량판. .NET Desktop
  Runtime 10 x64가 이미 설치되어 있거나 별도로 설치할 때 권장합니다.
- `Starboard-<version>-win-x64.zip`: .NET 런타임 포함판. .NET 별도 설치 없이
  실행할 수 있지만 ZIP 용량이 더 큽니다.
- 각 ZIP의 `.sha256`: 다운로드한 파일의 무결성 확인용입니다.

두 배포판 모두 WebView2 Evergreen Runtime이 필요합니다. 압축을 전부 해제한 뒤
`Starboard.exe`를 실행하세요. 아래 [실행 환경](#실행-환경)과
[시작하기](#시작하기)에서 자세한 설치·업데이트 방법을 확인할 수 있습니다.

## 주요 기능

- 작업표시줄에 밀착하는 테두리 없는 패널과 높이 조절 손잡이
- Windows ConPTY 기반의 실제 대화형 셸: 탭마다 작업 폴더·환경·실행 상태 유지
- 최대 8개 독립 탭, 이름 변경·순서 이동·탭 구성 복제
- `+`에서 PowerShell 7, Windows PowerShell, 명령 프롬프트와 설치된 WSL 배포판 선택
- 자주 쓰는 이름·시작 폴더·셸을 최대 20개 저장하고 새 탭으로 실행
- 선택적 작업공간 복원: 다음 실행에 탭 구성을 다시 열기
- 현재 탭 출력 검색, 복사·붙여넣기, 여러 줄 붙여넣기와 탭 닫기 확인
- 파일·폴더 드롭 시 경로 확인 및 셸별 인용, HTTP/HTTPS 링크 확인 후 열기
- 비활성 탭의 새 출력 표시와 선택적 PowerShell 명령 완료 알림
- 트레이 메뉴, 전역 단축키, 로그인 시 자동 실행 설정
- Dark, Light, One Dark, Tokyo Night 테마와 글꼴·크기·불투명도 설정
- 로컬 xterm.js/WebView2 렌더러: 실행 중 CDN이나 외부 폰트 없이 동작

항상 다른 창 위에 올라오는 터미널이나 분할 화면 중심의 터미널을 지향하지 않습니다.
작업표시줄 근처에서 빠르고 간단하게 쓰는 보조 작업 공간이 목표입니다.

## 실행 환경

Windows 11 x64를 기준으로 개발합니다. Windows 10 1809 이상은 best-effort
대상이며, 런타임 지원 조건과 실제 장비에 따른 호환성을 보장하지 않습니다.

| 요구 사항 | 경량 배포 — 기본 | 런타임 포함 배포 |
| --- | --- | --- |
| .NET Desktop Runtime 10 x64 | 별도 설치 또는 기존 설치 사용 | 배포물에 포함 |
| Microsoft Edge WebView2 Evergreen Runtime | 필요 | 필요 |
| 개발용 .NET SDK / Node.js | 실행에 불필요 | 실행에 불필요 |

- [.NET 10 다운로드](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)에서
  Windows용 **.NET Desktop Runtime x64**를 선택하세요. 호환되는 10.0.x가 이미
  설치되어 있다면 다시 설치할 필요가 없습니다. 일반 .NET Runtime, x86 또는 다른
  메이저 버전만 설치된 경우에는 요구 사항을 충족하지 않습니다.
- [WebView2 다운로드](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)에서
  Evergreen Runtime을 설치할 수 있습니다. 두 배포 방식 모두 WebView2는 별도로
  필요하며, Starboard가 설치 프로그램을 자동으로 내려받거나 실행하지는 않습니다.
- PowerShell 7을 우선 탐색하고, 없으면 Windows PowerShell → `cmd.exe` 순으로
  선택합니다. WSL은 이미 설치된 배포판이 있을 때 선택할 수 있습니다.

Starboard의 일반 실행에는 관리자 권한이 필요하지 않습니다.

## 시작하기

1. 배포 ZIP 전체를 원하는 폴더에 압축 해제합니다. `Starboard.exe`만 따로 옮기지 마세요.
2. 함께 제공되는 `.sha256` 값과 ZIP의 SHA-256을 비교할 수 있습니다.
   `Get-FileHash -Algorithm SHA256 <ZIP 경로>`로 확인합니다.
3. `Starboard.exe`를 실행하면 작업표시줄 바로 위에 터미널이 나타납니다.
4. 패널을 클릭해 입력하고, `+`로 새 탭의 셸을 선택합니다.
5. 트레이 아이콘을 오른쪽 클릭하면 표시·숨기기, 설정, 단축키 안내와 종료를 사용할 수 있습니다.

기본 높이는 200 DIP입니다. 탭 바의 가운데 손잡이나 패널 상단 경계를 드래그하면
96~720 DIP 범위에서 조절할 수 있습니다. 탭이 많아 손잡이가 좁아지면 상단 경계를
사용하세요. 상단 경계를 두 번 클릭하면 기본 높이로 돌아갑니다. `Ctrl+Alt+E`로
작업 영역까지 확장했다가 이전 크기로 복원할 수 있습니다.

패널 숨기기는 세션 종료가 아닙니다. 앱을 완전히 종료하려면 실행 중인 작업을 정리한
뒤 트레이의 `종료`를 선택하세요. 종료된 셸의 작업을 다음 실행에서 이어받지는 않습니다.

### 런타임이 없거나 오프라인 PC에서 실행하는 경우

.NET이 없으면 실행기의 누락 안내에 따라 Desktop Runtime을 설치한 뒤 다시
실행하세요. WebView2가 없으면 앱의 안내 화면에서 설치 후 `다시 시도`할 수 있습니다.

오프라인 PC에는 연결 가능한 PC에서 Microsoft의 .NET 설치 파일과 WebView2
**Evergreen Standalone Installer x64**를 받아 전달하세요. 설치 파일의 디지털 서명
게시자가 `Microsoft Corporation`인지 확인하고, 조직 정책으로 설치가 막힌 경우
관리자에게 런타임 배포를 요청하세요. 필요한 런타임이 준비된 뒤에는 Starboard의
터미널 렌더링에 네트워크가 필요하지 않습니다.

### 업데이트

실행 중인 작업을 정리하고 트레이에서 종료한 뒤, 새 ZIP을 새 폴더에 압축 해제해
실행하세요. 특히 런타임 포함 배포에서 경량 배포로 바꿀 때는 기존 폴더에 덮어써서
이전 런타임 DLL을 남기지 않는 것이 좋습니다.

사용자 설정은 배포 폴더 밖에 저장됩니다. 실행 파일 위치를 바꿨고 로그인 자동
실행을 사용 중이라면, 설정에서 해당 옵션을 한 번 껐다가 다시 켜 경로를 갱신하세요.
이전 버전으로 돌아갈 때는 설정 스키마 호환성을 확인하고 사용자 데이터를 먼저
백업하세요. 자동 실행 경로도 이전 실행 파일로 다시 등록해야 합니다.

## 단축키와 입력

| 입력 | 동작 |
| --- | --- |
| `Ctrl+Alt+S` | 숨김·비활성 패널 표시 및 활성화 / 활성 패널 숨기기 |
| `Ctrl+Alt+E` | 패널 확장 / 이전 축소 크기로 복원 |
| `Ctrl+Shift+T` | 기본 프로필로 새 탭 열기 |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | 다음 / 이전 탭 |
| `Ctrl+Shift+W` | 현재 탭 닫기 요청; 살아 있는 세션은 확인 후 종료 |
| `Ctrl+C` | 선택이 있으면 복사, 없으면 셸에 인터럽트 전달 |
| `Ctrl+Shift+C` | 선택한 텍스트 복사 |
| `Ctrl+V` / `Ctrl+Shift+V` | 붙여넣기; 여러 줄이면 미리보기·확인 |
| `Ctrl+F` | 현재 탭 출력 검색; `Enter` / `Shift+Enter`로 다음 / 이전 결과 |
| `Escape` | 검색 등 현재 열린 보조 화면 닫기 |
| 탭 우클릭 / 탭에서 `Shift+F10` | 이름 변경, 이동, 시작 폴더, 복제·저장 메뉴 |
| HTTP/HTTPS 주소 `Ctrl+클릭` | 대상 주소를 확인한 뒤 기본 브라우저에서 열기 |

전역 단축키는 설정에서 바꿀 수 있습니다. 다른 프로그램이 이미 사용 중이면 등록이
실패할 수 있으며, 트레이의 `단축키 안내`에서 실제 적용·등록 상태를 확인할 수 있습니다.

여러 줄 붙여넣기는 확인한 텍스트를 한 번만 전달합니다. 취소하면 입력하지 않습니다.
파일·폴더 드롭은 내용이 아닌 로컬 경로만 확인 후 입력하고, Enter는 자동으로 보내지
않습니다. 지원하지 않는 셸이나 안전하게 인용할 수 없는 경로는 입력을 거부합니다.
HTTP/HTTPS 외의 URL 스킴은 열지 않습니다.

## 저장한 탭과 작업공간 복원

### 자주 쓰는 탭 저장

탭 메뉴의 `저장한 탭에 추가…`에서 이름·시작 폴더·셸을 저장하고,
`저장한 탭` 메뉴에서 다시 열 수 있습니다. 저장 항목을 열 때마다 새 셸 세션을
시작하며, 기존 탭의 실행 중인 명령이나 상태를 복제하지 않습니다.

PowerShell 7과 Windows PowerShell에서는 최근 프롬프트에서 확인한 실제 현재 폴더를
저장 화면에 우선 표시합니다. 확인할 수 없거나 CMD 등 미지원 셸인 경우에는 설정된
시작 폴더와 안내가 표시되므로 경로를 확인한 뒤 저장하세요.

### 다음 실행에 탭 구성 복원

`설정 > 작업공간 복원`은 기본적으로 꺼져 있습니다. 켜면 탭 이름·순서·선택된 탭,
설정된 시작 폴더와 기본 셸 종류를 저장하고 다음 실행에서 새 프로세스로 엽니다.

실행 중인 명령·작업, PID, 환경 변수, 명령 이력, 출력·스크롤백과 클립보드는
저장하거나 복원하지 않습니다. 실행 중 `cd`로 이동한 위치가 자동으로 다음 시작
폴더가 되지는 않으며, 필요한 위치는 명시적으로 저장해야 합니다.

복원 옵션을 끄고 설정 저장에 성공하면 저장된 작업공간 구성을 삭제합니다.
`저장한 탭` 목록은 별개이므로 그대로 유지됩니다.

WSL 탭은 배포판의 Linux 홈에서 시작하며 Windows 시작 폴더를 Linux 경로로 변환하지
않습니다. 현재 WSL 프로필은 저장한 탭·작업공간 복원 대상이 아니며, 앱 재시작 시
기본 내장 셸 탭으로 대체될 수 있습니다.

## 개인정보와 로컬 데이터

Starboard 자체에는 분석·텔레메트리·크래시 업로드·원격 설정 기능이 없습니다.
렌더러 스크립트와 스타일은 배포물에 포함되며 CDN을 사용하지 않습니다.
명령 내용·터미널 출력·클립보드·환경 변수를 수집해 파일에 기록하거나 외부로
전송하지 않습니다. 셸 및 사용자가 실행한 프로그램의 기록·네트워크 동작,
시스템 런타임의 업데이트 정책은 별개입니다.

사용자 데이터는 다음 위치에 저장됩니다. ZIP 배포물에는 포함되지 않습니다.

```text
%LOCALAPPDATA%/Starboard/
  settings.json       # 화면·입력·자동 실행 등 설정
  workspace.json      # 복원 옵션을 켠 경우의 탭 구성
  saved-tabs.json     # 사용자가 저장한 이름·시작 폴더·셸
  Logs/starboard.log  # 제한된 진단 메타데이터, 회전 보관
  WebView2/           # 렌더러 사용자 데이터
```

설정 저장·복구 과정에서 `.bak`이나 `.tmp` 파일이 생길 수 있습니다. 구성 파일은
암호화되지 않은 로컬 JSON이므로 탭 이름과 저장한 폴더 경로를 공유할 때 주의하세요.
진단 로그는 수준·하위 시스템·동작·네이티브 오류 코드만 기록하며, 원본 예외 본문이나
터미널 내용은 남기지 않습니다. 로그는 파일당 512 KiB, 회전 보관본을 포함해 최대
2.5 MiB로 제한합니다.

`명령 완료 알림`은 기본적으로 꺼져 있으며 PowerShell 7/Windows PowerShell의
명시적 완료 신호에만 반응합니다. 알림에는 성공·실패만 표시하고 명령·출력·작업 폴더·
탭 이름은 포함하지 않습니다. 출력이 멈췄다는 이유만으로 완료를 추측하거나 알림을
위해 패널을 활성화하지 않습니다.

## 알려진 제약

- Windows x64 전용입니다. macOS/Linux용 빌드나 ARM64 네이티브 배포는 제공하지 않습니다.
- 다른 앱을 계속 덮는 always-on-top 동작이나 모든 가상 데스크톱에 고정하는 기능은 제공하지 않습니다.
- 전체 화면 앱 위에 패널을 강제로 표시하지 않습니다.
- 셸 오류 시 다시 시작할 수 있지만 종료된 셸의 작업 상태는 복구할 수 없습니다.
- 렌더러 재시작 시 살아 있는 셸에 다시 연결하지만, 이전 렌더러의 스크롤백은 복원되지 않습니다.
- 멀티모니터·mixed-DPI·작업표시줄 자동 숨김·Explorer 재시작·전체 화면·한글 IME 등
  실제 장비의 수동 검증에는 미완료 항목이 있습니다. 자동 테스트 통과와 실제 환경
  검증을 구분하며, 상세 상태는 [테스트 계획](docs/test-plan.md)을 확인하세요.

## 소스에서 빌드하기

Windows와 [global.json](global.json)에 지정한 .NET 10 SDK가 필요합니다.
기준 SDK는 `10.0.301`이며 같은 feature band의 최신 패치만 허용합니다.
일반 빌드는 저장소에 포함된 렌더러를 사용하므로 Node.js가 필요하지 않습니다.
의존성 복원에는 NuGet 및 취약점 메타데이터 소스에 대한 접근이 필요합니다.

저장소 루트에서 실행합니다.

```powershell
dotnet restore Starboard.Windows.sln
dotnet build Starboard.Windows.sln --configuration Debug --no-restore
dotnet test Starboard.Windows.sln --configuration Debug --no-build --no-restore
dotnet run --project src/Starboard.Windows/Starboard.Windows.csproj
```

### Portable ZIP 만들기

```powershell
# 기본: 설치된 .NET Desktop Runtime을 사용하는 경량 배포
powershell -NoProfile -File scripts/package-portable.ps1

# 선택: .NET 런타임을 포함하는 배포
powershell -NoProfile -File scripts/package-portable.ps1 -SelfContained
```

Windows PowerShell 5.1과 PowerShell 7을 지원합니다. `dotnet`이 PATH에 없다면
`-DotNetPath '<dotnet.exe 경로>'`를 전달할 수 있습니다.
스크립트는 Release restore/build/test, publish, ZIP·SHA-256 생성과 추출 실행
검증을 수행합니다. 결과 경로는 다음과 같습니다.

```text
out/portable/<version>/framework-dependent/
  Starboard-<version>-win-x64-framework-dependent.zip
  Starboard-<version>-win-x64-framework-dependent.zip.sha256

out/portable/<version>/
  Starboard-<version>-win-x64.zip
  Starboard-<version>-win-x64.zip.sha256
```

ZIP에는 앱, 로컬 렌더러, 아이콘, 라이선스·third-party 고지와
`release-metadata.json`이 포함됩니다. 설정 화면의 버전·빌드 커밋을 이 메타데이터와
대조할 수 있습니다. 사용자 데이터·로그·PDB는 포함하지 않습니다.

렌더러 소스나 패키지 버전을 변경할 때만 Node.js/npm으로 번들을 다시 생성합니다.

```powershell
Set-Location src/Modules/Starboard.Modules.Terminal/Presentation/Renderer
npm ci
npm run build
```

## 구조와 개발 문서

.NET 10 + WPF 기반의 모듈러 모놀리스입니다. 하나의 앱 프로세스·배포 단위를
유지하면서 기능 모듈은 별도 class library로 분리합니다.

```text
Starboard.Windows                         # 실행 파일, UI와 모듈 조합
  ├─ Starboard.Modules.Terminal           # ConPTY, 셸, WebView2/xterm.js
  ├─ Starboard.Modules.DesktopIntegration # 작업표시줄, 모니터, Win32 연동
  ├─ Starboard.Modules.Preferences        # 설정과 저장
  └─ Starboard.SharedKernel               # 최소 공통 계약
```

모듈끼리 직접 참조하지 않으며 아키텍처 테스트가 참조 방향과 공개 API 경계를
검증합니다. 구현 범위와 기여 규칙은 다음 문서를 참고하세요.

- [Windows 아키텍처](docs/architecture.md)
- [원본 프로젝트 분석](docs/architecture-reference.md)
- [테스트 계획과 검증 상태](docs/test-plan.md)
- [개발 작업 규칙](docs/development-workflow.md)
- [코드 스타일](docs/code-style.md)
- [기능 계획과 작업 기록](docs/plans/README.md)

버그 제보에는 앱 버전·빌드 커밋, Windows 버전, 화면 배율, 작업표시줄 설정과 재현
절차를 포함해 주세요. 스크린샷·설정 파일에 명령, 개인 경로 또는 토큰이 노출되지
않는지 먼저 확인해 주세요.

## 참고 프로젝트와 감사

이 프로젝트의 출발점은 Leonardo Palamim Cardozo의
[palamim/starboard](https://github.com/palamim/starboard)입니다. macOS Dock 옆에
머무는 터미널이라는 제품 아이디어와, 사용자 작업을 방해하지 않으면서 셸을 유지하는
상호작용 원칙을 참고했습니다. 좋은 프로젝트를 공개해 준 원작자에게 감사드립니다.

Windows 버전은 WPF, ConPTY, WebView2와 xterm.js로 별도 구현하며, 원본의
macOS 전용 동작이나 플랫폼 간 완전한 기능 일치를 보장하지 않습니다.
원본 프로젝트의 라이선스는 [MIT](https://github.com/palamim/starboard/blob/main/LICENSE)입니다.

## 라이선스

Starboard for Windows는 [MIT License](LICENSE)로 배포합니다.
포함된 라이브러리와 에셋의 개별 라이선스·고지는
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)를 참고하세요.
