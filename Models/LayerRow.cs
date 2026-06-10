namespace ThemeEditorCSharp.Models;

public sealed class LayerRow
{
    private readonly HashSet<string> _writableProperties = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _writableFontProperties = new(StringComparer.OrdinalIgnoreCase);

    public string Index { get; set; } = "";
    public string Type { get; set; } = "";
    public string DataSource { get; set; } = "";
    public string Text { get; set; } = "";
    public string Media { get; set; } = "";
    public string Description { get; set; } = "";
    public string X { get; set; } = "";
    public string Y { get; set; } = "";
    public string Size { get; set; } = "";
    public string Font { get; set; } = "";
    public string Bold { get; set; } = "";
    public string Italic { get; set; } = "";
    public string Color { get; set; } = "";
    public string Format { get; set; } = "";
    public string GraphStyle { get; set; } = "";
    public string OriginalGraphStyle { get; set; } = "";
    public bool ForceText { get; set; }
    public bool PreviewValueEdited { get; set; }

    // Graph & Image Properties
    public string Width { get; set; } = "";
    public string Height { get; set; } = "";
    public string Radius { get; set; } = "";
    public string Diameter { get; set; } = "";
    public string Thickness { get; set; } = "";
    public string FrontColor { get; set; } = "";
    public string BackColor { get; set; } = "";
    public string UseGradient { get; set; } = "";
    public string GradientColor { get; set; } = "";
    public string ZoomRate { get; set; } = "";
    public string Rotate { get; set; } = "";
    public string Rect { get; set; } = "";
    public string AlignmentIndex { get; set; } = "";
    public string AlignmentName { get; set; } = "";
    public string FontInterval { get; set; } = "";
    public string FontOrgSize { get; set; } = "";
    public string LineHeight { get; set; } = "";
    public string Direction { get; set; } = "";
    public string LineWidth { get; set; } = "";
    public string ColumnWidth { get; set; } = "";
    public string BorderWidth { get; set; } = "";
    public string InnerCircleRadius { get; set; } = "";
    public string SplitBlockWidth { get; set; } = "";
    public string SplitBlankWidth { get; set; } = "";
    public string UseSubsection { get; set; } = "";
    public string FillBack { get; set; } = "";
    public string Revert { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string SubTypeName { get; set; } = "";

    public IReadOnlyCollection<string> WritableProperties => _writableProperties;
    public IReadOnlyCollection<string> WritableFontProperties => _writableFontProperties;

    public void SetWritableProperties(IEnumerable<string> properties)
    {
        _writableProperties.Clear();
        foreach (var property in properties.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            _writableProperties.Add(property);
        }
    }

    public void SetWritableFontProperties(IEnumerable<string> properties)
    {
        _writableFontProperties.Clear();
        foreach (var property in properties.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            _writableFontProperties.Add(property);
        }
    }

    public bool CanWrite(string propertyName)
    {
        return _writableProperties.Count == 0 || _writableProperties.Contains(propertyName);
    }

    public bool CanWriteFont(string propertyName)
    {
        return _writableFontProperties.Count == 0 || _writableFontProperties.Contains(propertyName);
    }
}
