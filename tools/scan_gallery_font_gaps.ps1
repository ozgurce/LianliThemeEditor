$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$outRoot = Join-Path (Get-Location) 'gallery_font_gap_scan'
$downloadRoot = Join-Path $outRoot 'downloads'
New-Item -ItemType Directory -Force -Path $downloadRoot | Out-Null

$fontMap = [ordered]@{
  'GeForce' = @('GeForce_Bold.otf','GeForce_Light.otf')
  'Droid' = @('DroidLogo-Regular (1).ttf')
  'DroidLogo' = @('DroidLogo-Regular (1).ttf')
  'Agency Gothic CT' = @('agencygothicct-condensed.3fcc35ae.otf')
  'Agency Gothic' = @('agencygothicct-condensed.3fcc35ae.otf')
  'Agency FB' = @('AGENCYB.TTF','AGENCYR.TTF')
  'Alien Encounters' = @('ALIEN-ENCOUNTERS-REGULAR.TTF')
  'Digital-7 Mono' = @('digital-7 (mono) (1).ttf')
  'Digital-7' = @('digital-7.ttf','digital-7 (italic).ttf')
  'Future Z' = @('Future Z.ttf')
  'HarmonyOS Sans' = @('HarmonyOS_Sans_Regular.ttf','HarmonyOS_Sans_Medium.ttf','HarmonyOS_Sans_Bold.ttf','HarmonyOS_Sans_Light.ttf','HarmonyOS_Sans_Thin.ttf','HarmonyOS_Sans_Black.ttf')
  'Hyperspace Race' = @('hyperspacerace-bolditalic.f8e48ac1.otf')
  'Jokerman' = @('JOKERMAN.TTF')
  'Kumbh Sans' = @('KumbhSans-Regular.ttf','KumbhSans-Light.ttf','KumbhSans-Medium.ttf','KumbhSans-SemiBold.ttf','KumbhSans-Bold.ttf','KumbhSans-Black.ttf','KumbhSans-ExtraBold.ttf','KumbhSans-ExtraLight.ttf','KumbhSans-Thin.ttf')
  'Noto Sans TC' = @('NotoSansTC-Regular.6cd62e35.otf')
  'Orbitron' = @('orbitron-black.otf','orbitron-bold.otf','orbitron-light.otf','orbitron-medium.otf')
  'PerryGothic' = @('PERRYGOT.TTF')
  'Press Start 2P' = @('PressStart2P-Regular.ttf')
  'radioactive' = @('radioactive.ttf')
  'Roboto Condensed' = @('RobotoCondensed-Regular.ttf','RobotoCondensed-Bold.ttf','RobotoCondensed-BoldItalic.ttf','RobotoCondensed-Italic.ttf','RobotoCondensed-Light.ttf','RobotoCondensed-LightItalic.ttf')
  'Rush Driver' = @('RushDriver-Italic.otf')
  'Tsushima3' = @('Tsushima.otf')
  'Tw Cen MT Condensed Extra Bold' = @('TCCEB.TTF')
  'Tw Cen MT Condensed' = @('TCCB____.TTF','TCCM____.TTF')
  'Tw Cen MT' = @('TCB_____.TTF','TCBI____.TTF','TCM_____.TTF','TCMI____.TTF')
}

$knownStandardFonts = @(
  'Arial','Bahnschrift','Calibri','Cambria','Candara','Consolas','Corbel','Courier New',
  'Ebrima','Franklin Gothic','Gabriola','Georgia','Impact','Lucida Console','Microsoft JhengHei',
  'Microsoft YaHei','Segoe UI','Segoe UI Emoji','Segoe UI Symbol','Tahoma','Times New Roman',
  'Trebuchet MS','Verdana','Yu Gothic'
)

function Get-SafeFileName([string]$name) {
  $invalid = [IO.Path]::GetInvalidFileNameChars()
  $safe = -join ($name.ToCharArray() | ForEach-Object { if ($invalid -contains $_) { '_' } else { $_ } })
  if ([string]::IsNullOrWhiteSpace($safe)) { return 'package.zip' }
  return $safe
}

function Read-EntryBytes($entry) {
  $ms = [IO.MemoryStream]::new()
  $stream = $entry.Open()
  try {
    $stream.CopyTo($ms)
    return $ms.ToArray()
  } finally {
    $stream.Dispose()
    $ms.Dispose()
  }
}

function Read-EntrySearchText($entry) {
  $bytes = Read-EntryBytes $entry
  return [Text.Encoding]::UTF8.GetString($bytes) + "`n" + [Text.Encoding]::Unicode.GetString($bytes) + "`n" + [Text.Encoding]::Default.GetString($bytes)
}

