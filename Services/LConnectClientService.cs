using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text;

namespace ThemeEditorCSharp.Services;

public sealed record LConnectHttpResult(
    int? StatusCode,
    int? Port,
    string RequestMode,
    string ReasonPhrase,
    string Body,
    string Error)
{
    public bool IsHttpSuccess => StatusCode is >= 200 and <= 299;
}

public sealed class LConnectClientService
{
    private const int DefaultServicePort = 11021;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(900);
    private int? cachedServicePort;
    private LConnectRequestMode? cachedRequestMode;

    public async Task<LConnectHttpResult> SendDeviceRequestForJsonAsync(
        HttpClient client,
        string devicePath,
        string type,
        string? body)
    {
        if (cachedServicePort.HasValue && cachedRequestMode.HasValue)
        {
            var cachedResult = await SendDeviceRequestForJsonAsync(
                client,
                cachedServicePort.Value,
                devicePath,
                type,
                body,
                cachedRequestMode.Value).ConfigureAwait(false);
            if (IsMeaningfulServiceResponse(cachedResult) ||
                (AcceptsEmptyResponse(type) && IsServiceEndpointResponse(cachedResult)))
            {
                return cachedResult;
            }

            ClearCachedEndpoint();
        }

        LConnectHttpResult? lastResult = null;
        foreach (var mode in new[] { LConnectRequestMode.Legacy, LConnectRequestMode.OfficialCompatible })
        {
            var result = await SendDeviceRequestForJsonAsync(client, DefaultServicePort, devicePath, type, body, mode).ConfigureAwait(false);
            lastResult = result;
            if (IsMeaningfulServiceResponse(result))
            {
                CacheEndpoint(DefaultServicePort, mode);
                return result;
            }
        }

        if (lastResult != null &&
            (IsMeaningfulServiceResponse(lastResult) ||
             (AcceptsEmptyResponse(type) && IsServiceEndpointResponse(lastResult))))
        {
            CacheEndpoint(DefaultServicePort, LConnectRequestMode.OfficialCompatible);
            return lastResult;
        }

        var ports = await DiscoverResponsivePortsAsync(client).ConfigureAwait(false);
        foreach (var port in ports)
        {
            foreach (var mode in new[] { LConnectRequestMode.OfficialCompatible, LConnectRequestMode.Legacy })
            {
                var result = await SendDeviceRequestForJsonAsync(client, port, devicePath, type, body, mode).ConfigureAwait(false);
                lastResult = result;
                if (IsMeaningfulServiceResponse(result) ||
                    (AcceptsEmptyResponse(type) && IsServiceEndpointResponse(result)))
                {
                    CacheEndpoint(port, mode);
                    return result;
                }
            }
        }

        return lastResult ?? new LConnectHttpResult(null, null, "", "", "", "No L-Connect service port candidates were available.");
    }

    public async Task<LConnectHttpResult> SendServiceRequestForJsonAsync(
        HttpClient client,
        string action,
        string? body)
    {
        if (cachedServicePort.HasValue && cachedRequestMode.HasValue)
        {
            var cachedResult = await SendServiceRequestForJsonAsync(
                client,
                cachedServicePort.Value,
                action,
                body,
                cachedRequestMode.Value).ConfigureAwait(false);
            if (IsMeaningfulServiceResponse(cachedResult) ||
                (AcceptsEmptyResponse(action) && IsServiceEndpointResponse(cachedResult)))
            {
                return cachedResult;
            }

            ClearCachedEndpoint();
        }

        LConnectHttpResult? lastResult = null;
        foreach (var mode in new[] { LConnectRequestMode.Legacy, LConnectRequestMode.OfficialCompatible })
        {
            var result = await SendServiceRequestForJsonAsync(
                client,
                DefaultServicePort,
                action,
                body,
                mode).ConfigureAwait(false);
            lastResult = result;
            if (IsMeaningfulServiceResponse(result))
            {
                CacheEndpoint(DefaultServicePort, mode);
                return result;
            }
        }

        if (lastResult != null &&
            (IsMeaningfulServiceResponse(lastResult) ||
             (AcceptsEmptyResponse(action) && IsServiceEndpointResponse(lastResult))))
        {
            CacheEndpoint(DefaultServicePort, LConnectRequestMode.OfficialCompatible);
            return lastResult;
        }

        var ports = await DiscoverResponsivePortsAsync(client).ConfigureAwait(false);
        foreach (var port in ports)
        {
            foreach (var mode in new[] { LConnectRequestMode.OfficialCompatible, LConnectRequestMode.Legacy })
            {
                var result = await SendServiceRequestForJsonAsync(client, port, action, body, mode).ConfigureAwait(false);
                lastResult = result;
                if (IsMeaningfulServiceResponse(result) ||
                    (AcceptsEmptyResponse(action) && IsServiceEndpointResponse(result)))
                {
                    CacheEndpoint(port, mode);
                    return result;
                }
            }
        }

        return lastResult ?? new LConnectHttpResult(null, null, "", "", "", "No L-Connect service port candidates were available.");
    }

