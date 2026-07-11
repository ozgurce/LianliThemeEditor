# Lian Li LCD Template Editor

Windows desktop editor for creating, converting, validating, importing, exporting, and sharing Lian Li LCD device themes.

The project is currently focused on L-Connect 3 LCD devices such as HydroShift II LCD S/C, the 8.8" Universal Screen, and the 9.2" VM display. It edits L-Connect `.template` files, works with `.turtheme` packages, and includes a gallery workflow backed by the repository's `templates` content.

## Current Version

`V 2.3` / `2.3.0`

This version includes Universal Screen support, orientation-aware export/import, improved gallery performance, better L-Connect data-source handling, more reliable background packaging, and many editor UI fixes.

## What It Does

- Creates new themes from blank seed templates.
- Opens and edits existing L-Connect template files.
- Imports, converts, fixes, and exports `.turtheme` packages.
- Builds L-Connect-compatible ZIP exports.
- Validates theme packages, backgrounds, manifests, and device compatibility.
- Installs and activates gallery or local themes through L-Connect.
- Supports layered editing, grouping, preview selection, undo/redo, font handling, and recovery snapshots.
- Provides gallery browsing, filtering, pagination, preview caching, and submission packaging.
- Includes localized UI strings in English, Turkish, German, French, Korean, Russian, and Chinese.

## Supported Devices

- HydroShift II LCD S
- HydroShift II LCD C
- Universal Screen 8.8"
- VM 9.2"

Universal Screen exports are orientation-aware:

- Landscape: `1920x480`
- Portrait: `480x1920`

## Repository Layout

```text
Assets/                 App images and device artwork
Controls/               WPF helper controls
lang/                   Localization JSON files
Models/                 App data models
Services/               L-Connect, gallery, validation, recovery, and package services
SupporterCs/            .NET Framework helper used for template conversion/rendering
templates/blank-seeds/  Local seed templates used when creating blank themes
ViewModels/             MVVM support classes
MainWindow.xaml         Main WPF UI
MainWindow.xaml.cs      Main editor workflow
ThemeEditorCSharp.csproj
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
