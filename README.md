# Lian Li LCD Template Editor

**Current release:** V 1.2 Beta

An unofficial Windows editor for Lian Li L-Connect 3 LCD templates. It lets you load an existing LCD template, edit its layers with a live preview, export the edited template as an L-Connect import package, and apply changes back to L-Connect.

> This project is not affiliated with, endorsed by, or supported by Lian Li. It edits local L-Connect 3 template/profile files. Keep backups of important themes before experimenting.

## Supported Devices

- HydroShift II LCD-S
- HydroShift II LCD-C
- 8.8" Universal Screen
- VM 9.2 LCD

The editor resolves templates, backgrounds, images, videos, graph modules, and preview assets from the normal L-Connect folders under `C:\ProgramData\Lian-Li\L-Connect 3` and the installed L-Connect assets folder.

## Requirements

- Windows 10 or Windows 11
- L-Connect 3 installed
- Administrator rights recommended when writing into `C:\ProgramData\Lian-Li\L-Connect 3`
- .NET Framework 4.8 for the bundled C# template supporter

## Release Package

The release ZIP should contain:

- `LianLiThemeEditor.exe`
- `supporter.exe` (built from `SupporterCs`)
- `lang/`
- `README.md`

Keep these files together in the same folder. The editor uses the bundled C# `supporter.exe` for low-level L-Connect template operations and `lang/*.json` for UI language text. Built-in gallery packages and previews are maintained in the repository `templates/` folder, but that folder is not bundled into release ZIPs.

## Basic Usage

1. Open L-Connect 3.
2. Select your HydroShift LCD device and template.
3. Start `LianLiThemeEditor.exe`.
4. Keep `Use active template` enabled or choose a template manually.
5. Click `Load`.
6. Select a layer from the layer list or the preview canvas.
7. Edit position, size, font, color, text/data source, graph settings, image settings, or background media.
8. Use `Apply` for a single layer or `Apply All` for the current set of changes.
9. Use `Export Theme` to create an L-Connect importable ZIP.

## Main Features

- C# WPF interface with glassmorphism dark/light themes.
- Layer list with drag-and-drop ordering.
- Layer grouping, group visibility/lock actions, group duplication, and context menu movement.
- Multi-select layer actions including batch edit, alignment, distribution, duplicate, hide/show, lock/unlock, and solo preview mode.
- Undo/redo with edit history for template-side changes.
- Live preview canvas with zoom, Ctrl + mouse wheel zoom, alignment guides, and direct layer dragging.
- Right-side properties panel that only shows controls supported by the selected layer.
- Add layer workflow for text, data, image, graph, and optional shadow layers.
- Text/data layer editing: position, size, font, color, bold/italic, alignment, character spacing, line height, and format where supported.
- Image layer editing: image file, size/zoom, rotate, and rect/crop fields where supported.
- Graph editing: graph type, data source, dimensions, fill/track colors, gradient color, split/subsection options, direction, line/column/border settings, and supported graph-specific fields.
- Background upload/export support for MP4, GIF, JPG/JPEG, and PNG.
- L-Connect compatible export package generation.
- Import and validation flow for `.lltheme` and L-Connect ZIP packages.
- Built-in theme gallery with local packages, community stats, ratings, downloads, device filters, sorting, install/reinstall, and optional activation after install.
- Community theme submission workflow for review.
- Manual backup/restore and automatic recovery snapshots for unsaved work.
- Diagnostic info/package generation for bug reports.
- VM 9.2 conversion flow for compatible 8.8" L-Connect ZIP themes.
- Active template loading on startup workflow.
- Safer Apply/Apply All flow that refreshes layer state when L-Connect changes the template.
- Multi-language UI: English, Turkish, Russian, and Simplified Chinese.

## Data Sources

The editor exposes practical L-Connect data sources with user-friendly names and keeps the internal L-Connect keys when saving.

Common supported data sources include:

- CPU Clock
- CPU Clock (GHz)
- CPU Fan
- CPU Load
- CPU Model
- CPU Power
- CPU Temperature
- CPU Temperature (F)
- CPU Voltage
- Date
- Day
- Drive Load
- FPS
- GPU Clock
- GPU Clock (GHz)
- GPU Fan
- GPU Load
- GPU Model
- GPU Power
- GPU RAM
- GPU RAM Load
- GPU Temperature
- GPU Temperature (F)
- GPU Valid RAM
- GPU Voltage
- HDD Temperature
- HDD Temperature (F)
- HDD Used
- Pump / Water Pump
- RAM
- RAM Load
- RAM Model
- RAM Total
- RAM Valid
- Static Text
- Time
- Upload Speed
- Download Speed

### FPS

