# Changelog

## 1.2.0

- Added a seven-size application icon embedded in the EXE, window, taskbar, and tray.
- Added a light theme, instant theme switching, and persistence of the selected theme.
- Added confirmed quota resets through Codex's earned-reset API.
- Persisted idempotency keys before mutation; uncertain results can be retried across restarts without a new logical redemption.
- Added account/directory guards, concurrent-action prevention, and mocked reset regression checks.
- Preserved scroll position during quota refreshes.

## 1.1.0

- Rebuilt the interface with a dark navy palette, cyan quota bars, violet cache metrics, and monospace numbers.
- Separated quota percentages from progress bars and added chart guides and alternating table rows.
- Added an MIT license, source build documentation, privacy notes, and Windows CI.
- Retained live Codex quota reads, local usage summaries, model filters, CSV export, tray support, and automatic refresh.

## 1.0.0

- Initial native Windows application with live quota reads and local usage log parsing.
- Added seven parser regression checks covering deduplication, mixed log formats, partial lines, counter resets, cache updates, and missing directories.
