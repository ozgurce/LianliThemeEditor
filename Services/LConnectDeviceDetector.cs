using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ThemeEditorCSharp.Services;

public sealed record DetectedLConnectHardware(
    bool HasHydroshiftS,
    bool HasHydroshiftC,
    bool HasUniversal88,
    int Universal88Count,
    bool HasVm92,
    bool HasOledCurve,
    IReadOnlyList<string> CustomScreenNames
);

public static class LConnectDeviceDetector
{
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property, out uint propertyRegDataType, StringBuilder propertyBuffer, uint propertyBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_ALLCLASSES = 0x00000004;
    private const uint SPDRP_FRIENDLYNAME = 0x0000000C;
    private const uint SPDRP_DEVICEDESC = 0x00000000;

    public static DetectedLConnectHardware DetectConnectedHardware()
    {
        bool hasH2S = false;
        bool hasH2C = false;
        bool hasVm92 = false;
        bool hasOled = false;
        var p8050Set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pA088Set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pAD21Set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IntPtr deviceInfoSet = SetupDiGetClassDevs(IntPtr.Zero, "USB", IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
        if (deviceInfoSet != (IntPtr)(-1))
        {
            try
            {
                var da = new SP_DEVINFO_DATA();
                da.cbSize = (uint)Marshal.SizeOf(da);
                uint index = 0;

                var idBuf = new StringBuilder(512);
                var nameBuf = new StringBuilder(512);

                while (SetupDiEnumDeviceInfo(deviceInfoSet, index++, ref da))
                {
                    if (!SetupDiGetDeviceInstanceId(deviceInfoSet, ref da, idBuf, 512, out _))
                    {
                        continue;
                    }

                    var pnpId = idBuf.ToString();
                    string friendlyName = "";
                    if (SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref da, SPDRP_FRIENDLYNAME, out _, nameBuf, 512, out _))
                    {
                        friendlyName = nameBuf.ToString();
                    }
                    else if (SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref da, SPDRP_DEVICEDESC, out _, nameBuf, 512, out _))
                    {
                        friendlyName = nameBuf.ToString();
                    }

                    // Collect 8.8 screen hardware IDs by interface tier to prevent double-counting child interfaces
                    if (pnpId.Contains("PID_8050", StringComparison.OrdinalIgnoreCase))
                    {
                        p8050Set.Add(pnpId);
                    }
                    else if (pnpId.Contains("PID_A088", StringComparison.OrdinalIgnoreCase))
                    {
                        pA088Set.Add(pnpId);
                    }
                    else if (pnpId.Contains("PID_AD21", StringComparison.OrdinalIgnoreCase) &&
                             (friendlyName.Contains("Secondary Display", StringComparison.OrdinalIgnoreCase) ||
                              friendlyName.Contains("8.8", StringComparison.OrdinalIgnoreCase)))
                    {
                        pAD21Set.Add(pnpId);
                    }

                    // Hydroshift S (VID_1CBE&PID_A034, etc.)
                    if (pnpId.Contains("PID_A034", StringComparison.OrdinalIgnoreCase) ||
                        pnpId.Contains("PID_A035", StringComparison.OrdinalIgnoreCase) ||
                        friendlyName.Contains("H2S", StringComparison.OrdinalIgnoreCase) ||
                        friendlyName.Contains("Hydroshift-S", StringComparison.OrdinalIgnoreCase) ||
                        friendlyName.Contains("lianli-H2S", StringComparison.OrdinalIgnoreCase))
                    {
                        hasH2S = true;
                    }

                    // Hydroshift C (VID_1CBE&PID_A036, etc.)
                    if (pnpId.Contains("PID_A036", StringComparison.OrdinalIgnoreCase) ||
                        pnpId.Contains("PID_A037", StringComparison.OrdinalIgnoreCase) ||
                        friendlyName.Contains("H2C", StringComparison.OrdinalIgnoreCase) ||
                        friendlyName.Contains("Hydroshift-C", StringComparison.OrdinalIgnoreCase) ||
                        friendlyName.Contains("lianli-H2C", StringComparison.OrdinalIgnoreCase))
                    {
                        hasH2C = true;
                    }

                    // VM 9.2
                    if (pnpId.Contains("PID_A092", StringComparison.OrdinalIgnoreCase) ||
                        friendlyName.Contains("VM 9.2", StringComparison.OrdinalIgnoreCase))
                    {
                        hasVm92 = true;
                    }

                    // OLED Curve
                    if (pnpId.Contains("PID_A090", StringComparison.OrdinalIgnoreCase) ||
                        friendlyName.Contains("OLED Curve", StringComparison.OrdinalIgnoreCase) ||
                        friendlyName.Contains("Hydroshift-OLED", StringComparison.OrdinalIgnoreCase))
                    {
                        hasOled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error scanning SetupAPI PnP USB devices.", ex);
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
        }

        int u88Count = 0;
        if (p8050Set.Count > 0)
        {
            u88Count = p8050Set.Count;
        }
        else if (pA088Set.Count > 0)
        {
            u88Count = pA088Set.Count;
        }
        else if (pAD21Set.Count > 0)
        {
            u88Count = pAD21Set.Count;
        }

        bool hasU88 = u88Count > 0;
        u88Count = Math.Clamp(u88Count, 1, 4);

        // Fetch custom device names from L-Connect profiles
        var customNames = FetchCustomScreenNames();

        return new DetectedLConnectHardware(
            HasHydroshiftS: hasH2S,
            HasHydroshiftC: hasH2C,
            HasUniversal88: hasU88,
            Universal88Count: u88Count,
            HasVm92: hasVm92,
            HasOledCurve: hasOled,
            CustomScreenNames: customNames
        );
    }

    private static List<string> FetchCustomScreenNames()
    {
        var names = new List<string>();
        try
        {
            var profileDir = Path.Combine(LConnectPaths.ProgramDataRoot, "profile");
            if (!Directory.Exists(profileDir)) return names;

            foreach (var file in Directory.GetFiles(profileDir)
                .Where(f => !f.Contains("backup", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    var json = ReadGZipJsonContent(file);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("Name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                    {
                        var nameStr = nameProp.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(nameStr) &&
                            (root.TryGetProperty("PortraitTemplateConfig", out _) ||
                             root.TryGetProperty("LandscapeTemplateConfig", out _) ||
                             root.TryGetProperty("IsLandscape", out _)))
                        {
                            if (!names.Contains(nameStr, StringComparer.OrdinalIgnoreCase))
                            {
                                names.Add(nameStr);
                            }
                        }
                    }
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Error fetching custom screen names from L-Connect profiles.", ex);
        }

        return names;
    }

    private static string ReadGZipJsonContent(string filePath)
    {
        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzipStream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch
        {
            try
            {
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fileStream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch
            {
                return "";
            }
        }
    }
}
