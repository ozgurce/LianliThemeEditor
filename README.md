# Lian Li LCD Theme Editor

An unofficial Windows theme editor for Lian Li L-Connect 3 LCD templates.

The editor is built for Hydroshift II LCD devices and lets you inspect, edit, add, reorder, preview, and apply LCD template layers without doing every change manually inside L-Connect.

> This project is not affiliated with, endorsed by, or supported by Lian Li. It modifies local L-Connect 3 template/profile files, so keep backups of important themes.

## Screenshot

<img width="2546" height="1370" alt="image" src="https://github.com/user-attachments/assets/3fd774bc-e45a-44eb-822d-a93642ade68a" />


## Features

- Edit existing L-Connect 3 LCD template layers.
- Supports Hydroshift II LCD-S and Hydroshift II LCD-C.
- Automatic device detection fallback when the active template belongs to the other device family.
- Square preview for LCD-S and circular preview mask for LCD-C.
- Live preview for text, data, image, graph, GIF, and MP4 layers.
- Background GIF/MP4 upload and apply workflow.
- Layer list with editable index, type, data source, text, media, position, size, font, bold, color, and format fields.
- Add static text layers.
- Add live data layers such as CPU/GPU temperature, load, clocks, fan/pump data, time, date, and day fields.
- Add image layers.
- Add graph layers from available L-Connect modular graph styles.
- Edit graph style, position, size, colors, and data source where supported.
- Move layers up/down to control draw order.
- Add and track shadow layers.
- Sync shadow movement/color from the source layer.
- Transparent color support through manual ARGB/hex values.
- Date/time format controls, including formats such as `Y-M-D`, `D-M-Y`, `D.M.Y`, `00:00`, and `00:00:00`.
- Multi-language UI: English, Turkish, Russian, and Simplified Chinese.
- Dark/light UI theme selection.
- `Apply All` workflow for saving template changes and asking L-Connect to refresh without restarting the fan-control service.
- Optional EXE build support through `ps2exe`.

## Supported Devices

| Device | Status | Notes |
| --- | --- | --- |
| Hydroshift II LCD-S | Supported | Square LCD preview. |
| Hydroshift II LCD-C | Supported | Circular preview mask. Uses C-specific template/modular assets. |

The editor can seed missing ProgramData template/modular/theme/preview files from the installed L-Connect `Assets` directory when possible.

## Requirements

- Windows 10 or Windows 11.
- L-Connect 3 installed.
- PowerShell 5.1 or newer.
- .NET/WPF support available through Windows PowerShell.
- Administrator rights are recommended when writing to `C:\ProgramData\Lian-Li\L-Connect 3`.
- `ffmpeg` is recommended for reliable background video conversion/preview workflows.
- Optional: `ps2exe` if you want to build standalone EXE files.

When running from `EXE/`, the editor is designed to look for resources in the parent folder when needed.

## Running From PowerShell

Open PowerShell and run:

```powershell
powershell -ExecutionPolicy Bypass -File editor.ps1
```

If you installed the project somewhere else, change the path accordingly.

## Building EXE Files

Install/import `ps2exe`, then run these commands from the `ThemeEditor` folder.


## Basic Usage

1. Open L-Connect 3 and select the LCD device.
2. Select a template in L-Connect or leave the currently active template selected.
3. Run Theme Editor.
4. Choose the device type:
   - `Hydroshift II LCD-S`
   - `Hydroshift II LCD-C`
5. Keep `Use active template` enabled, or enter a template ID manually.
6. Click `Load`.
7. Select a layer from the layer list or from the preview.
8. Edit position, font, data source, text, size, color, format, or graph options.
9. Click `Apply` for the selected layer.
10. Use `Apply All` to write changes and trigger L-Connect to refresh.

## Layer Types

Common layer types include:

- `GraphAnimation`: background video/GIF/image animation layer.
- `GraphItem`: text or data text layer.
- `GraphImage`: image layer.
- `GraphStatuBar`: linear/progress-style bar.
- `GraphArchBar`: circular/arc-style graph.
- `GraphLine`: stream/line graph.
- `GraphDynamicBar`: dynamic segmented/bar graph.

Not every property exists on every L-Connect graph object. The editor shows and applies controls based on what the layer supports.

## Data Sources

The editor only keeps practical data sources that L-Connect templates can actually use or display. Common examples:

- `CPUTEMP`
- `CPUCLOCK`
- `CPULOAD`
- `CPUFAN`
- `GPUTEMP`
- `GPUCLOCK`
- `GPULOAD`
- `RAMLOAD`
- `DRVLOAD`
- `WATERPUMP`
- `TIME`
- `DATE`
- `DAY`
- `APM`
- `StaticText`

Some values depend on the hardware, L-Connect version, and available sensor data.

## Date And Time Formats

Time examples:

```text
00:00
00:00:00
```

Date examples:

```text
Y-M-D
D-M-Y
D.M.Y
M
D
```

Date and time layers should stay dynamic. They are not intended to be saved as fixed static text.

## Background Media

The editor supports GIF/MP4 background selection. When a background is applied, the helper attempts to mirror the way L-Connect stores uploaded background media.

Useful paths:

```text
C:\ProgramData\Lian-Li\L-Connect 3\uploaded
C:\ProgramData\Lian-Li\L-Connect 3\hydroshift-ii-lcd-s
C:\ProgramData\Lian-Li\L-Connect 3\hydroshift-ii-lcd-c
```

The editor avoids restarting the L-Connect service for normal apply operations because that service can also control fan behavior.

## Language Support

Language files are stored in:

```text
lang/en.json
lang/tr.json
lang/ru.json
lang/zh.json
```

If a UI string is missing or appears hardcoded, add it to all language JSON files and wire it through the localization helper in `editor.ps1`.

## Settings

Local editor settings are stored in:

```text
theme_editor_settings.json
```

This can include:

- selected language
- selected UI theme
- selected device model
- shadow layer links

Do not ship personal machine-specific settings if you are publishing a clean release.

## Troubleshooting

### Background applies but preview shows the wrong media

Check whether the template has a custom background in the L-Connect profile. The helper now filters custom background paths by selected device model, because some template IDs exist in both LCD-S and LCD-C families.

### Access denied

Run PowerShell or the EXE as Administrator. L-Connect stores templates under `C:\ProgramData`, which may require elevated permissions.

## Development Notes

- `editor.ps1` contains the WPF UI and user workflow.
- `supporter.ps1` performs low-level template/profile operations.
- The editor prefers `supporter.exe` if present; otherwise it falls back to `supporter.ps1`.
- Device-specific assets are resolved from both `ProgramData` and L-Connect `Assets`.
- LCD-C uses the same template object model for default templates, but its preview should be treated as circular.
- Some L-Connect custom theme/profile data is stored separately from default template `GraphList` layers.

## Disclaimer

Use at your own risk. Always keep backups before editing L-Connect templates. Fan and pump control are handled by L-Connect services, so avoid unnecessary service restarts while testing LCD theme changes.

