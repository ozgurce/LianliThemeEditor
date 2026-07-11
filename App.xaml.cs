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

            if (IsCriticalException(args.Exception))
            {
                args.Handled = false;
                return;
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

    private static bool IsCriticalException(Exception exception)
    {
        var current = exception;
        while (current is System.Reflection.TargetInvocationException && current.InnerException != null)
        {
            current = current.InnerException;
        }

        return current is OutOfMemoryException or StackOverflowException or AccessViolationException;
    }
}


