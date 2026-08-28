param(
    [string]$OutputRoot = "$env:USERPROFILE\Desktop",
    [switch]$SkipApiProbe
)

$ErrorActionPreference = "Continue"
$ProgressPreference = "SilentlyContinue"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "== $Message ==" -ForegroundColor Cyan
}

function Write-Info {
    param([string]$Message)
    Write-Host "   $Message" -ForegroundColor Gray
}

function Safe-Name {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "empty" }
    return ($Value -replace '[^\w\-.]+', '_').Trim('_')
}

function New-Folder {
    param([string]$Path)
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Copy-IfExists {
    param(
        [string]$Source,
        [string]$Destination,
        [switch]$Recurse
    )
    try {
        if (Test-Path -LiteralPath $Source) {
            New-Folder (Split-Path -Parent $Destination)
            if ($Recurse) {
                Copy-Item -LiteralPath $Source -Destination $Destination -Recurse -Force
            } else {
                Copy-Item -LiteralPath $Source -Destination $Destination -Force
            }
            return $true
        }
    } catch {
        Add-Content -LiteralPath $script:ErrorLog -Value "Copy failed: $Source -> $Destination :: $($_.Exception.Message)"
    }
    return $false
}

function Write-TextFile {
    param(
        [string]$Path,
        [string]$Content
    )
    New-Folder (Split-Path -Parent $Path)
    $Content | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Run-Cmd {
    param(
        [string]$Name,
        [scriptblock]$Block,
        [string]$OutFile
    )
    try {
        $text = & $Block 2>&1 | Out-String
        Write-TextFile $OutFile $text
    } catch {
        Write-TextFile $OutFile "FAILED: $Name`r`n$($_.Exception.ToString())"
        Add-Content -LiteralPath $script:ErrorLog -Value "Command failed: $Name :: $($_.Exception.Message)"
    }
}

function Get-LConnectSettingsRoots {
    @(
        "$env:ProgramData\Lian-Li\L-Connect 3",
        "$env:APPDATA\Lian-Li",
        "$env:LOCALAPPDATA\Lian-Li",
        "$env:LOCALAPPDATA\L-Connect 3",
        "$env:APPDATA\L-Connect 3"
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique
}

function Get-CandidatePorts {
    $ports = New-Object System.Collections.Generic.List[int]
    foreach ($p in 11021,11022,11023,11024,11025) {
        if (-not $ports.Contains($p)) { $ports.Add($p) }
    }

    foreach ($root in Get-LConnectSettingsRoots) {
        try {
            Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Extension -match '^\.(json|config|xml|settings|txt|log)$' } |
                Select-Object -First 250 |
                ForEach-Object {
                    try {
                        $text = Get-Content -LiteralPath $_.FullName -Raw -ErrorAction Stop
                        foreach ($m in [regex]::Matches($text, '(?<!\d)(11\d{3})(?!\d)')) {
                            $v = [int]$m.Groups[1].Value
                            if ($v -gt 0 -and $v -le 65535 -and -not $ports.Contains($v)) { $ports.Add($v) }
                        }
                    } catch {}
                }
        } catch {}
    }
    return $ports.ToArray()
}

function Invoke-LConnectService {
    param(
        [int]$Port,
        [string]$Action,
        [string]$Body = "{}",
        [switch]$EmptyBody
    )
    $url = "http://127.0.0.1:$Port/?action=$([uri]::EscapeDataString($Action))"
    $headers = @{ "Content-Type" = "application/json; charset=UTF-8" }
    $payload = if ($EmptyBody) { "" } else { $Body }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-WebRequest -Uri $url -Method Post -Headers $headers -Body $payload -TimeoutSec 3 -UseBasicParsing
        $sw.Stop()
        [pscustomobject]@{
            Kind = "Service"
            Port = $Port
            Action = $Action
            Url = $url
            EmptyBody = [bool]$EmptyBody
            StatusCode = [int]$response.StatusCode
            StatusDescription = $response.StatusDescription
            ElapsedMs = $sw.ElapsedMilliseconds
            Body = $response.Content
            Error = ""
        }
    } catch {
        $sw.Stop()
        [pscustomobject]@{
            Kind = "Service"
            Port = $Port
            Action = $Action
            Url = $url
            EmptyBody = [bool]$EmptyBody
            StatusCode = $null
            StatusDescription = ""
            ElapsedMs = $sw.ElapsedMilliseconds
            Body = ""
            Error = $_.Exception.Message
        }
    }
}

function Invoke-LConnectDevice {
    param(
        [int]$Port,
        [string]$DevicePath,
        [string]$Type,
        [string]$Body = "{}",
        [switch]$EmptyBody
    )
    $bytes = [Text.Encoding]::UTF8.GetBytes($DevicePath)
    $encodedPath = [uri]::EscapeDataString([Convert]::ToBase64String($bytes))
    $url = "http://127.0.0.1:$Port/?action=Device&devicePath=$encodedPath&type=$([uri]::EscapeDataString($Type))"
    $headers = @{ "Content-Type" = "application/json; charset=UTF-8" }
    $payload = if ($EmptyBody) { "" } else { $Body }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-WebRequest -Uri $url -Method Post -Headers $headers -Body $payload -TimeoutSec 4 -UseBasicParsing
        $sw.Stop()
        [pscustomobject]@{
            Kind = "Device"
            Port = $Port
            Type = $Type
            DevicePath = $DevicePath
            Url = $url
            EmptyBody = [bool]$EmptyBody
            StatusCode = [int]$response.StatusCode
            StatusDescription = $response.StatusDescription
            ElapsedMs = $sw.ElapsedMilliseconds
            Body = $response.Content
            Error = ""
        }
    } catch {
        $sw.Stop()
        [pscustomobject]@{
            Kind = "Device"
            Port = $Port
            Type = $Type
            DevicePath = $DevicePath
            Url = $url
            EmptyBody = [bool]$EmptyBody
            StatusCode = $null
            StatusDescription = ""
            ElapsedMs = $sw.ElapsedMilliseconds
            Body = ""
            Error = $_.Exception.Message
        }
    }
}

