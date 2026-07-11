using System.Collections.ObjectModel;

namespace ThemeEditorCSharp.Models;

public sealed class TemplateLoadResult
{
    public string TemplateId { get; init; } = "";
    public string TemplatePath { get; init; } = "";
    public string Background { get; set; } = "";
    public string BackgroundPath { get; set; } = "";
    public ObservableCollection<LayerRow> Layers { get; } = new();
}

