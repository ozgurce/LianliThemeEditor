# Lian Li LCD Template Editor

**Current release:** V 1.4

Lian Li LCD Template Editor is an unofficial Windows editor for L-Connect 3 LCD templates. It is built for editing, repairing, exporting, importing, previewing, sharing, and applying LCD themes without manually digging through L-Connect template/profile files.

The app can load the active L-Connect theme, show every editable layer, let you change the layout visually, and write the result back through the bundled C# `LianLiThemeEditor.TemplateWorker.exe` helper. It also includes a theme gallery, package validation, recovery snapshots, diagnostics, multi-language UI, and workflows for newer wide LCD screens.

> This project is not affiliated with, endorsed by, or supported by Lian Li. It edits local L-Connect 3 template/profile files. Keep backups of themes you care about before experimenting.

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
- .NET desktop runtime for the editor build you use
- .NET Framework 4.8 for the bundled C# template supporter

## Release Package

Release ZIPs are intentionally small. They should contain:

- `LianLiThemeEditor.exe`
- `LianLiThemeEditor.TemplateWorker.exe`
- `LianLiThemeEditor.TemplateWorker.exe.config`
- `lang/`
- `README.md`

Keep these files together in the same folder. Gallery manifests, packages, and previews are fetched from GitHub at runtime and are never bundled with the application.

## What It Can Do

### Template Loading

- Load the active L-Connect template automatically.
- Manually choose a supported device and template.
- Fall back to available local templates when the active template cannot be resolved.
- Refresh layer state when L-Connect changes the template externally.
- Keep current template path, template id, background path, and selected device state visible to the editor workflow.
- Resolve ProgramData and installed asset folders for L-Connect template/media dependencies.

### Visual Editor

- WPF desktop UI with dark and light glass-style themes.
- Device-aware preview canvas for square, round, 8.8", and VM 9.2 layouts.
- Live canvas preview for backgrounds, text, data, image, graph, sensor, clock, and animation layers.
- Zoom controls, fit-to-view, mouse-wheel zoom, and preview scrolling.
- Direct preview selection and dragging.
- Preview resize handles for supported layer types.
- Alignment guide lines and canvas reference guides.
- Right-click preview context menu for duplicate, hide/show, lock/unlock, bring forward, send backward, and solo selected.
- Solo preview mode to temporarily isolate selected layer(s).
- Landscape/portrait handling for the 8.8" Universal Screen.
- VM 9.2 direct-apply warning and conversion workflow where applicable.

### Layer Management

- Layer list with visual cards, icons, dirty badges, lock state, and visibility state.
- Multi-select support for layer actions.
- Drag-and-drop style reordering through editor actions.
- Move selected layer(s) up/down.
- Duplicate one or multiple layers.
- Remove one or multiple layers.
- Hide/show layers without losing them.
- Lock/unlock layers in the editor preview.
- Select layers from the grid or directly from the preview canvas.
- Preserve layer indexes and refresh them after template operations.
- Protect background/animation base layers from unsafe operations.

### Layer Groups

- Create named layer groups.
- Assign selected layers to a group.
- Rename groups.
- Duplicate whole groups.
- Remove a group while keeping its layers.
- Move selected layers into or out of groups.
- Group visibility toggle.
- Group lock/unlock toggle.
- Group color labels.
- Group expand/collapse state in the layer list.
- Group selection by clicking group headers.
- Group metadata persistence inside the template through editor metadata.
- Option to disable layer grouping from Settings.

### Batch And Alignment Tools

- Batch edit selected layers.
- Batch color change.
- Batch font change where supported.
- Batch position offset.
- Batch visibility change.
- Batch lock/unlock change.
- Align selected layers to canvas left, center, right, top, middle, or bottom.
- Distribute selected layers horizontally or vertically.
- Mark edited layers dirty so Apply/Apply All can warn before writing.

### Undo, Redo, And History

- Undo and redo for editor-side template changes.
- Ctrl+Z / Ctrl+Y support.
- Edit history popup.
- History labels for moves, resizes, properties, batch edits, alignment, visibility changes, group visibility, recovery restore, and grid edits.
- Undo snapshots stored in memory while editing.
- Dirty layer tracking after undo/redo so the user knows what still needs applying.

### Add Layer Workflow

- Add animation/background media layer.
- Add static text layer.
- Add data/sensor text layer.
- Add image layer.
- Add status bar / segmented bar graph.
- Add dynamic status graph.
- Add curved bar / donut / arc graph.
- Add ring graph.
- Add stream/chart graph.
- Optional shadow creation during add flow.
- Default positioning, font, color, alignment, and format values for safer new layers.
- Automatic selection of the newly created editable layer.

