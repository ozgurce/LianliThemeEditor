namespace ThemeEditorCSharp.Services;

public interface IGalleryStatsService
{
    Task<IReadOnlyList<GalleryThemeStats>> LoadStatsAsync(string apiBaseUrl, string voterKey);

    Task<GalleryThemeStats> SubmitVoteAsync(string apiBaseUrl, string themeId, string voterKey, int rating);

    Task<GalleryThemeStats> NotifyDownloadAsync(string apiBaseUrl, string themeId);
}
