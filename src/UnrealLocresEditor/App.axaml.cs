using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Media;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnrealLocresEditor.Utils;
using UnrealLocresEditor.Views;

namespace UnrealLocresEditor
{
    public partial class App : Application
    {
        // 1. DEFINE CUSTOM VARIANTS
        public static readonly ThemeVariant CoolGray = new ThemeVariant("CoolGray", ThemeVariant.Dark);
        // New: Explicitly define Purple so it doesn't get confused with "System Default"
        public static readonly ThemeVariant Purple = new ThemeVariant("Purple", ThemeVariant.Dark);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        private bool _consoleAllocated = false;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            var config = AppConfig.Instance;
            Logger.Configure(config.EnableDebugLogging);
            SetTheme(config.ThemeKey);
            SetAccent(Color.Parse(config.AccentColor));

            if (PlatformUtils.IsWindows() && Environment.GetCommandLineArgs().Contains("-console"))
            {
                if (AllocConsole()) { _consoleAllocated = true; }
            }

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        public void SetTheme(string themeKey)
        {
            ThemeVariant variantToApply;

            switch (themeKey)
            {
                case "Light":
                    variantToApply = ThemeVariant.Light;
                    break;
                case "CoolGray":
                    variantToApply = App.CoolGray;
                    break;
                case "Default":
                case "Purple":
                    // 2. MAP "Default" TO OUR PURPLE VARIANT
                    variantToApply = App.Purple;
                    break;
                case "Dark":
                default:
                    variantToApply = ThemeVariant.Dark;
                    break;
            }

            if (RequestedThemeVariant != variantToApply)
            {
                RequestedThemeVariant = variantToApply;
            }
        }

        public void SetAccent(Color accentColor)
        {
            if (Resources.ContainsKey("SystemAccentColor"))
            {
                Resources["SystemAccentColor"] = accentColor;
            }
            else
            {
                Resources.Add("SystemAccentColor", accentColor);
            }
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                singleViewPlatform.MainView = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();

            if (_consoleAllocated)
            {
                AppDomain.CurrentDomain.ProcessExit += (sender, args) => FreeConsole();
            }
        }

        private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            LogException(e.ExceptionObject as Exception, "Unhandled Exception");
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogException(e.Exception, "Unobserved Task Exception");
            e.SetObserved();
        }

        private void LogException(Exception? ex, string type)
        {
            try
            {
                string logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "UnrealLocresEditor",
                    "Logs"
                );
                Directory.CreateDirectory(logDirectory);
                string path = Path.Combine(logDirectory, "crashlog.txt");
                File.AppendAllText(path, $"{DateTime.Now}: {type} - {ex?.Message}\n{ex?.StackTrace}\n\n");
            }
            catch { }
        }
    }
}
