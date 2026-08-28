using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.Services;

public sealed class GalleryFilterService : IGalleryFilterService
{
    public IReadOnlyList<GalleryThemeItem> Filter(
        IEnumerable<GalleryThemeItem> themes,
        GalleryFilterOptions options)
    {
        IEnumerable<GalleryThemeItem> filteredThemes = themes.Where(theme =>
            MatchesDeviceFilter(theme, options.SelectedDevices) &&
            MatchesOrientationFilter(theme, options.SelectedOrientations) &&
            options.SelectedInstallStates.Contains(theme.IsInstalled) &&
            (options.MinimumRating <= 0 ||
             (theme.VoteCount > 0 && theme.AverageRating >= options.MinimumRating)) &&
            MatchesAuthorFilter(theme, options.SelectedAuthors));

        filteredThemes = options.SortMode switch
        {
            "downloads" => filteredThemes
                .OrderByDescending(theme => theme.DownloadCount)
                .ThenBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase),
            "rating" => filteredThemes
                .OrderByDescending(theme => theme.VoteCount > 0)
                .ThenByDescending(theme => theme.AverageRating)
                .ThenByDescending(theme => theme.VoteCount)
                .ThenBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase),
            "votes" => filteredThemes
                .OrderByDescending(theme => theme.VoteCount)
                .ThenByDescending(theme => theme.AverageRating)
                .ThenBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase),
            "name" => filteredThemes
                .OrderBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase),
            "newest" => filteredThemes
                .OrderByDescending(theme => theme.GallerySortTimeUtc)
                .ThenByDescending(theme => theme.GalleryOrder)
                .ThenBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase),
            _ => filteredThemes
        };

        return filteredThemes.ToList();
    }

    public static bool MatchesDeviceFilter(GalleryThemeItem theme, ISet<string> selectedDevices)
    {
        if (selectedDevices.Count == 0)
        {
            return false;
        }

        return selectedDevices.Contains(GalleryDeviceModelNormalizer.Normalize(theme.DeviceModel));
    }

    public static bool MatchesOrientationFilter(
        GalleryThemeItem theme,
        ISet<string> selectedOrientations)
    {
        if (selectedOrientations.Count == 0)
        {
            return false;
        }

        if (string.Equals(theme.DeviceModel, GalleryDeviceModelNormalizer.Vm92DeviceModel, StringComparison.OrdinalIgnoreCase))
        {
            return selectedOrientations.Contains("landscape");
        }

        var orientation = (theme.Orientation ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(orientation))
        {
            return selectedOrientations.Contains(orientation);
        }

        return true;
    }

    public static bool MatchesAuthorFilter(GalleryThemeItem theme, ISet<string>? selectedAuthors)
    {
        if (selectedAuthors == null || selectedAuthors.Count == 0)
        {
            return true;
        }

        var author = (theme.Author ?? "").Trim();
        return selectedAuthors.Contains(author);
    }
}