### Text And Data Editing

- Edit X/Y position.
- Edit text content.
- Enable text override for data-backed text layers.
- Edit font family.
- Edit size.
- Edit color.
- Bold and italic where supported.
- Alignment index/name handling.
- Character spacing.
- Line height.
- Time, date, and day format controls.
- Preserve valid L-Connect internal data keys while showing friendly names.
- Hide unsupported font controls for layer types that cannot safely write them.

### Image And Media Editing

- Replace image media.
- Replace background media.
- Support PNG, JPG/JPEG, GIF, MP4, and H.264-style media workflows where L-Connect accepts them.
- Edit image zoom rate.
- Edit image rotation.
- Edit image/source rect fields where supported.
- Use source image dimensions for safer initial sizing.
- Cache image dimensions and previews for faster redraw.
- Extract missing embedded previews when possible.
- Update theme preview images during export/apply flows.

### Graph Editing

- Filter graph styles to practical/useful H2 graph families instead of showing unstable duplicates.
- Edit graph data source.
- Edit width, height, radius, diameter, and thickness where supported.
- Edit front/fill, back/track, line, border, and gradient colors.
- Toggle gradients where supported.
- Edit graph direction.
- Edit line width, column width, border width.
- Edit inner circle radius.
- Edit split block width and split blank width.
- Toggle subsection/split behavior.
- Toggle fill back, revert, transparent background, ring border, round corners, block mode, and direction inversion where supported.
- Edit front/back alpha, max value, start percentage, and total angle for supported graph classes.
- Render richer local previews for supported graph styles.

### Sensor Preview And Sensor Layers

- Add and edit `GraphSensor` style layers.
- Sensor style/type handling.
- Sensor color 1, color 2, background color, main font color, top font color, and bottom font color.
- Sensor font family and zoom rate.
- Local sensor preview rendering through the supporter helper.
- Live sample values and fallback preview values when L-Connect/HWiNFO data is unavailable.

### Gauge Angle Calculation

Gauge angles use the 12 o'clock position as `0°` and increase clockwise:

| Clock position | Angle |
| --- | ---: |
| 12 o'clock | `0°` |
| 3 o'clock | `90°` |
| 6 o'clock | `180°` |
| 9 o'clock | `-90°` or `270°` |

`Start` is the needle angle at 0% and `Total` is the sweep range, not the final angle. The displayed angle is calculated as:

```text
Displayed angle = Start + (value / 100 × Total)
```

For example, to move from 9 o'clock at 0% to 3 o'clock at 100%:

```text
Start = -90
Total = 180
Rate Offset = 0
Revert = Off
```

This produces 9 o'clock at 0%, 12 o'clock at 50%, and 3 o'clock at 100%. Each hour step on a clock face equals `30°`. Enable `Revert` to sweep in the opposite direction. `Rate Offset` shifts the normalized input rate before the sweep is calculated and should normally remain `0`.

### Data Sources

The editor exposes practical L-Connect data sources with user-friendly names while preserving the internal keys when saving.

Common supported sources include:

- Static Text
- Time, Date, Day, AM/PM
- FPS / `FPS_AVG`
- CPU clock, load, temperature, fan, power, voltage, and model
- GPU clock, load, temperature, fan, power, voltage, model, memory used, memory total, and memory load
- RAM used, available, total, load, and model-related values
- Drive load, drive used, drive temperature
- Pump / water pump
- Water temperature
- Upload speed and download speed

CPU Power and GPU Power are formatted as integers so compact LCD layouts do not get cluttered with decimal fractions.

### Verified Date And Time Formats

Time formats:

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

Date formats:

```text
Y-M-D
D-M-Y
D.M.Y
M
D
```

Day formats:

```text
Day_en
ddd
```

### Backgrounds

- Load current template background.
- Upload a new background image/video.
- Revert current template background.
- Copy background media into L-Connect-compatible locations.
- Wait for uploaded background files to become available.
- Refresh preview once background media has been written.
- Include edited background and generated preview assets in exported L-Connect packages.

### Export, Import, And Install

- Export `.lltheme` packages.
- Export L-Connect-compatible ZIP packages.
- Import `.lltheme` packages.
- Import existing L-Connect ZIP packages.
- Validate packages before install/import.
- Detect corrupt packages.
- Detect missing manifest/template files.
- Detect unsafe paths in packages.
- Detect missing background/image references.
- Warn when a theme identity is already installed.
- Resolve template identity aliases and internal IDs from template content.
- Avoid duplicate imports when an installed template already matches the package.
- Activate installed themes through L-Connect where supported.
- Copy template backgrounds during activation/import.

