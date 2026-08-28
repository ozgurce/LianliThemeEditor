namespace ThemeEditorCSharp.Models;

public sealed class TemplateOption
{
    public string Id { get; set; } = "";
    public string LConnectId { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> GroupedTemplatePaths { get; set; } = new();
    public List<string> GroupedTemplateIds { get; set; } = new();
    public string OledCurveGroupKind { get; set; } = "";
    public bool IsOledCurveGroup => GroupedTemplatePaths.Count > 1;
    public string BackgroundPath { get; set; } = "";
    public string DeviceModel { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string UniversalOrientation { get; set; } = "";
    public string OrientationDisplay { get; set; } = "";
    public bool IsOfficial { get; set; }
    public bool CanDelete { get; set; }
    public string SourceDisplay { get; set; } = "";
    public string ApplyText { get; set; } = "Apply";
    public string DeleteText { get; set; } = "Delete";
    public bool LConnectVisible { get; set; } = true;
    public bool InstalledPackagedFonts { get; set; }
    public System.Windows.Media.ImageSource? Thumbnail { get; set; }
    public bool IsPortraitThumbnail { get; set; }
    public bool IsSquareThumbnail { get; set; }
    public double ThumbnailBoxWidth => IsPortraitThumbnail ? 74.0 : 174.0;
    public double ThumbnailBoxHeight => IsPortraitThumbnail ? 74.0 : 70.0;
    public System.Windows.GridLength ThumbnailColumnWidth => new(IsPortraitThumbnail ? 86.0 : 182.0);
    public double LocalCardWidth { get; set; } = 286.0;
    public double LocalCardHeight { get; set; } = 338.0;
    public double LocalCardPreviewHeight { get; set; } = 220.0;
    public System.Windows.Media.Stretch LocalPreviewStretch => IsLocalWideDevice
        ? System.Windows.Media.Stretch.Uniform
        : IsPortraitThumbnail
            ? System.Windows.Media.Stretch.Uniform
            : System.Windows.Media.Stretch.UniformToFill;

    public string LocalAspectLabel => IsLocalWideDevice
        ? GetWideAspectLabel()
        : "480 x 480";

    public bool IsLocalWideDevice =>
        string.Equals(DeviceModel, "universal-screen-8.8-inch", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(DeviceModel, "vm-9.2-inch", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(DeviceModel, "hydroshift-ii-oled-curve", StringComparison.OrdinalIgnoreCase);

    public bool IsLocalPortraitWideDevice =>
        IsLocalWideDevice &&
        string.Equals(UniversalOrientation, "portrait", StringComparison.OrdinalIgnoreCase);

    public bool IsLocalLandscapeWideDevice => IsLocalWideDevice && !IsLocalPortraitWideDevice;

    private string GetWideAspectLabel()
    {
        if (string.Equals(DeviceModel, "hydroshift-ii-oled-curve", StringComparison.OrdinalIgnoreCase))
        {
            return "2288 x 1080";
        }

        var isVm92 = string.Equals(DeviceModel, "vm-9.2-inch", StringComparison.OrdinalIgnoreCase);
        return IsLocalPortraitWideDevice
            ? isVm92 ? "464 x 1920" : "480 x 1920"
            : isVm92 ? "1920 x 464" : "1920 x 480";
    }

    public override string ToString() => Id;
}
