using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ThemeEditorCSharp.Models;

public sealed class LayerRow : INotifyPropertyChanged
{
    private readonly HashSet<string> _writableProperties = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _writableFontProperties = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLocked;
    private bool _isDirty;
    private bool _isLayerListDragging;
    private bool _isLayerDropBefore;
    private bool _isLayerDropAfter;
    private string _groupId = "";
    private string _groupName = "";
    private string _groupColor = "#246FF2";

    public string Index { get; set; } = "";
    [JsonIgnore]
    public string SourceLayerIndex { get; set; } = "";
    [JsonIgnore]
    public string SourceTemplatePath { get; set; } = "";
    [JsonIgnore]
    public string SourceTemplateId { get; set; } = "";
    [JsonIgnore]
    public double SourceOffsetX { get; set; }
    public string Type { get; set; } = "";
    public string DataSource { get; set; } = "";
    public string DataSourceDisplay { get; set; } = "";
    public string OriginalDataSource { get; set; } = "";
    public string DataRate { get; set; } = "";
    public string Text { get; set; } = "";
    public string Media { get; set; } = "";
    public string MediaPath { get; set; } = "";
    public string Description { get; set; } = "";
    public string TypeDisplay { get; set; } = "";
    public string LayerDataTitle =>
        !string.IsNullOrWhiteSpace(DataSourceDisplay)
            ? DataSourceDisplay
            : !string.IsNullOrWhiteSpace(Media)
                ? Media
                : !string.IsNullOrWhiteSpace(Text)
                    ? Text
                    : "-";
    public string LayerTypeSubtitle => !string.IsNullOrWhiteSpace(TypeDisplay)
        ? TypeDisplay
        : string.IsNullOrWhiteSpace(Description) ? Type : Description;
    public string IconData { get; set; } = "";
    public string IconColor { get; set; } = "#246FF2";
    public bool IsEditorMetadata { get; set; }
    public string GroupId
    {
        get => _groupId;
        set
        {
            if (_groupId == value) return;
            _groupId = value;
            OnPropertyChanged();
        }
    }
    public string GroupName
    {
        get => _groupName;
        set
        {
            if (_groupName == value) return;
            _groupName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GroupDisplayName));
        }
    }
    public string GroupDisplayName => string.IsNullOrWhiteSpace(_groupName) ? "Ungrouped" : _groupName;
    public string GroupColor
    {
        get => _groupColor;
        set
        {
            if (_groupColor == value) return;
            _groupColor = value;
            OnPropertyChanged();
        }
    }
    public bool IgnoreImgVideoMode { get; set; }
    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (_isLocked == value) return;
            _isLocked = value;
            OnPropertyChanged();
        }
    }
    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            OnPropertyChanged();
        }
    }
    [JsonIgnore]
    public bool IsLayerListDragging
    {
        get => _isLayerListDragging;
        set
        {
            if (_isLayerListDragging == value) return;
            _isLayerListDragging = value;
            OnPropertyChanged();
        }
    }
    [JsonIgnore]
    public bool IsLayerDropBefore
    {
        get => _isLayerDropBefore;
        set
        {
            if (_isLayerDropBefore == value) return;
            _isLayerDropBefore = value;
            OnPropertyChanged();
        }
    }
    [JsonIgnore]
    public bool IsLayerDropAfter
    {
        get => _isLayerDropAfter;
        set
        {
            if (_isLayerDropAfter == value) return;
            _isLayerDropAfter = value;
            OnPropertyChanged();
        }
    }
    public string VisibilityTooltip { get; set; } = "";
    public string LockTooltip { get; set; } = "";
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
    public string Hide { get; set; } = "";
    public string FontGradientColor { get; set; } = "";
    public string FontGradientDirection { get; set; } = "";
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
    public string LineColor { get; set; } = "";
    public string FillColor { get; set; } = "";
    public string BorderColor { get; set; } = "";
    public string Transparent { get; set; } = "";
    public string UseGradient { get; set; } = "";
    public string GradientColor { get; set; } = "";
    public string ZoomRate { get; set; } = "";

    // Graph & Image Properties
    public string Rotate { get; set; } = "";
    public string ClockCenterX { get; set; } = "";
    public string ClockCenterY { get; set; } = "";
    public string ClockAngle { get; set; } = "";
    public string ClockEndAngle { get; set; } = "";
    public string ClockOffset { get; set; } = "";
    public string ClockRateOffset { get; set; } = "";
    public string ClockMoveOrigin { get; set; } = "";
    public string ClockOriginX { get; set; } = "";
    public string ClockOriginY { get; set; } = "";
    public string Rect { get; set; } = "";
    public string AlignmentIndex { get; set; } = "";
    public string AlignmentName { get; set; } = "";
    public string FontInterval { get; set; } = "";
    public string FontOrgSize { get; set; } = "";
    public string FontWidth { get; set; } = "";
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
    public string FrontAlpha { get; set; } = "";
    public string BackAlpha { get; set; } = "";
    public string TransparentBackground { get; set; } = "";
    public string MinValue { get; set; } = "";
    public string MaxValue { get; set; } = "";
    public string InvertDirection { get; set; } = "";
    public string StartPercentage { get; set; } = "";
    public string TotalAngle { get; set; } = "";
    public string UseBlock { get; set; } = "";
    public string RingBorder { get; set; } = "";
    public string Round { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string SubTypeName { get; set; } = "";
    public string RenderMode { get; set; } = "";
    public string ThemeMode { get; set; } = "";
    public string SensorStyle { get; set; } = "";
    public string SensorType { get; set; } = "";
    public string SensorColor1 { get; set; } = "";
    public string SensorColor2 { get; set; } = "";
    public string SensorBgColor { get; set; } = "";
    public string SensorMainFontColor { get; set; } = "";
    public string SensorTopFontColor { get; set; } = "";
    public string SensorBottomFontColor { get; set; } = "";
    public string SensorFontFamily { get; set; } = "";
    public string SensorZoomRate { get; set; } = "";

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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


