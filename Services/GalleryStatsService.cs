using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace ThemeEditorCSharp.Services;

public sealed class GalleryStatsService : IGalleryStatsService
{
    private readonly HttpClient httpClient;

    public GalleryStatsService(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<IReadOnlyList<GalleryThemeStats>> LoadStatsAsync(string apiBaseUrl, string voterKey)
    {
        var json = await httpClient.GetStringAsync(
            $"{apiBaseUrl.TrimEnd('/')}/themes/stats?voterKey={Uri.EscapeDataString(voterKey)}").ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("themes", out var themesElement) ||
            themesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<GalleryThemeStats>();
        }

        var result = new List<GalleryThemeStats>();
        foreach (var element in themesElement.EnumerateArray())
        {
            var stats = ParseStats(element);
            if (!string.IsNullOrWhiteSpace(stats.Id))
            {
                result.Add(stats);
            }
        }

        return result;
    }

    public async Task<GalleryThemeStats> SubmitVoteAsync(string apiBaseUrl, string themeId, string voterKey, int rating)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(new { voterKey, rating }),
            System.Text.Encoding.UTF8,
            "application/json");
        var response = await httpClient.PostAsync(
            $"{apiBaseUrl.TrimEnd('/')}/themes/{Uri.EscapeDataString(themeId)}/vote",
            content).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return ParseStatsResponse(json, themeId);
    }

    public async Task<GalleryThemeStats> NotifyDownloadAsync(string apiBaseUrl, string themeId)
    {
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(
            $"{apiBaseUrl.TrimEnd('/')}/themes/{Uri.EscapeDataString(themeId)}/download",
            content).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return ParseStatsResponse(json, themeId);
    }

    private static GalleryThemeStats ParseStatsResponse(string json, string fallbackId)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseStats(doc.RootElement, fallbackId);
    }

    private static GalleryThemeStats ParseStats(JsonElement element, string fallbackId = "")
    {
        return new GalleryThemeStats(
            GetJsonString(element, "id", fallbackId),
            GetJsonInt(element, "downloads"),
            GetJsonInt(element, "voteCount"),
            GetJsonDouble(element, "averageRating"),
            GetJsonInt(element, "userRating"));
    }

    private static string GetJsonString(JsonElement element, string name, string fallback = "")
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static int GetJsonInt(JsonElement element, string name, int fallback = 0)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => fallback
        };
    }

    private static double GetJsonDouble(JsonElement element, string name, double fallback = 0)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            _ => fallback
        };
    }
}
