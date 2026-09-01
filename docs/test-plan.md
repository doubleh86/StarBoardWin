# Windows용 Starboard 테스트 계획

## 목적

persistent terminal, taskbar geometry와 focus 정책을 반복 가능한 방식으로
검증한다. 자동화로 증명한 항목과 실제 Windows desktop에서만 확인할 수 있는
항목을 구분한다.

## 상태 표기

| 상태 | 의미 |
|---|---|
| `Planned` | 구현 또는 실행 전 |
| `Passed` | 명시한 환경과 절차에서 통과 |
| `Failed` | 재현 가능한 실패가 남아 있음 |
| `Blocked` | 필요한 환경·권한·장비가 없음 |
| `Not run` | 실행하지 않음 |

mock/simulation 결과를 실제 monitor, taskbar, IME 또는 fullscreen 검증으로
보고하지 않는다.

## 현재 개발 환경

| 항목 | 값 |
|---|---|
| OS API version | Windows `10.0.26200` |
| architecture | x64 |
| .NET SDK | `10.0.301` at `C:/Users/round1studio_14/.dotnet/dotnet.exe` |
| Node.js/npm | 설치됨 |
| PowerShell | PowerShell 7과 Windows PowerShell 설치됨 |
| WebView2 Runtime | `151.0.4129.107` 발견 |
| Git 상태 | 현재 workspace는 Git repository가 아님 |

위 환경 값은 2026-09-01에 확인했다. 실제 monitor 수, taskbar 위치, scaling과
fullscreen application 종류는 실행할 때 별도로 기록한다.

## 최근 자동 검증 결과

- 실행일: 2026-09-01
- 명령: clean restore, Debug solution build, Debug solution test
- 결과: build 경고 0개·오류 0개, test 32개 통과
- 실제 executable smoke: foreground focus 유지, 116px collapsed → 1032px
  expanded → 116px 복원, `WS_EX_TOPMOST` 없음 확인
- tray smoke: notification icon callback으로 숨김 뒤 1.5초 유지, 숨김 중 동일
  PowerShell process 유지, 재표시와 `종료` 후 8초 이내 process 종료 확인
- summon smoke: 실제 `Ctrl+Alt+S` 입력으로 비활성 panel 호출, 활성 panel 숨김을
  확인하고 실제 notification-area icon 좌표 클릭으로 숨긴 panel의 표시·활성화를 확인
- WebView2·ConPTY·PowerShell 자식 process 확인
- self-contained Release: 501개 파일·196.5MiB, WPF·WinForms runtime, local
  renderer/notice 포함 및 실제 실행 smoke 통과
- 실제 화면 상호작용이 필요한 항목은 아래 manual matrix에 `Not run`으로 유지

## 공통 명령

system `dotnet` 대신 확인된 user-local SDK를 사용한다.

```powershell
$starboardDotnet = 'C:/Users/round1studio_14/.dotnet/dotnet.exe'
& $starboardDotnet restore Starboard.Windows.sln
& $starboardDotnet build Starboard.Windows.sln --configuration Debug --no-restore
& $starboardDotnet test Starboard.Windows.sln --configuration Debug --no-build
```

renderer asset을 변경한 경우에만 Terminal module의 renderer source directory에서
다음을 실행한다.

```powershell
npm ci
npm run build
```

일반 build와 runtime은 npm이나 network를 사용하지 않아야 한다.

## Automated test matrix

### Architecture

| ID | Case | 기대 결과 | 상태 |
|---|---|---|---|
| ARC-001 | 기능 module assembly reference 검사 | 다른 기능 module을 직접 참조하지 않음 | Passed |
| ARC-002 | SharedKernel reference 검사 | host/module/test를 참조하지 않음 | Passed |
| ARC-003 | module public type 검사 | Contracts 또는 승인 entry point만 public | Passed |
| ARC-004 | production project reference 검사 | test project로 역참조 없음 | Passed |
| ARC-005 | host namespace 검사 | module Infrastructure namespace 사용 없음 | Passed |

### Desktop geometry와 policy