function Get-FontFilesFromManifest([string]$manifestText) {
  $items = @()
  foreach ($fontMatch in [regex]::Matches($manifestText, '"FontFiles"\s*:\s*\[(?<items>.*?)\]', [Text.RegularExpressions.RegexOptions]::Singleline)) {
    foreach ($itemMatch in [regex]::Matches($fontMatch.Groups['items'].Value, '"((?:\\.|[^"])*)"')) {
      $items += [Text.RegularExpressions.Regex]::Unescape($itemMatch.Groups[1].Value)
    }
  }
  return $items
}

function Test-TemplateUsesFont([string]$text, [string]$fontName) {
  return $text.IndexOf($fontName, [StringComparison]::OrdinalIgnoreCase) -ge 0
}

function Scan-Package([string]$sourceName, [string]$themeId, [string]$themeName, [string]$packagePath, [string]$packageUrl) {
  $zip = [IO.Compression.ZipFile]::OpenRead($packagePath)
  try {
    $entries = @($zip.Entries | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) })
    $templateText = ''
    foreach ($template in ($entries | Where-Object { $_.Name -like '*.template' })) {
      $templateText += "`n" + (Read-EntrySearchText $template)
    }
    $zipFonts = @($entries | Where-Object { $_.Name -match '\.(ttf|otf)$' } | ForEach-Object { $_.FullName.Replace('\','/') })
    $manifestText = ''
    $manifestEntry = $zip.GetEntry('manifest.json')
    if ($manifestEntry) {
      $reader = [IO.StreamReader]::new($manifestEntry.Open(), [Text.Encoding]::UTF8)
      try { $manifestText = $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    $manifestFonts = @(Get-FontFilesFromManifest $manifestText)
    $allDeclared = @($zipFonts + $manifestFonts | Where-Object { $_ } | Select-Object -Unique)
    $rows = @()
    foreach ($font in $fontMap.Keys) {
      if (-not (Test-TemplateUsesFont $templateText $font)) { continue }
      $expected = @($fontMap[$font] | ForEach-Object { 'fonts/' + $_ })
      $missing = @($expected | Where-Object {
        $expectedEntry = $_
        -not ($allDeclared | Where-Object { $_.Equals($expectedEntry, [StringComparison]::OrdinalIgnoreCase) -or ([IO.Path]::GetFileName($_)).Equals([IO.Path]::GetFileName($expectedEntry), [StringComparison]::OrdinalIgnoreCase) })
      })
      if ($missing.Count -gt 0) {
        $rows += [pscustomobject]@{
          Source = $sourceName
          Id = $themeId
          Name = $themeName
          Font = $font
          MissingFontFiles = ($missing -join '; ')
          ZipFontFiles = ($zipFonts -join '; ')
          ManifestFontFiles = ($manifestFonts -join '; ')
          PackagePath = $packagePath
          PackageUrl = $packageUrl
        }
      }
    }
    return $rows
  } finally {
    $zip.Dispose()
  }
}

$packages = @()

$localOfficialRoot = Join-Path (Get-Location) 'templates\packages'
if (Test-Path $localOfficialRoot) {
  foreach ($file in Get-ChildItem $localOfficialRoot -Filter *.zip) {
    $packages += [pscustomobject]@{
      Source = 'local-official'
      Id = [IO.Path]::GetFileNameWithoutExtension($file.Name)
      Name = [IO.Path]::GetFileNameWithoutExtension($file.Name)
      PackagePath = $file.FullName
      PackageUrl = ''
    }
  }
}

$communityRoot = Join-Path (Get-Location) 'community_gallery_font_migration\packages'
if (Test-Path $communityRoot) {
  foreach ($file in Get-ChildItem $communityRoot -Filter *.zip) {
    $packages += [pscustomobject]@{
      Source = 'local-community-migrated'
      Id = [IO.Path]::GetFileNameWithoutExtension($file.Name)
      Name = [IO.Path]::GetFileNameWithoutExtension($file.Name)
      PackagePath = $file.FullName
      PackageUrl = ''
    }
  }
}

$community = Invoke-RestMethod -Uri 'https://lianli-theme-gallery.ozgurce.workers.dev/themes/community'
foreach ($theme in $community.themes) {
  $safe = Get-SafeFileName "$($theme.name)-$($theme.id).zip"
  $path = Join-Path $downloadRoot "live-community-$safe"
  Invoke-WebRequest -Uri $theme.packageUrl -OutFile $path -UseBasicParsing
  $packages += [pscustomobject]@{
    Source = 'live-community'
    Id = $theme.id
    Name = $theme.name
    PackagePath = $path
    PackageUrl = $theme.packageUrl
  }
}

$gaps = @()
foreach ($package in $packages) {
  $gaps += Scan-Package $package.Source $package.Id $package.Name $package.PackagePath $package.PackageUrl
}

$gaps | Export-Csv (Join-Path $outRoot 'font-gaps.csv') -NoTypeInformation -Encoding UTF8
$gaps | Sort-Object Source,Name,Font | Format-Table -AutoSize
if ($gaps.Count -eq 0) {
  'No known custom-font references are missing from the scanned packages.'
}
