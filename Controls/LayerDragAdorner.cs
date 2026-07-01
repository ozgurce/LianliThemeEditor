using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.Controls;

public sealed class LayerDragAdorner : Adorner
{
    private readonly VisualCollection _visuals;
    private readonly Border _root;
    private double _left;
    private double _top;

    public LayerDragAdorner(UIElement adornedElement, IReadOnlyList<LayerRow> layers)
        : base(adornedElement)
    {
        _root = BuildCard(layers);
        _visuals = new VisualCollection(this) { _root };
        IsHitTestVisible = false;
    }

    public void SetPosition(double left, double top)
    {
        _left = left + 18;
        _top = top + 12;
        InvalidateArrange();
        InvalidateVisual();
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override Size MeasureOverride(Size constraint)
    {
        _root.Measure(constraint);
        return _root.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _root.Arrange(new Rect(new Point(_left, _top), _root.DesiredSize));
        return finalSize;
    }

    public override GeneralTransform GetDesiredTransform(GeneralTransform transform)
    {
        return base.GetDesiredTransform(transform);
    }

    private static Border BuildCard(IReadOnlyList<LayerRow> layers)
    {
        var primary = layers.FirstOrDefault();
        var title = primary == null
            ? "Layer"
            : layers.Count == 1
                ? primary.LayerDataTitle
                : $"{layers.Count} layers";
        var subtitle = primary == null
            ? ""
            : layers.Count == 1
                ? primary.LayerTypeSubtitle
                : "Move selection";

        var icon = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(8),
            Background = CreateBrush(primary?.IconColor, "#246FF2"),
            Child = new TextBlock
            {
                Text = layers.Count > 1 ? layers.Count.ToString() : "#",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var text = new StackPanel { Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(title) ? "Layer" : title,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            MaxWidth = 190,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = new SolidColorBrush(Color.FromRgb(178, 201, 232)),
            FontSize = 10,
            MaxWidth = 190,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(icon);
        content.Children.Add(text);

        return new Border
        {
            Padding = new Thickness(10, 8, 12, 8),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(235, 18, 44, 88)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(210, 75, 147, 255)),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 8,
                Direction = 270,
                Opacity = 0.45,
                Color = Colors.Black
            },
            Child = content
        };
    }

    private static Brush CreateBrush(string? color, string fallback)
    {
        try
        {
            return (Brush)new BrushConverter().ConvertFromString(
                string.IsNullOrWhiteSpace(color) ? fallback : color)!;
        }
        catch
        {
            return (Brush)new BrushConverter().ConvertFromString(fallback)!;
        }
    }
}
