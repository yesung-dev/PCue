using System.Windows;
using System.Windows.Input;
using LibVLCSharp.Shared;
using PCue.Models;

namespace PCue.Views;

public partial class OutputWindow : Window
{
    public OutputWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public void AttachMediaPlayer(MediaPlayer mediaPlayer)
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
