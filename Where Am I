# L-Connect Vs Theme Editor

## 1. Executive Summary

The most important conclusion is that the current architecture is not yet fully data/config-driven for adding new devices. The Phone Link side already makes a serious attempt at generalization, but device discovery, model inference, lighting endpoint selection, theme package validation, 9.2-specific behavior, and some wireless fan state files still depend on hardcoded model strings and command fallback lists.

The L-Connect 9.2 flow is richer than the classic 480x480 `.template + background` flow. `VM9p2InchController` supports normal template apply, but also separate custom theme types: `ImageVideo`, `Color`, `ThemeInfo`, and `Modulars`. It stores `Applied*` state copies, has separate `LandscapeTemplateConfig` / `PortraitTemplateConfig`, copies SDK assets, and calls `LCD92Controller`. Therefore converting an existing 8.8 theme or our package format to 9.2 looks possible, but simply generating a ZIP manifest is not enough. The correct 9.2 path should generate a `ThemeEngine.Theme` or use the 9.2 custom theme APIs, then validate/apply through `ImportTemplate`, `ApplyThemeInfo`, `ApplyModulars`, or `ApplyBackgroundOnlyCustomTheme`.

The lighting system in L-Connect is not a single global effect capability table. `UniversalScreen8p8InchLightingMode`, wireless `LWirelessLightingSetting`, TL wireless, HydroShift II, SL/AL fan profiles, QuickSync mappings, and product-specific default setting tables are separate. Our Phone Link service reads some metadata from the installed L-Connect binary, which is a good direction, but paths such as `hydroshift-ii-lcd-s`, `universal-screen-8.8-inch`, `tl-wireless-fans`, `tl-wireless-fans-merge`, and `l-wireless-fans` are still embedded in code.

No production code was changed. This document is a research and recommendation report.

## 2. Sources Reviewed

Current project:

- `D:\ThemeEditor\MainWindow.xaml.cs`: main UI, layer editing, apply/export/import/gallery/Phone Link entry points.
- `D:\ThemeEditor\Models\LayerRow.cs`: editor-side layer model.
- `D:\ThemeEditor\Models\ThemePackageManifest.cs`: our package manifest model.
- `D:\ThemeEditor\Services\ThemePackageValidationService.cs`: package validation and supported device list.
- `D:\ThemeEditor\Services\ThemeInstallationService.cs`: activation candidate logic after gallery/import.
- `D:\ThemeEditor\Services\LConnectClientService.cs`: L-Connect local HTTP transport.
- `D:\ThemeEditor\IntegratedPhoneControl\LConnectControlService.cs`: Phone Link device, theme, lighting, and fan group control flow.
- `D:\ThemeEditor\IntegratedPhoneControl\LConnectEffectMetadataReader.cs`: reads effect color counts from the installed L-Connect binary.
- `D:\ThemeEditor\Services\ISupporterBridge.cs`: template/layer manipulation bridge surface.


## 3. L-Connect Architecture Overview

L-Connect 3 mainly uses three layers:

1. Desktop UI: `L-Connect_3.exe`. Lighting UI lists, QuickSync mappings, and user workflows are here.
2. Local service: `L-Connect-Service.exe`. HTTP request dispatch, device controllers, profile save/apply, and template import/apply are here.
3. SDK/native/theme engine: `lianli.ThemeEngine.dll`, `slv3.models.dll`, `lianli.lcd207.dll`, and device SDK wrappers. Actual `.template`, modular, media, and final device rendering happen here.

Our project uses these layers through two paths:

- Disk/template path: `MainWindow.xaml.cs` + `SupporterBridge` + `ThemePackageValidationService` + `ThemeInstallationService`.
- HTTP path: `LConnectClientService` and Phone Link's `LConnectControlService`.

## 4. Layer Types and Format Comparison

Our layer model is a broad but flat string-based model in `LayerRow`. Key fields include `Type`, `DataSource`, `Text`, `Media`, `X`, `Y`, `Size`, `Font`, `Bold`, `Italic`, `Color`, `Format`, `GraphStyle`, `Hide`, `Width`, `Height`, `Radius`, `Thickness`, `FrontColor`, `BackColor`, `LineColor`, `FillColor`, `BorderColor`, `Transparent`, `UseGradient`, `GradientColor`, `ZoomRate`, `Rotate`, clock angle/origin fields, chart/bar/ring fields, and sensor style fields.

