using System.Text;
using System.IO;

namespace ThemeEditorCSharp.Services;

public static class AppLogger
{
    private static readonly object Sync = new();
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LianLiThemeEditor",
        "Logs");

    public static string CurrentLogPath => Path.Combine(LogDirectory, $"editor-{DateTime.Today:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warning(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var line = $"{DateTimeOffset.Now:O} [{level}] {message}";
            if (exception != null)
            {
                line += Environment.NewLine + exception;
            }

            lock (Sync)
            {
                File.AppendAllText(CurrentLogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
        }
    }
}
