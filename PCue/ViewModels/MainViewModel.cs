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

    public bool HasPlaylistItems => Playlist.Count > 0;

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

    private bool _showRemainingTime;
    private int _currentIndex = -1;
    private bool _suppressOutputToggle;
    private readonly DispatcherTimer _positionTimer;
    private long? _pendingSeekMs;
    private DateTime _ignorePlayerSyncUntil;
    private int _seekRetryCount;
    private double _volumeBeforeMute = 80;

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

        Playlist.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPlaylistItems));
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

        // Finished track still loaded: restart instead of Play() from EOF.
        if (_currentIndex >= 0
            && _currentIndex < Playlist.Count
            && !_media.IsPlaying
            && _media.Player.Media is not null)
        {
            if (DurationMs > 0 && PositionMs >= DurationMs - 500)
            {
                PlayAt(_currentIndex);
                return;
            }

            _media.Play();
            StartPositionTimer();
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
        ClearSeekHold();
        StopPositionTimer();
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
        var vol = (int)Math.Round(Math.Clamp(value, 0, 100));
        if (vol > 0)
        {
            _volumeBeforeMute = vol;
            if (IsMuted)
                IsMuted = false;
            _media.Volume = vol;
        }
        else
        {
            IsMuted = true;
            _media.Volume = 0;
        }
    }

    [RelayCommand]
    private void ToggleMute()
    {
        if (IsMuted)
        {
            IsMuted = false;
            var restore = _volumeBeforeMute > 0 ? _volumeBeforeMute : 80;
            Volume = restore;
            _media.Volume = (int)Math.Round(restore);
        }
        else
        {
            if (Volume > 0)
                _volumeBeforeMute = Volume;
            IsMuted = true;
            Volume = 0;
            _media.Volume = 0;
        }
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
        _seekRetryCount = 0;
        _ignorePlayerSyncUntil = DateTime.UtcNow.AddMilliseconds(800);
        _media.EndSeek(clamped);
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

        ClearSeekHold();
        ClearPlayingFlags();
        _currentIndex = index;
        var item = Playlist[index];
        item.IsPlaying = true;
        SelectedItem = item;
        PositionMs = 0;
        DurationMs = item.DurationMs > 0 ? item.DurationMs : 0;
        UpdateTimeText();
        _media.PlayFile(item.FilePath);
        StatusText = $"재생: {item.DisplayName}";
        StartPositionTimer();
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        ClearSeekHold();
        StopPositionTimer();
        PositionMs = Math.Max(PositionMs, DurationMs);
        UpdateTimeText();

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
        ClearSeekHold();
        StopPositionTimer();
        DisableOutput();
        _media.EndReached -= OnEndReached;
        _media.TimeChanged -= OnTimeChanged;
        _media.LengthChanged -= OnLengthChanged;
    }
}
