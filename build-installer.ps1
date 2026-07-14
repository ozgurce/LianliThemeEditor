# Lian Li LCD Template Editor - Installer Build Script
# This script publishes the WPF app and compiles the installer using Inno Setup (ISCC.exe)

$ErrorActionPreference = "Stop"

# 1. Read version from project file
Write-Host "Reading version from project file..." -ForegroundColor Cyan
[xml]$proj = Get-Content -Raw "ThemeEditorCSharp.csproj"
$version = $proj.Project.PropertyGroup.Version
if (-not $version) {
    $version = "2.4.0"
}
Write-Host "Target Version: $version" -ForegroundColor Green

# 2. Publish the WPF project
Write-Host "Publishing the project in Release mode..." -ForegroundColor Cyan
dotnet publish -c Release

# 2.5 Generate Installer Artwork Images from App Assets
Write-Host "Generating installer artwork images from app assets..." -ForegroundColor Cyan
try {
    Add-Type -AssemblyName System.Drawing
    $srcPath = Join-Path (Get-Location) "Assets\glass-background.png"
    $installerDir = Join-Path (Get-Location) "bin_build\installer"
    if (-not (Test-Path $installerDir)) {
        New-Item -ItemType Directory -Path $installerDir -Force | Out-Null
    }

    if (Test-Path $srcPath) {
        $img = [System.Drawing.Image]::FromFile($srcPath)
        
        # Welcome image (328 x 628 for high-DPI)
        $wizardBmp = New-Object System.Drawing.Bitmap(328, 628)
        $g = [System.Drawing.Graphics]::FromImage($wizardBmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $srcRect = New-Object System.Drawing.Rectangle(100, 200, 500, 800)
        $destRect = New-Object System.Drawing.Rectangle(0, 0, 328, 628)
        $g.DrawImage($img, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
        $g.Dispose()
        $wizardBmp.Save((Join-Path $installerDir "wizard.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
        $wizardBmp.Dispose()

        # Header small image (110 x 110 for high-DPI)
        $smallBmp = New-Object System.Drawing.Bitmap(110, 110)
        $g = [System.Drawing.Graphics]::FromImage($smallBmp)
        $bgColor = [System.Drawing.Color]::FromArgb(255, 8, 20, 41) # #081429
        $g.Clear($bgColor)
        
        $icoPath = Join-Path (Get-Location) "editor.ico"
        if (Test-Path $icoPath) {
            try {
                Add-Type -AssemblyName PresentationCore
                Add-Type -AssemblyName WindowsBase
                
                # Load icon using WPF decoder to support PNG compression correctly
                $stream = New-Object System.IO.FileStream($icoPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
                $decoder = New-Object System.Windows.Media.Imaging.IconBitmapDecoder($stream, [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat, [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
                
                # Get the best frame (e.g. 64x64 or 48x48)
                $frame = $decoder.Frames | Where-Object { $_.Width -le 128 } | Sort-Object Width -Descending | Select-Object -First 1
                if (-not $frame) { $frame = $decoder.Frames[0] }
                
                # Save frame to a temporary PNG
                $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
                $encoder.Frames.Add($frame) | Out-Null
                $tempPng = [System.IO.Path]::GetTempFileName() + ".png"
                $tempStream = New-Object System.IO.FileStream($tempPng, [System.IO.FileMode]::Create)
                $encoder.Save($tempStream)
                $tempStream.Close()
                $stream.Close()
                
                # Draw the PNG image on our bitmap
                $pngImg = [System.Drawing.Image]::FromFile($tempPng)
                $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.DrawImage($pngImg, 23, 23, 64, 64)
                $pngImg.Dispose()
                
                # Cleanup temp file
                Remove-Item $tempPng -Force
            } catch {
                # Fallback to simple cropped image if WPF decoding fails
                $srcRect = New-Object System.Drawing.Rectangle(200, 200, 300, 300)
                $destRect = New-Object System.Drawing.Rectangle(0, 0, 110, 110)
                $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.DrawImage($img, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
            }
        } else {
            # Fallback
            $srcRect = New-Object System.Drawing.Rectangle(200, 200, 300, 300)
            $destRect = New-Object System.Drawing.Rectangle(0, 0, 110, 110)
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.DrawImage($img, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
        }
        $g.Dispose()
        $smallBmp.Save((Join-Path $installerDir "wizard_small.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
        $smallBmp.Dispose()

        $img.Dispose()
        Write-Host "Artwork images successfully generated!" -ForegroundColor Green
    } else {
        Write-Warning "Source asset Assets\glass-background.png not found. Skipping image generation."
    }
} catch {
    Write-Warning "Could not generate custom artwork: $_. Using default installer images."
}

# 3. Locate Inno Setup Compiler (ISCC.exe)
Write-Host "Locating Inno Setup Compiler (ISCC.exe)..." -ForegroundColor Cyan
$isccPaths = @(
    "iscc.exe", # If in PATH
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 7\ISCC.exe",
    "C:\Program Files\Inno Setup 7\ISCC.exe"
)

# Also try to read from Registry
$regPaths = @(
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1"
)

foreach ($regPath in $regPaths) {
    if (Test-Path $regPath) {
        $loc = (Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue).InstallLocation
        if ($loc) {
            $isccPaths += Join-Path $loc "ISCC.exe"
        }
    }
}

$isccPath = $null
foreach ($path in $isccPaths) {
    if ($path -eq "iscc.exe") {
        $check = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
        if ($check) {
            $isccPath = "iscc.exe"
            break
        }
    } elseif (Test-Path $path) {
        $isccPath = $path
        break
    }
}

if (-not $isccPath) {
    Write-Error "Inno Setup Compiler (ISCC.exe) could not be found! Please make sure Inno Setup is installed."
    Write-Host "You can install it using winget:" -ForegroundColor Yellow
    Write-Host "winget install JRSoftware.InnoSetup" -ForegroundColor Yellow
    exit 1
}

Write-Host "Found ISCC at: $isccPath" -ForegroundColor Green

# 4. Compile the installer
Write-Host "Compiling the installer with version $version..." -ForegroundColor Cyan
$setupFile = "bin_build\installer\LianLiThemeEditorSetup.exe"
if (Test-Path $setupFile) {
    Remove-Item $setupFile -Force
}

& $isccPath /dMyAppVersion="$version" "installer.iss"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compiler failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 5. Done!
if (Test-Path $setupFile) {
    $size = (Get-Item $setupFile).Length / 1MB
    Write-Host "Installer successfully built!" -ForegroundColor Green
    Write-Host ("Output: $setupFile (Size: {0:N2} MB)" -f $size) -ForegroundColor Green
} else {
    Write-Error "Installer build failed. Output file was not found."
    exit 1
}