| ID | Case | 기대 결과 | 상태 |
|---|---|---|---|
| GEO-001 | bottom taskbar, 100% DPI | panel이 work area 하단에 붙고 taskbar와 겹치지 않음 | Passed |
| GEO-002 | top taskbar | panel이 work area 상단 안쪽에 붙음 | Passed |
| GEO-003 | left/right taskbar | edge에 평행한 두께와 work area clamp 적용 | Passed |
| GEO-004 | negative monitor coordinate | 음수 좌표를 보존하고 primary origin으로 clamp하지 않음 | Passed |
| GEO-005 | 125/150/200% DPI | DIP height가 올바른 physical pixel로 변환됨 | Planned |
| GEO-006 | 높이가 work area보다 큼 | 최소 여백을 보존하도록 clamp됨 | Passed |
| GEO-007 | collapsed → expanded → collapsed | 원래 valid frame을 정확히 복원 | Passed |
| GEO-008 | 기억한 monitor 제거 | 최신 taskbar monitor의 안전 frame으로 복구 | Planned |
| POL-001 | idle + taskbar concealed | panel conceal | Planned |
| POL-002 | active + taskbar concealed | 마지막 안전 frame 유지 | Planned |
| POL-003 | expanded + taskbar concealed | expanded frame 유지 | Planned |
| POL-004 | fullscreen on same monitor | panel demote/conceal | Planned |
| POL-005 | fullscreen on other monitor | panel normal policy 유지 | Planned |
| POL-006 | unknown taskbar presence | off-screen 이동 없이 last safe frame 유지 | Planned |

### Terminal

| ID | Case | 기대 결과 | 상태 |
|---|---|---|---|
| TRM-001 | shell discovery | pwsh → powershell → cmd 순서 | Planned |
| TRM-002 | executable 없음 | recoverable missing-shell state | Planned |
| TRM-003 | custom executable/argument validation | path와 argument array를 안전하게 분리 | Planned |
| TRM-004 | resize validation | 1 이상의 bounded column/row만 native call로 전달 | Passed |
| TRM-005 | split UTF-8 sequence | chunk 경계에서도 문자 손실 없음 | Planned |
| TRM-006 | output batching | 순서 보존, 최대 batch와 flush interval 준수 | Planned |
| TRM-007 | renderer message version/type | unknown/malformed/oversized message 거부 | Passed |
| TRM-008 | shell exit | app은 유지되고 restart 가능 state로 전환 | Planned |
| TRM-009 | bounded shutdown | timeout 안에 resource 정리 완료 | Passed |

### Preferences와 theme

| ID | Case | 기대 결과 | 상태 |
|---|---|---|---|
| SET-001 | settings 없음 | defaults 생성 | Planned |
| SET-002 | partial document | 누락값만 defaults로 merge | Planned |
| SET-003 | old schema | 지원 migration 후 current schema | Planned |
| SET-004 | invalid JSON | backup 후 defaults, app 유지 | Planned |
| SET-005 | atomic write failure | 마지막 정상 파일 유지 | Planned |
| SET-006 | out-of-range value | validator가 안전 범위로 교정하고 이유 반환 | Passed |
| THM-001 | 네 built-in theme | 모든 필수 WPF/xterm/ANSI token 존재 | Passed |
| THM-002 | ANSI palette | 각 theme가 정확히 16색 제공 | Passed |
| THM-003 | foreground/background | 기본 terminal text contrast 기준 충족 | Passed |

### OS adapter

| ID | Case | 기대 결과 | 상태 |
|---|---|---|---|
| HOT-001 | 호출 shortcut message | `Ctrl+Alt+S` message당 activation toggle event 한 번 | Passed |
| HOT-002 | shortcut 충돌 | recoverable error, 앱 계속 실행 | Planned |
| RUN-001 | startup command quoting | 공백이 있는 executable path가 정확히 quote됨 | Planned |
| RUN-002 | enable/disable | 현재 user Run value만 생성/제거 | Planned |
| VDT-001 | official API available | capability와 current-desktop query 반환 | Planned |
| VDT-002 | COM unavailable | no-op fallback과 unsupported capability | Planned |

