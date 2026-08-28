$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

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

function Read-EntryBytes($entry) {
  $memory = [IO.MemoryStream]::new()
  $stream = $entry.Open()
  try {
    $stream.CopyTo($memory)
    return $memory.ToArray()
  } finally {
    $stream.Dispose()
    $memory.Dispose()
  }
}

function Contains-Bytes([byte[]]$haystack, [byte[]]$needle) {
  if ($needle.Length -eq 0 -or $haystack.Length -lt $needle.Length) { return $false }
  for ($i = 0; $i -le $haystack.Length - $needle.Length; $i++) {
    $ok = $true
    for ($j = 0; $j -lt $needle.Length; $j++) {
      if ($haystack[$i + $j] -ne $needle[$j]) {
        $ok = $false
        break
      }
    }
    if ($ok) { return $true }
  }
  return $false
}

function Test-TemplateUsesFont($templateEntries, [string]$font) {
  $utf8 = [Text.Encoding]::UTF8.GetBytes($font)
  $unicode = [Text.Encoding]::Unicode.GetBytes($font)
  foreach ($entry in $templateEntries) {
    $bytes = Read-EntryBytes $entry
    if ((Contains-Bytes $bytes $utf8) -or (Contains-Bytes $bytes $unicode)) {
      return $true
    }
  }
  return $false
}

function Scan-Package([string]$source, [string]$name, [string]$path) {
  $zip = [IO.Compression.ZipFile]::OpenRead($path)
  try {
    $entries = @($zip.Entries | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) })
    $templates = @($entries | Where-Object { $_.Name.EndsWith('.template', [StringComparison]::OrdinalIgnoreCase) })
    $zipFontNames = @($entries |
      Where-Object { $_.Name -match '\.(ttf|otf)$' } |
      ForEach-Object { [IO.Path]::GetFileName($_.Name) })
    $rows = @()
    foreach ($font in $fontMap.Keys) {
      if (-not (Test-TemplateUsesFont $templates $font)) { continue }
      $missing = @($fontMap[$font] | Where-Object {
        $expected = $_
        -not ($zipFontNames | Where-Object { $_.Equals($expected, [StringComparison]::OrdinalIgnoreCase) })
      })
      if ($missing.Count -gt 0) {
        $rows += [pscustomobject]@{
          Source = $source
          Package = $name
          Font = $font
          Missing = ($missing -join '; ')
          Present = ($zipFontNames -join '; ')
        }
      }
    }
    return $rows
  } finally {
    $zip.Dispose()
  }
}

$rows = @()
if (Test-Path 'templates\packages') {
  foreach ($file in Get-ChildItem 'templates\packages' -Filter *.zip) {
    $rows += Scan-Package 'local-official-updated' $file.Name $file.FullName
  }
}
if (Test-Path 'community_gallery_font_migration\packages') {
  foreach ($file in Get-ChildItem 'community_gallery_font_migration\packages' -Filter *.zip) {
    $rows += Scan-Package 'local-community-updated' $file.Name $file.FullName
  }
}
if (Test-Path 'gallery_font_gap_scan\downloads') {
  foreach ($file in Get-ChildItem 'gallery_font_gap_scan\downloads' -Filter 'live-community-*.zip') {
    $rows += Scan-Package 'live-community-current' $file.Name $file.FullName
  }
}

New-Item -ItemType Directory -Force -Path 'gallery_font_gap_scan' | Out-Null
$rows | Export-Csv 'gallery_font_gap_scan\font-gaps-fast.csv' -NoTypeInformation -Encoding UTF8
if ($rows.Count -eq 0) {
  'NO_GAPS'
} else {
  $rows | Sort-Object Source, Package, Font | Format-Table -AutoSize
}
