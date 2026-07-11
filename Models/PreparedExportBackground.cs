namespace ThemeEditorCSharp.Models;

public sealed class PreparedExportBackground
{
    public string Path { get; init; } = "";
    public string RuntimePath { get; init; } = "";
    public string PreviewPath { get; init; } = "";
    public List<string> TemporaryPaths { get; init; } = new();
    public bool IsTemporary => TemporaryPaths.Count > 0;
}
