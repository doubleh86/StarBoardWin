# Starboard 아이콘 시안 v1

- 생성: 2026-09-10, imagegen 스킬의 내장 이미지 생성 도구 사용(CLI 미사용).
- 파일: [starboard-icon-v1.png](starboard-icon-v1.png)
- 용도: 터미널 기호·별·하단 패널 선을 조합한 앱 아이콘 원본. 확정 배포본은 이 원본의
  시각 정체성을 유지하면서 작은 픽셀 크기에 맞게 다시 그렸다.

## 확정 배포 자산

- `starboard-icon.ico`: 16, 20, 24, 32, 48, 64, 256px RGBA PNG 프레임을 포함한
  canonical multi-resolution ICO다.
- `starboard-icon-{size}.png`: ICO 각 프레임의 실제 크기 미리보기이자 재현 가능한
  입력 자산이다. 16~24px는 바깥 여백을 줄이고 `>_`, 별, 하단선을 굵은 픽셀 단위로
  보정했다. 32px 이상도 같은 단색 기하와 비율을 사용해 앱과 트레이가 일관된다.
- 앱용 복사본은 `src/Starboard.Windows/Assets/Starboard.ico`, 트레이용 복사본은
  `src/Modules/Starboard.Modules.DesktopIntegration/Assets/Starboard.ico`다. 각 module이
  자기 로컬 자산을 소유하며 두 파일은 canonical ICO와 byte-for-byte 동일하다.
- `pwsh -NoProfile -File scripts/Test-StarboardIcon.ps1`은 네트워크나 사용자 파일 없이
  모든 크기, 32-bit RGBA, 실제 투명/불투명 픽셀, 청록 별·하단선과 밝은 terminal
  prompt 색상, 제품 복사본 일치를 검사한다.

16, 20, 24px PNG는 실제 크기와 nearest-neighbor 확대 보기로 육안 확인했다. 실제
Windows notification area, 밝은/어두운 작업표시줄과 DPI별 표시는 제품 적용 작업에서
별도로 검증해야 한다.

## 생성 프롬프트

Use case: logo-brand. Create ONE original polished raster app icon for Starboard, a lightweight Windows terminal that rests just above the taskbar. Square 1024x1024 icon asset, genuinely transparent background outside the icon. A bold deep midnight-navy rounded-square terminal tile, with a clean large pale-cyan terminal prompt >_ and a small but substantial four-point star accent integrated in the upper right. A single cyan horizontal docking strip along the tile's lower interior edge subtly evokes the panel resting above the taskbar. Flat vector-like graphic design, exceptionally clean geometry, balanced optical spacing, strong silhouette, restrained navy/cyan/off-white palette matching a dark technical terminal UI. Straight-on, no perspective, no 3D, no gloss, no gradients, no glow, no drop shadow, no scene, no wordmark, no lettering beyond the >_ prompt, no watermark, no presentation sheet, no alternate variants. Center one icon with roughly 8 percent transparent outer margin, thick simple forms designed to remain legible at small app-icon sizes. Deliver the single icon image, not an app screenshot.
