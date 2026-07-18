namespace ThemeEditorCSharp.Services;

public static class GalleryDeviceModelNormalizer
{
    public const string UniversalScreenDeviceModel = "universal-screen-8.8-inch";
    public const string Vm92DeviceModel = "vm-9.2-inch";
    public const string OledCurveDeviceModel = "hydroshift-ii-oled-curve";

    public static string Normalize(string value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant()
            .Replace('_', '-')
            .Replace(' ', '-');
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return normalized switch
        {
            "hydroshift-c" or "hydroshift-ii-c" or "hydroshift-ii-lcd-c" => "hydroshift-ii-lcd-c",
            "hydroshift-s" or "hydroshift-ii-s" or "hydroshift-ii-lcd-s" => "hydroshift-ii-lcd-s",
            "universal-screen-8.8" or "universal-8.8" or "universal-screen-8.8-inch" => UniversalScreenDeviceModel,
            "vm-9.2" or "vm-9.2-lcd" or "vm-9.2-inch" => Vm92DeviceModel,
            "hydroshift-ii-oled" or "hydroshift-oled-curve" or "oled-curve" or "hydroshift-ii-oled-curve" => OledCurveDeviceModel,
            _ => normalized
        };
    }
}
