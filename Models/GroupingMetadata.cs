namespace ThemeEditorCSharp.Models;

public sealed class GroupingMetadata
{
    public int Version { get; set; } = 1;
    public List<GroupingMetadataGroup> Groups { get; set; } = new();
    public List<GroupingMetadataMember> Members { get; set; } = new();
}

public sealed class GroupingMetadataGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsExpanded { get; set; } = true;
    public bool IsLocked { get; set; }
    public string Color { get; set; } = "#246FF2";
}

public sealed class GroupingMetadataMember
{
    public string GroupId { get; set; } = "";
    public int Index { get; set; }
    public string Signature { get; set; } = "";
}
