using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace PCue.Installer;

public sealed class SetupWindow : Window
{
    private readonly CheckBox _desktopCheck;
    private readonly CheckBox _launchCheck;
    private readonly Button _installButton;
    private readonly Button _cancelButton;
    private readonly TextBlock _statusText;
    private readonly ProgressBar _progress;

    public SetupWindow()
    {
        Title = "PCue 설치";
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Logo_B.png", UriKind.Absolute));
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        Background = Brush("#0B0E13");
        Foreground = Brush("#E8EEF4");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 40,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });

        _desktopCheck = new CheckBox
        {
            Content = "바탕화면에 바로가기 만들기",
            IsChecked = true,
            Foreground = Brush("#E8EEF4"),
            Margin = new Thickness(0, 0, 0, 10)
        };

        _launchCheck = new CheckBox
        {
            Content = "설치가 끝나면 PCue 실행",
            IsChecked = true,
            Foreground = Brush("#E8EEF4"),
            Margin = new Thickness(0, 0, 0, 18)
        };

        _statusText = new TextBlock
        {
            Text = "설치 옵션을 고른 다음 설치를 눌러 주세요.",
            Foreground = Brush("#8B97A8"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };

        _progress = new ProgressBar
        {
            Height = 6,
            IsIndeterminate = false,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 14),
            Foreground = Brush("#3DD6C6"),
            Background = Brush("#2A3340"),
            BorderThickness = new Thickness(0)
        };

        _installButton = new Button
        {
            Content = "설치",
            Width = 96,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = Brush("#3DD6C6"),
            Foreground = Brush("#0B0E13"),
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold
        };
        _installButton.Click += Install_Click;

        _cancelButton = new Button
        {
            Content = "취소",
            Width = 96,
            Height = 32,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = Brush("#181D26"),
            Foreground = Brush("#E8EEF4"),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("#2A3340")
        };
        _cancelButton.Click += (_, _) => Close();

        var chrome = new Border
        {
            Height = 40,
            Background = Brush("#12161D"),
            BorderBrush = Brush("#2A3340"),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0)
        };

        titleRow.Children.Add(new Image
        {
            Source = BitmapFrame.Create(new Uri("pack://application:,,,/Logo_B.png", UriKind.Absolute)),
            Width = 18,
            Height = 18,
            Margin = new Thickness(0, 0, 8, 0),
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        });
        titleRow.Children.Add(new TextBlock
        {
            Text = "PCue",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13
        });
        titleRow.Children.Add(new TextBlock
        {
            Text = "설치",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 1, 0, 0),
            Foreground = Brush("#8B97A8"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        });
        chrome.Child = titleRow;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(_installButton);
        buttons.Children.Add(_cancelButton);

        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(new TextBlock
        {
            Text = "PCue를 이 컴퓨터에 설치합니다.",
            Margin = new Thickness(0, 0, 0, 16),
            FontSize = 14
        });
        body.Children.Add(_desktopCheck);
        body.Children.Add(_launchCheck);
        body.Children.Add(_statusText);
        body.Children.Add(_progress);
        body.Children.Add(buttons);

        var root = new Border
        {
            BorderBrush = Brush("#2A3340"),
            BorderThickness = new Thickness(1)
        };
        var dock = new DockPanel();
        DockPanel.SetDock(chrome, Dock.Top);
        dock.Children.Add(chrome);
        dock.Children.Add(body);
        root.Child = dock;
        Content = root;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        _installButton.IsEnabled = false;
        _desktopCheck.IsEnabled = false;
        _launchCheck.IsEnabled = false;
        _progress.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = true;
        _statusText.Text = "설치하는 중…";

        var desktop = _desktopCheck.IsChecked == true;
        var launch = _launchCheck.IsChecked == true;

        try
        {
            await Task.Run(() => SetupRunner.Install(desktop, launch)).ConfigureAwait(true);
            _statusText.Text = "설치가 끝났습니다.";
            _progress.IsIndeterminate = false;
            _progress.Value = 100;
            await Task.Delay(400).ConfigureAwait(true);
            Close();
        }
        catch (Exception ex)
        {
            _progress.Visibility = Visibility.Collapsed;
            _statusText.Text = ex.Message;
            _installButton.IsEnabled = true;
            _desktopCheck.IsEnabled = true;
            _launchCheck.IsEnabled = true;
        }
    }

    private static SolidColorBrush Brush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
