using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCue.Models;
using PCue.Services;
using PCue.Views;

namespace PCue.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly MediaPlayerService _media;
    private readonly DisplayService _displays = new();
    private OutputWindow? _outputWindow;
    private bool _disposed;

    public ObservableCollection<PlaylistItem> Playlist { get; } = [];
    public ObservableCollection<DisplayInfo> Displays { get; } = [];

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
    private double _positionMs;

    [ObservableProperty]
    private double _durationMs;

    [ObservableProperty]
    private string _statusText = "준비";

    [ObservableProperty]
    private string _timeText = "00:00 / 00:00";

    private bool _showRemainingTime;
    private int _currentIndex = -1;
    private bool _suppressOutputToggle;
    private readonly DispatcherTimer _positionTimer;
    private long? _pendingSeekMs;
    private DateTime _ignorePlayerSyncUntil;

    public MainViewModel(MediaPlayerService media)
    {
        _media = media;
        _media.Volume = (int)Volume;
        _media.EndReached += OnEndReached;
        _media.TimeChanged += OnTimeChanged;
        _media.LengthChanged += OnLengthChanged;
        _media.Playing += (_, _) =>
        {
            StatusText = "재생 중";
            StartPositionTimer();
        };
        _media.Paused += (_, _) =>
        {
            StatusText = "일시정지";
            StopPositionTimer();
            SyncPositionFromPlayer();
        };
        _media.Stopped += (_, _) =>
        {
            StopPositionTimer();
            if (_currentIndex < 0)
                StatusText = "정지";
        };

        _positionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _positionTimer.Tick += (_, _) => SyncPositionFromPlayer();

        RefreshDisplays();
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
                     "오디오|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.wma|" +
                     "비디오|*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm;*.m4v|" +
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

        var index = Playlist.IndexOf(SelectedItem);
        var wasCurrent = index == _currentIndex;
        Playlist.RemoveAt(index);

        if (wasCurrent)
        {
            _media.Stop();
            ClearPlayingFlags();
            _currentIndex = -1;
            PositionMs = 0;
            DurationMs = 0;
            UpdateTimeText();
        }
        else if (_currentIndex > index)
        {
            _currentIndex--;
        }

        SelectedItem = Playlist.ElementAtOrDefault(Math.Min(index, Playlist.Count - 1));
        RenumberPlaylist();
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
    }

    [RelayCommand]
    private void Play()
    {
        if (Playlist.Count == 0)
            return;

        if (_currentIndex >= 0 && _currentIndex < Playlist.Count && !_media.IsPlaying && _media.Player.Media is not null)
        {
            _media.Play();
            return;
        }

        var index = SelectedItem is null ? 0 : Playlist.IndexOf(SelectedItem);
        if (index < 0)
            index = 0;

        PlayAt(index);
    }

    [RelayCommand]
    private void Pause() => _media.TogglePause();

    [RelayCommand]
    private void Stop()
    {
        _media.Stop();
        ClearPlayingFlags();
        PositionMs = 0;
        UpdateTimeText();
        StatusText = "정지";
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
        if (_media.Player.Media is null || DurationMs <= 0)
            return;

        var next = Math.Clamp(PositionMs + deltaMs, 0, DurationMs);
        _media.Time = (long)next;
        PositionMs = next;
        UpdateTimeText();
    }

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

    partial void OnVolumeChanged(double value)
    {
        _media.Volume = (int)Math.Round(value);
    }

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
        // LibVLC seek is async; hold UI on target until player catches up.
        _ignorePlayerSyncUntil = DateTime.UtcNow.AddMilliseconds(800);
        _media.EndSeek(clamped);
        PositionMs = clamped;
        UpdateTimeText();
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
                // Player reached the seek target.
                _pendingSeekMs = null;
            }
            else
            {
                // Keep showing the clicked point; never snap back to a stale Time.
                PositionMs = target;
                UpdateTimeText();

                if (DateTime.UtcNow >= _ignorePlayerSyncUntil)
                {
                    // One retry if seek hasn't landed yet.
                    _media.SeekTo(target);
                    _ignorePlayerSyncUntil = DateTime.UtcNow.AddMilliseconds(500);
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

            // Return focus to control window.
            System.Windows.Application.Current.MainWindow?.Activate();
        }
        else
        {
            PlaceOutput(SelectedDisplay);
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

    private void PlayAt(int index)
    {
        if (index < 0 || index >= Playlist.Count)
            return;

        ClearPlayingFlags();
        _currentIndex = index;
        var item = Playlist[index];
        item.IsPlaying = true;
        SelectedItem = item;
        PositionMs = 0;
        if (item.DurationMs > 0)
            DurationMs = item.DurationMs;
        UpdateTimeText();
        _media.CancelSeek();
        _media.PlayFile(item.FilePath);
        StatusText = $"재생: {item.DisplayName}";
        StartPositionTimer();
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        if (!AutoAdvance)
        {
            ClearPlayingFlags();
            StatusText = "재생 완료";
            return;
        }

        var next = _currentIndex + 1;
        if (next < Playlist.Count)
            PlayAt(next);
        else
        {
            ClearPlayingFlags();
            _currentIndex = -1;
            StatusText = "목록 끝";
        }
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

    private void UpdateTimeText()
    {
        if (_showRemainingTime)
        {
            var remaining = Math.Max(0, DurationMs - PositionMs);
            TimeText = $"-{FormatTime(remaining)} / {FormatTime(DurationMs)}";
        }
        else
        {
            TimeText = $"{FormatTime(PositionMs)} / {FormatTime(DurationMs)}";
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
        StopPositionTimer();
        DisableOutput();
        _media.EndReached -= OnEndReached;
        _media.TimeChanged -= OnTimeChanged;
        _media.LengthChanged -= OnLengthChanged;
    }
}
