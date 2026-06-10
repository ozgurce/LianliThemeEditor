namespace ThemeEditorCSharp.Models;

public sealed class TemplateOption
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";

    public override string ToString() => Id;
}
