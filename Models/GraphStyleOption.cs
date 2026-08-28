namespace ThemeEditorCSharp.Models;

public sealed class GraphStyleOption
{
    public string Label { get; set; } = "";
    public string Code { get; set; } = "";
    public string Source { get; set; } = "";
    public string GraphType { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string SubTypeName { get; set; } = "";
    public string Preview { get; set; } = "";

    public override string ToString() => Label;
}
