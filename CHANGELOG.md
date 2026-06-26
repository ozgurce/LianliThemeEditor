# Changelog

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
- Added a log-delta L-Connect background tracing tool used to diagnose import/apply/background behavior without collecting huge video/template folders.

## V 1.3 Beta

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
