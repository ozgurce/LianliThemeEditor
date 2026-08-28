namespace PhoneControl;

public sealed class PhoneControlOptions
{
    public string Host { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 37373;
    public bool UsePin { get; set; } = true;
    public string Token { get; set; } = "";
    public string Language { get; set; } = "en";
}

public sealed record ApiError(string Code, string Message);

public sealed record LConnectHttpResult(
    int? StatusCode,
    int? Port,
    string RequestMode,
    string ReasonPhrase,
    string Body,
    string Error)
{
    public bool IsHttpSuccess => StatusCode is >= 200 and <= 299;
}

public sealed record DeviceInfo(
    string Id,
    string Name,
    string Model,
    string Path,
    string SelectedTemplateId,
    int? ScreenBrightness = null);

public sealed record ThemeInfo(
    string Id,
    string Name,
    string PreviewUrl,
    bool CanDelete,
    bool IsSelected);

public sealed record ApplyThemeRequest(string ThemeId);

public sealed record BrightnessRequest(int Value);

public sealed record LightingEffectRequest(string Effect, int Brightness = 70, string? Color = null, string[]? Colors = null, int Speed = 50, int Direction = 0, bool Merge = false, bool ApplyAll = false);

public sealed record LightingEffectInfo(string Id, string Name, string Accent, int ColorCount);

public sealed record LightingEffectState(
    string Effect,
    int Brightness,
    string[] Colors,
    int Speed,
    int Direction,
    bool Merge);

public sealed record WirelessFanGroupInfo(
    string Id,
    string Name,
    string MacStr,
    int FanCount,
    int LedCount,
    int Group,
    string DeviceType,
    int SortIndex = 0);

public sealed record AccessSettingsRequest(bool UsePin, string? Token = null);

public sealed record CommandResult(
    bool Success,
    string Message,
    string? AppliedCommand = null,
    IReadOnlyList<string>? TriedCommands = null);

public sealed record LConnectStatus(
    bool Online,
    int? Port,
    string Mode,
    string Message);
