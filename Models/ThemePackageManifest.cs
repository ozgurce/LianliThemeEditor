namespace ThemeEditorCSharp.Models;

public sealed class ThemePackageManifest
{
    public int FormatVersion { get; set; } = 1;
    public string App { get; set; } = "Lian Li LCD Theme Editor";
    public string DeviceModel { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public string TemplateFile { get; set; } = "template/theme.template";
    public string BackgroundFile { get; set; } = "";
    public string PreviewFile { get; set; } = "";
    public string UniversalOrientation { get; set; } = "";
    public List<string> ImageFiles { get; set; } = new();
    public List<string> FontFiles { get; set; } = new();
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;

    // Backward-compatible legacy package fields.
    public string id { get; set; } = "";
    public string deviceModel { get; set; } = "";
    public string template { get; set; } = "";
    public string background { get; set; } = "";
    public string preview { get; set; } = "";
    public string universalOrientation { get; set; } = "";
}
