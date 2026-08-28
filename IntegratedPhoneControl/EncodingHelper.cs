using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace PhoneControl;

public static class EncodingHelper
{
    public static string ToBase64Url(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryFromBase64Url(string value, out string decoded)
    {
        decoded = "";
        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return !string.IsNullOrWhiteSpace(decoded);
        }
        catch
        {
            return false;
        }
    }
}

public static class NetworkAddressHelper
{
    public static IEnumerable<string> GetLanAddresses(int port)
    {
        yield return $"http://localhost:{port}";

        foreach (var address in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                                   nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                     .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                     .Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork)
                     .Select(ip => ip.Address)
                     .Distinct())
        {
            if (!IPAddress.IsLoopback(address))
            {
                yield return $"http://{address}:{port}";
            }
        }
    }
}
