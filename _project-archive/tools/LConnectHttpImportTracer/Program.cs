using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class Program
{
    private const string LConnectRoot = @"C:\ProgramData\Lian-Li\L-Connect 3";
    private static readonly string[] DeviceModels =
    [
        "universal-screen-8.8-inch",
        "hydroshift-ii-lcd-s",
        "hydroshift-ii-lcd-c"
    ];

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("L-Connect HTTP Import Tracer");
        Console.WriteLine("This tool does not restart L-Connect or change themes.");
        Console.WriteLine();
        Console.WriteLine("Steps:");
        Console.WriteLine("1. Keep L-Connect open.");
        Console.WriteLine("2. Tracing starts immediately when this window opens.");
        Console.WriteLine("3. In L-Connect, import/download/apply the theme exactly as you normally do.");
        Console.WriteLine("4. Return here and press ENTER when finished.");
        Console.WriteLine("5. Send the ZIP created on the Desktop.");
        Console.WriteLine();

        var runId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var workDir = Path.Combine(Path.GetTempPath(), "LConnectHttpImportTrace-" + runId);
        Directory.CreateDirectory(workDir);
        var startedUtc = DateTime.UtcNow;

        try
        {
            WriteText(Path.Combine(workDir, "trace-info.txt"), BuildTraceInfo(startedUtc));
            Snapshot("before", Path.Combine(workDir, "before"));

            using var cts = new CancellationTokenSource();
            var httpLogPath = Path.Combine(workDir, "http-probe-log.txt");
            var pollTask = Task.Run(() => PollLConnectAsync(httpLogPath, cts.Token));

            Console.WriteLine();
            Console.WriteLine("Tracing started. Import/apply the theme in L-Connect now.");
            Console.WriteLine("Press ENTER when finished.");
            Console.ReadLine();

            cts.Cancel();
            try { await pollTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }

            var endedUtc = DateTime.UtcNow;
            Snapshot("after", Path.Combine(workDir, "after"));
            WriteText(Path.Combine(workDir, "operation-report.txt"), BuildOperationReport(startedUtc, endedUtc));
            WriteChangedFiles(Path.Combine(workDir, "changed-files.txt"), startedUtc, endedUtc);
            CopyRelevantLogs(Path.Combine(workDir, "logs"), startedUtc, endedUtc);
            CopyProfiles(Path.Combine(workDir, "profiles"));

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var zipPath = Path.Combine(desktop, "LConnectHttpImportTrace-" + runId + ".zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(workDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            Console.WriteLine();
            Console.WriteLine("Trace package created:");
            Console.WriteLine(zipPath);
            Console.WriteLine("Press ENTER to close.");
            Console.ReadLine();
            return 0;
        }
        catch (Exception ex)
        {
            WriteText(Path.Combine(workDir, "tracer-error.txt"), ex.ToString());
            Console.WriteLine(ex);
            Console.WriteLine("Press ENTER to close.");
            Console.ReadLine();
            return 1;
        }
    }

    private static async Task PollLConnectAsync(string logPath, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1400) };
        var knownDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!cancellationToken.IsCancellationRequested)
        {
            var serviceResponses = new List<HttpProbeResult>();
            foreach (var mode in new[] { RequestMode.Legacy, RequestMode.OfficialCompatible })
            {
                serviceResponses.Add(await SendServiceRequestAsync(client, "SyncControllerList", "{}", mode, cancellationToken));
                serviceResponses.Add(await SendServiceRequestAsync(client, "GetControllerListTimestamp", "{}", mode, cancellationToken));
            }

            foreach (var result in serviceResponses)
            {
                AppendProbe(logPath, result);
                foreach (var device in ExtractDevicePaths(result.ResponseBody))
                {
                    knownDevices.Add(device);
                }
            }

            foreach (var device in knownDevices.ToArray())
            {
                foreach (var mode in new[] { RequestMode.Legacy, RequestMode.OfficialCompatible })
                {
                    AppendProbe(logPath, await SendDeviceRequestAsync(client, device, "GetTemplates", "{}", mode, cancellationToken));
                    AppendProbe(logPath, await SendDeviceRequestAsync(client, device, "GetSelectedTemplateId", "{}", mode, cancellationToken));
                    AppendProbe(logPath, await SendDeviceRequestAsync(client, device, "ReloadAssets", "{}", mode, cancellationToken));
                }
            }

            await Task.Delay(700, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<HttpProbeResult> SendServiceRequestAsync(
        HttpClient client,
        string action,
        string body,
        RequestMode mode,
        CancellationToken cancellationToken)
    {
        var url = "http://127.0.0.1:11021/?action=" + Uri.EscapeDataString(action);
        return await SendAsync(client, url, "", action, body, mode, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HttpProbeResult> SendDeviceRequestAsync(
        HttpClient client,
        string devicePath,
        string action,
        string body,
        RequestMode mode,
        CancellationToken cancellationToken)
    {
        var encodedPath = Uri.EscapeDataString(Convert.ToBase64String(Encoding.UTF8.GetBytes(devicePath)));
        var url = $"http://127.0.0.1:11021/?action=Device&devicePath={encodedPath}&type={Uri.EscapeDataString(action)}";
        return await SendAsync(client, url, devicePath, action, body, mode, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HttpProbeResult> SendAsync(
        HttpClient client,
        string url,
        string devicePath,
        string action,
        string body,
        RequestMode mode,
        CancellationToken cancellationToken)
    {
        var at = DateTime.UtcNow;
        try
        {
            using var content = CreateContent(action, body, mode);
            using var response = await client.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new HttpProbeResult(at, action, devicePath, mode.ToString(), (int)response.StatusCode, response.ReasonPhrase ?? "", body, responseBody, "");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HttpProbeResult(at, action, devicePath, mode.ToString(), null, "", body, "", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static HttpContent CreateContent(string action, string body, RequestMode mode)
    {
        if (mode == RequestMode.OfficialCompatible &&
            RequiresEmptyRequestBody(action) &&
            (string.IsNullOrWhiteSpace(body) || body.Trim() == "{}"))
        {
            var empty = new ByteArrayContent([]);
            empty.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
            {
                CharSet = "UTF-8"
            };
            return empty;
        }

        return new StringContent(body ?? "", Encoding.UTF8, "application/json");
    }

    private static bool RequiresEmptyRequestBody(string action) =>
        action.Equals("ReloadAssets", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("SyncControllerList", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("GetControllerListTimestamp", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("Ping", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("GetTemplates", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("GetSelectedTemplateId", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("SaveProfile", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("ApplyScreenContent", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("StopVideo", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ExtractDevicePaths(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) yield break;
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) yield break;
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.Name.Contains("vid_", StringComparison.OrdinalIgnoreCase) &&
                property.Name.Contains("pid_", StringComparison.OrdinalIgnoreCase))
            {
                yield return property.Name;
            }
        }
    }

    private static void AppendProbe(string path, HttpProbeResult result)
    {
        var line = string.Join(" | ", new[]
        {
            result.AtUtc.ToString("O", CultureInfo.InvariantCulture),
            "action=" + result.Action,
            "mode=" + result.Mode,
            "status=" + (result.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "<none>"),
            "reason=" + Empty(result.Reason),
            "device=" + RedactDevicePath(result.DevicePath),
            "request=" + OneLine(result.RequestBody),
            "responseLen=" + result.ResponseBody.Length.ToString(CultureInfo.InvariantCulture),
            "response=" + OneLine(result.ResponseBody),
            "error=" + Empty(result.Error)
        });
        File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
    }

    private static string BuildTraceInfo(DateTime startedUtc) => string.Join(Environment.NewLine, new[]
    {
        "L-Connect HTTP Import Trace",
        "StartedUtc: " + startedUtc.ToString("O", CultureInfo.InvariantCulture),
        "Machine: " + Environment.MachineName,
        "User: " + Environment.UserName,
        "OS: " + Environment.OSVersion,
        ".NET: " + Environment.Version,
        "Note: This tool actively probes L-Connect HTTP endpoints during manual import/apply. It does not restart services or modify themes."
    });

    private static string BuildOperationReport(DateTime startedUtc, DateTime endedUtc) => string.Join(Environment.NewLine, new[]
    {
        "TraceStartedUtc: " + startedUtc.ToString("O", CultureInfo.InvariantCulture),
        "TraceEndedUtc: " + endedUtc.ToString("O", CultureInfo.InvariantCulture),
        "DurationSeconds: " + (endedUtc - startedUtc).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)
    });

    private static void Snapshot(string name, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        WriteText(Path.Combine(targetDir, "template-files.txt"), ListFiles(DeviceModels.Select(model => Path.Combine(LConnectRoot, model, "template"))));
        WriteText(Path.Combine(targetDir, "uploaded-files.txt"), ListFiles([Path.Combine(LConnectRoot, "uploaded")]));
        WriteText(Path.Combine(targetDir, "profile-files.txt"), ListFiles([Path.Combine(LConnectRoot, "profile")]));
    }

    private static string ListFiles(IEnumerable<string> roots)
    {
        var lines = new List<string>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Take(300))
            {
                var info = new FileInfo(file);
                lines.Add($"{info.LastWriteTimeUtc:O} | {info.Length} | {file}");
            }
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static void WriteChangedFiles(string path, DateTime startedUtc, DateTime endedUtc)
    {
        var roots = DeviceModels.Select(model => Path.Combine(LConnectRoot, model))
            .Concat([Path.Combine(LConnectRoot, "uploaded"), Path.Combine(LConnectRoot, "profile")])
            .Where(Directory.Exists);
        var lines = new List<string>();
        foreach (var root in roots)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var time = File.GetLastWriteTimeUtc(file);
                if (time >= startedUtc.AddMinutes(-1) && time <= endedUtc.AddMinutes(2))
                {
                    var info = new FileInfo(file);
                    lines.Add($"{time:O} | {info.Length} | {file}");
                }
            }
        }
        WriteText(path, string.Join(Environment.NewLine, lines.OrderBy(line => line)));
    }

    private static void CopyRelevantLogs(string targetDir, DateTime startedUtc, DateTime endedUtc)
    {
        var logDir = Path.Combine(LConnectRoot, "logs");
        if (!Directory.Exists(logDir)) return;
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(logDir, "*.log")
                     .Where(path => File.GetLastWriteTimeUtc(path) >= startedUtc.Date.AddDays(-1))
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(20))
        {
            SafeCopy(file, Path.Combine(targetDir, Path.GetFileName(file)));
        }
    }

    private static void CopyProfiles(string targetDir)
    {
        var profileDir = Path.Combine(LConnectRoot, "profile");
        if (!Directory.Exists(profileDir)) return;
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(profileDir).OrderByDescending(File.GetLastWriteTimeUtc).Take(20))
        {
            SafeCopy(file, Path.Combine(targetDir, Path.GetFileName(file)));
        }
    }

    private static void SafeCopy(string source, string destination)
    {
        try { File.Copy(source, destination, true); }
        catch { }
    }

    private static void WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content ?? "", Encoding.UTF8);
    }

    private static string OneLine(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "<empty>";
        var single = Regex.Replace(value, "\\s+", " ").Trim();
        return single.Length <= 1200 ? single : single[..1200] + "...";
    }

    private static string Empty(string value) => string.IsNullOrWhiteSpace(value) ? "<empty>" : value;

    private static string RedactDevicePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "<none>";
        var normalized = path.Replace("\\\\", "\\");
        var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var hardwareId = parts.FirstOrDefault(part =>
            part.Contains("vid_", StringComparison.OrdinalIgnoreCase) &&
            part.Contains("pid_", StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(hardwareId)
            ? normalized
            : hardwareId + "\\<device-id-redacted>";
    }

    private enum RequestMode
    {
        Legacy,
        OfficialCompatible
    }

    private sealed record HttpProbeResult(
        DateTime AtUtc,
        string Action,
        string DevicePath,
        string Mode,
        int? StatusCode,
        string Reason,
        string RequestBody,
        string ResponseBody,
        string Error);
}
