using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ReactiveUI;
using UnrealLocresEditor.Utils;
using UnrealLocresEditor.Views;

#nullable disable

namespace UnrealLocresEditor.ViewModels
{
    public class PreferencesWindowViewModel : ReactiveObject
    {
        private readonly Window _window;
        private readonly MainWindow _mainWindow;

        // --- THEME SETTINGS ---
        public List<ThemeOption> AvailableThemes { get; }
        private ThemeOption _selectedTheme;
        public ThemeOption SelectedTheme
        {
            get => _selectedTheme;
            set => this.RaiseAndSetIfChanged(ref _selectedTheme, value);
        }

        private Color _accentColor;

        // --- OTHER SETTINGS ---
        private DiscordService _discordRPC;
        private bool _discordRPCEnabled;
        private bool _discordRPCPrivacy;
        private string _discordRPCPrivacyString;
        private bool _useWine;
        private TimeSpan _selectedAutoSaveInterval;
        private bool _autoSaveEnabled;
        private bool _autoUpdateEnabled;
        private bool _openSaveFolderAfterSaving;
        private double _defaultColumnWidth;

        // --- FONT SETTINGS ---
        private IEnumerable<FontFamily> _availableFonts;
        private FontFamily _selectedFont;
        private string _editorFontFamily;
        private double _editorFontSize;
        private bool _enableRTL;

        // *** RESTORED MISSING PROPERTY ***
        public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        public Color AccentColor
        {
            get => _accentColor;
            set
            {
                var hex = value.ToString();
                if (AppConfig.IsValidHexColor(hex))
                    this.RaiseAndSetIfChanged(ref _accentColor, value);
                else
                    this.RaiseAndSetIfChanged(ref _accentColor, Color.Parse("#4e3cb2"));
            }
        }

        public bool DiscordRPCEnabled
        {
            get => _discordRPCEnabled;
            set => this.RaiseAndSetIfChanged(ref _discordRPCEnabled, value);
        }

        public bool DiscordRPCPrivacy
        {
            get => _discordRPCPrivacy;
            set => this.RaiseAndSetIfChanged(ref _discordRPCPrivacy, value);
        }

        public string DiscordRPCPrivacyString
        {
            get => _discordRPCPrivacyString;
            set => this.RaiseAndSetIfChanged(ref _discordRPCPrivacyString, value);
        }

        public bool UseWine
        {
            get => _useWine;
            set => this.RaiseAndSetIfChanged(ref _useWine, value);
        }

        public TimeSpan SelectedAutoSaveInterval
        {
            get => _selectedAutoSaveInterval;
            set => this.RaiseAndSetIfChanged(ref _selectedAutoSaveInterval, value);
        }

        public IEnumerable<TimeSpan> AutoSaveIntervals { get; } = new[]
        {
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30),
        };

        public bool AutoSaveEnabled
        {
            get => _autoSaveEnabled;
            set => this.RaiseAndSetIfChanged(ref _autoSaveEnabled, value);
        }
        public bool AutoUpdateEnabled
        {
            get => _autoUpdateEnabled;
            set => this.RaiseAndSetIfChanged(ref _autoUpdateEnabled, value);
        }

        public double DefaultColumnWidth
        {
            get => _defaultColumnWidth;
            set
            {
                var clamped = Math.Clamp(value, 10, 10000);
                this.RaiseAndSetIfChanged(ref _defaultColumnWidth, Math.Round(clamped / 50.0) * 50.0);
            }
        }

        public bool OpenSaveFolderAfterSaving
        {
            get => _openSaveFolderAfterSaving;
            set => this.RaiseAndSetIfChanged(ref _openSaveFolderAfterSaving, value);
        }

        public IEnumerable<FontFamily> AvailableFonts
        {
            get => _availableFonts;
            set => this.RaiseAndSetIfChanged(ref _availableFonts, value);
        }

        public FontFamily SelectedFont
        {
            get => _selectedFont;
            set => this.RaiseAndSetIfChanged(ref _selectedFont, value);
        }
        public string EditorFontFamily
        {
            get => _editorFontFamily;
            set => this.RaiseAndSetIfChanged(ref _editorFontFamily, value);
        }

