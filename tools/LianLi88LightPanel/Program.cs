using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LianLi88LightPanel;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new LightPanelForm());
    }
}

internal sealed class LightPanelForm : Form
{
    private readonly LianLi88NativeController controller = new();
    private readonly ComboBox deviceBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox effectBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox directionBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button scanButton = new() { Text = "Scan" };
    private readonly Button colorButton = new() { Text = "Color" };
    private readonly Button applyButton = new() { Text = "Apply" };
    private readonly Button offButton = new() { Text = "Off" };
    private readonly TrackBar brightnessBar = new() { Minimum = 0, Maximum = 100, Value = 100, TickFrequency = 25 };
    private readonly TrackBar speedBar = new() { Minimum = 0, Maximum = 100, Value = 50, TickFrequency = 25 };
    private readonly Label statusLabel = new() { AutoSize = false, Height = 58 };
    private Color selectedColor = Color.White;

    public LightPanelForm()
    {
        Text = "Lian Li 8.8 Native Light Panel";
        Width = 520;
        Height = 360;
        MinimumSize = new Size(470, 330);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        effectBox.Items.AddRange(LianLiEffect.KnownEffects.Cast<object>().ToArray());
        effectBox.SelectedIndex = 2;
        directionBox.Items.AddRange(new object[] { "Clockwise", "Counter-clockwise" });
        directionBox.SelectedIndex = 0;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 2,
            RowCount = 8
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, 0, "Device", deviceBox);
        AddRow(layout, 1, "Effect", effectBox);
        AddRow(layout, 2, "Direction", directionBox);
        AddRow(layout, 3, "Brightness", brightnessBar);
        AddRow(layout, 4, "Speed", speedBar);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.Add(scanButton);
        buttons.Controls.Add(colorButton);
        buttons.Controls.Add(applyButton);
        buttons.Controls.Add(offButton);
        layout.Controls.Add(new Label { Text = "Controls", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 5);
        layout.Controls.Add(buttons, 1, 5);

        var hint = new Label
        {
            Text = "Uses Lian Li's local LEDController DLL directly. Close L-Connect first if the device is busy.",
            AutoSize = false,
            Dock = DockStyle.Fill
        };
        layout.Controls.Add(hint, 0, 6);
        layout.SetColumnSpan(hint, 2);
        layout.Controls.Add(statusLabel, 0, 7);
        layout.SetColumnSpan(statusLabel, 2);

        Controls.Add(layout);

        scanButton.Click += (_, _) => Scan();
        colorButton.Click += (_, _) => ChooseColor();
        applyButton.Click += (_, _) => ApplyEffect();
        offButton.Click += (_, _) => StopEffect();
        Shown += (_, _) => Scan();
        FormClosed += (_, _) => controller.Cleanup();
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        control.Dock = DockStyle.Fill;
        layout.Controls.Add(control, 1, row);
    }

    private void Scan()
    {
        try
        {
            SetStatus("Scanning 8.8 LED controllers...");
            controller.Initialize();
            deviceBox.Items.Clear();
            foreach (var device in controller.GetDevices())
            {
                deviceBox.Items.Add(device);
            }

            if (deviceBox.Items.Count > 0)
            {
                deviceBox.SelectedIndex = 0;
            }

            SetStatus(deviceBox.Items.Count == 0
                ? "No 8.8 LED controller found. Run as administrator and close L-Connect if needed."
                : $"Found {deviceBox.Items.Count} 8.8 LED controller(s).");
        }
        catch (Exception ex)
        {
            SetStatus(CleanMessage(ex));
        }
    }

    private void ChooseColor()
    {
        using var dialog = new ColorDialog { Color = selectedColor, FullOpen = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            selectedColor = dialog.Color;
            colorButton.BackColor = selectedColor;
            colorButton.ForeColor = selectedColor.GetBrightness() < 0.45f ? Color.White : Color.Black;
        }
    }

    private void ApplyEffect()
    {
        if (deviceBox.SelectedItem is not LianLiDevice device || effectBox.SelectedItem is not LianLiEffect effect)
        {
            SetStatus("Select a device and effect first.");
            return;
        }

        try
        {
            var ok = controller.SetEffect(
                device,
                effect,
                ToBrightnessLevel(brightnessBar.Value),
                ToSpeedLevel(speedBar.Value),
                directionBox.SelectedIndex == 1,
                BuildColors(effect.ColorCount));
            SetStatus(ok ? $"Applied {effect.Name}." : "Lian Li LEDController rejected the effect.");
        }
        catch (Exception ex)
        {
            SetStatus(CleanMessage(ex));
        }
    }

    private void StopEffect()
    {
        if (deviceBox.SelectedItem is not LianLiDevice device)
        {
            SetStatus("Select a device first.");
            return;
        }

        try
        {
            var ok = controller.StopEffect(device);
            SetStatus(ok ? "Effect stopped." : "StopEffect returned false.");
        }
        catch (Exception ex)
        {
            SetStatus(CleanMessage(ex));
        }
    }

