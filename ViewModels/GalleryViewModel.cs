using System.Collections.ObjectModel;
using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.ViewModels;

public sealed class GalleryViewModel : ObservableObject
{
    private int currentPage = 1;
    private int filteredThemeCount;
    private bool animateVideoPreviews = true;
    private bool backgroundPreviewPaused;

    public ObservableCollection<GalleryThemeItem> Themes { get; } = new();
    public ObservableCollection<GalleryThemeItem> VisibleThemes { get; } = new();

    public int CurrentPage
    {
        get => currentPage;
        set => SetProperty(ref currentPage, value);
    }

    public int FilteredThemeCount
    {
        get => filteredThemeCount;
        set => SetProperty(ref filteredThemeCount, value);
    }

    public bool AnimateVideoPreviews
    {
        get => animateVideoPreviews;
        set => SetProperty(ref animateVideoPreviews, value);
    }

    public bool BackgroundPreviewPaused
    {
        get => backgroundPreviewPaused;
        set => SetProperty(ref backgroundPreviewPaused, value);
    }
}
