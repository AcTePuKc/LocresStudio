using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using UnrealLocresEditor.Models;
using UnrealLocresEditor.Utils;

#nullable disable

namespace UnrealLocresEditor.Views
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // Main
        public DataGrid _dataGrid;
        private TextBox _searchTextBox;
        public ObservableCollection<DataRow> _rows;
        public string _currentLocresFilePath;
        private WindowNotificationManager _notificationManager;

        // Auto saving
        private System.Timers.Timer _autoSaveTimer;
        public bool _hasUnsavedChanges = false;

        // Settings
        private AppConfig _appConfig;
        private DiscordService _discordRPC;
        public bool UseWine;
        private const int MaxRecentFiles = 10;
        private bool _restoringSession;
        private bool _isSavingDocument;
        private bool _suppressNextDirtyMark;

        // Misc
        public string csvFile = "";
        public bool shownAddRowWarningDialog = false;

        private readonly ObservableCollection<LocresDocument> _documents = new();
        private LocresDocument _selectedDocument;

        public ObservableCollection<LocresDocument> Documents => _documents;

        public LocresDocument SelectedDocument
        {
            get => _selectedDocument;
            set
            {
                if (_selectedDocument != value)
                {
                    SaveSelectedDocumentState();
                    _selectedDocument = value;
                    RaisePropertyChanged(nameof(SelectedDocument));
                    ApplySelectedDocumentState();
                    UpdateStatusBar();

                    // NEW: Update Discord immediately when clicking a tab
                    _discordRPC.UpdatePresence(_selectedDocument);
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void RaisePropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public MainWindow()
        {
            _appConfig = AppConfig.Instance;
            InitializeComponent();

            // 1. FIND ALL CONTROLS (Fixes the Null Reference Crash)
            _dataGrid = this.FindControl<DataGrid>("uiDataGrid");

            // Add these two lines so C# knows about your new XAML tags:
            uiStatusText = this.FindControl<TextBlock>("uiStatusText");
            uiRowCounter = this.FindControl<TextBlock>("uiRowCounter");
            // Ensure we have a reference to the Recent Files menu item before populating it.
            try
            {
                uiRecentFilesMenuItem = this.FindControl<MenuItem>("uiRecentFilesMenuItem");
            }
            catch { }

            ApplyEditorSettings();

            // 2. SUBSCRIBE TO EVENTS
            _dataGrid.CellEditEnded += DataGrid_CellEditEnded;
            _dataGrid.BeginningEdit += DataGrid_BeginningEdit;

            // Keep this! This updates the bar when you click a row.
            _dataGrid.SelectionChanged += (s, e) => UpdateStatusBar();
            _dataGrid.PointerWheelChanged += DataGrid_PointerWheelChanged;

            UseWine = _appConfig.UseWine;
            _discordRPC = new DiscordService();

            this.Loaded += OnWindowLoaded;
            this.Closing += OnWindowClosing;
            this.KeyDown += MainWindow_KeyDown; // Keybinds

            _rows = new ObservableCollection<DataRow>();
            DataContext = this;
            _dataGrid.ItemsSource = _rows;
            RefreshRecentFilesMenu();

            Documents.CollectionChanged += Documents_CollectionChanged;
            ConfigureAutoSaveTimer();


            // For preventing shutdown if the work is unsaved
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                AppDomain.CurrentDomain.ProcessExit += OnSystemShutdown;
            }
        }
        private void DataGrid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
        {
            // 1. Get the data row being edited
            if (e.Row.DataContext is DataRow row)
            {
                // 2. Determine which column index (0, 1, or 2) is being edited
                // Note: This assumes your columns are in order: Key | Source | Target
                int columnIndex = e.Column.DisplayIndex;

                // 3. Save the OLD value safely
                if (row.Values != null && columnIndex >= 0 && columnIndex < row.Values.Length)
                {
                    _cellValueBeforeEdit = row.Values[columnIndex] ?? string.Empty;
                }
            }
        }
        public void ApplyEditorSettings()
        {
            // Reload config to get latest changes
            var _appConfig = AppConfig.Instance;

            if (_appConfig == null) return;

            // The _dataGrid field should reference your uiDataGrid control.
            if (_dataGrid != null)
            {
                if (!string.IsNullOrEmpty(_appConfig.EditorFontFamily))
                    // 1. Apply Font Family
                    try
                {
                        _dataGrid.FontFamily = new Avalonia.Media.FontFamily(_appConfig.EditorFontFamily);
                    }
                catch
                {
                        // Fallback to default if the font name is wrong or not installed
                        _dataGrid.FontFamily = Avalonia.Media.FontFamily.Default;
                    }

                // 2. Apply Font Size
                _dataGrid.FontSize = _appConfig.EditorFontSize;

                // 3. Apply RTL (Right-to-Left) direction
                // This affects column arrangement (Target | Source | Key) and text alignment.
                _dataGrid.FlowDirection = _appConfig.EnableRTL
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight;

                // Optional: Force a redraw (good practice)
                _dataGrid.InvalidateVisual();
            }
        }

        // Initialize auto saving
        private void ConfigureAutoSaveTimer()
        {
            if (_autoSaveTimer != null)
            {
                _autoSaveTimer.Stop();
                _autoSaveTimer.Elapsed -= AutoSave_Elapsed;
                _autoSaveTimer.Dispose();
                _autoSaveTimer = null;
            }

            if (_appConfig.AutoSaveEnabled && HasAutoSaveCandidates())
            {
                _autoSaveTimer = new System.Timers.Timer
                {
                    Interval = _appConfig.AutoSaveInterval.TotalMilliseconds,
                    AutoReset = true,
                };
                _autoSaveTimer.Elapsed += AutoSave_Elapsed;
                _autoSaveTimer.Start();
            }
        }

        private bool HasAutoSaveCandidates() =>
            _documents.Any(
                d => !string.IsNullOrWhiteSpace(d.WorkingPath) && d.Rows.Count > 0
            );

        private void Documents_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_documents.Count == 0)
            {
                _rows.Clear();
                _dataGrid.ItemsSource = _rows;
                _currentLocresFilePath = null;
                csvFile = string.Empty;
                _hasUnsavedChanges = false;
                if (_selectedDocument != null)
                {
                    _selectedDocument = null;
                    RaisePropertyChanged(nameof(SelectedDocument));
                }
            }
            else if (_selectedDocument == null || !_documents.Contains(_selectedDocument))
            {
                SelectedDocument = _documents.Last();
            }

            RefreshUnsavedChangesFlag();
            PersistSessionState();
            ConfigureAutoSaveTimer();
        }

        // Cleanup-After-Crash
        private void CleanupStaleTempDirectories()
        {
            try
            {
                var exeDirectory = AppContext.BaseDirectory;
                var currentInstanceId = Process.GetCurrentProcess().Id.ToString();

                // Find all folders that look like ".temp-UnrealLocresEditor-XXXX"
                var directories = Directory.GetDirectories(exeDirectory, ".temp-LocresStudio-*");

                foreach (var dir in directories)
                {
                    // IMPORTANT: Don't delete the folder we just created for THIS session!
                    if (dir.EndsWith(currentInstanceId))
                        continue;

                    try
                    {
                        // Try to delete the folder.
                        // If another instance of the app is currently running, Windows will lock the files
                        // and throw an exception. We catch that exception and simply skip it.
                        // This ensures we only delete folders from closed/crashed instances.
                        Directory.Delete(dir, true);
                        Console.WriteLine($"Cleaned up stale directory: {dir}");
                    }
                    catch
                    {
                        // Folder is locked (another app instance is running). Leave it alone.
                    }
                }
            }
            catch (Exception ex)
            {
                // General error (permissions, etc). Just ignore.
                Console.WriteLine($"Cleanup warning: {ex.Message}");
            }
        }
        private void SaveSelectedDocumentState()
        {
            if (_selectedDocument == null)
            {
                return;
            }

            _selectedDocument.ActiveCsvPath = string.IsNullOrWhiteSpace(csvFile)
                ? null
                : csvFile;
            _selectedDocument.HasUnsavedChanges = _hasUnsavedChanges;
        }

        private string _cellValueBeforeEdit = string.Empty;

        private void ApplySelectedDocumentState()
        {
            if (_selectedDocument == null)
            {
                _rows = new ObservableCollection<DataRow>();
                _dataGrid.ItemsSource = _rows;
                _dataGrid.Columns.Clear();
                _currentLocresFilePath = null;
                csvFile = string.Empty;
                _hasUnsavedChanges = false;
                UpdateDiscordPresence(null);
                return;
            }

            _rows = _selectedDocument.Rows;
            _dataGrid.ItemsSource = _rows;
            ApplyColumnsForDocument(_selectedDocument);
            _dataGrid.InvalidateMeasure();
            _dataGrid.InvalidateArrange();
            _dataGrid.InvalidateVisual();

            _currentLocresFilePath = string.IsNullOrWhiteSpace(_selectedDocument.WorkingPath)
                ? null
                : _selectedDocument.WorkingPath;
            csvFile = _selectedDocument.ActiveCsvPath ?? string.Empty;
            _hasUnsavedChanges = _selectedDocument.HasUnsavedChanges;
            UpdateDiscordPresence(_currentLocresFilePath);
        }

        private void ApplyColumnsForDocument(LocresDocument document)
        {
            if (_dataGrid == null)
            {
                return;
            }

            _dataGrid.Columns.Clear();

            for (int i = 0; i < document.ColumnHeaders.Count; i++)
            {
                var header = document.ColumnHeaders[i];
                var column = new DataGridTextColumn
                {
                    Header = header,
                    Binding = new Binding($"Values[{i}]")
                    {
                        Mode = BindingMode.TwoWay,
                    },
                    IsReadOnly = header.Equals("source", StringComparison.OrdinalIgnoreCase),
                    Width = new DataGridLength(AppConfig.Instance.DefaultColumnWidth),
                };

                _dataGrid.Columns.Add(column);
            }
        }

        private void RefreshUnsavedChangesFlag()
        {
            _hasUnsavedChanges = _documents.Any(d => d.HasUnsavedChanges);
        }

        private void MarkDocumentDirty(LocresDocument document)
        {
            if (document == null)
            {
                return;
            }

            document.HasUnsavedChanges = true;
            _hasUnsavedChanges = true;
            PersistSessionState();
            UpdateStatusBar();
        }

        private void ClearDocumentDirty(LocresDocument document)
        {
            if (document == null)
            {
                return;
            }

            document.HasUnsavedChanges = false;
            RefreshUnsavedChangesFlag();
            PersistSessionState();
            UpdateStatusBar();
        }

        private void UpdateDiscordPresence(string? path)
        {
            _discordRPC.UpdatePresence(SelectedDocument);
        }

        private void DataGrid_CellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
        {
            // 1. Check if user cancelled with Escape (we shouldn't save then)
            if (e.EditAction == DataGridEditAction.Cancel) return;
            if (_isSavingDocument || _suppressNextDirtyMark)
            {
                _suppressNextDirtyMark = false;
                return;
            }

            if (SelectedDocument != null && e.Row.DataContext is DataRow row)
            {
                int columnIndex = e.Column.DisplayIndex;

                // 2. Get the NEW value
                if (row.Values != null && columnIndex >= 0 && columnIndex < row.Values.Length)
                {
                    string newValue = row.Values[columnIndex] ?? string.Empty;

                    // 3. COMPARE: Old vs New
                    if (!string.Equals(_cellValueBeforeEdit, newValue, StringComparison.Ordinal))
                    {
                        // It changed! NOW we call your existing method.
                        MarkDocumentDirty(SelectedDocument);
                    }
                }
            }
        }
        private void SaveDocument(LocresDocument document, bool openExplorer, bool isAutoSave)
        {
            if (document == null)
                return;

            var originalDocument = SelectedDocument;

            try
            {
                if (originalDocument != document)
                {
                    SaveSelectedDocumentState();

                    SelectedDocument = document;
                }

                SaveEditedData(openExplorer);

                ClearDocumentDirty(document);
            }
            finally
            {
                if (originalDocument != null && originalDocument != document)
                {
                    SelectedDocument = originalDocument;
                }

                RefreshUnsavedChangesFlag();
            }
        }


        private void AutoSave_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!_appConfig.AutoSaveEnabled)
            {
                if (_autoSaveTimer != null)
                {
                    _autoSaveTimer.Stop();
                }
                return;
            }
            var documentsToSave = _documents
                .Where(
                    d =>
                        d.HasUnsavedChanges
                        && !string.IsNullOrWhiteSpace(d.WorkingPath)
                        && d.Rows.Count > 0
                )
                .ToList();

            if (documentsToSave.Count == 0)
            {
                return;
            }

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var document in documentsToSave)
                {
                    try
                    {
                        SaveDocument(document, openExplorer: false, isAutoSave: true);
                        _notificationManager.Show(
                            new Notification(
                                "Auto-save",
                                $"Automatically saved {document.DisplayName}.",
                                NotificationType.Information
                            )
                        );
                    }
                    catch (Exception ex)
                    {
                        _notificationManager.Show(
                            new Notification(
                                "Auto-save Error",
                                $"Failed to auto-save {document.DisplayName}: {ex.Message}",
                                NotificationType.Error
                            )
                        );
                    }
                }
            });
        }


        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            _notificationManager = new WindowNotificationManager(this)
            {
                Position = NotificationPosition.TopRight,
                MaxItems = 1,
            };

            await RestoreLastSessionAsync();

            // Skip update check in Avalonia Designer
            if (Design.IsDesignMode)
            {
                Console.WriteLine("In designer - skipping update check.");
                return;
            }