### VM 9.2 And 8.8" Conversion

- 8.8" Universal Screen editing with orientation support.
- VM 9.2 device selection and preview assets.
- Convert compatible 8.8" L-Connect ZIP themes to VM 9.2 packages.
- Show direct-apply limitations for VM 9.2 when L-Connect cannot accept the same live apply path.

### Theme Gallery

- Built-in gallery tab.
- Load the official manifest, packages, and previews directly from GitHub over HTTPS.
- Load community metadata from the Cloudflare-backed service while accepting only GitHub-hosted package and preview assets.
- Never fall back to local gallery files; show a connection error when GitHub is unavailable.
- Device filters for HydroShift II LCD-S, HydroShift II LCD-C, 8.8" Universal Screen, and VM 9.2 LCD.
- "My devices" settings to show only relevant devices across editor and gallery.
- Rating filter.
- Sorting by default order, most downloaded, highest rated, most votes, and name A-Z.
- Download/reinstall themes.
- Track installed gallery items.
- Optional activate-after-install behavior.
- Download count display.
- Average rating display.
- Per-user star voting when the stats endpoint is configured.
- Community theme details view.
- Cache downloaded package bytes during the session.

### Community Submission

- Send the current theme for review.
- Submit an existing `.lltheme` or L-Connect ZIP package.
- Validate selected packages before submission.
- Prompt for theme name, author, contact, and description.
- Upload through multipart form data to the gallery submission endpoint.
- Receive a submission ID after successful upload.
- Cloudflare Worker support for submissions, review, approved community themes, download stats, and votes.

### Backup, Recovery, And Diagnostics

- Manual template backup.
- Manual restore from the latest template backup.
- Automatic recovery snapshots for unsaved editor state.
- Recovery prompt/card in About when unsaved work is found.
- Restore or discard recovery snapshots.
- Daily editor logs in the local app data folder.
- Global unhandled exception logging.
- Copy diagnostic info.
- Create diagnostic ZIP packages with summary, logs, and selected files.
- GitHub issue and feature-request shortcuts.
- Check GitHub releases for updates from the About screen.

### Settings

- Choose UI language.
- Choose dark/light theme.
- Toggle layer grouping.
- Choose owned devices.
- Toggle automatic gallery theme activation after install.
- Placeholder/help entry for mapping unused L-Connect sensors through future Python/themeengine work.
- Settings are persisted locally.

### Localization

The UI currently includes:

- English
- Turkish
- Russian
- Simplified Chinese

Locale files use the same key structure as `en.json` and matching `{0}`-style placeholders.

## Basic Usage

1. Open L-Connect 3.
2. Select the LCD device/template you want to edit.
3. Start `LianLiThemeEditor.exe`.
4. Use `Active Theme` or choose a device/template manually.
5. Click `Load`.
6. Select a layer from the list or preview.
7. Edit properties in the right panel.
8. Use `Apply` for the selected layer or `Apply All` for pending changes.
9. Export a theme package or send changes back to L-Connect.

## Safer Editing Tips

- Keep L-Connect installed and open when using active-template workflows.
- Run as Administrator if Windows blocks writes to `C:\ProgramData`.
- Create a manual backup before large experiments.
- Use export/import for VM 9.2 paths when direct apply is unavailable.
- If L-Connect changes the template while editing, reload before applying more changes.
- Keep `LianLiThemeEditor.TemplateWorker.exe`, `LianLiThemeEditor.TemplateWorker.exe.config`, and `lang/` beside the editor executable.

## Repository Structure

```text
Assets/                    UI and device images
Models/                    Layer, gallery, group, template, and validation models
Services/                  Supporter bridge, gallery, validation, recovery, diagnostics, install services
SupporterCs/               C# helper used for low-level L-Connect template/profile operations
cloudflare/gallery-stats/  Worker, D1 schema, and R2-backed gallery stats/submission service
lang/                      UI translations
App.xaml(.cs)              WPF application entry
MainWindow.xaml(.cs)       Main editor UI and workflow
ColorPickerDialog.*        Color picker UI
create_release.ps1         Release ZIP builder
```

Generated folders such as `bin/`, `obj/`, `tmp/`, `artifacts/`, `Backups/`, and release ZIPs are local/build artifacts.

## Build

Build the editor:

```powershell
dotnet build
```

Create release ZIPs:

```powershell
.\create_release.ps1
```

The release script builds the editor and supporter, then creates versioned self-contained and framework-dependent ZIPs without bundling gallery assets.