    private static async Task<LConnectHttpResult> SendDeviceRequestForJsonAsync(
        HttpClient client,
        int port,
        string devicePath,
        string type,
        string? body,
        LConnectRequestMode mode)
    {
        try
        {
            var encodedPath = Uri.EscapeDataString(Convert.ToBase64String(Encoding.UTF8.GetBytes(devicePath)));
            var url = $"http://127.0.0.1:{port}/?action=Device&devicePath={encodedPath}&type={Uri.EscapeDataString(type)}";
            using var content = CreateContent(type, body, mode);
            using var response = await client.PostAsync(url, content).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return new LConnectHttpResult(
                (int)response.StatusCode,
                port,
                mode.ToString(),
                response.ReasonPhrase ?? "",
                responseBody,
                "");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"L-Connect request failed: {type}", ex);
            return new LConnectHttpResult(null, port, mode.ToString(), "", "", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<LConnectHttpResult> SendServiceRequestForJsonAsync(
        HttpClient client,
        int port,
        string action,
        string? body,
        LConnectRequestMode mode)
    {
        try
        {
            var url = $"http://127.0.0.1:{port}/?action={Uri.EscapeDataString(action)}";
            using var content = CreateContent(action, body, mode);
            using var response = await client.PostAsync(url, content).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return new LConnectHttpResult(
                (int)response.StatusCode,
                port,
                mode.ToString(),
                response.ReasonPhrase ?? "",
                responseBody,
                "");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"L-Connect service request failed: {action}", ex);
            return new LConnectHttpResult(null, port, mode.ToString(), "", "", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static HttpContent CreateContent(string type, string? body, LConnectRequestMode mode)
    {
        if (mode == LConnectRequestMode.OfficialCompatible &&
            RequiresEmptyRequestBody(type) &&
            (string.IsNullOrWhiteSpace(body) || body.Trim() == "{}"))
        {
            var empty = new ByteArrayContent(Array.Empty<byte>());
            empty.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
            {
                CharSet = "UTF-8"
            };
            return empty;
        }

        return new StringContent(body ?? "", Encoding.UTF8, "application/json");
    }

    private static bool RequiresEmptyRequestBody(string type) =>
        type.Equals("ReloadAssets", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("SyncControllerList", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("GetControllerListTimestamp", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("Ping", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("GetTemplates", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("GetSelectedTemplateId", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("SaveProfile", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("ApplyScreenContent", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("StopVideo", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("RevertTemplateBackground", StringComparison.OrdinalIgnoreCase);

    private static bool IsMeaningfulServiceResponse(LConnectHttpResult result)
    {
        if (!result.IsHttpSuccess)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(result.Body);
    }

    private static bool IsServiceEndpointResponse(LConnectHttpResult result) =>
        result.StatusCode.HasValue && result.StatusCode is not 404 and not 405;

    private static bool AcceptsEmptyResponse(string action) =>
        action.Equals("ReloadAssets", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("SaveProfile", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("StopVideo", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("ApplyScreenContent", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("SyncControllerList", StringComparison.OrdinalIgnoreCase);

    private void CacheEndpoint(int port, LConnectRequestMode mode)
    {
        cachedServicePort = port;
        cachedRequestMode = mode;
    }

    private void ClearCachedEndpoint()
    {
        cachedServicePort = null;
        cachedRequestMode = null;
    }

    private static async Task<IReadOnlyList<int>> DiscoverResponsivePortsAsync(HttpClient client)
    {
        var ports = new List<int>();
        void Add(int port)
        {
            if (port is > 0 and <= 65535 && !ports.Contains(port))
            {
                ports.Add(port);
            }
        }

        foreach (var port in DiscoverConfiguredPorts())
        {
            Add(port);
        }

        Add(11022);
        Add(11023);
        Add(11024);
        Add(11025);

        var probes = ports.Select(async port => new
        {
            Port = port,
            Responsive = await IsLConnectServicePortAsync(client, port).ConfigureAwait(false)
        });
        var results = await Task.WhenAll(probes).ConfigureAwait(false);
        return results.Where(result => result.Responsive).Select(result => result.Port).ToArray();
    }

    private static async Task<bool> IsLConnectServicePortAsync(HttpClient client, int port)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/?action=Ping")
            {
                Content = CreateEmptyJsonContent()
            };
            using var timeout = new CancellationTokenSource(ProbeTimeout);
            using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var body = (await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false)).Trim();
            return body.Equals("\"OK\"", StringComparison.OrdinalIgnoreCase) ||
                   body.Equals("OK", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static HttpContent CreateEmptyJsonContent()
    {
        var content = new ByteArrayContent(Array.Empty<byte>());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
        {
            CharSet = "UTF-8"
        };
        return content;
    }

    private static IEnumerable<int> DiscoverConfiguredPorts()
    {
        foreach (var root in GetLikelyLConnectSettingsRoots())
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                                   path.EndsWith(".config", StringComparison.OrdinalIgnoreCase) ||
                                   path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                                   path.EndsWith(".settings", StringComparison.OrdinalIgnoreCase))
                    .Take(200)
                    .ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                foreach (var port in TryReadServicePorts(file))
                {
                    yield return port;
                }
            }
        }
    }

    private static IEnumerable<string> GetLikelyLConnectSettingsRoots()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            foreach (var child in new[] { "Lian-Li", "Lian Li", "L-Connect 3", "LIANLI" })
            {
                var candidate = Path.Combine(root, child);
                if (Directory.Exists(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<int> TryReadServicePorts(string file)
    {
        string text;
        try
        {
            var info = new FileInfo(file);
            if (info.Length <= 0 || info.Length > 1024 * 1024)
            {
                yield break;
            }

            text = File.ReadAllText(file);
        }
        catch
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(text, "\"ServicePort\"\\s*:\\s*(\\d{2,5})", RegexOptions.IgnoreCase))
        {
            if (int.TryParse(match.Groups[1].Value, out var port))
            {
                yield return port;
            }
        }

        foreach (Match match in Regex.Matches(text, "<ServicePort>\\s*(\\d{2,5})\\s*</ServicePort>", RegexOptions.IgnoreCase))
        {
            if (int.TryParse(match.Groups[1].Value, out var port))
            {
                yield return port;
            }
        }
    }

    private enum LConnectRequestMode
    {
        Legacy,
        OfficialCompatible
    }
}
