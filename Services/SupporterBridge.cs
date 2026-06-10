using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.Services;

public sealed class SupporterBridge
{
    private readonly SemaphoreSlim _supporterGate = new(1, 1);
    private readonly string _supporterPath;
    private readonly string _workingDirectory;

    public SupporterBridge()
    {
        var appDir = AppContext.BaseDirectory;
        var candidateRoots = new[]
        {
            appDir,
            Path.GetFullPath(Path.Combine(appDir, "..", "..", "..", "..", "ThemeEditor")),
            Path.GetFullPath(Path.Combine(appDir, "..", "ThemeEditor")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "ThemeEditor"))
        };

        foreach (var root in candidateRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var exe = Path.Combine(root, "supporter.exe");
            var legacyExe = Path.Combine(root, "EXE", "supporter.exe");
            var available = new[] { exe, legacyExe }
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
            if (available.Count > 0)
            {
                _supporterPath = available[0];
                _workingDirectory = root;
                return;
            }
        }

        throw new FileNotFoundException("Could not find supporter.exe next to the application.");
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

    public async Task ApplyLayerAsync(string deviceModel, string templatePath, LayerRow layer, CancellationToken cancellationToken = default)
    {
        if (string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

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
        var isImage = type.Equals("GraphImage", StringComparison.OrdinalIgnoreCase);

        if (isText)
        {
            if (layer.CanWriteFont("size") && !string.IsNullOrWhiteSpace(layer.Size)) { args.Add("-LayerSize"); args.Add(layer.Size); }
            if (layer.CanWriteFont("color") && !string.IsNullOrWhiteSpace(layer.Color)) { args.Add("-LayerColor"); args.Add(layer.Color); }
            if (layer.CanWriteFont("name") && !string.IsNullOrWhiteSpace(layer.Font)) { args.Add("-LayerFont"); args.Add(layer.Font); }
            if (layer.CanWriteFont("isBold") && !string.IsNullOrWhiteSpace(layer.Bold)) { args.Add("-LayerBold"); args.Add(layer.Bold); }
            if (layer.CanWriteFont("IsItalic") && !string.IsNullOrWhiteSpace(layer.Italic)) { args.Add("-LayerItalic"); args.Add(layer.Italic); }
            AddIfPresent(args, "-LayerAlignmentIndex", layer.AlignmentIndex, layer.CanWriteFont("alignment.index"));
            AddIfPresent(args, "-LayerFontInterval", layer.FontInterval, layer.CanWriteFont("interval"));
            AddIfPresent(args, "-LayerLineHeight", layer.LineHeight, layer.CanWrite("LineHeight"));
            if (!string.IsNullOrWhiteSpace(layer.DataSource))
            {
                args.Add("-LayerDataSource");
                args.Add(NormalizeDataSource(layer.DataSource));
            }
        }

        var source = layer.DataSource ?? "";
        if (isText && SupportsFormat(source))
        {
            args.Add("-LayerFormat");
            args.Add(string.IsNullOrWhiteSpace(layer.Format) ? DefaultFormatForDataSource(source) : layer.Format);
        }
        else if (isText && (source == "StaticText" || layer.ForceText))
        {
            args.Add("-LayerText");
            args.Add(string.IsNullOrEmpty(layer.Text) ? " " : layer.Text);
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
            if (!string.IsNullOrWhiteSpace(layer.DataSource)) { args.Add("-LayerDataSource"); args.Add(NormalizeDataSource(layer.DataSource)); }
            if (layer.CanWrite("width") && !string.IsNullOrWhiteSpace(layer.Width)) { args.Add("-LayerWidth"); args.Add(layer.Width); }
            if (layer.CanWrite("height") && !string.IsNullOrWhiteSpace(layer.Height)) { args.Add("-LayerHeight"); args.Add(layer.Height); }
            if (layer.CanWrite("radius") && !string.IsNullOrWhiteSpace(layer.Radius)) { args.Add("-LayerRadius"); args.Add(layer.Radius); }
            if (layer.CanWrite("diameter") && !string.IsNullOrWhiteSpace(layer.Diameter)) { args.Add("-LayerDiameter"); args.Add(layer.Diameter); }
            if (layer.CanWrite("archWidth") && !string.IsNullOrWhiteSpace(layer.Thickness)) { args.Add("-LayerThickness"); args.Add(layer.Thickness); }
            if ((layer.CanWrite("FrontColor") || layer.CanWrite("LineColor") || layer.CanWrite("FillColor")) && !string.IsNullOrWhiteSpace(layer.FrontColor)) { args.Add("-LayerFrontColor"); args.Add(layer.FrontColor); }
            if ((layer.CanWrite("BackColor") || layer.CanWrite("BorderColor")) && !string.IsNullOrWhiteSpace(layer.BackColor)) { args.Add("-LayerBackColor"); args.Add(layer.BackColor); }
            if (layer.CanWrite("GradientColor") && !string.IsNullOrWhiteSpace(layer.GradientColor)) { args.Add("-LayerGradientColor"); args.Add(layer.GradientColor); }
            if (layer.CanWrite("useGradient") && !string.IsNullOrWhiteSpace(layer.UseGradient)) { args.Add("-LayerUseGradient"); args.Add(layer.UseGradient); }
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
        }

        if (isImage)
        {
            if (!string.IsNullOrWhiteSpace(layer.Media)) { args.Add("-LayerImgName"); args.Add(layer.Media); }
            if (!string.IsNullOrWhiteSpace(layer.ZoomRate)) { args.Add("-LayerZoomRate"); args.Add(layer.ZoomRate); }
            AddIfPresent(args, "-LayerRotate", layer.Rotate, layer.CanWrite("rotate"));
            AddIfPresent(args, "-LayerRect", layer.Rect, layer.CanWrite("rect"));
        }

        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
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
            "TIME" or "DATE" or "DAY" or "CPUPWR" or "CPUPOWER" or "GPUPWR" or "GPUPOWER" => true,
            _ => false
        };
    }

