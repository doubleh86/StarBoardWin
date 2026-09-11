[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$requiredSizes = @(16, 20, 24, 32, 48, 64, 256)
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$canonicalDirectory = Join-Path $repositoryRoot 'docs/assets'
$canonicalIconPath = Join-Path $canonicalDirectory 'starboard-icon.ico'
$productIconPaths = @(
    (Join-Path $repositoryRoot 'src/Starboard.Windows/Assets/Starboard.ico'),
    (Join-Path $repositoryRoot 'src/Modules/Starboard.Modules.DesktopIntegration/Assets/Starboard.ico')
)

Add-Type -AssemblyName PresentationCore

function Assert-Condition {
    param(
        [Parameter(Mandatory)]
        [bool] $Condition,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if ($Condition -eq $false) {
        throw $Message
    }
}

function Test-PngBytes {
    param(
        [Parameter(Mandatory)]
        [byte[]] $Bytes,

        [Parameter(Mandatory)]
        [int] $ExpectedSize,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $pngSignature = [byte[]] @(137, 80, 78, 71, 13, 10, 26, 10)
    Assert-Condition ($Bytes.Length -gt 33) "$Description is too short to be a PNG."
    for ($index = 0; $index -lt $pngSignature.Length; $index++) {
        Assert-Condition ($Bytes[$index] -eq $pngSignature[$index]) "$Description has an invalid PNG signature."
    }

    $width = ([int] $Bytes[16] -shl 24) -bor
        ([int] $Bytes[17] -shl 16) -bor
        ([int] $Bytes[18] -shl 8) -bor
        [int] $Bytes[19]
    $height = ([int] $Bytes[20] -shl 24) -bor
        ([int] $Bytes[21] -shl 16) -bor
        ([int] $Bytes[22] -shl 8) -bor
        [int] $Bytes[23]
    $bitDepth = $Bytes[24]
    $colorType = $Bytes[25]
    Assert-Condition ($width -eq $ExpectedSize -and $height -eq $ExpectedSize) `
        "$Description is ${width}x${height}; expected ${ExpectedSize}x${ExpectedSize}."
    Assert-Condition ($bitDepth -eq 8 -and $colorType -eq 6) `
        "$Description must be an 8-bit RGBA PNG (bit depth 8, color type 6)."

    $stream = [System.IO.MemoryStream]::new($Bytes, $false)
    try {
        $decoder = [System.Windows.Media.Imaging.PngBitmapDecoder]::new(
            $stream,
            [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
            [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        $converted = [System.Windows.Media.Imaging.FormatConvertedBitmap]::new(
            $decoder.Frames[0],
            [System.Windows.Media.PixelFormats]::Bgra32,
            $null,
            0)
        $pixels = [byte[]]::new($ExpectedSize * $ExpectedSize * 4)
        $converted.CopyPixels($pixels, $ExpectedSize * 4, 0)
    }
    finally {
        $stream.Dispose()
    }

    $transparentCount = 0
    $opaqueCount = 0
    $cyanCount = 0
    $promptCount = 0
    for ($offset = 0; $offset -lt $pixels.Length; $offset += 4) {
        $blue = $pixels[$offset]
        $green = $pixels[$offset + 1]
        $red = $pixels[$offset + 2]
        $alpha = $pixels[$offset + 3]

        if ($alpha -eq 0) {
            $transparentCount++
        }
        if ($alpha -eq 255) {
            $opaqueCount++
        }
        if ($alpha -ge 192 -and $blue -ge 180 -and $green -ge 170 -and $red -le 90) {
            $cyanCount++
        }
        if ($alpha -ge 192 -and $blue -ge 190 -and $green -ge 190 -and $red -ge 180) {
            $promptCount++
        }
    }

    Assert-Condition ($transparentCount -gt 0 -and $opaqueCount -gt 0) `
        "$Description must contain both transparent and opaque pixels."
    Assert-Condition ($cyanCount -ge 2) "$Description lost the cyan star/docking motif."
    Assert-Condition ($promptCount -ge 2) "$Description lost the pale terminal prompt motif."
}

function Get-IcoEntries {
    param(
        [Parameter(Mandatory)]
        [byte[]] $Bytes,

        [Parameter(Mandatory)]
        [string] $Description
    )

    Assert-Condition ($Bytes.Length -ge 6) "$Description is too short to be an ICO."
    Assert-Condition ([BitConverter]::ToUInt16($Bytes, 0) -eq 0) "$Description has an invalid ICO reserved field."
    Assert-Condition ([BitConverter]::ToUInt16($Bytes, 2) -eq 1) "$Description is not an icon file."
    $count = [BitConverter]::ToUInt16($Bytes, 4)
    Assert-Condition ($count -eq $requiredSizes.Count) `
        "$Description contains $count images; expected $($requiredSizes.Count)."
    Assert-Condition ($Bytes.Length -ge 6 + (16 * $count)) "$Description has a truncated directory."

    $entries = @()
    for ($index = 0; $index -lt $count; $index++) {
        $entryOffset = 6 + (16 * $index)
        $width = if ($Bytes[$entryOffset] -eq 0) { 256 } else { [int] $Bytes[$entryOffset] }
        $height = if ($Bytes[$entryOffset + 1] -eq 0) { 256 } else { [int] $Bytes[$entryOffset + 1] }
        $planes = [BitConverter]::ToUInt16($Bytes, $entryOffset + 4)
        $bitsPerPixel = [BitConverter]::ToUInt16($Bytes, $entryOffset + 6)
        $length = [BitConverter]::ToUInt32($Bytes, $entryOffset + 8)
        $imageOffset = [BitConverter]::ToUInt32($Bytes, $entryOffset + 12)
        Assert-Condition ($width -eq $height) "$Description entry $index is not square."
        Assert-Condition ($planes -eq 1 -and $bitsPerPixel -eq 32) `
            "$Description ${width}px entry must declare one plane and 32 bits per pixel."
        Assert-Condition (($imageOffset + $length) -le $Bytes.Length) `
            "$Description ${width}px entry extends beyond the file."

        $imageBytes = [byte[]]::new($length)
        [Array]::Copy($Bytes, $imageOffset, $imageBytes, 0, $length)
        $entries += [pscustomobject]@{
            Size = $width
            Bytes = $imageBytes
        }
    }

    return $entries
}

Assert-Condition (Test-Path -LiteralPath $canonicalIconPath -PathType Leaf) `
    "Missing canonical icon: $canonicalIconPath"
$canonicalIconBytes = [System.IO.File]::ReadAllBytes($canonicalIconPath)
$entries = Get-IcoEntries $canonicalIconBytes 'Canonical ICO'
$actualSizes = @($entries | ForEach-Object { $_.Size } | Sort-Object)
Assert-Condition (($actualSizes -join ',') -eq (($requiredSizes | Sort-Object) -join ',')) `
    "Canonical ICO sizes are $($actualSizes -join ','); expected $($requiredSizes -join ',')."

foreach ($size in $requiredSizes) {
    $pngPath = Join-Path $canonicalDirectory "starboard-icon-${size}.png"
    Assert-Condition (Test-Path -LiteralPath $pngPath -PathType Leaf) "Missing PNG: $pngPath"
    $pngBytes = [System.IO.File]::ReadAllBytes($pngPath)
    Test-PngBytes $pngBytes $size "${size}px PNG"

    $entry = @($entries | Where-Object { $_.Size -eq $size })
    Assert-Condition ($entry.Count -eq 1) "Canonical ICO must contain exactly one ${size}px entry."
    Test-PngBytes $entry[0].Bytes $size "Canonical ICO ${size}px entry"
    Assert-Condition ([System.Linq.Enumerable]::SequenceEqual[byte]($pngBytes, $entry[0].Bytes)) `
        "Canonical ICO ${size}px entry differs from its source PNG."
}

$canonicalHash = (Get-FileHash -LiteralPath $canonicalIconPath -Algorithm SHA256).Hash
foreach ($productIconPath in $productIconPaths) {
    Assert-Condition (Test-Path -LiteralPath $productIconPath -PathType Leaf) `
        "Missing local product icon: $productIconPath"
    $productHash = (Get-FileHash -LiteralPath $productIconPath -Algorithm SHA256).Hash
    Assert-Condition ($productHash -eq $canonicalHash) `
        "Product icon differs from the canonical ICO: $productIconPath"
}

Write-Output "Starboard icon validation passed: $($requiredSizes -join ', ')px RGBA images and 2 local product copies."
