using System.IO.Compression;
using System.IO;

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

        foreach (var path in additionalFiles.Concat(Directory.Exists(AppLogger.LogDirectory)
                      ? Directory.EnumerateFiles(AppLogger.LogDirectory, "*.log").TakeLast(5)
                     : Array.Empty<string>()).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { archive.CreateEntryFromFile(path, Path.Combine("files", Path.GetFileName(path)), CompressionLevel.Optimal); }
            catch (Exception ex) { AppLogger.Error($"Diagnostic file could not be added: {path}", ex); }
        }
        return outputPath;
    }
}
