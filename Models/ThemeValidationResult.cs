namespace ThemeEditorCSharp.Models;

public sealed class ThemeValidationIssue
{
    public string Severity { get; init; } = "Info";
    public string Message { get; init; } = "";
}

public sealed class ThemeValidationResult
{
    public bool IsValid => Issues.All(issue => !string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase));
    public string DeviceModel { get; init; } = "";
    public string TemplateId { get; init; } = "";
    public string TemplateFile { get; init; } = "";
    public List<ThemeValidationIssue> Issues { get; } = new();
}
