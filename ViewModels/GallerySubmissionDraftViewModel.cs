namespace ThemeEditorCSharp.ViewModels;

public sealed class GallerySubmissionDraftViewModel : ObservableObject
{
    private string themeName = "";
    private string description = "";
    private string selectedDeviceModel = "";

    public string PackagePath { get; init; } = "";
    public string DefaultName { get; init; } = "";
    public string DeviceModel { get; init; } = "";

    public string ThemeName
    {
        get => string.IsNullOrWhiteSpace(themeName) ? DefaultName : themeName;
        set => SetProperty(ref themeName, value);
    }

    public string Description
    {
        get => description;
        set => SetProperty(ref description, value);
    }

    public string SelectedDeviceModel
    {
        get => string.IsNullOrWhiteSpace(selectedDeviceModel) ? DeviceModel : selectedDeviceModel;
        set => SetProperty(ref selectedDeviceModel, value);
    }
}
