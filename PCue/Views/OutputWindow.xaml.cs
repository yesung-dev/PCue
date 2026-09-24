using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using LibVLCSharp.Shared;
using PCue.Models;

namespace PCue.Views;

public partial class OutputWindow : Window
{
    public OutputWindow()
    {
        InitializeComponent();
        ShowActivated = false;
        Loaded += OnLoaded;
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
            var bottomRight = fromDevice.Transform(new System.Windows.Point(display.X + display.Width, display.Y + display.Height));
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Topmost = true;
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
        DetachMediaPlayer();
        base.OnClosed(e);
    }
}
