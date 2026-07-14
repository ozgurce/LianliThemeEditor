using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PhoneControl;

public sealed class PhoneControlServer : IAsyncDisposable
{
    private readonly PhoneControlSettingsStore settingsStore;
    private readonly object sync = new();
    private WebApplication? app;

    public PhoneControlServer(PhoneControlSettingsStore settingsStore)
    {
        this.settingsStore = settingsStore;
    }

    public bool IsRunning { get; private set; }
    public string LastError { get; private set; } = "";
    public IReadOnlyList<string> Urls { get; private set; } = Array.Empty<string>();
    public PhoneControlOptions CurrentOptions { get; private set; } = new();

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await RestartAsync(settingsStore.Load(), cancellationToken).ConfigureAwait(false);
    }

    public async Task RestartAsync(PhoneControlOptions options, CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);

        CurrentOptions = Normalize(options);
        settingsStore.Save(CurrentOptions);
        LastError = "";

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ContentRootPath = settingsStore.ContentRoot
        });

        builder.Services.Configure<PhoneControlOptions>(source =>
        {
            source.Host = CurrentOptions.Host;
            source.Port = CurrentOptions.Port;
            source.UsePin = CurrentOptions.UsePin;
            source.Token = CurrentOptions.Token;
            source.Language = CurrentOptions.Language;
        });
        builder.Services.AddSingleton(settingsStore);
        builder.Services.AddSingleton<AccessControlService>();
        builder.Services.AddSingleton<LConnectClient>();
        builder.Services.AddSingleton<LConnectControlService>();
        builder.Services.AddSingleton<PreviewFileService>();

        builder.WebHost.UseUrls($"http://{CurrentOptions.Host}:{CurrentOptions.Port}");
        var nextApp = builder.Build();
        MapApplication(nextApp, CurrentOptions.Port);

        try
        {
            await nextApp.StartAsync(cancellationToken).ConfigureAwait(false);
            lock (sync)
            {
                app = nextApp;
                IsRunning = true;
                Urls = NetworkAddressHelper.GetLanAddresses(CurrentOptions.Port).ToArray();
            }
        }
        catch (Exception ex)
        {
            await nextApp.DisposeAsync().ConfigureAwait(false);
            LastError = ex.Message;
            IsRunning = false;
            Urls = Array.Empty<string>();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        WebApplication? current;
        lock (sync)
        {
            current = app;
            app = null;
            IsRunning = false;
            Urls = Array.Empty<string>();
        }

        if (current != null)
        {
            await current.StopAsync(cancellationToken).ConfigureAwait(false);
            await current.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private static PhoneControlOptions Normalize(PhoneControlOptions options) => new()
    {
        Host = string.IsNullOrWhiteSpace(options.Host) ? "0.0.0.0" : options.Host.Trim(),
        Port = options.Port is > 0 and <= 65535 ? options.Port : 37373,
        UsePin = options.UsePin,
        Token = options.Token.Trim(),
        Language = string.IsNullOrWhiteSpace(options.Language) ? "en" : options.Language.Trim()
    };

    private static void MapApplication(WebApplication app, int port)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api") ||
                context.Request.Path.StartsWithSegments("/api/config"))
            {
                await next();
                return;
            }

            var access = context.RequestServices.GetRequiredService<AccessControlService>();
            var accessToken = context.Request.Headers["X-PhoneControl-Token"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                accessToken = context.Request.Query["t"].FirstOrDefault();
            }

            if (access.IsAuthorized(accessToken))
            {
                await next();
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ApiError("unauthorized", "Invalid Lian-Li Phone Link token."));
        });

        app.MapGet("/api/config", (IOptions<PhoneControlOptions> options, AccessControlService accessControl) =>
        {
            var configuredPort = options.Value.Port is > 0 and <= 65535 ? options.Value.Port : port;
            return Results.Ok(new
            {
                port = configuredPort,
                tokenRequired = accessControl.UsePin,
                usePin = accessControl.UsePin,
                language = string.IsNullOrWhiteSpace(options.Value.Language) ? "en" : options.Value.Language,
                urls = NetworkAddressHelper.GetLanAddresses(configuredPort)
            });
        });

        app.MapPost("/api/config/access", (
            AccessSettingsRequest request,
            AccessControlService accessControl) =>
        {
            accessControl.SetAccess(request.UsePin, request.Token);
            return Results.Ok(new
            {
                tokenRequired = accessControl.UsePin,
                usePin = accessControl.UsePin
            });
        });

        app.MapGet("/api/status", async (LConnectControlService service, CancellationToken cancellationToken) =>
        {
            var status = await service.GetStatusAsync(cancellationToken);
            return Results.Ok(status);
        });

        app.MapGet("/api/devices", async (LConnectControlService service, CancellationToken cancellationToken) =>
        {
            var devices = await service.GetDevicesAsync(cancellationToken);
            return Results.Ok(devices);
        });

        app.MapGet("/api/lighting/effects", (LConnectControlService service) =>
            Results.Ok(service.GetLightingEffects()));

        app.MapGet("/api/devices/{deviceId}/lighting/effects", async (
            string deviceId,
            LConnectControlService service,
            CancellationToken cancellationToken) =>
        {
            var device = await service.ResolveDeviceAsync(deviceId, cancellationToken);
            return device is null
                ? Results.NotFound(new ApiError("device_not_found", "Device was not found."))
                : Results.Ok(await service.GetLightingEffectsAsync(device, cancellationToken));
        });

        app.MapGet("/api/fan-groups/{groupId}/lighting/effects", (
            string groupId,
            HttpRequest request,
            LConnectControlService service) =>
        {
            var merge = bool.TryParse(request.Query["merge"].FirstOrDefault(), out var parsedMerge) && parsedMerge;
            return Results.Ok(service.GetFanGroupLightingEffects(groupId, merge));
        });

        app.MapGet("/api/fan-groups/{groupId}/lighting/current", (
            string groupId,
            HttpRequest request,
            LConnectControlService service) =>
        {
            var merge = bool.TryParse(request.Query["merge"].FirstOrDefault(), out var parsedMerge) && parsedMerge;
            var state = service.GetFanGroupLightingState(groupId, merge);
            return state is null
                ? Results.NotFound(new ApiError("lighting_state_not_found", "Current lighting state was not found."))
                : Results.Ok(state);
        });

        app.MapGet("/api/fan-groups", (LConnectControlService service) =>
            Results.Ok(service.GetWirelessFanGroups()));

        app.MapGet("/api/devices/{deviceId}/themes", async (
            string deviceId,
            LConnectControlService service,
            CancellationToken cancellationToken) =>
        {
            var themes = await service.GetThemesAsync(deviceId, cancellationToken);
            return Results.Ok(themes);
        });

        app.MapGet("/api/devices/{deviceId}/themes/{themeId}/preview", async (
            string deviceId,
            string themeId,
            LConnectControlService service,
            PreviewFileService previewFileService,
            CancellationToken cancellationToken) =>
        {
            var device = await service.ResolveDeviceAsync(deviceId, cancellationToken);
            if (device is null)
            {
                return Results.NotFound(new ApiError("device_not_found", "Device was not found."));
            }

            var preview = previewFileService.ResolvePreview(device.Model, themeId);
            return preview is null
                ? Results.File(PreviewFileService.FallbackPng, "image/png")
                : Results.File(preview, "image/png");
        });

        app.MapPost("/api/devices/{deviceId}/apply", async (
            string deviceId,
            ApplyThemeRequest request,
            LConnectControlService service,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.ThemeId))
            {
                return Results.BadRequest(new ApiError("theme_required", "ThemeId is required."));
            }

            var result = await service.ApplyThemeAsync(deviceId, request.ThemeId, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        app.MapPost("/api/devices/{deviceId}/brightness", async (
            string deviceId,
            BrightnessRequest request,
            LConnectControlService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var value = NormalizeBrightness(request.Value);
                var result = await service.SetBrightnessAsync(deviceId, value, cancellationToken);
                return result.Success ? Results.Ok(result) : Results.BadRequest(result);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        app.MapPost("/api/devices/{deviceId}/led-brightness", async (
            string deviceId,
            BrightnessRequest request,
            LConnectControlService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var value = NormalizeBrightness(request.Value);
                var result = await service.SetLedBrightnessAsync(deviceId, value, cancellationToken);
                return result.Success ? Results.Ok(result) : Results.BadRequest(result);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        app.MapPost("/api/devices/{deviceId}/lighting-effect", async (
            string deviceId,
            LightingEffectRequest request,
            LConnectControlService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.SetLightingEffectAsync(
                    deviceId,
                    request.Effect,
                    NormalizeBrightness(request.Brightness),
                    request.Color,
                    request.Colors,
                    Math.Clamp(request.Speed, 0, 100),
                    Math.Clamp(request.Direction, 0, 1),
                    cancellationToken);
                return result.Success ? Results.Ok(result) : Results.BadRequest(result);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        app.MapPost("/api/fan-groups/{groupId}/led-brightness", async (
            string groupId,
            BrightnessRequest request,
            LConnectControlService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SetFanGroupLedBrightnessAsync(groupId, NormalizeBrightness(request.Value), cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        app.MapPost("/api/fan-groups/{groupId}/lighting-effect", async (
            string groupId,
            LightingEffectRequest request,
            LConnectControlService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SetFanGroupLightingEffectAsync(
                groupId,
                request.Effect,
                NormalizeBrightness(request.Brightness),
                request.Color,
                request.Colors,
                Math.Clamp(request.Speed, 0, 100),
                Math.Clamp(request.Direction, 0, 1),
                request.Merge,
                request.ApplyAll,
                cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });
    }

    private static int NormalizeBrightness(int value) =>
        Math.Clamp((int)Math.Round(value / 25.0) * 25, 0, 100);
}
