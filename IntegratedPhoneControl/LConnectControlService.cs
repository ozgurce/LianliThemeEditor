using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Reflection;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace PhoneControl;

public sealed class LConnectControlService
{
    private const string UniversalScreenDeviceModel = "universal-screen-8.8-inch";
    private const string Vm92DeviceModel = "vm-9.2-inch";
    private const string WirelessFanDeviceId = "wireless-fans";
    private const string WirelessFansModel = "l-wireless-fans";
    private const string Tlv2MergeFansModel = "tl-wireless-fans-merge";
    private static readonly Lazy<IReadOnlyDictionary<int, LConnectEffectMetadata>> WirelessHydroEffectMetadata = new(LConnectEffectMetadataReader.ReadWirelessHydroMetadata);
    private static readonly Lazy<IReadOnlyDictionary<int, LConnectEffectMetadata>> Tlv2EffectMetadata = new(LConnectEffectMetadataReader.ReadTlv2Metadata);
    private static readonly Lazy<IReadOnlyDictionary<int, LConnectEffectMetadata>> WirelessMergeEffectMetadata = new(LConnectEffectMetadataReader.ReadWirelessMergeMetadata);
    private static readonly Lazy<IReadOnlyList<int>> WirelessMergeEffectOrder = new(LConnectEffectMetadataReader.ReadWirelessMergeEffectOrder);
    private static readonly Lazy<IReadOnlyList<int>> Tlv2MergeEffectOrder = new(LConnectEffectMetadataReader.ReadTlv2MergeEffectOrder);
    private readonly LConnectClient lConnectClient;

    public LConnectControlService(LConnectClient lConnectClient)
    {
        this.lConnectClient = lConnectClient;
    }

    public async Task<LConnectStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var client = CreateClient(TimeSpan.FromSeconds(3));
        var result = await lConnectClient.SendServiceRequestForJsonAsync(client, "Ping", "{}", cancellationToken);
        if (!result.IsHttpSuccess)
        {
            return new LConnectStatus(false, result.Port, result.RequestMode, FirstNonEmpty(result.Error, result.ReasonPhrase, "L-Connect service is not reachable."));
        }

