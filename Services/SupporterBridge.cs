using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.Services;

public sealed class SupporterBridge : ISupporterBridge
{
    private static readonly Regex ColorAlphaRegex = new(@"A=(\d+)", RegexOptions.Compiled);
    private readonly SemaphoreSlim _supporterGate = new(1, 1);
    private readonly string _supporterPath;
    private readonly string _workingDirectory;

    public SupporterBridge()
    {
        var appDir = AppContext.BaseDirectory;
        var configuredPath = Environment.GetEnvironmentVariable("LIANLI_THEME_SUPPORTER");
        var candidatePaths = new[]
        {
            configuredPath,
            Path.Combine(appDir, "LianLiThemeEditor.TemplateWorker.exe"),
            Path.Combine(appDir, "supporter.exe"),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "SupporterCs", "bin", "Debug", "net48", "LianLiThemeEditor.TemplateWorker.exe")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "SupporterCs", "bin", "Release", "net48", "LianLiThemeEditor.TemplateWorker.exe")),
            Path.GetFullPath(Path.Combine(appDir, "..", "..", "..", "..", "ThemeEditor", "EXE", "supporter.exe")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "ThemeEditor", "EXE", "supporter.exe"))
        };

        foreach (var path in candidatePaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(path!);
            if (File.Exists(fullPath))
            {
                _supporterPath = fullPath;
                var dir = Path.GetDirectoryName(fullPath)!;
                if (Directory.Exists(Path.Combine(dir, "lang")))
                {
                    _workingDirectory = dir;
                }
                else
                {
                    var parent = Directory.GetParent(dir)?.FullName;
                    if (parent != null && Directory.Exists(Path.Combine(parent, "lang")))
                    {
                        _workingDirectory = parent;
                    }
                    else
                    {
                        _workingDirectory = dir;
                    }
                }
                return;
            }
        }

        throw new FileNotFoundException(
            "Could not find the C# supporter. Build the SupporterCs project or set LIANLI_THEME_SUPPORTER.");
    }

    public string SupporterPath => _supporterPath;
    public string WorkingDirectory => _workingDirectory;

    public Task<IReadOnlyList<string>> ListFontsAsync(CancellationToken cancellationToken = default)
    {
        return RunLinesAsync(new[] { "-ListFonts" }, cancellationToken);
    }

    public async Task<IReadOnlyList<GraphStyleOption>> ListGraphStylesAsync(CancellationToken cancellationToken = default)
    {
        var json = await RunSupporterAsync(new[] { "-ListGraphStyles", "-Json" }, cancellationToken).ConfigureAwait(false);
        var styles = JsonSerializer.Deserialize<List<GraphStyleOption>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        return styles ?? new List<GraphStyleOption>();
    }

    private static string NormalizeLayerText(string value) =>
        (value ?? "").Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

    private List<string> BuildApplyLayerArgs(string deviceModel, string templatePath, LayerRow layer)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[]
        {
            "-LayerIndex", layer.Index,
            "-LayerX", layer.X,
            "-LayerY", layer.Y
        });

        var type = layer.Type ?? "";
        var isText = type.Equals("GraphItem", StringComparison.OrdinalIgnoreCase);
        var isGraph = type.Equals("GraphStatuBar", StringComparison.OrdinalIgnoreCase) ||
                      type.Equals("GraphArchBar", StringComparison.OrdinalIgnoreCase) ||
                      type.Equals("GraphDynamicBar", StringComparison.OrdinalIgnoreCase) ||
                      type.Equals("GraphLine", StringComparison.OrdinalIgnoreCase);
        var isSensor = type.Equals("GraphSensor", StringComparison.OrdinalIgnoreCase);
        var isImage = type.Equals("GraphImage", StringComparison.OrdinalIgnoreCase);
        var isAnimation = type.Equals("GraphAnimation", StringComparison.OrdinalIgnoreCase);
        var isClock = type.Equals("GraphClock", StringComparison.OrdinalIgnoreCase);

        AddIfPresent(args, "-LayerHide", layer.Hide);

        if (isText)
        {
            if (layer.CanWriteFont("size") && !string.IsNullOrWhiteSpace(layer.Size)) { args.Add("-LayerSize"); args.Add(layer.Size); }
            if (layer.CanWriteFont("color") && !string.IsNullOrWhiteSpace(layer.Color)) { args.Add("-LayerColor"); args.Add(layer.Color); }
            if (layer.CanWriteFont("name") && !string.IsNullOrWhiteSpace(layer.Font)) { args.Add("-LayerFont"); args.Add(layer.Font); }
            if (layer.CanWriteFont("isBold") && !string.IsNullOrWhiteSpace(layer.Bold)) { args.Add("-LayerBold"); args.Add(layer.Bold); }
            if (layer.CanWriteFont("IsItalic") && !string.IsNullOrWhiteSpace(layer.Italic)) { args.Add("-LayerItalic"); args.Add(layer.Italic); }
            AddIfPresent(
                args,
                "-LayerAlignmentIndex",
                layer.AlignmentIndex,
                layer.CanWriteFont("alignment.index") ||
                string.Equals(layer.Type, "GraphItem", StringComparison.OrdinalIgnoreCase));
            AddIfPresent(args, "-LayerFontInterval", layer.FontInterval, layer.CanWriteFont("interval"));
            AddIfPresent(args, "-LayerFontGradientColor", layer.FontGradientColor, layer.CanWriteFont("GrColor"));
            AddIfPresent(args, "-LayerFontGradientDirection", layer.FontGradientDirection, layer.CanWriteFont("GrDirection"));
            if (!string.IsNullOrWhiteSpace(layer.DataSource) &&
                (!string.Equals(layer.DataSource, layer.OriginalDataSource, StringComparison.OrdinalIgnoreCase) ||
                 ShouldRebindNativeSource(layer.DataSource)))
            {
                args.Add("-LayerDataSource");
                args.Add(NormalizeDataSource(layer.DataSource));
            }
        }

        var source = layer.DataSource ?? "";
        if (isText && SupportsFormat(source))
        {
            var format = string.IsNullOrWhiteSpace(layer.Format) ? DefaultFormatForDataSource(source) : layer.Format;
            if (!string.IsNullOrWhiteSpace(format))
            {
                args.Add("-LayerFormat");
                args.Add(NormalizeFormatForDataSource(source, format));
            }
        }
        else if (isText && (source == "StaticText" || layer.ForceText))
        {
            args.Add("-LayerText");
            args.Add(string.IsNullOrEmpty(layer.Text) ? " " : NormalizeLayerText(layer.Text));
        }

        if (isGraph &&
            !string.IsNullOrWhiteSpace(layer.GraphStyle) &&
            !string.Equals(layer.GraphStyle, layer.OriginalGraphStyle, StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-LayerGraphStyle");
            args.Add(layer.GraphStyle);
        }

        if (isGraph)
        {
            if (!string.IsNullOrWhiteSpace(layer.DataSource) &&
                (!string.Equals(layer.DataSource, layer.OriginalDataSource, StringComparison.OrdinalIgnoreCase) ||
                 ShouldRebindNativeSource(layer.DataSource)))
            {
                args.Add("-LayerDataSource");
                args.Add(NormalizeDataSource(layer.DataSource));
            }
            if (layer.CanWrite("width") && !string.IsNullOrWhiteSpace(layer.Width)) { args.Add("-LayerWidth"); args.Add(layer.Width); }
            if (layer.CanWrite("height") && !string.IsNullOrWhiteSpace(layer.Height)) { args.Add("-LayerHeight"); args.Add(layer.Height); }
            if (layer.CanWrite("radius") && !string.IsNullOrWhiteSpace(layer.Radius)) { args.Add("-LayerRadius"); args.Add(layer.Radius); }
            if (layer.CanWrite("diameter") && !string.IsNullOrWhiteSpace(layer.Diameter)) { args.Add("-LayerDiameter"); args.Add(layer.Diameter); }
            if (layer.CanWrite("archWidth") && !string.IsNullOrWhiteSpace(layer.Thickness)) { args.Add("-LayerThickness"); args.Add(layer.Thickness); }
            if ((layer.CanWrite("FrontColor") || layer.CanWrite("LineColor") || layer.CanWrite("FillColor")) && !string.IsNullOrWhiteSpace(layer.FrontColor)) { args.Add("-LayerFrontColor"); args.Add(layer.FrontColor); }
            if ((layer.CanWrite("BackColor") || layer.CanWrite("BorderColor")) && !string.IsNullOrWhiteSpace(layer.BackColor)) { args.Add("-LayerBackColor"); args.Add(layer.BackColor); }
            AddIfPresent(args, "-LayerLineColor", layer.LineColor, layer.CanWrite("LineColor"));
            AddIfPresent(args, "-LayerFillColor", layer.FillColor, layer.CanWrite("FillColor"));
            AddIfPresent(args, "-LayerBorderColor", layer.BorderColor, layer.CanWrite("BorderColor"));
            AddIfPresent(args, "-LayerFillAlpha", layer.Transparent, layer.CanWrite("FillColor"));
            var supportsGraphGradient = !string.Equals(layer.Type, "GraphDynamicBar", StringComparison.OrdinalIgnoreCase);
            if (supportsGraphGradient && layer.CanWrite("GradientColor") && !string.IsNullOrWhiteSpace(layer.GradientColor)) { args.Add("-LayerGradientColor"); args.Add(layer.GradientColor); }
            if (supportsGraphGradient && layer.CanWrite("useGradient") && !string.IsNullOrWhiteSpace(layer.UseGradient)) { args.Add("-LayerUseGradient"); args.Add(layer.UseGradient); }
            AddIfPresent(args, "-LayerDirection", layer.Direction, layer.CanWrite("direction"));
            AddIfPresent(args, "-LayerLineWidth", layer.LineWidth, layer.CanWrite("lineWidth"));
            AddIfPresent(args, "-LayerColumnWidth", layer.ColumnWidth, layer.CanWrite("columnWidth"));
            AddIfPresent(args, "-LayerBorderWidth", layer.BorderWidth, layer.CanWrite("borderWidth"));
            AddIfPresent(args, "-LayerInnerCircleRadius", layer.InnerCircleRadius, layer.CanWrite("InnerCircleRadius"));
            AddIfPresent(args, "-LayerSplitBlockWidth", layer.SplitBlockWidth, layer.CanWrite("SplitBlockWidth"));
            AddIfPresent(args, "-LayerSplitBlankWidth", layer.SplitBlankWidth, layer.CanWrite("SplitBlankWidth"));
            AddIfPresent(args, "-LayerUseSubsection", layer.UseSubsection, layer.CanWrite("useSubsection"));
            AddIfPresent(args, "-LayerFillBack", layer.FillBack, layer.CanWrite("fillBack"));
            AddIfPresent(args, "-LayerRevert", layer.Revert, layer.CanWrite("revert"));
            AddIfPresent(args, "-LayerFrontAlpha", layer.FrontAlpha, layer.CanWrite("FrontAlpha"));
            AddIfPresent(args, "-LayerBackAlpha", layer.BackAlpha, layer.CanWrite("BackAlpha"));
            AddIfPresent(args, "-LayerTransparentBackground", layer.TransparentBackground, layer.CanWrite("trBack"));
            AddIfPresent(args, "-LayerMinValue", layer.MinValue, layer.CanWrite("minValue"));
            AddIfPresent(args, "-LayerMaxValue", layer.MaxValue, layer.CanWrite("maxValue"));
            AddIfPresent(args, "-LayerInvertDirection", layer.InvertDirection, layer.CanWrite("rollDirection"));
            AddIfPresent(args, "-LayerStartPercentage", layer.StartPercentage, layer.CanWrite("startPer"));
            AddIfPresent(args, "-LayerTotalAngle", layer.TotalAngle, layer.CanWrite(ThemeEngineNames.GraphArchBarTotalAngle));
            AddIfPresent(args, "-LayerUseBlock", layer.UseBlock, layer.CanWrite("useBlock"));
            AddIfPresent(args, "-LayerRingBorder", layer.RingBorder, layer.CanWrite("HasRingBorder"));
            AddIfPresent(args, "-LayerRound", layer.Round, layer.CanWrite("round"));
        }

        if (isSensor)
        {
            AddIfPresent(args, "-LayerDataSource", NormalizeDataSource(layer.DataSource ?? ""));
            AddIfPresent(args, "-LayerSensorStyle", layer.SensorStyle);
            AddIfPresent(args, "-LayerSensorType", layer.SensorType);
            AddIfPresent(args, "-LayerSensorColor1", layer.SensorColor1);
            AddIfPresent(args, "-LayerSensorColor2", layer.SensorColor2);
            AddIfPresent(args, "-LayerSensorBgColor", layer.SensorBgColor);
            AddIfPresent(args, "-LayerSensorMainFontColor", layer.SensorMainFontColor);
            AddIfPresent(args, "-LayerSensorTopFontColor", layer.SensorTopFontColor);
            AddIfPresent(args, "-LayerSensorBottomFontColor", layer.SensorBottomFontColor);
            AddIfPresent(args, "-LayerSensorFont", layer.SensorFontFamily);
            AddIfPresent(args, "-LayerZoomRate", layer.ZoomRate);
            AddIfPresent(args, "-LayerSensorZoom", layer.SensorZoomRate);
            AddIfPresent(args, "-LayerText", string.IsNullOrWhiteSpace(layer.Text) ? "52" : NormalizeLayerText(layer.Text));
        }

        if (isImage || isAnimation || isClock)
        {
            if (ShouldSendLayerMedia(deviceModel, layer, isAnimation, isClock))
            {
                args.Add("-LayerImgName");
                args.Add(layer.Media);
            }
            if (!string.IsNullOrWhiteSpace(layer.ZoomRate)) { args.Add("-LayerZoomRate"); args.Add(layer.ZoomRate); }
            AddIfPresent(args, "-LayerRotate", layer.Rotate,
                isImage || isClock ||
                layer.CanWrite(ThemeEngineNames.TransformRotation) ||
                layer.CanWrite(ThemeEngineNames.GraphAnimationRotation));
            AddIfPresent(args, "-LayerRect", layer.Rect, layer.CanWrite("rect"));
            if (isClock)
            {
                if (!string.IsNullOrWhiteSpace(layer.DataSource) &&
                    (!string.Equals(layer.DataSource, layer.OriginalDataSource, StringComparison.OrdinalIgnoreCase) ||
                     ShouldRebindNativeSource(layer.DataSource)))
                {
                    args.Add("-LayerDataSource");
                    args.Add(NormalizeDataSource(layer.DataSource));
                }
                if (!string.IsNullOrWhiteSpace(layer.Format))
                {
                    args.Add("-LayerFormat");
                    args.Add(NormalizeFormatForDataSource(layer.DataSource ?? "", layer.Format));
                }
                AddIfPresent(args, "-LayerClockCenterX", layer.ClockCenterX, layer.CanWrite("centerX"));
                AddIfPresent(args, "-LayerClockCenterY", layer.ClockCenterY, layer.CanWrite("centerY"));
                AddIfPresent(args, "-LayerClockAngle", layer.ClockAngle, layer.CanWrite("angle"));
                AddIfPresent(args, "-LayerClockEndAngle", layer.ClockEndAngle, layer.CanWrite("endAngle"));
                AddIfPresent(args, "-LayerClockOffset", layer.ClockOffset, layer.CanWrite("offset"));
                // moveOpoint is an editor drag-mode flag. Persisting true changes the
                // ThemeEngine render coordinate system and sends the hand off-canvas.
                AddIfPresent(args, "-LayerClockOriginX", layer.ClockOriginX, layer.CanWrite("o_X"));
                AddIfPresent(args, "-LayerClockOriginY", layer.ClockOriginY, layer.CanWrite("o_Y"));
                AddIfPresent(args, "-LayerRevert", layer.Revert, layer.CanWrite("revert"));
            }
        }

        return args;
    }

    private static bool ShouldSendLayerMedia(string deviceModel, LayerRow layer, bool isAnimation, bool isClock)
    {
        if (string.IsNullOrWhiteSpace(layer.Media))
        {
            return false;
        }

        if (isAnimation || isClock)
        {
            return true;
        }

        var mediaPath = layer.MediaPath;
        if (!string.IsNullOrWhiteSpace(mediaPath) && File.Exists(mediaPath))
        {
            var imageRoot = Path.GetFullPath(Path.Combine(LConnectPaths.ProgramDataRoot, deviceModel, "image"));
            var fullMediaPath = Path.GetFullPath(mediaPath);
            return fullMediaPath.StartsWith(imageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        var lConnectImagePath = Path.Combine(LConnectPaths.ProgramDataRoot, deviceModel, "image", Path.GetFileName(layer.Media));
        return File.Exists(lConnectImagePath);
    }

    public async Task ApplyLayerAsync(string deviceModel, string templatePath, LayerRow layer, CancellationToken cancellationToken = default)
    {
        await RunSupporterAsync(BuildApplyLayerArgs(deviceModel, templatePath, layer), cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyLayersAsync(string deviceModel, string templatePath, IEnumerable<LayerRow> layers, CancellationToken cancellationToken = default)
    {
        var batch = layers
            .Where(layer => layer != null)
            .Select(layer => BuildApplyLayerArgs(deviceModel, templatePath, layer))
            .ToList();
        if (batch.Count == 0) return;

        var batchPath = Path.Combine(Path.GetTempPath(), $"lianli_layer_batch_{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(batchPath, JsonSerializer.Serialize(batch), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        try
        {
            var args = BaseTemplateArgs(deviceModel, templatePath);
            args.AddRange(new[] { "-ApplyLayerBatchJson", batchPath, "-FastLayerBatch" });
            await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(batchPath); } catch { }
        }
    }

    public async Task AddSensorAsync(
        string deviceModel,
        string templatePath,
        string sensorStyle,
        string sensorType,
        string x,
        string y,
        string zoom,
        string color1,
        string color2,
        string bgColor,
        string textColor,
        string font,
        CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[]
        {
            "-AddSensor", "1",
            "-AddSensorStyle", sensorStyle,
            "-AddSensorType", sensorType,
            "-AddX", x,
            "-AddY", y,
            "-AddSensorZoom", zoom,
            "-AddSensorColor1", color1,
            "-AddSensorColor2", color2,
            "-AddSensorBgColor", bgColor,
            "-AddSensorTextColor", textColor,
            "-AddSensorFont", font,
            "-NoBackup"
        });
        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> RenderSensorPreviewAsync(
        LayerRow layer,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string>
        {
            "-RenderSensorPreview",
            "-SensorStyle", string.IsNullOrWhiteSpace(layer.SensorStyle) ? "Ring2" : layer.SensorStyle,
            "-SensorType", string.IsNullOrWhiteSpace(layer.SensorType) ? "CPULoad" : layer.SensorType,
            "-SensorColor1", string.IsNullOrWhiteSpace(layer.SensorColor1) ? "#FFFFFF" : layer.SensorColor1,
            "-SensorColor2", string.IsNullOrWhiteSpace(layer.SensorColor2) ? "#00FFEE" : layer.SensorColor2,
            "-SensorBgColor", string.IsNullOrWhiteSpace(layer.SensorBgColor) ? "#202020" : layer.SensorBgColor,
            "-SensorTextColor", string.IsNullOrWhiteSpace(layer.SensorMainFontColor) ? "#FFFFFF" : layer.SensorMainFontColor,
            "-SensorTopFontColor", string.IsNullOrWhiteSpace(layer.SensorTopFontColor) ? (string.IsNullOrWhiteSpace(layer.SensorMainFontColor) ? "#FFFFFF" : layer.SensorMainFontColor) : layer.SensorTopFontColor,
            "-SensorBottomFontColor", string.IsNullOrWhiteSpace(layer.SensorBottomFontColor) ? (string.IsNullOrWhiteSpace(layer.SensorMainFontColor) ? "#FFFFFF" : layer.SensorMainFontColor) : layer.SensorBottomFontColor,
            "-SensorFont", string.IsNullOrWhiteSpace(layer.SensorFontFamily) ? "Noto Sans TC" : layer.SensorFontFamily,
            "-SensorValue", string.IsNullOrWhiteSpace(layer.Text) ? "52" : layer.Text,
            "-Output", outputPath
        };
        var rendered = await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(rendered) ? outputPath : rendered.Trim();
    }

    public async Task<string> RenderGraphPreviewAsync(
        string deviceModel,
        string templatePath,
        LayerRow layer,
        string outputPath,
        int canvasWidth = 480,
        int canvasHeight = 480,
        CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[]
        {
            "-RenderGraphPreview",
            "-LayerIndex", layer.Index,
            "-LayerX", layer.X,
            "-LayerY", layer.Y,
            "-PreviewValue", "100",
            "-CanvasWidth", Math.Max(1, canvasWidth).ToString(CultureInfo.InvariantCulture),
            "-CanvasHeight", Math.Max(1, canvasHeight).ToString(CultureInfo.InvariantCulture),
            "-Output", outputPath
        });

        if (!string.IsNullOrWhiteSpace(layer.GraphStyle) &&
            (layer.GraphStyle.StartsWith("MOD::", StringComparison.OrdinalIgnoreCase) ||
             layer.GraphStyle.Equals("DynamicStatus", StringComparison.OrdinalIgnoreCase)))
        {
            AddIfPresent(args, "-LayerGraphStyle", layer.GraphStyle);
        }
        AddIfPresent(args, "-LayerDataSource", NormalizeDataSource(layer.DataSource ?? ""));
        AddIfPresent(args, "-LayerWidth", layer.Width);
        AddIfPresent(args, "-LayerHeight", layer.Height);
        AddIfPresent(args, "-LayerRadius", layer.Radius);
        AddIfPresent(args, "-LayerDiameter", layer.Diameter);
        AddIfPresent(args, "-LayerThickness", layer.Thickness);
        AddIfPresent(args, "-LayerFrontColor", layer.FrontColor);
        AddIfPresent(args, "-LayerBackColor", layer.BackColor);
        AddIfPresent(args, "-LayerLineColor", layer.LineColor);
        AddIfPresent(args, "-LayerFillColor", layer.FillColor);
        AddIfPresent(args, "-LayerBorderColor", layer.BorderColor);
        AddIfPresent(args, "-LayerFillAlpha", layer.Transparent);
        if (!string.Equals(layer.Type, "GraphDynamicBar", StringComparison.OrdinalIgnoreCase))
        {
            AddIfPresent(args, "-LayerGradientColor", layer.GradientColor);
            AddIfPresent(args, "-LayerUseGradient", layer.UseGradient);
        }
        AddIfPresent(args, "-LayerDirection", layer.Direction);
        AddIfPresent(args, "-LayerLineWidth", layer.LineWidth);
        AddIfPresent(args, "-LayerColumnWidth", layer.ColumnWidth);
        AddIfPresent(args, "-LayerBorderWidth", layer.BorderWidth);
        AddIfPresent(args, "-LayerInnerCircleRadius", layer.InnerCircleRadius);
        AddIfPresent(args, "-LayerSplitBlockWidth", layer.SplitBlockWidth);
        AddIfPresent(args, "-LayerSplitBlankWidth", layer.SplitBlankWidth);
        AddIfPresent(args, "-LayerUseSubsection", layer.UseSubsection);
        AddIfPresent(args, "-LayerFillBack", layer.FillBack);
        AddIfPresent(args, "-LayerRevert", layer.Revert);
        AddIfPresent(args, "-LayerFrontAlpha", layer.FrontAlpha);
        AddIfPresent(args, "-LayerBackAlpha", layer.BackAlpha);
        AddIfPresent(args, "-LayerTransparentBackground", layer.TransparentBackground);
        AddIfPresent(args, "-LayerMinValue", layer.MinValue);
        AddIfPresent(args, "-LayerMaxValue", layer.MaxValue);
        AddIfPresent(args, "-LayerInvertDirection", layer.InvertDirection);
        AddIfPresent(args, "-LayerStartPercentage", layer.StartPercentage);
        AddIfPresent(args, "-LayerTotalAngle", layer.TotalAngle);
        AddIfPresent(args, "-LayerUseBlock", layer.UseBlock);
        AddIfPresent(args, "-LayerRingBorder", layer.RingBorder);
        AddIfPresent(args, "-LayerRound", layer.Round);
        AddIfPresent(args, "-LayerTypeName", layer.TypeName);
        AddIfPresent(args, "-LayerSubTypeName", layer.SubTypeName);

        var rendered = await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(rendered) ? outputPath : rendered.Trim();
    }

    private static void AddIfPresent(List<string> args, string name, string value, bool enabled = true)
    {
        if (enabled && !string.IsNullOrWhiteSpace(value))
        {
            args.Add(name);
            args.Add(value);
        }
    }

    private static bool SupportsFormat(string dataSource)
    {
        return dataSource.ToUpperInvariant() switch
        {
            "TIME" or "DATE" or "DAY" or
            "HDDTEMP" or "HDDUSED" or
            "CPUPWR" or "CPUPOWER" or "GPUPWR" or "GPUPOWER" or
            "RAMLOAD" or "DRVLOAD" => true,
            _ => false
        };
    }

    private static bool ShouldRebindNativeSource(string dataSource)
    {
        return dataSource.ToUpperInvariant() is
            "UPSPEED" or "DOWNDSPEED" or
            "HDDTEMP" or "HDDUSED" or "DRVLOAD";
    }

    private static string DefaultFormatForDataSource(string dataSource)
    {
        return (dataSource ?? "").ToUpperInvariant() switch
        {
            "TIME" => "h:m",
            "DATE" => "Y-M-D",
            "DAY" => "Day_en",
            "HDDTEMP" or "HDDUSED" => "",
            "CPUPWR" or "CPUPOWER" or "GPUPWR" or "GPUPOWER" => "0",
            "RAMLOAD" or "DRVLOAD" => "1",
            _ => ""
        };
    }

    public async Task AddTextAsync(string deviceModel, string templatePath, string text, string x, string y, string size, string color, string font, bool bold, CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[]
        {
            "-AddText", NormalizeLayerText(text),
            "-AddX", x,
            "-AddY", y,
            "-AddSize", size,
            "-AddColor", color,
            "-AddFont", font,
            "-AddAlignmentIndex", "1",
            "-NoBackup"
        });
        if (bold) args.Add("-AddBold");
        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetGroupingMetadataAsync(string deviceModel, string templatePath, string metadata, CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[] { "-SetGroupingMetadata", metadata, "-NoBackup" });
        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddImageAsync(string deviceModel, string templatePath, string imagePath, string x, string y, string size, CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[]
        {
            "-AddImage", imagePath,
            "-AddX", x,
            "-AddY", y,
            "-AddSize", size,
            "-NoBackup"
        });
        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddClockAsync(string deviceModel, string templatePath, string imagePath, string dataSource,
        string centerX, string centerY, string size, string format, CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[]
        {
            "-AddClock", imagePath,
            "-AddDataSource", NormalizeDataSource(dataSource),
            "-AddX", centerX,
            "-AddY", centerY,
            "-AddSize", size,
            "-AddFormat", format,
            "-NoBackup"
        });
        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddGraphAsync(string deviceModel, string templatePath, string graphStyleCode, string dataSource, string x, string y, string size, string frontColor, string backColor, CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[]
        {
            "-AddProgressBar", graphStyleCode,
            "-AddDataSource", NormalizeDataSource(dataSource),
            "-AddX", x,
            "-AddY", y,
            "-AddSize", size,
            "-AddFrontColor", frontColor,
            "-AddBackColor", backColor,
            "-NoBackup"
        });
        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> SetBackgroundMediaAsync(
        string deviceModel,
        string templatePath,
        string mediaPath,
        int canvasWidth = 480,
        int canvasHeight = 480,
        CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[]
        {
            "-SetBackgroundMedia", mediaPath,
            "-CanvasWidth", canvasWidth.ToString(),
            "-CanvasHeight", canvasHeight.ToString(),
            "-NoBackup"
        });
        var output = await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
        const string prefix = "BackgroundPath:";
        return output
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(line => line[prefix.Length..].Trim())
            .LastOrDefault() ?? "";
    }

    public async Task UpdateThemePreviewAsync(string deviceModel, string templatePath, string imagePath, CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[]
        {
            "-UpdateThemePreview", imagePath,
            "-NoBackup"
        });
        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAnimationPreviewBitmapsAsync(string deviceModel, string templatePath, string imagePath, CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[]
        {
            "-UpdateAnimationPreviewBitmaps", imagePath,
            "-NoBackup"
        });
        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportTurzxThemeAsync(
        string deviceModel,
        string templatePath,
        string outputPath,
        string backgroundPath = "",
        CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[] { "-ExportTurzxTheme", outputPath });
        if (!string.IsNullOrWhiteSpace(backgroundPath))
        {
            args.AddRange(new[] { "-TurzxBackground", backgroundPath });
        }
        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnsureBackgroundLayerAsync(
        string deviceModel,
        string templatePath,
        CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[]
        {
            "-EnsureBackgroundLayer",
            "-NoBackup"
        });
        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
    }

    public async Task NormalizeTemplateIdentityAsync(
        string deviceModel,
        string templatePath,
        string templateId,
        CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[]
        {
            "-NormalizeTemplateId", templateId,
            "-NoBackup"
        });
        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExtractMissingPreviewsAsync(
        string deviceModel,
        string templateRoot,
        string thumbnailRoot,
        CancellationToken cancellationToken = default)
    {
        await RunSupporterAsync(new[]
        {
            "-ExtractMissingPreviews",
            "-DeviceModel", deviceModel,
            "-TemplateRoot", templateRoot,
            "-ThumbnailRoot", thumbnailRoot
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetLayerMediaAsync(string deviceModel, string templatePath, string layerIndex, string mediaName, CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[] { "-LayerIndex", layerIndex, "-LayerImgName", mediaName, "-NoBackup" });
        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddDataAsync(string deviceModel, string templatePath, string dataSource, string x, string y, string size, string color, string font, bool bold, string format = "", CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[]
        {
            "-AddDataSource", NormalizeDataSource(dataSource),
            "-AddX", x,
            "-AddY", y,
            "-AddSize", size,
            "-AddColor", color,
            "-AddFont", font,
            "-AddAlignmentIndex", "1",
            "-NoBackup"
        });
        if (bold) args.Add("-AddBold");
        if (!string.IsNullOrWhiteSpace(format))
        {
            args.Add("-AddFormat");
            args.Add(NormalizeFormatForDataSource(dataSource, format));
        }
        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
    }
    public async Task RemoveLayerAsync(string deviceModel, string templatePath, string layerIndex, CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[] { "-RemoveLayerIndex", layerIndex, "-ForceRemoveBaseLayer", "-NoBackup" });
        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
    }

    public async Task MoveLayerAsync(string deviceModel, string templatePath, string layerIndex, string direction, CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[] { "-MoveLayerIndex", layerIndex, "-MoveLayerDirection", direction, "-NoBackup" });
        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
    }

    public async Task DuplicateLayerAsync(string deviceModel, string templatePath, string layerIndex, CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[] { "-DuplicateLayerIndex", layerIndex, "-NoBackup" });
        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TemplateLoadResult> LoadLayersAsync(string deviceModel, bool useActiveTemplate, string templateId, CancellationToken cancellationToken = default)
    {
        var args = new List<string>
        {
            "-DeviceModel", deviceModel,
            "-ListLayers",
            "-Json"
        };

        if (useActiveTemplate)
        {
            args.Add("-UseActiveTemplate");
        }
        else if (!string.IsNullOrWhiteSpace(templateId))
        {
            args.Add("-TemplateId");
            args.Add(templateId.Trim());
        }
        else
        {
            throw new InvalidOperationException("Use active template or enter a template ID.");
        }

        var json = await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
        return ParseTemplateResult(json);
    }

    public async Task<TemplateLoadResult> LoadTemplatePathAsync(string deviceModel, string templatePath, bool inspectBitmaps = true, CancellationToken cancellationToken = default)
    {
        if (inspectBitmaps)
        {
            try
            {
                var inspectArgs = BaseTemplateArgs(deviceModel, templatePath);
                inspectArgs.Add("-InspectBitmaps");
                await RunSupporterAsync(inspectArgs, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Ignore extraction failures so the template can still load.
            }
        }

        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.Add("-ListLayers");
        args.Add("-Json");
        var json = await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
        return ParseTemplateResult(json);
    }

    private static List<string> BaseTemplateArgs(string deviceModel, string templatePath)
    {
        return new List<string>
        {
            "-LConnectDir", LConnectPaths.ProgramFilesRoot,
            "-DeviceModel", deviceModel,
            "-TemplatePath", templatePath
        };
    }

    private static string NormalizeDataSource(string dataSource)
    {
        dataSource = (dataSource ?? "").Trim();
        if (dataSource.StartsWith("#", StringComparison.Ordinal))
        {
            dataSource = dataSource[1..].Trim();
        }

        return dataSource.ToUpperInvariant() switch
        {
            "CPUPOWER" => "CPUPWR",
            "GPUPOWER" => "GPUPWR",
            _ => dataSource
        };
    }

    private static string DisplayDataSource(string dataSource)
    {
        return dataSource.ToUpperInvariant() switch
        {
            _ => dataSource
        };
    }

    private async Task<IReadOnlyList<string>> RunLinesAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var text = await RunSupporterAsync(arguments, cancellationToken).ConfigureAwait(false);
        return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async Task<string> RunSupporterAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        await _supporterGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var expectsJson = arguments.Any(argument => string.Equals(argument, "-Json", StringComparison.OrdinalIgnoreCase));
            string lastFailure = "";
            for (var attempt = 0; attempt < (expectsJson ? 4 : 1); attempt++)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _supporterPath,
                WorkingDirectory = _workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(SanitizeProcessArgument(argument));
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start supporter process.");
            string stdout;
            string stderr;
            try
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                stdout = await stdoutTask.ConfigureAwait(false);
                stderr = await stderrTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex) { AppLogger.Error("Timed-out supporter process could not be stopped.", ex); }

                throw new TimeoutException("The supporter operation took too long and was stopped.");
            }

            if (process.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                lastFailure = message.Trim();
                if (attempt + 1 < (expectsJson ? 4 : 1))
                {
                    await Task.Delay(250 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                throw new InvalidOperationException(lastFailure);
            }

            if (!expectsJson && string.IsNullOrWhiteSpace(stdout)) { return string.Empty; }
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                var output = stdout.Trim();
                if (!expectsJson || IsValidJson(output))
                {
                    return output;
                }
                lastFailure = string.IsNullOrWhiteSpace(stderr)
                    ? "The supporter returned incomplete template data."
                    : stderr.Trim();
            }
            else
            {
                lastFailure = string.IsNullOrWhiteSpace(stderr)
                    ? "The supporter returned an empty response while loading template data."
                    : stderr.Trim();
            }

            if (attempt + 1 < (expectsJson ? 4 : 1))
            {
                await Task.Delay(250 * (attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(lastFailure)
                ? "The supporter returned an empty response while loading template data. Please try again."
                : lastFailure);
        }
        finally
        {
            _supporterGate.Release();
        }
    }

    private static bool IsValidJson(string value)
    {
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string SanitizeProcessArgument(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(ch == '\0' ? ' ' : char.IsControl(ch) ? ' ' : ch);
        }

        return builder.ToString();
    }

    private static TemplateLoadResult ParseTemplateResult(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("The supporter returned empty template data.");
        }
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var result = new TemplateLoadResult
        {
            TemplateId = GetValue(root, "TemplateId"),
            TemplatePath = GetValue(root, "TemplatePath"),
            Background = GetValue(root, "Background"),
            BackgroundPath = GetValue(root, "BackgroundPath"),
            Width = GetIntValue(root, "Width"),
            Height = GetIntValue(root, "Height")
        };

        if (root.TryGetProperty("Layers", out var layers) && layers.ValueKind == JsonValueKind.Array)
        {
            foreach (var layer in layers.EnumerateArray())
            {
                var row = new LayerRow
                {
                    Index = GetValue(layer, "Index"),
                    Type = GetValue(layer, "Type"),
                    DataSource = DisplayDataSource(GetValue(layer, "DataSource")),
                    OriginalDataSource = DisplayDataSource(GetValue(layer, "DataSource")),
                    DataRate = GetValue(layer, "DataRate"),
                    Text = NormalizeDisplayText(GetValue(layer, "Text")),
                    Media = GetValue(layer, "Media"),
                    MediaPath = GetValue(layer, "MediaPath"),
                    X = GetValue(layer, "X"),
                    Y = GetValue(layer, "Y"),
                    Size = GetValue(layer, "Size"),
                    Font = GetValue(layer, "Font"),
                    Bold = GetValue(layer, "Bold"),
                    Italic = GetValue(layer, "Italic"),
                    Color = GetValue(layer, "Color"),
                    Format = NormalizeLayerFormat(DisplayDataSource(GetValue(layer, "DataSource")), NormalizeDisplayText(GetValue(layer, "Format"))),
                    GraphStyle = GetValue(layer, "GraphStyle"),
                    OriginalGraphStyle = GetValue(layer, "GraphStyle"),
                    Hide = GetValue(layer, "Hide"),
                    FontGradientColor = GetValue(layer, "FontGradientColor"),
                    FontGradientDirection = GetValue(layer, "FontGradientDirection"),
                    Width = GetValue(layer, "Width"),
                    Height = GetValue(layer, "Height"),
                    Radius = GetValue(layer, "Radius"),
                    Diameter = GetValue(layer, "Diameter"),
                    Thickness = GetValue(layer, "Thickness"),
                    FrontColor = GetValue(layer, "FrontColor"),
                    BackColor = GetValue(layer, "BackColor"),
                    LineColor = GetValue(layer, "LineColor"),
                    FillColor = GetValue(layer, "FillColor"),
                    BorderColor = GetValue(layer, "BorderColor"),
                    Transparent = GetColorAlpha(GetValue(layer, "FillColor")),
                    UseGradient = GetValue(layer, "UseGradient"),
                    GradientColor = GetValue(layer, "GradientColor"),
                    ZoomRate = GetValue(layer, "ZoomRate"),
                    Rotate = GetValue(layer, "Rotate"),
                    ClockCenterX = GetValue(layer, "ClockCenterX"),
                    ClockCenterY = GetValue(layer, "ClockCenterY"),
                    ClockAngle = GetValue(layer, "ClockAngle"),
                    ClockEndAngle = GetValue(layer, "ClockEndAngle"),
                    ClockOffset = GetValue(layer, "ClockOffset"),
                    ClockRateOffset = GetValue(layer, "ClockRateOffset"),
                    ClockMoveOrigin = GetValue(layer, "ClockMoveOrigin"),
                    ClockOriginX = GetValue(layer, "ClockOriginX"),
                    ClockOriginY = GetValue(layer, "ClockOriginY"),
                    Rect = GetValue(layer, "Rect"),
                    AlignmentIndex = GetValue(layer, "AlignmentIndex"),
                    AlignmentName = GetValue(layer, "AlignmentName"),
                    FontInterval = GetValue(layer, "FontInterval"),
                    FontOrgSize = GetValue(layer, "FontOrgSize"),
                    LineHeight = GetValue(layer, "LineHeight"),
                    Direction = GetValue(layer, "Direction"),
                    LineWidth = GetValue(layer, "LineWidth"),
                    ColumnWidth = GetValue(layer, "ColumnWidth"),
                    BorderWidth = GetValue(layer, "BorderWidth"),
                    InnerCircleRadius = GetValue(layer, "InnerCircleRadius"),
                    SplitBlockWidth = GetValue(layer, "SplitBlockWidth"),
                    SplitBlankWidth = GetValue(layer, "SplitBlankWidth"),
                    UseSubsection = GetValue(layer, "UseSubsection"),
                    FillBack = GetValue(layer, "FillBack"),
                    Revert = GetValue(layer, "Revert"),
                    FrontAlpha = GetValue(layer, "FrontAlpha"),
                    BackAlpha = GetValue(layer, "BackAlpha"),
                    TransparentBackground = GetValue(layer, "TransparentBackground"),
                    MinValue = GetValue(layer, "MinValue"),
                    MaxValue = GetValue(layer, "MaxValue"),
                    InvertDirection = GetValue(layer, "InvertDirection"),
                    StartPercentage = GetValue(layer, "StartPercentage"),
                    TotalAngle = GetValue(layer, "TotalAngle"),
                    UseBlock = GetValue(layer, "UseBlock"),
                    RingBorder = GetValue(layer, "RingBorder"),
                    Round = GetValue(layer, "Round"),
                    TypeName = GetValue(layer, "TypeName"),
                    SubTypeName = GetValue(layer, "SubTypeName"),
                    RenderMode = GetValue(layer, "RenderMode"),
                    ThemeMode = GetValue(layer, "ThemeMode"),
                    SensorStyle = GetValue(layer, "SensorStyle"),
                    SensorType = GetValue(layer, "SensorType"),
                    SensorColor1 = GetValue(layer, "SensorColor1"),
                    SensorColor2 = GetValue(layer, "SensorColor2"),
                    SensorBgColor = GetValue(layer, "SensorBgColor"),
                    SensorMainFontColor = GetValue(layer, "SensorMainFontColor"),
                    SensorTopFontColor = GetValue(layer, "SensorTopFontColor"),
                    SensorBottomFontColor = GetValue(layer, "SensorBottomFontColor"),
                    SensorFontFamily = GetValue(layer, "SensorFontFamily"),
                    SensorZoomRate = GetValue(layer, "SensorZoomRate")
                };
                row.SetWritableProperties(GetArrayValues(layer, "WritableProperties"));
                row.SetWritableFontProperties(GetArrayValues(layer, "WritableFontProperties"));
                row.Description = GetLayerDisplayType(row);
                (row.IconData, row.IconColor) = GetLayerIcon(row);
                result.Layers.Add(row);
            }
        }

        return result;
    }

    private static (string Data, string Color) GetLayerIcon(LayerRow layer)
    {
        return layer.Type switch
        {
            "GraphAnimation" => ("M5,6 H27 V26 H5 Z M8,6 V26 M24,6 V26 M12,12 L21,16 L12,20 Z", "#7C3AED"),
            "GraphItem" when string.Equals(layer.TypeName, "Text", StringComparison.OrdinalIgnoreCase)
                => ("M5,6 H27 V12 H20 V27 H12 V12 H5 Z", "#E11D48"),
            "GraphItem" => ("M6,8 H26 V24 H6 Z M9,17 H13 V20 H9 Z M15,13 H18 V20 H15 Z M20,15 H23 V20 H20 Z M24,5 A3,3 0 1 1 23.9,5", "#0284C7"),
            "GraphImage" => ("M5,7 H27 V25 H5 Z M8,22 L14,15 L18,19 L23,13 L27,22 Z M10,11 A2,2 0 1 1 9.9,11", "#059669"),
            "GraphClock" => ("M4,26 A12,12 0 0 1 28,26 H23 A7,7 0 0 0 9,26 Z M16,21 L25,11 L28,14 L19,24 Z M14,22 H19 V27 H14 Z", "#F97316"),
            "GraphSensor" => ("M16,4 A12,12 0 1 1 4,16 H10 A6,6 0 1 0 16,10 Z M16,14 A2,2 0 1 1 15.9,14", "#0891B2"),
            "GraphStatuBar" => ("M4,16 H9 V25 H4 Z M11,13 H16 V25 H11 Z M18,10 H23 V25 H18 Z M25,7 H30 V25 H25 Z", "#EA580C"),
            "GraphDynamicBar" or "DynamicBar" => ("M5,12 H27 A4,4 0 0 1 27,20 H5 A4,4 0 0 1 5,12 M18,10 A6,6 0 1 1 17.9,10", "#0F766E"),
            "GraphArchBar" => ("M4,27 A12,12 0 1 1 28,27 H22 A7,7 0 1 0 10,27 Z", "#DB2777"),
            "GraphLine" => ("M4,25 C9,9 14,27 19,15 S27,9 29,7 V27 H4 Z", "#4F46E5"),
            _ => ("M6,6 H26 V26 H6 Z", "#64748B")
        };
    }

    private static string GetLayerDisplayType(LayerRow layer)
    {
        if (string.Equals(layer.RenderMode, "D3", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(layer.Type, "GraphItem", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(layer.TypeName, "Text", StringComparison.OrdinalIgnoreCase)
                ? "3D Text"
                : "3D Data";
        }

        return (layer.Type ?? "") switch
        {
            "GraphAnimation" => "Background",
            "GraphImage" => "Image",
            "GraphClock" => "Gauge",
            "GraphSensor" => "Sensor",
            "GraphLine" => "Chart",
            "GraphArchBar" => "Curved Bar",
            "GraphDynamicBar" or "DynamicBar" => "Dynamic Status",
            "GraphStatuBar" => "Status Bar",
            "GraphItem" when string.Equals(layer.TypeName, "Text", StringComparison.OrdinalIgnoreCase) => "Text",
            "GraphItem" => "Data",
            _ => layer.Type ?? ""
        };
    }

    private static IEnumerable<string> GetArrayValues(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static string NormalizeLayerFormat(string dataSource, string format)
    {
        var source = dataSource.ToUpperInvariant();
        if (source == "TIME")
        {
            return NormalizeTimeFormat(format);
        }

        if (source == "DATE")
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(format ?? "", @"^\d{4}-\d{2}-\d{2}$")) return "Y-M-D";
            if (System.Text.RegularExpressions.Regex.IsMatch(format ?? "", @"^\d{2}-\d{2}-\d{4}$")) return "D-M-Y";
            if (System.Text.RegularExpressions.Regex.IsMatch(format ?? "", @"^\d{2}\.\d{2}\.\d{4}$")) return "D.M.Y";
        }

        if (source is "CPUPWR" or "CPUPOWER" or "GPUPWR" or "GPUPOWER" &&
            (string.IsNullOrWhiteSpace(format) || string.Equals(format, "0.0", StringComparison.OrdinalIgnoreCase)))
        {
            return "0";
        }

        return format ?? "";
    }

    private static string NormalizeFormatForDataSource(string dataSource, string format)
    {
        return (dataSource ?? "").ToUpperInvariant() == "TIME"
            ? NormalizeTimeFormat(format)
            : format;
    }

    private static string NormalizeTimeFormat(string? format)
    {
        return (format ?? "").Trim() switch
        {
            "00:00" or "HH:mm" => "h:m",
            "Hour:Minute" => "h:m",
            "00:00:00" or "HH:MM:SS" or "H:M:S" or "HH:mm:ss" => "h:m:s",
            "Hour:Minute:Second" => "h:m:s",
            var value => value
        };
    }

    private static string GetValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return "";
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? "",
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            JsonValueKind.Null => "",
            JsonValueKind.Undefined => "",
            _ => property.ToString()
        };
    }

    private static int GetIntValue(JsonElement element, string propertyName)
    {
        var value = GetValue(element, propertyName);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static string GetColorAlpha(string color)
    {
        var match = ColorAlphaRegex.Match(color ?? "");
        return match.Success ? match.Groups[1].Value : "255";
    }

    private static string NormalizeDisplayText(string value)
    {
        return (value ?? "")
            .Replace("ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â°", "ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â°", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€Â¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚ÂÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã‚ÂÃƒâ€šÃ‚Â¢", "ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚ÂÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬ÂÃ‚Â¢", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€Â¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚ÂÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â°", "ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚ÂÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â°", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€Â¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬ÂÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â", "ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€Â¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â¹ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚ÂÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢", "ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¹ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã‚ÂÃƒâ€šÃ‚Â¢", StringComparison.Ordinal);
    }
}



