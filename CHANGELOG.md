# Changelog

## Unreleased

### Added

- Native `.locres` reader/writer inside the app.
- `Recent Files` tracking and session restore support.
- Add-entry workflow from the editor UI.
- Remove selected rows from the grid with `Ctrl+Delete` and from the `Edit` menu.
- Optional debug logging through launch args, env var, or config.
- Tests for native `.locres` serialization.

### Changed

- `Save / Export locres` now saves the currently opened `.locres` directly instead of relying on the old external import flow.
- CSV import now behaves like a document replacement snapshot instead of only patching matching rows.
- Exit flow now prompts correctly for unsaved changes across multiple open documents.
- `Ctrl+S` and menu save now clear the unsaved marker consistently.
- Build output is consolidated back to `artifacts/win-test` for local Windows verification.

### Fixed

- Newly added keys now persist in saved `.locres` files.
- Root namespace key display no longer prepends a stray `/`.
- Duplicate recent-file entries are collapsed case-insensitively.
- `Ctrl+W` closes the active tab again.

### Known limitations

- `Merge locres` is still disabled.
- Large-file bottom scrolling in Avalonia `DataGrid` can still be inconsistent on Windows with DPI scaling.
