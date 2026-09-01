# 원본 Starboard 아키텍처 분석

## 분석 기준

- 저장소: [palamim/starboard](https://github.com/palamim/starboard)
- 기준 branch/commit: `main` /
  [`f4136dfaec89408b9948a0d0ac75d096ef44324c`](https://github.com/palamim/starboard/commit/f4136dfaec89408b9948a0d0ac75d096ef44324c)
- 확인일: 2026-09-01
- 확인 범위: `README.md`, `CLAUDE.md`, `Package.swift`,
  `Sources/Starboard`의 Swift source 전체

이 문서는 Swift/AppKit 구현을 그대로 옮기기 위한 명세가 아니다. 원본의 제품
의도, 상태 전이와 UX 불변 조건을 추출하고 Windows에서 교체해야 할 platform
mechanism을 구분한다.

## 핵심 목적

Starboard는 호출하고 숨기는 Quake-style terminal이 아니다. Dock 옆에 계속
붙어 있으면서 사용자가 다른 앱에서 하던 작업을 방해하지 않는 작은 실제 shell
surface다.

원본이 반복해서 지키는 제품 원칙은 다음과 같다.

1. terminal은 찾아서 여는 창이 아니라 desktop의 지속적인 일부다.
2. 보이기 위한 background update가 현재 앱의 focus를 빼앗지 않는다.
3. shell process 하나를 유지해 working directory와 process state를 보존한다.
4. Dock/display 상태가 바뀌면 현재 geometry를 다시 계산한다.
5. 사용자가 입력 중이거나 확장한 동안 auto-hide transition이 surface를
   빼앗지 않는다.
6. 추적할 수 없을 때 기능 전체를 종료하지 않고 안전한 고정 위치로 fallback한다.

## Source 구성과 책임

| Source | 책임 |
|---|---|
| `main.swift` | accessory application 생성과 run loop 시작 |
| `AppDelegate.swift` | 전체 상태 소유, 초기 panel/terminal 생성, shell 시작 |
| `AppDelegate+DockAccessibility.swift` | Dock Accessibility tree에서 icon tray frame 읽기 |
| `AppDelegate+DockGeometry.swift` | Dock presence를 collapsed/expanded frame으로 변환 |
| `AppDelegate+Tracking.swift` | coarse/fast timer, auto-hide state machine, freeze/recovery |
| `AppDelegate+ScreenResolution.swift` | screen/display ID 변환과 Dock host screen 추론 |
| `DockPresence.swift` | `revealed`, `concealed`, `untracked` 상태 model |
| `PanelBuilder.swift` / `KeyablePanel.swift` | nonactivating panel과 terminal view 구성 |
| `ShellEnvironment.swift` | login zsh environment 구성 |
| `TerminalLayout.swift` | font cell 높이에 맞춘 integer row layout |
| `Theme.swift` / `TerminalTheme.swift` | panel tint, foreground, ANSI palette와 font |
| `PanelSettings.swift` | corner radius, tint opacity, font override 저장 |
| `ThemePickerView.swift` / `SettingsPanelView.swift` | keyboard/mouse 설정 surface |

`AppDelegate`가 orchestration과 state를 소유하고 concern별 extension이 같은 상태를
조작한다. 원본 규모에서는 단순하지만 Windows판은 ConPTY, WebView2와 Win32
lifecycle이 추가되므로 이 구조를 그대로 복제하지 않는다.

## Application lifecycle

실제 시작 순서는 다음과 같다.

1. `NSApplication`을 `.accessory` activation policy로 시작한다.
2. Accessibility trust와 저장된 theme를 읽는다.
3. Dock orientation, auto-hide와 host screen cache를 갱신한다.
4. Dock presence와 초기 frame을 계산한다.
5. borderless nonactivating panel, SwiftTerm view와 menu button을 만든다.
6. Dock이 보이는 경우 panel을 activation 없이 앞으로 표시한다.
7. terminal view를 first responder로 지정하고 `/bin/zsh -l`을 시작한다.
8. 1초 또는 60ms timer와 display-change observer를 연결한다.
9. 권한이 없으면 fallback hint를 표시하고 Accessibility prompt를 요청한다.

source에는 별도 `applicationWillTerminate` cleanup이 없다. SwiftTerm/AppKit process
lifecycle에 기대는 부분이며 Windows판에서는 그대로 따르지 않고 module별 bounded
shutdown을 구현한다.

## Dock 위치와 크기 추적

### 실제 정보원

Dock window 자체의 Core Graphics bounds는 화면 전체에 가까워 icon tray 크기로
사용할 수 없다. 원본은 다음 정보원을 조합한다.

- `CFPreferences`의 `com.apple.dock` domain에서 orientation과 auto-hide 설정 읽기
- Dock process의 Accessibility root 자식 중 첫 `AXList`의 position/size 읽기
- layer 20인 Dock-owned window의 screen 교차 면적으로 host display 추론
- non-auto-hide에서는 `visibleFrame`에 bottom reserved strip이 있는 screen 우선

Accessibility permission은 shell, keyboard 또는 사용자 파일 접근 때문이 아니라
Dock의 `AXList` geometry를 읽기 위해서만 필요하다. 권한이 없거나 읽기에 실패하면
terminal은 계속 실행되고 bottom-right fallback frame을 사용한다.

### Tracking cadence

- 일반 상태: 1초마다 coarse cache와 geometry 갱신
- auto-hide 상태: 60ms fast timer
- fast timer에서도 16 tick마다 orientation, permission과 host screen 재확인
- panel이 숨겨져 있으면 pointer가 화면 하단 4pt strip에 들어왔을 때만 빠른
  geometry 평가 수행

이 구조는 auto-hide animation을 따라가되 계속 비싼 Accessibility query를 실행하지
않기 위한 절충이다.

### Presence state machine

- `revealed`: icon tray가 screen과 교차하며 geometry를 계산할 수 있음
- `concealed`: auto-hide Dock tray가 host screen 아래로 이동함
- `untracked`: orientation이 bottom이 아니거나 Accessibility geometry를 읽지 못함

panel이 key window이거나 expanded 상태에서 Dock이 숨기 시작하면 마지막으로 완전히
보였던 collapsed frame을 고정한다. 사용자가 입력 중인 surface가 animation과 함께
사라지는 것을 막는 정책이다. display가 제거되면 남아 있는 screen과 가장 크게
교차하는 위치 또는 새 Dock host로 복구한다.

## Geometry

bottom Dock이 보일 때 panel은 Dock icon tray의 오른쪽 빈 공간을 사용한다.

- panel right = host screen right
- panel left = `max(tray.right, screen.right - 300)`
- panel vertical range = tray range에서 위·아래 5pt 보정
- 최소 width = 300pt

left/right Dock은 현재 지원하지 않고 해당 screen의 bottom-right fallback으로 간다.
fallback width/height는 300×64pt이며, screen visible frame이 bottom strip을 예약하면
그 높이를 사용한다.

README는 expand를 full screen height로 설명하지만 현재 source의
`expandedFrame`은 active screen `visibleFrame` 중앙의 75% width × 75% height다.
Windows 요구사항은 이 세부 구현을 복제하지 않고 현재 monitor usable work area까지
확장하는 것으로 확정한다.

## Focus, activation과 항상 표시

원본 panel 설정은 다음 조합이다.

- `NSPanel` + `.borderless` + `.nonactivatingPanel`
- Dock window level보다 한 단계 높은 level
- `canJoinAllSpaces`, `stationary`, `fullScreenAuxiliary`, `ignoresCycle`
- `hidesOnDeactivate = false`, movable false, shadow false
- `KeyablePanel.canBecomeKey = true`, `canBecomeMain = false`

background 표시에는 `orderFrontRegardless`를 사용하므로 현재 application을
activate하지 않는다. 반면 사용자가 panel/menu/settings를 조작할 때는 key window와
first responder가 될 수 있다. 즉 “절대 focus를 받지 않는 창”이 아니라 “background
재배치와 표시로 focus를 훔치지 않지만 의도적인 입력은 받는 창”이다.

모든 Space와 fullscreen 위에 표시하는 것은 AppKit collection behavior가 직접
지원한다. Windows 공식 virtual desktop API에는 동일한 pin contract가 없으므로
이 구현 세부는 그대로 이식할 수 없다.

## Terminal session

원본은 직접 ANSI parser를 만들지 않고
[SwiftTerm](https://github.com/migueldeicaza/SwiftTerm)의
`LocalProcessTerminalView`를 사용한다.

- `/bin/zsh -l` process 하나를 application 시작 시 실행
- `xterm-256color` environment 생성
- library 기본 environment에 `SHELL`이 없으면 명시적으로 추가
- 같은 process가 계속 살아 있어 `cd`, history, environment와 job state 유지
- panel frame 변경 시 font cell height로 완전한 row 수를 계산해 terminal frame 조정

shell crash 표시, retry UI와 bounded shutdown은 원본 source의 명시적 기능이 아니다.
Windows 요구사항에서는 이 부분을 강화한다.

## Expand/collapse와 command surface

- application menu의 `Cmd+E`가 `isExpanded`를 toggle한다.
- 확장할 때 panel이 있던 screen을 우선하고 display ID를 기억한다.
- 축소할 때 Dock이 concealed/frozen이면 마지막 안전 collapsed frame으로 돌아간다.
- frame transition은 180ms ease-out이다.
- `Cmd+T`는 keyboard theme picker, panel 우측 상단 button은 popup menu를 연다.

accessory + nonactivating 조합에서는 main menu가 실질적으로 보이지 않아, 원본도
panel 내부의 클릭 가능한 menu를 별도로 제공한다.

## Theme와 settings

원본은 10개 dark와 10개 light theme를 code-defined value로 보유한다. 하나의
`Theme`이 다음을 함께 제공한다.

- panel tint와 기본 alpha
- terminal foreground
- ANSI 16색 palette
- dark/light에 맞는 chrome tint

theme ID, corner radius, optional opacity override와 optional font override를
`UserDefaults`에 저장한다. opacity가 설정되지 않았으면 theme 고유 alpha를 유지하고,
font는 설치된 Nerd Font 후보를 순서대로 탐색한 뒤 system monospace로 fallback한다.

Windows v0.1은 theme 수를 네 개로 줄이지만 WPF surface와 xterm ANSI palette가
같은 theme definition을 소비하는 원칙은 유지한다.

## 플랫폼 독립 개념과 macOS 전용 구현

| 제품 개념 | macOS 구현 | Windows 재구현 |
|---|---|---|
| persistent surface | Dock level `NSPanel` | borderless WPF tool window + 동적 z-order 정책 |
| background focus 보존 | nonactivating panel + `orderFrontRegardless` | `ShowActivated=false` + `SetWindowPos(SWP_NOACTIVATE)` |
| 의도적 terminal 입력 | key 가능, main 불가 panel | permanent `WS_EX_NOACTIVATE` 없이 클릭 activation 허용 |
| system bar geometry | Dock AX tree + screen visible frame | `SHAppBarMessage`, taskbar HWND와 `GetMonitorInfo` |
| auto-hide hold | revealed/concealed/frozen state | configured/visible/held state reducer |
| desktop 전역 표시 | `canJoinAllSpaces` | 공식 API 범위만 지원, pinning 미보장 |
| fullscreen | `fullScreenAuxiliary` | foreground fullscreen 감지 후 demote/conceal |
| terminal | SwiftTerm + zsh | ConPTY + bundled xterm.js + 선택 shell |
| settings | `UserDefaults` | schema-versioned local JSON + atomic replace |
| login start | Login Items/LaunchAgent | per-user HKCU Run |
| Accessibility | Dock AX geometry에 필요 | 필요 없음 |

## Windows판에서 의도적으로 달라지는 점

1. 작업표시줄 icon tray 옆의 짧은 폭이 아니라 taskbar monitor의 work area 전체
   폭을 쓰는 얇은 horizontal panel을 기본값으로 한다.
2. fullscreen 위에 항상 덮는 원본 동작을 복제하지 않고 사용 중인 게임·영상의
   비방해를 우선한다.
3. virtual desktop pinning을 undocumented COM에 의존해 기본 제공하지 않는다.
4. Accessibility에 해당하는 별도 권한을 요구하지 않는다.
5. shell/renderer 실패를 application 전체 종료와 분리하고 restart surface를
   제공한다.
6. ConPTY와 WebView2 resource를 명시적 순서와 timeout으로 정리한다.

## 원본에서 유지할 UX 기준

- “계속 보임”과 “계속 focus를 가짐”을 구분한다.
- background geometry update는 activation을 일으키지 않는다.
- terminal을 클릭하면 즉시 정상 입력할 수 있다.
- shell session은 명령마다 새로 만들지 않는다.
- auto-hide/display transition 중 active terminal frame을 안전하게 고정한다.
- platform 정보를 얻지 못해도 작은 안전 frame으로 복구한다.
- theme는 terminal palette와 주변 chrome을 따로 놀게 하지 않는다.

## 참고 링크

- [원본 README](https://github.com/palamim/starboard/blob/f4136dfaec89408b9948a0d0ac75d096ef44324c/README.md)
- [원본 architecture notes](https://github.com/palamim/starboard/blob/f4136dfaec89408b9948a0d0ac75d096ef44324c/CLAUDE.md)
- [원본 source tree](https://github.com/palamim/starboard/tree/f4136dfaec89408b9948a0d0ac75d096ef44324c/Sources/Starboard)
- [원본 package definition](https://github.com/palamim/starboard/blob/f4136dfaec89408b9948a0d0ac75d096ef44324c/Package.swift)
