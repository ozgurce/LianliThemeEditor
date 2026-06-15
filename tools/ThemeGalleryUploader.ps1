Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$AppRoot = Split-Path -Parent $ScriptDir
$DefaultRepo = Join-Path $AppRoot "tmp\github-lianli"
$Supporter = Join-Path $AppRoot "SupporterCs\bin\Debug\net48\supporter.exe"
if (!(Test-Path $Supporter)) {
    $Supporter = Join-Path $AppRoot "SupporterCs\bin\Release\net48\supporter.exe"
}

$Devices = @(
    [pscustomobject]@{ Name = '8.8" Universal Screen'; Model = 'universal-screen-8.8-inch' },
    [pscustomobject]@{ Name = 'Hydroshift II LCD-S'; Model = 'hydroshift-ii-lcd-s' },
    [pscustomobject]@{ Name = 'Hydroshift II LCD-C'; Model = 'hydroshift-ii-lcd-c' },
    [pscustomobject]@{ Name = 'VM 9.2 LCD'; Model = 'vm-9.2-inch' }
)

function Sanitize-Id([string]$value) {
    $id = ($value.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($id)) { $id = "theme" }
    return $id
}

function Get-ZipEntryByName($zip, [string]$name) {
    $normalized = $name.Replace('\', '/')
    return @($zip.Entries | Where-Object { $_.FullName.Replace('\', '/') -ieq $normalized })[0]
}

function Copy-ZipEntry($sourceEntry, $destinationZip, [string]$destinationName) {
    $entry = $destinationZip.CreateEntry($destinationName, [System.IO.Compression.CompressionLevel]::Optimal)
    $input = $sourceEntry.Open()
    $output = $entry.Open()
    try {
        $input.CopyTo($output)
    }
    finally {
        $output.Dispose()
        $input.Dispose()
    }
}

function Write-ZipTextEntry($zip, [string]$name, [string]$text) {
    $entry = $zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
    $writer = New-Object System.IO.StreamWriter($entry.Open(), [System.Text.Encoding]::UTF8)
    try {
        $writer.Write($text)
    }
    finally {
        $writer.Dispose()
    }
}

function Run-Process([string]$fileName, [string[]]$arguments, [string]$workingDirectory) {
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $fileName
    foreach ($arg in $arguments) { [void]$startInfo.ArgumentList.Add($arg) }
    $startInfo.WorkingDirectory = $workingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "$fileName failed with exit code $($process.ExitCode).`r`n$stdout`r`n$stderr"
    }

    return ($stdout + $stderr).Trim()
}

function Ensure-Repo([string]$repoPath, [string]$repoUrl) {
    if (Test-Path (Join-Path $repoPath ".git")) {
        return
    }

    $parent = Split-Path -Parent $repoPath
    if (!(Test-Path $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    Run-Process "git" @("clone", $repoUrl, $repoPath) $parent | Out-Null
}

function Extract-PreviewFromTemplate([string]$templatePath, [string]$themeId, [string]$deviceModel, [string]$previewPath) {
    if (!(Test-Path $Supporter)) {
        throw "supporter.exe was not found: $Supporter"
    }

    $stage = Join-Path ([System.IO.Path]::GetTempPath()) ("gallery_preview_" + [guid]::NewGuid().ToString("N"))
    $templateRoot = Join-Path $stage "template"
    $thumbRoot = Join-Path $stage "thumb"
    New-Item -ItemType Directory -Force -Path $templateRoot, $thumbRoot | Out-Null
    try {
        Copy-Item $templatePath (Join-Path $templateRoot "$themeId.template") -Force
        Run-Process $Supporter @(
            "-DeviceModel", $deviceModel,
            "-TemplateRoot", $templateRoot,
            "-ThumbnailRoot", $thumbRoot,
            "-ExtractMissingPreviews"
        ) $AppRoot | Out-Null

        $extracted = Join-Path $thumbRoot "$themeId.png"
        if (!(Test-Path $extracted)) {
            throw "The template did not expose an embedded preview image."
        }

        Copy-Item $extracted $previewPath -Force
    }
    finally {
        if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    }
}

function Convert-ZipToGalleryPackage($row, [string]$templatesRoot) {
    $sourcePath = [string]$row.Cells["Source"].Value
    $themeId = Sanitize-Id ([string]$row.Cells["Id"].Value)
    $themeName = [string]$row.Cells["ThemeName"].Value
    $author = [string]$row.Cells["Author"].Value
    $deviceModel = [string]$row.Cells["Device"].Value
    $deviceName = (@($Devices | Where-Object { $_.Model -eq $deviceModel })[0]).Name

    if (!(Test-Path $sourcePath)) { throw "ZIP was not found: $sourcePath" }
    if ([string]::IsNullOrWhiteSpace($themeName)) { throw "Theme name is required for $sourcePath" }
    if ([string]::IsNullOrWhiteSpace($author)) { throw "Author is required for $themeName" }

    $packagesRoot = Join-Path $templatesRoot "packages"
    $previewsRoot = Join-Path $templatesRoot "previews"
    New-Item -ItemType Directory -Force -Path $packagesRoot, $previewsRoot | Out-Null

    $packagePath = Join-Path $packagesRoot "$themeId.lltheme"
    $previewPath = Join-Path $previewsRoot "$themeId.png"
    if (Test-Path $packagePath) { Remove-Item $packagePath -Force }

    $tempTemplate = Join-Path ([System.IO.Path]::GetTempPath()) ("gallery_template_" + [guid]::NewGuid().ToString("N") + ".template")
    $sourceZip = [System.IO.Compression.ZipFile]::OpenRead($sourcePath)
    $destZip = [System.IO.Compression.ZipFile]::Open($packagePath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $templateEntry = @($sourceZip.Entries | Where-Object { $_.Name -like "*.template" })[0]
        if ($null -eq $templateEntry) { throw "No .template file was found inside $sourcePath" }

        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($templateEntry, $tempTemplate, $true)
        Copy-ZipEntry $templateEntry $destZip "template/theme.template"

        $backgroundEntry = @($sourceZip.Entries | Where-Object {
            $_.Name -match '\.(mp4|h264|gif|png|jpg|jpeg)$'
        })[0]
        $backgroundFile = ""
        if ($null -ne $backgroundEntry) {
            $safeName = [System.IO.Path]::GetFileName($backgroundEntry.Name)
            $backgroundFile = "background/$safeName"
            Copy-ZipEntry $backgroundEntry $destZip $backgroundFile
        }

        $manifest = [ordered]@{
            FormatVersion = 1
            App = "Lian Li LCD Theme Editor"
            DeviceModel = $deviceModel
            TemplateId = $themeId
            TemplateFile = "template/theme.template"
            BackgroundFile = $backgroundFile
            ImageFiles = @()
            ExportedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        } | ConvertTo-Json -Depth 6
        Write-ZipTextEntry $destZip "manifest.json" $manifest
    }
    finally {
        $destZip.Dispose()
        $sourceZip.Dispose()
    }

    try {
        Extract-PreviewFromTemplate $tempTemplate $themeId $deviceModel $previewPath
    }
    finally {
        if (Test-Path $tempTemplate) { Remove-Item $tempTemplate -Force }
    }

    return [ordered]@{
        id = $themeId
        name = $themeName
        author = $author
        deviceModel = $deviceModel
        deviceName = $deviceName
        previewUrl = "previews/$themeId.png"
        packageUrl = "packages/$themeId.lltheme"
    }
}

function Update-GalleryManifest([string]$templatesRoot, [object[]]$newThemes) {
    $manifestPath = Join-Path $templatesRoot "gallery.json"
    $themes = @()
    if (Test-Path $manifestPath) {
        $existing = Get-Content $manifestPath -Raw | ConvertFrom-Json
        if ($existing.themes) { $themes = @($existing.themes) }
    }

    foreach ($newTheme in $newThemes) {
        $themes = @($themes | Where-Object { $_.id -ne $newTheme.id })
        $themes += [pscustomobject]$newTheme
    }

    [ordered]@{ themes = $themes } |
        ConvertTo-Json -Depth 8 |
        Set-Content -Path $manifestPath -Encoding UTF8
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "Theme Gallery Uploader"
$form.Width = 1120
$form.Height = 680
$form.StartPosition = "CenterScreen"
$form.Font = New-Object System.Drawing.Font("Segoe UI", 9)

$repoLabel = New-Object System.Windows.Forms.Label
$repoLabel.Text = "GitHub repo folder"
$repoLabel.Left = 16
$repoLabel.Top = 18
$repoLabel.Width = 130
$form.Controls.Add($repoLabel)

$repoBox = New-Object System.Windows.Forms.TextBox
$repoBox.Left = 150
$repoBox.Top = 14
$repoBox.Width = 690
$repoBox.Text = $DefaultRepo
$form.Controls.Add($repoBox)

$repoBrowse = New-Object System.Windows.Forms.Button
$repoBrowse.Text = "Browse"
$repoBrowse.Left = 850
$repoBrowse.Top = 12
$repoBrowse.Width = 80
$form.Controls.Add($repoBrowse)

$cloneButton = New-Object System.Windows.Forms.Button
$cloneButton.Text = "Clone / Use Repo"
$cloneButton.Left = 940
$cloneButton.Top = 12
$cloneButton.Width = 130
$form.Controls.Add($cloneButton)

$repoUrlBox = New-Object System.Windows.Forms.TextBox
$repoUrlBox.Left = 150
$repoUrlBox.Top = 46
$repoUrlBox.Width = 690
$repoUrlBox.Text = "https://github.com/ozgurce/LianliThemeEditor.git"
$form.Controls.Add($repoUrlBox)

$repoUrlLabel = New-Object System.Windows.Forms.Label
$repoUrlLabel.Text = "Repo URL"
$repoUrlLabel.Left = 16
$repoUrlLabel.Top = 50
$repoUrlLabel.Width = 130
$form.Controls.Add($repoUrlLabel)

$grid = New-Object System.Windows.Forms.DataGridView
$grid.Left = 16
$grid.Top = 92
$grid.Width = 1054
$grid.Height = 430
$grid.AllowUserToAddRows = $false
$grid.AllowUserToDeleteRows = $true
$grid.AutoSizeColumnsMode = "Fill"
$grid.SelectionMode = "FullRowSelect"
$grid.MultiSelect = $true
$grid.RowHeadersVisible = $false
$form.Controls.Add($grid)

$sourceColumn = New-Object System.Windows.Forms.DataGridViewTextBoxColumn
$sourceColumn.Name = "Source"
$sourceColumn.HeaderText = "Source ZIP"
$sourceColumn.FillWeight = 210
$grid.Columns.Add($sourceColumn) | Out-Null

$idColumn = New-Object System.Windows.Forms.DataGridViewTextBoxColumn
$idColumn.Name = "Id"
$idColumn.HeaderText = "Theme ID"
$idColumn.FillWeight = 90
$grid.Columns.Add($idColumn) | Out-Null

$nameColumn = New-Object System.Windows.Forms.DataGridViewTextBoxColumn
$nameColumn.Name = "ThemeName"
$nameColumn.HeaderText = "Theme Name"
$nameColumn.FillWeight = 125
$grid.Columns.Add($nameColumn) | Out-Null

$authorColumn = New-Object System.Windows.Forms.DataGridViewTextBoxColumn
$authorColumn.Name = "Author"
$authorColumn.HeaderText = "Author"
$authorColumn.FillWeight = 80
$grid.Columns.Add($authorColumn) | Out-Null

$deviceColumn = New-Object System.Windows.Forms.DataGridViewComboBoxColumn
$deviceColumn.Name = "Device"
$deviceColumn.HeaderText = "Device"
$deviceColumn.DataSource = @($Devices | ForEach-Object { $_.Model })
$deviceColumn.FillWeight = 120
$grid.Columns.Add($deviceColumn) | Out-Null

$addButton = New-Object System.Windows.Forms.Button
$addButton.Text = "Add ZIP(s)"
$addButton.Left = 16
$addButton.Top = 538
$addButton.Width = 110
$form.Controls.Add($addButton)

$removeButton = New-Object System.Windows.Forms.Button
$removeButton.Text = "Remove Selected"
$removeButton.Left = 136
$removeButton.Top = 538
$removeButton.Width = 130
$form.Controls.Add($removeButton)

$uploadButton = New-Object System.Windows.Forms.Button
$uploadButton.Text = "Build, Commit && Push"
$uploadButton.Left = 870
$uploadButton.Top = 538
$uploadButton.Width = 200
$uploadButton.Height = 32
$form.Controls.Add($uploadButton)

$statusBox = New-Object System.Windows.Forms.TextBox
$statusBox.Left = 16
$statusBox.Top = 584
$statusBox.Width = 1054
$statusBox.Height = 45
$statusBox.ReadOnly = $true
$statusBox.Multiline = $true
$statusBox.ScrollBars = "Vertical"
$form.Controls.Add($statusBox)

function Set-Status([string]$message) {
    $statusBox.Text = $message
    $statusBox.SelectionStart = $statusBox.Text.Length
    $statusBox.ScrollToCaret()
    [System.Windows.Forms.Application]::DoEvents()
}

$repoBrowse.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Choose the cloned GitHub repository folder"
    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $repoBox.Text = $dialog.SelectedPath
    }
})

$cloneButton.Add_Click({
    try {
        Set-Status "Preparing repository..."
        Ensure-Repo $repoBox.Text $repoUrlBox.Text
        Set-Status "Repository is ready."
    }
    catch {
        [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "Repository error") | Out-Null
    }
})

$addButton.Add_Click({
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Title = "Choose theme ZIP package(s)"
    $dialog.Filter = "ZIP packages (*.zip)|*.zip|All files (*.*)|*.*"
    $dialog.Multiselect = $true
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return }

    foreach ($file in $dialog.FileNames) {
        $base = [System.IO.Path]::GetFileNameWithoutExtension($file)
        $rowIndex = $grid.Rows.Add()
        $row = $grid.Rows[$rowIndex]
        $row.Cells["Source"].Value = $file
        $row.Cells["Id"].Value = Sanitize-Id $base
        $row.Cells["ThemeName"].Value = $base
        $row.Cells["Author"].Value = ""
        $row.Cells["Device"].Value = "universal-screen-8.8-inch"
    }
})

$removeButton.Add_Click({
    foreach ($row in @($grid.SelectedRows)) {
        if (!$row.IsNewRow) { $grid.Rows.Remove($row) }
    }
})

$uploadButton.Add_Click({
    try {
        if ($grid.Rows.Count -eq 0) { throw "Add at least one ZIP package first." }

        Set-Status "Preparing repository..."
        Ensure-Repo $repoBox.Text $repoUrlBox.Text
        $templatesRoot = Join-Path $repoBox.Text "templates"
        New-Item -ItemType Directory -Force -Path $templatesRoot | Out-Null

        $newThemes = @()
        foreach ($row in $grid.Rows) {
            if ($row.IsNewRow) { continue }
            Set-Status ("Building gallery package: " + $row.Cells["ThemeName"].Value)
            $newThemes += Convert-ZipToGalleryPackage $row $templatesRoot
        }

        Set-Status "Updating gallery manifest..."
        Update-GalleryManifest $templatesRoot $newThemes

        Set-Status "Committing changes..."
        Run-Process "git" @("add", "templates") $repoBox.Text | Out-Null
        $status = Run-Process "git" @("status", "--short") $repoBox.Text
        if ([string]::IsNullOrWhiteSpace($status)) {
            Set-Status "No changes to commit."
            return
        }

        $message = "Add gallery theme" + ($(if ($newThemes.Count -gt 1) { "s" } else { "" }))
        Run-Process "git" @("commit", "-m", $message) $repoBox.Text | Out-Null
        Set-Status "Pushing to GitHub..."
        Run-Process "git" @("push", "origin", "main") $repoBox.Text | Out-Null
        Set-Status "Done. Theme gallery was updated on GitHub."
    }
    catch {
        [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "Upload failed") | Out-Null
        Set-Status "Upload failed."
    }
})

[void]$form.ShowDialog()
