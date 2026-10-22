using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCue.Models;
using PCue.Services;
using PCue.Views;

namespace PCue.ViewModels;

public sealed record EndActionOption(CueEndAction Value, string Label, string Description)
{
    public override string ToString() => Label;
}

public sealed record FadeStopOption(int Ms, string Label)
{
    public override string ToString() => Label;
}

public partial class MainViewModel : ObservableObject, IDisposable
{
    // Hold has to pause before LibVLC hits EOF and tears down the video surface.
    private const long HoldBeforeEofMs = 250;
    private const long OutArmMarginMs = 150;
    private const long SeekJumpThresholdMs = 1500;
    private static readonly TimeSpan ScheduleMissGrace = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ScheduleImminent = TimeSpan.FromSeconds(10);

    private readonly MediaPlayerService _media;
    private readonly DisplayService _displays = new();
    private OutputWindow? _outputWindow;
    private bool _disposed;

    public ObservableCollection<PlaylistItem> Playlist { get; } = [];
    public ObservableCollection<DisplayInfo> Displays { get; } = [];

    public IReadOnlyList<EndActionOption> EndActionOptions { get; } =
    [
        new(CueEndAction.Default, "기본 (자동 다음 설정 따름)", "재생 목록 위의 '자동 다음' 체크 상태를 따릅니다."),
        new(CueEndAction.Next, "다음 큐 재생", "끝나면 바로 다음 큐를 재생합니다."),
        new(CueEndAction.Stop, "정지", "끝나면 정지합니다. 출력 화면은 검정으로 바뀝니다."),
        new(CueEndAction.Hold, "마지막 장면에서 멈춤", "영상의 마지막 장면을 출력 화면에 띄운 채 멈춥니다. 음원은 정지와 같습니다. 정지·다음으로 해제합니다."),
        new(CueEndAction.Loop, "반복 재생", "시작~종료 구간을 계속 반복합니다. 다음·정지·페이드 정지로 빠져나옵니다.")
    ];

    public IReadOnlyList<FadeStopOption> FadeStopOptions { get; } =
    [
        new(1000, "1초"),
        new(2000, "2초"),
        new(3000, "3초"),
        new(5000, "5초"),
        new(8000, "8초"),
        new(10000, "10초")
    ];

    public bool HasPlaylistItems => Playlist.Count > 0;

    public int PlaylistCount => Playlist.Count;

    [ObservableProperty]
    private PlaylistItem? _selectedItem;

    [ObservableProperty]
    private DisplayInfo? _selectedDisplay;

    [ObservableProperty]
    private bool _autoAdvance = true;

    [ObservableProperty]
    private bool _isOutputEnabled;

    [ObservableProperty]
    private double _volume = 80;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private double _positionMs;

    [ObservableProperty]
    private double _durationMs;

    [ObservableProperty]
    private string _statusText = "준비";

    [ObservableProperty]
    private string _timeText = "00:00 / 00:00";

    [ObservableProperty]
    private bool _isCuePanelOpen = true;

    [ObservableProperty]
    private FadeStopOption _selectedFadeStop;

    [ObservableProperty]
    private bool _isFadingOut;

    /// <summary>Selected cue is the one loaded in the player (playing, paused or held).</summary>
    [ObservableProperty]
    private bool _isSelectedCueActive;

    [ObservableProperty]
    private string? _cueEditMessage;

    /// <summary>Seek bar markers for the current cue; -1 hides them.</summary>
    [ObservableProperty]
    private double _activeInPointMs = -1;

