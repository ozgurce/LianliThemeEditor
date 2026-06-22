using System.Text.Json;
using System.IO;
using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.Services;

public sealed class RecoverySnapshot
{
    public DateTime SavedAtUtc { get; init; }
    public string DeviceModel { get; init; } = "";
    public string TemplateId { get; init; } = "";
    public string TemplatePath { get; init; } = "";
    public List<LayerRow> Layers { get; init; } = new();
}

public sealed class RecoveryService
{
    public string RecoveryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LianLiThemeEditor", "recovery.json");

    public void Save(string deviceModel, string templateId, string templatePath, IEnumerable<LayerRow> layers)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RecoveryPath)!);
        var snapshot = new RecoverySnapshot
        {
            SavedAtUtc = DateTime.UtcNow,
            DeviceModel = deviceModel,
            TemplateId = templateId,
            TemplatePath = templatePath,
            Layers = layers.Where(layer => !layer.IsEditorMetadata).ToList()
        };
        File.WriteAllText(RecoveryPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
    }

    public Task SaveAsync(string deviceModel, string templateId, string templatePath, IEnumerable<LayerRow> layers)
    {
        var snapshotLayers = layers.Where(layer => !layer.IsEditorMetadata).ToList();
        return Task.Run(() => Save(deviceModel, templateId, templatePath, snapshotLayers));
    }

    public RecoverySnapshot? Load()
    {
        try
        {
            return File.Exists(RecoveryPath)
                ? JsonSerializer.Deserialize<RecoverySnapshot>(File.ReadAllText(RecoveryPath))
                : null;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Recovery snapshot could not be read.", ex);
            return null;
        }
    }

    public void Clear()
    {
        try { if (File.Exists(RecoveryPath)) File.Delete(RecoveryPath); }
        catch (Exception ex) { AppLogger.Error("Recovery snapshot could not be removed.", ex); }
    }
}
