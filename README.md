# Lian Li LCD Template Editor

Windows desktop editor for creating, converting, validating, installing, and sharing Lian Li LCD device themes.

The app focuses on L-Connect 3 LCD devices such as HydroShift II LCD S/C, the 8.8" Universal Screen, and the 9.2" VM display. It edits L-Connect `.template` files, works with `.turtheme` packages, includes an in-app theme gallery, and now integrates Phone Link for optional local phone control.

# If you want to support this project you can donate here, so i can get new devices like Curve Oled to add support for it

https://ozgurce.lemonsqueezy.com/


## Current Version

`2.6.1`

Version 2.6.1 fixes gallery-installed theme background revert behavior and corrects portrait gallery package previews/background media.

## Main Features

- Create new themes from blank seed templates.
- Open and edit existing L-Connect `.template` files.
- Import, convert, fix, validate, and export `.turtheme` packages.
- Build L-Connect-compatible ZIP exports.
- Install and activate gallery or local themes through L-Connect.
- Edit layered theme content with grouping, preview selection, undo/redo, font handling, and recovery snapshots.
- Browse, filter, paginate, preview, install, and submit gallery themes.
- Use the integrated Phone Link tab to run an optional local phone-control web interface.
- Check for updates on startup or from About, then self-update from the GitHub MSI release.
- Minimize to tray, with tray actions for showing the app, starting/stopping Phone Link, and exiting.
- Use localized UI strings in English, Turkish, German, French, Korean, Russian, and Chinese.

## Supported Devices

- HydroShift II LCD S
- HydroShift II LCD C
- Universal Screen 8.8"
- VM 9.2"

Universal Screen exports are orientation-aware:

- Landscape: `1920x480`
- Portrait: `480x1920`

## Phone Link

Phone Link is integrated into Theme Editor as a separate tab. It is disabled by default and only starts when the user enables it and starts the server.

The Phone Link web UI uses the same language selected in Theme Editor settings. If the server is running while the app is closing, Theme Editor asks for confirmation and stops the server before exiting.

## Installation and Updates

Public releases ship as MSI installer packages:

```text
LianLiThemeEditorSetup.msi
```

The release intentionally does not include an installer EXE. The MSI:

- Installs the app and required support files.
- Creates Start Menu and desktop shortcuts.
- Registers uninstall information in Windows Apps / Programs and Features.
- Includes an uninstall shortcut.
- Closes running `LianLiThemeEditor.exe` instances before replacing files during install/update.

The app checks GitHub releases for updates and recognizes MSI release assets. During self-update it downloads the MSI and launches it through `msiexec`.

## Repository Layout

```text
Assets/                  App images and device artwork
Controls/                WPF helper controls
IntegratedPhoneControl/  Integrated Phone Link server and L-Connect control services
PhoneLinkWeb/            Phone Link web UI assets and settings
installer-wix/           WiX MSI product definition, license, and installer artwork
lang/                    Localization JSON files
Models/                  App data models
Services/                L-Connect, gallery, validation, recovery, and package services
SupporterCs/             .NET Framework helper used for template conversion/rendering
templates/blank-seeds/   Local seed templates used when creating blank themes
tools/                   Diagnostic/capture helper scripts and probes
ViewModels/              MVVM support classes
MainWindow.xaml          Main WPF UI
MainWindow.xaml.cs       Main editor workflow
ThemeEditorCSharp.csproj Main WPF project
build-msi.ps1            WiX MSI build script
dotnet-tools.json        Local tool manifest, including WiX
```

The remote `templates` directory is also used by the public gallery. Do not replace or delete remote-only gallery files when publishing source updates. Local source updates should preserve existing remote `templates/gallery.json` and gallery package assets unless those files are intentionally being changed.

## Requirements

- Windows
- .NET SDK with `net10.0-windows` support
- .NET Framework targeting support for `net48`
- L-Connect 3 for live import/export, template registration, and device activation workflows

The main app targets `net10.0-windows`. The helper project under `SupporterCs` targets `net48` because it loads and works with L-Connect/UsbMonitorL template types.

## Build

From the repository root:

```powershell
dotnet build .\ThemeEditorCSharp.csproj -c Release
```

The main project builds the helper project first, then copies the helper executable as:

```text
LianLiThemeEditor.TemplateWorker.exe
```

Build output is written under `bin_build/` and is intentionally not tracked.

## Build the MSI Installer

The MSI build uses the local .NET tool manifest:

```powershell
dotnet tool restore
.\build-msi.ps1
```

The installer is written to:

```text
bin_build\installer\LianLiThemeEditorSetup.msi
```

Generated installer output, WiX cache files, logs, and build folders should stay out of source control.

## Run

```powershell
dotnet run --project .\ThemeEditorCSharp.csproj
```

For full device import/export behavior, run on a Windows machine with L-Connect 3 installed. Some gallery and package validation features can still be used without a connected device.

## Template and Gallery Notes

The app has two different template concerns:

- `templates/blank-seeds/*.template` are local seed templates that ship with the editor and are copied to build/publish output.
- Other remote `templates` content can be gallery metadata, previews, packages, or user-shared themes used by the app at runtime through GitHub raw/API URLs.

When updating GitHub from a local workspace, publish source files and blank seeds without deleting remote-only gallery assets.
