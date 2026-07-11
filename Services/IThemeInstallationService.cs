namespace ThemeEditorCSharp.Services;

public interface IThemeInstallationService
{
    Task<string> ActivateAsync(
        string requestedId,
        string templatePath,
        string backgroundPath,
        Func<Task<IReadOnlyList<string>>> getRegisteredMatches,
        Func<string, Task<bool>> tryApplyRegistered,
        Func<Task<string>> importAndApply);
}
