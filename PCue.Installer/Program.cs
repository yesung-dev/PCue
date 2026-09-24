using System.Windows;

namespace PCue.Installer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new SetupWindow());
    }
}
