using CommunityToolkit.Mvvm.ComponentModel;
using PCue.Services;

namespace PCue.Models;

public partial class PlaylistItem : ObservableObject
{
    public const long MinPlayLengthMs = 500;
    public const int MaxFadeMs = 60_000;

    public string FilePath { get; }

    public string DisplayName { get; }

    /// <summary>음원 또는 영상</summary>
    public string SourceType { get; }

    public bool IsVideo => SourceType == "영상";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText), nameof(ListDurationText), nameof(PlayLengthText))]
    private long _durationMs = -1;

    [ObservableProperty]
    private int _number;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InPointText), nameof(HasTrim), nameof(TrimText),
        nameof(ListDurationText), nameof(PlayLengthText))]
    private long _inPointMs;

    /// <summary>Null = play to the end of the file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutPointText), nameof(HasTrim), nameof(TrimText),
        nameof(ListDurationText), nameof(PlayLengthText))]
    private long? _outPointMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FadeInText), nameof(HasFade), nameof(FadeSummary))]
    private int _fadeInMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FadeOutText), nameof(HasFade), nameof(FadeSummary))]
    private int _fadeOutMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndActionBadge), nameof(HasEndActionBadge), nameof(EndActionTooltip))]
    private CueEndAction _endAction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScheduled), nameof(ScheduleTooltip))]
    private DateTime? _scheduledAt;

    [ObservableProperty]
    private string _scheduleCountdown = "";

    [ObservableProperty]
    private bool _isScheduleImminent;

    /// <summary>Raised when an edit is refused; the text explains why.</summary>
    public event EventHandler<string>? EditRejected;

    public string DurationText =>
        DurationMs < 0 ? "--:--" : FormatDuration(DurationMs);

    public bool HasTrim => InPointMs > 0 || OutPointMs is not null;

    public string TrimText =>
        $"구간 {TimeFormat.FormatCue(InPointMs)} – {(OutPointMs is long o ? TimeFormat.FormatCue(o) : "끝")}";

    /// <summary>Trimmed length, or -1 while the duration is unknown.</summary>
    public long PlayLengthMs
    {
        get
        {
            if (DurationMs <= 0)
                return -1;

            var end = OutPointMs is long o ? Math.Min(o, DurationMs) : DurationMs;
            return Math.Max(0, end - InPointMs);
        }
    }

    public string ListDurationText =>
        HasTrim && PlayLengthMs >= 0 ? FormatDuration(PlayLengthMs) : DurationText;

    public string PlayLengthText
    {
        get
        {
            if (DurationMs < 0)
                return "길이 --:--";

            return HasTrim
                ? $"재생 길이 {FormatDuration(PlayLengthMs)} · 원본 {DurationText}"
                : $"길이 {DurationText}";
        }
    }

    public bool HasFade => FadeInMs > 0 || FadeOutMs > 0;

    public string FadeSummary
    {
        get
        {
            var parts = new List<string>(2);
            if (FadeInMs > 0)
                parts.Add($"페이드 인 {TimeFormat.FormatSeconds(FadeInMs)}초");
            if (FadeOutMs > 0)
                parts.Add($"페이드 아웃 {TimeFormat.FormatSeconds(FadeOutMs)}초");
            return string.Join(" · ", parts);
        }
    }

    public string EndActionBadge => EndAction switch
    {
        CueEndAction.Next => "다음",
        CueEndAction.Stop => "정지",
        CueEndAction.Hold => "멈춤",
        CueEndAction.Loop => "반복",
        _ => ""
    };

    public bool HasEndActionBadge => EndAction != CueEndAction.Default;

    public string EndActionTooltip => EndAction switch
    {
        CueEndAction.Next => "끝나면 다음 큐 재생",
        CueEndAction.Stop => "끝나면 정지",
        CueEndAction.Hold => IsVideo ? "끝나면 마지막 장면에서 멈춤" : "끝나면 정지 (음원)",
        CueEndAction.Loop => "구간 반복 재생",
        _ => ""
    };

    public bool IsScheduled => ScheduledAt is not null;

    public string? ScheduleTooltip =>
        ScheduledAt is DateTime at ? $"{TimeFormat.FormatClock(at)} 재생 예약" : null;

    public string InPointText
    {
        get => InPointMs > 0 ? TimeFormat.FormatCue(InPointMs) : "";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                InPointMs = 0;
                return;
            }

            if (!TimeFormat.TryParseDuration(value, 1000, out var ms))
            {
                Reject(nameof(InPointText), "시간 형식을 확인하세요. 예: 90, 1:30, 1:30.5");
                return;
            }

            TrySetInPoint(ms);
        }
    }

    public string OutPointText
    {
        get => OutPointMs is long o ? TimeFormat.FormatCue(o) : "";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                OutPointMs = null;
                return;
            }

            if (!TimeFormat.TryParseDuration(value, 1000, out var ms))
            {
                Reject(nameof(OutPointText), "시간 형식을 확인하세요. 예: 90, 1:30, 1:30.5");
                return;
            }

            TrySetOutPoint(ms);
        }
    }

    public string FadeInText
    {
        get => FadeInMs > 0 ? TimeFormat.FormatSeconds(FadeInMs) : "";
        set => SetFadeFromText(value, isIn: true);
    }

    public string FadeOutText
    {
        get => FadeOutMs > 0 ? TimeFormat.FormatSeconds(FadeOutMs) : "";
        set => SetFadeFromText(value, isIn: false);
    }

    public PlaylistItem(string filePath)
    {
        FilePath = filePath;
        DisplayName = System.IO.Path.GetFileName(filePath);
        SourceType = ResolveSourceType(filePath);
    }

    public bool TrySetInPoint(long ms)
    {
        ms = RoundToTenth(ms);
        if (ms <= 0)
        {
            InPointMs = 0;
            return true;
        }

        if (DurationMs > 0 && ms > DurationMs - MinPlayLengthMs)
        {
            Reject(nameof(InPointText), $"시작 지점이 파일 길이({DurationText})를 넘습니다.");
            return false;
        }

        if (OutPointMs is long o && ms > o - MinPlayLengthMs)
        {
            Reject(nameof(InPointText), "시작 지점은 종료 지점보다 0.5초 이상 앞이어야 합니다.");
            return false;
        }

        InPointMs = ms;
        return true;
    }

    public bool TrySetOutPoint(long? ms)
    {
        if (ms is not long value)
        {
            OutPointMs = null;
            return true;
        }

        value = RoundToTenth(value);
        if (value < InPointMs + MinPlayLengthMs)
        {
            Reject(nameof(OutPointText), "종료 지점은 시작 지점보다 0.5초 이상 뒤여야 합니다.");
            return false;
        }

        // At (or past) the real end means "play to the end".
        OutPointMs = DurationMs > 0 && value >= DurationMs - 50 ? null : value;
        return true;
    }

    private void SetFadeFromText(string? value, bool isIn)
    {
        var property = isIn ? nameof(FadeInText) : nameof(FadeOutText);
        var ms = 0;
        if (!string.IsNullOrWhiteSpace(value)
            && (!TimeFormat.TryParseSeconds(value, out ms) || ms > MaxFadeMs))
        {
            Reject(property, "페이드는 0~60초 사이의 초 단위로 입력하세요. 예: 2, 1.5");
            return;
        }

        if (isIn)
            FadeInMs = ms;
        else
            FadeOutMs = ms;
    }

    private void Reject(string property, string message)
    {
        EditRejected?.Invoke(this, message);
        OnPropertyChanged(property);
    }

    private static long RoundToTenth(long ms) => (long)Math.Round(ms / 100.0) * 100;

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
