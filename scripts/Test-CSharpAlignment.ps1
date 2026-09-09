# Adapted from TRK-Server/Tools/Test-CSharpAlignment.ps1 (2026-09-09).
# Standalone read-only validation; no Git hook is installed.
[CmdletBinding()]
param(
    [switch]$Staged,

    [switch]$WorkingTree,

    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-CodeMasks {
    param(
        [string[]]$Lines
    )

    $masks = New-Object "System.Collections.Generic.List[string]"
    $mode = "Code"
    $rawQuoteCount = 0

    foreach ($line in $Lines) {
        $mask = New-Object char[] $line.Length
        for ($column = 0; $column -lt $mask.Length; $column++) {
            $mask[$column] = " "
        }

        $column = 0
        while ($column -lt $line.Length) {
            $current = $line[$column]
            $next = if ($column + 1 -lt $line.Length) { $line[$column + 1] } else { [char]0 }

            if ($mode -eq "BlockComment") {
                if ($current -eq "*" -and $next -eq "/") {
                    $mode = "Code"
                    $column += 2
                }
                else {
                    $column++
                }
                continue
            }

            if ($mode -eq "RawString") {
                $quoteCount = 0
                while ($column + $quoteCount -lt $line.Length -and
                       $line[$column + $quoteCount] -eq '"') {
                    $quoteCount++
                }

                if ($quoteCount -ge $rawQuoteCount) {
                    $mode = "Code"
                    $column += $rawQuoteCount
                }
                else {
                    $column++
                }
                continue
            }

            if ($mode -eq "VerbatimString") {
                if ($current -eq '"' -and $next -eq '"') {
                    $column += 2
                }
                elseif ($current -eq '"') {
                    $mode = "Code"
                    $column++
                }
                else {
                    $column++
                }
                continue
            }

            if ($mode -eq "String" -or $mode -eq "Character") {
                $terminator = if ($mode -eq "String") { '"' } else { "'" }
                if ($current -eq "\") {
                    $column += 2
                }
                elseif ($current -eq $terminator) {
                    $mode = "Code"
                    $column++
                }
                else {
                    $column++
                }
                continue
            }

            if ($current -eq "/" -and $next -eq "/") {
                break
            }
            if ($current -eq "/" -and $next -eq "*") {
                $mode = "BlockComment"
                $column += 2
                continue
            }
            if ($current -eq "'") {
                $mask[$column] = "C"
                $mode = "Character"
                $column++
                continue
            }
            if ($current -eq '"') {
                $mask[$column] = "S"
                $quoteCount = 0
                while ($column + $quoteCount -lt $line.Length -and
                       $line[$column + $quoteCount] -eq '"') {
                    $quoteCount++
                }

                if ($quoteCount -ge 3) {
                    $rawQuoteCount = $quoteCount
                    $mode = "RawString"
                    $column += $quoteCount
                }
                else {
                    $isVerbatim = $column -gt 0 -and $line[$column - 1] -eq "@"
                    $mode = if ($isVerbatim) { "VerbatimString" } else { "String" }
                    $column++
                }
                continue
            }

            $mask[$column] = $current
            $column++
        }

        if ($mode -eq "String" -or $mode -eq "Character") {
            $mode = "Code"
        }
        $masks.Add((-join $mask))
    }

    return $masks.ToArray()
}

function Get-FirstCodeColumn {
    param(
        [string]$Line
    )

    $match = [regex]::Match($Line, "\S")
    if ($match.Success) {
        return $match.Index
    }
    return -1
}

function Get-FirstStringPrefixColumn {
    param(
        [string]$Line,
        [string]$Mask,
        [int]$StartColumn
    )

    for ($column = $StartColumn; $column -lt $Line.Length; $column++) {
        if ($Line[$column] -ne '"' -or $Mask[$column] -ne "S") {
            continue
        }

        $prefixColumn = $column
        while ($prefixColumn -gt $StartColumn -and
               ($Line[$prefixColumn - 1] -eq "$" -or $Line[$prefixColumn - 1] -eq "@")) {
            $prefixColumn--
        }
        return $prefixColumn
    }

    return -1
}

function Test-IsCallOrDeclaration {
    param(
        [string]$Line,
        [int]$OpenColumn
    )

    if ($OpenColumn -le 0 -or [char]::IsWhiteSpace($Line[$OpenColumn - 1])) {
        return $false
    }

    return $Line[$OpenColumn - 1] -match '[\p{L}\p{Nd}_>\]\)!]'
}

function Test-RequiresInlineFirstArgument {
    param(
        [string]$Line,
        [int]$OpenColumn
    )

    if ($OpenColumn -le 0) {
        return $false
    }

    return $Line.Substring(0, $OpenColumn) -match '\bAddRange\s*$'
}

function Get-AlignmentViolations {
    param(
        [string[]]$Lines,
        [string]$DisplayPath,
        [object]$ChangedLines
    )

    $masks = @(Get-CodeMasks -Lines $Lines)
    $stack = New-Object System.Collections.ArrayList
    $violations = New-Object "System.Collections.Generic.List[object]"
    $braceDepth = 0
    $bracketDepth = 0

    for ($lineIndex = 0; $lineIndex -lt $masks.Count; $lineIndex++) {
        $mask = $masks[$lineIndex]
        $lineNumber = $lineIndex + 1
        $isChanged = $null -eq $ChangedLines -or $ChangedLines.Contains($lineNumber)

        if ($isChanged) {
            foreach ($group in $stack) {
                $group.Touched = $true
            }
        }
        if ($mask.Contains("=>")) {
            foreach ($group in $stack) {
                $group.Skip = $true
            }
        }

        $firstColumn = Get-FirstCodeColumn -Line $mask
        if ($firstColumn -ge 0 -and $stack.Count -gt 0) {
            $group = $stack[$stack.Count - 1]
            $effectiveBraceDepth = $braceDepth
            $effectiveBracketDepth = $bracketDepth
            $column = $firstColumn

            while ($column -lt $mask.Length) {
                if ($mask[$column] -eq "}") {
                    $effectiveBraceDepth--
                }
                elseif ($mask[$column] -eq "]") {
                    $effectiveBracketDepth--
                }
                elseif ([char]::IsWhiteSpace($mask[$column]) -eq $false) {
                    break
                }
                $column++
            }

            if ($effectiveBraceDepth -eq $group.BraceDepth -and
                $effectiveBracketDepth -eq $group.BracketDepth) {
                if ($group.IsExceptionConstructor -and $group.ExpectedStringColumn -ge 0) {
                    $stringColumn = -1
                    if ($group.ExpectStringContinuation) {
                        $stringColumn = Get-FirstStringPrefixColumn -Line $Lines[$lineIndex] -Mask $mask -StartColumn $firstColumn
                    }
                    elseif ($mask[$firstColumn] -eq "+") {
                        $afterPlus = [regex]::Match($mask.Substring($firstColumn + 1), "\S")
                        if ($afterPlus.Success) {
                            $stringColumn = Get-FirstStringPrefixColumn -Line $Lines[$lineIndex] -Mask $mask -StartColumn ($firstColumn + 1)
                            if ($stringColumn -eq $firstColumn + 1 + $afterPlus.Index) {
                                $group.Pending.Add([pscustomobject]@{
                                    Path = $DisplayPath
                                    Line = $lineNumber
                                    ActualColumn = $firstColumn
                                    ExpectedColumn = $group.ExpectedStringColumn
                                    Kind = "ExceptionStringPlusPlacement"
                                })
                            }
                        }
                    }
                    $group.ExpectStringContinuation = $false

                    if ($stringColumn -eq $firstColumn -and $stringColumn -ne $group.ExpectedStringColumn) {
                        $group.Pending.Add([pscustomobject]@{
                            Path = $DisplayPath
                            Line = $lineNumber
                            ActualColumn = $stringColumn
                            ExpectedColumn = $group.ExpectedStringColumn
                            Kind = "ExceptionStringAlignment"
                        })
                    }
                }

                $isArgumentBoundary = $group.ExpectContinuation -or
                                      $mask[$firstColumn] -eq "{" -or
                                      $mask[$firstColumn] -eq "}"
                $group.ExpectContinuation = $false

                if ($group.IsTarget -and $group.Skip -eq $false -and
                    $group.HasInlineFirst -and $mask[$firstColumn] -ne ")" -and
                    $isArgumentBoundary -and $firstColumn -ne $group.ExpectedColumn) {
                    $group.Pending.Add([pscustomobject]@{
                        Path = $DisplayPath
                        Line = $lineNumber
                        ActualColumn = $firstColumn
                        ExpectedColumn = $group.ExpectedColumn
                        Kind = "Alignment"
                    })
                }

                if ($group.IsTarget -and $group.RequiresInlineFirstArgument -and
                    $group.HasInlineFirst -eq $false -and $group.InlineFirstViolationRecorded -eq $false -and
                    $mask[$firstColumn] -ne ")") {
                    $group.InlineFirstViolationRecorded = $true
                    $group.Pending.Add([pscustomobject]@{
                        Path = $DisplayPath
                        Line = $lineNumber
                        ActualColumn = $firstColumn
                        ExpectedColumn = -1
                        Kind = "AddRangeInlineFirst"
                    })
                }
            }
        }

        for ($column = 0; $column -lt $mask.Length; $column++) {
            $current = $mask[$column]

            if ($current -eq "(") {
                $inline = [regex]::Match($mask.Substring($column + 1), "\S")
                $expectedColumn = if ($inline.Success) { $column + 1 + $inline.Index } else { -1 }
                $isExceptionConstructor = $mask.Substring(0, $column) -match
                                          '\bthrow\s+new\s+[\p{L}_][\p{L}\p{Nd}_\.:]*Exception\s*$'
                $expectedStringColumn = if ($isExceptionConstructor) {
                    Get-FirstStringPrefixColumn -Line $Lines[$lineIndex] -Mask $mask -StartColumn ($column + 1)
                }
                else {
                    -1
                }
                $group = [pscustomobject]@{
                    IsTarget = Test-IsCallOrDeclaration -Line $mask -OpenColumn $column
                    IsExceptionConstructor = $isExceptionConstructor
                    HasInlineFirst = $inline.Success -and $mask[$expectedColumn] -ne ")"
                    RequiresInlineFirstArgument = Test-RequiresInlineFirstArgument -Line $mask -OpenColumn $column
                    InlineFirstViolationRecorded = $false
                    ExpectedColumn = $expectedColumn
                    ExpectedStringColumn = $expectedStringColumn
                    BraceDepth = $braceDepth
                    BracketDepth = $bracketDepth
                    OpenLine = $lineIndex
                    Touched = $isChanged
                    Skip = $mask.Substring($column + 1).Contains("=>")
                    ExpectContinuation = $false
                    ExpectStringContinuation = $false
                    Pending = New-Object "System.Collections.Generic.List[object]"
                }
                [void]$stack.Add($group)
                continue
            }

            if ($current -eq ")") {
                if ($stack.Count -gt 0) {
                    $group = $stack[$stack.Count - 1]
                    $stack.RemoveAt($stack.Count - 1)
                    if ($group.IsTarget -and $group.Skip -eq $false -and
                        $group.Touched -and $lineIndex -gt $group.OpenLine) {
                        foreach ($violation in $group.Pending) {
                            $violations.Add($violation)
                        }
                    }
                }
                continue
            }

            if ($current -eq "{") {
                $braceDepth++
            }
            elseif ($current -eq "}") {
                $braceDepth--
            }
            elseif ($current -eq "[") {
                $bracketDepth++
            }
            elseif ($current -eq "]") {
                $bracketDepth--
            }
        }

        $lastCode = [regex]::Match($mask, "\S(?=\s*$)")
        if ($lastCode.Success -and $lastCode.Value -eq "," -and $stack.Count -gt 0) {
            $group = $stack[$stack.Count - 1]
            if ($braceDepth -eq $group.BraceDepth -and $bracketDepth -eq $group.BracketDepth) {
                $group.ExpectContinuation = $true
            }
        }
        elseif ($lastCode.Success -and $lastCode.Value -eq "+" -and $stack.Count -gt 0) {
            $group = $stack[$stack.Count - 1]
            if ($group.IsExceptionConstructor -and $group.ExpectedStringColumn -ge 0 -and
                $braceDepth -eq $group.BraceDepth -and $bracketDepth -eq $group.BracketDepth) {
                $group.ExpectStringContinuation = $true
            }
        }
    }

    return $violations.ToArray()
}

function Get-StagedAddedLines {
    param(
        [string]$RepoPath,
        [string[]]$DiffLines
    )

    $result = New-Object "System.Collections.Generic.HashSet[int]"
    $newLineNumber = 0
    $inHunk = $false

    if ($null -eq $DiffLines) {
        $DiffLines = @(& git diff --cached --unified=0 --no-color --diff-filter=ACMR -- $RepoPath)
        if ($LASTEXITCODE -ne 0) {
            throw "Could not read staged diff: $RepoPath"
        }
    }

    foreach ($diffLine in $DiffLines) {
        $hunk = [regex]::Match($diffLine, '^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@')
        if ($hunk.Success) {
            $newLineNumber = [int]$hunk.Groups[1].Value
            $inHunk = $true
        }
        elseif ($inHunk -and $diffLine.StartsWith("+")) {
            [void]$result.Add($newLineNumber)
            $newLineNumber++
        }
        elseif ($inHunk -and $diffLine.StartsWith("-") -eq $false -and
                $diffLine.StartsWith("\ No newline") -eq $false) {
            $newLineNumber++
        }
    }

    return ,$result
}

function Assert-ViolationCount {
    param(
        [string]$Name,
        [string[]]$Lines,
        [int]$ExpectedCount,
        [object]$ChangedLines = $null
    )

    $actual = @(Get-AlignmentViolations -Lines $Lines -DisplayPath $Name -ChangedLines $ChangedLines)
    if ($actual.Count -ne $ExpectedCount) {
        throw "$Name self-test failed: expected=$ExpectedCount, actual=$($actual.Count)"
    }
}

function Invoke-SelfTest {
    $head = "    private async Task<Result> RunAsync(string first,"
    $column = $head.IndexOf("string first")

    Assert-ViolationCount -Name "valid-declaration" -ExpectedCount 0 -Lines @(
        $head,
        ("".PadLeft($column) + "string second)"),
        "    {",
        "    }"
    )
    Assert-ViolationCount -Name "off-by-one-declaration" -ExpectedCount 1 -Lines @(
        $head,
        ("".PadLeft($column + 1) + "string second)"),
        "    {",
        "    }"
    )

    $call = "    await ExecuteAsync(new ClaimParams"
    $column = $call.IndexOf("new ClaimParams")
    Assert-ViolationCount -Name "valid-object-initializer" -ExpectedCount 0 -Lines @(
        $call,
        ("".PadLeft($column) + "{"),
        ("".PadLeft($column + 4) + "ModeId = 1"),
        ("".PadLeft($column) + "}, GetContext());")
    )

    $call = '    Throw("first",'
    $column = $call.IndexOf('"first"')
    Assert-ViolationCount -Name "valid-string-argument" -ExpectedCount 0 -Lines @(
        $call,
        ("".PadLeft($column) + '"second");')
    )

    $exception = '    throw new ApiServerException(GameResultCode.NotOwned, $"Equipment sub option not found. " +'
    $column = $exception.IndexOf('$"')
    Assert-ViolationCount -Name "valid-exception-string" -ExpectedCount 0 -Lines @(
        $exception,
        ("".PadLeft($column) + '$"SubOptionInstanceId: {subOptionInstanceId}");')
    )
    Assert-ViolationCount -Name "off-by-one-exception-string" -ExpectedCount 1 -Lines @(
        $exception,
        ("".PadLeft($column + 1) + '$"SubOptionInstanceId: {subOptionInstanceId}");')
    )
    Assert-ViolationCount -Name "leading-plus-exception-string" -ExpectedCount 1 -Lines @(
        $exception.Substring(0, $exception.Length - 2),
        ("".PadLeft($column) + '+ $"SubOptionInstanceId: {subOptionInstanceId}");')
    )
    Assert-ViolationCount -Name "unrelated-string-concatenation" -ExpectedCount 0 -Lines @(
        '    var message = $"Equipment sub option not found. " +',
        '                  $"SubOptionInstanceId: {subOptionInstanceId}";'
    )

    Assert-ViolationCount -Name "valid-add-range-inline-first" -ExpectedCount 0 -Lines @(
        "    values.AddRange(source.Select(value => new Item",
        "                                            {",
        "                                                Value = value",
        "                                            }));"
    )
    $addRangeCall = "    result.Items.AddRange(ParseFile(sourceFile,"
    $column = $addRangeCall.IndexOf("sourceFile")
    Assert-ViolationCount -Name "valid-add-range-wrapped-inner-call" -ExpectedCount 0 -Lines @(
        $addRangeCall,
        ("".PadLeft($column) + "tableName, result));")
    )
    Assert-ViolationCount -Name "invalid-add-range-newline-first" -ExpectedCount 1 -Lines @(
        "    values.AddRange(",
        "        source);"
    )
    Assert-ViolationCount -Name "unrelated-call-newline-first" -ExpectedCount 0 -Lines @(
        "    Execute(",
        "        source);"
    )

    $singleChangedLine = Get-StagedAddedLines -RepoPath "self-test.cs" -DiffLines @(
        "@@ -1 +1 @@",
        "+changed"
    )
    if ($singleChangedLine -isnot [System.Collections.Generic.HashSet[int]] -or
        $singleChangedLine.Count -ne 1 -or $singleChangedLine.Contains(1) -eq $false) {
        throw "single-changed-line self-test failed"
    }

    $changedLines = New-Object "System.Collections.Generic.HashSet[int]"
    [void]$changedLines.Add(2)
    Assert-ViolationCount -Name "changed-group" -ExpectedCount 1 -ChangedLines $changedLines -Lines @(
        $head,
        ("".PadLeft($head.IndexOf("string first") + 1) + "string second)"),
        "    {",
        "    }"
    )

    $changedLines = New-Object "System.Collections.Generic.HashSet[int]"
    [void]$changedLines.Add(4)
    Assert-ViolationCount -Name "unchanged-legacy-group" -ExpectedCount 0 -ChangedLines $changedLines -Lines @(
        $head,
        ("".PadLeft($head.IndexOf("string first") + 1) + "string second)"),
        "    {",
        "        return;",
        "    }"
    )

    Write-Output "C# alignment validator self-test passed."
}

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}

