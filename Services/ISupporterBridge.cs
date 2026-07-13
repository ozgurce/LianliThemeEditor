using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.Services;

public interface ISupporterBridge
{
    string SupporterPath { get; }
    string WorkingDirectory { get; }
    Task<IReadOnlyList<string>> ListFontsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GraphStyleOption>> ListGraphStylesAsync(CancellationToken cancellationToken = default);
    Task ApplyLayerAsync(string deviceModel, string templatePath, LayerRow layer, CancellationToken cancellationToken = default);
    Task ApplyLayersAsync(string deviceModel, string templatePath, IEnumerable<LayerRow> layers, CancellationToken cancellationToken = default);
    Task AddSensorAsync(string deviceModel, string templatePath, string sensorStyle, string sensorType, string x, string y, string zoom, string color1, string color2, string bgColor, string textColor, string font, CancellationToken cancellationToken = default);
    Task<string> RenderSensorPreviewAsync(LayerRow layer, string outputPath, CancellationToken cancellationToken = default);
    Task<string> RenderGraphPreviewAsync(string deviceModel, string templatePath, LayerRow layer, string outputPath, int canvasWidth = 480, int canvasHeight = 480, CancellationToken cancellationToken = default);
    Task AddTextAsync(string deviceModel, string templatePath, string text, string x, string y, string size, string color, string font, bool bold, CancellationToken cancellationToken = default);
    Task SetGroupingMetadataAsync(string deviceModel, string templatePath, string metadata, CancellationToken cancellationToken = default);
    Task AddImageAsync(string deviceModel, string templatePath, string imagePath, string x, string y, string size, CancellationToken cancellationToken = default);
    Task AddClockAsync(string deviceModel, string templatePath, string imagePath, string dataSource, string centerX, string centerY, string size, string format, CancellationToken cancellationToken = default);
    Task AddGraphAsync(string deviceModel, string templatePath, string graphStyleCode, string dataSource, string x, string y, string size, string frontColor, string backColor, CancellationToken cancellationToken = default);
    Task<string> SetBackgroundMediaAsync(string deviceModel, string templatePath, string mediaPath, int canvasWidth = 480, int canvasHeight = 480, CancellationToken cancellationToken = default);
    Task UpdateThemePreviewAsync(string deviceModel, string templatePath, string imagePath, CancellationToken cancellationToken = default);
    Task UpdateAnimationPreviewBitmapsAsync(string deviceModel, string templatePath, string imagePath, CancellationToken cancellationToken = default);
    Task ExportTurzxThemeAsync(string deviceModel, string templatePath, string outputPath, string backgroundPath = "", CancellationToken cancellationToken = default);
    Task EnsureBackgroundLayerAsync(string deviceModel, string templatePath, CancellationToken cancellationToken = default);
    Task NormalizeTemplateIdentityAsync(string deviceModel, string templatePath, string templateId, CancellationToken cancellationToken = default);
    Task ExtractMissingPreviewsAsync(string deviceModel, string templateRoot, string thumbnailRoot, CancellationToken cancellationToken = default);
    Task SetLayerMediaAsync(string deviceModel, string templatePath, string layerIndex, string mediaName, CancellationToken cancellationToken = default);
    Task AddDataAsync(string deviceModel, string templatePath, string dataSource, string x, string y, string size, string color, string font, bool bold, string format = "", CancellationToken cancellationToken = default);
    Task RemoveLayerAsync(string deviceModel, string templatePath, string layerIndex, CancellationToken cancellationToken = default);
    Task MoveLayerAsync(string deviceModel, string templatePath, string layerIndex, string direction, CancellationToken cancellationToken = default);
    Task DuplicateLayerAsync(string deviceModel, string templatePath, string layerIndex, CancellationToken cancellationToken = default);
    Task<TemplateLoadResult> LoadLayersAsync(string deviceModel, bool useActiveTemplate, string templateId, CancellationToken cancellationToken = default);
    Task<TemplateLoadResult> LoadTemplatePathAsync(string deviceModel, string templatePath, bool inspectBitmaps = true, CancellationToken cancellationToken = default);
}
