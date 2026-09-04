# Release Notes Draft

## Fixes

- Fixed a bug where adding a new layer could bring back layers that had already been deleted, moved, or duplicated but not yet applied.
- Fixed an editor loading-state issue that could cause template clicks to be ignored until the app was restarted.
- Fixed 8.8-inch to VM 9.2-inch L-Connect ZIP conversion for animated/video backgrounds. Converted VM 9.2 runtime video is now written as a 480x1920 raw H.264 stream with the 464px content centered, avoiding the corrupted striped output seen on device.
- Preserved template orientation metadata through the loader for grouped OLED curve templates.

## Technical Notes

- Replaced fragile `_isLoading` save/restore blocks with a nested loading-depth guard so overlapping async UI operations cannot leave the editor stuck in a loading state.
- Flushes pending structural layer changes before add-layer operations because adding a layer reloads the template from disk.
- Parses `IsLandscape` from worker JSON as a nullable boolean, including string values.
- Ignores local Beads/Dolt files generated during development.

## Verification

- Built `ThemeEditorCSharp.csproj` successfully with 0 warnings and 0 errors.
- Tested the VM 9.2 video conversion path with `ColorTransient_sensor_panel (1).mp4`; the generated raw H.264 output decodes as 480x1920 without visible corruption.
