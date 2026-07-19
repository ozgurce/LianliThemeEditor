# Changelog

## V 2.5.9

- Fixed active theme background recovery after backgrounds are changed or reverted in L-Connect outside the editor.
- Improved loaded background resolution by falling back through `GraphAnimation` media paths, template ids, stable aliases, and L-Connect media folders when profile paths are stale or missing.
- Fixed stale L-Connect service log entries overriding newer profile background state after a background revert.
- Improved background video preview reliability in the editor by retrying failed playback with a temporary preview-friendly MP4 before falling back to a still frame.
- Updated editor preview FFmpeg lookup to use the detected L-Connect install path instead of assuming the default Program Files location.
- Fixed theme selector loading state after importing a theme so later selections load normally.
- Kept Gallery installs on the Gallery tab instead of switching back to the editor after reinstall/download.
- Fixed pending community gallery entries overriding approved GitHub gallery themes with the same id/name.
- Preserved original L-Connect ZIP gallery submissions that already include `.template` and `.h264` files, avoiding unnecessary background reprocessing and layer/font id rewrites.
- Fixed Universal 8.8" portrait gallery packages whose MP4 preview companions were incorrectly encoded as landscape while the H.264 runtime background was already portrait.
- Corrected portrait metadata for Universal 8.8" gallery themes using the preview image as the source of truth, including `Doom`, `Doom the Dark Ages V2 Vertical`, `GrayMetal`, and older vertical `.lltheme` packages.

## V 2.5.8

- Added OLED Curve D3 text layer creation support with theme-specific 3D text preset selection.
- Added editing support for existing OLED Curve D3 text layers, including preset selection for the cached 3D glyphs used by the active template.
- Improved OLED Curve D3 cache discovery so the editor can find matching `.cache` files from the active template folder, ProgramData template folder, and L-Connect asset template folder.
- Hid the normal size control for OLED Curve D3 text layers because ThemeEngine renders those glyphs from fixed cached bitmap assets instead of normal font size values.
- Investigated normal text rotation support and confirmed ThemeEngine `GraphItem` text layers do not expose a native rotation property accepted by L-Connect; true rotated text would require converting text to an image-based layer.
- Fixed Universal 8.8" `GraphImage` inspection so image layers with empty ThemeEngine `width`/`height` fields now inherit their real PNG dimensions from the resolved media file.
- Added template-load diagnostics that log total layer count, image layer count, and background layer count to make missing 8.8" image-layer cases easier to verify.

## V 2.5.7

- Fixed active theme font loading on startup.
- Fixed embedded PNG/background detection for templates that store images inside the `.template` file instead of as separate PNG files.
- Fixed `themePic` export/preview generation so off-canvas/wide layers no longer collapse the preview into a vertical stripe.
- Fixed background detection so `themePic` is no longer treated as the real background when a template has separate background media.
- Fixed editor text preview data rendering to match L-Connect more closely: dynamic data layers now render only the value, while `%`, `GB`, `RPM`, `°C`, and similar unit labels remain separate static text layers.
- Fixed text alignment preview behavior so left, center, and right aligned text positions are closer to L-Connect/ThemeEngine output.
- Added release notes display to the update dialog with a scrollable notes area.
- Added export buttons for image/background layer media.
- Added Change Orientation flow for saving an orientation-converted copy of a theme and applying it without making normal Apply pay the conversion cost.
- Fixed installer/update behavior for non-default installation folders. The MSI now detects an existing install location from registry and app-driven updates pass the current install directory to `msiexec`, so updates no longer default back to the standard install path.
- Added proper major-upgrade handling to the WiX MSI so newer versions replace existing installs instead of behaving like a separate default-location install.
- Improved L-Connect 3 path detection for custom installs. The editor, integrated phone control, and template worker now resolve L-Connect from `LIANLI_LCONNECT_DIR`, registry uninstall entries, service image paths, then the default Program Files fallback.
- Removed remaining runtime hardcoded L-Connect asset/font/app paths from the main editor flow and passed the resolved L-Connect directory into the TemplateWorker.

## V 2.5.6

