using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace PhoneControl;

public sealed class LConnectClient
{
    private const int DefaultServicePort = 11021;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(900);
    private readonly object cacheSync = new();
    private int? cachedServicePort;
    private LConnectRequestMode? cachedRequestMode;

    public async Task<LConnectHttpResult> SendDeviceRequestForJsonAsync(
        HttpClient client,
        string devicePath,
        string type,
        string? body,
        CancellationToken cancellationToken = default)
    {
        if (TryGetCachedEndpoint(out var cachedPort, out var cachedMode))
        {
            var cachedResult = await SendDeviceRequestForJsonAsync(
                client,
                cachedPort,
                devicePath,
                type,
                body,
                cachedMode,
                cancellationToken).ConfigureAwait(false);
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
            var result = await SendDeviceRequestForJsonAsync(
                client,
                DefaultServicePort,
                devicePath,
                type,
                body,
                mode,
                cancellationToken).ConfigureAwait(false);
            lastResult = result;
            if (IsMeaningfulServiceResponse(result))
            {
                CacheEndpoint(DefaultServicePort, mode);
                return result;
            }
        }

        var ports = await DiscoverResponsivePortsAsync(client, cancellationToken).ConfigureAwait(false);
        foreach (var port in ports)
        {
            foreach (var mode in new[] { LConnectRequestMode.OfficialCompatible, LConnectRequestMode.Legacy })
            {
                var result = await SendDeviceRequestForJsonAsync(
                    client,
                    port,
                    devicePath,
                    type,
                    body,
                    mode,
                    cancellationToken).ConfigureAwait(false);
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
        string? body,
        CancellationToken cancellationToken = default)
    {
        return await SendServiceRequestForJsonAsync(client, action, null, body, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LConnectHttpResult> SendServiceRequestForJsonAsync(
        HttpClient client,
        string action,
        IReadOnlyDictionary<string, string>? query,
        string? body,
        CancellationToken cancellationToken = default)
    {
        if (TryGetCachedEndpoint(out var cachedPort, out var cachedMode))
        {
            var cachedResult = await SendServiceRequestForJsonAsync(
                client,
                cachedPort,
                action,
                query,
                body,
                cachedMode,
                cancellationToken).ConfigureAwait(false);
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
                query,
                body,
                mode,
                cancellationToken).ConfigureAwait(false);
            lastResult = result;
            if (IsMeaningfulServiceResponse(result))
            {
                CacheEndpoint(DefaultServicePort, mode);
                return result;
            }
        }

        var ports = await DiscoverResponsivePortsAsync(client, cancellationToken).ConfigureAwait(false);
        foreach (var port in ports)
        {
            foreach (var mode in new[] { LConnectRequestMode.OfficialCompatible, LConnectRequestMode.Legacy })
            {
                var result = await SendServiceRequestForJsonAsync(client, port, action, query, body, mode, cancellationToken).ConfigureAwait(false);
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
        LConnectRequestMode mode,
        CancellationToken cancellationToken)
    {
        try
        {
            var encodedPath = Uri.EscapeDataString(Convert.ToBase64String(Encoding.UTF8.GetBytes(devicePath)));
            var url = $"http://127.0.0.1:{port}/?action=Device&devicePath={encodedPath}&type={Uri.EscapeDataString(type)}";
            LogRequestBody("Device", type, url, body, mode);
            using var content = CreateContent(type, body, mode);
            using var response = await client.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            LogResponseBody("Device", type, (int)response.StatusCode, responseBody, mode);
            return new LConnectHttpResult((int)response.StatusCode, port, mode.ToString(), response.ReasonPhrase ?? "", responseBody, "");
        }
        catch (Exception ex)
        {
            return new LConnectHttpResult(null, port, mode.ToString(), "", "", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<LConnectHttpResult> SendServiceRequestForJsonAsync(
        HttpClient client,
        int port,
        string action,
        IReadOnlyDictionary<string, string>? query,
        string? body,
        LConnectRequestMode mode,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = BuildServiceUrl(port, action, query);
            LogRequestBody("Service", action, url, body, mode);
            using var content = CreateContent(action, body, mode);
            using var response = await client.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            LogResponseBody("Service", action, (int)response.StatusCode, responseBody, mode);
            return new LConnectHttpResult((int)response.StatusCode, port, mode.ToString(), response.ReasonPhrase ?? "", responseBody, "");
        }
        catch (Exception ex)
        {
            return new LConnectHttpResult(null, port, mode.ToString(), "", "", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string BuildServiceUrl(int port, string action, IReadOnlyDictionary<string, string>? query)
    {
        var builder = new StringBuilder($"http://127.0.0.1:{port}/?action={Uri.EscapeDataString(action)}");
        if (query is null)
        {
            return builder.ToString();
        }

        foreach (var (key, value) in query)
        {
            if (string.Equals(key, "action", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder
                .Append('&')
                .Append(Uri.EscapeDataString(key))
                .Append('=')
                .Append(Uri.EscapeDataString(value));
        }

        return builder.ToString();
    }

    private static HttpContent CreateContent(string type, string? body, LConnectRequestMode mode)
    {
        if (mode == LConnectRequestMode.OfficialCompatible &&
            RequiresEmptyRequestBody(type) &&
            (string.IsNullOrWhiteSpace(body) || body.Trim() == "{}"))
        {
            var empty = new ByteArrayContent(Array.Empty<byte>());
            empty.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "UTF-8" };
            return empty;
        }

        return new StringContent(body ?? "", Encoding.UTF8, "application/json");
    }

    private static void LogRequestBody(string channel, string type, string url, string? body, LConnectRequestMode mode)
    {
        if (!ShouldLogBody(channel, type))
        {
            return;
        }

        AppendBodyLog($"""
            [{DateTimeOffset.Now:O}] REQUEST {channel}:{type} mode={mode}
            URL: {url}
            BODY: {body}

            """);
    }

    private static void LogResponseBody(string channel, string type, int statusCode, string body, LConnectRequestMode mode)
    {
        if (!ShouldLogBody(channel, type))
        {
            return;
        }

        AppendBodyLog($"""
            [{DateTimeOffset.Now:O}] RESPONSE {channel}:{type} mode={mode} status={statusCode}
            BODY: {body}

            """);
    }

    private static bool ShouldLogBody(string channel, string type) =>
        type.Equals("FanLightingSetting", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("FanMergeLightingSetting", StringComparison.OrdinalIgnoreCase) ||
        (channel.Equals("Service", StringComparison.OrdinalIgnoreCase) &&
         type.Equals("LWireless", StringComparison.OrdinalIgnoreCase));

    private static void AppendBodyLog(string text)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Lian-Li Phone Link");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "lconnect-body.log"), text, Encoding.UTF8);
        }
        catch
        {
            // Diagnostics must never break lighting control.
        }
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
        type.Equals("StopVideo", StringComparison.OrdinalIgnoreCase);

    private static bool IsMeaningfulServiceResponse(LConnectHttpResult result) =>
        result.IsHttpSuccess && !string.IsNullOrWhiteSpace(result.Body);

    private static bool IsServiceEndpointResponse(LConnectHttpResult result) =>
        result.StatusCode.HasValue && result.StatusCode is not 404 and not 405;

    private static bool AcceptsEmptyResponse(string action) =>
        action.Equals("ReloadAssets", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("SaveProfile", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("StopVideo", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("ApplyScreenContent", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("SyncControllerList", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("LWireless", StringComparison.OrdinalIgnoreCase);

    private void CacheEndpoint(int port, LConnectRequestMode mode)
    {
        lock (cacheSync)
        {
            cachedServicePort = port;
            cachedRequestMode = mode;
        }
    }

    private void ClearCachedEndpoint()
    {
        lock (cacheSync)
        {
            cachedServicePort = null;
            cachedRequestMode = null;
        }
    }

    private bool TryGetCachedEndpoint(out int port, out LConnectRequestMode mode)
    {
        lock (cacheSync)
        {
            if (cachedServicePort.HasValue && cachedRequestMode.HasValue)
            {
                port = cachedServicePort.Value;
                mode = cachedRequestMode.Value;
                return true;
            }
        }

        port = 0;
        mode = default;
        return false;
    }

    private static async Task<IReadOnlyList<int>> DiscoverResponsivePortsAsync(HttpClient client, CancellationToken cancellationToken)
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
            Responsive = await IsLConnectServicePortAsync(client, port, cancellationToken).ConfigureAwait(false)
        });
        var results = await Task.WhenAll(probes).ConfigureAwait(false);
        return results.Where(result => result.Responsive).Select(result => result.Port).ToArray();
    }

    private static async Task<bool> IsLConnectServicePortAsync(HttpClient client, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/?action=Ping")
            {
                Content = CreateEmptyJsonContent()
            };
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
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "UTF-8" };
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
