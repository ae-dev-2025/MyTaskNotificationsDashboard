# docs/

Demo assets for showing the app without running it.

- **`tour.html`** — a screen-by-screen product tour with a Light/Dark toggle
  that swaps every screenshot at once. Open it straight from disk; it
  references `screenshots/` by relative path.
- **`screenshots/`** — the ten captures the tour uses: five screens × both
  themes, 1424×745, taken from the real Windows app.
- **`build-tour.py`** — regenerates `tour.html` from the screenshots. Pass
  `--embed` to produce `tour.embed.html`, a single self-contained file
  (~740 KB, git-ignored) for mailing or uploading somewhere that can't host
  the folder.

## Recapturing the screenshots

The captures come from the running app over CDP, seeded with demo data —
an in-progress task, a queue across all four priorities, completions earlier
the same afternoon, and recurring Sleep/Lunch blocks — so that the calendar
shows history, the now-line, and the forward plan in a single viewport.

1. **Back up your data file** (the demo seed replaces it):
   `%LOCALAPPDATA%\User Name\com.aedev2025.taskdashboard\Data\tasks.json`.
2. Seed demo data into that file. Keep completions in the early afternoon
   and the in-progress task "now", so one calendar frame tells the story.
3. Launch with remote debugging:

   ```powershell
   $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS='--remote-debugging-port=9333'
   Start-Process TaskDashboard\bin\Debug\net10.0-windows10.0.19041.0\win-x64\TaskDashboard.exe
   ```

4. For each theme (`taskDashboard.applyTheme('light'|'dark')` via
   `Runtime.evaluate`), visit each nav page and `Page.captureScreenshot`.
   On the calendar, scroll to `12 * 48px` first so the afternoon is framed.
   The edit-modal shot is the Tasks page with Edit clicked on a queued task.
5. Name the files `<n>-<page>-<theme>.png` to match `SCREENS` in
   `build-tour.py`, drop them in `screenshots/`, and run the script.
6. Restore your backed-up `tasks.json` **after closing the app** — a live
   instance writes the demo data back over your restore on exit.

Capturing over CDP rather than a window grab keeps the frames pixel-identical
in size and free of window chrome. Note the WebSocket handshake needs
origin suppression (`suppress_origin=True` in Python's websocket-client, or
launch with `--remote-allow-origins=*`).
