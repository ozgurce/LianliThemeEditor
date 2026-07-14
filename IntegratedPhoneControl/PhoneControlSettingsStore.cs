using System.Text.Json;
using System.Text.Json.Nodes;

namespace PhoneControl;

public sealed class PhoneControlSettingsStore
{
    private readonly object sync = new();

    public PhoneControlSettingsStore(string contentRoot)
    {
        ContentRoot = contentRoot;
        SettingsPath = Path.Combine(contentRoot, "appsettings.json");
    }

    public string ContentRoot { get; }
    public string SettingsPath { get; }

    public PhoneControlOptions Load()
    {
        lock (sync)
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return new PhoneControlOptions();
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (!doc.RootElement.TryGetProperty("PhoneControl", out var phoneControl))
                {
                    return new PhoneControlOptions();
                }

                return new PhoneControlOptions
                {
                    Host = GetString(phoneControl, "Host", "0.0.0.0"),
                    Port = GetInt(phoneControl, "Port", 37373),
                    UsePin = GetBool(phoneControl, "UsePin", true),
                    Token = GetString(phoneControl, "Token", ""),
                    Language = GetString(phoneControl, "Language", "en")
                };
            }
            catch
            {
                return new PhoneControlOptions();
            }
        }
    }

    public void Save(PhoneControlOptions options)
    {
        lock (sync)
        {
            JsonObject root;
            try
            {
                root = File.Exists(SettingsPath)
                    ? JsonNode.Parse(File.ReadAllText(SettingsPath))?.AsObject() ?? new JsonObject()
                    : new JsonObject();
            }
            catch
            {
                root = new JsonObject();
            }

            var phoneControl = root["PhoneControl"] as JsonObject ?? new JsonObject();
            root["PhoneControl"] = phoneControl;
            phoneControl["Host"] = string.IsNullOrWhiteSpace(options.Host) ? "0.0.0.0" : options.Host.Trim();
            phoneControl["Port"] = Math.Clamp(options.Port, 1, 65535);
            phoneControl["UsePin"] = options.UsePin;
            phoneControl["Token"] = options.Token.Trim();
            phoneControl["Language"] = string.IsNullOrWhiteSpace(options.Language) ? "en" : options.Language.Trim();

            var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
    }

    private static string GetString(JsonElement element, string name, string fallback) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;

    private static int GetInt(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var property) && property.TryGetInt32(out var value)
            ? value
            : fallback;

    private static bool GetBool(JsonElement element, string name, bool fallback) =>
        element.TryGetProperty(name, out var property)
            ? property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(property.GetString(), out var value) => value,
                _ => fallback
            }
            : fallback;
}
