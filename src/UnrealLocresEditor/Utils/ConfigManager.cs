using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace UnrealLocresEditor.Utils
{
    public static class DefaultConfig
    {
        public static readonly string ThemeKey = "CoolGray";
        public static readonly string AccentColor = "#4e3cb2";
        public static readonly bool DiscordRPCEnabled = !OperatingSystem.IsLinux();
        public static readonly bool DiscordRPCPrivacy = false;
        public static readonly string DiscordRPCPrivacyString = "Editing a file";
        public static readonly bool UseWine = OperatingSystem.IsLinux();
        public static readonly TimeSpan AutoSaveInterval = TimeSpan.FromMinutes(5);
        public static readonly bool AutoSaveEnabled = true;
        public static readonly bool AutoUpdateEnabled = true;
        public static readonly double DefaultColumnWidth = 300;
        public static readonly bool RestoreLastSession = true;
        public static readonly bool OpenSaveFolderAfterSaving = false;
        public static readonly bool EnableDebugLogging = false;

        // NEW FONT SETTINGS
        public static readonly string EditorFontFamily = "Segoe UI"; // Default font
        public static readonly double EditorFontSize = 14.0;         // Default size
        public static readonly bool EnableRTL = false;               // Default Left-to-Right
    }

    public class AppConfig
    {
        private static AppConfig? _instance;
        private static readonly object _lock = new object();

        public string ThemeKey { get; set; } = DefaultConfig.ThemeKey;
        public string AccentColor { get; set; } = DefaultConfig.AccentColor;
        public bool DiscordRPCEnabled { get; set; } = DefaultConfig.DiscordRPCEnabled;
        public bool DiscordRPCPrivacy { get; set; } = DefaultConfig.DiscordRPCPrivacy;
        public string DiscordRPCPrivacyString { get; set; } = DefaultConfig.DiscordRPCPrivacyString;
        public bool UseWine { get; set; } = DefaultConfig.UseWine;
        public TimeSpan AutoSaveInterval { get; set; } = DefaultConfig.AutoSaveInterval;
        public bool AutoSaveEnabled { get; set; } = DefaultConfig.AutoSaveEnabled;
        public bool AutoUpdateEnabled { get; set; } = DefaultConfig.AutoUpdateEnabled;
        public double DefaultColumnWidth { get; set; } = DefaultConfig.DefaultColumnWidth;
        public bool RestoreLastSession { get; set; } = DefaultConfig.RestoreLastSession;
        public bool OpenSaveFolderAfterSaving { get; set; } = DefaultConfig.OpenSaveFolderAfterSaving;
        public bool EnableDebugLogging { get; set; } = DefaultConfig.EnableDebugLogging;
        public List<string> RecentFiles { get; set; } = new();
        public List<string> LastSessionFiles { get; set; } = new();

        // NEW FONT SETTINGS PROPERTIES
        public string EditorFontFamily { get; set; } = DefaultConfig.EditorFontFamily;
        public double EditorFontSize { get; set; } = DefaultConfig.EditorFontSize;
        public bool EnableRTL { get; set; } = DefaultConfig.EnableRTL;

        public static AppConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= Load();
                    }
                }
                return _instance;
            }
        }

        public static AppConfig Reload()
        {
            lock (_lock)
            {
                _instance = Load();
                return _instance;
            }
        }

        private static string GetConfigDirectory()
        {
            if (OperatingSystem.IsWindows())
            {
                return Path.Combine(
                  Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                  "UnrealLocresEditor"
                );
            }
            else if (OperatingSystem.IsLinux())
            {
                return Path.Combine(
                  Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                  ".config",
                  "UnrealLocresEditor"
                );
            }

            throw new PlatformNotSupportedException("Unsupported OS.");
        }

        private static string GetConfigFilePath()
        {
            string configDirectory = GetConfigDirectory();
            Directory.CreateDirectory(configDirectory);
            return Path.Combine(configDirectory, "config.json");
        }

        private static Dictionary<string, Func<AppConfig, bool>> GetValidationRules()
        {
            return new Dictionary<string, Func<AppConfig, bool>>()
      {
        { "AccentColor", config => IsValidHexColor(config.AccentColor) },
        {
          "DiscordRPCEnabled",
          config => config.DiscordRPCEnabled == true || config.DiscordRPCEnabled == false
        },
        {
          "DiscordRPCPrivacy",
          config => config.DiscordRPCPrivacy == true || config.DiscordRPCPrivacy == false
        },
        {
          "DiscordRPCPrivacyString",
          config => !string.IsNullOrEmpty(config.DiscordRPCPrivacyString)
        },
        { "UseWine", config => config.UseWine == true || config.UseWine == false },
        {
          "AutoSaveInterval",
          config =>
            config.AutoSaveInterval > TimeSpan.Zero
            && config.AutoSaveInterval.TotalMilliseconds <= int.MaxValue
        },
        {
          "AutoSaveEnabled",
          config => config.AutoSaveEnabled == true || config.AutoSaveEnabled == false
        },
        {
          "AutoUpdateEnabled",
          config => config.AutoUpdateEnabled == true || config.AutoUpdateEnabled == false
        },
        {
          "RestoreLastSession",
          config => config.RestoreLastSession == true || config.RestoreLastSession == false
        },
        {
          "OpenSaveFolderAfterSaving",
          config => config.OpenSaveFolderAfterSaving == true || config.OpenSaveFolderAfterSaving == false
        },
        {
          "EnableDebugLogging",
          config => config.EnableDebugLogging == true || config.EnableDebugLogging == false
        },
        {
          "DefaultColumnWidth",
          config => config.DefaultColumnWidth > 0 && config.DefaultColumnWidth <= 2500
        },
                // NEW VALIDATION RULES
                {
                    "EditorFontSize",
                    config => config.EditorFontSize >= 8 && config.EditorFontSize <= 72
                },
                {
                    "EnableRTL",
                    config => config.EnableRTL == true || config.EnableRTL == false
                },
                {
                    "RecentFiles",
                    config => config.RecentFiles != null
                },
                {
                    "LastSessionFiles",
                    config => config.LastSessionFiles != null
                },
      };
        }

        public static bool IsValidHexColor(string color)
        {
            return TryNormalizeHexColor(color, out _);
        }

        private static bool TryNormalizeHexColor(string? color, out string normalizedColor)
        {
            normalizedColor = DefaultConfig.AccentColor;

            if (string.IsNullOrWhiteSpace(color))
                return false;

            var trimmed = color.Trim();
            if (Regex.IsMatch(trimmed, @"^#[0-9A-Fa-f]{6}$"))
            {
                normalizedColor = trimmed;
                return true;
            }

            if (Regex.IsMatch(trimmed, @"^#[0-9A-Fa-f]{8}$"))
            {
                normalizedColor = "#" + trimmed.Substring(3, 6);
                return true;
            }

            return false;
        }

        public static AppConfig Load()
        {
            string filePath = GetConfigFilePath();
            Logger.Log($"Attempting to load config from '{filePath}'");

            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    Logger.Log($"Read config.json ({json.Length} chars)");

                    var config = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                    if (TryNormalizeHexColor(config.AccentColor, out var normalizedAccentColor))
                    {
                        config.AccentColor = normalizedAccentColor;
                    }
                    Logger.Log($"Deserialized config. RecentFiles: {config.RecentFiles?.Count ?? 0}, LastSessionFiles: {config.LastSessionFiles?.Count ?? 0}");

                    var validationRules = GetValidationRules();

                    foreach (var rule in validationRules)
                    {
                        var property = typeof(AppConfig).GetProperty(rule.Key);
                        if (property != null)
                        {
                            if (!rule.Value(config))
                            {
                                // If validation fails, revert to the default config value.
                                property.SetValue(config, GetDefaultValue(rule.Key));
                                Logger.Log($"Validation failed for '{rule.Key}', reverted to default.");
                            }
                        }
                    }

                    config.RecentFiles = (config.RecentFiles ?? new List<string>())
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(10)
                        .ToList();
                    config.LastSessionFiles = (config.LastSessionFiles ?? new List<string>())
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(10)
                        .ToList();

                    Logger.Log($"Final RecentFiles count: {config.RecentFiles.Count}");
                    return config;
                }
                else
                {
                    Logger.Log("Config file does not exist. Using defaults.");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to load config.json: {ex}");
            }

            Logger.Log("Returning default AppConfig instance.");
            return new AppConfig();
        }

        private static object? GetDefaultValue(string propertyName) =>
            propertyName switch
            {
                nameof(AppConfig.ThemeKey) => DefaultConfig.ThemeKey,
                nameof(AppConfig.AccentColor) => DefaultConfig.AccentColor,
                nameof(AppConfig.DiscordRPCEnabled) => DefaultConfig.DiscordRPCEnabled,
                nameof(AppConfig.DiscordRPCPrivacy) => DefaultConfig.DiscordRPCPrivacy,
                nameof(AppConfig.DiscordRPCPrivacyString) => DefaultConfig.DiscordRPCPrivacyString,
                nameof(AppConfig.UseWine) => DefaultConfig.UseWine,
                nameof(AppConfig.AutoSaveInterval) => DefaultConfig.AutoSaveInterval,
                nameof(AppConfig.AutoSaveEnabled) => DefaultConfig.AutoSaveEnabled,
                nameof(AppConfig.AutoUpdateEnabled) => DefaultConfig.AutoUpdateEnabled,
                nameof(AppConfig.DefaultColumnWidth) => DefaultConfig.DefaultColumnWidth,
                nameof(AppConfig.RestoreLastSession) => DefaultConfig.RestoreLastSession,
                nameof(AppConfig.OpenSaveFolderAfterSaving) => DefaultConfig.OpenSaveFolderAfterSaving,
                nameof(AppConfig.EnableDebugLogging) => DefaultConfig.EnableDebugLogging,
                nameof(AppConfig.RecentFiles) => new List<string>(),
                nameof(AppConfig.LastSessionFiles) => new List<string>(),
                nameof(AppConfig.EditorFontFamily) => DefaultConfig.EditorFontFamily,
                nameof(AppConfig.EditorFontSize) => DefaultConfig.EditorFontSize,
                nameof(AppConfig.EnableRTL) => DefaultConfig.EnableRTL,
                _ => null,
            };

        public void Save()
        {
            try
            {
                Console.WriteLine(
                  $"Saving config: {JsonConvert.SerializeObject(this, Formatting.Indented)}"
                );
                string filePath = GetConfigFilePath();
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to save config: {e}");
            }
        }
    }
}