    [ObservableProperty]
    private double _activeOutPointMs = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScheduleClockMode))]
    private bool _isScheduleDelayMode = true;

    [ObservableProperty]
    private string _scheduleDelayText = "5";

    [ObservableProperty]
    private string _scheduleClockText = "";

    [ObservableProperty]
    private string? _scheduleError;

    [ObservableProperty]
    private bool _hasPendingSchedule;

    [ObservableProperty]
    private string _nextScheduleText = "";

    [ObservableProperty]
    private string? _nextScheduleTooltip;

    [ObservableProperty]
    private bool _isNextScheduleImminent;

    public bool IsScheduleClockMode
    {
        get => !IsScheduleDelayMode;
        set
        {
            if (value)
                IsScheduleDelayMode = false;
        }
    }

    private bool _showRemainingTime;
    private int _currentIndex = -1;
    private bool _suppressOutputToggle;
    private readonly DispatcherTimer _positionTimer;
    private long? _pendingSeekMs;
    private DateTime _ignorePlayerSyncUntil;
    private int _seekRetryCount;
    private double _volumeBeforeMute = 80;

    // Cue runtime (reset on every PlayAt / Stop).
    private readonly DispatcherTimer _cueTimer;
    private readonly DispatcherTimer _scheduleTimer;
    private readonly PlaybackClock _clock = new();
    private readonly Stopwatch _fadeInWatch = new();
    private readonly Stopwatch _cueStartWatch = new();
    private Stopwatch? _fadeStopWatch;
    private int _fadeStopMs;
    private int _activeFadeInMs;
    private bool _activeLoop;
    private long? _activeLoopOutMs;
    private bool _cueActive;
    private bool _isHolding;
    private bool _outArmed;
    private long _armedOutMs = long.MinValue;
    private long _lastEstimateMs = -1;
    private double _cueGain = 1;
    private int _appliedVolume = -1;
    private string? _playStatus;
    private PlaylistItem? _nextScheduledItem;

    private PlaylistItem? CurrentItem =>
        _currentIndex >= 0 && _currentIndex < Playlist.Count ? Playlist[_currentIndex] : null;

    public MainViewModel(MediaPlayerService media)
    {
        _media = media;
        _selectedFadeStop = FadeStopOptions[2];
        ApplyVolume();
        _media.EndReached += OnEndReached;
        _media.TimeChanged += OnTimeChanged;
        _media.LengthChanged += OnLengthChanged;
        _media.Playing += OnPlaying;
        _media.Paused += OnPaused;
        _media.Stopped += OnStopped;

        _positionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _positionTimer.Tick += (_, _) => SyncPositionFromPlayer();

        // Normal priority: fades must not stall behind layout/render work.
        _cueTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(25)
        };
        _cueTimer.Tick += (_, _) => OnCueTick();

        _scheduleTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _scheduleTimer.Tick += (_, _) => OnScheduleTick();
        _scheduleTimer.Start();

        Playlist.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasPlaylistItems));
            OnPropertyChanged(nameof(PlaylistCount));
        };
        RefreshDisplays();
    }

    private void OnPlaying(object? sender, EventArgs e)
    {
        StatusText = _fadeStopWatch is not null ? "페이드 정지 중" : _playStatus ?? "재생 중";
        _clock.Reset();

        // Some audio outputs reset volume when a stream starts; force a re-apply.
        _appliedVolume = -1;
        ApplyVolume();

        if (_activeFadeInMs > 0 && _fadeInWatch.ElapsedMilliseconds < _activeFadeInMs)
            _fadeInWatch.Start();
        _fadeStopWatch?.Start();

        SyncOutputSurface();
        StartPositionTimer();
        StartCueTimer();
    }

    private void OnPaused(object? sender, EventArgs e)
    {
        StatusText = _isHolding ? "마지막 장면에서 멈춤 · 정지 또는 다음으로 해제" : "일시정지";
        _fadeInWatch.Stop();
        _fadeStopWatch?.Stop();
        _clock.Reset();
        StopCueTimer();
        StopPositionTimer();
        SyncPositionFromPlayer();
    }

    private void OnStopped(object? sender, EventArgs e)
    {
        StopPositionTimer();
        StopCueTimer();
        SyncOutputSurface();
    }

    [RelayCommand]
    private void RefreshDisplays()
    {
        var previous = SelectedDisplay?.DeviceName;
        Displays.Clear();
        foreach (var d in _displays.GetDisplays())
            Displays.Add(d);

        SelectedDisplay = Displays.FirstOrDefault(d => d.DeviceName == previous)
                          ?? Displays.FirstOrDefault(d => !d.IsPrimary)
                          ?? Displays.FirstOrDefault();
    }

    [RelayCommand]
    private void AddFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "미디어 파일 추가",
            Multiselect = true,
            Filter = "미디어 파일|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.wma;*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm;*.m4v|" +
                     "음원|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.wma|" +
                     "영상|*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm;*.m4v|" +
                     "모든 파일|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        AddFilesFromPaths(dialog.FileNames);
    }

    public void AddFilesFromPaths(IEnumerable<string> paths)
    {
        var addedItems = new List<PlaylistItem>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                continue;

            if (!IsSupportedMedia(path))
                continue;

            var item = new PlaylistItem(path);
            item.EditRejected += OnCueEditRejected;
            item.PropertyChanged += OnItemPropertyChanged;
            Playlist.Add(item);
            addedItems.Add(item);
        }

        if (addedItems.Count == 0)
            return;

        RenumberPlaylist();

        if (SelectedItem is null && Playlist.Count > 0)
            SelectedItem = Playlist[0];

        StatusText = $"{addedItems.Count}개 파일 추가";
        _ = ProbeDurationsAsync(addedItems);
    }

    private async Task ProbeDurationsAsync(IReadOnlyList<PlaylistItem> items)
    {
        foreach (var item in items)
        {
            if (_disposed)
                return;

            try
            {
                var duration = await _media.ProbeDurationAsync(item.FilePath);
                if (duration is > 0)
                    item.DurationMs = duration.Value;
            }
            catch
            {
                // Keep "--:--" if probe fails.
            }
        }
    }

    private static bool IsSupportedMedia(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp3" or ".wav" or ".flac" or ".m4a" or ".aac" or ".ogg" or ".wma"
            or ".mp4" or ".mkv" or ".mov" or ".avi" or ".wmv" or ".webm" or ".m4v";
    }

    private void RenumberPlaylist()
    {
        for (var i = 0; i < Playlist.Count; i++)
            Playlist[i].Number = i + 1;
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedItem is null)
            return;

        var removed = SelectedItem;
        var index = Playlist.IndexOf(removed);
        var wasCurrent = index == _currentIndex;
        removed.EditRejected -= OnCueEditRejected;
        removed.PropertyChanged -= OnItemPropertyChanged;
        Playlist.RemoveAt(index);

        if (wasCurrent)
        {
            EndCueRuntime();
            _media.Stop();
            ClearPlayingFlags();
            _currentIndex = -1;
            PositionMs = 0;
            DurationMs = 0;
            ApplyVolume();
            UpdateTimeText();
            RefreshMarkers();
            StatusText = "정지";
        }
        else if (_currentIndex > index)
        {
            _currentIndex--;
        }

        SelectedItem = Playlist.ElementAtOrDefault(Math.Min(index, Playlist.Count - 1));
        RenumberPlaylist();
        RefreshSchedules();
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedItem is null)
            return;

        var index = Playlist.IndexOf(SelectedItem);
        if (index <= 0)
            return;

        Playlist.Move(index, index - 1);
        AdjustCurrentIndexAfterMove(index, index - 1);
        RenumberPlaylist();
        RefreshSchedules();
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedItem is null)
            return;

        var index = Playlist.IndexOf(SelectedItem);
        if (index < 0 || index >= Playlist.Count - 1)
            return;

        Playlist.Move(index, index + 1);
        AdjustCurrentIndexAfterMove(index, index + 1);
        RenumberPlaylist();
        RefreshSchedules();
    }

    [RelayCommand]
    private void Play()
    {
        if (Playlist.Count == 0)
            return;

        // Resume only when the paused cue is still the one selected.
        if (_cueActive
            && !_isHolding
            && _media.IsPaused
            && (SelectedItem is null || ReferenceEquals(SelectedItem, CurrentItem)))
        {
            _media.SetPause(false);
            return;
        }

        var index = SelectedItem is null ? Math.Max(_currentIndex, 0) : Playlist.IndexOf(SelectedItem);
        PlayAt(index < 0 ? 0 : index);
    }

    [RelayCommand]
    private void Pause()
    {
        if (_isHolding)
        {
            StatusText = "마지막 장면에서 멈춤 · 정지 또는 다음으로 해제";
            return;
        }

        if (_media.IsPlaying)
            _media.SetPause(true);
        else if (_cueActive && _media.IsPaused)
            _media.SetPause(false);
    }

    [RelayCommand]
    private void Stop()
    {
        ClearSeekHold();
        StopPositionTimer();
        EndCueRuntime();
        _media.Stop();
        ClearPlayingFlags();
        PositionMs = 0;
        ApplyVolume();
        UpdateTimeText();
        StatusText = "정지";
        _outputWindow?.SetBlackout(true);
        RefreshSelectedCueState();
    }

    /// <summary>First press fades out; a second press (or while paused) stops at once.</summary>
    [RelayCommand]
    private void FadeStop()
    {
        if (!_cueActive || CurrentItem is null)
            return;

        if (_fadeStopWatch is not null || !_media.IsPlaying)
        {
            Stop();
            return;
        }

        _fadeStopMs = SelectedFadeStop.Ms;
        _fadeStopWatch = Stopwatch.StartNew();
        IsFadingOut = true;
        StatusText = $"페이드 정지 중 ({SelectedFadeStop.Label}) · 한 번 더 누르면 즉시 정지";
    }

    [RelayCommand]
    private void Previous()
    {
        if (Playlist.Count == 0)
            return;

        var index = _currentIndex <= 0 ? 0 : _currentIndex - 1;
        PlayAt(index);
    }

    [RelayCommand]
    private void Next()
    {
        if (Playlist.Count == 0)
            return;

        var index = _currentIndex < 0 ? 0 : _currentIndex + 1;
        if (index >= Playlist.Count)
            return;

        PlayAt(index);
    }

    [RelayCommand]
    private void PlaySelected()
    {
        if (SelectedItem is null)
            return;

        var index = Playlist.IndexOf(SelectedItem);
        if (index >= 0)
            PlayAt(index);
    }

    [RelayCommand]
    private void SelectPrevious()
    {
        if (Playlist.Count == 0)
            return;

        if (SelectedItem is null)
        {
            SelectedItem = Playlist[0];
            return;
        }

        var index = Playlist.IndexOf(SelectedItem);
        if (index > 0)
            SelectedItem = Playlist[index - 1];
    }

    [RelayCommand]
    private void SelectNext()
    {
        if (Playlist.Count == 0)
            return;

        if (SelectedItem is null)
        {
            SelectedItem = Playlist[0];
            return;
        }

        var index = Playlist.IndexOf(SelectedItem);
        if (index >= 0 && index < Playlist.Count - 1)
            SelectedItem = Playlist[index + 1];
    }

    [RelayCommand]
    private void SeekBackward() => SeekBy(-5_000);

    [RelayCommand]
    private void SeekForward() => SeekBy(5_000);

    public void SeekBy(long deltaMs)
    {
        if (_media.Player.Media is null || DurationMs <= 0 || _isHolding)
            return;

        var next = Math.Clamp(PositionMs + deltaMs, 0, DurationMs);
        _media.Time = (long)next;
        _clock.Reset();
        PositionMs = next;
        UpdateTimeText();
    }

    [RelayCommand]
    private void ToggleCuePanel() => IsCuePanelOpen = !IsCuePanelOpen;

    // ---- Cue settings -------------------------------------------------

    [RelayCommand]
    private void SetInPointHere() => MarkCurrentCue(isIn: true);

    [RelayCommand]
    private void SetOutPointHere() => MarkCurrentCue(isIn: false);

    /// <summary>Sets the in/out point of the loaded cue at the current playback position.</summary>
    public void MarkCurrentCue(bool isIn)
    {
        if (!_cueActive || CurrentItem is not { } item)
        {
            StatusText = "재생 중이거나 일시정지된 큐에서만 지정할 수 있습니다";
            return;
        }

        var position = (long)Math.Round(PositionMs);
        var ok = isIn ? item.TrySetInPoint(position) : item.TrySetOutPoint(position);
        if (!ok)
            return;

        StatusText = isIn
            ? $"{item.Number:00}번 시작 지점 {TimeFormat.FormatCue(item.InPointMs)} · 다음 재생부터 적용"
            : item.OutPointMs is long o
                ? $"{item.Number:00}번 종료 지점 {TimeFormat.FormatCue(o)} · 다음 재생부터 적용"
                : $"{item.Number:00}번 종료 지점: 파일 끝";
    }

    [RelayCommand]
    private void ClearTrim()
    {
        if (SelectedItem is not { } item)
            return;

        item.TrySetOutPoint(null);
        item.TrySetInPoint(0);
        CueEditMessage = null;
    }

    private void OnCueEditRejected(object? sender, string message)
    {
        CueEditMessage = message;
        StatusText = message;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PlaylistItem item)
            return;

        switch (e.PropertyName)
        {
            case nameof(PlaylistItem.InPointMs):
            case nameof(PlaylistItem.OutPointMs):
                if (ReferenceEquals(item, CurrentItem))
                {
                    RefreshMarkers();
                    UpdateTimeText();
                }
                ClearEditMessageFor(item);
                break;
            case nameof(PlaylistItem.FadeInMs):
            case nameof(PlaylistItem.FadeOutMs):
            case nameof(PlaylistItem.EndAction):
                ClearEditMessageFor(item);
                break;
        }
    }

    private void ClearEditMessageFor(PlaylistItem item)
    {
        if (ReferenceEquals(item, SelectedItem))
            CueEditMessage = null;
    }

    partial void OnSelectedItemChanged(PlaylistItem? value)
    {
        CueEditMessage = null;
        ScheduleError = null;
        RefreshSelectedCueState();
    }

    // ---- Schedules ----------------------------------------------------

    partial void OnIsScheduleDelayModeChanged(bool value)
    {
        ScheduleError = null;
        if (!value && string.IsNullOrWhiteSpace(ScheduleClockText))
        {
            // Suggest the next 5-minute mark at least 10 minutes away.
            var t = DateTime.Now.AddMinutes(10);
            var rounded = t.Date.AddHours(t.Hour).AddMinutes((t.Minute + 4) / 5 * 5);
            ScheduleClockText = rounded.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
    }

    [RelayCommand]
    private void ScheduleSelected()
    {
        ScheduleError = null;
        if (SelectedItem is not { } item)
            return;

        var now = DateTime.Now;
        DateTime target;
        if (IsScheduleDelayMode)
        {
            if (!TimeFormat.TryParseDuration(ScheduleDelayText, 60_000, out var ms) || ms < 1000)
            {
                ScheduleError = "분 단위로 입력하세요. 예: 10 (10분), 2:30 (2분 30초), 1:00:00 (1시간)";
                return;
            }

            target = now.AddMilliseconds(ms);
        }
        else
        {
            if (!TimeFormat.TryParseClock(ScheduleClockText, out var timeOfDay))
            {
                ScheduleError = "24시간제로 입력하세요. 예: 19:30, 19:30:15";
                return;
            }

            target = now.Date + timeOfDay;
            if (target <= now.AddSeconds(1))
                target = target.AddDays(1);
        }

        item.ScheduledAt = target;
        RefreshSchedules();
        StatusText = $"{item.Number:00}번 예약: {TimeFormat.FormatClock(target)}";
    }

    [RelayCommand]
    private void CancelSchedule()
    {
        if (SelectedItem is not { ScheduledAt: not null } item)
            return;

        item.ScheduledAt = null;
        RefreshSchedules();
        StatusText = $"{item.Number:00}번 예약 취소";
    }

    [RelayCommand]
    private void SelectNextScheduled()
    {
        if (_nextScheduledItem is null)
            return;

        SelectedItem = _nextScheduledItem;
        IsCuePanelOpen = true;
    }

    private void RefreshSchedules() => OnScheduleTick();

    private void OnScheduleTick()
    {
        if (_disposed)
            return;

        var now = DateTime.Now;
        PlaylistItem? due = null;
        var dueAt = DateTime.MinValue;
        PlaylistItem? missed = null;
        PlaylistItem? next = null;

        foreach (var item in Playlist)
        {
            if (item.ScheduledAt is not DateTime at)
            {
                item.ScheduleCountdown = "";
                item.IsScheduleImminent = false;
                continue;
            }

            if (now >= at)
            {
                item.ScheduledAt = null;
                item.ScheduleCountdown = "";
                item.IsScheduleImminent = false;

                // PC slept or the clock jumped: firing minutes late would surprise the room.
                if (now - at > ScheduleMissGrace)
                    missed = item;
                else if (due is null || at >= dueAt)
                {
                    due = item;
                    dueAt = at;
                }
                continue;
            }

            var remaining = at - now;
            item.ScheduleCountdown = TimeFormat.FormatCountdown(remaining);
            item.IsScheduleImminent = remaining <= ScheduleImminent;
            if (next?.ScheduledAt is not DateTime nextAt || at < nextAt)
                next = item;
        }

        _nextScheduledItem = next;
        HasPendingSchedule = next is not null;
        NextScheduleText = next is null ? "" : $"{next.Number:00}번 · {next.ScheduleCountdown}";
        NextScheduleTooltip = next is null ? null : $"{next.DisplayName}\n{next.ScheduleTooltip}";
        IsNextScheduleImminent = next?.IsScheduleImminent == true;

        if (due is not null)
        {
            var index = Playlist.IndexOf(due);
            if (index >= 0)
                PlayAt(index, "예약 실행");
        }
        else if (missed is not null)
        {
            StatusText = $"{missed.Number:00}번 예약 시각이 지나 실행하지 않았습니다";
        }

        KeepAwake.Set(HasPendingSchedule || _media.IsPlaying);
    }

    // ---- Output -------------------------------------------------------

    partial void OnIsOutputEnabledChanged(bool value)
    {
        if (_suppressOutputToggle)
            return;

        if (value)
            EnableOutput();
        else
            DisableOutput();
    }

    partial void OnSelectedDisplayChanged(DisplayInfo? value)
    {
        if (IsOutputEnabled && value is not null)
            PlaceOutput(value);
    }

    // ---- Volume -------------------------------------------------------

    partial void OnVolumeChanged(double value)
    {
        var vol = (int)Math.Round(Math.Clamp(value, 0, 100));
        if (vol > 0)
        {
            _volumeBeforeMute = vol;
            if (IsMuted)
                IsMuted = false;
        }
        else
        {
            IsMuted = true;
        }

        ApplyVolume();
    }

    [RelayCommand]
    private void ToggleMute()
    {
        if (IsMuted)
        {
            IsMuted = false;
            Volume = _volumeBeforeMute > 0 ? _volumeBeforeMute : 80;
        }
        else
        {
            if (Volume > 0)
                _volumeBeforeMute = Volume;
            IsMuted = true;
            Volume = 0;
        }

        ApplyVolume();
    }

    /// <summary>Player volume = master slider × current fade gain.</summary>
    private void ApplyVolume()
    {
        var master = IsMuted ? 0 : Math.Clamp(Volume, 0, 100);
        var vol = (int)Math.Round(master * Math.Clamp(_cueGain, 0, 1));
        if (vol == _appliedVolume)
            return;

        _appliedVolume = vol;
        _media.Volume = vol;
    }

    // ---- Seek ---------------------------------------------------------

    public void BeginSeek() => _media.BeginSeek();

    public void PreviewSeek(double positionMs)
    {
        if (!_media.IsSeeking)
            return;

        PositionMs = Math.Clamp(positionMs, 0, Math.Max(0, DurationMs));
        UpdateTimeText();
    }

    public void EndSeek(double positionMs)
    {
        var clamped = (long)Math.Clamp(positionMs, 0, Math.Max(0, DurationMs));
        _pendingSeekMs = clamped;
        _seekRetryCount = 0;
        _ignorePlayerSyncUntil = DateTime.UtcNow.AddMilliseconds(800);
        _media.EndSeek(clamped);
        _clock.Reset();
        PositionMs = clamped;
        UpdateTimeText();
    }

    public void CancelSeekInteraction()
    {
        if (!_media.IsSeeking)
            return;

        _media.CancelSeek();
        // Keep pending if we already committed a value via EndSeek; only cancel in-progress drag.
        SyncPositionFromPlayer();
    }

    private void ClearSeekHold()
    {
        _pendingSeekMs = null;
        _seekRetryCount = 0;
        _ignorePlayerSyncUntil = DateTime.MinValue;
        _media.CancelSeek();
    }

    private void StartPositionTimer()
    {
        if (!_positionTimer.IsEnabled)
            _positionTimer.Start();
    }

    private void StopPositionTimer()
    {
        if (_positionTimer.IsEnabled)
            _positionTimer.Stop();
    }

    private void StartCueTimer()
    {
        if (!_cueTimer.IsEnabled)
            _cueTimer.Start();
    }

    private void StopCueTimer()
    {
        if (_cueTimer.IsEnabled)
            _cueTimer.Stop();
    }

    private void SyncPositionFromPlayer()
    {
        if (_media.IsSeeking || _disposed)
            return;

        var time = _media.GetPlaybackTimeMs();
        if (time < 0)
            return;

        if (_pendingSeekMs is long target)
        {
            var delta = Math.Abs(time - target);
            if (delta <= 350)
            {
                _pendingSeekMs = null;
                _seekRetryCount = 0;
            }
            else
            {
                PositionMs = target;
                UpdateTimeText();

                if (DateTime.UtcNow >= _ignorePlayerSyncUntil)
                {
                    if (_seekRetryCount >= 3)
                    {
                        _pendingSeekMs = null;
                        _seekRetryCount = 0;
                    }
                    else
                    {
                        _seekRetryCount++;
                        _media.SeekTo(target);
                        _ignorePlayerSyncUntil = DateTime.UtcNow.AddMilliseconds(500);
                    }
                }

                return;
            }
        }

        if (Math.Abs(PositionMs - time) < 1)
            return;

        PositionMs = time;
        UpdateTimeText();
    }

    private void EnableOutput()
    {
        if (SelectedDisplay is null)
        {
            SetOutputEnabledSafe(false);
            StatusText = "출력할 디스플레이를 선택하세요";
            return;
        }

        if (_outputWindow is null)
        {
            _outputWindow = new OutputWindow();
            _outputWindow.Closed += OnOutputClosed;
            _outputWindow.AttachMediaPlayer(_media.Player);
            PlaceOutput(SelectedDisplay);
            _outputWindow.Show();
            SyncOutputSurface();

            // Return focus to control window.
            System.Windows.Application.Current.MainWindow?.Activate();
        }
        else
        {
            PlaceOutput(SelectedDisplay);
            SyncOutputSurface();
        }

        SetOutputEnabledSafe(true);
        StatusText = $"출력: {SelectedDisplay.Label}";
    }

    private void DisableOutput()
    {
        if (_outputWindow is not null)
        {
            _outputWindow.Closed -= OnOutputClosed;
            _outputWindow.DetachMediaPlayer();
            _outputWindow.Close();
            _outputWindow = null;
        }

        SetOutputEnabledSafe(false);
    }

    private void SetOutputEnabledSafe(bool value)
    {
        if (IsOutputEnabled == value)
            return;

        _suppressOutputToggle = true;
        IsOutputEnabled = value;
        _suppressOutputToggle = false;
    }

    private void PlaceOutput(DisplayInfo display)
    {
        _outputWindow?.PlaceOnDisplay(display);
    }

    private void OnOutputClosed(object? sender, EventArgs e)
    {
        if (_outputWindow is not null)
        {
            _outputWindow.Closed -= OnOutputClosed;
            _outputWindow.DetachMediaPlayer();
            _outputWindow = null;
        }

        SetOutputEnabledSafe(false);
    }

    // ---- Cue engine ---------------------------------------------------

    private bool PlayAt(int index, string? statusPrefix = null)
    {
        if (index < 0 || index >= Playlist.Count)
            return false;

        var item = Playlist[index];
        if (!System.IO.File.Exists(item.FilePath))
        {
            SelectedItem = item;
            StatusText = $"파일을 찾을 수 없습니다: {item.DisplayName}";
            return false;
        }

        ClearSeekHold();
        ResetCueRuntime();
        ClearPlayingFlags();
        _currentIndex = index;
        item.IsPlaying = true;
        SelectedItem = item;
        PositionMs = item.InPointMs;
        DurationMs = item.DurationMs > 0 ? item.DurationMs : 0;

        _activeLoop = item.EndAction == CueEndAction.Loop;
        _activeLoopOutMs = _activeLoop ? item.OutPointMs : null;
        _activeFadeInMs = item.FadeInMs;
        _cueGain = _activeFadeInMs > 0 ? 0 : 1;
        _cueActive = true;
        _cueStartWatch.Restart();
        ApplyVolume();
        UpdateTimeText();
        RefreshMarkers();

        var verb = _activeLoop ? "반복 재생" : "재생";
        var status = $"{verb}: {item.DisplayName}";
        if (statusPrefix is not null)
            status = $"{statusPrefix} · {status}";
        if (item.IsVideo && !IsOutputEnabled)
            status += " · 영상 출력 꺼짐";
        _playStatus = status;
        StatusText = status;

        // Cover last frame / audio-only so the projector never flashes the previous cue.
        _outputWindow?.BeginCueTransition();
        _media.PlayFile(item.FilePath, BuildMediaOptions(item, _activeLoop));
        StartPositionTimer();
        RefreshSelectedCueState();
        return true;
    }

    private static List<string> BuildMediaOptions(PlaylistItem item, bool loop)
    {
        var options = new List<string>(3);
        if (item.InPointMs > 0)
            options.Add($":start-time={ToSeconds(item.InPointMs)}");

        // Loops run inside LibVLC so they stay seamless; other out points are
        // enforced by the cue timer so they can be edited while playing.
        if (loop)
        {
            if (item.OutPointMs is long outMs)
                options.Add($":stop-time={ToSeconds(outMs)}");
            options.Add(":input-repeat=65535");
        }

        return options;

        static string ToSeconds(long ms) => (ms / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
    }

    private CueEndAction ResolveEndAction(PlaylistItem item) => item.EndAction switch
    {
        CueEndAction.Default => AutoAdvance ? CueEndAction.Next : CueEndAction.Stop,
        CueEndAction.Hold when !item.IsVideo => CueEndAction.Stop,
        var action => action
    };

    private void OnCueTick()
    {
        if (_disposed || !_cueActive || _isHolding || CurrentItem is not { } item || !_media.IsPlaying)
            return;

        var raw = _media.GetPlaybackTimeMs();
        if (raw < 0)
            return;

        var est = _clock.Estimate(raw);
        var jumped = _lastEstimateMs >= 0 && Math.Abs(est - _lastEstimateMs) > SeekJumpThresholdMs;
        _lastEstimateMs = est;

        var length = _media.Length > 0 ? _media.Length : item.DurationMs;
        var action = ResolveEndAction(item);

        // Out point only fires when playback runs into it, not when the operator
        // sets it at the current spot or jumps past it with the seek bar.
        long outMs = item.OutPointMs is long o && (length <= 0 || o < length) ? o : -1;
        if (outMs != _armedOutMs || jumped || !_outArmed)
            _outArmed = outMs >= 0 && est < outMs - OutArmMarginMs;
        _armedOutMs = outMs;

        long cueEnd;
        bool endTracked;
        if (_activeLoop)
        {
            cueEnd = _activeLoopOutMs is long loopOut && (length <= 0 || loopOut < length) ? loopOut : length;
            endTracked = action != CueEndAction.Loop && cueEnd > 0;
        }
        else if (outMs >= 0)
        {
            cueEnd = outMs;
            endTracked = _outArmed;
        }
        else
        {
            cueEnd = length;
            endTracked = length > 0;
        }

        // Gains: fade in × fade out × fade stop.
        var inGain = 1.0;
        if (_activeFadeInMs > 0)
        {
            var t = _fadeInWatch.ElapsedMilliseconds / (double)_activeFadeInMs;
            inGain = t >= 1 ? 1 : t * t;
        }

        var outGain = 1.0;
        if (item.FadeOutMs > 0 && endTracked && action != CueEndAction.Loop)
        {
            var remaining = cueEnd - est;
            if (remaining < item.FadeOutMs)
            {
                var r = Math.Max(0, remaining) / (double)item.FadeOutMs;
                outGain = r * r;
            }
        }

        var stopGain = 1.0;
        if (_fadeStopWatch is not null)
        {
            var t = _fadeStopWatch.ElapsedMilliseconds / (double)Math.Max(1, _fadeStopMs);
            if (t >= 1)
            {
                Stop();
                return;
            }

            stopGain = (1 - t) * (1 - t);
        }

        _cueGain = inGain * outGain * stopGain;
        ApplyVolume();

        if (!endTracked)
            return;

        if (action == CueEndAction.Hold)
        {
            var holdAt = cueEnd >= length
                ? length - HoldBeforeEofMs
                : cueEnd - (_activeLoop ? 120 : 0);
            if (est < holdAt)
                return;

            if (_fadeStopWatch is not null)
                Stop();
            else
                EnterHold();
            return;
        }

        if (_activeLoop)
        {
            // Loop was switched off mid-play: leave at the end of this pass.
            if (est >= cueEnd - 120)
                CompleteCue(fromTimer: true);
            return;
        }

        if (outMs >= 0 && est >= outMs)
            CompleteCue(fromTimer: true);
    }

    private void EnterHold()
    {
        _isHolding = true;
        _media.SetPause(true);
        StatusText = "마지막 장면에서 멈춤 · 정지 또는 다음으로 해제";
        RefreshSelectedCueState();
    }

    /// <summary>The cue reached its end (EOF, out point, or loop exit).</summary>
    private void CompleteCue(bool fromTimer)
    {
        if (CurrentItem is not { } item)
            return;

        if (_fadeStopWatch is not null)
        {
            Stop();
            return;
        }

        switch (ResolveEndAction(item))
        {
            case CueEndAction.Loop:
                // A file that ends instantly would otherwise restart in a tight loop.
                if (!fromTimer && _cueStartWatch.ElapsedMilliseconds < 1500)
                {
                    FinishStopped(fromTimer, $"반복할 수 없는 파일입니다: {item.DisplayName}");
                    return;
                }

                if (!PlayAt(_currentIndex))
                    FinishStopped(fromTimer, StatusText);
                return;

            case CueEndAction.Hold when fromTimer:
                EnterHold();
                return;

            case CueEndAction.Next:
                var next = _currentIndex + 1;
                if (next >= Playlist.Count)
                {
                    FinishList(fromTimer);
                    return;
                }

                // Keep the "file not found" message PlayAt left behind.
                if (!PlayAt(next))
                    FinishStopped(fromTimer, StatusText);
                return;

            default:
                FinishStopped(fromTimer, "재생 완료");
                return;
        }
    }

    private void FinishStopped(bool stopPlayer, string status)
    {
        EndCueRuntime();
        if (stopPlayer)
            _media.Stop();
        ClearPlayingFlags();
        ApplyVolume();
        StatusText = status;
        _outputWindow?.SetBlackout(true);
        RefreshSelectedCueState();
    }

    private void FinishList(bool stopPlayer)
    {
        EndCueRuntime();
        if (stopPlayer)
            _media.Stop();
        ClearPlayingFlags();
        _currentIndex = -1;
        ApplyVolume();
        StatusText = "목록 끝";
        _outputWindow?.SetBlackout(true);
        RefreshSelectedCueState();
        RefreshMarkers();
    }

    /// <summary>Clears fades/hold and marks no cue as active.</summary>
    private void EndCueRuntime()
    {
        StopCueTimer();
        ResetCueRuntime();
        _cueActive = false;
    }

    private void ResetCueRuntime()
    {
        _fadeStopWatch = null;
        IsFadingOut = false;
        _isHolding = false;
        _fadeInWatch.Reset();
        _activeFadeInMs = 0;
        _activeLoop = false;
        _activeLoopOutMs = null;
        _outArmed = false;
        _armedOutMs = long.MinValue;
        _lastEstimateMs = -1;
        _cueGain = 1;
        _clock.Reset();
    }

    private void RefreshSelectedCueState()
    {
        IsSelectedCueActive = _cueActive
                              && SelectedItem is not null
                              && ReferenceEquals(SelectedItem, CurrentItem);
    }

    private void RefreshMarkers()
    {
        if (CurrentItem is { } item)
        {
            ActiveInPointMs = item.InPointMs > 0 ? item.InPointMs : -1;
            ActiveOutPointMs = item.OutPointMs ?? -1;
        }
        else
        {
            ActiveInPointMs = -1;
            ActiveOutPointMs = -1;
        }
    }

    /// <summary>
    /// Video surface: black for audio / stopped; show frames only while a video cue is active.
    /// </summary>
    private void SyncOutputSurface()
    {
        if (_outputWindow is null)
            return;

        var item = CurrentItem;
        if (item is null || !item.IsVideo || !_media.IsPlaying)
        {
            _outputWindow.SetBlackout(true);
            return;
        }

        _outputWindow.SetBlackout(false);
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        ClearSeekHold();
        StopPositionTimer();
        StopCueTimer();
        PositionMs = Math.Max(PositionMs, DurationMs);
        UpdateTimeText();
        CompleteCue(fromTimer: false);
    }

    private void OnTimeChanged(object? sender, EventArgs e)
    {
        // Prefer the smooth timer while playing; keep this as a fallback when paused.
        if (_positionTimer.IsEnabled)
            return;

        SyncPositionFromPlayer();
    }

    private void OnLengthChanged(object? sender, EventArgs e)
    {
        DurationMs = Math.Max(0, _media.Length);
        UpdateTimeText();
    }

    /// <summary>Out point of the current cue, or the file length.</summary>
    private double EffectiveEndMs()
    {
        if (CurrentItem?.OutPointMs is long o && (DurationMs <= 0 || o < DurationMs))
            return o;
        return DurationMs;
    }

    private void UpdateTimeText()
    {
        var end = EffectiveEndMs();
        if (_showRemainingTime)
        {
            var remaining = Math.Max(0, end - PositionMs);
            TimeText = $"-{FormatTime(remaining)} / {FormatTime(end)}";
        }
        else
        {
            TimeText = $"{FormatTime(PositionMs)} / {FormatTime(end)}";
        }
    }

    [RelayCommand]
    private void ToggleTimeDisplay()
    {
        _showRemainingTime = !_showRemainingTime;
        UpdateTimeText();
    }

    private static string FormatTime(double ms)
    {
        if (ms < 0 || double.IsNaN(ms))
            ms = 0;

        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"mm\:ss");
    }

    private void ClearPlayingFlags()
    {
        foreach (var item in Playlist)
            item.IsPlaying = false;
    }

    private void AdjustCurrentIndexAfterMove(int from, int to)
    {
        if (_currentIndex == from)
            _currentIndex = to;
        else if (from < _currentIndex && to >= _currentIndex)
            _currentIndex--;
        else if (from > _currentIndex && to <= _currentIndex)
            _currentIndex++;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ClearSeekHold();
        StopPositionTimer();
        StopCueTimer();
        _scheduleTimer.Stop();
        DisableOutput();
        KeepAwake.Set(false);
        _media.EndReached -= OnEndReached;
        _media.TimeChanged -= OnTimeChanged;
        _media.LengthChanged -= OnLengthChanged;
        _media.Playing -= OnPlaying;
        _media.Paused -= OnPaused;
        _media.Stopped -= OnStopped;
    }
}
