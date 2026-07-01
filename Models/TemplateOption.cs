namespace ThemeEditorCSharp.Models;

public sealed class TemplateOption
{
    public string Id { get; set; } = "";
    public string LConnectId { get; set; } = "";
    public string Path { get; set; } = "";
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
        ? (IsLocalPortraitWideDevice ? "480 x 1920" : "1920 x 480")
        : "480 x 480";

    public bool IsLocalWideDevice =>
        string.Equals(DeviceModel, "universal-screen-8.8-inch", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(DeviceModel, "vm-9.2-inch", StringComparison.OrdinalIgnoreCase);

    public bool IsLocalPortraitWideDevice =>
        IsLocalWideDevice &&
        string.Equals(UniversalOrientation, "portrait", StringComparison.OrdinalIgnoreCase);

    public bool IsLocalLandscapeWideDevice => IsLocalWideDevice && !IsLocalPortraitWideDevice;

    public override string ToString() => Id;
}
