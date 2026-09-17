# Changelog

## 1.5.3

- Applied the slim scrollbar style to the account history table's inner scroll area, replacing the default system scrollbar.

## 1.5.2

- Replaced the account history range buttons with a dropdown matching the model selector styling.
- Kept the current range selected across refreshes and re-renders.

## 1.5.1

- Removed the USD balance conversion card from quota cards and its balance formatting helpers.
- Kept quota percentages, reset times, weekly USD estimates and account history untouched.
- Replaced balance regression checks with a log credits normalization check.

## 1.5.0

- Split the dashboard into Account and Local usage modules with a segmented tab switcher under the title bar.
- Focused the account tab on live quota cards, USD balance, quota analysis, account history and reset actions.
- Moved the scheduled-request summary to the bottom of the account tab; the local tab keeps metrics, chart, records and CSV export.
- Refreshed tab styling in both light and dark themes and added module-switch regression checks.

## 1.4.0

- Added configurable daily lightweight Codex requests with presets for 05:00 and 05:00/10:00/15:00.
- Added per-user Windows scheduled tasks, optional sleep wake-up, and next-run/last-result display.
- Persisted intent before sending and synchronized desktop/background workers to avoid duplicate requests across restarts.
- Skipped missed slots after a two-minute grace period; failed or uncertain requests are not automatically retried.
- Isolated scheduled requests in ephemeral read-only Codex sessions, using ChatGPT login instead of API-key environment variables.
- Added schedule validation, persistence, duplicate/restart protection, Task Scheduler XML and settings-layout checks.

## 1.3.0

- Added four quota analysis cards, estimated weekly USD value, daily account Credits and history totals.
- Added 7/30-day account history selection and a 30-day local usage filter, chart and CSV range.
- Added read-only ChatGPT analytics requests using the existing local credential, with explicit unavailable states and no credential logging or persistence.
- Guarded estimates against missing/duplicate data, zero percentages, expired cycles and mismatched account scope.

## 1.2.1

- Replaced the native settings menu with rounded, theme-aware rows and right-aligned checkmarks.
- Added a compact scrollbar with a wider hit area and hover/drag feedback.
- Restyled the model selector and its popup to match both light and dark themes.
- Positioned the settings popup above and right-aligned with its button.

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