    private List<Color> BuildColors(int count)
    {
        if (count <= 0)
        {
            count = 1;
        }

        var colors = new List<Color> { selectedColor };
        var palette = new[] { Color.Red, Color.Gold, Color.Lime, Color.DeepSkyBlue, Color.BlueViolet, Color.HotPink };
        for (var i = 1; i < count; i++)
        {
            colors.Add(palette[(i - 1) % palette.Length]);
        }

        return colors;
    }

    private static int ToBrightnessLevel(int value) => value switch
    {
        <= 0 => 0,
        <= 25 => 64,
        <= 50 => 128,
        <= 75 => 192,
        _ => 255
    };

    private static int ToSpeedLevel(int value) => value switch
    {
        <= 0 => 7,
        <= 25 => 6,
        <= 50 => 5,
        <= 75 => 4,
        _ => 3
    };

    private void SetStatus(string text) => statusLabel.Text = text;

    private static string CleanMessage(Exception ex)
    {
        while (ex is TargetInvocationException && ex.InnerException is not null)
        {
            ex = ex.InnerException;
        }

        return ex.Message;
    }
}

internal sealed class LianLi88NativeController
{
    private const string LConnectDir = @"C:\Program Files\Lian-Li\L-Connect 3";

    private Assembly? lcdAssembly;
    private Assembly? modelsAssembly;
    private Type? ledControllerType;
    private Type? lightEffectSetType;
    private Type? rgbEffectTypesType;
    private Type? speedType;
    private Type? brightnessType;
    private Type? directionType;
    private Type? mediaColorType;
    private bool initialized;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    public void Initialize()
    {
        if (initialized)
        {
            return;
        }

        if (!Directory.Exists(LConnectDir))
        {
            throw new DirectoryNotFoundException("L-Connect 3 folder was not found: " + LConnectDir);
        }

        StopKnownConflicts();

        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromLConnectDir;
        Directory.SetCurrentDirectory(LConnectDir);
        SetDllDirectory(LConnectDir);

        modelsAssembly = Assembly.LoadFrom(Path.Combine(LConnectDir, "slv3.models.dll"));
        _ = Assembly.LoadFrom(Path.Combine(LConnectDir, "lianli.slv3.dll"));
        lcdAssembly = Assembly.LoadFrom(Path.Combine(LConnectDir, "lianli.lcd207.dll"));

        ledControllerType = RequireType(lcdAssembly, "lcd207.LEDController");
        lightEffectSetType = RequireType(modelsAssembly, "slv3.models.LightEffectSet");
        rgbEffectTypesType = RequireType(modelsAssembly, "slv3.models.RgbEffectTypes");
        speedType = RequireType(modelsAssembly, "slv3.models.RFLampEffectSpeedType");
        brightnessType = RequireType(modelsAssembly, "slv3.models.RFBrightnessType");
        directionType = RequireType(modelsAssembly, "slv3.models.EffectDirections");
        mediaColorType = typeof(System.Windows.Media.Color);

        var initSettings = CreateInitSettings();
        var initMethod = RequireMethod(ledControllerType, "Init", BindingFlags.Public | BindingFlags.Static);
        var ok = (bool)initMethod.Invoke(null, new object?[] { initSettings, null })!;
        if (!ok)
        {
            throw new InvalidOperationException("LEDController.Init failed. Close L-Connect/L-Connect-Service and run this app as administrator.");
        }

        initialized = true;
    }

    public List<LianLiDevice> GetDevices()
    {
        Initialize();
        var getAllDevices = RequireMethod(ledControllerType!, "GetAllDevices", BindingFlags.Public | BindingFlags.Static);
        var devices = (System.Collections.IEnumerable?)getAllDevices.Invoke(null, null);
        var result = new List<LianLiDevice>();
        if (devices is null)
        {
            return result;
        }

        var snProperty = RequireProperty(ledControllerType!, "SN", BindingFlags.Public | BindingFlags.Instance);
        var nameProperty = RequireProperty(ledControllerType!, "Name", BindingFlags.Public | BindingFlags.Instance);
        var lcdUsbProperty = RequireProperty(ledControllerType!, "lcdUsb", BindingFlags.Public | BindingFlags.Instance);
        var ledUsbProperty = RequireProperty(ledControllerType!, "ledUsb", BindingFlags.Public | BindingFlags.Instance);

        foreach (var device in devices)
        {
            var hasLcdUsb = lcdUsbProperty.GetValue(device) is not null;
            var hasLedUsb = ledUsbProperty.GetValue(device) is not null;
            if (!hasLcdUsb && !hasLedUsb)
            {
                continue;
            }

            var sn = Convert.ToString(snProperty.GetValue(device)) ?? "";
            var name = Convert.ToString(nameProperty.GetValue(device)) ?? "8.8 LED";
            var channel = hasLedUsb ? "ledUsb" : "lcdUsb";
            result.Add(new LianLiDevice(name, sn, channel, device));
        }

        return result;
    }

