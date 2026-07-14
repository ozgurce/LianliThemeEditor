namespace PhoneControl;

public sealed class PreviewFileService
{
    public static readonly byte[] FallbackPng =
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAASwAAACWCAYAAABkW7XSAAAACXBIWXMAAAsTAAALEwEAmpwYAAABrklEQVR4nO3TMQ0AMAwEMSr+O2cCBjQkC7QQO7Mzs6cB8G4A+FsggQUSWCCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYIEFFlhggQUWWGCBBRZYYOHlA3blA8qAxm0nAAAAAElFTkSuQmCC");

    public string? ResolvePreview(string deviceModel, string templateId)
    {
        foreach (var candidate in EnumeratePreviewCandidates(deviceModel, templateId))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumeratePreviewCandidates(string deviceModel, string templateId)
    {
        var previewDirectory = Path.Combine(LConnectPaths.ProgramDataRoot, deviceModel, "preview");
        foreach (var alias in GetTemplatePreviewAliases(templateId))
        {
            yield return Path.Combine(previewDirectory, $"template_{alias}.png");
            yield return Path.Combine(previewDirectory, $"{alias}.png");
        }

        var thumbnailRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LianLiThemeEditor",
            "TemplateThumbnails",
            deviceModel);
        yield return Path.Combine(thumbnailRoot, $"{templateId}.png");
        yield return Path.Combine(thumbnailRoot, $"{templateId.TrimEnd('_')}.png");
    }

    private static IEnumerable<string> GetTemplatePreviewAliases(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            yield break;
        }

        yield return templateId;
        var trimmed = templateId.TrimEnd('_');
        if (!string.Equals(trimmed, templateId, StringComparison.OrdinalIgnoreCase))
        {
            yield return trimmed;
        }
    }
}
