using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using PCue.Models;

namespace PCue.Views;

public partial class OutputWindow : Window
{
    private readonly DispatcherTimer _cursorTimer;
    private bool _blackout;

    public OutputWindow()
    {
        InitializeComponent();
        ShowActivated = false;
        Loaded += OnLoaded;
        MouseMove += OnMouseMove;

        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _cursorTimer.Tick += (_, _) =>
        {
            _cursorTimer.Stop();
            Cursor = System.Windows.Input.Cursors.None;
        };
    }

    public void AttachMediaPlayer(LibVLCSharp.Shared.MediaPlayer mediaPlayer)
    {
        VideoView.MediaPlayer = mediaPlayer;
    }

    public void DetachMediaPlayer()
    {
        VideoView.MediaPlayer = null;
    }

    public void PlaceOnDisplay(DisplayInfo display)
    {
        WindowState = WindowState.Normal;

        // Screen.Bounds are device pixels; WPF layout uses DIPs.
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is not null)
        {
            var fromDevice = source.CompositionTarget.TransformFromDevice;
            var topLeft = fromDevice.Transform(new System.Windows.Point(display.X, display.Y));
            var bottomRight = fromDevice.Transform(
                new System.Windows.Point(display.X + display.Width, display.Y + display.Height));
            Left = topLeft.X;
            Top = topLeft.Y;
            Width = Math.Max(1, bottomRight.X - topLeft.X);
            Height = Math.Max(1, bottomRight.Y - topLeft.Y);
            return;
        }

        Left = display.X;
        Top = display.Y;
        Width = display.Width;
        Height = display.Height;
    }

    /// <summary>Hide video surface (cue change / audio-only / stop).</summary>
    public void SetBlackout(bool on)
    {
        _blackout = on;
        Blackout.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        // Collapse the HWND host so it cannot punch through the blackout.
        VideoView.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
    }

    public void BeginCueTransition() => SetBlackout(true);

    public void EndCueTransition()
    {
        if (_blackout)
            SetBlackout(false);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Topmost = true;
        Cursor = System.Windows.Input.Cursors.None;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        Cursor = System.Windows.Input.Cursors.Arrow;
        _cursorTimer.Stop();
        _cursorTimer.Start();
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _cursorTimer.Stop();
        DetachMediaPlayer();
        base.OnClosed(e);
    }
}