        public double EditorFontSize
        {
            get => _editorFontSize;
            set => this.RaiseAndSetIfChanged(ref _editorFontSize, Math.Clamp(value, 8, 72));
        }

        public bool EnableRTL
        {
            get => _enableRTL;
            set => this.RaiseAndSetIfChanged(ref _enableRTL, value);
        }

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        public PreferencesWindowViewModel(Window window, MainWindow mainWindow)
        {
            _window = window;
            _mainWindow = mainWindow;
            var config = AppConfig.Instance;

            // 1. SETUP THEMES LIST
            AvailableThemes = new List<ThemeOption>
            {
                new ThemeOption("Cool Gray", "CoolGray"),
                new ThemeOption("Purple Dark (Default)", "Default"),
                new ThemeOption("Classic Dark", "Dark"),
                new ThemeOption("Soft Light", "Light")
            };

            // Set Selected Theme based on Config
            SelectedTheme = AvailableThemes.FirstOrDefault(x => x.Key == config.ThemeKey)
                            ?? AvailableThemes.First(x => x.Key == "Default");

            // 2. SETUP FONTS
            AvailableFonts = FontManager.Current.SystemFonts.OrderBy(x => x.Name).ToList();
            var currentFont = AvailableFonts.FirstOrDefault(x => x.Name == config.EditorFontFamily);
            SelectedFont = currentFont ?? AvailableFonts.FirstOrDefault(x => x.Name == "Segoe UI");

            // 3. LOAD OTHER SETTINGS
            AccentColor = Color.Parse(config.AccentColor);
            DiscordRPCEnabled = config.DiscordRPCEnabled;
            DiscordRPCPrivacy = config.DiscordRPCPrivacy;
            DiscordRPCPrivacyString = config.DiscordRPCPrivacyString;
            UseWine = config.UseWine;
            SelectedAutoSaveInterval = config.AutoSaveInterval;
            AutoSaveEnabled = config.AutoSaveEnabled;
            AutoUpdateEnabled = config.AutoUpdateEnabled;
            OpenSaveFolderAfterSaving = config.OpenSaveFolderAfterSaving;
            DefaultColumnWidth = config.DefaultColumnWidth;
            EditorFontFamily = config.EditorFontFamily;
            EditorFontSize = config.EditorFontSize;
            EnableRTL = config.EnableRTL;

            if (!AutoSaveIntervals.Contains(SelectedAutoSaveInterval))
                SelectedAutoSaveInterval = TimeSpan.FromMinutes(5);

            SaveCommand = ReactiveCommand.Create(Save);
            CancelCommand = ReactiveCommand.Create(Cancel);
        }

        private void Save()
        {
            var config = AppConfig.Instance;

            // 1. SAVE THEME & FONT
            config.ThemeKey = SelectedTheme.Key;
            if (SelectedFont != null) config.EditorFontFamily = SelectedFont.Name;

            // 2. SAVE OTHER SETTINGS
            config.AccentColor = AccentColor.ToString();
            config.DiscordRPCEnabled = DiscordRPCEnabled;
            config.DiscordRPCPrivacy = DiscordRPCPrivacy;
            config.DiscordRPCPrivacyString = DiscordRPCPrivacyString;
            config.UseWine = UseWine;
            config.AutoSaveInterval = SelectedAutoSaveInterval;
            config.AutoSaveEnabled = AutoSaveEnabled;
            config.AutoUpdateEnabled = AutoUpdateEnabled;
            config.OpenSaveFolderAfterSaving = OpenSaveFolderAfterSaving;
            config.DefaultColumnWidth = DefaultColumnWidth;
            config.EditorFontSize = EditorFontSize;
            config.EnableRTL = EnableRTL;

            // 3. SAVE TO DISK
            config.Save();

            // 4. APPLY INSTANTLY
            if (Application.Current is App app)
            {
                app.SetTheme(config.ThemeKey);
                app.SetAccent(AccentColor);
            }

            _mainWindow?.ApplyEditorSettings();

            _window.Close();
        }

        private void Cancel()
        {
            _window.Close();
        }
    }

    public class ThemeOption
    {
        public string Name { get; }
        public string Key { get; }
        public ThemeOption(string name, string key) { Name = name; Key = key; }
        public override string ToString() => Name;
    }
}
