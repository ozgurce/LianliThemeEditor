using System.IO.Compression;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.Services;

public sealed class ThemePackageValidationService : IThemePackageValidationService
{
    private static readonly HashSet<string> SupportedDevices = new(StringComparer.OrdinalIgnoreCase)
    {
        "hydroshift-ii-lcd-s", "hydroshift-ii-lcd-c", "universal-screen-8.8-inch", "vm-9.2-inch", "hydroshift-ii-oled-curve"
    };

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".gif", ".jpg", ".jpeg", ".png", ".h264", ".webp"
    };

    public ThemeValidationResult Validate(
        string packagePath,
        IEnumerable<string>? installedTemplateIds = null,
        string fallbackDeviceModel = "")
    {
        var result = new ThemeValidationResult();
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            result.Issues.Add(Error("Theme package was not found."));
            return result;
        }

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry == null)
            {
                var template = archive.Entries.FirstOrDefault(entry => entry.FullName.EndsWith(".template", StringComparison.OrdinalIgnoreCase));
                if (template == null)
                {
                    result.Issues.Add(Error("The ZIP does not contain a template file."));
                    return result;
                }
                if (IsUnsafePath(template.FullName))
                {
                    result.Issues.Add(Error("The template file path is unsafe."));
                    return result;
                }
                result = new ThemeValidationResult
                {
                    TemplateId = Path.GetFileNameWithoutExtension(template.Name),
                    TemplateFile = template.FullName
                };
                if (!archive.Entries.Any(entry => MediaExtensions.Contains(Path.GetExtension(entry.FullName))))
                    result.Issues.Add(Warning("Background media is missing."));
                result.Issues.Add(new ThemeValidationIssue { Severity = "Info", Message = "L-Connect ZIP package detected." });
                return result;
            }

            using var reader = new StreamReader(manifestEntry.Open());
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            var root = document.RootElement;
            var device = GetFirstString(root, "DeviceModel", "deviceModel", "device");
            if (string.IsNullOrWhiteSpace(device)) device = fallbackDeviceModel;
            var id = GetFirstString(root, "TemplateId", "templateId", "id");
            var templateFile = GetFirstString(root, "TemplateFile", "templateFile", "template");
            result = new ThemeValidationResult { DeviceModel = device, TemplateId = id, TemplateFile = templateFile };

            var version = TryGetInt(root, out var parsed, "FormatVersion", "formatVersion") ? parsed : 1;
            if (version != 1) result.Issues.Add(Error($"Unsupported package version: {version}."));
            if (!SupportedDevices.Contains(device)) result.Issues.Add(Error("The target device is not supported."));
            if (string.IsNullOrWhiteSpace(id)) result.Issues.Add(Error("Template ID is empty."));
            var templateEntry = GetPackageEntry(archive, templateFile);
            if (IsUnsafePath(templateFile) || templateEntry == null)
            {
                result.Issues.Add(Error("The template file is missing or unsafe."));
            }

            foreach (var entry in archive.Entries)
            {
                if (IsUnsafePath(entry.FullName)) result.Issues.Add(Error($"Unsafe package path: {entry.FullName}"));
                if (entry.Length > 500L * 1024 * 1024) result.Issues.Add(Error($"File is too large: {entry.Name}"));
            }

            var background = GetFirstString(root, "BackgroundFile", "backgroundFile", "background");
            if (string.IsNullOrWhiteSpace(background))
            {
                if (TemplateRequiresExternalBackground(templateEntry))
                {
                    result.Issues.Add(Warning("Background media is missing."));
                }
            }
            else if (GetPackageEntry(archive, background) == null)
            {
                result.Issues.Add(Warning("Background media is missing."));
            }
            else if (!MediaExtensions.Contains(Path.GetExtension(background)))
            {
                result.Issues.Add(Warning("The background format may not be supported by L-Connect."));
            }

            if (root.TryGetProperty("ImageFiles", out var images) && images.ValueKind == JsonValueKind.Array)
            {
                foreach (var image in images.EnumerateArray().Select(item => item.GetString() ?? ""))
                {
                    if (IsUnsafePath(image) || GetPackageEntry(archive, image) == null)
                    {
                        result.Issues.Add(Error($"Referenced image is missing: {image}"));
                    }
                }
            }

            if (root.TryGetProperty("FontFiles", out var fonts) && fonts.ValueKind == JsonValueKind.Array)
            {
                foreach (var font in fonts.EnumerateArray().Select(item => item.GetString() ?? ""))
                {
                    var extension = Path.GetExtension(font);
                    if (IsUnsafePath(font) ||
                        GetPackageEntry(archive, font) == null ||
                        (!extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) &&
                         !extension.Equals(".otf", StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Issues.Add(Error($"Referenced font is missing or unsupported: {font}"));
                    }
                }
            }

            if (installedTemplateIds?.Contains(id, StringComparer.OrdinalIgnoreCase) == true)
            {
                result.Issues.Add(Warning("A theme with the same identity is already installed and will be updated."));
            }

            if (result.Issues.Count == 0) result.Issues.Add(new ThemeValidationIssue { Message = "Package checks passed." });
        }
        catch (InvalidDataException ex)
        {
            result.Issues.Add(Error("The package is corrupt or is not a valid lltheme/zip file."));
            AppLogger.Error("Theme package validation failed.", ex);
        }
        catch (Exception ex)
        {
            result.Issues.Add(Error($"Validation failed: {ex.Message}"));
            AppLogger.Error("Theme package validation failed.", ex);
        }

        return result;
    }

    private static string GetFirstString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }
        }

        return "";
    }

    private static bool TryGetInt(JsonElement root, out int value, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var node) && node.TryGetInt32(out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }
    private static string Normalize(string path) => (path ?? "").Replace('\\', '/');
    private static ZipArchiveEntry? GetPackageEntry(ZipArchive archive, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || IsUnsafePath(entryName)) return null;

        var normalized = Normalize(entryName);
        return archive.GetEntry(entryName)
               ?? archive.GetEntry(normalized)
               ?? archive.Entries.FirstOrDefault(entry =>
                   string.Equals(Normalize(entry.FullName), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TemplateRequiresExternalBackground(ZipArchiveEntry? templateEntry)
    {
        if (templateEntry == null) return false;
        using var stream = templateEntry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var templateBytes = memory.ToArray();
        if (ContainsEmbeddedPng(templateBytes))
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(templateBytes);
        return text.Contains("GraphAnimation", StringComparison.OrdinalIgnoreCase) &&
               Regex.IsMatch(text, @"\.(mp4|h264|gif|png|jpe?g|webp)", RegexOptions.IgnoreCase);
    }

    private static bool ContainsEmbeddedPng(byte[] data)
    {
        ReadOnlySpan<byte> signature = stackalloc byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        for (var offset = 0; offset <= data.Length - signature.Length; offset++)
        {
            if (data.AsSpan(offset, signature.Length).SequenceEqual(signature))
            {
                return true;
            }
        }

        return false;
    }
    private static bool IsUnsafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)) return true;

        var basePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "lianli-package-root"));
        var fullPath = Path.GetFullPath(Path.Combine(basePath, path.Replace('/', Path.DirectorySeparatorChar)));
        return !fullPath.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(fullPath, basePath, StringComparison.OrdinalIgnoreCase);
    }
    private static ThemeValidationIssue Error(string message) => new() { Severity = "Error", Message = message };
    private static ThemeValidationIssue Warning(string message) => new() { Severity = "Warning", Message = message };
}
