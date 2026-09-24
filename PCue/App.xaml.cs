using LibVLCSharp.Shared;

namespace PCue;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        Core.Initialize();
        base.OnStartup(e);
    }
}
