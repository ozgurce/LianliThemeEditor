# Lian Li LCD Template Editor - WiX MSI Build Script
# Builds an MSI without Inno Setup, custom actions, post-install execution, or payload compression.

$ErrorActionPreference = "Stop"

Write-Host "Reading version from project file..." -ForegroundColor Cyan
[xml]$proj = Get-Content -Raw "ThemeEditorCSharp.csproj"
$version = $proj.Project.PropertyGroup.Version
if (-not $version) {
    $version = "2.4.0"
}
Write-Host "Target Version: $version" -ForegroundColor Green

Write-Host "Publishing the project in Release mode..." -ForegroundColor Cyan
dotnet publish -c Release

$publishDir = Join-Path (Get-Location) "bin_build\Release\net10.0-windows\publish"
if (-not (Test-Path (Join-Path $publishDir "LianLiThemeEditor.exe"))) {
    throw "Publish output is missing LianLiThemeEditor.exe: $publishDir"
}

$productCode = "{" + ([guid]::NewGuid().ToString().ToUpperInvariant()) + "}"
$payloadDir = Join-Path (Get-Location) "bin_build\msi-payload"
if (Test-Path $payloadDir) {
    Remove-Item $payloadDir -Recurse -Force
}
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $payloadDir -Recurse -Force
Get-ChildItem $payloadDir -Recurse -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force

$uninstallCmd = @"
@echo off
start "" msiexec.exe /x $productCode
"@
Set-Content -Path (Join-Path $payloadDir "Uninstall Lian Li LCD Template Editor.cmd") -Value $uninstallCmd -Encoding ASCII

$estimatedSizeKb = [int][Math]::Ceiling(((Get-ChildItem $payloadDir -Recurse -File | Measure-Object Length -Sum).Sum) / 1KB)

Write-Host "Restoring local WiX tool..." -ForegroundColor Cyan
dotnet tool restore

$installerDir = Join-Path (Get-Location) "bin_build\installer"
New-Item -ItemType Directory -Path $installerDir -Force | Out-Null

Write-Host "Generating WiX installer artwork..." -ForegroundColor Cyan
$themeDir = Join-Path (Get-Location) "installer-wix\theme"
New-Item -ItemType Directory -Path $themeDir -Force | Out-Null

