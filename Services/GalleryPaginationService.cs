namespace ThemeEditorCSharp.Services;

public sealed class GalleryPaginationService : IGalleryPaginationService
{
    public int GetTotalPages(int itemCount, int pageSize)
    {
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be greater than zero.");
        }

        return Math.Max(1, (int)Math.Ceiling(Math.Max(0, itemCount) / (double)pageSize));
    }

    public GalleryPageResult<T> GetPage<T>(
        IReadOnlyList<T> items,
        int requestedPage,
        int pageSize)
    {
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be greater than zero.");
        }

        var totalPages = GetTotalPages(items.Count, pageSize);
        var currentPage = Math.Clamp(requestedPage, 1, totalPages);
        var pageItems = items
            .Skip((currentPage - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        var first = items.Count > 0 ? ((currentPage - 1) * pageSize) + 1 : 0;
        var last = items.Count > 0 ? Math.Min(currentPage * pageSize, items.Count) : 0;

        return new GalleryPageResult<T>(pageItems, currentPage, totalPages, first, last);
    }
}