L-Connect has two different concepts:

- 480x480 / ThemeEngine graph-item system: `ThemeEngine.GraphAnimation`, `GraphImage`, `GraphClock`, `GraphArchBar`, `GraphLine`, `GraphSensor`, `GraphStatuBar`, `GraphSpecs.FlexWLessTheme*`, and `ThemeEngine.UpdateSet`.
- 8.8 / Flex / 9.2 modular system: `FlexLCDModularType`, `VM9p2InchModularTypes`, `FlexLCDModularSetting`, `VM9p2InchModularSetting`, `ThemeInfo`, and `TemplateComponent`.

### Layer Comparison Table

| Layer type | L-Connect support | Our support | Missing fields | Status | Recommendation |
|---|---|---|---|---|---|
| Background image/video | Template/custom theme background, `GraphAnimation`, `SelectedImageVideoPath`, `AppliedImageVideoPath` | Present: media/background template operations | 9.2 `AppliedImageVideoPath`, orientation-specific background state | P1 | Add 9.2 custom state model |
| Image/GIF/MP4 layer | `GraphImage`/`GraphAnimation`; media path cleanup through `ThemeEngine.ClearBitmap` | Present: `Media`, `MediaPath`, bridge `AddImage`, `SetBackgroundMedia` | L-Connect SDK fit/crop/stretch fields not fully confirmed | P2 | Normalize through a real template parser |
| Text layer | `GraphItem` TypeName `Text`/`StaticText`, font config, `UpdateCustomTextSize` | Present: `Text`, `Font`, `Bold`, `Italic`, `Color`, `LineHeight` | 9.2 font enums and text placeholder mapping | P1 | Add font enum adapter |
| Sensor/data | `GraphSensor`, `GraphArchBar.AcceptDataList`, `UpdateSet.DataSource`, `ThemeDataSourcTypes.DataType` | Present: `DataSource`, graph/sensor fields | Full L-Connect data source list and FixedData placeholder mappings | P1 | Generate a data source registry from decompile |
| Clock/date/time | `GraphClock`, modular type `DateTime`, `ThemeDataSourcTypes.ClockType` | Present: clock fields, `AddClock`, `Format` | 9.2 `DateTime` modular setting color/font split | P2 | Add `DateTime` modular adapter |
| Weather | Some data/source traces exist; runtime behavior not confirmed | Language files mark Weather as not working/experimental | Real service path not verified | P4 | Keep experimental/disabled |
| Graph/bar/ring | `GraphArchBar`, `GraphLine`, `GraphStatuBar`, gradient colors | Present: `GraphStyle`, `FrontColor`, `BackColor`, `GradientColor`, etc. | `DataSource2Color*` and some `GraphSpecs.FlexWLessTheme*` fields | P2 | Add style-specific metadata table |
| Wireless LCD modular | `VM9p2InchModularTypes.WirelessLCD`, `FlexLCDModularType` | Partial/sensor-like | SupportedDataSources and wireless-specific color sets | P1 | Model as separate layer/modular type |
| CustomText modular | `CustomText` modular type | Represented as text layer | `Id`, `Key`, `ZoomRate`, font enum, color type fields | P2 | Converter from text layer to modular layer |
| FixedData modular | `FixedData`, placeholder1/2 + data source | Missing/partial | Placeholder mapping, two text fields, two color types | P1 | New layer type or modular subtype |
| Template edit component | `FlexLCDThemeComponent`, template edit buffer | Missing | Component key, supported data sources, revert/flush/apply buffer | P2 | Flex/9.2-specific edit adapter |

## 5. Lighting Effects Comparison

Lighting in L-Connect is product-specific:

