using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ThemeEditorCSharp.Models;

public sealed class GalleryThemeItem : INotifyPropertyChanged
{
    private bool _isBusy;
    private double _progress;
    private string _status = "";
    private bool _isInstalled;
    private string _installedTemplateId = "";
    private string _installedTemplatePath = "";
    private ImageSource? _preview;
    private bool _isPreviewLoading;
    private int _downloadCount;
    private int _voteCount;
    private double _averageRating;
    private int _userRating;
    private int _hoverRating;
    private string _installedLabel = "Installed";
    private string _downloadLabel = "Download";
    private string _reinstallLabel = "Reinstall";
    private string _downloadsFormat = "{0:N0} downloads";
    private string _ratingFormat = "{0:0.0} / 5 ({1:N0} votes)";
    private string _noVotesYetLabel = "No votes yet";
    private string _rateLabel = "Rate";
    private string _authorFormat = "Author: {0}";

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "";
    public string Changelog { get; set; } = "";
    public string DeviceModel { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Orientation { get; set; } = "";
    public string PackageUrl { get; set; } = "";
    public string PreviewUrl { get; set; } = "";
    public DateTime GallerySortTimeUtc { get; set; } = DateTime.MinValue;
    public ImageSource? Preview
    {
        get => _preview;
        set => SetField(ref _preview, value);
    }
    public double CardWidth => IsLandscapeWideDevice ? 438 : 260;
    public double CardHeight => IsLandscapeWideDevice ? 286 : 402;
    public double PreviewHeight => IsLandscapeWideDevice ? 132 : 260;
    public string AspectLabel => IsWideDevice
        ? (IsPortraitWideDevice ? "480 x 1920" : "1920 x 480")
        : "480 x 480";
    public Stretch PreviewStretch => IsWideDevice ? Stretch.Uniform : Stretch.UniformToFill;
    public string InstallBadgeText => IsInstalled ? _installedLabel : "";
    public string DownloadButtonText => IsInstalled ? _reinstallLabel : _downloadLabel;
    public string StatsText => string.Format(CultureInfo.CurrentCulture, _downloadsFormat, DownloadCount);
    public string DownloadCountText => DownloadCount.ToString("N0", CultureInfo.CurrentCulture);
    public string RatingText => VoteCount > 0
        ? string.Format(CultureInfo.CurrentCulture, _ratingFormat, AverageRating, VoteCount)
        : _noVotesYetLabel;
    public string AverageStars => VoteCount > 0 ? BuildStars((int)Math.Round(AverageRating, MidpointRounding.AwayFromZero)) : "-----";
    public string AverageStar1Brush => GetAverageStarBrush(1);
    public string AverageStar2Brush => GetAverageStarBrush(2);
    public string AverageStar3Brush => GetAverageStarBrush(3);
    public string AverageStar4Brush => GetAverageStarBrush(4);
    public string AverageStar5Brush => GetAverageStarBrush(5);
    public string UserStars => UserRating > 0 ? BuildStars(UserRating) : _rateLabel;
    public string AuthorText => string.Format(CultureInfo.CurrentCulture, _authorFormat, Author);
    public string VoteStar1 => GetVoteStar(1);
    public string VoteStar2 => GetVoteStar(2);
    public string VoteStar3 => GetVoteStar(3);
    public string VoteStar4 => GetVoteStar(4);
    public string VoteStar5 => GetVoteStar(5);
    public bool IsPreviewLoading
    {
        get => _isPreviewLoading;
        set => SetField(ref _isPreviewLoading, value);
    }

    private bool IsWideDevice =>
        string.Equals(DeviceModel, "universal-screen-8.8-inch", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(DeviceModel, "vm-9.2-inch", StringComparison.OrdinalIgnoreCase);

    private bool IsPortraitWideDevice =>
        IsWideDevice &&
        string.Equals(Orientation, "portrait", StringComparison.OrdinalIgnoreCase);

    private bool IsLandscapeWideDevice =>
        IsWideDevice && !IsPortraitWideDevice;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (SetField(ref _isInstalled, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InstallBadgeText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DownloadButtonText)));
            }
        }
    }

    public string InstalledTemplateId
    {
        get => _installedTemplateId;
        set => SetField(ref _installedTemplateId, value);
    }

    public string InstalledTemplatePath
    {
        get => _installedTemplatePath;
        set => SetField(ref _installedTemplatePath, value);
    }

    public int DownloadCount
    {
        get => _downloadCount;
        set
        {
            if (SetField(ref _downloadCount, Math.Max(0, value)))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatsText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DownloadCountText)));
            }
        }
    }

    public int VoteCount
    {
        get => _voteCount;
        set
        {
            if (SetField(ref _voteCount, Math.Max(0, value)))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RatingText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AverageStars)));
                NotifyAverageStarBrushesChanged();
            }
        }
    }

    public double AverageRating
    {
        get => _averageRating;
        set
        {
            if (SetField(ref _averageRating, Math.Clamp(value, 0, 5)))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RatingText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AverageStars)));
                NotifyAverageStarBrushesChanged();
            }
        }
    }

    public int UserRating
    {
        get => _userRating;
        set
        {
            if (SetField(ref _userRating, Math.Clamp(value, 0, 5)))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UserStars)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VoteStar1)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VoteStar2)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VoteStar3)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VoteStar4)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VoteStar5)));
            }
        }
    }

    public int HoverRating
    {
        get => _hoverRating;
        set
        {
            if (SetField(ref _hoverRating, Math.Clamp(value, 0, 5)))
            {
                NotifyAverageStarBrushesChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ApplyLocalization(
        string installedLabel,
        string downloadLabel,
        string reinstallLabel,
        string downloadsFormat,
        string ratingFormat,
        string noVotesYetLabel,
        string rateLabel,
        string authorFormat)
    {
        _installedLabel = installedLabel;
        _downloadLabel = downloadLabel;
        _reinstallLabel = reinstallLabel;
        _downloadsFormat = downloadsFormat;
        _ratingFormat = ratingFormat;
        _noVotesYetLabel = noVotesYetLabel;
        _rateLabel = rateLabel;
        _authorFormat = authorFormat;

        foreach (var propertyName in new[]
                 {
                     nameof(InstallBadgeText),
                     nameof(DownloadButtonText),
                     nameof(StatsText),
                     nameof(DownloadCountText),
                     nameof(RatingText),
                     nameof(AverageStars),
                     nameof(AverageStar1Brush),
                     nameof(AverageStar2Brush),
                     nameof(AverageStar3Brush),
                     nameof(AverageStar4Brush),
                     nameof(AverageStar5Brush),
                     nameof(UserStars),
                     nameof(AuthorText)
                 })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private static string BuildStars(int rating)
    {
        rating = Math.Clamp(rating, 0, 5);
        return new string('\u2605', rating) + new string('\u2606', 5 - rating);
    }

    private string GetAverageStarBrush(int star)
    {
        if (HoverRating > 0)
        {
            return HoverRating >= star ? "#FFFFC83D" : "#8891A2B8";
        }

        if (VoteCount <= 0)
        {
            return "#8891A2B8";
        }

        var rounded = (int)Math.Round(AverageRating, MidpointRounding.AwayFromZero);
        return rounded >= star ? "#FFFFC83D" : "#8891A2B8";
    }

    private void NotifyAverageStarBrushesChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AverageStar1Brush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AverageStar2Brush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AverageStar3Brush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AverageStar4Brush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AverageStar5Brush)));
    }

    private string GetVoteStar(int star) => UserRating >= star ? "\u2605" : "\u2606";
}
