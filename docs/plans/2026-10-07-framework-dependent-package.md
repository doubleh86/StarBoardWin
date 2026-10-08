# 설치된 .NET 런타임을 사용하는 경량 배포

## 목표와 완료 조건

기본 배포 ZIP에서 .NET/WPF/WinForms 런타임을 제외하고 설치된 .NET Desktop
Runtime 10 x64를 사용한다. 실행 파일과 renderer, 앱 의존성, license는 포함한다.
호환 런타임이 설치된 PC에서 추출본의 실행 smoke가 통과하고 기존 런타임 포함판과
파일 수·용량을 비교할 수 있어야 한다.

## 범위와 영향 파일

- `scripts/package-portable.ps1`: 기본 framework-dependent publish, 명시적인
  `-SelfContained` 대안, 모드별 출력 격리와 metadata/content 검사.
- `README.md`, `docs/architecture.md`, `docs/test-plan.md`: 런타임 요구사항,
  설치 안내, 생성 명령·경로와 실제 검증 결과.
- 제품 C#/XAML과 셸·창 동작은 변경하지 않는다. 바탕화면 교체·실행, 런타임 설치,
  commit/push는 이번 배포본 생성 요청에 포함하지 않는다.

## 판단과 위험

- Microsoft의 [배포 문서](https://learn.microsoft.com/en-us/dotnet/core/deploying/)에
  따라 `--self-contained false`를 사용한다. 기본 apphost는 설치된 런타임을
  찾으므로 자체 런타임 탐색기나 installer를 추가하지 않는다.
- [.NET apphost 안내](https://devblogs.microsoft.com/dotnet/dotnet-apphost-improvements/)의
  누락 런타임 안내를 사용한다. SDK나 일반 .NET Runtime만으로 WPF 실행이
  보장되지 않으며 .NET Desktop Runtime 10 x64가 필요하다. 다른 major 또는
  x86만 설치된 환경도 설치 안내 대상이다. 앱이 installer를 자동 실행하지 않는다.
- 현재 PC에는 시스템 경로의 Desktop Runtime 10.0.12 x64가 있다. 이 환경에서의
  실행 성공과 런타임 없는 PC의 GUI 설치 안내 검증을 구분한다.
- 이전 501개 파일의 self-contained 출력 위에 경량 publish를 덮어쓰면 잔여
  런타임 때문에 용량이 줄지 않을 수 있다. 기본 경량 출력은 별도
  `out/portable/<version>/framework-dependent/` 아래에서 생성한다.
- 기존 런타임 포함판의 명령·출력 호환을 `-SelfContained`로 유지한다. 새 의존성,
  trimming, 언어 resource 삭제나 Windows SDK assembly 제거는 도입하지 않는다.

## 구현 단계

- [x] 기본 경량 publish와 별도 출력·metadata·런타임 제외 검사를 구현했다.
- [x] 설치 요구사항과 선택 가능한 두 배포 방식을 문서에 반영했다.
- [x] 두 모드의 Release 전체 검증, ZIP 재현성·checksum·추출 실행 smoke와 용량 비교를 수행했다.

## 검증 방법

기본 `powershell -NoProfile -File scripts/package-portable.ps1`로 Release
restore/build/test/publish를 수행한다. 경량 publish와 추출본에 `coreclr.dll`,
`System.Private.CoreLib.dll`, `PresentationFramework.dll`이 없고 runtimeconfig가
Desktop framework를 요구하는지 검사한다. 설치된 시스템 런타임으로 apphost
smoke를 실행하고, `-SelfContained` 분기는 같은 코드의 포함판 publish/content
검사와 smoke로 확인한다. 실제 런타임 누락 GUI는 별도 수동 검증으로 기록한다.

## 진행 기록

- 2026-10-07: 사용자 요청으로 경량 배포 생성을 시작했다. 기존 배포 폴더는
  약 197.19 MiB이고 ZIP은 76.93 MiB다.

## 완료 요약

- 기본 경량판은 33개 파일, 26.53 MiB이며 ZIP은 6.86 MiB다. 포함판은 501개
  파일, 197.19 MiB 및 ZIP 76.93 MiB다. 모드별 metadata·runtimeconfig·runtime
  DLL 검사와 사용자 data 제외, ZIP 재현성·checksum·추출 smoke를 모두 통과했다.
- 두 모드를 실제 packaging 명령으로 각각 검증했다. Release build 경고·오류가
  없었고 전체 545개 test가 각 실행에서 실패·skip 없이 통과했다.
- 별도 smoke process의 host trace에서 시스템 경로의 `Microsoft.NETCore.App` 및
  `Microsoft.WindowsDesktop.App` 10.0.12 선택과 exit code 0을 확인했다. 빈 런타임 경로를
  child process에만 지정한 simulation은 `You must install .NET to run this
  application`과 exit code -2147450749로 실패했다. GUI는 억제했으며 실제
  런타임 미설치 PC의 안내 창·설치 링크·설치 후 실행은 Not run (manual)이다.
- 앱 코드 기준은 `1cc1295`이고 packaging 변경은 미커밋 상태로 검증했다.
  경량 ZIP SHA-256은 `e6bae2eeac5be7b5fa904adfef38bd564acaa12217b55e7c735770b035b335dc`,
  포함판은 `11650e3acdec8a4e2fff20b8261a728fe848a5aa3cddb5ef7a0cfdc3b3dccb71`이다.
- 남은 용량 중 Windows SDK projection assembly가 약 22.54 MiB다. 현재
  의존성은 유지했으며 경량화를 이유로 해당 assembly를 수동 삭제하지 않았다.
  바탕화면에서 실행 중인 기존 배포본은 교체하지 않았다.
