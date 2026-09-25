using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace PCue;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        var splash = new SplashWindow();
        splash.Show();

        // Let the splash paint before heavy init.
        DoEvents();

        Core.Initialize();

        var main = new MainWindow();
        MainWindow = main;
        main.ContentRendered += (_, _) =>
        {
            splash.Close();
            main.Activate();
            main.Topmost = true;
            main.Topmost = false;
            main.Focus();
        };
        main.Show();

        base.OnStartup(e);
    }

    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Render,
            () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}
