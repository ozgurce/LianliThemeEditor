using System.IO.Compression;
using System.IO;
using ThemeEditorCSharp.Services;

namespace ThemeEditorCSharp.Services;

public sealed class DiagnosticService
{
    public string CreatePackage(string outputPath, string summary, IEnumerable<string> additionalFiles)
    {
        return CreatePackage(outputPath, summary, additionalFiles, Array.Empty<(string Name, string Content)>());
    }

    public string CreatePackage(
        string outputPath,
        string summary,
        IEnumerable<string> additionalFiles,
        IEnumerable<(string Name, string Content)> textEntries)
    {
        if (File.Exists(outputPath)) File.Delete(outputPath);
        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        var summaryEntry = archive.CreateEntry("diagnostics.txt", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(summaryEntry.Open())) writer.Write(summary);

        foreach (var (name, content) in textEntries)
        {
            try
            {
                var entry = archive.CreateEntry(name.Replace('\\', '/'), CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content ?? "");
            }
            catch (Exception ex) { AppLogger.Error($"Diagnostic text entry could not be added: {name}", ex); }
        }

        foreach (var path in additionalFiles
                     .Concat(EnumerateAppLogs())
                     .Concat(EnumerateLConnectDiagnosticsFiles())
                     .Where(File.Exists)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                archive.CreateEntryFromFile(
                    path,
                    Path.Combine("files", GetDiagnosticEntryName(path)),
                    CompressionLevel.Optimal);
            }
            catch (Exception ex) { AppLogger.Error($"Diagnostic file could not be added: {path}", ex); }
        }
        return outputPath;
    }

    private static IEnumerable<string> EnumerateAppLogs() =>
        Directory.Exists(AppLogger.LogDirectory)
            ? Directory.EnumerateFiles(AppLogger.LogDirectory, "*.log").TakeLast(5)
            : Array.Empty<string>();

    private static IEnumerable<string> EnumerateLConnectDiagnosticsFiles()
    {
        var root = LConnectPaths.ProgramDataRoot;
        foreach (var file in EnumerateRecentFiles(Path.Combine(root, "logs"), "*.log", 12))
        {
            yield return file;
        }

        foreach (var file in EnumerateRecentFiles(Path.Combine(root, "profile"), "*", 12))
        {
            yield return file;
        }

        foreach (var subdir in new[] { "template", "preview", "video", "temp" })
        {
            foreach (var file in EnumerateRecentFiles(Path.Combine(root, "vm-9.2-inch", subdir), "*", 20))
            {
                yield return file;
            }
        }

        foreach (var file in EnumerateRecentFiles(Path.Combine(root, "uploaded", "vm-9.2-inch"), "*", 20))
        {
            yield return file;
        }

        var phoneLinkLog = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Lian-Li Phone Link",
            "diagnostics.log");
        if (File.Exists(phoneLinkLog))
        {
            yield return phoneLinkLog;
        }
    }

    private static IEnumerable<string> EnumerateRecentFiles(string directory, string pattern, int count) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(count)
            : Array.Empty<string>();

    private static string GetDiagnosticEntryName(string path)
    {
        var lConnectRoot = LConnectPaths.ProgramDataRoot;
        if (path.StartsWith(lConnectRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(
                "l-connect",
                Path.GetRelativePath(lConnectRoot, path));
        }

        if (path.StartsWith(AppLogger.LogDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine("app", Path.GetFileName(path));
        }

        return Path.GetFileName(path);
    }
}
