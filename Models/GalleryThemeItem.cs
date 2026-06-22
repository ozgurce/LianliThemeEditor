using System.ComponentModel;
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

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "";
    public string Changelog { get; set; } = "";
    public string DeviceModel { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string PackageUrl { get; set; } = "";
    public string PreviewUrl { get; set; } = "";
    public ImageSource? Preview
    {
        get => _preview;
        set => SetField(ref _preview, value);
    }
    public double CardWidth => IsWideDevice ? 438 : 260;
    public double CardHeight => IsWideDevice ? 336 : 430;
    public double PreviewHeight => IsWideDevice ? 132 : 260;
    public string AspectLabel => IsWideDevice ? "1920 x 480" : "480 x 480";
    public Stretch PreviewStretch => IsWideDevice ? Stretch.Uniform : Stretch.UniformToFill;
    public string InstallBadgeText => IsInstalled ? "Installed" : "";
    public string DownloadButtonText => IsInstalled ? "Reinstall" : "Download";
    public string StatsText => $"{DownloadCount:N0} downloads";
    public string RatingText => VoteCount > 0
        ? $"{AverageRating:0.0} / 5 ({VoteCount:N0} votes)"
        : "No votes yet";
    public string AverageStars => VoteCount > 0 ? BuildStars((int)Math.Round(AverageRating, MidpointRounding.AwayFromZero)) : "-----";
    public string UserStars => UserRating > 0 ? BuildStars(UserRating) : "Rate";
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

    public event PropertyChangedEventHandler? PropertyChanged;

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
        return new string('★', rating) + new string('☆', 5 - rating);
    }

    private string GetVoteStar(int star) => UserRating >= star ? "★" : "☆";
}