## Integration test matrix

integration test는 Windows에서 실행하며 다른 앱의 focus나 실제 display topology를
변경하지 않는 범위만 자동화한다.

| ID | Case | 기대 결과 | 상태 |
|---|---|---|---|
| INT-001 | ConPTY 생성과 `pwsh` prompt | prompt output 수신 | Planned |
| INT-002 | `Set-Location` 후 다음 command | working directory 유지 | Passed |
| INT-003 | environment variable set/get | 같은 shell process에서 값 유지 | Planned |
| INT-004 | ConPTY resize | shell이 새 column/row를 보고 | Planned |
| INT-005 | Unicode/한글 round trip | UTF-8 text 손실 없음 | Planned |
| INT-006 | long-running command + interrupt | `Ctrl+C`로 prompt 복귀 | Planned |
| INT-007 | shell explicit exit | restart surface state와 새 session 생성 | Planned |
| INT-008 | output drain + dispose | hang 없이 timeout 내 종료 | Passed |
| INT-009 | bundled renderer load | network request 없이 ready message | Planned |
| INT-010 | WebView2 process failure simulation | app 유지, surface recovery 또는 오류 표시 | Planned |

ConPTY test는 각 case에 timeout을 두고 실패 시 orphan child process를 남기지 않는다.

## 실제 executable smoke matrix

| ID | Case | 확인 결과 | 상태 |
|---|---|---|---|
| SMK-001 | hidden process launch | foreground HWND가 시작 전후 동일 | Passed |
| SMK-002 | background expand/collapse | 116px → 1032px → 116px, 원래 rectangle 정확히 복원 | Passed |
| SMK-003 | renderer/shell process tree | `msedgewebview2.exe`, `conhost.exe`, `pwsh.exe` 확인 | Passed |
| SMK-004 | self-contained Release 실행 | 외부 .NET Runtime 없이 host·renderer·shell 시작 | Passed |
| SMK-005 | tray 표시/숨김 | 1.5초 timer 이후에도 hidden, shell PID 유지, 재표시 성공 | Passed |
| SMK-006 | tray 종료 | 접근 가능한 `Starboard 종료` menu 실행 뒤 8초 이내 process 종료 | Passed |
| SMK-007 | normal z-order | `WS_EX_TOPMOST` 없음, background expand/collapse focus 유지 | Passed |
| SMK-008 | global panel 호출 | 비활성→표시·foreground, 활성→숨김 | Passed |
| SMK-009 | 실제 tray icon 왼쪽 클릭 | 숨긴 panel 표시·foreground, normal z-order 유지 | Passed |

## Manual desktop matrix

각 실행에서 OS build, monitor topology, scaling, taskbar edge/auto-hide, shell과 app
build hash를 함께 기록한다.

