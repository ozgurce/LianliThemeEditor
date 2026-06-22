using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.Services;

public sealed class LayerGroupService
{
    public string GetUniqueName(IEnumerable<LayerGroup> groups, string requested, string? excludedId = null)
    {
        var baseName = string.IsNullOrWhiteSpace(requested) ? "Group" : requested.Trim();
        var name = baseName;
        var suffix = 2;
        while (groups.Any(group => group.Id != excludedId && string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} ({suffix++})";
        return name;
    }

    public IReadOnlyList<string> Assign(IEnumerable<LayerRow> layers, LayerGroup group)
    {
        var previousIds = layers.Select(layer => layer.GroupId)
            .Where(id => !string.IsNullOrWhiteSpace(id) && id != group.Id).Distinct().ToList();
        foreach (var layer in layers)
        {
            layer.GroupId = group.Id;
            layer.GroupName = group.Name;
            layer.GroupColor = group.Color;
        }
        return previousIds;
    }

    public IReadOnlyList<string> Remove(IEnumerable<LayerRow> layers)
    {
        var list = layers.ToList();
        var previousIds = list.Select(layer => layer.GroupId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        foreach (var layer in list)
        {
            layer.GroupId = "";
            layer.GroupName = "";
            layer.GroupColor = "#246FF2";
        }
        return previousIds;
    }
}
