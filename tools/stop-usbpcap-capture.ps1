param(
    [string]$CaptureDir
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($CaptureDir)) {
    $latest = Get-ChildItem 'D:\ThemeEditor\PhoneControl\obj' -Directory -Filter 'usbpcap-capture-*' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        throw 'No usbpcap-capture-* directory found.'
    }

    $CaptureDir = $latest.FullName
}

$pidFile = Join-Path $CaptureDir 'pids.txt'
if (-not (Test-Path $pidFile)) {
    throw "PID file not found: $pidFile"
}

Get-Content $pidFile | Where-Object { $_ -match '^\d+$' } | ForEach-Object {
    Stop-Process -Id ([int]$_) -Force -ErrorAction SilentlyContinue
}

Start-Sleep -Milliseconds 500

"CAPTURE_DIR=$CaptureDir"
Get-ChildItem $CaptureDir -Filter '*.pcap' |
    Sort-Object Length -Descending |
    Select-Object Name,Length,LastWriteTime |
    Format-Table -AutoSize