try {
    Add-Type -AssemblyName System.Drawing

    $backgroundPath = Join-Path (Get-Location) "Assets\glass-background.png"
    $iconPath = Join-Path (Get-Location) "editor.ico"
    $dialogPath = Join-Path $themeDir "dialog.bmp"
    $bannerPath = Join-Path $themeDir "banner.bmp"

    function Draw-AppIcon([System.Drawing.Graphics]$graphics, [string]$path, [int]$x, [int]$y, [int]$size) {
        if (Test-Path $path) {
            try {
                Add-Type -AssemblyName PresentationCore
                Add-Type -AssemblyName WindowsBase

                $stream = New-Object System.IO.FileStream($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
                $decoder = New-Object System.Windows.Media.Imaging.IconBitmapDecoder($stream, [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat, [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
                $frame = $decoder.Frames |
                    Sort-Object { [Math]::Abs($_.PixelWidth - $size) + [Math]::Abs($_.PixelHeight - $size) } |
                    Select-Object -First 1

                $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
                $encoder.Frames.Add($frame) | Out-Null
                $tempPng = [System.IO.Path]::GetTempFileName() + ".png"
                $tempStream = New-Object System.IO.FileStream($tempPng, [System.IO.FileMode]::Create)
                $encoder.Save($tempStream)
                $tempStream.Close()
                $stream.Close()

                $img = [System.Drawing.Image]::FromFile($tempPng)
                $graphics.DrawImage($img, $x, $y, $size, $size)
                $img.Dispose()
                Remove-Item $tempPng -Force
            } catch {
                $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
                    (New-Object System.Drawing.Rectangle($x, $y, $size, $size)),
                    [System.Drawing.Color]::FromArgb(36, 120, 243),
                    [System.Drawing.Color]::FromArgb(28, 214, 186),
                    [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
                $graphics.FillRectangle($brush, $x, $y, $size, $size)
                $brush.Dispose()

                $fontSize = [Math]::Max(9, [int]($size * 0.34))
                $font = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold)
                $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
                $format = New-Object System.Drawing.StringFormat
                $format.Alignment = [System.Drawing.StringAlignment]::Center
                $format.LineAlignment = [System.Drawing.StringAlignment]::Center
                $graphics.DrawString("TE", $font, $white, (New-Object System.Drawing.RectangleF($x, $y, $size, $size)), $format)
                $format.Dispose(); $font.Dispose(); $white.Dispose()
            }
        }
    }

    function New-InstallerBitmap([int]$width, [int]$height, [string]$outputPath, [bool]$large) {
        $bmp = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

        if ($large) {
            $g.Clear([System.Drawing.Color]::FromArgb(247, 249, 252))

            $panelWidth = 128
            if (Test-Path $backgroundPath) {
                $bg = [System.Drawing.Image]::FromFile($backgroundPath)
                $src = New-Object System.Drawing.Rectangle(80, 120, [Math]::Min(620, $bg.Width - 80), [Math]::Min(620, $bg.Height - 120))
                $dest = New-Object System.Drawing.Rectangle(0, 0, $panelWidth, $height)
                $g.DrawImage($bg, $dest, $src, [System.Drawing.GraphicsUnit]::Pixel)
                $bg.Dispose()
            } else {
                $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(7, 21, 47))), 0, 0, $panelWidth, $height)
            }

            $panelOverlay = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
                (New-Object System.Drawing.Rectangle(0, 0, $panelWidth, $height)),
                [System.Drawing.Color]::FromArgb(244, 7, 21, 47),
                [System.Drawing.Color]::FromArgb(230, 20, 53, 99),
                [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
            $g.FillRectangle($panelOverlay, 0, 0, $panelWidth, $height)
            $panelOverlay.Dispose()

            $accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(36, 120, 243))
            $g.FillRectangle($accent, 0, 0, 6, $height)
            $accent.Dispose()

            Draw-AppIcon $g $iconPath 34 42 60

            $brandFont = New-Object System.Drawing.Font("Segoe UI", 12, [System.Drawing.FontStyle]::Bold)
            $smallFont = New-Object System.Drawing.Font("Segoe UI", 8, [System.Drawing.FontStyle]::Regular)
            $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
            $muted = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(205, 225, 235, 245))
            $center = New-Object System.Drawing.StringFormat
            $center.Alignment = [System.Drawing.StringAlignment]::Center
            $g.DrawString("Theme`nEditor", $brandFont, $white, (New-Object System.Drawing.RectangleF(10, 126, 108, 48)), $center)
            $g.DrawString("Phone Link ready", $smallFont, $muted, (New-Object System.Drawing.RectangleF(10, 252, 108, 28)), $center)
            $center.Dispose(); $brandFont.Dispose(); $smallFont.Dispose(); $white.Dispose(); $muted.Dispose()
        } else {
            $g.Clear([System.Drawing.Color]::FromArgb(247, 249, 252))
            $line = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(36, 120, 243))
            $g.FillRectangle($line, 0, 0, 6, $height)
            $line.Dispose()

            $soft = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
                (New-Object System.Drawing.Rectangle(0, 0, $width, $height)),
                [System.Drawing.Color]::FromArgb(248, 251, 255),
                [System.Drawing.Color]::FromArgb(230, 238, 248),
                [System.Drawing.Drawing2D.LinearGradientMode]::Horizontal)
            $g.FillRectangle($soft, 6, 0, $width - 6, $height)
            $soft.Dispose()

            Draw-AppIcon $g $iconPath 438 11 34
        }

        $g.Dispose()
        $bmp.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
        $bmp.Dispose()
    }

    New-InstallerBitmap 493 312 $dialogPath $true
    New-InstallerBitmap 493 58 $bannerPath $false
    Write-Host "WiX installer artwork generated." -ForegroundColor Green
} catch {
    Write-Warning "Could not generate WiX installer artwork: $_"
}

$msiPath = Join-Path $installerDir "LianLiThemeEditorSetup.msi"
if (Test-Path $msiPath) {
    Remove-Item $msiPath -Force
}

Write-Host "Building MSI with WiX Toolset..." -ForegroundColor Cyan
$uiExtensionPath = Join-Path (Get-Location) ".wix\extensions\WixToolset.UI.wixext\7.0.0\wixext7\WixToolset.UI.wixext.dll"
if (-not (Test-Path $uiExtensionPath)) {
    dotnet tool run wix -- extension add WixToolset.UI.wixext/7.0.0 | Out-Host
}
if (-not (Test-Path $uiExtensionPath)) {
    throw "WiX UI extension was not found: $uiExtensionPath"
}
$utilExtensionPath = Join-Path (Get-Location) ".wix\extensions\WixToolset.Util.wixext\7.0.0\wixext7\WixToolset.Util.wixext.dll"
if (-not (Test-Path $utilExtensionPath)) {
    dotnet tool run wix -- extension add WixToolset.Util.wixext/7.0.0 | Out-Host
}
if (-not (Test-Path $utilExtensionPath)) {
    throw "WiX Util extension was not found: $utilExtensionPath"
}

dotnet tool run wix -- build `
    "installer-wix\Product.wxs" `
    -arch x64 `
    -ext $uiExtensionPath `
    -ext $utilExtensionPath `
    -d "PublishDir=$payloadDir" `
    -d "ProductVersion=$version" `
    -d "ProductCode=$productCode" `
    -d "EstimatedSizeKb=$estimatedSizeKb" `
    -out $msiPath

if (-not (Test-Path $msiPath)) {
    throw "MSI build failed. Output file was not found."
}

$size = (Get-Item $msiPath).Length / 1MB
Write-Host "MSI successfully built!" -ForegroundColor Green
Write-Host ("Output: $msiPath (Size: {0:N2} MB)" -f $size) -ForegroundColor Green
