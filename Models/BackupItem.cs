namespace ThemeEditorCSharp.Models;

public sealed class BackupItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Details { get; set; } = "";
    public string OpenText { get; set; } = "Open";
    public string DeleteText { get; set; } = "Delete";
    public DateTime LastWriteTime { get; set; }
}
