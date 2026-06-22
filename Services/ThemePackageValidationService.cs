using System.IO.Compression;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.Services;

public sealed class ThemePackageValidationService
{
    private static readonly HashSet<string> SupportedDevices = new(StringComparer.OrdinalIgnoreCase)
    {
        "hydroshift-ii-lcd-s", "hydroshift-ii-lcd-c", "universal-screen-8.8-inch", "vm-9.2-inch"
    };

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".gif", ".jpg", ".jpeg", ".png", ".h264", ".webp"
    };

    public ThemeValidationResult Validate(string packagePath, IEnumerable<string>? installedTemplateIds = null)
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
                result = new ThemeValidationResult { TemplateId = Path.GetFileNameWithoutExtension(template.Name), TemplateFile = template.FullName };
                if (!archive.Entries.Any(entry => MediaExtensions.Contains(Path.GetExtension(entry.FullName))))
                    result.Issues.Add(Warning("Background media is missing."));
                result.Issues.Add(new ThemeValidationIssue { Severity = "Info", Message = "L-Connect ZIP package detected." });
                return result;
            }

            using var reader = new StreamReader(manifestEntry.Open());
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            var root = document.RootElement;
            var device = GetString(root, "DeviceModel");
            var id = GetString(root, "TemplateId");
            var templateFile = GetString(root, "TemplateFile");
            result = new ThemeValidationResult { DeviceModel = device, TemplateId = id, TemplateFile = templateFile };

            var version = root.TryGetProperty("FormatVersion", out var versionNode) && versionNode.TryGetInt32(out var parsed) ? parsed : 0;
            if (version != 1) result.Issues.Add(Error($"Unsupported package version: {version}."));
            if (!SupportedDevices.Contains(device)) result.Issues.Add(Error("The target device is not supported."));
            if (string.IsNullOrWhiteSpace(id)) result.Issues.Add(Error("Template ID is empty."));
            if (IsUnsafePath(templateFile) || archive.GetEntry(Normalize(templateFile)) == null)
            {
                result.Issues.Add(Error("The template file is missing or unsafe."));
            }

            foreach (var entry in archive.Entries)
            {
                if (IsUnsafePath(entry.FullName)) result.Issues.Add(Error($"Unsafe package path: {entry.FullName}"));
                if (entry.Length > 500L * 1024 * 1024) result.Issues.Add(Error($"File is too large: {entry.Name}"));
            }

            var background = GetString(root, "BackgroundFile");
            if (string.IsNullOrWhiteSpace(background))
            {
                var templateEntry = archive.GetEntry(Normalize(templateFile));
                if (TemplateRequiresExternalBackground(templateEntry))
                {
                    result.Issues.Add(Warning("Background media is missing."));
                }
            }
            else if (archive.GetEntry(Normalize(background)) == null)
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
                    if (IsUnsafePath(image) || archive.GetEntry(Normalize(image)) == null)
                    {
                        result.Issues.Add(Error($"Referenced image is missing: {image}"));
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

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static string Normalize(string path) => (path ?? "").Replace('\\', '/');
    private static bool TemplateRequiresExternalBackground(ZipArchiveEntry? templateEntry)
    {
        if (templateEntry == null) return false;
        using var stream = templateEntry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var text = Encoding.UTF8.GetString(memory.ToArray());
        return text.Contains("GraphAnimation", StringComparison.OrdinalIgnoreCase) &&
               Regex.IsMatch(text, @"\.(mp4|h264|gif|png|jpe?g|webp)", RegexOptions.IgnoreCase);
    }
    private static bool IsUnsafePath(string path) => string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || Normalize(path).Split('/').Any(part => part == "..");
    private static ThemeValidationIssue Error(string message) => new() { Severity = "Error", Message = message };
    private static ThemeValidationIssue Warning(string message) => new() { Severity = "Warning", Message = message };
}
