# Lian Li LCD Theme Editor

An unofficial visual theme editor for Lian Li LCD devices and L-Connect 3 templates.

The project aims to make advanced LCD customization easier without requiring users to manually inspect or modify L-Connect template files. It provides a live visual workspace for editing existing themes, creating layers, changing media, and applying the result back to L-Connect.

> [!IMPORTANT]
> This is an unofficial community project and is not affiliated with, endorsed by, or supported by Lian Li. This is an early beta release. Back up themes you care about before editing them.

## Supported Devices

| Device | Status |
| --- | --- |
| HydroShift II LCD-S | Supported |
| HydroShift II LCD-C | Supported |
| Universal Screen 8.8" | Experimental |

Universal Screen 8.8" support includes landscape and portrait editing, but has not yet been verified on physical hardware.

## Features

- Modern C# WPF editor with dark and light themes.
- Live visual preview of the selected L-Connect template.
- Automatic active-theme loading on startup.
- Automatic fallback to the first available theme when no active theme can be found.
- Device-aware preview dimensions and circular masking for compatible displays.
- Landscape and portrait support for Universal Screen 8.8".
- Zoom, scrolling, fit-to-screen, alignment guides, dragging, and resizing.
- Cached device and theme thumbnails for faster browsing.
- Layer visibility and locking controls.
- Layer reordering, duplication, deletion, and direct selection from the preview.
- Per-layer Apply and full-theme Apply All workflows.
- Automatic local backups and manual Backup/Restore controls.
- Progress feedback while applying changes.

### Layer Types

- Animation and background media
- Static text
- Live sensor data
- Images
- Status bars and segmented status bars
- Dynamic status graphs
- Curved/donut bars
- Line and stream charts

### Editing

- Position, dimensions, rotation, zoom, and alignment
- Fonts, size, bold, italic, spacing, and text formatting
- Text colors and gradients
- Graph colors, gradients, direction, radius, thickness, and subdivision settings
- Layer shadows
- Image and media replacement
- Background image, GIF, MP4, and H.264 workflows
- Live sample values for supported sensor sources

### Languages

- English
- Turkish
- Russian
- Simplified Chinese

## Installation

1. Download or clone this repository.
2. Install the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) if it is not already installed.
3. Make sure L-Connect 3 is installed.
4. Keep the `EXE` and `lang` folders together.
5. Run `EXE/LianLiThemeEditor.exe`.
6. Administrator rights are recommended because L-Connect stores templates under `C:\ProgramData`.

The bundled `supporter.exe` performs the low-level L-Connect template operations and must remain beside the editor.

## Basic Usage

1. Install and open L-Connect 3.
2. Select the desired LCD device.
3. Start `LianLiThemeEditor.exe`.
4. The editor loads the active theme when available, otherwise it loads the first theme found for that device.
5. Select a layer from the list or directly from the preview.
6. Edit its properties and press **Apply**.
7. Use **Apply All** to apply the complete theme.
8. Use **Export Theme** to create an L-Connect-compatible package.

## Data Sources

The editor supports common L-Connect sensor and system values, including:

- CPU/GPU temperature, load, clock, fan, power, and voltage
- CPU/GPU model information
- RAM usage, total memory, and model
- GPU memory values
- Pump and water-pump data
- Drive and HDD data
- Upload and download speed
- FPS
- Time, date, and day
- Static text

Available values still depend on L-Connect, connected hardware, and the sensor providers available on the system.

## Notes

- Theme files are edited inside the normal L-Connect data folders.
- The editor creates automatic backups before template changes.
- Background video processing may take longer than normal layer edits.
- Some properties are only available on specific L-Connect layer classes.
- Device communication still relies on L-Connect's existing services.

## Troubleshooting

### Access denied

Run the editor as Administrator.

### A theme does not appear

Open the device once in L-Connect and confirm that its templates exist under:

```text
C:\ProgramData\Lian-Li\L-Connect 3
```

### A sensor value is unavailable

The value must be exposed by L-Connect or its supported sensor provider. The editor may display a sample value when live data is unavailable.

## Current Release

**Version:** 1.0 Beta

This first public beta focuses on visual editing, practical L-Connect integration, performance with complex themes, and support for the HydroShift II LCD family. Feedback, bug reports, device testing, and theme samples are welcome.

## Disclaimer

Use this software at your own risk. L-Connect controls hardware including LCD screens, fans, and pumps. Keep backups and avoid interrupting L-Connect while a theme is being written.
