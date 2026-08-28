using System.Net.Http;

namespace ThemeEditorCSharp.Services;

public interface ILConnectClientService
{
    Task<LConnectHttpResult> SendDeviceRequestForJsonAsync(
        HttpClient client,
        string devicePath,
        string type,
        string? body);

    Task<LConnectHttpResult> SendServiceRequestForJsonAsync(
        HttpClient client,
        string action,
        string? body);
}
