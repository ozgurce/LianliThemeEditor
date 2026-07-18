using Microsoft.Win32;

namespace PhoneControl;

public static class LConnectPaths
{
    public static string ProgramDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Lian-Li",
        "L-Connect 3");

    public static string ProgramFilesRoot { get; } = ResolveProgramFilesRoot();

    private static string ResolveProgramFilesRoot()
    {
        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Lian-Li",
            "L-Connect 3");

        foreach (var candidate in EnumerateInstallCandidates(fallback))
        {
            try
            {
                var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"')));
                if (File.Exists(Path.Combine(path, "L-Connect 3.exe")) ||
                    File.Exists(Path.Combine(path, "lianli.ThemeEngine.dll")))
                {
                    return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
            }
            catch
            {
            }
        }

        return fallback;
    }

    private static IEnumerable<string> EnumerateInstallCandidates(string fallback)
    {
        var env = Environment.GetEnvironmentVariable("LIANLI_LCONNECT_DIR");
        if (!string.IsNullOrWhiteSpace(env)) yield return env;

        foreach (var value in ReadRegistryInstallLocations())
        {
            yield return value;
        }

        foreach (var imagePath in ReadServiceImagePaths())
        {
            var directory = TryGetExecutableDirectory(imagePath);
            if (!string.IsNullOrWhiteSpace(directory)) yield return directory;
        }

        yield return fallback;
    }

    private static IEnumerable<string> ReadRegistryInstallLocations()
    {
        var keys = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\L-Connect 3",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Lian Li L-Connect 3",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Lian-Li L-Connect 3",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\L-Connect 3",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Lian Li L-Connect 3",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Lian-Li L-Connect 3"
        };

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var key in keys)
            {
                using var subKey = hive.OpenSubKey(key);
                var value = subKey?.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(value)) yield return value;
            }
        }
    }

    private static IEnumerable<string> ReadServiceImagePaths()
    {
        foreach (var serviceName in new[] { "LConnectService", "LConnectServiceWatcher" })
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            var value = key?.GetValue("ImagePath") as string;
            if (!string.IsNullOrWhiteSpace(value)) yield return value;
        }
    }

    private static string? TryGetExecutableDirectory(string imagePath)
    {
        var expanded = Environment.ExpandEnvironmentVariables(imagePath.Trim());
        var match = System.Text.RegularExpressions.Regex.Match(expanded, "^\"([^\"]+\\.exe)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var exe = match.Success
            ? match.Groups[1].Value
            : expanded.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(part => part.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(exe) ? null : Path.GetDirectoryName(exe);
    }
}
