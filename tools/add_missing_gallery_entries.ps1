$ErrorActionPreference = 'Stop'

$jsonPath = 'templates\gallery.json'
$json = Get-Content $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
$themes = [System.Collections.Generic.List[object]]::new()
foreach ($theme in $json.themes) {
  $themes.Add($theme)
}

$missing = @(
  [pscustomobject]@{
    id = '019-time-lcd-s-76fc6b11'
    name = '019 + time (LCD-S)'
    author = 'hatiko'
    description = ''
    deviceModel = 'hydroshift-ii-lcd-s'
    deviceName = 'HydroShift II LCD-S'
    packageUrl = 'packages/019-time-lcd-s-76fc6b11.zip'
  },
  [pscustomobject]@{
    id = 'neon-board-44d1876c'
    name = 'Neon Board'
    author = 'Ozgur'
    description = ''
    deviceModel = 'hydroshift-ii-lcd-s'
    deviceName = 'HydroShift II LCD-S'
    packageUrl = 'packages/neon-board-44d1876c.zip'
  },
  [pscustomobject]@{
    id = 'kuromi-96bc246c'
    name = 'Kuromi'
    author = 'Ozgur'
    description = ''
    deviceModel = 'universal-screen-8.8-inch'
    deviceName = '8.8" Universal Screen'
    packageUrl = 'packages/kuromi-96bc246c.zip'
  }
)

$ids = @($themes | ForEach-Object { $_.id })
foreach ($theme in $missing) {
  if ($ids -notcontains $theme.id) {
    $themes.Add($theme)
  }
}

[pscustomobject]@{ themes = @($themes) } |
  ConvertTo-Json -Depth 20 |
  Set-Content $jsonPath -Encoding UTF8
