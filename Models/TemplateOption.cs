namespace ThemeEditorCSharp.Models;

public sealed class TemplateOption
{
    public string Id { get; set; } = "";
    public string LConnectId { get; set; } = "";
    public string Path { get; set; } = "";
    public string BackgroundPath { get; set; } = "";
    public bool LConnectVisible { get; set; } = true;
    public System.Windows.Media.ImageSource? Thumbnail { get; set; }

    public override string ToString() => Id;
}