function Save-Json {
    param(
        [string]$Path,
        [object]$Object
    )
    New-Folder (Split-Path -Parent $Path)
    $Object | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Save-DirectoryListing {
    param(
        [string]$Root,
        [string]$OutCsv
    )
    if (-not (Test-Path -LiteralPath $Root)) { return }
    Get-ChildItem -LiteralPath $Root -Recurse -Force -ErrorAction SilentlyContinue |
        Select-Object FullName, Mode, Length, CreationTimeUtc, LastWriteTimeUtc |
        Export-Csv -LiteralPath $OutCsv -NoTypeInformation -Encoding UTF8
}

function Snapshot-State {
    param(
        [string]$Name,
        [string]$Root,
        [int[]]$Ports,
        [switch]$SkipApiProbe
    )

    $snap = Join-Path $Root $Name
    New-Folder $snap
    Write-Step "Snapshot: $Name"

    $programDataRoot = "$env:ProgramData\Lian-Li\L-Connect 3"
    Save-DirectoryListing $programDataRoot (Join-Path $snap "programdata_listing.csv")
    Save-DirectoryListing (Join-Path $programDataRoot "vm-9.2-inch") (Join-Path $snap "vm-9.2-inch_listing.csv")
    Save-DirectoryListing (Join-Path $programDataRoot "uploaded") (Join-Path $snap "uploaded_listing.csv")
    Save-DirectoryListing (Join-Path $programDataRoot "profile") (Join-Path $snap "profile_listing.csv")

    Copy-IfExists (Join-Path $programDataRoot "profile") (Join-Path $snap "profile") -Recurse | Out-Null
    Copy-IfExists (Join-Path $programDataRoot "settings") (Join-Path $snap "settings") -Recurse | Out-Null

    $vmRoot = Join-Path $programDataRoot "vm-9.2-inch"
    foreach ($folder in "template","theme","preview") {
        Copy-IfExists (Join-Path $vmRoot $folder) (Join-Path $snap "vm-9.2-inch\$folder") -Recurse | Out-Null
    }
    if (Test-Path -LiteralPath (Join-Path $vmRoot "video")) {
        New-Folder (Join-Path $snap "vm-9.2-inch\video")
        Get-ChildItem -LiteralPath (Join-Path $vmRoot "video") -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 25 |
            ForEach-Object {
                Copy-IfExists $_.FullName (Join-Path $snap "vm-9.2-inch\video\$($_.Name)") | Out-Null
            }
    }

    if (-not $SkipApiProbe) {
        $apiRoot = Join-Path $snap "api"
        New-Folder $apiRoot
        $serviceResults = @()
        foreach ($port in $Ports) {
            foreach ($empty in $false,$true) {
                $serviceResults += Invoke-LConnectService -Port $port -Action "Ping" -Body "{}" -EmptyBody:([bool]$empty)
                $serviceResults += Invoke-LConnectService -Port $port -Action "SyncControllerList" -Body "{}" -EmptyBody:([bool]$empty)
            }
        }
        Save-Json (Join-Path $apiRoot "service_probe.json") $serviceResults

        $controllers = @()
        foreach ($r in $serviceResults | Where-Object { $_.Action -eq "SyncControllerList" -and $_.StatusCode -ge 200 -and $_.StatusCode -lt 300 -and $_.Body }) {
            try {
                $json = $r.Body | ConvertFrom-Json
                $json.PSObject.Properties | ForEach-Object {
                    $controllers += [pscustomobject]@{ Port = $r.Port; Path = $_.Name; Value = $_.Value }
                }
            } catch {}
        }
        $controllers = $controllers | Sort-Object Port,Path -Unique
        Save-Json (Join-Path $apiRoot "controllers.json") $controllers

        $deviceResults = @()
        foreach ($controller in $controllers) {
            if ($controller.Path -notmatch '9\.2|vm|vid_1cbe&pid_a092|vid_1cbe&pid_a088|universal|8\.8') { continue }
            foreach ($type in "ReloadAssets","GetTemplates","GetSelectedTemplateId","SaveProfile","ApplyScreenContent") {
                foreach ($empty in $false,$true) {
                    $deviceResults += Invoke-LConnectDevice -Port $controller.Port -DevicePath $controller.Path -Type $type -Body "{}" -EmptyBody:([bool]$empty)
                }
            }
        }
        Save-Json (Join-Path $apiRoot "device_probe.json") $deviceResults
    }
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$caseRoot = Join-Path $OutputRoot "LConnect92Diagnostics-$timestamp"
$script:ErrorLog = Join-Path $caseRoot "errors.txt"
New-Folder $caseRoot
Write-TextFile $script:ErrorLog ""

Start-Transcript -LiteralPath (Join-Path $caseRoot "collector-transcript.txt") -Force | Out-Null

try {
    Write-Host "L-Connect 9.2 diagnostics collector" -ForegroundColor Green
    Write-Host "This script does not import or apply anything by itself; it snapshots files and probes L-Connect while you reproduce the L-Connect import/apply flow." -ForegroundColor Gray
    Write-Host "Output folder: $caseRoot" -ForegroundColor Gray

    Write-Step "Step 1 - Preparation"
    Write-Host "1. Open L-Connect 3."
    Write-Host "2. Open the VM 9.2 LCD page."
    Write-Host "3. Do not apply yet."
    Write-Host "4. Keep the theme package ready, but do not import it yet."
    Read-Host "Press ENTER when L-Connect is open on the VM 9.2 screen"

    Write-Step "Collecting environment"
    Run-Cmd "systeminfo" { systeminfo } (Join-Path $caseRoot "systeminfo.txt")
    Run-Cmd "whoami" { whoami /all } (Join-Path $caseRoot "whoami.txt")
    Run-Cmd "processes" { Get-Process | Sort-Object ProcessName | Select-Object ProcessName,Id,Path,StartTime -ErrorAction SilentlyContinue | Format-Table -AutoSize } (Join-Path $caseRoot "processes.txt")
    Run-Cmd "services" { Get-Service | Where-Object { $_.Name -match 'L-?Connect|Lian|LIAN' -or $_.DisplayName -match 'L-?Connect|Lian|LIAN' } | Format-List * } (Join-Path $caseRoot "services_lianli.txt")
    Run-Cmd "netstat" { netstat -ano | Select-String -Pattern '1102|LConnect|Lian' } (Join-Path $caseRoot "netstat_lconnect.txt")

    $ports = Get-CandidatePorts
    Save-Json (Join-Path $caseRoot "candidate_ports.json") $ports

    Snapshot-State -Name "before-lconnect-import" -Root $caseRoot -Ports $ports -SkipApiProbe:$SkipApiProbe

    Write-Step "Step 2 - Import in L-Connect"
    Write-Host "Now import the theme package inside L-Connect itself:"
    Write-Host "1. Stay in L-Connect 3 on the VM 9.2 LCD/theme screen."
    Write-Host "2. Use L-Connect's own import/add theme flow."
    Write-Host "3. Select the package manually in the L-Connect file picker."
    Write-Host "4. Wait until L-Connect finishes creating the theme/background files."
    Write-Host "5. Do not press Apply yet."
    Read-Host "Press ENTER after the package has been imported in L-Connect, before Apply"

    Snapshot-State -Name "after-lconnect-import-before-apply" -Root $caseRoot -Ports $ports -SkipApiProbe:$SkipApiProbe

    Write-Step "Step 3 - Apply in L-Connect"
    Write-Host "Now apply the imported theme in L-Connect:"
    Write-Host "1. Select the imported VM 9.2 theme."
    Write-Host "2. Press Apply."
    Write-Host "3. If Apply All exists for this flow, press Apply All too."
    Write-Host "4. Wait 10-15 seconds after the device/L-Connect responds."
    Read-Host "Press ENTER after the VM 9.2 L-Connect apply flow has been attempted"

    Snapshot-State -Name "after-lconnect-apply" -Root $caseRoot -Ports $ports -SkipApiProbe:$SkipApiProbe

    Write-Step "Collecting recent logs"
    $programDataRoot = "$env:ProgramData\Lian-Li\L-Connect 3"
    $logDest = Join-Path $caseRoot "recent-logs"
    New-Folder $logDest
    if (Test-Path -LiteralPath (Join-Path $programDataRoot "logs")) {
        Get-ChildItem -LiteralPath (Join-Path $programDataRoot "logs") -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 80 |
            ForEach-Object {
                Copy-IfExists $_.FullName (Join-Path $logDest $_.Name) | Out-Null
            }
    }
    foreach ($root in Get-LConnectSettingsRoots) {
        Save-DirectoryListing $root (Join-Path $caseRoot ("listing_" + (Safe-Name $root) + ".csv"))
    }

    Write-Step "Packaging"
    $zipPath = "$caseRoot.zip"
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -LiteralPath $caseRoot -DestinationPath $zipPath -CompressionLevel Optimal

    Write-Host ""
    Write-Host "Done." -ForegroundColor Green
    Write-Host "Send this ZIP file:" -ForegroundColor Yellow
    Write-Host $zipPath -ForegroundColor Yellow
    Write-TextFile (Join-Path $caseRoot "DONE_SEND_THIS_ZIP.txt") $zipPath
} finally {
    Stop-Transcript | Out-Null
}