## Cloudflare Gallery Service

The `cloudflare/gallery-stats` folder contains the optional Worker used by the gallery for:

- Theme download counts
- Ratings and per-user vote lookup
- Community theme listing
- Theme submissions
- Admin review actions
- Approved/pending submission file serving through R2

Theme packages and previews can live outside release ZIPs while the desktop app still reads gallery metadata and live stats.

## License

This project is licensed under the PolyForm Noncommercial License 1.0.0.

You may use, copy, modify, and distribute it for noncommercial purposes. Commercial use, resale, paid distribution, or use in a commercial product or service requires a separate written commercial license from the copyright holder.

## Changelog

### V 1.4

- Fixed 8.8" Universal Screen background export/import compatibility. L-Connect packages now reference the raw `.h264` background, with `.h264` encoded as 480x1920 Constrained Baseline and `.mp4` kept as the 1920x480 preview companion.
- Updated GitHub gallery 8.8" theme packages so downloaded gallery themes apply their backgrounds correctly through L-Connect.
- Added safer 8.8" background media normalization for both editor exports and in-editor background changes.
- Added Offline Mode so themes can be edited against a local working copy without repeatedly talking to the device.
- Improved Apply and Apply All performance by reducing unnecessary L-Connect refresh/probing work and batching layer writes more efficiently.
- Fixed cross-thread Apply All failures seen during L-Connect refresh operations.
- Improved 8.8" text preview calibration so editor text placement is closer to the actual device output.
- Added Ctrl multi-select support in the preview and added horizontal/vertical value matching alignment actions.
- Reworked the right-side layer editor expanders, including separate Data and Text/Format sections, batch bold/size editing, and stable open/closed state behavior.
- Added a Thanks tab to credit community members who helped test, translate, share themes, and improve the editor.
- Added a log-delta L-Connect background tracing tool used to diagnose import/apply/background behavior without collecting huge video/template folders.

### V 1.3 Beta

- Added detailed HTTP status, reason, response body, and network-error tracing for 8.8-inch L-Connect apply requests.
- Kept the legacy 11021 L-Connect request path as the first apply attempt, then added service-port probing and official-compatible empty-body fallback requests when the legacy path returns no useful response.
- Separated device-confirmed activation from unconfirmed local profile fallback.
- Removed the external `System.ServiceProcess.ServiceController` dependency from the restart workflow.
- Fixed gallery template restoration when an L-Connect import is not immediately visible.
- Fixed a startup deadlock introduced by synchronous L-Connect service discovery.
- Cached the working L-Connect port and request mode so Apply All no longer probes every candidate port for every request.
- Added parallel fallback probing when the cached L-Connect endpoint is no longer available.
- Renamed the generic `supporter.exe` helper to `LianLiThemeEditor.TemplateWorker.exe` and added complete product/version metadata.
- Removed direct `BinaryFormatter` usage from the template worker and delegated template serialization to the installed L-Connect ThemeEngine.
- Disabled verbose template-worker argument logging by default; it can be enabled with `LIANLI_THEME_SUPPORTER_TRACE=1` for diagnostics.
- Removed the unused `System.Management` dependency and DLL from the release package.

### V 1.2 Beta

- Added 8.8" Universal Screen and VM 9.2 workflows.
- Added Theme Gallery with local/community items, downloads, ratings, filters, sorting, install/reinstall, and optional activation.
- Added community theme submission flow.
- Added package validation for `.lltheme` and L-Connect ZIP imports.
- Added safer activation logic for already-installed template identities.
- Added VM 9.2 conversion for compatible 8.8" L-Connect ZIP packages.
- Added layer grouping, group actions, batch edit, alignment/distribution, preview context menu, and solo mode.
- Added undo/redo history.
- Added automatic recovery snapshots.
- Added diagnostic info/package generation.
- Added manual backup/restore controls.
- Added localization quality checks.
- Updated release packaging to include app files, supporter, `lang/`, and `README.md` while relying on GitHub-hosted gallery assets.

### V 1.0 Beta

- Migrated the original workflow into a C# WPF application.
- Added the main glass-style editor layout.
- Added live preview and direct canvas movement.
- Added text, data, image, graph, background, and shadow layer workflows.
- Added L-Connect-compatible export generation.
- Added graph/data-source support for practical HydroShift II themes.
- Added dark/light themes and multilingual UI.

## Disclaimer

Use at your own risk. L-Connect controls LCD screens, fans, and pumps through its own services. Avoid unnecessary service restarts while editing and always keep backups of themes you care about.