- Added alpha testing support for the HydroShift II OLED Curve 8.2" device.
- Added OLED Curve device selection, device icon, gallery/local filters, owned-device settings, and an editor beta warning to remind testers to back up before editing or applying.
- Added OLED Curve template discovery for `hydroshift-ii-oled-curve` ProgramData/asset folders and grouped handling for dual/triple split factory templates.
- Added OLED Curve preview mode controls for full, dual split, and triple split layouts, with split guide rendering and clipped preview slots to prevent split templates from bleeding into each other.
- Temporarily hid OLED Curve dual/triple split template parts from the normal template list while keeping grouped apply logic available internally.
- Added OLED Curve apply command handling that sends grouped split templates with the correct OLED Curve screen mode hints.
- Added OLED Curve nested theme/layer index support (`theme:x:y`) so layers inside split/3D templates can be edited without corrupting normal layer numbering.
- Fixed OLED Curve multi-layer delete and stale `LayerIndex` handling so repeated delete/apply operations skip synthetic/non-persisted layers instead of failing on shifted indices.
- Improved OLED Curve background detection to prefer real `videoPath`/media files over `themePic`, including fallback lookup by template id in the OLED Curve video folder.
- Added `.mov` media support for OLED Curve official templates, package validation, media selection, preview path resolution, and export/import compatibility.
- Added 2288x1080 OLED Curve full-screen sizing support for background preparation and preview scaling.
- Added OLED Curve D3 cache text preview support by reading ThemeEngine `.cache` glyph dictionaries through the template worker and drawing cached 3D text/number glyphs in the editor preview.
- Added template-worker inspection fields for `RenderMode` and `ThemeMode`, allowing OLED Curve D3/static/dynamic layers to be detected instead of being treated as plain text.
- Improved OLED Curve text preview anchoring and bounds calculations so cached D3 text/data layers line up closer to L-Connect's ThemeEngine output without runtime render passes.
- Improved OLED Curve status/progress graph preview handling, including curved/status bar layer metadata and split/gap controls.
- Improved OLED Curve export/package validation paths for new media extensions and OLED Curve device ids.
- Kept the OLED Curve changes scoped to the OLED Curve device path so existing HydroShift II LCD S/C, Universal 8.8, and VM 9.2 behavior is not intentionally changed.

## V 2.5.2

- Fixed HydroShift II LCD-S/C active theme background detection when L-Connect keeps a stale custom background path in the profile.
- Fixed active HydroShift themes loading old profile background media instead of the currently applied `ApplyTemplate videoPath`.
- Fixed `GraphAnimation` layers reporting empty `MediaPath` during template inspection.
- Added background lookup for HydroShift `temp` folders and `uploaded/<device>/template-background` runtime folders.
- Improved VM 9.2 support with real `1920x464` and `464x1920` canvas handling.
- Reworked wide-screen canvas logic so Universal 8.8 and VM 9.2 no longer share the same hardcoded `1920x480` assumptions.
- Added VM 9.2 runtime H.264 sizing/cropping logic, including `464x1920` portrait handling.
- Enabled VM 9.2 direct apply/profile handling paths and added VM 9.2 L-Connect log tags for active template/background detection.
- Fixed gallery/local filters so VM 9.2 themes stay classified as `vm-9.2-inch` instead of being folded into Universal 8.8.
- Updated VM 9.2 aspect labels to show `1920 x 464` / `464 x 1920`.
- Aligned data-source format support with decompiled L-Connect behavior, keeping format selection focused on sources L-Connect actually reads via `SubName`.
- Improved gallery ordering and community-gallery asset URL filtering.
- Added approved gallery themes after 2.5.1: `Crystals_182127`, `Casio-Hydroshift-C`, `Casio-Hydroshift-S`, and `BlueTech_Animated`.

## V 1.5

- Fixed 8.8" Universal Screen gallery/import/apply targeting by using the actual L-Connect template endpoint (`vid_1cbe&pid_a088`) and excluding the wireless transmitter endpoint (`vid_0416&pid_8040`) from template operations.
- Removed the temporary Gallery install diagnostic result popup and automatic gallery diagnostic ZIP generation. Gallery install/apply results now stay in the card progress/status area and the editor status bar.
- Made single-layer Apply wait for the L-Connect send/refresh result instead of firing it in the background, so editor-side Apply now reports the device-send state like Apply All.

## V 1.4

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

## V 1.3 Beta

- Kept the legacy 11021 L-Connect request path as the first apply attempt, then added service-port probing and official-compatible empty-body fallback requests when the legacy path returns no useful response.
- Separated device-confirmed activation from unconfirmed local profile fallback.
- Removed the external `System.ServiceProcess.ServiceController` dependency from the restart workflow.
- Fixed gallery template restoration when an L-Connect import is not immediately visible.
- Fixed a startup deadlock introduced by synchronous L-Connect service discovery.
- Cached the working L-Connect port and request mode so Apply All no longer probes every candidate port for every request.
- Added parallel fallback probing when the cached L-Connect endpoint is no longer available.
- Renamed the generic `supporter.exe` helper to `LianLiThemeEditor.TemplateWorker.exe` and added complete product/version metadata.
- Removed direct `BinaryFormatter` usage from the template worker and delegated template serialization to the installed L-Connect ThemeEngine.
- Removed the unused `System.Management` dependency and DLL from the release package.