- `UniversalScreen8p8InchLightingMode`: `Rainbow`, `Wave`, `StaticColor`, `Breathing`, `RainbowMorph`, `Paint`, `Runway`, `Tide`, `BlowUp`, `Meteor`, `Snooker`, `Mixing`, `PingPong`, `BulletStack`, `Twinkle`, `River`, `Hourglass`, `ElectricCurrent`, `RainbowWave`.
- `LWirelessLightingSetting`: shared fields are `Color[] Colors`, nullable `Speed`, nullable `Brightness`, nullable `Direction`.
- `LConnect3cs.Products.LWireless.*SubProfile.DefaultLightingSettings`: wireless HydroShift/TL default effect tables.
- `LConnect3cs.Products.LWireless.LWirelessProfile.DefaultMergeLightingSettings`: merge lighting tables.
- `LConnect3cs.Products.Ene6K77Fan.*FanProfile`: effect dictionaries for AL/SL/Infinity/V2 fan families.
- `LConnect3cs.Views.Pages.QuickSyncLighting.MainPage`: QuickSync product-specific enum mappings.

Our Phone Link side has normalized effect IDs and uses two metadata sources:

- Static fallback: `LConnectControlService.LConnectEffects`.
- Runtime metadata: `LConnectEffectMetadataReader`, which reads `DefaultLightingSettings` color array lengths from the L-Connect binary.

### Effect Comparison Table

| Effect | Internal ID | Supported devices | Color count | Parameters | Our status | Missing |
|---|---:|---|---:|---|---|---|
| Rainbow | Usually enum 0, product-specific | 8.8, fan, pump, HydroShift, QuickSync | 0/automatic | speed, brightness, direction on some products | Present | `colorCount=0` auto behavior is not modeled clearly |
| Wave | 8.8 enum, some wireless/fan profiles | 8.8, fan families | 1-4 depending on product | speed, brightness, direction | Present/partial | Color count is not fully capability-driven |
| StaticColor | 8.8 enum, fan/pump enums | General | 1, TL fan static can be fan count 1-4 | brightness, colors | Present | TL static fanCount behavior is hardcoded |
| Breathing | product enums | General | usually 1 | speed, brightness, color | Present | Product enum ID mappings are not fully externalized |
| RainbowMorph | product enums | General | 0/automatic | speed, brightness | Present | Some products use null colorCount |
| Runway | product enums | General/TL/Hydro | 2 or product-table value | speed, brightness, direction | Present | Direction enum conversion is not device-specific |
| Meteor | product enums | General | 1-4 | speed, brightness, direction | Present | Default color fallback is fixed |
| Twinkle | 8.8/wireless/fan | Partial | 0/1/4 | speed, brightness | Present/partial | Device support matrix is incomplete |
| ElectricCurrent | 8.8/fan | Partial | 4 in some profiles | speed, brightness | Present/partial | Multi-color UI validation is weak |
| Paint/Tide/BlowUp/Snooker/Mixing/PingPong/BulletStack/River/Hourglass/RainbowWave | 8.8 enum | Mostly 8.8 screen LED | variable | speed/brightness/colors | Partial | Phone Link list/apply body is not fully derived from L-Connect enum |
| QuickSync-specific effects | `QuickSyncLightingEffectMode` mappings | MB sync/fan/pump/screen | variable | product-specific | Missing/partial | QuickSync is not modeled as a separate capability domain |
| Strimer/Separate Strimer | `LConnectCore.Products.StrimerPlus.*` | Strimer Plus | special | speed/brightness/direction/scope | Missing | No Strimer adapter |

Confidence: Exact for the Universal 8.8 enum and LWireless base fields; high probability for per-product color counts because some come from static profile tables and some from SDK behavior.

## 6. Device Capability Comparison

### Device Capability Table

| Device/model | Screen type | Lighting support | Effects | Color model | Special cases | Our support |
|---|---|---|---|---|---|---|
| `hydroshift-ii-lcd-s` | LCD + wireless LED | Wireless merge/HydroShift path | `DefaultLightingSettings` + merge | Color count read from L-Connect binary | Special `SetLCDBrightness`, `WirelessMergeTarget.HydroShift` | Present but hardcoded |
| `hydroshift-ii-lcd-c` | LCD | Similar LCD, different template/device | Not fully confirmed | Partial | Supported in theme validation, no Phone Link LED special path | Partial |
| `universal-screen-8.8-inch` | 1920x480 / 480x1920-like | Screen LED | `UniversalScreen8p8InchLightingMode` | Attempts service color-count read | `SetLightingEffectSetting`, `ApplyScreenContent` | Present but 8.8-specific |
| `vm-9.2-inch` | 1920x464 / 464x1920 | 9.2 screen/custom theme | 9.2 controller-focused; lighting enum not fully verified | Custom theme + template state | Direct apply is warned/missing; L-Connect import is recommended | Partial/missing |
| TL wireless fan | Fan group | Individual + merge | TLV2 subprofile/order | Static can depend on fan count | `FanLightingSetting`, `FanMergeLightingSetting`, saved config sync | Present but heavily hardcoded |
| SL/AL/Infinity fan families | Fan | Product profile lighting | Large `LightingMode` set | 1/2/3/4 colors | `Ene6K77FanProfile` variants | Not general in Phone Link |
| Strimer Plus | Strimer | Separate lighting config | `StrimerPlus.LightingMode` | scope/separate config | separate controller | Missing |
| Lancool207Digital | LCD/digital | template/custom theme | image/video/modular/theme | own request types | raw asset theme/modular files exist | Missing/partial |
| Flex LCD / TL Flex / SLINF Flex | Multi/virtual screen | template + group/vGroup + lighting | FlexLCDThemeEngine | modular/component | group, vGroup, startup screen | Missing/partial |

