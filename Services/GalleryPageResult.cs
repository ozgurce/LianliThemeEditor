namespace ThemeEditorCSharp.Services;

public sealed record GalleryPageResult<T>(
    IReadOnlyList<T> Items,
    int CurrentPage,
    int TotalPages,
    int FirstItem,
    int LastItem);
