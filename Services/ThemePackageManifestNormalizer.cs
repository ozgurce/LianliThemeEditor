using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.Services;

public static class ThemePackageManifestNormalizer
{
    public static bool Normalize(
        ThemePackageManifest manifest,
        string fallbackDeviceModel = "",
        string fallbackTemplateId = "")
    {
        var changed = false;

        if (string.IsNullOrWhiteSpace(manifest.DeviceModel))
        {
            manifest.DeviceModel = !string.IsNullOrWhiteSpace(manifest.deviceModel)
                ? manifest.deviceModel
                : fallbackDeviceModel;
            changed = !string.IsNullOrWhiteSpace(manifest.DeviceModel);
        }

        if (string.IsNullOrWhiteSpace(manifest.TemplateId))
        {
            manifest.TemplateId = !string.IsNullOrWhiteSpace(manifest.id)
                ? manifest.id
                : fallbackTemplateId;
            changed = !string.IsNullOrWhiteSpace(manifest.TemplateId);
        }

        if (string.IsNullOrWhiteSpace(manifest.TemplateFile) ||
            string.Equals(manifest.TemplateFile, "template/theme.template", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(manifest.template))
            {
                manifest.TemplateFile = manifest.template;
                changed = true;
            }
        }

        if (string.IsNullOrWhiteSpace(manifest.BackgroundFile) && !string.IsNullOrWhiteSpace(manifest.background))
        {
            manifest.BackgroundFile = manifest.background;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(manifest.PreviewFile) && !string.IsNullOrWhiteSpace(manifest.preview))
        {
            manifest.PreviewFile = manifest.preview;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(manifest.UniversalOrientation) &&
            !string.IsNullOrWhiteSpace(manifest.universalOrientation))
        {
            manifest.UniversalOrientation = manifest.universalOrientation;
            changed = true;
        }

        manifest.UniversalOrientation = NormalizeUniversalOrientation(manifest.UniversalOrientation);

        return changed;
    }

    public static string NormalizeUniversalOrientation(string? orientation)
    {
        return string.Equals(orientation, "portrait", StringComparison.OrdinalIgnoreCase)
            ? "portrait"
            : string.Equals(orientation, "landscape", StringComparison.OrdinalIgnoreCase)
                ? "landscape"
                : "";
    }
}