| ID | Scenario | 확인 내용 | 상태 |
|---|---|---|---|
| MAN-001 | cold launch while editor focused | editor focus 유지, panel 표시 | Not run |
| MAN-002 | terminal click | 한 번의 click으로 caret/IME 입력 가능 | Not run |
| MAN-003 | background reposition | foreground HWND 변화 없음 | Not run |
| MAN-004 | bottom taskbar | 겹침과 1px gap/overlap 오류 없음 | Not run |
| MAN-005 | top taskbar | 올바른 edge에 표시 | Not run |
| MAN-006 | left taskbar | geometry와 usable content 확인 | Not run |
| MAN-007 | right taskbar | geometry와 usable content 확인 | Not run |
| MAN-008 | taskbar auto-hide reveal/conceal | idle은 따라가고 active는 유지 | Not run |
| MAN-009 | taskbar monitor 이동 | panel이 새 monitor로 이동 | Not run |
| MAN-010 | secondary monitor negative coordinate | 잘못된 primary clamp 없음 | Not run |
| MAN-011 | monitor disconnect/reconnect | visible monitor의 안전 frame으로 복구 | Not run |
| MAN-012 | 100% scaling | 선명한 text와 정확한 geometry | Not run |
| MAN-013 | 125% scaling | 선명한 text와 정확한 geometry | Not run |
| MAN-014 | 150% scaling | 선명한 text와 정확한 geometry | Not run |
| MAN-015 | 200% scaling | 최소 terminal row와 geometry 유지 | Not run |
| MAN-016 | mixed-DPI monitor 이동 | DPI 변경 후 즉시 선명하게 reflow | Not run |
| MAN-017 | Explorer restart | taskbar tracking과 z-order 복구 | Not run |
| MAN-018 | maximized application | panel visible, app focus 유지 | Not run |
| MAN-019 | borderless fullscreen | 같은 monitor에서 panel이 방해하지 않음 | Not run |
| MAN-020 | exclusive fullscreen game/video | 강제 overlay 없음 | Not run |
| MAN-021 | virtual desktop switch | 지원 capability와 실제 visibility 일치 | Not run |
| MAN-022 | expand/collapse hotkey | 현재 monitor work area 확장과 정확한 복원 | Not run |
| MAN-023 | hotkey conflict | settings에서 실패가 설명되고 app 유지 | Not run |
| MAN-024 | PowerShell 7 | persistent prompt, history, resize | Not run |
| MAN-025 | Windows PowerShell | persistent prompt, history, resize | Not run |
| MAN-026 | cmd | persistent prompt, Unicode 한계가 명시됨 | Not run |
| MAN-027 | WSL/custom shell | 선택한 경우 argument/resize/exit 검증 | Not run |
| MAN-028 | 한글 IME composition | 조합 중복·누락·caret 이탈 없음 | Not run |
| MAN-029 | clipboard shortcuts | selection copy, paste와 Ctrl+C interrupt 구분 | Not run |
| MAN-030 | WebView2 Runtime missing simulation | local error와 설치 안내 | Not run |
| MAN-031 | login startup | 일반 user 권한으로 한 instance만 실행 | Not run |
| MAN-032 | Release folder offline | renderer가 network 없이 로드 | Not run |

## Focus 검증 절차

1. Notepad 또는 editor에 text caret를 둔다.
2. foreground HWND와 process를 기록한다.
3. Starboard를 launch하거나 taskbar/display refresh를 유발한다.
4. foreground HWND가 그대로인지 확인한다.
5. terminal을 click하고 입력이 되는지 확인한다.
6. editor를 다시 click한 뒤 background reconciliation이 focus를 되찾지 않는지
   확인한다.

launch focus 보존과 terminal click activation은 서로 다른 요구사항이며 하나의
`WS_EX_NOACTIVATE` 결과로 함께 통과했다고 판단하지 않는다.

## IME와 shortcut 검증 절차

1. Microsoft Korean IME를 활성화한다.
2. 한글 syllable, 자모 수정, backspace와 space 확정을 입력한다.
3. multiline paste와 emoji/CJK text를 확인한다.
4. selection이 없을 때 `Ctrl+C`가 interrupt를 보내는지 확인한다.
5. selection이 있을 때 copy shortcut과 interrupt 정책이 문서와 일치하는지
   확인한다.
6. `Ctrl+V`와 `Ctrl+Shift+V`를 각각 확인한다.

## Explorer와 display recovery 절차

1. terminal에서 작업 directory와 environment marker를 만든다.
2. Explorer를 정상적인 사용자 절차로 restart한다.
3. `TaskbarCreated` 이후 panel이 새 taskbar geometry로 돌아오는지 확인한다.
4. shell PID 또는 marker가 유지되는지 확인한다.
5. monitor를 제거했을 때 창이 off-screen에 남지 않는지 확인한다.

## Release gate

- clean restore/build/test 성공
- self-contained `win-x64` publish 성공
- committed renderer `dist`와 license notice 존재
- runtime network request 없음
- automated/integration 결과가 이 문서에 갱신됨
- 실제로 실행한 manual case만 `Passed`로 표시
- 미실행 DPI, multi-monitor, fullscreen과 IME case가 숨김없이 남아 있음
