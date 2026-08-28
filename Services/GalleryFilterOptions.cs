namespace ThemeEditorCSharp.Services;

public sealed record GalleryFilterOptions(
    ISet<string> SelectedDevices,
    ISet<string> SelectedOrientations,
    ISet<bool> SelectedInstallStates,
    double MinimumRating,
    string SortMode,
    ISet<string>? SelectedAuthors = null);