The current system cannot add a new device by adding only a device definition and capability data. At minimum, these areas need code changes:

- `ThemePackageValidationService.SupportedDevices`
- UI device lists and asset paths (`MainWindow.xaml.cs`, `Assets\Devices`, blank seeds)
- `LConnectControlService.InferDeviceModel`, `BuildDeviceName`, `EffectsForTarget`, `ColorCountForTarget`, apply command selection
- Gallery model/orientation filters
- SupporterBridge device model branches
- Template dimensions/canvas rules

## 7. L-Connect 9.2 Theme Format

Confirmed findings:

- `VM9p2InchConstants` defines screen dimensions as `ScreenLandscapeWidth=1920`, `ScreenLandscapeHeight=464`, `ScreenPortraitWidth=464`, `ScreenPortraitHeight=1920`.
- `VM9p2InchTemplateConfig` fields: `IsCustomThemeEnabled`, `SelectedTemplateId`, `CustomTheme`.
- `VM9p2InchCustomTheme` fields: `Type`, `SelectedImageVideoPath`, `SelectedThemeInfoId`, `ThemeInfos`, `Modulars`, `AppliedImageVideoPath`, `AppliedThemeInfo`, `AppliedModulars`.
- `VM9p2InchCustomThemeTypes`: `ImageVideo`, `Color`, `ThemeInfo`, `Modulars`.
- Template import result is represented by `VM9p2InchImportTemplateResults`.
- Real 9.2 template operations use `LCD92Controller.GetTemplate`, `GetTemplates`, `ImportTemplate`, `ApplyTemplate`, `SaveTemplate`, `GetThemeInfos`, `GetModulars`, `UpdateThemeInfo`, and `UpdateModular`.

Unconfirmed:

- Every field inside the `.template` binary/serialized payload. The decompiled wrapper works through `ThemeEngine.Theme`, but the complete SDK internal format is not fully visible in C#.
- No mandatory hash/signature mechanism was found in the reviewed C# service path; import appears to depend mostly on SDK parsing/validation.

## 8. 9.2 Import Flow

L-Connect 9.2 import chain:

`UI ImportTemplate`
→ `VM9p2InchRequestType.ImportTemplate`
→ `VM9p2InchController.handleImportTemplateRequest`
→ read file path from request body
→ `LCD92Controller.ImportTemplate(path)`
→ refresh `GetTemplates/GetThemeInfos/GetModulars` cache if import succeeds
→ return `VM9p2InchImportTemplateResults`.

Our current import/gallery chain:

`ZIP selection/gallery download`
→ `ThemePackageValidationService.Validate`
→ install files into ProgramData/ProgramFiles template/background/preview structure
→ `ThemeInstallationService.ActivateAsync`
→ registered id matching
→ try `ApplyTemplate/SetTemplate/Apply2DTemplate`
→ `SaveProfile`, `ApplyScreenContent`.

Difference: L-Connect 9.2 imports by passing a file path into the SDK. Our path often depends on copying files to the right directory and then applying a template id. That can be insufficient for 9.2.

## 9. 9.2 Apply Flow

L-Connect 9.2 normal template apply:

