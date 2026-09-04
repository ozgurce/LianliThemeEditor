# Lian Li LCD Template Editor 2.9.1

## Fixes

- Fixed an issue where adding a new layer could bring back layers that had already been deleted, moved, or duplicated but not yet applied.
- Fixed an editor loading-state bug that could cause template selections to be ignored until the app was restarted.
- Fixed 8.8-inch to VM 9.2-inch L-Connect ZIP conversion for animated/video backgrounds. VM 9.2 runtime video is now exported as a 480x1920 raw H.264 stream with the 464px content centered, avoiding corrupted striped output on device.
- Fixed copied Curved Bar previews changing shape after being moved by keeping the editor fallback drawing aligned with the layer's real Diameter instead of treating the padded selection bounds as the graph diameter.
- Preserved template orientation metadata through the loader for grouped OLED curve templates.
- Improved background media references so animation layers point to the resolved background file more consistently.
