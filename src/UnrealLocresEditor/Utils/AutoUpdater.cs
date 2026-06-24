using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json; // Requires System.Text.Json package
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using UnrealLocresEditor.Utils;
using UnrealLocresEditor.Views;

public class AutoUpdater
{
    // Queries the GitHub API directly for the tag name
    private const string GitHubApiUrl = "https://api.github.com/repos/AcTePuKc/LocresStudio/releases/latest";
    private const string LocalVersionFile = "version.txt";
    private const string TempUpdatePath = "update.zip";

    private readonly INotificationManager _notificationManager;
    private readonly MainWindow _mainWindow;

    public AutoUpdater(INotificationManager notificationManager, MainWindow mainWindow)
    {
        _notificationManager = notificationManager;
        _mainWindow = mainWindow;
    }

    public async Task CheckForUpdates(bool manualCheck = false)
    {
        if (Debugger.IsAttached)
        {
            Console.WriteLine("Skipping update check - debug mode.");
            return;
        }

        try
        {
            // 1. Get Latest Version Tag from GitHub API (e.g. "v1.0")
            string latestVersion = (await GetLatestVersionFromApi()).Trim();

            // 2. Get Local Version (e.g. "v1.0" from the text file in the folder)
            string currentVersion = File.Exists(LocalVersionFile)
                ? File.ReadAllText(LocalVersionFile).Trim()
                : "v0.0.0";

            // 3. Compare
            if (!VersionsMatch(currentVersion, latestVersion))
            {
                // UPDATE AVAILABLE
                if (manualCheck)
                {
                    var manualUpdateDialog = await ShowManualUpdateDialog(latestVersion);
                    if (manualUpdateDialog != "Update") return;
                }
                else
                {
                    // Auto-check on startup
                    if (_mainWindow._hasUnsavedChanges)
                    {
                        var result = await ShowUpdateConfirmDialog();
                        if (result != "Update") return;
                    }
                }

                await ShowUpdateNotification();

                // Construct URL: .../releases/download/v1.0/LocresStudio-v1.0-win-x64.zip
                string platformSpecificUrl = GetPlatformSpecificUrl(latestVersion);

                await DownloadUpdate(platformSpecificUrl);
                LaunchUpdateProcess();
            }
            else if (manualCheck)
            {
                _notificationManager.Show(new Notification(
                    "No Updates Available",
                    $"You are running the latest version ({currentVersion}).",
                    NotificationType.Information));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Update check failed: {ex.Message}");
            if (manualCheck)
            {
                _notificationManager.Show(new Notification(
                    "Update Check Failed",
                    $"Error: {ex.Message}",
                    NotificationType.Error));
            }
        }
    }

    private bool VersionsMatch(string v1, string v2)
    {
        // Normalize versions (remove 'v', trim spaces)
        v1 = v1.TrimStart('v', 'V').Trim();
        v2 = v2.TrimStart('v', 'V').Trim();
        return string.Equals(v1, v2, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> GetLatestVersionFromApi()
    {
        using (HttpClient client = new HttpClient())
        {
            // GitHub API requires a User-Agent
            client.DefaultRequestHeaders.Add("User-Agent", "LocresStudio-AutoUpdater");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

            var response = await client.GetAsync(GitHubApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"GitHub API returned {response.StatusCode}");
            }

            string json = await response.Content.ReadAsStringAsync();

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.TryGetProperty("tag_name", out JsonElement tagName))
                {
                    return tagName.GetString() ?? "v0.0.0";
                }
            }
            throw new Exception("Could not parse tag_name from GitHub API response.");
        }
    }

    private string GetPlatformSpecificUrl(string version)
    {
        string os = GetOperatingSystem();
        string arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";

        // Matches the format defined in release.yml: LocresStudio-v1.0-win-x64.zip
        string fileName = $"LocresStudio-{version}-{os}-{arch}.zip";

        // Points to the specific release asset
        return $"https://github.com/AcTePuKc/LocresStudio/releases/download/{version}/{fileName}";
    }

