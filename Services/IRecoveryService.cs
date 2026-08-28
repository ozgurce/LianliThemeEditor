using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.Services;

public interface IRecoveryService
{
    string RecoveryPath { get; }
    void Save(string deviceModel, string templateId, string templatePath, IEnumerable<LayerRow> layers);
    Task SaveAsync(string deviceModel, string templateId, string templatePath, IEnumerable<LayerRow> layers);
    RecoverySnapshot? Load();
    void Clear();
}
