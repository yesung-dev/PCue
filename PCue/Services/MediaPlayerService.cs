using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace PCue.Services;

public sealed class MediaPlayerService : IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private readonly Dispatcher _dispatcher;
    private Media? _currentMedia;
    private bool _disposed;
    private bool _isSeeking;

    public MediaPlayer Player => _mediaPlayer;

    public LibVLC LibVlc => _libVlc;

    public event EventHandler? EndReached;
    public event EventHandler? TimeChanged;
    public event EventHandler? LengthChanged;
    public event EventHandler? Playing;
    public event EventHandler? Paused;
    public event EventHandler? Stopped;

    public void SeekTo(long timeMs)
    {
        if (_mediaPlayer.Media is null)
            return;

        timeMs = Math.Max(0, timeMs);
        var length = _mediaPlayer.Length;

        if (length > 0)
            timeMs = Math.Min(timeMs, length);

        // Use Time only — setting Time and Position together can cause a double jump.
        if (_mediaPlayer.IsSeekable || length > 0)
            _mediaPlayer.Time = timeMs;
    }

    public long Time
    {
        get => _mediaPlayer.Time;
        set => SeekTo(value);
    }

    public long Length => _mediaPlayer.Length;

    public int Volume
    {
        get => _mediaPlayer.Volume;
        set => _mediaPlayer.Volume = Math.Clamp(value, 0, 100);
    }

    public bool IsPlaying => _mediaPlayer.IsPlaying;

    public MediaPlayerService()
    {
        Core.Initialize();
        _libVlc = new LibVLC("--no-video-title-show", "--quiet");
        _mediaPlayer = new MediaPlayer(_libVlc);
        _dispatcher = Dispatcher.CurrentDispatcher;

        _mediaPlayer.EndReached += OnEndReached;
        _mediaPlayer.TimeChanged += (_, _) => RaiseOnUi(TimeChanged);
        _mediaPlayer.LengthChanged += (_, _) => RaiseOnUi(LengthChanged);
        _mediaPlayer.Playing += (_, _) => RaiseOnUi(Playing);
        _mediaPlayer.Paused += (_, _) => RaiseOnUi(Paused);
        _mediaPlayer.Stopped += (_, _) => RaiseOnUi(Stopped);
    }

    public void PlayFile(string filePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _currentMedia?.Dispose();
        _currentMedia = new Media(_libVlc, filePath, FromType.FromPath);
        _mediaPlayer.Play(_currentMedia);
    }

    public void Play()
    {
        if (_mediaPlayer.Media is not null)
            _mediaPlayer.Play();
    }

    public void Pause() => _mediaPlayer.Pause();

    public void Stop() => _mediaPlayer.Stop();

    public void TogglePause()
    {
        if (_mediaPlayer.Media is null)
            return;

        if (_mediaPlayer.IsPlaying)
            _mediaPlayer.Pause();
        else
            _mediaPlayer.Play();
    }

    public void BeginSeek() => _isSeeking = true;

    public void EndSeek(long timeMs)
    {
        _isSeeking = false;
        Time = timeMs;
    }

    public void CancelSeek() => _isSeeking = false;

    public bool IsSeeking => _isSeeking;

    /// <summary>Current media time in ms.</summary>
    public long GetPlaybackTimeMs()
    {
        // Prefer Time after seeks — Position can briefly disagree and cause UI jumps.
        var time = _mediaPlayer.Time;
        if (time >= 0)
            return time;

        if (_mediaPlayer.Length > 0 && _mediaPlayer.Position >= 0)
            return (long)(_mediaPlayer.Position * _mediaPlayer.Length);

        return -1;
    }

    /// <summary>Returns duration in milliseconds, or null if unknown.</summary>
    public async Task<long?> ProbeDurationAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var media = new Media(_libVlc, filePath, FromType.FromPath);
        var status = await media.Parse(MediaParseOptions.ParseLocal, timeout: 8000);
        cancellationToken.ThrowIfCancellationRequested();

        if (status == MediaParsedStatus.Done && media.Duration > 0)
            return media.Duration;

        return null;
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        // LibVLC callbacks are not on the UI thread; marshal EndReached.
        _dispatcher.BeginInvoke(() => EndReached?.Invoke(this, EventArgs.Empty));
    }

    private void RaiseOnUi(EventHandler? handler)
    {
        if (handler is null)
            return;

        if (_dispatcher.CheckAccess())
            handler.Invoke(this, EventArgs.Empty);
        else
            _dispatcher.BeginInvoke(() => handler.Invoke(this, EventArgs.Empty));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _mediaPlayer.EndReached -= OnEndReached;
        _mediaPlayer.Stop();
        _currentMedia?.Dispose();
        _mediaPlayer.Dispose();
        _libVlc.Dispose();
    }
}
