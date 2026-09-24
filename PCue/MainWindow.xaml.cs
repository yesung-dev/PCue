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
        _viewModel = new MainViewModel(_media);
        DataContext = _viewModel;
        Closed += OnClosed;
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

    private void SeekSlider_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _viewModel.BeginSeek();
    }

    private void SeekSlider_OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider)
            _viewModel.EndSeek(slider.Value);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
        _media.Dispose();
    }
}