    private static string DefaultFormatForDataSource(string dataSource)
    {
        return (dataSource ?? "").ToUpperInvariant() switch
        {
            "TIME" => "00:00",
            "DATE" => "Y-M-D",
            "DAY" => "Day_en",
            "CPUPWR" or "CPUPOWER" or "GPUPWR" or "GPUPOWER" => "0",
            _ => ""
        };
    }

    public async Task AddTextAsync(string deviceModel, string templatePath, string text, string x, string y, string size, string color, string font, bool bold, CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[]
        {
            "-AddText", text,
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

    public async Task SetBackgroundMediaAsync(string deviceModel, string templatePath, string mediaPath, CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[] { "-SetBackgroundMedia", mediaPath });
        await RunSupporterAsync(args, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddShadowAsync(string deviceModel, string templatePath, string layerIndex, string offsetX, string offsetY, string color, CancellationToken cancellationToken = default)
    {
        var args = BaseTemplateArgs(deviceModel, templatePath);
        args.AddRange(new[]
        {
            "-AddShadowForLayer", layerIndex,
            "-ShadowOffsetX", offsetX,
            "-ShadowOffsetY", offsetY,
            "-ShadowColor", color,
            "-NoBackup"
        });
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
            args.Add(format);
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

    public async Task<TemplateLoadResult> LoadTemplatePathAsync(string deviceModel, string templatePath, CancellationToken cancellationToken = default)
    {
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
            "-DeviceModel", deviceModel,
            "-TemplatePath", templatePath
        };
    }

    private static string NormalizeDataSource(string dataSource)
    {
        return dataSource.ToUpperInvariant() switch
        {
            "CPUPOWER" => "CPUPWR",
            "GPUPOWER" => "GPUPWR",
            "FPS" => "FPS_AVG",
            _ => dataSource
        };
    }

    private static string DisplayDataSource(string dataSource)
    {
        return dataSource.ToUpperInvariant() switch
        {
            "FPS_AVG" => "FPS",
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
        try
        {
            var logPath = Path.Combine(_workingDirectory, "csharp_supporter_calls.log");
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Running supporter with args:");
            foreach (var arg in arguments)
            {
                sb.AppendLine($"  {arg}");
            }
            sb.AppendLine();
            File.AppendAllText(logPath, sb.ToString());
        }
        catch { }

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
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
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
                catch { }

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
            BackgroundPath = GetValue(root, "BackgroundPath")
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
                    Text = NormalizeDisplayText(GetValue(layer, "Text")),
                    Media = GetValue(layer, "Media"),
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
                    Width = GetValue(layer, "Width"),
                    Height = GetValue(layer, "Height"),
                    Radius = GetValue(layer, "Radius"),
                    Diameter = GetValue(layer, "Diameter"),
                    Thickness = GetValue(layer, "Thickness"),
                    FrontColor = GetValue(layer, "FrontColor"),
                    BackColor = GetValue(layer, "BackColor"),
                    UseGradient = GetValue(layer, "UseGradient"),
                    GradientColor = GetValue(layer, "GradientColor"),
                    ZoomRate = GetValue(layer, "ZoomRate"),
                    Rotate = GetValue(layer, "Rotate"),
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
                    TypeName = GetValue(layer, "TypeName"),
                    SubTypeName = GetValue(layer, "SubTypeName")
                };
                row.SetWritableProperties(GetArrayValues(layer, "WritableProperties"));
                row.SetWritableFontProperties(GetArrayValues(layer, "WritableFontProperties"));
                result.Layers.Add(row);
            }
        }

        return result;
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

    private static string NormalizeDisplayText(string value)
    {
        return (value ?? "")
            .Replace("Â°", "°", StringComparison.Ordinal)
            .Replace("â„ƒ", "℃", StringComparison.Ordinal)
            .Replace("â„‰", "℉", StringComparison.Ordinal)
            .Replace("Ã—", "×", StringComparison.Ordinal)
            .Replace("âˆ’", "−", StringComparison.Ordinal);
    }
}

