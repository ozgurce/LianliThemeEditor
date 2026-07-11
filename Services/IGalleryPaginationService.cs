namespace ThemeEditorCSharp.Services;

public interface IGalleryPaginationService
{
    int GetTotalPages(int itemCount, int pageSize);

    GalleryPageResult<T> GetPage<T>(
        IReadOnlyList<T> items,
        int requestedPage,
        int pageSize);
}
