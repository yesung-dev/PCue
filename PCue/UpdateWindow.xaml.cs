using System.Windows;
using Velopack;

namespace PCue;

public partial class UpdateWindow : Window
{
    private readonly UpdateManager _manager;
    private readonly UpdateInfo _update;
    private bool _busy;

    public UpdateWindow(UpdateManager manager, UpdateInfo update)
    {
        _manager = manager;
        _update = update;
        InitializeComponent();

        var version = update.TargetFullRelease.Version;
        MessageText.Text =
            $"새 버전 {version}이(가) 있습니다. 지금 설치하거나, 이번은 건너뛸 수 있습니다.";
    }

    private void Later_OnClick(object sender, RoutedEventArgs e)
        => Close();

    private async void Update_OnClick(object sender, RoutedEventArgs e)
    {
        _busy = true;
        AppUpdate.BlockExit = true;
        ButtonRow.Visibility = Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
        BusyText.Visibility = Visibility.Visible;
        Progress.Visibility = Visibility.Visible;
        PercentText.Visibility = Visibility.Visible;
        Progress.Value = 0;
        PercentText.Text = "0%";

        try
        {
            await _manager.DownloadUpdatesAsync(_update, percent =>
            {
                Dispatcher.Invoke(() =>
                {
                    Progress.Value = percent;
                    PercentText.Text = $"{percent}%";
                });
            }).ConfigureAwait(true);

            _manager.ApplyUpdatesAndRestart(_update);
        }
        catch (Exception ex)
        {
            _busy = false;
            AppUpdate.BlockExit = false;
            BusyText.Visibility = Visibility.Collapsed;
            Progress.Visibility = Visibility.Collapsed;
            PercentText.Visibility = Visibility.Collapsed;
            ButtonRow.Visibility = Visibility.Visible;
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_busy || AppUpdate.BlockExit)
            e.Cancel = true;
    }
}
