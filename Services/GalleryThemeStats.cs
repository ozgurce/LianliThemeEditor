namespace ThemeEditorCSharp.Services;

public sealed record GalleryThemeStats(
    string Id,
    int Downloads,
    int VoteCount,
    double AverageRating,
    int UserRating);
