$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$api = 'https://lianli-theme-gallery.ozgurce.workers.dev/themes/community'
$outRoot = Join-Path (Get-Location) 'community_gallery_font_migration'
$pkgOut = Join-Path $outRoot 'packages'
New-Item -ItemType Directory -Force -Path $pkgOut | Out-Null
$fontRoot = 'C:\Users\Ozgur\Desktop\1'
$fontMap = [ordered]@{
  'GeForce' = @('GeForce_Bold.otf','GeForce_Light.otf')
  'Droid' = @('DroidLogo-Regular (1).ttf')
  'DroidLogo' = @('DroidLogo-Regular (1).ttf')
  'Agency Gothic' = @('agencygothicct-condensed.3fcc35ae.otf')
  'Agency Gothic CT' = @('agencygothicct-condensed.3fcc35ae.otf')
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

function Get-SafeFileName([string]$name) {
  $invalid = [IO.Path]::GetInvalidFileNameChars()
  $chars = $name.ToCharArray() | ForEach-Object { if ($invalid -contains $_) { '_' } else { $_ } }
  $safe = -join $chars
  if ([string]::IsNullOrWhiteSpace($safe)) { return 'theme.zip' }
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

function Add-FileEntry($zip, [string]$source, [string]$entryName) {
  $old = $zip.GetEntry($entryName)
  if ($old) { $old.Delete() }
  [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $source, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
}

function Add-TextEntry($zip, [string]$entryName, [string]$text) {
  $old = $zip.GetEntry($entryName)
  if ($old) { $old.Delete() }
  $entry = $zip.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
  $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
  try { $writer.Write($text) } finally { $writer.Dispose() }
}

function Get-JsonStringValue([string]$json, [string]$name) {
  $pattern = '"' + [regex]::Escape($name) + '"\s*:\s*"((?:\\.|[^"])*)"'
  $match = [regex]::Match($json, $pattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
  if (-not $match.Success) { return '' }
  return [Text.RegularExpressions.Regex]::Unescape($match.Groups[1].Value)
}

$json = Invoke-RestMethod -Uri $api
$report = @()
foreach ($theme in $json.themes) {
  $safe = Get-SafeFileName "$($theme.name)-$($theme.id).zip"
  $download = Join-Path $outRoot "download-$safe"
  $output = Join-Path $pkgOut $safe
  Invoke-WebRequest -Uri $theme.packageUrl -OutFile $download -UseBasicParsing
  Copy-Item $download $output -Force

  $zip = [IO.Compression.ZipFile]::Open($output, [IO.Compression.ZipArchiveMode]::Update)
  try {
    $entries = @($zip.Entries | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) })
    $template = $entries | Where-Object { $_.Name -like '*.template' } | Select-Object -First 1
    $text = if ($template) { Read-EntrySearchText $template } else { '' }
    $manifestText = ''
    $usedFonts = [Collections.Generic.List[string]]::new()
    $fontEntries = [Collections.Generic.List[string]]::new()

    foreach ($key in $fontMap.Keys) {
      if ($text.IndexOf($key, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        $usedFonts.Add($key)
        foreach ($fileName in $fontMap[$key]) {
          $source = Join-Path $fontRoot $fileName
          if (Test-Path $source) {
            $entryName = 'fonts/' + $fileName
            Add-FileEntry $zip $source $entryName
            if (-not $fontEntries.Contains($entryName)) { $fontEntries.Add($entryName) }
          }
        }
      }
    }

    $manifestEntry = $zip.GetEntry('manifest.json')
    if ($manifestEntry) {
      $reader = [IO.StreamReader]::new($manifestEntry.Open(), [Text.Encoding]::UTF8)
      try { $manifestText = $reader.ReadToEnd() } finally { $reader.Dispose() }
      $manifest = [ordered]@{
        FormatVersion = 1
        App = 'Lian Li LCD Theme Editor'
        DeviceModel = Get-JsonStringValue $manifestText 'DeviceModel'
        TemplateId = Get-JsonStringValue $manifestText 'TemplateId'
        TemplateFile = Get-JsonStringValue $manifestText 'TemplateFile'
        BackgroundFile = Get-JsonStringValue $manifestText 'BackgroundFile'
        PreviewFile = Get-JsonStringValue $manifestText 'PreviewFile'
        UniversalOrientation = Get-JsonStringValue $manifestText 'UniversalOrientation'
        ImageFiles = @()
      }
    } else {
      $background = $entries | Where-Object { $_.Name -match '\.(mp4|h264|gif|png|jpg|jpeg)$' } | Select-Object -First 1
      $manifest = [ordered]@{
        FormatVersion = 1
        App = 'Lian Li LCD Theme Editor'
        DeviceModel = $theme.deviceModel
        TemplateId = [IO.Path]::GetFileNameWithoutExtension($template.Name)
        TemplateFile = $template.Name
        BackgroundFile = if ($background) { $background.Name } else { '' }
        PreviewFile = ''
        UniversalOrientation = ''
        ImageFiles = @()
      }
    }

    $existingFonts = @()
    foreach ($fontMatch in [regex]::Matches($manifestText, '"FontFiles"\s*:\s*\[(?<items>.*?)\]', [Text.RegularExpressions.RegexOptions]::Singleline)) {
      foreach ($itemMatch in [regex]::Matches($fontMatch.Groups['items'].Value, '"((?:\\.|[^"])*)"')) {
        $existingFonts += [Text.RegularExpressions.Regex]::Unescape($itemMatch.Groups[1].Value)
      }
    }
    $allFonts = @($existingFonts + @($fontEntries) | Where-Object { $_ } | Select-Object -Unique)
    $manifest['FontFiles'] = $allFonts
    if ([string]::IsNullOrWhiteSpace($manifest['DeviceModel'])) {
      $manifest['DeviceModel'] = $theme.deviceModel
    }
    if ([string]::IsNullOrWhiteSpace($manifest['TemplateFile']) -and $template) {
      $manifest['TemplateFile'] = $template.Name
    }
    if ([string]::IsNullOrWhiteSpace($manifest['TemplateId']) -and $template) {
      $manifest['TemplateId'] = [IO.Path]::GetFileNameWithoutExtension($template.Name)
    }

    Add-TextEntry $zip 'manifest.json' ($manifest | ConvertTo-Json -Depth 20)
    $report += [pscustomobject]@{
      Id = $theme.id
      Name = $theme.name
      Package = $safe
      UsedFonts = ($usedFonts -join '; ')
      AddedFontFiles = ($fontEntries -join '; ')
      Output = $output
    }
  } finally {
    $zip.Dispose()
  }
}

$report | Export-Csv (Join-Path $outRoot 'community-migration-report.csv') -NoTypeInformation -Encoding UTF8
$report | Format-Table -AutoSize
