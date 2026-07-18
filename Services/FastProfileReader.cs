using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ThemeEditorCSharp.Services;

public static class FastProfileReader
{
    public static string GetActiveTemplateId(string deviceModel)
    {
        try
        {
            var profileDir = Path.Combine(LConnectPaths.ProgramDataRoot, "profile");
            if (!Directory.Exists(profileDir)) return "";

            var latestLogId = GetLatestAppliedTemplateIdFromLogs(deviceModel);
            if (TemplateExistsForActiveId(deviceModel, latestLogId))
            {
                return latestLogId;
            }

            foreach (var file in Directory.GetFiles(profileDir).OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    var json = ReadFileContent(file);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (IsUniversalDevice(deviceModel))
                    {
                        foreach (var config in EnumerateUniversalTemplateConfigs(root))
                        {
                            if (config.TryGetProperty("SelectedTemplateId", out var selected) &&
                                !string.IsNullOrWhiteSpace(selected.GetString()))
                            {
                                return selected.GetString()!;
                            }
                        }
                    }

                    if (root.TryGetProperty("SelectedTemplateId", out var selectedId) &&
                        !string.IsNullOrWhiteSpace(selectedId.GetString()))
                    {
                        return selectedId.GetString()!;
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return "";
    }

    public static string GetActiveTemplateBackground(string deviceModel, string templateId)
    {
        try
        {
            var latestLogBackground = GetLatestAppliedTemplateBackgroundFromLogs(deviceModel, templateId);
            if (!string.IsNullOrWhiteSpace(latestLogBackground))
            {
                return latestLogBackground;
            }

            var profileDir = Path.Combine(LConnectPaths.ProgramDataRoot, "profile");
            if (!Directory.Exists(profileDir)) return "";

            foreach (var file in Directory.GetFiles(profileDir).OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    var json = ReadFileContent(file);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (IsUniversalDevice(deviceModel))
                    {
                        foreach (var config in EnumerateUniversalTemplateConfigs(root))
                        {
                            if (config.TryGetProperty("SelectedTemplateId", out var selected) &&
                                string.Equals(selected.GetString(), templateId, StringComparison.OrdinalIgnoreCase) &&
                                TryGetCustomThemeBackground(config, deviceModel, out var background))
                            {
                                return background;
                            }
                        }
                    }

                    if (root.TryGetProperty("TemplateCustomBackgrounds", out var backgrounds) &&
                        backgrounds.ValueKind == JsonValueKind.Object &&
                        backgrounds.TryGetProperty(templateId, out var mapped) &&
                        !string.IsNullOrWhiteSpace(mapped.GetString()) &&
                        BackgroundMatches(mapped.GetString(), deviceModel))
                    {
                        return mapped.GetString()!;
                    }

                    if (root.TryGetProperty("SelectedTemplateId", out var selectedId) &&
                        selectedId.GetString() == templateId &&
                        root.TryGetProperty("CustomTheme", out var customTheme) &&
                        customTheme.ValueKind == JsonValueKind.Object &&
                        TryGetCustomThemeBackground(root, deviceModel, out var directBackground))
                    {
                        return directBackground;
                    }

                    foreach (var nestedBackground in EnumerateTemplateCustomThemeBackgrounds(root, templateId, deviceModel))
                    {
                        return nestedBackground;
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return "";
    }

    private static string ReadFileContent(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
        {
            using var ms = new MemoryStream(bytes);
            using var gzip = new GZipStream(ms, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            return reader.ReadToEnd();
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static IEnumerable<JsonElement> EnumerateUniversalTemplateConfigs(JsonElement root)
    {
        if (root.TryGetProperty("LcdLayoutConfigs", out var layoutConfigs) &&
            layoutConfigs.ValueKind == JsonValueKind.Array)
        {
            foreach (var config in layoutConfigs.EnumerateArray())
            {
                if (config.ValueKind == JsonValueKind.Object)
                {
                    yield return config;
                }
            }
        }

        var hasLandscape = root.TryGetProperty("IsLandscape", out var isLandscapeProp) &&
                           (isLandscapeProp.ValueKind == JsonValueKind.True ||
                            isLandscapeProp.ValueKind == JsonValueKind.False);
        var isLandscape = !hasLandscape || isLandscapeProp.GetBoolean();

        var propNames = new[]
        {
            isLandscape ? "LandscapeTemplateConfig" : "PortraitTemplateConfig",
            isLandscape ? "PortraitTemplateConfig" : "LandscapeTemplateConfig"
        };

        foreach (var propName in propNames)
        {
            if (root.TryGetProperty(propName, out var config) && config.ValueKind == JsonValueKind.Object)
            {
                yield return config;
            }
        }
    }

    private static bool TryGetCustomThemeBackground(JsonElement owner, string deviceModel, out string background)
    {
        background = "";
        if (!owner.TryGetProperty("CustomTheme", out var customTheme) ||
            customTheme.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var propertyName in new[] { "AppliedImageVideoPath", "SelectedImageVideoPath" })
        {
            if (customTheme.TryGetProperty(propertyName, out var value) &&
                !string.IsNullOrWhiteSpace(value.GetString()) &&
                BackgroundMatches(value.GetString(), deviceModel))
            {
                background = value.GetString()!;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateTemplateCustomThemeBackgrounds(JsonElement element, string templateId, string deviceModel)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("SelectedTemplateId", out var selectedId) &&
                string.Equals(selectedId.GetString(), templateId, StringComparison.OrdinalIgnoreCase) &&
                TryGetCustomThemeBackground(element, deviceModel, out var background))
            {
                yield return background;
            }

            foreach (var property in element.EnumerateObject())
            {
                foreach (var nested in EnumerateTemplateCustomThemeBackgrounds(property.Value, templateId, deviceModel))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in EnumerateTemplateCustomThemeBackgrounds(item, templateId, deviceModel))
                {
                    yield return nested;
                }
            }
        }
    }

    private static string GetLatestAppliedTemplateIdFromLogs(string deviceModel)
    {
        var tags = GetLogDeviceTags(deviceModel).ToArray();
        if (tags.Length == 0) return "";

        var logDir = Path.Combine(LConnectPaths.ProgramDataRoot, "logs");
        if (!Directory.Exists(logDir)) return "";

        var tagPattern = string.Join("|", tags.Select(Regex.Escape));
        var regex = new Regex(@"\[(?:" + tagPattern + @")\]\s+Template\s+'(?<id>[^']+)'\s+applied", RegexOptions.IgnoreCase);

        foreach (var file in Directory.GetFiles(logDir, "L-Connect-Service-*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(3))
        {
            string[] lines;
            try
            {
                lines = ReadSharedLogTailLines(file);
            }
            catch
            {
                continue;
            }

            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var match = regex.Match(lines[i]);
                if (match.Success)
                {
                    return match.Groups["id"].Value.Trim();
                }
            }
        }

        return "";
    }

    private static string GetLatestAppliedTemplateBackgroundFromLogs(string deviceModel, string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId)) return "";

        var tags = GetLogDeviceTags(deviceModel).ToArray();
        if (tags.Length == 0) return "";

        var logDir = Path.Combine(LConnectPaths.ProgramDataRoot, "logs");
        if (!Directory.Exists(logDir)) return "";

        var tagPattern = string.Join("|", tags.Select(Regex.Escape));
        var appliedRegex = new Regex(@"\[(?:" + tagPattern + @")\]\s+Template\s+'" + Regex.Escape(templateId) + @"'\s+applied", RegexOptions.IgnoreCase);
        var pathRegex = new Regex(@"(?:videoPath=>|ApplyTemplate videoPath=)(?<path>[A-Z]:\\[^""]+\.(?:h264|mp4|png|jpg|jpeg|gif))", RegexOptions.IgnoreCase);

        foreach (var file in Directory.GetFiles(logDir, "L-Connect-Service-*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(3))
        {
            string[] lines;
            try
            {
                lines = ReadSharedLogTailLines(file);
            }
            catch
            {
                continue;
            }

            for (var i = lines.Length - 1; i >= 0; i--)
            {
                if (!appliedRegex.IsMatch(lines[i])) continue;

                for (var j = i; j >= Math.Max(0, i - 12); j--)
                {
                    var match = pathRegex.Match(lines[j]);
                    if (!match.Success) continue;

                    var path = Regex.Unescape(match.Groups["path"].Value);
                    if (File.Exists(path) && BackgroundMatches(path, deviceModel))
                    {
                        return path;
                    }
                }
            }
        }

        return "";
    }

    private static string[] ReadSharedLogTailLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 64L * 1024L * 1024L)
        {
            stream.Seek(-64L * 1024L * 1024L, SeekOrigin.End);
        }

        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines.ToArray();
    }

    private static IEnumerable<string> GetLogDeviceTags(string deviceModel)
    {
        if (deviceModel.Equals("hydroshift-ii-lcd-s", StringComparison.OrdinalIgnoreCase))
        {
            yield return "HydroShift II LCD-S";
        }
        else if (deviceModel.Equals("hydroshift-ii-lcd-c", StringComparison.OrdinalIgnoreCase))
        {
            yield return "HydroShift II LCD-C";
        }
        else if (deviceModel.Equals("universal-screen-8.8-inch", StringComparison.OrdinalIgnoreCase) ||
                 deviceModel.Equals("universal-88", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Universal Screen 8.8 Inch";
            yield return "Universal Screen";
        }
        else if (deviceModel.Equals("vm-9.2-inch", StringComparison.OrdinalIgnoreCase))
        {
            yield return "VM 9.2";
            yield return "VM 9.2 LCD";
            yield return "9.2";
            yield return "8.8 inch";
        }
    }

    private static bool TemplateExistsForActiveId(string deviceModel, string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId)) return false;

        var roots = new[]
        {
            Path.Combine(LConnectPaths.ProgramDataRoot, deviceModel, "template"),
            Path.Combine(LConnectPaths.ProgramFilesRoot, "Assets", deviceModel, "template")
        };

        foreach (var root in roots)
        {
            try
            {
                if (File.Exists(Path.Combine(root, templateId + ".template")))
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool BackgroundMatches(string? path, string deviceModel)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var match = Regex.Match(path, @"hydroshift-ii-lcd-[sc]|universal-screen-8\.8-inch|vm-9\.2-inch|hydroshift-ii-oled-curve", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return DeviceModelsShareBackgroundPool(match.Value, deviceModel);
        }

        return true;
    }

    private static bool DeviceModelsShareBackgroundPool(string pathDeviceModel, string selectedDeviceModel)
    {
        if (pathDeviceModel.Equals(selectedDeviceModel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (pathDeviceModel.Equals("universal-screen-8.8-inch", StringComparison.OrdinalIgnoreCase) &&
                selectedDeviceModel.Equals("vm-9.2-inch", StringComparison.OrdinalIgnoreCase)) ||
               (pathDeviceModel.Equals("vm-9.2-inch", StringComparison.OrdinalIgnoreCase) &&
                selectedDeviceModel.Equals("universal-screen-8.8-inch", StringComparison.OrdinalIgnoreCase)) ||
               (pathDeviceModel.Equals("hydroshift-ii-lcd-s", StringComparison.OrdinalIgnoreCase) &&
                selectedDeviceModel.Equals("hydroshift-ii-lcd-c", StringComparison.OrdinalIgnoreCase)) ||
               (pathDeviceModel.Equals("hydroshift-ii-lcd-c", StringComparison.OrdinalIgnoreCase) &&
                selectedDeviceModel.Equals("hydroshift-ii-lcd-s", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUniversalDevice(string deviceModel) =>
        deviceModel.Equals("universal-88", StringComparison.OrdinalIgnoreCase) ||
        deviceModel.Equals("universal-screen-8.8-inch", StringComparison.OrdinalIgnoreCase) ||
        deviceModel.Equals("vm-9.2-inch", StringComparison.OrdinalIgnoreCase) ||
        deviceModel.Equals("hydroshift-ii-oled-curve", StringComparison.OrdinalIgnoreCase) ||
        deviceModel.Equals("hydroshift-ii-lcd-s", StringComparison.OrdinalIgnoreCase) ||
        deviceModel.Equals("hydroshift-ii-lcd-c", StringComparison.OrdinalIgnoreCase);
}
