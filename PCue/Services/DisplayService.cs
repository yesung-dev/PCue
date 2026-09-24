using System.Windows.Forms;
using PCue.Models;

namespace PCue.Services;

public sealed class DisplayService
{
    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var screens = Screen.AllScreens;
        var list = new List<DisplayInfo>(screens.Length);

        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var bounds = screen.Bounds;
            var role = screen.Primary ? "주 모니터" : "보조";
            list.Add(new DisplayInfo
            {
                Index = i,
                DeviceName = screen.DeviceName,
                Label = $"{i + 1}: {role} ({bounds.Width}x{bounds.Height})",
                X = bounds.X,
                Y = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height,
                IsPrimary = screen.Primary
            });
        }

        return list;
    }

    public DisplayInfo? FindByDeviceName(string? deviceName)
    {
        if (string.IsNullOrEmpty(deviceName))
            return null;

        return GetDisplays().FirstOrDefault(d => d.DeviceName == deviceName);
    }
}
