using System.Windows;
using System.Windows.Media;
using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.ViewModels;

public sealed class ConvertFixFontItem
{
    private static readonly Brush InstalledBrush = CreateFrozenBrush("#62D6B5");
    private static readonly Brush PackagedBrush = CreateFrozenBrush("#F0B84B");
    private static readonly Brush MissingBrush = CreateFrozenBrush("#E05A67");

    public string Source { get; init; } = "";
    public string ExtractedPath { get; init; } = "";
    public string FamilyName { get; init; } = "";
    public bool InstalledMachineWide { get; init; }
    public bool InstallSelected { get; set; }
    public bool HasPackagedFont => !string.IsNullOrWhiteSpace(ExtractedPath);
    public string StatusText { get; init; } = "";
    public string SearchText { get; init; } = "";
    public string SearchDafontButtonText { get; init; } = "";
    public Brush AccentBrush => InstalledMachineWide ? InstalledBrush : HasPackagedFont ? PackagedBrush : MissingBrush;
    public string DisplayText => $"{(InstalledMachineWide ? "Installed" : "Missing")} - {FamilyName} [{Source}]";

    private static Brush CreateFrozenBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}

public sealed class ConvertFixReportItem
{
    private static readonly Brush OkBrush = CreateFrozenBrush("#62D6B5");
    private static readonly Brush WarningBrush = CreateFrozenBrush("#F0B84B");
    private static readonly Brush ErrorBrush = CreateFrozenBrush("#E05A67");

    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public string Severity { get; init; } = "Info";
    public FontWeight Weight => string.Equals(Severity, "Header", StringComparison.OrdinalIgnoreCase)
        ? FontWeights.SemiBold
        : FontWeights.Normal;
    public Brush AccentBrush => Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)
        ? ErrorBrush
        : Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase)
            ? WarningBrush
            : OkBrush;

    private static Brush CreateFrozenBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}

public sealed class ConvertFixTemplateInspection
{
    public string PackagePath { get; init; } = "";
    public string TemplatePath { get; init; } = "";
    public string TemplateEntryName { get; init; } = "";
    public string DeviceModel { get; init; } = "";
    public ThemePackageManifest? Manifest { get; init; }
    public IReadOnlyList<LayerRow> Layers { get; init; } = Array.Empty<LayerRow>();
    public IReadOnlyDictionary<string, string> PackageEntries { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> PackagedFonts { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string TempRoot { get; init; } = "";
}
