$ErrorActionPreference = 'Stop'

$usb = 'C:\Program Files\USBPcap\USBPcapCMD.exe'
if (-not (Test-Path $usb)) {
    throw "USBPcapCMD.exe not found at $usb"
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$captureDir = Join-Path 'D:\ThemeEditor\PhoneControl\obj' "usbpcap-capture-$timestamp"
New-Item -ItemType Directory -Force -Path $captureDir | Out-Null

$pidFile = Join-Path $captureDir 'pids.txt'
$infoFile = Join-Path $captureDir 'capture-info.txt'
$started = @()

1..10 | ForEach-Object {
    $pcap = Join-Path $captureDir "USBPcap$_.pcap"
    $arguments = "-d \\.\USBPcap$_ -o `"$pcap`" -A --inject-descriptors"
    $process = Start-Process -FilePath $usb -ArgumentList $arguments -PassThru -WindowStyle Hidden
    Start-Sleep -Milliseconds 350

    if (-not $process.HasExited) {
        $started += [pscustomobject]@{
            Id = $process.Id
            Device = "USBPcap$_"
            File = $pcap
        }
    }
}

if ($started.Count -eq 0) {
    throw 'No USBPcap capture device could be opened. Run this script from an elevated PowerShell window.'
}

$started.Id | Set-Content -Encoding ASCII -Path $pidFile
$started | Format-Table -AutoSize | Out-String | Set-Content -Encoding UTF8 -Path $infoFile

"CAPTURE_DIR=$captureDir"
"STARTED=$($started.Count)"
$started | Format-Table -AutoSize
