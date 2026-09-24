using CommunityToolkit.Mvvm.ComponentModel;

namespace PCue.Models;

public partial class PlaylistItem : ObservableObject
{
    public string FilePath { get; }

    public string DisplayName { get; }

    /// <summary>음원 또는 영상</summary>
    public string SourceType { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    private long _durationMs = -1;

    [ObservableProperty]
    private int _number;

    [ObservableProperty]
    private bool _isPlaying;

    public string DurationText =>
        DurationMs < 0 ? "--:--" : FormatDuration(DurationMs);

    public PlaylistItem(string filePath)
    {
        FilePath = filePath;
        DisplayName = System.IO.Path.GetFileName(filePath);
        SourceType = ResolveSourceType(filePath);
    }

    private static string ResolveSourceType(string filePath)
    {
        var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".mp3" or ".wav" or ".flac" or ".m4a" or ".aac" or ".ogg" or ".wma" => "음원",
            ".mp4" or ".mkv" or ".mov" or ".avi" or ".wmv" or ".webm" or ".m4v" => "영상",
            _ => "기타"
        };
    }

    private static string FormatDuration(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"mm\:ss");
    }
}
