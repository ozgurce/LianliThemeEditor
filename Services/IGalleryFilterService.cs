using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.Services;

public interface IGalleryFilterService
{
    IReadOnlyList<GalleryThemeItem> Filter(
        IEnumerable<GalleryThemeItem> themes,
        GalleryFilterOptions options);
}