    private string GetOperatingSystem()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "win";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
        throw new NotSupportedException("Unsupported OS platform.");
    }

    private async Task DownloadUpdate(string url)
    {
        using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("User-Agent", "LocresStudio-AutoUpdater");
            byte[] updateData = await client.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(TempUpdatePath, updateData);
        }
    }

    private void LaunchUpdateProcess()
    {
        string currentProcessId = Process.GetCurrentProcess().Id.ToString();
        string currentExePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to resolve current executable path.");
        string updateScriptPath = Path.Combine(Path.GetTempPath(), "update_script");

        string scriptContent;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            scriptContent = @$"
@echo off
timeout /t 1 /nobreak >nul
:loop
tasklist /fi ""PID eq {currentProcessId}"" 2>nul | find ""{currentProcessId}"" >nul
if errorlevel 1 (
    powershell -Command ""Expand-Archive -Path '{TempUpdatePath}' -DestinationPath '{AppDomain.CurrentDomain.BaseDirectory}' -Force""
    del ""{TempUpdatePath}""
    start """" ""{currentExePath}""
    del ""%~f0""
    exit
) else (
    timeout /t 1 /nobreak >nul
    goto loop
)";
            File.WriteAllText(updateScriptPath + ".bat", scriptContent);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            scriptContent = @$"
#!/bin/bash
while true; do
    if ! ps -p {currentProcessId} > /dev/null; then
        unzip -o {TempUpdatePath} -d {AppDomain.CurrentDomain.BaseDirectory}
        rm {TempUpdatePath}
        nohup {currentExePath} &
        rm -- ""$0""
        exit
    else
        sleep 1
    fi
done";
            File.WriteAllText(updateScriptPath + ".sh", scriptContent);
            Process.Start(new ProcessStartInfo { FileName = "chmod", Arguments = $"+x {updateScriptPath}.sh", UseShellExecute = true });
        }
        else
        {
            throw new NotSupportedException("Unsupported OS.");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "bash",
            Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"/c start /min \"\" \"{updateScriptPath}.bat\"" : updateScriptPath + ".sh",
            UseShellExecute = true,
            CreateNoWindow = true
        });

        Environment.Exit(0);
    }

    // --- UI DIALOGS ---

    private async Task<string> ShowManualUpdateDialog(string latestVersion)
    {
        var dialog = new Window
        {
            Title = "Update Available",
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 20,
                Children =
                {
                    new TextBlock { Text = $"A new version {latestVersion} is available. Update now?", TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center,
                        Children = { new Button { Content = "Update" }, new Button { Content = "Cancel" } } }
                }
            }
        };

        var tcs = new TaskCompletionSource<string>();
        var buttons = GetDialogButtons(dialog, 2);
        if (buttons == null)
            return "Cancel";

        buttons[0].Click += (s, e) => { tcs.TrySetResult("Update"); dialog.Close(); };
        buttons[1].Click += (s, e) => { tcs.TrySetResult("Cancel"); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult("Cancel");

        await dialog.ShowDialog(_mainWindow);
        return await tcs.Task;
    }

    private async Task<string> ShowUpdateConfirmDialog()
    {
        var dialog = new Window
        {
            Title = "Unsaved Changes",
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 20,
                Children =
                {
                    new TextBlock { Text = "You have unsaved changes. Save before updating?", TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center,
                        Children = { new Button { Content = "Save & Update" }, new Button { Content = "Update Anyway" }, new Button { Content = "Cancel" } } }
                }
            }
        };

        var tcs = new TaskCompletionSource<string>();
        var buttons = GetDialogButtons(dialog, 3);
        if (buttons == null)
            return "Cancel";

        buttons[0].Click += (s, e) =>
        {
            try { _mainWindow.SaveEditedData(); tcs.TrySetResult("Update"); dialog.Close(); }
            catch (Exception ex)
            {
                _notificationManager.Show(new Notification("Save Error", ex.Message, NotificationType.Error));
                tcs.TrySetResult("Cancel"); dialog.Close();
            }
        };
        buttons[1].Click += (s, e) => { tcs.TrySetResult("Update"); dialog.Close(); };
        buttons[2].Click += (s, e) => { tcs.TrySetResult("Cancel"); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult("Cancel");

        await dialog.ShowDialog(_mainWindow);
        return await tcs.Task;
    }

    private async Task ShowUpdateNotification()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _notificationManager.Show(new Notification(
                "Update in progress",
                "The application will restart shortly.",
                NotificationType.Information,
            TimeSpan.FromSeconds(10)));
        });
    }

    private static Button[]? GetDialogButtons(Window dialog, int expectedCount)
    {
        var rootPanel = dialog.Content as StackPanel;
        var buttonPanel = rootPanel?.Children.Count > 1 ? rootPanel.Children[1] as StackPanel : null;
        Button[]? buttons = null;

        if (buttonPanel != null)
        {
            var result = new Button[buttonPanel.Children.Count];
            for (int i = 0; i < buttonPanel.Children.Count; i++)
            {
                if (buttonPanel.Children[i] is not Button button)
                    return null;

                result[i] = button;
            }

            buttons = result;
        }

        if (buttons == null || buttons.Length < expectedCount)
            return null;

        return buttons;
    }
}
