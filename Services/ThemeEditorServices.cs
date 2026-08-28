using System.Net.Http;

namespace ThemeEditorCSharp.Services;

public sealed class ThemeEditorServices
{
    public ThemeEditorServices(
        ISupporterBridge supporter,
        IRecoveryService recovery,
        IThemePackageValidationService themeValidator,
        DiagnosticService diagnostics,
        ILConnectClientService lConnectClient,
        IGallerySubmissionService gallerySubmission,
        IThemeInstallationService themeInstallation,
        IGalleryFilterService galleryFilter,
        IGalleryPaginationService galleryPagination,
        IGalleryManifestService galleryManifest,
        IGalleryStatsService galleryStats)
    {
        Supporter = supporter;
        Recovery = recovery;
        ThemeValidator = themeValidator;
        Diagnostics = diagnostics;
        LConnectClient = lConnectClient;
        GallerySubmission = gallerySubmission;
        ThemeInstallation = themeInstallation;
        GalleryFilter = galleryFilter;
        GalleryPagination = galleryPagination;
        GalleryManifest = galleryManifest;
        GalleryStats = galleryStats;
    }

    public ISupporterBridge Supporter { get; }
    public IRecoveryService Recovery { get; }
    public IThemePackageValidationService ThemeValidator { get; }
    public DiagnosticService Diagnostics { get; }
    public ILConnectClientService LConnectClient { get; }
    public IGallerySubmissionService GallerySubmission { get; }
    public IThemeInstallationService ThemeInstallation { get; }
    public IGalleryFilterService GalleryFilter { get; }
    public IGalleryPaginationService GalleryPagination { get; }
    public IGalleryManifestService GalleryManifest { get; }
    public IGalleryStatsService GalleryStats { get; }

    public static ThemeEditorServices CreateDefault() => new(
        new SupporterBridge(),
        new RecoveryService(),
        new ThemePackageValidationService(),
        new DiagnosticService(),
        new LConnectClientService(),
        new GallerySubmissionService(),
        new ThemeInstallationService(),
        new GalleryFilterService(),
        new GalleryPaginationService(),
        new GalleryManifestService(CreateSharedHttpClient()),
        new GalleryStatsService(CreateSharedHttpClient()));

    private static HttpClient CreateSharedHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LianLiThemeEditor");
        return client;
    }
}