    public bool SetEffect(LianLiDevice device, LianLiEffect effect, int brightness, int speed, bool reverse, List<Color> colors)
    {
        Initialize();
        var lightEffect = Activator.CreateInstance(lightEffectSetType!);
        Set(lightEffect!, "RgbEffectType", Enum.ToObject(rgbEffectTypesType!, effect.Mode));
        Set(lightEffect!, "BrightnessType", Enum.ToObject(brightnessType!, brightness));
        Set(lightEffect!, "SpeedType", Enum.ToObject(speedType!, speed));
        Set(lightEffect!, "iDir", Enum.ToObject(directionType!, reverse ? 1 : 0));
        Set(lightEffect!, "UserColors", CreateMediaColorList(colors));

        var method = RequireMethod(ledControllerType!, "SetEffect", BindingFlags.Public | BindingFlags.Instance);
        return (bool)method.Invoke(device.NativeDevice, new[] { lightEffect, 0 })!;
    }

    public bool StopEffect(LianLiDevice device)
    {
        Initialize();
        var method = RequireMethod(ledControllerType!, "StopEffect", BindingFlags.Public | BindingFlags.Instance);
        return (bool)method.Invoke(device.NativeDevice, null)!;
    }

    public void Cleanup()
    {
        if (ledControllerType is null)
        {
            return;
        }

        var method = ledControllerType.GetMethod("Cleanup", BindingFlags.Public | BindingFlags.Static);
        try
        {
            method?.Invoke(null, null);
        }
        catch
        {
            // Lian Li's cleanup path may request optional .NET Framework-era assemblies.
            // The device state is already set by this point, so shutdown should stay quiet.
        }
    }

    private object CreateInitSettings()
    {
        var type = RequireType(modelsAssembly!, "slv3.models.InitSettings");
        var settings = Activator.CreateInstance(type)!;
        var temp = Path.Combine(Path.GetTempPath(), "LianLi88LightPanel");
        Directory.CreateDirectory(temp);

        Set(settings, "TemplatePath", Path.Combine(temp, "template"));
        Set(settings, "ThemePath", Path.Combine(temp, "theme"));
        Set(settings, "ModularsPath", Path.Combine(temp, "modulars"));
        Set(settings, "FFMPEGPath", LConnectDir);
        Set(settings, "GifPath", Path.Combine(temp, "gif"));
        Set(settings, "VideoPath", Path.Combine(temp, "video"));
        Set(settings, "TempPath", temp);
        Set(settings, "BgPath", Path.Combine(temp, "bg"));
        Set(settings, "FWPath", Path.Combine(temp, "fw"));
        return settings;
    }

    private object CreateMediaColorList(List<Color> colors)
    {
        var listType = typeof(List<>).MakeGenericType(mediaColorType!);
        var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
        foreach (var color in colors)
        {
            list.Add(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B));
        }

        return list;
    }

    private static Assembly? ResolveFromLConnectDir(object? sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name + ".dll";
        var path = Path.Combine(LConnectDir, name);
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

    private static void StopKnownConflicts()
    {
        foreach (var name in new[] { "L-Connect 3", "OpenRGB", "nOpenRGB" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    process.Kill(true);
                    process.WaitForExit(3000);
                }
                catch
                {
                }
            }
        }

        foreach (var service in new[] { "LConnectServiceWatcher", "LConnectService", "OpenRGB" })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = "stop " + service,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                process?.WaitForExit(4000);
            }
            catch
            {
            }
        }
    }

    private static Type RequireType(Assembly assembly, string name) =>
        assembly.GetType(name) ?? throw new InvalidOperationException("Type not found: " + name);

    private static MethodInfo RequireMethod(Type type, string name, BindingFlags flags) =>
        type.GetMethod(name, flags) ?? throw new InvalidOperationException("Method not found: " + type.FullName + "." + name);

    private static PropertyInfo RequireProperty(Type type, string name, BindingFlags flags) =>
        type.GetProperty(name, flags) ?? throw new InvalidOperationException("Property not found: " + type.FullName + "." + name);

    private static void Set(object instance, string propertyName, object value) =>
        instance.GetType().GetProperty(propertyName)?.SetValue(instance, value);
}

internal sealed record LianLiDevice(string Name, string Serial, string Channel, object NativeDevice)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Serial) ? $"{Name} [{Channel}]" : $"{Name} ({Serial}) [{Channel}]";
}

internal sealed record LianLiEffect(string Name, int Mode, int ColorCount)
{
    public static readonly LianLiEffect[] KnownEffects =
    {
        new("Rainbow", 0, 6),
        new("Wave", 1, 6),
        new("Static Color", 2, 1),
        new("Breathing", 3, 2),
        new("Rainbow Morph", 4, 6),
        new("Paint", 5, 6),
        new("Runway", 6, 2),
        new("Tide", 7, 2),
        new("Blow Up", 8, 6),
        new("Meteor", 9, 2),
        new("Snooker", 10, 6),
        new("Mixing", 11, 6),
        new("Ping Pong", 12, 2),
        new("Stack", 13, 2),
        new("Twinkle", 14, 6),
        new("River", 15, 6),
        new("Hourglass", 16, 2),
        new("Electric Current", 17, 2),
        new("Rainbow Wave", 18, 6)
    };

    public override string ToString() => Name;
}
