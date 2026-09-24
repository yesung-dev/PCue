using System.Windows;
using Velopack;
using Velopack.Sources;

namespace PCue;

internal static class AppUpdate
{
    private const string RepoUrl = "https://github.com/yesung-dev/PCue";

    public static bool BlockExit { get; set; }

    public static async Task PromptIfAvailableAsync(Window owner)
    {
        try
        {
            var manager = new UpdateManager(
                new GithubSource(RepoUrl, accessToken: null, prerelease: false),
                new UpdateOptions { MaximumDeltasBeforeFallback = -1 });

            if (!manager.IsInstalled)
                return;

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(true);
            if (update is null)
                return;

            var dialog = new UpdateWindow(manager, update) { Owner = owner };
            dialog.ShowDialog();
        }
        catch
        {
            // Network/API failures must not block app startup.
        }
    }
}
