# create_release.ps1
# Script to build and package Lian Li Theme Editor release ZIPs.

$ProjectRoot = Get-Item .
$ReleaseDir = Join-Path $ProjectRoot.FullName "release"
$TmpDir = Join-Path $ProjectRoot.FullName "tmp\release_build"
$ProjectFile = Join-Path $ProjectRoot.FullName "ThemeEditorCSharp.csproj"
$ProjectXml = [xml](Get-Content $ProjectFile)
$InformationalVersion = $ProjectXml.Project.PropertyGroup.InformationalVersion
if ([string]::IsNullOrWhiteSpace($InformationalVersion)) {
    $InformationalVersion = $ProjectXml.Project.PropertyGroup.Version
}
$VersionLabel = ($InformationalVersion -replace '^V\s*', 'V' -replace '\s+', '_')

if (!(Test-Path $ReleaseDir)) {
    New-Item -ItemType Directory -Path $ReleaseDir | Out-Null
}

# Clean previous build artifacts
if (Test-Path $TmpDir) {
    Remove-Item -Recurse -Force $TmpDir
}
New-Item -ItemType Directory -Path $TmpDir | Out-Null

Write-Host "1. Building C# Supporter and Theme Editor (Self-Contained)..."
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) {
    throw "Self-contained publish failed with exit code $LASTEXITCODE."
}

Write-Host "2. Packaging Self-Contained version..."
$SelfContainedPublishDir = Join-Path $ProjectRoot.FullName "bin\Release\net10.0-windows\win-x64\publish"
$SelfContainedTmp = Join-Path $TmpDir "SelfContained"
New-Item -ItemType Directory -Path $SelfContainedTmp | Out-Null

Copy-Item -Path (Join-Path $SelfContainedPublishDir "LianLiThemeEditor.exe") -Destination $SelfContainedTmp
if (Test-Path (Join-Path $SelfContainedPublishDir "LianLiThemeEditor.dll.config")) {
    Copy-Item -Path (Join-Path $SelfContainedPublishDir "LianLiThemeEditor.dll.config") -Destination $SelfContainedTmp
}
Copy-Item -Path (Join-Path $SelfContainedPublishDir "LianLiThemeEditor.TemplateWorker.exe") -Destination $SelfContainedTmp
Copy-Item -Path (Join-Path $SelfContainedPublishDir "LianLiThemeEditor.TemplateWorker.exe.config") -Destination $SelfContainedTmp
Copy-Item -Path (Join-Path $SelfContainedPublishDir "lang") -Destination $SelfContainedTmp -Recurse
Copy-Item -Path (Join-Path $ProjectRoot.FullName "README.md") -Destination $SelfContainedTmp

$SelfContainedZip = Join-Path $ReleaseDir "LianLiThemeEditor_${VersionLabel}_SelfContained_win-x64.zip"
if (Test-Path $SelfContainedZip) { Remove-Item $SelfContainedZip }
Compress-Archive -Path (Join-Path $SelfContainedTmp "*") -DestinationPath $SelfContainedZip

Write-Host "3. Building C# Supporter and Theme Editor (Framework-Dependent)..."
dotnet publish -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Framework-dependent publish failed with exit code $LASTEXITCODE."
}

Write-Host "4. Packaging Framework-Dependent version..."
$FrameworkDepPublishDir = Join-Path $ProjectRoot.FullName "bin\Release\net10.0-windows\publish"
$FrameworkDepTmp = Join-Path $TmpDir "FrameworkDependent"
New-Item -ItemType Directory -Path $FrameworkDepTmp | Out-Null

Copy-Item -Path (Join-Path $FrameworkDepPublishDir "LianLiThemeEditor.exe") -Destination $FrameworkDepTmp
Copy-Item -Path (Join-Path $FrameworkDepPublishDir "LianLiThemeEditor.dll") -Destination $FrameworkDepTmp
Copy-Item -Path (Join-Path $FrameworkDepPublishDir "LianLiThemeEditor.deps.json") -Destination $FrameworkDepTmp
Copy-Item -Path (Join-Path $FrameworkDepPublishDir "System.ServiceProcess.ServiceController.dll") -Destination $FrameworkDepTmp -ErrorAction SilentlyContinue
if (Test-Path (Join-Path $FrameworkDepPublishDir "LianLiThemeEditor.dll.config")) {
    Copy-Item -Path (Join-Path $FrameworkDepPublishDir "LianLiThemeEditor.dll.config") -Destination $FrameworkDepTmp
}
Copy-Item -Path (Join-Path $FrameworkDepPublishDir "LianLiThemeEditor.runtimeconfig.json") -Destination $FrameworkDepTmp
Copy-Item -Path (Join-Path $FrameworkDepPublishDir "LianLiThemeEditor.TemplateWorker.exe") -Destination $FrameworkDepTmp
Copy-Item -Path (Join-Path $FrameworkDepPublishDir "LianLiThemeEditor.TemplateWorker.exe.config") -Destination $FrameworkDepTmp
Copy-Item -Path (Join-Path $FrameworkDepPublishDir "lang") -Destination $FrameworkDepTmp -Recurse
Copy-Item -Path (Join-Path $ProjectRoot.FullName "README.md") -Destination $FrameworkDepTmp

$FrameworkDepZip = Join-Path $ReleaseDir "LianLiThemeEditor_${VersionLabel}_FrameworkDependent.zip"
if (Test-Path $FrameworkDepZip) { Remove-Item $FrameworkDepZip }
Compress-Archive -Path (Join-Path $FrameworkDepTmp "*") -DestinationPath $FrameworkDepZip

# Clean up temp directory
Remove-Item -Recurse -Force $TmpDir

Write-Host "Build and packaging complete!"
Write-Host "Packages created under:"
Write-Host "  - $SelfContainedZip"
Write-Host "  - $FrameworkDepZip"
