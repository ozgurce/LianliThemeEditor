using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using ThemeEditorCSharp.Models;

namespace ThemeEditorCSharp.Services;

public sealed class GalleryManifestService : IGalleryManifestService
{
    private const string GalleryManifestUrl = "https://raw.githubusercontent.com/ozgurce/LianliThemeEditor/main/templates/gallery.json";
    private const string GalleryContentsApiUrl = "https://api.github.com/repos/ozgurce/LianliThemeEditor/contents/templates/gallery.json?ref=main";
    private readonly HttpClient httpClient;
    private static string ManifestCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LianLiThemeEditor", "GalleryCache", "gallery.json");

    public GalleryManifestService(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public bool TryLoadCachedManifestJson(out string json)
    {
        json = "";
        try
        {
            if (!File.Exists(ManifestCachePath))
            {
                return false;
            }

            json = CleanJson(File.ReadAllText(ManifestCachePath, System.Text.Encoding.UTF8));
            return !string.IsNullOrWhiteSpace(json);
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> LoadRemoteManifestJsonAsync()
    {
        var cacheBustUrl = $"{GalleryManifestUrl}?cacheBust={Guid.NewGuid():N}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GalleryContentsApiUrl);
            request.Headers.UserAgent.ParseAdd("LianLiThemeEditor");
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };
            using var response = await httpClient.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var apiJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(apiJson);
            var encoded = GetJsonString(doc.RootElement, "content");
            if (!string.IsNullOrWhiteSpace(encoded))
            {
                var bytes = Convert.FromBase64String(Regex.Replace(encoded, @"\s+", ""));
                var json = CleanJson(System.Text.Encoding.UTF8.GetString(bytes));
                SaveManifestCache(json);
                return json;
            }
        }
        catch
        {
            // Fall back to raw GitHub below.
        }

        try
        {
            using var fastRawResponse = await httpClient.GetAsync(cacheBustUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            fastRawResponse.EnsureSuccessStatusCode();
            var json = CleanJson(await fastRawResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
            SaveManifestCache(json);
            return json;
        }
        catch
        {
            // Fall back to raw GitHub with a cache-busting query string below.
        }

        using var rawResponse = await httpClient.GetAsync(cacheBustUrl).ConfigureAwait(false);
        rawResponse.EnsureSuccessStatusCode();
        var fallbackJson = CleanJson(await rawResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
        SaveManifestCache(fallbackJson);
        return fallbackJson;
    }

    private static void SaveManifestCache(string json)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ManifestCachePath)!);
            File.WriteAllText(ManifestCachePath, json, System.Text.Encoding.UTF8);
        }
        catch
        {
        }
    }

    public IReadOnlyList<GalleryThemeItem> LoadThemesFromJson(
        string json,
        string basePathOrUrl,
        bool isRemote)
    {
        using var doc = JsonDocument.Parse(CleanJson(json));
        var root = doc.RootElement;
        var themesElement = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("themes", out var themes)
                ? themes
                : default;
        if (themesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<GalleryThemeItem>();
        }

        var result = new List<GalleryThemeItem>();
        var galleryOrder = 0;
        foreach (var element in themesElement.EnumerateArray())
        {
            galleryOrder++;
            var deviceModel = GalleryDeviceModelNormalizer.Normalize(GetJsonString(element, "deviceModel"));
            if (deviceModel is not ("hydroshift-ii-lcd-s" or "hydroshift-ii-lcd-c" or GalleryDeviceModelNormalizer.UniversalScreenDeviceModel or GalleryDeviceModelNormalizer.Vm92DeviceModel or GalleryDeviceModelNormalizer.OledCurveDeviceModel))
            {
                continue;
            }

            var packageUrl = ResolveGalleryPath(GetJsonString(element, "packageUrl"), basePathOrUrl, isRemote);
            if (string.IsNullOrWhiteSpace(packageUrl))
            {
                packageUrl = ResolveGalleryPath(GetJsonString(element, "package"), basePathOrUrl, isRemote);
            }

            var previewUrl = ResolveGalleryPath(GetJsonString(element, "previewUrl"), basePathOrUrl, isRemote);
            if (string.IsNullOrWhiteSpace(previewUrl))
            {
                previewUrl = ResolveGalleryPath(GetJsonString(element, "preview"), basePathOrUrl, isRemote);
            }

            if (string.IsNullOrWhiteSpace(packageUrl) || !IsGitHubGalleryAssetUrl(packageUrl))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(previewUrl) && !IsGitHubGalleryAssetUrl(previewUrl))
            {
                previewUrl = "";
            }

            var id = GetJsonString(element, "id");
            var name = GetJsonString(element, "name", "Theme");
            var deviceName = GetJsonString(element, "deviceName");
            var description = GetJsonString(element, "description");
            result.Add(new GalleryThemeItem
            {
                Id = string.IsNullOrWhiteSpace(id) ? Path.GetFileNameWithoutExtension(packageUrl) : id,
                Name = name,
                Author = GetJsonString(element, "author", "Unknown"),
                Description = description,
                Version = GetJsonString(element, "version"),
                Changelog = GetJsonString(element, "changelog"),
                DeviceModel = deviceModel,
                DeviceName = deviceName,
                Orientation = GetGalleryThemeOrientation(element, packageUrl, previewUrl, id, name, deviceName, description),
                PackageUrl = packageUrl,
                PreviewUrl = previewUrl,
                GallerySortTimeUtc = GetGalleryThemeSortTimeUtc(element),
                GalleryOrder = galleryOrder
            });
        }

