using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private string statusText = "Ready.";
    private bool isBusy;

    public MainWindowViewModel()
    {
        LayerView = CollectionViewSource.GetDefaultView(Layers);
        LayerView.Filter = item => item is LayerRow layer && !layer.IsEditorMetadata;
    }

    public ObservableRangeCollection<LayerRow> Layers { get; } = new();
    public ICollectionView LayerView { get; }
    public ObservableCollection<LayerGroup> LayerGroups { get; } = new();
    public ObservableCollection<GraphStyleOption> GraphStyles { get; } = new();
    public ObservableCollection<TemplateOption> TemplateOptions { get; } = new();
    public ObservableCollection<TemplateOption> LocalThemes { get; } = new();
    public ObservableCollection<TemplateOption> LocalVisibleThemes { get; } = new();
    public ObservableCollection<BackupItem> BackupItems { get; } = new();
    public GalleryViewModel Gallery { get; } = new();
    public ObservableCollection<GalleryThemeItem> GalleryThemes => Gallery.Themes;
    public ObservableCollection<GalleryThemeItem> GalleryVisibleThemes => Gallery.VisibleThemes;

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        set => SetProperty(ref isBusy, value);
    }
}
