namespace ThemeEditorCSharp.Models;

public sealed class ThemeExportSnapshot
{
    public string DeviceModel { get; init; } = "";
    public string TemplateId { get; init; } = "";
    public string ExportTemplateId { get; init; } = "";
    public string TemplatePath { get; init; } = "";
    public string BackgroundPath { get; init; } = "";
    public string BackgroundEntryName { get; init; } = "";
    public string PreviewPath { get; init; } = "";
    public string UniversalOrientation { get; init; } = "";
    public List<string> ImagePaths { get; init; } = new();
    public List<string> FontPaths { get; init; } = new();
}
