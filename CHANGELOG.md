# Changelog

## V 2.8.1

- Fixed 8.8" Universal Screen ring graph previews that could appear oversized in the editor preview on first load.
- Fixed ring graph previews shrinking while dragging or editing and then growing again after the async ThemeEngine preview refresh completed.
- Kept GraphSensor preview media stable during edits so the preview does not jump between the local fallback drawing and rendered bitmap paths.
- Fixed toolbar tooltips for export, import, orientation, and Apply All buttons so icon-only buttons no longer show WPF control type names such as `System.Windows.Shapes.Rectangle`.

## V 2.7.1

- Fixed Universal 8.8" L-Connect ZIP imports that could incorrectly mark horizontal themes as portrait when the package contained only a rotated runtime `.h264` background stream such as `480x1920`.
- Stopped using background, template, package, or theme file names as orientation evidence during Universal 8.8" import/conversion paths, avoiding fragile `portrait`/`landscape` inference from names.
- Added stronger Universal 8.8" orientation detection from the imported `.template` content itself, using embedded preview/canvas dimensions such as `1920x480` before falling back to other reliable metadata.
- Kept raw `.h264` dimensions from forcing portrait detection, because L-Connect can store horizontal Universal 8.8" runtime backgrounds in a physically vertical H.264 layout.
- Verified the `Static_Theme_Metallic-ThemeEditor.zip` sample imports as a horizontal theme source by reading its embedded template preview dimensions as `1920x480`.
- Confirmed the sample `.h264` can still be converted into a horizontal editor preview MP4 at `1920x480`.

## V 2.7.0

- Fixed delayed multi-layer delete flows so queued deletes keep a stable editor layer identity and original template source index, preventing shifted UI layer numbers from deleting the wrong layer after earlier deletes.
- Added batched template-layer deletion in the template worker, so multiple queued deletes are applied against one loaded template in descending source order instead of repeatedly rewriting the file with shifting indexes.
- Improved delete and move verification by matching layers with richer signatures and logging queued/delete target snapshots when the layer list changes unexpectedly.
- Fixed local layer source-index tracking after deletes, moves, and duplicates so later Apply All operations continue targeting the intended persisted layer.
- Fixed imported Universal 8.8" themes whose background appeared in the editor only after manually reloading a background.
- Improved Universal 8.8" import normalization: MP4 backgrounds are inspected for orientation, converted to the runtime H.264 format expected by L-Connect, and referenced back from the template while keeping MP4 preview companions.
- Regenerated broken import `themePic` previews when needed so packaged/imported themes open with a valid preview and background reference.
- Reduced unnecessary template rewrites by keeping structural changes queued locally until Apply All where possible, while forcing a sync only before operations that depend on the persisted layer order.

## V 2.6.5

- Fixed HydroShift II LCD-S/C and Universal 8.8" PNG layer previews that used the wrong embedded bitmap or ignored source rectangles, causing some images to appear at the wrong position or size in the editor.
- Fixed PNG/image zoom handling so `zoom_rate` and `ZoomRate` are both read and written, and editor previews account for images whose embedded bitmap already has zoom baked into it.
- Fixed Universal 8.8" image-layer zoom edits that changed the layer data but did not visibly update the selected PNG in the preview.
- Fixed active-theme startup background loading so the editor no longer shows a stale or unrelated profile background before switching themes.
- Fixed gauge needle previews after the PNG bitmap-order fix by keeping `GraphClock`/gauge media on the original bitmap-first path while retaining rendered bitmap preference for `GraphImage` layers.
- Improved export and apply H.264 encoding to keep L-Connect-compatible `Constrained Baseline` H.264 while enabling full-range color output (`color_range=pc`) for darker blacks and more accurate colors.
- Updated Universal 8.8" H.264 generation to preserve the runtime orientation expected by L-Connect, including the rotated `480x1920` H.264 stream used by many landscape 8.8" themes.
- Added stronger H.264 export/apply consistency checks around profile, level, B-frames, references, pixel format, color range, and device-specific runtime dimensions.
- Regenerated MP4-backed GitHub Gallery H.264 companions with full-range color where safe, while preserving existing H.264 resolution and visual orientation instead of deriving them only from manifest orientation.
- Fixed Gallery H.264 handling for landscape Universal 8.8" packages whose MP4 previews are `1920x480` but whose accepted runtime H.264 backgrounds are physically stored as `480x1920`.
- Added missing H.264 companions for Gallery packages that previously contained MP4 backgrounds without a matching runtime H.264 file.

## V 2.6.2

- Fixed localized update and download progress messages so version/progress placeholders are replaced with actual values instead of appearing on screen.
- Fixed delayed layer delete/apply sequencing so remaining layer source indexes are rebased after deletion, preventing Apply All from updating or removing the wrong layer.
- Improved active theme reload/background recovery when L-Connect changes or reverts the background outside the editor, including fallback through `GraphAnimation` media paths, template ids, stable aliases, temp folders, and uploaded background folders.
- Fixed stale L-Connect service log entries overriding newer profile background state after a background revert.
- Improved background video preview reliability by retrying failed playback with a temporary preview-friendly MP4 before falling back to a still frame.
- Updated editor preview FFmpeg lookup to use the detected L-Connect install path instead of assuming the default Program Files location.
- Fixed theme selector loading state after importing a theme so later selections load normally.
- Kept Gallery installs on the Gallery tab instead of switching back to the editor after reinstall/download.
- Fixed pending community gallery entries overriding approved GitHub gallery themes with the same id/name.
- Preserved original L-Connect ZIP gallery submissions that already include `.template` and `.h264` files, avoiding unnecessary background reprocessing and layer/font id rewrites.
- Improved Gallery submission packaging by embedding normalized previews, inferring Universal 8.8" orientation from preview/background media, and keeping package metadata aligned with the submitted assets.
- Fixed Universal 8.8" portrait gallery packages whose MP4 preview companions were incorrectly encoded as landscape while the H.264 runtime background was already portrait.
- Corrected portrait metadata for Universal 8.8" gallery themes using the preview image as the source of truth, including `Doom`, `Doom the Dark Ages V2 Vertical`, `GrayMetal`, and older vertical `.lltheme` packages.
- Added Turzx `.turtheme` export/normalization support, including `themePic`, `videoPath`/`o_videoPath`, animation graph handling, sensor/data binding normalization, and font metadata repair.
- Added Turzx inspection and debug tooling for theme trees, bitmap fields, video references, and layer serialization checks.

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
