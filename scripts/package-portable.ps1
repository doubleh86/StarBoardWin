[CmdletBinding()]
param(
    [string]$DotNetPath = "",
    [switch]$SelfContained
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Get-Sha256Hash {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath
    )

    $fileHashCommand = Get-Command Get-FileHash -ErrorAction SilentlyContinue
    if ($null -ne $fileHashCommand) {
        return (Get-FileHash -LiteralPath $LiteralPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }

    $stream = [System.IO.File]::Open(
        $LiteralPath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            $hashBytes = $sha256.ComputeHash($stream)
            return [System.BitConverter]::ToString($hashBytes).Replace("-", "").ToLowerInvariant()
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-SafeChildPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ParentPath,

        [Parameter(Mandatory = $true)]
        [string]$ChildPath
    )

    $normalizedParent = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $normalizedChild = [System.IO.Path]::GetFullPath($ChildPath)
    $parentPrefix = $normalizedParent + [System.IO.Path]::DirectorySeparatorChar
    if (-not $normalizedChild.StartsWith(
            $parentPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe output path outside the verified parent: $normalizedChild"
    }
}

function Remove-SafeDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ParentPath,

        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    Assert-SafeChildPath -ParentPath $ParentPath -ChildPath $TargetPath
    if (-not (Test-Path -LiteralPath $TargetPath)) {
        return
    }

    $targetItem = Get-Item -LiteralPath $TargetPath -Force
    if (($targetItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to clean a staging path that is a reparse point: $TargetPath"
    }

    $deletePath = $targetItem.FullName
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT -and
        -not $deletePath.StartsWith("\\?\", [System.StringComparison]::Ordinal)) {
        $deletePath = "\\?\$deletePath"
    }

    [System.IO.Directory]::Delete($deletePath, $true)
}

function New-DeterministicArchive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,

        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,

        [Parameter(Mandatory = $true)]
        [DateTimeOffset]$EntryTimestamp
    )

    $archiveStream = [System.IO.File]::Open(
        $ArchivePath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $archiveStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $true)
        try {
            $sourcePrefix = $SourceDirectory.TrimEnd(
                [System.IO.Path]::DirectorySeparatorChar) +
                [System.IO.Path]::DirectorySeparatorChar
            $files = Get-ChildItem -LiteralPath $SourceDirectory -File -Recurse |
                Sort-Object {
                    $_.FullName.Substring($sourcePrefix.Length).Replace("\", "/")
                }
            foreach ($file in $files) {
                $entryName = $file.FullName.Substring($sourcePrefix.Length).Replace("\", "/")
                $entry = $archive.CreateEntry(
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $EntryTimestamp
                $inputStream = $file.OpenRead()
                $outputStream = $entry.Open()
                try {
                    $inputStream.CopyTo($outputStream)
                }
                finally {
                    $outputStream.Dispose()
                    $inputStream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $archiveStream.Dispose()
    }
}

function Test-PortableContents {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory,

        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [bool]$IsSelfContained
    )

    $requiredPaths = @(
        "Starboard.exe",
        "Starboard.dll",
        "Starboard.runtimeconfig.json",
        "LICENSE",
        "README.md",
        "THIRD-PARTY-NOTICES.md",
        "release-metadata.json",
        "Assets/Starboard.ico",
        "Renderer/index.html",
        "Renderer/app.js",
        "Renderer/app.css",
        "Renderer/xterm-LICENSE.txt",
        "Renderer/xterm-addon-fit-LICENSE.txt",
        "ThirdParty/Microsoft.Web.WebView2-LICENSE.txt",
        "ThirdParty/Microsoft.Web.WebView2-NOTICE.txt"
    )
    foreach ($relativePath in $requiredPaths) {
        $requiredPath = Join-Path $PublishDirectory $relativePath
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Required portable file is missing: $relativePath"
        }
    }

    foreach ($runtimeFile in @("coreclr.dll", "System.Private.CoreLib.dll", "PresentationFramework.dll")) {
        $runtimeExists = Test-Path -LiteralPath (Join-Path $PublishDirectory $runtimeFile) -PathType Leaf
        if ($runtimeExists -ne $IsSelfContained) {
            throw "Runtime contents do not match the selected deployment mode: $runtimeFile"
        }
    }

    $metadata = Get-Content -LiteralPath (Join-Path $PublishDirectory "release-metadata.json") -Raw |
        ConvertFrom-Json
    if ($metadata.selfContained -ne $IsSelfContained) {
        throw "Release metadata does not match the selected deployment mode."
    }

    if ($IsSelfContained -eq $false) {
        $runtimeConfiguration = Get-Content -LiteralPath (
            Join-Path $PublishDirectory "Starboard.runtimeconfig.json") -Raw | ConvertFrom-Json
        $desktopFrameworks = @($runtimeConfiguration.runtimeOptions.frameworks |
            Where-Object { $_.name -eq "Microsoft.WindowsDesktop.App" })
        if ($desktopFrameworks.Count -ne 1) {
            throw "The lightweight package must require the installed .NET Desktop Runtime."
        }
    }

    $publishPrefix = $PublishDirectory.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    $files = Get-ChildItem -LiteralPath $PublishDirectory -File -Recurse
    $sensitiveCaptureNames = @(
        "command.txt",
        "command-history.txt",
        "command-output.txt",
        "terminal-command.txt",
        "terminal-output.txt",
        "terminal-transcript.txt",
        "output.txt"
    )
    foreach ($file in $files) {
        $relativePath = $file.FullName.Substring($publishPrefix.Length).Replace("\", "/")
        $segments = $relativePath.Split("/")
        if ($file.Name -in @(
                "settings.json",
                "settings.json.bak",
                "Starboard.settings.json",
                "workspace.json",
                "workspace.json.bak",
                "saved-tabs.json",
                "saved-tabs.json.bak",
                "saved-tabs.json.tmp") -or
            $file.Name -in $sensitiveCaptureNames -or
            $file.Name -match "(?i)^starboard\.log(?:\.[0-9]+)?$" -or
            $file.Extension -in @(".bak", ".tmp", ".log", ".pdb", ".dmp", ".hdmp", ".sqlite", ".db") -or
            $segments -contains "WebView2" -or
            $segments -contains "WebView2Data" -or
            $segments -contains "EBWebView" -or
            $segments -contains "User Data" -or
            $segments -contains "Logs") {
            throw "Runtime or developer-only data was included in the package: $relativePath"
        }
    }

    $textExtensions = @(
        ".config",
        ".css",
        ".html",
        ".js",
        ".json",
        ".md",
        ".txt",
        ".xml"
    )
    $normalizedRepositoryRoot = $RepositoryRoot.Replace("\", "/")
    foreach ($file in $files) {
        if ($file.Extension -notin $textExtensions) {
            continue
        }

        $content = [System.IO.File]::ReadAllText($file.FullName)
        $normalizedContent = $content.Replace("\", "/")
        if ($normalizedContent.IndexOf(
                $normalizedRepositoryRoot,
                [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $content -match "(?i)[A-Z]:[\\/](Users|PrivateProject|Work)[\\/]") {
            throw "A development-machine absolute path was included in $($file.FullName)."
        }
    }
}

function Invoke-PortableSmoke {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath,

        [Parameter(Mandatory = $true)]
        [string]$Version,

        [Parameter(Mandatory = $true)]
        [string]$Commit
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $ExecutablePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.Arguments = "--portable-smoke-test --expected-version $Version --expected-commit $Commit"

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if ($process.Start() -ne $true) {
            throw "The extracted Starboard executable did not start."
        }

        if ($process.WaitForExit(30000) -ne $true) {
            $process.Kill()
            throw "The extracted Starboard smoke check did not exit within 30 seconds."
        }

        if ($process.ExitCode -ne 0) {
            throw "The extracted Starboard smoke check failed with exit code $($process.ExitCode)."
        }
    }
    finally {
        $process.Dispose()
    }
}

$scriptDirectory = Split-Path -Parent $PSCommandPath
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $scriptDirectory ".."))
$userDotNetPath = Join-Path (
    [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) ".dotnet/dotnet.exe"
if ([string]::IsNullOrWhiteSpace($DotNetPath)) {
    if (Test-Path -LiteralPath $userDotNetPath -PathType Leaf) {
        $DotNetPath = $userDotNetPath
    }
    else {
        $dotNetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop
        $DotNetPath = $dotNetCommand.Source
    }
}

$reportedGitRoot = (& git -C $repositoryRoot rev-parse --show-toplevel)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to verify the repository root with Git."
}

$verifiedGitRoot = [System.IO.Path]::GetFullPath(
    $reportedGitRoot.Trim()).TrimEnd("\", "/")
$normalizedRepositoryRoot = $repositoryRoot.TrimEnd("\", "/")
if ([string]::Equals(
        $verifiedGitRoot,
        $normalizedRepositoryRoot,
        [System.StringComparison]::OrdinalIgnoreCase) -ne $true) {
    throw "The packaging script is not running from its verified repository root."
}

[xml]$buildProperties = Get-Content -LiteralPath (
    Join-Path $repositoryRoot "Directory.Build.props") -Raw
$versionNodes = @(
    @($buildProperties.Project.PropertyGroup.VersionPrefix) |
        Where-Object { [string]::IsNullOrWhiteSpace([string]$_) -eq $false }
)
if ($versionNodes.Count -ne 1) {
    throw "Directory.Build.props must define exactly one VersionPrefix."
}

$version = ([string]$versionNodes[0]).Trim()
if ($version -notmatch "^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$") {
    throw "VersionPrefix is not a safe semantic version: $version"
}

$commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch "^[0-9a-fA-F]{40}$") {
    throw "Unable to resolve a full Git build commit."
}

$commitTimestampText = (& git -C $repositoryRoot show -s --format=%ct HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Unable to resolve the Git commit timestamp."
}

$commitTimestampSeconds = [long]::Parse(
    $commitTimestampText,
    [System.Globalization.CultureInfo]::InvariantCulture)
$archiveTimestamp = [DateTimeOffset]::FromUnixTimeSeconds(
    $commitTimestampSeconds).ToUniversalTime()
$minimumZipTimestamp = [DateTimeOffset]::new(
    1980,
    1,
    1,
    0,
    0,
    0,
    [TimeSpan]::Zero)
if ($archiveTimestamp -lt $minimumZipTimestamp) {
    $archiveTimestamp = $minimumZipTimestamp
}

$portableRoot = Join-Path $repositoryRoot "out/portable"
$versionRoot = Join-Path $portableRoot $version
$isSelfContained = $SelfContained.IsPresent
$selfContainedArgument = $isSelfContained.ToString().ToLowerInvariant()
$packageRoot = $versionRoot
$archiveName = "Starboard-$version-win-x64.zip"
if ($isSelfContained -eq $false) {
    $packageRoot = Join-Path $versionRoot "framework-dependent"
    $archiveName = "Starboard-$version-win-x64-framework-dependent.zip"
}

$stagingRoot = Join-Path $packageRoot "staging"
$publishDirectory = Join-Path $stagingRoot "publish"
$smokeDirectory = Join-Path $stagingRoot "smoke"
$buildArtifactsPath = Join-Path $stagingRoot "build"
$archivePath = Join-Path $packageRoot $archiveName
$hashPath = "$archivePath.sha256"

Assert-SafeChildPath -ParentPath $repositoryRoot -ChildPath $portableRoot
Assert-SafeChildPath -ParentPath $portableRoot -ChildPath $versionRoot
Assert-SafeChildPath -ParentPath $versionRoot -ChildPath $stagingRoot
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
Remove-SafeDirectory -ParentPath $packageRoot -TargetPath $stagingRoot
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $smokeDirectory -Force | Out-Null

$solutionPath = Join-Path $repositoryRoot "Starboard.Windows.sln"
$applicationProject = Join-Path $repositoryRoot "src/Starboard.Windows/Starboard.Windows.csproj"
$buildPropertiesArguments = @(
    "-p:StarboardBuildCommit=$commit",
    "-p:SourceRevisionId=$commit",
    "-p:ContinuousIntegrationBuild=true",
    "-p:PathMap=$repositoryRoot=/_/",
    "-p:ArtifactsPath=$buildArtifactsPath"
)

Push-Location $repositoryRoot
try {
    Invoke-CheckedCommand -FilePath $DotNetPath -Arguments @(
        "build-server",
        "shutdown"
    )
    Invoke-CheckedCommand -FilePath $DotNetPath -Arguments (@(
        "restore",
        $solutionPath,
        "--runtime",
        "win-x64",
        "--disable-parallel",
        "--disable-build-servers",
        "-maxcpucount:1"
    ) + $buildPropertiesArguments)
    Invoke-CheckedCommand -FilePath $DotNetPath -Arguments (@(
        "build",
        $solutionPath,
        "--configuration",
        "Release",
        "--no-restore",
        "--disable-build-servers",
        "-maxcpucount:1"
    ) + $buildPropertiesArguments)
    Invoke-CheckedCommand -FilePath $DotNetPath -Arguments (@(
        "test",
        $solutionPath,
        "--configuration",
        "Release",
        "--no-build",
        "--no-restore",
        "--disable-build-servers",
        "-maxcpucount:1"
    ) + $buildPropertiesArguments)
    Invoke-CheckedCommand -FilePath $DotNetPath -Arguments (@(
        "publish",
        $applicationProject,
        "--configuration",
        "Release",
        "--runtime",
        "win-x64",
        "--self-contained",
        $selfContainedArgument,
        "--no-restore",
        "--disable-build-servers",
        "-maxcpucount:1",
        "--output",
        $publishDirectory,
        "-p:PublishSingleFile=false",
        "-p:DebugSymbols=false",
        "-p:DebugType=None"
    ) + $buildPropertiesArguments)
}
finally {
    Pop-Location
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot "README.md") `
    -Destination (Join-Path $publishDirectory "README.md")
New-Item -ItemType Directory -Path (Join-Path $publishDirectory "Assets") -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repositoryRoot "src/Starboard.Windows/Assets/Starboard.ico") `
    -Destination (Join-Path $publishDirectory "Assets/Starboard.ico")

$licenseText = @"
MIT License

Copyright (c) 2026 Starboard contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
"@
[System.IO.File]::WriteAllText(
    (Join-Path $publishDirectory "LICENSE"),
    $licenseText.Replace("`r`n", "`n"),
    [System.Text.UTF8Encoding]::new($false))

$releaseMetadata = [ordered]@{
    product = "Starboard for Windows"
    productVersion = $version
    buildCommit = $commit
    runtimeIdentifier = "win-x64"
    selfContained = $isSelfContained
    archive = $archiveName
    sourceDateUtc = $archiveTimestamp.ToString("O")
}
$metadataJson = $releaseMetadata | ConvertTo-Json
[System.IO.File]::WriteAllText(
    (Join-Path $publishDirectory "release-metadata.json"),
    $metadataJson.Replace("`r`n", "`n") + "`n",
    [System.Text.UTF8Encoding]::new($false))

$publishedVersionInfo = (
    Get-Item -LiteralPath (Join-Path $publishDirectory "Starboard.exe")
).VersionInfo
$expectedFileVersion = "$version.0"
$expectedProductVersion = "$version+$commit"
if ([string]::Equals(
        $publishedVersionInfo.FileVersion,
        $expectedFileVersion,
        [System.StringComparison]::Ordinal) -ne $true -or
    [string]::Equals(
        $publishedVersionInfo.ProductVersion,
        $expectedProductVersion,
        [System.StringComparison]::OrdinalIgnoreCase) -ne $true) {
    throw "Published executable version metadata does not match the package metadata."
}

Test-PortableContents `
    -PublishDirectory $publishDirectory `
    -RepositoryRoot $repositoryRoot `
    -IsSelfContained $isSelfContained

foreach ($outputFile in @($archivePath, $hashPath)) {
    Assert-SafeChildPath -ParentPath $packageRoot -ChildPath $outputFile
    if (Test-Path -LiteralPath $outputFile) {
        $outputItem = Get-Item -LiteralPath $outputFile -Force
        if (($outputItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to replace a package output that is a reparse point: $outputFile"
        }

        Remove-Item -LiteralPath $outputFile -Force
    }
}

New-DeterministicArchive `
    -SourceDirectory $publishDirectory `
    -ArchivePath $archivePath `
    -EntryTimestamp $archiveTimestamp

$archiveHash = Get-Sha256Hash -LiteralPath $archivePath
$reproductionArchivePath = Join-Path $stagingRoot "reproduction-check.zip"
Assert-SafeChildPath -ParentPath $stagingRoot -ChildPath $reproductionArchivePath
New-DeterministicArchive `
    -SourceDirectory $publishDirectory `
    -ArchivePath $reproductionArchivePath `
    -EntryTimestamp $archiveTimestamp
$reproductionHash = Get-Sha256Hash -LiteralPath $reproductionArchivePath
Remove-Item -LiteralPath $reproductionArchivePath -Force
if ([string]::Equals(
        $archiveHash,
        $reproductionHash,
        [System.StringComparison]::Ordinal) -ne $true) {
    throw "Recreating the portable ZIP from the same staging content changed its SHA-256."
}

[System.IO.File]::WriteAllText(
    $hashPath,
    "$archiveHash  $archiveName`n",
    [System.Text.UTF8Encoding]::new($false))
$recordedHash = ((Get-Content -LiteralPath $hashPath -Raw).Split(
    [char[]]@(" ", "`r", "`n", "`t"),
    [System.StringSplitOptions]::RemoveEmptyEntries))[0]
$verifiedHash = Get-Sha256Hash -LiteralPath $archivePath
if ([string]::Equals(
        $recordedHash,
        $verifiedHash,
        [System.StringComparison]::OrdinalIgnoreCase) -ne $true) {
    throw "The recorded SHA-256 does not match the portable ZIP."
}

[System.IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $smokeDirectory)
Test-PortableContents `
    -PublishDirectory $smokeDirectory `
    -RepositoryRoot $repositoryRoot `
    -IsSelfContained $isSelfContained
Invoke-PortableSmoke `
    -ExecutablePath (Join-Path $smokeDirectory "Starboard.exe") `
    -Version $version `
    -Commit $commit

Write-Host "Portable package: $archivePath"
Write-Host "SHA-256: $archiveHash"
Write-Host "Smoke check: passed"
