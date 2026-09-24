using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCue.Services;
using PCue.ViewModels;

namespace PCue;

public partial class MainWindow : Window
{
    private readonly MediaPlayerService _media = new();
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        WindowMaximizeFix.Attach(this);
        _viewModel = new MainViewModel(_media);
        DataContext = _viewModel;
        Closed += OnClosed;
        StateChanged += (_, _) =>
        {
            UpdateMaximizeGlyph();
            UpdateCornerRadius();
        };
        UpdateMaximizeGlyph();
        UpdateCornerRadius();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Focus();
        await AppUpdate.PromptIfAvailableAsync(this);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (AppUpdate.BlockExit)
            e.Cancel = true;
    }

    private void Window_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Keep hotkeys working even when ListBox/buttons hold focus.
        // Skip when typing isn't a concern and ComboBox dropdown is open.
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)
            return;

        if (Keyboard.FocusedElement is System.Windows.Controls.ComboBox { IsDropDownOpen: true })
            return;

        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        switch (e.Key)
        {
            case Key.Up:
                _viewModel.SelectPreviousCommand.Execute(null);
                ScrollSelectedIntoView();
                e.Handled = true;
                break;
            case Key.Down:
                _viewModel.SelectNextCommand.Execute(null);
                ScrollSelectedIntoView();
                e.Handled = true;
                break;
            case Key.Enter:
                _viewModel.PlaySelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Space:
                _viewModel.PauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left when shift:
                _viewModel.PreviousCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right when shift:
                _viewModel.NextCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
                _viewModel.SeekBackwardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
                _viewModel.SeekForwardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Delete:
                _viewModel.RemoveSelectedCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void ScrollSelectedIntoView()
    {
        if (PlaylistBox.SelectedItem is not null)
            PlaylistBox.ScrollIntoView(PlaylistBox.SelectedItem);
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_OnClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void Maximize_OnClick(object sender, RoutedEventArgs e)
        => ToggleMaximize();

    private void Close_OnClick(object sender, RoutedEventArgs e)
        => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void UpdateMaximizeGlyph()
    {
        if (MaximizeButton is null)
            return;

        MaximizeButton.Content = WindowState == WindowState.Maximized
            ? (string)FindResource("GlyphChromeRestore")
            : (string)FindResource("GlyphChromeMaximize");
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "이전 크기로" : "최대화";
    }

    private void UpdateCornerRadius()
    {
        var radius = WindowState == WindowState.Maximized ? 0 : 10;
        RootBorder.CornerRadius = new CornerRadius(radius);
        TitleBarBorder.CornerRadius = new CornerRadius(radius, radius, 0, 0);

        if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is { } chrome)
            chrome.CornerRadius = new CornerRadius(radius);
    }

    private void PlaylistBox_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.PlaySelectedCommand.CanExecute(null))
            _viewModel.PlaySelectedCommand.Execute(null);
    }

    private void PlaylistBox_OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = HasFileDrop(e) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void PlaylistBox_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!HasFileDrop(e))
            return;

        var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop)!;
        _viewModel.AddFilesFromPaths(ExpandDroppedPaths(files));
        e.Handled = true;
    }

    private static bool HasFileDrop(System.Windows.DragEventArgs e)
        => e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop);

    private static IEnumerable<string> ExpandDroppedPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (System.IO.Directory.Exists(path))
            {
                foreach (var file in System.IO.Directory.EnumerateFiles(path))
                    yield return file;
            }
            else
            {
                yield return path;
            }
        }
    }

    private double _seekPointerDownValue;
    private System.Windows.Point _seekPointerDownPos;
    private bool _seekDragMoved;

    private void SeekSlider_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider)
            return;

        if (slider.Maximum <= 0 && _viewModel.DurationMs > 0)
            slider.Maximum = _viewModel.DurationMs;

        if (slider.Maximum <= 0)
            return;

        _seekPointerDownPos = e.GetPosition(slider);
        _seekPointerDownValue = GetSliderValueFromMouse(slider, e);
        _seekDragMoved = false;

        _viewModel.BeginSeek();
        _viewModel.PreviewSeek(_seekPointerDownValue);
        slider.CaptureMouse();
        e.Handled = true;
    }

    private void SeekSlider_OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Slider slider || !slider.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
            return;

        var pos = e.GetPosition(slider);
        if (Math.Abs(pos.X - _seekPointerDownPos.X) > 3 || Math.Abs(pos.Y - _seekPointerDownPos.Y) > 3)
            _seekDragMoved = true;

        if (!_seekDragMoved)
            return;

        // UI scrub only — media seek once on mouse-up.
        _viewModel.PreviewSeek(GetSliderValueFromMouse(slider, e));
        e.Handled = true;
    }

    private void SeekSlider_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider || !_media.IsSeeking)
            return;

        var value = _seekDragMoved
            ? GetSliderValueFromMouse(slider, e)
            : _seekPointerDownValue;

        _viewModel.EndSeek(value);
        if (slider.IsMouseCaptured)
            slider.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void SeekSlider_OnLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_media.IsSeeking)
            return;

        // Capture lost without mouse-up (Alt+Tab, etc.) — cancel scrub without freezing sync.
        _viewModel.CancelSeekInteraction();
    }

    private static double GetSliderValueFromMouse(Slider slider, System.Windows.Input.MouseEventArgs e)
    {
        const double inset = 8;
        slider.ApplyTemplate();

        if (slider.Template?.FindName("PART_Track", slider) is System.Windows.Controls.Primitives.Track track
            && track.ActualWidth > 0)
        {
            return Math.Clamp(track.ValueFromPoint(e.GetPosition(track)), slider.Minimum, slider.Maximum);
        }

        var x = e.GetPosition(slider).X - inset;
        var width = Math.Max(1, slider.ActualWidth - inset * 2);
        var ratio = Math.Clamp(x / width, 0, 1);
        return slider.Minimum + (slider.Maximum - slider.Minimum) * ratio;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
        _media.Dispose();
    }
}
