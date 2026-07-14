namespace PhoneControl;

public static class LConnectPaths
{
    public static string ProgramDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Lian-Li",
        "L-Connect 3");

    public static string ProgramFilesRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "Lian-Li",
        "L-Connect 3");
}
