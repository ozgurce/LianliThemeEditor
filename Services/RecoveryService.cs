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

public sealed class RecoveryService : IRecoveryService
{
    private static readonly SemaphoreSlim SaveGate = new(1, 1);

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
        WriteSnapshot(RecoveryPath, snapshot);
    }

    public async Task SaveAsync(string deviceModel, string templateId, string templatePath, IEnumerable<LayerRow> layers)
    {
        var snapshotLayers = layers.Where(layer => !layer.IsEditorMetadata).ToList();
        var snapshot = new RecoverySnapshot
        {
            SavedAtUtc = DateTime.UtcNow,
            DeviceModel = deviceModel,
            TemplateId = templateId,
            TemplatePath = templatePath,
            Layers = snapshotLayers
        };

        await WriteSnapshotAsync(RecoveryPath, snapshot).ConfigureAwait(false);
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

    private static void WriteSnapshot(string path, RecoverySnapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tempPath, json);
        ReplaceFile(tempPath, path);
    }

    private static async Task WriteSnapshotAsync(string path, RecoverySnapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await SaveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            ReplaceFile(tempPath, path);
        }
        finally
        {
            SaveGate.Release();
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch (Exception ex) { AppLogger.Error("Temporary recovery snapshot could not be removed.", ex); }
        }
    }

    private static void ReplaceFile(string tempPath, string path)
    {
        if (File.Exists(path))
        {
            File.Replace(tempPath, path, null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }
}
