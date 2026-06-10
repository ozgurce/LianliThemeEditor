using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ThemeEditorCSharp.Models;
using ThemeEditorCSharp.Services;
using Ellipse = System.Windows.Shapes.Ellipse;
using Line = System.Windows.Shapes.Line;
using Polygon = System.Windows.Shapes.Polygon;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace ThemeEditorCSharp;

public partial class MainWindow : Window
{
    private const int WmGetMinMaxInfo = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    private sealed class ThemePackageManifest
    {
        public int FormatVersion { get; set; } = 1;
        public string App { get; set; } = "Lian Li LCD Theme Editor";
        public string DeviceModel { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public string TemplateFile { get; set; } = "template/theme.template";
        public string BackgroundFile { get; set; } = "";
        public List<string> ImageFiles { get; set; } = new();
        public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
    }

    private sealed class ThemeExportSnapshot
    {
        public string DeviceModel { get; init; } = "";
        public string TemplateId { get; init; } = "";
        public string TemplatePath { get; init; } = "";
        public string BackgroundPath { get; init; } = "";
        public string BackgroundEntryName { get; init; } = "";
        public string PreviewBackgroundPath { get; init; } = "";
        public string PreviewBackgroundEntryName { get; init; } = "";
        public List<string> ReferencedBackgroundPaths { get; init; } = new();
        public List<string> ImagePaths { get; init; } = new();
        public byte[] PreviewPng { get; init; } = Array.Empty<byte>();
    }

    private static readonly string[] DataSources =
    {
        "CPUTEMP", "CPUTEMP_F", "CPUCLOCK", "CPUCLOCK_G", "CPULOAD", "CPUFAN",
        "CPUPOWER", "CPUVOLTAGE", "CPUMODEL",
        "GPUTEMP", "GPUTEMP_F", "GPUCLOCK", "GPUCLOCK_G", "GPULOAD", "GPUFAN",
        "GPUPOWER", "GPUVOLTAGE", "GPUMODEL", "GPURAMLOAD", "GPURAM", "GPUVALIDRAM",
        "RAMLOAD", "RAM", "RAM_GB", "RAMVALID", "RAMTOTAL", "RAMTOTAL_GB", "RAMMODEL",
        "HDDTEMP", "HDDTEMP_F", "HDDUSED", "DRVLOAD", "WATERPUMP", "PUMP",
        "CASEFAN1", "CASEFAN2", "CASEFAN3", "CASEFAN4",
        "CASEFAN5", "CASEFAN6", "CASEFAN7", "CASEFAN8",
        "UPSPEED", "DOWNDSPEED", "FPS_AVG",
        "TIME", "DATE", "DAY", "APM", "StaticText"
    };

    private readonly SupporterBridge _supporter;
    private string _currentTemplatePath = "";
    private string _currentTemplateId = "";
    private string _currentBackgroundPath = "";
    private bool _isLoading;
    private bool _isDraggingPreview;
    private Point _dragStartTemplatePoint;
    private readonly Dictionary<LayerRow, Point> _dragStartPositions = new();
    private readonly Dictionary<LayerRow, Rect> _dragStartPreviewBounds = new();
    private readonly Dictionary<LayerRow, Rect> _dragStartSelectionBounds = new();
    private LayerRow? _dragLayer;
    private readonly Dictionary<LayerRow, FrameworkElement> _previewLayerVisuals = new();
    private readonly Dictionary<LayerRow, Rectangle> _previewSelectionVisuals = new();
    private Rectangle? _previewResizeHandle;

    // Preview Resizing States
    private bool _isResizingPreview;
    private Point _resizeStartTemplatePoint;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private double _resizeStartDiameter;
    private double _resizeStartSize;
    private double _resizeStartZoom;
    private double _resizeStartColumnWidth;

    // Undo/Redo and Shadow Pairing States
    private readonly Stack<byte[]> _undoStack = new();
    private readonly Stack<byte[]> _redoStack = new();
    private bool _editorUndoArmed;
    private readonly Dictionary<int, int> _shadowLinks = new();
    private readonly Dictionary<LayerRow, Point> _shadowStartPositions = new();
    private readonly HashSet<LayerRow> _dirtyLayers = new();
    private readonly System.Windows.Threading.DispatcherTimer _autoSaveTimer;
    private bool _backgroundDirty;
    private double _canvasZoom = 1.8;
    private Dictionary<string, string> _languageText = new(StringComparer.OrdinalIgnoreCase);
    private bool _updatingTextOverride;
    private readonly Dictionary<string, string> _previewSampleOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource> _previewImageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Size> _imageBoundsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FontFamily> _resolvedFontsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource> _gdiTextCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Rect> _gdiTextInkCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextLayerRenderResult> _gdiTextLayerCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Threading.DispatcherTimer _livePreviewTimer;
    private const string HwInfoSensorsPath = @"C:\ProgramData\Lian-Li\L-Connect 3\hwinfo-sensors.json";
    private static readonly Dictionary<string, string> _liveSensorValueCache = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _liveSensorCacheWriteUtc;
    private static DateTime _liveSensorCacheReadUtc;
    private readonly System.Windows.Threading.DispatcherTimer _previewDrawTimer;
    private bool _previewDrawPending;
    private const string DefaultLayerFontName = "GeForce";
    private static readonly HashSet<string> _systemFonts = new(
        System.Windows.Media.Fonts.SystemFontFamilies.Select(f => f.Source),
        StringComparer.OrdinalIgnoreCase
    );
    private Point _layerListDragStart;
    private LayerRow? _layerListDragLayer;
    private bool _syncingNumericSliders;
    private bool _syncingThemeToggle;

    public ObservableCollection<LayerRow> Layers { get; } = new();
    public ObservableCollection<GraphStyleOption> GraphStyles { get; } = new();
    public ObservableCollection<TemplateOption> TemplateOptions { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        InitializeCustomFonts();

        _supporter = new SupporterBridge();
        _autoSaveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;
        _previewDrawTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _previewDrawTimer.Tick += (_, _) =>
        {
            _previewDrawTimer.Stop();
            if (!_previewDrawPending)
            {
                return;
            }

            _previewDrawPending = false;
            DrawPreview();
        };
        _livePreviewTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _livePreviewTimer.Tick += (_, _) =>
        {
            if (!_isDraggingPreview && !_isResizingPreview && Layers.Any(IsDynamicDataLayer))
            {
                _gdiTextLayerCache.Clear();
                RequestPreviewDraw();
            }
        };
        _livePreviewTimer.Start();

        SupporterPathText.Text = _supporter.SupporterPath;
        DeviceCombo.SelectedIndex = 0;
        LanguageCombo.SelectedIndex = 0;
        UiThemeCombo.SelectedIndex = 0;

        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        PreviewCanvas.MouseMove += PreviewCanvas_MouseMove;
        PreviewCanvas.MouseLeftButtonUp += PreviewCanvas_MouseLeftButtonUp;
        PreviewFrame.PreviewMouseWheel += PreviewFrame_PreviewMouseWheel;
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        AttachAlphaColorMenu(ColorPickButton, ColorBox);
        AttachAlphaColorMenu(ShadowColorPickButton, ShadowColorBox);
        AttachAlphaColorMenu(AddColorPickButton, AddColorBox);
        AttachAlphaColorMenu(FrontColorPickButton, FrontColorBox);
        AttachAlphaColorMenu(BackColorPickButton, BackColorBox);
        AttachAlphaColorMenu(GradientColorPickButton, GradientColorBox);
        SetCanvasZoom(_canvasZoom);

        RegisterInputListeners();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WindowMessageHook);
    }

    private static IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        var monitor = MonitorFromWindow(hwnd, 0x00000002);
        if (monitor == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return IntPtr.Zero;
        }

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        minMaxInfo.MaxPosition.X = monitorInfo.WorkArea.Left - monitorInfo.Monitor.Left;
        minMaxInfo.MaxPosition.Y = monitorInfo.WorkArea.Top - monitorInfo.Monitor.Top;
        minMaxInfo.MaxSize.X = monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left;
        minMaxInfo.MaxSize.Y = monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top;
        Marshal.StructureToPtr(minMaxInfo, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _isLoading = true;
            TemplateCombo.ItemsSource = TemplateOptions;
            RefreshTemplateList();
            foreach (var data in DataSources.OrderBy(GetDataSourceDisplayName, StringComparer.OrdinalIgnoreCase))
            {
                var display = GetDataSourceDisplayName(data);
                DataCombo.Items.Add(new ComboBoxItem { Content = display, Tag = data });
                AddDataCombo.Items.Add(new ComboBoxItem { Content = display, Tag = data });
            }
            SetComboText(DataCombo, "CPUTEMP");
            SetComboText(AddDataCombo, "GPUTEMP");
            AddLayerTypeCombo.SelectedIndex = 0;

            var fonts = await _supporter.ListFontsAsync();
            foreach (var font in fonts
                         .Concat(_customFontMap.Keys)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(font => font, StringComparer.OrdinalIgnoreCase))
            {
                FontCombo.Items.Add(font);
                AddFontCombo.Items.Add(font);
            }
            var defaultFont = GetDefaultLayerFontName();
            SetComboText(FontCombo, defaultFont);
            SetComboText(AddFontCombo, defaultFont);

            IReadOnlyList<GraphStyleOption> graphStyles;
            try
            {
                graphStyles = (await _supporter.ListGraphStylesAsync())
                    .Where(style => style.Code.StartsWith("MOD::H2_", StringComparison.OrdinalIgnoreCase))
                    .Select(NormalizeGraphStyleLabel)
                    .ToList();
                if (graphStyles.Count == 0)
                {
                    graphStyles = GetFallbackGraphStyles();
                }
            }
            catch
            {
                graphStyles = GetFallbackGraphStyles();
            }
            GraphStyles.Clear();
            foreach (var style in graphStyles)
            {
                GraphStyles.Add(style);
                GraphStyleCombo.Items.Add(style);
                AddGraphStyleCombo.Items.Add(style);
            }
            if (AddGraphStyleCombo.Items.Count > 0)
            {
                AddGraphStyleCombo.SelectedIndex = 0;
            }

            LoadEditorSettings();
            UseActiveCheck.IsChecked = true;
            _isLoading = false;
            await LoadLayersAsync(false);
        }
        catch (Exception ex)
        {
            _isLoading = false;
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup_error.log"), ex.ToString());
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup_error.log"), ex.ToString());
            }
            catch {}
            SetStatus("Initialization failed.");
            MessageBox.Show(this, ex.Message, "Initialization failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadLayersAsync(false);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void UseActiveCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || !IsLoaded || UseActiveCheck.IsChecked != true)
        {
            return;
        }

        await LoadLayersAsync(false);
    }

