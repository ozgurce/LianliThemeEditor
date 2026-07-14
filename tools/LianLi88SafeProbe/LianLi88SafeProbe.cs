using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static class Program
{
    private const string LConnectDir = @"C:\Program Files\Lian-Li\L-Connect 3";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromLConnectDir;
        Directory.SetCurrentDirectory(LConnectDir);
        SetDllDirectory(LConnectDir);

        bool queryDir = args.Contains("--query-dir", StringComparer.OrdinalIgnoreCase);
        bool fileSize = args.Contains("--file-size", StringComparer.OrdinalIgnoreCase);
        string queryPath = GetArgValue(args, "--path");
        string targetSn = GetArgValue(args, "--sn");
        if (queryPath == null)
        {
            queryPath = "/usr/data/";
        }
        if (targetSn == null)
        {
            targetSn = "0f309bd5cad00203";
        }

        Console.WriteLine("Lian Li 8.8 safe probe");
        Console.WriteLine("Allowed USB commands in this tool: GetVer(10), GetFileSize(98), QueryDir(99).");
        Console.WriteLine("No write/delete/reboot/set commands are sent.");

        Assembly lcdAsm = Assembly.LoadFrom(Path.Combine(LConnectDir, "lianli.lcd207.dll"));
        Assembly modelsAsm = Assembly.LoadFrom(Path.Combine(LConnectDir, "slv3.models.dll"));

        Type initSettingsType = RequireType(modelsAsm, "slv3.models.InitSettings");
        object initSettings = Activator.CreateInstance(initSettingsType);
        Set(initSettings, "TemplatePath", Path.Combine(LConnectDir, "probe-template"));
        Set(initSettings, "ThemePath", Path.Combine(LConnectDir, "probe-theme"));
        Set(initSettings, "ModularsPath", Path.Combine(LConnectDir, "probe-modulars"));
        Set(initSettings, "FFMPEGPath", LConnectDir);
        Set(initSettings, "GifPath", Path.Combine(LConnectDir, "probe-gif"));
        Set(initSettings, "VideoPath", Path.Combine(LConnectDir, "probe-video"));
        Set(initSettings, "TempPath", Path.GetTempPath());
        Set(initSettings, "BgPath", Path.Combine(LConnectDir, "probe-bg"));
        Set(initSettings, "FWPath", Path.Combine(LConnectDir, "probe-fw"));

        Type ledControllerType = RequireType(lcdAsm, "lcd207.LEDController");
        MethodInfo initMethod = RequireMethod(ledControllerType, "Init", BindingFlags.Public | BindingFlags.Static);
        bool initOk = (bool)initMethod.Invoke(null, new object[] { initSettings, null });
        Console.WriteLine("LEDController.Init = " + initOk);
        if (!initOk)
        {
            Console.WriteLine("Init failed. Close L-Connect/L-Connect-Service and retry if the device is busy.");
            return 2;
        }

        object controller = FindLedController(ledControllerType, targetSn);
        if (controller == null)
        {
            Console.WriteLine("No LEDController matched SN " + targetSn + ".");
            return 3;
        }

        string controllerSn = Convert.ToString(RequireProperty(ledControllerType, "SN", BindingFlags.Public | BindingFlags.Instance).GetValue(controller, null));
        string controllerName = Convert.ToString(RequireProperty(ledControllerType, "Name", BindingFlags.Public | BindingFlags.Instance).GetValue(controller, null));
        Console.WriteLine("Selected controller: " + controllerName + " SN=" + controllerSn);

        object winUsb = RequireProperty(ledControllerType, "lcdUsb", BindingFlags.Public | BindingFlags.Instance).GetValue(controller, null);
        if (winUsb == null)
        {
            Console.WriteLine("Selected controller has no lcdUsb channel.");
            return 4;
        }

        Type cmdType = RequireType(lcdAsm, "lcd207.CmdType");

        byte[] verResponse = Send(winUsb, cmdType, "GetVer", Array.Empty<byte>());
        DumpResponse("GetVer", verResponse);
        PrintVersionIfPresent(verResponse);

        if (queryDir)
        {
            byte[] payload = BuildPathPayload(queryPath);
            byte[] dirResponse = Send(winUsb, cmdType, "QueryDir", 99, payload);
            DumpResponse("QueryDir " + queryPath, dirResponse);
            PrintAscii("QueryDir ASCII", dirResponse);
        }

        if (fileSize)
        {
            byte[] payload = BuildPathPayload(queryPath);
            byte[] sizeResponse = Send(winUsb, cmdType, "GetFileSize", 98, payload);
            DumpResponse("GetFileSize " + queryPath, sizeResponse);
            PrintFileSizeIfPresent(sizeResponse);
            PrintAscii("GetFileSize ASCII", sizeResponse);
        }

        MethodInfo cleanup = ledControllerType.GetMethod("Cleanup", BindingFlags.Public | BindingFlags.Static);
        if (cleanup != null)
        {
            cleanup.Invoke(null, null);
        }
        return 0;
    }

    private static Assembly ResolveFromLConnectDir(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        string path = Path.Combine(LConnectDir, name);
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

    private static byte[] BuildPathPayload(string path)
    {
        byte[] pathBytes = Encoding.ASCII.GetBytes(path);
        byte[] payload = new byte[8 + pathBytes.Length + 1];
        int len = pathBytes.Length;
        payload[0] = (byte)(len >> 24);
        payload[1] = (byte)((len >> 16) & 0xFF);
        payload[2] = (byte)((len >> 8) & 0xFF);
        payload[3] = (byte)(len & 0xFF);
        Array.Copy(pathBytes, 0, payload, 8, pathBytes.Length);
        return payload;
    }

    private static byte[] Send(object winUsb, Type cmdType, string commandName, byte[] payload)
    {
        return Send(winUsb, cmdType, commandName, null, payload);
    }

    private static byte[] Send(object winUsb, Type cmdType, string commandName, int? commandValue, byte[] payload)
    {
        if (!string.Equals(commandName, "GetVer", StringComparison.Ordinal) &&
            !string.Equals(commandName, "GetFileSize", StringComparison.Ordinal) &&
            !string.Equals(commandName, "QueryDir", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Command is not whitelisted: " + commandName);
        }

        object command = commandValue.HasValue ? Enum.ToObject(cmdType, commandValue.Value) : Enum.Parse(cmdType, commandName);
        MethodInfo send = winUsb.GetType().GetMethod(
            "Send",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { cmdType, typeof(byte[]) },
            null);

        if (payload.Length == 0)
        {
            MethodInfo sendNoPayload = winUsb.GetType().GetMethod(
                "Send",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { cmdType },
                null);
            return (byte[])sendNoPayload.Invoke(winUsb, new[] { command });
        }

        return (byte[])send.Invoke(winUsb, new object[] { command, payload });
    }

    private static void DumpResponse(string label, byte[] data)
    {
        if (data == null)
        {
            Console.WriteLine(label + ": <null>");
            return;
        }

        Console.WriteLine(label + ": " + data.Length + " bytes");
        Console.WriteLine(BitConverter.ToString(data.Take(Math.Min(data.Length, 128)).ToArray()));
    }

    private static void PrintVersionIfPresent(byte[] data)
    {
        if (data.Length < 40 || data[0] != 10)
        {
            return;
        }

        string version = Encoding.UTF8.GetString(data, 8, Math.Min(32, data.Length - 8)).TrimEnd('\0', ' ');
        Console.WriteLine("Version: " + version);
    }

    private static void PrintFileSizeIfPresent(byte[] data)
    {
        if (data == null || data.Length < 12 || data[0] != 98)
        {
            return;
        }

        int size = data[8] | (data[9] << 8) | (data[10] << 16) | (data[11] << 24);
        Console.WriteLine("FileSize: " + size);
    }

    private static void PrintAscii(string label, byte[] data)
    {
        if (data == null)
        {
            return;
        }

        string ascii = Encoding.ASCII.GetString(data.Select(b => b >= 32 && b <= 126 ? b : (byte)'.').ToArray());
        Console.WriteLine(label + ": " + ascii);
    }

    private static string GetArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static Type RequireType(Assembly assembly, string name)
    {
        Type type = assembly.GetType(name);
        if (type == null)
        {
            throw new InvalidOperationException("Type not found: " + name);
        }

        return type;
    }

    private static MethodInfo RequireMethod(Type type, string name, BindingFlags flags)
    {
        MethodInfo method = type.GetMethod(name, flags);
        if (method == null)
        {
            throw new InvalidOperationException("Method not found: " + type.FullName + "." + name);
        }

        return method;
    }

    private static object FindLedController(Type ledControllerType, string targetSn)
    {
        MethodInfo getAllDevices = RequireMethod(ledControllerType, "GetAllDevices", BindingFlags.Public | BindingFlags.Static);
        object devices = getAllDevices.Invoke(null, null);
        if (devices == null)
        {
            return null;
        }

        PropertyInfo snProperty = RequireProperty(ledControllerType, "SN", BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo lcdUsbProperty = RequireProperty(ledControllerType, "lcdUsb", BindingFlags.Public | BindingFlags.Instance);
        object firstWithLcdUsb = null;

        foreach (object device in (System.Collections.IEnumerable)devices)
        {
            string sn = Convert.ToString(snProperty.GetValue(device, null));
            object lcdUsb = lcdUsbProperty.GetValue(device, null);
            Console.WriteLine("Found controller SN=" + sn + " lcdUsb=" + (lcdUsb != null));
            if (lcdUsb != null && firstWithLcdUsb == null)
            {
                firstWithLcdUsb = device;
            }

            if (lcdUsb != null && string.Equals(sn, targetSn, StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }
        }

        return firstWithLcdUsb;
    }

    private static PropertyInfo RequireProperty(Type type, string name)
    {
        return RequireProperty(type, name, BindingFlags.Public | BindingFlags.Static);
    }

    private static PropertyInfo RequireProperty(Type type, string name, BindingFlags flags)
    {
        PropertyInfo property = type.GetProperty(name, flags);
        if (property == null)
        {
            throw new InvalidOperationException("Property not found: " + type.FullName + "." + name);
        }

        return property;
    }

    private static void Set(object instance, string propertyName, string value)
    {
        instance.GetType().GetProperty(propertyName).SetValue(instance, value, null);
    }
}