`ApplyTemplate(id)`
→ `handleApplyTemplateRequest`
→ `applyTemplate(id)`
→ `LCD92Controller.GetTemplate(id)`
→ `applyTemplate(Theme theme)`
→ `LCD92Controller.ApplyTemplate(theme)`
→ `templateApplied=true`
→ `SelectedTemplateId=id`
→ `IsCustomThemeEnabled=false`
→ `SaveProfile`.

9.2 custom theme apply:

`ApplyBackgroundOnlyCustomTheme`
→ `AppliedImageVideoPath = SelectedImageVideoPath`
→ `createBaseTemplate(bgPath, isLandscape)`
→ `applyTemplate(theme)`
→ `IsCustomThemeEnabled=true`, `Type=ImageVideo`.

`ApplyThemeInfo`
→ set `AppliedThemeInfo`
→ `createTemplateWithThemeInfo(bgPath, AppliedThemeInfo)`
→ `LCD92Controller.UpdateThemeInfo`
→ `applyTemplate(theme)`
→ `Type=ThemeInfo`.

`ApplyModulars`
→ `AppliedModulars = Modulars.DeepClone()`
→ `createTemplateWithModulars(bgPath, AppliedModulars)`
→ `LCD92Controller.UpdateModular`
→ `applyTemplate(theme)`
→ `Type=Modulars`.

## 10. Phone Link Flow Comparison

Phone Link:

`PhoneLinkWeb`
→ `/api/devices`, `/api/devices/{id}/themes`, `/api/devices/{id}/theme`, `/api/lighting/effects`, `/api/fan-groups`
→ `LConnectControlService`
→ `LConnectClient.SendServiceRequestForJsonAsync` or `SendDeviceRequestForJsonAsync`
→ L-Connect local HTTP.

Differences/risks:

- Phone Link `ApplyThemeAsync` only applies a template id; it does not know the 9.2 custom theme apply types.
- Phone Link lighting also updates disk profile files (`SyncTlWirelessMergeState`, `SyncWirelessFanUnbindLightingSetting`, `SyncWirelessFanIndividualLightingState`). Normal apply/gallery flows do not do this.
- Wireless fan apply treats some `500/Object reference` responses as acceptable. This may be practical, but it carries false-positive risk.

## 11. Gallery Flow Comparison

Our gallery flow centers around `MainWindow.xaml.cs`, `GalleryManifestService`, `ThemePackageValidationService`, and `ThemeInstallationService`. Our package manifest format contains:

- `FormatVersion`
- `DeviceModel`
- `TemplateId`
- `TemplateFile`
- `BackgroundFile`
- `PreviewFile`
- `ImageFiles`
- legacy fields.

In L-Connect, official-like local flows go through product controllers and asset/template folders. For 9.2, SDK import is the safer route.

Risks:

- The same template id may map to different internal `.turtheme` or media ids; `ExtractInternalIds` is regex-based.
- If a gallery package is an L-Connect ZIP without our manifest, the device model may be unknown.
- If 9.2 `SelectedTemplateId` and orientation-specific config are not updated, L-Connect may revert to another theme after restart.

## 12. Normal Apply Flow Comparison

Our normal apply:

`UI Apply/Save`
→ write layer changes into `.template` through `SupporterBridge`
→ update preview/background
→ send `ApplyScreenContent` or template apply commands to L-Connect
→ save final state/recovery.

L-Connect normal apply:

`Controller RequestType`
→ product controller handler
→ clone/modify SDK object (`Theme`)
→ SDK apply
→ update profile fields
→ `SaveProfile`.

Main difference: L-Connect treats the SDK model as authoritative. We often edit templates through files/text/regex/external worker bridge. That works for 480x480 cases, but is incomplete for 9.2/Flex custom states.

### Theme Flow Comparison Table

| Stage | L-Connect | Phone Link | Gallery | Normal apply | Difference/Risk |
|---|---|---|---|---|---|
| Device discovery | Controller registry + SDK bind | `SyncControllerList` + `InferDeviceModel` | selected device model | UI selection | model inference is hardcoded |
| Import | SDK `ImportTemplate` | Missing | ZIP extract/install | ZIP/import | SDK import missing for 9.2 |
| Apply | Product controller + SDK apply | `ApplyTemplate/SetTemplate/Apply2DTemplate` fallback | activation candidates + apply | bridge + apply content | success criteria differ |
| Profile save | controller `SaveProfile` | `SaveProfile` after command | indirect in some flows | in some flows | not consistent across all flows |
| Custom theme | `ImageVideo/ThemeInfo/Modulars` | Missing | Missing | partial layer edit | 9.2 state loss |
| Lighting | product profiles + device/service API | normalized + special paths | Missing | Missing | Phone Link behavior is separate |
| Asset paths | SDK/work dir copy | disk scan + service | ZIP paths | template paths | 9.2 work dir/asset copy may be missing |
| Error/rollback | response + log, some fallback | false/true response | validation + result | status/recovery | no general rollback |

