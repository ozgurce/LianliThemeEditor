using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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
    private const string UniversalScreenDeviceModel = "universal-screen-8.8-inch";
    private const string Vm92DeviceModel = "vm-9.2-inch";
    private const string GroupMetadataMarker = "__LIAN_EDITOR_GROUPS_V1__";
    private const string GalleryManifestUrl = "https://raw.githubusercontent.com/ozgurce/LianliThemeEditor/main/templates/gallery.json";
    private const string GalleryRawBaseUrl = "https://raw.githubusercontent.com/ozgurce/LianliThemeEditor/main/templates/";
    private const string GalleryContentsApiUrl = "https://api.github.com/repos/ozgurce/LianliThemeEditor/contents/templates/gallery.json?ref=main";
    private const string GalleryStatsApiBaseUrl = "https://lianli-theme-gallery.ozgurce.workers.dev";
    private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();
    private const string GitHubRepoUrl = "https://github.com/ozgurce/LianliThemeEditor";
    private const string GitHubIssuesUrl = "https://github.com/ozgurce/LianliThemeEditor/issues";
    private const string GitHubLatestReleaseApiUrl = "https://api.github.com/repos/ozgurce/LianliThemeEditor/releases/latest";
    private const string GitHubReleasesUrl = "https://github.com/ozgurce/LianliThemeEditor/releases";

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
        public string ExportTemplateId { get; init; } = "";
        public string TemplatePath { get; init; } = "";
        public string BackgroundPath { get; init; } = "";
        public string BackgroundEntryName { get; init; } = "";
        public List<string> ImagePaths { get; init; } = new();
    }

    private sealed class PreparedExportBackground
    {
        public string Path { get; init; } = "";
        public List<string> TemporaryPaths { get; init; } = new();
        public bool IsTemporary => TemporaryPaths.Count > 0;
    }

    private sealed class GroupingMetadata
    {
        public int Version { get; set; } = 1;
        public List<GroupingMetadataGroup> Groups { get; set; } = new();
        public List<GroupingMetadataMember> Members { get; set; } = new();
    }

    private sealed class GroupingMetadataGroup
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsExpanded { get; set; } = true;
        public bool IsLocked { get; set; }
        public string Color { get; set; } = "#246FF2";
    }

    private sealed class GroupingMetadataMember
    {
        public string GroupId { get; set; } = "";
        public int Index { get; set; }
        public string Signature { get; set; } = "";
    }

    private static readonly string[] DataSources =
    {
        "CPUTEMP", "CPUTEMP_F", "CPUCLOCK", "CPUCLOCK_G", "CPULOAD", "CPUFAN",
        "CPUPWR", "CPUVOLTAGE", "CPUMODEL",
        "GPUTEMP", "GPUTEMP_F", "GPUCLOCK", "GPUCLOCK_G", "GPULOAD", "GPUFAN",
        "GPUPWR", "GPUVOLTAGE", "GPUMODEL", "GPURAMLOAD", "GPURAM", "GPURAMTOTAL",
        "RAMLOAD", "RAM", "RAM_GB", "RAMVALID", "RAMVALID_GB", "RAMTOTAL", "RAMTOTAL_GB",
        "HDDTEMP", "HDDTEMP_F", "HDDUSED", "DRVLOAD", "PUMP", "WATERPUMP",
        "WATERTEMPC", "WATERTEMPF",
        "UPSPEED", "DOWNDSPEED", "FPS_AVG",
        "TIME", "DATE", "DAY", "APM", "StaticText"
    };

    private readonly SupporterBridge _supporter;
    private string _currentTemplatePath = "";
    private string _currentTemplateId = "";
    private DateTime _currentTemplateWriteStampUtc = DateTime.MinValue;
    private bool IsOfflineMode => OfflineModeCheck?.IsChecked == true;
    private string _currentBackgroundPath = "";
    private string _selectedBackgroundSourcePath = "";
    private bool _isLoading;
    private bool _isDraggingPreview;
    private Point _dragStartTemplatePoint;
    private readonly Dictionary<LayerRow, Point> _dragStartPositions = new();
    private readonly Dictionary<LayerRow, Rect> _dragStartPreviewBounds = new();
    private readonly Dictionary<LayerRow, Rect> _dragStartSelectionBounds = new();
    private LayerRow? _dragLayer;
    private readonly Dictionary<LayerRow, FrameworkElement> _previewLayerVisuals = new();
    private readonly Dictionary<LayerRow, Rectangle> _previewSelectionVisuals = new();
    private readonly Dictionary<LayerRow, Ellipse> _previewClockCenterMarkers = new();
    private readonly HashSet<LayerRow> _clockDragEditPoseLayers = new();
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
    private int _sensorPreviewRenderVersion;
    private CancellationTokenSource? _sensorPreviewRenderCts;
    private int _graphPreviewRenderVersion;
    private CancellationTokenSource? _graphPreviewRenderCts;

    // Undo/Redo and Shadow Pairing States
    private sealed record EditSnapshot(string Description, byte[] Layers, DateTime CreatedAtUtc);
    private readonly Stack<EditSnapshot> _undoStack = new();
    private readonly Stack<EditSnapshot> _redoStack = new();
    private bool _editorUndoArmed;
    private readonly Dictionary<int, int> _shadowLinks = new();
    private readonly HashSet<string> _lockedLayerKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<LayerRow, Point> _shadowStartPositions = new();
    private readonly HashSet<LayerRow> _dirtyLayers = new();
    private Dictionary<string, LayerRow>? _pendingDirtyLayersAfterAdd;
    private bool _soloSelectedLayers;
    private bool _groupingEnabled = true;
    private bool _savingGroupingMetadata;
    private readonly System.Windows.Threading.DispatcherTimer _autoSaveTimer;
    private bool _isSavingRecoverySnapshot;
    private bool _backgroundDirty;
    private readonly RecoveryService _recoveryService = new();
    private RecoverySnapshot? _pendingRecoverySnapshot;
    private readonly ThemePackageValidationService _themeValidator = new();
    private readonly DiagnosticService _diagnosticService = new();
    private readonly LConnectClientService _lConnectClient = new();
    private string _universal88ApplyTraceId = "";
    private readonly GallerySubmissionService _gallerySubmissionService = new();
    private readonly ThemeInstallationService _themeInstallationService = new();
    private readonly LayerGroupService _layerGroupService = new();
    private double _canvasZoom = 1.8;
    private Dictionary<string, string> _languageText = new(StringComparer.OrdinalIgnoreCase);
    private bool _updatingTextOverride;
    private readonly Dictionary<string, string> _previewSampleOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource> _previewImageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource> _templateThumbnailCache = new(StringComparer.OrdinalIgnoreCase);
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
    private bool _syncingNumericSliders;
    private bool _syncingThemeToggle;
    private bool _syncingUniversalOrientation;
    private double _templateCanvasWidth = 480.0;
    private double _templateCanvasHeight = 480.0;
    private double _previewCanvasWidth = 240.0;
    private double _previewCanvasHeight = 240.0;
    private double _previewScale = 0.5;
    private const double PreviewMaskTemplateThickness = 10.0;
    private string _generatedBackgroundPreviewFramePath = "";
    private static double _textPreviewRenderScale = 1.0;

    public ObservableCollection<LayerRow> Layers { get; } = new();
    public ObservableCollection<LayerGroup> LayerGroups { get; } = new();
    public ICollectionView LayerView { get; }
    public ObservableCollection<GraphStyleOption> GraphStyles { get; } = new();
    public ObservableCollection<TemplateOption> TemplateOptions { get; } = new();
    public ObservableCollection<GalleryThemeItem> GalleryThemes { get; } = new();
    public ObservableCollection<GalleryThemeItem> GalleryVisibleThemes { get; } = new();
    private readonly HashSet<string> _activeGalleryDownloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _recentGalleryDownloads = new(StringComparer.OrdinalIgnoreCase);
    private bool _galleryLoadStarted;
    private readonly Dictionary<string, byte[]> _galleryPackageBytesCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLoadingGalleryPreviews;
    private const int GalleryPreviewBatchSize = 18;
    private bool _animateVideoPreviews = true;

    private static HttpClient CreateSharedHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LianLiThemeEditor");
        return client;
    }

    public MainWindow()
    {
        LayerView = CollectionViewSource.GetDefaultView(Layers);
        LayerView.Filter = item => item is LayerRow layer && !layer.IsEditorMetadata;
        DataContext = this;

        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            WriteStartupExceptionLog(ex);
            throw;
        }
        InitializeCustomFonts();

        _supporter = new SupporterBridge();
        _autoSaveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.5)
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
                RequestPreviewDraw();
            }
        };
        _livePreviewTimer.Start();

        SupporterPathText.Text = _supporter.SupporterPath;
        DeviceCombo.SelectedIndex = 0;
        UniversalOrientationCombo.SelectedIndex = 0;
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
        AttachAlphaColorMenu(SensorRingEndColorPickButton, SensorRingEndColorBox);
        AttachAlphaColorMenu(GradientColorPickButton, GradientColorBox);
        AttachAlphaColorMenu(TextGradientColorPickButton, TextGradientColorBox);
        AttachAlphaColorMenu(SensorTopColorPickButton, SensorTopColorBox);
        AttachAlphaColorMenu(SensorBottomColorPickButton, SensorBottomColorBox);
        AttachAlphaColorMenu(ChartFillColorPickButton, ChartFillColorBox);
        AttachColorTextBoxPreview(
            ColorBox,
            TextGradientColorBox,
            SensorTopColorBox,
            SensorBottomColorBox,
            FrontColorBox,
            SensorRingEndColorBox,
            BackColorBox,
            GradientColorBox,
            ChartFillColorBox,
            AddColorBox,
            ShadowColorBox);
        SetCanvasZoom(_canvasZoom);

        RegisterInputListeners();
    }

    private static void WriteStartupExceptionLog(Exception ex)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "startup_error.log");
            File.WriteAllText(logPath, ex.ToString());
        }
        catch
        {
        }
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
            GalleryItemsControl.ItemsSource = GalleryVisibleThemes;
            AboutVersionText.Text = GetAppBuildDisplayText();
            RefreshTemplateList();
            RefreshDataSourceItems();
            SetComboText(DataCombo, "CPUTEMP");
            SetComboText(AddDataCombo, "GPUTEMP");
            AddLayerTypeCombo.SelectedIndex = 0;

            var defaultFont = GetDefaultLayerFontName();
            PopulateFontCombos(new[] { defaultFont }.Concat(_customFontNames));
            SetComboText(FontCombo, defaultFont);
            SetComboText(AddFontCombo, defaultFont);

            LoadEditorSettings();
            UpdateCanvasConfiguration(resetZoom: true);
            RefreshTemplateList(selectFirstWhenMissing: true);
            _isLoading = false;
            await LoadInitialThemeAsync();
            LoadRecoverySnapshotForAbout();
            UpdateHistoryButtons();
            _ = RunDeferredStartupWorkAsync();
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
            SetStatus(GetLanguageText("messages.initializationFailed", "Initialization failed"));
            MessageBox.Show(this, ex.Message, GetLanguageText("messages.initializationFailed", "Initialization failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        var option = TemplateCombo.SelectedItem as TemplateOption;
        if (option is null || string.IsNullOrWhiteSpace(option.Path))
        {
            RefreshTemplateList(selectFirstWhenMissing: true);
            option = TemplateCombo.SelectedItem as TemplateOption;
        }

        if (option is null || string.IsNullOrWhiteSpace(option.Path))
        {
            MessageBox.Show(this, GetLanguageText("messages.noTemplateFound", "No template was found for this device."),
                GetLanguageText("messages.loadFailed", "Load failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        UseActiveCheck.IsChecked = false;
        TemplateIdBox.Text = option.Id;
        _currentTemplatePath = option.Path;
        await LoadLayersAsync(true);
    }

    private async void ActiveThemeButton_Click(object sender, RoutedEventArgs e)
    {
        // A template picked from the list is an explicit edit target. Do not switch
        // back to "Use active theme" here: L-Connect can briefly keep reporting the
        // previously active ID while ReloadAssets is in flight, causing Apply All to
        // redirect the write and activation to the wrong template.
        var wasLoading = _isLoading;
        _isLoading = true;
        UseActiveCheck.IsChecked = false;
        _isLoading = wasLoading;
        await LoadLayersAsync(false);
    }

    private void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_currentTemplatePath) || !File.Exists(_currentTemplatePath))
            {
                throw new FileNotFoundException(
                    GetLanguageText("messages.loadThemeFirst", "Load a theme first."));
            }

            var backupPath = GetManualBackupPath();
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(_currentTemplatePath, backupPath, true);
            SetStatus(FormatLanguageText(
                "status.backupCreated",
                "Backup created: {0}",
                Path.GetFileName(_currentTemplatePath)));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                GetLanguageText("messages.backupFailed", "Backup failed"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_currentTemplatePath))
            {
                throw new InvalidOperationException(
                    GetLanguageText("messages.loadThemeFirst", "Load a theme first."));
            }

            var backupPath = GetManualBackupPath();
            if (!File.Exists(backupPath))
            {
                throw new FileNotFoundException(
                    GetLanguageText("messages.noBackupFound", "No manual backup exists for this template."));
            }

            if (MessageBox.Show(
                    this,
                    GetLanguageText("messages.restoreBackupConfirm", "Restore the last manual backup for this template?"),
                    GetLanguageText("messages.restoreBackupTitle", "Restore backup"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            SetBusy(true, GetLanguageText("status.restoringBackup", "Restoring backup..."));
            File.Copy(backupPath, _currentTemplatePath, true);
            var deviceModel = GetSelectedDeviceModel();
            var templatePath = _currentTemplatePath;
            var restored = await Task.Run(() => _supporter.LoadTemplatePathAsync(
                deviceModel, templatePath));
            await Dispatcher.InvokeAsync(() => ApplyTemplateResult(restored));
            SetBusy(false, GetLanguageText("status.backupRestored", "Backup restored."));
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.restoreFailed", "Restore failed."));
            MessageBox.Show(
                this,
                ex.Message,
                GetLanguageText("messages.restoreFailed", "Restore failed"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string GetManualBackupPath()
    {
        var templateId = string.IsNullOrWhiteSpace(_currentTemplateId)
            ? Path.GetFileNameWithoutExtension(_currentTemplatePath)
            : _currentTemplateId;
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LianLiThemeEditor",
            "Backups",
            SanitizeFileName(GetSelectedDeviceModel()));
        return Path.Combine(root, $"{SanitizeFileName(templateId)}.template");
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

    private void CopyrightButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.instagram.com/ozgur.ny/",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                FormatLanguageText("messages.linkOpenFailed", "The link could not be opened.\n\n{0}", ex.Message),
                GetLanguageText("messages.linkOpenFailedTitle", "Link could not be opened"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void UseActiveCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || !IsLoaded || UseActiveCheck.IsChecked != true)
        {
            return;
        }

        await LoadLayersAsync(false);
    }

    private async void OfflineModeCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || !IsLoaded)
        {
            return;
        }

        if (OfflineModeCheck.IsChecked == true)
        {
            try
            {
                SetBusy(true, GetLanguageText("status.creatingOfflineCopy", "Creating offline copy..."));
                var wasLoading = _isLoading;
                _isLoading = true;
                UseActiveCheck.IsChecked = false;
                _isLoading = wasLoading;
                await EnsureOfflineTemplateCopyAsync(GetSelectedDeviceModel(), GetSelectedTemplatePath());
                await LoadLayersAsync(true);
                SetBusy(false, GetLanguageText("status.offlineModeReady", "Offline mode is using a local copy."));
            }
            catch (Exception ex)
            {
                SetBusy(false, GetLanguageText("status.loadFailed", "Load failed."));
                MessageBox.Show(this, ex.Message, GetLanguageText("messages.loadFailed", "Load failed"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            SetStatus(GetLanguageText("status.offlineModeDisabled", "Offline mode disabled."));
        }
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
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            await ApplyDirtyLayersAsync(deviceModel, target.TemplatePath);
            var exportSnapshot = CreateThemeExportSnapshot(deviceModel);
            await ExportThemePackageAsync(dialog.FileName, exportSnapshot);
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
            Filter = GetLanguageText("dialogs.themeImportFilter", "Theme packages (*.lltheme;*.zip)|*.lltheme;*.zip|Lian Li themes (*.lltheme)|*.lltheme|L-Connect ZIP (*.zip)|*.zip")
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            SetBusy(true, GetLanguageText("status.importingTheme", "Importing theme..."));
            var importPath = dialog.FileName;
            if (Path.GetExtension(importPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var converted = Path.Combine(Path.GetTempPath(), $"import_{Guid.NewGuid():N}.lltheme");
                var item = new GalleryThemeItem
                {
                    Id = Path.GetFileNameWithoutExtension(importPath),
                    Name = Path.GetFileNameWithoutExtension(importPath),
                    DeviceModel = GetSelectedDeviceModel()
                };
                ConvertLConnectZipToThemeEditorPackage(importPath, converted, item, item.Id);
                importPath = converted;
            }
            if (!ShowThemeValidation(_themeValidator.Validate(importPath, TemplateOptions.Select(option => option.Id))))
            {
                SetBusy(false, GetLanguageText("status.importCancelled", "Import cancelled."));
                return;
            }
            var result = await ImportThemePackageAsync(importPath);
            _isLoading = true;
            RefreshTemplateList();
            _isLoading = false;
            UseActiveCheck.IsChecked = false;
            TemplateIdBox.Text = result.Id;
            _currentTemplatePath = result.Path;
            await LoadLayersAsync(true);
            await ActivateInstalledThemeAsync(
                string.IsNullOrWhiteSpace(result.LConnectId) ? result.Id : result.LConnectId,
                GetSelectedDeviceModel(), result.Path, result.BackgroundPath);
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
            FileName = $"{CreateExportPackageBaseName(_currentTemplateId)}-LConnect.zip",
            DefaultExt = ".zip",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            SetBusy(true, GetLanguageText("status.exportingLConnect", "Creating L-Connect package..."));
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var requestedExportName = PromptExportTemplateId(Path.GetFileNameWithoutExtension(dialog.FileName), _currentTemplateId);
            if (string.IsNullOrWhiteSpace(requestedExportName))
            {
                SetBusy(false, GetLanguageText("status.exportCancelled", "Export cancelled."));
                return;
            }
            var exportTemplateId = GetUniqueTemplateId(GetTemplateRoot(deviceModel), requestedExportName);

            var animationMedia = Layers
                .FirstOrDefault(layer => string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
                ?.Media ?? "";
            var resolvedCurrentBackground = ResolveBackgroundPath(_currentBackgroundPath, animationMedia);
            var backgroundSource = File.Exists(resolvedCurrentBackground)
                ? resolvedCurrentBackground
                : File.Exists(_selectedBackgroundSourcePath)
                    ? _selectedBackgroundSourcePath
                    : resolvedCurrentBackground;
            var preparedExportBackground = await PrepareExportBackgroundAsync(
                deviceModel, backgroundSource, exportTemplateId);
            var exportBackground = preparedExportBackground.Path;

            try
            {
                await ApplyDirtyLayersAsync(deviceModel, target.TemplatePath);

                var refreshed = await Task.Run(() => _supporter.LoadTemplatePathAsync(
                    deviceModel, target.TemplatePath));
                ApplyTemplateResult(refreshed);
                _selectedBackgroundSourcePath = File.Exists(backgroundSource)
                    ? backgroundSource
                    : "";

                var exportTemplatePath = await PrepareLConnectExportTemplateAsync(
                    deviceModel, target.TemplatePath, exportTemplateId, exportBackground);
                var editorBackgroundPath = _currentBackgroundPath;
                try
                {
                    var exportPreviewFrame = await CreateDeterministicBackgroundPreviewAsync(exportBackground);
                    try
                    {
                        var previewSource = string.IsNullOrWhiteSpace(exportPreviewFrame)
                            ? exportBackground
                            : exportPreviewFrame;
                        LoadBackgroundPreview(previewSource, Path.GetFileName(previewSource));
                        DrawPreview();
                        await Dispatcher.InvokeAsync(
                            () =>
                            {
                                DrawPreview();
                                PreviewSurface.UpdateLayout();
                            },
                            System.Windows.Threading.DispatcherPriority.Render);
                        await SaveAndApplyThemePreviewAsync(
                            deviceModel, exportTemplatePath, exportTemplateId);
                    }
                    finally
                    {
                        TryDeleteFile(exportPreviewFrame);
                    }
                    var exportTemplate = await Task.Run(() => _supporter.LoadTemplatePathAsync(
                        deviceModel, exportTemplatePath));
                    var exportSnapshot = CreateThemeExportSnapshot(
                        deviceModel,
                        exportTemplate.TemplateId,
                        exportTemplate.TemplatePath,
                        exportTemplate.Layers,
                        exportBackground,
                        $"{exportTemplateId}{GetExportBackgroundExtension(deviceModel)}");
                    await Task.Run(() => ExportLConnectPackage(dialog.FileName, exportSnapshot));
                }
                finally
                {
                    LoadBackgroundPreview(editorBackgroundPath, Path.GetFileName(editorBackgroundPath));
                    RequestPreviewDraw();
                    TryDeleteFile(exportTemplatePath);
                }
            }
            finally
            {
                if (preparedExportBackground.IsTemporary)
                {
                    foreach (var temporaryPath in preparedExportBackground.TemporaryPaths)
                    {
                        TryDeleteFile(temporaryPath);
                    }
                }
            }

            SetBusy(false, GetLanguageText("status.lConnectExported", "L-Connect package exported."));
            var importHint = IsVm92Selected()
                ? GetLanguageText(
                    "messages.vm92LConnectImportHint",
                    "In L-Connect, open the VM 9.2 LCD screen, choose Import Template, and select this ZIP.")
                : GetLanguageText("messages.lConnectImportHint",
                    "In L-Connect, open the HydroShift template screen, choose Import Template, and select this ZIP.");
            MessageBox.Show(this,
                importHint,
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

    private async void Convert88To92Button_Click(object sender, RoutedEventArgs e)
    {
        var openDialog = new OpenFileDialog
        {
            Title = GetLanguageText("dialogs.convert88To92Open", "Choose an 8.8 L-Connect ZIP"),
            Filter = GetLanguageText("dialogs.lConnectTemplateFilter", "L-Connect template package (*.zip)|*.zip")
        };
        if (openDialog.ShowDialog(this) != true) return;

        var suggestedName = $"{Path.GetFileNameWithoutExtension(openDialog.FileName)}-VM92.zip";
        var saveDialog = new SaveFileDialog
        {
            Title = GetLanguageText("dialogs.convert88To92Save", "Save VM 9.2 L-Connect ZIP"),
            Filter = GetLanguageText("dialogs.lConnectTemplateFilter", "L-Connect template package (*.zip)|*.zip"),
            FileName = suggestedName,
            DefaultExt = ".zip",
            AddExtension = true
        };
        if (saveDialog.ShowDialog(this) != true) return;

        try
        {
            SetBusy(true, GetLanguageText("status.converting88To92", "Converting 8.8 package for VM 9.2..."));
            var exportTemplateId = CreateExportTemplateName(
                Path.GetFileNameWithoutExtension(saveDialog.FileName),
                Path.GetFileNameWithoutExtension(openDialog.FileName));
            await Task.Run(() => ConvertUniversal88LConnectZipToVm92(
                openDialog.FileName,
                saveDialog.FileName,
                exportTemplateId));
            SetBusy(false, GetLanguageText("status.converted88To92", "VM 9.2 package created."));
            MessageBox.Show(
                this,
                GetLanguageText(
                    "messages.convert88To92Hint",
                    "The converted ZIP is ready. In L-Connect, open the VM 9.2 LCD screen, choose Import Template, and select this ZIP."),
                GetLanguageText("messages.convert88To92Done", "VM 9.2 package created"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.convert88To92Failed", "Conversion failed."));
            MessageBox.Show(
                this,
                ex.Message,
                GetLanguageText("messages.convert88To92Failed", "Conversion failed"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task LoadLayersAsync(bool useCurrentTemplate)
    {
        try
        {
            SetBusy(true, GetLanguageText("status.loadingLayers", "Loading layers..."));
            var deviceModel = GetSelectedDeviceModel();
            TemplateLoadResult result;
            if (IsOfflineMode)
            {
                var selectedOption = TemplateCombo.SelectedItem as TemplateOption;
                var sourcePath = useCurrentTemplate && File.Exists(_currentTemplatePath)
                    ? _currentTemplatePath
                    : selectedOption?.Path ?? _currentTemplatePath;
                var offlinePath = await EnsureOfflineTemplateCopyAsync(deviceModel, sourcePath);
                result = await Task.Run(() => _supporter.LoadTemplatePathAsync(deviceModel, offlinePath));
            }
            else if (UseActiveCheck.IsChecked == true)
            {
                var activeTemplateId = await TryGetActiveTemplateIdFromLConnectAsync(deviceModel);
                var activeTemplatePath = ResolveTemplatePathByIdOrAlias(deviceModel, activeTemplateId);
                result = !string.IsNullOrWhiteSpace(activeTemplatePath)
                    ? await Task.Run(() => _supporter.LoadTemplatePathAsync(deviceModel, activeTemplatePath))
                    : !string.IsNullOrWhiteSpace(activeTemplateId)
                        ? await Task.Run(() => _supporter.LoadLayersAsync(deviceModel, false, activeTemplateId))
                        : await Task.Run(() => _supporter.LoadLayersAsync(deviceModel, true, ""));
            }
            else
            {
                var selectedOption = TemplateCombo.SelectedItem as TemplateOption;
                var templatePath = useCurrentTemplate && File.Exists(_currentTemplatePath)
                    ? _currentTemplatePath
                    : selectedOption?.Path;

                if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
                {
                    RefreshTemplateList(selectFirstWhenMissing: true);
                    selectedOption = TemplateCombo.SelectedItem as TemplateOption;
                    templatePath = selectedOption?.Path;
                }

                if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
                {
                    throw new FileNotFoundException(
                        GetLanguageText("messages.noTemplateFound", "No template was found for this device."));
                }

                _currentTemplatePath = templatePath;
                TemplateIdBox.Text = Path.GetFileNameWithoutExtension(templatePath);
                result = await Task.Run(() => _supporter.LoadTemplatePathAsync(deviceModel, templatePath));
            }

            ApplyTemplateResult(result);
            SetBusy(false, FormatLanguageText("status.layersLoaded", "Loaded {0} layer(s).", Layers.Count(layer => !layer.IsEditorMetadata)));
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.loadFailed", "Load failed."));
            MessageBox.Show(this, ex.Message, GetLanguageText("messages.loadFailed", "Load failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadInitialThemeAsync()
    {
        SetBusy(true, GetLanguageText("status.loadingLayers", "Loading layers..."));
        var deviceModel = GetSelectedDeviceModel();
        TemplateLoadResult? result = null;

        try
        {
            var activeTemplateId = await TryGetActiveTemplateIdFromLConnectAsync(deviceModel);
            var activeTemplatePath = ResolveTemplatePathByIdOrAlias(deviceModel, activeTemplateId);
            result = !string.IsNullOrWhiteSpace(activeTemplatePath)
                ? await Task.Run(() => _supporter.LoadTemplatePathAsync(deviceModel, activeTemplatePath))
                : !string.IsNullOrWhiteSpace(activeTemplateId)
                    ? await Task.Run(() => _supporter.LoadLayersAsync(deviceModel, false, activeTemplateId))
                    : await Task.Run(() => _supporter.LoadLayersAsync(deviceModel, true, ""));
            if (string.IsNullOrWhiteSpace(result.TemplatePath) || !File.Exists(result.TemplatePath))
            {
                result = null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Active template could not be loaded at startup: {ex.Message}");
        }

        if (result != null)
        {
            var wasLoading = _isLoading;
            _isLoading = true;
            UseActiveCheck.IsChecked = true;
            _isLoading = wasLoading;
        }
        else
        {
            RefreshTemplateList(selectFirstWhenMissing: true);
            var firstTemplate = TemplateOptions.FirstOrDefault(option =>
                !string.IsNullOrWhiteSpace(option.Path) && File.Exists(option.Path));
            if (firstTemplate == null)
            {
                SetBusy(false, GetLanguageText("status.loadFailed", "Load failed."));
                throw new FileNotFoundException(
                    GetLanguageText("messages.noTemplateFound", "No template was found for this device."));
            }

            var wasLoading = _isLoading;
            _isLoading = true;
            UseActiveCheck.IsChecked = false;
            TemplateCombo.SelectedItem = firstTemplate;
            TemplateIdBox.Text = firstTemplate.Id;
            _currentTemplatePath = firstTemplate.Path;
            _isLoading = wasLoading;
            result = await Task.Run(() =>
                _supporter.LoadTemplatePathAsync(deviceModel, firstTemplate.Path));
        }

        ApplyTemplateResult(result);
        SetBusy(false, FormatLanguageText("status.layersLoaded", "Loaded {0} layer(s).", Layers.Count(layer => !layer.IsEditorMetadata)));
    }

    private void ApplyTemplateResult(TemplateLoadResult result)
    {
        var wasLoading = _isLoading;
        _isLoading = true;
        _selectedBackgroundSourcePath = "";
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

        try
        {
            Layers.Clear();
            LayerGroups.Clear();
            var groupingMetadata = ReadGroupingMetadata(result.Layers);
            foreach (var layer in result.Layers)
            {
                NormalizeAnimationLayerZoom(layer);
                layer.IsEditorMetadata = IsGroupingMetadataLayer(layer);
                if (layer.IsEditorMetadata)
                {
                    Layers.Add(layer);
                    continue;
                }
                if (NormalizeLayerTextForDevice(layer))
                {
                    layer.IsDirty = true;
                    _dirtyLayers.Add(layer);
                }
                if (int.TryParse(layer.Index, out var index) && _shadowLinks.TryGetValue(index, out var sourceIndex))
                {
                    layer.Description = FormatLanguageText("layers.shadowOfLayer", "Shadow of Layer {0}", sourceIndex);
                }
                layer.IsLocked = _lockedLayerKeys.Contains(GetLayerLockKey(_currentTemplatePath, layer.Index));
                SetLayerActionTooltips(layer);
                Layers.Add(layer);
            }
            ApplyGroupingMetadata(groupingMetadata);
            ConfigureLayerGrouping();

            TemplateTitleText.Text = string.IsNullOrWhiteSpace(_currentTemplateId)
                ? "Template ID: -"
                : $"Template ID: {_currentTemplateId}";
            TemplatePathText.Text = _currentTemplatePath;
            _currentTemplateWriteStampUtc = GetTemplateWriteStampUtc(_currentTemplatePath);
            var displayBackground = !string.IsNullOrWhiteSpace(result.Background)
                ? result.Background
                : Layers.FirstOrDefault(layer => string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))?.Media ?? "";

            BackgroundText.Text = string.IsNullOrWhiteSpace(displayBackground)
                ? GetLanguageText("top.backgroundEmpty", "Background: -")
                : FormatLanguageText("top.backgroundLoaded", "Background: {0}", displayBackground);
            LayerCountText.Text = string.Format(
                GetText(_languageText, "layers.count", "{0} layers"),
                Layers.Count(layer => !layer.IsEditorMetadata));
            SyncDeviceFromTemplatePath(_currentTemplatePath);
            SetUniversalOrientationFromLayers();
            SelectTemplateCombo(_currentTemplatePath);
            LoadBackgroundPreview(result.BackgroundPath, displayBackground);

            if (LayerView.Cast<object>().Any())
            {
                LayerGrid.SelectedIndex = Math.Min(1, LayerView.Cast<object>().Count() - 1);
            }
        }
        finally
        {
            _isLoading = wasLoading;
        }

        _livePreviewTimer.Interval = Layers.Count >= 60
            ? TimeSpan.FromSeconds(3)
            : TimeSpan.FromSeconds(1);

        if (!wasLoading)
        {
            PopulateEditorFromSelection();
        }
        RequestPreviewDraw();
    }

    private async Task<(string DeviceModel, string TemplatePath)> ResolveTemplateTargetAsync()
    {
        var deviceModel = GetSelectedDeviceModel();
        if (IsOfflineMode)
        {
            var offlinePath = await EnsureOfflineTemplateCopyAsync(deviceModel, GetSelectedTemplatePath());
            return (deviceModel, offlinePath);
        }

        if (UseActiveCheck.IsChecked == true)
        {
            // L-Connect can switch the active theme while the editor remains open. Resolve
            // the active id again before every write so an edit never lands in the theme
            // that happened to be active when the layer list was first loaded.
            var activeTemplateId = await TryGetActiveTemplateIdFromLConnectAsync(deviceModel);
            var activeTemplatePath = ResolveTemplatePathByIdOrAlias(deviceModel, activeTemplateId);
            if (!string.IsNullOrWhiteSpace(activeTemplatePath) && File.Exists(activeTemplatePath))
            {
                _currentTemplatePath = activeTemplatePath;
                _currentTemplateId = Path.GetFileNameWithoutExtension(activeTemplatePath);
                TemplateIdBox.Text = _currentTemplateId;
                TemplatePathText.Text = activeTemplatePath;
                TemplateTitleText.Text = $"Template ID: {_currentTemplateId}";
                return (deviceModel, activeTemplatePath);
            }

            // Keep the last verified path as a fallback when L-Connect's local API is
            // temporarily unavailable.
            if (!string.IsNullOrWhiteSpace(_currentTemplatePath) && File.Exists(_currentTemplatePath))
            {
                return (deviceModel, _currentTemplatePath);
            }

            var active = !string.IsNullOrWhiteSpace(activeTemplateId)
                    ? await Task.Run(() => _supporter.LoadLayersAsync(deviceModel, false, activeTemplateId))
                    : await Task.Run(() => _supporter.LoadLayersAsync(deviceModel, true, ""));
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
        else
        {
            var selectedTemplatePath = GetSelectedTemplatePath();
            if (!string.IsNullOrWhiteSpace(selectedTemplatePath) &&
                File.Exists(selectedTemplatePath))
            {
                _currentTemplatePath = selectedTemplatePath;
                _currentTemplateId = Path.GetFileNameWithoutExtension(selectedTemplatePath);
                TemplateIdBox.Text = _currentTemplateId;
                TemplatePathText.Text = selectedTemplatePath;
                await EnsureTemplateActiveForEditAsync(deviceModel, selectedTemplatePath);
            }
        }

        if (string.IsNullOrWhiteSpace(_currentTemplatePath) || !File.Exists(_currentTemplatePath))
        {
            throw new InvalidOperationException(GetLanguageText("messages.loadThemeFirst", "Load a theme first."));
        }

        return (deviceModel, _currentTemplatePath);
    }

    private async Task<string> EnsureOfflineTemplateCopyAsync(string deviceModel, string sourceTemplatePath)
    {
        if (!string.IsNullOrWhiteSpace(_currentTemplatePath) &&
            File.Exists(_currentTemplatePath) &&
            IsOfflineTemplatePath(_currentTemplatePath))
        {
            return _currentTemplatePath;
        }

        var sourcePath = !string.IsNullOrWhiteSpace(sourceTemplatePath) && File.Exists(sourceTemplatePath)
            ? sourceTemplatePath
            : _currentTemplatePath;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new InvalidOperationException(GetLanguageText("messages.loadThemeFirst", "Load a theme first."));
        }

        if (IsOfflineTemplatePath(sourcePath))
        {
            _currentTemplatePath = sourcePath;
            _currentTemplateId = Path.GetFileNameWithoutExtension(sourcePath);
            TemplateIdBox.Text = _currentTemplateId;
            TemplatePathText.Text = sourcePath;
            return sourcePath;
        }

        var templateRoot = GetTemplateRoot(deviceModel);
        Directory.CreateDirectory(templateRoot);
        var sourceId = Path.GetFileNameWithoutExtension(sourcePath);
        var offlineId = SanitizeFileName(sourceId + "_offline");
        if (string.IsNullOrWhiteSpace(offlineId)) offlineId = "OfflineTheme";
        var offlinePath = Path.Combine(templateRoot, offlineId + ".template");

        if (!File.Exists(offlinePath))
        {
            File.Copy(sourcePath, offlinePath, false);
            var sourceMetadata = sourcePath + ".themeeditor.json";
            if (File.Exists(sourceMetadata))
            {
                File.Copy(sourceMetadata, offlinePath + ".themeeditor.json", false);
            }
            await _supporter.NormalizeTemplateIdentityAsync(deviceModel, offlinePath, offlineId);
        }

        _currentTemplatePath = offlinePath;
        _currentTemplateId = offlineId;
        TemplateIdBox.Text = offlineId;
        TemplatePathText.Text = offlinePath;
        TemplateTitleText.Text = $"Template ID: {offlineId}";
        RefreshTemplateList();
        SelectTemplateCombo(offlinePath);
        return offlinePath;
    }

    private static bool IsOfflineTemplatePath(string templatePath)
    {
        var id = Path.GetFileNameWithoutExtension(templatePath);
        return id.EndsWith("_offline", StringComparison.OrdinalIgnoreCase);
    }

    private string GetSelectedTemplatePath()
    {
        if (TemplateCombo.SelectedItem is TemplateOption option &&
            !string.IsNullOrWhiteSpace(option.Path))
        {
            return option.Path;
        }

        return _currentTemplatePath;
    }

    private async Task EnsureTemplateActiveForEditAsync(string deviceModel, string templatePath)
    {
        var templateId = Path.GetFileNameWithoutExtension(templatePath);
        if (string.IsNullOrWhiteSpace(templateId) || !File.Exists(templatePath))
        {
            return;
        }

        _currentTemplatePath = templatePath;
        _currentTemplateId = templateId;
        TemplateIdBox.Text = templateId;
        TemplatePathText.Text = templatePath;
        TemplateTitleText.Text = $"Template ID: {templateId}";

        var wasLoading = _isLoading;
        _isLoading = true;
        // A template selected from the combo is an explicit edit target. Do not flip
        // back to "Use active template"; otherwise the next write can resolve the
        // older L-Connect selection and apply/save the wrong theme.
        UseActiveCheck.IsChecked = false;
        _isLoading = wasLoading;

        await Task.Run(() => TrySetActiveTemplateProfile(templateId, deviceModel));
        await TriggerLConnectRefreshAsync();
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

        ClearDirtyLayers();
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

    private static DateTime GetTemplateWriteStampUtc(string templatePath)
    {
        try
        {
            return string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath)
                ? DateTime.MinValue
                : File.GetLastWriteTimeUtc(templatePath);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private bool HasTemplateChangedSinceLastLoad(string templatePath)
    {
        var currentStamp = GetTemplateWriteStampUtc(templatePath);
        return currentStamp != DateTime.MinValue &&
               _currentTemplateWriteStampUtc != DateTime.MinValue &&
               currentStamp != _currentTemplateWriteStampUtc;
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
            .Where(item => !item.IsEditorMetadata && !string.Equals(item.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
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

    private void RefreshLayerIndexes()
    {
        for (var index = 0; index < Layers.Count; index++)
        {
            Layers[index].Index = index.ToString();
        }
        LayerGrid.Items.Refresh();
    }

    private List<LayerRow> GetSelectedLayers(bool includeLocked = true, bool includeAnimation = true)
    {
        return LayerGrid.SelectedItems
            .OfType<LayerRow>()
            .Where(layer => includeLocked || !layer.IsLocked)
            .Where(layer => includeAnimation || !string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
            .OrderBy(layer => int.TryParse(layer.Index, out var index) ? index : int.MaxValue)
            .ToList();
    }

    private void MarkLayerDirty(LayerRow layer)
    {
        if (!Layers.Contains(layer))
        {
            return;
        }

        layer.IsDirty = true;
        _dirtyLayers.Add(layer);
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void ClearDirtyLayers()
    {
        foreach (var layer in _dirtyLayers.ToList())
        {
            layer.IsDirty = false;
        }

        _dirtyLayers.Clear();
        _recoveryService.Clear();
        LayerGrid.Items.Refresh();
    }

    private void RefreshDirtyBadges()
    {
        foreach (var layer in Layers)
        {
            layer.IsDirty = _dirtyLayers.Contains(layer);
        }

        LayerGrid.Items.Refresh();
    }

    private void PopulateEditorFromSelection()
    {
        if (LayerGrid.SelectedItem is not LayerRow layer)
        {
            DuplicateButton.IsEnabled = false;
            PropertyEditorContent.Visibility = Visibility.Visible;
            ApplyLayerIcon(
                SelectedLayerIconBorder,
                SelectedLayerIconPath,
                SelectedLayerIconShadow,
                "M7,7 H25 V25 H7 Z M15,13 H17 V21 H15 Z M15,9 H17 V11 H15 Z",
                "#64748B");
            SelectedLayerTypeText.Text = GetLanguageText("properties.noLayerSelected", "Bir layer seçin");
            SelectedLayerDetailText.Text = GetLanguageText("properties.noLayerSelectedHint", "Düzenlemek için soldaki listeden bir layer seçin.");
            GeneralPropertiesCard.Visibility = Visibility.Collapsed;
            DataPropertiesCard.Visibility = Visibility.Collapsed;
            TextAndFormatCard.Visibility = Visibility.Collapsed;
            LayerOptionsCard.Visibility = Visibility.Collapsed;
            GradientOptionsCard.Visibility = Visibility.Collapsed;
            GraphEditPanel.Visibility = Visibility.Collapsed;
            ImageEditPanel.Visibility = Visibility.Collapsed;
            EditSeparator.Visibility = Visibility.Collapsed;
            return;
        }

        _isLoading = true;
        PropertyEditorContent.Visibility = Visibility.Visible;
        GeneralPropertiesCard.Visibility = Visibility.Visible;
        ApplyLayerIcon(
            SelectedLayerIconBorder,
            SelectedLayerIconPath,
            SelectedLayerIconShadow,
            layer.IconData,
            layer.IconColor);
        SelectedLayerTypeText.Text = GetLayerDisplayType(layer);
        SelectedLayerDetailText.Text = !string.IsNullOrWhiteSpace(layer.DataSource)
            ? layer.DataSource
            : layer.Media;
        IndexBox.Text = layer.Index;
        XBox.Text = layer.X;
        YBox.Text = layer.Y;
        SizeBox.Text = layer.Size;
        ColorBox.Text = NormalizeColorText(layer.Color);
        TextBox.Text = NormalizeLConnectText(layer.Text);
        FormatBox.Text = layer.Format;
        BoldCheck.IsChecked = string.Equals(layer.Bold, "True", StringComparison.OrdinalIgnoreCase);
        ItalicCheck.IsChecked = string.Equals(layer.Italic, "True", StringComparison.OrdinalIgnoreCase);
        SetComboText(FontCombo, ResolveCanonicalFontName(GetEffectiveLayerFont(layer.Font)));
        SetComboText(DataCombo, layer.DataSource);
        SetComboValue(GraphStyleCombo, layer.GraphStyle);
        SetAlignmentCombo(layer.AlignmentIndex);
        FontIntervalBox.Text = layer.FontInterval;
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
        FormatLabel.Content = GetLanguageText("labels.format", "FORMAT");

        // Toggle Dynamic Panels
        var type = layer.Type ?? "";
        bool isText = type.Equals("GraphItem", StringComparison.OrdinalIgnoreCase);
        bool isGraph = type.Contains("GraphStatuBar", StringComparison.OrdinalIgnoreCase) ||
                       type.Contains("GraphArchBar", StringComparison.OrdinalIgnoreCase) ||
                       type.Contains("GraphLine", StringComparison.OrdinalIgnoreCase) ||
                       type.Contains("DynamicBar", StringComparison.OrdinalIgnoreCase) ||
                       type.Equals("GraphSensor", StringComparison.OrdinalIgnoreCase);
        bool isSensor = type.Equals("GraphSensor", StringComparison.OrdinalIgnoreCase);
        bool isAnimation = type.Equals("GraphAnimation", StringComparison.OrdinalIgnoreCase);
        bool isClock = type.Equals("GraphClock", StringComparison.OrdinalIgnoreCase);
        DuplicateButton.IsEnabled = !isAnimation;
        bool isImage = type.Contains("Image", StringComparison.OrdinalIgnoreCase) || isAnimation || isClock;
        bool isStaticText = isText && string.Equals(layer.TypeName, "Text", StringComparison.OrdinalIgnoreCase);
        bool isDataText = isText && !isStaticText;

        XLabel.Content = isClock ? "OFFSET X" : "X";
        YLabel.Content = isClock ? "OFFSET Y" : "Y";
        if (isClock)
        {
            DragHintText.Text = string.Equals(layer.ClockMoveOrigin, "True", StringComparison.OrdinalIgnoreCase)
                ? "Drag to move gauge center"
                : "Drag to move gauge hand";
        }
        else
        {
            DragHintText.Text = GetLanguageText("preview.dragToReposition", "Drag to reposition");
        }

        TextGradientColorBox.Text = NormalizeColorText(layer.FontGradientColor);
        SetComboTag(TextGradientDirectionCombo, string.IsNullOrWhiteSpace(layer.FontGradientDirection) ? "0" : layer.FontGradientDirection);
        FrontAlphaBox.Text = layer.FrontAlpha;
        BackAlphaBox.Text = layer.BackAlpha;
        ChartFillColorBox.Text = NormalizeColorText(layer.FillColor);
        ChartTransparentBox.Text = layer.Transparent;
        TransparentBackgroundCheck.IsChecked = string.Equals(layer.TransparentBackground, "True", StringComparison.OrdinalIgnoreCase);
        InvertDirectionCheck.IsChecked = string.Equals(layer.InvertDirection, "True", StringComparison.OrdinalIgnoreCase);
        RingBorderCheck.IsChecked = string.Equals(layer.RingBorder, "True", StringComparison.OrdinalIgnoreCase);
        RoundCheck.IsChecked = string.Equals(layer.Round, "True", StringComparison.OrdinalIgnoreCase);
        UseBlockCheck.IsChecked = string.Equals(layer.UseBlock, "True", StringComparison.OrdinalIgnoreCase);
        MaxValueBox.Text = layer.MaxValue;
        StartPercentageBox.Text = layer.StartPercentage;
        TotalAngleBox.Text = layer.TotalAngle;

        var textGradientVisibility = isText && layer.CanWriteFont("GrColor") ? Visibility.Visible : Visibility.Collapsed;
        TextGradientColorLabel.Visibility = TextGradientColorPanel.Visibility = textGradientVisibility;
        TextGradientDirectionLabel.Visibility = TextGradientDirectionCombo.Visibility =
            isText && layer.CanWriteFont("GrDirection") ? Visibility.Visible : Visibility.Collapsed;
        UseGradientCheck.Visibility = Visibility.Collapsed;
        GradientColorLabel.Visibility = GraphGradientColorPanel.Visibility = Visibility.Collapsed;
        GraphGradientDirectionLabel.Visibility = GraphGradientDirectionCombo.Visibility = Visibility.Collapsed;
        GradientOptionsCard.Visibility = Visibility.Collapsed;
        AlphaPanel.Visibility = isGraph && (layer.CanWrite("FrontAlpha") || layer.CanWrite("BackAlpha")) ? Visibility.Visible : Visibility.Collapsed;
        ChartColorsPanel.Visibility = layer.CanWrite("FillColor") ? Visibility.Visible : Visibility.Collapsed;
        FrontAlphaBox.Visibility = layer.CanWrite("FrontAlpha") ? Visibility.Visible : Visibility.Collapsed;
        BackAlphaBox.Visibility = layer.CanWrite("BackAlpha") ? Visibility.Visible : Visibility.Collapsed;
        TransparentBackgroundCheck.Visibility = layer.CanWrite("trBack") ? Visibility.Visible : Visibility.Collapsed;
        InvertDirectionCheck.Visibility = layer.CanWrite("rollDirection") ? Visibility.Visible : Visibility.Collapsed;
        RingBorderCheck.Visibility = layer.CanWrite("HasRingBorder") ? Visibility.Visible : Visibility.Collapsed;
        RoundCheck.Visibility = layer.CanWrite("round") ? Visibility.Visible : Visibility.Collapsed;
        UseBlockCheck.Visibility = layer.CanWrite("useBlock") ? Visibility.Visible : Visibility.Collapsed;
        MaxValueLabel.Visibility = MaxValueBox.Visibility = layer.CanWrite("maxValue") ? Visibility.Visible : Visibility.Collapsed;
        StartPercentageLabel.Visibility = StartPercentageBox.Visibility = layer.CanWrite("startPer") ? Visibility.Visible : Visibility.Collapsed;
        TotalAngleLabel.Visibility = TotalAngleBox.Visibility = layer.CanWrite("totalAngel") ? Visibility.Visible : Visibility.Collapsed;
        GraphExtraFlagsPanel.Visibility =
            TransparentBackgroundCheck.Visibility == Visibility.Visible ||
            InvertDirectionCheck.Visibility == Visibility.Visible ||
            RingBorderCheck.Visibility == Visibility.Visible ||
            RoundCheck.Visibility == Visibility.Visible ||
            UseBlockCheck.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        GraphExtraValuesPanel.Visibility =
            MaxValueBox.Visibility == Visibility.Visible ||
            StartPercentageBox.Visibility == Visibility.Visible ||
            TotalAngleBox.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        LayerOptionsCard.Visibility =
            AlphaPanel.Visibility == Visibility.Visible ||
            ChartColorsPanel.Visibility == Visibility.Visible ||
            GraphExtraFlagsPanel.Visibility == Visibility.Visible ||
            GraphExtraValuesPanel.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;

        var textVisibility = isText ? Visibility.Visible : Visibility.Collapsed;
        FontLabel.Visibility = isSensor ? Visibility.Visible : textVisibility;
        FontCombo.Visibility = isSensor ? Visibility.Visible : textVisibility;
        SensorFontColorsPanel.Visibility = isSensor ? Visibility.Visible : Visibility.Collapsed;
        SizeLabel.Visibility = textVisibility;
        SizePanel.Visibility = textVisibility;
        SizeLabel.Content = isText ? GetLanguageText("labels.size", "SIZE") : "W";
        SizeHeightLabel.Visibility = Visibility.Collapsed;
        SizeHeightPanel.Visibility = Visibility.Collapsed;
        ColorLabel.Visibility = isSensor ? Visibility.Visible : textVisibility;
        ColorPanel.Visibility = isSensor ? Visibility.Visible : textVisibility;
        TextAndFormatCard.Visibility =
            (isText || isSensor || isClock) &&
            (FontCombo.Visibility == Visibility.Visible ||
             ColorPanel.Visibility == Visibility.Visible ||
             textVisibility == Visibility.Visible ||
             TextGradientColorPanel.Visibility == Visibility.Visible ||
             TextGradientDirectionCombo.Visibility == Visibility.Visible ||
             SensorFontColorsPanel.Visibility == Visibility.Visible)
                ? Visibility.Visible
                : Visibility.Collapsed;
        if (isSensor)
        {
            ColorLabel.Content = "MAIN TEXT";
            ColorBox.Text = NormalizeColorText(layer.SensorMainFontColor);
            SensorTopColorLabel.Content = "TOP TEXT";
            SensorTopColorBox.Text = NormalizeColorText(string.IsNullOrWhiteSpace(layer.SensorTopFontColor) ? layer.SensorMainFontColor : layer.SensorTopFontColor);
            SensorBottomColorLabel.Content = "BOTTOM TEXT";
            SensorBottomColorBox.Text = NormalizeColorText(string.IsNullOrWhiteSpace(layer.SensorBottomFontColor) ? layer.SensorMainFontColor : layer.SensorBottomFontColor);
            SetComboText(FontCombo, ResolveCanonicalFontName(GetEffectiveLayerFont(layer.SensorFontFamily)));
        }
        SetTextCheck.Visibility = textVisibility;
        TextBox.Visibility = textVisibility;
        TextEditorGrid.Visibility = textVisibility;
        if (isSensor)
        {
            PopulateSensorTypeCombo(DataCombo);
            SetComboText(DataCombo, string.IsNullOrWhiteSpace(layer.SensorType) ? SensorTypeFromDataSource(layer.DataSource) : layer.SensorType);
        }
        else
        {
            PopulateDataSourceCombo(DataCombo, layer.DataSource);
        }
        DataLabel.Visibility = isDataText || isGraph || isClock ? Visibility.Visible : Visibility.Collapsed;
        DataCombo.Visibility = isDataText || isGraph || isClock ? Visibility.Visible : Visibility.Collapsed;
        DataPropertiesCard.Visibility =
            DataCombo.Visibility == Visibility.Visible ||
            FormatPanel.Visibility == Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
        AlignmentLabel.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        AlignmentCombo.Visibility = AlignmentLabel.Visibility;
        FontIntervalLabel.Visibility = isText && layer.CanWriteFont("interval") ? Visibility.Visible : Visibility.Collapsed;
        FontIntervalBox.Visibility = FontIntervalLabel.Visibility;
        BoldCheck.Visibility = textVisibility;
        ItalicCheck.Visibility = isText && layer.CanWriteFont("IsItalic") ? Visibility.Visible : Visibility.Collapsed;
        SetTextCheck.IsChecked = isText &&
                                 (layer.ForceText ||
                                  string.Equals(layer.DataSource, "StaticText", StringComparison.OrdinalIgnoreCase));

        GraphStyleLabel.Visibility = Visibility.Collapsed;
        GraphStyleCombo.Visibility = Visibility.Collapsed;
        var showZoomRateEditor = isSensor || (isImage && !isAnimation);
        ZoomRateLabel.Visibility = showZoomRateEditor ? Visibility.Visible : Visibility.Collapsed;
        ZoomPanel.Visibility = showZoomRateEditor ? Visibility.Visible : Visibility.Collapsed;
        if (isSensor)
        {
            ZoomRateLabel.Content = "SIZE";
            ZoomBox.Text = FormatZoom(GetSensorZoomRate(layer));
        }
        else
        {
            ZoomRateLabel.Content = GetLanguageText("labels.zoomRate", "ZOOM RATE");
        }
        if (isGraph)
        {
            GraphEditPanel.Visibility = Visibility.Visible;
            ImageEditPanel.Visibility = Visibility.Collapsed;
            EditSeparator.Visibility = Visibility.Visible;

            bool isArcGraph = type.Contains("GraphArchBar", StringComparison.OrdinalIgnoreCase);
            bool isStatusBar = type.Contains("GraphStatuBar", StringComparison.OrdinalIgnoreCase);
            bool isDynamicStatus = type.Contains("GraphDynamicBar", StringComparison.OrdinalIgnoreCase);
            bool isChart = type.Contains("GraphLine", StringComparison.OrdinalIgnoreCase);
            if (isSensor)
            {
                WidthLabel.Visibility = WidthPanel.Visibility = WidthSlider.Visibility = Visibility.Collapsed;
                HeightLabel.Visibility = HeightPanel.Visibility = HeightSlider.Visibility = Visibility.Collapsed;
                RadiusLabel.Visibility = RadiusPanel.Visibility = RadiusSlider.Visibility = Visibility.Collapsed;
                DiameterLabel.Visibility = DiameterPanel.Visibility = DiameterSlider.Visibility = Visibility.Collapsed;
                ThicknessLabel.Visibility = ThicknessPanel.Visibility = ThicknessSlider.Visibility = Visibility.Collapsed;
            }
            WidthLabel.Content = GetLanguageText(isStatusBar ? "labels.length" : "labels.width", isStatusBar ? "LENGTH" : "WIDTH");
            RadiusLabel.Content = GetLanguageText(isDynamicStatus || isStatusBar ? "labels.cornerRadius" : "labels.radius", isDynamicStatus || isStatusBar ? "CORNER RADIUS" : "RADIUS");
            GraphLineWidthLabel.Content = GetLanguageText(isDynamicStatus || isArcGraph ? "labels.border" : "labels.lineWidth", isDynamicStatus || isArcGraph ? "BORDER" : "LINE WIDTH");
            GraphInnerCircleRadiusLabel.Content = GetLanguageText(isDynamicStatus ? "labels.sliderRadius" : "labels.innerRadius", isDynamicStatus ? "SLIDER RADIUS" : "INNER RADIUS");
            GraphSplitLabel.Content = GetLanguageText(isArcGraph ? "labels.blockAngleSpacing" : "labels.blockWidthSpacing", isArcGraph ? "BLOCK ANGLE / SPACING" : "BLOCK WIDTH / SPACING");
            GraphBorderWidthLabel.Content = GetLanguageText(isChart ? "labels.border" : "labels.borderWidth", isChart ? "BORDER" : "BORDER WIDTH");
            FrontColorLabel.Content = GetLanguageText(isChart ? "labels.lineColor" : "labels.frontColor", isChart ? "LINE COLOR" : "FRONT COLOR");
            BackColorLabel.Content = GetLanguageText(isChart ? "labels.borderColor" : "labels.borderBackgroundColor", isChart ? "BORDER COLOR" : "BORDER / BG COLOR");
            if (isSensor)
            {
                // Sensor graphs are always rendered by L-Connect as a 400x400 sensor image.
            }
            else if (isArcGraph)
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

            FrontColorBox.Text = NormalizeColorText(isSensor ? layer.SensorColor1 : layer.FrontColor);
            BackColorBox.Text = NormalizeColorText(isSensor ? layer.SensorBgColor : layer.BackColor);
            GradientColorBox.Text = NormalizeColorText(layer.GradientColor);
            UseGradientCheck.IsChecked = string.Equals(layer.UseGradient, "True", StringComparison.OrdinalIgnoreCase);
            SetComboTag(GraphDirectionCombo, layer.Direction);
            SetComboTag(GraphGradientDirectionCombo, layer.Direction);
            GraphLineWidthBox.Text = layer.LineWidth;
            GraphColumnWidthBox.Text = layer.ColumnWidth;
            GraphBorderWidthBox.Text = layer.BorderWidth;
            GraphInnerCircleRadiusBox.Text = layer.InnerCircleRadius;
            GraphSplitBlockWidthBox.Text = layer.SplitBlockWidth;
            GraphSplitBlankWidthBox.Text = layer.SplitBlankWidth;
            PopulateGraphTypeSelectors(layer);
            SetComboText(GraphTypeNameBox, layer.TypeName);
            SetComboText(GraphSubTypeNameBox, isSensor ? (string.IsNullOrWhiteSpace(layer.SensorStyle) ? layer.SubTypeName : layer.SensorStyle) : layer.SubTypeName);
            var useSubsection = string.Equals(layer.UseSubsection, "True", StringComparison.OrdinalIgnoreCase);
            GraphUseSubsectionCheck.IsChecked = useSubsection;
            GraphFillBackCheck.IsChecked = string.Equals(layer.FillBack, "True", StringComparison.OrdinalIgnoreCase);
            GraphRevertCheck.IsChecked = string.Equals(layer.Revert, "True", StringComparison.OrdinalIgnoreCase);

            var frontVisibility = isSensor || layer.CanWrite("FrontColor") || layer.CanWrite("LineColor") || layer.CanWrite("FillColor")
                ? Visibility.Visible
                : Visibility.Collapsed;
            var backVisibility = isSensor || layer.CanWrite("BackColor") || layer.CanWrite("BorderColor")
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (isSensor)
            {
                FrontColorLabel.Content = "RING START";
                SensorRingEndLabel.Content = "RING END";
                SensorRingEndColorBox.Text = NormalizeColorText(layer.SensorColor2);
                BackColorLabel.Content = "TRACK COLOR";
            }
            FrontColorLabel.Visibility = frontVisibility;
            FrontColorBox.Visibility = frontVisibility;
            FrontColorPickButton.Visibility = frontVisibility;
            SensorRingEndLabel.Visibility = SensorRingEndPanel.Visibility = isSensor ? Visibility.Visible : Visibility.Collapsed;
            BackColorLabel.Visibility = backVisibility;
            BackColorBox.Visibility = backVisibility;
            BackColorPickButton.Visibility = backVisibility;
            GradientColorLabel.Visibility = GraphGradientColorPanel.Visibility =
                !isSensor && layer.CanWrite("GradientColor") && !isDynamicStatus ? Visibility.Visible : Visibility.Collapsed;
            UseGradientCheck.Visibility =
                !isSensor && layer.CanWrite("useGradient") && !isChart && !isDynamicStatus ? Visibility.Visible : Visibility.Collapsed;
            GraphGradientDirectionLabel.Visibility = GraphGradientDirectionCombo.Visibility =
                UseGradientCheck.Visibility == Visibility.Visible && layer.CanWrite("direction")
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            GradientOptionsCard.Visibility =
                GradientColorLabel.Visibility == Visibility.Visible ||
                UseGradientCheck.Visibility == Visibility.Visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            GraphDirectionLabel.Visibility = GraphDirectionCombo.Visibility = !isSensor && layer.CanWrite("direction") ? Visibility.Visible : Visibility.Collapsed;
            GraphLineWidthLabel.Visibility = GraphLineWidthBox.Visibility = layer.CanWrite("lineWidth") ? Visibility.Visible : Visibility.Collapsed;
            GraphColumnWidthLabel.Visibility = GraphColumnWidthBox.Visibility = layer.CanWrite("columnWidth") ? Visibility.Visible : Visibility.Collapsed;
            GraphBorderWidthLabel.Visibility = GraphBorderWidthBox.Visibility = layer.CanWrite("borderWidth") ? Visibility.Visible : Visibility.Collapsed;
            GraphInnerCircleRadiusLabel.Visibility = GraphInnerCircleRadiusBox.Visibility = layer.CanWrite("InnerCircleRadius") ? Visibility.Visible : Visibility.Collapsed;
            GraphSplitLabel.Visibility = GraphSplitPanel.Visibility =
                (layer.CanWrite("SplitBlockWidth") || layer.CanWrite("SplitBlankWidth"))
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            GraphSplitBlockWidthBox.Visibility = layer.CanWrite("SplitBlockWidth") ? Visibility.Visible : Visibility.Collapsed;
            GraphSplitBlankWidthBox.Visibility = layer.CanWrite("SplitBlankWidth") ? Visibility.Visible : Visibility.Collapsed;
            GraphTypeNameLabel.Visibility = GraphTypeNameBox.Visibility = Visibility.Collapsed;
            GraphSubTypeNameLabel.Visibility = GraphSubTypeNameBox.Visibility = isSensor ? Visibility.Visible : Visibility.Collapsed;
            if (isSensor)
            {
                GraphSubTypeNameLabel.Content = "RING STYLE";
            }
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
                GraphDirectionCombo.Visibility == Visibility.Visible ||
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
            var rotationText = isClock ? layer.ClockAngle : layer.Rotate;
            var rotation = int.TryParse(rotationText, out var storedRotation) ? storedRotation : 0;
            if (isAnimation && rotation is >= 0 and <= 3)
            {
                rotation *= 90;
            }
            SetComboTag(ImageRotateCombo, rotation.ToString(CultureInfo.InvariantCulture));
            ImageRectBox.Text = layer.Rect;
            ClockSettingsPanel.Visibility = isClock ? Visibility.Visible : Visibility.Collapsed;
            ClockCenterXBox.Text = layer.ClockCenterX;
            ClockCenterYBox.Text = layer.ClockCenterY;
            ClockStartAngleBox.Text = layer.ClockAngle;
            ClockTotalAngleBox.Text = layer.ClockEndAngle;
            ClockOffsetBox.Text = layer.ClockOffset;
            ClockOriginXBox.Text = layer.ClockOriginX;
            ClockOriginYBox.Text = layer.ClockOriginY;
            ClockMoveOriginCheck.IsChecked = string.Equals(layer.ClockMoveOrigin, "True", StringComparison.OrdinalIgnoreCase);
            ClockRevertCheck.IsChecked = string.Equals(layer.Revert, "True", StringComparison.OrdinalIgnoreCase);
            ImageSettingsTitle.Text = isClock ? "Gauge Settings" : GetLanguageText("sections.imageSettings", "Image Settings");
            ImageFileLabel.Content = isClock
                ? "GAUGE NEEDLE"
                : GetLanguageText(isAnimation ? "labels.backgroundFile" : "labels.imageFile", isAnimation ? "BACKGROUND FILE" : "IMAGE FILE");
            var showRotateEditor = isAnimation || layer.CanWrite("rotate") || layer.CanWrite("ration");
            ImageRotateLabel.Visibility = ImageRotateCombo.Visibility = showRotateEditor ? Visibility.Visible : Visibility.Collapsed;
            ImageRectLabel.Visibility = ImageRectBox.Visibility = layer.CanWrite("rect") ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            GraphEditPanel.Visibility = Visibility.Collapsed;
            ImageEditPanel.Visibility = Visibility.Collapsed;
            ClockSettingsPanel.Visibility = Visibility.Collapsed;
            ImageSettingsTitle.Text = GetLanguageText("sections.imageSettings", "Image Settings");
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
        if (!CanDirectApplySelectedDevice())
        {
            ShowDirectApplyUnsupportedMessage();
            return;
        }
        var selectedIndex = layer.Index;
        try
        {
            SetApplyProgress(5, GetLanguageText("status.applyingChanges", "Applying changed layers..."));
            // Commit the visible editor values before any asynchronous template lookup can
            // refresh the sidebar and restore stale values.
            UpdateLayerFromInputs(layer);
            MarkLayerDirty(layer);

            SetBusy(true, GetLanguageText("status.applyingChanges", "Applying changed layers..."));
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var templatePath = target.TemplatePath;
            SetApplyProgress(15, GetLanguageText("status.checkingTemplate", "Checking template..."));
            if (HasTemplateChangedSinceLastLoad(templatePath) &&
                await RefreshIfTemplateStructureChangedAsync(deviceModel, templatePath, selectedIndex))
            {
                SetBusy(false, GetLanguageText("status.templateChangedReloaded", "Template changed; layers reloaded."));
                return;
            }

            var lConnectFontChanged = IsOfflineMode
                ? false
                : await EnsureLConnectFontsInstalledAsync(_dirtyLayers.ToList());
            await ApplyDirtyLayersAsync(
                deviceModel,
                templatePath,
                includePairedLayers: PairCheck.IsChecked == true,
                progress: value => SetApplyProgress(
                    25 + 40.0 * value,
                    GetLanguageText("status.savingLayerChanges", "Saving layer changes...")));

            _editorUndoArmed = false;
            ClearTextPreviewCaches();
            SetApplyProgress(88, IsOfflineMode
                ? GetLanguageText("status.offlineSaved", "Saved to offline copy.")
                : GetLanguageText("status.layerSaved", "Layer saved."));
            if (!IsOfflineMode)
            {
                _ = RefreshLConnectAfterSingleApplyAsync();
            }
            SetApplyProgress(96, GetLanguageText("status.refreshingEditor", "Refreshing editor..."));
            LayerGrid.Items.Refresh();
            SelectLayerByIndex(selectedIndex);
            SetBusy(false, GetLanguageText("status.allChangesApplied", "All changes applied to template."));
            if (lConnectFontChanged)
            {
                MessageBox.Show(
                    this,
                    GetLanguageText(
                        "messages.fontInstallRestartRequired",
                        "The selected font was installed for L-Connect. Please restart L-Connect once so the device renderer can load it."),
                    GetLanguageText("messages.restartTitle", "Restart L-Connect"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.saveFailed", "Save failed."));
            MessageBox.Show(this, ex.Message, GetLanguageText("messages.applyFailed", "Apply failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            HideApplyProgress();
        }
    }

    private async Task RefreshLConnectAfterSingleApplyAsync()
    {
        try
        {
            if (!await TriggerLConnectRefreshAsync(skipUniversalPreviewUpdate: true, fastApply: true))
            {
                SetStatus(GetLanguageText(
                    "status.layerSavedRefreshPending",
                    "Layer was saved. If L-Connect does not update immediately, reopen the template in L-Connect."));
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Background L-Connect refresh after single apply failed.", ex);
            SetStatus(GetLanguageText(
                "status.layerSavedRefreshPending",
                "Layer was saved. If L-Connect does not update immediately, reopen the template in L-Connect."));
        }
    }

    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        await RemoveSelectedLayersAsync();
    }

    private async void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        await DuplicateSelectedLayersAsync();
    }

    private async Task DuplicateSelectedLayersAsync()
    {
        var selected = GetSelectedLayers(includeLocked: true, includeAnimation: false)
            .Where(layer => !string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (selected.Count == 0) return;
        var firstIndex = selected[0].Index;
        try
        {
            SetBusy(true, selected.Count == 1
                ? GetLanguageText("status.duplicatingLayer", "Duplicating layer...")
                : $"Duplicating {selected.Count} layers...");
            var target = await ResolveTemplateTargetAsync();
            if (await RefreshIfTemplateStructureChangedAsync(target.DeviceModel, target.TemplatePath, firstIndex))
            {
                return;
            }

            foreach (var layer in selected.OrderByDescending(layer => int.TryParse(layer.Index, out var index) ? index : -1))
            {
                await Task.Run(() => _supporter.DuplicateLayerAsync(
                    target.DeviceModel, target.TemplatePath, layer.Index));
            }

            await LoadLayersAsync(true);
            SelectNewestEditableLayer();
            SetBusy(false, selected.Count == 1
                ? GetLanguageText("status.layerDuplicated", "Layer duplicated.")
                : "Layers duplicated.");
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.duplicateFailed", "Duplicate failed."));
            MessageBox.Show(this, ex.Message, GetLanguageText("messages.duplicateFailed", "Duplicate failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
            SetBusy(true, GetLanguageText("status.removingLayers", "Removing layer(s)..."));
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
            SetBusy(false, GetLanguageText("status.layerRemoved", "Layer removed."));
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.removeFailed", "Remove failed."));
            MessageBox.Show(this, ex.Message, GetLanguageText("messages.removeFailed", "Remove failed"), MessageBoxButton.OK, MessageBoxImage.Error);
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
        if (targetLayer?.IsEditorMetadata == true ||
            string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetLayer?.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, GetLanguageText("messages.backgroundCannotMove", "Background animation layer cannot be reordered."), GetLanguageText("messages.moveFailed", "Move failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            SetBusy(true, FormatLanguageText("status.movingLayerDirection", "Moving layer {0}...", direction.ToLowerInvariant()));
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

            SetBusy(false, FormatLanguageText("status.layerMovedDirection", "Layer moved {0}.", direction.ToLowerInvariant()));
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.moveFailed", "Move failed."));
            MessageBox.Show(this, ex.Message, GetLanguageText("messages.moveFailed", "Move failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task MoveSelectedLayersOneStepAsync(string direction)
    {
        var selected = GetSelectedLayers(includeLocked: true, includeAnimation: false)
            .Select(layer => new { Layer = layer, Index = int.TryParse(layer.Index, out var index) ? index : -1 })
            .Where(item => item.Index >= 0)
            .ToList();
        if (selected.Count == 0) return;

        var ordered = direction.Equals("Down", StringComparison.OrdinalIgnoreCase)
            ? selected.OrderByDescending(item => item.Index).ToList()
            : selected.OrderBy(item => item.Index).ToList();

        try
        {
            SetBusy(true, direction.Equals("Down", StringComparison.OrdinalIgnoreCase)
                ? "Sending selected layer(s) backward..."
                : "Bringing selected layer(s) forward...");
            var target = await ResolveTemplateTargetAsync();
            if (await RefreshIfTemplateStructureChangedAsync(target.DeviceModel, target.TemplatePath, ordered[0].Layer.Index))
            {
                return;
            }

            foreach (var item in ordered)
            {
                var currentIndex = int.TryParse(item.Layer.Index, out var liveIndex) ? liveIndex : item.Index;
                var targetIndex = direction.Equals("Down", StringComparison.OrdinalIgnoreCase)
                    ? currentIndex + 1
                    : currentIndex - 1;
                if (targetIndex < 0 || targetIndex >= Layers.Count)
                {
                    continue;
                }

                var neighbor = Layers.FirstOrDefault(layer => layer.Index == targetIndex.ToString());
                if (neighbor?.IsEditorMetadata == true ||
                    string.Equals(neighbor?.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await Task.Run(() => _supporter.MoveLayerAsync(
                    target.DeviceModel,
                    target.TemplatePath,
                    currentIndex.ToString(),
                    direction));
                SwapShadowLinksForLayerMove(currentIndex, targetIndex);
            }

            SaveShadowLinks();
            var selectedIndexes = selected
                .Select(item => direction.Equals("Down", StringComparison.OrdinalIgnoreCase) ? item.Index + 1 : item.Index - 1)
                .Where(index => index >= 0)
                .ToHashSet();
            await LoadLayersAsync(true);
            LayerGrid.SelectedItems.Clear();
            foreach (var layer in Layers.Where(layer => int.TryParse(layer.Index, out var index) && selectedIndexes.Contains(index)))
            {
                LayerGrid.SelectedItems.Add(layer);
            }
            PopulateEditorFromSelection();
            SetBusy(false, "Layer order updated.");
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.moveFailed", "Move failed."));
            MessageBox.Show(this, ex.Message, GetLanguageText("messages.moveFailed", "Move failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddTextButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AddTextBox.Text)) return;
        try
        {
            CapturePendingDirtyLayersBeforeAdd();
            SetBusy(true, GetLanguageText("status.addingText", "Adding text..."));
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var templatePath = target.TemplatePath;
            var text = NormalizeLConnectText(AddTextBox.Text);
            var x = AddXBox.Text;
            var y = AddYBox.Text;
            var size = AddSizeBox.Text;
            var color = AddColorBox.Text;
            var font = ResolveCanonicalFontName(GetComboText(AddFontCombo));
            var bold = AddBoldCheck.IsChecked == true;
            await Task.Run(() => _supporter.AddTextAsync(deviceModel, templatePath, text, x, y, size, color, font, bold));
            
            await FinalizeAddedLayerAsync();
            SetBusy(false, GetLanguageText("status.textLayerAdded", "Text layer added."));
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.addFailed", "Add failed."));
            MessageBox.Show(this, ex.Message, GetLanguageText("messages.addTextFailed", "Add text failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddDataButton_Click(object sender, RoutedEventArgs e)
    {
        var data = GetComboText(AddDataCombo);
        if (string.IsNullOrWhiteSpace(data)) return;
        try
        {
            CapturePendingDirtyLayersBeforeAdd();
            SetBusy(true, GetLanguageText("status.addingData", "Adding data..."));
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var templatePath = target.TemplatePath;
            var x = AddXBox.Text;
            var y = AddYBox.Text;
            var size = AddSizeBox.Text;
            var color = AddColorBox.Text;
            var font = ResolveCanonicalFontName(GetComboText(AddFontCombo));
            var bold = AddBoldCheck.IsChecked == true;

            var format = "";
            if (AddFormatCombo.Visibility == Visibility.Visible)
            {
                format = NormalizeFormatForDataSource(data, GetComboValue(AddFormatCombo));
            }

            await Task.Run(() => _supporter.AddDataAsync(deviceModel, templatePath, data, x, y, size, color, font, bold, format));
            
            await FinalizeAddedLayerAsync();
            SetBusy(false, GetLanguageText("status.dataLayerAdded", "Data layer added."));
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.addFailed", "Add failed."));
            MessageBox.Show(this, ex.Message, GetLanguageText("messages.addDataFailed", "Add data failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddImageButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedType = (AddLayerTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Image";
        var dialog = new OpenFileDialog
        {
            Title = GetLanguageText("dialogs.chooseImage", "Choose LCD image"),
            Filter = GetLanguageText("dialogs.imageFilter", "Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All files|*.*")
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            CapturePendingDirtyLayersBeforeAdd();
            var isClock = string.Equals(selectedType, "Gauge", StringComparison.OrdinalIgnoreCase);
            SetBusy(true, isClock ? "Adding gauge layer..." : GetLanguageText("status.addingImageLayer", "Adding image layer..."));
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var templatePath = target.TemplatePath;
            var imagePath = dialog.FileName;
            var placement = GetImagePlacement(imagePath, AddSizeBox.Text, AddXBox.Text, AddYBox.Text);
            var clockX = int.TryParse(AddXBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedX) ? parsedX : (int)(_templateCanvasWidth / 2);
            var clockY = int.TryParse(AddYBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedY) ? parsedY : (int)(_templateCanvasHeight / 2);
            var x = (isClock ? clockX : placement.X).ToString(CultureInfo.InvariantCulture);
            var y = (isClock ? clockY : placement.Y).ToString(CultureInfo.InvariantCulture);
            var size = placement.Width.ToString(CultureInfo.InvariantCulture);

            if (isClock)
            {
                var dataSource = GetComboText(AddDataCombo);
                if (string.IsNullOrWhiteSpace(dataSource)) dataSource = "TIME";
                var format = dataSource.Equals("TIME", StringComparison.OrdinalIgnoreCase)
                    ? (string.IsNullOrWhiteSpace(GetComboText(AddFormatCombo)) ? "h_12" : GetComboText(AddFormatCombo))
                    : "";
                await Task.Run(() => _supporter.AddClockAsync(deviceModel, templatePath, imagePath, dataSource, x, y, size, format));
            }
            else
            {
                await Task.Run(() => _supporter.AddImageAsync(deviceModel, templatePath, imagePath, x, y, size));
            }
            
            await FinalizeAddedLayerAsync();
            SetBusy(false, isClock ? "Gauge layer added." : GetLanguageText("status.imageLayerAdded", "Image layer added."));
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.addFailed", "Add failed."));
            MessageBox.Show(this, ex.Message, GetLanguageText("messages.addImageFailed", "Add image failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddGraphButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedType = (AddLayerTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        if (selectedType == "Animation")
        {
            await ChooseAndSetBackgroundAsync();
            return;
        }
        var styleCode = selectedType switch
        {
            "StatusBar" => "MOD::H2_Bar_chart_1.modular::GraphStatuBar",
            "DynamicStatus" => "DynamicStatus",
            "CurvedBar" => "MOD::H2_Donut chart_1.modular::GraphArchBar",
            "Chart" => "MOD::H2_Stream Chart_1.modular::GraphLine",
            "RingGraph" => GetComboValue(AddGraphStyleCombo),
            _ => ""
        };
        if (string.IsNullOrWhiteSpace(styleCode)) return;

        try
        {
            CapturePendingDirtyLayersBeforeAdd();
            SetBusy(true, GetLanguageText("status.addingGraph", "Adding graph..."));
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var templatePath = target.TemplatePath;
            var data = GetComboText(AddDataCombo);
            var x = AddXBox.Text;
            var y = AddYBox.Text;
            var size = AddSizeBox.Text;
            var color = AddColorBox.Text;
            var font = ResolveCanonicalFontName(GetComboText(AddFontCombo));
            if (selectedType == "RingGraph")
            {
                await Task.Run(() => _supporter.AddSensorAsync(
                    deviceModel,
                    templatePath,
                    string.IsNullOrWhiteSpace(styleCode) ? "Ring2" : styleCode,
                    string.IsNullOrWhiteSpace(data) ? "CPULoad" : data,
                    x,
                    y,
                    string.IsNullOrWhiteSpace(size) ? "1.0" : size,
                    color,
                    "#00FFEE",
                    "#202020",
                    "#FFFFFF",
                    font));
            }
            else
            {
                await Task.Run(() => _supporter.AddGraphAsync(deviceModel, templatePath, styleCode, data, x, y, size, color, "#20FFFFFF"));
            }
            
            await FinalizeAddedLayerAsync();
            SetBusy(false, FormatLanguageText("status.graphLayerAdded", "{0} layer added.", selectedType));
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.addFailed", "Add failed."));
            MessageBox.Show(this, ex.Message, GetLanguageText("messages.addGraphFailed", "Add graph failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        await ChooseAndSetBackgroundAsync();
    }

    private async Task ChooseAndSetBackgroundAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = GetLanguageText("dialogs.chooseBackgroundMedia", "Choose LCD background media"),
            Filter = GetLanguageText("dialogs.mediaFilter", "Media files (*.mp4;*.gif;*.jpg;*.jpeg;*.h264)|*.mp4;*.gif;*.jpg;*.jpeg;*.h264|All files (*.*)|*.*")
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            SetBusy(true, GetLanguageText("status.backgroundPreparing", "Preparing background media..."));
            
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var templatePath = target.TemplatePath;
            var mediaPath = dialog.FileName;
            var templateIdBeforeBackgroundChange = _currentTemplateId;

            _selectedBackgroundSourcePath = mediaPath;
            _currentBackgroundPath = mediaPath;
            LoadBackgroundPreview(mediaPath, Path.GetFileName(mediaPath));
            RequestPreviewDraw();

            await RevertTemplateBackgroundAsync();
            await Task.Delay(900);

            var backgroundOperationStartedUtc = DateTime.UtcNow;
            var stagedMediaPath = CreateShortBackgroundStagingPath(mediaPath);
            string generatedBackgroundPath;
            try
            {
                using var backgroundCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                var canvas = GetTemplateCanvasPixels();
                generatedBackgroundPath = await Task.Run(() => _supporter.SetBackgroundMediaAsync(
                    deviceModel,
                    templatePath,
                    stagedMediaPath,
                    canvas.Width,
                    canvas.Height,
                    backgroundCts.Token));
            }
            finally
            {
                TryDeleteFile(stagedMediaPath);
            }

            _backgroundDirty = true;
            if (string.IsNullOrWhiteSpace(generatedBackgroundPath))
            {
                generatedBackgroundPath = ResolveUploadedBackgroundPath(
                    deviceModel, _currentTemplateId);
                if (string.IsNullOrWhiteSpace(generatedBackgroundPath) ||
                    !File.Exists(generatedBackgroundPath) ||
                    File.GetLastWriteTimeUtc(generatedBackgroundPath) <
                    backgroundOperationStartedUtc.AddSeconds(-1))
                {
                    throw new InvalidOperationException(
                        "L-Connect background conversion did not return a newly generated media file.");
                }
            }

            var backgroundForLConnect = await WaitForUploadedBackgroundReadyAsync(
                generatedBackgroundPath,
                TimeSpan.FromSeconds(15));

            // Update the embedded/card preview before asking L-Connect to activate the
            // background. UpdateThemePreview serializes the template, so doing it after
            // ChangeTemplateBackground can invalidate the state L-Connect just loaded.
            var previewFramePath = await CreateDeterministicBackgroundPreviewAsync(backgroundForLConnect);
            try
            {
                var previewSource = string.IsNullOrWhiteSpace(previewFramePath)
                    ? mediaPath
                    : previewFramePath;
                LoadBackgroundPreview(previewSource, Path.GetFileName(previewSource));
                DrawPreview();
                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        DrawPreview();
                        PreviewSurface.UpdateLayout();
                    },
                    System.Windows.Threading.DispatcherPriority.Render);
                await SaveAndApplyThemePreviewAsync(
                    deviceModel,
                    templatePath,
                    templateIdBeforeBackgroundChange,
                    GetTemplatePreviewAliases(templateIdBeforeBackgroundChange));
            }
            finally
            {
                TryDeleteFile(previewFramePath);
            }

            var accepted = IsOfflineMode ||
                await TriggerLConnectBackgroundChangeAsync(
                    backgroundForLConnect,
                    templateIdBeforeBackgroundChange);
            if (!accepted && !IsOfflineMode)
            {
                accepted = await TriggerLConnectRefreshAsync();
            }

            if (accepted)
            {
                _backgroundDirty = false;
            }

            _selectedBackgroundSourcePath = mediaPath;
            _currentBackgroundPath = backgroundForLConnect;
            var animationLayer = Layers.FirstOrDefault(layer =>
                string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase));
            if (animationLayer != null)
            {
                animationLayer.Media = Path.GetFileName(mediaPath);
            }
            LoadBackgroundPreview(backgroundForLConnect, Path.GetFileName(backgroundForLConnect));
            RequestPreviewDraw();

            SetBusy(false, GetLanguageText("status.backgroundChanged", "Background media changed."));
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.backgroundChangeFailed", "Failed to change background."));
            MessageBox.Show(this, ex.Message, GetLanguageText("messages.backgroundFailed", "Background failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string ResolveUploadedBackgroundPath(
        string deviceModel,
        string templateId)
    {
        if (string.IsNullOrWhiteSpace(deviceModel) || string.IsNullOrWhiteSpace(templateId))
        {
            return "";
        }

        var safeTemplateId = Regex.Replace(templateId, @"[^A-Za-z0-9_.-]", "_");
        var uploadRoot = Path.Combine(
            @"C:\ProgramData\Lian-Li\L-Connect 3\uploaded",
            deviceModel,
            "template-background");
        if (!Directory.Exists(uploadRoot))
        {
            return "";
        }

        // Sync-UploadedBackgroundMedia always stores the profile reference as MP4,
        // even when the user selected H264 or a still image.
        const string preferredExtension = ".mp4";
        var fixedPath = Path.Combine(uploadRoot, $"{safeTemplateId}{preferredExtension}");
        if (File.Exists(fixedPath))
        {
            return fixedPath;
        }

        const string alternateExtension = ".h264";
        var alternatePath = Path.Combine(uploadRoot, $"{safeTemplateId}{alternateExtension}");
        if (File.Exists(alternatePath))
        {
            return alternatePath;
        }

        return Directory.EnumerateFiles(uploadRoot, $"{safeTemplateId}-*.*")
            .Where(path =>
                Path.GetExtension(path).Equals(preferredExtension, StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(alternateExtension, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault() ?? "";
    }

    private static async Task<string> WaitForUploadedBackgroundReadyAsync(
        string generatedPath,
        TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(generatedPath))
        {
            throw new InvalidOperationException(
                "L-Connect background conversion did not return an output path.");
        }

        var mp4Path = Path.ChangeExtension(generatedPath, ".mp4");
        var h264Path = Path.ChangeExtension(generatedPath, ".h264");
        var deadline = DateTime.UtcNow + timeout;
        long previousMp4Size = -1;
        long previousH264Size = -1;
        var stableSamples = 0;

        while (DateTime.UtcNow < deadline)
        {
            var mp4Size = File.Exists(mp4Path) ? new FileInfo(mp4Path).Length : 0;
            var h264Size = File.Exists(h264Path) ? new FileInfo(h264Path).Length : 0;

            if (mp4Size > 0 &&
                h264Size > 0 &&
                mp4Size == previousMp4Size &&
                h264Size == previousH264Size)
            {
                stableSamples++;
                if (stableSamples >= 3)
                {
                    return mp4Path;
                }
            }
            else
            {
                stableSamples = 0;
            }

            previousMp4Size = mp4Size;
            previousH264Size = h264Size;
            await Task.Delay(150);
        }

        throw new InvalidOperationException(
            "The converted L-Connect background files were not ready in time. Please try the upload again.");
    }

    private async Task WaitForBackgroundPreviewReadyAsync()
    {
        await Dispatcher.InvokeAsync(
            () => PreviewSurface.UpdateLayout(),
            System.Windows.Threading.DispatcherPriority.Render);

        if (BackgroundImage.Visibility == Visibility.Visible &&
            BackgroundImage.Source != null)
        {
            return;
        }

        if (BackgroundMedia.Visibility != Visibility.Visible ||
            BackgroundMedia.Source == null)
        {
            return;
        }

        if (BackgroundMedia.NaturalVideoWidth <= 0)
        {
            var opened = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            RoutedEventHandler? openedHandler = null;
            EventHandler<ExceptionRoutedEventArgs>? failedHandler = null;
            openedHandler = (_, _) => opened.TrySetResult(true);
            failedHandler = (_, _) => opened.TrySetResult(false);
            BackgroundMedia.MediaOpened += openedHandler;
            BackgroundMedia.MediaFailed += failedHandler;
            try
            {
                await Task.WhenAny(opened.Task, Task.Delay(3000));
            }
            finally
            {
                BackgroundMedia.MediaOpened -= openedHandler;
                BackgroundMedia.MediaFailed -= failedHandler;
            }
        }

        try
        {
            BackgroundMedia.Position = TimeSpan.FromMilliseconds(120);
            BackgroundMedia.Play();
        }
        catch
        {
        }

        await Task.Delay(250);
        await Dispatcher.InvokeAsync(
            () => PreviewSurface.UpdateLayout(),
            System.Windows.Threading.DispatcherPriority.Render);
    }

    private async void ApplyAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanDirectApplySelectedDevice())
        {
            ShowDirectApplyUnsupportedMessage();
            return;
        }

        try
        {
            SetApplyProgress(5, GetLanguageText("status.applyingChanges", "Applying changed layers..."));
            var selectedLayer = LayerGrid.SelectedItem as LayerRow;
            var selectedIndex = selectedLayer?.Index ?? "";
            if (selectedLayer != null && _dirtyLayers.Contains(selectedLayer))
            {
                // Capture pending text-box/selector values before resolving the active
                // template, which may otherwise repopulate the editor with disk values.
                UpdateLayerFromInputs(selectedLayer);
            }

            SetBusy(true, GetLanguageText("status.applyingChanges", "Applying changed layers..."));
            var target = await ResolveTemplateTargetAsync();
            var deviceModel = target.DeviceModel;
            var templatePath = target.TemplatePath;
            SetApplyProgress(15, GetLanguageText("status.checkingTemplate", "Checking template..."));
            IEnumerable<LayerRow> fontCheckLayers = _dirtyLayers.Count > 0
                ? _dirtyLayers.ToList()
                : selectedLayer is null
                    ? Array.Empty<LayerRow>()
                    : new[] { selectedLayer };
            var lConnectFontChanged = IsOfflineMode
                ? false
                : await EnsureLConnectFontsInstalledAsync(fontCheckLayers);
            if (HasTemplateChangedSinceLastLoad(templatePath) &&
                await RefreshIfTemplateStructureChangedAsync(deviceModel, templatePath, selectedIndex))
            {
                SetBusy(false, GetLanguageText("status.templateChangedReloaded", "Template changed; layers reloaded."));
                return;
            }

            await ApplyDirtyLayersAsync(
                deviceModel,
                templatePath,
                includePairedLayers: PairCheck.IsChecked == true,
                progress: value => SetApplyProgress(
                    20 + 45.0 * value,
                    GetLanguageText("status.savingLayerChanges", "Saving layer changes...")));

            SetBusy(true, IsOfflineMode
                ? GetLanguageText("status.offlineSaved", "Saved to offline copy.")
                : GetLanguageText("status.sendingApplyAll", "Sending Apply All..."));
            SetApplyProgress(88, IsOfflineMode
                ? GetLanguageText("status.offlineSaved", "Saved to offline copy.")
                : GetLanguageText("status.sendingApplyAll", "Sending Apply All..."));
            if (!IsOfflineMode && !await TriggerLConnectRefreshAsync(skipUniversalPreviewUpdate: true, fastApply: true))
            {
                SetStatus(GetLanguageText(
                    "status.applyAllSavedRefreshPending",
                    "Changes were saved. If L-Connect does not update immediately, reopen the template in L-Connect."));
            }
            SetApplyProgress(96, GetLanguageText("status.refreshingEditor", "Refreshing editor..."));
            LayerGrid.Items.Refresh();
            if (!string.IsNullOrWhiteSpace(selectedIndex))
            {
                SelectLayerByIndex(selectedIndex);
            }
            SetBusy(false, GetLanguageText("status.allChangesApplied", "All changes applied to template."));
            if (lConnectFontChanged)
            {
                MessageBox.Show(
                    this,
                    GetLanguageText(
                        "messages.fontInstallRestartRequired",
                        "The selected font was installed for L-Connect. Please restart L-Connect once so the device renderer can load it."),
                    GetLanguageText("messages.restartTitle", "Restart L-Connect"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.applyAllFailed", "Apply All failed."));
            MessageBox.Show(this, ex.Message, GetLanguageText("messages.applyAllFailed", "Apply All failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            HideApplyProgress();
        }
    }

    private bool ConfirmApplyAllChangedLayers(IReadOnlyList<LayerRow> dirtyList)
    {
        if (dirtyList.Count == 0)
        {
            return true;
        }

        var lines = dirtyList
            .OrderBy(layer => int.TryParse(layer.Index, out var index) ? index : int.MaxValue)
            .Take(12)
            .Select(layer => $"#{layer.Index} {GetLayerDisplayType(layer)} {GetLayerSummary(layer)}")
            .ToList();
        if (dirtyList.Count > lines.Count)
        {
            lines.Add($"...and {dirtyList.Count - lines.Count} more");
        }

        var message = "Apply these changed layers?\n\n" + string.Join("\n", lines);
        return MessageBox.Show(
            this,
            message,
            "Apply All",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private async Task ApplyDirtyLayersAsync(
        string deviceModel,
        string templatePath,
        bool includePairedLayers = false,
        Action<double>? progress = null)
    {
        if (_dirtyLayers.Count == 0)
        {
            progress?.Invoke(1);
            return;
        }

        if (LayerGrid.SelectedItem is LayerRow selected && _dirtyLayers.Contains(selected))
        {
            UpdateLayerFromInputs(selected);
        }

        var validIndexes = Layers
            .Where(item => !item.IsEditorMetadata)
            .Select(item => item.Index)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _dirtyLayers.RemoveWhere(item =>
            !Layers.Contains(item) ||
            !validIndexes.Contains(item.Index) ||
            (int.TryParse(item.Index, out var index) && index >= Layers.Count));

        var dirtyList = _dirtyLayers
            .OrderBy(item => int.TryParse(item.Index, out var index) ? index : int.MaxValue)
            .ToList();
        if (dirtyList.Count == 0)
        {
            progress?.Invoke(1);
            return;
        }

        var layersToApply = new List<LayerRow>(dirtyList);
        if (includePairedLayers)
        {
            foreach (var layer in dirtyList)
            {
                var paired = FindPairedLayer(layer);
                if (paired == null) continue;
                SyncShadowProperties(layer, paired);
                if (!layersToApply.Contains(paired))
                {
                    layersToApply.Add(paired);
                }
            }
        }

        await _supporter.ApplyLayersAsync(deviceModel, templatePath, layersToApply);
        _currentTemplateWriteStampUtc = GetTemplateWriteStampUtc(templatePath);

        foreach (var layer in layersToApply)
        {
            layer.OriginalGraphStyle = layer.GraphStyle;
            layer.OriginalDataSource = layer.DataSource;
            layer.IsDirty = false;
            _dirtyLayers.Remove(layer);
        }

        progress?.Invoke(1);
    }

    private static string GetLayerSummary(LayerRow layer)
    {
        var summary = !string.IsNullOrWhiteSpace(layer.DataSource)
            ? layer.DataSource
            : !string.IsNullOrWhiteSpace(layer.Text)
                ? layer.Text
                : layer.Media;
        return string.IsNullOrWhiteSpace(summary) ? "" : $"- {summary}";
    }

    private static string NormalizeLConnectText(string value) =>
        Regex.Replace((value ?? "").Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' '), @"\s{2,}", " ").Trim();

    private static bool NormalizeLayerTextForDevice(LayerRow layer)
    {
        if (!string.Equals(layer.Type, "GraphItem", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(layer.Text))
        {
            return false;
        }

        var normalized = NormalizeLConnectText(layer.Text);
        if (string.Equals(layer.Text, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        layer.Text = normalized;
        return true;
    }

    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, GetLanguageText("messages.restartConfirm", "Close L-Connect, restart its services, and open it again?"), GetLanguageText("messages.restartTitle", "Restart L-Connect"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            SetBusy(true, GetLanguageText("status.restartingLConnect", "Restarting L-Connect..."));

            var success = await Task.Run(RestartLConnectStack);

            if (!string.IsNullOrWhiteSpace(_currentBackgroundPath))
            {
                var deviceModel = GetSelectedDeviceModel();
                var templatePath = _currentTemplatePath;
                try
                {
                    var canvas = GetTemplateCanvasPixels();
                    await Task.Run(() => _supporter.SetBackgroundMediaAsync(
                        deviceModel,
                        templatePath,
                        _currentBackgroundPath,
                        canvas.Width,
                        canvas.Height));
                }
                catch (Exception ex) { AppLogger.Error("Saved shadow links could not be read.", ex); }
            }

            var appPath = @"C:\Program Files\Lian-Li\L-Connect 3\L-Connect 3.exe";
            if (System.IO.File.Exists(appPath))
            {
                Process.Start(new ProcessStartInfo(appPath) { UseShellExecute = true });
            }

            _backgroundDirty = false;

            if (!success)
            {
                SetStatus(GetLanguageText("status.lConnectServiceNeedsAdmin", "L-Connect service restart needs Administrator."));
            }
            else
            {
            SetStatus(GetLanguageText("status.lConnectRestarted", "L-Connect restarted."));
            }
            SetBusy(false, GetLanguageText("status.lConnectRestarted", "L-Connect services restarted."));
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.restartFailed", "Restart failed."));
            MessageBox.Show(this, ex.Message, GetLanguageText("messages.restartFailed", "Restart failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool RestartLConnectStack()
    {
        var success = true;
        foreach (var serviceName in new[] { "LConnectServiceWatcher", "LConnectService" })
        {
            success &= TryStopService(serviceName, TimeSpan.FromSeconds(15));
        }

        foreach (var processName in new[] { "L-Connect 3", "L-Connect Editor", "CefSharp.BrowserSubprocess" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch
                {
                    success = false;
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        foreach (var serviceName in new[] { "LConnectService", "LConnectServiceWatcher" })
        {
            success &= TryStartService(serviceName, TimeSpan.FromSeconds(15));
        }

        return success;
    }

    private static bool TryStopService(string serviceName, TimeSpan timeout)
    {
        var state = QueryWindowsServiceState(serviceName);
        if (state == 1) return true;
        if (!RunWindowsServiceCommand("stop", serviceName)) return false;
        return WaitForWindowsServiceState(serviceName, 1, timeout);
    }

    private static bool TryStartService(string serviceName, TimeSpan timeout)
    {
        var state = QueryWindowsServiceState(serviceName);
        if (state == 4) return true;
        if (!RunWindowsServiceCommand("start", serviceName)) return false;
        return WaitForWindowsServiceState(serviceName, 4, timeout);
    }

    private static bool RunWindowsServiceCommand(string command, string serviceName)
    {
        try
        {
            var scPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "sc.exe");
            var startInfo = new ProcessStartInfo
            {
                FileName = scPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add(command);
            startInfo.ArgumentList.Add(serviceName);
            using var process = Process.Start(startInfo);
            if (process == null) return false;
            process.WaitForExit(10_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool WaitForWindowsServiceState(string serviceName, int expectedState, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (QueryWindowsServiceState(serviceName) == expectedState) return true;
            Thread.Sleep(250);
        }

        return QueryWindowsServiceState(serviceName) == expectedState;
    }

    private static int QueryWindowsServiceState(string serviceName)
    {
        try
        {
            var scPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "sc.exe");
            var startInfo = new ProcessStartInfo
            {
                FileName = scPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("query");
            startInfo.ArgumentList.Add(serviceName);
            using var process = Process.Start(startInfo);
            if (process == null) return 0;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5_000);
            var match = Regex.Match(output, @"(?im)^\s*STATE\s*:\s*(\d+)");
            return match.Success && int.TryParse(match.Groups[1].Value, out var state)
                ? state
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private void DrawPreview()
    {
        PreviewCanvas.Children.Clear();
        _previewLayerVisuals.Clear();
        _previewSelectionVisuals.Clear();
        _previewClockCenterMarkers.Clear();
        _previewResizeHandle = null;
        var selected = LayerGrid.SelectedItem as LayerRow;
        var soloLayers = _soloSelectedLayers
            ? LayerGrid.SelectedItems.OfType<LayerRow>().ToHashSet()
            : new HashSet<LayerRow>();
        var animationLayer = Layers.FirstOrDefault(layer =>
            string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase));
        var backgroundHidden = string.Equals(animationLayer?.Hide, "True", StringComparison.OrdinalIgnoreCase);
        var backgroundSoloHidden = _soloSelectedLayers && (animationLayer == null || !soloLayers.Contains(animationLayer));
        BackgroundMedia.Opacity = backgroundHidden || backgroundSoloHidden ? 0 : 1;
        BackgroundImage.Opacity = backgroundHidden || backgroundSoloHidden ? 0 : 1;
        if (animationLayer != null)
        {
            var zoom = TryParseZoom(animationLayer.ZoomRate, out var parsedZoom) && parsedZoom > 0
                ? parsedZoom
                : 1.0;
            var rotation = double.TryParse(animationLayer.Rotate, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRotation)
                ? parsedRotation is >= 0 and <= 3 ? parsedRotation * 90 : parsedRotation
                : 0;
            var transform = new TransformGroup();
            transform.Children.Add(new ScaleTransform(zoom, zoom));
            transform.Children.Add(new RotateTransform(rotation));
            BackgroundMedia.RenderTransformOrigin = BackgroundImage.RenderTransformOrigin = new Point(0.5, 0.5);
            BackgroundMedia.RenderTransform = transform;
            BackgroundImage.RenderTransform = transform.Clone();
        }
        else
        {
            BackgroundMedia.RenderTransform = Transform.Identity;
            BackgroundImage.RenderTransform = Transform.Identity;
        }
        DrawAlignmentGuides(selected);
        var zIndex = 10;
        foreach (var layer in Layers)
        {
            if (layer.IsEditorMetadata)
            {
                continue;
            }
            if (string.Equals(layer.Hide, "True", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (_soloSelectedLayers && !soloLayers.Contains(layer))
            {
                continue;
            }
            if (string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var bounds = GetLayerBounds(layer);
            var visual = CreateLayerPreviewVisual(layer, bounds, layer == selected);
            ConfigurePreviewLayerVisual(layer, visual);
            Canvas.SetLeft(visual, bounds.Left);
            Canvas.SetTop(visual, bounds.Top);
            Canvas.SetZIndex(visual, zIndex++);
            PreviewCanvas.Children.Add(visual);
            _previewLayerVisuals[layer] = visual;
        }

        foreach (var selectedLayer in LayerGrid.SelectedItems.OfType<LayerRow>())
        {
            if (string.Equals(selectedLayer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(selectedLayer.Hide, "True", StringComparison.OrdinalIgnoreCase)) continue;
            if (_soloSelectedLayers && !soloLayers.Contains(selectedLayer)) continue;
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
            Canvas.SetZIndex(selectionBorder, 1000);
            PreviewCanvas.Children.Add(selectionBorder);
            _previewSelectionVisuals[selectedLayer] = selectionBorder;

            if (string.Equals(selectedLayer.Type, "GraphClock", StringComparison.OrdinalIgnoreCase))
            {
                var center = GetClockCenterPreviewPoint(selectedLayer);
                var centerMarker = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = Brushes.Red,
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(centerMarker, center.X - centerMarker.Width / 2.0);
                Canvas.SetTop(centerMarker, center.Y - centerMarker.Height / 2.0);
                Canvas.SetZIndex(centerMarker, 1002);
                PreviewCanvas.Children.Add(centerMarker);
                _previewClockCenterMarkers[selectedLayer] = centerMarker;
            }
        }

        if (selected != null)
        {
            var bounds = GetLayerSelectionBounds(selected);
            var type = selected.Type ?? "";
            bool isAnimation = string.Equals(type, "GraphAnimation", StringComparison.OrdinalIgnoreCase);
            if (!isAnimation && !selected.IsLocked &&
                !string.Equals(selected.Hide, "True", StringComparison.OrdinalIgnoreCase))
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
                Canvas.SetZIndex(resizeHandle, 1001);
                PreviewCanvas.Children.Add(resizeHandle);
                _previewResizeHandle = resizeHandle;
            }
        }

        if (GetSelectedDeviceModel() == "hydroshift-ii-lcd-c")
        {
            var radius = Math.Min(_previewCanvasWidth, _previewCanvasHeight) / 2.0;
            PreviewSurface.Clip = new EllipseGeometry(
                new Point(_previewCanvasWidth / 2.0, _previewCanvasHeight / 2.0),
                radius,
                radius);
            PreviewFrame.CornerRadius = new CornerRadius(radius);
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

    private void ConfigurePreviewLayerVisual(LayerRow layer, FrameworkElement visual)
    {
        visual.ToolTip = $"{layer.Index} {layer.Type} {layer.DataSource} {layer.Text}";
        visual.Cursor = layer.IsLocked ? Cursors.Arrow : Cursors.Hand;
        if (layer.IsLocked)
        {
            return;
        }

        visual.PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
                Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                TogglePreviewLayerSelection(
                    layer,
                    removeWhenSelected: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
                args.Handled = true;
            }
        };
        visual.MouseLeftButtonDown += (_, args) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
                Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                TogglePreviewLayerSelection(
                    layer,
                    removeWhenSelected: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
                args.Handled = true;
                return;
            }

            StartPreviewDrag(layer, args.GetPosition(PreviewCanvas));
            args.Handled = true;
        };
        visual.MouseRightButtonDown += (_, args) =>
        {
            SelectLayerForContext(layer, Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            PreviewLayerContextMenu.PlacementTarget = PreviewCanvas;
            PreviewLayerContextMenu.IsOpen = true;
            args.Handled = true;
        };
    }

    private void UpdateLayerPreviewVisual(LayerRow layer)
    {
        if (string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
        {
            RequestPreviewDraw();
            return;
        }

        if (_previewLayerVisuals.TryGetValue(layer, out var previousVisual))
        {
            PreviewCanvas.Children.Remove(previousVisual);
            _previewLayerVisuals.Remove(layer);
        }

        if (string.Equals(layer.Hide, "True", StringComparison.OrdinalIgnoreCase))
        {
            RequestPreviewDraw();
            return;
        }

        var bounds = GetLayerBounds(layer);
        var visual = CreateLayerPreviewVisual(layer, bounds, ReferenceEquals(LayerGrid.SelectedItem, layer));
        ConfigurePreviewLayerVisual(layer, visual);
        Canvas.SetLeft(visual, bounds.Left);
        Canvas.SetTop(visual, bounds.Top);
        Canvas.SetZIndex(visual, GetPreviewLayerZIndex(layer));
        PreviewCanvas.Children.Add(visual);
        _previewLayerVisuals[layer] = visual;

        if (_previewSelectionVisuals.TryGetValue(layer, out var selection))
        {
            var selectionBounds = GetLayerSelectionBounds(layer);
            selection.Width = selectionBounds.Width;
            selection.Height = selectionBounds.Height;
            Canvas.SetLeft(selection, selectionBounds.Left);
            Canvas.SetTop(selection, selectionBounds.Top);
            Canvas.SetZIndex(selection, 1000);

            if (ReferenceEquals(LayerGrid.SelectedItem, layer) && _previewResizeHandle != null)
            {
                Canvas.SetLeft(_previewResizeHandle, selectionBounds.Right - 5);
                Canvas.SetTop(_previewResizeHandle, selectionBounds.Bottom - 5);
                Canvas.SetZIndex(_previewResizeHandle, 1001);
            }
        }

        if (_previewClockCenterMarkers.TryGetValue(layer, out var centerMarker))
        {
            var center = GetClockCenterPreviewPoint(layer);
            Canvas.SetLeft(centerMarker, center.X - centerMarker.Width / 2.0);
            Canvas.SetTop(centerMarker, center.Y - centerMarker.Height / 2.0);
            Canvas.SetZIndex(centerMarker, 1002);
        }

        DrawAlignmentGuides(LayerGrid.SelectedItem as LayerRow);
    }

    private static int GetPreviewLayerZIndex(LayerRow layer)
    {
        return int.TryParse(layer.Index, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            ? 10 + index
            : 10;
    }

    private const double TextPreviewSupersample = 2.0;
    private const int GdiTextPadding = 4;
    private const double GdiTextPaddingLayout = GdiTextPadding / TextPreviewSupersample;

    private static double TextPreviewRenderScale => _textPreviewRenderScale;

    private double ToPreview(double templateValue) => templateValue * _previewScale;
    private double ToTemplate(double previewValue) => previewValue / _previewScale;
    private double ToPreviewFontSize(double templateFontSize) => Math.Max(1.0, templateFontSize * _previewScale);

    private void DrawAlignmentGuides(LayerRow? selected)
    {
        RemovePreviewGuideLines();
        if (selected is null)
        {
            return;
        }

        AddPreviewGridBackground();
        var selectedBounds = GetLayerSelectionBounds(selected);
        var selectedCenterX = selectedBounds.Left + selectedBounds.Width / 2.0;
        var selectedCenterY = selectedBounds.Top + selectedBounds.Height / 2.0;
        if (string.Equals(selected.Type, "GraphClock", StringComparison.OrdinalIgnoreCase))
        {
            var clockCenter = GetClockCenterPreviewPoint(selected);
            selectedCenterX = clockCenter.X;
            selectedCenterY = clockCenter.Y;
        }

        if (TryParseInt(selected.X, out _))
        {
            foreach (var x in GetCanvasGuidePositions(_templateCanvasWidth))
            {
                AddGuideLine("X", x, "#7B879A", 0.28, dashed: true);
            }
            AddPreviewGuideLine("X", selectedCenterX, "#39C6FF", 0.9);
        }

        if (TryParseInt(selected.Y, out _))
        {
            foreach (var y in GetCanvasGuidePositions(_templateCanvasHeight))
            {
                AddGuideLine("Y", y, "#7B879A", 0.28, dashed: true);
            }
            AddPreviewGuideLine("Y", selectedCenterY, "#39C6FF", 0.9);
        }
    }

    private void AddPreviewGridBackground()
    {
        var background = new Rectangle
        {
            Tag = "PreviewGuide",
            Width = _previewCanvasWidth,
            Height = _previewCanvasHeight,
            Fill = NewBrush("#12071324", "#12071324"),
            IsHitTestVisible = false
        };
        PreviewCanvas.Children.Add(background);
    }

    private void AddGuideLine(string axis, int templateValue, string color, double opacity, bool dashed = false)
    {
        AddPreviewGuideLine(axis, ToPreview(templateValue), color, opacity, dashed);
    }

    private void AddPreviewGuideLine(string axis, double previewValue, string color, double opacity, bool dashed = false)
    {
        var line = new Line
        {
            Tag = "PreviewGuide",
            Stroke = NewBrush(color, color),
            StrokeThickness = 1,
            Opacity = opacity,
            IsHitTestVisible = false
        };
        if (dashed)
        {
            line.StrokeDashArray = new DoubleCollection { 2, 3 };
        }

        if (axis == "X")
        {
            line.X1 = previewValue;
            line.X2 = previewValue;
            line.Y1 = 0;
            line.Y2 = _previewCanvasHeight;
        }
        else
        {
            line.X1 = 0;
            line.X2 = _previewCanvasWidth;
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
        if (layer.IsLocked ||
            string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase)) return;

        PreviewCanvas.Focus();
        PushUndoState(GetLanguageText("history.move", "Move layers"));
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
        _clockDragEditPoseLayers.Clear();
        foreach (LayerRow selectedLayer in LayerGrid.SelectedItems)
        {
            if (string.Equals(selectedLayer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(selectedLayer.Type, "GraphClock", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(selectedLayer.ClockMoveOrigin, "True", StringComparison.OrdinalIgnoreCase))
            {
                _clockDragEditPoseLayers.Add(selectedLayer);
            }
            _dragStartPositions[selectedLayer] = GetPreviewDragPosition(selectedLayer);
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

        if (_clockDragEditPoseLayers.Count > 0)
        {
            DrawPreview();
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

                SetPreviewDragPosition(targetLayer, snapX, snapY);

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
            bool isSensor = type.Equals("GraphSensor", StringComparison.OrdinalIgnoreCase);
            bool isImage = type.Contains("Image", StringComparison.OrdinalIgnoreCase);
            bool isArcGraph = type.Contains("GraphArchBar", StringComparison.OrdinalIgnoreCase);

            if (isSensor)
            {
                var delta = Math.Abs(dx) > Math.Abs(dy) ? dx : dy;
                var newZoom = Math.Clamp(Math.Round((_resizeStartZoom * 400.0 + delta) / 400.0, 3), 0.05, 10.0);
                _dragLayer.ZoomRate = FormatZoom(newZoom);
                _dragLayer.SensorZoomRate = _dragLayer.ZoomRate;
                ZoomBox.Text = _dragLayer.ZoomRate;
            }
            else if (isGraph)
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
            var currentTemplate = GetPreviewDragPosition(layer);
            var dx = ToPreview(currentTemplate.X - startTemplate.X);
            var dy = ToPreview(currentTemplate.Y - startTemplate.Y);
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

            if (_previewClockCenterMarkers.TryGetValue(layer, out var centerMarker))
            {
                var center = GetClockCenterPreviewPoint(layer);
                Canvas.SetLeft(centerMarker, center.X - centerMarker.Width / 2.0);
                Canvas.SetTop(centerMarker, center.Y - centerMarker.Height / 2.0);
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
                var currentPos = GetPreviewDragPosition(layer);
                if (Math.Round(currentPos.X) != Math.Round(startPos.X) ||
                    Math.Round(currentPos.Y) != Math.Round(startPos.Y))
                {
                    MarkLayerDirty(layer);
                }
            }
            foreach (var paired in _shadowStartPositions.Keys)
            {
                MarkLayerDirty(paired);
            }
            PopulateEditorFromSelection();
            LayerGrid.Items.Refresh();
            _clockDragEditPoseLayers.Clear();
            DrawPreview();
            SetStatus(GetLanguageText("status.layoutChanged", "Layout changed. Press Apply to save."));
            _dragLayer = null;
            _dragStartPositions.Clear();
            _dragStartPreviewBounds.Clear();
            _dragStartSelectionBounds.Clear();
            _shadowStartPositions.Clear();
            _clockDragEditPoseLayers.Clear();
        }
        else if (_isResizingPreview)
        {
            _isResizingPreview = false;
            PreviewCanvas.ReleaseMouseCapture();
            if (_dragLayer != null)
            {
                MarkLayerDirty(_dragLayer);
                if (PairCheck.IsChecked == true)
                {
                    var paired = FindPairedLayer(_dragLayer);
                    if (paired != null)
                    {
                        SyncShadowProperties(_dragLayer, paired);
                        MarkLayerDirty(paired);
                    }
                }
                LayerGrid.Items.Refresh();
                PopulateEditorFromSelection();
                DrawPreview();
                SetStatus(GetLanguageText("status.layerSizeChanged", "Layer size changed. Press Apply to save."));
            }
            _dragLayer = null;
        }
    }

    private int SnapValue(int value, string axis, LayerRow current)
    {
        var dimension = axis == "X" ? _templateCanvasWidth : _templateCanvasHeight;
        var targets = GetCanvasGuidePositions(dimension);
        foreach (var target in targets)
        {
            if (Math.Abs(value - target) <= 5) return target;
        }
        return value;
    }

    private static int[] GetCanvasGuidePositions(double dimension)
    {
        var size = Math.Max(1, (int)Math.Round(dimension));
        return Enumerable.Range(0, 5)
            .Select(index => (int)Math.Round(size * index / 4.0))
            .Distinct()
            .ToArray();
    }

    private static bool TryParseInt(string value, out int result)
    {
        return int.TryParse(value, out result);
    }

    private Point GetPreviewDragPosition(LayerRow layer)
    {
        if (string.Equals(layer.Type, "GraphClock", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(layer.ClockMoveOrigin, "True", StringComparison.OrdinalIgnoreCase))
        {
            var centerX = double.TryParse(layer.ClockCenterX, NumberStyles.Float, CultureInfo.InvariantCulture, out var cx)
                ? cx
                : _templateCanvasWidth / 2.0;
            var centerY = double.TryParse(layer.ClockCenterY, NumberStyles.Float, CultureInfo.InvariantCulture, out var cy)
                ? cy
                : _templateCanvasHeight / 2.0;
            return new Point(centerX, centerY);
        }

        var x = double.TryParse(layer.X, NumberStyles.Float, CultureInfo.InvariantCulture, out var lx) ? lx : 0.0;
        var y = double.TryParse(layer.Y, NumberStyles.Float, CultureInfo.InvariantCulture, out var ly) ? ly : 0.0;
        return new Point(x, y);
    }

    private static void SetPreviewDragPosition(LayerRow layer, int x, int y)
    {
        if (string.Equals(layer.Type, "GraphClock", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(layer.ClockMoveOrigin, "True", StringComparison.OrdinalIgnoreCase))
        {
            layer.ClockCenterX = x.ToString(CultureInfo.InvariantCulture);
            layer.ClockCenterY = y.ToString(CultureInfo.InvariantCulture);
            return;
        }

        layer.X = x.ToString(CultureInfo.InvariantCulture);
        layer.Y = y.ToString(CultureInfo.InvariantCulture);
    }

    private Point GetClockCenterPreviewPoint(LayerRow layer)
    {
        var centerX = double.TryParse(layer.ClockCenterX, NumberStyles.Float, CultureInfo.InvariantCulture, out var cx)
            ? cx
            : _templateCanvasWidth / 2.0;
        var centerY = double.TryParse(layer.ClockCenterY, NumberStyles.Float, CultureInfo.InvariantCulture, out var cy)
            ? cy
            : _templateCanvasHeight / 2.0;
        var originX = double.TryParse(layer.ClockOriginX, NumberStyles.Float, CultureInfo.InvariantCulture, out var ox)
            ? ox
            : 0.0;
        var originY = double.TryParse(layer.ClockOriginY, NumberStyles.Float, CultureInfo.InvariantCulture, out var oy)
            ? oy
            : 0.0;
        return new Point(ToPreview(centerX + originX), ToPreview(centerY + originY));
    }

    private FrameworkElement CreateLayerPreviewVisual(LayerRow layer, Rect bounds, bool selected)
    {
        var type = layer.Type ?? "";
        if (type.Contains("Image", StringComparison.OrdinalIgnoreCase))
        {
            var imagePath = ResolveLayerMediaPath(layer);
            if (!string.IsNullOrWhiteSpace(imagePath))
            {
                return CreatePreviewImage(imagePath, bounds.Width, bounds.Height, selected, layer.Rotate);
            }
        }

        if (type.Equals("GraphClock", StringComparison.OrdinalIgnoreCase))
        {
            var imagePath = ResolveLayerMediaPath(layer);
            if (!string.IsNullOrWhiteSpace(imagePath))
            {
                // L-Connect edits the hand offset with the clock at its zero-angle pose.
                // Keep that pose active for the whole hand-positioning mode, rather than
                // switching on mouse-down; switching on mouse-down makes the image appear
                // to jump away from the pointer before the drag even starts.
                var positioningHand = selected &&
                    !string.Equals(layer.ClockMoveOrigin, "True", StringComparison.OrdinalIgnoreCase);
                var angle = positioningHand || _clockDragEditPoseLayers.Contains(layer)
                    ? 0.0
                    : GetClockLayerAngle(layer);
                return CreateClockPreviewImage(layer, imagePath, bounds.Width, bounds.Height, angle);
            }
        }

        if (type.Equals("GraphSensor", StringComparison.OrdinalIgnoreCase))
        {
            var sensorPreviewPath = ResolveLayerMediaPath(layer);
            if (!string.IsNullOrWhiteSpace(sensorPreviewPath))
            {
                return CreatePreviewImage(sensorPreviewPath, bounds.Width, bounds.Height, selected: false, rotationText: "");
            }
            return CreateSensorPreview(layer, bounds.Width, bounds.Height);
        }

        if (type.Contains("GraphStatuBar", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("GraphArchBar", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("GraphLine", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("DynamicBar", StringComparison.OrdinalIgnoreCase))
        {
            var graphPreviewPath = ResolveLayerMediaPath(layer);
            if (!string.IsNullOrWhiteSpace(graphPreviewPath))
            {
                return CreatePreviewImage(graphPreviewPath, bounds.Width, bounds.Height, selected: false, rotationText: "");
            }
            return CreateGraphPreview(layer, bounds.Width, bounds.Height, selected);
        }

        var value = GetPreviewText(layer);
        value = ApplyDataDisplayOptions(layer, value);
        if (string.IsNullOrWhiteSpace(value))
        {
            value = layer.DataSource;
        }

        return CreateGdiTextPreviewVisual(layer, value);
    }

    private FrameworkElement CreateClockPreviewImage(LayerRow layer, string imagePath, double width, double height, double angle)
    {
        var image = CreatePreviewImage(imagePath, width, height, selected: false, rotationText: "");
        var templateWidth = Math.Max(1.0, ToTemplate(width));
        var templateHeight = Math.Max(1.0, ToTemplate(height));
        var offsetX = double.TryParse(layer.X, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) ? px : -templateWidth / 2.0;
        var offsetY = double.TryParse(layer.Y, NumberStyles.Float, CultureInfo.InvariantCulture, out var py) ? py : -templateHeight / 2.0;
        var originX = double.TryParse(layer.ClockOriginX, NumberStyles.Float, CultureInfo.InvariantCulture, out var ox) ? ox : 0.0;
        var originY = double.TryParse(layer.ClockOriginY, NumberStyles.Float, CultureInfo.InvariantCulture, out var oy) ? oy : 0.0;
        image.RenderTransformOrigin = new Point(
            (originX - offsetX) / templateWidth,
            (originY - offsetY) / templateHeight);
        image.RenderTransform = new RotateTransform(angle);
        return image;
    }

    private FrameworkElement CreateSensorPreview(LayerRow layer, double width, double height)
    {
        var canvas = new Canvas
        {
            Width = width,
            Height = height,
            Background = Brushes.Transparent
        };
        var scale = Math.Min(width, height) / ToPreview(400.0);
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0) scale = 1.0;
        var center = new Point(width / 2.0, height / 2.0);
        var style = string.IsNullOrWhiteSpace(layer.SensorStyle) ? layer.SubTypeName : layer.SensorStyle;
        if (string.IsNullOrWhiteSpace(style)) style = "Ring2";
        var sensorType = string.IsNullOrWhiteSpace(layer.SensorType) ? SensorTypeFromDataSource(layer.DataSource) : layer.SensorType;
        var info = GetSensorTypeInfo(sensorType);
        var valueText = string.IsNullOrWhiteSpace(layer.Text)
            ? SampleValueFor(info.DataSource)
            : layer.Text;
        var numeric = ExtractSensorNumericValue(valueText);
        var ratio = Math.Clamp(info.Type == "FanRPM" ? numeric / 1900.0 : numeric / 100.0, 0.0, 1.0);
        var active = NewBrush(layer.SensorColor1, "#FFFFFF");
        var active2 = NewBrush(layer.SensorColor2, "#00FFEE");
        var track = NewBrush(layer.SensorBgColor, "#303030");
        var textBrush = NewBrush(layer.SensorMainFontColor, "#FFFFFF");

        if (!style.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            DrawSensorStyle(canvas, center, scale, style, ratio, active, active2, track);
        }

        AddSensorText(canvas, center, scale, info.Top, -58, 22, textBrush);
        AddSensorText(canvas, center, scale, FormatSensorMainValue(numeric, info.Unit), 0, 58, textBrush);
        AddSensorText(canvas, center, scale, info.Bottom, 70, 22, textBrush);
        return canvas;
    }

    private static double GetSensorZoomRate(LayerRow layer)
    {
        if (TryParseZoom(layer.SensorZoomRate, out var sensorZoom) && sensorZoom > 0)
        {
            return sensorZoom;
        }
        if (TryParseZoom(layer.ZoomRate, out var zoom) && zoom > 0)
        {
            return zoom;
        }
        return 0.5;
    }

    private void DrawSensorStyle(Canvas canvas, Point center, double scale, string style, double ratio, Brush active, Brush active2, Brush track)
    {
        if (style.Equals("Ring3", StringComparison.OrdinalIgnoreCase))
        {
            var radius = 140 * scale;
            var bg = new System.Windows.Shapes.Path
            {
                Fill = track,
                Data = new PathGeometry(new[]
                {
                    new PathFigure(new Point(center.X - radius, center.Y + 18 * scale), new PathSegment[]
                    {
                        new ArcSegment(new Point(center.X + radius, center.Y + 18 * scale), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true),
                        new LineSegment(new Point(center.X, center.Y + 95 * scale), true)
                    }, true)
                })
            };
            canvas.Children.Add(bg);
            for (var i = 0; i <= 20; i++)
            {
                var angle = 180 + i * 9.0;
                var p1 = PointOnCircle(center, radius - 18 * scale, angle);
                var p2 = PointOnCircle(center, radius - 34 * scale, angle);
                canvas.Children.Add(new Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = Brushes.White, StrokeThickness = 2 * scale });
            }
            var needleAngle = 180 + ratio * 180;
            var end = PointOnCircle(center, radius - 48 * scale, needleAngle);
            canvas.Children.Add(new Line { X1 = center.X, Y1 = center.Y + 18 * scale, X2 = end.X, Y2 = end.Y, Stroke = active, StrokeThickness = 8 * scale, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            var hub = new Ellipse { Width = 28 * scale, Height = 28 * scale, Fill = active, Stroke = Brushes.White, StrokeThickness = 4 * scale };
            Canvas.SetLeft(hub, center.X - 14 * scale);
            Canvas.SetTop(hub, center.Y + 4 * scale);
            canvas.Children.Add(hub);
            return;
        }

        if (style.Equals("Ring5", StringComparison.OrdinalIgnoreCase))
        {
            var outer = 118 * scale;
            var disk = new Ellipse { Width = outer * 2, Height = outer * 2, Fill = track };
            Canvas.SetLeft(disk, center.X - outer);
            Canvas.SetTop(disk, center.Y - outer);
            canvas.Children.Add(disk);
            AddArcPath(canvas, center, 78 * scale, -90, Math.Max(4, ratio * 359.0), active, 76 * scale, PenLineCap.Flat);
            var inner = new Ellipse
            {
                Width = 132 * scale,
                Height = 132 * scale,
                Fill = new SolidColorBrush(Color.FromArgb(238, 235, 244, 246)),
                Stroke = new SolidColorBrush(Color.FromArgb(160, 180, 190, 194)),
                StrokeThickness = 4 * scale
            };
            Canvas.SetLeft(inner, center.X - 66 * scale);
            Canvas.SetTop(inner, center.Y - 66 * scale);
            canvas.Children.Add(inner);
            return;
        }

        var radiusValue = style.Equals("Ring2", StringComparison.OrdinalIgnoreCase)
            ? 104
            : style.Equals("Ring4", StringComparison.OrdinalIgnoreCase)
                ? 98
                : 108;
        var thicknessValue = style.Equals("Ring2", StringComparison.OrdinalIgnoreCase)
            ? 20
            : style.Equals("Ring4", StringComparison.OrdinalIgnoreCase)
                ? 18
                : 38;
        var radius2 = radiusValue * scale;
        var thickness = thicknessValue * scale;
        if (!style.Equals("Ring2", StringComparison.OrdinalIgnoreCase))
        {
            var trackEllipse = new Ellipse
            {
                Width = radius2 * 2,
                Height = radius2 * 2,
                Stroke = track,
                StrokeThickness = thickness
            };
            Canvas.SetLeft(trackEllipse, center.X - radius2);
            Canvas.SetTop(trackEllipse, center.Y - radius2);
            canvas.Children.Add(trackEllipse);
        }

        if (style.Equals("Ring2", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < 20; i++)
            {
                var segmentRatio = (i + 1) / 20.0;
                var brush = segmentRatio <= ratio ? active : track;
                AddArcPath(canvas, center, radius2, -90 + i * 18 + 1.5, 15, brush, thickness, PenLineCap.Flat);
            }
        }
        else
        {
            AddArcPath(canvas, center, radius2, -90, Math.Max(1, ratio * 359.0), active, thickness, PenLineCap.Round);
        }

        if (style.Equals("Ring4", StringComparison.OrdinalIgnoreCase))
        {
            AddArcPath(canvas, center, radius2 - 34 * scale, -90, 359, active2, 4 * scale, PenLineCap.Flat);
            for (var i = 0; i < 48; i++)
            {
                var p1 = PointOnCircle(center, radius2 + 30 * scale, -90 + i * 7.5);
                var p2 = PointOnCircle(center, radius2 + 16 * scale, -90 + i * 7.5);
                canvas.Children.Add(new Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = i % 6 == 0 ? Brushes.White : active2, StrokeThickness = 2.5 * scale });
            }
        }
    }

    private static void AddSensorText(Canvas canvas, Point center, double scale, string text, double yOffset, double size, Brush brush)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontSize = Math.Max(8, size * scale),
            FontWeight = FontWeights.Bold,
            FontFamily = new System.Windows.Media.FontFamily("Agency FB"),
            TextAlignment = TextAlignment.Center,
            Width = 180 * scale
        };
        Canvas.SetLeft(block, center.X - block.Width / 2);
        Canvas.SetTop(block, center.Y + yOffset * scale - size * scale / 2);
        canvas.Children.Add(block);
    }

    private static double ExtractSensorNumericValue(string value)
    {
        var match = Regex.Match(value ?? "", @"[-+]?\d+(?:[.,]\d+)?");
        return match.Success &&
               double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric)
            ? numeric
            : 52.0;
    }

    private static string FormatSensorMainValue(double value, string unit)
    {
        var rounded = Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
        return string.Equals(unit, "RPM", StringComparison.OrdinalIgnoreCase)
            ? rounded
            : rounded;
    }

    private static double GetClockLayerAngle(LayerRow layer)
    {
        var now = DateTime.Now;
        var format = (layer.Format ?? "").Trim();
        var start = TryParseClockNumber(layer.ClockAngle, out var startAngle) ? startAngle : 0.0;
        var total = TryParseClockNumber(layer.ClockEndAngle, out var totalAngle) ? totalAngle : 360.0;
        var rateOffset = TryParseClockNumber(layer.ClockOffset, out var parsedRateOffset) ? parsedRateOffset : 0.0;
        double rate;
        if (string.Equals(layer.DataSource, "TIME", StringComparison.OrdinalIgnoreCase))
        {
            var hour12 = now.Hour % 12;
            if (hour12 == 0) hour12 = 12;
            rate = format.ToLowerInvariant() switch
            {
                "s" => now.Second / 60.0,
                "m" => now.Minute / 60.0,
                "h_12" => hour12 / 12.0,
                "h_24" => now.Hour / 24.0,
                _ => 0.0
            };
        }
        else
        {
            rate = TryGetLiveClockRate(layer.DataSource, layer.Format ?? "", out var liveRate)
                ? liveRate
                : TryParseClockNumber(GetDefaultPreviewValue(layer.DataSource), out var defaultValue)
                    ? NormalizeClockRate(layer.DataSource, defaultValue)
                    : TryParseClockNumber(layer.DataRate, out var storedRate)
                        ? storedRate
                        : TryParseClockNumber(layer.Text, out var value) ? NormalizeClockRate(layer.DataSource, value) : 0.0;
        }
        var delta = (rate - rateOffset) * total;
        var angle = string.Equals(layer.Revert, "True", StringComparison.OrdinalIgnoreCase)
            ? start - delta
            : start + delta;
        return angle;
    }

    private static string GetDefaultPreviewValue(string dataSource)
    {
        return NormalizeDataSourceKey(dataSource) switch
        {
            "CPUTEMP" => "52",
            "CPUTEMP_F" => "126",
            "GPUTEMP" => "54",
            "GPUTEMP_F" => "129",
            "CPULOAD" => "23",
            "GPULOAD" => "17",
            "RAMLOAD" => "42",
            "GPURAMLOAD" => "48",
            "CPUPWR" => "65",
            "GPUPWR" => "175",
            "CPUFAN" => "1250",
            "GPUFAN" => "1400",
            "PUMP" or "WATERPUMP" => "2600",
            "CPUCLOCK" => "5200",
            "GPUCLOCK" => "2750",
            "CPUCLOCK_G" => "5.2",
            "GPUCLOCK_G" => "2.8",
            "UPSPEED" => "8.5",
            "DOWNDSPEED" => "45.2",
            "FPS_AVG" => "120",
            _ => "50"
        };
    }

    private static bool TryGetLiveClockRate(string dataSource, string format, out double rate)
    {
        rate = 0.0;
        var key = NormalizeDataSourceKey(dataSource);
        if (string.IsNullOrWhiteSpace(key) || key is "TIME" or "DATE" or "DAY" or "STATICTEXT")
        {
            return false;
        }

        if (!TryGetLiveSensorValue(key, format ?? "", out var liveValue) ||
            !TryParseClockNumber(liveValue, out var numeric))
        {
            return false;
        }

        rate = NormalizeClockRate(key, numeric);
        return true;
    }

    private static string NormalizeDataSourceKey(string dataSource)
    {
        var key = (dataSource ?? "").Trim().ToUpperInvariant();
        return key switch
        {
            "CPUPOWER" => "CPUPWR",
            "GPUPOWER" => "GPUPWR",
            _ => key
        };
    }

    private static double NormalizeClockRate(string dataSource, double value)
    {
        var key = NormalizeDataSourceKey(dataSource);
        var divisor = key switch
        {
            "CPUPWR" or "CPUPOWER" or "GPUPWR" or "GPUPOWER" => 250.0,
            "CPUFAN" or "GPUFAN" or "PUMP" or "WATERPUMP" => 6000.0,
            "CPUCLOCK" or "GPUCLOCK" => 6000.0,
            "CPUCLOCK_G" or "GPUCLOCK_G" => 6.0,
            "UPSPEED" or "DOWNDSPEED" => 100.0,
            "FPS_AVG" => 240.0,
            _ => 100.0
        };

        return Math.Clamp(value / divisor, 0.0, 1.0);
    }

    private static bool TryParseClockNumber(string? value, out double result)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
               double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
    }

    private static string ApplyDataDisplayOptions(LayerRow layer, string value)
    {
        return value ?? string.Empty;
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

        if (LayerGrid.SelectedItem is LayerRow addedSensorLayer &&
            string.Equals(addedSensorLayer.Type, "GraphSensor", StringComparison.OrdinalIgnoreCase))
        {
            await RefreshSensorPreviewAsync(addedSensorLayer);
        }

        if (AddWithShadowCheck.IsChecked != true || LayerGrid.SelectedItem is not LayerRow sourceLayer)
        {
            RestorePendingDirtyLayersAfterAdd();
            return;
        }

        if (!int.TryParse(sourceLayer.Index, out var sourceIndex))
        {
            throw new InvalidOperationException("Invalid source layer index.");
        }

        var deviceModel = GetSelectedDeviceModel();
        var templatePath = _currentTemplatePath;
        var shadowX = ShadowXBox.Text;
        var shadowY = ShadowYBox.Text;
        var shadowColor = ShadowColorBox.Text;
        await Task.Run(() => _supporter.AddShadowAsync(
            deviceModel,
            templatePath,
            sourceLayer.Index,
            shadowX,
            shadowY,
            shadowColor));

        ShiftShadowLinksForInsert(sourceIndex);
        _shadowLinks[sourceIndex] = sourceIndex + 1;
        SaveShadowLinks();
        await LoadLayersAsync(true);
        RestorePendingDirtyLayersAfterAdd();
        SelectLayerByIndex(sourceIndex.ToString());
    }

    private void CapturePendingDirtyLayersBeforeAdd()
    {
        if (LayerGrid.SelectedItem is LayerRow selected && _dirtyLayers.Contains(selected))
        {
            UpdateLayerFromInputs(selected);
        }
        _pendingDirtyLayersAfterAdd = _dirtyLayers
            .Where(layer => Layers.Contains(layer) && !layer.IsEditorMetadata)
            .ToDictionary(layer => layer.Index, CloneLayerState, StringComparer.OrdinalIgnoreCase);
    }

    private void RestorePendingDirtyLayersAfterAdd()
    {
        if (_pendingDirtyLayersAfterAdd == null) return;
        foreach (var pair in _pendingDirtyLayersAfterAdd)
        {
            var target = Layers.FirstOrDefault(layer => string.Equals(layer.Index, pair.Key, StringComparison.OrdinalIgnoreCase));
            if (target == null) continue;
            CopyLayerState(pair.Value, target);
            MarkLayerDirty(target);
        }
        _pendingDirtyLayersAfterAdd = null;
        DrawPreview();
    }

    private static LayerRow CloneLayerState(LayerRow source)
    {
        var clone = new LayerRow();
        CopyLayerState(source, clone);
        return clone;
    }

    private static void CopyLayerState(LayerRow source, LayerRow target)
    {
        foreach (var property in typeof(LayerRow).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.CanRead && property.CanWrite &&
                                        property.Name is not nameof(LayerRow.Index) and not nameof(LayerRow.Type)))
        {
            property.SetValue(target, property.GetValue(source));
        }
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

    private static void NormalizeAnimationLayerZoom(LayerRow layer)
    {
        if (!string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        layer.ZoomRate = "1";
    }

    private static bool IsGroupingMetadataLayer(LayerRow layer) =>
        string.Equals(layer.Type, "GraphItem", StringComparison.OrdinalIgnoreCase) &&
        (layer.Text ?? "").StartsWith(GroupMetadataMarker, StringComparison.Ordinal);

    private static GroupingMetadata ReadGroupingMetadata(IEnumerable<LayerRow> layers)
    {
        try
        {
            var markerLayer = layers.FirstOrDefault(IsGroupingMetadataLayer);
            if (markerLayer == null) return new GroupingMetadata();
            var encoded = markerLayer.Text[GroupMetadataMarker.Length..];
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return JsonSerializer.Deserialize<GroupingMetadata>(json) ?? new GroupingMetadata();
        }
        catch
        {
            return new GroupingMetadata();
        }
    }

    private void ApplyGroupingMetadata(GroupingMetadata metadata)
    {
        foreach (var groupData in metadata.Groups.Where(group => !string.IsNullOrWhiteSpace(group.Id)))
        {
            LayerGroups.Add(new LayerGroup
            {
                Id = groupData.Id,
                Name = string.IsNullOrWhiteSpace(groupData.Name) ? "Group" : groupData.Name,
                IsExpanded = groupData.IsExpanded,
                IsLocked = groupData.IsLocked,
                Color = string.IsNullOrWhiteSpace(groupData.Color) ? "#246FF2" : groupData.Color
            });
        }

        var available = Layers.Where(layer => !layer.IsEditorMetadata).ToList();
        var assigned = new HashSet<LayerRow>();
        foreach (var member in metadata.Members)
        {
            var group = LayerGroups.FirstOrDefault(item => item.Id == member.GroupId);
            if (group == null) continue;
            var layer = available.FirstOrDefault(item =>
                            !assigned.Contains(item) &&
                            int.TryParse(item.Index, out var index) && index == member.Index &&
                            string.Equals(GetLayerGroupingSignature(item), member.Signature, StringComparison.Ordinal))
                        ?? available.FirstOrDefault(item =>
                            !assigned.Contains(item) &&
                            string.Equals(GetLayerGroupingSignature(item), member.Signature, StringComparison.Ordinal))
                        ?? available.FirstOrDefault(item =>
                            !assigned.Contains(item) &&
                            int.TryParse(item.Index, out var index) && index == member.Index);
            if (layer == null) continue;
            layer.GroupId = group.Id;
            layer.GroupName = group.Name;
            layer.GroupColor = group.Color;
            if (group.IsLocked)
            {
                layer.IsLocked = true;
                _lockedLayerKeys.Add(GetLayerLockKey(_currentTemplatePath, layer.Index));
            }
            assigned.Add(layer);
        }
    }

    private static string GetLayerGroupingSignature(LayerRow layer)
    {
        var value = string.Join("|", new[]
        {
            layer.Type, layer.DataSource, layer.TypeName, layer.SubTypeName,
            layer.Media, layer.Text, layer.X, layer.Y
        }.Select(item => (item ?? "").Replace("|", "||")));
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..16];
    }

    private void ConfigureLayerGrouping()
    {
        PreserveLayerGridScroll(() =>
        {
            if (LayerView is ListCollectionView view)
            {
                view.GroupDescriptions.Clear();
                var hasVisibleGroups = Layers.Any(layer =>
                    !layer.IsEditorMetadata &&
                    !string.IsNullOrWhiteSpace(layer.GroupName));
                if (_groupingEnabled && hasVisibleGroups)
                {
                    view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LayerRow.GroupDisplayName)));
                }
            }
            LayerView.Refresh();
        });
        Dispatcher.BeginInvoke(
            new Action(UpdateLayerGridColumnWidth),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void PreserveLayerGridScroll(Action refreshAction)
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(LayerGrid);
        var horizontalOffset = scrollViewer?.HorizontalOffset ?? 0;
        var verticalOffset = scrollViewer?.VerticalOffset ?? 0;
        refreshAction();
        if (scrollViewer == null) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            scrollViewer.ScrollToHorizontalOffset(horizontalOffset);
            scrollViewer.ScrollToVerticalOffset(verticalOffset);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void UpdateLayerGridColumnWidth()
    {
        if (LayerGrid == null || LayerCardColumn == null || LayerGrid.ActualWidth <= 0) return;
        LayerCardColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
    }

    private string BuildGroupingMetadataValue()
    {
        var metadata = new GroupingMetadata
        {
            Groups = LayerGroups.Select(group => new GroupingMetadataGroup
            {
                Id = group.Id,
                Name = group.Name,
                IsExpanded = group.IsExpanded,
                IsLocked = group.IsLocked,
                Color = group.Color
            }).ToList(),
            Members = Layers
                .Where(layer => !layer.IsEditorMetadata && !string.IsNullOrWhiteSpace(layer.GroupId))
                .Select(layer => new GroupingMetadataMember
                {
                    GroupId = layer.GroupId,
                    Index = int.TryParse(layer.Index, out var index) ? index : -1,
                    Signature = GetLayerGroupingSignature(layer)
                }).ToList()
        };
        var json = JsonSerializer.Serialize(metadata);
        return GroupMetadataMarker + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }

    private async Task PersistGroupingMetadataAsync()
    {
        if (_savingGroupingMetadata || string.IsNullOrWhiteSpace(_currentTemplatePath) || !File.Exists(_currentTemplatePath)) return;
        _savingGroupingMetadata = true;
        try
        {
            var target = await ResolveTemplateTargetAsync();
            var value = LayerGroups.Count == 0 ? "" : BuildGroupingMetadataValue();
            await _supporter.SetGroupingMetadataAsync(target.DeviceModel, target.TemplatePath, value);
            var existing = Layers.FirstOrDefault(layer => layer.IsEditorMetadata);
            if (string.IsNullOrEmpty(value))
            {
                if (existing != null) Layers.Remove(existing);
            }
            else if (existing == null)
            {
                Layers.Add(new LayerRow
                {
                    Index = Layers.Count.ToString(CultureInfo.InvariantCulture),
                    Type = "GraphItem",
                    TypeName = "Text",
                    SubTypeName = "EditorMetadata",
                    Text = value,
                    Hide = "True",
                    X = "-10000",
                    Y = "-10000",
                    IsEditorMetadata = true
                });
            }
            else
            {
                existing.Text = value;
            }
            LayerView.Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, GetLanguageText("settings.layerGrouping", "Layer grouping"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _savingGroupingMetadata = false;
        }
    }

    private async void CreateLayerGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedLayers(includeAnimation: false);
        if (selected.Count == 0)
        {
            SetStatus(GetLanguageText("status.selectLayersToGroup", "Select one or more layers to group."));
            return;
        }

        var defaultName = $"Group {LayerGroups.Count + 1}";
        var name = PromptForGroupName(defaultName);
        if (string.IsNullOrWhiteSpace(name)) return;
        var uniqueName = _layerGroupService.GetUniqueName(LayerGroups, name);
        var previousGroupIds = selected.Select(layer => layer.GroupId).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet();
        var group = new LayerGroup { Name = uniqueName };
        LayerGroups.Add(group);
        _layerGroupService.Assign(selected, group);
        foreach (var previousId in previousGroupIds)
        {
            if (!Layers.Any(layer => layer.GroupId == previousId))
            {
                var emptyGroup = LayerGroups.FirstOrDefault(item => item.Id == previousId);
                if (emptyGroup != null) LayerGroups.Remove(emptyGroup);
            }
        }
        ConfigureLayerGrouping();
        await PersistGroupingMetadataAsync();
        SetStatus($"Created group “{group.Name}”.");
    }

    private string? PromptForGroupName(string defaultName, string title = "Create layer group", string action = "Create")
    {
        var input = new TextBox { Text = defaultName, MinWidth = 300, Margin = new Thickness(0, 8, 0, 16) };
        var ok = new Button { Content = action, Width = 92, IsDefault = true, Margin = new Thickness(8, 0, 0, 0), Style = (Style)FindResource("BtnPrimary") };
        var cancel = new Button { Content = GetLanguageText("common.cancel", "Cancel"), Width = 86, IsCancel = true, Style = (Style)FindResource("BtnGhost") };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = GetLanguageText("groups.name", "Group name"), FontSize = 12, Foreground = (Brush)FindResource("BrTextSecondary") });
        panel.Children.Add(input);
        panel.Children.Add(buttons);
        var dialog = CreateThemedDialog(title, panel, 420);
        ok.Click += (_, _) => dialog.DialogResult = true;
        input.SelectAll();
        input.Focus();
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private LayerGroup? FindLayerGroup(object? tag) =>
        LayerGroups.FirstOrDefault(group => string.Equals(group.Name, tag?.ToString(), StringComparison.Ordinal));

    private async void LayerGroupHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<Button>(e.OriginalSource as DependencyObject) != null ||
            FindVisualParent<ToggleButton>(e.OriginalSource as DependencyObject) != null ||
            sender is not FrameworkElement element ||
            FindLayerGroup(element.Tag) is not { } group)
        {
            return;
        }

        e.Handled = true;
        if (e.ClickCount < 2)
        {
            SelectLayerGroupMembers(group, Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            return;
        }

        await RenameLayerGroupAsync(group);
    }

    private async Task RenameLayerGroupAsync(LayerGroup group)
    {
        var name = PromptForGroupName(group.Name, GetLanguageText("groups.renameTitle", "Rename layer group"), GetLanguageText("groups.rename", "Rename"));
        if (string.IsNullOrWhiteSpace(name)) return;

        var newName = name.Trim();
        if (string.Equals(newName, group.Name, StringComparison.Ordinal)) return;

        var uniqueName = _layerGroupService.GetUniqueName(LayerGroups, newName, group.Id);

        group.Name = uniqueName;
        foreach (var layer in Layers.Where(layer => layer.GroupId == group.Id))
        {
            layer.GroupName = uniqueName;
        }

        ConfigureLayerGrouping();
        await PersistGroupingMetadataAsync();
        SetStatus($"Renamed group to “{group.Name}”.");
    }

    private void SelectLayerGroupMembers(LayerGroup group, bool extendSelection)
    {
        var members = Layers
            .Where(layer => layer.GroupId == group.Id && !layer.IsEditorMetadata)
            .OrderBy(layer => int.TryParse(layer.Index, out var index) ? index : int.MaxValue)
            .ToList();
        if (members.Count == 0) return;

        if (!extendSelection)
        {
            LayerGrid.SelectedItems.Clear();
        }

        foreach (var layer in members)
        {
            if (!LayerGrid.SelectedItems.Contains(layer))
            {
                LayerGrid.SelectedItems.Add(layer);
            }
        }

        LayerGrid.SelectedItem = members[0];
        LayerGrid.ScrollIntoView(members[0]);
        LayerGrid.Focus();
        PopulateEditorFromSelection();
        RequestPreviewDraw();
        SetStatus($"Selected group “{group.Name}”. Use arrow keys or drag one selected layer on preview to move together.");
    }

    private void LayerGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateLayerGridColumnWidth();
    }

    private void LayerGroupExpander_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { IsLoaded: false }) return;
        if (sender is FrameworkElement element && FindLayerGroup(element.Tag) is { } group) group.IsExpanded = true;
    }

    private void LayerGroupExpander_Collapsed(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { IsLoaded: false }) return;
        if (sender is FrameworkElement element && FindLayerGroup(element.Tag) is { } group) group.IsExpanded = false;
    }

    private void LayerGroupExpander_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggle && FindLayerGroup(toggle.Tag) is { } group)
        {
            toggle.IsChecked = group.IsExpanded;
        }
    }

    private void LayerGroupVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || FindLayerGroup(element.Tag) is not { } group) return;
        var members = Layers.Where(layer => layer.GroupId == group.Id && !layer.IsEditorMetadata).ToList();
        var hide = members.Any(layer => !string.Equals(layer.Hide, "True", StringComparison.OrdinalIgnoreCase));
        PushUndoState(GetLanguageText("history.groupVisibility", "Change group visibility"));
        foreach (var layer in members)
        {
            layer.Hide = hide ? "True" : "False";
            MarkLayerDirty(layer);
        }
        PreserveLayerGridScroll(() => LayerGrid.Items.Refresh());
        RequestPreviewDraw();
        SetStatus(hide ? $"Group “{group.Name}” hidden. Press Apply to save." : $"Group “{group.Name}” shown. Press Apply to save.");
        e.Handled = true;
    }

    private async void LayerGroupLockButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || FindLayerGroup(element.Tag) is not { } group) return;
        e.Handled = true;
        var members = Layers.Where(layer => layer.GroupId == group.Id && !layer.IsEditorMetadata).ToList();
        var locked = members.Any(layer => !layer.IsLocked);
        group.IsLocked = locked;
        foreach (var layer in members)
        {
            layer.IsLocked = locked;
            var key = GetLayerLockKey(_currentTemplatePath, layer.Index);
            if (locked) _lockedLayerKeys.Add(key); else _lockedLayerKeys.Remove(key);
        }
        PreserveLayerGridScroll(() => LayerGrid.Items.Refresh());
        RequestPreviewDraw();
        await PersistGroupingMetadataAsync();
        SetStatus(locked ? $"Group “{group.Name}” locked." : $"Group “{group.Name}” unlocked.");
    }

    private async void LayerGroupRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || FindLayerGroup(element.Tag) is not { } group) return;
        e.Handled = true;
        _layerGroupService.Remove(Layers.Where(layer => layer.GroupId == group.Id));
        LayerGroups.Remove(group);
        ConfigureLayerGrouping();
        await PersistGroupingMetadataAsync();
        SetStatus($"Removed group “{group.Name}”; its layers were kept.");
    }

    private void LayerGroupActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || FindLayerGroup(button.Tag) is not { } group) return;
        e.Handled = true;
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom, Style = (Style)FindResource("ThemedContextMenu") };
        foreach (System.Collections.DictionaryEntry resource in Resources)
            menu.Resources[resource.Key] = resource.Value;
        var rename = new MenuItem { Header = GetLanguageText("groups.rename", "Rename group") };
        rename.Click += async (_, _) => await RenameLayerGroupAsync(group);
        var color = new MenuItem { Header = GetLanguageText("groups.color", "Label Color") };
        var labelColors = new (string Name, string Value)[]
        {
            ("Blue", "#3B82F6"),
            ("Cyan", "#06B6D4"),
            ("Green", "#22C55E"),
            ("Yellow", "#EAB308"),
            ("Orange", "#F97316"),
            ("Red", "#EF4444"),
            ("Pink", "#EC4899"),
            ("Purple", "#8B5CF6")
        };
        foreach (var labelColor in labelColors)
        {
            var swatch = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(4),
                Background = (Brush)new BrushConverter().ConvertFromString(labelColor.Value)!,
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0)
            };
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(swatch);
            header.Children.Add(new TextBlock { Text = labelColor.Name, VerticalAlignment = VerticalAlignment.Center });

            var choice = new MenuItem
            {
                Header = header,
                IsCheckable = true,
                IsChecked = string.Equals(group.Color, labelColor.Value, StringComparison.OrdinalIgnoreCase),
                Style = (Style)FindResource("ThemedMenuItem")
            };
            choice.Click += async (_, _) =>
            {
                group.Color = labelColor.Value;
                foreach (var layer in Layers.Where(layer => layer.GroupId == group.Id)) layer.GroupColor = labelColor.Value;
                PreserveLayerGridScroll(() => LayerGrid.Items.Refresh());
                await PersistGroupingMetadataAsync();
            };
            color.Items.Add(choice);
        }
        var duplicate = new MenuItem { Header = GetLanguageText("groups.duplicate", "Duplicate group") };
        duplicate.Click += async (_, _) => await DuplicateLayerGroupAsync(group);
        var forward = new MenuItem { Header = GetLanguageText("groups.forward", "Bring group forward") };
        forward.Click += async (_, _) => await MoveLayerGroupOneStepAsync(group, "Up");
        var backward = new MenuItem { Header = GetLanguageText("groups.backward", "Send group backward") };
        backward.Click += async (_, _) => await MoveLayerGroupOneStepAsync(group, "Down");
        var remove = new MenuItem { Header = GetLanguageText("groups.remove", "Remove group (keep layers)") };
        remove.Click += (_, args) => LayerGroupRemoveButton_Click(button, args);
        foreach (var item in new[] { rename, color, duplicate, forward, backward, remove })
            item.Style = (Style)FindResource("ThemedMenuItem");
        menu.Items.Add(rename);
        menu.Items.Add(color);
        menu.Items.Add(duplicate);
        menu.Items.Add(new Separator { Style = (Style)FindResource("ThemedMenuSeparator") });
        menu.Items.Add(forward);
        menu.Items.Add(backward);
        menu.Items.Add(new Separator { Style = (Style)FindResource("ThemedMenuSeparator") });
        menu.Items.Add(remove);
        menu.IsOpen = true;
    }

    private async Task DuplicateLayerGroupAsync(LayerGroup sourceGroup)
    {
        var members = Layers.Where(layer => layer.GroupId == sourceGroup.Id && !layer.IsEditorMetadata)
            .OrderBy(layer => int.TryParse(layer.Index, out var index) ? index : int.MaxValue).ToList();
        if (members.Count == 0) return;
        try
        {
            SetBusy(true, GetLanguageText("status.duplicatingGroup", "Duplicating group..."));
            var target = await ResolveTemplateTargetAsync();
            foreach (var layer in members.OrderByDescending(layer => int.TryParse(layer.Index, out var index) ? index : -1))
                await _supporter.DuplicateLayerAsync(target.DeviceModel, target.TemplatePath, layer.Index);
            await LoadLayersAsync(true);
            var duplicateMembers = Layers.Where(layer => !layer.IsEditorMetadata && string.IsNullOrWhiteSpace(layer.GroupId))
                .OrderByDescending(layer => int.TryParse(layer.Index, out var index) ? index : -1)
                .Take(members.Count).ToList();
            var newGroup = new LayerGroup { Name = GetUniqueGroupName(sourceGroup.Name + " Copy"), Color = sourceGroup.Color };
            LayerGroups.Add(newGroup);
            foreach (var layer in duplicateMembers)
            {
                layer.GroupId = newGroup.Id;
                layer.GroupName = newGroup.Name;
                layer.GroupColor = newGroup.Color;
            }
            ConfigureLayerGrouping();
            await PersistGroupingMetadataAsync();
            SetBusy(false, GetLanguageText("status.groupDuplicated", "Group duplicated."));
        }
        catch (Exception ex)
        {
            AppLogger.Error("Layer group duplication failed.", ex);
            SetBusy(false, GetLanguageText("status.duplicateFailed", "Duplicate failed."));
        }
    }

    private string GetUniqueGroupName(string requested)
    {
        var name = requested;
        var suffix = 2;
        while (LayerGroups.Any(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{requested} ({suffix++})";
        return name;
    }

    private async Task MoveLayerGroupOneStepAsync(LayerGroup group, string direction)
    {
        SelectLayerGroupMembers(group, false);
        await MoveSelectedLayersOneStepAsync(direction);
    }

    private void BatchEditButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedLayers(includeLocked: true, includeAnimation: false);
        if (selected.Count == 0)
        {
            SetStatus(GetLanguageText("status.selectLayers", "Select two or more layers first."));
            return;
        }

        var colorCheck = new CheckBox { Content = GetLanguageText("batch.color", "Color"), Margin = new Thickness(0, 7, 12, 7), VerticalAlignment = VerticalAlignment.Center };
        var colorBox = new TextBox { Text = NormalizeColorText(selected[0].Color), MinWidth = 170, BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.SemiBold };
        var colorField = new Border { MinWidth = 170, Height = 34, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Padding = new Thickness(8, 0, 8, 0), Child = colorBox };
        colorField.SetResourceReference(Border.BorderBrushProperty, "BrBorder");
        ApplyColorFieldPreview(colorField, colorBox, colorBox.Text);
        var colorPick = new Button { Width = 38, Height = 34, Margin = new Thickness(10, 0, 0, 0), ToolTip = GetLanguageText("batch.pickColor", "Pick color"), Style = (Style)FindResource("LayerIconActionButton") };
        colorPick.Content = new System.Windows.Shapes.Path { Data = Geometry.Parse("M4,15 C7,7 11,4 15,4 C18,4 20,6 20,9 C20,13 16,16 12,16 H8 C6,16 5,16 4,15 Z M13,6 C12,9 10,11 7,14"), Stroke = (Brush)FindResource("BrTextPrimary"), StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Fill = Brushes.Transparent, Width = 20, Height = 20, Stretch = Stretch.Uniform };
        var colorEditor = new StackPanel { Orientation = Orientation.Horizontal };
        colorEditor.Children.Add(colorField); colorEditor.Children.Add(colorPick);
        var fontCheck = new CheckBox { Content = GetLanguageText("batch.font", "Font"), Margin = new Thickness(0, 5, 8, 5) };
        var fontItems = FontCombo.Items.OfType<ComboBoxItem>().Select(item => item.Content?.ToString() ?? "").Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        var fontBox = new ComboBox { IsEditable = true, Text = selected[0].Font, ItemsSource = fontItems, MinWidth = 255 };
        var sizeCheck = new CheckBox { Content = GetLanguageText("labels.size", "Size"), Margin = new Thickness(0, 5, 8, 5) };
        var sizeBox = new TextBox { Text = selected[0].Size, MinWidth = 255 };
        var boldCheck = new CheckBox { Content = GetLanguageText("common.bold", "Bold"), Margin = new Thickness(0, 5, 8, 5) };
        var boldValue = new CheckBox
        {
            Content = GetLanguageText("common.bold", "Bold"),
            IsChecked = string.Equals(selected[0].Bold, "True", StringComparison.OrdinalIgnoreCase),
            Margin = new Thickness(0, 5, 8, 5)
        };
        var offsetCheck = new CheckBox { Content = GetLanguageText("batch.offset", "Position offset"), Margin = new Thickness(0, 5, 8, 5) };
        var offsetPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var xBox = new TextBox { Text = "0", Width = 64, ToolTip = "X" };
        var yBox = new TextBox { Text = "0", Width = 64, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Y" };
        offsetPanel.Children.Add(xBox); offsetPanel.Children.Add(yBox);
        var visibility = new ComboBox { MinWidth = 255, ItemsSource = new[] { GetLanguageText("batch.keep", "Keep"), GetLanguageText("batch.show", "Show"), GetLanguageText("batch.hide", "Hide") }, SelectedIndex = 0 };
        var locking = new ComboBox { MinWidth = 255, ItemsSource = new[] { GetLanguageText("batch.keep", "Keep"), GetLanguageText("batch.unlock", "Unlock"), GetLanguageText("batch.lockAction", "Lock") }, SelectedIndex = 0 };
        var grid = new Grid { Margin = new Thickness(18, 12, 18, 18) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 8; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddBatchRow(grid, 0, colorCheck, colorEditor);
        AddBatchRow(grid, 1, fontCheck, fontBox);
        AddBatchRow(grid, 2, sizeCheck, sizeBox);
        AddBatchRow(grid, 3, boldCheck, boldValue);
        AddBatchRow(grid, 4, offsetCheck, offsetPanel);
        AddBatchRow(grid, 5, new TextBlock { Text = GetLanguageText("batch.visibility", "Visibility"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 5, 8, 5) }, visibility);
        AddBatchRow(grid, 6, new TextBlock { Text = GetLanguageText("batch.lock", "Lock"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 5, 8, 5) }, locking);
        var apply = new Button { Content = GetLanguageText("common.apply", "Apply"), Width = 96, IsDefault = true, Margin = new Thickness(8, 16, 0, 0), Style = (Style)FindResource("BtnPrimary") };
        var cancel = new Button { Content = GetLanguageText("common.cancel", "Cancel"), Width = 90, IsCancel = true, Margin = new Thickness(0, 16, 0, 0), Style = (Style)FindResource("BtnGhost") };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(cancel); actions.Children.Add(apply); Grid.SetRow(actions, 7); Grid.SetColumnSpan(actions, 2); grid.Children.Add(actions);
        var dialog = CreateThemedDialog(GetLanguageText("batch.title", "Edit selected layers"), grid, 500);
        colorPick.Click += (_, _) =>
        {
            var value = ColorPickerDialog.ShowDialog(dialog, colorBox.Text);
            if (string.IsNullOrWhiteSpace(value)) return;
            colorBox.Text = NormalizeColorText(value);
            ApplyColorFieldPreview(colorField, colorBox, value);
            colorCheck.IsChecked = true;
        };
        colorBox.TextChanged += (_, _) => ApplyColorFieldPreview(colorField, colorBox, colorBox.Text);
        sizeBox.TextChanged += (_, _) => sizeCheck.IsChecked = true;
        boldValue.Checked += (_, _) => boldCheck.IsChecked = true;
        boldValue.Unchecked += (_, _) => boldCheck.IsChecked = true;
        apply.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() != true) return;

        PushUndoState(GetLanguageText("history.batchEdit", "Batch edit"));
        var dx = int.TryParse(xBox.Text, out var parsedX) ? parsedX : 0;
        var dy = int.TryParse(yBox.Text, out var parsedY) ? parsedY : 0;
        foreach (var layer in selected)
        {
            if (colorCheck.IsChecked == true && layer.CanWriteFont("color")) layer.Color = NormalizeColorText(colorBox.Text);
            if (fontCheck.IsChecked == true && layer.CanWriteFont("name")) layer.Font = ResolveCanonicalFontName(fontBox.Text);
            if (sizeCheck.IsChecked == true && layer.CanWriteFont("size")) layer.Size = sizeBox.Text;
            if (boldCheck.IsChecked == true && layer.CanWriteFont("isBold")) layer.Bold = boldValue.IsChecked == true ? "True" : "False";
            if (offsetCheck.IsChecked == true)
            {
                layer.X = ((TryParseInt(layer.X, out var x) ? x : 0) + dx).ToString(CultureInfo.InvariantCulture);
                layer.Y = ((TryParseInt(layer.Y, out var y) ? y : 0) + dy).ToString(CultureInfo.InvariantCulture);
            }
            if (visibility.SelectedIndex == 1) layer.Hide = "False";
            if (visibility.SelectedIndex == 2) layer.Hide = "True";
            if (locking.SelectedIndex > 0) layer.IsLocked = locking.SelectedIndex == 2;
            MarkLayerDirty(layer);
        }
        PopulateEditorFromSelection();
        PreserveLayerGridScroll(() => LayerGrid.Items.Refresh());
        RequestPreviewDraw();
        SetStatus(GetLanguageText("status.batchEdited", "Selected layers updated. Press Apply to save."));
    }

    private static void AddBatchRow(Grid grid, int row, UIElement label, UIElement editor)
    {
        Grid.SetRow(label, row); Grid.SetColumn(label, 0); grid.Children.Add(label);
        Grid.SetRow(editor, row); Grid.SetColumn(editor, 1); grid.Children.Add(editor);
    }

    private void ApplyColorFieldPreview(Border field, TextBox textBox, string colorText)
    {
        var normalized = NormalizeColorText(colorText);
        var brush = NewBrush(normalized, normalized);
        field.Background = brush;
        if (brush is SolidColorBrush solid)
        {
            var luminance = (0.299 * solid.Color.R + 0.587 * solid.Color.G + 0.114 * solid.Color.B) / 255.0;
            textBox.Foreground = luminance > 0.55 ? Brushes.Black : Brushes.White;
        }
        else
        {
            textBox.SetResourceReference(Control.ForegroundProperty, "BrTextPrimary");
        }
    }

    private Window CreateThemedDialog(string title, UIElement content, double width)
    {
        var dialog = new Window
        {
            Owner = this,
            Width = width,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };
        foreach (System.Collections.DictionaryEntry resource in Resources)
            dialog.Resources[resource.Key] = resource.Value;
        dialog.SetResourceReference(Control.ForegroundProperty, "BrTextPrimary");
        dialog.FontFamily = FontFamily;

        var root = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(1) };
        root.SetResourceReference(Border.BackgroundProperty, "GlassPopupBrush");
        root.SetResourceReference(Border.BorderBrushProperty, "BrBorderSoft");
        root.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Direction = 270, Opacity = 0.55, Color = Colors.Black };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Border { CornerRadius = new CornerRadius(9, 9, 0, 0), Padding = new Thickness(16, 0, 9, 0), Cursor = Cursors.SizeAll };
        header.SetResourceReference(Border.BackgroundProperty, "GlassToolbarBrush");
        var headerGrid = new Grid();
        headerGrid.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var closeIcon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M5,5 L15,15 M15,5 L5,15"),
            Stroke = (Brush)FindResource("BrTextPrimary"),
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform
        };
        var close = new Button { Content = closeIcon, Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Style = (Style)FindResource("LayerIconActionButton"), IsCancel = true, ToolTip = GetLanguageText("common.close", "Close") };
        close.Click += (_, _) => dialog.DialogResult = false;
        headerGrid.Children.Add(close); header.Child = headerGrid;
        header.MouseLeftButtonDown += (_, args) => { if (args.LeftButton == MouseButtonState.Pressed) dialog.DragMove(); };
        layout.Children.Add(header);
        Grid.SetRow(content, 1); layout.Children.Add(content);
        root.Child = layout; dialog.Content = root;
        return dialog;
    }

    private void AlignLayersButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string mode }) return;
        var selected = GetSelectedLayers(includeLocked: false, includeAnimation: false);
        var minimum = mode.StartsWith("Distribute", StringComparison.Ordinal) ||
                      mode is "MatchX" or "MatchY"
            ? 2
            : 1;
        if (selected.Count < minimum)
        {
            SetStatus(FormatLanguageText("status.alignmentSelection", "Select at least {0} unlocked layers.", minimum));
            return;
        }
        PushUndoState(GetLanguageText("history.align", "Align layers"));
        var bounds = selected.ToDictionary(layer => layer, GetLayerSelectionBounds);
        if (mode is "MatchX" or "MatchY")
        {
            var reference = LayerGrid.SelectedItem is LayerRow active && selected.Contains(active)
                ? active
                : selected[0];
            var target = GetPreviewDragPosition(reference);
            foreach (var layer in selected)
            {
                if (ReferenceEquals(layer, reference))
                {
                    continue;
                }

                var current = GetPreviewDragPosition(layer);
                SetPreviewDragPosition(
                    layer,
                    (int)Math.Round(mode == "MatchX" ? target.X : current.X),
                    (int)Math.Round(mode == "MatchY" ? target.Y : current.Y));
            }
        }
        else if (mode == "DistributeX" || mode == "DistributeY")
        {
            var horizontal = mode == "DistributeX";
            var ordered = selected.OrderBy(layer => horizontal ? bounds[layer].Left : bounds[layer].Top).ToList();
            var start = 0.0;
            var end = horizontal ? _previewCanvasWidth : _previewCanvasHeight;
            var totalSize = ordered.Sum(layer => horizontal ? bounds[layer].Width : bounds[layer].Height);
            var gap = (end - start - totalSize) / (ordered.Count - 1);
            var cursor = start;
            foreach (var layer in ordered)
            {
                MoveLayerBoundsTo(layer, bounds[layer], horizontal ? cursor : bounds[layer].Left, horizontal ? bounds[layer].Top : cursor);
                cursor += (horizontal ? bounds[layer].Width : bounds[layer].Height) + gap;
            }
        }
        else
        {
            foreach (var layer in selected)
            {
                var current = bounds[layer];
                var left = mode switch
                {
                    "Left" => 0,
                    "CenterX" => (_previewCanvasWidth - current.Width) / 2,
                    "Right" => _previewCanvasWidth - current.Width,
                    _ => current.Left
                };
                var top = mode switch
                {
                    "Top" => 0,
                    "CenterY" => (_previewCanvasHeight - current.Height) / 2,
                    "Bottom" => _previewCanvasHeight - current.Height,
                    _ => current.Top
                };
                MoveLayerBoundsTo(layer, current, left, top);
            }
        }
        foreach (var layer in selected) MarkLayerDirty(layer);
        PopulateEditorFromSelection();
        RequestPreviewDraw();
        SetStatus(GetLanguageText("status.layersAligned", "Layers aligned. Press Apply to save."));
    }

    private void MoveLayerBoundsTo(LayerRow layer, Rect currentBounds, double targetLeft, double targetTop)
    {
        var dx = ToTemplate(targetLeft - currentBounds.Left);
        var dy = ToTemplate(targetTop - currentBounds.Top);
        var x = double.TryParse(layer.X, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedX) ? parsedX : 0;
        var y = double.TryParse(layer.Y, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedY) ? parsedY : 0;
        layer.X = Math.Round(x + dx).ToString(CultureInfo.InvariantCulture);
        layer.Y = Math.Round(y + dy).ToString(CultureInfo.InvariantCulture);
    }

    private void LayerGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not LayerRow layer || layer.IsEditorMetadata)
        {
            LayerGrid.ContextMenu?.Items.Clear();
            e.Handled = true;
            return;
        }

        if (!LayerGrid.SelectedItems.Contains(layer))
        {
            LayerGrid.SelectedItems.Clear();
            LayerGrid.SelectedItems.Add(layer);
            LayerGrid.SelectedItem = layer;
            PopulateEditorFromSelection();
            RequestPreviewDraw();
        }

        var menu = LayerGrid.ContextMenu ?? new ContextMenu();
        menu.Style = (Style)FindResource("ThemedContextMenu");
        foreach (System.Collections.DictionaryEntry resource in Resources)
            menu.Resources[resource.Key] = resource.Value;
        PopulateLayerGroupContextMenu(menu);
        LayerGrid.ContextMenu = menu;
    }

    private void PopulateLayerGroupContextMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        var selected = GetSelectedLayers(includeLocked: true, includeAnimation: false);
        var hasSelection = selected.Count > 0;
        var removeFromGroup = new MenuItem
        {
            Header = GetLanguageText("groups.removeFromGroup", "Remove from Group"),
            IsEnabled = hasSelection && selected.Any(layer => !string.IsNullOrWhiteSpace(layer.GroupId)),
            Style = (Style)FindResource("ThemedMenuItem")
        };
        removeFromGroup.Click += async (_, _) => await RemoveSelectedLayersFromGroupsAsync();
        menu.Items.Add(removeFromGroup);

        var moveToGroup = new MenuItem
        {
            Header = GetLanguageText("groups.moveToGroup", "Move to Group"),
            IsEnabled = hasSelection && LayerGroups.Count > 0,
            Style = (Style)FindResource("ThemedMenuItem")
        };
        foreach (var group in LayerGroups.OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var targetGroup = group;
            var item = new MenuItem
            {
                Header = targetGroup.Name,
                IsCheckable = true,
                IsChecked = selected.Count > 0 && selected.All(layer => layer.GroupId == targetGroup.Id),
                Style = (Style)FindResource("ThemedMenuItem")
            };
            item.Click += async (_, _) => await MoveSelectedLayersToGroupAsync(targetGroup);
            moveToGroup.Items.Add(item);
        }
        menu.Items.Add(moveToGroup);
    }

    private async Task RemoveSelectedLayersFromGroupsAsync()
    {
        var selected = GetSelectedLayers(includeLocked: true, includeAnimation: false)
            .Where(layer => !string.IsNullOrWhiteSpace(layer.GroupId))
            .ToList();
        if (selected.Count == 0) return;

        var previousGroupIds = _layerGroupService.Remove(selected);

        RemoveEmptyLayerGroups(previousGroupIds);
        ConfigureLayerGrouping();
        await PersistGroupingMetadataAsync();
        SetStatus(selected.Count == 1
            ? GetLanguageText("status.layerRemovedFromGroup", "Layer removed from group.")
            : FormatLanguageText("status.layersRemovedFromGroups", "{0} layers removed from their groups.", selected.Count));
    }

    private async Task MoveSelectedLayersToGroupAsync(LayerGroup targetGroup)
    {
        var selected = GetSelectedLayers(includeLocked: true, includeAnimation: false);
        if (selected.Count == 0 || !LayerGroups.Contains(targetGroup)) return;

        var previousGroupIds = _layerGroupService.Assign(selected, targetGroup);

        RemoveEmptyLayerGroups(previousGroupIds);
        ConfigureLayerGrouping();
        await PersistGroupingMetadataAsync();
        SetStatus(selected.Count == 1
            ? FormatLanguageText("status.layerMovedToGroup", "Layer moved to group '{0}'.", targetGroup.Name)
            : FormatLanguageText("status.layersMovedToGroup", "{0} layers moved to group '{1}'.", selected.Count, targetGroup.Name));
    }

    private void RemoveEmptyLayerGroups(IEnumerable<string> groupIds)
    {
        foreach (var groupId in groupIds.Distinct().ToList())
        {
            if (Layers.Any(layer => layer.GroupId == groupId)) continue;

            var group = LayerGroups.FirstOrDefault(item => item.Id == groupId);
            if (group != null)
            {
                LayerGroups.Remove(group);
            }
        }
    }

    private void LayerGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None ||
            FindVisualParent<Button>(e.OriginalSource as DependencyObject) != null)
        {
            return;
        }

        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not LayerRow layer || !LayerGrid.SelectedItems.Contains(layer))
        {
            return;
        }

        LayerGrid.UnselectAll();
        PopulateEditorFromSelection();
        RequestPreviewDraw();
        e.Handled = true;
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

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) return null;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;

            var nested = FindVisualChild<T>(child);
            if (nested != null) return nested;
        }

        return null;
    }

    private void PreviewCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        PreviewCanvas.Focus();
        var point = e.GetPosition(PreviewCanvas);
        var hit = Layers
            .Where(layer => !string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
            .Where(layer => !string.Equals(layer.Hide, "True", StringComparison.OrdinalIgnoreCase))
            .LastOrDefault(layer => GetLayerSelectionBounds(layer).Contains(point));

        if (hit != null)
        {
            SelectLayerForContext(hit, Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }

        PreviewLayerContextMenu.PlacementTarget = PreviewCanvas;
        PreviewLayerContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void SelectLayerForContext(LayerRow layer, bool extendSelection)
    {
        if (!extendSelection && !LayerGrid.SelectedItems.Contains(layer))
        {
            LayerGrid.SelectedItems.Clear();
        }

        if (!LayerGrid.SelectedItems.Contains(layer))
        {
            LayerGrid.SelectedItems.Add(layer);
        }

        LayerGrid.SelectedItem = layer;
        LayerGrid.ScrollIntoView(layer);
        PopulateEditorFromSelection();
        RequestPreviewDraw();
    }

    private void TogglePreviewLayerSelection(LayerRow layer, bool removeWhenSelected)
    {
        if (layer.IsLocked ||
            string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (LayerGrid.SelectedItems.Contains(layer))
        {
            if (removeWhenSelected && LayerGrid.SelectedItems.Count > 1)
            {
                LayerGrid.SelectedItems.Remove(layer);
            }
            else
            {
                LayerGrid.CurrentItem = layer;
            }
        }
        else
        {
            LayerGrid.SelectedItems.Add(layer);
            LayerGrid.CurrentItem = layer;
        }

        LayerGrid.ScrollIntoView(layer);
        PopulateEditorFromSelection();
        RequestPreviewDraw();
    }

    private void PreviewLayerContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedLayers();
        foreach (var item in PreviewLayerContextMenu.Items.OfType<MenuItem>())
        {
            item.IsEnabled = selected.Count > 0;
            if (item.IsCheckable)
            {
                item.IsChecked = _soloSelectedLayers;
            }
        }
    }

    private async void PreviewDuplicateMenu_Click(object sender, RoutedEventArgs e) => await DuplicateSelectedLayersAsync();

    private void PreviewHideMenu_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedLayers(includeLocked: true, includeAnimation: true);
        if (selected.Count == 0) return;

        PushUndoState(GetLanguageText("history.visibility", "Change layer visibility"));
        var shouldHide = selected.Any(layer => !string.Equals(layer.Hide, "True", StringComparison.OrdinalIgnoreCase));
        foreach (var layer in selected)
        {
            layer.Hide = shouldHide ? "True" : "False";
            MarkLayerDirty(layer);
        }

        PreserveLayerGridScroll(() => LayerGrid.Items.Refresh());
        PopulateEditorFromSelection();
        RequestPreviewDraw();
        SetStatus(shouldHide ? "Selected layer(s) hidden. Press Apply to save." : "Selected layer(s) shown. Press Apply to save.");
    }

    private void PreviewLockMenu_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedLayers(includeLocked: true, includeAnimation: false);
        if (selected.Count == 0) return;

        var shouldLock = selected.Any(layer => !layer.IsLocked);
        foreach (var layer in selected)
        {
            layer.IsLocked = shouldLock;
            var key = GetLayerLockKey(_currentTemplatePath, layer.Index);
            if (shouldLock)
            {
                _lockedLayerKeys.Add(key);
            }
            else
            {
                _lockedLayerKeys.Remove(key);
            }
        }

        PreserveLayerGridScroll(() => LayerGrid.Items.Refresh());
        RequestPreviewDraw();
        SetStatus(shouldLock ? "Selected layer(s) locked in preview." : "Selected layer(s) unlocked in preview.");
    }

    private async void PreviewBringForwardMenu_Click(object sender, RoutedEventArgs e) => await MoveSelectedLayersOneStepAsync("Up");

    private async void PreviewSendBackwardMenu_Click(object sender, RoutedEventArgs e) => await MoveSelectedLayersOneStepAsync("Down");

    private void PreviewSoloMenu_Click(object sender, RoutedEventArgs e)
    {
        SetSoloLayerMode(!_soloSelectedLayers);
    }

    private string GetPreviewText(LayerRow layer)
    {
        var text = NormalizeLConnectText(layer.Text ?? "");
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

    private FrameworkElement CreatePreviewImage(
        string imagePath,
        double width,
        double height,
        bool selected,
        string rotationText = "0")
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
            var image = new Image
            {
                Source = GetCachedPreviewImage(imagePath),
                Stretch = Stretch.Uniform,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            if (double.TryParse(rotationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var rotation))
            {
                image.RenderTransform = new RotateTransform(rotation);
            }
            border.Child = image;
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
        var outerWidth = width;
        var outerHeight = height;
        var logicalWidth = double.TryParse(layer.Width, out var templateWidth) && templateWidth > 0
            ? ToPreview(templateWidth)
            : width;
        var logicalHeight = double.TryParse(layer.Height, out var templateHeight) && templateHeight > 0
            ? ToPreview(templateHeight)
            : height;
        var type = layer.Type ?? "";
        var layoutDirection = GetGraphDirection(layer);
        var isDirectionalBar =
            type.Contains("StatuBar", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("DynamicBar", StringComparison.OrdinalIgnoreCase);
        if (isDirectionalBar && layoutDirection is 2 or 3)
        {
            (logicalWidth, logicalHeight) = (logicalHeight, logicalWidth);
        }
        var isRectangularGraph =
            type.Contains("StatuBar", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("DynamicBar", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("GraphLine", StringComparison.OrdinalIgnoreCase);
        var contentLeft = isRectangularGraph ? Math.Max(0, (outerWidth - logicalWidth) / 2) : 0;
        var contentTop = isRectangularGraph ? Math.Max(0, (outerHeight - logicalHeight) / 2) : 0;
        if (isRectangularGraph)
        {
            width = logicalWidth;
            height = logicalHeight;
        }

        var canvas = new Canvas
        {
            Width = outerWidth,
            Height = outerHeight,
            Background = Brushes.Transparent
        };
        var graphStyle = layer.GraphStyle ?? "";
        var h2Style = GetH2GraphPreviewStyle(layer);

        if (h2Style.StartsWith("donut", StringComparison.OrdinalIgnoreCase) || type.Contains("Arch", StringComparison.OrdinalIgnoreCase))
        {
            double.TryParse(layer.Thickness, out var thickVal);
            var thickness = thickVal > 0 ? Math.Max(2.0, ToPreview(thickVal)) : Math.Max(8.0, Math.Min(width, height) * 0.12);
            double.TryParse(layer.BorderWidth, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedBorderWidth);
            var borderThickness = parsedBorderWidth > 0 ? Math.Max(0.75, ToPreview(parsedBorderWidth)) : 1.0;
            var padding = Math.Max(2.0, borderThickness + 1.0);
            var centerDiameter = Math.Max(4.0, Math.Min(width, height) - thickness - (padding * 2.0));
            var center = new Point(width / 2.0, height / 2.0);
            var circleLeft = center.X - centerDiameter / 2.0;
            var circleTop = center.Y - centerDiameter / 2.0;

            var drawBackground = string.Equals(layer.FillBack, "True", StringComparison.OrdinalIgnoreCase);
            var back = new Ellipse
            {
                Width = centerDiameter,
                Height = centerDiameter,
                Stroke = drawBackground ? NewBrush(layer.BackColor, "#55FFFFFF") : Brushes.Transparent,
                StrokeThickness = thickness,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Canvas.SetLeft(back, circleLeft);
            Canvas.SetTop(back, circleTop);
            canvas.Children.Add(back);

            PenLineCap lineCap = string.Equals(layer.Round, "True", StringComparison.OrdinalIgnoreCase)
                ? PenLineCap.Round
                : PenLineCap.Flat;
            var isSubsection = string.Equals(layer.UseSubsection, "True", StringComparison.OrdinalIgnoreCase);
            if (isSubsection)
            {
                lineCap = PenLineCap.Flat;
            }
            else if (h2Style == "donut3")
            {
                lineCap = PenLineCap.Round;
            }

            DrawCurvedGraphProgress(
                canvas,
                layer,
                center,
                centerDiameter / 2.0,
                thickness,
                lineCap,
                isSubsection || h2Style == "donut1" || string.Equals(layer.UseBlock, "True", StringComparison.OrdinalIgnoreCase));

            if (string.Equals(layer.RingBorder, "True", StringComparison.OrdinalIgnoreCase))
            {
                var borderBrush = NewBrush(layer.BorderColor, layer.BackColor);
                var outerDiameter = Math.Max(2.0, centerDiameter + thickness);
                var outerBorder = new Ellipse
                {
                    Width = outerDiameter,
                    Height = outerDiameter,
                    Stroke = borderBrush,
                    StrokeThickness = borderThickness
                };
                Canvas.SetLeft(outerBorder, center.X - outerDiameter / 2.0);
                Canvas.SetTop(outerBorder, center.Y - outerDiameter / 2.0);
                canvas.Children.Add(outerBorder);

                var innerDiameter = Math.Max(2.0, centerDiameter - thickness);
                var innerBorder = new Ellipse
                {
                    Width = innerDiameter,
                    Height = innerDiameter,
                    Stroke = borderBrush,
                    StrokeThickness = borderThickness
                };
                Canvas.SetLeft(innerBorder, center.X - innerDiameter / 2.0);
                Canvas.SetTop(innerBorder, center.Y - innerDiameter / 2.0);
                canvas.Children.Add(innerBorder);
            }
        }
        else
        {
            double.TryParse(layer.Radius, out var radVal);
            var rad = radVal >= 0 ? ToPreview(radVal) : 4.0;
            var cornerRadius = ClampPreviewCornerRadius(rad, width, height);

            var barBg = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(cornerRadius),
                Background = string.Equals(layer.TransparentBackground, "True", StringComparison.OrdinalIgnoreCase) ||
                             !string.Equals(layer.FillBack, "True", StringComparison.OrdinalIgnoreCase)
                    ? Brushes.Transparent
                    : ApplyBrushAlpha(NewBrush(layer.BackColor, "#20FFFFFF"), layer.BackAlpha, zeroMeansUnspecified: true),
                BorderBrush = string.Equals(layer.TransparentBackground, "True", StringComparison.OrdinalIgnoreCase)
                    ? Brushes.Transparent
                    : ApplyBrushAlpha(NewBrush(layer.BackColor, "#20242A"), layer.BackAlpha, zeroMeansUnspecified: true),
                BorderThickness = string.Equals(layer.TransparentBackground, "True", StringComparison.OrdinalIgnoreCase)
                    ? new Thickness(0)
                    : new Thickness(1)
            };
            
            var barGrid = new Grid
            {
                Width = width,
                Height = height
            };
            barGrid.Children.Add(barBg);

            if (type.Contains("DynamicBar", StringComparison.OrdinalIgnoreCase))
            {
                DrawDynamicStatusPreview(barGrid, layer, width, height, cornerRadius);
            }
            else if (h2Style == "stream")
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
                area.Fill = ApplyBrushAlpha(NewBrush(layer.FillColor, "#40000000"), layer.Transparent);
                area.Stroke = NewBrush(layer.LineColor, layer.FrontColor);
                area.StrokeThickness = Math.Max(1.0, double.TryParse(layer.LineWidth, out var streamLine) ? ToPreview(streamLine) : 1.0);
                if (string.Equals(layer.InvertDirection, "True", StringComparison.OrdinalIgnoreCase))
                {
                    area.RenderTransformOrigin = new Point(0.5, 0.5);
                    area.RenderTransform = new ScaleTransform(-1, 1);
                }
                streamCanvas.Children.Add(area);
                barGrid.Children.Add(streamCanvas);
            }
            else if (h2Style == "bar2" &&
                     !string.Equals(layer.UseSubsection, "True", StringComparison.OrdinalIgnoreCase))
            {
                var barFill = CreateDirectionalGraphFill(layer, width, height, cornerRadius);
                barGrid.Children.Add(barFill);
            }
            else if (type.Contains("StatuBar") || type.Contains("DynamicBar"))
            {
                bool isSegmented = string.Equals(layer.UseSubsection, "True", StringComparison.OrdinalIgnoreCase);
                
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
                    var direction = GetGraphDirection(layer);
                    var vertical = direction is 2 or 3;
                    var availableLength = vertical ? height : width;
                    int count = Math.Max(1, (int)(availableLength / (segW + gap)));
                    int fillCount = (int)Math.Ceiling(count * GetGraphPreviewRatio(layer));

                    for (int s = 0; s < count; s++)
                    {
                        var filled = direction is 1 or 3 ? s >= count - fillCount : s < fillCount;
                        var segmentWidth = vertical ? width : segW;
                        var segmentHeight = vertical ? segW : height;
                        var seg = new Border
                        {
                            Width = segmentWidth,
                            Height = segmentHeight,
                            CornerRadius = GetSegmentCornerRadius(
                                s,
                                count,
                                vertical,
                                ClampPreviewCornerRadius(rad, segmentWidth, segmentHeight)),
                            Background = filled
                                ? CreateGraphFill(layer)
                                : !string.Equals(layer.TransparentBackground, "True", StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(layer.FillBack, "True", StringComparison.OrdinalIgnoreCase)
                                    ? ApplyBrushAlpha(NewBrush(layer.BackColor, "#30303030"), layer.BackAlpha, zeroMeansUnspecified: true)
                                    : Brushes.Transparent
                        };
                        Canvas.SetLeft(seg, vertical ? 0 : s * (segW + gap));
                        Canvas.SetTop(seg, vertical ? s * (segW + gap) : 0);
                        segCanvas.Children.Add(seg);
                    }
                    barGrid.Children.Add(segCanvas);
                }
                else
                {
                    var barFill = CreateDirectionalGraphFill(
                        layer,
                        width,
                        height,
                        cornerRadius,
                        GetGraphPreviewRatio(layer));
                    barGrid.Children.Add(barFill);
                }
            }
            else
            {
                var barFill = CreateDirectionalGraphFill(layer, width, height, cornerRadius, GetGraphPreviewRatio(layer));
                barGrid.Children.Add(barFill);
            }
            Canvas.SetLeft(barGrid, contentLeft);
            Canvas.SetTop(barGrid, contentTop);
            canvas.Children.Add(barGrid);
        }

        var border = new Border
        {
            Width = outerWidth,
            Height = outerHeight,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Child = canvas
        };
        return border;
    }

    private void DrawCurvedGraphProgress(
        Canvas canvas,
        LayerRow layer,
        Point center,
        double radius,
        double thickness,
        PenLineCap lineCap,
        bool segmented)
    {
        var ratio = GetGraphPreviewRatio(layer);
        if (ratio <= 0)
        {
            return;
        }

        var startAngle = TryParseInvariant(layer.StartPercentage, out var parsedStart)
            ? parsedStart * 3.6 - 90.0
            : -90.0;
        var totalAngle = TryParseInvariant(layer.TotalAngle, out var parsedTotal) && parsedTotal > 0
            ? Math.Clamp(parsedTotal, 1.0, 360.0)
            : 360.0;
        var sweepAngle = Math.Clamp(totalAngle * ratio, 0.0, 359.9);
        var brush = CreateGraphFill(layer);

        if (!segmented)
        {
            AddArcPath(canvas, center, radius, startAngle, sweepAngle, brush, thickness, lineCap);
            return;
        }

        var circumference = Math.Max(1.0, 2.0 * Math.PI * radius);
        var blockPixels = TryParseInvariant(layer.SplitBlockWidth, out var blockWidth) && blockWidth > 0
            ? Math.Max(2.0, ToPreview(blockWidth))
            : Math.Max(8.0, thickness * 1.25);
        var gapPixels = TryParseInvariant(layer.SplitBlankWidth, out var gapWidth) && gapWidth > 0
            ? Math.Max(1.0, ToPreview(gapWidth))
            : Math.Max(2.0, thickness * 0.28);
        var blockDegrees = blockPixels / circumference * 360.0;
        var gapDegrees = gapPixels / circumference * 360.0;
        var cursor = 0.0;
        while (cursor < sweepAngle - 0.1)
        {
            var segmentSweep = Math.Min(blockDegrees, sweepAngle - cursor);
            if (segmentSweep > 0.2)
            {
                AddArcPath(canvas, center, radius, startAngle + cursor, segmentSweep, brush, thickness, PenLineCap.Flat);
            }

            cursor += blockDegrees + gapDegrees;
        }
    }

    private static void AddArcPath(
        Canvas canvas,
        Point center,
        double radius,
        double startAngle,
        double sweepAngle,
        Brush stroke,
        double strokeThickness,
        PenLineCap lineCap)
    {
        if (sweepAngle <= 0 || radius <= 0)
        {
            return;
        }

        sweepAngle = Math.Clamp(sweepAngle, 0.0, 359.9);
        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, startAngle + sweepAngle);
        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false
        };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweepAngle > 180.0
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stroke = stroke,
            StrokeThickness = strokeThickness,
            StrokeStartLineCap = lineCap,
            StrokeEndLineCap = lineCap,
            StrokeLineJoin = PenLineJoin.Round
        });
    }

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return new Point(
            center.X + Math.Cos(radians) * radius,
            center.Y + Math.Sin(radians) * radius);
    }

    private static bool TryParseInvariant(string? value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
        double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);

    private static int GetGraphDirection(LayerRow layer)
    {
        return int.TryParse(layer.Direction, out var direction) ? Math.Clamp(direction, 0, 3) : 0;
    }

    private static double GetGraphPreviewRatio(LayerRow layer)
    {
        var numericText = Regex.Match(layer.Text ?? "", @"[-+]?\d+(?:[.,]\d+)?").Value;
        var value = double.TryParse(
            numericText.Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsedValue) ? parsedValue : 65.0;
        var maximum = double.TryParse(
            layer.MaxValue,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsedMaximum) && parsedMaximum > 0 ? parsedMaximum : 100.0;
        return Math.Clamp(value / maximum, 0.0, 1.0);
    }

    private void DrawDynamicStatusPreview(
        Grid host,
        LayerRow layer,
        double width,
        double height,
        double radius)
    {
        var direction = GetGraphDirection(layer);
        var ratio = GetGraphPreviewRatio(layer);
        var vertical = direction is 2 or 3;
        var subsection = string.Equals(layer.UseSubsection, "True", StringComparison.OrdinalIgnoreCase);
        var canvas = new Canvas { Width = width, Height = height, ClipToBounds = false };

        if (subsection)
        {
            var block = double.TryParse(layer.SplitBlockWidth, out var blockValue)
                ? Math.Max(1, ToPreview(blockValue))
                : 5;
            var gap = double.TryParse(layer.SplitBlankWidth, out var gapValue)
                ? Math.Max(0.5, ToPreview(gapValue))
                : 1;
            var length = vertical ? height : width;
            var count = Math.Max(1, (int)Math.Floor((length + gap) / (block + gap)));
            var filledCount = (int)Math.Ceiling(count * ratio);
            for (var index = 0; index < count; index++)
            {
                var filled = direction is 1 or 3
                    ? index >= count - filledCount
                    : index < filledCount;
                var segmentWidth = vertical ? width : block;
                var segmentHeight = vertical ? block : height;
                var segment = new Border
                {
                    Width = segmentWidth,
                    Height = segmentHeight,
                    CornerRadius = GetSegmentCornerRadius(
                        index,
                        count,
                        vertical,
                        ClampPreviewCornerRadius(radius, segmentWidth, segmentHeight)),
                    Background = filled
                        ? CreateGraphFill(layer)
                        : !string.Equals(layer.TransparentBackground, "True", StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(layer.FillBack, "True", StringComparison.OrdinalIgnoreCase)
                            ? ApplyBrushAlpha(NewBrush(layer.BackColor, "#20FFFFFF"), layer.BackAlpha, zeroMeansUnspecified: true)
                            : Brushes.Transparent
                };
                Canvas.SetLeft(segment, vertical ? 0 : index * (block + gap));
                Canvas.SetTop(segment, vertical ? index * (block + gap) : 0);
                canvas.Children.Add(segment);
            }
        }
        else
        {
            var fillHost = new Grid { Width = width, Height = height };
            fillHost.Children.Add(CreateDirectionalGraphFill(layer, width, height, radius, ratio));
            canvas.Children.Add(fillHost);
        }

        var knobDiameter = double.TryParse(layer.InnerCircleRadius, out var knobValue)
            ? Math.Max(2, ToPreview(knobValue))
            : Math.Max(height * 1.5, 8);
        var knob = new Ellipse
        {
            Width = knobDiameter,
            Height = knobDiameter,
            Fill = string.Equals(layer.UseGradient, "True", StringComparison.OrdinalIgnoreCase)
                ? NewBrush(layer.GradientColor, layer.FrontColor)
                : NewBrush(layer.FrontColor, "#FFFFFF"),
            Stroke = NewBrush(layer.BackColor, "#40FFFFFF"),
            StrokeThickness = Math.Max(0.5, ToPreview(
                double.TryParse(layer.LineWidth, out var lineWidth) ? lineWidth : 1))
        };

        var progress = vertical ? height * ratio : width * ratio;
        var knobLeft = direction switch
        {
            1 => width - progress - knobDiameter / 2,
            2 or 3 => (width - knobDiameter) / 2,
            _ => progress - knobDiameter / 2
        };
        var knobTop = direction switch
        {
            2 => progress - knobDiameter / 2,
            3 => height - progress - knobDiameter / 2,
            _ => (height - knobDiameter) / 2
        };
        Canvas.SetLeft(knob, knobLeft);
        Canvas.SetTop(knob, knobTop);
        canvas.Children.Add(knob);
        host.Children.Add(canvas);
    }

    private Border CreateDirectionalGraphFill(
        LayerRow layer,
        double width,
        double height,
        double radius,
        double ratio = 0.65)
    {
        var direction = GetGraphDirection(layer);
        var vertical = direction is 2 or 3;
        var fillWidth = vertical ? width : width * ratio;
        var fillHeight = vertical ? height * ratio : height;
        return new Border
        {
            Width = fillWidth,
            Height = fillHeight,
            CornerRadius = new CornerRadius(ClampPreviewCornerRadius(radius, fillWidth, fillHeight)),
            Background = CreateGraphFill(layer),
            HorizontalAlignment = direction == 1 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            VerticalAlignment = direction == 3 ? VerticalAlignment.Bottom : VerticalAlignment.Top
        };
    }

    private static double ClampPreviewCornerRadius(double radius, double width, double height)
    {
        return Math.Max(0, Math.Min(radius, Math.Min(width, height) / 2.0));
    }

    private static CornerRadius GetSegmentCornerRadius(int index, int count, bool vertical, double radius)
    {
        if (count <= 1)
        {
            return new CornerRadius(radius);
        }

        if (vertical)
        {
            if (index == 0) return new CornerRadius(radius, radius, 0, 0);
            if (index == count - 1) return new CornerRadius(0, 0, radius, radius);
        }
        else
        {
            if (index == 0) return new CornerRadius(radius, 0, 0, radius);
            if (index == count - 1) return new CornerRadius(0, radius, radius, 0);
        }

        return new CornerRadius(0);
    }

    private string ResolveLayerMediaPath(LayerRow layer)
    {
        if (!string.IsNullOrWhiteSpace(layer.MediaPath) && File.Exists(layer.MediaPath))
        {
            return layer.MediaPath;
        }
        return ResolveLayerMediaPath(layer.Media);
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
                PushUndoState(GetLanguageText("history.opacity", "Change opacity"));
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

    private void AttachColorTextBoxPreview(params TextBox[] textBoxes)
    {
        foreach (var textBox in textBoxes.Distinct())
        {
            textBox.TextChanged += (_, _) => UpdateColorTextBoxPreview(textBox);
            UpdateColorTextBoxPreview(textBox);
        }
    }

    private void LayerVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LayerRow layer })
        {
            return;
        }

        PushUndoState(GetLanguageText("history.visibility", "Change layer visibility"));
        layer.Hide = string.Equals(layer.Hide, "True", StringComparison.OrdinalIgnoreCase)
            ? "False"
            : "True";
        MarkLayerDirty(layer);
        PreserveLayerGridScroll(() => LayerGrid.Items.Refresh());
        PopulateEditorFromSelection();
        RequestPreviewDraw();
        SetStatus(string.Equals(layer.Hide, "True", StringComparison.OrdinalIgnoreCase)
            ? GetLanguageText("status.layerHidden", "Layer hidden. Press Save to apply.")
            : GetLanguageText("status.layerShown", "Layer shown. Press Save to apply."));
        e.Handled = true;
    }

    private void LayerLockButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LayerRow layer })
        {
            return;
        }

        layer.IsLocked = !layer.IsLocked;
        var key = GetLayerLockKey(_currentTemplatePath, layer.Index);
        if (layer.IsLocked)
        {
            _lockedLayerKeys.Add(key);
        }
        else
        {
            _lockedLayerKeys.Remove(key);
        }

        PreserveLayerGridScroll(() => LayerGrid.Items.Refresh());
        RequestPreviewDraw();
        SetStatus(layer.IsLocked
            ? GetLanguageText("status.layerLocked", "Layer locked in preview.")
            : GetLanguageText("status.layerUnlocked", "Layer unlocked in preview."));
        e.Handled = true;
    }

    private static string GetLayerLockKey(string templatePath, string layerIndex)
    {
        return $"{templatePath}|{layerIndex}";
    }

    private void SetLayerActionTooltips(LayerRow layer)
    {
        layer.VisibilityTooltip = GetLanguageText(
            "tooltips.toggleLayerVisibility",
            "Show or hide this layer");
        layer.LockTooltip = GetLanguageText(
            "tooltips.toggleLayerLock",
            "Lock or unlock this layer in the preview");
    }

    private static void UpdateColorTextBoxPreview(TextBox textBox)
    {
        try
        {
            var normalized = NormalizeColorText(textBox.Text);
            if (ColorConverter.ConvertFromString(normalized) is not Color color)
            {
                return;
            }

            textBox.Background = new SolidColorBrush(color);
            var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) * color.A / 255.0;
            var foreground = luminance > 150 ? Colors.Black : Colors.White;
            textBox.Foreground = new SolidColorBrush(foreground);
            textBox.CaretBrush = new SolidColorBrush(foreground);
        }
        catch
        {
            textBox.ClearValue(Control.BackgroundProperty);
            textBox.ClearValue(Control.ForegroundProperty);
            textBox.ClearValue(TextBox.CaretBrushProperty);
        }
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
            var trackThickness = h;
            var isDirectionalBar =
                type.Contains("GraphStatuBar", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("GraphDynamicBar", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("DynamicBar", StringComparison.OrdinalIgnoreCase);
            if (isDirectionalBar && GetGraphDirection(layer) is 2 or 3)
            {
                (w, h) = (h, w);
            }
            var lineOverflow = GetGraphPreviewPadding(layer);
            if (type.Contains("DynamicBar", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(layer.InnerCircleRadius, out var knobDiameter))
            {
                lineOverflow = Math.Max(6.0, Math.Max(0, (knobDiameter - trackThickness) / 2.0) + 6.0);
            }
            return new Rect(
                ToPreview(lx - lineOverflow),
                ToPreview(ly - lineOverflow),
                ToPreview(w + lineOverflow * 2),
                ToPreview(h + lineOverflow * 2));
        }
        else if (type.Contains("GraphArchBar", StringComparison.OrdinalIgnoreCase))
        {
            double d = double.TryParse(layer.Diameter, out var ld) && ld > 0 ? ld : 120.0;
            var padding = GetArchGraphPreviewPadding(layer);
            return new Rect(
                ToPreview(lx - padding),
                ToPreview(ly - padding),
                ToPreview(d + padding * 2),
                ToPreview(d + padding * 2));
        }
        else if (type.Equals("GraphClock", StringComparison.OrdinalIgnoreCase))
        {
            var size = GetLayerMediaPixelSize(layer, fallbackWidth: _templateCanvasWidth, fallbackHeight: _templateCanvasHeight);
            var zoom = TryParseZoom(layer.ZoomRate, out var zr) && zr > 0 ? zr : 1.0;
            var centerX = double.TryParse(layer.ClockCenterX, NumberStyles.Float, CultureInfo.InvariantCulture, out var cx)
                ? cx : _templateCanvasWidth / 2.0;
            var centerY = double.TryParse(layer.ClockCenterY, NumberStyles.Float, CultureInfo.InvariantCulture, out var cy)
                ? cy : _templateCanvasHeight / 2.0;
            var clockLeft = ToPreview(centerX + lx);
            var clockTop = ToPreview(centerY + ly);
            return new Rect(clockLeft, clockTop, ToPreview(size.Width * zoom), ToPreview(size.Height * zoom));
        }
        else if (type.Equals("GraphSensor", StringComparison.OrdinalIgnoreCase))
        {
            var zoom = GetSensorZoomRate(layer);
            return new Rect(left, top, ToPreview(400.0 * zoom), ToPreview(400.0 * zoom));
        }
        else if (type.Contains("Image", StringComparison.OrdinalIgnoreCase) ||
                 type.Contains("Animation", StringComparison.OrdinalIgnoreCase))
        {
            var size = GetLayerMediaPixelSize(layer, fallbackWidth: 80.0, fallbackHeight: 80.0);
            double w = size.Width;
            double h = size.Height;
            double zoom = TryParseZoom(layer.ZoomRate, out var zr) && zr > 0 ? zr : 1.0;
            w *= zoom;
            h *= zoom;
            if (int.TryParse(layer.Rotate, out var imageRotation) && imageRotation % 180 != 0)
            {
                (w, h) = (h, w);
            }
            return new Rect(left, top, ToPreview(w), ToPreview(h));
        }
        else
        {
            if (!double.TryParse(layer.Size, out var lsize)) lsize = 20;
            var text = GetPreviewText(layer);
            text = ApplyDataDisplayOptions(layer, text);
            if (string.IsNullOrWhiteSpace(text)) text = layer.DataSource;
            return GetGdiTextLayerRender(layer, text).Bounds;
        }
    }

    private Size GetLayerMediaPixelSize(LayerRow layer, double fallbackWidth, double fallbackHeight)
    {
        var w = fallbackWidth;
        var h = fallbackHeight;
        if (string.IsNullOrWhiteSpace(layer.Media) &&
            (string.IsNullOrWhiteSpace(layer.MediaPath) || !File.Exists(layer.MediaPath)))
        {
            return new Size(w, h);
        }

        var imgPath = ResolveLayerMediaPath(layer);
        if (string.IsNullOrWhiteSpace(imgPath))
        {
            return new Size(w, h);
        }

        if (_imageBoundsCache.TryGetValue(imgPath, out var cachedSize))
        {
            return cachedSize;
        }

        if (!File.Exists(imgPath))
        {
            return new Size(w, h);
        }

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
        catch
        {
        }

        return new Size(w, h);
    }

    private Rect GetLayerSelectionBounds(LayerRow layer)
    {
        var type = layer.Type ?? "";
        if (!type.Equals("GraphItem", StringComparison.OrdinalIgnoreCase))
        {
            return GetLayerBounds(layer);
        }

        var text = GetPreviewText(layer);
        text = ApplyDataDisplayOptions(layer, text);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = layer.DataSource;
        }

        return GetGdiTextLayerRender(layer, text).Bounds;
    }

    private static double GetGraphPreviewPadding(LayerRow layer)
    {
        var lineWidth = double.TryParse(layer.LineWidth, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLineWidth)
            ? Math.Max(0, parsedLineWidth)
            : 0;
        var borderWidth = double.TryParse(layer.BorderWidth, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedBorderWidth)
            ? Math.Max(0, parsedBorderWidth)
            : 0;
        var maxStroke = Math.Max(lineWidth, borderWidth);
        return Math.Max(0, Math.Floor(maxStroke / 2.0) + 2.0);
    }

    private static double GetArchGraphPreviewPadding(LayerRow layer)
    {
        var archWidth = double.TryParse(layer.Thickness, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedArchWidth)
            ? Math.Max(0, parsedArchWidth)
            : 0;
        var lineWidth = double.TryParse(layer.LineWidth, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLineWidth)
            ? Math.Max(0, parsedLineWidth)
            : 0;
        var borderWidth = double.TryParse(layer.BorderWidth, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedBorderWidth)
            ? Math.Max(0, parsedBorderWidth)
            : 0;
        var maxStroke = Math.Max(archWidth, Math.Max(lineWidth, borderWidth));
        return Math.Max(6.0, Math.Floor(maxStroke / 2.0) + 6.0);
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
        ("h:m", "Hour:Minute"),
        ("h:m:s", "Hour:Minute:Second"),
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
        ("M", "Month"),
        ("D", "Day")
    };

    private static readonly (string Style, string Label)[] SensorStyleOptions =
    {
        ("Ring1", "Ring1 - Classic Ring"),
        ("Ring2", "Ring2 - Segmented Ring"),
        ("Ring3", "Ring3 - Needle Gauge"),
        ("Ring4", "Ring4 - Dual Ring"),
        ("Ring5", "Ring5 - Thick Mask"),
        ("none", "None - Text Only")
    };

    private static readonly (string Type, string Label, string DataSource, string Top, string Bottom, string Unit)[] SensorTypeOptions =
    {
        ("CPULoad", "CPU Load", "CPULOAD", "CPU", "LOAD", "%"),
        ("CPUTemperature", "CPU Temperature °C", "CPUTEMP", "CPU", "TEMP", "°C"),
        ("CPUTemperatureF", "CPU Temperature °F", "CPUTEMP_F", "CPU", "TEMP", "°F"),
        ("GPULoad", "GPU Load", "GPULOAD", "GPU", "LOAD", "%"),
        ("GPUTemperature", "GPU Temperature °C", "GPUTEMP", "GPU", "TEMP", "°C"),
        ("GPUTemperatureF", "GPU Temperature °F", "GPUTEMP_F", "GPU", "TEMP", "°F"),
    };

    private static string SampleValueFor(string dataSource, string formatText = "")
    {
        if (string.IsNullOrWhiteSpace(dataSource)) return "";
        var key = dataSource.ToUpperInvariant();
        var fmt = formatText ?? "";

        if (TryGetLiveSensorValue(key, fmt, out var liveValue))
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
            case "GPURAM": return "5800";
            case "GPURAMTOTAL": return "12288";
            case "RAMLOAD": return "42";
            case "RAM": return "13.4";
            case "RAMVALID": return "16.0";
            case "RAMMODEL": return "G.Skill DDR5";
            case "RAMTOTAL": return "32.0";
            case "RAM_GB": return "13.4";
            case "RAMVALID_GB": return "16.0";
            case "RAMTOTAL_GB": return "32.0";
            case "CPUPWR": return "65.0";
            case "CPUPOWER": return "65.0";
            case "GPUPWR": return "175.0";
            case "GPUPOWER": return "175.0";
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
            case "WATERTEMPC": return "31";
            case "WATERTEMPF": return "88";
            case "UPSPEED": return "8.5";
            case "DOWNDSPEED": return "45.2";
            case "FPS_AVG": return "120";
            case "GPUMODEL": return "GPU";
            case "TIME":
                var currentTime = DateTime.Now;
                if (fmt is "h:m" or "00:00" or "HH:mm") return currentTime.ToString("HH:mm");
                if (fmt is "h:m:s" or "00:00:00" or "HH:MM:SS" or "H:M:S" or "HH:mm:ss") return currentTime.ToString("HH:mm:ss");
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
                    "M" => now.ToString("MM"),
                    "D" => now.ToString("dd"),
                    _ => now.ToString("yyyy-MM-dd")
                };
            case "DAY":
                return DateTime.Now.ToString("dddd", CultureInfo.InvariantCulture);
            default:
                return dataSource;
        }
    }


    private static bool TryGetLiveSensorValue(string key, string selector, out string value)
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
                return Math.Round(numeric, 1).ToString("0.0", CultureInfo.InvariantCulture);
            }
        }

        return value;
    }

    private static bool IsDriveDataSource(string dataSource)
    {
        var source = (dataSource ?? "").ToUpperInvariant();
        return source is "HDDTEMP" or "HDDUSED" or "DRVLOAD";
    }

    private static string NormalizeDriveSelector(string selector)
    {
        var match = Regex.Match(selector ?? "", @"[A-Za-z]");
        return match.Success ? match.Value.ToUpperInvariant() : "C";
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
            if ((now - info.LastWriteTimeUtc).TotalSeconds > 30)
            {
                _liveSensorValueCache.Clear();
                _liveSensorCacheWriteUtc = DateTime.MinValue;
                _liveSensorCacheReadUtc = now;
                return;
            }

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
            PutRounded(fresh, "CPUPWR", FindReading(readings, r => IsCpuSensor(r) && r.Group == "READING_POWER" && r.Name.Equals("CPU Package Power", StringComparison.OrdinalIgnoreCase)), 1);
            PutAlias(fresh, "CPUPOWER", "CPUPWR");
            PutRounded(fresh, "GPUPWR", FindReading(readings, r => IsDiscreteGpuSensor(r) && r.Group == "READING_POWER" && r.Name.Equals("GPU Power", StringComparison.OrdinalIgnoreCase)), 1);
            PutAlias(fresh, "GPUPOWER", "GPUPWR");
            PutRounded(fresh, "CPUVOLTAGE", FindReading(readings, r => IsCpuSensor(r) && r.Group == "READING_VOLT" && r.Name.Contains("CPU VDDCR_VDD Voltage", StringComparison.OrdinalIgnoreCase)), 2);
            PutRounded(fresh, "GPUVOLTAGE", FindReading(readings, r => IsDiscreteGpuSensor(r) && r.Group == "READING_VOLT" && r.Name.Equals("GPU Core Voltage", StringComparison.OrdinalIgnoreCase)), 2);
            PutRounded(fresh, "CPUCLOCK", FindBestClock(readings, true));
            PutClockGhz(fresh, "CPUCLOCK_G", FindBestClock(readings, true));
            PutRounded(fresh, "GPUCLOCK", FindBestClock(readings, false));
            PutClockGhz(fresh, "GPUCLOCK_G", FindBestClock(readings, false));
            PutRounded(fresh, "HDDTEMP", FindReading(readings, r => r.Group == "READING_TEMP" && r.Name.Equals("Drive Temperature", StringComparison.OrdinalIgnoreCase)));
            PutFahrenheit(fresh, "HDDTEMP_F", FindReading(readings, r => r.Group == "READING_TEMP" && r.Name.Equals("Drive Temperature", StringComparison.OrdinalIgnoreCase)));
            foreach (var driveGroup in readings
                         .Select(r => (Reading: r, Match: Regex.Match(r.Sensor, @"\[([A-Za-z]):\]")))
                         .Where(item => item.Match.Success)
                         .GroupBy(item => item.Match.Groups[1].Value.ToUpperInvariant()))
            {
                var drive = driveGroup.Key;
                PutRounded(fresh, $"HDDTEMP:{drive}", FindReading(driveGroup.Select(item => item.Reading),
                    r => r.Group == "READING_TEMP" && r.Name.Equals("Drive Temperature", StringComparison.OrdinalIgnoreCase)));
                PutFahrenheit(fresh, $"HDDTEMP_F:{drive}", FindReading(driveGroup.Select(item => item.Reading),
                    r => r.Group == "READING_TEMP" && r.Name.Equals("Drive Temperature", StringComparison.OrdinalIgnoreCase)));
                var activity = driveGroup
                    .Select(item => item.Reading)
                    .Where(r => r.Group == "READING_USAGE" &&
                                (r.Name.Equals("Read Activity", StringComparison.OrdinalIgnoreCase) ||
                                 r.Name.Equals("Write Activity", StringComparison.OrdinalIgnoreCase)))
                    .Select(r => r.Value)
                    .DefaultIfEmpty(0)
                    .Max();
                fresh[$"DRVLOAD:{drive}"] = Math.Round(Math.Clamp(activity, 0, 100)).ToString("0", CultureInfo.InvariantCulture);
            }

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var letter = drive.Name[..1].ToUpperInvariant();
                var usedPercent = drive.TotalSize <= 0
                    ? 0
                    : (drive.TotalSize - drive.AvailableFreeSpace) * 100.0 / drive.TotalSize;
                fresh[$"HDDUSED:{letter}"] = Math.Round(usedPercent).ToString("0", CultureInfo.InvariantCulture);
            }

            PutMemoryValues(fresh, readings);
            PutNetworkValues(fresh, readings);
            PutRounded(fresh, "FPS_AVG", FindReading(readings, r => r.Sensor.Contains("PresentMon", StringComparison.OrdinalIgnoreCase) && r.Unit.Equals("FPS", StringComparison.OrdinalIgnoreCase) && r.Name.Contains("Presented (avg)", StringComparison.OrdinalIgnoreCase)));

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

    private static void PutMemoryValues(Dictionary<string, string> target, IReadOnlyCollection<SensorReading> readings)
    {
        var used = FindReading(readings, r => r.Name.Equals("Physical Memory Used", StringComparison.OrdinalIgnoreCase));
        var available = FindReading(readings, r => r.Name.Equals("Physical Memory Available", StringComparison.OrdinalIgnoreCase));
        var load = FindReading(readings, r => r.Name.Equals("Physical Memory Load", StringComparison.OrdinalIgnoreCase));
        if (used is not null) target["RAM"] = Math.Round(used.Value.Value).ToString("0", CultureInfo.InvariantCulture);
        if (available is not null) target["RAMVALID"] = Math.Round(available.Value.Value).ToString("0", CultureInfo.InvariantCulture);
        if (load is not null) target["RAMLOAD"] = Math.Round(load.Value.Value).ToString("0", CultureInfo.InvariantCulture);
        if (used is not null && available is not null)
        {
            var total = used.Value.Value + available.Value.Value;
            target["RAMTOTAL"] = Math.Round(total).ToString("0", CultureInfo.InvariantCulture);
            target["RAM_GB"] = (used.Value.Value / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
            target["RAMVALID_GB"] = (available.Value.Value / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
            target["RAMTOTAL_GB"] = (total / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
        }

        var gpuUsed = FindReading(readings, r => IsDiscreteGpuSensor(r) && r.Name.Equals("GPU Memory Allocated", StringComparison.OrdinalIgnoreCase));
        var gpuAvailable = FindReading(readings, r => IsDiscreteGpuSensor(r) && r.Name.Equals("GPU Memory Available", StringComparison.OrdinalIgnoreCase));
        var gpuLoad = FindReading(readings, r => IsDiscreteGpuSensor(r) && r.Name.Equals("GPU Memory Usage", StringComparison.OrdinalIgnoreCase));
        if (gpuUsed is not null) target["GPURAM"] = Math.Round(gpuUsed.Value.Value).ToString("0", CultureInfo.InvariantCulture);
        if (gpuUsed is not null && gpuAvailable is not null)
        {
            target["GPURAMTOTAL"] = Math.Round(gpuUsed.Value.Value + gpuAvailable.Value.Value)
                .ToString("0", CultureInfo.InvariantCulture);
        }
        if (gpuLoad is not null) target["GPURAMLOAD"] = Math.Round(gpuLoad.Value.Value).ToString("0", CultureInfo.InvariantCulture);
    }

    private static void PutNetworkValues(Dictionary<string, string> target, IEnumerable<SensorReading> readings)
    {
        var download = readings
            .Where(r => r.Name.Equals("Current DL rate", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.Value);
        var upload = readings
            .Where(r => r.Name.Equals("Current UP rate", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.Value);
        target["DOWNDSPEED"] = FormatNetworkRate(download);
        target["UPSPEED"] = FormatNetworkRate(upload);
    }

    private static string FormatNetworkRate(double kilobytesPerSecond)
    {
        return kilobytesPerSecond >= 1024
            ? $"{kilobytesPerSecond / 1024.0:0.0} MB/s"
            : $"{kilobytesPerSecond:0.0} KB/s";
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
        var front = ApplyBrushAlpha(
            NewBrush(layer.FrontColor, "#FFFFFF"),
            layer.FrontAlpha,
            zeroMeansUnspecified: true);
        if ((layer.Type ?? "").Contains("GraphDynamicBar", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(layer.UseGradient, "True", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(layer.GradientColor))
        {
            return front;
        }

        var frontColor = ((SolidColorBrush)front).Color;
        var gradientColor = ((SolidColorBrush)NewBrush(layer.GradientColor, layer.FrontColor)).Color;
        var direction = int.TryParse(layer.Direction, out var parsedDirection) ? parsedDirection : 0;
        var (start, end) = direction switch
        {
            1 => (new Point(1, 0.5), new Point(0, 0.5)),
            2 => (new Point(0.5, 0), new Point(0.5, 1)),
            3 => (new Point(0.5, 1), new Point(0.5, 0)),
            _ => (new Point(0, 0.5), new Point(1, 0.5))
        };
        return new LinearGradientBrush(frontColor, gradientColor, start, end);
    }

    private static Brush ApplyBrushAlpha(
        Brush brush,
        string alphaText,
        bool zeroMeansUnspecified = false)
    {
        if (brush is not SolidColorBrush solid || !byte.TryParse(alphaText, out var alpha))
        {
            return brush;
        }
        if (zeroMeansUnspecified && alpha == 0)
        {
            return brush;
        }
        var color = solid.Color;
        color.A = alpha;
        return new SolidColorBrush(color);
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
        var gradientColor = NormalizeColorText(layer.FontGradientColor);
        var gradientDirection = int.TryParse(layer.FontGradientDirection, out var parsedDirection) ? parsedDirection : -1;
        var cacheKey = string.Join("|layer-v9", text, x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture),
            size.ToString(CultureInfo.InvariantCulture), fontName, bold, color, gradientColor, gradientDirection,
            alignmentIndex, interval.ToString(CultureInfo.InvariantCulture));

        if (_gdiTextLayerCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        if (_gdiTextLayerCache.Count > 1000)
        {
            _gdiTextLayerCache.Clear();
            _gdiTextCache.Clear();
            _gdiTextInkCache.Clear();
        }

        using var measureBitmap = new System.Drawing.Bitmap(
            1,
            1,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var measureGraphics = System.Drawing.Graphics.FromImage(measureBitmap);
        ConfigureGdiTextGraphics(measureGraphics);
        using var measureFont = CreateGdiFont(fontName, size, bold, 1.0);
        var measured = Math.Abs(interval) > double.Epsilon
            ? MeasureGdiIntervalTextAtScale(measureGraphics, text, measureFont, interval)
            : MeasureGdiString(measureGraphics, text, measureFont);
        var padding = Math.Max(4, (int)Math.Ceiling(size * 0.25));
        var bitmapWidth = Math.Max(1, (int)Math.Ceiling(measured.Width) + padding * 2);
        var bitmapHeight = Math.Max(1, (int)Math.Ceiling(measured.Height) + padding * 2);
        var localAnchorX = alignmentIndex switch
        {
            1 => padding + measured.Width / 2f,
            2 => padding + measured.Width,
            _ => padding
        };

        using var bitmap = new System.Drawing.Bitmap(
            bitmapWidth,
            bitmapHeight,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        bitmap.SetResolution(96f, 96f);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.Transparent);
            ConfigureGdiTextGraphics(graphics);
            using var font = CreateGdiFont(fontName, size, bold, 1.0);
            using var brush = CreateTextDrawingBrush(
                color,
                gradientColor,
                    gradientDirection,
                    new System.Drawing.RectangleF(
                        padding,
                        padding,
                        Math.Max(1f, measured.Width),
                        Math.Max(1f, measured.Height)));
            using var format = CreateGdiStringFormat(alignmentIndex);
            if (Math.Abs(interval) > double.Epsilon)
            {
                DrawGdiIntervalTextAtTemplatePoint(
                    graphics,
                    text,
                    font,
                    brush,
                    (float)localAnchorX,
                    padding,
                    interval,
                    alignmentIndex);
            }
            else
            {
                graphics.DrawString(
                    text,
                    font,
                    brush,
                    new System.Drawing.PointF((float)localAnchorX, padding),
                    format);
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

        var source = ToBitmapSource(bitmap);
        var templateLeft = x - localAnchorX;
        var templateTop = y - padding;
        var bounds = new Rect(
            templateLeft * _previewScale,
            templateTop * _previewScale,
            Math.Max(1.0, bitmapWidth * _previewScale),
            Math.Max(1.0, bitmapHeight * _previewScale));

        var render = new TextLayerRenderResult(source, bounds);
        if (!_isDraggingPreview && !_isResizingPreview)
        {
            _gdiTextLayerCache[cacheKey] = render;
        }
        return render;
    }

    private static System.Drawing.SizeF MeasureGdiIntervalTextAtScale(
        System.Drawing.Graphics graphics,
        string text,
        System.Drawing.Font font,
        double interval)
    {
        var width = 0.0f;
        var height = 0.0f;
        var lineHeight = font.GetHeight(graphics);
        foreach (var line in SplitGdiTextLines(text))
        {
            var lineWidth = MeasureGdiIntervalLineWidth(graphics, line, font, interval);
            width = Math.Max(width, lineWidth);
            height += lineHeight;
        }

        return new System.Drawing.SizeF(Math.Max(1.0f, width), Math.Max(1.0f, height));
    }

    private static System.Drawing.Brush CreateTextDrawingBrush(
        string color,
        string gradientColor,
        int direction,
        System.Drawing.RectangleF bounds)
    {
        if (direction <= 0 || string.IsNullOrWhiteSpace(gradientColor))
        {
            return new System.Drawing.SolidBrush(ToDrawingColor(color));
        }

        var start = direction switch
        {
            2 => new System.Drawing.PointF(bounds.Left + bounds.Width / 2f, bounds.Top),
            3 => new System.Drawing.PointF(bounds.Left, bounds.Top),
            4 => new System.Drawing.PointF(bounds.Right, bounds.Top),
            _ => new System.Drawing.PointF(bounds.Left, bounds.Top + bounds.Height / 2f)
        };
        var end = direction switch
        {
            2 => new System.Drawing.PointF(bounds.Left + bounds.Width / 2f, bounds.Bottom),
            3 => new System.Drawing.PointF(bounds.Right, bounds.Bottom),
            4 => new System.Drawing.PointF(bounds.Left, bounds.Bottom),
            _ => new System.Drawing.PointF(bounds.Right, bounds.Top + bounds.Height / 2f)
        };
        return new System.Drawing.Drawing2D.LinearGradientBrush(
            start,
            end,
            ToDrawingColor(color),
            ToDrawingColor(gradientColor));
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
        return MeasureGdiString(graphics, text, font);
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
            var charSize = MeasureGdiString(graphics, c.ToString(), font);
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
        var lineHeight = font.GetHeight(graphics);
        foreach (var line in SplitGdiTextLines(text))
        {
            point.X = x;
            foreach (var c in line)
            {
                var glyph = c.ToString();
                var size = MeasureGdiString(graphics, glyph, font);
                if (c == '.')
                {
                    point.X -= (float)(size.Width * 0.1);
                }

                graphics.DrawString(glyph, font, brush, point);
                point.X += size.Width + interval;
            }
            point.Y += lineHeight;
        }
    }

    private static void DrawGdiIntervalTextAtTemplatePoint(System.Drawing.Graphics graphics, string text, System.Drawing.Font font, System.Drawing.Brush brush, float x, float y, double templateInterval, int alignmentIndex)
    {
        var interval = (float)templateInterval;
        var lineHeight = font.GetHeight(graphics);
        var point = new System.Drawing.PointF(x, y);
        foreach (var line in SplitGdiTextLines(text))
        {
            var lineX = alignmentIndex switch
            {
                1 => x - MeasureGdiIntervalLineWidth(graphics, line, font, interval) / 2.0f,
                2 => x - MeasureGdiIntervalLineWidth(graphics, line, font, interval),
                _ => x
            };
            point.X = lineX;
            foreach (var c in line)
            {
                var glyph = c.ToString();
                var size = MeasureGdiString(graphics, glyph, font);
                if (c == '.')
                {
                    point.X -= (float)(size.Width * 0.1);
                }
                graphics.DrawString(glyph, font, brush, point);
                point.X += size.Width + interval;
            }
            point.Y += lineHeight;
        }
    }

    private static IEnumerable<string> SplitGdiTextLines(string text) =>
        (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static float MeasureGdiIntervalLineWidth(
        System.Drawing.Graphics graphics,
        string line,
        System.Drawing.Font font,
        double interval)
    {
        var width = 0.0f;
        foreach (var character in line)
        {
            width += MeasureGdiString(graphics, character.ToString(), font).Width + (float)interval;
        }
        if (line.Length > 0)
        {
            width -= (float)interval;
        }
        return Math.Max(1.0f, width);
    }

    private static void ConfigureGdiTextGraphics(System.Drawing.Graphics graphics)
    {
        graphics.PageUnit = System.Drawing.GraphicsUnit.Pixel;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Default;
    }

    private static System.Drawing.SizeF MeasureGdiString(
        System.Drawing.Graphics graphics,
        string text,
        System.Drawing.Font font)
    {
        using var format = CreateGdiStringFormat(0);
        return graphics.MeasureString(
            string.IsNullOrEmpty(text) ? " " : text,
            font,
            new System.Drawing.SizeF(100000f, 100000f),
            format);
    }

    private static System.Drawing.StringFormat CreateGdiStringFormat(int alignmentIndex)
    {
        var format = (System.Drawing.StringFormat)System.Drawing.StringFormat.GenericTypographic.Clone();
        format.Alignment = (System.Drawing.StringAlignment)Math.Clamp(alignmentIndex, 0, 2);
        format.FormatFlags |= System.Drawing.StringFormatFlags.MeasureTrailingSpaces |
                              System.Drawing.StringFormatFlags.NoClip;
        format.Trimming = System.Drawing.StringTrimming.None;
        return format;
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
    private static readonly HashSet<string> _customFontNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> _canonicalFontNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> _customFontFiles = new(StringComparer.OrdinalIgnoreCase);

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
                    RegisterCanonicalFontName(name, name);
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
            @"C:\Program Files\Lian-Li\L-Connect 3\Assets\tl-sensor\assets\",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LianLiThemeEditor", "Fonts")
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
                            RegisterCanonicalFontName(familyName, familyName);
                            _customFontFiles[familyName] = file;
                            _customFontFiles[NormalizeFontLookupKey(familyName)] = file;

                            var baseName = Path.GetFileNameWithoutExtension(fileName);
                            _customFontMap[baseName] = wpfFontFamily;
                            _customFontMap[NormalizeFontLookupKey(baseName)] = wpfFontFamily;
                            RegisterCanonicalFontName(baseName, familyName);
                            MapGdiFontAlias(baseName, familyName);
                            _customFontFiles[baseName] = file;
                            _customFontFiles[NormalizeFontLookupKey(baseName)] = file;

                            var dotIndex = baseName.IndexOf('.');
                            if (dotIndex > 0)
                            {
                                var cleanBase = baseName.Substring(0, dotIndex);
                                _customFontMap[cleanBase] = wpfFontFamily;
                                _customFontMap[NormalizeFontLookupKey(cleanBase)] = wpfFontFamily;
                                RegisterCanonicalFontName(cleanBase, familyName);
                                MapGdiFontAlias(cleanBase, familyName);
                                _customFontFiles[cleanBase] = file;
                                _customFontFiles[NormalizeFontLookupKey(cleanBase)] = file;
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
                RegisterCanonicalFontName(latestFamily.Name, latestFamily.Name);
                RegisterCanonicalFontName(baseName, latestFamily.Name);
                RegisterCanonicalFontName(cleanBase, latestFamily.Name);
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

    private static void RegisterCanonicalFontName(string alias, string familyName)
    {
        if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(familyName))
        {
            return;
        }

        _customFontNames.Add(familyName);
        _canonicalFontNames[alias] = familyName;
        _canonicalFontNames[NormalizeFontLookupKey(alias)] = familyName;
        _canonicalFontNames[familyName] = familyName;
        _canonicalFontNames[NormalizeFontLookupKey(familyName)] = familyName;
    }

    private static void MapGdiFontAlias(string alias, string familyName)
    {
        if (!_gdiFontMap.TryGetValue(familyName, out var family) &&
            !_gdiFontMap.TryGetValue(NormalizeFontLookupKey(familyName), out family))
        {
            return;
        }

        _gdiFontMap[alias] = family;
        _gdiFontMap[NormalizeFontLookupKey(alias)] = family;
    }

    private static string ResolveCanonicalFontName(string fontName)
    {
        var value = (fontName ?? "").Trim();
        if (_canonicalFontNames.TryGetValue(value, out var canonical) ||
            _canonicalFontNames.TryGetValue(NormalizeFontLookupKey(value), out canonical))
        {
            return canonical;
        }

        return value;
    }

    private void ClearTextPreviewCaches()
    {
        _gdiTextCache.Clear();
        _gdiTextInkCache.Clear();
        _gdiTextLayerCache.Clear();
    }

    private static string SafeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((value ?? string.Empty).Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        safe = Regex.Replace(safe, @"\s+", "_");
        if (safe.Length <= 160)
        {
            return safe;
        }

        return safe[..160] + "-" + Math.Abs(safe.GetHashCode()).ToString(CultureInfo.InvariantCulture);
    }

    private static Task<bool> EnsureLConnectFontsInstalledAsync(IEnumerable<LayerRow> layers)
    {
        return Task.Run(() =>
        {
            var changed = false;
            foreach (var fontName in layers
                         .Where(layer => string.Equals(layer.Type, "GraphItem", StringComparison.OrdinalIgnoreCase))
                         .Select(layer => ResolveCanonicalFontName(layer.Font))
                         .Where(name => !string.IsNullOrWhiteSpace(name))
                         .Where(IsCustomEditorFont)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                changed |= EnsureLConnectFontInstalled(fontName);
            }

            return changed;
        });
    }

    private static bool IsCustomEditorFont(string fontName)
    {
        return _customFontFiles.ContainsKey(fontName) ||
               _customFontFiles.ContainsKey(NormalizeFontLookupKey(fontName));
    }

    private static bool EnsureLConnectFontInstalled(string fontName)
    {
        var sourcePath = ResolveFontSourcePath(fontName);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return false;
        }

        var changed = CopyLConnectRuntimeFont(sourcePath);

        var windowsFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var targetFileName = Path.GetFileName(sourcePath);
        var targetPath = Path.Combine(windowsFonts, targetFileName);
        if (!File.Exists(targetPath))
        {
            File.Copy(sourcePath, targetPath, false);
            changed = true;
        }

        var fontType = Path.GetExtension(sourcePath).Equals(".otf", StringComparison.OrdinalIgnoreCase)
            ? "OpenType"
            : "TrueType";
        var registryName = $"{fontName} ({fontType})";
        changed |= SetMachineFontRegistryValue(registryName, targetFileName);

        AddFontResourceEx(targetPath, 0, IntPtr.Zero);
        SendMessageTimeout(
            new IntPtr(0xffff),
            WmFontChange,
            IntPtr.Zero,
            IntPtr.Zero,
            SendMessageTimeoutFlags.AbortIfHung,
            1000,
            out _);
        return changed;
    }

    private static string ResolveFontSourcePath(string fontName)
    {
        if (_customFontFiles.TryGetValue(fontName, out var sourcePath) ||
            _customFontFiles.TryGetValue(NormalizeFontLookupKey(fontName), out sourcePath))
        {
            return sourcePath;
        }

        foreach (var entry in EnumerateFontRegistryEntries())
        {
            if (!FontRegistryNameMatches(entry.Name, fontName))
            {
                continue;
            }

            var resolved = ResolveFontRegistryPath(entry.Value);
            if (File.Exists(resolved))
            {
                return resolved;
            }
        }

        return "";
    }

    private static bool SetMachineFontRegistryValue(string registryName, string targetFileName)
    {
        var changed = false;
        foreach (var view in GetRegistryViews())
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var fontsKey = baseKey.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts",
                    writable: true);
                if (fontsKey == null)
                {
                    continue;
                }

                if (!string.Equals(fontsKey.GetValue(registryName)?.ToString(), targetFileName, StringComparison.OrdinalIgnoreCase))
                {
                    fontsKey.SetValue(registryName, targetFileName, RegistryValueKind.String);
                    changed = true;
                }
            }
            catch
            {
            }
        }

        return changed;
    }

    private static IEnumerable<(string Name, string Value)> EnumerateFontRegistryEntries()
    {
        foreach (var view in GetRegistryViews())
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var fontsKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
                if (fontsKey == null)
                {
                    continue;
                }

                foreach (var name in fontsKey.GetValueNames())
                {
                    var value = fontsKey.GetValue(name)?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        yield return (name, value);
                    }
                }
            }
        }
    }

    private static IEnumerable<RegistryView> GetRegistryViews()
    {
        yield return RegistryView.Registry64;
        yield return RegistryView.Registry32;
    }

    private static bool FontRegistryNameMatches(string registryName, string fontName)
    {
        var cleanRegistryName = Regex.Replace(registryName ?? "", @"\s+\((TrueType|OpenType|Type 1)\)\s*$", "", RegexOptions.IgnoreCase);
        return cleanRegistryName.Equals(fontName, StringComparison.OrdinalIgnoreCase) ||
               NormalizeFontLookupKey(cleanRegistryName).Equals(NormalizeFontLookupKey(fontName), StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveFontRegistryPath(string value)
    {
        var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
        if (Path.IsPathRooted(expanded))
        {
            return expanded;
        }

        var windowsFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var candidate = Path.Combine(windowsFonts, expanded);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var localFonts = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "Windows",
            "Fonts",
            expanded);
        return localFonts;
    }

    private static bool CopyLConnectRuntimeFont(string sourcePath)
    {
        try
        {
            var fontDir = @"C:\Program Files\Lian-Li\L-Connect 3\fonts";
            Directory.CreateDirectory(fontDir);
            var targetPath = Path.Combine(fontDir, Path.GetFileName(sourcePath));
            if (!File.Exists(targetPath))
            {
                File.Copy(sourcePath, targetPath, false);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private const uint WmFontChange = 0x001D;

    [Flags]
    private enum SendMessageTimeoutFlags : uint
    {
        AbortIfHung = 0x0002
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResourceEx(string name, uint flags, IntPtr reserved);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        SendMessageTimeoutFlags flags,
        uint timeout,
        out IntPtr result);

    private (int Width, int Height, int X, int Y) GetImagePlacement(
        string imagePath, string requestedSize, string requestedX, string requestedY)
    {
        var canvasWidth = Math.Max(1, (int)Math.Round(_templateCanvasWidth));
        var canvasHeight = Math.Max(1, (int)Math.Round(_templateCanvasHeight));
        var canvasMaximum = Math.Max(canvasWidth, canvasHeight);
        var maxDimension = int.TryParse(requestedSize, out var requested)
            ? Math.Clamp(requested, 10, canvasMaximum)
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
        x = Math.Clamp(x, 0, Math.Max(0, canvasWidth - width));
        y = Math.Clamp(y, 0, Math.Max(0, canvasHeight - height));
        return (width, height, x, y);
    }

    private double GetImageFitZoom(string imagePath)
    {
        try
        {
            var decoder = BitmapDecoder.Create(
                new Uri(imagePath, UriKind.Absolute),
                BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
            {
                return 1.0;
            }

            return Math.Min(
                1.0,
                Math.Min(
                    _templateCanvasWidth / frame.PixelWidth,
                    _templateCanvasHeight / frame.PixelHeight));
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
        if (TemplateCombo.SelectedItem is TemplateOption selected)
        {
            SelectedTemplateImage.Source = selected.Thumbnail;
            TemplateSelectionText.Text = selected.Id;
        }
        if (_isLoading || TemplateCombo.SelectedItem is not TemplateOption option || string.IsNullOrWhiteSpace(option.Path))
        {
            return;
        }

        UseActiveCheck.IsChecked = false;
        TemplateIdBox.Text = option.Id;
        _currentTemplatePath = option.Path;
        await LoadLayersAsync(true);
    }

    private async void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedDeviceImage.Source = LoadPackImage(GetDeviceImagePath(GetSelectedDeviceModel()));
        DeviceSelectionText.Text = GetSelectedDeviceDisplayName();
        UpdateDeviceCapabilityNotice();
        if (_isLoading) return;
        UpdateCanvasConfiguration(resetZoom: true);
        RefreshTemplateList(selectFirstWhenMissing: true);
        await RefreshGraphStylesAsync();
        DrawPreview();
        SaveShadowLinks();

        if (TemplateCombo.SelectedItem is TemplateOption option &&
            !string.IsNullOrWhiteSpace(option.Path))
        {
            UseActiveCheck.IsChecked = false;
            TemplateIdBox.Text = option.Id;
            _currentTemplatePath = option.Path;
            await LoadLayersAsync(true);
        }
    }

    private async Task RefreshGraphStylesAsync()
    {
        IReadOnlyList<GraphStyleOption> graphStyles;
        try
        {
            graphStyles = await _supporter.ListGraphStylesAsync();
            graphStyles = graphStyles
                .Where(style => IsWideScreenDeviceSelected()
                    ? !style.Code.StartsWith("MOD::H2_", StringComparison.OrdinalIgnoreCase)
                    : style.Code.StartsWith("MOD::H2_", StringComparison.OrdinalIgnoreCase))
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
        GraphStyleCombo.Items.Clear();
        AddGraphStyleCombo.Items.Clear();
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
    }

    private void UniversalOrientationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUniversalOrientation || _isLoading || !IsLoaded)
        {
            return;
        }

        UpdateCanvasConfiguration(resetZoom: false);
        _gdiTextLayerCache.Clear();
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
                    ? GetLanguageText("tooltips.switchDark", "Switch to dark theme")
                    : GetLanguageText("tooltips.switchLight", "Switch to light theme");
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
            ? GetLanguageText("tooltips.switchDark", "Switch to dark theme")
            : GetLanguageText("tooltips.switchLight", "Switch to light theme");
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

    private string GetSelectedDeviceDisplayName()
    {
        if (DeviceCombo.SelectedItem is ComboBoxItem item &&
            item.Content is StackPanel panel &&
            panel.Children.OfType<TextBlock>().FirstOrDefault() is { } textBlock)
        {
            return textBlock.Text;
        }

        return GetDeviceDisplayName(GetSelectedDeviceModel());
    }

    private static string GetDeviceDisplayName(string deviceModel)
    {
        return deviceModel switch
        {
            "hydroshift-ii-lcd-c" => "Hydroshift II LCD-C",
            "universal-screen-8.8-inch" => "8.8\" Universal Screen",
            Vm92DeviceModel => "VM 9.2 LCD",
            _ => "Hydroshift II LCD-S"
        };
    }

    private bool IsUniversalScreenSelected()
    {
        return string.Equals(
            GetSelectedDeviceModel(),
            UniversalScreenDeviceModel,
            StringComparison.OrdinalIgnoreCase);
    }

    private bool IsVm92Selected()
    {
        return string.Equals(
            GetSelectedDeviceModel(),
            Vm92DeviceModel,
            StringComparison.OrdinalIgnoreCase);
    }

    private bool IsWideScreenDeviceSelected()
    {
        var deviceModel = GetSelectedDeviceModel();
        return IsWideScreenDeviceModel(deviceModel);
    }

    private static bool IsWideScreenDeviceModel(string deviceModel) =>
        string.Equals(deviceModel, UniversalScreenDeviceModel, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(deviceModel, Vm92DeviceModel, StringComparison.OrdinalIgnoreCase);

    private bool CanDirectApplySelectedDevice() => IsOfflineMode || !IsVm92Selected();

    private void ShowDirectApplyUnsupportedMessage()
    {
        MessageBox.Show(
            this,
            GetLanguageText(
                "messages.vm92DirectApplyUnsupported",
                "Direct apply is not supported for VM 9.2 LCD yet. Export a ZIP and import it from L-Connect."),
            GetLanguageText("messages.applyFailed", "Apply failed"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void UpdateDeviceCapabilityNotice()
    {
        var showVm92Notice = IsVm92Selected();
        Vm92DirectApplyNotice.Visibility = showVm92Notice ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = !showVm92Notice;
        ApplyButton.IsEnabled = !showVm92Notice;
        ApplyAllButton.IsEnabled = !showVm92Notice;
        Convert88To92Button.IsEnabled = showVm92Notice;
    }

    private bool IsUniversalLandscape()
    {
        return !string.Equals(
            (UniversalOrientationCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
            "portrait",
            StringComparison.OrdinalIgnoreCase);
    }

    private (int Width, int Height) GetTemplateCanvasPixels()
    {
        return GetTemplateCanvasPixels(GetSelectedDeviceModel());
    }

    private (int Width, int Height) GetTemplateCanvasPixels(string deviceModel)
    {
        if (!string.Equals(deviceModel, GetSelectedDeviceModel(), StringComparison.OrdinalIgnoreCase))
        {
            return IsWideScreenDeviceModel(deviceModel) ? (1920, 480) : (480, 480);
        }

        return (
            Math.Max(1, (int)Math.Round(_templateCanvasWidth)),
            Math.Max(1, (int)Math.Round(_templateCanvasHeight)));
    }

    private void UpdateCanvasConfiguration(bool resetZoom)
    {
        var universal = IsWideScreenDeviceSelected();
        UniversalOrientationPanel.Visibility = universal ? Visibility.Visible : Visibility.Collapsed;

        if (universal)
        {
            var landscape = IsUniversalLandscape();
            _templateCanvasWidth = landscape ? 1920.0 : 480.0;
            _templateCanvasHeight = landscape ? 480.0 : 1920.0;
            _previewCanvasWidth = landscape ? 480.0 : 120.0;
            _previewCanvasHeight = landscape ? 120.0 : 480.0;
        }
        else
        {
            _templateCanvasWidth = 480.0;
            _templateCanvasHeight = 480.0;
            _previewCanvasWidth = 240.0;
            _previewCanvasHeight = 240.0;
        }

        _previewScale = Math.Min(
            _previewCanvasWidth / _templateCanvasWidth,
            _previewCanvasHeight / _templateCanvasHeight);
        _textPreviewRenderScale = _previewScale * TextPreviewSupersample;

        var isCircularDevice = string.Equals(
            GetSelectedDeviceModel(),
            "hydroshift-ii-lcd-c",
            StringComparison.OrdinalIgnoreCase);
        var frameBorderThickness = isCircularDevice ? 0.0 : 1.0;
        PreviewFrame.BorderThickness = new Thickness(frameBorderThickness);
        PreviewFrame.Width = _previewCanvasWidth + frameBorderThickness * 2;
        PreviewFrame.Height = _previewCanvasHeight + frameBorderThickness * 2;
        PreviewSurface.Width = _previewCanvasWidth;
        PreviewSurface.Height = _previewCanvasHeight;
        PreviewCanvas.Width = _previewCanvasWidth;
        PreviewCanvas.Height = _previewCanvasHeight;
        BackgroundMedia.Width = _previewCanvasWidth;
        BackgroundMedia.Height = _previewCanvasHeight;
        BackgroundImage.Width = _previewCanvasWidth;
        BackgroundImage.Height = _previewCanvasHeight;
        PreviewClipGeometry.Rect = new Rect(0, 0, _previewCanvasWidth, _previewCanvasHeight);
        var previewMaskThickness = Math.Max(1.0, Math.Round(PreviewMaskTemplateThickness * _previewScale));
        PreviewMaskTop.Height = previewMaskThickness;
        PreviewMaskBottom.Height = previewMaskThickness;
        PreviewMaskLeft.Width = previewMaskThickness;
        PreviewMaskRight.Width = previewMaskThickness;
        var maskVisibility = ShouldShowPreviewMask(GetSelectedDeviceModel()) ? Visibility.Visible : Visibility.Collapsed;
        PreviewMaskTop.Visibility = maskVisibility;
        PreviewMaskBottom.Visibility = maskVisibility;
        PreviewMaskLeft.Visibility = maskVisibility;
        PreviewMaskRight.Visibility = maskVisibility;

        var maximumDimension = Math.Max(_templateCanvasWidth, _templateCanvasHeight);
        SizeSlider.Maximum = maximumDimension;
        SizeHeightSlider.Maximum = maximumDimension;
        WidthSlider.Maximum = maximumDimension;
        HeightSlider.Maximum = maximumDimension;
        DiameterSlider.Maximum = maximumDimension;

        _gdiTextCache.Clear();
        _gdiTextInkCache.Clear();
        _gdiTextLayerCache.Clear();

        if (resetZoom)
        {
            SetCanvasZoom(universal ? 1.0 : 1.8);
        }
    }

    private static bool ShouldShowPreviewMask(string deviceModel)
    {
        return !string.Equals(deviceModel, "hydroshift-ii-lcd-s", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(deviceModel, "hydroshift-ii-lcd-c", StringComparison.OrdinalIgnoreCase);
    }

    private void SetUniversalOrientationFromLayers()
    {
        if (!IsWideScreenDeviceSelected() || Layers.Count == 0)
        {
            return;
        }

        var maxX = Layers
            .Select(layer => double.TryParse(layer.X, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0)
            .DefaultIfEmpty()
            .Max();
        var maxY = Layers
            .Select(layer => double.TryParse(layer.Y, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0)
            .DefaultIfEmpty()
            .Max();
        var orientation = maxY > 480 && maxY > maxX ? "portrait" : "landscape";

        _syncingUniversalOrientation = true;
        foreach (var item in UniversalOrientationCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), orientation, StringComparison.OrdinalIgnoreCase))
            {
                UniversalOrientationCombo.SelectedItem = item;
                break;
            }
        }
        _syncingUniversalOrientation = false;
        UpdateCanvasConfiguration(resetZoom: false);
    }

    private void SyncDeviceFromTemplatePath(string templatePath)
    {
        var model = templatePath.Contains(Vm92DeviceModel, StringComparison.OrdinalIgnoreCase)
            ? Vm92DeviceModel
            : templatePath.Contains(UniversalScreenDeviceModel, StringComparison.OrdinalIgnoreCase)
            ? UniversalScreenDeviceModel
            : templatePath.Contains("hydroshift-ii-lcd-c", StringComparison.OrdinalIgnoreCase)
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

    private void RefreshTemplateList(bool selectFirstWhenMissing = false)
    {
        var selectedPath = _currentTemplatePath;
        var wasLoading = _isLoading;
        _isLoading = true;
        TemplateOptions.Clear();
        var deviceModel = GetSelectedDeviceModel();
        var templateRoot = GetTemplateRoot(deviceModel);
        if (IsWideScreenDeviceModel(deviceModel))
        {
            try
            {
                _supporter.ExtractMissingPreviewsAsync(
                    deviceModel,
                    templateRoot,
                    GetEmbeddedThumbnailCacheRoot(deviceModel)).GetAwaiter().GetResult();
            }
            catch
            {
            }
        }

        if (Directory.Exists(templateRoot))
        {
            foreach (var path in Directory.EnumerateFiles(templateRoot, "*.template").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var id = Path.GetFileNameWithoutExtension(path);
                TemplateOptions.Add(new TemplateOption
                {
                    Id = id,
                    Path = path,
                    Thumbnail = GetTemplateThumbnail(deviceModel, id)
                });
            }
        }
        SelectTemplateCombo(selectedPath);
        if (TemplateCombo.SelectedItem is null && selectFirstWhenMissing && TemplateOptions.Count > 0)
        {
            TemplateCombo.SelectedItem = TemplateOptions[0];
        }
        SelectedTemplateImage.Source = (TemplateCombo.SelectedItem as TemplateOption)?.Thumbnail;
        TemplateSelectionText.Text = (TemplateCombo.SelectedItem as TemplateOption)?.Id ?? "";
        _isLoading = wasLoading;
    }

    private BitmapSource? GetTemplateThumbnail(string deviceModel, string templateId)
    {
        var previewDirectory = Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", deviceModel, "preview");
        var previewPath = GetTemplatePreviewAliases(templateId)
            .SelectMany(id => new[]
            {
                Path.Combine(previewDirectory, $"template_{id}.png"),
                Path.Combine(previewDirectory, $"{id}.png")
            })
            .FirstOrDefault(File.Exists);
        previewPath ??= new[]
            {
                Path.Combine(GetEmbeddedThumbnailCacheRoot(deviceModel), $"{templateId}.png"),
                Path.Combine(GetEmbeddedThumbnailCacheRoot(deviceModel), $"{templateId.TrimEnd('_')}.png")
            }
            .FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(previewPath))
        {
            return LoadPackImage(GetDeviceImagePath(deviceModel), 80);
        }

        var cacheKey = $"{previewPath}|{File.GetLastWriteTimeUtc(previewPath).Ticks}";
        if (_templateThumbnailCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 96;
            bitmap.UriSource = new Uri(previewPath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            _templateThumbnailCache[cacheKey] = bitmap;
            return bitmap;
        }
        catch
        {
            return LoadPackImage(GetDeviceImagePath(deviceModel), 80);
        }
    }

    private static string GetEmbeddedThumbnailCacheRoot(string deviceModel) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LianLiThemeEditor",
            "TemplateThumbnails",
            deviceModel);

    private static string GetDeviceImagePath(string deviceModel) => deviceModel switch
    {
        "hydroshift-ii-lcd-c" => "Assets/Devices/hydroshift-ii-lcd-c.png",
        "universal-screen-8.8-inch" => "Assets/Devices/universal-screen-8.8.png",
        Vm92DeviceModel => "Assets/Devices/vm-9.2.png",
        _ => "Assets/Devices/hydroshift-ii-lcd-s.png"
    };

    private static BitmapSource? LoadPackImage(string relativePath, int decodeWidth = 96)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = decodeWidth;
            bitmap.UriSource = new Uri($"pack://application:,,,/{relativePath}", UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
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

    private void FitPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        PreviewScrollViewer.UpdateLayout();
        var availableWidth = PreviewScrollViewer.ViewportWidth;
        var availableHeight = PreviewScrollViewer.ViewportHeight;
        if (availableWidth <= 0 || availableHeight <= 0)
        {
            availableWidth = PreviewScrollViewer.ActualWidth;
            availableHeight = PreviewScrollViewer.ActualHeight;
        }

        var widthScale = Math.Max(0.2, (availableWidth - 8) / Math.Max(1, PreviewFrame.Width));
        var heightScale = Math.Max(0.2, (availableHeight - 8) / Math.Max(1, PreviewFrame.Height));
        SetCanvasZoom(Math.Min(widthScale, heightScale));
        PreviewScrollViewer.ScrollToHorizontalOffset(0);
        PreviewScrollViewer.ScrollToVerticalOffset(0);
    }

    private void SetCanvasZoom(double zoom)
    {
        _canvasZoom = Math.Clamp(Math.Round(zoom, 2), 0.2, 5.0);
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

    private ThemeExportSnapshot CreateThemeExportSnapshot(
        string deviceModel,
        string? exportTemplateId = null,
        string? templatePath = null,
        IEnumerable<LayerRow>? sourceLayers = null,
        string? backgroundPathOverride = null,
        string? backgroundEntryNameOverride = null)
    {
        var layers = sourceLayers?.ToList() ?? Layers.ToList();
        var actualTemplateId = string.IsNullOrWhiteSpace(exportTemplateId)
            ? _currentTemplateId
            : exportTemplateId;
        var actualTemplatePath = string.IsNullOrWhiteSpace(templatePath)
            ? _currentTemplatePath
            : templatePath;
        var backgroundPath = string.IsNullOrWhiteSpace(backgroundPathOverride)
            ? _currentBackgroundPath
            : backgroundPathOverride;

        var animationMediaName = layers
            .FirstOrDefault(layer => string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
            ?.Media ?? "";
        var resolvedBackground = ResolveBackgroundPath(backgroundPath, animationMediaName);
        var templateBackgroundName = Path.GetFileName(backgroundEntryNameOverride);
        if (string.IsNullOrWhiteSpace(templateBackgroundName))
        {
            templateBackgroundName = Path.GetFileName(animationMediaName);
        }
        if (string.IsNullOrWhiteSpace(templateBackgroundName))
        {
            templateBackgroundName = Path.GetFileName(backgroundPath);
        }

        var exportBackground = ResolveBackgroundVariant(resolvedBackground, Path.GetExtension(templateBackgroundName));
        var imagePaths = layers
            .Where(layer => string.Equals(layer.Type, "GraphImage", StringComparison.OrdinalIgnoreCase))
            .Select(layer => ResolveLayerMediaPath(layer.Media))
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ThemeExportSnapshot
        {
            DeviceModel = deviceModel,
            TemplateId = actualTemplateId,
            ExportTemplateId = actualTemplateId,
            TemplatePath = actualTemplatePath,
            BackgroundPath = exportBackground,
            BackgroundEntryName = templateBackgroundName,
            ImagePaths = imagePaths
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

            var canvas = GetTemplateCanvasPixels();
            var brush = new VisualBrush(PreviewSurface);
            var drawingVisual = new DrawingVisual();
            using (var context = drawingVisual.RenderOpen())
            {
                context.DrawRectangle(brush, null, new Rect(0, 0, canvas.Width, canvas.Height));
            }

            var bitmap = new RenderTargetBitmap(canvas.Width, canvas.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(drawingVisual);

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

    private async Task SaveAndApplyThemePreviewAsync(
        string deviceModel,
        string templatePath,
        string templateId,
        IEnumerable<string>? previewAliases = null,
        bool embedInTemplate = true)
    {
        try
        {
            var previewBytes = RenderCurrentThemePreview(cleanEditorOverlay: true);
            if (previewBytes == null || previewBytes.Length == 0) return;

            // 1. Save locally for L-Connect 3 UI to update instantly
            var previewDir = Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", deviceModel, "preview");
            if (Directory.Exists(previewDir))
            {
                var previewIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    templateId
                };
                if (previewAliases != null)
                {
                    foreach (var alias in previewAliases.Where(value => !string.IsNullOrWhiteSpace(value)))
                    {
                        previewIds.Add(alias);
                    }
                }

                foreach (var previewId in previewIds)
                {
                    var previewPath = Path.Combine(previewDir, $"template_{previewId}.png");
                    await File.WriteAllBytesAsync(previewPath, previewBytes);
                }
            }

            if (embedInTemplate)
            {
                // Save to temp and update inside the .template file using the
                // supporter, which understands both current and legacy theme types.
                var tempPath = Path.Combine(Path.GetTempPath(), $"theme_preview_{Guid.NewGuid():N}.png");
                await File.WriteAllBytesAsync(tempPath, previewBytes);
                try
                {
                    await _supporter.UpdateThemePreviewAsync(deviceModel, templatePath, tempPath);
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }
            }

            var currentOption = TemplateOptions.FirstOrDefault(option =>
                string.Equals(option.Path, templatePath, StringComparison.OrdinalIgnoreCase));
            if (currentOption != null)
            {
                currentOption.Thumbnail = GetTemplateThumbnail(deviceModel, currentOption.Id);
                SelectedTemplateImage.Source = currentOption.Thumbnail;
                TemplateCombo.Items.Refresh();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating theme preview: {ex.Message}");
            throw new InvalidOperationException(
                "The current theme preview could not be embedded into the template.",
                ex);
        }
    }

    private static IEnumerable<string> GetTemplatePreviewAliases(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            yield break;
        }

        yield return templateId;

        // L-Connect creates timestamp-suffixed working copies but often keeps the
        // original ID for the template card shown in the UI.
        var baseId = Regex.Replace(
            templateId,
            @"_20\d{6}(?:_\d{6})?.*$",
            "",
            RegexOptions.CultureInvariant);
        if (!string.IsNullOrWhiteSpace(baseId) &&
            !string.Equals(baseId, templateId, StringComparison.OrdinalIgnoreCase))
        {
            yield return baseId;
        }
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
            var primaryBackgroundPath = backgroundPath;
            if (IsWideScreenDeviceModel(snapshot.DeviceModel))
            {
                var h264Path = ResolveBackgroundVariant(backgroundPath, ".h264");
                if (!string.IsNullOrWhiteSpace(h264Path) && File.Exists(h264Path))
                {
                    primaryBackgroundPath = h264Path;
                }
            }

            manifest.BackgroundFile = $"background/{Path.GetFileName(primaryBackgroundPath)}";
            archive.CreateEntryFromFile(primaryBackgroundPath, manifest.BackgroundFile, CompressionLevel.Optimal);

            var companionExtension = Path.GetExtension(primaryBackgroundPath).Equals(".h264", StringComparison.OrdinalIgnoreCase)
                ? ".mp4"
                : Path.GetExtension(primaryBackgroundPath).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                    ? ".h264"
                    : "";
            if (!string.IsNullOrWhiteSpace(companionExtension))
            {
                var companionPath = ResolveBackgroundVariant(primaryBackgroundPath, companionExtension);
                if (!string.IsNullOrWhiteSpace(companionPath) && File.Exists(companionPath))
                {
                    var companionEntryName = $"background/{Path.GetFileName(companionPath)}";
                    if (!string.Equals(companionEntryName, manifest.BackgroundFile, StringComparison.OrdinalIgnoreCase))
                    {
                        archive.CreateEntryFromFile(companionPath, companionEntryName, CompressionLevel.Optimal);
                    }
                }
            }
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
        var exportTemplateId = string.IsNullOrWhiteSpace(snapshot.ExportTemplateId)
            ? CreateUniqueExportTemplateId(snapshot.TemplateId, snapshot.DeviceModel)
            : snapshot.ExportTemplateId;

        // Revert to a flat ZIP structure: no subdirectory prefix!
        var templateEntryName = $"{exportTemplateId}.template";
        archive.CreateEntryFromFile(snapshot.TemplatePath, templateEntryName, CompressionLevel.Optimal);

        var addedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            templateEntryName
        };

        if (string.IsNullOrWhiteSpace(snapshot.BackgroundPath) || !File.Exists(snapshot.BackgroundPath))
        {
            return;
        }

        var backgroundEntryName = Path.GetFileName(snapshot.BackgroundEntryName);
        if (string.IsNullOrWhiteSpace(backgroundEntryName))
        {
            backgroundEntryName = Path.GetFileName(snapshot.BackgroundPath);
        }

        AddFlatPackageFile(archive, addedEntries, snapshot.BackgroundPath, backgroundEntryName);

        var backgroundExtension = Path.GetExtension(snapshot.BackgroundPath);
        var companionExtension = backgroundExtension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            ? ".h264"
            : backgroundExtension.Equals(".h264", StringComparison.OrdinalIgnoreCase)
                ? ".mp4"
                : "";
        if (!string.IsNullOrWhiteSpace(companionExtension))
        {
            var companionPath = Path.ChangeExtension(snapshot.BackgroundPath, companionExtension);
            var companionEntryName = Path.ChangeExtension(backgroundEntryName, companionExtension);
            AddFlatPackageFile(archive, addedEntries, companionPath, companionEntryName);
        }
    }

    private static void AddFlatPackageFile(
        ZipArchive archive,
        HashSet<string> addedEntries,
        string sourcePath,
        string entryName)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return;
        }

        var safeEntryName = Path.GetFileName(entryName);
        if (string.IsNullOrWhiteSpace(safeEntryName))
        {
            safeEntryName = Path.GetFileName(sourcePath);
        }

        if (addedEntries.Add(safeEntryName))
        {
            archive.CreateEntryFromFile(sourcePath, safeEntryName, CompressionLevel.Optimal);
        }
    }

    private static void ConvertUniversal88LConnectZipToVm92(
        string sourcePackagePath,
        string destinationPackagePath,
        string exportTemplateId)
    {
        var sourceFullPath = Path.GetFullPath(sourcePackagePath);
        var destinationFullPath = Path.GetFullPath(destinationPackagePath);
        if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Choose a different output file for the converted package.");
        }

        using var sourceArchive = ZipFile.OpenRead(sourceFullPath);
        if (sourceArchive.GetEntry("manifest.json") != null)
        {
            throw new InvalidDataException("Choose an L-Connect ZIP package, not a Theme Editor .lltheme package.");
        }

        var entries = sourceArchive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .ToList();
        var templateEntries = entries
            .Where(entry => entry.Name.EndsWith(".template", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (templateEntries.Count == 0)
        {
            throw new InvalidDataException("No .template file was found in the selected ZIP.");
        }

        if (templateEntries.Count > 1)
        {
            throw new InvalidDataException("The selected ZIP contains more than one .template file.");
        }

        var templateEntry = templateEntries[0];
        var convertedTemplateName = $"{SanitizeFileName(exportTemplateId)}.template";
        if (string.IsNullOrWhiteSpace(convertedTemplateName) ||
            string.Equals(convertedTemplateName, ".template", StringComparison.OrdinalIgnoreCase))
        {
            convertedTemplateName = $"{Path.GetFileNameWithoutExtension(templateEntry.Name)}-VM92.template";
        }

        var tempDestination = Path.Combine(
            Path.GetDirectoryName(destinationFullPath)!,
            $"{Path.GetFileNameWithoutExtension(destinationFullPath)}.{Guid.NewGuid():N}.tmp");
        if (File.Exists(tempDestination)) File.Delete(tempDestination);

        try
        {
            using (var destinationArchive = ZipFile.Open(tempDestination, ZipArchiveMode.Create))
            {
                var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CopyZipEntry(templateEntry, destinationArchive, convertedTemplateName, addedNames);

                foreach (var entry in entries.Where(entry => !ReferenceEquals(entry, templateEntry)))
                {
                    var entryName = GetSafeFlatZipEntryName(entry);
                    CopyZipEntry(entry, destinationArchive, entryName, addedNames);
                }
            }

            if (File.Exists(destinationFullPath))
            {
                File.Delete(destinationFullPath);
            }

            File.Move(tempDestination, destinationFullPath);
        }
        finally
        {
            TryDeleteFile(tempDestination);
        }
    }

    private static string GetSafeFlatZipEntryName(ZipArchiveEntry entry)
    {
        var fullName = entry.FullName.Replace('\\', '/');
        if (Path.IsPathRooted(fullName) || fullName.Split('/').Any(part => part == ".."))
        {
            throw new InvalidDataException($"The ZIP contains an unsafe file path: {entry.FullName}");
        }

        var name = Path.GetFileName(fullName);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidDataException("The ZIP contains an unnamed file entry.");
        }

        return name;
    }

    private static void CopyZipEntry(
        ZipArchiveEntry sourceEntry,
        ZipArchive destinationArchive,
        string destinationEntryName,
        HashSet<string> addedNames)
    {
        destinationEntryName = GetSafeFileName(destinationEntryName);
        if (!addedNames.Add(destinationEntryName))
        {
            throw new InvalidDataException($"The ZIP contains duplicate file names: {destinationEntryName}");
        }

        var destinationEntry = destinationArchive.CreateEntry(destinationEntryName, CompressionLevel.Optimal);
        using var sourceStream = sourceEntry.Open();
        using var destinationStream = destinationEntry.Open();
        sourceStream.CopyTo(destinationStream);
    }

    private static string GetSafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name) ||
            Path.IsPathRooted(name) ||
            name.Split('/', '\\').Any(part => part == ".."))
        {
            throw new InvalidDataException($"Unsafe ZIP file name: {fileName}");
        }

        return name;
    }

    private static void InstallPackagedFonts(ZipArchive archive)
    {
        var fontEntries = archive.Entries.Where(entry =>
            !string.IsNullOrWhiteSpace(entry.Name) &&
            (Path.GetExtension(entry.Name).Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
             Path.GetExtension(entry.Name).Equals(".otf", StringComparison.OrdinalIgnoreCase))).ToList();
        if (fontEntries.Count == 0) return;

        var fontRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LianLiThemeEditor", "Fonts");
        Directory.CreateDirectory(fontRoot);
        foreach (var entry in fontEntries)
        {
            var safeName = GetSafeFileName(entry.Name);
            var destination = Path.Combine(fontRoot, safeName);
            ExtractPackageEntry(entry, destination);
        }

        InitializeCustomFonts();
        foreach (var entry in fontEntries)
        {
            var path = Path.Combine(fontRoot, GetSafeFileName(entry.Name));
            try
            {
                var glyph = new GlyphTypeface(new Uri(path, UriKind.Absolute));
                var family = glyph.Win32FamilyNames.Values.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(family)) EnsureLConnectFontInstalled(family);
            }
            catch { }
        }
    }

    private async Task<TemplateOption> ImportThemePackageAsync(
        string packagePath,
        string preferredTemplateId = "",
        bool overwriteExisting = false,
        bool installThroughLConnect = false)
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

        InstallPackagedFonts(archive);

        var deviceModel = manifest.DeviceModel;
        if (deviceModel is not ("hydroshift-ii-lcd-s" or "hydroshift-ii-lcd-c" or UniversalScreenDeviceModel or Vm92DeviceModel))
        {
            throw new InvalidDataException("The theme package contains an unsupported device model.");
        }

        var templateEntry = GetSafePackageEntry(archive, manifest.TemplateFile);
        var templateRoot = GetTemplateRoot(deviceModel);
        var imageRoot = Path.Combine(Path.GetDirectoryName(templateRoot)!, "image");
        Directory.CreateDirectory(templateRoot);
        Directory.CreateDirectory(imageRoot);

        var baseId = SanitizeFileName(string.IsNullOrWhiteSpace(preferredTemplateId)
            ? manifest.TemplateId
            : preferredTemplateId);
        if (string.IsNullOrWhiteSpace(baseId)) baseId = "ImportedTheme";
        var importedId = overwriteExisting
            ? baseId
            : GetUniqueTemplateId(templateRoot, $"{baseId}-imported");
        var destinationTemplate = Path.Combine(templateRoot, $"{importedId}.template");
        if (overwriteExisting && File.Exists(destinationTemplate))
        {
            File.Delete(destinationTemplate);
        }
        ExtractPackageEntry(templateEntry, destinationTemplate);
        await _supporter.NormalizeTemplateIdentityAsync(deviceModel, destinationTemplate, importedId);

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
            var imageLayersToApply = new List<LayerRow>();
            foreach (var layer in importedTemplate.Layers.Where(layer =>
                         string.Equals(layer.Type, "GraphImage", StringComparison.OrdinalIgnoreCase) &&
                         importedImages.ContainsKey(Path.GetFileName(layer.Media))))
            {
                layer.Media = importedImages[Path.GetFileName(layer.Media)];
                imageLayersToApply.Add(layer);
            }
            await _supporter.ApplyLayersAsync(deviceModel, destinationTemplate, imageLayersToApply);
        }

        string backgroundTemp = "";
        if (!string.IsNullOrWhiteSpace(manifest.BackgroundFile))
        {
            var backgroundEntry = GetSafePackageEntry(archive, manifest.BackgroundFile);
            var extension = Path.GetExtension(backgroundEntry.FullName);
            backgroundTemp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
            ExtractPackageEntry(backgroundEntry, backgroundTemp);
        }

        var importedBackgroundPath = "";
        try
        {
            if (!string.IsNullOrWhiteSpace(backgroundTemp))
            {
                var canvas = GetTemplateCanvasPixels(deviceModel);
                importedBackgroundPath = await _supporter.SetBackgroundMediaAsync(
                    deviceModel,
                    destinationTemplate,
                    backgroundTemp,
                    canvas.Width,
                    canvas.Height);
                if (string.IsNullOrWhiteSpace(importedBackgroundPath) || !File.Exists(importedBackgroundPath))
                {
                    throw new InvalidDataException("L-Connect did not create the imported theme background media.");
                }
                AppLogger.Info($"Theme background imported for {importedId}: {importedBackgroundPath}");
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

        var lConnectId = importedId;
        var installedTemplatePath = destinationTemplate;
        if (installThroughLConnect)
        {
            var importZip = "";
            byte[] preservedTemplateBytes = Array.Empty<byte>();
            try
            {
                var mediaFiles = importedImages.Values
                    .Select(name => Path.Combine(imageRoot, name))
                    .Concat(string.IsNullOrWhiteSpace(importedBackgroundPath)
                        ? Array.Empty<string>()
                        : GetBackgroundMediaBundleFiles(importedBackgroundPath))
                    .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                importZip = CreateLConnectImportZipFromFiles(destinationTemplate, mediaFiles);
                if (overwriteExisting && File.Exists(destinationTemplate))
                {
                    // L-Connect must not see the editor-side copy as a pre-existing
                    // imported theme, but keep the normalized copy available as a
                    // fallback in case its asynchronous import is not visible yet.
                    preservedTemplateBytes = await File.ReadAllBytesAsync(destinationTemplate);
                    File.Delete(destinationTemplate);
                }

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                foreach (var path in GetLConnectDevicePaths())
                {
                    var previousImportedIds = (await GetLConnectTemplateIdsAsync(client, path))
                        .Where(id => IsLConnectImportedTemplateId(id, importedId))
                        .ToList();
                    var importedLConnectId = await ImportTemplateIntoLConnectAsync(client, path, importZip);
                    if (!string.IsNullOrWhiteSpace(importedLConnectId))
                    {
                        lConnectId = importedLConnectId;
                        if (!string.IsNullOrWhiteSpace(importedBackgroundPath) && File.Exists(importedBackgroundPath))
                        {
                            await CopyTemplateBackgroundAsync(
                                client,
                                path,
                                deviceModel,
                                importedLConnectId,
                                importedBackgroundPath);
                        }
                        foreach (var previousId in previousImportedIds.Where(id =>
                                     !string.Equals(id, importedLConnectId, StringComparison.OrdinalIgnoreCase)))
                        {
                            await SendLConnectDeviceRequestAsync(
                                client,
                                path,
                                "DeleteTemplate",
                                JsonSerializer.Serialize(previousId),
                                requireDataSuccess: true);
                        }
                        if (previousImportedIds.Count > 0)
                        {
                            await SendLConnectDeviceRequestAsync(client, path, "ReloadAssets", "{}");
                        }
                        var resolved = "";
                        for (var attempt = 0; attempt < 10; attempt++)
                        {
                            resolved = ResolveTemplatePathByIdOrAlias(deviceModel, importedLConnectId);
                            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                            {
                                break;
                            }

                            await Task.Delay(150);
                        }
                        if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                        {
                            installedTemplatePath = resolved;
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("L-Connect removable import failed; keeping direct installed template.", ex);
                if (!File.Exists(destinationTemplate))
                {
                    if (preservedTemplateBytes.Length > 0)
                    {
                        await File.WriteAllBytesAsync(destinationTemplate, preservedTemplateBytes);
                    }
                    else
                    {
                        ExtractPackageEntry(templateEntry, destinationTemplate);
                    }
                }
            }
            finally
            {
                TryDeleteFile(importZip);
            }

            if (!File.Exists(installedTemplatePath))
            {
                if (preservedTemplateBytes.Length == 0)
                {
                    throw new FileNotFoundException(
                        "L-Connect imported the theme but did not create a readable template file.",
                        installedTemplatePath);
                }

                await File.WriteAllBytesAsync(destinationTemplate, preservedTemplateBytes);
                installedTemplatePath = destinationTemplate;
                AppLogger.Info($"L-Connect import was not visible yet; restored editor template: {destinationTemplate}");
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

        return new TemplateOption
        {
            Id = importedId,
            LConnectId = lConnectId,
            Path = installedTemplatePath,
            BackgroundPath = importedBackgroundPath
        };
    }

    private static ZipArchiveEntry GetSafePackageEntry(ZipArchive archive, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) ||
            Path.IsPathRooted(entryName) ||
            entryName.Split('/', '\\').Any(part => part == ".."))
        {
            throw new InvalidDataException("The theme package contains an unsafe file path.");
        }

        var normalizedEntryName = entryName.Replace('\\', '/');
        return archive.GetEntry(entryName)
               ?? archive.GetEntry(normalizedEntryName)
               ?? archive.Entries.FirstOrDefault(entry =>
                   string.Equals(entry.FullName.Replace('\\', '/'), normalizedEntryName, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidDataException($"Package file is missing: {entryName}");
    }

    private static void ExtractPackageEntry(ZipArchiveEntry entry, string destinationPath)
    {
        var fullDestination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        entry.ExtractToFile(fullDestination, true);
    }

    private async void RefreshGalleryButton_Click(object sender, RoutedEventArgs e)
    {
        _galleryLoadStarted = true;
        await LoadThemeGalleryAsync(forceRefresh: true);
    }

    private void GalleryFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || GalleryEmptyText == null || GalleryItemsControl == null)
        {
            return;
        }

        ApplyGalleryFilter();
    }

    private void GalleryComboFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || GalleryEmptyText == null || GalleryItemsControl == null)
        {
            return;
        }

        ApplyGalleryFilter();
    }

    private void GalleryScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isLoadingGalleryPreviews || GalleryVisibleThemes.Count == 0)
        {
            return;
        }

        if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 420)
        {
            _ = LoadVisibleGalleryPreviewsAsync();
        }
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MainTabs))
        {
            return;
        }

        MainTabs.BeginAnimation(OpacityProperty, null);
        MainTabs.Opacity = 0.92;
        MainTabs.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });

        EditorToolbar.Visibility = MainTabs.SelectedIndex == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        FooterBorder.Visibility = Visibility.Visible;
        if (MainTabs.SelectedItem == GalleryTab && !_galleryLoadStarted)
        {
            _galleryLoadStarted = true;
            _ = LoadThemeGalleryAsync();
        }
    }

    private void OpenGitHubIssuesButton_Click(object sender, RoutedEventArgs e)
    {
        OpenExternalUrl(GitHubIssuesUrl);
    }

    private void OpenGitHubButton_Click(object sender, RoutedEventArgs e)
    {
        OpenExternalUrl(GitHubRepoUrl);
    }

    private void CopyDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        var info = string.Join(Environment.NewLine, new[]
        {
            "Lian Li LCD Theme Editor diagnostics",
            $"Version: {GetAppDisplayVersion()}",
            $"Built: {BuildInfo.BuiltAt}",
            $"Device: {GetSelectedDeviceDisplayName()} ({GetSelectedDeviceModel()})",
            $"Template: {_currentTemplateId}",
            $"Template path: {_currentTemplatePath}",
            $"Background: {_currentBackgroundPath}",
            $"Layer count: {Layers.Count}",
            $"OS: {Environment.OSVersion}",
            $".NET: {Environment.Version}"
        });

        Clipboard.SetText(info);
        SetStatus(GetLanguageText("status.diagnosticInfoCopied", "Diagnostic info copied."));
    }

    private void CreateDiagnosticPackageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = GetLanguageText("diagnostics.save", "Save diagnostic package"),
            Filter = GetLanguageText("dialogs.zipFilter", "ZIP (*.zip)|*.zip"),
            FileName = $"LianLiThemeEditor-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var settings = Path.Combine(_supporter.WorkingDirectory, "theme_editor_settings.json");
            _diagnosticService.CreatePackage(dialog.FileName, BuildDiagnosticSummary(), new[] { settings });
            SetStatus(GetLanguageText("status.diagnosticsCreated", "Diagnostic package created."));
        }
        catch (Exception ex)
        {
            AppLogger.Error("Diagnostic package creation failed.", ex);
            MessageBox.Show(this, ex.Message, GetLanguageText("diagnostics.failed", "Diagnostic package failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string BuildDiagnosticSummary() => string.Join(Environment.NewLine, new[]
    {
        "Lian Li LCD Theme Editor diagnostics",
        $"Version: {GetAppDisplayVersion()}", $"Built: {BuildInfo.BuiltAt}",
        $"Device: {GetSelectedDeviceDisplayName()} ({GetSelectedDeviceModel()})",
        $"Template: {_currentTemplateId}", $"Template path: {_currentTemplatePath}",
        $"Background: {_currentBackgroundPath}", $"Layers: {Layers.Count}",
        $"Dirty layers: {_dirtyLayers.Count}", $"OS: {Environment.OSVersion}", $".NET: {Environment.Version}"
    });

    private bool ShowThemeValidation(ThemeValidationResult validation)
    {
        var panel = new StackPanel { Margin = new Thickness(20), Width = 560 };
        panel.Children.Add(new TextBlock { Text = GetLanguageText("validation.title", "Theme package check"), FontSize = 18, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"{validation.TemplateId}  |  {validation.DeviceModel}", Margin = new Thickness(0, 5, 0, 12), Foreground = (Brush)FindResource("BrTextTertiary") });
        foreach (var issue in validation.Issues)
        {
            var color = issue.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase) ? "#E05A67" : issue.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? "#F0B84B" : "#62D6B5";
            panel.Children.Add(new TextBlock { Text = $"{issue.Severity}: {issue.Message}", Foreground = (Brush)new BrushConverter().ConvertFromString(color)!, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) });
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = GetLanguageText("common.cancel", "Cancel"), Width = 90, IsCancel = true };
        var proceed = new Button { Content = GetLanguageText("validation.continue", "Continue"), Width = 90, IsDefault = true, Margin = new Thickness(8, 0, 0, 0), IsEnabled = validation.IsValid };
        buttons.Children.Add(cancel); buttons.Children.Add(proceed); panel.Children.Add(buttons);
        var window = new Window { Owner = this, Content = panel, Title = GetLanguageText("validation.windowTitle", "Validate theme"), SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
        proceed.Click += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true;
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = GetLanguageText("updates.checking", "Checking GitHub releases...");
        try
        {
            var json = await SharedHttpClient.GetStringAsync(GitHubLatestReleaseApiUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var latestName = GetJsonString(root, "name");
            var latestTag = GetJsonString(root, "tag_name", latestName);
            var releaseUrl = GetJsonString(root, "html_url", GitHubReleasesUrl);
            var currentVersion = GetAppDisplayVersion();

            if (LooksLikeSameVersion(currentVersion, latestTag) ||
                LooksLikeSameVersion(currentVersion, latestName))
            {
                UpdateStatusText.Text = FormatLanguageText("updates.current", "You are up to date. Current version: {0}.", currentVersion);
                SetStatus(GetLanguageText("updates.noUpdate", "No update found."));
                return;
            }

            UpdateStatusText.Text = FormatLanguageText("updates.latestFound", "Latest release: {0}. Current version: {1}.", latestTag, currentVersion);
            var result = MessageBox.Show(
                this,
                FormatLanguageText("updates.openReleasePrompt", "Latest release: {0}\nCurrent version: {1}\n\nOpen the release page?", latestTag, currentVersion),
                GetLanguageText("updates.availableTitle", "Update available"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
            {
                OpenExternalUrl(releaseUrl);
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = FormatLanguageText("updates.failed", "Could not check updates: {0}", ex.Message);
            var result = MessageBox.Show(
                this,
                GetLanguageText("updates.failedPrompt", "The automatic check could not reach GitHub releases. Open the releases page instead?"),
                GetLanguageText("updates.failedTitle", "Update check failed"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
            {
                OpenExternalUrl(GitHubReleasesUrl);
            }
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private static void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private static string GetAppDisplayVersion()
    {
        var assembly = typeof(MainWindow).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return assembly.GetName().Version?.ToString() ?? "Unknown";
    }

    private static string GetAppBuildDisplayText()
    {
        return $"Version {GetAppDisplayVersion()}  |  Built {BuildInfo.BuiltAt}";
    }

    private static bool LooksLikeSameVersion(string currentVersion, string latestVersion)
    {
        var current = NormalizeVersionLabel(currentVersion);
        var latest = NormalizeVersionLabel(latestVersion);
        return !string.IsNullOrWhiteSpace(current) &&
               !string.IsNullOrWhiteSpace(latest) &&
               string.Equals(current, latest, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersionLabel(string version)
    {
        return Regex.Replace(version ?? "", @"[^0-9a-z]+", "", RegexOptions.IgnoreCase)
            .ToLowerInvariant()
            .TrimStart('v');
    }

    private async void GalleryDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as Button)?.CommandParameter is not GalleryThemeItem item || item.IsBusy)
        {
            return;
        }

        var downloadKey = GetGalleryDownloadKey(item);
        if (_recentGalleryDownloads.TryGetValue(downloadKey, out var lastDownloadUtc) &&
            DateTime.UtcNow - lastDownloadUtc < TimeSpan.FromSeconds(5))
        {
            return;
        }
        if (!_activeGalleryDownloads.Add(downloadKey))
        {
            return;
        }

        item.IsBusy = true;
        try
        {
            if (await DownloadAndInstallGalleryThemeAsync(item, GalleryActivateAfterInstallCheck.IsChecked == true))
            {
                _recentGalleryDownloads[downloadKey] = DateTime.UtcNow;
            }
        }
        finally
        {
            _activeGalleryDownloads.Remove(downloadKey);
        }
    }

    private static string GetGalleryDownloadKey(GalleryThemeItem item)
    {
        var packageName = GetGalleryPackageFileBaseName(item.PackageUrl);
        if (!string.IsNullOrWhiteSpace(packageName))
        {
            return $"{item.DeviceModel}|{SanitizeFileName(packageName)}";
        }

        if (!string.IsNullOrWhiteSpace(item.PackageUrl))
        {
            return $"{item.DeviceModel}|{item.PackageUrl.Trim()}";
        }

        return $"{item.DeviceModel}|{SanitizeFileName(item.Id)}";
    }

    private async void GalleryDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as Button)?.CommandParameter is not GalleryThemeItem item)
        {
            return;
        }

        await ShowGalleryThemePreviewAsync(item);
    }

    private async void GalleryPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is not GalleryThemeItem item)
        {
            return;
        }

        await ShowGalleryThemePreviewAsync(item);
    }

    private async Task ShowGalleryThemePreviewAsync(GalleryThemeItem item)
    {
        if (item.Preview == null && !item.IsPreviewLoading)
        {
            item.IsPreviewLoading = true;
            item.Preview = await LoadGalleryPreviewAsync(item.PreviewUrl)
                           ?? await LoadGalleryPreviewAsync(item.PackageUrl);
            item.IsPreviewLoading = false;
        }

        ShowGalleryThemeDetails(item);
    }

    private void ShowGalleryThemeDetails(GalleryThemeItem item)
    {
        var detailsWindow = new Window
        {
            Title = item.Name,
            Owner = this,
            Width = Math.Min(SystemParameters.WorkArea.Width * 0.82, 1180),
            Height = Math.Min(SystemParameters.WorkArea.Height * 0.82, 820),
            MinWidth = 720,
            MinHeight = 520,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = NewBrush("#0B1120", "#0B1120"),
            Foreground = Brushes.White
        };
        detailsWindow.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                detailsWindow.Close();
            }
        };

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var previewHost = new Border
        {
            Background = Brushes.Black,
            BorderBrush = NewBrush("#334155", "#334155"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true
        };
        previewHost.Child = item.Preview != null
            ? new Image { Source = item.Preview, Stretch = Stretch.Uniform }
            : new TextBlock
            {
                Text = "Preview unavailable",
                Foreground = NewBrush("#94A3B8", "#94A3B8"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        Grid.SetRow(previewHost, 0);
        root.Children.Add(previewHost);

        var closeButton = new Button
        {
            Content = "X",
            Width = 34,
            Height = 34,
            MinWidth = 34,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 10, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Background = NewBrush("#CC0B1120", "#CC0B1120"),
            BorderBrush = NewBrush("#64748B", "#64748B"),
            BorderThickness = new Thickness(1),
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            ToolTip = "Close"
        };
        closeButton.Click += (_, _) => detailsWindow.Close();
        Grid.SetRow(closeButton, 0);
        Panel.SetZIndex(closeButton, 5);
        root.Children.Add(closeButton);

        var infoBar = new Border
        {
            Background = NewBrush("#E60B1120", "#E60B1120"),
            BorderBrush = NewBrush("#334155", "#334155"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(28, 16, 28, 18)
        };
        var infoGrid = new Grid();
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = item.Name,
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = $"Author: {item.Author}",
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = NewBrush("#CBD5E1", "#CBD5E1"),
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap
        });
        infoGrid.Children.Add(titleStack);

        var statsStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(statsStack, 1);
        statsStack.Children.Add(new TextBlock
        {
            Text = item.RatingText,
            Foreground = NewBrush("#FDE68A", "#FDE68A"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        statsStack.Children.Add(new TextBlock
        {
            Text = item.StatsText,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = NewBrush("#CBD5E1", "#CBD5E1"),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        infoGrid.Children.Add(statsStack);

        infoBar.Child = infoGrid;
        Grid.SetRow(infoBar, 1);
        root.Children.Add(infoBar);

        detailsWindow.Content = root;
        detailsWindow.ShowDialog();
    }

    private async Task LoadThemeGalleryAsync(bool forceRefresh = false)
    {
        if (forceRefresh)
        {
            GalleryThemes.Clear();
            GalleryVisibleThemes.Clear();
        }

        GalleryEmptyText.Text = "Loading gallery...";
        GalleryEmptyText.Visibility = Visibility.Visible;
        RefreshGalleryButton.IsEnabled = false;

        try
        {
            var json = await LoadRemoteGalleryManifestJsonAsync();
            var officialThemes = LoadGalleryThemesFromJson(json, GalleryRawBaseUrl, isRemote: true);
            var communityThemes = await LoadCommunityGalleryThemesAsync();
            IReadOnlyList<GalleryThemeItem> themes = officialThemes.Concat(communityThemes).ToList();
            GallerySourceText.Text = $"GitHub gallery ({themes.Count} themes)";

            GalleryThemes.Clear();
            foreach (var theme in themes)
            {
                theme.DeviceName = string.IsNullOrWhiteSpace(theme.DeviceName)
                    ? GetDeviceDisplayName(theme.DeviceModel)
                    : theme.DeviceName;
                theme.Status = "Loading preview";
                UpdateGalleryInstalledState(theme);
                GalleryThemes.Add(theme);
            }

            await LoadGalleryStatsAsync();
            ApplyGalleryFilter();
            _ = LoadVisibleGalleryPreviewsAsync();
        }
        catch (Exception ex)
        {
            GalleryThemes.Clear();
            GalleryVisibleThemes.Clear();
            GallerySourceText.Text = "GitHub gallery unavailable";
            GalleryEmptyText.Visibility = Visibility.Visible;
            GalleryEmptyText.Text = $"GitHub gallery could not be loaded. Check your connection and refresh.\n{ex.Message}";
            AppLogger.Error("GitHub gallery could not be loaded.", ex);
        }
        finally
        {
            RefreshGalleryButton.IsEnabled = true;
        }
    }

    private void ApplyGalleryFilter()
    {
        RefreshGalleryInstalledStates();
        var selectedDevices = GetSelectedGalleryDeviceFilters();
        var minimumRating = GetGalleryMinimumRatingFilter();
        var sortMode = (GallerySortCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "default";

        var filteredThemes = GalleryThemes.Where(theme =>
            selectedDevices.Contains(theme.DeviceModel) &&
            (minimumRating <= 0 ||
             (theme.VoteCount > 0 && theme.AverageRating >= minimumRating)));

        filteredThemes = sortMode switch
        {
            "downloads" => filteredThemes
                .OrderByDescending(theme => theme.DownloadCount)
                .ThenBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase),
            "rating" => filteredThemes
                .OrderByDescending(theme => theme.VoteCount > 0)
                .ThenByDescending(theme => theme.AverageRating)
                .ThenByDescending(theme => theme.VoteCount)
                .ThenBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase),
            "votes" => filteredThemes
                .OrderByDescending(theme => theme.VoteCount)
                .ThenByDescending(theme => theme.AverageRating)
                .ThenBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase),
            "name" => filteredThemes
                .OrderBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase),
            _ => filteredThemes
        };

        GalleryVisibleThemes.Clear();
        foreach (var theme in filteredThemes)
        {
            GalleryVisibleThemes.Add(theme);
        }

        GalleryEmptyText.Visibility = GalleryVisibleThemes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GalleryEmptyText.Text = GalleryThemes.Count == 0
            ? "No themes were returned by the GitHub gallery."
            : GalleryVisibleThemes.Count == 0
                ? "No themes match these filters."
                : "";

        _ = LoadVisibleGalleryPreviewsAsync();
    }

    private HashSet<string> GetSelectedGalleryDeviceFilters()
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (GalleryFilterHydroshiftSCheck?.IsChecked == true)
        {
            selected.Add("hydroshift-ii-lcd-s");
        }

        if (GalleryFilterHydroshiftCCheck?.IsChecked == true)
        {
            selected.Add("hydroshift-ii-lcd-c");
        }

        if (GalleryFilterUniversal88Check?.IsChecked == true)
        {
            selected.Add(UniversalScreenDeviceModel);
        }

        if (GalleryFilterVm92Check?.IsChecked == true)
        {
            selected.Add(Vm92DeviceModel);
        }

        if (selected.Count == 0 &&
            GalleryFilterHydroshiftSCheck == null &&
            GalleryFilterHydroshiftCCheck == null &&
            GalleryFilterUniversal88Check == null &&
            GalleryFilterVm92Check == null)
        {
            selected.Add("hydroshift-ii-lcd-s");
            selected.Add("hydroshift-ii-lcd-c");
            selected.Add(UniversalScreenDeviceModel);
            selected.Add(Vm92DeviceModel);
        }

        return selected;
    }

    private double GetGalleryMinimumRatingFilter()
    {
        var tag = (GalleryRatingFilterCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var rating)
            ? rating
            : 0;
    }

    private void RefreshGalleryInstalledStates()
    {
        foreach (var theme in GalleryThemes)
        {
            UpdateGalleryInstalledState(theme);
        }
    }

    private void UpdateGalleryInstalledState(GalleryThemeItem theme)
    {
        var installedPath = FindInstalledGalleryTemplatePath(theme);
        theme.InstalledTemplatePath = installedPath;
        theme.InstalledTemplateId = string.IsNullOrWhiteSpace(installedPath)
            ? ""
            : Path.GetFileNameWithoutExtension(installedPath);
        theme.IsInstalled = !string.IsNullOrWhiteSpace(installedPath);

        if (!theme.IsBusy &&
            (string.IsNullOrWhiteSpace(theme.Status) ||
             string.Equals(theme.Status, "Ready", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(theme.Status, "Installed in L-Connect", StringComparison.OrdinalIgnoreCase)))
        {
            theme.Status = GetGalleryReadyStatus(theme);
        }
    }

    private static string GetGalleryReadyStatus(GalleryThemeItem theme) =>
        theme.IsInstalled ? "Installed in L-Connect" : "Ready";

    private static string FindInstalledGalleryTemplatePath(GalleryThemeItem theme)
    {
        var ids = GetGalleryInstallCandidateIds(theme)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0 || string.IsNullOrWhiteSpace(theme.DeviceModel))
        {
            return "";
        }

        var roots = new[]
        {
            GetTemplateRoot(theme.DeviceModel),
            Path.Combine(@"C:\Program Files\Lian-Li\L-Connect 3", "Assets", theme.DeviceModel, "template")
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var id in ids)
            {
                var exactPath = Path.Combine(root, id + ".template");
                if (File.Exists(exactPath))
                {
                    return exactPath;
                }
            }

            foreach (var id in ids)
            {
                var imported = Directory.EnumerateFiles(root, $"{id}-imported*.template")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(imported))
                {
                    return imported;
                }

                var lConnectImported = Directory.EnumerateFiles(root, $"{id}_????????_??????.template")
                    .Where(path => IsLConnectImportedTemplateId(
                        Path.GetFileNameWithoutExtension(path),
                        id))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(lConnectImported))
                {
                    return lConnectImported;
                }
            }
        }

        return "";
    }

    private static IEnumerable<string> GetGalleryInstallCandidateIds(GalleryThemeItem theme)
    {
        yield return SanitizeFileName(theme.Id);

        var packageName = GetGalleryPackageFileBaseName(theme.PackageUrl);
        if (!string.IsNullOrWhiteSpace(packageName))
        {
            yield return SanitizeFileName(packageName);
        }
    }

    private static string GetGalleryPackageFileBaseName(string packageUrl)
    {
        if (string.IsNullOrWhiteSpace(packageUrl))
        {
            return "";
        }

        try
        {
            if (Uri.TryCreate(packageUrl, UriKind.Absolute, out var uri))
            {
                return Path.GetFileNameWithoutExtension(uri.LocalPath);
            }
        }
        catch
        {
        }

        return Path.GetFileNameWithoutExtension(packageUrl);
    }

    private async Task LoadVisibleGalleryPreviewsAsync()
    {
        if (_isLoadingGalleryPreviews)
        {
            return;
        }

        var visibleSnapshot = GalleryVisibleThemes
            .Where(theme => theme.Preview == null &&
                            !theme.IsPreviewLoading &&
                            (!string.IsNullOrWhiteSpace(theme.PreviewUrl) ||
                             !string.IsNullOrWhiteSpace(theme.PackageUrl)))
            .Take(GalleryPreviewBatchSize)
            .ToList();
        if (visibleSnapshot.Count == 0)
        {
            foreach (var theme in GalleryVisibleThemes.Where(theme => theme.Preview != null && theme.Status == "Loading preview"))
            {
                theme.Status = GetGalleryReadyStatus(theme);
            }
            return;
        }

        _isLoadingGalleryPreviews = true;
        try
        {
            using var semaphore = new SemaphoreSlim(3);
            var tasks = visibleSnapshot.Select(async theme =>
            {
                theme.IsPreviewLoading = true;
                await semaphore.WaitAsync();
                try
                {
                    var previewSource = !string.IsNullOrWhiteSpace(theme.PreviewUrl)
                        ? theme.PreviewUrl
                        : theme.PackageUrl;
                    var preview = await LoadGalleryPreviewAsync(previewSource);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        theme.Preview = preview;
                        theme.Status = preview == null ? "Preview unavailable" : GetGalleryReadyStatus(theme);
                        theme.IsPreviewLoading = false;
                    });
                }
                catch
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        theme.Status = "Preview unavailable";
                        theme.IsPreviewLoading = false;
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }
        finally
        {
            _isLoadingGalleryPreviews = false;
        }
    }

    private static async Task<string> LoadRemoteGalleryManifestJsonAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GalleryContentsApiUrl);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };
            using var response = await SharedHttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var apiJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(apiJson);
            var encoded = GetJsonString(doc.RootElement, "content");
            if (!string.IsNullOrWhiteSpace(encoded))
            {
                var bytes = Convert.FromBase64String(Regex.Replace(encoded, @"\s+", ""));
                return CleanJson(System.Text.Encoding.UTF8.GetString(bytes));
            }
        }
        catch
        {
            // Fall back to raw GitHub with a cache-busting query string below.
        }

        var cacheBustUrl = $"{GalleryManifestUrl}?cacheBust={Guid.NewGuid():N}";
        using var rawResponse = await SharedHttpClient.GetAsync(cacheBustUrl);
        rawResponse.EnsureSuccessStatusCode();
        return CleanJson(await rawResponse.Content.ReadAsStringAsync());
    }

    private static string CleanJson(string json)
    {
        return (json ?? "").TrimStart('\uFEFF', '\u200B', '\r', '\n', ' ', '\t');
    }

    private static IReadOnlyList<GalleryThemeItem> LoadGalleryThemesFromJson(string json, string basePathOrUrl, bool isRemote)
    {
        using var doc = JsonDocument.Parse(CleanJson(json));
        var root = doc.RootElement;
        var themesElement = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("themes", out var themes)
                ? themes
                : default;
        if (themesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<GalleryThemeItem>();
        }

        var result = new List<GalleryThemeItem>();
        foreach (var element in themesElement.EnumerateArray())
        {
            var deviceModel = NormalizeGalleryDeviceModel(GetJsonString(element, "deviceModel"));
            if (deviceModel is not ("hydroshift-ii-lcd-s" or "hydroshift-ii-lcd-c" or UniversalScreenDeviceModel or Vm92DeviceModel))
            {
                continue;
            }

            var packageUrl = ResolveGalleryPath(GetJsonString(element, "packageUrl"), basePathOrUrl, isRemote);
            if (string.IsNullOrWhiteSpace(packageUrl))
            {
                packageUrl = ResolveGalleryPath(GetJsonString(element, "package"), basePathOrUrl, isRemote);
            }

            var previewUrl = ResolveGalleryPath(GetJsonString(element, "previewUrl"), basePathOrUrl, isRemote);
            if (string.IsNullOrWhiteSpace(previewUrl))
            {
                previewUrl = ResolveGalleryPath(GetJsonString(element, "preview"), basePathOrUrl, isRemote);
            }

            if (string.IsNullOrWhiteSpace(packageUrl) || !IsGitHubGalleryAssetUrl(packageUrl))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(previewUrl) && !IsGitHubGalleryAssetUrl(previewUrl))
            {
                previewUrl = "";
            }

            var id = GetJsonString(element, "id");
            result.Add(new GalleryThemeItem
            {
                Id = string.IsNullOrWhiteSpace(id) ? Path.GetFileNameWithoutExtension(packageUrl) : id,
                Name = GetJsonString(element, "name", "Theme"),
                Author = GetJsonString(element, "author", "Unknown"),
                Description = GetJsonString(element, "description"),
                Version = GetJsonString(element, "version"),
                Changelog = GetJsonString(element, "changelog"),
                DeviceModel = deviceModel,
                DeviceName = GetJsonString(element, "deviceName"),
                PackageUrl = packageUrl,
                PreviewUrl = previewUrl
            });
        }

        return result;
    }

    private static string NormalizeGalleryDeviceModel(string value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant()
            .Replace('_', '-')
            .Replace(' ', '-');
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return normalized switch
        {
            "hydroshift-c" or "hydroshift-ii-c" or "hydroshift-ii-lcd-c" => "hydroshift-ii-lcd-c",
            "hydroshift-s" or "hydroshift-ii-s" or "hydroshift-ii-lcd-s" => "hydroshift-ii-lcd-s",
            "universal-screen-8.8" or "universal-8.8" or "universal-screen-8.8-inch" => UniversalScreenDeviceModel,
            "vm-9.2" or "vm-9.2-lcd" or "vm-9.2-inch" => Vm92DeviceModel,
            _ => normalized
        };
    }

    private static string GetJsonString(JsonElement element, string name, string fallback = "")
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private async Task LoadGalleryStatsAsync()
    {
        var apiBaseUrl = GetConfiguredGalleryStatsApiBaseUrl();
        if (string.IsNullOrWhiteSpace(apiBaseUrl) || GalleryThemes.Count == 0)
        {
            return;
        }

        try
        {
            var voterKey = Uri.EscapeDataString(GetOrCreateGalleryVoterKey());
            var json = await SharedHttpClient.GetStringAsync($"{apiBaseUrl.TrimEnd('/')}/themes/stats?voterKey={voterKey}");
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("themes", out var themesElement) ||
                themesElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var byId = GalleryThemes.ToDictionary(theme => theme.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var element in themesElement.EnumerateArray())
            {
                var id = GetJsonString(element, "id");
                if (string.IsNullOrWhiteSpace(id) || !byId.TryGetValue(id, out var theme))
                {
                    continue;
                }

                theme.DownloadCount = GetJsonInt(element, "downloads");
                theme.VoteCount = GetJsonInt(element, "voteCount");
                theme.AverageRating = GetJsonDouble(element, "averageRating");
                theme.UserRating = GetJsonInt(element, "userRating");
            }
        }
        catch
        {
            // Gallery stats are optional; the gallery should keep working if the service is unavailable.
        }
    }

    private async void GalleryVoteButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not GalleryThemeItem item ||
            (sender as Button)?.Tag?.ToString() is not { } ratingText ||
            !int.TryParse(ratingText, out var rating))
        {
            return;
        }

        await SubmitGalleryVoteAsync(item, rating);
    }

    private async Task SubmitGalleryVoteAsync(GalleryThemeItem item, int rating)
    {
        var apiBaseUrl = GetConfiguredGalleryStatsApiBaseUrl();
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            MessageBox.Show(
                this,
                GetLanguageText("gallery.votingNotConfigured", "Gallery voting service is unavailable."),
                GetLanguageText("gallery.votingInactiveTitle", "Voting is not active"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        rating = Math.Clamp(rating, 1, 5);
        try
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(new { voterKey = GetOrCreateGalleryVoterKey(), rating }),
                System.Text.Encoding.UTF8,
                "application/json");
            var response = await SharedHttpClient.PostAsync(
                $"{apiBaseUrl.TrimEnd('/')}/themes/{Uri.EscapeDataString(item.Id)}/vote",
                content);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            ApplyGalleryStatsResponse(item, json);
            SetStatus(FormatLanguageText("status.voteSaved", "Vote saved: {0}", item.Name));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                GetLanguageText("gallery.voteFailedTitle", "Vote could not be saved"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task NotifyGalleryDownloadAsync(GalleryThemeItem item)
    {
        var apiBaseUrl = GetConfiguredGalleryStatsApiBaseUrl();
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return;
        }

        try
        {
            using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            var response = await SharedHttpClient.PostAsync(
                $"{apiBaseUrl.TrimEnd('/')}/themes/{Uri.EscapeDataString(item.Id)}/download",
                content);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            ApplyGalleryStatsResponse(item, json);
        }
        catch
        {
            // Download tracking must never block a successful theme install.
        }
    }

    private static void ApplyGalleryStatsResponse(GalleryThemeItem item, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        item.DownloadCount = GetJsonInt(root, "downloads");
        item.VoteCount = GetJsonInt(root, "voteCount");
        item.AverageRating = GetJsonDouble(root, "averageRating");
        item.UserRating = GetJsonInt(root, "userRating");
    }

    private static int GetJsonInt(JsonElement element, string name, int fallback = 0)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => fallback
        };
    }

    private static double GetJsonDouble(JsonElement element, string name, double fallback = 0)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            _ => fallback
        };
    }

    private static string GetConfiguredGalleryStatsApiBaseUrl()
    {
        return GalleryStatsApiBaseUrl.TrimEnd('/');
    }

    private static async Task<IReadOnlyList<GalleryThemeItem>> LoadCommunityGalleryThemesAsync()
    {
        var apiBaseUrl = GetConfiguredGalleryStatsApiBaseUrl();
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return Array.Empty<GalleryThemeItem>();
        }

        try
        {
            var json = await SharedHttpClient.GetStringAsync($"{apiBaseUrl.TrimEnd('/')}/themes/community");
            return LoadGalleryThemesFromJson(json, apiBaseUrl.TrimEnd('/') + "/", isRemote: true);
        }
        catch
        {
            return Array.Empty<GalleryThemeItem>();
        }
    }

    private static string GetOrCreateGalleryVoterKey()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LianLiThemeEditor");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "gallery-voter-key.txt");
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }
        }

        var created = Guid.NewGuid().ToString("N");
        File.WriteAllText(path, created);
        return created;
    }

    private static string ResolveGalleryPath(string value, string basePathOrUrl, bool isRemote)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return value;
        }

        value = value.Replace('\\', '/').TrimStart('/');
        return isRemote
            ? new Uri(new Uri(basePathOrUrl), value).ToString()
            : Path.GetFullPath(Path.Combine(basePathOrUrl, value.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static bool IsGitHubGalleryAssetUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ImageSource?> LoadGalleryPreviewAsync(string previewUrl)
    {
        if (string.IsNullOrWhiteSpace(previewUrl))
        {
            return null;
        }

        try
        {
            if (!IsGitHubGalleryAssetUrl(previewUrl))
            {
                return null;
            }

            var uri = new Uri(previewUrl);
            var bytes = await SharedHttpClient.GetByteArrayAsync(uri);

            if (LooksLikeZipPackage(previewUrl, bytes))
            {
                _galleryPackageBytesCache[previewUrl] = bytes;
                return await LoadGalleryPackagePreviewAsync(bytes);
            }

            return LoadBitmapImage(bytes);
        }
        catch
        {
            return null;
        }
    }

    private async Task<ImageSource?> LoadGalleryPackagePreviewAsync(byte[] packageBytes)
    {
        try
        {
            using var stream = new MemoryStream(packageBytes);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var manifestBackground = GetGalleryPackageManifestBackground(archive);
            var entry = !string.IsNullOrWhiteSpace(manifestBackground)
                ? archive.GetEntry(manifestBackground)
                : null;
            entry ??= archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name) && IsGalleryPreviewCandidateEntry(entry.Name))
                .OrderByDescending(entry => entry.Name.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
                                            entry.FullName.Contains("preview", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(entry => entry.Name.Contains("background", StringComparison.OrdinalIgnoreCase) ||
                                           entry.FullName.Contains("background", StringComparison.OrdinalIgnoreCase))
                .ThenBy(entry => entry.FullName.Length)
                .FirstOrDefault();
            if (entry == null)
            {
                return null;
            }

            var extension = Path.GetExtension(entry.Name);
            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            await entryStream.CopyToAsync(buffer);
            var bytes = buffer.ToArray();

            if (IsGalleryImageExtension(extension))
            {
                return LoadBitmapImage(bytes);
            }

            var tempMedia = Path.Combine(Path.GetTempPath(), $"gallery_preview_{Guid.NewGuid():N}{extension}");
            try
            {
                await File.WriteAllBytesAsync(tempMedia, bytes);
                var frame = CreateBackgroundPreviewFrame(tempMedia);
                if (!string.IsNullOrWhiteSpace(frame) && File.Exists(frame))
                {
                    var image = LoadBitmapImage(await File.ReadAllBytesAsync(frame));
                    TryDeleteFile(frame);
                    return image;
                }
            }
            finally
            {
                TryDeleteFile(tempMedia);
            }
        }
        catch
        {
        }

        return null;
    }

    private static string GetGalleryPackageManifestBackground(ZipArchive archive)
    {
        try
        {
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry == null)
            {
                return "";
            }

            using var reader = new StreamReader(manifestEntry.Open(), System.Text.Encoding.UTF8);
            using var doc = JsonDocument.Parse(reader.ReadToEnd());
            return GetJsonString(doc.RootElement, "BackgroundFile");
        }
        catch
        {
            return "";
        }
    }

    private static bool LooksLikeZipPackage(string source, byte[] bytes)
    {
        var extension = Path.GetExtension(Uri.TryCreate(source, UriKind.Absolute, out var uri)
            ? uri.LocalPath
            : source);
        return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".lltheme", StringComparison.OrdinalIgnoreCase) ||
               (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B);
    }

    private static bool IsGalleryPreviewCandidateEntry(string name)
    {
        var extension = Path.GetExtension(name);
        return IsGalleryImageExtension(extension) ||
               extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".h264", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGalleryImageExtension(string extension)
    {
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
    }

    private static ImageSource LoadBitmapImage(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private async Task<bool> DownloadAndInstallGalleryThemeAsync(GalleryThemeItem item, bool activateAfterInstall)
    {
        UpdateGalleryInstalledState(item);
        var targetTemplateId = SanitizeFileName(item.Id);
        if (string.IsNullOrWhiteSpace(targetTemplateId))
        {
            targetTemplateId = SanitizeFileName(GetGalleryPackageFileBaseName(item.PackageUrl));
        }

        item.IsBusy = true;
        item.Progress = 0;
        item.Status = "Downloading";
        SetStatus($"Downloading theme: {item.Name}");

        var tempPackage = Path.Combine(Path.GetTempPath(), $"gallery_theme_{Guid.NewGuid():N}.lltheme");
        try
        {
            await DownloadGalleryPackageForInstallAsync(item.PackageUrl, tempPackage, progress =>
            {
                Dispatcher.Invoke(() =>
                {
                    item.Progress = progress;
                    item.Status = progress > 0 ? $"{progress:0}%" : "Downloading";
                });
            });

            var installPackage = await EnsureThemeEditorPackageAsync(tempPackage, item, targetTemplateId);
            var validation = _themeValidator.Validate(installPackage, TemplateOptions.Select(option => option.Id));
            if (!validation.IsValid)
            {
                throw new InvalidDataException(string.Join(Environment.NewLine, validation.Issues.Select(issue => issue.Message)));
            }

            item.Status = "Installing";
            item.Progress = 92;
            SetBusy(true, $"Installing theme: {item.Name}");

            var imported = await ImportThemePackageAsync(
                installPackage,
                targetTemplateId,
                overwriteExisting: true,
                installThroughLConnect: true);
            RefreshTemplateList();
            SelectTemplateCombo(imported.Path);
            UseActiveCheck.IsChecked = false;
            TemplateIdBox.Text = imported.Id;
            _currentTemplatePath = imported.Path;
            _currentTemplateId = imported.Id;
            var selectedDeviceModel = GetSelectedDeviceModel();
            var importedTemplate = await _supporter.LoadTemplatePathAsync(selectedDeviceModel, imported.Path);
            ApplyTemplateResult(importedTemplate);
            if (!string.IsNullOrWhiteSpace(imported.BackgroundPath) && File.Exists(imported.BackgroundPath))
            {
                _currentBackgroundPath = imported.BackgroundPath;
                _selectedBackgroundSourcePath = imported.BackgroundPath;
                LoadBackgroundPreview(imported.BackgroundPath, Path.GetFileName(imported.BackgroundPath));
            }

            if (activateAfterInstall)
            {
                item.Status = "Activating";
                item.Progress = 98;
                SetStatus($"Activating theme: {item.Name}");
                var lConnectTemplateId = string.IsNullOrWhiteSpace(imported.LConnectId)
                    ? imported.Id
                    : imported.LConnectId;
                var accepted = await ActivateInstalledThemeAsync(
                    lConnectTemplateId,
                    GetSelectedDeviceModel(),
                    imported.Path,
                    imported.BackgroundPath);
                item.Status = accepted ? "Installed and active" : "Installed";
            }
            else
            {
                await ReloadInstalledTemplatesInLConnectAsync();
                item.Status = "Installed";
            }

            item.Progress = 100;
            item.InstalledTemplatePath = imported.Path;
            item.InstalledTemplateId = string.IsNullOrWhiteSpace(imported.LConnectId)
                ? imported.Id
                : imported.LConnectId;
            item.IsInstalled = true;
            await NotifyGalleryDownloadAsync(item);
            SetBusy(false, $"Theme installed: {item.Name}");
            return true;
        }
        catch (Exception ex)
        {
            item.Status = "Error";
            SetBusy(false, "Gallery theme install failed.");
            MessageBox.Show(
                this,
                ex.Message,
                "Theme could not be installed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
        finally
        {
            TryDeleteFile(tempPackage);
            item.IsBusy = false;
        }
    }

    private async Task DownloadGalleryPackageForInstallAsync(
        string source,
        string destinationPath,
        Action<double> reportProgress)
    {
        if (_galleryPackageBytesCache.TryGetValue(source, out var cachedBytes) && cachedBytes.Length > 0)
        {
            await File.WriteAllBytesAsync(destinationPath, cachedBytes);
            reportProgress(100);
            return;
        }

        await DownloadGalleryPackageAsync(source, destinationPath, reportProgress);
    }

    private static async Task<string> EnsureThemeEditorPackageAsync(
        string packagePath,
        GalleryThemeItem item,
        string targetTemplateId)
    {
        ThemePackageManifest? existingManifest = null;
        string repairedBackgroundEntryName = "";
        var existingTemplateRequiresBackground = false;
        using (var archive = ZipFile.OpenRead(packagePath))
        {
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry != null)
            {
                using var reader = new StreamReader(manifestEntry.Open(), System.Text.Encoding.UTF8);
                existingManifest = JsonSerializer.Deserialize<ThemePackageManifest>(await reader.ReadToEndAsync());
                if (existingManifest == null)
                {
                    throw new InvalidDataException("The gallery package manifest is invalid.");
                }

                var declaredBackground = (existingManifest.BackgroundFile ?? "").Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(declaredBackground) && archive.GetEntry(declaredBackground) != null)
                {
                    return packagePath;
                }

                var templateEntry = archive.GetEntry((existingManifest.TemplateFile ?? "").Replace('\\', '/'))
                                    ?? archive.Entries.FirstOrDefault(entry => entry.Name.EndsWith(".template", StringComparison.OrdinalIgnoreCase));
                existingTemplateRequiresBackground = TemplateRequiresExternalBackground(templateEntry);
                repairedBackgroundEntryName = SelectGalleryBackgroundEntry(archive.Entries, templateEntry)?.FullName ?? "";
            }
        }

        if (existingManifest != null)
        {
            if (string.IsNullOrWhiteSpace(repairedBackgroundEntryName))
            {
                if (existingTemplateRequiresBackground)
                {
                    throw new InvalidDataException("The gallery package references background media, but the media file is missing.");
                }

                AppLogger.Info($"Gallery package uses embedded template visuals without external background media: {targetTemplateId}");
                return packagePath;
            }

            existingManifest.BackgroundFile = repairedBackgroundEntryName.Replace('\\', '/');
            using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update);
            archive.GetEntry("manifest.json")?.Delete();
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using var writer = new StreamWriter(manifestEntry.Open(), System.Text.Encoding.UTF8);
            await writer.WriteAsync(JsonSerializer.Serialize(existingManifest, new JsonSerializerOptions { WriteIndented = true }));
            AppLogger.Info($"Gallery package background repaired: {existingManifest.BackgroundFile}");
            return packagePath;
        }

        var convertedPath = Path.Combine(Path.GetTempPath(), $"gallery_theme_converted_{Guid.NewGuid():N}.lltheme");
        await Task.Run(() => ConvertLConnectZipToThemeEditorPackage(packagePath, convertedPath, item, targetTemplateId));
        TryDeleteFile(packagePath);
        File.Move(convertedPath, packagePath);
        return packagePath;
    }

    private static void ConvertLConnectZipToThemeEditorPackage(
        string sourcePackagePath,
        string destinationPackagePath,
        GalleryThemeItem item,
        string targetTemplateId)
    {
        using var sourceArchive = ZipFile.OpenRead(sourcePackagePath);
        var entries = sourceArchive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .ToList();
        var templateEntry = entries
            .FirstOrDefault(entry => entry.Name.EndsWith(".template", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("No .template file was found in the selected ZIP.");

        var backgroundEntry = SelectGalleryBackgroundEntry(entries, templateEntry);
        if (backgroundEntry == null && TemplateRequiresExternalBackground(templateEntry))
        {
            throw new InvalidDataException("The theme references external background media, but that media was not found in the ZIP package.");
        }

        var deviceModel = string.IsNullOrWhiteSpace(item.DeviceModel) ? "hydroshift-ii-lcd-s" : item.DeviceModel;
        var templateId = SanitizeFileName(string.IsNullOrWhiteSpace(targetTemplateId)
            ? Path.GetFileNameWithoutExtension(templateEntry.Name)
            : targetTemplateId);
        if (string.IsNullOrWhiteSpace(templateId))
        {
            templateId = SanitizeFileName(item.Id);
        }

        using var destinationArchive = ZipFile.Open(destinationPackagePath, ZipArchiveMode.Create);
        var templateName = $"{templateId}.template";
        CopyZipEntry(templateEntry, destinationArchive, templateName, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var backgroundName = "";
        if (backgroundEntry != null)
        {
            backgroundName = Path.GetFileName(backgroundEntry.Name);
            CopyZipEntry(backgroundEntry, destinationArchive, backgroundName, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
        var usedFontNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fontEntry in entries.Where(entry => Path.GetExtension(entry.Name).Equals(".ttf", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(entry.Name).Equals(".otf", StringComparison.OrdinalIgnoreCase)))
        {
            CopyZipEntry(fontEntry, destinationArchive, $"fonts/{GetSafeFileName(fontEntry.Name)}", usedFontNames);
        }

        var manifest = new ThemePackageManifest
        {
            DeviceModel = deviceModel,
            TemplateId = templateId,
            TemplateFile = templateName,
            BackgroundFile = backgroundName
        };
        var manifestEntry = destinationArchive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using var writer = new StreamWriter(manifestEntry.Open(), System.Text.Encoding.UTF8);
        writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task ExportThemePackageAsync(string packagePath, ThemeExportSnapshot snapshot)
    {
        var exportTemplateId = string.IsNullOrWhiteSpace(snapshot.ExportTemplateId)
            ? snapshot.TemplateId
            : snapshot.ExportTemplateId;
        if (string.IsNullOrWhiteSpace(exportTemplateId))
        {
            exportTemplateId = Path.GetFileNameWithoutExtension(snapshot.TemplatePath);
        }

        var tempTemplate = Path.Combine(
            Path.GetTempPath(),
            $"theme_export_{SanitizeFileName(exportTemplateId)}_{Guid.NewGuid():N}.template");
        File.Copy(snapshot.TemplatePath, tempTemplate, true);

        try
        {
            await _supporter.NormalizeTemplateIdentityAsync(snapshot.DeviceModel, tempTemplate, exportTemplateId);
            var normalized = new ThemeExportSnapshot
            {
                DeviceModel = snapshot.DeviceModel,
                TemplateId = exportTemplateId,
                ExportTemplateId = exportTemplateId,
                TemplatePath = tempTemplate,
                BackgroundPath = snapshot.BackgroundPath,
                BackgroundEntryName = snapshot.BackgroundEntryName,
                ImagePaths = snapshot.ImagePaths
            };
            await Task.Run(() => ExportThemePackage(packagePath, normalized));
        }
        finally
        {
            TryDeleteFile(tempTemplate);
        }
    }

    private static bool IsGalleryBackgroundEntry(string name)
    {
        var extension = Path.GetExtension(name);
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".h264", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static ZipArchiveEntry? SelectGalleryBackgroundEntry(
        IEnumerable<ZipArchiveEntry> sourceEntries,
        ZipArchiveEntry? templateEntry)
    {
        var entries = sourceEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name) && IsGalleryBackgroundEntry(entry.Name))
            .ToList();
        if (entries.Count == 0) return null;

        var templateText = "";
        if (templateEntry != null)
        {
            using var stream = templateEntry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            templateText = System.Text.Encoding.UTF8.GetString(memory.ToArray());
        }

        static bool IsVideo(string extension) =>
            extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".h264", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".gif", StringComparison.OrdinalIgnoreCase);

        return entries
            .OrderByDescending(entry =>
            {
                var score = 0;
                var extension = Path.GetExtension(entry.Name);
                if (!string.IsNullOrWhiteSpace(templateText) && templateText.Contains(entry.Name, StringComparison.OrdinalIgnoreCase)) score += 100;
                if (entry.FullName.Contains("background", StringComparison.OrdinalIgnoreCase)) score += 50;
                if (IsVideo(extension)) score += 30;
                if (entry.FullName.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
                    entry.FullName.Contains("thumbnail", StringComparison.OrdinalIgnoreCase)) score -= 60;
                return score;
            })
            .ThenByDescending(entry => entry.Length)
            .First();
    }

    private static bool TemplateRequiresExternalBackground(ZipArchiveEntry? templateEntry)
    {
        if (templateEntry == null) return false;
        using var stream = templateEntry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var templateText = System.Text.Encoding.UTF8.GetString(memory.ToArray());
        return templateText.Contains("GraphAnimation", StringComparison.OrdinalIgnoreCase) &&
               Regex.IsMatch(templateText, @"\.(mp4|h264|gif|png|jpe?g|webp)", RegexOptions.IgnoreCase);
    }

    private static async Task DownloadGalleryPackageAsync(string source, string destinationPath, Action<double> reportProgress)
    {
        if (!IsGitHubGalleryAssetUrl(source))
        {
            throw new InvalidDataException("Gallery packages must be downloaded from GitHub over HTTPS.");
        }

        var uri = new Uri(source);
        using var request = new HttpRequestMessage(HttpMethod.Get, AddCacheBuster(uri));
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true
        };
        using var response = await SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read));
            readTotal += read;
            if (total is > 0)
            {
                reportProgress(Math.Clamp(readTotal * 90.0 / total.Value, 0, 90));
            }
        }

        reportProgress(90);
    }

    private static Uri AddCacheBuster(Uri uri)
    {
        var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
        return new Uri(uri + separator + "v=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
    }

    private string PromptExportTemplateId(string suggestedName, string fallbackTemplateId)
    {
        var initialName = CreateExportTemplateName(suggestedName, fallbackTemplateId);
        var dialogBackground = TryFindResource("WindowBackgroundBrush") as Brush
                               ?? new SolidColorBrush(Color.FromRgb(10, 26, 54));
        var dialogText = TryFindResource("TextBrush") as Brush
                         ?? Brushes.White;
        var dialog = new Window
        {
            Title = GetLanguageText("dialogs.exportThemeName", "Export theme name"),
            Owner = this,
            Width = 420,
            Height = 190,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = dialogBackground
        };

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = GetLanguageText("dialogs.exportThemeNamePrompt", "Theme name to use in L-Connect:"),
            Foreground = dialogText,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(label, 0);
        root.Children.Add(label);

        var box = new TextBox
        {
            Text = initialName,
            MinHeight = 34,
            Padding = new Thickness(10, 6, 10, 6),
            Style = TryFindResource("ModernTextBox") as Style
        };
        Grid.SetRow(box, 1);
        root.Children.Add(box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var ok = new Button
        {
            Content = GetLanguageText("common.ok", "OK"),
            MinWidth = 92,
            Height = 34,
            IsDefault = true,
            Style = TryFindResource("PrimaryButtonStyle") as Style
        };
        var cancel = new Button
        {
            Content = GetLanguageText("common.cancel", "Cancel"),
            MinWidth = 92,
            Height = 34,
            Margin = new Thickness(10, 0, 0, 0),
            IsCancel = true,
            Style = TryFindResource("SecondaryButtonStyle") as Style
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        ok.Click += (_, _) =>
        {
            var name = CreateExportTemplateName(box.Text, fallbackTemplateId);
            if (string.IsNullOrWhiteSpace(name))
            {
                box.Focus();
                box.SelectAll();
                return;
            }

            box.Text = name;
            dialog.DialogResult = true;
        };

        dialog.Content = root;
        box.SelectAll();
        box.Focus();
        return dialog.ShowDialog() == true
            ? CreateExportTemplateName(box.Text, fallbackTemplateId)
            : "";
    }

    private static string CreateExportTemplateName(string value, string fallbackTemplateId)
    {
        var name = SanitizeFileName(value);
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileNameWithoutExtension(name);
        }

        name = Regex.Replace(name, @"(?i)[-_ ]?LConnect$", "");
        name = Regex.Replace(name, @"\s+", "_").Trim(' ', '.', '_', '-');
        if (string.IsNullOrWhiteSpace(name))
        {
            name = CreateExportPackageBaseName(fallbackTemplateId);
        }

        return name;
    }

    private async Task<string> PrepareLConnectExportTemplateAsync(
        string deviceModel,
        string sourceTemplatePath,
        string exportTemplateId,
        string backgroundPath)
    {
        var templateRoot = GetTemplateRoot(deviceModel);
        Directory.CreateDirectory(templateRoot);
        var exportTemplatePath = Path.Combine(templateRoot, $"{exportTemplateId}.template");
        File.Copy(sourceTemplatePath, exportTemplatePath, true);
        await _supporter.NormalizeTemplateIdentityAsync(deviceModel, exportTemplatePath, exportTemplateId);

        if (!string.IsNullOrWhiteSpace(backgroundPath) && File.Exists(backgroundPath))
        {
            // Only update the relative package reference. Passing an existing file path
            // makes the supporter transcode it before L-Connect sees it.
            await _supporter.SetBackgroundMediaAsync(
                deviceModel,
                exportTemplatePath,
                $"{exportTemplateId}{GetExportBackgroundExtension(deviceModel)}",
                GetTemplateCanvasPixels(deviceModel).Width,
                GetTemplateCanvasPixels(deviceModel).Height);
        }

        return exportTemplatePath;
    }

    private async Task<PreparedExportBackground> PrepareExportBackgroundAsync(
        string deviceModel,
        string sourcePath,
        string exportTemplateId)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return new PreparedExportBackground { Path = sourcePath };
        }

        if (IsWideScreenDeviceModel(deviceModel))
        {
            var safeId = Regex.Replace(exportTemplateId, @"[^A-Za-z0-9_.-]", "_");
            var outputBase = Path.Combine(Path.GetTempPath(), $"{safeId}-hq-{Guid.NewGuid():N}");
            var convertedMp4 = await ConvertExportBackgroundAsync(
                deviceModel,
                sourcePath,
                exportTemplateId,
                ".mp4",
                outputBase + ".mp4");
            var convertedH264 = await ConvertExportBackgroundAsync(
                deviceModel,
                convertedMp4,
                exportTemplateId,
                ".h264",
                outputBase + ".h264");

            return new PreparedExportBackground
            {
                Path = convertedMp4,
                TemporaryPaths = new List<string> { convertedMp4, convertedH264 }
            };
        }

        // HydroShift import is picky about the MP4 stream. Normalize even existing
        // MP4 files to the device canvas/profile used by official L-Connect packs.
        var converted = await ConvertExportBackgroundAsync(deviceModel, sourcePath, exportTemplateId, ".mp4");
        return new PreparedExportBackground
        {
            Path = converted,
            TemporaryPaths = new List<string> { converted }
        };
    }

    private async Task<string> ConvertExportBackgroundAsync(
        string deviceModel,
        string sourcePath,
        string exportTemplateId,
        string outputExtension,
        string? outputPathOverride = null)
    {
        var ffmpegPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Lian-Li",
            "L-Connect 3",
            "x64",
            "ffmpeg.exe");
        if (!File.Exists(ffmpegPath))
        {
            throw new FileNotFoundException(
                "L-Connect FFmpeg was not found. The selected background could not be converted for export.",
                ffmpegPath);
        }

        var safeId = Regex.Replace(exportTemplateId, @"[^A-Za-z0-9_.-]", "_");
        var outputPath = string.IsNullOrWhiteSpace(outputPathOverride)
            ? Path.Combine(
                Path.GetTempPath(),
                $"{safeId}-hq-{Guid.NewGuid():N}{outputExtension}")
            : outputPathOverride;
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("-y");
        if (extension is ".png" or ".jpg" or ".jpeg")
        {
            startInfo.ArgumentList.Add("-loop");
            startInfo.ArgumentList.Add("1");
        }
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);
        if (extension is ".png" or ".jpg" or ".jpeg")
        {
            startInfo.ArgumentList.Add("-t");
            startInfo.ArgumentList.Add("10");
        }
        startInfo.ArgumentList.Add("-an");
        startInfo.ArgumentList.Add("-vf");
        var canvas = GetTemplateCanvasPixels(deviceModel);
        var isWideScreen = IsWideScreenDeviceModel(deviceModel);
        if (isWideScreen)
        {
            var filter = outputExtension.Equals(".h264", StringComparison.OrdinalIgnoreCase)
                ? "transpose=clock,scale=480:1920,setsar=1,fps=24,format=yuv420p"
                : "scale=1920:480,setsar=1,fps=24,format=yuv420p";
            startInfo.ArgumentList.Add(filter);
        }
        else
        {
            startInfo.ArgumentList.Add(
                $"scale={canvas.Width}:{canvas.Height}:force_original_aspect_ratio=increase:flags=lanczos," +
                $"crop={canvas.Width}:{canvas.Height},setsar=1,fps=24,format=yuv420p");
        }
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add("libx264");
        if (isWideScreen)
        {
            startInfo.ArgumentList.Add("-preset");
            startInfo.ArgumentList.Add("ultrafast");
            startInfo.ArgumentList.Add("-x264opts");
            startInfo.ArgumentList.Add("bframes=0");
            startInfo.ArgumentList.Add("-profile:v");
            startInfo.ArgumentList.Add("baseline");
            startInfo.ArgumentList.Add("-level");
            startInfo.ArgumentList.Add("3.1");
            startInfo.ArgumentList.Add("-refs");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-b:v");
            startInfo.ArgumentList.Add("2400k");
            startInfo.ArgumentList.Add("-tune");
            startInfo.ArgumentList.Add("zerolatency");
            startInfo.ArgumentList.Add("-pix_fmt");
            startInfo.ArgumentList.Add("yuv420p");
        }
        else
        {
            startInfo.ArgumentList.Add("-preset");
            startInfo.ArgumentList.Add("veryfast");
            startInfo.ArgumentList.Add("-crf");
            startInfo.ArgumentList.Add("14");
            startInfo.ArgumentList.Add("-profile:v");
            startInfo.ArgumentList.Add("main");
            startInfo.ArgumentList.Add("-level");
            startInfo.ArgumentList.Add("3.1");
            startInfo.ArgumentList.Add("-refs");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-bf");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-g");
            startInfo.ArgumentList.Add("24");
            startInfo.ArgumentList.Add("-keyint_min");
            startInfo.ArgumentList.Add("24");
            startInfo.ArgumentList.Add("-sc_threshold");
            startInfo.ArgumentList.Add("0");
            startInfo.ArgumentList.Add("-fps_mode");
            startInfo.ArgumentList.Add("cfr");
        }
        if (outputExtension.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add("-movflags");
            startInfo.ArgumentList.Add("+faststart");
        }
        else
        {
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("h264");
        }
        startInfo.ArgumentList.Add(outputPath);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var errorTask = process.StandardError.ReadToEndAsync();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        var error = await errorTask;
        _ = await outputTask;

        if (process.ExitCode != 0 || !File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            TryDeleteFile(outputPath);
            throw new InvalidOperationException(
                $"High-quality background conversion failed.{Environment.NewLine}{error}");
        }

        return outputPath;
    }

    private static string GetExportBackgroundExtension(string deviceModel) =>
        IsWideScreenDeviceModel(deviceModel)
            ? ".h264"
            : ".mp4";

    private async Task<string> CreateDeterministicBackgroundPreviewAsync(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return "";
        }

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg" or ".bmp")
        {
            return "";
        }

        var ffmpegPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Lian-Li",
            "L-Connect 3",
            "x64",
            "ffmpeg.exe");
        if (!File.Exists(ffmpegPath))
        {
            return "";
        }

        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"theme_background_preview_{Guid.NewGuid():N}.png");
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("-y");
        if (extension == ".h264")
        {
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("h264");
        }
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add("0.12");
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-vf");
        var canvas = GetTemplateCanvasPixels();
        startInfo.ArgumentList.Add(
            $"scale={canvas.Width}:{canvas.Height}:force_original_aspect_ratio=increase:flags=lanczos," +
            $"crop={canvas.Width}:{canvas.Height}");
        startInfo.ArgumentList.Add(outputPath);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var errorTask = process.StandardError.ReadToEndAsync();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        _ = await errorTask;
        _ = await outputTask;

        if (process.ExitCode != 0 ||
            !File.Exists(outputPath) ||
            new FileInfo(outputPath).Length == 0)
        {
            TryDeleteFile(outputPath);
            return "";
        }

        return outputPath;
    }

    private static string CreateShortBackgroundStagingPath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The selected background media was not found.", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var stagedPath = Path.Combine(
            Path.GetTempPath(),
            $"lian-bg-{Guid.NewGuid():N}{extension}");
        File.Copy(sourcePath, stagedPath, true);
        return stagedPath;
    }

    private static void TryDeleteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
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

    private static string CreateUniqueExportTemplateId(string templateId, string deviceModel)
    {
        var candidateBase = CreateExportPackageBaseName(templateId);
        var templateRoot = Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", deviceModel, "template");
        if (!Directory.Exists(templateRoot))
        {
            return candidateBase;
        }

        return GetUniqueTemplateId(templateRoot, candidateBase);
    }

    private static string CreateExportPackageBaseName(string templateId)
    {
        var baseId = SanitizeFileName(templateId);
        if (string.IsNullOrWhiteSpace(baseId))
        {
            baseId = "LianLiTheme";
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return $"{baseId}_{timestamp}";
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
        TryDeleteFile(_generatedBackgroundPreviewFramePath);
        _generatedBackgroundPreviewFramePath = "";

        var resolved = ResolveBackgroundPath(backgroundPath, backgroundName);
        if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
        {
            return;
        }

        if (Path.GetExtension(resolved).Equals(".h264", StringComparison.OrdinalIgnoreCase))
        {
            var mp4Variant = ResolveBackgroundVariant(resolved, ".mp4");
            if (!mp4Variant.Equals(resolved, StringComparison.OrdinalIgnoreCase))
            {
                resolved = mp4Variant;
            }
            else
            {
                var previewFrame = CreateBackgroundPreviewFrame(resolved);
                if (!string.IsNullOrWhiteSpace(previewFrame))
                {
                    _generatedBackgroundPreviewFramePath = previewFrame;
                    resolved = previewFrame;
                }
            }
        }

        var ext = Path.GetExtension(resolved).ToLowerInvariant();
        try
        {
            if (!_animateVideoPreviews && ext is (".mp4" or ".avi" or ".mov" or ".wmv" or ".h264"))
            {
                var previewFrame = CreateBackgroundPreviewFrame(resolved);
                if (!string.IsNullOrWhiteSpace(previewFrame) && File.Exists(previewFrame))
                {
                    _generatedBackgroundPreviewFramePath = previewFrame;
                    resolved = previewFrame;
                    ext = Path.GetExtension(resolved).ToLowerInvariant();
                }
            }

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
            var assetRoot = @"C:\Program Files\Lian-Li\L-Connect 3\Assets";
            var searchModels = new List<string> { model };
            if (string.Equals(model, Vm92DeviceModel, StringComparison.OrdinalIgnoreCase))
            {
                searchModels.Add(UniversalScreenDeviceModel);
            }
            
            var baseName = Path.GetFileNameWithoutExtension(backgroundName);
            var templateDir = string.IsNullOrWhiteSpace(_currentTemplatePath)
                ? ""
                : Path.GetDirectoryName(_currentTemplatePath) ?? "";
            var searchPaths = new List<string> { templateDir };
            foreach (var searchModel in searchModels.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                searchPaths.Add(Path.Combine(lconnect, searchModel, "video"));
                searchPaths.Add(Path.Combine(lconnect, searchModel, "theme"));
                searchPaths.Add(Path.Combine(lconnect, searchModel, "template"));
                searchPaths.Add(Path.Combine(lconnect, searchModel, "temp"));
                searchPaths.Add(Path.Combine(lconnect, "uploaded", searchModel, "template-background"));
                searchPaths.Add(Path.Combine(assetRoot, searchModel, "video"));
                searchPaths.Add(Path.Combine(assetRoot, searchModel, "theme"));
            }

            var distinctSearchPaths = searchPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var dir in distinctSearchPaths)
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
            if (string.Equals(model, Vm92DeviceModel, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(Path.Combine(lconnect, UniversalScreenDeviceModel, "video", backgroundName));
                candidates.Add(Path.Combine(lconnect, UniversalScreenDeviceModel, "theme", backgroundName));
                candidates.Add(Path.Combine(lconnect, UniversalScreenDeviceModel, "temp", backgroundName));
                candidates.Add(Path.Combine(lconnect, UniversalScreenDeviceModel, "template", backgroundName));
                candidates.Add(Path.Combine(lconnect, "uploaded", UniversalScreenDeviceModel, "template-background", backgroundName));
                candidates.Add(Path.Combine(assetRoot, UniversalScreenDeviceModel, "video", backgroundName));
                candidates.Add(Path.Combine(assetRoot, UniversalScreenDeviceModel, "theme", backgroundName));
            }
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

    private string CreateBackgroundPreviewFrame(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return "";
        }

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var ffmpegPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Lian-Li",
            "L-Connect 3",
            "x64",
            "ffmpeg.exe");
        if (!File.Exists(ffmpegPath))
        {
            return "";
        }

        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"theme_background_preview_{Guid.NewGuid():N}.png");
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("-y");
        if (extension == ".h264")
        {
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("h264");
        }
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);
        if (extension != ".h264")
        {
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add("0.12");
        }
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-vf");
        var canvas = GetTemplateCanvasPixels();
        startInfo.ArgumentList.Add(
            $"scale={canvas.Width}:{canvas.Height}:force_original_aspect_ratio=increase:flags=lanczos," +
            $"crop={canvas.Width}:{canvas.Height}");
        startInfo.ArgumentList.Add(outputPath);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            _ = process.StandardError.ReadToEnd();
            _ = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0 &&
                File.Exists(outputPath) &&
                new FileInfo(outputPath).Length > 0)
            {
                return outputPath;
            }
        }
        catch
        {
        }

        TryDeleteFile(outputPath);
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
        ColorPickerDialog.TextProvider = GetLanguageText;
        Title = GetText(text, "app.title", "Lian Li LCD Template Editor V 1.4");
        DeviceLabel.Text = GetText(text, "top.device", "DEVICE");
        TemplateLabel.Text = GetText(text, "top.templateId", "TEMPLATE");
        LanguageLabel.Text = GetText(text, "footer.language", "Language");
        ThemeLabel.Text = GetText(text, "footer.theme", "Theme");
        DarkThemeItem.Content = GetText(text, "footer.dark", "Dark");
        LightThemeItem.Content = GetText(text, "footer.light", "Light");
        UseActiveCheck.Content = GetText(text, "top.useActiveTemplate", "Use active template");
        OfflineModeCheck.Content = GetText(text, "top.offlineMode", "Offline");
        OfflineModeCheck.ToolTip = GetText(text, "tooltips.offlineMode", "Work on a local copy without sending changes to L-Connect");
        ActiveThemeButton.Content = GetText(text, "top.activeTheme", "Active Theme");
        LoadButton.Content = GetText(text, "top.load", "Load");
        SaveButton.Content = GetText(text, "top.save", "Save");
        BackupButton.Content = "";
        RestoreBackupButton.Content = "";
        UndoButton.Content = "";
        RedoButton.Content = "";
        UndoHistoryButton.Content = "";
        BackupButton.ToolTip = GetText(text, "tooltips.backup", "Create backup");
        RestoreBackupButton.ToolTip = GetText(text, "tooltips.restore", "Restore backup");
        UndoHistoryButton.ToolTip = GetText(text, "tooltips.history", "Edit history");
        UndoButton.ToolTip = GetText(text, "tooltips.undo", "Revert last template change (Ctrl+Z)");
        RedoButton.ToolTip = GetText(text, "tooltips.redo", "Restore template change (Ctrl+Y)");
        UndoHistoryButton.ToolTip = GetText(text, "tooltips.history", "Open edit history");
        BackupButton.ToolTip = GetText(text, "tooltips.backup", "Create a template backup");
        RestoreBackupButton.ToolTip = GetText(text, "tooltips.restore", "Restore the latest template backup");
        ExportLConnectButtonText.Text = GetText(text, "top.exportTheme", "Export Theme");
        LayersHeaderText.Text = GetText(text, "sections.layers", "Layers");
        EditLayerHeaderText.Text = GetText(text, "sections.editLayer", "Edit Layer");
        AddLayerHeaderText.Text = GetText(text, "sections.addNewLayer", "Add New Layer");
        ShadowHeaderText.Text = GetText(text, "sections.dropShadow", "Drop Shadow");
        AddTypeLabel.Content = GetText(text, "add.layerType", "LAYER TYPE");
        foreach (var comboItem in AddLayerTypeCombo.Items.OfType<ComboBoxItem>())
        {
            var type = comboItem.Tag?.ToString() ?? "";
            var fallback = type switch
            {
                "StatusBar" => "Status Bar",
                "DynamicStatus" => "Dynamic Status",
                "CurvedBar" => "Curved Bar",
                "RingGraph" => "Ring Graph",
                _ => type
            };
            comboItem.Content = GetText(text, $"add.type{type}", fallback);
        }
        AddWithShadowCheck.Content = GetText(text, "add.withShadow", "Add shadow");
        ShadowAutoAddHint.Text = GetText(text, "add.shadowAutoHint", "Shadow will be added with the layer.");
        GraphSettingsTitle.Text = GetText(text, "sections.graphSettings", "Graph Dimensions & Styling");
        ImageSettingsTitle.Text = GetText(text, "sections.imageSettings", "Image Settings");
        PositionSizeHeader.Text = GetText(text, "labels.positionSize", "Position & Size");
        DataPropertiesHeader.Text = GetText(text, "labels.data", "Data");
        TextAndFormatHeader.Text = GetText(text, "sections.textAndFormat", "Text and Format");
        MatchXToolbarText.Text = GetText(text, "align.matchHorizontal", "Horizontal=");
        MatchYToolbarText.Text = GetText(text, "align.matchVertical", "Vertical=");
        SettingsDevicesTitleText.Text = GetText(text, "settings.myDevices", "My devices");
        SettingsDevicesDescText.Text = GetText(text, "settings.myDevicesDesc", "Only selected devices appear across the editor and gallery.");
        SettingsLanguageThemeTitleText.Text = GetText(text, "settings.languageTheme", "Language & theme");
        SettingsLanguageThemeDescText.Text = GetText(text, "settings.languageThemeDesc", "These choices also update the footer controls.");
        SettingsGalleryTitleText.Text = GetText(text, "settings.themeGallery", "Theme Gallery");
        SettingsGalleryDescText.Text = GetText(text, "settings.themeGalleryDesc", "Automatically apply downloaded themes after installation.");
        AutoApplyGalleryThemesCheck.Content = GetText(text, "settings.applyAutomatically", "Apply automatically");
        AnimateVideoPreviewsCheck.Content = GetText(text, "settings.playAnimatedPreviews", "Play animated previews");
        SettingsUnusedSensorsTitleText.Text = GetText(text, "settings.unusedSensorsTitle", "Map unused L-Connect sensors");
        SettingsUnusedSensorsDescText.Text = GetText(text, "settings.unusedSensorsDesc", "Map unused L-Connect sensors to other values");
        UnusedSensorsComingSoonCheck.Content = GetText(text, "settings.comingSoon", "Coming soon");
        ApplyLanguageComboText(LanguageCombo, text);
        ApplyLanguageComboText(SettingsLanguageCombo, text);
        GallerySendThemesButton.Content = GetText(text, "gallery.sendThemes", "Send Themes");
        EditorSendThemeButton.Content = GetText(text, "gallery.sendTheme", "Send Theme");
        GalleryActivateAfterInstallCheck.Content = GetText(text, "gallery.activateAfterInstall", "Activate after install");
        EditorTab.Header = GetText(text, "tabs.editor", "Editor");
        GalleryTab.Header = GetText(text, "tabs.gallery", "Theme Gallery");
        SettingsTab.Header = GetText(text, "tabs.settings", "Settings");
        DiagnosticsTab.Header = GetText(text, "tabs.diagnostics", "Diagnostics");
        ThanksTab.Header = GetText(text, "tabs.thanks", "");
        ThanksTitleText.Text = GetText(text, "thanks.title", "");
        ThanksDescriptionText.Text = GetText(text, "thanks.description", "");
        ThanksGalrimNameText.Text = GetText(text, "thanks.galrimName", "");
        ThanksGalrimText.Text = GetText(text, "thanks.galrim", "");
        ThanksRBuschyXNameText.Text = GetText(text, "thanks.rbuschyxName", "");
        ThanksRBuschyXText.Text = GetText(text, "thanks.rbuschyx", "");
        ThanksSOncoreNameText.Text = GetText(text, "thanks.sOncoreName", "");
        ThanksSOncoreText.Text = GetText(text, "thanks.sOncore", "");
        Thanks88TestersNameText.Text = GetText(text, "thanks.testers88Name", "");
        Thanks88TestersText.Text = GetText(text, "thanks.testers88", "");
        ThanksMrDoNameText.Text = GetText(text, "thanks.mrDoName", "");
        ThanksMrDoText.Text = GetText(text, "thanks.mrDo", "");
        ThanksJiveturkeyNameText.Text = GetText(text, "thanks.jiveturkeyName", "");
        ThanksJiveturkeyText.Text = GetText(text, "thanks.jiveturkey", "");
        ThanksJimmyNameText.Text = GetText(text, "thanks.jimmyName", "");
        ThanksJimmyText.Text = GetText(text, "thanks.jimmy", "");
        ThanksHatikoNameText.Text = GetText(text, "thanks.hatikoName", "");
        ThanksHatikoText.Text = GetText(text, "thanks.hatiko", "");
        ThanksClosingText.Text = GetText(text, "thanks.closing", "");
        AboutTab.Header = GetText(text, "tabs.about", "About");
        OpenGitHubIssuesButton.Content = GetText(text, "diagnostics.openIssues", "Open GitHub Issues");
        CopyDiagnosticPackageInfoButton.Content = GetText(text, "diagnostics.copyInfo", "Copy Diagnostic Info");
        CreateDiagnosticPackageButton.Content = GetText(text, "diagnostics.createPackage", "Create Diagnostic Package");
        AboutCreateDiagnosticPackageButton.Content = GetText(text, "diagnostics.createPackage", "Create Diagnostic Package");
        EditorImportThemeButton.Content = GetText(text, "top.importTheme", "Import Theme");
        CheckUpdatesButton.Content = GetText(text, "about.checkUpdates", "Check for Updates");
        OpenGitHubButton.Content = GetText(text, "about.openGitHub", "Open GitHub");
        BugReportButton.Content = GetText(text, "about.bugReport", "Bug Report");
        FeatureRequestButton.Content = GetText(text, "about.featureRequest", "Feature Request");
        CopyDiagnosticInfoButton.Content = GetText(text, "about.copyDiagnosticInfo", "Copy Diagnostic Info");
        AboutIntroText.Text = GetText(text, "about.intro", "This editor is built for people who like to make tiny screens feel personal.");
        AboutDescText.Text = GetText(text, "about.desc", "It tries to keep the technical parts quiet: load a theme, see what is there, change it, and send it back to L-Connect without turning the whole thing into a puzzle.");
        AboutWarningText.Text = GetText(text, "about.warning", "It is an unofficial community tool, so please keep backups of themes you care about. Careful work still deserves a safety net.");
        RestoreRecoveryButton.Content = GetText(text, "recovery.restore", "Restore");
        DismissRecoveryButton.Content = GetText(text, "recovery.discard", "Discard");

        IndexLabel.Content = GetText(text, "labels.index", "INDEX");
        FontLabel.Content = GetText(text, "labels.font", "FONT");
        DataLabel.Content = GetText(text, "labels.data", "DATA");
        FontIntervalLabel.Content = GetText(text, "labels.charSpacing", "CHAR SPACING");
        SizeLabel.Content = "W";
        SizeHeightLabel.Content = "H";
        AlignmentLabel.Content = GetText(text, "labels.alignment", "ALIGNMENT");
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
        TextGradientColorLabel.Content = GetText(text, "labels.fontGradientColor", "FONT GRADIENT");
        TextGradientDirectionLabel.Content = GetText(text, "labels.fontGradientDirection", "GRADIENT DIRECTION");
        GraphGradientDirectionLabel.Content = GetText(text, "labels.fontGradientDirection", "GRADIENT DIRECTION");
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
        MaxValueLabel.Content = GetText(text, "labels.maxValue", "MAX VALUE");
        StartPercentageLabel.Content = GetText(text, "labels.startPercentage", "START %");
        TotalAngleLabel.Content = GetText(text, "labels.totalAngle", "TOTAL ANGLE");

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
        ApplyButton.ToolTip = GetText(text, "common.apply", "Apply");
        RemoveButton.ToolTip = GetText(text, "common.remove", "Remove");
        MoveUpButton.ToolTip = GetText(text, "common.moveUp", "Move Up");
        MoveDownButton.ToolTip = GetText(text, "common.moveDown", "Move Down");
        BackgroundButton.Content = GetText(text, "preview.uploadBackground", "Upload Background (GIF / JPG / MP4)");
        RestartButtonText.Text = GetText(text, "top.restartLConnect", "Restart L-Connect");
        ApplyAllButtonText.Text = GetText(text, "common.applyAll", "Apply All");
        ShadowTitleText.Text = GetText(text, "shadow.options", "Shadow options");
        PairCheck.Content = GetText(text, "shadow.pair", "Pair shadow");
        SyncShadowColorCheck.Content = GetText(text, "shadow.syncColor", "Sync color");
        ChangeImageButton.Content = GetText(text, "common.change", "Change...");
        DragHintText.Text = GetText(text, "preview.dragToReposition", "Drag to reposition");
        FitPreviewButton.Content = GetText(text, "preview.fit", "Fit");
        Vm92DirectApplyNoticeText.Text = GetText(
            text,
            "preview.vm92DirectApplyNotice",
            "Direct apply is not supported for this screen yet. Export it as a ZIP and import it from L-Connect.");
        Convert88To92Button.Content = GetText(
            text,
            "preview.convert88To92",
            "Convert 8.8 theme to 9.2 theme");
        FitPreviewButton.ToolTip = GetText(text, "tooltips.fitPreview", "Fit preview to the available area");
        OrientationLabel.Text = GetText(text, "preview.orientation", "ORIENTATION");
        if (UniversalOrientationCombo.Items.Count >= 2)
        {
            ((ComboBoxItem)UniversalOrientationCombo.Items[0]).Content =
                GetText(text, "preview.landscape", "Landscape");
            ((ComboBoxItem)UniversalOrientationCombo.Items[1]).Content =
                GetText(text, "preview.portrait", "Portrait");
        }
        foreach (var layer in Layers)
        {
            SetLayerActionTooltips(layer);
        }
        LayerGrid.Items.Refresh();

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
            Layers.Count(layer => !layer.IsEditorMetadata));

        // Translated main editor window UI controls
        PositionSizeHeader.Text = GetText(text, "labels.positionSize", "POSITION & SIZE");
        DataPropertiesHeader.Text = GetText(text, "labels.data", "DATA");
        LayerOptionsHeader.Text = GetText(text, "labels.layerOptions", "LAYER OPTIONS");
        FrontAlphaLabel.Content = GetText(text, "labels.frontAlpha", "FRONT ALPHA");
        BackAlphaLabel.Content = GetText(text, "labels.backAlpha", "BACK ALPHA");
        ChartFillColorLabel.Content = GetText(text, "labels.fillColor", "FILL COLOR");
        ChartTransparentLabel.Content = GetText(text, "labels.transparent", "TRANSPARENT");
        TransparentBackgroundCheck.Content = GetText(text, "labels.transparentBG", "TransparentBG");
        InvertDirectionCheck.Content = GetText(text, "labels.invertDirection", "Invert Direction");
        RingBorderCheck.Content = GetText(text, "labels.ringBorder", "Ring Border");
        RoundCheck.Content = GetText(text, "labels.round", "Round");
        UseBlockCheck.Content = GetText(text, "labels.subsection", "Subsection");
        GradientHeader.Text = GetText(text, "labels.gradient", "GRADIENT");
        DuplicateButton.ToolTip = GetText(text, "common.duplicate", "Duplicate");
        MinimizeWindowButton.ToolTip = GetText(text, "window.minimize", "Minimize");
        MaximizeWindowButton.ToolTip = GetText(text, "window.maximize", "Maximize");
        CloseWindowButton.ToolTip = GetText(text, "common.close", "Close");
        GraphSplitBlockWidthBox.ToolTip = GetText(text, "tooltips.graphSplitBlock", "Segment count or block width, depending on the graph style");
        GraphSplitBlankWidthBox.ToolTip = GetText(text, "tooltips.graphSplitGap", "Gap between blocks in template pixels");
        ImageRectBox.ToolTip = GetText(text, "tooltips.imageRect", "Source rectangle: x, y, width, height");
        ThemeToggleButton.ToolTip = ThemeToggleButton.IsChecked == true
            ? GetText(text, "tooltips.switchDark", "Switch to dark theme")
            : GetText(text, "tooltips.switchLight", "Switch to light theme");
        RefreshDataSourceItems();

        // Popup "Add Layer" translation
        AddLayerMenuButtonText.Text = GetText(text, "labels.addLayer", "Add Layer");
        AddLayerPopupTitleText.Text = GetText(text, "labels.addLayer", "Add Layer");
        AddLayerPopupDescText.Text = GetText(text, "labels.chooseContent", "Choose content for the canvas.");
        AddLayerAnimationTitle.Text = GetText(text, "add.typeAnimation", "Animation");
        AddLayerAnimationDesc.Text = GetText(text, "add.descAnimation", "Background media");
        AddLayerTextTitle.Text = GetText(text, "add.typeText", "Text");
        AddLayerTextDesc.Text = GetText(text, "add.descText", "Static content");
        AddLayerDataTitle.Text = GetText(text, "add.typeData", "Data");
        AddLayerDataDesc.Text = GetText(text, "add.descData", "Live sensor value");
        AddLayerImageTitle.Text = GetText(text, "add.typeImage", "Image");
        AddLayerImageDesc.Text = GetText(text, "add.descImage", "PNG or JPG");
        AddLayerStatusBarTitle.Text = GetText(text, "add.typeStatusBar", "Status Bar");
        AddLayerStatusBarDesc.Text = GetText(text, "add.descStatusBar", "Segmented bar");
        AddLayerDynamicStatusTitle.Text = GetText(text, "add.typeDynamicStatus", "Dynamic Status");
        AddLayerDynamicStatusDesc.Text = GetText(text, "add.descDynamicStatus", "Slider style bar");
        AddLayerCurvedBarTitle.Text = GetText(text, "add.typeCurvedBar", "Curved Bar");
        AddLayerCurvedBarDesc.Text = GetText(text, "add.descCurvedBar", "Donut or arc");
        AddLayerRingGraphTitle.Text = GetText(text, "add.typeRingGraph", "Ring Graph");
        AddLayerRingGraphDesc.Text = GetText(text, "add.descRingGraph", "Circular sensor gauge");
        AddLayerChartTitle.Text = GetText(text, "add.typeChart", "Chart");
        AddLayerChartDesc.Text = GetText(text, "add.descChart", "Stream chart");

        // Labels not previously set
        GraphTypeNameLabel.Content = GetText(text, "labels.type", "TYPE");
        GraphSubTypeNameLabel.Content = GetText(text, "labels.subtype", "SUBTYPE");

        // ComboBoxItems translations
        if (AlignmentCombo.Items.Count >= 3)
        {
            ((ComboBoxItem)AlignmentCombo.Items[0]).Content = GetText(text, "common.left", "Left");
            ((ComboBoxItem)AlignmentCombo.Items[1]).Content = GetText(text, "common.center", "Center");
            ((ComboBoxItem)AlignmentCombo.Items[2]).Content = GetText(text, "common.right", "Right");
        }
        if (TextGradientDirectionCombo.Items.Count >= 5)
        {
            ((ComboBoxItem)TextGradientDirectionCombo.Items[0]).Content = GetText(text, "gradient.none", "No Gradient");
            ((ComboBoxItem)TextGradientDirectionCombo.Items[1]).Content = GetText(text, "gradient.leftToRight", "Left to Right");
            ((ComboBoxItem)TextGradientDirectionCombo.Items[2]).Content = GetText(text, "gradient.topToBottom", "Top to Bottom");
            ((ComboBoxItem)TextGradientDirectionCombo.Items[3]).Content = GetText(text, "gradient.topLeftToBottomRight", "Top Right to Bottom Left");
            ((ComboBoxItem)TextGradientDirectionCombo.Items[4]).Content = GetText(text, "gradient.topRightToBottomLeft", "Top Left to Bottom Right");
        }
        if (GraphGradientDirectionCombo.Items.Count >= 4)
        {
            ((ComboBoxItem)GraphGradientDirectionCombo.Items[0]).Content = GetText(text, "direction.right", "0 - Right");
            ((ComboBoxItem)GraphGradientDirectionCombo.Items[1]).Content = GetText(text, "direction.left", "1 - Left");
            ((ComboBoxItem)GraphGradientDirectionCombo.Items[2]).Content = GetText(text, "direction.down", "2 - Down");
            ((ComboBoxItem)GraphGradientDirectionCombo.Items[3]).Content = GetText(text, "direction.up", "3 - Up");
        }
        if (GraphDirectionCombo.Items.Count >= 4)
        {
            ((ComboBoxItem)GraphDirectionCombo.Items[0]).Content = GetText(text, "direction.right", "0 - Right");
            ((ComboBoxItem)GraphDirectionCombo.Items[1]).Content = GetText(text, "direction.left", "1 - Left");
            ((ComboBoxItem)GraphDirectionCombo.Items[2]).Content = GetText(text, "direction.down", "2 - Down");
            ((ComboBoxItem)GraphDirectionCombo.Items[3]).Content = GetText(text, "direction.up", "3 - Up");
        }

        if (!_isLoading && LayerGrid.SelectedItem is LayerRow)
        {
            PopulateEditorFromSelection();
        }
        ApplyMappedLanguageText(text);
        ApplyPreviewContextMenuLanguage(text);
        FitLocalizedButtons();
        UndoButton.ToolTip = GetText(text, "tooltips.undo", "Revert last template change (Ctrl+Z)");
        RedoButton.ToolTip = GetText(text, "tooltips.redo", "Restore template change (Ctrl+Y)");
        UndoHistoryButton.ToolTip = GetText(text, "tooltips.history", "Open edit history");
        BackupButton.ToolTip = GetText(text, "tooltips.backup", "Create a template backup");
        RestoreBackupButton.ToolTip = GetText(text, "tooltips.restore", "Restore the latest template backup");
        ThemeToggleButton.ToolTip = ThemeToggleButton.IsChecked == true
            ? GetText(text, "tooltips.switchDark", "Switch to dark theme")
            : GetText(text, "tooltips.switchLight", "Switch to light theme");
        UppercaseEditorPanelHeaders();
    }

    private static void ApplyLanguageComboText(ComboBox combo, Dictionary<string, string> text)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            item.Content = item.Tag?.ToString() switch
            {
                "en" => GetText(text, "languages.en", "English"),
                "tr" => GetText(text, "languages.tr", "Turkish"),
                "ru" => GetText(text, "languages.ru", "Russian"),
                "zh" => GetText(text, "languages.zh", "Chinese"),
                _ => item.Content
            };
        }
    }

    private void ApplyPreviewContextMenuLanguage(Dictionary<string, string> text)
    {
        var items = PreviewLayerContextMenu.Items.OfType<MenuItem>().ToList();
        if (items.Count < 7) return;

        items[0].Header = GetText(text, "previewMenu.duplicate", "Duplicate");
        items[1].Header = GetText(text, "previewMenu.hideShow", "Hide / Show");
        items[2].Header = GetText(text, "previewMenu.lockUnlock", "Lock / Unlock");
        items[4].Header = GetText(text, "previewMenu.bringForward", "Bring Forward");
        items[5].Header = GetText(text, "previewMenu.sendBackward", "Send Backward");
        items[6].Header = GetText(text, "previewMenu.soloSelected", "Solo Selected");
    }

    private void ApplyMappedLanguageText(Dictionary<string, string> text)
    {
        var replacements = new Dictionary<string, (string Key, string Fallback)>(StringComparer.Ordinal)
        {
            ["Settings"] = ("tabs.settings", "Settings"),
            ["Choose how the editor behaves."] = ("settings.description", "Choose how the editor behaves."),
            ["Layer grouping"] = ("settings.layerGrouping", "Layer grouping"),
            ["Show saved groups in the layer list."] = ("settings.layerGroupingDesc", "Show saved groups in the layer list."),
            ["Enabled"] = ("settings.enabled", "Enabled"),
            ["Dark"] = ("footer.dark", "Dark"),
            ["Light"] = ("footer.light", "Light"),
            ["Bug Report"] = ("about.bugReport", "Bug Report"),
            ["Help make the editor steadier by sharing what happened and what you expected."] = ("diagnostics.description", "Help make the editor steadier by sharing what happened and what you expected."),
            ["When reporting a bug, include the device, the theme name, what you clicked, and whether L-Connect was running."] = ("diagnostics.reportHint", "When reporting a bug, include the device, the theme name, what you clicked, and whether L-Connect was running."),
            ["A screenshot or the theme package usually saves a lot of guessing."] = ("diagnostics.screenshotHint", "A screenshot or the theme package usually saves a lot of guessing."),
            ["Updates"] = ("about.updates", "Updates"),
            ["Check the latest release without leaving the editor."] = ("about.updatesDesc", "Check the latest release without leaving the editor."),
            ["Support"] = ("about.support", "Support"),
            ["Report bugs or suggest the next improvement."] = ("about.supportDesc", "Report bugs or suggest the next improvement."),
            ["Diagnostics"] = ("tabs.diagnostics", "Diagnostics"),
            ["Collect version, device and log details for troubleshooting."] = ("about.diagnosticsDesc", "Collect version, device and log details for troubleshooting."),
            ["Unsaved Recovery"] = ("recovery.aboutTitle", "Unsaved Recovery"),
            ["An unsaved edit is available."] = ("recovery.genericAvailable", "An unsaved edit is available."),
            ["Theme Gallery"] = ("tabs.gallery", "Theme Gallery"),
            ["GitHub gallery"] = ("gallery.source", "GitHub gallery"),
            ["Devices"] = ("gallery.devices", "Devices"),
            ["Rating"] = ("gallery.rating", "Rating"),
            ["All ratings"] = ("gallery.allRatings", "All ratings"),
            ["Rated only"] = ("gallery.ratedOnly", "Rated only"),
            ["Sort"] = ("gallery.sort", "Sort"),
            ["Default order"] = ("gallery.defaultOrder", "Default order"),
            ["Most downloaded"] = ("gallery.mostDownloaded", "Most downloaded"),
            ["Highest rated"] = ("gallery.highestRated", "Highest rated"),
            ["Most votes"] = ("gallery.mostVotes", "Most votes"),
            ["Name A-Z"] = ("gallery.nameAz", "Name A-Z"),
            ["Loading gallery..."] = ("gallery.loading", "Loading gallery..."),
            ["Details"] = ("gallery.details", "Details"),
            ["Refresh"] = ("common.refresh", "Refresh"),
            ["Edit selected layers"] = ("batch.title", "Edit selected layers"),
            ["Group selected layers"] = ("groups.createSelected", "Group selected layers"),
            ["Click to select group layers. Double-click to rename."] = ("groups.headerTooltip", "Click to select group layers. Double-click to rename."),
            ["Show or hide group"] = ("groups.visibilityTooltip", "Show or hide group"),
            ["Lock or unlock group"] = ("groups.lockTooltip", "Lock or unlock group"),
            ["Group actions"] = ("groups.actionsTooltip", "Group actions"),
            ["Align left to canvas"] = ("tooltips.alignLeft", "Align left to canvas"),
            ["Center horizontally on canvas"] = ("tooltips.alignCenterX", "Center horizontally on canvas"),
            ["Align right to canvas"] = ("tooltips.alignRight", "Align right to canvas"),
            ["Align top to canvas"] = ("tooltips.alignTop", "Align top to canvas"),
            ["Center vertically on canvas"] = ("tooltips.alignCenterY", "Center vertically on canvas"),
            ["Align bottom to canvas"] = ("tooltips.alignBottom", "Align bottom to canvas"),
            ["Match selected layers X"] = ("tooltips.matchX", "Match selected layers X"),
            ["Match selected layers Y"] = ("tooltips.matchY", "Match selected layers Y"),
            ["Distribute horizontally across canvas"] = ("tooltips.distributeX", "Distribute horizontally across canvas"),
            ["Distribute vertically across canvas"] = ("tooltips.distributeY", "Distribute vertically across canvas"),
            ["Solo"] = ("preview.solo", "Solo"),
            ["Show only selected layer(s)"] = ("tooltips.soloLayers", "Show only selected layer(s)"),
            ["Template ID"] = ("tooltips.templateId", "Template ID"),
            ["Pick Shadow Color"] = ("tooltips.pickShadowColor", "Pick Shadow Color"),
            ["Lian Li Theme Editor"] = ("about.title", "Lian Li Theme Editor"),
            ["Version"] = ("about.version", "Version")
        };

        ApplyMappedLanguageText(this, text, replacements, new HashSet<DependencyObject>());
    }

    private void ApplyMappedLanguageText(
        DependencyObject root,
        Dictionary<string, string> text,
        IReadOnlyDictionary<string, (string Key, string Fallback)> replacements,
        ISet<DependencyObject> visited)
    {
        if (!visited.Add(root)) return;

        if (root is TextBlock textBlock)
        {
            textBlock.Text = TranslateMappedValue(textBlock.Text, text, replacements);
        }

        if (root is ContentControl contentControl && contentControl.Content is string content)
        {
            contentControl.Content = TranslateMappedValue(content, text, replacements);
        }

        if (root is HeaderedContentControl headeredContentControl && headeredContentControl.Header is string headerContent)
        {
            headeredContentControl.Header = TranslateMappedValue(headerContent, text, replacements);
        }

        if (root is HeaderedItemsControl headeredItemsControl && headeredItemsControl.Header is string headerItems)
        {
            headeredItemsControl.Header = TranslateMappedValue(headerItems, text, replacements);
        }

        if (root is FrameworkElement frameworkElement && frameworkElement.ToolTip is string tooltip)
        {
            frameworkElement.ToolTip = TranslateMappedValue(tooltip, text, replacements);
        }

        var visualChildren = 0;
        try
        {
            visualChildren = VisualTreeHelper.GetChildrenCount(root);
        }
        catch (InvalidOperationException)
        {
            visualChildren = 0;
        }

        for (var index = 0; index < visualChildren; index++)
        {
            ApplyMappedLanguageText(VisualTreeHelper.GetChild(root, index), text, replacements, visited);
        }

        IEnumerable logicalChildren;
        try
        {
            logicalChildren = LogicalTreeHelper.GetChildren(root);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        foreach (var child in logicalChildren.OfType<DependencyObject>())
        {
            ApplyMappedLanguageText(child, text, replacements, visited);
        }
    }

    private static string TranslateMappedValue(
        string value,
        Dictionary<string, string> text,
        IReadOnlyDictionary<string, (string Key, string Fallback)> replacements)
    {
        var trimmed = value.Trim();
        if (!replacements.TryGetValue(trimmed, out var replacement)) return value;

        var translated = GetText(text, replacement.Key, replacement.Fallback);
        return value.Length == trimmed.Length ? translated : value.Replace(trimmed, translated, StringComparison.Ordinal);
    }

    private void UppercaseEditorPanelHeaders()
    {
        foreach (var textBlock in new[]
                 {
                     EditLayerHeaderText, AddLayerHeaderText, ShadowHeaderText,
                     GraphSettingsTitle, ImageSettingsTitle, PositionSizeHeader,
                     TextAndFormatHeader, LayerOptionsHeader, GradientHeader
                 })
        {
            if (!string.IsNullOrWhiteSpace(textBlock.Text))
            {
                textBlock.Text = textBlock.Text.ToUpper(CultureInfo.CurrentCulture);
            }
        }
    }

    private void FitLocalizedButtons()
    {
        foreach (var button in new[]
                 {
                     ActiveThemeButton, LoadButton, BackupButton, RestoreBackupButton,
                     UndoButton, RedoButton, ExportLConnectButton,
                     EditorSendThemeButton, EditorImportThemeButton, GallerySendThemesButton,
                     CheckUpdatesButton, OpenGitHubButton, BugReportButton,
                     FeatureRequestButton, CopyDiagnosticInfoButton,
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
            SetBrush("BrBg", "#FFF1F5FB");
            SetBrush("BrSurface", "#A8FFFFFF");
            SetBrush("BrSurface2", "#9CF7FAFF");
            SetBrush("BrField", "#F4FFFFFF");
            SetBrush("BrSelectionField", "#FFFFFFFF");
            SetBrush("BrBorder", "#A6A9BDD5");
            SetBrush("BrBorderSoft", "#7B8FAACA");
            SetBrush("BrHover", "#C7DDEBFF");
            SetBrush("BrSelectedLayer", "#BFCFE2FF");
            SetBrush("BrGridHeader", "#EAF0F6FD");
            SetBrush("BrGridRow", "#DFFFFFFF");
            SetBrush("BrGridAltRow", "#C9F4F8FD");
            SetBrush("BrGridCellBorder", "#88B7C7DA");
            SetBrush("BrDecor1", "#305A8DFF");
            SetBrush("BrDecor2", "#2D8A72E6");
            SetBrush("BrDecor3", "#2A8D5BE8");
            SetBrush("BrDecorStroke", "#33729EDB");
            SetBrush("BrTextPrimary", "#162238");
            SetBrush("BrTextSecondary", "#40516A");
            SetBrush("BrTextTertiary", "#718097");
            SetBrush("BrLockBackground", "#D02B2108");
            SetBrush("BrLockBorder", "#FFFFCB45");
            SetBrush("BrLockIcon", "#FFFFCB45");
            SetLinearGradient("GlassPanelBrush", "#A6FFFFFF", "#78E8F0FA", "#92FFFFFF");
            SetLinearGradient("ExpanderGlassBrush", "#8FFFFFFF", "#62E8F1FA", "#7CFFFFFF");
            SetLinearGradient("GlassHeaderBrush", "#F7FFFFFF", "#E0EAF2FC", "#F2FFFFFF");
            SetLinearGradient("GlassToolbarBrush", "#F2FFFFFF", "#D6E7F0FB", "#EAFFFFFF");
            SetLinearGradient("GlassPopupBrush", "#FCFFFFFF", "#EEF4F9FF", "#F8FFFFFF");
            SetLinearGradient("GlassShimmerBrush", "#00FFFFFF", "#D8FFFFFF", "#5C7C9DC4");
            SetLinearGradient("GlassAccentBorderBrush", "#2874A4E8", "#8B4E86E8", "#2874A4E8");
            SetLinearGradient("BannerSeparatorBrush", "#0074A4E8", "#704E86E8", "#0074A4E8");
            SetShadowCard("#52627A", 0.28);
            FooterBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#52FFFFFF"));
            ExportLConnectButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D9D8F4EE"));
            ExportLConnectButton.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B159A87"));
            ExportLConnectButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#075E54"));
            RestartButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F9DDE3"));
            RestartButton.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CC94D67"));
            RestartButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B2039"));
            WindowRoot.Background = new ImageBrush(
                new BitmapImage(new Uri("pack://application:,,,/Assets/glass-background-light.png")))
            {
                Stretch = Stretch.UniformToFill
            };
            return;
        }

        SetBrush("BrBg", "#FF07152F");
        SetBrush("BrSurface", "#CC10264A");
        SetBrush("BrSurface2", "#C8172E59");
        SetBrush("BrField", "#D90B1B38");
        SetBrush("BrSelectionField", "#FF0B1B38");
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
        SetBrush("BrLockBackground", "#32FFB72E");
        SetBrush("BrLockBorder", "#90FFCB45");
        SetBrush("BrLockIcon", "#FFFFD15A");
        SetLinearGradient("GlassPanelBrush", "#550D2040", "#420A1A35", "#580D2040");
        SetLinearGradient("ExpanderGlassBrush", "#240D2040", "#180A1A35", "#280D2040");
        SetLinearGradient("GlassHeaderBrush", "#E0060E20", "#C0040C1A", "#E0060E20");
        SetLinearGradient("GlassToolbarBrush", "#D00B1F3E", "#B0071528", "#D00B1F3E");
        SetLinearGradient("GlassPopupBrush", "#E0122D58", "#D50B2348", "#E2143563");
        SetLinearGradient("GlassShimmerBrush", "#005080B0", "#705488C8", "#005080B0");
        SetLinearGradient("GlassAccentBorderBrush", "#401A4A8C", "#802478F3", "#401A4A8C");
        SetLinearGradient("BannerSeparatorBrush", "#001A4A8C", "#802478F3", "#001A4A8C");
        SetShadowCard("#000820", 0.55);
        FooterBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#14060E1C"));
        ExportLConnectButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2020BFA8"));
        ExportLConnectButton.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6620BFA8"));
        ExportLConnectButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5DE4D0"));
        RestartButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20D55263"));
        RestartButton.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#66D55263"));
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

    private void SetShadowCard(string color, double opacity)
    {
        Resources["ShadowCard"] = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 28,
            ShadowDepth = 2,
            Direction = 270,
            Color = (Color)ColorConverter.ConvertFromString(color),
            Opacity = opacity
        };
    }

    private static string GetComboText(ComboBox combo)
    {
        if (combo.IsEditable && !string.IsNullOrWhiteSpace(combo.Text))
        {
            return combo.Text.Trim();
        }

        if (combo.SelectedItem is ComboBoxItem item) return item.Tag?.ToString() ?? item.Content?.ToString() ?? "";
        if (combo.SelectedItem is GraphStyleOption graphStyle) return graphStyle.Code;
        return combo.SelectedItem?.ToString() ?? combo.Text ?? "";
    }

    private void AddFontComboItem(ComboBox combo, string fontName)
    {
        var canonicalName = ResolveCanonicalFontName(fontName);
        if (combo.Items.OfType<ComboBoxItem>().Any(item =>
                string.Equals(item.Tag?.ToString(), canonicalName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        combo.Items.Add(new ComboBoxItem
        {
            Content = canonicalName,
            Tag = canonicalName,
            FontFamily = ResolveFontFamily(canonicalName)
        });
    }

    private void PopulateFontCombos(IEnumerable<string> fonts)
    {
        foreach (var font in fonts
                     .Where(font => !string.IsNullOrWhiteSpace(font))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(font => font, StringComparer.OrdinalIgnoreCase))
        {
            AddFontComboItem(FontCombo, font);
            AddFontComboItem(AddFontCombo, font);
        }
    }

    private async Task RunDeferredStartupWorkAsync()
    {
        try
        {
            await Task.Delay(350);
            await LoadFontsDeferredAsync();
            await Task.Delay(250);
            await RefreshGraphStylesAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Deferred startup work failed.", ex);
        }
    }

    private async Task LoadFontsDeferredAsync()
    {
        var selectedFont = GetComboValue(FontCombo);
        var selectedAddFont = GetComboValue(AddFontCombo);
        IReadOnlyList<string> fonts;
        try
        {
            fonts = await _supporter.ListFontsAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Font list could not be loaded.", ex);
            return;
        }

        PopulateFontCombos(fonts.Concat(_customFontNames));
        SetComboText(FontCombo, string.IsNullOrWhiteSpace(selectedFont) ? GetDefaultLayerFontName() : selectedFont);
        SetComboText(AddFontCombo, string.IsNullOrWhiteSpace(selectedAddFont) ? GetDefaultLayerFontName() : selectedAddFont);
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

    private string GetDataSourceDisplayName(string dataSource)
    {
        var normalized = (dataSource ?? "").ToUpperInvariant();
        var fallback = normalized switch
        {
            "APM" => "APM",
            "CPUCLOCK" => "CPU Clock MHz",
            "CPUCLOCK_G" => "CPU Clock GHz",
            "CPUFAN" => "CPU Fan",
            "CPULOAD" => "CPU Load",
            "CPUMODEL" => "CPU Model",
            "CPUPOWER" or "CPUPWR" => "CPU Power",
            "CPUTEMP" => "CPU Temperature",
            "CPUTEMP_F" => "CPU Temperature °F",
            "CPUVOLTAGE" => "CPU Voltage",
            "DATE" => "Date",
            "DAY" => "Day",
            "DOWNDSPEED" => "Download Speed",
            "DRVLOAD" => "Drive Load",
            "FPS_AVG" => "Average FPS",
            "GPUCLOCK" => "GPU Clock MHz",
            "GPUCLOCK_G" => "GPU Clock GHz",
            "GPUFAN" => "GPU Fan",
            "GPULOAD" => "GPU Load",
            "GPUPOWER" or "GPUPWR" => "GPU Power",
            "GPUMODEL" => "GPU Model",
            "GPURAM" => "GPU Memory Used MB",
            "GPURAMLOAD" => "GPU Memory Load",
            "GPURAMTOTAL" => "GPU Memory Total MB",
            "GPUTEMP" => "GPU Temperature",
            "GPUTEMP_F" => "GPU Temperature °F",
            "GPUVOLTAGE" => "GPU Voltage",
            "HDDTEMP" => "Drive Temperature",
            "HDDTEMP_F" => "Drive Temperature °F",
            "HDDUSED" => "Drive Used",
            "PUMP" => "Pump",
            "RAM" => "RAM Used",
            "RAM_GB" => "RAM Used GB",
            "RAMLOAD" => "RAM Load",
            "RAMTOTAL" => "RAM Total",
            "RAMTOTAL_GB" => "RAM Total GB",
            "RAMVALID" => "RAM Available",
            "RAMVALID_GB" => "RAM Available GB",
            "STATICTEXT" => "Static Text",
            "TIME" => "Time",
            "UPSPEED" => "Upload Speed",
            "WATERPUMP" => "Water Pump",
            "WATERTEMPC" => "Water Temperature °C",
            "WATERTEMPF" => "Water Temperature °F",
            _ => dataSource ?? ""
        };
        var key = normalized switch
        {
            "CPUPOWER" => "CPUPWR",
            "GPUPOWER" => "GPUPWR",
            _ => normalized
        };
        return GetLanguageText($"dataSources.{key}", fallback);
    }

    private void RefreshDataSourceItems()
    {
        var wasLoading = _isLoading;
        _isLoading = true;
        var dataSelection = GetComboValue(DataCombo);
        var addSelection = GetComboValue(AddDataCombo);
        DataCombo.Items.Clear();
        AddDataCombo.Items.Clear();
        foreach (var data in DataSources.OrderBy(GetDataSourceDisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var display = GetDataSourceDisplayName(data);
            DataCombo.Items.Add(new ComboBoxItem { Content = display, Tag = data });
            AddDataCombo.Items.Add(new ComboBoxItem { Content = display, Tag = data });
        }
        SetComboText(DataCombo, dataSelection);
        SetComboText(AddDataCombo, addSelection);
        _isLoading = wasLoading;
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

    private static void SetComboTag(ComboBox combo, string value) => SetComboText(combo, value);

    private static string GetLayerDisplayType(LayerRow layer)
    {
        return (layer.Type ?? "") switch
        {
            "GraphAnimation" => "Animation",
            "GraphImage" => "Image",
            "GraphLine" => "Chart",
            "GraphArchBar" => "Curved Bar",
            "GraphSensor" => "Ring Graph",
            "GraphClock" => "Gauge",
            "GraphDynamicBar" => "Dynamic Status",
            "GraphStatuBar" => "Status Bar",
            "GraphItem" when string.Equals(layer.TypeName, "Text", StringComparison.OrdinalIgnoreCase) => "Text",
            "GraphItem" => "Data",
            _ => layer.Type ?? ""
        };
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
        if (string.Equals(layer.Type, "GraphSensor", StringComparison.OrdinalIgnoreCase))
        {
            GraphTypeNameBox.Items.Clear();
            GraphTypeNameBox.Items.Add("Sensor");
            GraphSubTypeNameBox.Items.Clear();
            foreach (var style in SensorStyleOptions)
            {
                GraphSubTypeNameBox.Items.Add(style.Style);
            }
            return;
        }

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

    private static void PopulateSensorTypeCombo(ComboBox combo)
    {
        combo.Items.Clear();
        foreach (var sensor in SensorTypeOptions)
        {
            combo.Items.Add(new ComboBoxItem { Content = sensor.Label, Tag = sensor.Type });
        }
    }

    private static string SensorTypeFromDataSource(string dataSource)
    {
        return SensorTypeOptions.FirstOrDefault(option =>
            string.Equals(option.DataSource, dataSource, StringComparison.OrdinalIgnoreCase)).Type ?? "CPULoad";
    }

    private static string SensorDataSourceFromType(string sensorType)
    {
        return SensorTypeOptions.FirstOrDefault(option =>
            string.Equals(option.Type, sensorType, StringComparison.OrdinalIgnoreCase)).DataSource ?? "CPULOAD";
    }

    private static (string Type, string Label, string DataSource, string Top, string Bottom, string Unit) GetSensorTypeInfo(string sensorType)
    {
        var match = SensorTypeOptions.FirstOrDefault(option =>
            string.Equals(option.Type, sensorType, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(match.Type) ? SensorTypeOptions[0] : match;
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

        var namedColor = Regex.Match(value, @"^Color\s*\[(?<name>[^\]]+)\]$", RegexOptions.IgnoreCase);
        if (namedColor.Success)
        {
            var name = namedColor.Groups["name"].Value.Trim();
            return name.Equals("Empty", StringComparison.OrdinalIgnoreCase) ? "" : name;
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
        if (newColor == null) return;
        ColorBox.Text = newColor;
        CommitSelectedColor(ColorBox, static (layer, color) => layer.Color = color);
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
        if (newColor == null) return;
        FrontColorBox.Text = newColor;
        CommitSelectedColor(FrontColorBox, static (layer, color) => layer.FrontColor = color);
    }

    private void BackColorPickButton_Click(object sender, RoutedEventArgs e)
    {
        var newColor = ColorPickerDialog.ShowDialog(this, BackColorBox.Text);
        if (newColor == null) return;
        BackColorBox.Text = newColor;
        CommitSelectedColor(BackColorBox, static (layer, color) => layer.BackColor = color);
    }

    private void SensorRingEndColorPickButton_Click(object sender, RoutedEventArgs e)
    {
        var newColor = ColorPickerDialog.ShowDialog(this, SensorRingEndColorBox.Text);
        if (newColor == null) return;
        SensorRingEndColorBox.Text = newColor;
        CommitSelectedColor(SensorRingEndColorBox, static (layer, color) => layer.SensorColor2 = color);
    }

    private void GradientColorPickButton_Click(object sender, RoutedEventArgs e)
    {
        var newColor = ColorPickerDialog.ShowDialog(this, GradientColorBox.Text);
        if (newColor == null) return;
        GradientColorBox.Text = newColor;
        CommitSelectedColor(GradientColorBox, static (layer, color) => layer.GradientColor = color);
    }

    private void TextGradientColorPickButton_Click(object sender, RoutedEventArgs e)
    {
        var newColor = ColorPickerDialog.ShowDialog(this, TextGradientColorBox.Text);
        if (newColor == null) return;
        TextGradientColorBox.Text = newColor;
        CommitSelectedColor(TextGradientColorBox, static (layer, color) => layer.FontGradientColor = color);
    }

    private void SensorTopColorPickButton_Click(object sender, RoutedEventArgs e)
    {
        var newColor = ColorPickerDialog.ShowDialog(this, SensorTopColorBox.Text);
        if (newColor == null) return;
        SensorTopColorBox.Text = newColor;
        CommitSelectedColor(SensorTopColorBox, static (layer, color) => layer.SensorTopFontColor = color);
    }

    private void SensorBottomColorPickButton_Click(object sender, RoutedEventArgs e)
    {
        var newColor = ColorPickerDialog.ShowDialog(this, SensorBottomColorBox.Text);
        if (newColor == null) return;
        SensorBottomColorBox.Text = newColor;
        CommitSelectedColor(SensorBottomColorBox, static (layer, color) => layer.SensorBottomFontColor = color);
    }

    private void ChartFillColorPickButton_Click(object sender, RoutedEventArgs e)
    {
        var newColor = ColorPickerDialog.ShowDialog(this, ChartFillColorBox.Text);
        if (newColor == null) return;
        ChartFillColorBox.Text = newColor;
        CommitSelectedColor(ChartFillColorBox, static (layer, color) => layer.FillColor = color);
    }

    private void CommitSelectedColor(
        TextBox colorBox,
        Action<LayerRow, string> assignColor)
    {
        if (LayerGrid.SelectedItem is not LayerRow layer)
        {
            return;
        }

        var color = NormalizeColorText(colorBox.Text);
        colorBox.Text = color;
        assignColor(layer, color);
        MarkLayerDirty(layer);
        RequestPreviewDraw();
    }

    // Change Image handler
    private async void ChangeImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (LayerGrid.SelectedItem is LayerRow selected &&
            string.Equals(selected.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
        {
            await ChooseAndSetBackgroundAsync();
            return;
        }

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
            if (LayerGrid.SelectedItem is LayerRow selectedLayer)
            {
                selectedLayer.Media = fileName;
                selectedLayer.MediaPath = destPath;
                if (!string.Equals(selectedLayer.Type, "GraphClock", StringComparison.OrdinalIgnoreCase))
                {
                    var fitZoom = GetImageFitZoom(destPath);
                    ZoomBox.Text = FormatZoom(fitZoom);
                    var placement = GetImagePlacement(
                        destPath,
                        Math.Max(_templateCanvasWidth, _templateCanvasHeight).ToString(CultureInfo.InvariantCulture),
                        selectedLayer.X,
                        selectedLayer.Y);
                    selectedLayer.X = placement.X.ToString(CultureInfo.InvariantCulture);
                    selectedLayer.Y = placement.Y.ToString(CultureInfo.InvariantCulture);
                    XBox.Text = selectedLayer.X;
                    YBox.Text = selectedLayer.Y;
                }
                MarkLayerDirty(selectedLayer);
                UpdateLayerPreviewVisual(selectedLayer);
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
                TextBox.Text = NormalizeLConnectText(layer.Text);
            }
        }
    }

    private void DataCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        var data = GetComboText(DataCombo);
        if (LayerGrid.SelectedItem is LayerRow sensorLayer &&
            string.Equals(sensorLayer.Type, "GraphSensor", StringComparison.OrdinalIgnoreCase))
        {
            _isLoading = true;
            sensorLayer.SensorType = data;
            sensorLayer.DataSource = SensorDataSourceFromType(data);
            sensorLayer.Text = SampleValueFor(sensorLayer.DataSource);
            TextBox.Text = sensorLayer.Text;
            _isLoading = false;
            OnInputChanged();
            return;
        }

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
        FormatLabel.Content = GetLanguageText("labels.format", "FORMAT");
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
        var icon = GetAddLayerIcon(type);
        ApplyLayerIcon(AddLayerIconBorder, AddLayerIconPath, AddLayerIconShadow, icon.Data, icon.Color);
        var isAnimation = type == "Animation";
        var isText = type == "Text";
        var isData = type == "Data";
        var isImage = type == "Image";
        var isClock = type == "Gauge";
        var isRingGraph = type == "RingGraph";
        var isGraph = type is "StatusBar" or "DynamicStatus" or "CurvedBar" or "Chart" or "RingGraph";

        AddTextPanel.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        AddTextButton.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;

        if (isRingGraph)
        {
            PopulateAddSensorTypeItems();
        }
        else
        {
            PopulateAddDataSourceItems();
        }

        AddDataPanel.Visibility = isData || isGraph || isClock ? Visibility.Visible : Visibility.Collapsed;
        AddDataButton.Visibility = isData ? Visibility.Visible : Visibility.Collapsed;
        AddFormatPanel.Visibility = Visibility.Collapsed;
        if (!isData && !isClock)
        {
            AddFormatCombo.Visibility = Visibility.Collapsed;
        }
        else
        {
            if (isClock && string.IsNullOrWhiteSpace(GetComboText(AddDataCombo))) SetComboText(AddDataCombo, "TIME");
            UpdateFormatComboItems(GetComboText(AddDataCombo), AddFormatCombo, null);
            AddFormatPanel.Visibility = AddFormatCombo.Visibility;
        }

        AddImageButton.Visibility = isImage || isClock ? Visibility.Visible : Visibility.Collapsed;
        AddImageButton.Content = isClock ? "Choose & Add Gauge Needle" : GetLanguageText("add.chooseAddImage", "Choose & Add Image");
        AddGraphPanel.Visibility = isRingGraph ? Visibility.Visible : Visibility.Collapsed;
        AddGraphButton.Visibility = isGraph || isAnimation ? Visibility.Visible : Visibility.Collapsed;
        AddGraphButton.Content = isAnimation
            ? GetLanguageText("add.chooseBackground", "Choose Background")
            : FormatLanguageText("add.addTypedLayer", "Add {0}", GetLanguageText($"add.type{type}", type));

        AddFontPanel.Visibility = isText || isData ? Visibility.Visible : Visibility.Collapsed;
        AddBoldPanel.Visibility = isText || isData ? Visibility.Visible : Visibility.Collapsed;
        AddColorPanel.Visibility = isImage || isClock || isAnimation ? Visibility.Collapsed : Visibility.Visible;

        AddXBox.Text = isRingGraph ? "40" : AddXBox.Text;
        AddYBox.Text = isRingGraph ? "40" : AddYBox.Text;
        AddSizeBox.Text = isImage || isClock ? "160" : isRingGraph ? "0.5" : isGraph ? "80" : "40";
    }

    private void PopulateAddDataSourceItems()
    {
        var selection = GetComboValue(AddDataCombo);
        AddDataCombo.Items.Clear();
        foreach (var data in DataSources.OrderBy(GetDataSourceDisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            AddDataCombo.Items.Add(new ComboBoxItem { Content = GetDataSourceDisplayName(data), Tag = data });
        }
        SetComboText(AddDataCombo, selection);
    }

    private void PopulateDataSourceCombo(ComboBox combo, string selection)
    {
        combo.Items.Clear();
        foreach (var data in DataSources.OrderBy(GetDataSourceDisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            combo.Items.Add(new ComboBoxItem { Content = GetDataSourceDisplayName(data), Tag = data });
        }
        SetComboText(combo, selection);
    }

    private void PopulateAddSensorTypeItems()
    {
        var selection = GetComboValue(AddDataCombo);
        AddDataCombo.Items.Clear();
        foreach (var sensor in SensorTypeOptions)
        {
            AddDataCombo.Items.Add(new ComboBoxItem { Content = sensor.Label, Tag = sensor.Type });
        }
        SetComboText(AddDataCombo, string.IsNullOrWhiteSpace(selection) ? "CPULoad" : selection);

        AddGraphStyleCombo.Items.Clear();
        foreach (var style in SensorStyleOptions)
        {
            AddGraphStyleCombo.Items.Add(new GraphStyleOption
            {
                Label = style.Label,
                Code = style.Style,
                GraphType = "GraphSensor",
                TypeName = "Sensor",
                SubTypeName = style.Style
            });
        }
        AddGraphStyleCombo.SelectedIndex = 1;
    }

    private static (string Data, string Color) GetAddLayerIcon(string type)
    {
        return type switch
        {
            "Animation" => ("M10,6 L26,16 L10,26 Z", "#7C3AED"),
            "Text" => ("M6,7 H26 V11 H20 V26 H12 V11 H6 Z", "#DC2626"),
            "Data" => ("M5,25 V18 H11 V25 Z M13,25 V9 H19 V25 Z M21,25 V14 H27 V25 Z", "#06B6D4"),
            "Image" => ("M5,7 H27 V25 H5 Z M8,22 L14,15 L18,19 L22,13 L27,22 Z M10,11 A2,2 0 1 1 9.9,11", "#16A34A"),
            "Gauge" => ("M16,5 A11,11 0 1 1 15.9,5 M16,9 V16 L21,19", "#F59E0B"),
            "StatusBar" => ("M4,12 H10 V20 H4 Z M12,12 H18 V20 H12 Z M20,12 H26 V20 H20 Z M28,12 H30 V20 H28 Z", "#EA7C17"),
            "DynamicStatus" => ("M5,14 H27 V18 H5 Z M18,9 H23 V23 H18 Z", "#0D9488"),
            "CurvedBar" => ("M5,25 A11,11 0 1 1 27,25 L23,25 A7,7 0 1 0 9,25 Z", "#DB2777"),
            "RingGraph" => ("M16,4 A12,12 0 1 1 15.9,4 M9,18 A7,7 0 0 1 23,18 M16,18 L21,12", "#14B8A6"),
            "Chart" => ("M5,24 L11,17 L16,20 L22,9 L28,13 L28,18 L22,14 L17,25 L12,22 L8,27 Z", "#4F46E5"),
            _ => ("M6,6 H26 V26 H6 Z", "#64748B")
        };
    }

    private static void ApplyLayerIcon(
        Border border,
        System.Windows.Shapes.Path path,
        System.Windows.Media.Effects.DropShadowEffect shadow,
        string data,
        string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        border.Background = brush;
        path.Data = Geometry.Parse(string.IsNullOrWhiteSpace(data) ? "M6,6 H26 V26 H6 Z" : data);
        shadow.Color = brush.Color;
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
            if (box != null)
            {
                box.Text = NormalizeTimeFormatForLConnect(box.Text);
            }
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
            SelectFormatOption(combo, box?.Text);
        }
        else if (source is "CPUPWR" or "CPUPOWER" or "GPUPWR" or "GPUPOWER")
        {
            combo.Visibility = Visibility.Visible;
            AddFormatOption(combo, "1 decimal", "0.0");
            if (box != null && !string.Equals(box.Text, "0.0", StringComparison.OrdinalIgnoreCase))
            {
                box.Text = "0.0";
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
        return source is "TIME" or "DATE" or "DAY" or
               "CPUPWR" or "CPUPOWER" or "GPUPWR" or "GPUPOWER";
    }

    private static string DefaultFormatForDataSource(string dataSource)
    {
        return (dataSource ?? "").ToUpperInvariant() switch
        {
            "TIME" => "h:m",
            "DATE" => "Y-M-D",
            "DAY" => "Day_en",
            "CPUPWR" or "CPUPOWER" or "GPUPWR" or "GPUPOWER" => "0.0",
            _ => ""
        };
    }

    private static string NormalizeTimeFormatForLConnect(string? format)
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

    private static string NormalizeFormatForDataSource(string dataSource, string format)
    {
        return string.Equals(dataSource, "TIME", StringComparison.OrdinalIgnoreCase)
            ? NormalizeTimeFormatForLConnect(format)
            : format;
    }

    // Undo/Redo Engine
    private void PushUndoState(string description = "Edit layers")
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(Layers);
            _undoStack.Push(new EditSnapshot(description, bytes, DateTime.UtcNow));
            while (_undoStack.Count > 50)
            {
                var trimmed = _undoStack.Take(50).Reverse().ToArray();
                _undoStack.Clear();
                foreach (var item in trimmed) _undoStack.Push(item);
            }
            _redoStack.Clear();
            UpdateHistoryButtons();
        }
        catch (Exception ex) { AppLogger.Error("Undo snapshot could not be created.", ex); }
    }

    private async void UndoButton_Click(object sender, RoutedEventArgs e) => await UndoAsync();
    private async void RedoButton_Click(object sender, RoutedEventArgs e) => await RedoAsync();

    private async Task UndoAsync()
    {
        if (_undoStack.Count == 0) return;
        try
        {
            SetBusy(true, GetLanguageText("status.undoingChange", "Undoing change..."));
            var currentBytes = JsonSerializer.SerializeToUtf8Bytes(Layers);
            var previous = _undoStack.Pop();
            _redoStack.Push(new EditSnapshot(previous.Description, currentBytes, DateTime.UtcNow));
            
            var previousLayers = JsonSerializer.Deserialize<List<LayerRow>>(previous.Layers);
            if (previousLayers != null)
            {
                Layers.Clear();
                foreach (var l in previousLayers)
                {
                    Layers.Add(l);
                }
                ClearDirtyLayers();
                foreach (var layer in Layers) MarkLayerDirty(layer);
                _editorUndoArmed = false;
                LayerGrid.Items.Refresh();
                DrawPreview();
            }
            SetBusy(false, GetLanguageText("status.undoApplied", "Undo applied. Click Apply to save to disk."));
            UpdateHistoryButtons();
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.undoFailed", "Undo failed."));
            MessageBox.Show(this, ex.Message, GetLanguageText("messages.undoFailed", "Undo failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        await Task.CompletedTask;
    }

    private async Task RedoAsync()
    {
        if (_redoStack.Count == 0) return;
        try
        {
            SetBusy(true, GetLanguageText("status.redoingChange", "Redoing change..."));
            var currentBytes = JsonSerializer.SerializeToUtf8Bytes(Layers);
            var next = _redoStack.Pop();
            _undoStack.Push(new EditSnapshot(next.Description, currentBytes, DateTime.UtcNow));
            
            var nextLayers = JsonSerializer.Deserialize<List<LayerRow>>(next.Layers);
            if (nextLayers != null)
            {
                Layers.Clear();
                foreach (var l in nextLayers)
                {
                    Layers.Add(l);
                }
                ClearDirtyLayers();
                foreach (var layer in Layers) MarkLayerDirty(layer);
                _editorUndoArmed = false;
                LayerGrid.Items.Refresh();
                DrawPreview();
            }
            SetBusy(false, GetLanguageText("status.redoApplied", "Redo applied. Click Apply to save to disk."));
            UpdateHistoryButtons();
        }
        catch (Exception ex)
        {
            SetBusy(false, GetLanguageText("status.redoFailed", "Redo failed."));
            MessageBox.Show(this, ex.Message, GetLanguageText("messages.redoFailed", "Redo failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        await Task.CompletedTask;
    }

    private void UndoHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom, Style = (Style)FindResource("ThemedContextMenu") };
        foreach (System.Collections.DictionaryEntry resource in Resources)
            menu.Resources[resource.Key] = resource.Value;
        var entries = _undoStack.Take(15).ToList();
        if (entries.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = GetLanguageText("history.empty", "No edits yet"), IsEnabled = false, Style = (Style)FindResource("ThemedMenuItem") });
        }
        else
        {
            for (var index = 0; index < entries.Count; index++)
            {
                var undoCount = index + 1;
                var entry = entries[index];
                var item = new MenuItem { Header = $"{entry.Description}  |  {entry.CreatedAtUtc.ToLocalTime():HH:mm:ss}", Style = (Style)FindResource("ThemedMenuItem") };
                item.Click += async (_, _) =>
                {
                    for (var step = 0; step < undoCount && _undoStack.Count > 0; step++) await UndoAsync();
                };
                menu.Items.Add(item);
            }
        }
        menu.IsOpen = true;
    }

    private void UpdateHistoryButtons()
    {
        if (UndoButton == null) return;
        UndoButton.IsEnabled = _undoStack.Count > 0;
        RedoButton.IsEnabled = _redoStack.Count > 0;
        UndoHistoryButton.IsEnabled = _undoStack.Count > 0;
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

        if ((LayerGrid.IsKeyboardFocusWithin || PreviewCanvas.IsKeyboardFocusWithin) &&
            e.Key is Key.Left or Key.Right or Key.Up or Key.Down &&
            LayerGrid.SelectedItems.Count > 0)
        {
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            var dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
            var dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
            if (NudgeSelectedLayers(dx, dy))
            {
                e.Handled = true;
            }
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

    private bool NudgeSelectedLayers(int dx, int dy)
    {
        var selected = GetSelectedLayers(includeLocked: false, includeAnimation: false);
        if (selected.Count == 0) return false;

        if (!_editorUndoArmed)
        {
            PushUndoState(GetLanguageText("history.move", "Move layers"));
            _editorUndoArmed = true;
        }

        var touchedLayers = new HashSet<LayerRow>();
        foreach (var layer in selected)
        {
            var x = TryParseInt(layer.X, out var parsedX) ? parsedX : 0;
            var y = TryParseInt(layer.Y, out var parsedY) ? parsedY : 0;
            layer.X = (x + dx).ToString(CultureInfo.InvariantCulture);
            layer.Y = (y + dy).ToString(CultureInfo.InvariantCulture);
            MarkLayerDirty(layer);
            touchedLayers.Add(layer);

            if (PairCheck.IsChecked == true)
            {
                var paired = FindPairedLayer(layer);
                if (paired != null && !paired.IsLocked)
                {
                    var px = TryParseInt(paired.X, out var parsedPairX) ? parsedPairX : 0;
                    var py = TryParseInt(paired.Y, out var parsedPairY) ? parsedPairY : 0;
                    paired.X = (px + dx).ToString(CultureInfo.InvariantCulture);
                    paired.Y = (py + dy).ToString(CultureInfo.InvariantCulture);
                    MarkLayerDirty(paired);
                    touchedLayers.Add(paired);
                }
            }
        }

        if (LayerGrid.SelectedItem is LayerRow current)
        {
            XBox.Text = current.X;
            YBox.Text = current.Y;
        }

        foreach (var layer in touchedLayers)
        {
            UpdateLayerPreviewVisual(layer);
        }
        DrawAlignmentGuides(LayerGrid.SelectedItem as LayerRow);
        SetStatus($"Moved selected layer(s) by {Math.Max(Math.Abs(dx), Math.Abs(dy))} px. Press Apply to save.");
        return true;
    }

    private void SoloLayerButton_Click(object sender, RoutedEventArgs e)
    {
        SetSoloLayerMode(!_soloSelectedLayers);
    }

    private void SetSoloLayerMode(bool enabled)
    {
        _soloSelectedLayers = enabled && LayerGrid.SelectedItems.Count > 0;
        if (_soloSelectedLayers)
        {
            SoloLayerButton.Background = NewBrush("#3B82F6", "#2563EB");
        }
        else
        {
            SoloLayerButton.ClearValue(Control.BackgroundProperty);
        }
        SetStatus(_soloSelectedLayers
            ? "Solo mode enabled for selected layer(s)."
            : "Solo mode disabled.");
        RequestPreviewDraw();
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
                universalOrientation = IsUniversalLandscape() ? "landscape" : "portrait",
                groupingEnabled = _groupingEnabled,
                ownedDevices = GetOwnedDeviceModels().ToArray(),
                autoApplyGalleryThemes = AutoApplyGalleryThemesCheck?.IsChecked == true,
                animateVideoPreviews = AnimateVideoPreviewsCheck?.IsChecked != false,
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
    private List<string> GetLConnectDevicePaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var result = Task.Run(() =>
                    _lConnectClient.SendServiceRequestForJsonAsync(client, "SyncControllerList", "{}"))
                .GetAwaiter()
                .GetResult();
            TraceUniversal88Apply(
                $"L-Connect HTTP action=SyncControllerList; port={(result.Port?.ToString(CultureInfo.InvariantCulture) ?? "<none>")}; " +
                $"mode={result.RequestMode}; status={(result.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "<none>")}; " +
                $"body={DescribeLConnectResponseForTrace(result.Body, "SyncControllerList")}; " +
                $"error={(string.IsNullOrWhiteSpace(result.Error) ? "<none>" : result.Error)}");
            if (result.IsHttpSuccess && !string.IsNullOrWhiteSpace(result.Body))
            {
                using var doc = JsonDocument.Parse(result.Body);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (MatchesSelectedLConnectController(prop.Name))
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
                                if (!MatchesSelectedLConnectController(path))
                                {
                                    continue;
                                }
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

        if (paths.Count == 0 && !IsWideScreenDeviceSelected())
        {
            paths.Add(@"usb\\vid_1cbe&pid_a034\\0834ab040486c702w");
            paths.Add(@"usb\vid_1cbe&pid_a034\0834ab040486c702w");
        }

        return paths.ToList();
    }

    private bool MatchesSelectedLConnectController(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith("dummy-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsWideScreenDeviceSelected())
        {
            return path.Contains("universal", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("vm", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("9.2", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("us88", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("8.8", StringComparison.OrdinalIgnoreCase) ||
                   (path.Contains("vid_0416", StringComparison.OrdinalIgnoreCase) &&
                    path.Contains("pid_8040", StringComparison.OrdinalIgnoreCase));
        }

        return path.Contains("vid_1cbe", StringComparison.OrdinalIgnoreCase) &&
               path.Contains("pid_a034", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> ActivateInstalledThemeAsync(
        string templateId,
        string deviceModel,
        string templatePath,
        string backgroundPath)
    {
        if (string.Equals(deviceModel, UniversalScreenDeviceModel, StringComparison.OrdinalIgnoreCase))
        {
            return await ActivateUniversal88ThemeAsync(templateId, templatePath, backgroundPath);
        }

        var activatedId = await _themeInstallationService.ActivateAsync(
            templateId,
            templatePath,
            backgroundPath,
            async () => await GetRegisteredTemplateIdsContainedInFileAsync(templatePath),
            candidate => ApplyInstalledTemplateThroughLConnectAsync(deviceModel, candidate, backgroundPath),
            () => ApplyTemplateThroughLConnectAsync(deviceModel, templateId, templatePath, backgroundPath));
        if (!string.IsNullOrWhiteSpace(activatedId))
        {
            await Task.Run(() => TrySetActiveTemplateProfile(activatedId, deviceModel));
            await TriggerLConnectRefreshAsync();
            return true;
        }
        return false;
    }

    private async Task<bool> ActivateUniversal88ThemeAsync(
        string templateId,
        string templatePath,
        string backgroundPath,
        bool updatePreview = true)
    {
        var previousTraceId = _universal88ApplyTraceId;
        _universal88ApplyTraceId = Guid.NewGuid().ToString("N")[..8];
        var traceId = _universal88ApplyTraceId;
        var stopwatch = Stopwatch.StartNew();
        // Read WPF state once on the UI thread. The profile work below runs on a
        // worker thread and must not touch UniversalOrientationCombo directly.
        var preferLandscape = IsUniversalLandscape();
        var candidates = ThemeInstallationService
            .BuildActivationCandidates(templateId, templatePath, backgroundPath)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(templateId))
        {
            candidates.Add(templateId);
        }

        TraceUniversal88Apply(
            $"BEGIN version={GetAppDisplayVersion()} built={BuildInfo.BuiltAt}; " +
            $"orientation={(preferLandscape ? "landscape" : "portrait")}; " +
            $"templateId={templateId}; templateFile={DescribeFileForTrace(templatePath)}; " +
            $"backgroundFile={DescribeFileForTrace(backgroundPath)}; candidates=[{string.Join(", ", candidates)}]");

        try
        {
            if (updatePreview)
            {
                try
                {
                    TraceUniversal88Apply("Preview update started.");
                    await SaveAndApplyThemePreviewAsync(
                        UniversalScreenDeviceModel,
                        templatePath,
                        templateId,
                        candidates.Concat(GetTemplatePreviewAliases(templateId)));
                    TraceUniversal88Apply("Preview update completed.");
                }
                catch (Exception ex)
                {
                    TraceUniversal88Apply($"Preview update failed: {ex.GetType().Name}: {ex.Message}", warning: true);
                }
            }
            else
            {
                TraceUniversal88Apply("Preview update skipped by caller.");
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
                var devicePaths = GetLConnectDevicePaths();
                TraceUniversal88Apply(
                    $"L-Connect controller discovery count={devicePaths.Count}; " +
                    $"controllers=[{string.Join(", ", devicePaths.Select(DescribeControllerForTrace))}]");
                foreach (var path in devicePaths)
                {
                    TraceUniversal88Apply($"Controller begin: {DescribeControllerForTrace(path)}");
                    await SendLConnectDeviceRequestAsync(client, path, "ReloadAssets", "{}");
                    var registeredIds = await GetLConnectTemplateIdsAsync(client, path);
                    TraceUniversal88Apply(
                        $"L-Connect templates count={registeredIds.Count}; ids=[{string.Join(", ", registeredIds)}]");
                    var liveCandidates = ThemeInstallationService
                        .MatchRegisteredIds(templatePath, registeredIds)
                        .Concat(candidates.Where(id => registeredIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
                        .Concat(candidates)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    TraceUniversal88Apply($"Apply order=[{string.Join(", ", liveCandidates)}]");

                    foreach (var candidateId in liveCandidates)
                    {
                        TraceUniversal88Apply($"Trying ApplyTemplate candidate={candidateId}");
                        var accepted = await SendLConnectDeviceRequestAsync(
                            client,
                            path,
                            "ApplyTemplate",
                            JsonSerializer.Serialize(candidateId));
                        var selected = accepted && await WaitForSelectedTemplateAsync(client, path, candidateId);
                        TraceUniversal88Apply($"Candidate result id={candidateId}; accepted={accepted}; selectedConfirmed={selected}");
                        if (selected)
                        {
                            await CopyTemplateBackgroundAsync(client, path, UniversalScreenDeviceModel, candidateId, backgroundPath);
                            var profileSaved = await SendLConnectDeviceRequestAsync(client, path, "SaveProfile", "{}");
                            var localProfilePatched = await Task.Run(() =>
                                TrySetUniversal88ActiveTemplateProfile(candidateId, preferLandscape));
                            TraceUniversal88Apply(
                                $"SUCCESS via L-Connect candidate={candidateId}; SaveProfile={profileSaved}; " +
                                $"localProfilePatched={localProfilePatched}");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TraceUniversal88Apply($"L-Connect API phase failed: {ex.GetType().Name}: {ex.Message}", warning: true);
            }

            TraceUniversal88Apply("L-Connect API did not confirm selection; starting local profile fallback.", warning: true);
            foreach (var candidateId in candidates)
            {
                var existsForDevice = TemplateExistsForDevice(UniversalScreenDeviceModel, candidateId);
                var existsAsAlias = TemplateFileContainsAlias(templatePath, candidateId);
                TraceUniversal88Apply(
                    $"Fallback candidate={candidateId}; fileExists={existsForDevice}; aliasInTemplate={existsAsAlias}");
                if (!existsForDevice && !existsAsAlias)
                {
                    continue;
                }

                var patched = await Task.Run(() =>
                    TrySetUniversal88ActiveTemplateProfile(candidateId, preferLandscape));
                TraceUniversal88Apply($"Fallback profile patch candidate={candidateId}; patched={patched}");
                if (patched)
                {
                    var backgroundPatched = false;
                    if (!string.IsNullOrWhiteSpace(backgroundPath) && File.Exists(backgroundPath))
                    {
                        backgroundPatched = await Task.Run(() =>
                            TrySetUniversal88TemplateBackgroundProfile(candidateId, backgroundPath));
                    }

                    await ReloadInstalledTemplatesInLConnectAsync();
                    TraceUniversal88Apply(
                        $"PROFILE_FALLBACK_WRITTEN candidate={candidateId}; backgroundPatched={backgroundPatched}; " +
                        "deviceConfirmed=false",
                        warning: true);
                    return false;
                }
            }

            await ReloadInstalledTemplatesInLConnectAsync();
            TraceUniversal88Apply("FAILED: no L-Connect candidate was selected and local profile fallback did not apply.", warning: true);
            return false;
        }
        finally
        {
            stopwatch.Stop();
            TraceUniversal88Apply($"END elapsedMs={stopwatch.ElapsedMilliseconds}");
            _universal88ApplyTraceId = previousTraceId;
        }
    }

    private void TraceUniversal88Apply(string message, bool warning = false)
    {
        if (string.IsNullOrWhiteSpace(_universal88ApplyTraceId))
        {
            return;
        }

        var line = $"[8.8 APPLY {_universal88ApplyTraceId}] {message}";
        if (warning) AppLogger.Warning(line);
        else AppLogger.Info(line);
    }

    private static string DescribeFileForTrace(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "<none>";
        if (!File.Exists(path)) return $"{Path.GetFileName(path)} (missing)";
        try
        {
            var file = new FileInfo(path);
            return $"{file.Name} ({file.Length} bytes, utc={file.LastWriteTimeUtc:O})";
        }
        catch
        {
            return $"{Path.GetFileName(path)} (exists)";
        }
    }

    private static string DescribeControllerForTrace(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "<empty>";
        var normalized = path.Replace("\\\\", "\\");
        var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var hardwareId = parts.FirstOrDefault(part =>
            part.Contains("vid_", StringComparison.OrdinalIgnoreCase) &&
            part.Contains("pid_", StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(hardwareId)
            ? normalized.Length <= 80 ? normalized : normalized[..80]
            : hardwareId + "\\<device-id-redacted>";
    }

    private async Task<List<string>> GetRegisteredTemplateIdsContainedInFileAsync(string templatePath)
    {
        var registeredIds = new List<string>();
        if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
        {
            return registeredIds;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            foreach (var path in GetLConnectDevicePaths())
            {
                await SendLConnectDeviceRequestAsync(client, path, "ReloadAssets", "{}");
                foreach (var id in await GetLConnectTemplateIdsAsync(client, path))
                {
                    if (!registeredIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                    {
                        registeredIds.Add(id);
                    }
                }
            }
        }
        catch
        {
        }

        return ThemeInstallationService.MatchRegisteredIds(templatePath, registeredIds).ToList();
    }

    private static IEnumerable<string> GetActivationTemplateIdCandidates(
        string templateId,
        string templatePath,
        string backgroundPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string> { templateId };
        candidates.AddRange(GetTemplateInternalIds(templatePath));
        candidates.AddRange(new[]
                 {
                     Path.GetFileNameWithoutExtension(templatePath),
                     Path.GetFileNameWithoutExtension(backgroundPath)
                 });

        foreach (var candidate in candidates)
        {
            var sanitized = SanitizeFileName(candidate ?? "");
            if (!string.IsNullOrWhiteSpace(sanitized) && seen.Add(sanitized))
            {
                yield return sanitized;
            }
        }
    }

    private static IEnumerable<string> GetTemplateInternalIds(string templatePath)
    {
        return ThemeInstallationService.ExtractInternalIds(templatePath);
    }

    private async Task<bool> ApplyInstalledTemplateThroughLConnectAsync(
        string deviceModel,
        string templateId,
        string backgroundPath)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return false;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            foreach (var path in GetLConnectDevicePaths())
            {
                await SendLConnectDeviceRequestAsync(client, path, "ReloadAssets", "{}");
                var selectedTemplateId = await GetLConnectSelectedTemplateIdAsync(client, path);
                if (string.Equals(selectedTemplateId, templateId, StringComparison.OrdinalIgnoreCase))
                {
                    var fallbackTemplateId = (await GetLConnectTemplateIdsAsync(client, path))
                        .FirstOrDefault(id => !string.Equals(id, templateId, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(fallbackTemplateId))
                    {
                        await SendLConnectDeviceRequestAsync(
                            client,
                            path,
                            "ApplyTemplate",
                            JsonSerializer.Serialize(fallbackTemplateId));
                        await Task.Delay(200);
                    }
                }

                if (await SendLConnectDeviceRequestAsync(client, path, "ApplyTemplate", JsonSerializer.Serialize(templateId)))
                {
                    if (await WaitForSelectedTemplateAsync(client, path, templateId))
                    {
                        await CopyTemplateBackgroundAsync(client, path, deviceModel, templateId, backgroundPath);
                        await SendLConnectDeviceRequestAsync(client, path, "SaveProfile", "{}");
                        return true;
                    }
                }

                if (await SendLConnectDeviceRequestAsync(client, path, "SetTemplate", JsonSerializer.Serialize(templateId)))
                {
                    if (await WaitForSelectedTemplateAsync(client, path, templateId))
                    {
                        await CopyTemplateBackgroundAsync(client, path, deviceModel, templateId, backgroundPath);
                        await SendLConnectDeviceRequestAsync(client, path, "SaveProfile", "{}");
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
        }

        return false;
    }

    private async Task<bool> WaitForSelectedTemplateAsync(
        HttpClient client,
        string devicePath,
        string expectedTemplateId)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(160);
            }

            var selectedId = await GetLConnectSelectedTemplateIdAsync(client, devicePath);
            TraceUniversal88Apply(
                $"Selection poll attempt={attempt + 1}/5; expected={expectedTemplateId}; " +
                $"observed={(string.IsNullOrWhiteSpace(selectedId) ? "<empty>" : selectedId)}");
            if (string.Equals(selectedId, expectedTemplateId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<string> GetLConnectSelectedTemplateIdAsync(HttpClient client, string devicePath)
    {
        var json = await SendLConnectDeviceRequestForJsonAsync(client, devicePath, "GetSelectedTemplateId", "{}");
        if (string.IsNullOrWhiteSpace(json))
        {
            return "";
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Success", out var success) &&
                success.ValueKind == JsonValueKind.True &&
                doc.RootElement.TryGetProperty("Data", out var data) &&
                data.ValueKind == JsonValueKind.String)
            {
                return data.GetString() ?? "";
            }
        }
        catch
        {
        }

        return "";
    }

    private static bool IsLConnectImportedTemplateId(string candidateId, string baseId)
    {
        if (string.IsNullOrWhiteSpace(candidateId) || string.IsNullOrWhiteSpace(baseId) ||
            !candidateId.StartsWith(baseId + "_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = candidateId[(baseId.Length + 1)..];
        return DateTime.TryParseExact(
            suffix,
            "yyyyMMdd_HHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    private async Task<string> TryGetActiveTemplateIdFromLConnectAsync(string deviceModel)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            foreach (var path in GetLConnectDevicePaths())
            {
                var templateId = await GetLConnectSelectedTemplateIdAsync(client, path);
                if (!string.IsNullOrWhiteSpace(ResolveTemplatePathByIdOrAlias(deviceModel, templateId)))
                {
                    return templateId;
                }
            }
        }
        catch
        {
        }

        return "";
    }

    private async Task<bool> ReloadInstalledTemplatesInLConnectAsync()
    {
        var accepted = false;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            foreach (var path in GetLConnectDevicePaths())
            {
                accepted |= await SendLConnectDeviceRequestAsync(client, path, "ReloadAssets", "{}");
            }
        }
        catch
        {
        }

        return accepted;
    }

    private async Task<string> ApplyTemplateThroughLConnectAsync(
        string deviceModel,
        string templateId,
        string templatePath,
        string backgroundPath)
    {
        if (string.IsNullOrWhiteSpace(templateId) || string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
        {
            return "";
        }

        var importZip = "";
        try
        {
            importZip = CreateLConnectImportZip(deviceModel, templateId, templatePath);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            foreach (var path in GetLConnectDevicePaths())
            {
                var importedId = await ImportTemplateIntoLConnectAsync(client, path, importZip);
                var candidateIds = new[] { importedId }
                    .Concat(GetActivationTemplateIdCandidates(templateId, templatePath, backgroundPath))
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var candidateId in candidateIds)
                {
                    if (await SendLConnectDeviceRequestAsync(client, path, "ApplyTemplate", JsonSerializer.Serialize(candidateId)))
                    {
                        if (await WaitForSelectedTemplateAsync(client, path, candidateId))
                        {
                            await CopyTemplateBackgroundAsync(client, path, deviceModel, candidateId, backgroundPath);
                            await SendLConnectDeviceRequestAsync(client, path, "SaveProfile", "{}");
                            return candidateId;
                        }
                    }
                }
            }
        }
        catch
        {
        }
        finally
        {
            TryDeleteFile(importZip);
        }

        return "";
    }

    private async Task<string> ImportTemplateIntoLConnectAsync(HttpClient client, string devicePath, string importZip)
    {
        await SendLConnectDeviceRequestAsync(client, devicePath, "ReloadAssets", "{}");
        var beforeIds = await GetLConnectTemplateIdsAsync(client, devicePath);
        if (!await SendLConnectDeviceRequestAsync(client, devicePath, "ImportTemplate", JsonSerializer.Serialize(importZip), requireDataSuccess: true))
        {
            return "";
        }

        await SendLConnectDeviceRequestAsync(client, devicePath, "ReloadAssets", "{}");
        var afterIds = await GetLConnectTemplateIdsAsync(client, devicePath);
        var beforeLookup = new HashSet<string>(beforeIds, StringComparer.OrdinalIgnoreCase);
        var newId = afterIds.FirstOrDefault(id => !beforeLookup.Contains(id));
        if (!string.IsNullOrWhiteSpace(newId))
        {
            return newId;
        }

        return "";
    }

    private async Task<List<string>> GetLConnectTemplateIdsAsync(HttpClient client, string devicePath)
    {
        var ids = new List<string>();
        var json = await SendLConnectDeviceRequestForJsonAsync(client, devicePath, "GetTemplates", "{}");
        if (string.IsNullOrWhiteSpace(json))
        {
            return ids;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Success", out var success) &&
                success.ValueKind == JsonValueKind.True &&
                doc.RootElement.TryGetProperty("Data", out var data) &&
                data.ValueKind == JsonValueKind.Array &&
                data.GetArrayLength() > 0)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("Id", out var id))
                    {
                        var value = id.GetString();
                        if (!string.IsNullOrWhiteSpace(value) &&
                            !ids.Contains(value, StringComparer.OrdinalIgnoreCase))
                        {
                            ids.Add(value);
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return ids;
    }

    private async Task CopyTemplateBackgroundAsync(
        HttpClient client,
        string devicePath,
        string deviceModel,
        string targetTemplateId,
        string backgroundPath)
    {
        if (string.IsNullOrWhiteSpace(backgroundPath) || !File.Exists(backgroundPath))
        {
            TraceUniversal88Apply("Background update skipped because no readable background file was available.");
            return;
        }

        var accepted = await SendLConnectDeviceRequestAsync(
            client,
            devicePath,
            "ChangeTemplateBackground",
            JsonSerializer.Serialize(new
            {
                Id = targetTemplateId,
                TemplateId = targetTemplateId,
                ScreenType = 0,
                Path = backgroundPath
            }));
        await SendLConnectDeviceRequestAsync(client, devicePath, "SaveProfile", "{}");
        TraceUniversal88Apply(
            $"Background request candidate={targetTemplateId}; accepted={accepted}; " +
            $"file={DescribeFileForTrace(backgroundPath)}");
        if (accepted)
        {
            var profilePatched = await Task.Run(() =>
                string.Equals(deviceModel, UniversalScreenDeviceModel, StringComparison.OrdinalIgnoreCase)
                    ? TrySetUniversal88TemplateBackgroundProfile(targetTemplateId, backgroundPath) ||
                      TrySetTemplateBackgroundProfile(targetTemplateId, backgroundPath, deviceModel)
                    : TrySetTemplateBackgroundProfile(targetTemplateId, backgroundPath, deviceModel));
            TraceUniversal88Apply($"Background local profile patch={profilePatched}");
        }
    }

    private async Task<bool> SendLConnectDeviceRequestAsync(
        HttpClient client,
        string devicePath,
        string type,
        string body,
        bool requireDataSuccess = false)
    {
        var json = await SendLConnectDeviceRequestForJsonAsync(client, devicePath, type, body);
        var successful = IsSuccessfulLConnectResponse(json, requireDataSuccess);
        TraceUniversal88Apply(
            $"L-Connect action={type}; controller={DescribeControllerForTrace(devicePath)}; " +
            $"success={successful}; response={DescribeLConnectResponseForTrace(json, type)}");
        return successful;
    }

    private async Task<string> SendLConnectDeviceRequestForJsonAsync(
        HttpClient client,
        string devicePath,
        string type,
        string body)
    {
        var result = await _lConnectClient.SendDeviceRequestForJsonAsync(client, devicePath, type, body);
        TraceUniversal88Apply(
            $"L-Connect HTTP action={type}; controller={DescribeControllerForTrace(devicePath)}; " +
            $"port={(result.Port?.ToString(CultureInfo.InvariantCulture) ?? "<none>")}; mode={result.RequestMode}; " +
            $"status={(result.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "<none>")}; " +
            $"reason={(string.IsNullOrWhiteSpace(result.ReasonPhrase) ? "<none>" : result.ReasonPhrase)}; " +
            $"body={DescribeLConnectResponseForTrace(result.Body, type)}; " +
            $"error={(string.IsNullOrWhiteSpace(result.Error) ? "<none>" : result.Error)}");
        return result.IsHttpSuccess ? result.Body : "";
    }

    private static string DescribeLConnectResponseForTrace(string json, string action)
    {
        if (string.IsNullOrWhiteSpace(json)) return "<empty>";
        if (action.Equals("GetTemplates", StringComparison.OrdinalIgnoreCase))
        {
            return $"<parsed separately; {json.Length} chars>";
        }

        var singleLine = Regex.Replace(json, @"\s+", " ").Trim();
        return singleLine.Length <= 500 ? singleLine : singleLine[..500] + "...";
    }

    private static bool IsSuccessfulLConnectResponse(string json, bool requireDataSuccess = false)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Success", out var success) &&
                success.ValueKind != JsonValueKind.True)
            {
                return false;
            }

            if (requireDataSuccess &&
                doc.RootElement.TryGetProperty("Data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("Success", out var dataSuccess) &&
                dataSuccess.ValueKind != JsonValueKind.True)
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAcceptedLConnectRefreshResponse(string json)
    {
        return string.IsNullOrWhiteSpace(json) || IsSuccessfulLConnectResponse(json);
    }

    private string CreateLConnectImportZip(string deviceModel, string templateId, string templatePath)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"lconnect_import_{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(templatePath, Path.GetFileName(templatePath), CompressionLevel.Optimal);

        foreach (var mediaPath in GetTemplateMediaFilesForImport(deviceModel, templateId))
        {
            try
            {
                archive.CreateEntryFromFile(mediaPath, Path.GetFileName(mediaPath), CompressionLevel.Optimal);
            }
            catch
            {
            }
        }

        return zipPath;
    }

    private static string CreateLConnectImportZipFromFiles(string templatePath, IEnumerable<string> mediaPaths)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"lconnect_import_{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(templatePath, Path.GetFileName(templatePath), CompressionLevel.Optimal);
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFileName(templatePath)
        };

        foreach (var mediaPath in mediaPaths)
        {
            if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
            {
                continue;
            }

            var entryName = Path.GetFileName(mediaPath);
            if (string.IsNullOrWhiteSpace(entryName) || !added.Add(entryName))
            {
                continue;
            }

            try
            {
                archive.CreateEntryFromFile(mediaPath, entryName, CompressionLevel.Optimal);
            }
            catch
            {
            }
        }

        return zipPath;
    }

    private static IEnumerable<string> GetBackgroundMediaBundleFiles(string backgroundPath)
    {
        if (string.IsNullOrWhiteSpace(backgroundPath))
        {
            yield break;
        }

        var directory = Path.GetDirectoryName(backgroundPath);
        var baseName = Path.Combine(directory ?? "", Path.GetFileNameWithoutExtension(backgroundPath));
        foreach (var path in new[]
                 {
                     backgroundPath,
                     baseName + ".h264",
                     baseName + ".mp4",
                     baseName + ".png"
                 })
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                yield return path;
            }
        }
    }

    private IEnumerable<string> GetTemplateMediaFilesForImport(string deviceModel, string templateId)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new[]
        {
            Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", deviceModel, "image"),
            Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", deviceModel, "video"),
            Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", "uploaded")
        };

        foreach (var layer in Layers)
        {
            var fileName = Path.GetFileName(layer.Media);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            foreach (var root in roots)
            {
                var path = Path.Combine(root, fileName);
                if (File.Exists(path) && seen.Add(path))
                {
                    yield return path;
                }
            }
        }

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(root, templateId + "-*"))
            {
                if (seen.Add(path))
                {
                    yield return path;
                }
            }
        }
    }

    private static bool TrySetActiveTemplateProfile(string templateId, string deviceModel)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return false;
        }

        var profileDir = Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", "profile");
        if (!Directory.Exists(profileDir))
        {
            return false;
        }

        var fallback = default((string File, JsonObject Root, bool Gzip)?);
        foreach (var file in Directory.GetFiles(profileDir).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var (json, gzip) = ReadLConnectProfileJson(file);
                if (json is not JsonObject root ||
                    !root.ContainsKey("SelectedTemplateId"))
                {
                    continue;
                }

                var current = root["SelectedTemplateId"]?.GetValue<string>();
                fallback ??= (file, root, gzip);
                if (string.Equals(current, templateId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (TemplateExistsForDevice(deviceModel, current))
                {
                    SetSelectedTemplateId(file, root, gzip, templateId);
                    return true;
                }
            }
            catch
            {
            }
        }

        if (fallback is { } target)
        {
            SetSelectedTemplateId(target.File, target.Root, target.Gzip, templateId);
            return true;
        }

        return false;
    }

    private static bool TrySetTemplateBackgroundProfile(string templateId, string backgroundPath, string deviceModel)
    {
        if (string.IsNullOrWhiteSpace(templateId) ||
            string.IsNullOrWhiteSpace(backgroundPath) ||
            string.IsNullOrWhiteSpace(deviceModel))
        {
            return false;
        }

        var profileDir = Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", "profile");
        if (!Directory.Exists(profileDir))
        {
            return false;
        }

        foreach (var file in Directory.GetFiles(profileDir).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var (json, gzip) = ReadLConnectProfileJson(file);
                if (json is not JsonObject root ||
                    !root.ContainsKey("SelectedTemplateId") ||
                    !TemplateExistsForDevice(deviceModel, root["SelectedTemplateId"]?.GetValue<string>()))
                {
                    continue;
                }

                if (root["TemplateCustomBackgrounds"] is not JsonObject backgrounds)
                {
                    backgrounds = new JsonObject();
                    root["TemplateCustomBackgrounds"] = backgrounds;
                }

                backgrounds[templateId] = backgroundPath;
                BackupLConnectProfile(file);
                WriteLConnectProfileJson(file, root, gzip);
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool TemplateExistsForDevice(string deviceModel, string? templateId)
    {
        if (string.IsNullOrWhiteSpace(deviceModel) || string.IsNullOrWhiteSpace(templateId))
        {
            return false;
        }

        return File.Exists(Path.Combine(GetTemplateRoot(deviceModel), templateId + ".template")) ||
               File.Exists(Path.Combine(
                   @"C:\Program Files\Lian-Li\L-Connect 3",
                   "Assets",
                   deviceModel,
                   "template",
                   templateId + ".template"));
    }

    private static string ResolveTemplatePathByIdOrAlias(string deviceModel, string? templateId)
    {
        return ThemeInstallationService.ResolveTemplatePath(deviceModel, templateId);
    }

    private static bool TrySetUniversal88ActiveTemplateProfile(string templateId, bool preferLandscape)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return false;
        }

        var profileDir = Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", "profile");
        if (!Directory.Exists(profileDir))
        {
            return false;
        }

        foreach (var file in Directory.GetFiles(profileDir).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var (json, gzip) = ReadLConnectProfileJson(file);
                if (json is not JsonObject root ||
                    (!root.ContainsKey("LandscapeTemplateConfig") &&
                     !root.ContainsKey("PortraitTemplateConfig") &&
                     !root.ContainsKey("IsLandscape")))
                {
                    continue;
                }

                var changed = false;
                if (preferLandscape)
                {
                    changed |= PatchUniversal88TemplateConfig(root, "LandscapeTemplateConfig", templateId);
                    changed |= PatchUniversal88TemplateConfig(root, "PortraitTemplateConfig", templateId);
                }
                else
                {
                    changed |= PatchUniversal88TemplateConfig(root, "PortraitTemplateConfig", templateId);
                    changed |= PatchUniversal88TemplateConfig(root, "LandscapeTemplateConfig", templateId);
                }

                if (!changed)
                {
                    continue;
                }

                BackupLConnectProfile(file);
                WriteLConnectProfileJson(file, root, gzip);
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool PatchUniversal88TemplateConfig(JsonObject root, string propertyName, string templateId)
    {
        if (root[propertyName] is not JsonObject config)
        {
            config = new JsonObject();
            root[propertyName] = config;
        }

        var previous = config["SelectedTemplateId"]?.GetValue<string>();
        config["SelectedTemplateId"] = templateId;
        config["IsCustomThemeEnabled"] = false;
        return !string.Equals(previous, templateId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrySetUniversal88TemplateBackgroundProfile(string templateId, string backgroundPath)
    {
        if (string.IsNullOrWhiteSpace(templateId) ||
            string.IsNullOrWhiteSpace(backgroundPath) ||
            !File.Exists(backgroundPath))
        {
            return false;
        }

        var profileDir = Path.Combine(@"C:\ProgramData\Lian-Li\L-Connect 3", "profile");
        if (!Directory.Exists(profileDir))
        {
            return false;
        }

        foreach (var file in Directory.GetFiles(profileDir).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var (json, gzip) = ReadLConnectProfileJson(file);
                if (json is not JsonObject root ||
                    (!root.ContainsKey("TemplateCustomBackgrounds") &&
                     !root.ContainsKey("AllTemplateAssignedCategories") &&
                     !root.ContainsKey("TemplateCategories") &&
                     !root.ContainsKey("LandscapeTemplateConfig") &&
                     !root.ContainsKey("PortraitTemplateConfig") &&
                     !root.ContainsKey("IsLandscape")))
                {
                    continue;
                }

                if (root["TemplateCustomBackgrounds"] is not JsonObject backgrounds)
                {
                    backgrounds = new JsonObject();
                    root["TemplateCustomBackgrounds"] = backgrounds;
                }

                backgrounds[templateId] = backgroundPath;
                BackupLConnectProfile(file);
                WriteLConnectProfileJson(file, root, gzip);
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool TemplateFileContainsAlias(string templatePath, string alias)
    {
        if (string.IsNullOrWhiteSpace(templatePath) ||
            string.IsNullOrWhiteSpace(alias) ||
            !File.Exists(templatePath))
        {
            return false;
        }

        try
        {
            var bytes = File.ReadAllBytes(templatePath);
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            return text.Contains(alias, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void SetSelectedTemplateId(string file, JsonObject root, bool gzip, string templateId)
    {
        root["SelectedTemplateId"] = templateId;
        BackupLConnectProfile(file);
        WriteLConnectProfileJson(file, root, gzip);
    }

    private static (JsonNode? Json, bool Gzip) ReadLConnectProfileJson(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var gzip = bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b;
        string text;
        if (gzip)
        {
            using var source = new MemoryStream(bytes);
            using var gzipStream = new GZipStream(source, CompressionMode.Decompress);
            using var reader = new StreamReader(gzipStream, System.Text.Encoding.UTF8);
            text = reader.ReadToEnd();
        }
        else
        {
            text = System.Text.Encoding.UTF8.GetString(bytes);
        }

        return (JsonNode.Parse(text), gzip);
    }

    private static void WriteLConnectProfileJson(string path, JsonNode json, bool gzip)
    {
        var text = json.ToJsonString();
        if (!gzip)
        {
            File.WriteAllText(path, text, System.Text.Encoding.UTF8);
            return;
        }

        using var file = File.Create(path);
        using var gzipStream = new GZipStream(file, CompressionMode.Compress);
        using var writer = new StreamWriter(gzipStream, new System.Text.UTF8Encoding(false));
        writer.Write(text);
    }

    private static void BackupLConnectProfile(string path)
    {
        var backup = $"{path}.theme-editor-backup";
        if (!File.Exists(backup))
        {
            File.Copy(path, backup, false);
        }
    }

    private async Task<bool> TriggerLConnectRefreshAsync(bool skipUniversalPreviewUpdate = false, bool fastApply = false)
    {
        var accepted = false;
        var selectedDeviceModel = GetSelectedDeviceModel();
        var selectedTemplateId = _currentTemplateId;

        // Keep the profile and the explicit API requests pointed at the same template.
        // Otherwise an early ApplyAll request can make L-Connect re-apply the theme that
        // was active before the user selected a different item in the editor.
        if (!IsUniversalScreenSelected() && !string.IsNullOrWhiteSpace(selectedTemplateId))
        {
            await Task.Run(() => TrySetActiveTemplateProfile(
                selectedTemplateId,
                selectedDeviceModel));
        }
        
        if (_backgroundDirty && !string.IsNullOrWhiteSpace(_currentTemplateId))
        {
            accepted = await TriggerLConnectBackgroundChangeAsync();
            if (accepted) _backgroundDirty = false;
        }

        if (fastApply)
        {
            return await TriggerFastLConnectRefreshAsync(
                selectedDeviceModel,
                selectedTemplateId,
                _currentBackgroundPath);
        }

        if (IsUniversalScreenSelected() && !string.IsNullOrWhiteSpace(_currentTemplateId))
        {
            accepted |= await ActivateUniversal88ThemeAsync(
                _currentTemplateId,
                _currentTemplatePath,
                _currentBackgroundPath,
                updatePreview: !skipUniversalPreviewUpdate);
        }

        var devicePaths = GetLConnectDevicePaths();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        var templateCandidates = ThemeInstallationService
            .BuildActivationCandidates(
                selectedTemplateId,
                _currentTemplatePath,
                _currentBackgroundPath)
            .ToList();

        foreach (var path in devicePaths)
        {
            // The template file may have changed without its id changing. Force L-Connect
            // to drop its cached asset first, then re-apply the active id below.
            accepted |= await SendLConnectDeviceRequestAsync(client, path, "ReloadAssets", "{}");
            var registeredIds = await GetLConnectTemplateIdsAsync(client, path);
            var liveTemplateCandidates = ThemeInstallationService
                .MatchRegisteredIds(_currentTemplatePath, registeredIds)
                .Concat(templateCandidates.Where(id => registeredIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
                .Concat(templateCandidates)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var pathApplied = false;
            foreach (var type in new[] { "ApplyTemplate", "SetTemplate", "ChangeTemplate" })
            {
                if (pathApplied)
                {
                    break;
                }

                foreach (var candidateId in liveTemplateCandidates)
                {
                    if (pathApplied)
                    {
                        break;
                    }

                    var candidateJson = JsonSerializer.Serialize(candidateId);
                    try
                    {
                        var responseJson = await SendLConnectDeviceRequestForJsonAsync(client, path, type, candidateJson);
                        if (IsAcceptedLConnectRefreshResponse(responseJson))
                        {
                            accepted = true;
                            if (await WaitForSelectedTemplateAsync(client, path, candidateId))
                            {
                                await SendLConnectDeviceRequestAsync(client, path, "StopVideo", "{}");
                                await SendLConnectDeviceRequestAsync(client, path, "Apply2DTemplate", candidateJson);
                                accepted |= await SendLConnectDeviceRequestAsync(client, path, "SaveProfile", "{}");
                                accepted |= await SendLConnectDeviceRequestAsync(client, path, "ApplyScreenContent", "{}");
                                await Task.Run(() => TrySetActiveTemplateProfile(candidateId, selectedDeviceModel));
                                pathApplied = true;
                                break;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        if (!accepted && !IsUniversalScreenSelected() && !string.IsNullOrWhiteSpace(selectedTemplateId))
        {
            accepted |= await Task.Run(() => TrySetActiveTemplateProfile(
                selectedTemplateId,
                selectedDeviceModel));
        }

        return accepted;
    }

    private async Task<bool> TriggerFastLConnectRefreshAsync(
        string deviceModel,
        string templateId,
        string backgroundPath)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return false;
        }

        var isUniversalScreen = IsUniversalScreenSelected();
        var preferLandscape = isUniversalScreen && IsUniversalLandscape();
        var profilePatched = isUniversalScreen
            ? await Task.Run(() =>
                TrySetUniversal88ActiveTemplateProfile(templateId, preferLandscape) &&
                (string.IsNullOrWhiteSpace(backgroundPath) ||
                 !File.Exists(backgroundPath) ||
                 TrySetUniversal88TemplateBackgroundProfile(templateId, backgroundPath)))
            : await Task.Run(() => TrySetActiveTemplateProfile(templateId, deviceModel));

        var accepted = profilePatched;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var candidateJson = JsonSerializer.Serialize(templateId);
        foreach (var path in GetLConnectDevicePaths())
        {
            try
            {
                accepted |= await SendLConnectDeviceRequestAsync(client, path, "ApplyTemplate", candidateJson);
                accepted |= await SendLConnectDeviceRequestAsync(client, path, "Apply2DTemplate", candidateJson);
                accepted |= await SendLConnectDeviceRequestAsync(client, path, "SaveProfile", "{}");
            }
            catch
            {
            }
        }

        return accepted;
    }

    private async Task<bool> TriggerLConnectBackgroundChangeAsync(
        string? backgroundPathOverride = null,
        string? templateIdOverride = null)
    {
        var templateId = string.IsNullOrWhiteSpace(templateIdOverride)
            ? _currentTemplateId
            : templateIdOverride;
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return false;
        }

        var bgPath = !string.IsNullOrWhiteSpace(backgroundPathOverride)
            ? backgroundPathOverride
            : !string.IsNullOrWhiteSpace(_currentBackgroundPath)
            ? _currentBackgroundPath
            : Layers.FirstOrDefault(layer => string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))?.Media ?? "";
        if (string.IsNullOrWhiteSpace(bgPath))
        {
            return false;
        }

        var bodyObj = new
        {
            Id = templateId,
            TemplateId = templateId,
            ScreenType = 0,
            Path = bgPath
        };
        var jsonBody = JsonSerializer.Serialize(bodyObj);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        var accepted = false;
        foreach (var path in GetLConnectDevicePaths())
        {
            try
            {
                accepted |= await SendLConnectDeviceRequestAsync(client, path, "ChangeTemplateBackground", jsonBody);
            }
            catch
            {
            }
        }

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
                      type.Equals("GraphLine", StringComparison.OrdinalIgnoreCase) ||
                      type.Equals("GraphSensor", StringComparison.OrdinalIgnoreCase);
        var isSensor = type.Equals("GraphSensor", StringComparison.OrdinalIgnoreCase);
        var isClock = type.Equals("GraphClock", StringComparison.OrdinalIgnoreCase);

        if (isClock)
        {
            layer.DataSource = GetComboText(DataCombo);
            layer.Format = SupportsFormat(layer.DataSource) && string.IsNullOrWhiteSpace(FormatBox.Text)
                ? (layer.DataSource.Equals("TIME", StringComparison.OrdinalIgnoreCase) ? "h_12" : DefaultFormatForDataSource(layer.DataSource))
                : FormatBox.Text;
        }

        if (isText)
        {
            if (layer.CanWriteFont("size")) layer.Size = SizeBox.Text;
            if (layer.CanWriteFont("color")) layer.Color = ColorBox.Text;
            if (layer.CanWriteFont("name")) layer.Font = ResolveCanonicalFontName(GetComboText(FontCombo));
            layer.AlignmentIndex = (AlignmentCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? layer.AlignmentIndex;
            if (layer.CanWriteFont("interval")) layer.FontInterval = FontIntervalBox.Text;
            if (layer.CanWriteFont("GrColor")) layer.FontGradientColor = TextGradientColorBox.Text;
            if (layer.CanWriteFont("GrDirection"))
            {
                layer.FontGradientDirection = (TextGradientDirectionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "0";
            }
            layer.DataSource = SetTextCheck.IsChecked == true ? "StaticText" : GetComboText(DataCombo);
            layer.Format = SupportsFormat(layer.DataSource) && string.IsNullOrWhiteSpace(FormatBox.Text)
                ? DefaultFormatForDataSource(layer.DataSource)
                : FormatBox.Text;
            if (layer.CanWriteFont("isBold")) layer.Bold = BoldCheck.IsChecked == true ? "True" : "False";
            if (layer.CanWriteFont("IsItalic")) layer.Italic = ItalicCheck.IsChecked == true ? "True" : "False";
            layer.ForceText = SetTextCheck.IsChecked == true;
            if (layer.DataSource == "StaticText" || SetTextCheck.IsChecked == true)
            {
                layer.Text = NormalizeLConnectText(TextBox.Text);
            }
        }

        if (isGraph && GraphEditPanel.Visibility == Visibility.Visible)
        {
            if (isSensor)
            {
                layer.SensorType = GetComboText(DataCombo);
                layer.DataSource = SensorDataSourceFromType(layer.SensorType);
                layer.SensorStyle = GetComboText(GraphSubTypeNameBox);
                layer.SubTypeName = layer.SensorStyle;
                layer.TypeName = "Sensor";
                layer.SensorColor1 = FrontColorBox.Text;
                layer.SensorColor2 = SensorRingEndColorBox.Text;
                layer.SensorBgColor = BackColorBox.Text;
                layer.SensorMainFontColor = ColorBox.Text;
                layer.SensorTopFontColor = SensorTopColorBox.Text;
                layer.SensorBottomFontColor = SensorBottomColorBox.Text;
                layer.SensorFontFamily = ResolveCanonicalFontName(GetComboText(FontCombo));
                layer.ZoomRate = TryParseZoom(ZoomBox.Text, out var sensorZoom)
                    ? FormatZoom(Math.Clamp(sensorZoom, 0.01, 10.0))
                    : "0.5";
                layer.SensorZoomRate = layer.ZoomRate;
                layer.Text = string.IsNullOrWhiteSpace(TextBox.Text)
                    ? SampleValueFor(layer.DataSource)
                    : NormalizeLConnectText(TextBox.Text);
                return;
            }

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
            if (layer.CanWrite("direction"))
            {
                var directionSource = UseGradientCheck.IsChecked == true &&
                                      GraphGradientDirectionCombo.Visibility == Visibility.Visible
                    ? GraphGradientDirectionCombo
                    : GraphDirectionCombo;
                layer.Direction = (directionSource.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? layer.Direction;
            }
            if (layer.CanWrite("lineWidth")) layer.LineWidth = GraphLineWidthBox.Text;
            if (layer.CanWrite("columnWidth")) layer.ColumnWidth = GraphColumnWidthBox.Text;
            if (layer.CanWrite("borderWidth")) layer.BorderWidth = GraphBorderWidthBox.Text;
            if (layer.CanWrite("InnerCircleRadius")) layer.InnerCircleRadius = GraphInnerCircleRadiusBox.Text;
            if (layer.CanWrite("SplitBlockWidth")) layer.SplitBlockWidth = GraphSplitBlockWidthBox.Text;
            if (layer.CanWrite("SplitBlankWidth")) layer.SplitBlankWidth = GraphSplitBlankWidthBox.Text;
            if (layer.CanWrite("useSubsection")) layer.UseSubsection = GraphUseSubsectionCheck.IsChecked == true ? "True" : "False";
            if (layer.CanWrite("fillBack")) layer.FillBack = GraphFillBackCheck.IsChecked == true ? "True" : "False";
            if (layer.CanWrite("revert")) layer.Revert = GraphRevertCheck.IsChecked == true ? "True" : "False";
            if (layer.CanWrite("FrontAlpha")) layer.FrontAlpha = FrontAlphaBox.Text;
            if (layer.CanWrite("BackAlpha")) layer.BackAlpha = BackAlphaBox.Text;
            if (layer.CanWrite("LineColor")) layer.LineColor = FrontColorBox.Text;
            if (layer.CanWrite("FillColor"))
            {
                layer.FillColor = ChartFillColorBox.Text;
                layer.Transparent = ChartTransparentBox.Text;
            }
            if (layer.CanWrite("BorderColor")) layer.BorderColor = BackColorBox.Text;
            if (layer.CanWrite("trBack")) layer.TransparentBackground = TransparentBackgroundCheck.IsChecked == true ? "True" : "False";
            if (layer.CanWrite("maxValue")) layer.MaxValue = MaxValueBox.Text;
            if (layer.CanWrite("rollDirection")) layer.InvertDirection = InvertDirectionCheck.IsChecked == true ? "True" : "False";
            if (layer.CanWrite("startPer")) layer.StartPercentage = StartPercentageBox.Text;
            if (layer.CanWrite("totalAngel")) layer.TotalAngle = TotalAngleBox.Text;
            if (layer.CanWrite("useBlock")) layer.UseBlock = UseBlockCheck.IsChecked == true ? "True" : "False";
            if (layer.CanWrite("HasRingBorder")) layer.RingBorder = RingBorderCheck.IsChecked == true ? "True" : "False";
            if (layer.CanWrite("round")) layer.Round = RoundCheck.IsChecked == true ? "True" : "False";
        }

        if (ImageEditPanel.Visibility == Visibility.Visible)
        {
            layer.Media = ImageFileBox.Text;
            if (!string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))
            {
                layer.ZoomRate = TryParseZoom(ZoomBox.Text, out var zoom)
                    ? FormatZoom(Math.Clamp(zoom, 0.01, 10.0))
                    : "1";
            }
            if (ImageRotateCombo.Visibility == Visibility.Visible)
            {
                var selectedDegrees = int.TryParse(
                    (ImageRotateCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
                    out var degrees) ? degrees : 0;
                if (string.Equals(layer.Type, "GraphClock", StringComparison.OrdinalIgnoreCase))
                {
                    layer.ClockAngle = selectedDegrees.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    layer.Rotate = string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase)
                        ? (selectedDegrees / 90).ToString(CultureInfo.InvariantCulture)
                        : selectedDegrees.ToString(CultureInfo.InvariantCulture);
                }
            }
            if (layer.CanWrite("rect")) layer.Rect = ImageRectBox.Text;
            if (string.Equals(layer.Type, "GraphClock", StringComparison.OrdinalIgnoreCase))
            {
                layer.ClockCenterX = ClockCenterXBox.Text;
                layer.ClockCenterY = ClockCenterYBox.Text;
                layer.ClockAngle = ClockStartAngleBox.Text;
                layer.ClockEndAngle = ClockTotalAngleBox.Text;
                layer.ClockOffset = ClockOffsetBox.Text;
                layer.ClockOriginX = ClockOriginXBox.Text;
                layer.ClockOriginY = ClockOriginYBox.Text;
                layer.ClockMoveOrigin = ClockMoveOriginCheck.IsChecked == true ? "True" : "False";
                layer.Revert = ClockRevertCheck.IsChecked == true ? "True" : "False";
            }
        }
    }

    private void SetBusy(bool isBusy, string status)
    {
        var directApplySupported = CanDirectApplySelectedDevice();
        ActiveThemeButton.IsEnabled = !isBusy;
        LoadButton.IsEnabled = !isBusy;
        OfflineModeCheck.IsEnabled = !isBusy;
        SaveButton.IsEnabled = !isBusy && directApplySupported;
        BackupButton.IsEnabled = !isBusy;
        RestoreBackupButton.IsEnabled = !isBusy;
        ApplyButton.IsEnabled = !isBusy && directApplySupported;
        RemoveButton.IsEnabled = !isBusy;
        MoveUpButton.IsEnabled = !isBusy;
        MoveDownButton.IsEnabled = !isBusy;
        DuplicateButton.IsEnabled = !isBusy &&
                                    LayerGrid.SelectedItem is LayerRow selected &&
                                    !string.Equals(selected.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase);
        AddTextButton.IsEnabled = !isBusy;
        AddDataButton.IsEnabled = !isBusy;
        AddImageButton.IsEnabled = !isBusy;
        AddGraphButton.IsEnabled = !isBusy;
        BackgroundButton.IsEnabled = !isBusy;
        ApplyAllButton.IsEnabled = !isBusy && directApplySupported;
        RestartButton.IsEnabled = !isBusy;
        ExportLConnectButton.IsEnabled = !isBusy;
        Convert88To92Button.IsEnabled = !isBusy && IsVm92Selected();
        StatusText.Text = status;
    }

    private void SetApplyProgress(double value, string status)
    {
        ApplyProgressBar.Visibility = Visibility.Visible;
        ApplyProgressBar.Value = Math.Clamp(value, 0, 100);
        StatusText.Text = status;
    }

    private void HideApplyProgress()
    {
        ApplyProgressBar.Value = 0;
        ApplyProgressBar.Visibility = Visibility.Collapsed;
    }

    private void SetStatus(string status)
    {
        StatusText.Text = status;
    }

    private void StartResize(LayerRow layer, Point previewPoint)
    {
        if (layer.IsLocked)
        {
            return;
        }

        PushUndoState(GetLanguageText("history.resize", "Resize layer"));
        _editorUndoArmed = true;
        _isResizingPreview = true;
        _dragLayer = layer;
        _resizeStartTemplatePoint = new Point(ToTemplate(previewPoint.X), ToTemplate(previewPoint.Y));

        double.TryParse(layer.Width, out _resizeStartWidth);
        double.TryParse(layer.Height, out _resizeStartHeight);
        double.TryParse(layer.ColumnWidth, out _resizeStartColumnWidth);
        double.TryParse(layer.Diameter, out _resizeStartDiameter);
        double.TryParse(layer.Size, out _resizeStartSize);
        if (string.Equals(layer.Type, "GraphSensor", StringComparison.OrdinalIgnoreCase))
        {
            _resizeStartZoom = GetSensorZoomRate(layer);
        }
        else if (TryParseZoom(layer.ZoomRate, out _resizeStartZoom))
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
        SensorRingEndColorBox.TextChanged += (s, e) => OnInputChanged();
        BackColorBox.TextChanged += (s, e) => OnInputChanged();
        GradientColorBox.TextChanged += (s, e) => OnInputChanged();
        SensorTopColorBox.TextChanged += (s, e) => OnInputChanged();
        SensorBottomColorBox.TextChanged += (s, e) => OnInputChanged();
        TextGradientColorBox.TextChanged += (s, e) => OnInputChanged();
        TextGradientDirectionCombo.SelectionChanged += (s, e) => OnInputChanged();
        FrontAlphaBox.TextChanged += (s, e) => OnInputChanged();
        BackAlphaBox.TextChanged += (s, e) => OnInputChanged();
        ChartFillColorBox.TextChanged += (s, e) => OnInputChanged();
        ChartTransparentBox.TextChanged += (s, e) => OnInputChanged();
        TransparentBackgroundCheck.Checked += (s, e) => OnInputChanged();
        TransparentBackgroundCheck.Unchecked += (s, e) => OnInputChanged();
        InvertDirectionCheck.Checked += (s, e) => OnInputChanged();
        InvertDirectionCheck.Unchecked += (s, e) => OnInputChanged();
        RingBorderCheck.Checked += (s, e) => OnInputChanged();
        RingBorderCheck.Unchecked += (s, e) => OnInputChanged();
        RoundCheck.Checked += (s, e) => OnInputChanged();
        RoundCheck.Unchecked += (s, e) => OnInputChanged();
        UseBlockCheck.Checked += (s, e) => OnInputChanged();
        UseBlockCheck.Unchecked += (s, e) => OnInputChanged();
        MaxValueBox.TextChanged += (s, e) => OnInputChanged();
        StartPercentageBox.TextChanged += (s, e) => OnInputChanged();
        TotalAngleBox.TextChanged += (s, e) => OnInputChanged();
        UseGradientCheck.Checked += (s, e) => OnInputChanged();
        UseGradientCheck.Unchecked += (s, e) => OnInputChanged();
        ZoomBox.TextChanged += (s, e) =>
        {
            SyncSliderFromText(ZoomBox, ZoomSlider);
            OnInputChanged();
        };
        ImageFileBox.TextChanged += (s, e) => OnInputChanged();
        ImageRotateCombo.SelectionChanged += (s, e) => OnInputChanged();
        ImageRectBox.TextChanged += (s, e) => OnInputChanged();
        ClockCenterXBox.TextChanged += (s, e) => OnInputChanged();
        ClockCenterYBox.TextChanged += (s, e) => OnInputChanged();
        ClockStartAngleBox.TextChanged += (s, e) => OnInputChanged();
        ClockTotalAngleBox.TextChanged += (s, e) => OnInputChanged();
        ClockOffsetBox.TextChanged += (s, e) => OnInputChanged();
        ClockOriginXBox.TextChanged += (s, e) => OnInputChanged();
        ClockOriginYBox.TextChanged += (s, e) => OnInputChanged();
        ClockMoveOriginCheck.Checked += (s, e) => OnInputChanged();
        ClockMoveOriginCheck.Unchecked += (s, e) => OnInputChanged();
        ClockRevertCheck.Checked += (s, e) => OnInputChanged();
        ClockRevertCheck.Unchecked += (s, e) => OnInputChanged();

        FontCombo.SelectionChanged += FontCombo_SelectionChanged;
        FontCombo.AddHandler(
            System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((s, e) => OnInputChanged()));
        DataCombo.SelectionChanged += (s, e) => OnInputChanged();
        GraphStyleCombo.SelectionChanged += (_, _) =>
        {
            if (!_isLoading && LayerGrid.SelectedItem is LayerRow layer)
            {
                layer.MediaPath = "";
            }
            OnInputChanged();
        };
        AlignmentCombo.SelectionChanged += (s, e) => OnInputChanged();
        FontIntervalBox.TextChanged += (s, e) => OnInputChanged();
        ItalicCheck.Checked += (s, e) => OnInputChanged();
        ItalicCheck.Unchecked += (s, e) => OnInputChanged();
        GraphDirectionCombo.SelectionChanged += GraphDirectionCombo_SelectionChanged;
        GraphGradientDirectionCombo.SelectionChanged += GraphGradientDirectionCombo_SelectionChanged;
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

        var canShowSplit = layer.CanWrite("SplitBlockWidth") || layer.CanWrite("SplitBlankWidth");
        GraphSplitLabel.Visibility = GraphSplitPanel.Visibility = canShowSplit ? Visibility.Visible : Visibility.Collapsed;
        GraphAdvancedExpander.Visibility =
            GraphDirectionCombo.Visibility == Visibility.Visible ||
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

    private void GraphDirectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        SetComboTag(
            GraphGradientDirectionCombo,
            (GraphDirectionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "0");
        _isLoading = false;
        OnInputChanged();
    }

    private void GraphGradientDirectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        SetComboTag(
            GraphDirectionCombo,
            (GraphGradientDirectionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "0");
        _isLoading = false;
        OnInputChanged();
    }

    private void OnInputChanged()
    {
        if (_isLoading) return;
        if (LayerGrid.SelectedItem is not LayerRow layer) return;

        if (!_editorUndoArmed)
        {
            PushUndoState(GetLanguageText("history.properties", "Edit layer properties"));
            _editorUndoArmed = true;
        }

        int oldX = 0;
        int oldY = 0;
        if (TryParseInt(layer.X, out var ox)) oldX = ox;
        if (TryParseInt(layer.Y, out var oy)) oldY = oy;
        var oldSize = layer.Size ?? "";

        UpdateLayerFromInputs(layer);
        if (string.Equals(layer.Type, "GraphClock", StringComparison.OrdinalIgnoreCase))
        {
            DragHintText.Text = string.Equals(layer.ClockMoveOrigin, "True", StringComparison.OrdinalIgnoreCase)
                ? "Drag to move gauge center"
                : "Drag to move gauge hand";
        }
        MarkLayerDirty(layer);
        if (string.Equals(layer.Type, "GraphSensor", StringComparison.OrdinalIgnoreCase))
        {
            _ = RefreshSensorPreviewAsync(layer);
        }
        else if (IsThemeEngineGraphPreviewLayer(layer))
        {
            _ = RefreshGraphPreviewAsync(layer);
        }

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
                    MarkLayerDirty(paired);
                }
            }
        }

        UpdateLayerPreviewVisual(layer);
        if (PairCheck.IsChecked == true)
        {
            var paired = FindPairedLayer(layer);
            if (paired != null)
            {
                UpdateLayerPreviewVisual(paired);
            }
        }
    }

    private async Task RefreshSensorPreviewAsync(LayerRow layer)
    {
        var version = Interlocked.Increment(ref _sensorPreviewRenderVersion);
        _sensorPreviewRenderCts?.Cancel();
        _sensorPreviewRenderCts?.Dispose();
        var previewCts = new CancellationTokenSource();
        _sensorPreviewRenderCts = previewCts;
        var token = previewCts.Token;

        try
        {
            await Task.Delay(70, token);
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LianLiThemeEditor",
                "SensorLivePreview");
            Directory.CreateDirectory(root);
            var key = string.Join("-",
                _currentTemplateId,
                layer.Index,
                layer.SensorStyle,
                layer.SensorType,
                layer.SensorColor1,
                layer.SensorColor2,
                layer.SensorBgColor,
                layer.SensorMainFontColor,
                layer.SensorTopFontColor,
                layer.SensorBottomFontColor,
                layer.SensorFontFamily,
                layer.Text);
            var output = Path.Combine(root, SanitizeFileName(key) + ".png");
            var rendered = await _supporter.RenderSensorPreviewAsync(layer, output, token);
            if (token.IsCancellationRequested ||
                version != _sensorPreviewRenderVersion ||
                !ReferenceEquals(LayerGrid.SelectedItem, layer) ||
                !File.Exists(rendered))
            {
                return;
            }

            layer.MediaPath = rendered;
            UpdateLayerPreviewVisual(layer);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            RequestPreviewDraw();
        }
    }

    private async Task RefreshGraphPreviewAsync(LayerRow layer)
    {
        if (string.IsNullOrWhiteSpace(_currentTemplatePath) || !File.Exists(_currentTemplatePath))
        {
            RequestPreviewDraw();
            return;
        }

        var version = Interlocked.Increment(ref _graphPreviewRenderVersion);
        _graphPreviewRenderCts?.Cancel();
        _graphPreviewRenderCts?.Dispose();
        var previewCts = new CancellationTokenSource();
        _graphPreviewRenderCts = previewCts;
        var token = previewCts.Token;

        try
        {
            await Task.Delay(70, token);
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LianLiThemeEditor",
                "GraphLivePreview");
            Directory.CreateDirectory(root);
            var key = string.Join("-",
                _currentTemplateId,
                layer.Index,
                layer.Type,
                layer.GraphStyle,
                layer.DataSource,
                layer.Width,
                layer.Height,
                layer.Radius,
                layer.Diameter,
                layer.Thickness,
                layer.FrontColor,
                layer.BackColor,
                layer.LineColor,
                layer.FillColor,
                layer.BorderColor,
                layer.GradientColor,
                layer.UseGradient,
                layer.Direction,
                layer.LineWidth,
                layer.ColumnWidth,
                layer.BorderWidth,
                layer.InnerCircleRadius,
                layer.SplitBlockWidth,
                layer.SplitBlankWidth,
                layer.UseSubsection,
                layer.FillBack,
                layer.Revert,
                layer.FrontAlpha,
                layer.BackAlpha,
                layer.TransparentBackground,
                layer.MaxValue,
                layer.InvertDirection,
                layer.StartPercentage,
                layer.TotalAngle,
                layer.UseBlock,
                layer.RingBorder,
                layer.Round,
                layer.TypeName,
                layer.SubTypeName);
            var output = Path.Combine(root, SanitizeFileName(key) + ".png");
            var rendered = await _supporter.RenderGraphPreviewAsync(
                GetSelectedDeviceModel(),
                _currentTemplatePath,
                layer,
                output,
                token);
            if (token.IsCancellationRequested ||
                version != _graphPreviewRenderVersion ||
                !ReferenceEquals(LayerGrid.SelectedItem, layer) ||
                !File.Exists(rendered))
            {
                return;
            }

            layer.MediaPath = rendered;
            _imageBoundsCache.Remove(rendered);
            UpdateLayerPreviewVisual(layer);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            RequestPreviewDraw();
        }
    }

    private static bool IsThemeEngineGraphPreviewLayer(LayerRow layer)
    {
        var type = layer.Type ?? "";
        return type.Contains("GraphStatuBar", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("GraphArchBar", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("GraphLine", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("GraphDynamicBar", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("DynamicBar", StringComparison.OrdinalIgnoreCase);
    }

    private void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || FontCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        FontCombo.Text = item.Tag?.ToString() ?? item.Content?.ToString() ?? "";
        Dispatcher.BeginInvoke(
            new Action(OnInputChanged),
            System.Windows.Threading.DispatcherPriority.DataBind);
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
        var normalizedText = NormalizeLConnectText(TextBox.Text);
        if (!string.Equals(TextBox.Text, normalizedText, StringComparison.Ordinal))
        {
            var caret = Math.Min(TextBox.CaretIndex, normalizedText.Length);
            TextBox.Text = normalizedText;
            TextBox.CaretIndex = caret;
            return;
        }
        if (LayerGrid.SelectedItem is LayerRow layer &&
            string.Equals(layer.Type, "GraphItem", StringComparison.OrdinalIgnoreCase))
        {
            var source = GetComboText(DataCombo);
            if (SetTextCheck.IsChecked != true &&
                !string.IsNullOrWhiteSpace(source) &&
                !source.Equals("StaticText", StringComparison.OrdinalIgnoreCase))
            {
                layer.PreviewValueEdited = true;
                layer.Text = normalizedText;
                _previewSampleOverrides[source] = normalizedText;
            }
        }
        OnInputChanged();
    }

    private async void AutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        _autoSaveTimer.Stop();
        if (_isSavingRecoverySnapshot ||
            _dirtyLayers.Count == 0 ||
            string.IsNullOrWhiteSpace(_currentTemplatePath))
        {
            return;
        }

        _isSavingRecoverySnapshot = true;
        try
        {
            var deviceModel = GetSelectedDeviceModel();
            var templateId = _currentTemplateId;
            var templatePath = _currentTemplatePath;
            var snapshot = Layers.Where(layer => !layer.IsEditorMetadata).ToList();
            await _recoveryService.SaveAsync(deviceModel, templateId, templatePath, snapshot);
            SetStatus(GetLanguageText("status.recoverySaved", "Recovery copy saved."));
        }
        catch (Exception ex)
        {
            AppLogger.Error("Automatic recovery save failed.", ex);
        }
        finally
        {
            _isSavingRecoverySnapshot = false;
        }
    }

    private void LoadRecoverySnapshotForAbout()
    {
        var recovery = _recoveryService.Load();
        if (recovery == null || recovery.Layers.Count == 0 || DateTime.UtcNow - recovery.SavedAtUtc > TimeSpan.FromDays(7))
        {
            _pendingRecoverySnapshot = null;
            RecoveryAboutCard.Visibility = Visibility.Collapsed;
            return;
        }

        _pendingRecoverySnapshot = recovery;
        RecoveryAboutText.Text = FormatLanguageText(
            "recovery.available",
            "An unsaved edit from {0} is available.",
            recovery.SavedAtUtc.ToLocalTime().ToString("g"));
        RecoveryAboutCard.Visibility = Visibility.Visible;
        SetStatus(GetLanguageText("status.recoveryAvailable", "Unsaved recovery is available in About."));
    }

    private void RestoreRecoveryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingRecoverySnapshot is not { } recovery) return;
        PushUndoState(GetLanguageText("history.restoreRecovery", "Restore recovery"));
        Layers.Clear();
        foreach (var layer in recovery.Layers) Layers.Add(layer);
        _currentTemplateId = recovery.TemplateId;
        _currentTemplatePath = recovery.TemplatePath;
        foreach (var layer in Layers) MarkLayerDirty(layer);
        ConfigureLayerGrouping();
        PopulateEditorFromSelection();
        RequestPreviewDraw();
        RecoveryAboutCard.Visibility = Visibility.Collapsed;
        _pendingRecoverySnapshot = null;
        SetStatus(GetLanguageText("status.recoveryRestored", "Unsaved work restored. Press Apply to save."));
    }

    private void DismissRecoveryButton_Click(object sender, RoutedEventArgs e)
    {
        _recoveryService.Clear();
        _pendingRecoverySnapshot = null;
        RecoveryAboutCard.Visibility = Visibility.Collapsed;
        SetStatus(GetLanguageText("status.recoveryDismissed", "Recovery copy discarded."));
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

            if (doc.RootElement.TryGetProperty("universalOrientation", out var orientationProp))
            {
                var orientation = orientationProp.GetString();
                if (!string.IsNullOrWhiteSpace(orientation))
                {
                    _syncingUniversalOrientation = true;
                    foreach (var item in UniversalOrientationCombo.Items.OfType<ComboBoxItem>())
                    {
                        if (string.Equals(item.Tag?.ToString(), orientation, StringComparison.OrdinalIgnoreCase))
                        {
                            UniversalOrientationCombo.SelectedItem = item;
                            break;
                        }
                    }
                    _syncingUniversalOrientation = false;
                }
            }

            if (doc.RootElement.TryGetProperty("groupingEnabled", out var groupingProp) &&
                groupingProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                _groupingEnabled = groupingProp.GetBoolean();
                GroupingEnabledCheckBox.IsChecked = _groupingEnabled;
                ConfigureLayerGrouping();
            }
            if (doc.RootElement.TryGetProperty("ownedDevices", out var ownedProp) && ownedProp.ValueKind == JsonValueKind.Array)
            {
                var owned = ownedProp.EnumerateArray().Select(item => item.GetString() ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
                OwnedHydroshiftSCheck.IsChecked = owned.Contains("hydroshift-ii-lcd-s");
                OwnedHydroshiftCCheck.IsChecked = owned.Contains("hydroshift-ii-lcd-c");
                OwnedUniversal88Check.IsChecked = owned.Contains(UniversalScreenDeviceModel);
                OwnedVm92Check.IsChecked = owned.Contains(Vm92DeviceModel);
            }
            if (doc.RootElement.TryGetProperty("autoApplyGalleryThemes", out var autoApply) && autoApply.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                AutoApplyGalleryThemesCheck.IsChecked = GalleryActivateAfterInstallCheck.IsChecked = autoApply.GetBoolean();
            }
            if (doc.RootElement.TryGetProperty("animateVideoPreviews", out var animatePreviews) && animatePreviews.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                _animateVideoPreviews = animatePreviews.GetBoolean();
                AnimateVideoPreviewsCheck.IsChecked = _animateVideoPreviews;
            }
            SettingsLanguageCombo.SelectedItem = SettingsLanguageCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), StringComparison.OrdinalIgnoreCase));
            SettingsThemeCombo.SelectedItem = SettingsThemeCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), (UiThemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), StringComparison.OrdinalIgnoreCase));
            ApplyOwnedDeviceVisibility(save: false);
        }
        catch (Exception ex) { AppLogger.Error("Shadow links could not be saved.", ex); }
    }

    private void GroupingEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (GroupingEnabledCheckBox == null) return;
        _groupingEnabled = GroupingEnabledCheckBox.IsChecked == true;
        ConfigureLayerGrouping();
        if (CreateLayerGroupButton != null)
        {
            CreateLayerGroupButton.IsEnabled = _groupingEnabled;
        }
        if (!_isLoading) SaveShadowLinks();
    }

    private void OwnedDevices_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || OwnedHydroshiftSCheck == null) return;
        ApplyOwnedDeviceVisibility(save: !_isLoading);
    }

    private void ApplyOwnedDeviceVisibility(bool save)
    {
        var owned = GetOwnedDeviceModels();
        if (owned.Count == 0)
        {
            OwnedHydroshiftSCheck.IsChecked = true;
            owned = GetOwnedDeviceModels();
            if (owned.Count == 0)
            {
                return;
            }
        }

        foreach (var item in DeviceCombo.Items.OfType<ComboBoxItem>())
        {
            var visible = owned.Contains(item.Tag?.ToString() ?? "");
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            item.IsEnabled = visible;
        }
        if (!owned.Contains(GetSelectedDeviceModel()))
        {
            DeviceCombo.SelectedItem = DeviceCombo.Items.OfType<ComboBoxItem>().First(item => item.Visibility == Visibility.Visible);
        }
        GalleryFilterHydroshiftSCheck.IsChecked = owned.Contains("hydroshift-ii-lcd-s");
        GalleryFilterHydroshiftCCheck.IsChecked = owned.Contains("hydroshift-ii-lcd-c");
        GalleryFilterUniversal88Check.IsChecked = owned.Contains(UniversalScreenDeviceModel);
        GalleryFilterVm92Check.IsChecked = owned.Contains(Vm92DeviceModel);
        if (save)
        {
            SaveShadowLinks();
        }
    }

    private HashSet<string> GetOwnedDeviceModels()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (OwnedHydroshiftSCheck?.IsChecked == true) result.Add("hydroshift-ii-lcd-s");
        if (OwnedHydroshiftCCheck?.IsChecked == true) result.Add("hydroshift-ii-lcd-c");
        if (OwnedUniversal88Check?.IsChecked == true) result.Add(UniversalScreenDeviceModel);
        if (OwnedVm92Check?.IsChecked == true) result.Add(Vm92DeviceModel);
        return result;
    }

    private void SettingsLanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || SettingsLanguageCombo.SelectedItem is not ComboBoxItem selected) return;
        LanguageCombo.SelectedItem = LanguageCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), selected.Tag?.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private void SettingsThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || SettingsThemeCombo.SelectedItem is not ComboBoxItem selected) return;
        UiThemeCombo.SelectedItem = UiThemeCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), selected.Tag?.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private void AutoApplyGalleryThemes_Changed(object sender, RoutedEventArgs e)
    {
        if (GalleryActivateAfterInstallCheck != null)
            GalleryActivateAfterInstallCheck.IsChecked = AutoApplyGalleryThemesCheck.IsChecked;
        if (!_isLoading) SaveShadowLinks();
    }

    private void AnimateVideoPreviews_Changed(object sender, RoutedEventArgs e)
    {
        if (AnimateVideoPreviewsCheck == null) return;
        _animateVideoPreviews = AnimateVideoPreviewsCheck.IsChecked == true;
        if (!_isLoading) SaveShadowLinks();
        if (!string.IsNullOrWhiteSpace(_currentBackgroundPath) || Layers.Any(layer => string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase)))
        {
            var displayBackground = Layers.FirstOrDefault(layer => string.Equals(layer.Type, "GraphAnimation", StringComparison.OrdinalIgnoreCase))?.Media ?? "";
            LoadBackgroundPreview(_currentBackgroundPath, displayBackground);
            RequestPreviewDraw();
        }
    }

    private void UnusedSensorsHelp_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            GetLanguageText("settings.unusedSensorsHelp", ""),
            GetLanguageText("settings.unusedSensorsTitle", ""), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void FeatureRequestButton_Click(object sender, RoutedEventArgs e) =>
        OpenExternalUrl(GitHubIssuesUrl + "/new?labels=enhancement&template=feature_request.md&title=%5BFeature%5D+");

    private async void SendCurrentThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentTemplatePath) || !File.Exists(_currentTemplatePath))
        {
            MessageBox.Show(this, GetLanguageText("messages.loadThemeFirst", "Load a theme first."), GetLanguageText("gallery.submitTitle", "Submit theme"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_dirtyLayers.Count > 0)
        {
            MessageBox.Show(this, GetLanguageText("gallery.saveBeforeSubmit", "Apply or save your pending layer changes before submitting the theme."), GetLanguageText("gallery.submitTitle", "Submit theme"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var packagePath = Path.Combine(Path.GetTempPath(), $"gallery-submit-{Guid.NewGuid():N}.lltheme");
        var previewPath = Path.Combine(Path.GetTempPath(), $"gallery-submit-preview-{Guid.NewGuid():N}.png");
        try
        {
            SetBusy(true, GetLanguageText("gallery.preparing", "Preparing theme package..."));
            var snapshot = CreateThemeExportSnapshot(GetSelectedDeviceModel());
            await ExportThemePackageAsync(packagePath, snapshot);
            await File.WriteAllBytesAsync(previewPath, RenderCurrentThemePreview(cleanEditorOverlay: true));
            SetBusy(false, "");
            await SubmitThemePackageAsync(packagePath, _currentTemplateId, GetSelectedDeviceModel(), previewPath);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Gallery submission failed.", ex);
            SetBusy(false, GetLanguageText("gallery.submitFailed", "Theme submission failed."));
            MessageBox.Show(this, ex.Message, GetLanguageText("gallery.submitFailed", "Theme submission failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TryDeleteFile(packagePath);
            TryDeleteFile(previewPath);
        }
    }

    private async void SendThemesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = GetLanguageText("gallery.choosePackage", "Choose a theme package to submit"),
            Filter = GetLanguageText("dialogs.themeImportFilter", "Theme packages (*.lltheme;*.zip)|*.lltheme;*.zip")
        };
        if (dialog.ShowDialog(this) != true) return;
        var validation = _themeValidator.Validate(dialog.FileName, TemplateOptions.Select(option => option.Id));
        var defaultName = string.IsNullOrWhiteSpace(validation.TemplateId) ? Path.GetFileNameWithoutExtension(dialog.FileName) : validation.TemplateId;
        await SubmitThemePackageAsync(dialog.FileName, defaultName, validation.DeviceModel);
    }

    private async Task SubmitThemePackageAsync(string packagePath, string defaultName, string deviceModel, string previewPath = "")
    {
        var validation = _themeValidator.Validate(packagePath, TemplateOptions.Select(option => option.Id));
        if (!ShowThemeValidation(validation)) return;
        if (string.IsNullOrWhiteSpace(deviceModel)) deviceModel = GetSelectedDeviceModel();

        var nameBox = new TextBox { Text = defaultName, MinWidth = 320 };
        var authorBox = new TextBox { MinWidth = 320 };
        var contactBox = new TextBox { MinWidth = 320 };
        var descriptionBox = new TextBox { MinWidth = 320, MinHeight = 75, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var form = new Grid { Margin = new Thickness(20) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < 5; i++) form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddSubmissionRow(form, 0, GetLanguageText("gallery.themeName", "Theme name"), nameBox);
        AddSubmissionRow(form, 1, GetLanguageText("gallery.author", "Author"), authorBox);
        AddSubmissionRow(form, 2, GetLanguageText("gallery.contact", "Contact"), contactBox);
        AddSubmissionRow(form, 3, GetLanguageText("gallery.description", "Description"), descriptionBox);
        var send = new Button { Content = GetLanguageText("gallery.submit", "Submit for review"), Width = 130, IsDefault = true, Margin = new Thickness(8, 14, 0, 0), Style = (Style)FindResource("BtnPrimary") };
        var cancel = new Button { Content = GetLanguageText("common.cancel", "Cancel"), Width = 90, IsCancel = true, Margin = new Thickness(0, 14, 0, 0), Style = (Style)FindResource("BtnGhost") };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(cancel); actions.Children.Add(send); Grid.SetRow(actions, 4); Grid.SetColumnSpan(actions, 2); form.Children.Add(actions);
        var window = CreateThemedDialog(GetLanguageText("gallery.submitTitle", "Submit theme"), form, 520);
        send.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(nameBox.Text) && !string.IsNullOrWhiteSpace(authorBox.Text)) window.DialogResult = true; };
        if (window.ShowDialog() != true) return;

        var generatedPreviewPath = "";
        try
        {
            if (string.IsNullOrWhiteSpace(previewPath) || !File.Exists(previewPath))
            {
                generatedPreviewPath = TryExtractSubmissionPreview(packagePath);
                previewPath = generatedPreviewPath;
            }
            SetBusy(true, GetLanguageText("gallery.uploading", "Uploading theme..."));
            var submissionId = await _gallerySubmissionService.SubmitAsync(new GallerySubmission
            {
                ThemeName = nameBox.Text.Trim(), Author = authorBox.Text.Trim(), Contact = contactBox.Text.Trim(),
                Description = descriptionBox.Text.Trim(), DeviceModel = NormalizeGalleryDeviceModel(deviceModel),
                PackagePath = packagePath, PreviewPath = previewPath
            });
            SetBusy(false, GetLanguageText("gallery.submitted", "Theme submitted for review."));
            MessageBox.Show(this, FormatLanguageText("gallery.submittedMessage", "Theme submitted for review. Submission ID: {0}", submissionId), GetLanguageText("gallery.submitTitle", "Submit theme"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Gallery submission failed.", ex);
            SetBusy(false, GetLanguageText("gallery.submitFailed", "Theme submission failed."));
            MessageBox.Show(this, ex.Message, GetLanguageText("gallery.submitFailed", "Theme submission failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TryDeleteFile(generatedPreviewPath);
        }
    }

    private static string TryExtractSubmissionPreview(string packagePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var imageEntry = archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name) &&
                                Path.GetExtension(entry.Name).Equals(".png", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.FullName.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
                                            entry.FullName.Contains("thumbnail", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(entry => entry.Length)
                .FirstOrDefault();
            byte[] previewBytes;
            if (imageEntry != null)
            {
                using var stream = imageEntry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                previewBytes = memory.ToArray();
            }
            else
            {
                var templateEntry = archive.Entries.FirstOrDefault(entry =>
                    entry.Name.EndsWith(".template", StringComparison.OrdinalIgnoreCase));
                if (templateEntry == null) return "";
                using var stream = templateEntry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                previewBytes = ExtractLargestEmbeddedPng(memory.ToArray());
            }

            if (previewBytes.Length == 0) return "";
            var output = Path.Combine(Path.GetTempPath(), $"gallery-submit-preview-{Guid.NewGuid():N}.png");
            File.WriteAllBytes(output, previewBytes);
            return output;
        }
        catch
        {
            return "";
        }
    }

    private static byte[] ExtractLargestEmbeddedPng(byte[] data)
    {
        ReadOnlySpan<byte> signature = stackalloc byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        ReadOnlySpan<byte> endMarker = stackalloc byte[] { 73, 69, 78, 68, 174, 66, 96, 130 };
        byte[] largest = Array.Empty<byte>();
        for (var offset = 0; offset <= data.Length - signature.Length; offset++)
        {
            if (!data.AsSpan(offset, signature.Length).SequenceEqual(signature)) continue;
            var tail = data.AsSpan(offset + signature.Length);
            var relativeEnd = tail.IndexOf(endMarker);
            if (relativeEnd < 0) continue;
            var length = signature.Length + relativeEnd + endMarker.Length;
            if (length > largest.Length) largest = data.AsSpan(offset, length).ToArray();
            offset += length - 1;
        }
        return largest;
    }

    private static void AddSubmissionRow(Grid grid, int row, string labelText, Control editor)
    {
        var label = new TextBlock { Text = labelText, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 12, 7) };
        editor.Margin = new Thickness(0, 4, 0, 4);
        Grid.SetRow(label, row); Grid.SetColumn(label, 0); grid.Children.Add(label);
        Grid.SetRow(editor, row); Grid.SetColumn(editor, 1); grid.Children.Add(editor);
    }

    private async Task RevertTemplateBackgroundAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentTemplateId)) return;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var devicePaths = GetLConnectDevicePaths();
        var templateIdJson = JsonSerializer.Serialize(_currentTemplateId);

        foreach (var path in devicePaths)
        {
            try
            {
                await SendLConnectDeviceRequestAsync(client, path, "RevertTemplateBackground", templateIdJson);
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

        PushUndoState(GetLanguageText("history.gridEdit", "Edit layer in list"));
        Dispatcher.BeginInvoke(new Action(() =>
        {
            MarkLayerDirty(layer);
            PopulateEditorFromSelection();
            LayerGrid.Items.Refresh();
            DrawPreview();
            SetStatus(GetLanguageText("status.gridEditChanged", "Grid edit changed. Press Apply to save."));
        }), System.Windows.Threading.DispatcherPriority.Background);
    }
}

