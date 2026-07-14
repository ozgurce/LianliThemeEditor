$ErrorActionPreference = "Continue"

$probe = "D:\ThemeEditor\tools\LianLi88SafeProbe\bin\LianLi88SafeProbe.exe"
$log = "D:\ThemeEditor\tools\LianLi88SafeProbe\safe-probe-output.txt"

"Lian Li 8.8 safe read-only probe - $(Get-Date -Format o)" | Set-Content -Path $log -Encoding UTF8
"This script stops L-Connect temporarily, runs only GetVer, QueryDir, and GetFileSize, then restarts services." | Add-Content -Path $log
"No write/delete/reboot/set command is sent by the probe executable." | Add-Content -Path $log
"" | Add-Content -Path $log

try {
    taskkill /IM "L-Connect 3.exe" /F 2>&1 | Add-Content -Path $log
    sc.exe stop LConnectServiceWatcher 2>&1 | Add-Content -Path $log
    sc.exe stop LConnectService 2>&1 | Add-Content -Path $log
    foreach ($svcName in @("LConnectServiceWatcher", "LConnectService")) {
        for ($i = 0; $i -lt 12; $i++) {
            $svc = Get-Service -Name $svcName -ErrorAction SilentlyContinue
            if ($null -eq $svc -or $svc.Status -eq "Stopped") {
                break
            }
            Start-Sleep -Milliseconds 500
        }
    }
    Start-Sleep -Seconds 1

    "=== GetVer only ===" | Add-Content -Path $log
    & $probe 2>&1 | Add-Content -Path $log

    "=== QueryDir /usr/data/ ===" | Add-Content -Path $log
    & $probe --query-dir --path "/usr/data/" 2>&1 | Add-Content -Path $log

    "=== QueryDir /media/ ===" | Add-Content -Path $log
    & $probe --query-dir --path "/media/" 2>&1 | Add-Content -Path $log

    "=== GetFileSize /usr/data/version ===" | Add-Content -Path $log
    & $probe --file-size --path "/usr/data/version" 2>&1 | Add-Content -Path $log

    "=== GetFileSize /usr/data/app.cfg ===" | Add-Content -Path $log
    & $probe --file-size --path "/usr/data/app.cfg" 2>&1 | Add-Content -Path $log

    "=== GetFileSize /usr/data/boot.sign ===" | Add-Content -Path $log
    & $probe --file-size --path "/usr/data/boot.sign" 2>&1 | Add-Content -Path $log
}
finally {
    "=== Restart services ===" | Add-Content -Path $log
    sc.exe start LConnectService 2>&1 | Add-Content -Path $log
    Start-Sleep -Seconds 2
    sc.exe start LConnectServiceWatcher 2>&1 | Add-Content -Path $log
    "Done - $(Get-Date -Format o)" | Add-Content -Path $log
}

Get-Content -Path $log
