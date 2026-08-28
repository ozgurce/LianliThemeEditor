using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;

namespace UsbMonitorL;

#pragma warning disable CS0169, CS0414

[Serializable]
public sealed class Theme
{
    public bool setColor { get; set; }
    public Color frontColor { get; set; }
    public Color backColor { get; set; }
    public bool isVisualTheme { get; set; }
    public bool isTempTheme { get; set; }
    public bool isAidaTheme { get; set; }
    public bool isAidaTransparent { get; set; }
    public string? aidaLoadMark { get; set; }
    public bool reload { get; set; }
    public string name { get; set; } = "";
    public bool isLanscape { get; set; }
    public int width { get; set; }
    public int height { get; set; }
    public string themePath { get; set; } = "";
    public Bitmap? themePic { get; set; }
    public string? videoPath { get; set; }
    public string? o_videoPath { get; set; }
    public string? videoTargetPath { get; set; }
    public string? videoName { get; set; }
    public int FrameRate { get; set; } = 60;
    public ObservableCollection<GraphItem> GraphList { get; set; } = new();
}

[Serializable]
public class GraphItem
{
    public List<string> AcceptDataList { get; set; } = new();
    public bool hide { get; set; }
    public bool useGradient { get; set; }
    public bool enabled { get; set; } = true;
    public string TypeName { get; set; } = "";
    public string? SubTypeName { get; set; }
    private string _DisplayName = "";
    public int posX { get; set; }
    public int posY { get; set; }
    public bool revert { get; set; }
    public bool fahrenheit { get; set; }
    public M_Data m_data { get; set; } = new();
    public FontConfig fontConfig { get; set; } = new();
}

[Serializable]
public sealed class GraphAnimation : GraphItem
{
    public double step;
    public double zoom_rate { get; set; } = 1;
    public int midLevel;
    public Bitmap? bitmap { get; set; }
    public Bitmap? O_bitmap { get; set; }
    public Bitmap? S_bitmap { get; set; }
    public string? videoName { get; set; }
    public TransFormInfo? crop { get; set; }
    public int direction { get; set; }
    public string FilePath { get; set; } = "";
    public bool potritMode { get; set; }
    public int ration { get; set; }
    public int SWith;
    public int SHeight;
}

[Serializable]
public class GraphImage : GraphItem
{
    public double step;
    public double zoom_rate { get; set; } = 1;
    public Bitmap? bitmap { get; set; }
    public Bitmap? O_bitmap { get; set; }
    public string ImgName { get; set; } = "";
}

[Serializable]
public sealed class GraphClock : GraphImage
{
    public int centerX { get; set; }
    public int centerY { get; set; }
    public float lastRate { get; set; }
    public int o_Y { get; set; }
    public int o_X { get; set; }
    public int angle { get; set; }
    public int endAngle { get; set; }
    public float offset { get; set; }
    public bool moveOpoint { get; set; }
    public new bool revert { get; set; }
    public Bitmap? tmp { get; set; }
    public float AngleOffset { get; set; }
}

[Serializable]
public class GraphStatuBar : GraphItem
{
    public int direction { get; set; }
    public bool trBack { get; set; }
    public new bool useGradient { get; set; }
    public bool fillBack { get; set; }
    public int lineWidth { get; set; }
    public int width { get; set; }
    public int height { get; set; }
    public int radius { get; set; }
    public Color FrontColor { get; set; }
    public byte FrontAlpha { get; set; } = 255;
    public Color BackColor { get; set; }
    public byte BackAlpha { get; set; } = 255;
    public Color GradientColor { get; set; }
    public bool useSubsection { get; set; }
    public int SplitBlockWidth { get; set; }
    public int SplitBlankWidth { get; set; }
}

[Serializable]
public sealed class GraphDynamicBar : GraphStatuBar
{
    public int InnerCircleRadius { get; set; }
}

[Serializable]
public sealed class GraphArchBar : GraphItem
{
    public bool useBlock { get; set; }
    public new bool revert { get; set; }
    public bool round { get; set; }
    public bool trBack { get; set; }
    public bool fillBack { get; set; }
    public int archWidth { get; set; }
    public int lineWidth { get; set; }
    public int diameter { get; set; }
    public int height { get; set; }
    public int startPer { get; set; }
    public int totalAngel { get; set; }
    public Color FrontColor { get; set; }
    public byte FrontAlpha { get; set; } = 255;
    public Color BackColor { get; set; }
    public byte BackAlpha { get; set; } = 255;
    public int SplitBlockWidth { get; set; }
    public int SplitBlankWidth { get; set; }
    public bool HasRingBorder { get; set; }
    public Color GradientColor { get; set; }
}

[Serializable]
public sealed class GraphLine : GraphItem
{
    public Color LineColor { get; set; }
    public Color FillColor { get; set; }
    public Color BorderColor { get; set; }
    public int lineWidth { get; set; }
    public bool rollDirection { get; set; }
    private int _width;
    public float maxValue { get; set; }
    private int _height;
    public int borderWidth { get; set; }
    public int columnWidth { get; set; }
    public float coefficient { get; set; }
}

[Serializable]
public sealed class FontConfig
{
    public bool isBold { get; set; }
    public string name { get; set; } = "";
    public int size { get; set; }
    public float interval { get; set; }
    public Color color { get; set; }
    public Color GrColor { get; set; }
    public int GrDirection { get; set; }
    public TextAlignment alignment { get; set; } = new();
}

[Serializable]
public sealed class M_Data
{
    public string content { get; set; } = "";
    public Queue<string> DataQueue { get; set; } = new();
    public int queueLen { get; set; }
    public bool ShowUnit { get; set; }
    public string DataName { get; set; } = "";
    public string Sanma_Eng_Name { get; set; } = "";
    public string SubName { get; set; } = "";
    public string b_DataName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public double Rate { get; set; }
    public string ValueWithUnit { get; set; } = "";
    private string _Value = "";
    private bool _IsEnabled = true;
}

[Serializable]
public sealed class TextAlignment
{
    public string displayName { get; set; } = "Middle";
    public int index { get; set; } = 1;
}

[Serializable]
public sealed class TransFormInfo
{
    public int ret { get; set; }
    public int rotate { get; set; }
    public Rectangle rect { get; set; }
}
