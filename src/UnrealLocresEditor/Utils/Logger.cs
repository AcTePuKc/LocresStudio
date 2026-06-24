using System;
using System.Diagnostics;
using System.IO;

namespace UnrealLocresEditor.Utils
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static bool _enabled = IsEnabledByEnvironment();

        public static bool IsEnabled => _enabled;

        public static void Configure(bool enabled)
        {
            _enabled = enabled || IsEnabledByEnvironment();
        }

        private static bool IsEnabledByEnvironment()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                if (Array.Exists(args, arg =>
                    string.Equals(arg, "-debug-log", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "--debug-log", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                var env = Environment.GetEnvironmentVariable("LOCRESSTUDIO_DEBUG_LOG");
                return string.Equals(env, "1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string GetLogFilePath()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnrealLocresEditor");
                    Directory.CreateDirectory(dir);
                    return Path.Combine(dir, "app.log");
                }
                else if (OperatingSystem.IsLinux())
                {
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "UnrealLocresEditor");
                    Directory.CreateDirectory(dir);
                    return Path.Combine(dir, "app.log");
                }
            }
            catch { }

            // Fallback to current directory
            return Path.Combine(Directory.GetCurrentDirectory(), "app.log");
        }

        public static void Log(string message)
        {
            if (!_enabled)
                return;

            try
            {
                var line = $"[{DateTime.UtcNow:O}] {message}";
                Debug.WriteLine(line);
                lock (_lock)
                {
                    File.AppendAllText(GetLogFilePath(), line + Environment.NewLine);
                }
            }
            catch
            {
                // Swallow logging errors to avoid interfering with app behavior
            }
        }
    }
}