$repoRoot = & git rev-parse --show-toplevel 2>$null
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    throw "Run this validator inside a Git repository."
}

$allViolations = New-Object "System.Collections.Generic.List[object]"
Push-Location $repoRoot
try {
    if ($WorkingTree -and $Staged) {
        throw "Choose either -WorkingTree or -Staged."
    }
    $paths = if ($WorkingTree) {
        @(& git ls-files --cached --others --exclude-standard -- "src/**/*.cs" "tests/**/*.cs")
    }
    else {
        @(& git diff --cached --name-only --diff-filter=ACMR -- "*.cs")
    }
    if ($LASTEXITCODE -ne 0) { throw "Could not enumerate C# files." }
    foreach ($repoPath in $paths) {
        if ([string]::IsNullOrWhiteSpace($repoPath)) {
            continue
        }

        $changedLines = $null
        if ($WorkingTree) {
            if ($repoPath -match '(^|/)(bin|obj)/') { continue }
            if ((Test-Path -LiteralPath $repoPath -PathType Leaf) -eq $false) { continue }
            $lines = @(Get-Content -LiteralPath $repoPath)
        }
        else {
            $lines = @(& git show ":$repoPath")
            if ($LASTEXITCODE -ne 0) { throw "Could not read staged file: $repoPath" }
            $changedLines = Get-StagedAddedLines -RepoPath $repoPath
        }
        foreach ($violation in @(Get-AlignmentViolations -Lines $lines -DisplayPath $repoPath -ChangedLines $changedLines)) {
            $allViolations.Add($violation)
        }
    }
}
finally {
    Pop-Location
}

if ($allViolations.Count -gt 0) {
    Write-Output "C# multiline alignment check failed."
    foreach ($violation in $allViolations) {
        if ($violation.Kind -eq "AddRangeInlineFirst") {
            Write-Output "$($violation.Path):$($violation.Line): AddRange first argument must stay on the opening line."
        }
        elseif ($violation.Kind -eq "ExceptionStringPlusPlacement") {
            Write-Output "$($violation.Path):$($violation.Line): place + after the previous exception message string and align the continuation string at 0-based column $($violation.ExpectedColumn)."
        }
        elseif ($violation.Kind -eq "ExceptionStringAlignment") {
            Write-Output "$($violation.Path):$($violation.Line): exception message continuation starts at 0-based column $($violation.ActualColumn); expected $($violation.ExpectedColumn)."
        }
        else {
            Write-Output "$($violation.Path):$($violation.Line): continuation starts at 0-based column $($violation.ActualColumn); expected $($violation.ExpectedColumn)."
        }
    }
    Write-Output "See docs/code-style.md: C# method call arguments and declaration parameters."
    exit 1
}

Write-Output "C# multiline alignment check passed."
exit 0
