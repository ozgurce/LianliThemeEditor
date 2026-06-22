$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectRoot 'ThemeEditorCSharp.csproj'
$desktop = [Environment]::GetFolderPath('Desktop')
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$stageRoot = Join-Path $projectRoot "tmp\framework-release-$stamp"
$publishDir = Join-Path $stageRoot 'publish'
$buildOutputDir = Join-Path $stageRoot 'build\'
$intermediateDir = Join-Path $stageRoot 'obj\'

try {
    $projectXml = [xml](Get-Content -LiteralPath $projectFile)
    $version = [string]$projectXml.Project.PropertyGroup.InformationalVersion
    if ([string]::IsNullOrWhiteSpace($version)) {
        $version = [string]$projectXml.Project.PropertyGroup.Version
    }
    $versionLabel = ($version -replace '^V\s*', 'V' -replace '[^A-Za-z0-9._-]+', '_').Trim('_')
    if ([string]::IsNullOrWhiteSpace($versionLabel)) {
        $versionLabel = 'Release'
    }

    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
    Write-Host 'Theme Editor framework-dependent Release paketi derleniyor...'
    & dotnet build (Join-Path $projectRoot 'SupporterCs\SupporterCs.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Supporter derlemesi başarısız oldu (çıkış kodu: $LASTEXITCODE)."
    }

    & dotnet publish $projectFile -c Release --self-contained false --nologo -o $publishDir `
        "-p:BaseOutputPath=$buildOutputDir" `
        "-p:BaseIntermediateOutputPath=$intermediateDir" `
        '-p:SkipSupporterBuild=true'
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish başarısız oldu (çıkış kodu: $LASTEXITCODE)."
    }

    $pdb = Join-Path $publishDir 'LianLiThemeEditor.pdb'
    if (Test-Path -LiteralPath $pdb) {
        Remove-Item -LiteralPath $pdb -Force
    }

    $readme = Join-Path $projectRoot 'README.md'
    if (Test-Path -LiteralPath $readme) {
        Copy-Item -LiteralPath $readme -Destination $publishDir
    }

    $zipPath = Join-Path $desktop "LianLiThemeEditor_${versionLabel}_FrameworkDependent_$stamp.zip"
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

    Write-Host ''
    Write-Host 'Release paketi hazır:' -ForegroundColor Green
    Write-Host $zipPath
    Start-Process explorer.exe -ArgumentList "/select,`"$zipPath`""
}
catch {
    Write-Host ''
    Write-Host "Release oluşturulamadı: $($_.Exception.Message)" -ForegroundColor Red
    Read-Host 'Kapatmak için Enter tuşuna basın'
    exit 1
}
finally {
    if (Test-Path -LiteralPath $stageRoot) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force
    }
}
