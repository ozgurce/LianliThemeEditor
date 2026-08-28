param(
    [string]$SourceRoot = "C:\Program Files\Lian-Li\L-Connect 3",
    [string]$OutputBase = "C:\Users\Ozgur\Documents\lconnect decompile",
    [string]$PreviousRawSnapshot = "C:\Users\Ozgur\Documents\lconnect decompile\pre_update_snapshot_20260802-001723\installed_files_sha256.csv",
    [string]$PreviousDecompile = "C:\Users\Ozgur\Documents\lconnect decompile\L-Connect3_decompiled_20260727-175213"
)

$ErrorActionPreference = "Stop"
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -Scope Global -ErrorAction SilentlyContinue) {
    $Global:PSNativeCommandUseErrorActionPreference = $false
}

function Get-SafeRelativeName {
    param([string]$RelativePath)
    return ($RelativePath -replace '[\\/]', '_')
}

function Read-HashCsv {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path) {
        return Import-Csv -LiteralPath $Path
    }
    return @()
}

function Invoke-NativeLogged {
    param(
        [string]$LogPath,
        [string[]]$CommandLine
    )

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $CommandLine[0] @($CommandLine[1..($CommandLine.Count - 1)]) *> $LogPath
        return $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousPreference
    }
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$out = Join-Path $OutputBase "L-Connect3_decompiled_$stamp"
$raw = Join-Path $out "raw_files"
$cs = Join-Path $out "decompiled_cs"
$il = Join-Path $out "decompiled_il"
$logs = Join-Path $out "logs"
$deob = Join-Path $out "deobfuscated"
$compare = Join-Path $out "comparison_vs_pre_update_raw"

New-Item -ItemType Directory -Path $raw,$cs,$il,$logs,$deob,$compare -Force | Out-Null

Copy-Item -Path (Join-Path $SourceRoot "*") -Destination $raw -Recurse -Force