FPS uses the L-Connect/HWiNFO sensor path exposed as `FPS_AVG` internally and is displayed in the editor as `FPS`. If L-Connect or HWiNFO is not providing this sensor, the preview may fall back to a sample value.

### Power Values

CPU Power and GPU Power are formatted as integers in the editor and save path so decimal fractions from L-Connect sensors do not clutter compact LCD layouts.

## Date And Time Formats

Only formats verified to work with L-Connect are exposed.

Time:

```text
00:00
00:00:00
h_12
h_24
m
s
AM
PM
```

Date:

```text
Y-M-D
D-M-Y
D.M.Y
M
D
```

Day:

```text
Day_en
ddd
```

## Graph Notes

The graph list is filtered to the useful H2 graph styles instead of showing duplicate or unsupported L-Connect module entries. The editor shows graph controls based on the selected graph object's supported fields.

Known graph families:

- Bar Chart: horizontal bar graph
- Donut Bar: circular ring graph
- Stream Bar: line/stream graph

Some fields only apply to specific graph classes. Unsupported fields are intentionally hidden to avoid saving no-op or unstable values.

## Background Media

Backgrounds can be selected as:

- MP4
- GIF
- JPG/JPEG
- PNG

The export flow attempts to include the edited background and generated preview assets so L-Connect imports show the edited theme instead of the original default preview.

## Repository Structure

```text
Assets/                 UI background assets
Models/                 Layer/template model classes
Services/               C# supporter process bridge
lang/                   UI language JSON files
App.xaml(.cs)           App entry
MainWindow.xaml(.cs)    Main editor UI and workflow
ColorPickerDialog.*     Color picker UI
supporter.exe           C# L-Connect template/profile helper used by releases
```

Generated folders such as `bin/`, `obj/`, `dist/`, local logs, local settings, and release ZIPs are ignored by Git.

## Changelog

### V 1.2 Beta

- Added support for 8.8" Universal Screen and VM 9.2 LCD workflows.
- Added built-in Theme Gallery with local packages, previews, device filters, sorting, download counts, ratings, and install/reinstall flow.
- Added community theme submission support backed by the Cloudflare gallery service.
- Added `.lltheme`/ZIP import validation and safer activation handling for already-installed theme identities.
- Added VM 9.2 conversion flow for compatible 8.8" L-Connect ZIP packages.
- Added layer grouping, group actions, batch editing, alignment/distribution tools, preview context menu actions, and solo preview mode.
- Added undo/redo history and automatic recovery snapshots for unsaved layer edits.
- Added diagnostic info and diagnostic package creation for support reports.
- Added manual template backup/restore controls.
- Added localization quality checks to keep language files structurally aligned.
- Updated release packaging to include `lang/`, `supporter.exe`, and `README.md` with versioned ZIP names.

### V 1.0 Beta

- Migrated the editor workflow from the original PowerShell UI to the C# WPF application.
- Added the new glassmorphism layout with left layer list, central canvas, and right properties sidebar.
- Added draggable layer cards and direct preview-canvas layer movement.
- Added Add Layer popup flow for text, data, image, graph, and shadow-enabled layers.
- Added L-Connect compatible `Export Theme` ZIP generation.
- Added support for exporting/importing edited background media instead of falling back to the default template background.
- Added JPG/JPEG/PNG support for background images in addition to GIF/MP4.
- Added image layer sizing based on source image dimensions and safer image apply behavior.
- Added graph style filtering and graph-specific controls for supported fields.
- Added graph dimensions, fill color, track/background color, gradient color, subsection/split, direction, line width, column width, border width, and related supported settings.
- Added live preview value handling for supported sensors.
- Added FPS support through the L-Connect/HWiNFO `FPS_AVG` path.
- Added CPU/GPU power, voltage, model, RAM, drive, pump, fan, upload/download, HDD, and GPU RAM data paths.
- Added integer formatting for CPU Power and GPU Power values.
- Added safer date/time/day format handling and removed unsupported date/weekday combinations.
- Added live sample value preservation where possible when loading templates.
- Added light and dark UI themes.
- Added Ctrl + mouse wheel preview zoom and 100% to 300% zoom range.
- Added startup active-template workflow.
- Removed unsupported/no-op Font W and Font Gradient controls from the editor UI and apply path.

## Development Notes

- Build with the .NET SDK on Windows.
- The app targets `net10.0-windows` and uses WPF.
- `supporter.exe` is built automatically from `SupporterCs` and is required at runtime.
- `dotnet build`, `dotnet run`, and `dotnet publish` build and bundle the C# supporter automatically.

## Disclaimer

Use at your own risk. L-Connect controls LCD screens, fans, and pumps through its own services. Avoid unnecessary service restarts while editing and always keep backups of themes you care about.