        return new LConnectStatus(true, result.Port, result.RequestMode, "L-Connect service is online.");
    }

    public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        using var client = CreateClient(TimeSpan.FromSeconds(4));
        var result = await lConnectClient.SendServiceRequestForJsonAsync(client, "SyncControllerList", "{}", cancellationToken);
        if (!result.IsHttpSuccess || string.IsNullOrWhiteSpace(result.Body))
        {
            return Array.Empty<DeviceInfo>();
        }

        var devices = new List<DeviceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profileNames = ReadDeviceNamesByModel();
        var screenBrightness = ReadScreenBrightnessByModel();

        try
        {
            using var doc = JsonDocument.Parse(result.Body);
            foreach (var controller in EnumerateControllers(doc.RootElement))
            {
                var path = controller.Path;
                if (string.IsNullOrWhiteSpace(path) || !seen.Add(NormalizeControllerPathForCompare(path)))
                {
                    continue;
                }

                var model = InferDeviceModel(path);
                if (string.IsNullOrWhiteSpace(model))
                {
                    continue;
                }

                var selected = await GetSelectedTemplateIdAsync(client, path, cancellationToken);
                var fallbackName = BuildDeviceName(model, devices.Count(d => string.Equals(d.Model, model, StringComparison.OrdinalIgnoreCase)) + 1);
                var profileName = TryDequeueDeviceName(profileNames, model);
                var brightness = TryDequeueScreenBrightness(screenBrightness, model);
                devices.Add(new DeviceInfo(
                    EncodingHelper.ToBase64Url(path),
                    BuildUniqueDeviceName(FirstNonEmpty(controller.Name, profileName, fallbackName), devices),
                    model,
                    path,
                    selected,
                    brightness));
            }
        }
        catch
        {
            return Array.Empty<DeviceInfo>();
        }

        if (GetWirelessFanGroups().Count > 0)
        {
            devices.Add(new DeviceInfo(WirelessFanDeviceId, "Wireless Fan Groups", WirelessFansModel, WirelessFanDeviceId, ""));
        }

        return devices;
    }

    public async Task<DeviceInfo?> ResolveDeviceAsync(string deviceId, CancellationToken cancellationToken)
    {
        if (!EncodingHelper.TryFromBase64Url(deviceId, out var path))
        {
            return null;
        }

        var model = InferDeviceModel(path);
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        using var client = CreateClient(TimeSpan.FromSeconds(3));
        var selected = await GetSelectedTemplateIdAsync(client, path, cancellationToken);
        var name = await GetControllerNameAsync(client, path, cancellationToken);
        var profileNames = ReadDeviceNamesByModel();
        var screenBrightness = ReadScreenBrightnessByModel();
        return new DeviceInfo(
            deviceId,
            FirstNonEmpty(name, TryDequeueDeviceName(profileNames, model), BuildDeviceName(model, 1)),
            model,
            path,
            selected,
            TryDequeueScreenBrightness(screenBrightness, model));
    }

    public IReadOnlyList<WirelessFanGroupInfo> GetWirelessFanGroups()
    {
        var path = Path.Combine(LConnectPaths.ProgramDataRoot, "slv3", "config", "savedDevices.config");
        if (!File.Exists(path))
        {
            return Array.Empty<WirelessFanGroupInfo>();
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("$values", out var values) || values.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<WirelessFanGroupInfo>();
            }

            var groups = new List<WirelessFanGroupInfo>();
            var profileNames = ReadWirelessFanGroupNames();
            foreach (var item in values.EnumerateArray())
            {
                var fanCount = GetJsonInt(item, "FanNum", "fan_num");
                if (fanCount <= 0)
                {
                    continue;
                }

                var mac = GetJsonString(item, "MacStr");
                if (string.IsNullOrWhiteSpace(mac))
                {
                    continue;
                }

                var group = GetJsonInt(item, "LcdGroup");
                var sortIndex = GetJsonInt(item, "SortIndex");
                var savedName = FirstNonEmpty(
                    profileNames.TryGetValue(mac, out var profileName) ? profileName : "",
                    GetJsonString(item, "Name"),
                    GetJsonString(item, "GroupName"));
                var ledCount = GetJsonInt(item, "LedNum");
                var recType = ReadFirstInt(item, "RecType");
                var typeName = recType switch
                {
                    28 => "TL Wireless Fan",
                    36 => "SL-INF Wireless Fan",
                    59 => "SL Wireless V4",
                    63 => "P28 Wireless",
                    _ => "Wireless Fan"
                };
                var shortTypeName = recType switch
                {
                    28 => "TL W",
                    36 => "SL-INF W",
                    59 => "SL W V4",
                    63 => "P28 W",
                    _ => "Wireless"
                };
                groups.Add(new WirelessFanGroupInfo(
                    EncodingHelper.ToBase64Url(mac),
                    string.IsNullOrWhiteSpace(savedName)
                        ? $"#{group} {shortTypeName}_G{sortIndex} ({fanCount} fan{(fanCount == 1 ? "" : "s")})"
                        : $"{savedName} (#{group} {shortTypeName}_G{sortIndex}, {fanCount} fan{(fanCount == 1 ? "" : "s")})",
                    mac,
                    fanCount,
                    ledCount,
                    group,
                    typeName,
                    sortIndex));
            }

            return groups.OrderBy(g => g.Group).ToArray();
        }
        catch
        {
            return Array.Empty<WirelessFanGroupInfo>();
        }
    }

    public async Task<IReadOnlyList<ThemeInfo>> GetThemesAsync(string deviceId, CancellationToken cancellationToken)
    {
        var device = await ResolveDeviceAsync(deviceId, cancellationToken);
        if (device is null)
        {
            return Array.Empty<ThemeInfo>();
        }

        using var client = CreateClient(TimeSpan.FromSeconds(8));
        await SendDeviceCommandAsync(client, device.Path, "ReloadAssets", "{}", cancellationToken);
        var result = await lConnectClient.SendDeviceRequestForJsonAsync(client, device.Path, "GetTemplates", "{}", cancellationToken);
        if (!result.IsHttpSuccess || string.IsNullOrWhiteSpace(result.Body))
        {
            return Array.Empty<ThemeInfo>();
        }

        var selectedId = await GetSelectedTemplateIdAsync(client, device.Path, cancellationToken);
        var themes = ParseThemes(result.Body, deviceId, selectedId);
        return themes.Count == 0
            ? ReadThemesFromDisk(device.Model, deviceId, selectedId)
            : themes;
    }

    public async Task<CommandResult> ApplyThemeAsync(string deviceId, string themeId, CancellationToken cancellationToken)
    {
        var device = await ResolveDeviceAsync(deviceId, cancellationToken);
        if (device is null)
        {
            return new CommandResult(false, "Device was not found.");
        }

        using var client = CreateClient(TimeSpan.FromSeconds(12));
        var tried = new List<string>();
        await SendDeviceCommandAsync(client, device.Path, "ReloadAssets", "{}", cancellationToken);

        foreach (var command in new[] { "ApplyTemplate", "SetTemplate", "Apply2DTemplate" })
        {
            tried.Add(command);
            var result = await SendDeviceCommandAsync(client, device.Path, command, JsonSerializer.Serialize(themeId), cancellationToken);
            if (!result)
            {
                continue;
            }

            if (await WaitForSelectedTemplateAsync(client, device.Path, themeId, cancellationToken))
            {
                await SendDeviceCommandAsync(client, device.Path, "SaveProfile", "{}", cancellationToken);
                await SendDeviceCommandAsync(client, device.Path, "ApplyScreenContent", "{}", cancellationToken);
                return new CommandResult(true, "Theme applied.", command, tried);
            }
        }

        return new CommandResult(false, "L-Connect did not confirm the selected theme.", null, tried);
    }

    public async Task<CommandResult> SetBrightnessAsync(string deviceId, int value, CancellationToken cancellationToken)
    {
        var device = await ResolveDeviceAsync(deviceId, cancellationToken);
        if (device is null)
        {
            return new CommandResult(false, "Device was not found.");
        }

        using var client = CreateClient(TimeSpan.FromSeconds(5));
        var commandNames = device.Model.Equals("hydroshift-ii-lcd-s", StringComparison.OrdinalIgnoreCase)
            ? new[]
            {
                "SetPumpLCDBrightness",
                "SetPumpLcdBrightness",
                "SetLCDScreenBrightness",
                "SetLCDBrightness",
                "SetLcdBrightness",
                "SetScreenBrightness",
                "SetBrightness",
                "ChangeBrightness",
                "ScreenBrightness"
            }
            : new[]
            {
                "SetBrightness",
                "SetScreenBrightness",
                "ChangeBrightness",
                "ScreenBrightness",
                "SetLcdBrightness",
                "SetLCDBrightness",
                "SetLCDScreenBrightness",
                "SetPumpLCDBrightness",
                "SetPumpLcdBrightness"
            };
        var bodies = new[]
        {
            JsonSerializer.Serialize(value),
            JsonSerializer.Serialize(new { Brightness = value }),
            JsonSerializer.Serialize(new { brightness = value }),
            JsonSerializer.Serialize(new { Value = value }),
            JsonSerializer.Serialize(new { value })
        };

        var tried = new List<string>();
        if (device.Model.Equals(UniversalScreenDeviceModel, StringComparison.OrdinalIgnoreCase))
        {
            var command = "SetScreenBrightness";
            tried.Add(command);
            if (await SendDeviceCommandAsync(client, device.Path, command, JsonSerializer.Serialize(value), cancellationToken, acceptEmptyResponse: true))
            {
                return new CommandResult(true, $"Brightness set to {value}.", command, tried);
            }
        }

        if (device.Model.Equals("hydroshift-ii-lcd-s", StringComparison.OrdinalIgnoreCase))
        {
            var command = "SetLCDBrightness";
            tried.Add(command);
            if (await SendDeviceCommandAsync(client, device.Path, command, JsonSerializer.Serialize(value), cancellationToken, acceptEmptyResponse: true))
            {
                return new CommandResult(true, $"Brightness set to {value}.", command, tried);
            }
        }

        foreach (var command in commandNames)
        {
            foreach (var body in bodies)
            {
                tried.Add(command);
                if (await SendDeviceCommandAsync(client, device.Path, command, body, cancellationToken))
                {
                    return new CommandResult(true, $"Brightness set to {value}.", command, tried);
                }
            }
        }

        return new CommandResult(false, "No known brightness command was accepted by L-Connect.", null, tried.Distinct().ToArray());
    }

    public IReadOnlyList<LightingEffectInfo> GetLightingEffects(string targetModel = "") =>
        EffectsForTarget(targetModel)
            .Select(effect => new LightingEffectInfo(effect.Id, effect.Name, effect.Accent, ColorCountForTarget(effect, targetModel)))
            .ToArray();

    public async Task<IReadOnlyList<LightingEffectInfo>> GetLightingEffectsAsync(DeviceInfo device, CancellationToken cancellationToken)
    {
        var colorCounts = device.Model.Equals(UniversalScreenDeviceModel, StringComparison.OrdinalIgnoreCase)
            ? await GetUniversalScreenLightingColorCountsAsync(device.Path, cancellationToken)
            : new Dictionary<int, int>();

        return EffectsForTarget(device.Model)
            .Select(effect =>
            {
                var colorCount = colorCounts.TryGetValue(effect.UniversalMode, out var count)
                    ? count
                    : ColorCountForTarget(effect, device.Model);
                return new LightingEffectInfo(effect.Id, effect.Name, effect.Accent, colorCount);
            })
            .ToArray();
    }

    public IReadOnlyList<LightingEffectInfo> GetFanGroupLightingEffects(string groupId, bool merge = false)
    {
        var group = ResolveFanGroup(groupId);
        var isTlFan = group?.DeviceType.Contains("TL", StringComparison.OrdinalIgnoreCase) == true;
        var targetModel = merge && isTlFan
            ? Tlv2MergeFansModel
            : isTlFan
            ? "tl-wireless-fans"
            : WirelessFansModel;

        return EffectsForTarget(targetModel)
            .Select(effect =>
            {
                var colorCount = ColorCountForTarget(effect, targetModel);
                if (!merge && isTlFan && effect.Id == "static" && group is not null)
                {
                    colorCount = Math.Clamp(group.FanCount, 1, 4);
                }

                return new LightingEffectInfo(effect.Id, effect.Name, effect.Accent, colorCount);
            })
            .ToArray();
    }

    public LightingEffectState? GetFanGroupLightingState(string groupId, bool merge = false)
    {
        var group = ResolveFanGroup(groupId);
        if (group is null)
        {
            return null;
        }

        var isTlFan = group.DeviceType.Contains("TL", StringComparison.OrdinalIgnoreCase);
        var targetModel = merge && isTlFan
            ? Tlv2MergeFansModel
            : isTlFan
            ? "tl-wireless-fans"
            : WirelessFansModel;

        var template = TryReadWirelessFanLightingTemplate(group);
        if (template is null)
        {
            return null;
        }

        var mode = ReadNodeInt(template["TLV2Mode"])
            ?? ReadNodeInt(template["MergeMode"])
            ?? 0;
        var effect = EffectsForTarget(targetModel).FirstOrDefault(item => item.WirelessMode == mode)
            ?? EffectsForTarget("tl-wireless-fans").FirstOrDefault(item => item.WirelessMode == mode);
        if (effect is null)
        {
            return null;
        }

        var brightness = FromWirelessBrightness(ReadNodeInt(template["Brightness"]) ?? 255);
        var speed = FromWirelessSpeed(ReadNodeInt(template["Speed"]) ?? 5);
        var direction = Math.Clamp(ReadNodeInt(template["Direction"]) ?? 0, 0, 1);
        var colors = ReadColorHexArray(template["Color"]);
        return new LightingEffectState(effect.Id, brightness, colors, speed, direction, merge);
    }

    public async Task<CommandResult> SetLedBrightnessAsync(string deviceId, int value, CancellationToken cancellationToken)
    {
        var device = await ResolveDeviceAsync(deviceId, cancellationToken);
        if (device is null)
        {
            return new CommandResult(false, "Device was not found.");
        }

        using var client = CreateClient(TimeSpan.FromSeconds(6));
        var tried = new List<string>();
        var commandNames = new[]
        {
            "SetLEDBrightness",
            "SetLedBrightness",
            "SetLightingBrightness",
            "SetRGBBrightness",
            "SetEffectBrightness",
            "SetLightingEffectBrightness",
            "SetScreenLightingBrightness",
            "SetScreenBrightness"
        };
        var bodies = new[]
        {
            JsonSerializer.Serialize(value),
            JsonSerializer.Serialize(new { Brightness = value }),
            JsonSerializer.Serialize(new { brightness = value }),
            JsonSerializer.Serialize(new { LedBrightness = value }),
            JsonSerializer.Serialize(new { ledBrightness = value })
        };

        if (device.Model.Equals(UniversalScreenDeviceModel, StringComparison.OrdinalIgnoreCase))
        {
            var result = await ApplyUniversalLightingAsync(client, device.Path, "meteor", value, "#ff6b6b", null, 3, 0, tried, cancellationToken);
            if (result.Success)
            {
                return result with { Message = $"LED brightness set to {value}." };
            }
        }

        if (device.Model.Equals("hydroshift-ii-lcd-s", StringComparison.OrdinalIgnoreCase))
        {
            var result = await ApplyWirelessMergeLightingAsync(client, WirelessMergeTarget.HydroShift, "meteor", value, "#ff6b6b", null, 3, 0, false, tried, cancellationToken);
            if (result.Success)
            {
                return result with { Message = $"LED brightness set to {value}." };
            }
        }

        foreach (var command in commandNames)
        {
            foreach (var body in bodies)
            {
                tried.Add(command);
                if (await SendDeviceCommandAsync(client, device.Path, command, body, cancellationToken))
                {
                    await SendDeviceCommandAsync(client, device.Path, "SaveProfile", "{}", cancellationToken);
                    await SendDeviceCommandAsync(client, device.Path, "ApplyScreenContent", "{}", cancellationToken);
                    return new CommandResult(true, $"LED brightness set to {value}.", command, tried);
                }
            }
        }

        return new CommandResult(false, "No known LED brightness command was accepted by L-Connect.", null, tried.Distinct().ToArray());
    }

    public async Task<CommandResult> SetLightingEffectAsync(string deviceId, string effect, int brightness, string? color, string[]? colors, int speed, int direction, CancellationToken cancellationToken)
    {
        var normalizedEffect = NormalizeEffect(effect);
        if (string.IsNullOrWhiteSpace(normalizedEffect))
        {
            return new CommandResult(false, "Unknown lighting effect.");
        }

        var device = await ResolveDeviceAsync(deviceId, cancellationToken);
        if (device is null)
        {
            return new CommandResult(false, "Device was not found.");
        }

        using var client = CreateClient(TimeSpan.FromSeconds(8));
        var tried = new List<string>();
        var normalizedColor = NormalizeColor(color, GetEffectAccent(normalizedEffect));
        if (device.Model.Equals(UniversalScreenDeviceModel, StringComparison.OrdinalIgnoreCase))
        {
            return await ApplyUniversalLightingAsync(client, device.Path, normalizedEffect, Math.Clamp(brightness, 0, 100), normalizedColor, colors, speed, direction, tried, cancellationToken);
        }

        if (device.Model.Equals("hydroshift-ii-lcd-s", StringComparison.OrdinalIgnoreCase))
        {
            return await ApplyWirelessMergeLightingAsync(client, WirelessMergeTarget.HydroShift, normalizedEffect, Math.Clamp(brightness, 0, 100), normalizedColor, colors, speed, direction, false, tried, cancellationToken);
        }

        foreach (var body in BuildLightingEffectBodies(normalizedEffect, Math.Clamp(brightness, 0, 100), normalizedColor, colors, speed, direction))
        {
            foreach (var command in new[] { "SetLightingEffect", "SetLightingEffectSetting", "SetLedEffect", "SetLEDEffect", "SetRGBEffect" })
            {
                tried.Add(command);
                if (await SendDeviceCommandAsync(client, device.Path, command, body, cancellationToken))
                {
                    await SendDeviceCommandAsync(client, device.Path, "SaveProfile", "{}", cancellationToken);
                    return new CommandResult(true, $"Lighting effect set to {ToEffectDisplayName(normalizedEffect)}.", command, tried);
                }
            }
        }

        return new CommandResult(false, "No known lighting effect command was accepted by L-Connect.", null, tried.Distinct().ToArray());
    }

    public async Task<CommandResult> SetFanGroupLedBrightnessAsync(string groupId, int value, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        return new CommandResult(false, "Fan LED brightness must be applied with the selected lighting effect.");
    }

    public async Task<CommandResult> SetFanGroupLightingEffectAsync(string groupId, string effect, int brightness, string? color, string[]? colors, int speed, int direction, bool merge, bool applyAll, CancellationToken cancellationToken)
    {
        var group = ResolveFanGroup(groupId);
        if (group is null)
        {
            return new CommandResult(false, "Wireless fan group was not found.");
        }

        var normalizedEffect = NormalizeEffect(effect);
        if (string.IsNullOrWhiteSpace(normalizedEffect))
        {
            return new CommandResult(false, "Unknown lighting effect.");
        }

        using var client = CreateClient(TimeSpan.FromSeconds(8));
        var tried = new List<string>();
        if (!applyAll)
        {
            if (!merge)
            {
                return await ApplyWirelessFanLightingAsync(
                    client,
                    group,
                    normalizedEffect,
                    Math.Clamp(brightness, 0, 100),
                    NormalizeColor(color, GetEffectAccent(normalizedEffect)),
                    colors,
                    speed,
                    direction,
                    tried,
                    cancellationToken);
            }

            if (!group.DeviceType.Contains("TL Wireless", StringComparison.OrdinalIgnoreCase))
            {
                return new CommandResult(false, "Merge lighting is only available for TL wireless fan groups.", null, tried);
            }

            return await ApplyWirelessFanGroupMergeLightingAsync(
                client,
                group,
                new[] { group },
                normalizedEffect,
                Math.Clamp(brightness, 0, 100),
                NormalizeColor(color, GetEffectAccent(normalizedEffect)),
                colors,
                speed,
                direction,
                tried,
                cancellationToken,
                selectedOnly: true);
        }

        var tlGroups = GetWirelessFanGroups()
            .Where(item => item.DeviceType.Contains("TL Wireless", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Group)
            .ToArray();

        return await ApplyWirelessFanGroupMergeLightingAsync(
            client,
            group,
            tlGroups.Length > 0 ? tlGroups : new[] { group },
            normalizedEffect,
            Math.Clamp(brightness, 0, 100),
            NormalizeColor(color, GetEffectAccent(normalizedEffect)),
            colors,
            speed,
            direction,
            tried,
            cancellationToken);
    }

    private async Task<CommandResult> ApplyUniversalLightingAsync(
        HttpClient client,
        string devicePath,
        string effect,
        int brightness,
        string color,
        string[]? colors,
        int speed,
        int direction,
        List<string> tried,
        CancellationToken cancellationToken)
    {
        var mode = ToUniversalLightingMode(effect);
        var colorCounts = await GetUniversalScreenLightingColorCountsAsync(devicePath, cancellationToken);
        var colorCount = colorCounts.TryGetValue(mode, out var count)
            ? count
            : (int?)null;

        foreach (var body in BuildLightingEffectBodies(effect, brightness, color, colors, speed, direction, colorCount))
        {
            foreach (var command in new[] { "SetLightingEffectSetting", "SetLightingSetting", "SetScreenLightingEffectSetting" })
            {
                tried.Add(command);
                if (await SendDeviceCommandAsync(client, devicePath, command, body, cancellationToken, acceptEmptyResponse: command is "SetLightingEffectSetting"))
                {
                    await SendDeviceCommandAsync(client, devicePath, "SaveProfile", "{}", cancellationToken);
                    await SendDeviceCommandAsync(client, devicePath, "ApplyScreenContent", "{}", cancellationToken);
                    return new CommandResult(true, $"Lighting effect set to {ToEffectDisplayName(effect)}.", command, tried);
                }
            }
        }

        return new CommandResult(false, "No known lighting effect command was accepted by L-Connect.", null, tried.Distinct().ToArray());
    }

    private async Task<CommandResult> ApplyWirelessMergeLightingAsync(
        HttpClient client,
        WirelessMergeTarget target,
        string effect,
        int brightness,
        string color,
        string[]? colors,
        int speed,
        int direction,
        bool merge,
        List<string> tried,
        CancellationToken cancellationToken)
    {
        var body = BuildWirelessMergeLightingBody(target, effect, brightness, color, colors, speed, direction, merge);
        tried.Add("SetWMergeLightingEffect");
        if (!await SendServiceCommandAsync(client, "SetWMergeLightingEffect", body, cancellationToken, acceptEmptyResponse: true))
        {
            return new CommandResult(false, "L-Connect did not accept the HydroShift LED setting.", null, tried);
        }

        tried.Add("ApplyWMergeLightingEffect");
        if (!await SendServiceCommandAsync(client, "ApplyWMergeLightingEffect", body, cancellationToken, acceptEmptyResponse: true))
        {
            return new CommandResult(false, "L-Connect did not apply the HydroShift LED setting.", null, tried);
        }

        return new CommandResult(true, $"Lighting effect set to {ToEffectDisplayName(effect)}.", "ApplyWMergeLightingEffect", tried);
    }

    private async Task<CommandResult> ApplyWirelessFanLightingAsync(
        HttpClient client,
        WirelessFanGroupInfo group,
        string effect,
        int brightness,
        string color,
        string[]? colors,
        int speed,
        int direction,
        List<string> tried,
        CancellationToken cancellationToken)
    {
        var body = BuildWirelessFanLightingBody(group, effect, brightness, color, colors, speed, direction);
        AppendDiagnosticLog($"FanLightingSetting request mac={group.MacStr} effect={effect} brightness={brightness} speed={speed} direction={direction} body={body}");
        SyncWirelessFanUnbindLightingSetting(group, body);
        SyncWirelessFanIndividualLightingState(group, true);
        var wirelessPath = await GetWirelessControllerPathAsync(client, cancellationToken);
        if (string.IsNullOrWhiteSpace(wirelessPath))
        {
            return new CommandResult(false, "L-Connect wireless controller was not found.", null, tried);
        }

        tried.Add("Device:FanLightingSetting");
        if (!await SendWirelessDeviceCommandAsync(client, wirelessPath, "FanLightingSetting", body, cancellationToken))
        {
            return new CommandResult(false, "L-Connect did not accept the fan group LED setting.", null, tried);
        }

        return new CommandResult(true, $"Lighting effect set to {ToEffectDisplayName(effect)}.", "Device:FanLightingSetting", tried);
    }

    private async Task RestoreWirelessMergeLightingAsync(HttpClient client, List<string> tried, CancellationToken cancellationToken)
    {
        tried.Add("RestoreWMergeLightingEffect");
        await SendServiceCommandAsync(client, "RestoreWMergeLightingEffect", "{}", cancellationToken, acceptEmptyResponse: true);
    }

    private async Task<CommandResult> ApplyWirelessFanGroupMergeLightingAsync(
        HttpClient client,
        WirelessFanGroupInfo group,
        IReadOnlyList<WirelessFanGroupInfo> targetGroups,
        string effect,
        int brightness,
        string color,
        string[]? colors,
        int speed,
        int direction,
        List<string> tried,
        CancellationToken cancellationToken,
        bool selectedOnly = false)
    {
        if (!group.DeviceType.Contains("TL Wireless", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(false, "Group merge is only available for TL wireless fan groups.", null, tried);
        }

        var tlGroups = targetGroups
            .Where(item => item.DeviceType.Contains("TL Wireless", StringComparison.OrdinalIgnoreCase))
            .Where(item => !string.IsNullOrWhiteSpace(item.MacStr))
            .ToArray();
        if (tlGroups.Length == 0)
        {
            return new CommandResult(false, "No TL wireless fan groups were found for merge lighting.", null, tried);
        }

        if (!selectedOnly)
        {
            SyncTlWirelessMergeState(true, tlGroups);
        }

        var body = BuildWirelessFanMergeLightingBody(tlGroups, effect, brightness, color, colors, speed, direction, selectedOnly);
        var wirelessPath = await GetWirelessControllerPathAsync(client, cancellationToken);
        if (string.IsNullOrWhiteSpace(wirelessPath))
        {
            return new CommandResult(false, "L-Connect wireless controller was not found.", null, tried);
        }

        tried.Add("Device:FanMergeLightingSetting");
        if (!await SendWirelessDeviceCommandAsync(client, wirelessPath, "FanMergeLightingSetting", body, cancellationToken))
        {
            return new CommandResult(false, "L-Connect did not accept the merged fan LED setting.", null, tried);
        }

        var message = selectedOnly
            ? $"Lighting effect set to {ToEffectDisplayName(effect)} for {group.Name}."
            : $"TL wireless merged lighting effect set to {ToEffectDisplayName(effect)}.";
        return new CommandResult(true, message, "Device:FanMergeLightingSetting", tried);
    }

    private async Task<bool> WaitForSelectedTemplateAsync(HttpClient client, string devicePath, string expectedTemplateId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(125, cancellationToken);
            }

            var selectedId = await GetSelectedTemplateIdAsync(client, devicePath, cancellationToken);
            if (string.Equals(selectedId, expectedTemplateId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<string> GetSelectedTemplateIdAsync(HttpClient client, string devicePath, CancellationToken cancellationToken)
    {
        var result = await lConnectClient.SendDeviceRequestForJsonAsync(client, devicePath, "GetSelectedTemplateId", "{}", cancellationToken);
        if (!result.IsHttpSuccess || string.IsNullOrWhiteSpace(result.Body))
        {
            return "";
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Body);
            return FirstNonEmpty(
                GetJsonString(doc.RootElement, "Data"),
                GetJsonString(doc.RootElement, "SelectedTemplateId"),
                GetJsonString(doc.RootElement, "TemplateId"),
                doc.RootElement.ValueKind == JsonValueKind.String ? doc.RootElement.GetString() ?? "" : "");
        }
        catch
        {
            return "";
        }
    }

    private async Task<bool> SendDeviceCommandAsync(
        HttpClient client,
        string devicePath,
        string command,
        string body,
        CancellationToken cancellationToken,
        bool acceptEmptyResponse = false)
    {
        var result = await lConnectClient.SendDeviceRequestForJsonAsync(client, devicePath, command, body, cancellationToken);
        if (!result.IsHttpSuccess)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(result.Body))
        {
            return acceptEmptyResponse || command is "ReloadAssets" or "SaveProfile" or "ApplyScreenContent" or "StopVideo";
        }

        return IsSuccessfulLConnectResponse(result.Body);
    }

    private async Task<bool> SendServiceCommandAsync(
        HttpClient client,
        string command,
        string body,
        CancellationToken cancellationToken,
        bool acceptEmptyResponse = false)
    {
        var result = await lConnectClient.SendServiceRequestForJsonAsync(client, command, body, cancellationToken);
        if (!result.IsHttpSuccess)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(result.Body))
        {
            return acceptEmptyResponse;
        }

        return IsSuccessfulLConnectResponse(result.Body);
    }

    private async Task<bool> SendLWirelessCommandAsync(
        HttpClient client,
        string type,
        string body,
        CancellationToken cancellationToken,
        bool acceptEmptyResponse = false)
    {
        var result = await lConnectClient.SendServiceRequestForJsonAsync(
            client,
            "LWireless",
            new Dictionary<string, string> { ["type"] = type },
            body,
            cancellationToken);
        if (!result.IsHttpSuccess)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(result.Body))
        {
            return acceptEmptyResponse;
        }

        return IsSuccessfulLConnectResponse(result.Body);
    }

    private async Task<string> GetWirelessControllerPathAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var result = await lConnectClient.SendServiceRequestForJsonAsync(client, "SyncControllerList", "{}", cancellationToken);
        if (!result.IsHttpSuccess || string.IsNullOrWhiteSpace(result.Body))
        {
            return "";
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Body);
            return EnumerateControllers(doc.RootElement)
                .Select(controller => controller.Path)
                .FirstOrDefault(IsWirelessTransmitterControllerPath) ?? "";
        }
        catch
        {
            return "";
        }
    }

    private async Task<string> GetControllerNameAsync(HttpClient client, string devicePath, CancellationToken cancellationToken)
    {
        var result = await lConnectClient.SendServiceRequestForJsonAsync(client, "SyncControllerList", "{}", cancellationToken);
        if (!result.IsHttpSuccess || string.IsNullOrWhiteSpace(result.Body))
        {
            return "";
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Body);
            var normalizedPath = NormalizeControllerPathForCompare(devicePath);
            return EnumerateControllers(doc.RootElement)
                .Where(controller => string.Equals(NormalizeControllerPathForCompare(controller.Path), normalizedPath, StringComparison.OrdinalIgnoreCase))
                .Select(controller => controller.Name)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "";
        }
        catch
        {
            return "";
        }
    }

    private async Task<bool> SendWirelessDeviceCommandAsync(
        HttpClient client,
        string devicePath,
        string command,
        string body,
        CancellationToken cancellationToken)
    {
        var result = await lConnectClient.SendDeviceRequestForJsonAsync(client, devicePath, command, body, cancellationToken);
        if (result.IsHttpSuccess)
        {
            return string.IsNullOrWhiteSpace(result.Body) || IsSuccessfulLConnectResponse(result.Body);
        }

        return result.StatusCode is 500 &&
               result.Body.Contains("Object reference", StringComparison.OrdinalIgnoreCase);
    }

    private static void SyncTlWirelessMergeState(bool merged, IEnumerable<WirelessFanGroupInfo> groups)
    {
        try
        {
            var tlMacs = groups
                .Where(group => group.DeviceType.Contains("TL Wireless", StringComparison.OrdinalIgnoreCase))
                .Select(group => group.MacStr)
                .Where(mac => !string.IsNullOrWhiteSpace(mac))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (tlMacs.Count == 0)
            {
                return;
            }

            UpdateWirelessProfileMergeFlag(merged);
            UpdateMergeDeviceListFlags(merged, tlMacs);
        }
        catch
        {
        }
    }

    private static void UpdateWirelessProfileMergeFlag(bool merged)
    {
        foreach (var file in EnumerateProfileFiles())
        {
            if (!TryReadGZipText(file, out var json) ||
                !json.Contains("\"IsTLV2Merged\"", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var root = ParseLConnectJson(json)?.AsObject();
            if (root is null)
            {
                continue;
            }

            root["IsAllMerged"] = false;
            root["IsTLV2Merged"] = merged;
            WriteGZipText(file, root.ToJsonString());
            return;
        }
    }

    private static void UpdateMergeDeviceListFlags(bool merged, HashSet<string> tlMacs)
    {
        foreach (var file in EnumerateProfileFiles())
        {
            if (!TryReadGZipText(file, out var json) ||
                !json.Contains("\"IsIndividualLightingActive\"", StringComparison.OrdinalIgnoreCase) ||
                !json.Contains("\"DeviceList\"", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var root = ParseLConnectJson(json)?.AsObject();
            var devices = root?["DeviceList"]?.AsArray();
            if (root is null || devices is null)
            {
                continue;
            }

            foreach (var device in devices.OfType<JsonObject>())
            {
                var mac = device["MacStr"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(mac) && tlMacs.Contains(mac))
                {
                    device["IsIndividualLightingActive"] = !merged;
                }
            }

            WriteGZipText(file, root.ToJsonString());
            return;
        }
    }

    private static void SyncWirelessFanUnbindLightingSetting(WirelessFanGroupInfo group, string fanLightingBody)
    {
        try
        {
            if (!group.DeviceType.Contains("TL Wireless", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(group.MacStr))
            {
                return;
            }

            var file = GetLConnectDeviceSettingFile("LWireless-UnBindDevice-Setting", "Fan");
            if (!File.Exists(file) || !TryReadGZipText(file, out var json))
            {
                AppendDiagnosticLog($"Fan unbind setting not readable: {file}");
                return;
            }

            var root = ParseLConnectJson(json)?.AsObject();
            var data = root?["Data"]?.AsObject();
            var fan = data?[group.MacStr]?.AsObject();
            if (root is null || data is null || fan is null)
            {
                AppendDiagnosticLog($"Fan unbind setting missing MAC {group.MacStr}: {file}");
                return;
            }

            var lightingSetting = fan["LightingSetting"] as JsonObject;
            if (lightingSetting is null)
            {
                lightingSetting = new JsonObject();
                fan["LightingSetting"] = lightingSetting;
            }

            var config = ParseLConnectJson(fanLightingBody);
            if (config is null)
            {
                AppendDiagnosticLog($"Fan lighting body could not be parsed for {group.MacStr}");
                return;
            }

            fan["Scope"] = 5;
            lightingSetting["TLAll"] = config.DeepClone();
            WriteGZipText(file, root.ToJsonString());
            AppendDiagnosticLog($"Fan unbind setting synced for {group.MacStr}: {file}");
        }
        catch (Exception ex)
        {
            AppendDiagnosticLog($"Fan unbind setting sync failed for {group.MacStr}: {ex}");
        }
    }

    private static void SyncWirelessFanIndividualLightingState(WirelessFanGroupInfo group, bool individual)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(group.MacStr))
            {
                return;
            }

            var file = GetLConnectDeviceSettingFile("MergeLightingEffectSetting", "MergeLightingEffectSetting");
            if (!File.Exists(file) || !TryReadGZipText(file, out var json))
            {
                AppendDiagnosticLog($"Merge lighting setting not readable: {file}");
                return;
            }

            var root = ParseLConnectJson(json)?.AsObject();
            var data = root?["Data"]?.AsObject();
            var devices = data?["DeviceList"]?.AsArray();
            if (root is null || data is null || devices is null)
            {
                AppendDiagnosticLog($"Merge lighting setting has no DeviceList: {file}");
                return;
            }

            foreach (var device in devices.OfType<JsonObject>())
            {
                var mac = device["MacStr"]?.GetValue<string>();
                if (string.Equals(mac, group.MacStr, StringComparison.OrdinalIgnoreCase))
                {
                    device["IsIndividualLightingActive"] = individual;
                    WriteGZipText(file, root.ToJsonString());
                    AppendDiagnosticLog($"Merge lighting individual state set for {group.MacStr}: {individual}");
                    return;
                }
            }

            AppendDiagnosticLog($"Merge lighting DeviceList missing MAC {group.MacStr}: {file}");
        }
        catch (Exception ex)
        {
            AppendDiagnosticLog($"Merge lighting individual state sync failed for {group.MacStr}: {ex}");
        }
    }

    private static string GetLConnectDeviceSettingFile(string deviceKey, string settingKey) =>
        Path.Combine(
            LConnectPaths.ProgramDataRoot,
            "device",
            Md5Lower(deviceKey),
            $"{Md5Lower(settingKey)}.0");

    private static string Md5Lower(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value.ToLowerInvariant());
        var hash = MD5.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AppendDiagnosticLog(string message)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Lian-Li Phone Link");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "diagnostics.log"), $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static IEnumerable<string> EnumerateProfileFiles()
    {
        var profileRoot = Path.Combine(LConnectPaths.ProgramDataRoot, "profile");
        if (!Directory.Exists(profileRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(profileRoot)
            .Where(file => !Path.GetFileName(file).Contains("backup", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static Dictionary<string, string> ReadWirelessFanGroupNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? mac, string? name)
        {
            if (!string.IsNullOrWhiteSpace(mac) && IsUsableDeviceName(name ?? ""))
            {
                names[mac.Trim()] = name!.Trim();
            }
        }

        foreach (var file in EnumerateProfileFiles())
        {
            if (!TryReadGZipText(file, out var json) ||
                (!json.Contains("\"SubProfiles\"", StringComparison.OrdinalIgnoreCase) &&
                 !json.Contains("\"DeviceList\"", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                var root = ParseLConnectJson(json)?.AsObject();
                var profiles = root?["SubProfiles"]?.AsArray();
                if (profiles is null)
                {
                    continue;
                }

                foreach (var profile in profiles.OfType<JsonObject>())
                {
                    Add(profile["MacStr"]?.GetValue<string>(), profile["GroupName"]?.GetValue<string>());
                }

                foreach (var device in root?["DeviceList"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                {
                    Add(device["MacStr"]?.GetValue<string>(), device["Name"]?.GetValue<string>());
                }
            }
            catch
            {
            }
        }

        AddWirelessFanNamesFromDeviceSettings(names);
        return names;
    }

    private static void AddWirelessFanNamesFromDeviceSettings(Dictionary<string, string> names)
    {
        var file = GetLConnectDeviceSettingFile("LWireless-UnBindDevice-Setting", "Fan");
        if (!File.Exists(file) || !TryReadGZipText(file, out var json))
        {
            return;
        }

        try
        {
            var data = ParseLConnectJson(json)?["Data"]?.AsObject();
            if (data is null)
            {
                return;
            }

            foreach (var item in data)
            {
                var name = item.Value?["Name"]?.GetValue<string>();
                if (IsUsableDeviceName(name ?? ""))
                {
                    names[item.Key] = name!.Trim();
                }
            }
        }
        catch
        {
        }
    }

    private static Dictionary<string, Queue<string>> ReadDeviceNamesByModel()
    {
        var names = new Dictionary<string, Queue<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string model, string name)
        {
            if (string.IsNullOrWhiteSpace(model) || !IsUsableDeviceName(name))
            {
                return;
            }

            if (!names.TryGetValue(model, out var queue))
            {
                queue = new Queue<string>();
                names[model] = queue;
            }

            if (!queue.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                queue.Enqueue(name.Trim());
            }
        }

        foreach (var file in EnumerateProfileFiles())
        {
            if (!TryReadGZipText(file, out var json))
            {
                continue;
            }

            try
            {
                var root = ParseLConnectJson(json)?.AsObject();
                if (root is null)
                {
                    continue;
                }

                if (LooksLikeUniversalScreenProfile(root))
                {
                    Add(UniversalScreenDeviceModel, root["Name"]?.GetValue<string>() ?? "");
                }

                foreach (var device in root["DeviceList"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                {
                    var productType = ReadNodeInt(device["ProductType"]) ?? 0;
                    var mac = device["MacStr"]?.GetValue<string>() ?? "";
                    if (productType == 5 || string.Equals(mac, WirelessMergeTarget.HydroShift.PortOrderList[0], StringComparison.OrdinalIgnoreCase))
                    {
                        Add("hydroshift-ii-lcd-s", device["Name"]?.GetValue<string>() ?? "");
                    }
                }
            }
            catch
            {
            }
        }

        AddHydroShiftNamesFromDeviceSettings(names);
        return names;
    }

    private static bool LooksLikeUniversalScreenProfile(JsonObject root) =>
        root.ContainsKey("Name") &&
        (root.ContainsKey("PortraitTemplateConfig") ||
         root.ContainsKey("LandscapeTemplateConfig")) &&
        (root.ContainsKey("LightingSettings") ||
         root.ContainsKey("LightingMode") ||
         root.ContainsKey("Brightness"));

    private static void AddHydroShiftNamesFromDeviceSettings(Dictionary<string, Queue<string>> names)
    {
        var file = GetLConnectDeviceSettingFile("LWireless-UnBindDevice-Setting", "Pump");
        if (!File.Exists(file) || !TryReadGZipText(file, out var json))
        {
            return;
        }

        try
        {
            var data = ParseLConnectJson(json)?["Data"]?.AsObject();
            var hydro = data?[WirelessMergeTarget.HydroShift.PortOrderList[0]]?.AsObject();
            var name = hydro?["Name"]?.GetValue<string>() ?? "";
            if (!IsUsableDeviceName(name))
            {
                return;
            }

            var model = "hydroshift-ii-lcd-s";
            if (!names.TryGetValue(model, out var queue))
            {
                queue = new Queue<string>();
                names[model] = queue;
            }

            if (queue.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            names[model] = new Queue<string>(new[] { name.Trim() }.Concat(queue));
        }
        catch
        {
        }
    }

    private static string TryDequeueDeviceName(Dictionary<string, Queue<string>> names, string model)
    {
        if (names.TryGetValue(model, out var queue) && queue.Count > 0)
        {
            return queue.Dequeue();
        }

        return "";
    }

    private static Dictionary<string, Queue<int>> ReadScreenBrightnessByModel()
    {
        var values = new Dictionary<string, Queue<int>>(StringComparer.OrdinalIgnoreCase);
        void Add(string model, int? value)
        {
            if (string.IsNullOrWhiteSpace(model) || !value.HasValue)
            {
                return;
            }

            if (!values.TryGetValue(model, out var queue))
            {
                queue = new Queue<int>();
                values[model] = queue;
            }

            queue.Enqueue(Math.Clamp(value.Value, 0, 100));
        }

        foreach (var file in EnumerateProfileFiles())
        {
            if (!TryReadGZipText(file, out var json))
            {
                continue;
            }

            try
            {
                var root = ParseLConnectJson(json)?.AsObject();
                if (root is null)
                {
                    continue;
                }

                if (LooksLikeUniversalScreenProfile(root))
                {
                    Add(UniversalScreenDeviceModel, ReadNodeInt(root["Brightness"]));
                }

                if (root.ContainsKey("ScreenBrightness") &&
                    (root.ContainsKey("Fan") || root.ContainsKey("Pump") || root.ContainsKey("CurrentH2EffectsScope")))
                {
                    Add("hydroshift-ii-lcd-s", ReadNodeInt(root["ScreenBrightness"]));
                }
            }
            catch
            {
            }
        }

        AddHydroShiftScreenBrightnessFromDeviceSettings(values);
        return values;
    }

    private static void AddHydroShiftScreenBrightnessFromDeviceSettings(Dictionary<string, Queue<int>> values)
    {
        var candidates = new[]
        {
            GetLConnectDeviceSettingFile("LWireless-Controller", "Pump"),
            GetLConnectDeviceSettingFile("LWireless-UnBindDevice-Setting", "Pump")
        };

        foreach (var file in candidates)
        {
            if (!File.Exists(file) || !TryReadGZipText(file, out var json))
            {
                continue;
            }

            try
            {
                var data = ParseLConnectJson(json)?["Data"]?.AsObject();
                var hydro = data?[WirelessMergeTarget.HydroShift.PortOrderList[0]]?.AsObject();
                var value =
                    ReadNodeInt(hydro?["AioParams"]?["LcdBrightness"]) ??
                    ReadNodeInt(hydro?["LcdBrightness"]) ??
                    ReadNodeInt(hydro?["LCDBrightness"]) ??
                    ReadNodeInt(hydro?["ScreenBrightness"]) ??
                    ReadNodeInt(hydro?["Brightness"]);

                if (!value.HasValue)
                {
                    continue;
                }

                var model = "hydroshift-ii-lcd-s";
                if (!values.TryGetValue(model, out var queue))
                {
                    queue = new Queue<int>();
                }

                values[model] = new Queue<int>(new[] { Math.Clamp(value.Value, 0, 100) }.Concat(queue));
                return;
            }
            catch
            {
            }
        }
    }

    private static int? TryDequeueScreenBrightness(Dictionary<string, Queue<int>> values, string model)
    {
        if (values.TryGetValue(model, out var queue) && queue.Count > 0)
        {
            return queue.Dequeue();
        }

        return null;
    }

    private static bool TryReadGZipText(string file, out string text)
    {
        text = "";
        try
        {
            using var fileStream = File.OpenRead(file);
            using var gzip = new GZipStream(fileStream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            text = reader.ReadToEnd();
            return !string.IsNullOrWhiteSpace(text);
        }
        catch
        {
            return false;
        }
    }

    private static JsonNode? ParseLConnectJson(string json) =>
        JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            MaxDepth = 512,
            AllowTrailingCommas = true
        });

    private static void WriteGZipText(string file, string text)
    {
        var temp = $"{file}.tmp";
        using (var fileStream = File.Create(temp))
        using (var gzip = new GZipStream(fileStream, CompressionLevel.Optimal))
        using (var writer = new StreamWriter(gzip))
        {
            writer.Write(text);
        }

        File.Copy(temp, file, overwrite: true);
        File.Delete(temp);
    }

    private async Task<Dictionary<int, int>> GetUniversalScreenLightingColorCountsAsync(string devicePath, CancellationToken cancellationToken)
    {
        using var client = CreateClient(TimeSpan.FromSeconds(4));
        var result = await lConnectClient.SendDeviceRequestForJsonAsync(client, devicePath, "GetLightingEffectSettings", "{}", cancellationToken);
        if (!result.IsHttpSuccess || string.IsNullOrWhiteSpace(result.Body))
        {
            return new Dictionary<int, int>();
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Body);
            if (!doc.RootElement.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return new Dictionary<int, int>();
            }

            var counts = new Dictionary<int, int>();
            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("Key", out var keyElement) || !keyElement.TryGetInt32(out var mode))
                {
                    continue;
                }

                var colorCount = 0;
                if (item.TryGetProperty("Value", out var value) &&
                    value.ValueKind == JsonValueKind.Object &&
                    value.TryGetProperty("Colors", out var colors) &&
                    colors.ValueKind == JsonValueKind.Array)
                {
                    colorCount = colors.GetArrayLength();
                }

                counts[mode] = Math.Clamp(colorCount, 0, 6);
            }

            return counts;
        }
        catch
        {
            return new Dictionary<int, int>();
        }
    }

    private static string BuildUniversalScreenBrightnessBody(int value)
    {
        var body = new
        {
            Colors = Enumerable.Range(0, 6).Select(index => new
            {
                ColorContext = (object?)null,
                A = 255,
                R = index % 2 == 0 ? 255 : 0,
                G = 0,
                B = 0,
                ScA = 1,
                ScR = index % 2 == 0 ? 1 : 0,
                ScG = 0,
                ScB = 0,
                type = "Color"
            }),
            Brightness = value,
            Speed = 50,
            IsReverseDirection = false,
            type = "UniversalScreen8p8InchLightingSetting"
        };

        return JsonSerializer.Serialize(body).Replace("\"type\"", "\"$type\"");
    }

    private static IReadOnlyList<string> BuildLightingEffectBodies(string effect, int brightness, string color, string[]? requestedColors, int speed, int direction, int? colorCountOverride = null)
    {
        var name = ToEffectDisplayName(effect, UniversalScreenDeviceModel);
        var mode = ToUniversalLightingMode(effect);
        var effectDefinition = EffectsForTarget(UniversalScreenDeviceModel).FirstOrDefault(item => item.Id == effect);
        var colorCount = colorCountOverride ?? (effectDefinition is null ? 6 : ColorCountForTarget(effectDefinition, UniversalScreenDeviceModel));
        object? colors = colorCount <= 0
            ? null
            : BuildEffectColors(effect, color, requestedColors, Math.Clamp(colorCount, 0, 6), fillRequestedColors: true);
        var universalSpeed = ToUniversalSpeed(speed);
        var universalSetting = new
        {
            Colors = colors,
            Brightness = brightness,
            Speed = universalSpeed,
            IsReverseDirection = direction != 0,
            type = "LConnectCore.Products.UniversalScreen8p8Inch.UniversalScreen8p8InchLightingSetting, L-Connect.Core"
        };
        var universalRequest = new
        {
            IgnoreApply = false,
            Mode = mode,
            Setting = universalSetting,
            type = "LConnectCore.Products.UniversalScreen8p8Inch.Requests.UniversalScreen8p8InchSetLightingEffectSettingRequest, L-Connect.Core"
        };
        var universalRequestWithoutType = new
        {
            IgnoreApply = false,
            Mode = mode,
            Setting = new
            {
                Colors = colors,
                Brightness = brightness,
                Speed = universalSpeed,
                IsReverseDirection = direction != 0
            }
        };
        var body = new
        {
            Name = name,
            Effect = name,
            LightingEffect = name,
            LightEffect = name,
            Colors = colors,
            Brightness = brightness,
            Speed = universalSpeed,
            IsReverseDirection = direction != 0,
            type = "UniversalScreen8p8InchLightingSetting"
        };

        return new[]
        {
            JsonSerializer.Serialize(universalRequest).Replace("\"type\"", "\"$type\""),
            JsonSerializer.Serialize(universalRequestWithoutType),
            JsonSerializer.Serialize(body).Replace("\"type\"", "\"$type\""),
            JsonSerializer.Serialize(mode),
            JsonSerializer.Serialize(new { Mode = mode, Brightness = brightness }),
            JsonSerializer.Serialize(new { Effect = name, Brightness = brightness }),
            JsonSerializer.Serialize(new { LightingEffect = name, Brightness = brightness })
        };
    }

    private static string BuildWirelessMergeLightingBody(WirelessMergeTarget target, string effect, int brightness, string color, string[]? requestedColors, int speed, int direction, bool merge = false)
    {
        var targetModel = target.Scope == 7 ? "hydroshift-ii-lcd-s" : merge ? Tlv2MergeFansModel : "tl-wireless-fans";
        var mode = ToWirelessMode(effect, targetModel);
        var wirelessBrightness = ToWirelessBrightness(brightness);
        var effectDefinition = EffectsForTarget(targetModel).FirstOrDefault(item => item.Id == effect);
        var colorCount = effectDefinition is null ? 4 : ColorCountForTarget(effectDefinition, targetModel);
        var colors = BuildEffectColors(effect, color, requestedColors, Math.Clamp(colorCount, 0, 4), fillRequestedColors: false);
        var wirelessSpeed = ToWirelessSpeed(speed);

        var body = new
        {
            PortOrderList = target.PortOrderList,
            DirectionList = target.PortOrderList.Select(_ => Math.Clamp(direction, 0, 1)).ToArray(),
            Scope = target.Scope,
            MergeMode = mode,
            Color = colors,
            Speed = wirelessSpeed,
            Brightness = wirelessBrightness,
            Direction = Math.Clamp(direction, 0, 1)
        };

        return JsonSerializer.Serialize(body).Replace("\"type\"", "\"$type\"");
    }

    private static string BuildWirelessFanLightingBody(WirelessFanGroupInfo group, string effect, int brightness, string color, string[]? requestedColors, int speed, int direction)
    {
        var targetModel = group.DeviceType.Contains("TL Wireless", StringComparison.OrdinalIgnoreCase)
            ? "tl-wireless-fans"
            : WirelessFansModel;
        var mode = ToWirelessMode(effect, targetModel);
        var effectDefinition = EffectsForTarget(targetModel).FirstOrDefault(item => item.Id == effect);
        var colorCount = GetSelectedFanGroupColorCount(group, effect, effectDefinition, targetModel);
        var colors = BuildEffectColors(effect, color, requestedColors, colorCount, fillRequestedColors: true);

        var template = TryReadWirelessFanLightingTemplate(group);
        if (template is not null)
        {
            template["MacStr"] = group.MacStr;
            template["TLV2Mode"] = mode;
            template["Scope"] = 5;
            template["FanCount"] = (byte)Math.Clamp(group.FanCount, 0, byte.MaxValue);
            template["LedNum"] = group.LedCount;
            template["Speed"] = ToWirelessSpeed(speed);
            template["Direction"] = Math.Clamp(direction, 0, 1);
            template["Brightness"] = ToWirelessBrightness(brightness);
            template["Color"] = JsonSerializer.SerializeToNode(colors)?.AsArray();
            return template.ToJsonString();
        }

        var body = new
        {
            MacStr = group.MacStr,
            SLV3Mode = 4,
            SLV4Mode = 1,
            SLINFWMode = 34,
            H2Mode = 1,
            CLMode = 1,
            LC217Mode = 0,
            P28Mode = 0,
            TLV2Mode = mode,
            UpDownSwap = false,
            MergeMode = 4,
            Scope = 5,
            FanCount = (byte)Math.Clamp(group.FanCount, 0, byte.MaxValue),
            LedNum = group.LedCount,
            Speed = ToWirelessSpeed(speed),
            Direction = Math.Clamp(direction, 0, 1),
            Brightness = ToWirelessBrightness(brightness),
            Color = colors
        };

        return JsonSerializer.Serialize(body).Replace("\"type\"", "\"$type\"");
    }

    private static JsonObject? TryReadWirelessFanLightingTemplate(WirelessFanGroupInfo group)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(group.MacStr))
            {
                return null;
            }

            var file = GetLConnectDeviceSettingFile("LWireless-UnBindDevice-Setting", "Fan");
            if (!File.Exists(file) || !TryReadGZipText(file, out var json))
            {
                return null;
            }

            var root = ParseLConnectJson(json)?.AsObject();
            var data = root?["Data"]?.AsObject();
            var fan = data?[group.MacStr]?.AsObject();
            var lightingSetting = fan?["LightingSetting"]?.AsObject();
            var template = lightingSetting?["TLAll"]?.DeepClone().AsObject();
            if (template is null)
            {
                return null;
            }

            AppendDiagnosticLog($"FanLightingSetting template loaded for {group.MacStr}: {file}");
            return template;
        }
        catch (Exception ex)
        {
            AppendDiagnosticLog($"FanLightingSetting template read failed for {group.MacStr}: {ex.Message}");
            return null;
        }
    }

    private static int GetSelectedFanGroupColorCount(WirelessFanGroupInfo group, string effect, LConnectEffect? effectDefinition, string targetModel)
    {
        if (effect == "static" && group.DeviceType.Contains("TL Wireless", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        var colorCount = effectDefinition is null ? 4 : ColorCountForTarget(effectDefinition, targetModel);
        return Math.Clamp(colorCount, 0, 4);
    }

    private static string BuildWirelessFanMergeLightingBody(IReadOnlyList<WirelessFanGroupInfo> groups, string effect, int brightness, string color, string[]? requestedColors, int speed, int direction, bool selectedOnly = false)
    {
        var targetModel = selectedOnly ? "tl-wireless-fans" : Tlv2MergeFansModel;
        var mode = ToWirelessMode(effect, targetModel);
        var effectDefinition = EffectsForTarget(targetModel).FirstOrDefault(item => item.Id == effect);
        var colorCount = effectDefinition is null ? 4 : ColorCountForTarget(effectDefinition, targetModel);
        if (selectedOnly && effect == "static" && groups.Count > 0)
        {
            colorCount = Math.Clamp(groups[0].FanCount, 1, 4);
        }

        var colors = BuildEffectColors(effect, color, requestedColors, Math.Clamp(colorCount, 0, 4), fillRequestedColors: selectedOnly);
        var normalizedDirection = Math.Clamp(direction, 0, 1);
        var sequence = selectedOnly
            ? WirelessMergeSequence.FromGroups(groups, normalizedDirection)
            : ReadTlWirelessMergeSequence(groups, normalizedDirection);

        var body = new
        {
            PortOrderList = sequence.PortOrderList,
            DirectionList = sequence.DirectionList,
            MergeMode = mode,
            Speed = ToWirelessSpeed(speed),
            Direction = normalizedDirection,
            Brightness = ToWirelessBrightness(brightness),
            Color = colors
        };

        return JsonSerializer.Serialize(body).Replace("\"type\"", "\"$type\"");
    }

    private static WirelessMergeSequence ReadTlWirelessMergeSequence(IReadOnlyList<WirelessFanGroupInfo> groups, int fallbackDirection)
    {
        var groupMap = groups
            .Where(group => !string.IsNullOrWhiteSpace(group.MacStr))
            .ToDictionary(group => group.MacStr, StringComparer.OrdinalIgnoreCase);
        if (groupMap.Count == 0)
        {
            return WirelessMergeSequence.FromGroups(groups, fallbackDirection);
        }

        foreach (var file in EnumerateProfileFiles())
        {
            if (!Path.GetFileName(file).Equals(Md5Lower("MergeLightingEffectSetting"), StringComparison.OrdinalIgnoreCase) ||
                !TryReadGZipText(file, out var json) ||
                !json.Contains("\"DeviceList\"", StringComparison.OrdinalIgnoreCase) ||
                !json.Contains("\"MergeDirection\"", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var root = ParseLConnectJson(json)?.AsObject();
                var devices = root?["DeviceList"]?.AsArray();
                if (devices is null)
                {
                    continue;
                }

                var ports = new List<string>();
                var directions = new List<int>();
                foreach (var device in devices.OfType<JsonObject>())
                {
                    var mac = device["MacStr"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(mac) || !groupMap.ContainsKey(mac))
                    {
                        continue;
                    }

                    ports.Add(mac);
                    directions.Add(Math.Clamp(ReadNodeInt(device["MergeDirection"]) ?? fallbackDirection, 0, 1));
                }

                foreach (var group in groups)
                {
                    if (!ports.Contains(group.MacStr, StringComparer.OrdinalIgnoreCase))
                    {
                        ports.Add(group.MacStr);
                        directions.Add(fallbackDirection);
                    }
                }

                if (ports.Count > 0)
                {
                    AppendDiagnosticLog($"TL merge sequence loaded: {string.Join(",", ports)} directions={string.Join(",", directions)}");
                    return new WirelessMergeSequence(ports.ToArray(), directions.ToArray());
                }
            }
            catch (Exception ex)
            {
                AppendDiagnosticLog($"TL merge sequence read failed: {ex.Message}");
            }
        }

        return WirelessMergeSequence.FromGroups(groups, fallbackDirection);
    }

    private WirelessFanGroupInfo? ResolveFanGroup(string groupId) =>
        GetWirelessFanGroups().FirstOrDefault(group => string.Equals(group.Id, groupId, StringComparison.OrdinalIgnoreCase));

    private static object[] BuildEffectColors(string effect, string color, string[]? requestedColors, int count, bool fillRequestedColors)
    {
        var customColors = NormalizeColors(requestedColors, count, fillRequestedColors);
        if (customColors.Length > 0)
        {
            return customColors.Select(hex =>
            {
                var (r, g, b) = ParseHexColor(hex);
                return Rgb(r, g, b);
            }).ToArray();
        }

        var (r, g, b) = ParseHexColor(color);
        object primary = Rgb(r, g, b);
        object black = Rgb(0, 0, 0);
        var palette = effect switch
        {
            "rainbow" => new[] { Rgb(255, 0, 0), Rgb(255, 208, 64), Rgb(68, 214, 111), Rgb(38, 198, 218), Rgb(115, 103, 240), Rgb(255, 88, 143) },
            "color-cycle" => new[] { Rgb(179, 140, 255), Rgb(38, 198, 218), Rgb(123, 214, 111), Rgb(255, 207, 90), Rgb(255, 107, 107), Rgb(179, 140, 255) },
            "breathing" or "meteor" or "runway" => Enumerable.Range(0, count).Select(i => i % 2 == 0 ? primary : black).ToArray(),
            _ => Enumerable.Range(0, count).Select(_ => primary).ToArray()
        };

        if (palette.Length >= count)
        {
            return palette.Take(count).ToArray();
        }

        return palette.Concat(Enumerable.Repeat(primary, count - palette.Length)).ToArray();
    }

    private static string[] NormalizeColors(string[]? colors, int count, bool fill)
    {
        var normalized = (colors ?? Array.Empty<string>())
            .Select(value => NormalizeColor(value, ""))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(count)
            .ToList();

        if (normalized.Count == 0)
        {
            return Array.Empty<string>();
        }

        if (fill)
        {
            var fillColor = normalized[^1];
            while (normalized.Count < count)
            {
                normalized.Add(fillColor);
            }
        }

        return normalized.ToArray();
    }

    private static object Rgb(int r, int g, int b) => new
    {
        ColorContext = (object?)null,
        A = 255,
        R = r,
        G = g,
        B = b,
        ScA = 1,
        ScR = SrgbToScRgb(r),
        ScG = SrgbToScRgb(g),
        ScB = SrgbToScRgb(b)
    };

    private static double SrgbToScRgb(int component)
    {
        var value = Math.Clamp(component, 0, 255) / 255.0;
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static int ToUniversalSpeed(int speed) =>
        SnapPercent(speed);

    private static int ToWirelessSpeed(int speed) =>
        SnapPercent(speed) switch
        {
            <= 0 => 7,
            <= 25 => 6,
            <= 50 => 5,
            <= 75 => 4,
            _ => 3
        };

    private static int SnapPercent(int value) =>
        Math.Clamp((int)Math.Round(Math.Clamp(value, 0, 100) / 25.0) * 25, 0, 100);

    private static readonly LConnectEffect[] LConnectEffects =
    {
        new("rainbow", "Rainbow", "#ff0000", 1, 0, 6, true, true, true),
        new("rainbow-morph", "Rainbow Morph", "#ffcc00", 2, 4, 6, true, true, true),
        new("static", "Static Color", "#00c7be", 3, 2, 1, true, true, true),
        new("breathing", "Breathing", "#34c759", 4, 3, 2, true, true, true),
        new("runway", "Runway", "#ffcc00", 5, 6, 2, true, true, true),
        new("meteor", "Meteor", "#ff0000", 6, 9, 2, true, true, true),
        new("taichi", "Taichi", "#00c7be", 7, 9, 2, false, false, true),
        new("staggered", "Staggered", "#ff9500", 8, 9, 2, true, false, false),
        new("stack", "Stack", "#ffcc00", 7, 13, 2, true, true, false),
        new("twinkle", "Twinkle", "#ffffff", 8, 14, 6, true, true, true),
        new("voice", "Voice", "#007aff", 9, 9, 1, false, false, true),
        new("color-cycle", "Color Cycle", "#af52de", 9, 11, 6, true, false, true),
        new("cover-cycle", "Cover Cycle", "#00c7ff", 10, 11, 6, true, false, false),
        new("wave", "Wave", "#00c7be", 11, 1, 6, true, true, true),
        new("meteor-shower", "Meteor Shower", "#ff9500", 12, 9, 2, true, false, false),
        new("tide", "Tide", "#00c7be", 13, 7, 2, true, true, true),
        new("electric-current", "Electric Current", "#007aff", 14, 17, 2, true, true, true),
        new("mop-up", "Mop Up", "#34c759", 15, 6, 6, true, false, false),
        new("render", "Render", "#34c759", 13, 11, 2, true, false, false),
        new("ripple", "Ripple", "#00c7ff", 14, 12, 2, true, false, true),
        new("reflect", "Reflect", "#007aff", 15, 15, 2, true, false, false),
        new("tail-chasing", "Tail Chasing", "#ffcc00", 16, 6, 1, true, false, false),
        new("disco", "Disco", "#ff2d55", 16, 18, 6, true, false, false),
        new("mixing", "Mixing", "#ff9500", 17, 11, 6, true, true, true),
        new("paint", "Paint", "#ffcc00", 18, 5, 6, true, true, false),
        new("snooker", "Snooker", "#34c759", 19, 10, 6, true, true, false),
        new("volume", "Volume", "#007aff", 20, 12, 6, true, false, false),
        new("blow-up", "Blow Up", "#ff0000", 21, 8, 6, true, true, false),
        new("warning", "Warning", "#ff0000", 22, 16, 2, true, false, true),
        new("hourglass", "Hourglass", "#ffcc00", 23, 16, 2, true, true, false),
        new("caterpillar", "Caterpillar", "#34c759", 24, 6, 6, true, false, false),
        new("lollipop", "Lollipop", "#ff2d55", 25, 18, 6, true, false, false),
        new("racing", "Racing", "#ff3b30", 22, 9, 1, true, false, false),
        new("lottery", "Lottery", "#af52de", 23, 9, 0, true, false, false),
        new("intertwine", "Intertwine", "#00c7be", 24, 9, 2, true, false, false),
        new("echo", "Echo", "#007aff", 26, 15, 2, true, false, true),
        new("heartbeat", "Heartbeat", "#ff2d55", 27, 3, 2, true, false, true),
        new("collide", "Collide", "#ff9500", 26, 9, 2, true, false, false),
        new("kaleidoscope", "Kaleidoscope", "#af52de", 28, 9, 4, true, false, false),
        new("sea-flow", "Sea Flow", "#00c7be", 29, 15, 6, true, false, false),
        new("door", "Door", "#64d2ff", 12, 9, 2, true, false, false),
        new("ping-pong", "Ping Pong", "#5856d6", 12, 12, 2, false, true, false),
        new("river", "River", "#00c7be", 15, 15, 6, false, true, false),
        new("rainbow-wave", "Rainbow Wave", "#ff0000", 1, 18, 6, false, true, false),
        new("pump", "Pump", "#34c759", 17, 9, 1, false, false, true),
        new("bounce", "Bounce", "#ff9500", 16, 9, 2, false, false, true)
    };

    private static IEnumerable<LConnectEffect> EffectsForTarget(string targetModel)
    {
        if (targetModel.Equals(UniversalScreenDeviceModel, StringComparison.OrdinalIgnoreCase))
        {
            return ReadEnumEffects(
                "LConnectCore.Products.UniversalScreen8p8Inch.UniversalScreen8p8InchLightingMode",
                effectName => effectName is not "None",
                (id, name, value) => MergeEffectDefinition(id, name, universalMode: value, supportsUniversal: true))
                .DefaultIfEmpty()
                .Where(effect => effect is not null)
                .Cast<LConnectEffect>()
                .DefaultIfEmpty()
                .Where(effect => effect is not null)
                .Cast<LConnectEffect>();
        }

        if (targetModel.Equals("hydroshift-ii-lcd-s", StringComparison.OrdinalIgnoreCase))
        {
            var hydroEffects = ReadEnumEffects(
                "slv3.models.H2Effects",
                effectName => effectName is not "None",
                (id, name, value) => MergeEffectDefinition(id, name, wirelessMode: value, supportsHydro: true))
                .ToList();

            return hydroEffects.Count > 0
                ? hydroEffects
                : LConnectEffects.Where(effect => effect.SupportsHydro);
        }

        if (targetModel.Equals("tl-wireless-fans", StringComparison.OrdinalIgnoreCase))
        {
            var tlEffects = ReadEnumEffects(
                "slv3.models.TLEffects",
                effectName => effectName is not "None",
                (id, name, value) => MergeEffectDefinition(id, name, wirelessMode: value, supportsWirelessFan: true))
                .ToList();

            return tlEffects.Count > 0
                ? tlEffects
                : LConnectEffects.Where(effect => effect.SupportsWirelessFan);
        }

        if (targetModel.Equals(Tlv2MergeFansModel, StringComparison.OrdinalIgnoreCase))
        {
            var profileEffects = ReadProfileLightingEffects("TLV2CurrentMergeLightSetting").ToArray();
            if (profileEffects.Length > 0)
            {
                return profileEffects;
            }

            var effects = ReadEnumEffects(
                "slv3.models.UIEffects",
                effectName => effectName is not "None",
                (id, name, value) => MergeEffectDefinition(id, name, wirelessMode: value, supportsWirelessFan: true))
                .ToDictionary(effect => effect.WirelessMode, effect => effect);

            var sourceOrder = WirelessMergeEffectOrder.Value.Count > 0
                ? WirelessMergeEffectOrder.Value
                : Tlv2MergeEffectOrder.Value;
            var order = sourceOrder.Count == 11 && sourceOrder.Contains(6) && sourceOrder.Contains(12)
                ? sourceOrder
                : sourceOrder.Count > 0
                    ? sourceOrder
                    : new[] { 1, 2, 3, 4, 5, 6, 9, 10, 11, 12, 8 };

            var ordered = order
                .Select(mode => effects.TryGetValue(mode, out var effect) ? effect : null)
                .Where(effect => effect is not null)
                .Cast<LConnectEffect>()
                .ToArray();

            return ordered.Length > 0
                ? ordered
                : LConnectEffects.Where(effect => WirelessMergeEffectMetadata.Value.ContainsKey(effect.WirelessMode));
        }

        return LConnectEffects.Where(effect => effect.SupportsWirelessFan);
    }

    private static IEnumerable<LConnectEffect> ReadEnumEffects(
        string enumTypeName,
        Func<string, bool> include,
        Func<string, string, int, LConnectEffect> create)
    {
        var enumType = TryGetLConnectCoreType(enumTypeName);
        if (enumType is null || !enumType.IsEnum)
        {
            return Array.Empty<LConnectEffect>();
        }

        var effects = new List<LConnectEffect>();
        foreach (var enumName in Enum.GetNames(enumType))
        {
            if (!include(enumName))
            {
                continue;
            }

            var displayName = ToEffectDisplayNameFromEnum(enumName);
            var id = NormalizeEffect(displayName);
            if (string.IsNullOrWhiteSpace(id))
            {
                id = ToKebabCase(enumName);
            }

            var value = Convert.ToInt32(Enum.Parse(enumType, enumName));
            effects.Add(create(id, displayName, value));
        }

        return effects;
    }

    private static IEnumerable<LConnectEffect> ReadProfileLightingEffects(string settingPropertyName)
    {
        foreach (var file in EnumerateProfileFiles())
        {
            if (!TryReadGZipText(file, out var json) ||
                !json.Contains($"\"{settingPropertyName}\"", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (settingPropertyName.Equals("TLV2CurrentMergeLightSetting", StringComparison.OrdinalIgnoreCase) &&
                !json.Contains("\"IsTLV2Merged\"", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var root = ParseLConnectJson(json)?.AsObject();
                var settings = root?[settingPropertyName]?.AsObject();
                if (settings is null)
                {
                    continue;
                }

                var effects = new List<LConnectEffect>();
                foreach (var item in settings)
                {
                    if (item.Value is not JsonObject setting ||
                        !TryReadNodeInt(setting["Mode"], out var mode))
                    {
                        continue;
                    }

                    var displayName = ToEffectDisplayNameFromEnum(item.Key);
                    var id = NormalizeEffect(displayName);
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        id = ToKebabCase(item.Key);
                    }

                    var colorCount = setting["Colors"] is JsonArray colors ? colors.Count : 0;
                    effects.Add(MergeEffectDefinition(
                        id,
                        displayName,
                        wirelessMode: mode,
                        colorCount: colorCount,
                        supportsWirelessFan: true));
                }

                if (effects.Count > 0)
                {
                    return effects;
                }
            }
            catch
            {
            }
        }

        return Array.Empty<LConnectEffect>();
    }

    private static Type? TryGetLConnectCoreType(string typeName)
    {
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var loadedType = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
                if (loadedType is not null)
                {
                    return loadedType;
                }
            }

            var preferredDlls = new[] { "L-Connect.Core.dll", "slv3.models.dll" };
            foreach (var dllName in preferredDlls)
            {
                var path = Path.Combine(LConnectPaths.ProgramFilesRoot, dllName);
                if (!File.Exists(path))
                {
                    continue;
                }

                var type = Assembly.LoadFrom(path).GetType(typeName, throwOnError: false, ignoreCase: false);
                if (type is not null)
                {
                    return type;
                }
            }

        }
        catch
        {
        }

        return null;
    }

    private static LConnectEffect MergeEffectDefinition(
        string id,
        string name,
        int? wirelessMode = null,
        int? universalMode = null,
        int? colorCount = null,
        bool supportsWirelessFan = false,
        bool supportsUniversal = false,
        bool supportsHydro = false)
    {
        var existing = LConnectEffects.FirstOrDefault(effect => effect.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return existing is null
            ? new LConnectEffect(
                id,
                name,
                GetGeneratedAccent(id),
                wirelessMode ?? 6,
                universalMode ?? 9,
                colorCount ?? DefaultColorCount(id),
                supportsWirelessFan,
                supportsUniversal,
                supportsHydro)
            : existing with
            {
                Name = name,
                WirelessMode = wirelessMode ?? existing.WirelessMode,
                UniversalMode = universalMode ?? existing.UniversalMode,
                ColorCount = colorCount ?? existing.ColorCount,
                SupportsWirelessFan = supportsWirelessFan || existing.SupportsWirelessFan,
                SupportsUniversal = supportsUniversal || existing.SupportsUniversal,
                SupportsHydro = supportsHydro || existing.SupportsHydro
            };
    }

    private static void AddHydroCompatibilityEffect(List<LConnectEffect> effects, string enumName)
    {
        var displayName = ToEffectDisplayNameFromEnum(enumName);
        var id = NormalizeEffect(displayName);
        if (string.IsNullOrWhiteSpace(id) || effects.Any(effect => effect.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var source = TryGetLConnectCoreType("LConnectCore.Products.Galahad2Trinity.PumpLightingMode")
            ?? TryGetLConnectCoreType("LConnectCore.Products.HydroShiftLCD.FanLightingMode");
        if (source is null || !source.IsEnum)
        {
            return;
        }

        var match = Enum.GetNames(source).FirstOrDefault(name =>
            name.Equals(enumName, StringComparison.OrdinalIgnoreCase) ||
            NormalizeEffect(ToEffectDisplayNameFromEnum(name)).Equals(id, StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(enumName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return;
        }

        var value = Convert.ToInt32(Enum.Parse(source, match));
        effects.Add(MergeEffectDefinition(id, displayName, wirelessMode: value, supportsHydro: true));
    }

    private static int ColorCountForTarget(LConnectEffect effect, string targetModel)
    {
        if (targetModel.Equals(UniversalScreenDeviceModel, StringComparison.OrdinalIgnoreCase))
        {
            return effect.Id switch
            {
                "rainbow" or "rainbow-morph" or "stack" or "twinkle" or "rainbow-wave" => 0,
                "static" or "breathing" => 1,
                "runway" or "mixing" or "river" => 2,
                "hourglass" or "electric-current" => 4,
                "wave" or "paint" or "tide" or "blow-up" or "meteor" or "snooker" or "ping-pong" => 6,
                _ => Math.Min(effect.ColorCount, 6)
            };
        }

        if (targetModel.Equals("hydroshift-ii-lcd-s", StringComparison.OrdinalIgnoreCase))
        {
            if (WirelessHydroEffectMetadata.Value.TryGetValue(effect.WirelessMode, out var metadata))
            {
                return metadata.ColorCount;
            }

            return effect.Id switch
            {
                "rainbow" or "rainbow-morph" or "twinkle" => 0,
                "static" or "breathing" or "voice" or "pump" => 1,
                "runway" => 2,
                "taichi" => 2,
                "bounce" => 4,
                "meteor" => 4,
                _ => Math.Min(effect.ColorCount, 4)
            };
        }

        if (targetModel.Equals("tl-wireless-fans", StringComparison.OrdinalIgnoreCase))
        {
            if (Tlv2EffectMetadata.Value.TryGetValue(effect.WirelessMode, out var metadata))
            {
                return metadata.ColorCount;
            }

            return effect.Id switch
            {
                "rainbow" or "rainbow-morph" or "voice" or "kaleidoscope" or "twinkle" => 0,
                "runway" or "staggered" or "tide" or "mixing" or "tail-chasing" or "racing" or "lottery" or "intertwine" => 2,
                "color-cycle" or "cover-cycle" => 3,
                "static" or "breathing" or "meteor" or "door" or "render" or "ripple" or "reflect" or "paint" or "ping-pong" or "stack" or "wave" or "meteor-shower" or "collide" or "electric-current" => 4,
                _ => Math.Min(effect.ColorCount, 4)
            };
        }

        if (targetModel.Equals(Tlv2MergeFansModel, StringComparison.OrdinalIgnoreCase))
        {
            var profileColorCounts = ReadProfileLightingEffects("TLV2CurrentMergeLightSetting")
                .GroupBy(item => item.WirelessMode)
                .ToDictionary(group => group.Key, group => group.First().ColorCount);
            if (profileColorCounts.TryGetValue(effect.WirelessMode, out var profileColorCount))
            {
                return profileColorCount;
            }

            if (WirelessMergeEffectMetadata.Value.TryGetValue(effect.WirelessMode, out var metadata))
            {
                return metadata.ColorCount;
            }

            return effect.Id switch
            {
                "rainbow" or "rainbow-morph" or "twinkle" => 0,
                "static" or "breathing" or "meteor" or "stack" or "wave" => 1,
                "runway" or "cover-cycle" => 2,
                "color-cycle" => 3,
                "meteor-shower" => 4,
                _ => Math.Min(effect.ColorCount, 4)
            };
        }

        if (targetModel.Equals(WirelessFansModel, StringComparison.OrdinalIgnoreCase))
        {
            return effect.Id switch
            {
                "rainbow" or "rainbow-morph" or "color-cycle" or "cover-cycle" => 0,
                "static" or "breathing" or "runway" or "meteor" or "meteor-shower" or "warning" or "echo" or "heartbeat" or "volume" => 1,
                "stack" or "tide" or "electric-current" or "hourglass" or "ripple" => 2,
                _ => Math.Min(effect.ColorCount, 4)
            };
        }

        return effect.ColorCount;
    }

    private static string NormalizeEffect(string effect) =>
        (effect ?? "").Trim().ToLowerInvariant() switch
        {
            "static" or "static-color" or "static color" => "static",
            "breathing" or "breath" => "breathing",
            "rainbow" => "rainbow",
            "rainbow-morph" or "rainbow morph" => "rainbow-morph",
            "runway" => "runway",
            "meteor" => "meteor",
            "staggered" => "staggered",
            "stack" or "bullet stack" => "stack",
            "color-cycle" or "color cycle" or "cycle" => "color-cycle",
            "cover-cycle" or "cover cycle" => "cover-cycle",
            "wave" => "wave",
            "meteor-shower" or "meteor shower" => "meteor-shower",
            "twinkle" => "twinkle",
            "tide" => "tide",
            "electric-current" or "electric current" => "electric-current",
            "mop-up" or "mop up" => "mop-up",
            "render" => "render",
            "reflect" => "reflect",
            "tail-chasing" or "tail chasing" => "tail-chasing",
            "disco" => "disco",
            "mixing" => "mixing",
            "paint" => "paint",
            "snooker" => "snooker",
            "volume" => "volume",
            "blow-up" or "blow up" => "blow-up",
            "door" => "door",
            "racing" => "racing",
            "lottery" => "lottery",
            "intertwine" => "intertwine",
            "collide" => "collide",
            "kaleidoscope" => "kaleidoscope",
            "warning" or "warnning" => "warning",
            "hourglass" => "hourglass",
            "caterpillar" => "caterpillar",
            "lollipop" => "lollipop",
            "echo" => "echo",
            "heartbeat" => "heartbeat",
            "sea-flow" or "sea flow" => "sea-flow",
            "ripple" => "ripple",
            "ping-pong" or "ping pong" => "ping-pong",
            "bullet-stack" or "bullet stack" => "stack",
            "river" => "river",
            "rainbow-wave" or "rainbow wave" => "rainbow-wave",
            "taichi" or "tai-chi" or "tai chi" => "taichi",
            "voice" => "voice",
            "pump" => "pump",
            "bounce" => "bounce",
            _ => ""
        };

    private static string ToEffectDisplayNameFromEnum(string enumName) =>
        enumName switch
        {
            "StaticColor" => "Static Color",
            "RainbowMorph" => "Rainbow Morph",
            "BlowUp" => "Blow Up",
            "PingPong" => "Ping-Pong",
            "BulletStack" => "Bullet Stack",
            "ElectricCurrent" => "Electric Current",
            "RainbowWave" => "RainbowWave",
            "TailChasing" => "Tail Chasing",
            "MeteorShower" => "Meteor Shower",
            "ColorCycle" => "Color Cycle",
            "CoverCycle" => "Cover Cycle",
            "Taichi" => "Taichi",
            _ => Regex.Replace(enumName, "([a-z])([A-Z])", "$1 $2").Replace("_", " ")
        };

    private static string ToKebabCase(string value) =>
        Regex.Replace(value, "([a-z])([A-Z])", "$1-$2").Replace("_", "-").ToLowerInvariant();

    private static int DefaultColorCount(string id) =>
        id switch
        {
            "rainbow" or "rainbow-morph" => 0,
            "static" or "voice" or "pump" => 1,
            "breathing" or "runway" or "meteor" or "tide" or "electric-current" or "bounce" => 2,
            _ => 6
        };

    private static string GetGeneratedAccent(string id)
    {
        var palette = new[] { "#ff3b30", "#ffcc00", "#34c759", "#00c7be", "#007aff", "#af52de", "#ff2d55" };
        return palette[Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(id)) % palette.Length];
    }

    private static string ToEffectDisplayName(string effect, string targetModel = "") =>
        (!string.IsNullOrWhiteSpace(targetModel)
            ? EffectsForTarget(targetModel).FirstOrDefault(item => item.Id == effect)?.Name
            : null) ??
        LConnectEffects.FirstOrDefault(item => item.Id == effect)?.Name ??
        "Meteor";

    private static int ToUniversalLightingMode(string effect) =>
        EffectsForTarget(UniversalScreenDeviceModel).FirstOrDefault(item => item.Id == effect)?.UniversalMode ??
        LConnectEffects.FirstOrDefault(item => item.Id == effect)?.UniversalMode ??
        9;

    private static int ToWirelessMode(string effect, string targetModel = "") =>
        (!string.IsNullOrWhiteSpace(targetModel)
            ? EffectsForTarget(targetModel).FirstOrDefault(item => item.Id == effect)?.WirelessMode
            : null) ??
        LConnectEffects.FirstOrDefault(item => item.Id == effect)?.WirelessMode ??
        6;

    private static string GetEffectAccent(string effect) =>
        LConnectEffects.FirstOrDefault(item => item.Id == effect)?.Accent ?? "#ff6b6b";

    private static int ToWirelessBrightness(int brightness) => brightness switch
    {
        <= 10 => 0,
        <= 35 => 64,
        <= 60 => 128,
        <= 85 => 192,
        _ => 255
    };

    private static int FromWirelessBrightness(int brightness) => brightness switch
    {
        <= 10 => 0,
        <= 96 => 25,
        <= 160 => 50,
        <= 224 => 75,
        _ => 100
    };

    private static int FromWirelessSpeed(int speed) => speed switch
    {
        >= 7 => 0,
        6 => 25,
        5 => 50,
        4 => 75,
        _ => 100
    };

    private static string NormalizeColor(string? color, string fallback) =>
        Regex.IsMatch(color ?? "", "^#[0-9a-fA-F]{6}$") ? color! : fallback;

    private static (int R, int G, int B) ParseHexColor(string color)
    {
        var text = NormalizeColor(color, "#26c6da").TrimStart('#');
        return (
            Convert.ToInt32(text[..2], 16),
            Convert.ToInt32(text.Substring(2, 2), 16),
            Convert.ToInt32(text.Substring(4, 2), 16));
    }

    private sealed record LConnectEffect(
        string Id,
        string Name,
        string Accent,
        int WirelessMode,
        int UniversalMode,
        int ColorCount,
        bool SupportsWirelessFan,
        bool SupportsUniversal,
        bool SupportsHydro);

    private sealed record WirelessMergeTarget(string[] PortOrderList, int Scope)
    {
        public static WirelessMergeTarget HydroShift { get; } = new(new[] { "1e:5e:63:62:32:e1" }, 7);

        public static WirelessMergeTarget FanGroup(WirelessFanGroupInfo group) => new(new[] { group.MacStr }, 2);
    }

    private sealed record WirelessMergeSequence(string[] PortOrderList, int[] DirectionList)
    {
        public static WirelessMergeSequence FromGroups(IReadOnlyList<WirelessFanGroupInfo> groups, int direction) =>
            new(
                groups.Select(group => group.MacStr).ToArray(),
                groups.Select(_ => Math.Clamp(direction, 0, 1)).ToArray());
    }

    private sealed record ControllerDiscoveryInfo(string Path, string Name);

    private static List<ThemeInfo> ParseThemes(string json, string deviceId, string selectedId)
    {
        var result = new List<ThemeInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(json);
            ParseThemeElement(doc.RootElement, deviceId, selectedId, result, seen);
        }
        catch
        {
            return result;
        }

        return result.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ParseThemeElement(JsonElement element, string deviceId, string selectedId, List<ThemeInfo> result, HashSet<string> seen)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var id = FirstNonEmpty(
                GetJsonString(element, "Id"),
                GetJsonString(element, "id"),
                GetJsonString(element, "TemplateId"),
                GetJsonString(element, "templateId"));
            if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
            {
                var name = FirstNonEmpty(
                    GetJsonString(element, "Name"),
                    GetJsonString(element, "name"),
                    GetJsonString(element, "Title"),
                    GetJsonString(element, "title"),
                    id);
                var canDelete = TryGetJsonBool(
                    element,
                    out var deleteValue,
                    "Uninstallable", "uninstallable", "CanUninstall", "canUninstall",
                    "CanDelete", "canDelete", "Deletable", "deletable") && deleteValue;
                result.Add(new ThemeInfo(
                    id,
                    name,
                    $"/api/devices/{Uri.EscapeDataString(deviceId)}/themes/{Uri.EscapeDataString(id)}/preview",
                    canDelete,
                    string.Equals(id, selectedId, StringComparison.OrdinalIgnoreCase)));
            }

            foreach (var property in element.EnumerateObject())
            {
                ParseThemeElement(property.Value, deviceId, selectedId, result, seen);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ParseThemeElement(item, deviceId, selectedId, result, seen);
            }
        }
    }

    private static IReadOnlyList<ThemeInfo> ReadThemesFromDisk(string deviceModel, string deviceId, string selectedId)
    {
        var templateRoot = Path.Combine(LConnectPaths.ProgramDataRoot, deviceModel, "template");
        if (!Directory.Exists(templateRoot))
        {
            return Array.Empty<ThemeInfo>();
        }

        return Directory.EnumerateFiles(templateRoot, "*.template")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Select(id => new ThemeInfo(
                id,
                id,
                $"/api/devices/{Uri.EscapeDataString(deviceId)}/themes/{Uri.EscapeDataString(id)}/preview",
                false,
                string.Equals(id, selectedId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static IEnumerable<ControllerDiscoveryInfo> EnumerateControllers(JsonElement element) =>
        EnumerateControllers(element, "");

    private static IEnumerable<ControllerDiscoveryInfo> EnumerateControllers(JsonElement element, string inheritedName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var objectName = FirstNonEmpty(ReadControllerName(element), inheritedName);
            foreach (var property in element.EnumerateObject())
            {
                if (LooksLikeControllerPath(property.Name))
                {
                    yield return new ControllerDiscoveryInfo(
                        property.Name,
                        FirstNonEmpty(ReadControllerName(property.Value), objectName));
                }

                foreach (var nested in EnumerateControllers(property.Value, objectName))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in EnumerateControllers(item, inheritedName))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString() ?? "";
            if (LooksLikeControllerPath(value))
            {
                yield return new ControllerDiscoveryInfo(value, inheritedName);
            }
        }
    }

    private static string ReadControllerName(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        foreach (var name in new[]
        {
            "DeviceName",
            "DisplayName",
            "NickName",
            "Nickname",
            "Alias",
            "ProductName",
            "ModelName",
            "Name",
            "Title"
        })
        {
            var value = GetJsonStringIgnoreCase(element, name);
            if (IsUsableDeviceName(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static string GetJsonStringIgnoreCase(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? "",
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.ToString(),
                _ => ""
            };
        }

        return "";
    }

    private static bool IsUsableDeviceName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !LooksLikeControllerPath(value) &&
        !value.Contains("vid_", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains("pid_", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeControllerPath(string value) =>
        value.Contains("vid_", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("pid_", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("usb", StringComparison.OrdinalIgnoreCase);

    private static string InferDeviceModel(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith("dummy-", StringComparison.OrdinalIgnoreCase) ||
            IsWirelessTransmitterControllerPath(path))
        {
            return "";
        }

        if (path.Contains("vm", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("9.2", StringComparison.OrdinalIgnoreCase))
        {
            return Vm92DeviceModel;
        }

        if (IsUniversal88TemplateControllerPath(path) ||
            path.Contains("universal", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("us88", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("8.8", StringComparison.OrdinalIgnoreCase))
        {
            return UniversalScreenDeviceModel;
        }

        if (path.Contains("vid_1cbe", StringComparison.OrdinalIgnoreCase) &&
            path.Contains("pid_a034", StringComparison.OrdinalIgnoreCase))
        {
            return "hydroshift-ii-lcd-s";
        }

        return "";
    }

    private static bool IsUniversal88TemplateControllerPath(string path) =>
        path.Contains("vid_1cbe", StringComparison.OrdinalIgnoreCase) &&
        path.Contains("pid_a088", StringComparison.OrdinalIgnoreCase);

    private static bool IsWirelessTransmitterControllerPath(string path) =>
        path.Contains("vid_0416", StringComparison.OrdinalIgnoreCase) &&
        path.Contains("pid_8040", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeControllerPathForCompare(string path) =>
        (path ?? string.Empty).Replace("\\\\", "\\").Trim();

    private static string BuildUniqueDeviceName(string requestedName, IReadOnlyList<DeviceInfo> existingDevices)
    {
        var name = string.IsNullOrWhiteSpace(requestedName) ? "LCD Device" : requestedName.Trim();
        if (!existingDevices.Any(device => string.Equals(device.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return name;
        }

        var index = 2;
        string candidate;
        do
        {
            candidate = $"{name} #{index++}";
        }
        while (existingDevices.Any(device => string.Equals(device.Name, candidate, StringComparison.OrdinalIgnoreCase)));

        return candidate;
    }

    private static string BuildDeviceName(string model, int instance) => model switch
    {
        "hydroshift-ii-lcd-s" => instance > 1 ? $"HydroShift II LCD-S #{instance}" : "HydroShift II LCD-S",
        "hydroshift-ii-lcd-c" => instance > 1 ? $"HydroShift II LCD-C #{instance}" : "HydroShift II LCD-C",
        UniversalScreenDeviceModel => instance > 1 ? $"8.8\" Universal Screen #{instance}" : "8.8\" Universal Screen",
        Vm92DeviceModel => instance > 1 ? $"VM 9.2 LCD #{instance}" : "VM 9.2 LCD",
        _ => instance > 1 ? $"LCD Device #{instance}" : "LCD Device"
    };

    private static bool IsSuccessfulLConnectResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (root.ValueKind == JsonValueKind.String)
            {
                var text = root.GetString();
                return text is "OK" or "Success" or "true" or "True";
            }

            if (TryReadSuccess(root, out var successResult, "Success", "success", "Data", "data", "Result", "result", "Status", "status"))
            {
                return successResult;
            }

            if (TryReadNumber(root, out var code, "Code", "code", "StatusCode", "statusCode"))
            {
                return code == 0;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool TryReadSuccess(JsonElement element, out bool value, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.True)
            {
                value = true;
                return true;
            }

            if (property.ValueKind == JsonValueKind.False)
            {
                value = false;
                return true;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                var text = property.GetString();
                if (bool.TryParse(text, out value))
                {
                    return true;
                }

                if (text is "OK" or "Success")
                {
                    value = true;
                    return true;
                }
            }
        }

        value = false;
        return false;
    }

    private static bool TryReadNumber(JsonElement element, out int value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property) && TryReadJsonInt(property, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static int GetJsonInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property) && TryReadJsonInt(property, out var value))
            {
                return value;
            }
        }

        return 0;
    }

    private static int ReadFirstInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return 0;
        }

        if (TryReadJsonInt(property, out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in property.EnumerateArray())
            {
                if (TryReadJsonInt(item, out value))
                {
                    return value;
                }
            }
        }

        if (property.ValueKind == JsonValueKind.Object &&
            property.TryGetProperty("$values", out var values) &&
            values.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in values.EnumerateArray())
            {
                if (TryReadJsonInt(item, out value))
                {
                    return value;
                }
            }
        }

        return 0;
    }

    private static bool TryReadJsonInt(JsonElement property, out int value)
    {
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryReadNodeInt(JsonNode? node, out int value)
    {
        value = 0;
        if (node is null)
        {
            return false;
        }

        try
        {
            value = node.GetValue<int>();
            return true;
        }
        catch
        {
            return int.TryParse(node.ToString(), out value);
        }
    }

    private static int? ReadNodeInt(JsonNode? node) =>
        TryReadNodeInt(node, out var value) ? value : null;

    private static string[] ReadColorHexArray(JsonNode? node)
    {
        if (node is not JsonArray colors)
        {
            return Array.Empty<string>();
        }

        return colors
            .OfType<JsonObject>()
            .Select(ColorNodeToHex)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static string ColorNodeToHex(JsonObject color)
    {
        var r = Math.Clamp(ReadNodeInt(color["R"]) ?? 0, 0, 255);
        var g = Math.Clamp(ReadNodeInt(color["G"]) ?? 0, 0, 255);
        var b = Math.Clamp(ReadNodeInt(color["B"]) ?? 0, 0, 255);
        return $"#{r:x2}{g:x2}{b:x2}";
    }

    private static string GetJsonString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? "";
            }

            if (property.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                return property.ToString();
            }
        }

        return "";
    }

    private static bool TryGetJsonBool(JsonElement element, out bool value, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = property.GetBoolean();
                return true;
            }

            if (property.ValueKind == JsonValueKind.String &&
                bool.TryParse(property.GetString(), out value))
            {
                return true;
            }

            if (property.ValueKind == JsonValueKind.Number &&
                property.TryGetInt32(out var number))
            {
                value = number != 0;
                return true;
            }
        }

        value = false;
        return false;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static HttpClient CreateClient(TimeSpan timeout) => new() { Timeout = timeout };
}
