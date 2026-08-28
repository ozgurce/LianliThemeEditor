using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.Services;

public interface IThemePackageValidationService
{
    ThemeValidationResult Validate(
        string packagePath,
        IEnumerable<string>? installedTemplateIds = null,
        string fallbackDeviceModel = "");
}