    private async void ExportThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (UseActiveCheck.IsChecked != true &&
            (string.IsNullOrWhiteSpace(_currentTemplatePath) || !File.Exists(_currentTemplatePath)))
        {
            MessageBox.Show(this, GetLanguageText("messages.loadThemeFirst", "Load a theme first."),
                GetLanguageText("messages.exportThemeFailed", "Theme export failed"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = GetLanguageText("dialogs.exportTheme", "Export Lian Li theme"),
            Filter = GetLanguageText("dialogs.themePackageFilter", "Lian Li theme package (*.lltheme)|*.lltheme"),
            FileName = $"{SanitizeFileName(_currentTemplateId)}.lltheme",
            DefaultExt = ".lltheme",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            SetBusy(true, GetLanguageText("status.exportingTheme", "Exporting theme..."));
            var deviceModel = GetSelectedDeviceModel();
            if (_dirtyLayers.Count > 0)
            {
                if (LayerGrid.SelectedItem is LayerRow selected && _dirtyLayers.Contains(selected))
                {
                    UpdateLayerFromInputs(selected);
                }
                foreach (var layer in _dirtyLayers.OrderBy(item =>
                             int.TryParse(item.Index, out var index) ? index : int.MaxValue).ToList())
                {
                    await Task.Run(() => _supporter.ApplyLayerAsync(
                        deviceModel, _currentTemplatePath, layer));
                }
                _dirtyLayers.Clear();
            }
            var exportSnapshot = CreateThemeExportSnapshot(deviceModel);
            await Task.Run(() => ExportThemePackage(dialog.FileName, exportSnapshot));
            SetBusy(false, GetLanguageText("status.themeExported", "Theme package exported."));
            MessageBox.Show(this, dialog.FileName,
                GetLanguageText("messages.themeExported", "Theme exported"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.exportFailed", "Export failed."));
            MessageBox.Show(this, ex.Message,
                GetLanguageText("messages.exportThemeFailed", "Theme export failed"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ImportThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = GetLanguageText("dialogs.importTheme", "Import Lian Li theme"),
            Filter = GetLanguageText("dialogs.themePackageFilter", "Lian Li theme package (*.lltheme)|*.lltheme")
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            SetBusy(true, GetLanguageText("status.importingTheme", "Importing theme..."));
            var result = await ImportThemePackageAsync(dialog.FileName);
            _isLoading = true;
            RefreshTemplateList();
            _isLoading = false;
            UseActiveCheck.IsChecked = false;
            TemplateIdBox.Text = result.Id;
            _currentTemplatePath = result.Path;
            await LoadLayersAsync(true);
            SetBusy(false, GetLanguageText("status.themeImported", "Theme package imported."));
            MessageBox.Show(this, result.Id,
                GetLanguageText("messages.themeImported", "Theme imported"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _isLoading = false;
            SetBusy(false, GetLanguageText("status.importFailed", "Import failed."));
            MessageBox.Show(this, ex.Message,
                GetLanguageText("messages.importThemeFailed", "Theme import failed"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportLConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (UseActiveCheck.IsChecked != true &&
            (string.IsNullOrWhiteSpace(_currentTemplatePath) || !File.Exists(_currentTemplatePath)))
        {
            MessageBox.Show(this, GetLanguageText("messages.loadThemeFirst", "Load a theme first."),
                GetLanguageText("messages.exportLConnectFailed", "L-Connect export failed"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = GetLanguageText("dialogs.exportLConnect", "Export for L-Connect"),
            Filter = GetLanguageText("dialogs.lConnectTemplateFilter", "L-Connect template package (*.zip)|*.zip"),
            FileName = $"{SanitizeFileName(_currentTemplateId)}-LConnect.zip",
            DefaultExt = ".zip",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            SetBusy(true, GetLanguageText("status.exportingLConnect", "Creating L-Connect package..."));
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var animationMedia = Layers
                .FirstOrDefault(layer => string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
                ?.Media ?? "";
            var exportBackground = ResolveBackgroundPath(_currentBackgroundPath, animationMedia);

            if (_dirtyLayers.Count > 0)
            {
                if (LayerGrid.SelectedItem is LayerRow selected && _dirtyLayers.Contains(selected))
                {
                    UpdateLayerFromInputs(selected);
                }

                var templatePath = target.TemplatePath;
                foreach (var layer in _dirtyLayers.OrderBy(item =>
                             int.TryParse(item.Index, out var index) ? index : int.MaxValue).ToList())
                {
                    await Task.Run(() => _supporter.ApplyLayerAsync(deviceModel, templatePath, layer));
                }
                _dirtyLayers.Clear();
            }

            if (!string.IsNullOrWhiteSpace(exportBackground) && File.Exists(exportBackground))
            {
                await Task.Run(() => _supporter.SetBackgroundMediaAsync(
                    deviceModel, target.TemplatePath, exportBackground));
            }

            var refreshed = await Task.Run(() => _supporter.LoadTemplatePathAsync(
                deviceModel, target.TemplatePath));
            ApplyTemplateResult(refreshed);

            var exportSnapshot = CreateThemeExportSnapshot(deviceModel);
            await Task.Run(() => ExportLConnectPackage(dialog.FileName, exportSnapshot));
            SetBusy(false, GetLanguageText("status.lConnectExported", "L-Connect package exported."));
            MessageBox.Show(this,
                GetLanguageText("messages.lConnectImportHint",
                    "In L-Connect, open the HydroShift template screen, choose Import Template, and select this ZIP."),
                GetLanguageText("messages.lConnectExported", "L-Connect package exported"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.exportFailed", "Export failed."));
            MessageBox.Show(this, ex.Message,
                GetLanguageText("messages.exportLConnectFailed", "L-Connect export failed"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadLayersAsync(bool useCurrentTemplate)
    {
        try
        {
            SetBusy(true, "Loading layers...");
            var deviceModel = GetSelectedDeviceModel();
            TemplateLoadResult result;
            if (useCurrentTemplate &&
                UseActiveCheck.IsChecked != true &&
                !string.IsNullOrWhiteSpace(_currentTemplatePath))
            {
                var templatePath = _currentTemplatePath;
                result = await Task.Run(() => _supporter.LoadTemplatePathAsync(deviceModel, templatePath));
            }
            else
            {
                var useActiveTemplate = UseActiveCheck.IsChecked == true;
                var templateId = TemplateIdBox.Text;
                result = await Task.Run(() => _supporter.LoadLayersAsync(deviceModel, useActiveTemplate, templateId));
            }

            ApplyTemplateResult(result);
            SetBusy(false, $"Loaded {Layers.Count} layer(s).");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Load failed.");
            MessageBox.Show(this, ex.Message, "Load failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyTemplateResult(TemplateLoadResult result)
    {
        _currentTemplatePath = result.TemplatePath;
        _currentTemplateId = result.TemplateId;
        _currentBackgroundPath = result.BackgroundPath;
        _previewSampleOverrides.Clear();
        _gdiTextCache.Clear();
        _gdiTextInkCache.Clear();
        _gdiTextLayerCache.Clear();
        _dirtyLayers.Clear();
        _editorUndoArmed = false;

        LoadShadowLinks();

        Layers.Clear();
        foreach (var layer in result.Layers)
        {
            if (int.TryParse(layer.Index, out var index) && _shadowLinks.TryGetValue(index, out var sourceIndex))
            {
                layer.Description = $"Shadow of Layer {sourceIndex}";
            }
            Layers.Add(layer);
        }

        TemplateTitleText.Text = string.IsNullOrWhiteSpace(_currentTemplateId) ? "Template: loaded" : $"Template: {_currentTemplateId}";
        TemplatePathText.Text = _currentTemplatePath;
        var displayBackground = !string.IsNullOrWhiteSpace(result.Background)
            ? result.Background
            : Layers.FirstOrDefault(layer => string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))?.Media ?? "";

        BackgroundText.Text = string.IsNullOrWhiteSpace(displayBackground) ? "Background: -" : $"Background: {displayBackground}";
        LayerCountText.Text = string.Format(
            GetText(_languageText, "layers.count", "{0} layers"),
            Layers.Count);
        SyncDeviceFromTemplatePath(_currentTemplatePath);
        var wasLoading = _isLoading;
        _isLoading = true;
        SelectTemplateCombo(_currentTemplatePath);
        _isLoading = wasLoading;
        LoadBackgroundPreview(result.BackgroundPath, displayBackground);
        RequestPreviewDraw();

        if (Layers.Count > 0)
        {
            LayerGrid.SelectedIndex = Math.Min(1, Layers.Count - 1);
        }
    }

    private async Task<(string DeviceModel, string TemplatePath)> ResolveTemplateTargetAsync()
    {
        var deviceModel = GetSelectedDeviceModel();
        if (UseActiveCheck.IsChecked == true)
        {
            var active = await Task.Run(() => _supporter.LoadLayersAsync(deviceModel, true, ""));
            if (string.IsNullOrWhiteSpace(active.TemplatePath) || !File.Exists(active.TemplatePath))
            {
                throw new InvalidOperationException("L-Connect active template could not be resolved.");
            }

            _currentTemplatePath = active.TemplatePath;
            _currentTemplateId = active.TemplateId;
            _currentBackgroundPath = active.BackgroundPath;
            TemplateIdBox.Text = active.TemplateId;
            TemplatePathText.Text = active.TemplatePath;
        }

        if (string.IsNullOrWhiteSpace(_currentTemplatePath) || !File.Exists(_currentTemplatePath))
        {
            throw new InvalidOperationException(GetLanguageText("messages.loadThemeFirst", "Load a theme first."));
        }

        return (deviceModel, _currentTemplatePath);
    }

    private async Task<bool> RefreshIfTemplateStructureChangedAsync(
        string deviceModel,
        string templatePath,
        string preferredIndex)
    {
        var current = await Task.Run(() => _supporter.LoadTemplatePathAsync(deviceModel, templatePath));
        var structureChanged = current.Layers.Count != Layers.Count;
        if (!structureChanged)
        {
            for (var index = 0; index < current.Layers.Count; index++)
            {
                var diskLayer = current.Layers[index];
                var uiLayer = Layers[index];
                if (!string.Equals(diskLayer.Index, uiLayer.Index, StringComparison.Ordinal) ||
                    !string.Equals(diskLayer.Type, uiLayer.Type, StringComparison.OrdinalIgnoreCase))
                {
                    structureChanged = true;
                    break;
                }
            }
        }

        if (!structureChanged)
        {
            return false;
        }

        _dirtyLayers.Clear();
        _editorUndoArmed = false;
        ApplyTemplateResult(current);

        var preferred = int.TryParse(preferredIndex, out var requestedIndex)
            ? Math.Clamp(requestedIndex, 0, Math.Max(0, Layers.Count - 1)).ToString()
            : "";
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            SelectLayerByIndex(preferred);
        }

        SetBusy(false, GetLanguageText(
            "status.templateChangedReloaded",
            "The template changed in L-Connect. Layers were refreshed."));
        MessageBox.Show(
            this,
            GetLanguageText(
                "messages.templateChangedReloaded",
                "The template was changed by L-Connect or another operation. The current layer list has been refreshed; please apply your change again."),
            GetLanguageText("messages.templateChangedTitle", "Template refreshed"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return true;
    }

    private void SelectLayerByIndex(string index)
    {
        var layer = Layers.FirstOrDefault(item => string.Equals(item.Index, index, StringComparison.OrdinalIgnoreCase));
        if (layer == null) return;
        LayerGrid.SelectedItem = layer;
        LayerGrid.ScrollIntoView(layer);
    }

    private void SelectNewestEditableLayer()
    {
        var layer = Layers
            .Where(item => !string.Equals(item.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => int.TryParse(item.Index, out var index) ? index : -1)
            .FirstOrDefault();
        if (layer != null)
        {
            SelectLayerByIndex(layer.Index);
        }
    }

    private void LayerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        _editorUndoArmed = false;
        AddLayerExpander.IsExpanded = false;
        AddLayerExpander.Visibility = Visibility.Collapsed;
        PopulateEditorFromSelection();
        RequestPreviewDraw();
    }

    private void LayerDragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _layerListDragStart = e.GetPosition(LayerGrid);
        _layerListDragLayer = (sender as FrameworkElement)?.DataContext as LayerRow;
        if (_layerListDragLayer != null)
        {
            LayerGrid.SelectedItem = _layerListDragLayer;
            if (sender is UIElement element)
            {
                element.CaptureMouse();
            }
            SetStatus($"Move layer #{_layerListDragLayer.Index}: drag it above or below another layer.");
            e.Handled = true;
        }
    }

    private void LayerDragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _layerListDragLayer == null)
        {
            return;
        }

        var point = e.GetPosition(LayerGrid);
        if (Math.Abs(point.X - _layerListDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _layerListDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var layer = _layerListDragLayer;
        _layerListDragLayer = null;
        if (sender is UIElement element)
        {
            element.ReleaseMouseCapture();
        }
        var data = new DataObject(typeof(LayerRow), layer);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        e.Handled = true;
    }

    private void LayerDragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _layerListDragLayer = null;
        if (sender is UIElement element)
        {
            element.ReleaseMouseCapture();
        }
        e.Handled = true;
    }

    private async void LayerGrid_Drop(object sender, DragEventArgs e)
    {
        var dropPoint = e.GetPosition(LayerGrid);
        var hit = LayerGrid.InputHitTest(dropPoint) as DependencyObject;
        var targetRow = FindVisualParent<DataGridRow>(hit);
        if (!e.Data.GetDataPresent(typeof(LayerRow)) ||
            e.Data.GetData(typeof(LayerRow)) is not LayerRow source ||
            !int.TryParse(source.Index, out var sourceIndex))
        {
            return;
        }

        e.Handled = true;
        var sourcePosition = Layers.IndexOf(source);
        var targetPosition = targetRow?.Item is LayerRow target
            ? Layers.IndexOf(target)
            : Layers.Count;
        if (sourcePosition < 0 || targetPosition < 0)
        {
            return;
        }

        if (targetRow != null && e.GetPosition(targetRow).Y > targetRow.ActualHeight / 2)
        {
            targetPosition++;
        }
        if (sourcePosition < targetPosition)
        {
            targetPosition--;
        }

        var firstMovableIndex = Layers.FirstOrDefault()?.Type.Equals(
            "GraphAnimation", StringComparison.OrdinalIgnoreCase) == true ? 1 : 0;
        targetPosition = Math.Clamp(targetPosition, firstMovableIndex, Layers.Count - 1);
        if (sourcePosition == targetPosition ||
            string.Equals(source.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var originalOrder = Layers.ToList();
        Layers.RemoveAt(sourcePosition);
        Layers.Insert(targetPosition, source);
        RefreshLayerIndexes();
        LayerGrid.SelectedItem = source;
        LayerGrid.ScrollIntoView(source);
        DrawPreview();

        await MoveLayerToIndexAsync(source, sourceIndex, targetPosition, originalOrder);
    }

    private void LayerGrid_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(LayerRow))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        if (e.Effects == DragDropEffects.Move)
        {
            var hit = LayerGrid.InputHitTest(e.GetPosition(LayerGrid)) as DependencyObject;
            var row = FindVisualParent<DataGridRow>(hit);
            if (row?.Item is LayerRow target)
            {
                var place = e.GetPosition(row).Y > row.ActualHeight / 2 ? "below" : "above";
                SetStatus($"Release to move layer {place} #{target.Index}.");
            }
        }
        e.Handled = true;
    }

    private async Task MoveLayerToIndexAsync(
        LayerRow source,
        int sourceIndex,
        int targetIndex,
        IReadOnlyList<LayerRow>? rollbackOrder = null)
    {
        if (string.Equals(source.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            SetBusy(true, "Reordering layer...");
            var target = await ResolveTemplateTargetAsync();
            var currentIndex = sourceIndex;
            var direction = targetIndex < sourceIndex ? "Up" : "Down";
            while (currentIndex != targetIndex)
            {
                var nextIndex = direction == "Up" ? currentIndex - 1 : currentIndex + 1;
                await Task.Run(() => _supporter.MoveLayerAsync(
                    target.DeviceModel, target.TemplatePath, currentIndex.ToString(), direction));
                SwapShadowLinksForLayerMove(currentIndex, nextIndex);
                currentIndex = nextIndex;
            }

            SaveShadowLinks();
            await LoadLayersAsync(true);
            SelectLayerByIndex(targetIndex.ToString());
            SetBusy(false, "Layer order updated.");
        }
        catch (Exception ex)
        {
            if (rollbackOrder != null)
            {
                Layers.Clear();
                foreach (var layer in rollbackOrder)
                {
                    Layers.Add(layer);
                }
                RefreshLayerIndexes();
                LayerGrid.SelectedItem = source;
                DrawPreview();
            }
            SetBusy(false, "Layer reorder failed.");
            MessageBox.Show(this, ex.Message, "Move failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshLayerIndexes()
    {
        for (var index = 0; index < Layers.Count; index++)
        {
            Layers[index].Index = index.ToString();
        }
        LayerGrid.Items.Refresh();
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T match)
            {
                return match;
            }
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void PopulateEditorFromSelection()
    {
        if (LayerGrid.SelectedItem is not LayerRow layer)
        {
            PropertyEditorContent.Visibility = Visibility.Visible;
            SelectedLayerIconText.Text = "i";
            SelectedLayerTypeText.Text = GetLanguageText("properties.noLayerSelected", "Bir layer seçin");
            SelectedLayerDetailText.Text = GetLanguageText("properties.noLayerSelectedHint", "Düzenlemek için soldaki listeden bir layer seçin.");
            GeneralPropertiesCard.Visibility = Visibility.Collapsed;
            GraphEditPanel.Visibility = Visibility.Collapsed;
            ImageEditPanel.Visibility = Visibility.Collapsed;
            EditSeparator.Visibility = Visibility.Collapsed;
            return;
        }

        _isLoading = true;
        PropertyEditorContent.Visibility = Visibility.Visible;
        GeneralPropertiesCard.Visibility = Visibility.Visible;
        SelectedLayerIconText.Text = layer.Type switch
        {
            "GraphItem" => "T",
            "GraphImage" => "I",
            "GraphAnimation" => "?",
            _ => "G"
        };
        SelectedLayerTypeText.Text = layer.Type;
        SelectedLayerDetailText.Text = !string.IsNullOrWhiteSpace(layer.DataSource)
            ? layer.DataSource
            : layer.Media;
        IndexBox.Text = layer.Index;
        XBox.Text = layer.X;
        YBox.Text = layer.Y;
        SizeBox.Text = layer.Size;
        ColorBox.Text = NormalizeColorText(layer.Color);
        TextBox.Text = layer.Text;
        FormatBox.Text = layer.Format;
        BoldCheck.IsChecked = string.Equals(layer.Bold, "True", StringComparison.OrdinalIgnoreCase);
        ItalicCheck.IsChecked = string.Equals(layer.Italic, "True", StringComparison.OrdinalIgnoreCase);
        SetComboText(FontCombo, GetEffectiveLayerFont(layer.Font));
        SetComboText(DataCombo, layer.DataSource);
        SetComboValue(GraphStyleCombo, layer.GraphStyle);
        SetAlignmentCombo(layer.AlignmentIndex);
        FontIntervalBox.Text = layer.FontInterval;
        LineHeightBox.Text = layer.LineHeight;
        if (GraphStyleCombo.SelectedItem is GraphStyleOption selectedStyle)
        {
            layer.GraphStyle = selectedStyle.Code;
            layer.OriginalGraphStyle = selectedStyle.Code;
        }

        // Populate format preset combolist
        UpdateFormatComboItems(layer.DataSource, FormatCombo, FormatBox);
        var needsFormat = SupportsFormat(layer.DataSource);
        if (needsFormat && string.IsNullOrWhiteSpace(layer.Format))
        {
            layer.Format = DefaultFormatForDataSource(layer.DataSource);
            FormatBox.Text = layer.Format;
        }
        FormatLabel.Visibility = needsFormat ? Visibility.Visible : Visibility.Collapsed;
        FormatPanel.Visibility = needsFormat ? Visibility.Visible : Visibility.Collapsed;

        // Toggle Dynamic Panels
        var type = layer.Type ?? "";
        bool isText = type.Equals("GraphItem", StringComparison.OrdinalIgnoreCase);
        bool isGraph = type.Contains("GraphStatuBar", StringComparison.OrdinalIgnoreCase) ||
                       type.Contains("GraphArchBar", StringComparison.OrdinalIgnoreCase) ||
                       type.Contains("GraphLine", StringComparison.OrdinalIgnoreCase) ||
                       type.Contains("DynamicBar", StringComparison.OrdinalIgnoreCase);
        bool isImage = type.Contains("Image", StringComparison.OrdinalIgnoreCase);

        var textVisibility = isText ? Visibility.Visible : Visibility.Collapsed;
        FontLabel.Visibility = textVisibility;
        FontCombo.Visibility = textVisibility;
        SizeLabel.Visibility = textVisibility;
        SizePanel.Visibility = textVisibility;
        SizeLabel.Content = isText ? GetLanguageText("labels.size", "SIZE") : "W";
        SizeHeightLabel.Visibility = Visibility.Collapsed;
        SizeHeightPanel.Visibility = Visibility.Collapsed;
        ColorLabel.Visibility = textVisibility;
        ColorPanel.Visibility = textVisibility;
        SetTextCheck.Visibility = textVisibility;
        TextBox.Visibility = textVisibility;
        AlignmentLabel.Visibility = isText && layer.CanWriteFont("alignment.index") ? Visibility.Visible : Visibility.Collapsed;
        AlignmentCombo.Visibility = AlignmentLabel.Visibility;
        FontIntervalLabel.Visibility = isText && layer.CanWriteFont("interval") ? Visibility.Visible : Visibility.Collapsed;
        FontIntervalBox.Visibility = FontIntervalLabel.Visibility;
        LineHeightLabel.Visibility = isText && layer.CanWrite("LineHeight") ? Visibility.Visible : Visibility.Collapsed;
        LineHeightBox.Visibility = LineHeightLabel.Visibility;
        BoldCheck.Visibility = textVisibility;
        ItalicCheck.Visibility = isText && layer.CanWriteFont("IsItalic") ? Visibility.Visible : Visibility.Collapsed;
        SetTextCheck.IsChecked = isText &&
                                 (layer.ForceText ||
                                  string.Equals(layer.DataSource, "StaticText", StringComparison.OrdinalIgnoreCase));

        GraphStyleLabel.Visibility = isGraph ? Visibility.Visible : Visibility.Collapsed;
        GraphStyleCombo.Visibility = isGraph ? Visibility.Visible : Visibility.Collapsed;
        ZoomRateLabel.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
        ZoomPanel.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
        if (isGraph)
        {
            GraphEditPanel.Visibility = Visibility.Visible;
            ImageEditPanel.Visibility = Visibility.Collapsed;
            EditSeparator.Visibility = Visibility.Visible;

            bool isArcGraph = type.Contains("GraphArchBar", StringComparison.OrdinalIgnoreCase);
            if (isArcGraph)
            {
                WidthLabel.Visibility = Visibility.Collapsed;
                WidthPanel.Visibility = Visibility.Collapsed;
                WidthSlider.Visibility = Visibility.Collapsed;
                HeightLabel.Visibility = Visibility.Collapsed;
                HeightPanel.Visibility = Visibility.Collapsed;
                HeightSlider.Visibility = Visibility.Collapsed;
                RadiusLabel.Visibility = Visibility.Collapsed;
                RadiusPanel.Visibility = Visibility.Collapsed;
                RadiusSlider.Visibility = Visibility.Collapsed;

                DiameterLabel.Visibility = Visibility.Visible;
                DiameterPanel.Visibility = Visibility.Visible;
                DiameterSlider.Visibility = Visibility.Visible;
                ThicknessLabel.Visibility = Visibility.Visible;
                ThicknessPanel.Visibility = Visibility.Visible;
                ThicknessSlider.Visibility = Visibility.Visible;

                DiameterBox.Text = layer.Diameter;
                ThicknessBox.Text = layer.Thickness;
            }
            else
            {
                var widthVisibility = layer.CanWrite("width") ? Visibility.Visible : Visibility.Collapsed;
                var heightVisibility = layer.CanWrite("height") ? Visibility.Visible : Visibility.Collapsed;
                var supportsRadius = layer.CanWrite("radius") &&
                                     !type.Contains("GraphStatuBar", StringComparison.OrdinalIgnoreCase) &&
                                     !type.Contains("GraphLine", StringComparison.OrdinalIgnoreCase);
                var radiusVisibility = supportsRadius ? Visibility.Visible : Visibility.Collapsed;
                WidthLabel.Visibility = widthVisibility;
                WidthPanel.Visibility = widthVisibility;
                WidthSlider.Visibility = widthVisibility;
                HeightLabel.Visibility = heightVisibility;
                HeightPanel.Visibility = heightVisibility;
                HeightSlider.Visibility = heightVisibility;
                RadiusLabel.Visibility = radiusVisibility;
                RadiusPanel.Visibility = radiusVisibility;
                RadiusSlider.Visibility = radiusVisibility;

                DiameterLabel.Visibility = Visibility.Collapsed;
                DiameterPanel.Visibility = Visibility.Collapsed;
                DiameterSlider.Visibility = Visibility.Collapsed;
                ThicknessLabel.Visibility = Visibility.Collapsed;
                ThicknessPanel.Visibility = Visibility.Collapsed;
                ThicknessSlider.Visibility = Visibility.Collapsed;

                WidthBox.Text = layer.Width;
                HeightBox.Text = layer.Height;
                RadiusBox.Text = layer.Radius;
            }

            FrontColorBox.Text = NormalizeColorText(layer.FrontColor);
            BackColorBox.Text = NormalizeColorText(layer.BackColor);
            GradientColorBox.Text = NormalizeColorText(layer.GradientColor);
            UseGradientCheck.IsChecked = string.Equals(layer.UseGradient, "True", StringComparison.OrdinalIgnoreCase);
            GraphDirectionBox.Text = layer.Direction;
            GraphLineWidthBox.Text = layer.LineWidth;
            GraphColumnWidthBox.Text = layer.ColumnWidth;
            GraphBorderWidthBox.Text = layer.BorderWidth;
            GraphInnerCircleRadiusBox.Text = layer.InnerCircleRadius;
            GraphSplitBlockWidthBox.Text = layer.SplitBlockWidth;
            GraphSplitBlankWidthBox.Text = layer.SplitBlankWidth;
            PopulateGraphTypeSelectors(layer);
            SetComboText(GraphTypeNameBox, layer.TypeName);
            SetComboText(GraphSubTypeNameBox, layer.SubTypeName);
            var useSubsection = string.Equals(layer.UseSubsection, "True", StringComparison.OrdinalIgnoreCase);
            GraphUseSubsectionCheck.IsChecked = useSubsection;
            GraphFillBackCheck.IsChecked = string.Equals(layer.FillBack, "True", StringComparison.OrdinalIgnoreCase);
            GraphRevertCheck.IsChecked = string.Equals(layer.Revert, "True", StringComparison.OrdinalIgnoreCase);

            var frontVisibility = layer.CanWrite("FrontColor") || layer.CanWrite("LineColor") || layer.CanWrite("FillColor")
                ? Visibility.Visible
                : Visibility.Collapsed;
            var backVisibility = layer.CanWrite("BackColor") || layer.CanWrite("BorderColor")
                ? Visibility.Visible
                : Visibility.Collapsed;
            FrontColorLabel.Visibility = frontVisibility;
            FrontColorBox.Visibility = frontVisibility;
            FrontColorPickButton.Visibility = frontVisibility;
            BackColorLabel.Visibility = backVisibility;
            BackColorBox.Visibility = backVisibility;
            BackColorPickButton.Visibility = backVisibility;
            GradientColorLabel.Visibility = layer.CanWrite("GradientColor") ? Visibility.Visible : Visibility.Collapsed;
            GradientColorBox.Visibility = GradientColorLabel.Visibility;
            GradientColorPickButton.Visibility = GradientColorLabel.Visibility;
            UseGradientCheck.Visibility = layer.CanWrite("useGradient") ? Visibility.Visible : Visibility.Collapsed;
            GraphDirectionLabel.Visibility = GraphDirectionBox.Visibility = layer.CanWrite("direction") ? Visibility.Visible : Visibility.Collapsed;
            GraphLineWidthLabel.Visibility = GraphLineWidthBox.Visibility = layer.CanWrite("lineWidth") ? Visibility.Visible : Visibility.Collapsed;
            GraphColumnWidthLabel.Visibility = GraphColumnWidthBox.Visibility = layer.CanWrite("columnWidth") ? Visibility.Visible : Visibility.Collapsed;
            GraphBorderWidthLabel.Visibility = GraphBorderWidthBox.Visibility = layer.CanWrite("borderWidth") ? Visibility.Visible : Visibility.Collapsed;
            GraphInnerCircleRadiusLabel.Visibility = GraphInnerCircleRadiusBox.Visibility = layer.CanWrite("InnerCircleRadius") ? Visibility.Visible : Visibility.Collapsed;
            GraphSplitLabel.Visibility = GraphSplitPanel.Visibility = !isArcGraph && useSubsection && (layer.CanWrite("SplitBlockWidth") || layer.CanWrite("SplitBlankWidth")) ? Visibility.Visible : Visibility.Collapsed;
            GraphSplitBlockWidthBox.Visibility = layer.CanWrite("SplitBlockWidth") ? Visibility.Visible : Visibility.Collapsed;
            GraphSplitBlankWidthBox.Visibility = layer.CanWrite("SplitBlankWidth") ? Visibility.Visible : Visibility.Collapsed;
            GraphTypeNameLabel.Visibility = GraphTypeNameBox.Visibility = Visibility.Collapsed;
            GraphSubTypeNameLabel.Visibility = GraphSubTypeNameBox.Visibility = Visibility.Collapsed;
            GraphUseSubsectionCheck.Visibility = layer.CanWrite("useSubsection") ? Visibility.Visible : Visibility.Collapsed;
            GraphFillBackCheck.Visibility = layer.CanWrite("fillBack") ? Visibility.Visible : Visibility.Collapsed;
            GraphRevertCheck.Visibility = layer.CanWrite("revert") ? Visibility.Visible : Visibility.Collapsed;
            GraphFlagsPanel.Visibility =
                GraphUseSubsectionCheck.Visibility == Visibility.Visible ||
                GraphFillBackCheck.Visibility == Visibility.Visible ||
                GraphRevertCheck.Visibility == Visibility.Visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            GraphAdvancedExpander.Visibility =
                GraphDirectionBox.Visibility == Visibility.Visible ||
                GraphLineWidthBox.Visibility == Visibility.Visible ||
                GraphColumnWidthBox.Visibility == Visibility.Visible ||
                GraphBorderWidthBox.Visibility == Visibility.Visible ||
                GraphInnerCircleRadiusBox.Visibility == Visibility.Visible ||
                GraphSplitPanel.Visibility == Visibility.Visible ||
                GraphTypeNameBox.Visibility == Visibility.Visible ||
                GraphSubTypeNameBox.Visibility == Visibility.Visible ||
                GraphFlagsPanel.Visibility == Visibility.Visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
        else if (isImage)
        {
            GraphEditPanel.Visibility = Visibility.Collapsed;
            ImageEditPanel.Visibility = Visibility.Visible;
            EditSeparator.Visibility = Visibility.Visible;

            ImageFileBox.Text = layer.Media;
            ZoomBox.Text = layer.ZoomRate;
            ImageRotateBox.Text = layer.Rotate;
            ImageRectBox.Text = layer.Rect;
            ImageRotateLabel.Visibility = ImageRotateBox.Visibility = layer.CanWrite("rotate") ? Visibility.Visible : Visibility.Collapsed;
            ImageRectLabel.Visibility = ImageRectBox.Visibility = layer.CanWrite("rect") ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            GraphEditPanel.Visibility = Visibility.Collapsed;
            ImageEditPanel.Visibility = Visibility.Collapsed;
            EditSeparator.Visibility = Visibility.Collapsed;
        }

        if (int.TryParse(layer.Index, out var selectedIndex))
        {
            var previous = Layers.FirstOrDefault(item => item.Index == (selectedIndex - 1).ToString());
            var next = Layers.FirstOrDefault(item => item.Index == (selectedIndex + 1).ToString());
            MoveUpButton.IsEnabled = previous != null &&
                                     !string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase) &&
                                     !string.Equals(previous.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase);
            MoveDownButton.IsEnabled = next != null &&
                                       !string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase) &&
                                       !string.Equals(next.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase);
        }

        SyncAllNumericSliders();
        _isLoading = false;
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (LayerGrid.SelectedItem is not LayerRow layer) return;
        var selectedIndex = layer.Index;
        try
        {
            SetBusy(true, "Applying changed layers...");
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var templatePath = target.TemplatePath;
            if (await RefreshIfTemplateStructureChangedAsync(deviceModel, templatePath, selectedIndex))
            {
                return;
            }

            UpdateLayerFromInputs(layer);
            _dirtyLayers.Add(layer);

            if (PairCheck.IsChecked == true)
            {
                var paired = FindPairedLayer(layer);
                if (paired != null)
                {
                    SyncShadowProperties(layer, paired);
                    _dirtyLayers.Add(paired);
                }
            }

            foreach (var dirtyLayer in _dirtyLayers.OrderBy(item => int.TryParse(item.Index, out var index) ? index : int.MaxValue).ToList())
            {
                await Task.Run(() => _supporter.ApplyLayerAsync(deviceModel, templatePath, dirtyLayer));
                dirtyLayer.OriginalGraphStyle = dirtyLayer.GraphStyle;
            }

            _dirtyLayers.Clear();
            _editorUndoArmed = false;
            await LoadLayersAsync(true);
            SelectLayerByIndex(selectedIndex);
            SetBusy(false, "Changed layers applied.");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Apply failed.");
            MessageBox.Show(this, ex.Message, "Apply failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        await RemoveSelectedLayersAsync();
    }

    private async Task RemoveSelectedLayersAsync()
    {
        var selected = LayerGrid.SelectedItems.OfType<LayerRow>().ToList();
        if (selected.Count == 0) return;

        var confirmMsg = selected.Count == 1
            ? FormatLanguageText("messages.removeLayerConfirm", "Remove layer {0}?", selected[0].Index)
            : FormatLanguageText("messages.removeLayersConfirm", "Remove {0} selected layers?", selected.Count);

        if (MessageBox.Show(this, confirmMsg, GetLanguageText("messages.removeLayerTitle", "Remove layer"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            SetBusy(true, "Removing layer(s)...");
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var templatePath = target.TemplatePath;
            if (await RefreshIfTemplateStructureChangedAsync(
                    deviceModel, templatePath, selected[0].Index))
            {
                return;
            }

            var sortedSelected = selected
                .Select(l => new { Layer = l, Index = int.TryParse(l.Index, out var idx) ? idx : -1 })
                .Where(x => x.Index >= 0)
                .OrderByDescending(x => x.Index)
                .ToList();

            foreach (var item in sortedSelected)
            {
                await Task.Run(() => _supporter.RemoveLayerAsync(deviceModel, templatePath, item.Index.ToString()));
                RemoveShadowLinkForDeletedIndex(item.Index);
            }

            await LoadLayersAsync(true);
            SetBusy(false, "Layer(s) removed.");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Remove failed.");
            MessageBox.Show(this, ex.Message, "Remove failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void MoveUpButton_Click(object sender, RoutedEventArgs e) => await MoveSelectedLayerAsync("Up");
    private async void MoveDownButton_Click(object sender, RoutedEventArgs e) => await MoveSelectedLayerAsync("Down");

    private async Task MoveSelectedLayerAsync(string direction)
    {
        if (LayerGrid.SelectedItem is not LayerRow layer || !int.TryParse(layer.Index, out var idx)) return;

        int targetIdx = direction == "Up" ? idx - 1 : idx + 1;
        if (targetIdx < 0 || targetIdx >= Layers.Count) return;
        var targetLayer = Layers.FirstOrDefault(item => item.Index == targetIdx.ToString());
        if (string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetLayer?.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Background animation layer cannot be reordered.", "Move failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            SetBusy(true, $"Moving layer {direction.ToLowerInvariant()}...");
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var templatePath = target.TemplatePath;
            var layerIndex = layer.Index;
            if (await RefreshIfTemplateStructureChangedAsync(deviceModel, templatePath, layerIndex))
            {
                return;
            }
            
            await Task.Run(() => _supporter.MoveLayerAsync(deviceModel, templatePath, layerIndex, direction));
            
            SwapShadowLinksForLayerMove(idx, targetIdx);
            SaveShadowLinks();

            await LoadLayersAsync(true);

            var movedLayer = Layers.FirstOrDefault(l => l.Index == targetIdx.ToString());
            if (movedLayer != null)
            {
                LayerGrid.SelectedItem = movedLayer;
                LayerGrid.ScrollIntoView(movedLayer);
            }

            SetBusy(false, $"Layer moved {direction.ToLowerInvariant()}.");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Move failed.");
            MessageBox.Show(this, ex.Message, "Move failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddTextButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AddTextBox.Text)) return;
        try
        {
            SetBusy(true, "Adding text...");
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var templatePath = target.TemplatePath;
            var text = AddTextBox.Text;
            var x = AddXBox.Text;
            var y = AddYBox.Text;
            var size = AddSizeBox.Text;
            var color = AddColorBox.Text;
            var font = GetComboText(AddFontCombo);
            var bold = AddBoldCheck.IsChecked == true;
            await Task.Run(() => _supporter.AddTextAsync(deviceModel, templatePath, text, x, y, size, color, font, bold));
            
            await FinalizeAddedLayerAsync();
            SetBusy(false, "Text layer added.");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Add text failed.");
            MessageBox.Show(this, ex.Message, "Add text failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddDataButton_Click(object sender, RoutedEventArgs e)
    {
        var data = GetComboText(AddDataCombo);
        if (string.IsNullOrWhiteSpace(data)) return;
        try
        {
            SetBusy(true, "Adding data...");
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var templatePath = target.TemplatePath;
            var x = AddXBox.Text;
            var y = AddYBox.Text;
            var size = AddSizeBox.Text;
            var color = AddColorBox.Text;
            var font = GetComboText(AddFontCombo);
            var bold = AddBoldCheck.IsChecked == true;

            var format = "";
            if (AddFormatCombo.Visibility == Visibility.Visible && AddFormatCombo.SelectedItem != null)
            {
                format = AddFormatCombo.SelectedItem.ToString() ?? "";
            }

            await Task.Run(() => _supporter.AddDataAsync(deviceModel, templatePath, data, x, y, size, color, font, bold, format));
            
            await FinalizeAddedLayerAsync();
            SetBusy(false, "Data layer added.");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Add data failed.");
            MessageBox.Show(this, ex.Message, "Add data failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = GetLanguageText("dialogs.chooseImage", "Choose LCD image"),
            Filter = GetLanguageText("dialogs.imageFilter", "Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All files|*.*")
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            SetBusy(true, "Adding image...");
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var templatePath = target.TemplatePath;
            var imagePath = dialog.FileName;
            var placement = GetImagePlacement(imagePath, AddSizeBox.Text, AddXBox.Text, AddYBox.Text);
            var x = placement.X.ToString(CultureInfo.InvariantCulture);
            var y = placement.Y.ToString(CultureInfo.InvariantCulture);
            var size = placement.Width.ToString(CultureInfo.InvariantCulture);
            
            await Task.Run(() => _supporter.AddImageAsync(deviceModel, templatePath, imagePath, x, y, size));
            
            await FinalizeAddedLayerAsync();
            SetBusy(false, "Image layer added.");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Add image failed.");
            MessageBox.Show(this, ex.Message, "Add image failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddGraphButton_Click(object sender, RoutedEventArgs e)
    {
        var styleCode = GetComboValue(AddGraphStyleCombo);
        if (string.IsNullOrWhiteSpace(styleCode)) return;

        try
        {
            SetBusy(true, "Adding graph...");
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var templatePath = target.TemplatePath;
            var data = GetComboText(AddDataCombo);
            var x = AddXBox.Text;
            var y = AddYBox.Text;
            var size = AddSizeBox.Text;
            var color = AddColorBox.Text;
            await Task.Run(() => _supporter.AddGraphAsync(deviceModel, templatePath, styleCode, data, x, y, size, color, "#20FFFFFF"));
            
            await FinalizeAddedLayerAsync();
            SetBusy(false, "Graph layer added.");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Add graph failed.");
            MessageBox.Show(this, ex.Message, "Add graph failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose LCD background media",
            Filter = "Media|*.mp4;*.gif;*.h264;*.png;*.jpg;*.jpeg|Video|*.mp4;*.gif;*.h264|Images|*.png;*.jpg;*.jpeg|All files|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            SetBusy(true, "Changing background...");
            
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var templatePath = target.TemplatePath;
            var mediaPath = dialog.FileName;

            _currentBackgroundPath = mediaPath;
            LoadBackgroundPreview(mediaPath, Path.GetFileName(mediaPath));
            RequestPreviewDraw();

            using var backgroundCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await Task.Run(() => _supporter.SetBackgroundMediaAsync(deviceModel, templatePath, mediaPath, backgroundCts.Token));

            _backgroundDirty = true;
            await LoadLayersAsync(true);
            SetBusy(false, "Background changed.");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Background failed.");
            MessageBox.Show(this, ex.Message, "Background failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ApplyAllButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true, "Applying unsaved layers...");
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var templatePath = target.TemplatePath;
            var selectedIndex = (LayerGrid.SelectedItem as LayerRow)?.Index ?? "";
            if (await RefreshIfTemplateStructureChangedAsync(deviceModel, templatePath, selectedIndex))
            {
                return;
            }

            if (_dirtyLayers.Count > 0)
            {
                var validIndexes = Layers
                    .Select(item => item.Index)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                _dirtyLayers.RemoveWhere(item =>
                    !Layers.Contains(item) ||
                    !validIndexes.Contains(item.Index) ||
                    (int.TryParse(item.Index, out var index) && index >= Layers.Count));

                var dirtyList = _dirtyLayers.ToList();
                foreach (var layer in dirtyList)
                {
                    if (LayerGrid.SelectedItem is LayerRow selected && selected == layer)
                    {
                        UpdateLayerFromInputs(selected);
                    }
                    await Task.Run(() => _supporter.ApplyLayerAsync(deviceModel, templatePath, layer));

                    if (PairCheck.IsChecked == true)
                    {
                        var paired = FindPairedLayer(layer);
                        if (paired != null)
                        {
                            SyncShadowProperties(layer, paired);
                            await Task.Run(() => _supporter.ApplyLayerAsync(deviceModel, templatePath, paired));
                        }
                    }
                    _dirtyLayers.Remove(layer);
                }
            }

            SetBusy(true, "Sending Apply All...");
            if (!await TriggerLConnectRefreshAsync())
            {
                throw new InvalidOperationException("Changes were saved, but L-Connect did not accept Apply All.");
            }
            await LoadLayersAsync(true);
            if (!string.IsNullOrWhiteSpace(selectedIndex))
            {
                SelectLayerByIndex(selectedIndex);
            }
            SetBusy(false, "Apply All completed.");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Apply All failed.");
            MessageBox.Show(this, ex.Message, "Apply All failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Close L-Connect, restart its services, and open it again?", "Restart L-Connect", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            SetBusy(true, "Restarting L-Connect app & services...");

            var psScript = @"
$serviceError = $false
foreach ($name in @('LConnectServiceWatcher', 'LConnectService')) {
    try {
        $service = Get-Service -Name $name -ErrorAction Stop
        if ($service.Status -ne 'Stopped') {
            Stop-Service -Name $name -Force -ErrorAction Stop
        }
    } catch {
        $serviceError = $true
    }
}
Get-Process | Where-Object { $_.ProcessName -in @('L-Connect 3', 'L-Connect Editor', 'CefSharp.BrowserSubprocess') } | ForEach-Object {
    try { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue } catch {}
}
foreach ($name in @('LConnectService', 'LConnectServiceWatcher')) {
    try { Start-Service -Name $name -ErrorAction Stop } catch {
        $serviceError = $true
    }
}
if ($serviceError) {
    exit 1
} else {
    exit 0
}
";
            
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"{psScript.Replace("\r\n", " ").Replace("\"", "\\\"")}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            var process = new Process { StartInfo = psi };
            bool success = await Task.Run(() =>
            {
                try
                {
                    process.Start();
                    process.WaitForExit(30000);
                    return process.ExitCode == 0;
                }
                catch
                {
                    return false;
                }
            });

            if (!string.IsNullOrWhiteSpace(_currentBackgroundPath))
            {
                var deviceModel = GetSelectedDeviceModel();
                var templatePath = _currentTemplatePath;
                try
                {
                    await Task.Run(() => _supporter.SetBackgroundMediaAsync(deviceModel, templatePath, _currentBackgroundPath));
                }
                catch { }
            }

            var appPath = @"C:\Program Files\Lian-Li\L-Connect 3\L-Connect 3.exe";
            if (System.IO.File.Exists(appPath))
            {
                Process.Start(new ProcessStartInfo(appPath) { UseShellExecute = true });
            }

            _backgroundDirty = false;

            if (!success)
            {
                SetStatus("L-Connect service restart needs Administrator.");
            }
            else
            {
                SetStatus("L-Connect and services restarted.");
            }
            SetBusy(false, "Restart done.");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Restart failed.");
            MessageBox.Show(this, ex.Message, "Restart failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DrawPreview()
    {
        PreviewCanvas.Children.Clear();
        _previewLayerVisuals.Clear();
        _previewSelectionVisuals.Clear();
        _previewResizeHandle = null;
        var selected = LayerGrid.SelectedItem as LayerRow;
        DrawAlignmentGuides(selected);
        foreach (var layer in Layers)
        {
            if (string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var bounds = GetLayerBounds(layer);
            var visual = CreateLayerPreviewVisual(layer, bounds, layer == selected);
            visual.ToolTip = $"{layer.Index} {layer.Type} {layer.DataSource} {layer.Text}";
            visual.Cursor = Cursors.Hand;
            visual.MouseLeftButtonDown += (_, args) =>
            {
                StartPreviewDrag(layer, args.GetPosition(PreviewCanvas));
                args.Handled = true;
            };
            Canvas.SetLeft(visual, bounds.Left);
            Canvas.SetTop(visual, bounds.Top);
            PreviewCanvas.Children.Add(visual);
            _previewLayerVisuals[layer] = visual;
        }

        foreach (var selectedLayer in LayerGrid.SelectedItems.OfType<LayerRow>())
        {
            if (string.Equals(selectedLayer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase)) continue;
            var bounds = GetLayerSelectionBounds(selectedLayer);
            var selectionBorder = new Rectangle
            {
                Width = bounds.Width,
                Height = bounds.Height,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                IsHitTestVisible = false
            };
            Canvas.SetLeft(selectionBorder, bounds.Left);
            Canvas.SetTop(selectionBorder, bounds.Top);
            PreviewCanvas.Children.Add(selectionBorder);
            _previewSelectionVisuals[selectedLayer] = selectionBorder;
        }

        if (selected != null)
        {
            var bounds = GetLayerSelectionBounds(selected);
            var type = selected.Type ?? "";
            bool isAnimation = string.Equals(type, "GraphAnimation", StringComparison.OrdinalIgnoreCase);
            if (!isAnimation)
            {
                var resizeHandle = new Rectangle
                {
                    Width = 10,
                    Height = 10,
                    Fill = Brushes.White,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1,
                    Cursor = Cursors.SizeNWSE
                };
                resizeHandle.MouseLeftButtonDown += (s, args) =>
                {
                    StartResize(selected, args.GetPosition(PreviewCanvas));
                    args.Handled = true;
                };
                Canvas.SetLeft(resizeHandle, bounds.Right - 5);
                Canvas.SetTop(resizeHandle, bounds.Bottom - 5);
                PreviewCanvas.Children.Add(resizeHandle);
                _previewResizeHandle = resizeHandle;
            }
        }

        if (GetSelectedDeviceModel() == "hydroshift-ii-lcd-c")
        {
            PreviewSurface.Clip = new EllipseGeometry(new Point(120, 120), 120, 120);
            PreviewFrame.CornerRadius = new CornerRadius(120);
        }
        else
        {
            PreviewSurface.Clip = null;
            PreviewFrame.CornerRadius = new CornerRadius(0);
        }
    }

    private void RequestPreviewDraw()
    {
        _previewDrawPending = true;
        if (!_previewDrawTimer.IsEnabled)
        {
            _previewDrawTimer.Start();
        }
    }

    private const double TemplateCanvasSize = 480.0;
    private const double PreviewCanvasSize = 240.0;
    private const double PreviewScale = PreviewCanvasSize / TemplateCanvasSize;
    private const double TextPreviewSupersample = 2.0;
    private const double TextPreviewRenderScale = PreviewScale * TextPreviewSupersample;
    private const int GdiTextPadding = 4;
    private const double GdiTextPaddingLayout = GdiTextPadding / TextPreviewSupersample;

    private static double ToPreview(double templateValue) => templateValue * PreviewScale;
    private static double ToTemplate(double previewValue) => previewValue / PreviewScale;
    private static double ToPreviewFontSize(double templateFontSize) => Math.Max(1.0, templateFontSize * PreviewScale);

    private void DrawAlignmentGuides(LayerRow? selected)
    {
        RemovePreviewGuideLines();
        if (selected is null || !TryParseInt(selected.Index, out var selectedIndex))
        {
            return;
        }

        var selectedBounds = GetLayerSelectionBounds(selected);
        var selectedCenterX = selectedBounds.Left + selectedBounds.Width / 2.0;
        var selectedCenterY = selectedBounds.Top + selectedBounds.Height / 2.0;

        if (TryParseInt(selected.X, out var selectedX))
        {
            foreach (var x in new[] { 0, 120, 240, 360, 480 })
            {
                AddGuideLine("X", x, "#626D7E", 0.35);
            }
            AddPreviewGuideLine("X", selectedCenterX, "#FF4FCB", 0.95);
        }

        if (TryParseInt(selected.Y, out var selectedY))
        {
            foreach (var y in new[] { 0, 120, 240, 360, 480 })
            {
                AddGuideLine("Y", y, "#626D7E", 0.35);
            }
            AddPreviewGuideLine("Y", selectedCenterY, "#FF4FCB", 0.95);
        }

        foreach (var layer in Layers)
        {
            if (!TryParseInt(layer.Index, out var index) ||
                index == selectedIndex ||
                string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryParseInt(layer.X, out var x))
            {
                var color = TryParseInt(selected.X, out var sx) && Math.Abs(sx - x) <= 5 ? "#FFD166" : "#8B5CF6";
                AddGuideLine("X", x, color, 0.55);
            }
            if (TryParseInt(layer.Y, out var y))
            {
                var color = TryParseInt(selected.Y, out var sy) && Math.Abs(sy - y) <= 5 ? "#FFD166" : "#8B5CF6";
                AddGuideLine("Y", y, color, 0.55);
            }
        }
    }

    private void AddGuideLine(string axis, int templateValue, string color, double opacity)
    {
        AddPreviewGuideLine(axis, ToPreview(templateValue), color, opacity);
    }

    private void AddPreviewGuideLine(string axis, double previewValue, string color, double opacity)
    {
        var line = new Line
        {
            Tag = "PreviewGuide",
            Stroke = NewBrush(color, color),
            StrokeThickness = 1,
            Opacity = opacity,
            IsHitTestVisible = false
        };

        if (axis == "X")
        {
            line.X1 = previewValue;
            line.X2 = previewValue;
            line.Y1 = 0;
            line.Y2 = PreviewCanvasSize;
        }
        else
        {
            line.X1 = 0;
            line.X2 = PreviewCanvasSize;
            line.Y1 = previewValue;
            line.Y2 = previewValue;
        }

        PreviewCanvas.Children.Add(line);
    }

    private void RemovePreviewGuideLines()
    {
        for (var i = PreviewCanvas.Children.Count - 1; i >= 0; i--)
        {
            if (PreviewCanvas.Children[i] is FrameworkElement { Tag: "PreviewGuide" })
            {
                PreviewCanvas.Children.RemoveAt(i);
            }
        }
    }

    private void StartPreviewDrag(LayerRow layer, Point previewPoint)
    {
        if (string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase)) return;

        PreviewCanvas.Focus();
        PushUndoState();
        _editorUndoArmed = true;

        if (!LayerGrid.SelectedItems.Contains(layer))
        {
            LayerGrid.SelectedItem = layer;
        }
        LayerGrid.ScrollIntoView(layer);
        PopulateEditorFromSelection();

        _dragLayer = layer;
        _isDraggingPreview = true;
        _dragStartTemplatePoint = new Point(ToTemplate(previewPoint.X), ToTemplate(previewPoint.Y));

        _dragStartPositions.Clear();
        _dragStartPreviewBounds.Clear();
        _dragStartSelectionBounds.Clear();
        _shadowStartPositions.Clear();
        foreach (LayerRow selectedLayer in LayerGrid.SelectedItems)
        {
            if (string.Equals(selectedLayer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase)) continue;
            var sx = TryParseInt(selectedLayer.X, out var x) ? x : 0;
            var sy = TryParseInt(selectedLayer.Y, out var y) ? y : 0;
            _dragStartPositions[selectedLayer] = new Point(sx, sy);
            _dragStartPreviewBounds[selectedLayer] = GetLayerBounds(selectedLayer);
            _dragStartSelectionBounds[selectedLayer] = GetLayerSelectionBounds(selectedLayer);

            if (PairCheck.IsChecked == true)
            {
                var paired = FindPairedLayer(selectedLayer);
                if (paired != null)
                {
                    var px = TryParseInt(paired.X, out var shX) ? shX : 0;
                    var py = TryParseInt(paired.Y, out var shY) ? shY : 0;
                    _shadowStartPositions[paired] = new Point(px, py);
                }
            }
        }

        PreviewCanvas.CaptureMouse();
    }

    private void PreviewCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingPreview && _dragLayer is not null)
        {
            var point = e.GetPosition(PreviewCanvas);
            var templateX = ToTemplate(point.X);
            var templateY = ToTemplate(point.Y);
            var dx = (int)Math.Round(templateX - _dragStartTemplatePoint.X);
            var dy = (int)Math.Round(templateY - _dragStartTemplatePoint.Y);

            foreach (var kvp in _dragStartPositions)
            {
                var targetLayer = kvp.Key;
                var startPos = kvp.Value;

                var snapX = (int)startPos.X + dx;
                var snapY = (int)startPos.Y + dy;

                targetLayer.X = snapX.ToString();
                targetLayer.Y = snapY.ToString();

                if (PairCheck.IsChecked == true)
                {
                    var paired = FindPairedLayer(targetLayer);
                    if (paired != null && _shadowStartPositions.TryGetValue(paired, out var shStart))
                    {
                        int parentDx = snapX - (int)startPos.X;
                        int parentDy = snapY - (int)startPos.Y;

                        var snapShX = (int)shStart.X + parentDx;
                        var snapShY = (int)shStart.Y + parentDy;

                        paired.X = snapShX.ToString();
                        paired.Y = snapShY.ToString();
                    }
                }
            }

            UpdateDraggedPreviewVisuals();
        }
        else if (_isResizingPreview && _dragLayer is not null)
        {
            var point = e.GetPosition(PreviewCanvas);
            var templateX = ToTemplate(point.X);
            var templateY = ToTemplate(point.Y);
            var dx = templateX - _resizeStartTemplatePoint.X;
            var dy = templateY - _resizeStartTemplatePoint.Y;

            var type = _dragLayer.Type ?? "";
            bool isGraph = type.Contains("GraphStatuBar", StringComparison.OrdinalIgnoreCase) ||
                           type.Contains("GraphArchBar", StringComparison.OrdinalIgnoreCase) ||
                           type.Contains("GraphLine", StringComparison.OrdinalIgnoreCase) ||
                           type.Contains("DynamicBar", StringComparison.OrdinalIgnoreCase);
            bool isImage = type.Contains("Image", StringComparison.OrdinalIgnoreCase);
            bool isArcGraph = type.Contains("GraphArchBar", StringComparison.OrdinalIgnoreCase);

            if (isGraph)
            {
                if (isArcGraph)
                {
                    var newDiameter = (int)Math.Round(Math.Max(10, _resizeStartDiameter + dx));
                    _dragLayer.Diameter = newDiameter.ToString();
                    DiameterBox.Text = _dragLayer.Diameter;
                }
                else
                {
                    var newWidth = (int)Math.Round(Math.Max(10, _resizeStartWidth + dx));
                    var newHeight = (int)Math.Round(Math.Max(5, _resizeStartHeight + dy));
                    _dragLayer.Width = newWidth.ToString();
                    _dragLayer.Height = newHeight.ToString();
                    WidthBox.Text = _dragLayer.Width;
                    HeightBox.Text = _dragLayer.Height;
                    if (ShouldResizeGraphViaColumnWidth(_dragLayer))
                    {
                        var baseColumnWidth = _resizeStartColumnWidth > 0 ? _resizeStartColumnWidth : 1.0;
                        var newColumnWidth = (int)Math.Round(Math.Max(1, baseColumnWidth + (dx / 10.0)));
                        _dragLayer.ColumnWidth = newColumnWidth.ToString();
                        if (GraphColumnWidthBox.Visibility == Visibility.Visible)
                        {
                            GraphColumnWidthBox.Text = _dragLayer.ColumnWidth;
                        }
                    }
                }
            }
            else if (isImage)
            {
                var newZoom = Math.Clamp(Math.Round(_resizeStartZoom + dx * 0.005, 3), 0.01, 10.0);
                _dragLayer.ZoomRate = FormatZoom(newZoom);
                ZoomBox.Text = _dragLayer.ZoomRate;
            }
            else
            {
                var newSize = (int)Math.Round(Math.Max(5, _resizeStartSize + dx * 0.5));
                _dragLayer.Size = newSize.ToString();
                SizeBox.Text = _dragLayer.Size;

                if (PairCheck.IsChecked == true)
                {
                    var paired = FindPairedLayer(_dragLayer);
                    if (paired != null)
                    {
                        SyncShadowProperties(_dragLayer, paired);
                    }
                }
            }

            RequestPreviewDraw();
        }
    }

    private void UpdateDraggedPreviewVisuals()
    {
        foreach (var layer in _dragStartPositions.Keys)
        {
            if (!_previewLayerVisuals.TryGetValue(layer, out var visual))
            {
                continue;
            }

            var startTemplate = _dragStartPositions[layer];
            var dx = TryParseInt(layer.X, out var currentX) ? ToPreview(currentX - startTemplate.X) : 0;
            var dy = TryParseInt(layer.Y, out var currentY) ? ToPreview(currentY - startTemplate.Y) : 0;
            var bounds = _dragStartPreviewBounds[layer];
            Canvas.SetLeft(visual, bounds.Left + dx);
            Canvas.SetTop(visual, bounds.Top + dy);

            if (_previewSelectionVisuals.TryGetValue(layer, out var selection))
            {
                var selectionBounds = _dragStartSelectionBounds[layer];
                selection.Width = selectionBounds.Width;
                selection.Height = selectionBounds.Height;
                Canvas.SetLeft(selection, selectionBounds.Left + dx);
                Canvas.SetTop(selection, selectionBounds.Top + dy);

                if (ReferenceEquals(layer, _dragLayer) && _previewResizeHandle != null)
                {
                    Canvas.SetLeft(_previewResizeHandle, selectionBounds.Right + dx - 5);
                    Canvas.SetTop(_previewResizeHandle, selectionBounds.Bottom + dy - 5);
                }
            }
        }
        DrawAlignmentGuides(_dragLayer);
    }

    private void PreviewCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingPreview)
        {
            _isDraggingPreview = false;
            PreviewCanvas.ReleaseMouseCapture();
            foreach (var kvp in _dragStartPositions)
            {
                var layer = kvp.Key;
                var startPos = kvp.Value;
                if (layer.X != startPos.X.ToString() || layer.Y != startPos.Y.ToString())
                {
                    _dirtyLayers.Add(layer);
                }
            }
            foreach (var paired in _shadowStartPositions.Keys)
            {
                _dirtyLayers.Add(paired);
            }
            PopulateEditorFromSelection();
            LayerGrid.Items.Refresh();
            DrawPreview();
            SetStatus("Layout changed. Press Apply to save.");
            _dragLayer = null;
            _dragStartPositions.Clear();
            _dragStartPreviewBounds.Clear();
            _dragStartSelectionBounds.Clear();
            _shadowStartPositions.Clear();
        }
        else if (_isResizingPreview)
        {
            _isResizingPreview = false;
            PreviewCanvas.ReleaseMouseCapture();
            if (_dragLayer != null)
            {
                _dirtyLayers.Add(_dragLayer);
                if (PairCheck.IsChecked == true)
                {
                    var paired = FindPairedLayer(_dragLayer);
                    if (paired != null)
                    {
                        SyncShadowProperties(_dragLayer, paired);
                        _dirtyLayers.Add(paired);
                    }
                }
                LayerGrid.Items.Refresh();
                PopulateEditorFromSelection();
                DrawPreview();
                SetStatus("Layer size changed. Press Apply to save.");
            }
            _dragLayer = null;
        }
    }

    private int SnapValue(int value, string axis, LayerRow current)
    {
        var targets = new[] { 0, 120, 240, 360, 480 };
        foreach (var target in targets)
        {
            if (Math.Abs(value - target) <= 5) return target;
        }
        return value;
    }

    private static bool TryParseInt(string value, out int result)
    {
        return int.TryParse(value, out result);
    }

    private FrameworkElement CreateLayerPreviewVisual(LayerRow layer, Rect bounds, bool selected)
    {
        var type = layer.Type ?? "";
        if (type.Contains("Image", StringComparison.OrdinalIgnoreCase))
        {
            var imagePath = ResolveLayerMediaPath(layer.Media);
            if (!string.IsNullOrWhiteSpace(imagePath))
            {
                return CreatePreviewImage(imagePath, bounds.Width, bounds.Height, selected);
            }
        }

        if (type.Contains("GraphStatuBar", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("GraphArchBar", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("GraphLine", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("DynamicBar", StringComparison.OrdinalIgnoreCase))
        {
            return CreateGraphPreview(layer, bounds.Width, bounds.Height, selected);
        }

        var value = GetPreviewText(layer);
        if (string.IsNullOrWhiteSpace(value))
        {
            value = layer.DataSource;
        }

        return CreateGdiTextPreviewVisual(layer, value);
    }

    private FrameworkElement CreateGdiTextPreviewVisual(LayerRow layer, string value)
    {
        var render = GetGdiTextLayerRender(layer, value);

        var image = new Image
        {
            Source = render.Source,
            Width = render.Bounds.Width,
            Height = render.Bounds.Height,
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private async Task FinalizeAddedLayerAsync()
    {
        ShiftShadowLinksForInsert(Layers.Count);
        SaveShadowLinks();
        await LoadLayersAsync(true);
        SelectNewestEditableLayer();

        if (AddWithShadowCheck.IsChecked != true || LayerGrid.SelectedItem is not LayerRow sourceLayer)
        {
            return;
        }

        if (!int.TryParse(sourceLayer.Index, out var sourceIndex))
        {
            throw new InvalidOperationException("Invalid source layer index.");
        }

        await Task.Run(() => _supporter.AddShadowAsync(
            GetSelectedDeviceModel(),
            _currentTemplatePath,
            sourceLayer.Index,
            ShadowXBox.Text,
            ShadowYBox.Text,
            ShadowColorBox.Text));

        ShiftShadowLinksForInsert(sourceIndex);
        _shadowLinks[sourceIndex] = sourceIndex + 1;
        SaveShadowLinks();
        await LoadLayersAsync(true);
        SelectLayerByIndex(sourceIndex.ToString());
    }

    private void PreviewCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, PreviewCanvas)) return;
        if (LayerGrid.SelectionMode == DataGridSelectionMode.Single)
        {
            LayerGrid.SelectedItem = null;
        }
        else
        {
            LayerGrid.UnselectAll();
        }
        PopulateEditorFromSelection();
        RequestPreviewDraw();
    }

    private string GetPreviewText(LayerRow layer)
    {
        var text = layer.Text ?? "";
        var source = layer.DataSource ?? "";
        var isDynamicSource = !string.IsNullOrWhiteSpace(source) &&
                              !source.Equals("StaticText", StringComparison.OrdinalIgnoreCase);

        if (isDynamicSource &&
            _previewSampleOverrides.TryGetValue(source, out var selectedPreviewValue))
        {
            return selectedPreviewValue;
        }

        if (ReferenceEquals(LayerGrid.SelectedItem, layer))
        {
            return isDynamicSource && !layer.PreviewValueEdited
                ? SampleValueFor(source, layer.Format)
                : TextBox.Text;
        }

        if (layer.ForceText)
        {
            return text;
        }

        if (isDynamicSource && !layer.PreviewValueEdited)
        {
            return SampleValueFor(source, layer.Format);
        }

        if (!isDynamicSource &&
            !string.IsNullOrWhiteSpace(text) &&
            !text.Equals(source, StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        if (isDynamicSource)
        {
            if (_previewSampleOverrides.TryGetValue(source, out var previewValue))
            {
                return previewValue;
            }
            return SampleValueFor(source, layer.Format);
        }

        return string.IsNullOrEmpty(text) ? source : text;
    }

    private static bool IsDynamicDataLayer(LayerRow layer)
    {
        var source = layer.DataSource ?? "";
        return string.Equals(layer.Type, "GraphItem", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(source) &&
               !source.Equals("StaticText", StringComparison.OrdinalIgnoreCase);
    }

    private FrameworkElement CreatePreviewImage(string imagePath, double width, double height, bool selected)
    {
        var border = new Border
        {
            Width = width,
            Height = height,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        try
        {
            border.Child = new Image { Source = GetCachedPreviewImage(imagePath), Stretch = Stretch.Uniform };
        }
        catch
        {
            border.Child = new Rectangle { Fill = NewBrush("#4F8CFF", "#4F8CFF") };
        }
        return border;
    }

    private string GetH2GraphPreviewStyle(LayerRow layer)
    {
        if (layer == null) return "";
        var style = layer.GraphStyle ?? "";
        if (Regex.IsMatch(style, "H2_Bar_chart_1|(^|::)Bar1($|::)", RegexOptions.IgnoreCase)) return "bar1";
        if (Regex.IsMatch(style, "H2_Bar_chart_2|(^|::)Bar2($|::)", RegexOptions.IgnoreCase)) return "bar2";
        if (Regex.IsMatch(style, "H2_Donut chart_1|(^|::)Donut1($|::)", RegexOptions.IgnoreCase)) return "donut1";
        if (Regex.IsMatch(style, "H2_Donut chart_2|(^|::)Donut2($|::)", RegexOptions.IgnoreCase)) return "donut2";
        if (Regex.IsMatch(style, "H2_Donut chart_3|(^|::)Donut3($|::)", RegexOptions.IgnoreCase)) return "donut3";
        if (Regex.IsMatch(style, "H2_Stream Chart_1|(^|::)Stream($|::)", RegexOptions.IgnoreCase)) return "stream";

        var type = layer.Type ?? "";
        if (string.Equals(type, "GraphLine", StringComparison.OrdinalIgnoreCase)) return "stream";
        if (string.Equals(type, "GraphArchBar", StringComparison.OrdinalIgnoreCase)) return "donut1";
        if (string.Equals(type, "GraphStatuBar", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "DynamicBar", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(layer.Radius, out var rad) && rad <= 1) return "bar2";
            return "bar1";
        }
        return "";
    }

    private FrameworkElement CreateGraphPreview(LayerRow layer, double width, double height, bool selected)
    {
        var canvas = new Canvas
        {
            Width = width,
            Height = height,
            Background = Brushes.Transparent
        };
        var graphStyle = layer.GraphStyle ?? "";
        var h2Style = GetH2GraphPreviewStyle(layer);
        var type = layer.Type ?? "";

        if (h2Style.StartsWith("donut", StringComparison.OrdinalIgnoreCase) || type.Contains("Arch", StringComparison.OrdinalIgnoreCase))
        {
            var ellipseWidth = Math.Max(10, width - 10);
            var ellipseHeight = Math.Max(10, height - 10);
            double.TryParse(layer.Thickness, out var thickVal);
            var thickness = thickVal > 0 ? ToPreview(thickVal) : 8.0;

            var back = new Ellipse
            {
                Width = ellipseWidth,
                Height = ellipseHeight,
                Stroke = h2Style == "donut2" ? NewBrush(layer.BackColor, "#55FFFFFF") : NewBrush(layer.BackColor, "#55FFFFFF"),
                StrokeThickness = thickness,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Canvas.SetLeft(back, (width - ellipseWidth) / 2);
            Canvas.SetTop(back, (height - ellipseHeight) / 2);
            canvas.Children.Add(back);

            var strokeDash = new DoubleCollection();
            PenLineCap lineCap = PenLineCap.Flat;
            if (h2Style == "donut1")
            {
                strokeDash = new DoubleCollection { 3.8, 8.0 };
                lineCap = PenLineCap.Flat;
            }
            else if (h2Style == "donut3")
            {
                lineCap = PenLineCap.Round;
            }

            var front = new Ellipse
            {
                Width = ellipseWidth,
                Height = ellipseHeight,
                Stroke = CreateGraphFill(layer),
                StrokeThickness = thickness,
                StrokeDashArray = strokeDash.Count > 0 ? strokeDash : null,
                StrokeStartLineCap = lineCap,
                StrokeEndLineCap = lineCap,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(-90),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Canvas.SetLeft(front, (width - ellipseWidth) / 2);
            Canvas.SetTop(front, (height - ellipseHeight) / 2);
            canvas.Children.Add(front);
        }
        else
        {
            double.TryParse(layer.Radius, out var radVal);
            var rad = radVal > 0 ? ToPreview(radVal) : 4.0;

            var barBg = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(rad),
                Background = NewBrush(layer.BackColor, "#20FFFFFF"),
                BorderBrush = NewBrush("#20242A", "#20242A"),
                BorderThickness = new Thickness(1)
            };
            
            var barGrid = new Grid
            {
                Width = width,
                Height = height
            };
            barGrid.Children.Add(barBg);

            if (h2Style == "stream")
            {
                var streamCanvas = new Canvas
                {
                    Width = width,
                    Height = height
                };
                var area = new Polygon();
                var fillWidth = width;
                if (double.TryParse(layer.ColumnWidth, NumberStyles.Float, CultureInfo.InvariantCulture, out var columnWidth) && columnWidth > 0)
                {
                    fillWidth = Math.Clamp(ToPreview(columnWidth * 14.0), Math.Min(width, 8.0), width);
                }
                var points = new PointCollection
                {
                    new Point(0, height),
                    new Point(fillWidth * 0.2, height * 0.4),
                    new Point(fillWidth * 0.4, height * 0.1),
                    new Point(fillWidth * 0.6, height * 0.7),
                    new Point(fillWidth * 0.8, height * 0.3),
                    new Point(fillWidth, height * 0.8),
                    new Point(fillWidth, height)
                };
                area.Points = points;
                area.Fill = Brushes.Transparent;
                area.Stroke = CreateGraphFill(layer);
                area.StrokeThickness = Math.Max(1.0, double.TryParse(layer.LineWidth, out var streamLine) ? ToPreview(streamLine) : 1.0);
                streamCanvas.Children.Add(area);
                barGrid.Children.Add(streamCanvas);
            }
            else if (h2Style == "bar2")
            {
                var barFill = new Border
                {
                    Width = width * 0.65,
                    Height = height,
                    CornerRadius = new CornerRadius(0),
                    Background = CreateGraphFill(layer),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };
                barGrid.Children.Add(barFill);
            }
            else if (type.Contains("StatuBar") || type.Contains("DynamicBar"))
            {
                double.TryParse(layer.SplitBlockWidth, out var sbwCheck);
                double.TryParse(layer.Radius, out var rCheck);
                bool isSegmented = (sbwCheck > 0) || (rCheck <= 1);
                
                if (isSegmented)
                {
                    var segCanvas = new Canvas
                    {
                        Width = width,
                        Height = height
                    };
                    double.TryParse(layer.SplitBlankWidth, out var sbkVal);
                    var gap = sbkVal > 0 ? Math.Max(1.0, ToPreview(sbkVal)) : 2.0;
                    double.TryParse(layer.SplitBlockWidth, out var sbwVal);
                    var segW = sbwVal > 0 ? Math.Max(2.0, ToPreview(sbwVal)) : 6.0;
                    int count = Math.Max(1, (int)(width / (segW + gap)));
                    int fillCount = (int)(count * 0.65);

                    for (int s = 0; s < count; s++)
                    {
                        var seg = new Rectangle
                        {
                            Width = segW,
                            Height = height,
                            RadiusX = Math.Min(rad, 2),
                            RadiusY = Math.Min(rad, 2),
                            Fill = s < fillCount ? CreateGraphFill(layer) : NewBrush(layer.BackColor, "#30303030")
                        };
                        Canvas.SetLeft(seg, s * (segW + gap));
                        Canvas.SetTop(seg, 0);
                        segCanvas.Children.Add(seg);
                    }
                    barGrid.Children.Add(segCanvas);
                }
                else
                {
                    var barFill = new Border
                    {
                        Width = h2Style == "bar1" ? width * 0.18 : width * 0.65,
                        Height = h2Style == "bar1" ? Math.Max(2, height - 3) : height,
                        CornerRadius = new CornerRadius(rad),
                        Background = CreateGraphFill(layer),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    barGrid.Children.Add(barFill);
                }
            }
            else
            {
                var barFill = new Border
                {
                    Width = width * 0.65,
                    Height = height,
                    CornerRadius = new CornerRadius(rad),
                    Background = CreateGraphFill(layer),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };
                barGrid.Children.Add(barFill);
            }
            canvas.Children.Add(barGrid);
        }

        var border = new Border
        {
            Width = width,
            Height = height,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Child = canvas
        };
        return border;
    }

    private string ResolveLayerMediaPath(string mediaName)
    {
        if (string.IsNullOrWhiteSpace(mediaName)) return "";
        if (File.Exists(mediaName)) return mediaName;

        var templateDir = Path.GetDirectoryName(_currentTemplatePath) ?? "";
        var deviceDir = Path.GetDirectoryName(templateDir) ?? "";

        var candidates = new List<string>
        {
            Path.Combine(templateDir, mediaName),
            Path.Combine(deviceDir, "image", mediaName),
            Path.Combine(deviceDir, "video", mediaName),
            Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", GetSelectedDeviceModel(), "image", mediaName),
            Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", GetSelectedDeviceModel(), "video", mediaName),
            Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", GetSelectedDeviceModel(), "template", mediaName),
            Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", "uploaded", mediaName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        var baseName = Path.GetFileNameWithoutExtension(mediaName);
        var extensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".mp4", ".h264" };
        var searchDirs = new[] { Path.Combine(deviceDir, "image"), Path.Combine(deviceDir, "video"), Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", GetSelectedDeviceModel(), "image"), Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", GetSelectedDeviceModel(), "video") };

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, baseName + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return "";
    }

    private void AttachAlphaColorMenu(Button button, TextBox targetBox)
    {
        var menu = new ContextMenu();
        var items = new[]
        {
            new { Label = "Transparent", Alpha = 0, Black = true },
            new { Label = "20% alpha", Alpha = 32, Black = false },
            new { Label = "40% alpha", Alpha = 102, Black = false },
            new { Label = "70% alpha", Alpha = 179, Black = false },
            new { Label = "Opaque", Alpha = 255, Black = false }
        };

        foreach (var def in items)
        {
            var mi = new MenuItem { Header = def.Label };
            var alphaVal = def.Alpha;
            var blackVal = def.Black;
            mi.Click += (s, e) =>
            {
                SetColorBoxAlpha(targetBox, alphaVal, blackVal);
                PushUndoState();
                DrawPreview();
            };
            menu.Items.Add(mi);
        }

        button.ContextMenu = menu;
        button.PreviewMouseRightButtonUp += (sender, args) =>
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
            args.Handled = true;
        };
    }

    private void SetColorBoxAlpha(TextBox targetBox, int alpha, bool transparentBlack)
    {
        var hex = targetBox.Text.Trim();
        if (transparentBlack)
        {
            targetBox.Text = "#00000000";
            return;
        }

        if (!hex.StartsWith("#"))
        {
            hex = "#" + hex;
        }

        if (hex.Length == 4)
        {
            hex = $"#{hex[1]}{hex[1]}{hex[2]}{hex[2]}{hex[3]}{hex[3]}";
        }

        string rawHex = hex.Substring(1);
        if (rawHex.Length == 8)
        {
            rawHex = rawHex.Substring(2);
        }
        else if (rawHex.Length != 6)
        {
            rawHex = "FFFFFF";
        }

        string newHex = alpha == 255 ? $"#{rawHex}" : $"#{alpha:X2}{rawHex}";
        targetBox.Text = newHex;
    }

    private Rect GetLayerBounds(LayerRow layer)
    {
        var type = layer.Type ?? "";
        if (!double.TryParse(layer.X, out var lx)) lx = 0;
        if (!double.TryParse(layer.Y, out var ly)) ly = 0;

        double left = ToPreview(lx);
        double top = ToPreview(ly);

        if (type.Contains("GraphStatuBar", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("GraphDynamicBar", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("GraphLine", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("DynamicBar", StringComparison.OrdinalIgnoreCase))
        {
            double w = double.TryParse(layer.Width, out var lw) && lw > 0 ? lw : 200.0;
            double h = double.TryParse(layer.Height, out var lh) && lh > 0 ? lh : 20.0;
            return new Rect(left, top, ToPreview(w), ToPreview(h));
        }
        else if (type.Contains("GraphArchBar", StringComparison.OrdinalIgnoreCase))
        {
            double d = double.TryParse(layer.Diameter, out var ld) && ld > 0 ? ld : 120.0;
            return new Rect(left, top, ToPreview(d), ToPreview(d));
        }
        else if (type.Contains("Image", StringComparison.OrdinalIgnoreCase) ||
                 type.Contains("Animation", StringComparison.OrdinalIgnoreCase))
        {
            double w = 80.0;
            double h = 80.0;
            if (!string.IsNullOrWhiteSpace(layer.Media))
            {
                var imgPath = ResolveLayerMediaPath(layer.Media);
                if (!string.IsNullOrWhiteSpace(imgPath))
                {
                    if (_imageBoundsCache.TryGetValue(imgPath, out var cachedSize))
                    {
                        w = cachedSize.Width;
                        h = cachedSize.Height;
                    }
                    else if (File.Exists(imgPath))
                    {
                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.UriSource = new Uri(imgPath, UriKind.Absolute);
                            bitmap.EndInit();
                            w = bitmap.PixelWidth;
                            h = bitmap.PixelHeight;
                            _imageBoundsCache[imgPath] = new Size(w, h);
                        }
                        catch { }
                    }
                }
            }
            double zoom = TryParseZoom(layer.ZoomRate, out var zr) && zr > 0 ? zr : 1.0;
            w *= zoom;
            h *= zoom;
            return new Rect(left, top, ToPreview(w), ToPreview(h));
        }
        else
        {
            if (!double.TryParse(layer.Size, out var lsize)) lsize = 20;
            var text = GetPreviewText(layer);
            if (string.IsNullOrWhiteSpace(text)) text = layer.DataSource;
            return GetGdiTextLayerRender(layer, text).Bounds;
        }
    }

    private Rect GetLayerSelectionBounds(LayerRow layer)
    {
        var type = layer.Type ?? "";
        if (!type.Equals("GraphItem", StringComparison.OrdinalIgnoreCase))
        {
            return GetLayerBounds(layer);
        }

        var text = GetPreviewText(layer);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = layer.DataSource;
        }

        return GetGdiTextLayerRender(layer, text).Bounds;
    }

    private static bool ShouldResizeGraphViaColumnWidth(LayerRow layer)
    {
        var type = layer.Type ?? "";
        return type.Equals("GraphLine", StringComparison.OrdinalIgnoreCase) &&
               layer.CanWrite("columnWidth");
    }

    private static int GetTextAlignmentIndex(LayerRow layer)
    {
        return int.TryParse(layer.AlignmentIndex, out var alignIdx) && alignIdx is >= 0 and <= 2
            ? alignIdx
            : 0;
    }

    private static double GetTextInterval(LayerRow layer)
    {
        return double.TryParse(layer.FontInterval, NumberStyles.Float, CultureInfo.InvariantCulture, out var interval)
            ? interval
            : 0.0;
    }

    private static double GetTextAlignmentOffset(double width, int alignmentIndex)
    {
        return alignmentIndex switch
        {
            1 => width / 2.0,
            2 => width,
            _ => 0.0
        };
    }

    private static readonly (string Code, string Label)[] TimeFormats =
    {
        ("00:00", "Hour:Minute"),
        ("00:00:00", "Hour:Minute:Second"),
        ("h_12", "Hour (12-hour)"),
        ("h_24", "Hour (24-hour)"),
        ("m", "Minute"),
        ("s", "Second"),
        ("AM", "AM"),
        ("PM", "PM")
    };

    private static readonly (string Code, string Label)[] DateFormats =
    {
        ("Y-M-D", "Year-Month-Day"),
        ("D-M-Y", "Day-Month-Year"),
        ("D.M.Y", "Day.Month.Year"),
        ("M", "Month"),
        ("D", "Day")
    };

    private static string SampleValueFor(string dataSource, string formatText = "")
    {
        if (string.IsNullOrWhiteSpace(dataSource)) return "";
        var key = dataSource.ToUpperInvariant();
        var fmt = formatText ?? "";

        if (TryGetLiveSensorValue(key, out var liveValue))
        {
            return FormatLivePreviewValue(key, liveValue);
        }

        switch (key)
        {
            case "CPUTEMP": return "52";
            case "CPUTEMP_F": return "126";
            case "GPUTEMP": return "54";
            case "GPUTEMP_F": return "129";
            case "CPUCLOCK": return "5200";
            case "CPUCLOCK_G": return "5.2";
            case "GPUCLOCK": return "2750";
            case "GPUCLOCK_G": return "2.8";
            case "CPULOAD": return "16";
            case "GPULOAD": return "17";
            case "GPURAMLOAD": return "48";
            case "GPURAM": return "5.7";
            case "GPUVALIDRAM": return "12.0";
            case "RAMLOAD": return "42";
            case "RAM": return "13.4";
            case "RAMVALID": return "16.0";
            case "RAMMODEL": return "G.Skill DDR5";
            case "RAMTOTAL": return "32.0";
            case "RAM_GB": return "13.4";
            case "RAMVALID_GB": return "16.0";
            case "RAMTOTAL_GB": return "32.0";
            case "CPUPWR": return "65";
            case "CPUPOWER": return "65";
            case "GPUPWR": return "175";
            case "GPUPOWER": return "175";
            case "CPUVOLTAGE": return "1.25";
            case "GPUVOLTAGE": return "0.95";
            case "CPUFAN": return "1250";
            case "GPUFAN": return "1400";
            case "HDDTEMP": return "38";
            case "HDDTEMP_F": return "100";
            case "HDDUSED": return "64";
            case "DRVLOAD": return "12";
            case "PUMP": return "2600";
            case "WATERPUMP": return "2600";
            case "CASEFAN1": return "1050";
            case "CASEFAN2":
            case "CASEFAN3":
            case "CASEFAN4":
            case "CASEFAN5":
            case "CASEFAN6":
            case "CASEFAN7":
            case "CASEFAN8": return "1100";
            case "UPSPEED": return "8.5";
            case "DOWNDSPEED": return "45.2";
            case "FPS":
            case "FPS_AVG": return "120";
            case "GPUMODEL": return "GPU";
            case "VOLUME": return "50";
            case "WEATHER": return "25";
            case "TIME":
                var currentTime = DateTime.Now;
                if (fmt == "00:00") return currentTime.ToString("HH:mm");
                if (fmt == "00:00:00") return currentTime.ToString("HH:mm:ss");
                if (fmt is "HH:MM:SS" or "H:M:S") return currentTime.ToString("HH:mm:ss");
                if (fmt == "h_12") return currentTime.ToString("%h");
                if (fmt == "h_24") return currentTime.ToString("%H");
                if (fmt == "m") return currentTime.ToString("%m");
                if (fmt == "s") return currentTime.ToString("%s");
                if (fmt is "AM" or "PM") return currentTime.ToString("tt", CultureInfo.InvariantCulture);
                return currentTime.ToString("HH:mm");
            case "DATE":
                var now = DateTime.Now;
                return fmt switch
                {
                    "Y-M-D" => now.ToString("yyyy-MM-dd"),
                    "D-M-Y" => now.ToString("dd-MM-yyyy"),
                    "D.M.Y" => now.ToString("dd.MM.yyyy"),
                    "M" => now.ToString("MM"),
                    "D" => now.ToString("dd"),
                    _ => now.ToString("yyyy-MM-dd")
                };
            case "DAY":
                if (fmt is "ddd" or "Sun") return DateTime.Now.ToString("ddd", CultureInfo.InvariantCulture);
                return DateTime.Now.ToString("ddd", CultureInfo.InvariantCulture);
            default:
                return dataSource;
        }
    }

    private static bool TryGetLiveSensorValue(string key, out string value)
    {
        value = "";
        if (key is "TIME" or "DATE" or "DAY")
        {
            return false;
        }

        RefreshLiveSensorCache();
        if (!_liveSensorValueCache.TryGetValue(key, out var cachedValue) ||
            string.IsNullOrWhiteSpace(cachedValue))
        {
            return false;
        }

        value = cachedValue;
        return true;
    }

    private static string FormatLivePreviewValue(string key, string value)
    {
        if (key is "CPUPWR" or "CPUPOWER" or "GPUPWR" or "GPUPOWER")
        {
            var normalized = value.Replace(',', '.');
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
            {
                return Math.Round(numeric).ToString("0", CultureInfo.InvariantCulture);
            }
        }

        return value;
    }

    private static void RefreshLiveSensorCache()
    {
        try
        {
            if (!File.Exists(HwInfoSensorsPath))
            {
                return;
            }

            var info = new FileInfo(HwInfoSensorsPath);
            var now = DateTime.UtcNow;
            if (_liveSensorValueCache.Count > 0 &&
                _liveSensorCacheWriteUtc == info.LastWriteTimeUtc &&
                (now - _liveSensorCacheReadUtc).TotalSeconds < 2)
            {
                return;
            }

            using var stream = File.Open(HwInfoSensorsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var doc = JsonDocument.Parse(stream);
            var readings = new List<SensorReading>();
            foreach (var sensor in doc.RootElement.EnumerateArray())
            {
                var sensorName = sensor.TryGetProperty("Name", out var sensorNameEl)
                    ? sensorNameEl.GetString() ?? ""
                    : "";
                if (!sensor.TryGetProperty("SensorValues", out var groups))
                {
                    continue;
                }

                foreach (var group in groups.EnumerateObject())
                {
                    if (group.Value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var item in group.Value.EnumerateArray())
                    {
                        if (item.TryGetProperty("IsValid", out var validEl) &&
                            validEl.TryGetInt32(out var valid) &&
                            valid != 1)
                        {
                            continue;
                        }

                        if (!item.TryGetProperty("Value", out var valueEl) ||
                            !TryReadDouble(valueEl, out var numericValue))
                        {
                            continue;
                        }

                        var name = item.TryGetProperty("ReadingLocationName", out var nameEl)
                            ? nameEl.GetString() ?? ""
                            : "";
                        var unit = item.TryGetProperty("Unit", out var unitEl)
                            ? unitEl.GetString() ?? ""
                            : "";

                        readings.Add(new SensorReading(sensorName, group.Name, name, numericValue, unit));
                    }
                }
            }

            var fresh = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            PutRounded(fresh, "CPUTEMP", FindReading(readings, r => IsCpuSensor(r) && r.Group == "READING_TEMP" && r.Name.Contains("CPU (Tctl/Tdie)", StringComparison.OrdinalIgnoreCase)));
            PutFahrenheit(fresh, "CPUTEMP_F", FindReading(readings, r => IsCpuSensor(r) && r.Group == "READING_TEMP" && r.Name.Contains("CPU (Tctl/Tdie)", StringComparison.OrdinalIgnoreCase)));
            PutRounded(fresh, "GPUTEMP", FindReading(readings, r => IsDiscreteGpuSensor(r) && r.Group == "READING_TEMP" && r.Name.Equals("GPU Temperature", StringComparison.OrdinalIgnoreCase)));
            PutFahrenheit(fresh, "GPUTEMP_F", FindReading(readings, r => IsDiscreteGpuSensor(r) && r.Group == "READING_TEMP" && r.Name.Equals("GPU Temperature", StringComparison.OrdinalIgnoreCase)));
            PutRounded(fresh, "CPULOAD", FindReading(readings, r => IsCpuSensor(r) && r.Group == "READING_USAGE" && r.Name.Equals("Total CPU Usage", StringComparison.OrdinalIgnoreCase)));
            PutRounded(fresh, "GPULOAD", FindReading(readings, r => IsDiscreteGpuSensor(r) && r.Group == "READING_USAGE" && (r.Name.Equals("GPU Utilization", StringComparison.OrdinalIgnoreCase) || r.Name.Equals("GPU Core Load", StringComparison.OrdinalIgnoreCase))));
            PutRounded(fresh, "RAMLOAD", FindReading(readings, r => r.Sensor.Contains("System", StringComparison.OrdinalIgnoreCase) && r.Name.Equals("Physical Memory Load", StringComparison.OrdinalIgnoreCase)));
            PutRounded(fresh, "CPUPWR", FindReading(readings, r => IsCpuSensor(r) && r.Group == "READING_POWER" && r.Name.Equals("CPU Package Power", StringComparison.OrdinalIgnoreCase)));
            PutAlias(fresh, "CPUPOWER", "CPUPWR");
            PutRounded(fresh, "GPUPWR", FindReading(readings, r => IsDiscreteGpuSensor(r) && r.Group == "READING_POWER" && r.Name.Equals("GPU Power", StringComparison.OrdinalIgnoreCase)));
            PutAlias(fresh, "GPUPOWER", "GPUPWR");
            PutRounded(fresh, "CPUVOLTAGE", FindReading(readings, r => IsCpuSensor(r) && r.Group == "READING_VOLT" && r.Name.Contains("CPU VDDCR_VDD Voltage", StringComparison.OrdinalIgnoreCase)), 2);
            PutRounded(fresh, "GPUVOLTAGE", FindReading(readings, r => IsDiscreteGpuSensor(r) && r.Group == "READING_VOLT" && r.Name.Equals("GPU Core Voltage", StringComparison.OrdinalIgnoreCase)), 2);
            PutRounded(fresh, "CPUCLOCK", FindBestClock(readings, true));
            PutClockGhz(fresh, "CPUCLOCK_G", FindBestClock(readings, true));
            PutRounded(fresh, "GPUCLOCK", FindBestClock(readings, false));
            PutClockGhz(fresh, "GPUCLOCK_G", FindBestClock(readings, false));
            PutRounded(fresh, "HDDTEMP", FindReading(readings, r => r.Group == "READING_TEMP" && r.Name.Equals("Drive Temperature", StringComparison.OrdinalIgnoreCase)));
            PutFahrenheit(fresh, "HDDTEMP_F", FindReading(readings, r => r.Group == "READING_TEMP" && r.Name.Equals("Drive Temperature", StringComparison.OrdinalIgnoreCase)));
            PutRounded(fresh, "FPS", FindReading(readings, r => r.Sensor.Contains("PresentMon", StringComparison.OrdinalIgnoreCase) && r.Unit.Equals("FPS", StringComparison.OrdinalIgnoreCase) && r.Name.Contains("Presented (avg)", StringComparison.OrdinalIgnoreCase)));
            PutAlias(fresh, "FPS_AVG", "FPS");

            _liveSensorValueCache.Clear();
            foreach (var item in fresh)
            {
                _liveSensorValueCache[item.Key] = item.Value;
            }

            _liveSensorCacheWriteUtc = info.LastWriteTimeUtc;
            _liveSensorCacheReadUtc = now;
        }
        catch
        {
            // Preview should never fail because L-Connect/HWiNFO telemetry is temporarily unavailable.
        }
    }

    private readonly record struct SensorReading(string Sensor, string Group, string Name, double Value, string Unit);

    private static bool TryReadDouble(JsonElement element, out double value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetDouble(out value);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString() ?? "";
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                   double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                   double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        value = 0;
        return false;
    }

    private static SensorReading? FindReading(IEnumerable<SensorReading> readings, Func<SensorReading, bool> predicate)
    {
        return readings.FirstOrDefault(predicate) is var reading && !string.IsNullOrEmpty(reading.Name)
            ? reading
            : null;
    }

    private static SensorReading? FindBestClock(IEnumerable<SensorReading> readings, bool cpu)
    {
        if (cpu)
        {
            var cpuClocks = readings
                .Where(r => IsCpuSensor(r) && r.Group == "READING_CLOCK" && Regex.IsMatch(r.Name, @"^Core \d+ Clock", RegexOptions.IgnoreCase))
                .OrderByDescending(r => r.Value)
                .ToList();
            return cpuClocks.Count > 0 ? cpuClocks[0] : null;
        }

        return FindReading(readings, r => IsDiscreteGpuSensor(r) && r.Group == "READING_CLOCK" && r.Name.Equals("GPU Clock", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCpuSensor(SensorReading reading)
    {
        return reading.Sensor.StartsWith("CPU ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDiscreteGpuSensor(SensorReading reading)
    {
        if (!reading.Sensor.StartsWith("GPU ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return reading.Sensor.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
               reading.Sensor.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
               !reading.Sensor.Contains("AMD Radeon", StringComparison.OrdinalIgnoreCase);
    }

    private static void PutRounded(Dictionary<string, string> target, string key, SensorReading? reading, int decimals = 0)
    {
        if (reading is null)
        {
            return;
        }

        target[key] = Math.Round(reading.Value.Value, decimals).ToString(decimals == 0 ? "0" : $"F{decimals}", CultureInfo.InvariantCulture);
    }

    private static void PutFahrenheit(Dictionary<string, string> target, string key, SensorReading? reading)
    {
        if (reading is null)
        {
            return;
        }

        var fahrenheit = reading.Value.Value * 9.0 / 5.0 + 32.0;
        target[key] = Math.Round(fahrenheit).ToString("0", CultureInfo.InvariantCulture);
    }

    private static void PutClockGhz(Dictionary<string, string> target, string key, SensorReading? reading)
    {
        if (reading is null)
        {
            return;
        }

        target[key] = Math.Round(reading.Value.Value / 1000.0, 1).ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static void PutAlias(Dictionary<string, string> target, string alias, string source)
    {
        if (target.TryGetValue(source, out var value))
        {
            target[alias] = value;
        }
    }

    private static Brush NewBrush(string colorText, string fallback)
    {
        foreach (var candidate in new[] { colorText, fallback, "#FFFFFF" })
        {
            try
            {
                var normalized = NormalizeColorText(candidate);
                if (ColorConverter.ConvertFromString(normalized) is Color color)
                {
                    return new SolidColorBrush(color);
                }
            }
            catch
            {
            }
        }

        return Brushes.White;
    }

    private Brush CreateGraphFill(LayerRow layer)
    {
        var front = NewBrush(layer.FrontColor, "#FFFFFF");
        if (!string.Equals(layer.UseGradient, "True", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(layer.GradientColor))
        {
            return front;
        }

        var frontColor = ((SolidColorBrush)front).Color;
        var gradientColor = ((SolidColorBrush)NewBrush(layer.GradientColor, layer.FrontColor)).Color;
        return new LinearGradientBrush(frontColor, gradientColor, new Point(0, 0.5), new Point(1, 0.5));
    }

    private static bool IsBoldFont(LayerRow layer)
    {
        return string.Equals(layer.Bold, "True", StringComparison.OrdinalIgnoreCase) ||
               (layer.Font ?? "").Contains("Bold", StringComparison.OrdinalIgnoreCase);
    }

    private static Size MeasureGdiText(string text, string fontName, double templateFontSize, bool bold, double templateInterval)
    {
        var pixelSize = MeasureGdiTextPixels(text, fontName, templateFontSize, bold, templateInterval);
        return new Size(
            Math.Max(1.0, pixelSize.Width / TextPreviewSupersample),
            Math.Max(1.0, pixelSize.Height / TextPreviewSupersample));
    }

    private sealed record TextLayerRenderResult(BitmapSource Source, Rect Bounds);

    private TextLayerRenderResult GetGdiTextLayerRender(LayerRow layer, string text)
    {
        if (!double.TryParse(layer.X, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) x = 0;
        if (!double.TryParse(layer.Y, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) y = 0;
        if (!double.TryParse(layer.Size, NumberStyles.Float, CultureInfo.InvariantCulture, out var size)) size = 20;

        var fontName = GetEffectiveLayerFont(layer.Font);
        var bold = IsBoldFont(layer);
        var color = NormalizeColorText(layer.Color);
        var alignmentIndex = GetTextAlignmentIndex(layer);
        var interval = GetTextInterval(layer);
        var cacheKey = string.Join("|layer", text, x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture),
            size.ToString(CultureInfo.InvariantCulture), fontName, bold, color, alignmentIndex, interval.ToString(CultureInfo.InvariantCulture));

        if (_gdiTextLayerCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        if (_gdiTextLayerCache.Count > 150)
        {
            _gdiTextLayerCache.Clear();
            _gdiTextCache.Clear();
            _gdiTextInkCache.Clear();
        }

        const int templatePixels = (int)TemplateCanvasSize;
        using var bitmap = new System.Drawing.Bitmap(templatePixels, templatePixels, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        bitmap.SetResolution(96f, 96f);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.Transparent);
            ConfigureGdiTextGraphics(graphics);
            using var font = CreateGdiFont(fontName, size, bold, 1.0);
            using var brush = new System.Drawing.SolidBrush(ToDrawingColor(color));
            using var format = CreateGdiStringFormat(alignmentIndex);
            if (Math.Abs(interval) > double.Epsilon)
            {
                DrawGdiIntervalTextAtTemplatePoint(graphics, text, font, brush, (float)x, (float)y, interval, alignmentIndex);
            }
            else
            {
                graphics.DrawString(text, font, brush, new System.Drawing.PointF((float)x, (float)y), format);
            }
        }

        var ink = FindBitmapInkBounds(bitmap);
        if (ink.Width <= 0 || ink.Height <= 0)
        {
            var empty = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Pbgra32, null, new byte[] { 0, 0, 0, 0 }, 4);
            empty.Freeze();
            var result = new TextLayerRenderResult(empty, new Rect(ToPreview(x), ToPreview(y), 1, 1));
            if (!_isDraggingPreview && !_isResizingPreview)
            {
                _gdiTextLayerCache[cacheKey] = result;
            }
            return result;
        }

        using var crop = bitmap.Clone(ink, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var source = ToBitmapSource(crop);
        var bounds = new Rect(
            ink.Left * PreviewScale,
            ink.Top * PreviewScale,
            Math.Max(1.0, ink.Width * PreviewScale),
            Math.Max(1.0, ink.Height * PreviewScale));

        var render = new TextLayerRenderResult(source, bounds);
        if (!_isDraggingPreview && !_isResizingPreview)
        {
            _gdiTextLayerCache[cacheKey] = render;
        }
        return render;
    }

    private static System.Drawing.Rectangle FindBitmapInkBounds(System.Drawing.Bitmap bitmap)
    {
        var minX = bitmap.Width;
        var minY = bitmap.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A == 0)
                {
                    continue;
                }

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        return maxX < minX || maxY < minY
            ? System.Drawing.Rectangle.Empty
            : System.Drawing.Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static Size MeasureGdiTextPixels(string text, string fontName, double templateFontSize, bool bold, double templateInterval = 0.0)
    {
        var measured = Math.Abs(templateInterval) > double.Epsilon
            ? MeasureGdiTextIntervalContentPixels(text, fontName, templateFontSize, bold, templateInterval)
            : MeasureGdiTextContentPixels(text, fontName, templateFontSize, bold);
        return new Size(
            Math.Max(1.0, Math.Ceiling(measured.Width) + GdiTextPadding * 2),
            Math.Max(1.0, Math.Ceiling(measured.Height) + GdiTextPadding * 2));
    }

    private static System.Drawing.SizeF MeasureGdiTextContentPixels(string text, string fontName, double templateFontSize, bool bold)
    {
        using var font = CreateGdiFont(fontName, templateFontSize, bold, TextPreviewRenderScale);
        using var bitmap = new System.Drawing.Bitmap(1, 1, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        ConfigureGdiTextGraphics(graphics);
        return graphics.MeasureString(text, font);
    }

    private static System.Drawing.SizeF MeasureGdiTextIntervalContentPixels(string text, string fontName, double templateFontSize, bool bold, double templateInterval)
    {
        using var font = CreateGdiFont(fontName, templateFontSize, bold, TextPreviewRenderScale);
        using var bitmap = new System.Drawing.Bitmap(1, 1, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        ConfigureGdiTextGraphics(graphics);

        var interval = (float)(templateInterval * TextPreviewRenderScale);
        var width = 0.0f;
        var height = 0.0f;
        foreach (var c in text)
        {
            var charSize = graphics.MeasureString(c.ToString(), font);
            width += charSize.Width + interval;
            height = Math.Max(height, charSize.Height);
        }

        if (text.Length > 0)
        {
            width -= interval;
        }

        return new System.Drawing.SizeF(Math.Max(1.0f, width), Math.Max(1.0f, height));
    }

    private static Rect MeasureGdiTextInkPixels(string text, string fontName, double templateFontSize, bool bold, int alignmentIndex, double templateInterval)
    {
        var size = MeasureGdiTextPixels(text, fontName, templateFontSize, bold, templateInterval);
        var width = Math.Max(1, (int)Math.Ceiling(size.Width));
        var height = Math.Max(1, (int)Math.Ceiling(size.Height));
        using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.Transparent);
            ConfigureGdiTextGraphics(graphics);
            using var font = CreateGdiFont(fontName, templateFontSize, bold, TextPreviewRenderScale);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            using var format = CreateGdiStringFormat(alignmentIndex);
            if (Math.Abs(templateInterval) > double.Epsilon)
            {
                DrawGdiIntervalText(graphics, text, font, brush, GdiTextPadding, GdiTextPadding, templateInterval);
            }
            else
            {
                var drawX = GdiTextPadding + (float)GetTextAlignmentOffset(size.Width - GdiTextPadding * 2, alignmentIndex);
                graphics.DrawString(text, font, brush, new System.Drawing.PointF(drawX, GdiTextPadding), format);
            }
        }

        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (bitmap.GetPixel(x, y).A == 0)
                {
                    continue;
                }

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return new Rect(0, 0, width, height);
        }

        return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static BitmapSource RenderGdiTextBitmap(string text, string fontName, double templateFontSize, bool bold, string colorText, int alignmentIndex, double templateInterval)
    {
        var size = MeasureGdiTextPixels(text, fontName, templateFontSize, bold, templateInterval);
        var width = Math.Max(1, (int)Math.Ceiling(size.Width));
        var height = Math.Max(1, (int)Math.Ceiling(size.Height));
        using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.Transparent);
            ConfigureGdiTextGraphics(graphics);
            using var font = CreateGdiFont(fontName, templateFontSize, bold, TextPreviewRenderScale);
            using var brush = new System.Drawing.SolidBrush(ToDrawingColor(colorText));
            using var format = CreateGdiStringFormat(alignmentIndex);
            if (Math.Abs(templateInterval) > double.Epsilon)
            {
                DrawGdiIntervalText(graphics, text, font, brush, GdiTextPadding, GdiTextPadding, templateInterval);
            }
            else
            {
                var drawX = GdiTextPadding + (float)GetTextAlignmentOffset(size.Width - GdiTextPadding * 2, alignmentIndex);
                graphics.DrawString(text, font, brush, new System.Drawing.PointF(drawX, GdiTextPadding), format);
            }
        }

        return ToBitmapSource(bitmap);
    }

    private static void DrawGdiIntervalText(System.Drawing.Graphics graphics, string text, System.Drawing.Font font, System.Drawing.Brush brush, float x, float y, double templateInterval)
    {
        var interval = (float)(templateInterval * TextPreviewRenderScale);
        var point = new System.Drawing.PointF(x, y);
        foreach (var c in text)
        {
            var glyph = c.ToString();
            var size = graphics.MeasureString(glyph, font);
            if (c == '.')
            {
                point.X -= (float)(size.Width * 0.1);
            }

            graphics.DrawString(glyph, font, brush, point);
            point.X += size.Width + interval;
        }
    }

    private static void DrawGdiIntervalTextAtTemplatePoint(System.Drawing.Graphics graphics, string text, System.Drawing.Font font, System.Drawing.Brush brush, float x, float y, double templateInterval, int alignmentIndex)
    {
        var interval = (float)templateInterval;
        if (alignmentIndex == 1)
        {
            var width = 0.0f;
            foreach (var c in text)
            {
                width += graphics.MeasureString(c.ToString(), font).Width + interval;
            }
            if (text.Length > 0)
            {
                width -= interval;
            }
            x -= width / 2.0f;
        }
        else if (alignmentIndex == 2)
        {
            for (var i = text.Length - 1; i >= 0; i--)
            {
                var glyph = text[i].ToString();
                var size = graphics.MeasureString(glyph, font);
                graphics.DrawString(glyph, font, brush, new System.Drawing.PointF(x, y));
                x -= size.Width + interval;
            }
            return;
        }

        var point = new System.Drawing.PointF(x, y);
        foreach (var c in text)
        {
            var glyph = c.ToString();
            var size = graphics.MeasureString(glyph, font);
            if (c == '.')
            {
                point.X -= (float)(size.Width * 0.1);
            }
            graphics.DrawString(glyph, font, brush, point);
            point.X += size.Width + interval;
        }
    }

    private static void ConfigureGdiTextGraphics(System.Drawing.Graphics graphics)
    {
        graphics.PageUnit = System.Drawing.GraphicsUnit.Pixel;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Default;
    }

    private static System.Drawing.StringFormat CreateGdiStringFormat(int alignmentIndex)
    {
        return new System.Drawing.StringFormat
        {
            Alignment = (System.Drawing.StringAlignment)Math.Clamp(alignmentIndex, 0, 2)
        };
    }

    private static System.Drawing.Font CreateGdiFont(string fontName, double templateFontSize, bool bold, double renderScale)
    {
        var family = ResolveGdiFontFamily(fontName);
        var style = bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular;
        if (!family.IsStyleAvailable(style))
        {
            style = family.IsStyleAvailable(System.Drawing.FontStyle.Regular)
                ? System.Drawing.FontStyle.Regular
                : family.IsStyleAvailable(System.Drawing.FontStyle.Bold)
                    ? System.Drawing.FontStyle.Bold
                    : family.IsStyleAvailable(System.Drawing.FontStyle.Italic)
                        ? System.Drawing.FontStyle.Italic
                        : System.Drawing.FontStyle.Regular;
        }

        return new System.Drawing.Font(
            family,
            Math.Max(1.0f, (float)(templateFontSize * renderScale)),
            style,
            System.Drawing.GraphicsUnit.Point);
    }

    private static System.Drawing.FontFamily ResolveGdiFontFamily(string fontName)
    {
        var effective = GetEffectiveLayerFont(fontName);
        if (_gdiFontMap.TryGetValue(effective, out var mapped))
        {
            return mapped;
        }

        var normalized = NormalizeFontLookupKey(effective);
        if (_gdiFontMap.TryGetValue(normalized, out mapped))
        {
            return mapped;
        }

        try
        {
            var family = new System.Drawing.FontFamily(effective);
            _gdiFontMap[effective] = family;
            _gdiFontMap[normalized] = family;
            return family;
        }
        catch
        {
            var fallback = new System.Drawing.FontFamily("Arial");
            _gdiFontMap[effective] = fallback;
            return fallback;
        }
    }

    private static System.Drawing.Color ToDrawingColor(string colorText)
    {
        try
        {
            var normalized = NormalizeColorText(colorText);
            if (ColorConverter.ConvertFromString(normalized) is Color color)
            {
                return System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
            }
        }
        catch
        {
        }

        return System.Drawing.Color.White;
    }

    private static BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap)
    {
        var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(
            rect,
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            var source = BitmapSource.Create(
                bitmap.Width,
                bitmap.Height,
                bitmap.HorizontalResolution,
                bitmap.VerticalResolution,
                PixelFormats.Pbgra32,
                null,
                data.Scan0,
                data.Stride * bitmap.Height,
                data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private BitmapSource GetCachedPreviewImage(string imagePath)
    {
        if (_previewImageCache.TryGetValue(imagePath, out var cached))
        {
            return cached;
        }

        if (_previewImageCache.Count > 80)
        {
            _previewImageCache.Clear();
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        _previewImageCache[imagePath] = bitmap;
        return bitmap;
    }

    private FontFamily ResolveFontFamily(string fontName)
    {
        var key = fontName ?? "";
        if (_resolvedFontsCache.TryGetValue(key, out var cached)) return cached;

        var resolved = ResolveFontFamilyInternal(key);
        _resolvedFontsCache[key] = resolved;
        return resolved;
    }

    private static string GetEffectiveLayerFont(string fontName)
    {
        return string.IsNullOrWhiteSpace(fontName) ? DefaultLayerFontName : fontName;
    }

    private static string GetDefaultLayerFontName()
    {
        if (_customFontMap.ContainsKey(DefaultLayerFontName) || _systemFonts.Contains(DefaultLayerFontName))
        {
            return DefaultLayerFontName;
        }

        var geForce = _customFontMap.Keys.FirstOrDefault(font =>
            font.Contains("GeForce", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(geForce))
        {
            return geForce;
        }

        return _systemFonts.Contains("Agency FB") ? "Agency FB" : "Segoe UI";
    }

    private FontFamily ResolveFontFamilyInternal(string fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName))
        {
            fontName = GetDefaultLayerFontName();
        }

        if (_customFontMap.TryGetValue(fontName, out var custom))
        {
            return custom;
        }

        if (_systemFonts.Contains(fontName))
        {
            return new FontFamily(fontName);
        }

        if (_customFontMap.TryGetValue(NormalizeFontLookupKey(fontName), out var normalizedCustom))
        {
            return normalizedCustom;
        }

        return new FontFamily(fontName);
    }

    private static readonly Dictionary<string, FontFamily> _customFontMap = new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Drawing.Text.PrivateFontCollection _privateGdiFonts = new();
    private static readonly Dictionary<string, System.Drawing.FontFamily> _gdiFontMap = new(StringComparer.OrdinalIgnoreCase);

    private static void LoadThemeEngineFonts()
    {
        try
        {
            var dllPath = @"C:\Program Files\Lian-Li\L-Connect 3\lianli.ThemeEngine.dll";
            if (!File.Exists(dllPath))
            {
                return;
            }

            var assembly = System.Reflection.Assembly.LoadFrom(dllPath);
            var engineType = assembly.GetType("ThemeEngine.ThemeEngine", false);
            engineType?.GetMethod("Init", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                ?.Invoke(null, null);

            var cacheField = engineType?.GetField("FontFamilyCaches", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (cacheField?.GetValue(null) is not System.Collections.IDictionary cache)
            {
                return;
            }

            foreach (System.Collections.DictionaryEntry entry in cache)
            {
                if (entry.Key is string name && entry.Value is System.Drawing.FontFamily family)
                {
                    _gdiFontMap[name] = family;
                    _gdiFontMap[NormalizeFontLookupKey(name)] = family;
                }
            }
        }
        catch
        {
        }
    }

    private static void InitializeCustomFonts()
    {
        LoadThemeEngineFonts();

        var paths = new[]
        {
            @"C:\Program Files\Lian-Li\L-Connect 3\Assets\ga2v\fonts\",
            @"C:\Program Files\Lian-Li\L-Connect 3\Assets\tl-sensor\assets\"
        };

        foreach (var dir in paths)
        {
            if (!Directory.Exists(dir)) continue;

            try
            {
                var folderUri = new Uri(dir, UriKind.Absolute);
                var files = Directory.GetFiles(dir, "*.*")
                    .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || 
                                f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase));

                foreach (var file in files)
                {
                    try
                    {
                        RegisterPrivateGdiFont(file);
                        var fileName = Path.GetFileName(file);
                        var fileUri = new Uri(file, UriKind.Absolute);
                        
                        var glyph = new GlyphTypeface(fileUri);
                        if (glyph.Win32FamilyNames.Values.FirstOrDefault() is string familyName)
                        {
                            var wpfFontFamily = new FontFamily(folderUri, $"./{fileName}#{familyName}");

                            _customFontMap[familyName] = wpfFontFamily;
                            _customFontMap[NormalizeFontLookupKey(familyName)] = wpfFontFamily;

                            var baseName = Path.GetFileNameWithoutExtension(fileName);
                            _customFontMap[baseName] = wpfFontFamily;
                            _customFontMap[NormalizeFontLookupKey(baseName)] = wpfFontFamily;
                            
                            var dotIndex = baseName.IndexOf('.');
                            if (dotIndex > 0)
                            {
                                var cleanBase = baseName.Substring(0, dotIndex);
                                _customFontMap[cleanBase] = wpfFontFamily;
                                _customFontMap[NormalizeFontLookupKey(cleanBase)] = wpfFontFamily;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }
    }

    private static void RegisterPrivateGdiFont(string file)
    {
        try
        {
            var beforeCount = _privateGdiFonts.Families.Length;
            _privateGdiFonts.AddFontFile(file);
            foreach (var family in _privateGdiFonts.Families.Skip(beforeCount))
            {
                _gdiFontMap[family.Name] = family;
                _gdiFontMap[NormalizeFontLookupKey(family.Name)] = family;
            }

            var baseName = Path.GetFileNameWithoutExtension(file);
            var cleanBase = baseName;
            var dotIndex = cleanBase.IndexOf('.');
            if (dotIndex > 0)
            {
                cleanBase = cleanBase.Substring(0, dotIndex);
            }

            var latestFamily = _privateGdiFonts.Families.LastOrDefault();
            if (latestFamily != null)
            {
                _gdiFontMap[baseName] = latestFamily;
                _gdiFontMap[cleanBase] = latestFamily;
                _gdiFontMap[NormalizeFontLookupKey(baseName)] = latestFamily;
                _gdiFontMap[NormalizeFontLookupKey(cleanBase)] = latestFamily;
            }
        }
        catch
        {
        }
    }

    private static string NormalizeFontLookupKey(string fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName))
        {
            return "";
        }

        var key = Regex.Replace(fontName, @"\.[0-9a-f]{6,}$", "", RegexOptions.IgnoreCase);
        key = Regex.Replace(key, @"\b(regular|bold|italic|medium|light|heavy|demi|semibold|condensed|wide)\b", "", RegexOptions.IgnoreCase);
        key = Regex.Replace(key, @"[^a-z0-9]+", "", RegexOptions.IgnoreCase);
        return key.ToLowerInvariant();
    }

    private static (int Width, int Height, int X, int Y) GetImagePlacement(
        string imagePath, string requestedSize, string requestedX, string requestedY)
    {
        var maxDimension = int.TryParse(requestedSize, out var requested)
            ? Math.Clamp(requested, 10, 480)
            : 160;
        var width = maxDimension;
        var height = maxDimension;
        try
        {
            var decoder = BitmapDecoder.Create(
                new Uri(imagePath, UriKind.Absolute),
                BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            var sourceMax = Math.Max(frame.PixelWidth, frame.PixelHeight);
            if (sourceMax <= 0) sourceMax = maxDimension;
            var scale = Math.Min(1.0, (double)maxDimension / sourceMax);
            width = Math.Max(1, (int)Math.Round(frame.PixelWidth * scale));
            height = Math.Max(1, (int)Math.Round(frame.PixelHeight * scale));
        }
        catch
        {
        }

        var x = int.TryParse(requestedX, out var parsedX) ? parsedX : 0;
        var y = int.TryParse(requestedY, out var parsedY) ? parsedY : 0;
        x = Math.Clamp(x, 0, Math.Max(0, 480 - width));
        y = Math.Clamp(y, 0, Math.Max(0, 480 - height));
        return (width, height, x, y);
    }

    private static double GetImageFitZoom(string imagePath)
    {
        try
        {
            var decoder = BitmapDecoder.Create(
                new Uri(imagePath, UriKind.Absolute),
                BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            var sourceMax = Math.Max(frame.PixelWidth, frame.PixelHeight);
            return sourceMax > 480 ? 480.0 / sourceMax : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private static bool TryParseZoom(string? value, out double zoom)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out zoom))
        {
            return true;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out zoom);
    }

    private static string FormatZoom(double zoom)
    {
        return zoom.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private async void TemplateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || TemplateCombo.SelectedItem is not TemplateOption option || string.IsNullOrWhiteSpace(option.Path))
        {
            return;
        }

        UseActiveCheck.IsChecked = false;
        TemplateIdBox.Text = option.Id;
        _currentTemplatePath = option.Path;
        await LoadLayersAsync(true);
    }

    private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        RefreshTemplateList();
        DrawPreview();
        SaveShadowLinks();
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageCombo.SelectedItem is ComboBoxItem item && item.Tag is string lang)
        {
            ApplyLanguage(lang);
            if (!_isLoading) SaveShadowLinks();
        }
    }

    private void UiThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UiThemeCombo.SelectedItem is ComboBoxItem item && item.Tag is string theme)
        {
            ApplyUiTheme(theme);
            if (!_syncingThemeToggle)
            {
                _syncingThemeToggle = true;
                ThemeToggleButton.IsChecked = string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase);
                ThemeToggleButton.ToolTip = ThemeToggleButton.IsChecked == true
                    ? "Switch to dark theme"
                    : "Switch to light theme";
                _syncingThemeToggle = false;
            }
            if (!_isLoading) SaveShadowLinks();
        }
    }

    private void ThemeToggleButton_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingThemeToggle || !IsLoaded)
        {
            return;
        }

        var theme = ThemeToggleButton.IsChecked == true ? "light" : "dark";
        _syncingThemeToggle = true;
        foreach (var item in UiThemeCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), theme, StringComparison.OrdinalIgnoreCase))
            {
                UiThemeCombo.SelectedItem = item;
                break;
            }
        }
        _syncingThemeToggle = false;
        ApplyUiTheme(theme);
        ThemeToggleButton.ToolTip = theme == "light"
            ? "Switch to dark theme"
            : "Switch to light theme";
        if (!_isLoading)
        {
            SaveShadowLinks();
        }
    }

    private void BackgroundMedia_MediaEnded(object sender, RoutedEventArgs e)
    {
        BackgroundMedia.Position = TimeSpan.Zero;
        BackgroundMedia.Play();
    }

    private string GetSelectedDeviceModel()
    {
        if (DeviceCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
        {
            return tag;
        }
        return "hydroshift-ii-lcd-s";
    }

    private void SyncDeviceFromTemplatePath(string templatePath)
    {
        var model = templatePath.Contains("hydroshift-ii-lcd-c", StringComparison.OrdinalIgnoreCase)
            ? "hydroshift-ii-lcd-c"
            : templatePath.Contains("hydroshift-ii-lcd-s", StringComparison.OrdinalIgnoreCase)
                ? "hydroshift-ii-lcd-s"
                : "";
        if (string.IsNullOrWhiteSpace(model)) return;
        foreach (var candidate in DeviceCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(candidate.Tag as string, model, StringComparison.OrdinalIgnoreCase))
            {
                DeviceCombo.SelectedItem = candidate;
                break;
            }
        }
    }

    private void RefreshTemplateList()
    {
        var selectedPath = _currentTemplatePath;
        TemplateOptions.Clear();
        var templateRoot = GetTemplateRoot(GetSelectedDeviceModel());
        if (Directory.Exists(templateRoot))
        {
            foreach (var path in Directory.EnumerateFiles(templateRoot, "*.template").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                TemplateOptions.Add(new TemplateOption
                {
                    Id = Path.GetFileNameWithoutExtension(path),
                    Path = path
                });
            }
        }
        SelectTemplateCombo(selectedPath);
    }

    private void SelectTemplateCombo(string templatePath)
    {
        if (string.IsNullOrWhiteSpace(templatePath)) return;
        foreach (var option in TemplateOptions)
        {
            if (string.Equals(option.Path, templatePath, StringComparison.OrdinalIgnoreCase))
            {
                TemplateCombo.SelectedItem = option;
                return;
            }
        }
    }

    private static string GetTemplateRoot(string deviceModel)
    {
        return Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", deviceModel, "template");
    }

    private void CanvasZoomMinus_Click(object sender, RoutedEventArgs e)
    {
        SetCanvasZoom(_canvasZoom - 0.1);
    }

    private void CanvasZoomPlus_Click(object sender, RoutedEventArgs e)
    {
        SetCanvasZoom(_canvasZoom + 0.1);
    }

    private void SetCanvasZoom(double zoom)
    {
        _canvasZoom = Math.Clamp(Math.Round(zoom, 1), 1.0, 3.0);
        PreviewFrame.LayoutTransform = new ScaleTransform(_canvasZoom, _canvasZoom);
        CanvasZoomText.Text = $"{_canvasZoom * 100:0}%";
        if (CanvasZoomSlider.Value != _canvasZoom)
        {
            CanvasZoomSlider.Value = _canvasZoom;
        }
    }

    private void CanvasZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
        {
            return;
        }

        SetCanvasZoom(e.NewValue);
    }

    private void PreviewFrame_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        SetCanvasZoom(_canvasZoom + (e.Delta > 0 ? 0.1 : -0.1));
        e.Handled = true;
    }

    private ThemeExportSnapshot CreateThemeExportSnapshot(string deviceModel)
    {
        var animationMediaName = Layers
            .FirstOrDefault(layer => string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
            ?.Media ?? "";
        var resolvedBackground = ResolveBackgroundPath(_currentBackgroundPath, animationMediaName);
        var templateBackgroundName = Path.GetFileName(_currentBackgroundPath);
        if (string.IsNullOrWhiteSpace(templateBackgroundName))
        {
            templateBackgroundName = Path.GetFileName(animationMediaName);
        }

        var exportBackground = ResolveBackgroundVariant(resolvedBackground, Path.GetExtension(templateBackgroundName));
        var imagePaths = Layers
            .Where(layer => string.Equals(layer.Type, "GraphImage", StringComparison.OrdinalIgnoreCase))
            .Select(layer => ResolveLayerMediaPath(layer.Media))
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ThemeExportSnapshot
        {
            DeviceModel = deviceModel,
            TemplateId = _currentTemplateId,
            TemplatePath = _currentTemplatePath,
            BackgroundPath = exportBackground,
            BackgroundEntryName = templateBackgroundName,
            PreviewBackgroundPath = resolvedBackground,
            PreviewBackgroundEntryName = Path.GetFileName(animationMediaName),
            ReferencedBackgroundPaths = ResolveReferencedBackgroundPaths(
                _currentTemplatePath, deviceModel, resolvedBackground),
            ImagePaths = imagePaths,
            PreviewPng = RenderCurrentThemePreview(cleanEditorOverlay: true)
        };
    }

    private byte[] RenderCurrentThemePreview(bool cleanEditorOverlay = false)
    {
        object? selectedItem = null;
        try
        {
            if (cleanEditorOverlay)
            {
                selectedItem = LayerGrid.SelectedItem;
                LayerGrid.SelectedItem = null;
                DrawPreview();
            }

            PreviewSurface.UpdateLayout();
            var bitmap = new RenderTargetBitmap(480, 480, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(PreviewSurface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        finally
        {
            if (cleanEditorOverlay)
            {
                LayerGrid.SelectedItem = selectedItem;
                DrawPreview();
            }
        }
    }

    private static List<string> ResolveReferencedBackgroundPaths(
        string templatePath, string deviceModel, string resolvedBackground)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var bytes = File.ReadAllBytes(templatePath);
            foreach (var text in new[]
                     {
                         System.Text.Encoding.ASCII.GetString(bytes),
                         System.Text.Encoding.Unicode.GetString(bytes)
                     })
            {
                foreach (Match match in Regex.Matches(
                             text,
                             @"[A-Za-z0-9_\-.]+\.(?:mp4|gif|h264|png|jpg|jpeg)",
                             RegexOptions.IgnoreCase))
                {
                    names.Add(match.Value);
                }
            }
        }
        catch
        {
        }

        if (!string.IsNullOrWhiteSpace(resolvedBackground))
        {
            names.Add(Path.GetFileName(resolvedBackground));
        }

        foreach (var name in names.ToList())
        {
            var extension = Path.GetExtension(name);
            if (extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(Path.ChangeExtension(name, ".h264"));
            }
            else if (extension.Equals(".h264", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(Path.ChangeExtension(name, ".mp4"));
            }
        }

        var roots = new[]
        {
            Path.GetDirectoryName(resolvedBackground) ?? "",
            Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", deviceModel, "video"),
            Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", deviceModel, "temp"),
            Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3\uploaded", deviceModel, "template-background"),
            Path.Combine(@"C:\Program Files\Lian-Li\L-Connect 3\Assets", deviceModel, "video")
        };

        var paths = new List<string>();
        foreach (var name in names)
        {
            var path = roots
                .Where(Directory.Exists)
                .Select(root => Path.Combine(root, name))
                .FirstOrDefault(File.Exists);
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ExportThemePackage(string packagePath, ThemeExportSnapshot snapshot)
    {
        var manifest = new ThemePackageManifest
        {
            DeviceModel = snapshot.DeviceModel,
            TemplateId = snapshot.TemplateId
        };

        var fullPackagePath = Path.GetFullPath(packagePath);
        if (File.Exists(fullPackagePath)) File.Delete(fullPackagePath);
        using var archive = ZipFile.Open(fullPackagePath, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(snapshot.TemplatePath, manifest.TemplateFile, CompressionLevel.Optimal);

        var addedImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var imagePath in snapshot.ImagePaths)
        {
            var fileName = Path.GetFileName(imagePath);
            if (!addedImages.Add(fileName)) continue;
            var entryName = $"images/{fileName}";
            archive.CreateEntryFromFile(imagePath, entryName, CompressionLevel.Optimal);
            manifest.ImageFiles.Add(entryName);
        }

        var backgroundPath = snapshot.BackgroundPath;
        if (!string.IsNullOrWhiteSpace(backgroundPath) && File.Exists(backgroundPath))
        {
            manifest.BackgroundFile = $"background/{Path.GetFileName(backgroundPath)}";
            archive.CreateEntryFromFile(backgroundPath, manifest.BackgroundFile, CompressionLevel.Optimal);
        }

        var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using var writer = new StreamWriter(manifestEntry.Open(), System.Text.Encoding.UTF8);
        writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void ExportLConnectPackage(string packagePath, ThemeExportSnapshot snapshot)
    {
        var fullPackagePath = Path.GetFullPath(packagePath);
        if (File.Exists(fullPackagePath)) File.Delete(fullPackagePath);

        using var archive = ZipFile.Open(fullPackagePath, ZipArchiveMode.Create);
        var templateEntryName = Path.GetFileName(snapshot.TemplatePath);
        archive.CreateEntryFromFile(snapshot.TemplatePath, templateEntryName, CompressionLevel.Optimal);

        var addedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            templateEntryName
        };

        foreach (var mediaPath in snapshot.ReferencedBackgroundPaths.Where(File.Exists))
        {
            var mediaName = Path.GetFileName(mediaPath);
            if (addedEntries.Add(mediaName))
            {
                archive.CreateEntryFromFile(mediaPath, mediaName, CompressionLevel.Optimal);
            }
        }

        if (snapshot.PreviewPng.Length > 0)
        {
            WritePreviewEntry(archive, $"preview/template_{snapshot.TemplateId}.png", snapshot.PreviewPng, addedEntries);
            WritePreviewEntry(archive, $"template_{snapshot.TemplateId}.png", snapshot.PreviewPng, addedEntries);
        }

        if (string.IsNullOrWhiteSpace(snapshot.BackgroundPath) || !File.Exists(snapshot.BackgroundPath))
        {
            return;
        }

        var backgroundEntryName = Path.GetFileName(snapshot.BackgroundEntryName);
        if (string.IsNullOrWhiteSpace(backgroundEntryName))
        {
            backgroundEntryName = Path.GetFileName(snapshot.BackgroundPath);
        }

        if (addedEntries.Add(backgroundEntryName))
        {
            archive.CreateEntryFromFile(snapshot.BackgroundPath, backgroundEntryName, CompressionLevel.Optimal);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.PreviewBackgroundPath) &&
            File.Exists(snapshot.PreviewBackgroundPath) &&
            !string.IsNullOrWhiteSpace(snapshot.PreviewBackgroundEntryName) &&
            !string.Equals(backgroundEntryName, snapshot.PreviewBackgroundEntryName,
                StringComparison.OrdinalIgnoreCase))
        {
            if (addedEntries.Add(snapshot.PreviewBackgroundEntryName))
            {
                archive.CreateEntryFromFile(snapshot.PreviewBackgroundPath,
                    snapshot.PreviewBackgroundEntryName, CompressionLevel.Optimal);
            }
        }
    }

    private static void WritePreviewEntry(ZipArchive archive, string entryName, byte[] previewPng, HashSet<string> addedEntries)
    {
        if (!addedEntries.Add(entryName))
        {
            return;
        }

        var previewEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var previewStream = previewEntry.Open();
        previewStream.Write(previewPng);
    }

    private async Task<TemplateOption> ImportThemePackageAsync(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var manifestEntry = archive.GetEntry("manifest.json")
                            ?? throw new InvalidDataException("manifest.json was not found in the theme package.");
        ThemePackageManifest manifest;
        using (var reader = new StreamReader(manifestEntry.Open(), System.Text.Encoding.UTF8))
        {
            manifest = JsonSerializer.Deserialize<ThemePackageManifest>(await reader.ReadToEndAsync())
                       ?? throw new InvalidDataException("The theme package manifest is invalid.");
        }

        if (manifest.FormatVersion != 1)
        {
            throw new InvalidDataException($"Unsupported theme package version: {manifest.FormatVersion}");
        }

        var deviceModel = manifest.DeviceModel;
        if (deviceModel is not ("hydroshift-ii-lcd-s" or "hydroshift-ii-lcd-c"))
        {
            throw new InvalidDataException("The theme package contains an unsupported device model.");
        }

        var templateEntry = GetSafePackageEntry(archive, manifest.TemplateFile);
        var templateRoot = GetTemplateRoot(deviceModel);
        var imageRoot = Path.Combine(Path.GetDirectoryName(templateRoot)!, "image");
        Directory.CreateDirectory(templateRoot);
        Directory.CreateDirectory(imageRoot);

        var baseId = SanitizeFileName(manifest.TemplateId);
        if (string.IsNullOrWhiteSpace(baseId)) baseId = "ImportedTheme";
        var importedId = GetUniqueTemplateId(templateRoot, $"{baseId}-imported");
        var destinationTemplate = Path.Combine(templateRoot, $"{importedId}.template");
        ExtractPackageEntry(templateEntry, destinationTemplate);

        var importedImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var imageEntryName in manifest.ImageFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var imageEntry = GetSafePackageEntry(archive, imageEntryName);
            var originalName = Path.GetFileName(imageEntry.FullName);
            var importedName = $"{importedId}-{originalName}";
            ExtractPackageEntry(imageEntry, Path.Combine(imageRoot, importedName));
            importedImages[originalName] = importedName;
        }

        if (importedImages.Count > 0)
        {
            var importedTemplate = await _supporter.LoadTemplatePathAsync(deviceModel, destinationTemplate);
            foreach (var layer in importedTemplate.Layers.Where(layer =>
                         string.Equals(layer.Type, "GraphImage", StringComparison.OrdinalIgnoreCase) &&
                         importedImages.ContainsKey(Path.GetFileName(layer.Media))))
            {
                layer.Media = importedImages[Path.GetFileName(layer.Media)];
                await _supporter.ApplyLayerAsync(deviceModel, destinationTemplate, layer);
            }
        }

        string backgroundTemp = "";
        if (!string.IsNullOrWhiteSpace(manifest.BackgroundFile))
        {
            var backgroundEntry = GetSafePackageEntry(archive, manifest.BackgroundFile);
            var extension = Path.GetExtension(backgroundEntry.FullName);
            backgroundTemp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
            ExtractPackageEntry(backgroundEntry, backgroundTemp);
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(backgroundTemp))
            {
                await _supporter.SetBackgroundMediaAsync(deviceModel, destinationTemplate, backgroundTemp);
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(backgroundTemp))
            {
                File.Delete(backgroundTemp);
            }
        }

        foreach (var item in DeviceCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), deviceModel, StringComparison.OrdinalIgnoreCase))
            {
                DeviceCombo.SelectedItem = item;
                break;
            }
        }

        return new TemplateOption { Id = importedId, Path = destinationTemplate };
    }

    private static ZipArchiveEntry GetSafePackageEntry(ZipArchive archive, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) ||
            Path.IsPathRooted(entryName) ||
            entryName.Split('/', '\\').Any(part => part == ".."))
        {
            throw new InvalidDataException("The theme package contains an unsafe file path.");
        }
        return archive.GetEntry(entryName)
               ?? throw new InvalidDataException($"Package file is missing: {entryName}");
    }

    private static void ExtractPackageEntry(ZipArchiveEntry entry, string destinationPath)
    {
        var fullDestination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        entry.ExtractToFile(fullDestination, true);
    }

    private static string GetUniqueTemplateId(string templateRoot, string baseId)
    {
        var id = baseId;
        var suffix = 2;
        while (File.Exists(Path.Combine(templateRoot, $"{id}.template")))
        {
            id = $"{baseId}-{suffix++}";
        }
        return id;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string((value ?? "").Where(character => !invalid.Contains(character)).ToArray()).Trim();
    }

    private void LoadBackgroundPreview(string backgroundPath, string backgroundName)
    {
        BackgroundMedia.Stop();
        BackgroundMedia.Source = null;
        BackgroundMedia.Visibility = Visibility.Collapsed;
        BackgroundImage.Source = null;
        BackgroundImage.Visibility = Visibility.Collapsed;

        var resolved = ResolveBackgroundPath(backgroundPath, backgroundName);
        if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
        {
            return;
        }

        if (Path.GetExtension(resolved).Equals(".h264", StringComparison.OrdinalIgnoreCase))
        {
            resolved = ResolveBackgroundVariant(resolved, ".mp4");
        }

        var ext = Path.GetExtension(resolved).ToLowerInvariant();
        try
        {
            if (ext is ".mp4" or ".avi" or ".mov" or ".wmv" or ".h264")
            {
                BackgroundMedia.Source = new Uri(resolved, UriKind.Absolute);
                BackgroundMedia.Visibility = Visibility.Visible;
                BackgroundMedia.Position = TimeSpan.Zero;
                BackgroundMedia.Play();
                return;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(resolved, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            BackgroundImage.Source = bitmap;
            BackgroundImage.Visibility = Visibility.Visible;
        }
        catch
        {
            // Preview media is best-effort; template editing should keep working.
        }
    }

    private string ResolveBackgroundPath(string backgroundPath, string backgroundName)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(backgroundPath))
        {
            candidates.Add(backgroundPath);
            if (!Path.IsPathRooted(backgroundPath) && !string.IsNullOrWhiteSpace(_currentTemplatePath))
            {
                candidates.Add(Path.Combine(Path.GetDirectoryName(_currentTemplatePath) ?? "", backgroundPath));
            }
        }
        if (!string.IsNullOrWhiteSpace(backgroundName))
        {
            var lconnect = @"C:\ProgramData\Lian-Li\L-Connect 3";
            var model = GetSelectedDeviceModel();
            
            var baseName = Path.GetFileNameWithoutExtension(backgroundName);
            var templateDir = string.IsNullOrWhiteSpace(_currentTemplatePath)
                ? ""
                : Path.GetDirectoryName(_currentTemplatePath) ?? "";
            var searchPaths = new[]
            {
                templateDir,
                Path.Combine(lconnect, model, "video"),
                Path.Combine(lconnect, model, "theme"),
                Path.Combine(lconnect, model, "template"),
                Path.Combine(lconnect, model, "temp"),
                Path.Combine(lconnect, "uploaded", model, "template-background"),
                Path.Combine(@"C:\Program Files\Lian-Li\L-Connect 3\Assets", model, "video"),
                Path.Combine(@"C:\Program Files\Lian-Li\L-Connect 3\Assets", model, "theme")
            }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            foreach (var dir in searchPaths)
            {
                if (Directory.Exists(dir))
                {
                    try
                    {
                        var matchingFiles = Directory.GetFiles(dir, baseName + "*.*")
                            .Where(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".h264", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                            .OrderBy(f => Path.GetExtension(f).Equals(".h264", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                            .ThenByDescending(f => new FileInfo(f).Length)
                            .ToList();
                        
                        candidates.AddRange(matchingFiles);
                    }
                    catch {}
                }
            }

            candidates.Add(Path.Combine(lconnect, model, "video", backgroundName));
            candidates.Add(Path.Combine(lconnect, model, "theme", backgroundName));
            candidates.Add(Path.Combine(lconnect, model, "temp", backgroundName));
            candidates.Add(Path.Combine(lconnect, model, "template", backgroundName));
            candidates.Add(Path.Combine(lconnect, "uploaded", model, "template-background", backgroundName));
            candidates.Add(Path.Combine(lconnect, "uploaded", backgroundName));
            if (!string.IsNullOrWhiteSpace(_currentTemplatePath))
            {
                candidates.Add(Path.Combine(Path.GetDirectoryName(_currentTemplatePath) ?? "", backgroundName));
            }
        }

        // First pass: try to find a non-placeholder file
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                bool isPlaceholder = candidate.Contains("uploaded", StringComparison.OrdinalIgnoreCase) &&
                                     new FileInfo(candidate).Length < 50000;
                if (!isPlaceholder)
                {
                    return candidate;
                }
            }

            var baseNoExt = Path.Combine(Path.GetDirectoryName(candidate) ?? "", Path.GetFileNameWithoutExtension(candidate));
            foreach (var ext in new[] { ".mp4", ".h264", ".gif", ".png", ".jpg", ".jpeg" })
            {
                var alternate = baseNoExt + ext;
                if (File.Exists(alternate))
                {
                    bool isPlaceholder = alternate.Contains("uploaded", StringComparison.OrdinalIgnoreCase) &&
                                         new FileInfo(alternate).Length < 50000;
                    if (!isPlaceholder)
                    {
                        return alternate;
                    }
                }
            }
        }

        // Second pass: accept placeholders if nothing else exists
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }

            var baseNoExt = Path.Combine(Path.GetDirectoryName(candidate) ?? "", Path.GetFileNameWithoutExtension(candidate));
            foreach (var ext in new[] { ".mp4", ".h264", ".gif", ".png", ".jpg", ".jpeg" })
            {
                var alternate = baseNoExt + ext;
                if (File.Exists(alternate)) return alternate;
            }
        }

        return "";
    }

    private static string ResolveBackgroundVariant(string backgroundPath, string requestedExtension)
    {
        if (string.IsNullOrWhiteSpace(backgroundPath) || !File.Exists(backgroundPath) ||
            string.IsNullOrWhiteSpace(requestedExtension))
        {
            return backgroundPath;
        }

        var variant = Path.ChangeExtension(backgroundPath, requestedExtension);
        return File.Exists(variant) ? variant : backgroundPath;
    }

    private void ApplyLanguage(string lang)
    {
        var text = LoadLanguage(lang);
        _languageText = text;
        Title = GetText(text, "app.title", "Lian Li LCD Template Editor V 1.0 Beta");
        DeviceLabel.Text = GetText(text, "top.device", "DEVICE");
        TemplateLabel.Text = GetText(text, "top.templateId", "TEMPLATE");
        LanguageLabel.Text = GetText(text, "footer.language", "Language");
        ThemeLabel.Text = GetText(text, "footer.theme", "Theme");
        DarkThemeItem.Content = GetText(text, "footer.dark", "Dark");
        LightThemeItem.Content = GetText(text, "footer.light", "Light");
        UseActiveCheck.Content = GetText(text, "top.useActiveTemplate", "Use active template");
        LoadButton.Content = GetText(text, "top.load", "Load");
        SaveButton.Content = GetText(text, "top.save", "Save");
        UndoButton.Content = GetText(text, "common.undo", "Undo");
        RedoButton.Content = GetText(text, "common.redo", "Redo");
        ExportLConnectButtonText.Text = GetText(text, "top.exportTheme", "Export Theme");
        LayersHeaderText.Text = GetText(text, "sections.layers", "Layers");
        EditLayerHeaderText.Text = GetText(text, "sections.editLayer", "Edit Layer");
        AddLayerHeaderText.Text = GetText(text, "sections.addNewLayer", "Add New Layer");
        ShadowHeaderText.Text = GetText(text, "sections.dropShadow", "Drop Shadow");
        AddTypeLabel.Content = GetText(text, "add.layerType", "LAYER TYPE");
        if (AddLayerTypeCombo.Items.Count >= 4)
        {
            ((ComboBoxItem)AddLayerTypeCombo.Items[0]).Content = GetText(text, "add.typeText", "Text");
            ((ComboBoxItem)AddLayerTypeCombo.Items[1]).Content = GetText(text, "add.typeData", "Data");
            ((ComboBoxItem)AddLayerTypeCombo.Items[2]).Content = GetText(text, "add.typeImage", "Image");
            ((ComboBoxItem)AddLayerTypeCombo.Items[3]).Content = GetText(text, "add.typeGraph", "Graph");
        }
        AddWithShadowCheck.Content = GetText(text, "add.withShadow", "Add shadow");
        ShadowAutoAddHint.Text = GetText(text, "add.shadowAutoHint", "Shadow will be added with the layer.");
        GraphSettingsTitle.Text = GetText(text, "sections.graphSettings", "Graph Dimensions & Styling");
        ImageSettingsTitle.Text = GetText(text, "sections.imageSettings", "Image Settings");

        IndexLabel.Content = GetText(text, "labels.index", "INDEX");
        FontLabel.Content = GetText(text, "labels.font", "FONT");
        DataLabel.Content = GetText(text, "labels.data", "DATA");
        FontIntervalLabel.Content = GetText(text, "labels.charSpacing", "CHAR SPACING");
        SizeLabel.Content = "W";
        SizeHeightLabel.Content = "H";
        ColorLabel.Content = GetText(text, "labels.color", "COLOR");
        FormatLabel.Content = GetText(text, "labels.format", "FORMAT");
        GraphStyleLabel.Content = GetText(text, "labels.graph", "GRAPH");
        WidthLabel.Content = GetText(text, "labels.width", "WIDTH");
        HeightLabel.Content = GetText(text, "labels.height", "HEIGHT");
        RadiusLabel.Content = GetText(text, "labels.radius", "RADIUS");
        DiameterLabel.Content = GetText(text, "labels.diameter", "DIAMETER");
        ThicknessLabel.Content = GetText(text, "labels.thickness", "THICKNESS");
        FrontColorLabel.Content = GetText(text, "labels.fillColor", "FILL COLOR");
        BackColorLabel.Content = GetText(text, "labels.trackColor", "TRACK COLOR");
        GradientColorLabel.Content = GetText(text, "labels.gradientColor", "GRADIENT COLOR");
        UseGradientCheck.Content = GetText(text, "labels.useGradient", "Use gradient");
        GraphAdvancedHeaderText.Text = GetText(text, "sections.advancedGraph", "Advanced Graph");
        GraphDirectionLabel.Content = GetText(text, "labels.direction", "DIRECTION");
        GraphLineWidthLabel.Content = GetText(text, "labels.lineWidth", "LINE WIDTH");
        GraphColumnWidthLabel.Content = GetText(text, "labels.columnWidth", "COLUMN WIDTH");
        GraphBorderWidthLabel.Content = GetText(text, "labels.borderWidth", "BORDER WIDTH");
        GraphInnerCircleRadiusLabel.Content = GetText(text, "labels.innerRadius", "INNER RADIUS");
        GraphSplitLabel.Content = GetText(text, "labels.segmentsGap", "SEGMENTS / GAP");
        GraphUseSubsectionCheck.Content = GetText(text, "labels.subsection", "Subsection");
        GraphFillBackCheck.Content = GetText(text, "labels.fillBackground", "Fill background");
        GraphRevertCheck.Content = GetText(text, "labels.revert", "Revert");
        ImageFileLabel.Content = GetText(text, "labels.imageFile", "IMAGE FILE");
        ImageRotateLabel.Content = GetText(text, "labels.rotate", "ROTATE");
        ImageRectLabel.Content = GetText(text, "labels.rect", "RECT");
        ZoomRateLabel.Content = GetText(text, "labels.zoomRate", "ZOOM RATE");

        AddTextLabel.Content = GetText(text, "labels.text", "TEXT");
        AddDataLabel.Content = GetText(text, "labels.data", "DATA");
        AddSizeLabel.Content = GetText(text, "labels.size", "SIZE");
        AddColorLabel.Content = GetText(text, "labels.color", "COLOR");
        AddFontLabel.Content = GetText(text, "labels.font", "FONT");
        AddFormatLabel.Content = GetText(text, "labels.format", "FORMAT");
        AddGraphLabel.Content = GetText(text, "labels.graph", "GRAPH");
        ShadowXLabel.Content = GetText(text, "labels.offsetX", "OFFSET X");
        ShadowYLabel.Content = GetText(text, "labels.offsetY", "OFFSET Y");
        ShadowColorLabel.Content = GetText(text, "labels.color", "COLOR");
        BoldCheck.Content = GetText(text, "common.bold", "Bold");
        ItalicCheck.Content = GetText(text, "common.italic", "Italic");
        AddBoldCheck.Content = GetText(text, "common.bold", "Bold");
        SetTextCheck.Content = GetText(text, "edit.text", "Text");
        PairCheck.Content = GetText(text, "edit.applyToShadow", "Apply to shadow");
        SyncShadowColorCheck.Content = GetText(text, "edit.syncShadow", "Sync shadow");
        AddTextButton.Content = GetText(text, "add.addText", "Add Text");
        AddDataButton.Content = GetText(text, "add.addData", "Add Data");
        AddImageButton.Content = GetText(text, "add.chooseAddImage", "Choose & Add Image");
        AddGraphButton.Content = GetText(text, "add.addGraph", "Add Graph");
        ApplyButton.Content = GetText(text, "common.apply", "Apply");
        RemoveButton.Content = GetText(text, "common.remove", "Remove");
        MoveUpButton.Content = GetText(text, "common.moveUp", "Move Up");
        MoveDownButton.Content = GetText(text, "common.moveDown", "Move Down");
        BackgroundButton.Content = GetText(text, "preview.uploadBackground", "Upload Background (GIF / JPG / MP4)");
        RestartButtonText.Text = GetText(text, "top.restartLConnect", "Restart L-Connect");
        ApplyAllButton.Content = GetText(text, "common.applyAll", "Apply All");
        ShadowTitleText.Text = GetText(text, "shadow.options", "Shadow options");
        PairCheck.Content = GetText(text, "shadow.pair", "Pair shadow");
        SyncShadowColorCheck.Content = GetText(text, "shadow.syncColor", "Sync color");
        ChangeImageButton.Content = GetText(text, "common.change", "Change...");
        DragHintText.Text = GetText(text, "preview.dragToReposition", "Drag to reposition");

        var headers = new[]
        {
            "#",
            GetText(text, "grid.type", "TYPE"),
            GetText(text, "grid.data", "DATA"),
            GetText(text, "grid.text", "TEXT"),
            GetText(text, "grid.media", "MEDIA"),
            GetText(text, "grid.description", "DESCRIPTION"),
            "X", "Y",
            GetText(text, "grid.size", "SIZE"),
            GetText(text, "grid.font", "FONT"),
            GetText(text, "grid.bold", "BOLD"),
            GetText(text, "grid.color", "COLOR"),
            GetText(text, "grid.format", "FORMAT"),
            GetText(text, "labels.graph", "GRAPH")
        };
        for (var index = 0; index < headers.Length && index < LayerGrid.Columns.Count; index++)
        {
            LayerGrid.Columns[index].Header = headers[index];
        }

        LayerCountText.Text = string.Format(
            GetText(text, "layers.count", "{0} layers"),
            Layers.Count);
        if (!_isLoading && LayerGrid.SelectedItem is LayerRow)
        {
            PopulateEditorFromSelection();
        }
        FitLocalizedButtons();
    }

    private void FitLocalizedButtons()
    {
        foreach (var button in new[]
                 {
                     LoadButton, UndoButton, RedoButton, ExportLConnectButton,
                     MoveUpButton, MoveDownButton, ApplyButton, RemoveButton,
                     AddTextButton, AddDataButton, AddImageButton, AddGraphButton,
                     BackgroundButton, RestartButton, ApplyAllButton,
                     ChangeImageButton
                 })
        {
            var value = button.Content?.ToString() ?? "";
            button.ToolTip = value;
            button.FontSize = value.Length switch
            {
                > 24 => 9,
                > 17 => 10,
                _ => 11
            };
        }
    }

    private Dictionary<string, string> LoadLanguage(string lang)
    {
        try
        {
            var path = Path.Combine(_supporter.WorkingDirectory, "lang", $"{lang}.json");
            if (!File.Exists(path)) return new Dictionary<string, string>();
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            FlattenLanguage(document.RootElement, "", result);
            return result;
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static void FlattenLanguage(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        foreach (var property in element.EnumerateObject())
        {
            var key = string.IsNullOrWhiteSpace(prefix) ? property.Name : $"{prefix}.{property.Name}";
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                result[key] = property.Value.GetString() ?? "";
            }
            else if (property.Value.ValueKind == JsonValueKind.Object)
            {
                FlattenLanguage(property.Value, key, result);
            }
        }
    }

    private static string GetText(Dictionary<string, string> text, string key, string fallback)
    {
        return text.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }

    private string GetLanguageText(string key, string fallback) => GetText(_languageText, key, fallback);

    private string FormatLanguageText(string key, string fallback, params object[] values)
    {
        var value = GetLanguageText(key, fallback);
        for (var index = 0; index < values.Length; index++)
        {
            value = value.Replace($"{{{index}}}", values[index]?.ToString() ?? "");
        }
        return value;
    }

    private void ApplyUiTheme(string theme)
    {
        if (theme == "light")
        {
            SetBrush("BrBg", "#FFF8F3E8");
            SetBrush("BrSurface", "#BAFFFFFF");
            SetBrush("BrSurface2", "#92FFFFFF");
            SetBrush("BrField", "#EFFFFFFF");
            SetBrush("BrBorder", "#9BC7B89E");
            SetBrush("BrBorderSoft", "#A08BB5A8");
            SetBrush("BrHover", "#A7E6D8C9");
            SetBrush("BrSelectedLayer", "#B5D7F1CE");
            SetBrush("BrGridHeader", "#C8F9FBF6");
            SetBrush("BrGridRow", "#96FFFFFF");
            SetBrush("BrGridAltRow", "#78F2F8F4");
            SetBrush("BrGridCellBorder", "#6BB8C8BC");
            SetBrush("BrDecor1", "#42FFBE86");
            SetBrush("BrDecor2", "#38A5E8CE");
            SetBrush("BrDecor3", "#34CBA7FF");
            SetBrush("BrDecorStroke", "#4AA2DCC8");
            SetBrush("BrTextPrimary", "#17291F");
            SetBrush("BrTextSecondary", "#3F564A");
            SetBrush("BrTextTertiary", "#6A7E72");
            SetLinearGradient("GlassPanelBrush", "#C9FFFFFF", "#82F5EEE0", "#B8FFFFFF");
            SetLinearGradient("GlassHeaderBrush", "#F0FFFFFF", "#B8F2E5D4", "#DDFFFFFF");
            SetLinearGradient("GlassToolbarBrush", "#E2FFFFFF", "#A4EEDFCC", "#CCFFFFFF");
            SetLinearGradient("GlassPopupBrush", "#F6FFFFFF", "#E8F7F0E6", "#F0FFFFFF");
            SetLinearGradient("GlassShimmerBrush", "#88FFFFFF", "#FCFFFFFF", "#98DCC9B1");
            ExportLConnectButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F6B5C"));
            RestartButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9D3448"));
            WindowRoot.Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new((Color)ColorConverter.ConvertFromString("#FFF9F3E8"), 0),
                    new((Color)ColorConverter.ConvertFromString("#FFE9F5EF"), 0.46),
                    new((Color)ColorConverter.ConvertFromString("#FFDCEAF8"), 1)
                },
                new Point(0, 0),
                new Point(1, 1));
            return;
        }

        SetBrush("BrBg", "#FF07152F");
        SetBrush("BrSurface", "#CC10264A");
        SetBrush("BrSurface2", "#C8172E59");
        SetBrush("BrField", "#D90B1B38");
        SetBrush("BrBorder", "#B3294A7A");
        SetBrush("BrBorderSoft", "#4977B7");
        SetBrush("BrHover", "#234A82");
        SetBrush("BrSelectedLayer", "#70246FF2");
        SetBrush("BrGridHeader", "#202630");
        SetBrush("BrGridRow", "#151A22");
        SetBrush("BrGridAltRow", "#181D26");
        SetBrush("BrGridCellBorder", "#252B35");
        SetBrush("BrDecor1", "#253E91FF");
        SetBrush("BrDecor2", "#214A53D8");
        SetBrush("BrDecor3", "#1826C6A5");
        SetBrush("BrDecorStroke", "#1E68A8FF");
        SetBrush("BrTextPrimary", "#F5F7FA");
        SetBrush("BrTextSecondary", "#C9D1D9");
        SetBrush("BrTextTertiary", "#8F9AA8");
        SetLinearGradient("GlassPanelBrush", "#550D2040", "#420A1A35", "#580D2040");
        SetLinearGradient("GlassHeaderBrush", "#E0060E20", "#C0040C1A", "#E0060E20");
        SetLinearGradient("GlassToolbarBrush", "#D00B1F3E", "#B0071528", "#D00B1F3E");
        SetLinearGradient("GlassPopupBrush", "#E0122D58", "#D50B2348", "#E2143563");
        SetLinearGradient("GlassShimmerBrush", "#005080B0", "#705488C8", "#005080B0");
        ExportLConnectButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5DE4D0"));
        RestartButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8998"));
        WindowRoot.Background = new ImageBrush(
            new BitmapImage(new Uri("pack://application:,,,/Assets/glass-background.png")))
        {
            Stretch = Stretch.UniformToFill
        };
    }

    private void SetBrush(string key, string color)
    {
        Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private void SetLinearGradient(string key, string start, string middle, string end)
    {
        Resources[key] = new LinearGradientBrush(
            new GradientStopCollection
            {
                new((Color)ColorConverter.ConvertFromString(start), 0),
                new((Color)ColorConverter.ConvertFromString(middle), 0.5),
                new((Color)ColorConverter.ConvertFromString(end), 1)
            },
            new Point(0, 0),
            new Point(1, 1));
    }

    private static string GetComboText(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item) return item.Tag?.ToString() ?? item.Content?.ToString() ?? "";
        if (combo.SelectedItem is GraphStyleOption graphStyle) return graphStyle.Label;
        return combo.SelectedItem?.ToString() ?? combo.Text ?? "";
    }

    private static string GetComboValue(ComboBox combo)
    {
        if (combo.SelectedItem is GraphStyleOption graphStyle) return graphStyle.Code;
        if (combo.SelectedItem is ComboBoxItem item) return item.Tag?.ToString() ?? item.Content?.ToString() ?? "";
        return combo.SelectedValue?.ToString() ?? combo.Text ?? "";
    }

    private static IReadOnlyList<GraphStyleOption> GetFallbackGraphStyles()
    {
        return new[]
        {
            new GraphStyleOption { Label = "Bar Chart 1", Code = "MOD::H2_Bar_chart_1.modular::GraphStatuBar", Source = "Fallback", GraphType = "GraphStatuBar", TypeName = "Bar" },
            new GraphStyleOption { Label = "Bar Chart 2", Code = "MOD::H2_Bar_chart_2.modular::GraphStatuBar", Source = "Fallback", GraphType = "GraphStatuBar", TypeName = "Bar" },
            new GraphStyleOption { Label = "Donut Bar 1", Code = "MOD::H2_Donut chart_1.modular::GraphArchBar", Source = "Fallback", GraphType = "GraphArchBar", TypeName = "Donut" },
            new GraphStyleOption { Label = "Donut Bar 2", Code = "MOD::H2_Donut chart_2.modular::GraphArchBar", Source = "Fallback", GraphType = "GraphArchBar", TypeName = "Donut" },
            new GraphStyleOption { Label = "Donut Bar 3", Code = "MOD::H2_Donut chart_3.modular::GraphArchBar", Source = "Fallback", GraphType = "GraphArchBar", TypeName = "Donut" },
            new GraphStyleOption { Label = "Stream Bar", Code = "MOD::H2_Stream Chart_1.modular::GraphLine", Source = "Fallback", GraphType = "GraphLine", TypeName = "Chart", SubTypeName = "Stream" }
        };
    }

    private static GraphStyleOption NormalizeGraphStyleLabel(GraphStyleOption style)
    {
        style.Label = GetGraphStyleDisplayName(style.Code, style.Label);
        return style;
    }

    private static string GetGraphStyleDisplayName(string code, string label)
    {
        var text = $"{code} {label}";
        if (Regex.IsMatch(text, "H2_Bar_chart_1", RegexOptions.IgnoreCase)) return "Bar Chart 1";
        if (Regex.IsMatch(text, "H2_Bar_chart_2", RegexOptions.IgnoreCase)) return "Bar Chart 2";
        if (Regex.IsMatch(text, "H2_Donut chart_1", RegexOptions.IgnoreCase)) return "Donut Bar 1";
        if (Regex.IsMatch(text, "H2_Donut chart_2", RegexOptions.IgnoreCase)) return "Donut Bar 2";
        if (Regex.IsMatch(text, "H2_Donut chart_3", RegexOptions.IgnoreCase)) return "Donut Bar 3";
        if (Regex.IsMatch(text, "H2_Stream Chart_1", RegexOptions.IgnoreCase)) return "Stream Bar";

        var clean = Regex.Replace(label ?? "", "^H2[_\\s-]*", "", RegexOptions.IgnoreCase)
            .Replace("_", " ");
        return string.IsNullOrWhiteSpace(clean) ? label ?? "" : clean;
    }

    private static string GetDataSourceDisplayName(string dataSource)
    {
        return (dataSource ?? "").ToUpperInvariant() switch
        {
            "APM" => "APM",
            "CASEFAN1" => "Case Fan 1",
            "CASEFAN2" => "Case Fan 2",
            "CASEFAN3" => "Case Fan 3",
            "CASEFAN4" => "Case Fan 4",
            "CASEFAN5" => "Case Fan 5",
            "CASEFAN6" => "Case Fan 6",
            "CASEFAN7" => "Case Fan 7",
            "CASEFAN8" => "Case Fan 8",
            "CPUCLOCK" => "CPU Clock MHz",
            "CPUCLOCK_G" => "CPU Clock GHz",
            "CPUFAN" => "CPU Fan",
            "CPULOAD" => "CPU Load",
            "CPUMODEL" => "CPU Model",
            "CPUPOWER" or "CPUPWR" => "CPU Power",
            "CPUTEMP" => "CPU Temperature",
            "CPUTEMP_F" => "CPU Temperature Fahrenheit",
            "CPUVOLTAGE" => "CPU Voltage",
            "DATE" => "Date",
            "DAY" => "Day",
            "DOWNDSPEED" => "Download Speed",
            "DRVLOAD" => "Drive Load",
            "FPS" or "FPS_AVG" => "FPS",
            "GPUCLOCK" => "GPU Clock MHz",
            "GPUCLOCK_G" => "GPU Clock GHz",
            "GPUFAN" => "GPU Fan",
            "GPULOAD" => "GPU Load",
            "GPUPOWER" or "GPUPWR" => "GPU Power",
            "GPUMODEL" => "GPU Model",
            "GPURAM" => "GPU Memory Used",
            "GPURAMLOAD" => "GPU Memory Load",
            "GPUVALIDRAM" => "GPU Memory Total",
            "GPUTEMP" => "GPU Temperature",
            "GPUTEMP_F" => "GPU Temperature Fahrenheit",
            "GPUVOLTAGE" => "GPU Voltage",
            "HDDTEMP" => "Drive Temperature",
            "HDDTEMP_F" => "Drive Temperature Fahrenheit",
            "HDDUSED" => "Drive Used",
            "PUMP" => "Pump",
            "RAM" => "RAM Used",
            "RAM_GB" => "RAM Used GB",
            "RAMLOAD" => "RAM Load",
            "RAMMODEL" => "RAM Model",
            "RAMTOTAL" => "RAM Total",
            "RAMTOTAL_GB" => "RAM Total GB",
            "RAMVALID" => "RAM Available",
            "STATICTEXT" => "Static Text",
            "TIME" => "Time",
            "UPSPEED" => "Upload Speed",
            "WATERPUMP" => "Water Pump",
            "VOLUME" => "Volume",
            "WEATHER" => "Weather",
            _ => dataSource ?? ""
        };
    }

    private static void SetComboText(ComboBox combo, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            combo.SelectedIndex = -1;
            combo.Text = "";
            return;
        }

        foreach (var item in combo.Items)
        {
            var itemValue = item switch
            {
                ComboBoxItem comboItem => comboItem.Tag?.ToString() ?? comboItem.Content?.ToString() ?? "",
                GraphStyleOption graphStyle => graphStyle.Code,
                _ => item?.ToString() ?? ""
            };
            var itemText = item switch
            {
                ComboBoxItem comboItem => comboItem.Content?.ToString() ?? "",
                GraphStyleOption graphStyle => graphStyle.Label,
                _ => item?.ToString() ?? ""
            };

            if (string.Equals(itemValue, value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(itemText, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                combo.Text = itemText;
                return;
            }
        }
        combo.SelectedIndex = -1;
        combo.Text = value;
    }

    private static void SetComboValue(ComboBox combo, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            combo.SelectedIndex = -1;
            combo.Text = "";
            return;
        }

        foreach (var item in combo.Items)
        {
            if (item is GraphStyleOption graphStyle &&
                (string.Equals(graphStyle.Code, value, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(graphStyle.Label, value, StringComparison.OrdinalIgnoreCase)))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.Text = value;
    }

    private void PopulateGraphTypeSelectors(LayerRow layer)
    {
        var graphType = layer.Type ?? "";
        var relatedStyles = GraphStyles
            .Where(style => string.Equals(style.GraphType, graphType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var typeNames = relatedStyles
            .Select(style => style.TypeName)
            .Concat(new[] { layer.TypeName, "Chart", "DynamicBar" })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var subTypeNames = relatedStyles
            .Select(style => style.SubTypeName)
            .Concat(new[] { layer.SubTypeName, "Stream" })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        GraphTypeNameBox.Items.Clear();
        foreach (var value in typeNames)
        {
            GraphTypeNameBox.Items.Add(value);
        }

        GraphSubTypeNameBox.Items.Clear();
        foreach (var value in subTypeNames)
        {
            GraphSubTypeNameBox.Items.Add(value);
        }
    }

    private void SetAlignmentCombo(string value)
    {
        var target = string.IsNullOrWhiteSpace(value) ? "0" : value;
        foreach (var item in AlignmentCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), target, StringComparison.OrdinalIgnoreCase))
            {
                AlignmentCombo.SelectedItem = item;
                return;
            }
        }

        AlignmentCombo.SelectedIndex = 0;
    }

    private static string NormalizeColorText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "#FFFFFF";
        if (value.StartsWith("#", StringComparison.Ordinal)) return value;
        var match = Regex.Match(value, @"A=(?<a>\d+),\s*R=(?<r>\d+),\s*G=(?<g>\d+),\s*B=(?<b>\d+)");
        if (match.Success)
        {
            var a = Math.Clamp(int.Parse(match.Groups["a"].Value), 0, 255);
            var r = Math.Clamp(int.Parse(match.Groups["r"].Value), 0, 255);
            var g = Math.Clamp(int.Parse(match.Groups["g"].Value), 0, 255);
            var b = Math.Clamp(int.Parse(match.Groups["b"].Value), 0, 255);
            return a == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{a:X2}{r:X2}{g:X2}{b:X2}";
        }
        return value;
    }

    private void Nudge(TextBox box, int delta)
    {
        if (!int.TryParse(box.Text, out var value)) value = 0;
        box.Text = (value + delta).ToString();
    }

    private void NumericSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingNumericSliders || sender is not Slider slider || slider.Tag is not string textBoxName)
        {
            return;
        }

        if (FindName(textBoxName) is not TextBox box)
        {
            return;
        }

        _syncingNumericSliders = true;
        box.Text = textBoxName == nameof(ZoomBox)
            ? FormatZoom(Math.Round(slider.Value, 2))
            : Math.Round(slider.Value).ToString();
        _syncingNumericSliders = false;
    }

    private void SyncSliderFromText(TextBox box, Slider slider)
    {
        if (_syncingNumericSliders)
        {
            return;
        }

        var parsed = box == ZoomBox
            ? TryParseZoom(box.Text, out var zoomValue) ? zoomValue : slider.Value
            : double.TryParse(box.Text, out var numericValue) ? numericValue : slider.Value;

        _syncingNumericSliders = true;
        slider.Value = Math.Clamp(parsed, slider.Minimum, slider.Maximum);
        _syncingNumericSliders = false;
    }

    private void SyncAllNumericSliders()
    {
        SyncSliderFromText(SizeBox, SizeSlider);
        SyncSliderFromText(WidthBox, WidthSlider);
        SyncSliderFromText(HeightBox, HeightSlider);
        SyncSliderFromText(DiameterBox, DiameterSlider);
        SyncSliderFromText(ThicknessBox, ThicknessSlider);
        SyncSliderFromText(RadiusBox, RadiusSlider);
        SyncSliderFromText(ZoomBox, ZoomSlider);
    }

    private void XMinus_Click(object sender, RoutedEventArgs e) => Nudge(XBox, -1);
    private void XPlus_Click(object sender, RoutedEventArgs e) => Nudge(XBox, 1);
    private void YMinus_Click(object sender, RoutedEventArgs e) => Nudge(YBox, -1);
    private void YPlus_Click(object sender, RoutedEventArgs e) => Nudge(YBox, 1);
    private void SizeMinus_Click(object sender, RoutedEventArgs e) => Nudge(SizeBox, -1);
    private void SizePlus_Click(object sender, RoutedEventArgs e) => Nudge(SizeBox, 1);
    private void AddXMinus_Click(object sender, RoutedEventArgs e) => Nudge(AddXBox, -1);
    private void AddXPlus_Click(object sender, RoutedEventArgs e) => Nudge(AddXBox, 1);
    private void AddYMinus_Click(object sender, RoutedEventArgs e) => Nudge(AddYBox, -1);
    private void AddYPlus_Click(object sender, RoutedEventArgs e) => Nudge(AddYBox, 1);
    private void AddSizeMinus_Click(object sender, RoutedEventArgs e) => Nudge(AddSizeBox, -1);
    private void AddSizePlus_Click(object sender, RoutedEventArgs e) => Nudge(AddSizeBox, 1);
    private void ShadowXMinus_Click(object sender, RoutedEventArgs e) => Nudge(ShadowXBox, -1);
    private void ShadowXPlus_Click(object sender, RoutedEventArgs e) => Nudge(ShadowXBox, 1);
    private void ShadowYMinus_Click(object sender, RoutedEventArgs e) => Nudge(ShadowYBox, -1);
    private void ShadowYPlus_Click(object sender, RoutedEventArgs e) => Nudge(ShadowYBox, 1);

    // New Graph & Zoom nudges
    private void WidthMinus_Click(object sender, RoutedEventArgs e) => Nudge(WidthBox, -1);
    private void WidthPlus_Click(object sender, RoutedEventArgs e) => Nudge(WidthBox, 1);
    private void HeightMinus_Click(object sender, RoutedEventArgs e) => Nudge(HeightBox, -1);
    private void HeightPlus_Click(object sender, RoutedEventArgs e) => Nudge(HeightBox, 1);
    private void ThickMinus_Click(object sender, RoutedEventArgs e) => Nudge(ThicknessBox, -1);
    private void ThickPlus_Click(object sender, RoutedEventArgs e) => Nudge(ThicknessBox, 1);
    private void RadiusMinus_Click(object sender, RoutedEventArgs e) => Nudge(RadiusBox, -1);
    private void RadiusPlus_Click(object sender, RoutedEventArgs e) => Nudge(RadiusBox, 1);
    private void DiameterMinus_Click(object sender, RoutedEventArgs e) => Nudge(DiameterBox, -1);
    private void DiameterPlus_Click(object sender, RoutedEventArgs e) => Nudge(DiameterBox, 1);

    private void ZoomMinus_Click(object sender, RoutedEventArgs e)
    {
        if (TryParseZoom(ZoomBox.Text, out var val))
        {
            ZoomBox.Text = FormatZoom(Math.Max(0.01, Math.Round(val - 0.05, 3)));
        }
        else
        {
            ZoomBox.Text = "1.00";
        }
    }

    private void ZoomPlus_Click(object sender, RoutedEventArgs e)
    {
        if (TryParseZoom(ZoomBox.Text, out var val))
        {
            ZoomBox.Text = FormatZoom(Math.Min(10.0, Math.Round(val + 0.05, 3)));
        }
        else
        {
            ZoomBox.Text = "1.00";
        }
    }

    // Color Pickers Click handlers
    private void ColorPickButton_Click(object sender, RoutedEventArgs e)
    {
        var newColor = ColorPickerDialog.ShowDialog(this, ColorBox.Text);
        if (newColor != null) ColorBox.Text = newColor;
    }

    private void ShadowColorPickButton_Click(object sender, RoutedEventArgs e)
    {
        var newColor = ColorPickerDialog.ShowDialog(this, ShadowColorBox.Text);
        if (newColor != null) ShadowColorBox.Text = newColor;
    }

    private void AddColorPickButton_Click(object sender, RoutedEventArgs e)
    {
        var newColor = ColorPickerDialog.ShowDialog(this, AddColorBox.Text);
        if (newColor != null) AddColorBox.Text = newColor;
    }

    private void FrontColorPickButton_Click(object sender, RoutedEventArgs e)
    {
        var newColor = ColorPickerDialog.ShowDialog(this, FrontColorBox.Text);
        if (newColor != null) FrontColorBox.Text = newColor;
    }

    private void BackColorPickButton_Click(object sender, RoutedEventArgs e)
    {
        var newColor = ColorPickerDialog.ShowDialog(this, BackColorBox.Text);
        if (newColor != null) BackColorBox.Text = newColor;
    }

    private void GradientColorPickButton_Click(object sender, RoutedEventArgs e)
    {
        var newColor = ColorPickerDialog.ShowDialog(this, GradientColorBox.Text);
        if (newColor != null) GradientColorBox.Text = newColor;
    }

    // Change Image handler
    private async void ChangeImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = GetLanguageText("dialogs.chooseImage", "Choose LCD image"),
            Filter = GetLanguageText("dialogs.imageFilter", "Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All files|*.*")
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var selectedFile = dialog.FileName;
            var deviceModel = GetSelectedDeviceModel();
            var targetDir = Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", deviceModel, "image");
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
            
            var fileName = Path.GetFileName(selectedFile);
            var destPath = Path.Combine(targetDir, fileName);
            
            if (!string.Equals(Path.GetFullPath(selectedFile), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
            {
                await Task.Run(() => File.Copy(selectedFile, destPath, true));
            }

            _previewImageCache.Clear();
            ImageFileBox.Text = fileName;
            var fitZoom = GetImageFitZoom(destPath);
            ZoomBox.Text = FormatZoom(fitZoom);
            if (LayerGrid.SelectedItem is LayerRow selectedLayer)
            {
                var placement = GetImagePlacement(destPath, "480", selectedLayer.X, selectedLayer.Y);
                selectedLayer.X = placement.X.ToString(CultureInfo.InvariantCulture);
                selectedLayer.Y = placement.Y.ToString(CultureInfo.InvariantCulture);
                XBox.Text = selectedLayer.X;
                YBox.Text = selectedLayer.Y;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, GetLanguageText("messages.changeImageFailed", "Change image failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Format presets loading & selection
    private void FormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || FormatCombo.SelectedItem == null) return;
        var format = GetComboText(FormatCombo);
        FormatBox.Text = format;
        if (LayerGrid.SelectedItem is LayerRow layer)
        {
            layer.Format = format;
            layer.PreviewValueEdited = false;
            if (!string.Equals(layer.DataSource, "StaticText", StringComparison.OrdinalIgnoreCase))
            {
                layer.Text = SampleValueFor(layer.DataSource, format);
                TextBox.Text = layer.Text;
            }
        }
    }

    private void DataCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        var data = GetComboText(DataCombo);
        if (LayerGrid.SelectedItem is LayerRow layer &&
            !string.Equals(layer.DataSource, data, StringComparison.OrdinalIgnoreCase))
        {
            _isLoading = true;
            layer.DataSource = data;
            layer.ForceText = false;
            layer.PreviewValueEdited = false;
            _previewSampleOverrides.Remove(data);
            SetTextCheck.IsChecked = string.Equals(data, "StaticText", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(data, "StaticText", StringComparison.OrdinalIgnoreCase))
            {
                var format = DefaultFormatForDataSource(data);
                layer.Format = format;
                FormatBox.Text = format;
                TextBox.Text = SampleValueFor(data, format);
            }
            else
            {
                layer.Format = "";
                FormatBox.Text = "";
            }
            _isLoading = false;
        }
        UpdateFormatComboItems(data, FormatCombo, FormatBox);
    }

    private void AddDataCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var data = GetComboText(AddDataCombo);
        UpdateFormatComboItems(data, AddFormatCombo, null);
        if (AddFormatPanel != null)
        {
            AddFormatPanel.Visibility = AddFormatCombo.Visibility;
        }
    }

    private void AddLayerTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var type = (AddLayerTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Text";
        var isText = type == "Text";
        var isData = type == "Data";
        var isImage = type == "Image";
        var isGraph = type == "Graph";

        AddTextPanel.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        AddTextButton.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;

        AddDataPanel.Visibility = isData || isGraph ? Visibility.Visible : Visibility.Collapsed;
        AddDataButton.Visibility = isData ? Visibility.Visible : Visibility.Collapsed;
        AddFormatPanel.Visibility = Visibility.Collapsed;
        if (!isData)
        {
            AddFormatCombo.Visibility = Visibility.Collapsed;
        }
        else
        {
            UpdateFormatComboItems(GetComboText(AddDataCombo), AddFormatCombo, null);
            AddFormatPanel.Visibility = AddFormatCombo.Visibility;
        }

        AddImageButton.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
        AddGraphPanel.Visibility = isGraph ? Visibility.Visible : Visibility.Collapsed;
        AddGraphButton.Visibility = isGraph ? Visibility.Visible : Visibility.Collapsed;

        AddFontPanel.Visibility = isText || isData ? Visibility.Visible : Visibility.Collapsed;
        AddBoldPanel.Visibility = isText || isData ? Visibility.Visible : Visibility.Collapsed;
        AddColorPanel.Visibility = isImage ? Visibility.Collapsed : Visibility.Visible;

        AddSizeBox.Text = isImage ? "160" : isGraph ? "80" : "40";
    }

    private void AddLayerMenuButton_Click(object sender, RoutedEventArgs e)
    {
        AddLayerMenuPopup.IsOpen = true;
    }

    private void AddLayerChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string type)
        {
            return;
        }

        foreach (var item in AddLayerTypeCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), type, StringComparison.OrdinalIgnoreCase))
            {
                AddLayerTypeCombo.SelectedItem = item;
                break;
            }
        }

        AddLayerMenuPopup.IsOpen = false;
        LayerGrid.SelectedItem = null;
        AddLayerExpander.Visibility = Visibility.Visible;
        AddLayerExpander.IsExpanded = true;
        AddLayerExpander.BringIntoView();
        PopulateEditorFromSelection();
        RequestPreviewDraw();
    }

    private void AddWithShadowCheck_Changed(object sender, RoutedEventArgs e)
    {
        var enabled = AddWithShadowCheck.IsChecked == true;
        AddShadowExpander.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        AddShadowExpander.IsExpanded = enabled;
    }

    private void UpdateFormatComboItems(string dataSource, ComboBox combo, TextBox? box)
    {
        combo.Items.Clear();
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            combo.Visibility = Visibility.Collapsed;
            return;
        }

        var source = dataSource.ToUpperInvariant();
        if (source == "TIME")
        {
            combo.Visibility = Visibility.Visible;
            foreach (var fmt in TimeFormats) AddFormatOption(combo, fmt.Label, fmt.Code);
            SelectFormatOption(combo, box?.Text);
        }
        else if (source == "DATE")
        {
            combo.Visibility = Visibility.Visible;
            foreach (var fmt in DateFormats) AddFormatOption(combo, fmt.Label, fmt.Code);
            SelectFormatOption(combo, box?.Text);
        }
        else if (source == "DAY")
        {
            combo.Visibility = Visibility.Visible;
            AddFormatOption(combo, "Weekday", "Day_en");
            AddFormatOption(combo, "Short Weekday", "ddd");
            SelectFormatOption(combo, box?.Text);
        }
        else if (source is "CPUPWR" or "CPUPOWER" or "GPUPWR" or "GPUPOWER")
        {
            combo.Visibility = Visibility.Visible;
            AddFormatOption(combo, "Integer", "0");
            if (box != null && !string.Equals(box.Text, "0", StringComparison.OrdinalIgnoreCase))
            {
                box.Text = "0";
            }
            combo.SelectedIndex = 0;
        }
        else
        {
            combo.Visibility = Visibility.Collapsed;
        }
    }

    private static void AddFormatOption(ComboBox combo, string label, string code)
    {
        combo.Items.Add(new ComboBoxItem { Content = label, Tag = code });
    }

    private static void SelectFormatOption(ComboBox combo, string? code)
    {
        var target = string.IsNullOrWhiteSpace(code) ? null : code;
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem comboItem &&
                string.Equals(comboItem.Tag?.ToString(), target, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = comboItem;
                return;
            }
        }

        combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
    }

    private static bool SupportsFormat(string dataSource)
    {
        var source = (dataSource ?? "").ToUpperInvariant();
        return source is "TIME" or "DATE" or "DAY" or "CPUPWR" or "CPUPOWER" or "GPUPWR" or "GPUPOWER";
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

    // Undo/Redo Engine
    private void PushUndoState()
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(Layers);
            _undoStack.Push(bytes);
            _redoStack.Clear();
        }
        catch { }
    }

    private async void UndoButton_Click(object sender, RoutedEventArgs e) => await UndoAsync();
    private async void RedoButton_Click(object sender, RoutedEventArgs e) => await RedoAsync();

    private async Task UndoAsync()
    {
        if (_undoStack.Count == 0) return;
        try
        {
            SetBusy(true, "Undoing change...");
            var currentBytes = JsonSerializer.SerializeToUtf8Bytes(Layers);
            _redoStack.Push(currentBytes);
            
            var previousBytes = _undoStack.Pop();
            var previousLayers = JsonSerializer.Deserialize<List<LayerRow>>(previousBytes);
            if (previousLayers != null)
            {
                Layers.Clear();
                foreach (var l in previousLayers)
                {
                    Layers.Add(l);
                }
                _dirtyLayers.Clear();
                foreach (var layer in Layers) _dirtyLayers.Add(layer);
                _editorUndoArmed = false;
                LayerGrid.Items.Refresh();
                DrawPreview();
            }
            SetBusy(false, "Undo applied. Click Apply to save to disk.");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Undo failed.");
            MessageBox.Show(this, ex.Message, "Undo failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        await Task.CompletedTask;
    }

    private async Task RedoAsync()
    {
        if (_redoStack.Count == 0) return;
        try
        {
            SetBusy(true, "Redoing change...");
            var currentBytes = JsonSerializer.SerializeToUtf8Bytes(Layers);
            _undoStack.Push(currentBytes);
            
            var nextBytes = _redoStack.Pop();
            var nextLayers = JsonSerializer.Deserialize<List<LayerRow>>(nextBytes);
            if (nextLayers != null)
            {
                Layers.Clear();
                foreach (var l in nextLayers)
                {
                    Layers.Add(l);
                }
                _dirtyLayers.Clear();
                foreach (var layer in Layers) _dirtyLayers.Add(layer);
                _editorUndoArmed = false;
                LayerGrid.Items.Refresh();
                DrawPreview();
            }
            SetBusy(false, "Redo applied. Click Apply to save to disk.");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Redo failed.");
            MessageBox.Show(this, ex.Message, "Redo failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        await Task.CompletedTask;
    }

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete &&
            (LayerGrid.IsKeyboardFocusWithin || PreviewCanvas.IsKeyboardFocusWithin) &&
            LayerGrid.SelectedItems.Count > 0)
        {
            e.Handled = true;
            await RemoveSelectedLayersAsync();
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.Z)
            {
                e.Handled = true;
                await UndoAsync();
            }
            else if (e.Key == Key.Y)
            {
                e.Handled = true;
                await RedoAsync();
            }
        }
    }

    // Shadow Pairing Logic
    private void LoadShadowLinks()
    {
        _shadowLinks.Clear();
        if (string.IsNullOrWhiteSpace(_currentTemplatePath)) return;
        try
        {
            var settingsPath = Path.Combine(_supporter.WorkingDirectory, "theme_editor_settings.json");
            if (!File.Exists(settingsPath)) return;
            var json = File.ReadAllText(settingsPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("shadowLinks", out var shadowLinksProp))
            {
                var normPath = _currentTemplatePath.ToLowerInvariant().Replace("/", "\\");
                if (shadowLinksProp.TryGetProperty(normPath, out var templateLinksProp))
                {
                    foreach (var prop in templateLinksProp.EnumerateObject())
                    {
                        if (int.TryParse(prop.Name, out var shadowIdx) && prop.Value.TryGetInt32(out var sourceIdx))
                        {
                            _shadowLinks[shadowIdx] = sourceIdx;
                        }
                    }
                }
            }
        }
        catch { }
    }

    private void SaveShadowLinks()
    {
        if (string.IsNullOrWhiteSpace(_currentTemplatePath)) return;
        try
        {
            var settingsPath = Path.Combine(_supporter.WorkingDirectory, "theme_editor_settings.json");
            Dictionary<string, Dictionary<string, int>> shadowLinksDict = new(StringComparer.OrdinalIgnoreCase);
            string lang = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "en";
            string theme = (UiThemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Dark";
            string deviceModel = GetSelectedDeviceModel();

            if (File.Exists(settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(settingsPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("shadowLinks", out var shadowLinksProp))
                    {
                        foreach (var tProp in shadowLinksProp.EnumerateObject())
                        {
                            var tDict = new Dictionary<string, int>();
                            foreach (var sProp in tProp.Value.EnumerateObject())
                            {
                                if (sProp.Value.TryGetInt32(out var val))
                                {
                                    tDict[sProp.Name] = val;
                                }
                            }
                            shadowLinksDict[tProp.Name] = tDict;
                        }
                    }
                }
                catch { }
            }

            var normPath = _currentTemplatePath.ToLowerInvariant().Replace("/", "\\");
            var currentLinks = new Dictionary<string, int>();
            foreach (var kvp in _shadowLinks)
            {
                currentLinks[kvp.Key.ToString()] = kvp.Value;
            }
            shadowLinksDict[normPath] = currentLinks;

            var outputObj = new
            {
                language = lang,
                theme = theme,
                deviceModel = deviceModel,
                shadowLinks = shadowLinksDict
            };

            var opt = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(outputObj, opt));
        }
        catch { }
    }

    private void ShiftShadowLinksForInsert(int insertIndex)
    {
        var shifted = new Dictionary<int, int>();
        foreach (var kvp in _shadowLinks)
        {
            var key = kvp.Key;
            var val = kvp.Value;
            
            if (key >= insertIndex) key++;
            if (val >= insertIndex) val++;
            
            shifted[key] = val;
        }
        _shadowLinks.Clear();
        foreach (var kvp in shifted)
        {
            _shadowLinks[kvp.Key] = kvp.Value;
        }
    }

    private void SwapShadowLinksForLayerMove(int idxA, int idxB)
    {
        var swapped = new Dictionary<int, int>();
        foreach (var kvp in _shadowLinks)
        {
            var key = kvp.Key;
            var val = kvp.Value;
            
            if (key == idxA) key = idxB;
            else if (key == idxB) key = idxA;
            
            if (val == idxA) val = idxB;
            else if (val == idxB) val = idxA;
            
            swapped[key] = val;
        }
        _shadowLinks.Clear();
        foreach (var kvp in swapped)
        {
            _shadowLinks[kvp.Key] = kvp.Value;
        }
    }

    private void RemoveShadowLinkForDeletedIndex(int deletedIndex)
    {
        _shadowLinks.Remove(deletedIndex);
        var keysToRemove = _shadowLinks.Where(kvp => kvp.Value == deletedIndex).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
        {
            _shadowLinks.Remove(key);
        }
        
        var shifted = new Dictionary<int, int>();
        foreach (var kvp in _shadowLinks)
        {
            var key = kvp.Key;
            var val = kvp.Value;
            
            if (key > deletedIndex) key--;
            if (val > deletedIndex) val--;
            
            shifted[key] = val;
        }
        _shadowLinks.Clear();
        foreach (var kvp in shifted)
        {
            _shadowLinks[kvp.Key] = kvp.Value;
        }
        SaveShadowLinks();
    }

    private LayerRow? FindPairedLayer(LayerRow layer)
    {
        if (!int.TryParse(layer.Index, out var idx)) return null;
        
        if (_shadowLinks.TryGetValue(idx, out var parentIdx))
        {
            return Layers.FirstOrDefault(l => l.Index == parentIdx.ToString());
        }
        
        foreach (var kvp in _shadowLinks)
        {
            if (kvp.Value == idx)
            {
                return Layers.FirstOrDefault(l => l.Index == kvp.Key.ToString());
            }
        }
        
        if (string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase)) return null;
        if (!int.TryParse(layer.X, out var lx) || !int.TryParse(layer.Y, out var ly) || !int.TryParse(layer.Size, out var lsize)) return null;
        
        foreach (var l in Layers)
        {
            if (l == layer || string.Equals(l.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(l.X, out var ox) || !int.TryParse(l.Y, out var oy) || !int.TryParse(l.Size, out var osize)) continue;
            
            if (Math.Abs(lx - ox) <= 24 && Math.Abs(ly - oy) <= 24 && Math.Abs(lsize - osize) <= 6)
            {
                bool sameData = !string.IsNullOrWhiteSpace(layer.DataSource) && layer.DataSource != "StaticText" && l.DataSource == layer.DataSource;
                bool sameText = layer.DataSource == "StaticText" && l.DataSource == "StaticText" && l.Text == layer.Text && !string.IsNullOrWhiteSpace(layer.Text);
                if (sameData || sameText) return l;
            }
        }
        
        return null;
    }

    private bool IsPairedWith(LayerRow parent, LayerRow shadow)
    {
        if (int.TryParse(parent.Index, out var pIdx) && int.TryParse(shadow.Index, out var sIdx))
        {
            if (_shadowLinks.TryGetValue(sIdx, out var val) && val == pIdx) return true;
        }
        return false;
    }

    private void SyncShadowProperties(LayerRow parent, LayerRow shadow)
    {
        shadow.Size = parent.Size;
        shadow.Font = parent.Font;
        shadow.Bold = parent.Bold;
        if (SyncShadowColorCheck.IsChecked == true)
        {
            shadow.Color = parent.Color;
        }
    }

    // USB device and L-Connect controller services refresh logic
    private static List<string> GetLConnectDevicePaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = client.PostAsync("http://127.0.0.1:11021/?action=SyncControllerList", new StringContent("{}")).Result;
            if (response.IsSuccessStatusCode)
            {
                var json = response.Content.ReadAsStringAsync().Result;
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name.Contains("vid_1cbe", StringComparison.OrdinalIgnoreCase) && 
                        prop.Name.Contains("pid_a034", StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add(prop.Name);
                    }
                }
            }
        }
        catch { }

        try
        {
            var logDir = @"C:\ProgramData\Lian-Li\L-Connect 3\logs";
            if (Directory.Exists(logDir))
            {
                var files = Directory.EnumerateFiles(logDir, "*.log")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Take(8);
                
                var regex = new Regex(@"usb\\vid_[^,\]\s""]+", RegexOptions.IgnoreCase);
                foreach (var file in files)
                {
                    try
                    {
                        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(stream);
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            var matches = regex.Matches(line);
                            foreach (Match match in matches)
                            {
                                var path = match.Value;
                                paths.Add(path);
                                paths.Add(path.Replace("\\", "\\\\"));
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        if (paths.Count == 0)
        {
            paths.Add(@"usb\\vid_1cbe&pid_a034\\0834ab040486c702w");
            paths.Add(@"usb\vid_1cbe&pid_a034\0834ab040486c702w");
        }

        return paths.ToList();
    }

    private async Task<bool> TriggerLConnectRefreshAsync()
    {
        var devicePaths = GetLConnectDevicePaths();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        var accepted = false;
        
        if (_backgroundDirty && !string.IsNullOrWhiteSpace(_currentTemplateId))
        {
            var bgPath = !string.IsNullOrWhiteSpace(_currentBackgroundPath) ? _currentBackgroundPath : "";
            var bodyObj = new { Id = _currentTemplateId, Path = bgPath };
            var jsonBody = JsonSerializer.Serialize(bodyObj);
            
            var backgroundAccepted = false;
            foreach (var path in devicePaths)
            {
                var encodedPath = Uri.EscapeDataString(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(path)));
                try
                {
                    var url = $"http://127.0.0.1:11021/?action=Device&devicePath={encodedPath}&type=ChangeTemplateBackground";
                    var response = await client.PostAsync(url, new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json"));
                    if (response.IsSuccessStatusCode)
                    {
                        backgroundAccepted = true;
                        accepted = true;
                    }
                }
                catch { }
            }
            if (backgroundAccepted) _backgroundDirty = false;
        }

        var templateIdJson = JsonSerializer.Serialize(_currentTemplateId);

        foreach (var path in devicePaths)
        {
            var encodedPath = Uri.EscapeDataString(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(path)));
            foreach (var type in new[] { "ApplyAll", "ApplyTemplate", "SetTemplate" })
            {
                var url = $"http://127.0.0.1:11021/?action=Device&devicePath={encodedPath}&type={Uri.EscapeDataString(type)}";
                var body = type == "ApplyAll" ? "{}" : templateIdJson;
                try
                {
                    var response = await client.PostAsync(url, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
                    if (response.IsSuccessStatusCode) accepted = true;
                }
                catch { }
            }
        }

        try
        {
            var response = await client.PostAsync("http://127.0.0.1:11021/?action=ApplyAll", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            if (response.IsSuccessStatusCode) accepted = true;
        }
        catch { }
        return accepted;
    }

    private void UpdateLayerFromInputs(LayerRow layer)
    {
        layer.X = XBox.Text;
        layer.Y = YBox.Text;
        var type = layer.Type ?? "";
        var isText = type.Equals("GraphItem", StringComparison.OrdinalIgnoreCase);
        var isGraph = type.Equals("GraphStatuBar", StringComparison.OrdinalIgnoreCase) ||
                      type.Equals("GraphArchBar", StringComparison.OrdinalIgnoreCase) ||
                      type.Equals("GraphDynamicBar", StringComparison.OrdinalIgnoreCase) ||
                      type.Equals("GraphLine", StringComparison.OrdinalIgnoreCase);

        if (isText)
        {
            if (layer.CanWriteFont("size")) layer.Size = SizeBox.Text;
            if (layer.CanWriteFont("color")) layer.Color = ColorBox.Text;
            if (layer.CanWriteFont("name")) layer.Font = GetComboText(FontCombo);
            if (layer.CanWriteFont("alignment.index")) layer.AlignmentIndex = (AlignmentCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? layer.AlignmentIndex;
            if (layer.CanWriteFont("interval")) layer.FontInterval = FontIntervalBox.Text;
            if (layer.CanWrite("LineHeight")) layer.LineHeight = LineHeightBox.Text;
            layer.DataSource = SetTextCheck.IsChecked == true ? "StaticText" : GetComboText(DataCombo);
            layer.Format = SupportsFormat(layer.DataSource) && string.IsNullOrWhiteSpace(FormatBox.Text)
                ? DefaultFormatForDataSource(layer.DataSource)
                : FormatBox.Text;
            if (layer.CanWriteFont("isBold")) layer.Bold = BoldCheck.IsChecked == true ? "True" : "False";
            if (layer.CanWriteFont("IsItalic")) layer.Italic = ItalicCheck.IsChecked == true ? "True" : "False";
            layer.ForceText = SetTextCheck.IsChecked == true;
            if (layer.DataSource == "StaticText" || SetTextCheck.IsChecked == true)
            {
                layer.Text = TextBox.Text;
            }
        }

        if (isGraph && GraphEditPanel.Visibility == Visibility.Visible)
        {
            layer.DataSource = GetComboText(DataCombo);
            layer.GraphStyle = GetComboValue(GraphStyleCombo);
            if (layer.CanWrite("width")) layer.Width = WidthBox.Text;
            if (layer.CanWrite("diameter")) layer.Diameter = DiameterBox.Text;
            if (layer.CanWrite("archWidth")) layer.Thickness = ThicknessBox.Text;
            if (layer.CanWrite("radius")) layer.Radius = RadiusBox.Text;
            if (layer.CanWrite("FrontColor") || layer.CanWrite("LineColor") || layer.CanWrite("FillColor")) layer.FrontColor = FrontColorBox.Text;
            if (layer.CanWrite("BackColor") || layer.CanWrite("BorderColor")) layer.BackColor = BackColorBox.Text;
            if (layer.CanWrite("GradientColor")) layer.GradientColor = GradientColorBox.Text;
            if (layer.CanWrite("useGradient")) layer.UseGradient = UseGradientCheck.IsChecked == true ? "True" : "False";
            if (layer.CanWrite("height")) layer.Height = HeightBox.Text;
            if (layer.CanWrite("direction")) layer.Direction = GraphDirectionBox.Text;
            if (layer.CanWrite("lineWidth")) layer.LineWidth = GraphLineWidthBox.Text;
            if (layer.CanWrite("columnWidth")) layer.ColumnWidth = GraphColumnWidthBox.Text;
            if (layer.CanWrite("borderWidth")) layer.BorderWidth = GraphBorderWidthBox.Text;
            if (layer.CanWrite("InnerCircleRadius")) layer.InnerCircleRadius = GraphInnerCircleRadiusBox.Text;
            if (layer.CanWrite("SplitBlockWidth")) layer.SplitBlockWidth = GraphSplitBlockWidthBox.Text;
            if (layer.CanWrite("SplitBlankWidth")) layer.SplitBlankWidth = GraphSplitBlankWidthBox.Text;
            if (layer.CanWrite("useSubsection")) layer.UseSubsection = GraphUseSubsectionCheck.IsChecked == true ? "True" : "False";
            if (layer.CanWrite("fillBack")) layer.FillBack = GraphFillBackCheck.IsChecked == true ? "True" : "False";
            if (layer.CanWrite("revert")) layer.Revert = GraphRevertCheck.IsChecked == true ? "True" : "False";
        }

        if (ImageEditPanel.Visibility == Visibility.Visible)
        {
            layer.Media = ImageFileBox.Text;
            layer.ZoomRate = TryParseZoom(ZoomBox.Text, out var zoom)
                ? FormatZoom(Math.Clamp(zoom, 0.01, 10.0))
                : "1";
            if (layer.CanWrite("rotate")) layer.Rotate = ImageRotateBox.Text;
            if (layer.CanWrite("rect")) layer.Rect = ImageRectBox.Text;
        }
    }

    private void SetBusy(bool isBusy, string status)
    {
        LoadButton.IsEnabled = !isBusy;
        SaveButton.IsEnabled = !isBusy;
        ApplyButton.IsEnabled = !isBusy;
        RemoveButton.IsEnabled = !isBusy;
        MoveUpButton.IsEnabled = !isBusy;
        MoveDownButton.IsEnabled = !isBusy;
        AddTextButton.IsEnabled = !isBusy;
        AddDataButton.IsEnabled = !isBusy;
        AddImageButton.IsEnabled = !isBusy;
        AddGraphButton.IsEnabled = !isBusy;
        BackgroundButton.IsEnabled = !isBusy;
        ApplyAllButton.IsEnabled = !isBusy;
        RestartButton.IsEnabled = !isBusy;
        ExportLConnectButton.IsEnabled = !isBusy;
        StatusText.Text = status;
    }

    private void SetStatus(string status)
    {
        StatusText.Text = status;
    }

    private void StartResize(LayerRow layer, Point previewPoint)
    {
        PushUndoState();
        _editorUndoArmed = true;
        _isResizingPreview = true;
        _dragLayer = layer;
        _resizeStartTemplatePoint = new Point(ToTemplate(previewPoint.X), ToTemplate(previewPoint.Y));

        double.TryParse(layer.Width, out _resizeStartWidth);
        double.TryParse(layer.Height, out _resizeStartHeight);
        double.TryParse(layer.ColumnWidth, out _resizeStartColumnWidth);
        double.TryParse(layer.Diameter, out _resizeStartDiameter);
        double.TryParse(layer.Size, out _resizeStartSize);
        if (TryParseZoom(layer.ZoomRate, out _resizeStartZoom))
        {
            // Zoom rate parsed
        }
        else
        {
            _resizeStartZoom = 1.0;
        }

        _shadowStartPositions.Clear();
        if (PairCheck.IsChecked == true)
        {
            var paired = FindPairedLayer(layer);
            if (paired != null)
            {
                var sx = TryParseInt(paired.X, out var x) ? x : 0;
                var sy = TryParseInt(paired.Y, out var y) ? y : 0;
                _shadowStartPositions[paired] = new Point(sx, sy);
            }
        }

        PreviewCanvas.CaptureMouse();
    }

    private void RegisterInputListeners()
    {
        XBox.TextChanged += (s, e) => OnInputChanged();
        YBox.TextChanged += (s, e) => OnInputChanged();
        SizeBox.TextChanged += (s, e) =>
        {
            SyncSliderFromText(SizeBox, SizeSlider);
            OnInputChanged();
        };
        ColorBox.TextChanged += (s, e) => OnInputChanged();
        TextBox.TextChanged += TextBox_TextChanged;
        FormatBox.TextChanged += (s, e) => OnInputChanged();
        WidthBox.TextChanged += (s, e) =>
        {
            SyncSliderFromText(WidthBox, WidthSlider);
            OnInputChanged();
        };
        HeightBox.TextChanged += (s, e) =>
        {
            SyncSliderFromText(HeightBox, HeightSlider);
            OnInputChanged();
        };
        RadiusBox.TextChanged += (s, e) =>
        {
            SyncSliderFromText(RadiusBox, RadiusSlider);
            OnInputChanged();
        };
        DiameterBox.TextChanged += (s, e) =>
        {
            SyncSliderFromText(DiameterBox, DiameterSlider);
            OnInputChanged();
        };
        ThicknessBox.TextChanged += (s, e) =>
        {
            SyncSliderFromText(ThicknessBox, ThicknessSlider);
            OnInputChanged();
        };
        FrontColorBox.TextChanged += (s, e) => OnInputChanged();
        BackColorBox.TextChanged += (s, e) => OnInputChanged();
        GradientColorBox.TextChanged += (s, e) => OnInputChanged();
        UseGradientCheck.Checked += (s, e) => OnInputChanged();
        UseGradientCheck.Unchecked += (s, e) => OnInputChanged();
        ZoomBox.TextChanged += (s, e) =>
        {
            SyncSliderFromText(ZoomBox, ZoomSlider);
            OnInputChanged();
        };
        ImageFileBox.TextChanged += (s, e) => OnInputChanged();
        ImageRotateBox.TextChanged += (s, e) => OnInputChanged();
        ImageRectBox.TextChanged += (s, e) => OnInputChanged();

        FontCombo.SelectionChanged += (s, e) => OnInputChanged();
        DataCombo.SelectionChanged += (s, e) => OnInputChanged();
        GraphStyleCombo.SelectionChanged += (s, e) => OnInputChanged();
        AlignmentCombo.SelectionChanged += (s, e) => OnInputChanged();
        FontIntervalBox.TextChanged += (s, e) => OnInputChanged();
        LineHeightBox.TextChanged += (s, e) => OnInputChanged();
        ItalicCheck.Checked += (s, e) => OnInputChanged();
        ItalicCheck.Unchecked += (s, e) => OnInputChanged();
        GraphDirectionBox.TextChanged += (s, e) => OnInputChanged();
        GraphLineWidthBox.TextChanged += (s, e) => OnInputChanged();
        GraphColumnWidthBox.TextChanged += (s, e) => OnInputChanged();
        GraphBorderWidthBox.TextChanged += (s, e) => OnInputChanged();
        GraphInnerCircleRadiusBox.TextChanged += (s, e) => OnInputChanged();
        GraphSplitBlockWidthBox.TextChanged += (s, e) => OnInputChanged();
        GraphSplitBlankWidthBox.TextChanged += (s, e) => OnInputChanged();
        GraphTypeNameBox.SelectionChanged += (s, e) => OnInputChanged();
        GraphTypeNameBox.LostKeyboardFocus += (s, e) => OnInputChanged();
        GraphSubTypeNameBox.SelectionChanged += (s, e) => OnInputChanged();
        GraphSubTypeNameBox.LostKeyboardFocus += (s, e) => OnInputChanged();
        GraphUseSubsectionCheck.Checked += GraphUseSubsectionCheck_Changed;
        GraphUseSubsectionCheck.Unchecked += GraphUseSubsectionCheck_Changed;
        GraphFillBackCheck.Checked += (s, e) => OnInputChanged();
        GraphFillBackCheck.Unchecked += (s, e) => OnInputChanged();
        GraphRevertCheck.Checked += (s, e) => OnInputChanged();
        GraphRevertCheck.Unchecked += (s, e) => OnInputChanged();
        SetTextCheck.Checked += SetTextCheck_Changed;
        SetTextCheck.Unchecked += SetTextCheck_Changed;

        BoldCheck.Checked += (s, e) => OnInputChanged();
        BoldCheck.Unchecked += (s, e) => OnInputChanged();
    }

    private void GraphUseSubsectionCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        OnInputChanged();
        if (LayerGrid.SelectedItem is not LayerRow layer)
        {
            return;
        }

        var isArcGraph = (layer.Type ?? "").Contains("GraphArchBar", StringComparison.OrdinalIgnoreCase);
        var canShowSplit = !isArcGraph &&
                           GraphUseSubsectionCheck.IsChecked == true &&
                           (layer.CanWrite("SplitBlockWidth") || layer.CanWrite("SplitBlankWidth"));
        GraphSplitLabel.Visibility = GraphSplitPanel.Visibility = canShowSplit ? Visibility.Visible : Visibility.Collapsed;
        GraphAdvancedExpander.Visibility =
            GraphDirectionBox.Visibility == Visibility.Visible ||
            GraphLineWidthBox.Visibility == Visibility.Visible ||
            GraphColumnWidthBox.Visibility == Visibility.Visible ||
            GraphBorderWidthBox.Visibility == Visibility.Visible ||
            GraphInnerCircleRadiusBox.Visibility == Visibility.Visible ||
            GraphSplitPanel.Visibility == Visibility.Visible ||
            GraphTypeNameBox.Visibility == Visibility.Visible ||
            GraphSubTypeNameBox.Visibility == Visibility.Visible ||
            GraphFlagsPanel.Visibility == Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void OnInputChanged()
    {
        if (_isLoading) return;
        if (LayerGrid.SelectedItem is not LayerRow layer) return;

        if (!_editorUndoArmed)
        {
            PushUndoState();
            _editorUndoArmed = true;
        }

        int oldX = 0;
        int oldY = 0;
        if (TryParseInt(layer.X, out var ox)) oldX = ox;
        if (TryParseInt(layer.Y, out var oy)) oldY = oy;
        var oldSize = layer.Size ?? "";

        UpdateLayerFromInputs(layer);
        _dirtyLayers.Add(layer);

        if (!_isDraggingPreview && !_isResizingPreview)
        {
            if (PairCheck.IsChecked == true)
            {
                var paired = FindPairedLayer(layer);
                if (paired != null)
                {
                    int newX = 0;
                    int newY = 0;
                    if (TryParseInt(layer.X, out var nx)) newX = nx;
                    if (TryParseInt(layer.Y, out var ny)) newY = ny;
                    int dx = newX - oldX;
                    int dy = newY - oldY;

                    if (dx != 0 || dy != 0)
                    {
                        int shX = 0;
                        int shY = 0;
                        if (TryParseInt(paired.X, out var px)) shX = px;
                        if (TryParseInt(paired.Y, out var py)) shY = py;

                        paired.X = (shX + dx).ToString();
                        paired.Y = (shY + dy).ToString();
                    }

                    if (layer.Size != oldSize)
                    {
                        SyncShadowProperties(layer, paired);
                    }
                    _dirtyLayers.Add(paired);
                }
            }

        }

        RequestPreviewDraw();
    }

    private void SetTextCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _updatingTextOverride) return;
        _updatingTextOverride = true;
        if (SetTextCheck.IsChecked == true)
        {
            _isLoading = true;
            SetComboText(DataCombo, "StaticText");
            _isLoading = false;
        }
        _updatingTextOverride = false;
        OnInputChanged();
    }

    private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _updatingTextOverride) return;
        if (LayerGrid.SelectedItem is LayerRow layer &&
            string.Equals(layer.Type, "GraphItem", StringComparison.OrdinalIgnoreCase))
        {
            var source = GetComboText(DataCombo);
            if (SetTextCheck.IsChecked != true &&
                !string.IsNullOrWhiteSpace(source) &&
                !source.Equals("StaticText", StringComparison.OrdinalIgnoreCase))
            {
                layer.PreviewValueEdited = true;
                layer.Text = TextBox.Text;
                _previewSampleOverrides[source] = TextBox.Text;
            }
        }
        OnInputChanged();
    }

    private async void AutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        _autoSaveTimer.Stop();
        await Task.CompletedTask;
    }

    private void LoadEditorSettings()
    {
        try
        {
            var settingsPath = Path.Combine(_supporter.WorkingDirectory, "theme_editor_settings.json");
            if (!File.Exists(settingsPath)) return;
            var json = File.ReadAllText(settingsPath);
            using var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("language", out var langProp))
            {
                var lang = langProp.GetString();
                if (!string.IsNullOrEmpty(lang))
                {
                    foreach (ComboBoxItem item in LanguageCombo.Items)
                    {
                        if (string.Equals(item.Tag?.ToString(), lang, StringComparison.OrdinalIgnoreCase))
                        {
                            LanguageCombo.SelectedItem = item;
                            break;
                        }
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("theme", out var themeProp))
            {
                var theme = themeProp.GetString();
                if (!string.IsNullOrEmpty(theme))
                {
                    foreach (ComboBoxItem item in UiThemeCombo.Items)
                    {
                        if (string.Equals(item.Tag?.ToString(), theme, StringComparison.OrdinalIgnoreCase))
                        {
                            UiThemeCombo.SelectedItem = item;
                            break;
                        }
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("deviceModel", out var devProp))
            {
                var deviceModel = devProp.GetString();
                if (!string.IsNullOrEmpty(deviceModel))
                {
                    foreach (ComboBoxItem item in DeviceCombo.Items)
                    {
                        if (string.Equals(item.Tag?.ToString(), deviceModel, StringComparison.OrdinalIgnoreCase))
                        {
                            DeviceCombo.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
        }
        catch { }
    }

    private async Task RevertTemplateBackgroundAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentTemplateId)) return;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var devicePaths = GetLConnectDevicePaths();
        var templateIdJson = JsonSerializer.Serialize(_currentTemplateId);

        foreach (var path in devicePaths)
        {
            var encodedPath = Uri.EscapeDataString(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(path)));
            var url = $"http://127.0.0.1:11021/?action=Device&devicePath={encodedPath}&type=RevertTemplateBackground";
            try
            {
                await client.PostAsync(url, new StringContent(templateIdJson, System.Text.Encoding.UTF8, "application/json"));
            }
            catch { }
        }
    }

    private void LayerGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not LayerRow layer)
        {
            return;
        }

        PushUndoState();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _dirtyLayers.Add(layer);
            PopulateEditorFromSelection();
            LayerGrid.Items.Refresh();
            DrawPreview();
            SetStatus("Grid edit changed. Press Apply to save.");
        }), System.Windows.Threading.DispatcherPriority.Background);
    }
}