## 13. Hardcoded Dependencies

Confirmed:

- `ThemePackageValidationService.SupportedDevices`: only `hydroshift-ii-lcd-s`, `hydroshift-ii-lcd-c`, `universal-screen-8.8-inch`, `vm-9.2-inch`.
- `MainWindow.xaml.cs`: `UniversalScreenDeviceModel`, `Vm92DeviceModel`, GitHub gallery URLs, blank seed URLs, `EnableDeepGalleryOrientationProbe => false`.
- `GalleryThemeItem`: wide device check only includes `universal-screen-8.8-inch` and `vm-9.2-inch`.
- `LConnectControlService`: `UniversalScreenDeviceModel`, `Vm92DeviceModel`, `WirelessFanDeviceId`, `WirelessFansModel`, `Tlv2MergeFansModel`.
- `LConnectControlService.SetBrightnessAsync`: special command ordering for `hydroshift-ii-lcd-s` and universal 8.8.
- `LConnectControlService.SetLightingEffectAsync`: special branches for universal 8.8 and `hydroshift-ii-lcd-s`.
- `LConnectControlService.GetFanGroupLightingEffects`: TL fan detection uses `DeviceType.Contains("TL")`.
- `LConnectControlService.Sync*`: hashed L-Connect device setting file names and JSON fields are hardcoded.
- `ThemeInstallationService.ExtractInternalIds`: extracts internal ids with `.turtheme` and `LL...` regexes.

High probability:

- `SupporterBridge` and worker code have device-model-based template parse/write branches.
- Template canvas dimensions are selected from the device model, not from capability metadata.

## 14. Missing or Disabled Features

- `EnableDeepGalleryOrientationProbe => false`: gallery orientation/dimension probing is intentionally disabled.
- Language files mention `Volume`, `RamModel`, and `Weather` as not yet working/experimental on the L-Connect side.
- UI strings mention that 9.2 direct apply is not supported yet and recommend exporting a ZIP/importing from L-Connect.
- QuickSync lighting is not represented as a separate capability.
- There is no data-driven adapter for Strimer Plus, Lancool207Digital, Flex LCD/TL Flex/SLINF Flex.
- 9.2 `ThemeInfo`, `Modulars`, `FixedData`, and `WirelessLCD` custom theme flows are missing/partial.

## 15. Likely Bugs and Incompatibilities

P0/P1 risks:

- If a 9.2 theme is installed only by copying files, `VM9p2InchTemplateConfig.SelectedTemplateId`, orientation config, or custom `Applied*` state may not be set. This risks theme loss after restart or the wrong theme being restored.
- Phone Link wireless fan flow writes profile/config files manually. If the L-Connect schema changes, this can corrupt state or target the wrong device.
- Some apply paths accept HTTP success/empty response as success; selected template validation is not universal.
- Capability checks happen late; unsupported effects may be converted into a body and sent first.

P2/P3 risks:

- The layer model stores everything as strings, so numeric validation is not device-specific.
- If effect/device color count is not enforced in UI, colors may be lost or extra colors may be ignored.
- Asset paths can diverge between template-internal ids and ZIP manifest ids.
- Gallery, Phone Link, and normal apply use different id matching and save/apply chains.

## 16. New Device Support Assessment

Typical changes required for a new device today:

- Device model name and visual asset.
- Blank seed/template.
- `SupportedDevices`.
- Gallery filter/orientation logic.
- Device discovery/model inference.
- Template dimensions/canvas mapping.
- Apply/import command strategy.
- Lighting effect support matrix.
- SupporterBridge template parser/writer support.

So the target equation is not achieved yet:

`New device definition + capability data + protocol/endpoint mapping = new device support`

Blockers:

