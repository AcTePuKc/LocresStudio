# LocresStudio

![LocresStudio logo](wiki/assets/img/LocresStudio.png)

Tabbed desktop editor for Unreal Engine `.locres` files.

LocresStudio is a GUI fork of [UnrealLocresEditor](https://github.com/snoozeds/UnrealLocresEditor) focused on practical translation workflows: open native `.locres` files directly, work in multiple tabs, import/export spreadsheet-friendly formats, and save back to `.locres` without depending on an external converter executable.

## Features

- Native `.locres` open and save inside the app.
- Multi-tab editing with unsaved change tracking.
- `Recent Files` and restore-last-session support.
- CSV import as a full document replacement workflow.
- CSV and TXT/TSV export.
- Add new entries from the UI.
- Remove selected rows with `Ctrl+Delete` or `Edit -> Remove Selected Row(s)`.
- Save prompts when closing a tab or exiting with unsaved changes.
- Configurable theme, font, font size, RTL layout, auto-save, and update behavior.
- Optional Discord Rich Presence.
- Optional debug logging when troubleshooting.

## Platforms

Release builds are produced for:

- Windows x64
- Windows x86
- Linux x64

## Installation

1. Open the repository [Releases](../../releases).
2. Download the archive for your platform.
3. Extract it anywhere.
4. Run `LocresStudio`.

Notes:

- Windows releases are self-contained.
- Linux releases are also published from CI. No separate `UnrealLocres.exe` setup is required for normal open/save usage.

## Workflow Notes

### Recent Files

Opened `.locres` files are stored in the app config and shown in the `File` menu.

### Saving

- `Ctrl+S` saves the current `.locres`.
- `File -> Save / Export locres` does the same from the menu.
- CSV export is separate and does not replace native `.locres` saving.

### CSV Import

CSV import is treated as a replacement snapshot for the current document:

- rows present in the CSV are kept or updated
- new rows are added
- rows missing from the CSV are removed from the current document

This makes it practical to round-trip through spreadsheets and save back to a smaller `.locres` when needed.

### Row Editing

- Add entries with the add-entry action from the grid context menu.
- Delete rows with `Ctrl+Delete` or `Edit -> Remove Selected Row(s)`.

## Debug Logging

Debug logging is opt-in. Normal launches do not create `app.log`.

Enable it with any of these:

- launch arg: `LocresStudio.exe --debug-log`
- launch arg: `LocresStudio.exe -debug-log`
- env var: `LOCRESSTUDIO_DEBUG_LOG=1`
- env var: `LOCRESSTUDIO_DEBUG_LOG=true`
- config field: `"EnableDebugLogging": true`

Log file locations:

- Windows: `%APPDATA%\\UnrealLocresEditor\\app.log`
- Linux: `~/.config/UnrealLocresEditor/app.log`

Crash logging remains separate from normal debug logging.

## Configuration

The config file is stored here:

- Windows: `%APPDATA%\\UnrealLocresEditor\\config.json`
- Linux: `~/.config/UnrealLocresEditor/config.json`

Useful fields include:

- `RecentFiles`
- `LastSessionFiles`
- `RestoreLastSession`
- `OpenSaveFolderAfterSaving`
- `EnableDebugLogging`
- `EditorFontFamily`
- `EditorFontSize`
- `EnableRTL`

## Known Issues

- The Avalonia `DataGrid` can still behave oddly at the very bottom of very large files on Windows with DPI scaling. Keyboard navigation is more reliable than mouse-wheel scrolling in that edge case.
- `Merge locres` is currently disabled while the old external-tool path is being retired.

## Credits

- Original project: [snoozeds/UnrealLocresEditor](https://github.com/snoozeds/UnrealLocresEditor)
- Maintained fork: [AcTePuKc](https://github.com/AcTePuKc)
- UI framework: [AvaloniaUI](https://avaloniaui.net/)
