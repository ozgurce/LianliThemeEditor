using Microsoft.Extensions.Options;

namespace PhoneControl;

public sealed class AccessControlService
{
    private readonly object sync = new();
    private readonly PhoneControlSettingsStore settingsStore;

    public AccessControlService(IOptions<PhoneControlOptions> options, PhoneControlSettingsStore settingsStore)
    {
        this.settingsStore = settingsStore;
        Token = options.Value.Token.Trim();
        UsePin = options.Value.UsePin;
    }

    public string Token { get; private set; }
    public bool UsePin { get; private set; }

    public bool IsAuthorized(string? token)
    {
        lock (sync)
        {
            return !UsePin || (!string.IsNullOrWhiteSpace(Token) && string.Equals(token, Token, StringComparison.Ordinal));
        }
    }

    public void SetAccess(bool usePin, string? token = null)
    {
        lock (sync)
        {
            UsePin = usePin;
            if (token != null)
            {
                Token = token.Trim();
            }

            var settings = settingsStore.Load();
            settings.UsePin = UsePin;
            settings.Token = Token;
            settingsStore.Save(settings);
        }
    }
}