- No capability schema.
- Effect parameters and color counts are not in one table.
- Product controller command sets are not externalized.
- Theme format family changes by device family.
- Devices such as 9.2/Flex use custom theme/modular/component state instead of plain layers.

## 17. Recommended Target Architecture

1. `DeviceCapability` registry:
   - model id, aliases, screen dimensions, orientation support
   - theme engine family: `classic-template`, `universal-8.8`, `vm-9.2`, `flex-lcd`, `wireless-lcd`
   - import strategy
   - apply strategy
   - lighting strategy
   - supported layer/modular/component types.

2. `LightingCapability` registry:
   - effect id
   - L-Connect enum/ID per product
   - color count min/max/exact/null-auto
   - speed/brightness/direction support
   - endpoint command/body mapper
   - fallback behavior.

3. `ThemeIntermediateModel`:
   - background media
   - classic layers
   - modular components
   - themeInfo settings
   - assets
   - target device and orientation
   - metadata + preview.

4. Unified apply pipeline:
   - validate
   - migrate
   - normalize assets
   - resolve capability
   - import/install
   - apply
   - verify selected/applied state
   - save profile
   - rollback/recovery.

## 18. Prioritized Development Plan

### Priority Table

| Priority | Finding | Impact | Difficulty | Recommended action |
|---|---|---|---|---|
| P0 | 9.2 apply/import is not aligned with SDK state | Broken/non-persistent theme | Medium | Add 9.2 import/apply adapter and verification |
| P0 | Hardcoded writes to wireless fan profile files | Wrong device/state risk | Medium | Schema-guarded writer + backup + capability gate |
| P1 | No device capability registry | New devices require code changes | Medium | JSON/config capability system |
| P1 | Lighting support matrix is incomplete | Effect/color loss | Medium | Generate effect DB from L-Connect decompile |
| P1 | 9.2 modular/themeInfo types are missing | 9.2 conversion incomplete | High | Intermediate model + VM9p2 adapter |
| P2 | Apply flows use different success criteria | False success | Low | Shared apply result verifier |
| P2 | Layer fields are string-based and device-agnostic | Weak validation | Medium | Typed layer/modular model |
| P3 | Regex id matching is fragile | Wrong id matching | Low | Template parser/SDK metadata extractor |
| P4 | Weather/Volume/RamModel are experimental | User expectation risk | Low | Disabled feature flag + clear UI label |

## 19. Risks

- Decompiled sources may be incomplete or misleading because of obfuscation and SDK wrapper boundaries.
- Real binary validation/serialization behavior inside native SDKs (`lcd207`, `slv3`) is not fully visible.
- Request types, JSON shapes, and profile gzip files may change between L-Connect versions.
- The same effect name can map to different enum/color behavior across product families.

## 20. Open and Unconfirmed Points

- Full binary/serialized field layout of 9.2 `.template` files.
- Whether 9.2 import requires checksum/signature; no mandatory signature was found in the reviewed C# service path.
- Complete color count matrix for every fan family. It can likely be extracted automatically from profile tables, but this report did not normalize every family one by one.
- Runtime behavior of experimental data sources such as Weather/Volume/RamModel in L-Connect 9.2/Flex.
- If L-Connect has an official online gallery flow, it was not isolated as one clear entrypoint in the decompiled sources; the local asset/import flow is confirmed.

## Conclusion

L-Connect 9.2 conversion is possible, but the correct path is not merely resizing an 8.8 template and putting it into a ZIP. Required flow:

`Old theme / our format`
→ `ThemeIntermediateModel`
→ `9.2 orientation and capability normalization`
→ `ThemeEngine.Theme` or 9.2 custom theme state generation
→ `VM9p2Inch ImportTemplate` or `ApplyThemeInfo/ApplyModulars/ApplyBackgroundOnlyCustomTheme`
→ `GetSelectedTemplateId` and applied state verification
→ `SaveProfile`
→ optionally `ApplyScreenContent`.

The first implementation step should be a small automatic extractor before production code changes:

- Extract product-specific effect enum, default colors, color count, and request type lists from L-Connect decompile/binary sources into JSON.
- Diff that output against the current `LConnectControlService.LConnectEffects`.
- Use the same extractor to collect 9.2/Flex modular types and field lists.

With those extractor outputs, we can design a capability registry that makes new device support genuinely data-driven.
