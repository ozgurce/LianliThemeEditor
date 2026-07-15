# Changelog

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
