using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace PCue.Installer;

internal static class SetupRunner
{
    private const string PackId = "PCue";

    public static void Install(bool createDesktopShortcut, bool launchWhenDone)
    {
        var setupPath = ExtractEmbeddedSetup();
        var start = new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = "--silent",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("설치 프로그램을 시작하지 못했습니다.");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"설치가 실패했습니다. (코드 {process.ExitCode})");

        var exe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            PackId,
            "PCue.exe");

        if (!File.Exists(exe))
            throw new InvalidOperationException("설치는 끝났지만 PCue.exe를 찾지 못했습니다.");

        if (createDesktopShortcut)
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PCue.lnk"),
                exe);

        if (launchWhenDone)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true
            });
        }
    }

    private static string ExtractEmbeddedSetup()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("VelopackSetup.exe")
            ?? throw new InvalidOperationException(
                "설치 패키지가 포함되어 있지 않습니다. GitHub Release의 PCue-Setup.exe를 사용해 주세요.");

        var dir = Path.Combine(Path.GetTempPath(), "PCue-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "PCue-win-Setup.exe");
        using var file = File.Create(path);
        stream.CopyTo(file);
        return path;
    }

    private static void CreateShortcut(string linkPath, string targetPath)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(targetPath);
        link.SetWorkingDirectory(Path.GetDirectoryName(targetPath)!);
        link.SetDescription("PCue");
        ((IPersistFile)link).Save(linkPath, true);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