$rawFiles = Get-ChildItem -LiteralPath $raw -Recurse -File
$inventory = foreach ($file in $rawFiles) {
    $rel = $file.FullName.Substring($raw.Length).TrimStart("\")
    [PSCustomObject]@{
        RelativePath = $rel
        Length = $file.Length
        LastWriteTime = $file.LastWriteTime.ToString("o")
        SHA256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash
    }
}

$inventory | Sort-Object RelativePath | Export-Csv -Path (Join-Path $out "raw_SHA256SUMS.csv") -NoTypeInformation -Encoding UTF8
$inventory | Sort-Object RelativePath | ForEach-Object { $_.RelativePath } | Set-Content -Path (Join-Path $out "raw_FILELIST.txt") -Encoding UTF8

$inventory |
    Group-Object { [IO.Path]::GetExtension($_.RelativePath).ToLowerInvariant() } |
    Sort-Object Name |
    ForEach-Object { "{0}`t{1}" -f ($(if ($_.Name) { $_.Name } else { "<no extension>" }), $_.Count) } |
    Set-Content -Path (Join-Path $out "file_extension_inventory.txt") -Encoding UTF8

$assemblies = Get-ChildItem -LiteralPath $raw -Recurse -File |
    Where-Object { $_.Extension -in ".exe", ".dll" } |
    Sort-Object FullName

$allExeDll = foreach ($asm in $assemblies) { $asm.FullName.Substring($raw.Length).TrimStart("\") }
$allExeDll | Set-Content -Path (Join-Path $out "all_exe_dll_relative.txt") -Encoding UTF8

$success = New-Object System.Collections.Generic.List[string]
$failed = New-Object System.Collections.Generic.List[string]
$native = New-Object System.Collections.Generic.List[string]
$deobSuccess = New-Object System.Collections.Generic.List[string]

foreach ($asm in $assemblies) {
    $rel = $asm.FullName.Substring($raw.Length).TrimStart("\")
    $safe = Get-SafeRelativeName $rel
    $asmOut = Join-Path $cs $safe
    $log = Join-Path $logs "$safe.ilspy.log.txt"

    New-Item -ItemType Directory -Path $asmOut -Force | Out-Null
    $exitCode = Invoke-NativeLogged -LogPath $log -CommandLine @("ilspycmd", "-p", "-o", $asmOut, $asm.FullName)
    if ($exitCode -eq 0) {
        $success.Add($rel)
        continue
    }

    Remove-Item -LiteralPath $asmOut -Recurse -Force -ErrorAction SilentlyContinue

    $ilOut = Join-Path $il "$safe.il"
    $exitCode = Invoke-NativeLogged -LogPath $ilOut -CommandLine @("ilspycmd", "-il", $asm.FullName)
    if ($exitCode -ne 0) {
        $native.Add($rel)
    }

    $failed.Add($rel)
}

$de4dot = Join-Path $OutputBase "tools\de4dot-cex\de4dot-x64.exe"
if (Test-Path -LiteralPath $de4dot) {
    foreach ($rel in @($failed)) {
        $asm = Join-Path $raw $rel
        $safe = Get-SafeRelativeName $rel
        $clean = Join-Path $deob "$safe.de4dot$([IO.Path]::GetExtension($asm))"
        $de4Log = Join-Path $logs "$safe.de4dot.log.txt"
        $exitCode = Invoke-NativeLogged -LogPath $de4Log -CommandLine @($de4dot, $asm, "-o", $clean)
        if ($exitCode -ne 0 -or -not (Test-Path -LiteralPath $clean)) {
            continue
        }

        $asmOut = Join-Path $cs "$safe.de4dot"
        New-Item -ItemType Directory -Path $asmOut -Force | Out-Null
        $exitCode = Invoke-NativeLogged -LogPath (Join-Path $logs "$safe.de4dot.ilspy.log.txt") -CommandLine @("ilspycmd", "-p", "-o", $asmOut, $clean)
        if ($exitCode -eq 0) {
            $deobSuccess.Add($rel)
        } else {
            Remove-Item -LiteralPath $asmOut -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

$success | Set-Content -Path (Join-Path $out "decompile_success.txt") -Encoding UTF8
$failed | Set-Content -Path (Join-Path $out "decompile_failed.txt") -Encoding UTF8
$deobSuccess | Set-Content -Path (Join-Path $out "decompile_success_after_de4dot.txt") -Encoding UTF8
$native | Set-Content -Path (Join-Path $out "native_or_not_managed_binaries.txt") -Encoding UTF8
$success | Set-Content -Path (Join-Path $out "managed_assemblies_relative.txt") -Encoding UTF8

$previous = Read-HashCsv $PreviousRawSnapshot
if ($previous.Count -gt 0) {
    $oldByPath = @{}
    foreach ($row in $previous) { $oldByPath[$row.RelativePath] = $row }
    $newByPath = @{}
    foreach ($row in $inventory) { $newByPath[$row.RelativePath] = $row }

    $added = foreach ($path in $newByPath.Keys) {
        if (-not $oldByPath.ContainsKey($path)) { $newByPath[$path] }
    }
    $removed = foreach ($path in $oldByPath.Keys) {
        if (-not $newByPath.ContainsKey($path)) { $oldByPath[$path] }
    }
    $changed = foreach ($path in $newByPath.Keys) {
        if ($oldByPath.ContainsKey($path) -and $oldByPath[$path].SHA256 -ne $newByPath[$path].SHA256) {
            [PSCustomObject]@{
                RelativePath = $path
                OldLength = $oldByPath[$path].Length
                NewLength = $newByPath[$path].Length
                OldLastWriteTime = $oldByPath[$path].LastWriteTime
                NewLastWriteTime = $newByPath[$path].LastWriteTime
                OldSHA256 = $oldByPath[$path].SHA256
                NewSHA256 = $newByPath[$path].SHA256
            }
        }
    }

    $added | Sort-Object RelativePath | Export-Csv -Path (Join-Path $compare "added_files.csv") -NoTypeInformation -Encoding UTF8
    $removed | Sort-Object RelativePath | Export-Csv -Path (Join-Path $compare "removed_files.csv") -NoTypeInformation -Encoding UTF8
    $changed | Sort-Object RelativePath | Export-Csv -Path (Join-Path $compare "changed_files.csv") -NoTypeInformation -Encoding UTF8

    [PSCustomObject]@{
        PreviousSnapshot = $PreviousRawSnapshot
        NewOutput = $out
        AddedFiles = @($added).Count
        RemovedFiles = @($removed).Count
        ChangedFiles = @($changed).Count
    } | ConvertTo-Json | Set-Content -Path (Join-Path $compare "summary.json") -Encoding UTF8
}

$readme = @"
# L-Connect 3 Decompiled Output

Source: $SourceRoot
Created: $((Get-Date).ToString("o"))
Decompiler: $(ilspycmd --version)
Output size: $([Math]::Round(((Get-ChildItem -LiteralPath $out -Recurse -File | Measure-Object Length -Sum).Sum / 1GB), 2)) GB

## Layout
- raw_files: original installed L-Connect 3 directory copied as-is.
- decompiled_cs: C# project output generated from managed .NET exe/dll files.
- deobfuscated: cleaned assemblies produced before decompiling obfuscated files.
- decompiled_il: IL output for assemblies that needed lower-level inspection.
- logs: per-assembly ILSpy/de4dot logs.
- comparison_vs_pre_update_raw: hash-based raw file comparison against the pre-update installed snapshot.

## Counts
- Raw files copied: $($rawFiles.Count)
- Exe/dll files found: $($assemblies.Count)
- Decompiled successfully in first ILSpy pass: $($success.Count)
- Decompiled after deobfuscation: $($deobSuccess.Count)
- Failed first-pass decompile: $($failed.Count)
- Native or non-managed exe/dll files: $($native.Count)

## Most relevant starting points
- decompiled_cs\L-Connect 3.exe
- decompiled_cs\L-Connect Editor.exe
- decompiled_cs\L-Connect ScreenAnimationEditor.exe
- decompiled_cs\L-Connect.Core.dll
- decompiled_cs\lianli.ThemeEngine.dll
- decompiled_cs\lianli.slv3.dll
- raw_files\Assets
"@

$readme | Set-Content -Path (Join-Path $out "README.md") -Encoding UTF8

Write-Output $out