#if DEBUG
            Console.WriteLine("Skipping update check - DEBUG mode.");
#else
            if (_appConfig.AutoUpdateEnabled == false)
            {
                Console.WriteLine("Skipping update check - auto update disabled.");
                return;
            }
            else
            {
                AutoUpdater updater = new AutoUpdater(_notificationManager, this);
                try
                {
                    await updater.CheckForUpdates();
                }
                catch (Exception ex)
                {
                    _notificationManager.Show(
                        new Notification(
                            "Update Check Failed",
                            $"Could not check for updates: {ex.Message}",
                            NotificationType.Error
                        )
                    );
                }
            }
#endif

            _discordRPC.Initialize();

        }

        // Ask if user wants to save when window closes + has unsaved changes
        private bool _closingHandled = false;
        private bool _isSystemShutdown = false;

        private void OnSystemShutdown(object? sender, EventArgs e)
        {
            _isSystemShutdown = true;
        }

        private async void OnWindowClosing(object sender, WindowClosingEventArgs e)
        {
            if (_closingHandled)
                return;

            RefreshUnsavedChangesFlag();

            if (_hasUnsavedChanges)
            {
                var unsavedCount = _documents.Count(d => d.HasUnsavedChanges);

                // Cancel close event
                e.Cancel = true;

                // Display prompt to save changes
                var dialog = new Window
                {
                    Title = _isSystemShutdown
                        ? "System Shutdown - Unsaved Changes"
                        : "Unsaved Changes",
                    Width = 300,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(20),
                        Spacing = 20,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = _isSystemShutdown
                                    ? unsavedCount > 1
                                        ? $"The system is shutting down. Do you want to save all {unsavedCount} unsaved files before exiting?"
                                        : "The system is shutting down. Do you want to save changes before exiting?"
                                    : unsavedCount > 1
                                        ? $"You have {unsavedCount} unsaved files. Do you want to save them before closing?"
                                        : "You have unsaved changes. Do you want to save before closing?",
                                TextWrapping = TextWrapping.Wrap,
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 10,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                Children =
                                {
                                    new Avalonia.Controls.Button { Content = "Save" },
                                    new Avalonia.Controls.Button { Content = "Don't Save" },
                                    new Avalonia.Controls.Button { Content = "Cancel" },
                                },
                            },
                        },
                    },
                };

                var result = await ShowCustomDialog(dialog);

                switch (result)
                {
                    case "Save":
                        try
                        {
                            SaveAllUnsavedDocuments();
                            CompleteClosing(e);
                        }
                        catch (Exception ex)
                        {
                            _notificationManager.Show(
                                new Notification(
                                    "Save Error",
                                    $"Failed to save changes: {ex.Message}",
                                    NotificationType.Error
                                )
                            );
                        }
                        break;

                    case "Don't Save":
                        CompleteClosing(e);
                        break;

                    case "Cancel":
                        break;
                }
            }
            else
            {
                CompleteClosing(e);
            }
        }

        private void SaveAllUnsavedDocuments()
        {
            var originalDocument = SelectedDocument;
            var unsavedDocuments = _documents.Where(d => d.HasUnsavedChanges).ToList();

            try
            {
                foreach (var document in unsavedDocuments)
                {
                    if (SelectedDocument != document)
                        SelectedDocument = document;

                    SaveEditedData(openExplorer: false);
                }
            }
            finally
            {
                if (originalDocument != null && _documents.Contains(originalDocument))
                {
                    SelectedDocument = originalDocument;
                }
                else if (_documents.Count > 0)
                {
                    SelectedDocument = _documents[0];
                }
            }
        }

        private void CompleteClosing(WindowClosingEventArgs e)
        {
            _closingHandled = true;

            if (!_isSystemShutdown)
            {
                Closing -= OnWindowClosing;
            }

            e.Cancel = false;
            CloseApplication();
        }

        private void CloseApplication()
        {
            _autoSaveTimer?.Stop();
            _autoSaveTimer?.Dispose();
            _discordRPC.Dispose();

            var window = (Window)this;
            window.Close();
        }

        private TaskCompletionSource<string> _dialogResult;

        private async Task<string> ShowCustomDialog(Window dialog)
        {
            _dialogResult = new TaskCompletionSource<string>();

            var buttons = (
                (StackPanel)((StackPanel)dialog.Content).Children[1]
            ).Children.OfType<Avalonia.Controls.Button>();

            foreach (var button in buttons)
            {
                button.Click += (s, e) =>
                {
                    _dialogResult.SetResult(((Avalonia.Controls.Button)s).Content.ToString());
                    dialog.Close();
                };
            }

            await dialog.ShowDialog(this);
            return await _dialogResult.Task;
        }

        // Statusbar Viewer
        private void UpdateStatusBar()
        {
            // 1. SAFETY CHECK: If the controls are null, stop immediately.
            // This prevents the crash on startup.
            if (uiRowCounter == null || uiStatusText == null) return;

            if (_rows == null)
            {
                uiRowCounter.Text = "0 / 0";
                uiStatusText.Text = "Ready";
                return;
            }

            // Get counts
            int totalRows = _rows.Count;
            // SelectedIndex is 0-based, so we add 1. If nothing selected (-1), show 0.
            int currentRow = _dataGrid.SelectedIndex + 1;

            uiRowCounter.Text = $"{currentRow} / {totalRows}";

            // Update Left text based on file
            if (SelectedDocument != null)
            {
                string editedState = SelectedDocument.HasUnsavedChanges ? "[Unsaved]" : "";
                uiStatusText.Text = $"Editing: {SelectedDocument.DisplayName} {editedState}";
            }
            else
            {
                uiStatusText.Text = "Ready";
            }
        }
        // Keybinds
        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyModifiers == KeyModifiers.Control)
            {
                switch (e.Key)
                {
                    case Key.S:
                        SaveMenuItem_Click(sender, new RoutedEventArgs());
                        e.Handled = true;
                        break;
                    case Key.W:
                        CloseMenuItem_Click(sender, new RoutedEventArgs());
                        e.Handled = true;
                        break;
                    case Key.F:
                        ShowFindDialog();
                        break;
                    case Key.H:
                        ShowFindReplaceDialog();
                        break;
                    case Key.T:
                        CopySourceToTarget(sender, null);
                        e.Handled = true;
                        break;
                }
            }
            else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
            {
                switch (e.Key)
                {
                    case Key.T:
                        CopySourceToTargetMultiple(sender, null);
                        e.Handled = true;
                        break;
                }
            }
        }

        private async void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Allow pressing shift+enter for multiline text.
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Shift)
            {
                if (sender is DataGrid grid && e.Source is TextBox textBox)
                {
                    int caretIndex = textBox.CaretIndex;
                    string currentText = textBox.Text ?? string.Empty;
                    textBox.Text = currentText.Insert(caretIndex, Environment.NewLine);
                    textBox.CaretIndex = caretIndex + Environment.NewLine.Length;
                    e.Handled = true;
                }
            }

            // Handle Delete key to clear cell content
            if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None)
            {
                var focusedControl = FocusManager.GetFocusedElement() as TextBox;
                if (focusedControl != null)
                {
                    // If we're already editing a cell, let the default behavior handle it
                    return;
                }

                // If we have a selected cell but aren't editing it, clear the cell content
                if (_dataGrid.SelectedItem is DataRow selectedRow)
                {
                    int selectedColumnIndex = _dataGrid.Columns.IndexOf(_dataGrid.CurrentColumn);
                    if (selectedColumnIndex >= 0)
                    {
                        // Check if the column is read-only or the "key" column (some users may accidentally hit delete on the key column as it is selected by default, and key names can be very long, so.)
                        var column = _dataGrid.CurrentColumn as DataGridTextColumn;
                        var columnHeader = column?.Header?.ToString();

                        if (
                            column != null
                            && !column.IsReadOnly
                            && !columnHeader.Equals("key", StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            // Clear the cell content
                            var newValues = (string[])selectedRow.Values.Clone();
                            newValues[selectedColumnIndex] = string.Empty;
                            selectedRow.Values = newValues;

                            // Mark as having unsaved changes
                            _hasUnsavedChanges = true;

                            e.Handled = true;
                        }
                    }
                }
            }

            // Handle Ctrl+C / Ctrl+V for copy-pasting when a cell is focused but not being directly edited.
            if (e.KeyModifiers == KeyModifiers.Control)
            {
                if (e.Key == Key.S)
                {
                    SaveMenuItem_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.W)
                {
                    CloseMenuItem_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Delete)
                {
                    DeleteSelectedRows();
                    e.Handled = true;
                    return;
                }

                var focusedControl = FocusManager.GetFocusedElement() as TextBox;

                // CASE A: Editing a specific text box? Let default copy/paste happen.
                if (focusedControl != null)
                {
                    return; // Don't handle it, let the TextBox handle the operation
                }

                // CASE B: Not editing? Handle Grid row copying or cell pasting.

                // 1. Copy  (Ctrl+C): Copy selected rows
                if (e.Key == Key.C)
                {
                    var selectedItems = _dataGrid.SelectedItems;
                    if (selectedItems != null && selectedItems.Count > 0)
                    {
                        var sb = new StringBuilder();
                        foreach (DataRow row in selectedItems)
                        {
                            // Convert row to tab-separated string for easy paste into spreadsheets
                            sb.AppendLine(string.Join("\t", row.Values));
                        }

                        await this.Clipboard.SetTextAsync(sb.ToString());
                        e.Handled = true; // Stop Avalonia from trying to copy just one cell
                    }
                    // If no rows are selected but a cell is selected, copy only that cell.
                    else if (_dataGrid.SelectedItem is DataRow selectedRow && _dataGrid.CurrentColumn != null)
                    {
                        int selectedColumnIndex = _dataGrid.Columns.IndexOf(_dataGrid.CurrentColumn);
                        if (selectedColumnIndex >= 0)
                        {
                            // Copy cell content from the underlying data.
                            string cellValue = selectedRow.Values[selectedColumnIndex];
                            await this.Clipboard.SetTextAsync(cellValue);
                            e.Handled = true;
                        }
                    }
                }

                // 2. Paste (Ctrl+V): Pasting in selected cell
                else if (e.Key == Key.V)
                {
                    if (_dataGrid.SelectedItem is DataRow selectedRow)
                    {
                        int selectedColumnIndex = _dataGrid.Columns.IndexOf(_dataGrid.CurrentColumn);
                        if (selectedColumnIndex < 0)
                            return;

                        // Begin editing if not already editing.
                        _dataGrid.BeginEdit();

                        // Defer the paste operation until the editing control (TextBox) is available.
                        Dispatcher.UIThread.Post(
                            async () =>
                            {
                                var editTextBox = FocusManager.GetFocusedElement() as TextBox;
                                if (editTextBox != null)
                                {
                                    var clipboardText = await this.Clipboard.GetTextAsync();
                                    if (!string.IsNullOrEmpty(clipboardText))
                                    {
                                        // If user has selected text, replace that
                                        if (!string.IsNullOrEmpty(editTextBox.SelectedText))
                                        {
                                            int selectionStart = editTextBox.SelectionStart;
                                            editTextBox.Text = editTextBox
                                                .Text.Remove(
                                                    selectionStart,
                                                    editTextBox.SelectionEnd - selectionStart
                                                )
                                                .Insert(selectionStart, clipboardText);
                                            editTextBox.CaretIndex =
                                                selectionStart + clipboardText.Length;
                                        }
                                        // Otherwise, replace entire cell
                                        else
                                        {
                                            editTextBox.Text = clipboardText;
                                            editTextBox.CaretIndex = clipboardText.Length;
                                        }
                                    }
                                }
                            },
                            DispatcherPriority.Background
                        );

                        e.Handled = true;
                    }
                }
            }
        }

        private void ShowFindDialog()
        {
            if (findDialog == null)
            {
                findDialog = new FindDialog();
                findDialog.Closed += FindDialog_Closed;
                findDialog.MainWindow = this;
            }

            findDialog.Show(this);
            findDialog.Activate();
        }

        private void ShowFindReplaceDialog()
        {
            if (findReplaceDialog == null)
            {
                findReplaceDialog = new FindReplaceDialog();
                findReplaceDialog.Closed += FindReplaceDialog_Closed;
                findReplaceDialog.MainWindow = this;
            }

            findReplaceDialog.Show(this);
            findReplaceDialog.Activate();
        }

        #region Copy Source to Target operation
        private void CopySourceToTarget(object sender, RoutedEventArgs e)
        {
            if (_dataGrid.SelectedItem is not DataRow selectedRow)
            {
                _notificationManager.Show(
                    new Notification(
                        "No Selection",
                        "Please select a row to copy from source to target.",
                        NotificationType.Information
                    )
                );
                return;
            }

            // Find the source and target column indices
            int sourceColumnIndex = -1;
            int targetColumnIndex = -1;

            for (int i = 0; i < _dataGrid.Columns.Count; i++)
            {
                var header = ((DataGridTextColumn)_dataGrid.Columns[i]).Header?.ToString();
                if (header?.Equals("source", StringComparison.OrdinalIgnoreCase) == true)
                {
                    sourceColumnIndex = i;
                }
                else if (header?.Equals("target", StringComparison.OrdinalIgnoreCase) == true)
                {
                    targetColumnIndex = i;
                }
            }

            if (sourceColumnIndex == -1)
            {
                _notificationManager.Show(
                    new Notification(
                        "Column Not Found",
                        "Source column not found.",
                        NotificationType.Warning
                    )
                );
                return;
            }

            if (targetColumnIndex == -1)
            {
                _notificationManager.Show(
                    new Notification(
                        "Column Not Found",
                        "Target column not found.",
                        NotificationType.Warning
                    )
                );
                return;
            }

            // Copy the text from source to target
            string sourceText = selectedRow.Values[sourceColumnIndex];
            var newValues = (string[])selectedRow.Values.Clone();
            newValues[targetColumnIndex] = sourceText;
            selectedRow.Values = newValues;

            // Mark as having unsaved changes
            _hasUnsavedChanges = true;

            _notificationManager.Show(
                new Notification(
                    "Text Copied",
                    "Source text copied to target column.",
                    NotificationType.Success
                )
            );
        }

        private void CopySourceToTargetMultiple(object sender, RoutedEventArgs e)
        {
            var selectedRows = _dataGrid.SelectedItems?.Cast<DataRow>().ToList();

            if (selectedRows == null || !selectedRows.Any())
            {
                _notificationManager.Show(
                    new Notification(
                        "No Selection",
                        "Please select one or more rows to copy from source to target.",
                        NotificationType.Information
                    )
                );
                return;
            }

            // Find the source and target column indices
            int sourceColumnIndex = -1;
            int targetColumnIndex = -1;

            for (int i = 0; i < _dataGrid.Columns.Count; i++)
            {
                var header = ((DataGridTextColumn)_dataGrid.Columns[i]).Header?.ToString();
                if (header?.Equals("source", StringComparison.OrdinalIgnoreCase) == true)
                {
                    sourceColumnIndex = i;
                }
                else if (header?.Equals("target", StringComparison.OrdinalIgnoreCase) == true)
                {
                    targetColumnIndex = i;
                }
            }

            if (sourceColumnIndex == -1 || targetColumnIndex == -1)
            {
                _notificationManager.Show(
                    new Notification(
                        "Columns Not Found",
                        "Source or target column not found.",
                        NotificationType.Warning
                    )
                );
                return;
            }

            int copiedCount = 0;
            foreach (var row in selectedRows)
            {
                string sourceText = row.Values[sourceColumnIndex];
                if (!string.IsNullOrEmpty(sourceText))
                {
                    // Copy the text from source to target
                    var newValues = (string[])row.Values.Clone();
                    newValues[targetColumnIndex] = sourceText;
                    row.Values = newValues;
                    copiedCount++;
                }
            }

            // Mark as having unsaved changes
            _hasUnsavedChanges = true;

            _notificationManager.Show(
                new Notification(
                    "Text Copied",
                    $"Source text copied to target column for {copiedCount} row(s).",
                    NotificationType.Success
                )
            );
        }
        #endregion

        private static string GetOrCreateTempDirectory()
        {
            var exeDirectory = AppContext.BaseDirectory;
            // Create a unique instance ID so that if multiple instances are open, they don't overwrite eachother.
            var instanceId = Process.GetCurrentProcess().Id.ToString();
            var tempDirectoryName = $".temp-LocresStudio-{instanceId}";
            var tempDirectoryPath = Path.Combine(exeDirectory, tempDirectoryName);

            // Create folder if it does not exist
            if (!Directory.Exists(tempDirectoryPath))
            {
                Directory.CreateDirectory(tempDirectoryPath);

                // Set folder to hidden on Windows
                if (OperatingSystem.IsWindows())
                {
                    File.SetAttributes(
                        tempDirectoryPath,
                        FileAttributes.Directory | FileAttributes.Hidden
                    );
                }
            }

            return tempDirectoryPath;
        }

        // Unique file name so multiple instances don't overwrite eachother
        private string GetUniqueFileName(string baseName, string extension)
        {
            var instanceId = Process.GetCurrentProcess().Id.ToString();
            return $"{baseName}_{instanceId}{extension}";
        }

        private async void CloseMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            if (SelectedDocument == null)
                return;

            await CloseDocumentAsync(SelectedDocument);
        }
        private async void OpenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var storageProvider = StorageProvider;
            var result = await storageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    FileTypeFilter = new[]
                    {
                new FilePickerFileType("Localization Files")
                {
                    Patterns = new[] { "*.locres" },
                },
                    },
                    AllowMultiple = false,
                }
            );

            if (result != null && result.Count > 0)
            {
                string originalFilePath = result[0].Path.LocalPath;
                await OpenLocresFileAsync(originalFilePath);
            }
        }

        private void DataGrid_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (_rows == null || _rows.Count == 0 || e.Delta.Y >= 0)
                return;

            Dispatcher.UIThread.Post(EnsureLastRowVisibleAfterWheelScroll, DispatcherPriority.Background);
        }

        private void EnsureLastRowVisibleAfterWheelScroll()
        {
            if (_rows == null || _rows.Count == 0)
                return;

            var scrollViewer = _dataGrid
                .GetVisualDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault();

            if (scrollViewer == null)
                return;

            var remaining = scrollViewer.Extent.Height - (scrollViewer.Offset.Y + scrollViewer.Viewport.Height);
            if (remaining > 48.0)
                return;

            var lastRow = _rows[_rows.Count - 1];
            _dataGrid.ScrollIntoView(lastRow, null);

            // Avalonia's DataGrid wheel scrolling can stop one row early on Windows with DPI scaling.
            // Post a second pass after layout so the final row is forced into view near the bottom edge.
            Dispatcher.UIThread.Post(() => _dataGrid.ScrollIntoView(lastRow, null), DispatcherPriority.Loaded);
        }

        private async Task OpenLocresFileAsync(string originalFilePath)
        {
            if (string.IsNullOrWhiteSpace(originalFilePath))
                return;

            var existingDocument = _documents.FirstOrDefault(doc =>
                string.Equals(doc.OriginalPath, originalFilePath, StringComparison.OrdinalIgnoreCase)
            );
            if (existingDocument != null)
            {
                AddRecentFile(originalFilePath);
                SelectedDocument = existingDocument;
                _dataGrid.SelectedItem = existingDocument.Rows.FirstOrDefault();
                UpdateStatusBar();
                return;
            }

            if (!File.Exists(originalFilePath))
            {
                RemoveRecentFile(originalFilePath);
                _notificationManager?.Show(
                    new Notification(
                        "Missing File",
                        $"Could not find {Path.GetFileName(originalFilePath)}.",
                        NotificationType.Warning
                    )
                );
                return;
            }

            // Update Discord RPC
            _discordRPC.UpdatePresence(null);

            try
            {
                AddRecentFile(originalFilePath);
                var locresData = LocresFileData.Read(originalFilePath);
                var document = CreateDocumentFromLocres(originalFilePath, locresData);
                _documents.Add(document);
                _currentLocresFilePath = originalFilePath;
                SelectedDocument = document;
            }
            catch (Exception ex)
            {
                _notificationManager.Show(new Notification("Error Opening File", ex.Message, NotificationType.Error));
            }
        }

        private void AddRecentFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            _appConfig = AppConfig.Reload();
            _appConfig.RecentFiles ??= new List<string>();
            _appConfig.RecentFiles.RemoveAll(path =>
                string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase)
            );
            _appConfig.RecentFiles.Insert(0, filePath);

            if (_appConfig.RecentFiles.Count > MaxRecentFiles)
                _appConfig.RecentFiles = _appConfig.RecentFiles.Take(MaxRecentFiles).ToList();

            _appConfig.Save();
            RefreshRecentFilesMenu();
        }

        private void RemoveRecentFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            _appConfig = AppConfig.Reload();
            _appConfig.RecentFiles ??= new List<string>();
            _appConfig.RecentFiles.RemoveAll(path =>
                string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase)
            );
            _appConfig.Save();
            RefreshRecentFilesMenu();
        }

        private void RefreshRecentFilesMenu()
        {
            if (uiRecentFilesMenuItem == null)
            {
                Logger.Log("uiRecentFilesMenuItem is null in RefreshRecentFilesMenu");
                return;
            }

            _appConfig = AppConfig.Reload();
            Logger.Log($"Reloaded AppConfig. Raw RecentFiles count: {_appConfig.RecentFiles?.Count ?? 0}");
            var recentFiles = (_appConfig.RecentFiles ?? new List<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRecentFiles)
                .ToList();

            Logger.Log($"Filtered recentFiles list count: {recentFiles.Count}");

            if (recentFiles.Count != (_appConfig.RecentFiles?.Count ?? 0))
            {
                _appConfig.RecentFiles = recentFiles;
                _appConfig.Save();
                Logger.Log("AppConfig.RecentFiles trimmed and saved.");
            }

            var menuItems = BuildRecentFileMenuItems(recentFiles);
            Logger.Log($"Built {menuItems.Count} recent file menu items. Assigning to uiRecentFilesMenuItem.ItemsSource");
            uiRecentFilesMenuItem.ItemsSource = menuItems;
        }

        private List<object> BuildRecentFileMenuItems(List<string> recentFiles)
        {
            var items = new List<object>();

            if (recentFiles.Count == 0)
            {
                items.Add(new MenuItem
                {
                    Header = "(Empty)",
                    IsEnabled = false,
                });
            }
            else
            {
                foreach (var recentFile in recentFiles.Select((path, index) => new { path, index }))
                {
                    var fileName = Path.GetFileName(recentFile.path);
                    var exists = File.Exists(recentFile.path);
                    Logger.Log($"RecentFiles entry #{recentFile.index + 1}: '{recentFile.path}' exists={exists}");
                    var item = new MenuItem
                    {
                        Header = exists
                            ? $"{recentFile.index + 1}. {fileName}"
                            : $"{recentFile.index + 1}. {fileName} (missing)",
                        CommandParameter = recentFile.path,
                    };
                    ToolTip.SetTip(item, recentFile.path);
                    item.Click += RecentFileMenuItem_Click;
                    items.Add(item);
                }

                items.Add(new Separator());

                var removeItem = new MenuItem
                {
                    Header = "Remove from Recent Files",
                };

                foreach (var recentFile in recentFiles)
                {
                    var fileName = Path.GetFileName(recentFile);
                    var removeRecentItem = new MenuItem
                    {
                        Header = fileName,
                        Tag = recentFile,
                    };
                    ToolTip.SetTip(removeRecentItem, recentFile);
                    removeRecentItem.Click += RemoveRecentFileMenuItem_Click;
                    removeItem.Items.Add(removeRecentItem);
                }

                items.Add(removeItem);
                items.Add(CreateMenuItem("Clear Recent Files", ClearRecentFilesMenuItem_Click));
            }

            return items;
        }

        private MenuItem BuildImportExportMenu()
        {
            var importExportItem = new MenuItem
            {
                Header = "Import / Export Data",
            };

            importExportItem.ItemsSource = new List<object>
            {
                CreateMenuItem("Import CSV (Spreadsheet)", OpenSpreadsheetMenuItem_Click),
                CreateMenuItem("Import TXT (TSV)", ImportTxtMenuItem_Click),
                new Separator(),
                CreateMenuItem("Export to CSV", SaveAsMenuItem_Click),
                CreateMenuItem("Export to TXT (TSV)", ExportTxtMenuItem_Click),
            };

            return importExportItem;
        }

        private MenuItem CreateMenuHeader(string header) =>
            new()
            {
                Header = header,
                IsEnabled = false,
            };

        private MenuItem CreateMenuItem(string header, EventHandler<RoutedEventArgs> clickHandler, string? inputGesture = null)
        {
            var item = new MenuItem
            {
                Header = header,
            };

            if (!string.IsNullOrWhiteSpace(inputGesture))
            {
                item.InputGesture = KeyGesture.Parse(inputGesture);
            }

            item.Click += clickHandler;
            return item;
        }

        private async void RecentFileMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.CommandParameter is not string filePath)
                return;

            await OpenLocresFileAsync(filePath);
        }

        private void ClearRecentFilesMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            _appConfig = AppConfig.Reload();
            _appConfig.RecentFiles ??= new List<string>();
            _appConfig.RecentFiles.Clear();
            _appConfig.Save();
            RefreshRecentFilesMenu();
        }

        private void RemoveRecentFileMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.Tag is not string filePath)
                return;

            RemoveRecentFile(filePath);
        }

        private void PersistSessionState()
        {
            if (_restoringSession)
                return;

            _appConfig = AppConfig.Reload();
            _appConfig.RecentFiles ??= new List<string>();
            _appConfig.LastSessionFiles = _documents
                .Select(doc => doc.OriginalPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRecentFiles)
                .ToList();
            _appConfig.Save();
        }

        private async Task RestoreLastSessionAsync()
        {
            if (!_appConfig.RestoreLastSession || _documents.Count > 0)
                return;

            var sessionFiles = (_appConfig.LastSessionFiles ?? new List<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRecentFiles)
                .ToList();

            if (sessionFiles.Count == 0)
                return;

            _restoringSession = true;
            try
            {
                foreach (var filePath in sessionFiles)
                {
                    await OpenLocresFileAsync(filePath);
                }
            }
            finally
            {
                _restoringSession = false;
                PersistSessionState();
            }
        }

        private async Task CloseDocumentAsync(LocresDocument document)
        {
            if (document == null)
                return;

            if (document.HasUnsavedChanges)
            {
                var closeResult = await ShowCloseDocumentDialogAsync(document);
                if (closeResult == "Cancel")
                    return;

                if (closeResult == "Save")
                {
                    var originalDocument = SelectedDocument;
                    try
                    {
                        if (SelectedDocument != document)
                            SelectedDocument = document;

                        SaveEditedData(openExplorer: false);
                    }
                    finally
                    {
                        if (originalDocument != null && _documents.Contains(originalDocument))
                            SelectedDocument = originalDocument;
                    }
                }
            }

            _documents.Remove(document);
        }

        private async Task<string> ShowCloseDocumentDialogAsync(LocresDocument document)
        {
            var dialog = new Window
            {
                Title = "Unsaved Changes",
                Width = 420,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 20,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{document.DisplayName} has unsaved changes. Save before closing this tab?",
                            TextWrapping = TextWrapping.Wrap,
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 10,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Children =
                            {
                                new Button { Content = "Save" },
                                new Button { Content = "Don't Save" },
                                new Button { Content = "Cancel" },
                            },
                        },
                    },
                },
            };

            var tcs = new TaskCompletionSource<string>();
            var rootPanel = dialog.Content as StackPanel;
            var buttonPanel = rootPanel?.Children.Count > 1 ? rootPanel.Children[1] as StackPanel : null;
            var buttons = buttonPanel?.Children.OfType<Button>().ToList();

            if (buttons == null || buttons.Count < 3)
                return "Cancel";

            buttons[0].Click += (_, _) => { tcs.TrySetResult("Save"); dialog.Close(); };
            buttons[1].Click += (_, _) => { tcs.TrySetResult("Don't Save"); dialog.Close(); };
            buttons[2].Click += (_, _) => { tcs.TrySetResult("Cancel"); dialog.Close(); };
            dialog.Closed += (_, _) => tcs.TrySetResult("Cancel");

            await dialog.ShowDialog(this);
            return await tcs.Task;
        }

        // Change signature to accept locresPath
        private void LoadCsv(string csvFilePath, string locresPath, string originalUserPath = null)
        {
            try
            {
                // 1. PREPARE DATA
                var tempRows = new ObservableCollection<DataRow>();
                var tempHeaders = new List<string>();

                var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    BadDataFound = null,
                    MissingFieldFound = null,
                    HeaderValidated = null,
                };

                using (var reader = new StreamReader(csvFilePath))
                using (var csv = new CsvReader(reader, csvConfig))
                {
                    bool isFirstRow = true;
                    while (csv.Read())
                    {
                        string[] stringValues = new string[csv.Parser.Count];
                        for (int i = 0; i < csv.Parser.Count; i++) stringValues[i] = csv.GetField(i);

                        if (isFirstRow)
                        {
                            for (int i = 0; i < stringValues.Length; i++) tempHeaders.Add(stringValues[i]);
                            isFirstRow = false;
                        }
                        else
                        {
                            if (stringValues.Length < tempHeaders.Count)
                            {
                                Array.Resize(ref stringValues, tempHeaders.Count);
                                for (int i = 0; i < stringValues.Length; i++)
                                {
                                    stringValues[i] ??= string.Empty;
                                }
                            }

                            var key = stringValues[0];
                            var isNew = _newKeySet != null && _newKeySet.Contains(key);
                            tempRows.Add(new DataRow { Values = stringValues, IsNewKey = isNew });
                        }
                    }
                }

                // 2. FIND OR CREATE DOCUMENT
                // We identify documents by their WORKING path (the temp file)
                var doc = _documents.FirstOrDefault(d =>
                        string.Equals(d.WorkingPath, locresPath, StringComparison.OrdinalIgnoreCase));

                if (doc == null && !string.IsNullOrWhiteSpace(originalUserPath))
                {
                    doc = _documents.FirstOrDefault(d =>
                        string.Equals(d.OriginalPath, originalUserPath, StringComparison.OrdinalIgnoreCase));
                }

                if (doc == null)
                {

                    string pathForDisplay = !string.IsNullOrEmpty(originalUserPath) ? originalUserPath : locresPath;

                    doc = new LocresDocument(pathForDisplay);

                    doc.WorkingPath = locresPath;

                    _documents.Add(doc);
                }
                else
                {
                    doc.WorkingPath = locresPath;
                }

                if (doc.BaselineKeys.Count == 0 && tempHeaders.Count > 0)
                {
                    doc.BaselineKeys = new HashSet<string>(
                        tempRows
                            .Select(row => row.Values.Length > 0 ? row.Values[0] : string.Empty)
                            .Where(key => !string.IsNullOrWhiteSpace(key)),
                        StringComparer.Ordinal
                    );
                }

                // 3. UPDATE DOCUMENT DATA
                doc.ActiveCsvPath = csvFilePath;

                doc.Rows.Clear();
                foreach (var row in tempRows) doc.Rows.Add(row);

                doc.ColumnHeaders.Clear();
                foreach (var header in tempHeaders) doc.ColumnHeaders.Add(header);

                doc.HasUnsavedChanges = _hasUnsavedChanges;

                // 4. UPDATE UI
                if (SelectedDocument == doc)
                {
                    ApplySelectedDocumentState();
                    UpdateStatusBar();
                }
                else
                {
                    SelectedDocument = doc;
                }
            }
            catch (Exception ex)
            {
                _notificationManager.Show(new Notification("Error Loading CSV", ex.Message, NotificationType.Error));
            }
        }

        private async void ExportTxtMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_rows == null || _rows.Count == 0)
            {
                _notificationManager.Show(new Notification("No Data", "There is no data to export.", NotificationType.Warning));
                return;
            }

            var saveOptions = new FilePickerSaveOptions
            {
                Title = "Export to Text (TSV)",
                SuggestedFileName = Path.ChangeExtension(SelectedDocument.DisplayName, ".txt"),
                FileTypeChoices = new[]
        {
            new FilePickerFileType("Text (Tab Separated)") { Patterns = new[] { "*.txt", "*.tsv" } },
        },
            };

            var storageFile = await StorageProvider.SaveFilePickerAsync(saveOptions);

            if (storageFile != null)
            {
                var filePath = storageFile.Path.LocalPath;

                try
                {
                    // NEW CONFIGURATION: Cleaner Output
                    var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        Delimiter = "\t",
                        // This function decides when to add quotes.
                        // We tell it: ONLY quote if the text contains a Tab, Newline, or Quote.
                        // We do NOT quote just for spaces.
                        ShouldQuote = args =>
                        {
                            if (string.IsNullOrEmpty(args.Field)) return false;

                            return args.Field.Contains("\t")
                                || args.Field.Contains("\n")
                                || args.Field.Contains("\r")
                                || args.Field.Contains("\"");
                        }
                    };

                    using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
                    using (var csv = new CsvWriter(writer, config))
                    {
                        // Write Headers
                        for (int i = 0; i < _dataGrid.Columns.Count; i++)
                        {
                            csv.WriteField(((DataGridTextColumn)_dataGrid.Columns[i]).Header);
                        }
                        csv.NextRecord();

                        // Write Rows
                        foreach (DataRow row in _rows)
                        {
                            for (int i = 0; i < row.Values.Length; i++)
                            {
                                csv.WriteField(row.Values[i]);
                            }
                            csv.NextRecord();
                        }
                    }

                    _notificationManager.Show(new Notification("Success", $"Exported to {Path.GetFileName(filePath)}", NotificationType.Success));
                }
                catch (Exception ex)
                {
                    _notificationManager.Show(new Notification("Export Error", ex.Message, NotificationType.Error));
                }
            }
        }

        private async void ImportTxtMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentLocresFilePath))
            {
                _notificationManager.Show(new Notification("Error", "Please open a .locres file first.", NotificationType.Warning));
                return;
            }

            var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Text (Supports TSV or Old App Format)",
                FileTypeFilter = new[] { new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt", "*.tsv" } } },
                AllowMultiple = false
            });

            if (result != null && result.Count > 0)
            {
                var filePath = result[0].Path.LocalPath;
                try
                {
                    var lines = await File.ReadAllLinesAsync(filePath);

                    // CHECK: Is this the "Old App" format?
                    bool isOldFormat = lines.Length > 0 && lines[0].Contains("[~NAMES-INCLUDED~]");

                    var importedData = new Dictionary<string, string>();

                    if (isOldFormat)
                    {
                        // --- STRATEGY A: PARSE OLD FORMAT (Key=Value) ---
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//") || line.StartsWith("[~")) continue;

                            // Split only on the FIRST '=' found
                            var parts = line.Split(new[] { '=' }, 2);
                            if (parts.Length == 2)
                            {
                                var key = parts[0].Trim();
                                var val = parts[1].Trim();

                                // Old app often wraps values in quotes like "Hello World", we should remove them
                                if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
                                {
                                    val = val.Substring(1, val.Length - 2);
                                }

                                // Handle escaped newlines from old format
                                val = val.Replace("\\n", "\n").Replace("\\r", "");

                                importedData[key] = val;
                                // Add slash version too, just in case
                                if (!key.StartsWith("/")) importedData["/" + key] = val;
                            }
                        }
                    }
                    else
                    {
                        // --- STRATEGY B: PARSE STANDARD TSV (Key \t Source \t Target) ---
                        // We use manual splitting instead of CsvHelper here to be robust against bad quoting
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            var parts = line.Split('\t');

                            // Assume 3 columns: Key, Source, Target
                            // Or 2 columns: Key, Target
                            if (parts.Length >= 2)
                            {
                                var key = parts[0].Trim();
                                // If 3 parts, Target is usually index 2. If 2 parts, Target is index 1.
                                var val = parts.Length >= 3 ? parts[2] : parts[1];

                                // Clean quotes if TSV has them
                                val = val.Trim();
                                if (val.StartsWith("\"") && val.EndsWith("\"")) val = val.Substring(1, val.Length - 2);

                                // Handle standard CSV double-quote escaping ("" -> ")
                                val = val.Replace("\"\"", "\"");

                                importedData[key] = val;
                            }
                        }
                    }

                    // --- APPLY TO GRID ---
                    int keyIndex = -1;
                    int targetIndex = -1;

                    for (int i = 0; i < _dataGrid.Columns.Count; i++)
                    {
                        var header = ((DataGridTextColumn)_dataGrid.Columns[i]).Header?.ToString().ToLower();
                        if (header == "key") keyIndex = i;
                        else if (header == "target") targetIndex = i;
                    }

                    if (keyIndex == -1 || targetIndex == -1) return;

                    int matchCount = 0;
                    foreach (var row in _rows)
                    {
                        string currentKey = row.Values[keyIndex];

                        // Try exact match, then try adding/removing slash
                        string foundValue = null;
                        if (importedData.TryGetValue(currentKey, out var v1)) foundValue = v1;
                        else if (currentKey.StartsWith("/") && importedData.TryGetValue(currentKey.Substring(1), out var v2)) foundValue = v2;

                        if (foundValue != null)
                        {
                            var oldValues = row.Values;
                            // Only update if different
                            if (oldValues[targetIndex] != foundValue)
                            {
                                oldValues[targetIndex] = foundValue;
                                matchCount++;
                            }
                        }
                    }

                    // Force update
                    _dataGrid.InvalidateVisual();
                    if (matchCount > 0)
                    {
                        if (SelectedDocument != null)
                        {
                            MarkDocumentDirty(SelectedDocument);
                        }
                        _notificationManager.Show(new Notification("Import Successful", $"Updated {matchCount} rows from {(isOldFormat ? "Old Format" : "TSV")}.", NotificationType.Success));
                    }
                    else
                    {
                        _notificationManager.Show(new Notification("Import Result", "No matching keys found to update.", NotificationType.Warning));
                    }
                }
                catch (Exception ex)
                {
                    _notificationManager.Show(new Notification("Import Error", ex.Message, NotificationType.Error));
                }
            }
        }
        private void SaveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLocresFilePath == null)
            {
                _notificationManager.Show(
                    new Notification(
                        "No File Open",
                        "Please open a locres file first.",
                        NotificationType.Error
                    )
                );
                return;
            }
            if (_rows != null && _rows.Count > 0)
            {
                SaveEditedData(AppConfig.Instance.OpenSaveFolderAfterSaving);
            }
            else
            {
                _notificationManager.Show(
                    new Notification(
                        "No Data",
                        "There's no data to export.",
                        NotificationType.Information
                    )
                );
            }
        }

        public void SaveEditedData(bool openExplorer = false)
        {
            if (string.IsNullOrEmpty(_currentLocresFilePath))
            {
                _notificationManager.Show(
                    new Notification(
                        "Error",
                        "No file is currently open to save.",
                        NotificationType.Error
                    )
                );
                return;
            }

            if (SelectedDocument?.LocresData == null)
            {
                _notificationManager.Show(
                    new Notification(
                        "Error",
                        "The current document is not loaded as a locres file.",
                        NotificationType.Error
                    )
                );
                return;
            }

            var destinationFile = SelectedDocument.OriginalPath;
            var locresData = BuildLocresDataFromRows(SelectedDocument);

            try
            {
                _suppressNextDirtyMark = true;
                _isSavingDocument = true;
                locresData.Write(destinationFile);
                SelectedDocument.LocresData = locresData;

                if (openExplorer)
                {
                    var destinationDirectory = Path.GetDirectoryName(destinationFile);
                    if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    {
                        OpenDirectoryInExplorer(destinationDirectory);
                    }
                }

                _notificationManager.Show(
                    new Notification(
                        "Success!",
                        $"Saved {Path.GetFileName(destinationFile)}.",
                        NotificationType.Success
                    )
                );

                ClearDocumentDirty(SelectedDocument);
            }
            catch (Exception ex)
            {
                _notificationManager.Show(
                    new Notification(
                        "Error saving file:",
                        ex.Message,
                        NotificationType.Error
                    )
                );
            }
            finally
            {
                _isSavingDocument = false;
                Dispatcher.UIThread.Post(() => _suppressNextDirtyMark = false, DispatcherPriority.Background);
            }
        }

        private int GetColumnIndexByHeader(string headerName)
        {
            for (int i = 0; i < _dataGrid.Columns.Count; i++)
            {
                var header = (_dataGrid.Columns[i] as DataGridTextColumn)?.Header?.ToString();
                if (string.Equals(header, headerName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private LocresFileData BuildLocresDataFromRows(LocresDocument document)
        {
            var keyColumnIndex = GetColumnIndexByHeader("key");
            var sourceColumnIndex = GetColumnIndexByHeader("source");
            var targetColumnIndex = GetColumnIndexByHeader("target");

            if (keyColumnIndex < 0 || sourceColumnIndex < 0 || targetColumnIndex < 0)
            {
                throw new InvalidOperationException("Missing key/source/target columns.");
            }

            var result = new LocresFileData
            {
                Version = document.LocresData?.Version ?? LocresVersion.CityHash,
            };

            var namespaceMap = new Dictionary<string, LocresNamespaceData>(StringComparer.Ordinal);
            foreach (var row in document.Rows)
            {
                var displayKey = row.Values.Length > keyColumnIndex ? row.Values[keyColumnIndex]?.Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(displayKey))
                    continue;

                var (namespaceName, key) = LocresFileData.ParseDisplayKey(displayKey);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (!namespaceMap.TryGetValue(namespaceName, out var namespaceData))
                {
                    namespaceData = new LocresNamespaceData { Name = namespaceName };
                    namespaceMap[namespaceName] = namespaceData;
                    result.Namespaces.Add(namespaceData);
                }

                var source = row.Values.Length > sourceColumnIndex ? row.Values[sourceColumnIndex] ?? string.Empty : string.Empty;
                var target = row.Values.Length > targetColumnIndex ? row.Values[targetColumnIndex] ?? string.Empty : string.Empty;
                var translation = string.IsNullOrWhiteSpace(target) ? source : target;

                namespaceData.Entries.Add(
                    new LocresEntryData
                    {
                        NamespaceName = namespaceName,
                        Key = key,
                        Translation = translation,
                        SourceHash = row.SourceHash != 0 ? row.SourceHash : LocresCrc32.StrCrc32(source ?? string.Empty),
                    }
                );
            }

            return result;
        }

        private async void OpenSpreadsheetMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Safety check: We can only import a CSV if we already have a Locres file open
            if (string.IsNullOrEmpty(_currentLocresFilePath))
            {
                _notificationManager.Show(new Notification("Error", "Please open a .locres file first before importing a spreadsheet.", NotificationType.Warning));
                return;
            }

            var storageProvider = StorageProvider;
            var result = await storageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    FileTypeFilter = new[]
                    {
                new FilePickerFileType("Spreadsheet Files")
                {
                    Patterns = new[] { "*.csv" },
                },
                    },
                    AllowMultiple = false,
                }
            );

            if (result != null && result.Count > 0)
            {
                string filePath = result[0].Path.LocalPath;

                try
                {
                    ApplyCsvToCurrentDocument(filePath);
                    csvFile = filePath;
                    _discordRPC.UpdatePresence(null);
                }
                catch (Exception ex)
                {
                    _notificationManager.Show(new Notification("Import Error", ex.Message, NotificationType.Error));
                }
            }
        }

        private async void SaveAsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_rows != null && _rows.Count > 0)
            {
                var saveOptions = new FilePickerSaveOptions
                {
                    // OLD: SuggestedFileName = Path.GetFileNameWithoutExtension(_currentLocresFilePath),
                    // NEW:
                    SuggestedFileName = Path.ChangeExtension(SelectedDocument.DisplayName, ".csv"),
                    FileTypeChoices = new[]
                    {
                new FilePickerFileType("CSV file") { Patterns = new[] { "*.csv" } },
            },
                };

                var storageFile = await StorageProvider.SaveFilePickerAsync(saveOptions);

                if (storageFile != null)
                {
                    var filePath = storageFile.Path.LocalPath;
                    SaveAsCsv(filePath);

                    _notificationManager.Show(
                        new Notification(
                            "Success",
                            $"File saved as {Path.GetFileName(filePath)}",
                            NotificationType.Success
                        )
                    );
                }
            }
            else
            {
                _notificationManager.Show(
                    new Notification(
                        "No Data",
                        "There's no data to export.",
                        NotificationType.Information
                    )
                );
            }
        }

        private void SaveAsCsv(string filePath)
        {
            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            using (
                var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture))
            )
            {
                // Headers
                for (int i = 0; i < _dataGrid.Columns.Count; i++)
                {
                    csv.WriteField(((DataGridTextColumn)_dataGrid.Columns[i]).Header);
                }
                csv.NextRecord();

                // Rows
                foreach (DataRow row in _rows)
                {
                    for (int i = 0; i < row.Values.Length; i++)
                    {
                        csv.WriteField(row.Values[i]);
                    }
                    csv.NextRecord();
                }
            }
        }

        #region Merge Operation

        /***
         * Merging Files:
         *
         * usage: UnrealLocres.exe merge target_locres_path source_locres_path [-o output_path]
         *
         * positional arguments:
         * target_locres_path      Merge target locres file path, the file you want to translate
         * source_locres_path      Merge source locres file path, the file that has additional lines
         *
         * optional arguments:
         * -o                      Output locres file path (default: {target_locres_path}.new)
         *
         * Merge two locres files into one, adding strings that are present in source but not in target file.
         ***/

        private HashSet<string> _newKeySet = new();

        private async void MergeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await Task.CompletedTask;
            _notificationManager.Show(
                new Notification(
                    "Merge Unavailable",
                    "Merge is temporarily disabled while locres operations are moved to the native in-app implementation.",
                    NotificationType.Information
                )
            );
        }

        private async Task HighlightNewKeysAndOpen(string targetLocresPath, string mergedLocresPath)
        {
            try
            {
                // Export both files to CSV
                var tempDir = GetOrCreateTempDirectory();

                // Get list of existing CSV files before export
                var existingCsvFiles = Directory.GetFiles(tempDir, "*.csv").ToHashSet();

                // Export target file
                var exportTarget = new Process
                {
                    StartInfo = ProcessUtils.GetProcessStartInfo(
                        command: "export",
                        locresFilePath: targetLocresPath,
                        useWine: this.UseWine,
                        csvFilePath: "target.csv"
                    ),
                };

                exportTarget.StartInfo.WorkingDirectory = tempDir;
                exportTarget.StartInfo.RedirectStandardError = true;

                var targetOutputBuilder = new StringBuilder();
                var targetErrorBuilder = new StringBuilder();

                exportTarget.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                        targetOutputBuilder.AppendLine(args.Data);
                };
                exportTarget.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                        targetErrorBuilder.AppendLine(args.Data);
                };

                exportTarget.Start();
                exportTarget.BeginOutputReadLine();
                exportTarget.BeginErrorReadLine();
                await Task.Run(() => exportTarget.WaitForExit());

                // Check if target export succeeded
                if (exportTarget.ExitCode != 0)
                {
                    var errorMessage =
                        $"Failed to export target file.\n"
                        + $"Exit code: {exportTarget.ExitCode}\n"
                        + $"Output: {targetOutputBuilder}\n"
                        + $"Error: {targetErrorBuilder}";

                    _notificationManager.Show(
                        new Notification("Export Error", errorMessage, NotificationType.Error)
                    );
                    return;
                }

                // Find the new CSV file created for target
                var csvFilesAfterTarget = Directory.GetFiles(tempDir, "*.csv").ToHashSet();
                var targetCsvFiles = csvFilesAfterTarget.Except(existingCsvFiles).ToList();

                if (targetCsvFiles.Count == 0)
                {
                    _notificationManager.Show(
                        new Notification(
                            "Export Error",
                            "No CSV file was created for target locres file",
                            NotificationType.Error
                        )
                    );
                    return;
                }

                var targetCsv = targetCsvFiles.First();

                // Update existing files list
                existingCsvFiles = csvFilesAfterTarget;

                // Export merged file
                var exportMerged = new Process
                {
                    StartInfo = ProcessUtils.GetProcessStartInfo(
                        command: "export",
                        locresFilePath: mergedLocresPath,
                        useWine: this.UseWine,
                        csvFilePath: "merged.csv"
                    ),
                };

                exportMerged.StartInfo.WorkingDirectory = tempDir;
                exportMerged.StartInfo.RedirectStandardError = true;

                var mergedOutputBuilder = new StringBuilder();
                var mergedErrorBuilder = new StringBuilder();

                exportMerged.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                        mergedOutputBuilder.AppendLine(args.Data);
                };
                exportMerged.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                        mergedErrorBuilder.AppendLine(args.Data);
                };

                exportMerged.Start();
                exportMerged.BeginOutputReadLine();
                exportMerged.BeginErrorReadLine();
                await Task.Run(() => exportMerged.WaitForExit());

                // Check if merged export succeeded
                if (exportMerged.ExitCode != 0)
                {
                    var errorMessage =
                        $"Failed to export merged file.\n"
                        + $"Exit code: {exportMerged.ExitCode}\n"
                        + $"Output: {mergedOutputBuilder}\n"
                        + $"Error: {mergedErrorBuilder}";

                    _notificationManager.Show(
                        new Notification("Export Error", errorMessage, NotificationType.Error)
                    );
                    return;
                }

                // Find the new CSV file created for merged
                var csvFilesAfterMerged = Directory.GetFiles(tempDir, "*.csv").ToHashSet();
                var mergedCsvFiles = csvFilesAfterMerged.Except(existingCsvFiles).ToList();

                if (mergedCsvFiles.Count == 0)
                {
                    _notificationManager.Show(
                        new Notification(
                            "Export Error",
                            "No CSV file was created for merged locres file",
                            NotificationType.Error
                        )
                    );
                    return;
                }

                var mergedCsv = mergedCsvFiles.First();

                // Read keys from target CSV (assume first column is the key)
                var targetKeys = new HashSet<string>();
                try
                {
                    using (var reader = new StreamReader(targetCsv))
                    using (
                        var csv = new CsvHelper.CsvReader(
                            reader,
                            new CsvHelper.Configuration.CsvConfiguration(
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                        )
                    )
                    {
                        if (csv.Read() && csv.ReadHeader()) // skip header if it exists
                        {
                            while (csv.Read())
                            {
                                var key = csv.GetField(0);
                                if (!string.IsNullOrEmpty(key))
                                {
                                    targetKeys.Add(key);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _notificationManager.Show(
                        new Notification(
                            "CSV Parse Error",
                            $"Failed to parse target CSV ({Path.GetFileName(targetCsv)}): {ex.Message}",
                            NotificationType.Error
                        )
                    );
                    return;
                }

                // Read keys from merged CSV
                var mergedKeys = new List<string>();
                try
                {
                    using (var reader = new StreamReader(mergedCsv))
                    using (
                        var csv = new CsvHelper.CsvReader(
                            reader,
                            new CsvHelper.Configuration.CsvConfiguration(
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                        )
                    )
                    {
                        if (csv.Read() && csv.ReadHeader()) // skip header if it exists
                        {
                            while (csv.Read())
                            {
                                var key = csv.GetField(0);
                                if (!string.IsNullOrEmpty(key))
                                {
                                    mergedKeys.Add(key);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _notificationManager.Show(
                        new Notification(
                            "CSV Parse Error",
                            $"Failed to parse merged CSV ({Path.GetFileName(mergedCsv)}): {ex.Message}",
                            NotificationType.Error
                        )
                    );
                    return;
                }

                // Find new keys (keys that are in merged but not in target)
                _newKeySet = new HashSet<string>(mergedKeys.Where(k => !targetKeys.Contains(k)));

                // Show info about new keys found
                if (_newKeySet.Count > 0)
                {
                    _notificationManager.Show(
                        new Notification(
                            "New Keys Found",
                            $"Found {_newKeySet.Count} new keys.",
                            NotificationType.Success
                        )
                    );
                }
                else
                {
                    _notificationManager.Show(
                        new Notification(
                            "No New Keys",
                            "No new keys found in the merge operation.",
                            NotificationType.Information
                        )
                    );
                }

                // Open merged file in editor
                _currentLocresFilePath = mergedLocresPath;
                LoadCsv(mergedCsv, mergedLocresPath, null);


                // Clean up temp CSV files
                try
                {
                    if (File.Exists(targetCsv))
                        File.Delete(targetCsv);
                    if (File.Exists(mergedCsv))
                        File.Delete(mergedCsv);
                }
                catch (Exception ex)
                {
                    // Non-critical error, just log it
                    System.Diagnostics.Debug.WriteLine(
                        $"Failed to clean up temp files: {ex.Message}"
                    );
                }
            }
            catch (Exception ex)
            {
                _notificationManager.Show(
                    new Notification(
                        "Highlight Error",
                        $"Error during highlight operation: {ex.Message}",
                        NotificationType.Error
                    )
                );
            }
        }

        // Close

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #endregion

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            // DataGrid is critical
            _dataGrid = this.FindControl<DataGrid>("uiDataGrid");
            if (_dataGrid != null)
            {
                _dataGrid.AddHandler(KeyDownEvent, DataGrid_PreviewKeyDown, RoutingStrategies.Tunnel);
            }

            // Safety check for Linux Menu
            var linuxMenuItem = this.FindControl<MenuItem>("uiLinuxHeader");
            if (linuxMenuItem != null)
            {
                linuxMenuItem.IsVisible = PlatformUtils.IsLinux();
            }

            // Safety check for Preferences
            var preferencesMenuItem = this.FindControl<MenuItem>("uiPreferencesMenuItem");
            if (preferencesMenuItem != null)
            {
                preferencesMenuItem.Click += PreferencesMenuItem_Click;
            }

            // Safety check for Wine Prefix
            var winePrefixMenuItem = this.FindControl<MenuItem>("uiWinePrefix");
            if (winePrefixMenuItem != null)
            {
                winePrefixMenuItem.IsVisible = PlatformUtils.IsLinux();
            }
        }

        // Find dialog
        private FindDialog findDialog;

        private void FindMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (findDialog == null)
            {
                findDialog = new FindDialog();
                findDialog.Closed += FindDialog_Closed;
                findDialog.MainWindow = this;
            }

            findDialog.Show(this);
            findDialog.Activate();
        }

        private void FindDialog_Closed(object sender, EventArgs e)
        {
            findDialog = null;
        }

        // Find and replace dialog
        private FindReplaceDialog findReplaceDialog;

        private void FindReplaceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (findReplaceDialog == null)
            {
                findReplaceDialog = new FindReplaceDialog();
                findReplaceDialog.Closed += FindReplaceDialog_Closed;
                findReplaceDialog.MainWindow = this;
            }

            findReplaceDialog.Show(this);
            findReplaceDialog.Activate();
        }

        private void FindReplaceDialog_Closed(object sender, EventArgs e)
        {
            findReplaceDialog = null;
        }

        // Google Translate Left Click Integration
        private void GoogleTranslateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Check if a row is selected
            if (_dataGrid.SelectedItem is DataRow row)
            {
                // 1. Get the Source Text (Assuming Column 1 is Source)
                // We use null check in case the array is short
                string sourceText = row.Values.Length > 1 ? row.Values[1] : "";

                if (!string.IsNullOrWhiteSpace(sourceText))
                {
                    try
                    {
                        // 2. Encode text (handle spaces, symbols)
                        string encoded = System.Net.WebUtility.UrlEncode(sourceText);

                        // 3. Construct URL
                        // sl=auto (Detect Source), tl=auto (Translate to your OS language)
                        string url = $"https://translate.google.com/?sl=auto&tl=auto&text={encoded}&op=translate";

                        // 4. Open Browser
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Browser launch failed: {ex.Message}");
                    }
                }
            }
        }

        // Preferences
        private PreferencesWindow preferencesWindow;

        private void PreferencesMenuItem_Click(Object sender, RoutedEventArgs e)
        {
            if (preferencesWindow == null)
            {
                preferencesWindow = new PreferencesWindow(this);
                preferencesWindow.Closed += PreferencesWindow_Closed;
            }

            preferencesWindow.Show(this);
            preferencesWindow.Activate();
        }

        private void PreferencesWindow_Closed(object sender, EventArgs e)
        {
            // 1. Apply font settings (Local to MainWindow)
            ApplyEditorSettings();

            // 2. Ensure Theme is correct (Global via App.axaml.cs)
            // We cast Application.Current to your "App" class to access SetTheme
            if (Application.Current is App app)
            {
                // We use the singleton Instance so we don't have to reload from disk
                app.SetTheme(AppConfig.Instance.ThemeKey);
            }

            preferencesWindow = null;
        }

        // Attempt wine prefix (Linux)
        private void WinePrefix_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WineUtils.InitializeWinePrefix();
                _notificationManager.Show(
                    new Notification(
                        "Success",
                        "Success. Make sure to install Wine MONO and set to 32 bit.",
                        NotificationType.Success
                    )
                );
            }
            catch (Exception ex)
            {
                _notificationManager.Show(
                    new Notification(
                        "Error",
                        $"Failed to initialize Wine prefix: {ex.Message}",
                        NotificationType.Error
                    )
                );
            }
        }

        // Report issue
        private const string GitHubIssueUrl =
            "https://github.com/AcTePuKc/LocresStudio/issues/new";

        private void ReportIssueMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = GitHubIssueUrl,
                    UseShellExecute = true,
                };
                Process.Start(psi);
            }
            catch (Exception) { }
        }

        // About
        private AboutWindow aboutWindow;

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (aboutWindow == null)
            {
                aboutWindow = new AboutWindow();
                aboutWindow.Initialize(_notificationManager, this);
                aboutWindow.Closed += AboutWindow_Closed;
            }

            aboutWindow.Show(this);
            aboutWindow.Activate();
        }

        private void AboutWindow_Closed(Object sender, EventArgs e)
        {
            aboutWindow = null;
        }

        private async Task<bool> MergeSaveChanges()
        {
            if (!_hasUnsavedChanges)
                return true;

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
                        new TextBlock
                        {
                            Text =
                                "You have unsaved changes. Would you like to save before merging?",
                            TextWrapping = TextWrapping.Wrap,
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 10,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Children =
                            {
                                new Avalonia.Controls.Button { Content = "Save" },
                                new Avalonia.Controls.Button { Content = "Don't Save" },
                                new Avalonia.Controls.Button { Content = "Cancel" },
                            },
                        },
                    },
                },
            };

            var tcs = new TaskCompletionSource<string>();
            var rootPanel = dialog.Content as StackPanel;
            var buttonPanel = rootPanel?.Children.Count > 1 ? rootPanel.Children[1] as StackPanel : null;
            var buttons = buttonPanel?.Children.OfType<Avalonia.Controls.Button>().ToList();

            if (buttons == null || buttons.Count < 3)
                return false;

            buttons[0].Click += (s, e) =>
            {
                try
                {
                    SaveEditedData();
                    tcs.SetResult("Save");
                    dialog.Close();
                }
                catch (Exception ex)
                {
                    _notificationManager.Show(
                        new Notification(
                            "Save Error",
                            $"Failed to save: {ex.Message}",
                            NotificationType.Error
                        )
                    );
                    tcs.SetResult("Cancel");
                    dialog.Close();
                }
            };
            buttons[1].Click += (s, e) =>
            {
                tcs.SetResult("Don't Save");
                dialog.Close();
            };
            buttons[2].Click += (s, e) =>
            {
                tcs.SetResult("Cancel");
                dialog.Close();
            };

            dialog.Closed += (_, _) => tcs.TrySetResult("Cancel");

            await dialog.ShowDialog(this);
            var result = await tcs.Task;
            return result == "Save" || result == "Don't Save";
        }

        private void OpenDirectoryInExplorer(string directoryPath)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(
                        new ProcessStartInfo("explorer.exe", $"\"{directoryPath}\"")
                        {
                            UseShellExecute = true,
                        }
                    );
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start(
                        new ProcessStartInfo("xdg-open", $"\"{directoryPath}\"")
                        {
                            UseShellExecute = true,
                        }
                    );
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start(
                        new ProcessStartInfo("open", $"\"{directoryPath}\"")
                        {
                            UseShellExecute = true,
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                _notificationManager.Show(
                    new Notification(
                        "Error",
                        $"Failed to open directory: {ex.Message}",
                        NotificationType.Error
                    )
                );
            }
        }

        private LocresDocument CreateDocumentFromLocres(string originalFilePath, LocresFileData locresData)
        {
            var document = new LocresDocument(originalFilePath)
            {
                WorkingPath = originalFilePath,
                LocresData = locresData,
                HasUnsavedChanges = false,
            };

            document.ColumnHeaders.Clear();
            document.ColumnHeaders.Add("key");
            document.ColumnHeaders.Add("source");
            document.ColumnHeaders.Add("target");

            document.BaselineKeys = new HashSet<string>(
                locresData
                    .EnumerateEntries()
                    .Select(entry => LocresFileData.ComposeDisplayKey(entry.NamespaceName, entry.Key)),
                StringComparer.Ordinal
            );

            foreach (var entry in locresData.EnumerateEntries())
            {
                document.Rows.Add(
                    new DataRow
                    {
                        Values = new[]
                        {
                            LocresFileData.ComposeDisplayKey(entry.NamespaceName, entry.Key),
                            entry.Translation,
                            string.Empty,
                        },
                        SourceHash = entry.SourceHash,
                    }
                );
            }

            return document;
        }

        private void ApplyCsvToCurrentDocument(string csvFilePath)
        {
            if (SelectedDocument == null)
            {
                throw new InvalidOperationException("Please open a locres file first.");
            }

            var importedRows = ReadCsvRows(csvFilePath);
            var keyIndex = GetColumnIndexByHeader("key");
            var sourceIndex = GetColumnIndexByHeader("source");
            var targetIndex = GetColumnIndexByHeader("target");

            if (keyIndex < 0 || sourceIndex < 0 || targetIndex < 0)
            {
                throw new InvalidOperationException("The current document is missing key/source/target columns.");
            }

            var existingRows = new Dictionary<string, DataRow>(StringComparer.Ordinal);
            var duplicateExistingKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in SelectedDocument.Rows)
            {
                var rawKey = row.Values.Length > keyIndex ? row.Values[keyIndex] : string.Empty;
                var normalizedKey = NormalizeDisplayKey(rawKey);
                if (string.IsNullOrWhiteSpace(normalizedKey))
                    continue;

                if (!existingRows.TryAdd(normalizedKey, row))
                {
                    duplicateExistingKeys.Add(normalizedKey);
                }
            }

            if (duplicateExistingKeys.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The current document contains duplicate key(s): {string.Join(", ", duplicateExistingKeys.Take(5))}"
                );
            }

            var replacementRows = new List<DataRow>();
            var changedCount = 0;
            var addedCount = 0;
            var importedSeenKeys = new HashSet<string>(StringComparer.Ordinal);
            var duplicateImportedKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var importedRow in importedRows)
            {
                var importedKey = importedRow.Values.Length > 0 ? importedRow.Values[0] : string.Empty;
                var normalizedImportedKey = NormalizeDisplayKey(importedKey);
                if (string.IsNullOrWhiteSpace(normalizedImportedKey))
                    continue;

                if (!importedSeenKeys.Add(normalizedImportedKey))
                {
                    duplicateImportedKeys.Add(normalizedImportedKey);
                    continue;
                }

                var importedSource = importedRow.Values.Length > 1 ? importedRow.Values[1] ?? string.Empty : string.Empty;
                var importedTarget = importedRow.Values.Length > 2 ? importedRow.Values[2] ?? string.Empty : string.Empty;

                if (existingRows.TryGetValue(normalizedImportedKey, out var existingRow))
                {
                    var sourceToUse = string.IsNullOrWhiteSpace(importedSource)
                        && existingRow.Values.Length > sourceIndex
                        ? existingRow.Values[sourceIndex] ?? string.Empty
                        : importedSource;

                    var replacementRow = new DataRow
                    {
                        Values = new[] { normalizedImportedKey, sourceToUse, importedTarget },
                        SourceHash = existingRow.SourceHash != 0
                            ? existingRow.SourceHash
                            : LocresCrc32.StrCrc32(sourceToUse),
                    };

                    replacementRows.Add(replacementRow);

                    var sourceChanged = existingRow.Values.Length <= sourceIndex
                        || existingRow.Values[sourceIndex] != sourceToUse;
                    var targetChanged = existingRow.Values.Length <= targetIndex
                        || existingRow.Values[targetIndex] != importedTarget;
                    if (sourceChanged || targetChanged)
                    {
                        changedCount++;
                    }

                    continue;
                }

                replacementRows.Add(
                    new DataRow
                    {
                        Values = new[] { normalizedImportedKey, importedSource, importedTarget },
                        SourceHash = LocresCrc32.StrCrc32(importedSource),
                    }
                );
                addedCount++;
            }

            if (duplicateImportedKeys.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The imported CSV contains duplicate key(s): {string.Join(", ", duplicateImportedKeys.Take(5))}"
                );
            }

            var removedCount = SelectedDocument.Rows.Count - replacementRows.Count + addedCount;
            SelectedDocument.Rows.Clear();
            foreach (var row in replacementRows)
            {
                SelectedDocument.Rows.Add(row);
            }

            _rows = SelectedDocument.Rows;
            _dataGrid.ItemsSource = _rows;
            _dataGrid.InvalidateMeasure();
            _dataGrid.InvalidateArrange();
            _dataGrid.InvalidateVisual();

            if (changedCount > 0 || addedCount > 0 || removedCount > 0)
            {
                MarkDocumentDirty(SelectedDocument);
            }

            UpdateStatusBar();
            _notificationManager.Show(
                new Notification(
                    "CSV Imported",
                    $"Updated {changedCount} row(s), added {addedCount}, removed {Math.Max(removedCount, 0)}.",
                    NotificationType.Success
                )
            );
        }

        private List<DataRow> ReadCsvRows(string csvFilePath)
        {
            var rows = new List<DataRow>();
            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                BadDataFound = null,
                MissingFieldFound = null,
                HeaderValidated = null,
            };

            using var reader = new StreamReader(csvFilePath);
            using var csv = new CsvReader(reader, csvConfig);

            var headers = Array.Empty<string>();
            var isFirstRow = true;
            while (csv.Read())
            {
                var values = new string[csv.Parser.Count];
                for (int i = 0; i < csv.Parser.Count; i++)
                {
                    values[i] = csv.GetField(i);
                }

                if (isFirstRow)
                {
                    headers = values;
                    isFirstRow = false;
                    continue;
                }

                if (values.Length < headers.Length)
                {
                    Array.Resize(ref values, headers.Length);
                    for (var i = 0; i < values.Length; i++)
                    {
                        values[i] ??= string.Empty;
                    }
                }

                rows.Add(new DataRow { Values = values });
            }

            return rows;
        }

        private static string NormalizeDisplayKey(string key)
        {
            var trimmed = key?.Trim() ?? string.Empty;
            if (trimmed.StartsWith("/") && trimmed.IndexOf('/', 1) < 0)
            {
                return trimmed.Substring(1);
            }

            return trimmed;
        }

        private async void AddEmptyRowMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            if (SelectedDocument == null || _rows == null || _dataGrid.Columns.Count == 0)
                return;

            var newEntry = await ShowAddEntryDialogAsync();
            if (newEntry == null)
                return;

            var columnCount = _dataGrid.Columns.Count;
            var values = Enumerable.Repeat(string.Empty, columnCount).ToArray();

            var keyColumnIndex = -1;
            var sourceColumnIndex = -1;
            var targetColumnIndex = -1;
            for (int i = 0; i < _dataGrid.Columns.Count; i++)
            {
                var header = (_dataGrid.Columns[i] as DataGridTextColumn)?.Header?.ToString();
                if (string.Equals(header, "key", StringComparison.OrdinalIgnoreCase))
                {
                    keyColumnIndex = i;
                }
                else if (string.Equals(header, "source", StringComparison.OrdinalIgnoreCase))
                {
                    sourceColumnIndex = i;
                }
                else if (string.Equals(header, "target", StringComparison.OrdinalIgnoreCase))
                {
                    targetColumnIndex = i;
                }
            }

            if (keyColumnIndex >= 0)
            {
                values[keyColumnIndex] = newEntry.Key;
            }

            if (sourceColumnIndex >= 0)
            {
                values[sourceColumnIndex] = newEntry.Source;
            }

            if (targetColumnIndex >= 0)
            {
                values[targetColumnIndex] = newEntry.Target;
            }

            var newRow = new DataRow { Values = values };
            _rows.Add(newRow);
            MarkDocumentDirty(SelectedDocument);
            _dataGrid.SelectedItem = newRow;
            _dataGrid.ScrollIntoView(newRow, null);
            UpdateStatusBar();
        }

        private async Task<NewEntryInput> ShowAddEntryDialogAsync()
        {
            var keyTextBox = new TextBox { Watermark = "Key", MinWidth = 320 };
            var sourceTextBox = new TextBox { Watermark = "Source text", MinWidth = 320 };
            var targetTextBox = new TextBox { Watermark = "Target text (optional)", MinWidth = 320 };

            var dialog = new Window
            {
                Title = "Add New Entry",
                Width = 560,
                Height = 360,
                MinWidth = 520,
                MinHeight = 340,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Margin = new Thickness(20),
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "Enter the new key and source text. Use /Key for the root namespace or Namespace/Key for namespaced entries.",
                                TextWrapping = TextWrapping.Wrap,
                            },
                            new TextBlock { Text = "Key" },
                            keyTextBox,
                            new TextBlock { Text = "Source" },
                            sourceTextBox,
                            new TextBlock { Text = "Target" },
                            targetTextBox,
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 10,
                                HorizontalAlignment = HorizontalAlignment.Right,
                                Children =
                                {
                                    new Button { Content = "Add", MinWidth = 100 },
                                    new Button { Content = "Cancel", MinWidth = 100 },
                                },
                            },
                        },
                    },
                },
            };

            var tcs = new TaskCompletionSource<NewEntryInput?>();
            var scrollViewer = dialog.Content as ScrollViewer;
            var rootPanel = scrollViewer?.Content as StackPanel;
            var buttonPanel = rootPanel?.Children.Count > 7 ? rootPanel.Children[7] as StackPanel : null;
            var buttons = buttonPanel?.Children.OfType<Button>().ToList();

            if (buttons == null || buttons.Count < 2)
                return null;

            buttons[0].Click += (_, _) =>
            {
                var key = keyTextBox.Text?.Trim() ?? string.Empty;
                var source = sourceTextBox.Text?.Trim() ?? string.Empty;
                var target = targetTextBox.Text ?? string.Empty;

                if (string.IsNullOrWhiteSpace(key))
                {
                    _notificationManager.Show(new Notification("Missing Key", "Key is required.", NotificationType.Warning));
                    return;
                }

                if (string.IsNullOrWhiteSpace(source))
                {
                    _notificationManager.Show(new Notification("Missing Source", "Source is required for a new entry.", NotificationType.Warning));
                    return;
                }

                tcs.TrySetResult(new NewEntryInput(key, source, target));
                dialog.Close();
            };

            buttons[1].Click += (_, _) =>
            {
                tcs.TrySetResult(null);
                dialog.Close();
            };

            dialog.Closed += (_, _) => tcs.TrySetResult(null);

            await dialog.ShowDialog(this);
            return await tcs.Task;
        }

        private void DeleteSelectedRowsMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            DeleteSelectedRows();
        }

        private void DeleteSelectedRows()
        {
            if (SelectedDocument == null || _rows == null || _rows.Count == 0)
                return;

            var selectedRows = _dataGrid.SelectedItems?.Cast<DataRow>().Distinct().ToList()
                ?? new List<DataRow>();

            if (selectedRows.Count == 0 && _dataGrid.SelectedItem is DataRow selectedRow)
            {
                selectedRows.Add(selectedRow);
            }

            if (selectedRows.Count == 0)
                return;

            foreach (var row in selectedRows)
            {
                _rows.Remove(row);
            }

            MarkDocumentDirty(SelectedDocument);
            _dataGrid.InvalidateMeasure();
            _dataGrid.InvalidateArrange();
            _dataGrid.InvalidateVisual();
            UpdateStatusBar();

            _notificationManager.Show(
                new Notification(
                    "Rows Deleted",
                    $"Deleted {selectedRows.Count} row(s).",
                    NotificationType.Information
                )
            );
        }

        private sealed record NewEntryInput(string Key, string Source, string Target);
    }
}