        return result;
    }

    public static string CleanJson(string json)
    {
        return (json ?? "").TrimStart('\uFEFF', '\u200B', '\r', '\n', ' ', '\t');
    }

    public static string ResolveGalleryPath(string value, string basePathOrUrl, bool isRemote)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return value;
        }

        value = value.Replace('\\', '/').TrimStart('/');
        return isRemote
            ? new Uri(new Uri(basePathOrUrl), value).ToString()
            : Path.GetFullPath(Path.Combine(basePathOrUrl, value.Replace('/', Path.DirectorySeparatorChar)));
    }

    public static bool IsGitHubGalleryAssetUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("lianli-theme-gallery.ozgurce.workers.dev", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetGalleryThemeOrientation(
        JsonElement element,
        string packageUrl,
        string previewUrl,
        string id,
        string name,
        string deviceName,
        string description)
    {
        foreach (var propertyName in new[] { "orientation", "layout", "screenOrientation" })
        {
            var value = NormalizeGalleryOrientation(GetJsonString(element, propertyName));
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        foreach (var propertyName in new[] { "isLandscape", "landscape" })
        {
            if (element.TryGetProperty(propertyName, out var property) &&
                property.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return property.GetBoolean() ? "landscape" : "portrait";
            }
        }

        foreach (var propertyName in new[] { "isPortrait", "portrait" })
        {
            if (element.TryGetProperty(propertyName, out var property) &&
                property.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return property.GetBoolean() ? "portrait" : "landscape";
            }
        }

        var inferred = InferGalleryThemeOrientation(id, name, deviceName, description, packageUrl, previewUrl);
        return string.IsNullOrWhiteSpace(inferred) ? "" : inferred;
    }

    private static string InferGalleryThemeOrientation(params string[] values)
    {
        var text = string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        if (Regex.IsMatch(text, @"(^|[\s_\-./])(portrait|vertical|vert|ver)(?=$|[\s_\-./])|480\s*x\s*1920", RegexOptions.IgnoreCase))
        {
            return "portrait";
        }

        if (Regex.IsMatch(text, @"(^|[\s_\-./])(landscape|horizontal|horisontal|horiz|hor)(?=$|[\s_\-./])|(^|[\s_\-./])h(?=$|[\s_\-./])|1920\s*x\s*480", RegexOptions.IgnoreCase))
        {
            return "landscape";
        }

        return "";
    }

    private static string NormalizeGalleryOrientation(string value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant()
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
        return normalized switch
        {
            "landscape" or "wide" or "horizontal" => "landscape",
            "portrait" or "vertical" => "portrait",
            _ => ""
        };
    }

    private static DateTime GetGalleryThemeSortTimeUtc(JsonElement element)
    {
        foreach (var propertyName in new[] { "approvedAt", "approvalTime", "approved", "publishedAt", "submittedAt", "submissionTime", "submitted" })
        {
            var value = GetJsonString(element, propertyName);
            if (DateTimeOffset.TryParse(value, out var parsed))
            {
                return parsed.UtcDateTime;
            }
        }

        foreach (var propertyName in new[] { "uploadedAt", "uploaded", "createdAt", "date", "updatedAt" })
        {
            var value = GetJsonString(element, propertyName);
            if (DateTimeOffset.TryParse(value, out var parsed))
            {
                return parsed.UtcDateTime;
            }
        }

        return DateTime.MinValue;
    }

    private static string GetJsonString(JsonElement element, string name, string fallback = "")
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }
}
