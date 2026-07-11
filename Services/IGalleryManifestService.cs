using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.Services;

public interface IGalleryManifestService
{
    bool TryLoadCachedManifestJson(out string json);

    Task<string> LoadRemoteManifestJsonAsync();

    IReadOnlyList<GalleryThemeItem> LoadThemesFromJson(
        string json,
        string basePathOrUrl,
        bool isRemote);
}
