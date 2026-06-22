using System.Configuration;
using System.Data;
using System.Windows;

namespace ThemeEditorCSharp;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Services.AppLogger.Error("Unhandled UI exception.", args.Exception);
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppContext.BaseDirectory, "unhandled_error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{Environment.NewLine}{args.Exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
            }

            MessageBox.Show(GetReadableExceptionMessage(args.Exception), "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        base.OnStartup(e);
    }

    private static string GetReadableExceptionMessage(Exception exception)
    {
        var current = exception;
        while (current.InnerException != null &&
               (current is System.Reflection.TargetInvocationException ||
                current.GetType().FullName == "System.Windows.Markup.XamlParseException"))
        {
            current = current.InnerException;
        }

        return current.Message;
    }
}

