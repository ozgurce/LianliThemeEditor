using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ThemeEditorCSharp;

public partial class ColorPickerDialog : Window
{
    private bool _internalUpdate;
    private static readonly string[] Presets = {
        "#FFFFFF", "#000000", "#FF0000", "#00FF00", "#0000FF", "#FFFF00", "#FF00FF", "#00FFFF",
        "#FFA500", "#800080", "#FFC0CB", "#A52A2A", "#808080", "#D3D3D3",
        // Opacity Levels (for White and Black)
        "#CCFFFFFF", "#99FFFFFF", "#66FFFFFF", "#33FFFFFF",
        "#CC000000", "#99000000", "#66000000", "#33000000"
    };

    public string SelectedColorHex { get; private set; } = "#FFFFFF";

    public ColorPickerDialog(string initialColor)
    {
        InitializeComponent();
        InitializePresets();
        SetColor(initialColor);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        try
        {
            DragMove();
        }
        catch
        {
            // Ignore
        }
    }

    private void InitializePresets()
    {
        foreach (var preset in Presets)
        {
            var brush = NewBrush(preset);
            var btn = new Button
            {
                Style = (Style)Resources["PaletteButton"],
                Background = brush,
                ToolTip = preset
            };
            btn.Click += (s, e) => SetColor(preset);
            PresetWrap.Children.Add(btn);
        }
    }

    private void SetColor(string colorHex)
    {
        colorHex = NormalizeColorText(colorHex);
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(colorHex);
            _internalUpdate = true;

            RedSlider.Value = color.R;
            GreenSlider.Value = color.G;
            BlueSlider.Value = color.B;
            AlphaSlider.Value = color.A;

            RedInput.Text = color.R.ToString();
            GreenInput.Text = color.G.ToString();
            BlueInput.Text = color.B.ToString();
            AlphaInput.Text = color.A.ToString();

            HexInput.Text = colorHex;
            ColorPreviewBorder.Background = new SolidColorBrush(color);

            _internalUpdate = false;
        }
        catch
        {
            _internalUpdate = false;
        }
    }

    private void UpdateFromSliders()
    {
        if (_internalUpdate) return;
        _internalUpdate = true;

        byte r = (byte)RedSlider.Value;
        byte g = (byte)GreenSlider.Value;
        byte b = (byte)BlueSlider.Value;
        byte a = (byte)AlphaSlider.Value;

        RedInput.Text = r.ToString();
        GreenInput.Text = g.ToString();
        BlueInput.Text = b.ToString();
        AlphaInput.Text = a.ToString();

        var color = Color.FromArgb(a, r, g, b);
        var hex = a == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{a:X2}{r:X2}{g:X2}{b:X2}";
        HexInput.Text = hex;
        ColorPreviewBorder.Background = new SolidColorBrush(color);

        _internalUpdate = false;
    }

    private void UpdateFromInputs()
    {
        if (_internalUpdate) return;

        byte r = byte.TryParse(RedInput.Text, out var vr) ? vr : (byte)0;
        byte g = byte.TryParse(GreenInput.Text, out var vg) ? vg : (byte)0;
        byte b = byte.TryParse(BlueInput.Text, out var vb) ? vb : (byte)0;
        byte a = byte.TryParse(AlphaInput.Text, out var va) ? va : (byte)255;

        _internalUpdate = true;

        RedSlider.Value = r;
        GreenSlider.Value = g;
        BlueSlider.Value = b;
        AlphaSlider.Value = a;

        var color = Color.FromArgb(a, r, g, b);
        var hex = a == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{a:X2}{r:X2}{g:X2}{b:X2}";
        HexInput.Text = hex;
        ColorPreviewBorder.Background = new SolidColorBrush(color);

        _internalUpdate = false;
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateFromSliders();
    }

    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateFromInputs();
    }

    private void HexInput_GotFocus(object sender, RoutedEventArgs e)
    {
        // Highlight hex input text
        HexInput.SelectAll();
    }

    private void HexInput_LostFocus(object sender, RoutedEventArgs e)
    {
        var text = HexInput.Text.Trim();
        if (!text.StartsWith("#")) text = "#" + text;
        SetColor(text);
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        byte r = (byte)RedSlider.Value;
        byte g = (byte)GreenSlider.Value;
        byte b = (byte)BlueSlider.Value;
        byte a = (byte)AlphaSlider.Value;
        SelectedColorHex = a == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{a:X2}{r:X2}{g:X2}{b:X2}";
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
        if (picker.ShowDialog() == true)
        {
            return picker.SelectedColorHex;
        }
        return null;
    }

    private static Brush NewBrush(string colorText)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorText));
        }
        catch
        {
            return Brushes.White;
        }
    }

    private static string NormalizeColorText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "#FFFFFF";
        if (value.StartsWith("#", StringComparison.Ordinal)) return value;
        var match = Regex.Match(value, @"A=(?<a>\d+),\s*R=(?<r>\d+),\s*G=(?<g>\d+),\s*B=(?<b>\d+)");
        if (match.Success)
        {
            var a = byte.Parse(match.Groups["a"].Value);
            var r = byte.Parse(match.Groups["r"].Value);
            var g = byte.Parse(match.Groups["g"].Value);
            var b = byte.Parse(match.Groups["b"].Value);
            return a == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{a:X2}{r:X2}{g:X2}{b:X2}";
        }
        return value;
    }
}
