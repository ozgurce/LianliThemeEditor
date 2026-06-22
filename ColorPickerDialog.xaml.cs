using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ThemeEditorCSharp;

public partial class ColorPickerDialog : Window
{
    private enum ColorCodeFormat
    {
        Hex,
        Rgb
    }

    private bool _internalUpdate;
    private bool _draggingSv;
    private double _hue;
    private double _saturation = 0.68;
    private double _value = 0.9;
    private byte _alpha = 255;
    private Color _currentColor = Colors.White;
    private ColorCodeFormat _codeFormat = ColorCodeFormat.Hex;
    private Polygon? _selectedPaletteOutline;
    private readonly List<string> _savedColors = LoadSavedColors();

    public static Func<string, string, string>? TextProvider { get; set; }
    public string SelectedColorHex { get; private set; } = "#FFFFFF";

    private static readonly string[] DefaultSavedColors =
    {
        "#F94144", "#FACC15", "#22C55E", "#3B82F6", "#6366F1"
    };

    private static readonly string SavedColorsPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LianLiThemeEditor",
        "color-picker-saved.json");

    public ColorPickerDialog(string initialColor)
    {
        InitializeComponent();
        ApplyLanguage();
        BuildSavedColors();
        BuildHoneycombPalette();
        BuildCorePalette();
        BuildSelectedPreview();
        SetColor(initialColor);
        Loaded += (_, _) => UpdateSelectorPosition();
    }

    private void ApplyLanguage()
    {
        string Text(string key, string fallback) => TextProvider?.Invoke(key, fallback) ?? fallback;

        Title = Text("colorPicker.title", "Color Picker");
        HexLabel.Text = Text("colorPicker.code", "Color code");
        AlphaLabel.Text = Text("colorPicker.alpha", "Alpha");
        SavedColorsLabel.Text = Text("colorPicker.savedColors", "Saved colors:");
        AddSavedButton.Content = Text("colorPicker.addSaved", "+ Add");
        CancelButton.Content = Text("common.cancel", "Cancel");
        SelectButton.Content = Text("colorPicker.select", "Select");
        CloseButton.ToolTip = Text("common.close", "Close");
    }

    private void TitleSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideInteractiveElement(e.OriginalSource as DependencyObject)) return;
        try { DragMove(); }
        catch { }
    }

    private static bool IsInsideInteractiveElement(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is FrameworkElement { Name: "SvPicker" or "HoneycombCanvas" or "NeutralCanvas" or "SelectedHexCanvas" })
            {
                return true;
            }
            if (source is ButtonBase || source is TextBox || source is Slider || source is Thumb || source is Canvas)
            {
                return true;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void BuildSavedColors()
    {
        SavedColorsWrap.Children.Clear();
        foreach (var color in _savedColors.Take(5))
        {
            SavedColorsWrap.Children.Add(CreateCircleColorButton(color, 36, () => SetColor(color)));
        }
    }

    private Button CreateCircleColorButton(string colorText, double size, Action action)
    {
        var button = new Button
        {
            Width = size,
            Height = size,
            Margin = new Thickness(0, 0, 16, 12),
            Background = NewBrush(colorText),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = colorText
        };

        button.Template = BuildRoundButtonTemplate();
        button.Click += (_, _) => action();
        return button;
    }

    private static ControlTemplate BuildRoundButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var grid = new FrameworkElementFactory(typeof(Grid));

        var ring = new FrameworkElementFactory(typeof(Ellipse));
        ring.Name = "Ring";
        ring.SetValue(Shape.FillProperty, Brushes.Transparent);
        ring.SetValue(Shape.StrokeProperty, Brushes.Transparent);
        ring.SetValue(Shape.StrokeThicknessProperty, 4.0);
        grid.AppendChild(ring);

        var fill = new FrameworkElementFactory(typeof(Ellipse));
        fill.Name = "Fill";
        fill.SetValue(Shape.FillProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        fill.SetValue(FrameworkElement.MarginProperty, new Thickness(4));
        grid.AppendChild(fill);

        template.VisualTree = grid;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Shape.StrokeProperty, new SolidColorBrush(Color.FromRgb(190, 194, 202)), "Ring"));
        template.Triggers.Add(hover);
        return template;
    }

    private void BuildHoneycombPalette()
    {
        HoneycombCanvas.Children.Clear();

        const double radius = 11.5;
        const double gap = 1.2;
        var stepX = Math.Sqrt(3) * radius + gap;
        var stepY = 1.5 * radius + gap;
        var centerX = HoneycombCanvas.Width / 2;
        var centerY = HoneycombCanvas.Height / 2 - 6;
        const int ring = 6;

        for (var q = -ring; q <= ring; q++)
        {
            var r1 = Math.Max(-ring, -q - ring);
            var r2 = Math.Min(ring, -q + ring);
            for (var r = r1; r <= r2; r++)
            {
                var x = centerX + stepX * (q + r / 2.0);
                var y = centerY + stepY * r;
                var color = ColorFromHoneycomb(q, r, ring);
                AddHexCell(HoneycombCanvas, x, y, radius, color, true);
            }
        }
    }

    private void BuildCorePalette()
    {
        NeutralCanvas.Children.Clear();
        const double radius = 10.5;
        var colors = new[]
        {
            Colors.White, Color.FromRgb(160, 160, 160), Colors.Black, Colors.Red, Colors.Orange,
            Colors.Yellow, Colors.Lime, Colors.Cyan, Colors.Blue, Colors.Magenta
        };

        for (var i = 0; i < colors.Length; i++)
        {
            var row = i / 5;
            var column = i % 5;
            AddHexCell(NeutralCanvas, 18 + column * 30, 16 + row * 26, radius, colors[i], true);
        }
    }

    private void BuildSelectedPreview()
    {
        SelectedHexCanvas.Children.Clear();
        var shadow = BuildHexagon(25, 25, 23);
        shadow.Fill = Brushes.Transparent;
        shadow.Stroke = new SolidColorBrush(Color.FromRgb(79, 70, 229));
        shadow.StrokeThickness = 5;
        shadow.Cursor = Cursors.Hand;
        shadow.MouseLeftButtonDown += SelectedHexCanvas_MouseLeftButtonDown;
        SelectedHexCanvas.Children.Add(shadow);

        var fill = BuildHexagon(25, 25, 17);
        fill.Name = "SelectedPreviewHex";
        fill.Fill = new SolidColorBrush(_currentColor);
        fill.Stroke = Brushes.Transparent;
        fill.Cursor = Cursors.Hand;
        fill.MouseLeftButtonDown += SelectedHexCanvas_MouseLeftButtonDown;
        SelectedHexCanvas.Children.Add(fill);
    }

    private void AddHexCell(Canvas canvas, double centerX, double centerY, double radius, Color color, bool selectable)
    {
        var hex = BuildHexagon(centerX, centerY, radius);
        hex.Fill = new SolidColorBrush(color);
        hex.Stroke = Brushes.Transparent;
        hex.StrokeThickness = 0;
        hex.Cursor = selectable ? Cursors.Hand : Cursors.Arrow;
        if (selectable)
        {
            hex.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                SetColor(ColorToHex(color));
                MovePaletteOutline(canvas, centerX, centerY, radius);
            };
        }
        canvas.Children.Add(hex);
    }

    private void MovePaletteOutline(Canvas canvas, double centerX, double centerY, double radius)
    {
        if (_selectedPaletteOutline?.Parent is Canvas oldCanvas)
        {
            oldCanvas.Children.Remove(_selectedPaletteOutline);
        }
        _selectedPaletteOutline = BuildHexagon(centerX, centerY, radius - 3);
        _selectedPaletteOutline.Fill = Brushes.Transparent;
        _selectedPaletteOutline.Stroke = Brushes.White;
        _selectedPaletteOutline.StrokeThickness = 4;
        _selectedPaletteOutline.IsHitTestVisible = false;
        canvas.Children.Add(_selectedPaletteOutline);
    }

    private static Polygon BuildHexagon(double centerX, double centerY, double radius)
    {
        var polygon = new Polygon();
        for (var i = 0; i < 6; i++)
        {
            var angle = Math.PI / 180 * (60 * i - 30);
            polygon.Points.Add(new Point(centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle)));
        }
        return polygon;
    }

    private static Color ColorFromHoneycomb(int q, int r, int ring)
    {
        var x = q + r / 2.0;
        var y = r * Math.Sqrt(3) / 2.0;
        var angle = Math.Atan2(y, x) * 180 / Math.PI;
        if (angle < 0) angle += 360;

        var distance = Math.Sqrt(q * q + r * r + q * r) / ring;
        var saturation = Math.Clamp(distance, 0.12, 1.0);
        var value = Math.Clamp(1.08 - distance * 0.25, 0.45, 1.0);
        if (Math.Abs(q) + Math.Abs(r) < 2)
        {
            saturation = 0.08;
            value = 1.0;
        }
        return ColorFromHsv(angle, saturation, value, 255);
    }

    private void SetColor(string colorText)
    {
        var color = ParseColor(NormalizeColorText(colorText));
        var hsv = RgbToHsv(color);
        _hue = hsv.H;
        _saturation = hsv.S;
        _value = hsv.V;
        _alpha = color.A;
        _currentColor = color;
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (_internalUpdate) return;
        _internalUpdate = true;

        _currentColor = ColorFromHsv(_hue, _saturation, _value, _alpha);
        HueSurface.Background = new SolidColorBrush(ColorFromHsv(_hue, 1, 1, 255));
        HueSlider.Value = _hue;
        AlphaSlider.Value = Math.Round(_alpha / 255.0 * 100.0);
        AlphaGradient.Fill = new LinearGradientBrush(
            Color.FromArgb(0, _currentColor.R, _currentColor.G, _currentColor.B),
            Color.FromArgb(255, _currentColor.R, _currentColor.G, _currentColor.B),
            0);

        var hex = ColorToHex(_currentColor);
        HexInput.Text = ColorToHex(_currentColor);
        OpacityInput.Text = $"{Math.Round(_alpha / 255.0 * 100.0):0}%";
        RedInput.Text = _currentColor.R.ToString();
        GreenInput.Text = _currentColor.G.ToString();
        BlueInput.Text = _currentColor.B.ToString();
        RgbaAlphaInput.Text = $"{Math.Round(_alpha / 255.0 * 100.0):0}";
        SelectedColorHex = hex;
        ApplyCodeFieldColors(_currentColor);
        UpdateFormatButtons();

        foreach (var child in SelectedHexCanvas.Children.OfType<Polygon>())
        {
            if (child.Name == "SelectedPreviewHex") child.Fill = new SolidColorBrush(_currentColor);
        }

        UpdateSelectorPosition();
        _internalUpdate = false;
    }

    private void UpdateSelectorPosition()
    {
        if (SvPicker.ActualWidth <= 0 || SvPicker.ActualHeight <= 0) return;
        var x = _saturation * SvPicker.ActualWidth - SvSelector.Width / 2;
        var y = (1 - _value) * SvPicker.ActualHeight - SvSelector.Height / 2;
        SvSelector.Margin = new Thickness(Math.Clamp(x, -SvSelector.Width / 2, SvPicker.ActualWidth - SvSelector.Width / 2),
            Math.Clamp(y, -SvSelector.Height / 2, SvPicker.ActualHeight - SvSelector.Height / 2),
            0,
            0);
    }

    private void UpdateSvFromPoint(Point point)
    {
        _saturation = Math.Clamp(point.X / Math.Max(1, SvPicker.ActualWidth), 0, 1);
        _value = 1 - Math.Clamp(point.Y / Math.Max(1, SvPicker.ActualHeight), 0, 1);
        UpdateVisuals();
    }

    private void SvPicker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _draggingSv = true;
        SvPicker.CaptureMouse();
        UpdateSvFromPoint(e.GetPosition(SvPicker));
    }

    private void SvPicker_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingSv) return;
        e.Handled = true;
        UpdateSvFromPoint(e.GetPosition(SvPicker));
    }

    private void SvPicker_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _draggingSv = false;
        SvPicker.ReleaseMouseCapture();
    }

    private void HueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_internalUpdate) return;
        _hue = e.NewValue;
        UpdateVisuals();
    }

    private void AlphaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_internalUpdate) return;
        _alpha = (byte)Math.Clamp(Math.Round(e.NewValue / 100.0 * 255.0), 0, 255);
        UpdateVisuals();
    }

    private void HexInput_GotFocus(object sender, RoutedEventArgs e) => HexInput.SelectAll();

    private void HexInput_LostFocus(object sender, RoutedEventArgs e) => ApplyColorCodeInput();

    private void HexInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyColorCodeInput();
        Keyboard.ClearFocus();
    }

    private void ApplyColorCodeInput()
    {
        var text = HexInput.Text.Replace(" ", "").Trim();
        if (!text.StartsWith("#", StringComparison.Ordinal)) text = "#" + text;
        if (Regex.IsMatch(text, @"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$"))
        {
            SetColor(text);
            return;
        }

        UpdateVisuals();
    }

    private void RgbaInput_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox input) input.SelectAll();
    }

    private void RgbaInput_LostFocus(object sender, RoutedEventArgs e) => ApplyRgbaInputs();

    private void RgbaInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyRgbaInputs();
        Keyboard.ClearFocus();
    }

    private void ApplyRgbaInputs()
    {
        if (!byte.TryParse(RedInput.Text.Trim(), out var red) ||
            !byte.TryParse(GreenInput.Text.Trim(), out var green) ||
            !byte.TryParse(BlueInput.Text.Trim(), out var blue) ||
            !double.TryParse(RgbaAlphaInput.Text.Replace("%", "").Trim(), out var alphaPercent))
        {
            UpdateVisuals();
            return;
        }

        _alpha = (byte)Math.Clamp(Math.Round(Math.Clamp(alphaPercent, 0, 100) / 100.0 * 255.0), 0, 255);
        SetColor(ColorToHex(Color.FromArgb(_alpha, red, green, blue)));
    }

    private void HexFormatButton_Click(object sender, RoutedEventArgs e) => SetCodeFormat(ColorCodeFormat.Hex);

    private void RgbFormatButton_Click(object sender, RoutedEventArgs e) => SetCodeFormat(ColorCodeFormat.Rgb);

    private void SetCodeFormat(ColorCodeFormat format)
    {
        if (_codeFormat == format) return;
        if (_codeFormat == ColorCodeFormat.Hex) ApplyColorCodeInput();
        else ApplyRgbaInputs();
        _codeFormat = format;
        UpdateVisuals();
        if (_codeFormat == ColorCodeFormat.Hex) HexInput.Focus();
        else RedInput.Focus();
    }

    private void UpdateFormatButtons()
    {
        HexFormatButton.Background = (Brush)FindResource(_codeFormat == ColorCodeFormat.Hex ? "BrAccent" : "BrField");
        RgbFormatButton.Background = (Brush)FindResource(_codeFormat == ColorCodeFormat.Rgb ? "BrAccent" : "BrField");
        HexFormatButton.Foreground = _codeFormat == ColorCodeFormat.Hex ? Brushes.White : (Brush)FindResource("BrTextSecondary");
        RgbFormatButton.Foreground = _codeFormat == ColorCodeFormat.Rgb ? Brushes.White : (Brush)FindResource("BrTextSecondary");
        HexFieldsPanel.Visibility = _codeFormat == ColorCodeFormat.Hex ? Visibility.Visible : Visibility.Collapsed;
        RgbaFieldsPanel.Visibility = _codeFormat == ColorCodeFormat.Rgb ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpacityInput_LostFocus(object sender, RoutedEventArgs e) => ApplyOpacityInput();

    private void OpacityInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyOpacityInput();
        Keyboard.ClearFocus();
    }

    private void ApplyOpacityInput()
    {
        var raw = OpacityInput.Text.Replace("%", "").Trim();
        if (!double.TryParse(raw, out var percent)) percent = 100;
        _alpha = (byte)Math.Clamp(Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * 255.0), 0, 255);
        UpdateVisuals();
    }

    private void AddSavedButton_Click(object sender, RoutedEventArgs e)
    {
        var savedColor = SelectedColorHex;
        _savedColors.RemoveAll(color => string.Equals(color, savedColor, StringComparison.OrdinalIgnoreCase));
        _savedColors.Add(savedColor);
        while (_savedColors.Count > 5)
        {
            _savedColors.RemoveAt(0);
        }
        SaveSavedColors(_savedColors);
        BuildSavedColors();
    }

    private void SelectedHexCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Select_Click(sender, e);
    }

    private void ClearPaletteOutline()
    {
        if (_selectedPaletteOutline?.Parent is Canvas oldCanvas)
        {
            oldCanvas.Children.Remove(_selectedPaletteOutline);
        }
        _selectedPaletteOutline = null;
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        SelectedColorHex = ColorToHex(_currentColor);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public static string? ShowDialog(Window owner, string initialColor)
    {
        var picker = new ColorPickerDialog(initialColor) { Owner = owner };
        return picker.ShowDialog() == true ? picker.SelectedColorHex : null;
    }

    private static Brush NewBrush(string colorText)
    {
        return new SolidColorBrush(ParseColor(colorText));
    }

    private static Color ParseColor(string value)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(NormalizeColorText(value));
        }
        catch
        {
            return Colors.White;
        }
    }

    private static List<string> LoadSavedColors()
    {
        try
        {
            if (File.Exists(SavedColorsPath))
            {
                var colors = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(SavedColorsPath)) ?? [];
                var normalized = colors
                    .Select(color => ColorToHex(ParseColor(color)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .TakeLast(5)
                    .ToList();
                if (normalized.Count > 0) return normalized;
            }
        }
        catch
        {
        }

        return DefaultSavedColors.ToList();
    }

    private static void SaveSavedColors(IReadOnlyCollection<string> colors)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SavedColorsPath)!);
            File.WriteAllText(
                SavedColorsPath,
                JsonSerializer.Serialize(colors.TakeLast(5).ToArray(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private static string NormalizeColorText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "#FFFFFF";
        value = value.Trim().Replace(" ", "");
        if (value.StartsWith("#", StringComparison.Ordinal))
        {
            if (value.Length == 4)
            {
                return $"#{value[1]}{value[1]}{value[2]}{value[2]}{value[3]}{value[3]}";
            }
            return value;
        }

        var match = Regex.Match(value, @"A=(?<a>\d+),\s*R=(?<r>\d+),\s*G=(?<g>\d+),\s*B=(?<b>\d+)");
        if (match.Success)
        {
            var a = byte.Parse(match.Groups["a"].Value);
            var r = byte.Parse(match.Groups["r"].Value);
            var g = byte.Parse(match.Groups["g"].Value);
            var b = byte.Parse(match.Groups["b"].Value);
            return a == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{a:X2}{r:X2}{g:X2}{b:X2}";
        }
        return "#" + value;
    }

    private static string ColorToHex(Color color)
    {
        return color.A == 255
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private void ApplyCodeFieldColors(Color color)
    {
        var opaque = Color.FromRgb(color.R, color.G, color.B);
        HexInput.Background = new SolidColorBrush(opaque);
        HexInput.BorderBrush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
        HexInput.Foreground = luminance > 0.58 ? Brushes.Black : Brushes.White;
        HexInput.CaretBrush = HexInput.Foreground;
    }

    private static Color ColorFromHsv(double hue, double saturation, double value, byte alpha)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60.0) % 2 - 1));
        var m = value - c;

        var (r1, g1, b1) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        return Color.FromArgb(
            alpha,
            (byte)Math.Round((r1 + m) * 255),
            (byte)Math.Round((g1 + m) * 255),
            (byte)Math.Round((b1 + m) * 255));
    }

    private static (double H, double S, double V) RgbToHsv(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        var hue = delta == 0 ? 0 :
            max == r ? 60 * (((g - b) / delta) % 6) :
            max == g ? 60 * (((b - r) / delta) + 2) :
            60 * (((r - g) / delta) + 4);
        if (hue < 0) hue += 360;

        var saturation = max == 0 ? 0 : delta / max;
        return (hue, saturation, max);
    }
}
