using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ThemeEditorCSharp.Services;

public sealed class GallerySubmission
{
    public string ThemeName { get; init; } = "";
    public string Author { get; init; } = "";
    public string Contact { get; init; } = "";
    public string Description { get; init; } = "";
    public string DeviceModel { get; init; } = "";
    public string PackagePath { get; init; } = "";
    public string PreviewPath { get; init; } = "";
}

public sealed class GallerySubmissionService : IGallerySubmissionService
{
    private const string ApiUrl = "https://lianli-theme-gallery.ozgurce.workers.dev";
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LianLiThemeEditor");
        return client;
    }

    public async Task<string> SubmitAsync(GallerySubmission submission, CancellationToken cancellationToken = default)
    {
        using var form = CreateForm(submission);
        var response = await Client.PostAsync(ApiUrl + "/submissions", form, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(text);
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
        throw new InvalidOperationException(doc.RootElement.TryGetProperty("error", out var error) ? error.GetString() : "Gallery submission failed.");
    }

    public static MultipartFormDataContent CreateForm(GallerySubmission submission)
    {
        var form = new MultipartFormDataContent();
        AddString(form, "themeName", submission.ThemeName);
        AddString(form, "author", submission.Author);
        AddString(form, "contact", submission.Contact);
        AddString(form, "description", submission.Description);
        AddString(form, "deviceModel", submission.DeviceModel);
        AddFile(form, "package", submission.PackagePath);
        if (!string.IsNullOrWhiteSpace(submission.PreviewPath) && File.Exists(submission.PreviewPath))
        {
            AddFile(form, "preview", submission.PreviewPath, "image/png");
        }
        return form;
    }

    private static void AddString(MultipartFormDataContent form, string name, string value)
    {
        var content = new StringContent(value ?? "", Encoding.UTF8);
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = Quote(name)
        };
        form.Add(content);
    }

    private static void AddFile(MultipartFormDataContent form, string name, string path, string contentType = "application/octet-stream")
    {
        var stream = File.OpenRead(path);
        var content = new StreamContent(stream);
        try
        {
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = Quote(name),
                FileName = Quote(Path.GetFileName(path))
            };
            form.Add(content);
        }
        catch
        {
            content.Dispose();
            stream.Dispose();
            throw;
        }
    }

    private static string Quote(string value) => "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
