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

## Recreating the demo

**Recreate the demo locally on every PR** (after building the Windows TFM):

```powershell
.\docs\capture.ps1          # add -Embed for the single-file variant
```

Commit the refreshed `screenshots/` and `tour.html` with the PR, the same
way the UI test suites run locally before a PR rather than in CI.

This is deliberate. A CI job was tried and reverted: hosted runners can
build and even launch the app (self-contained publish), but they run
**elevated**, and the WebView2 debug port never opens there — the loader
drops `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` for elevated processes, and
even forwarding the arguments through `CoreWebView2EnvironmentOptions`
left the port closed. Capture follows the same rule as the tests: CI
builds, a local machine drives the real app.

The script backs up your real `tasks.json`, seeds clock-relative demo data —
an in-progress task anchoring the now-line, completions in the hours behind
it, a queue across all four priorities planning out ahead, recurring
Sleep/Lunch blocks — drives the app over CDP via `tools/UiTest`'s `capture`
mode, rebuilds `tour.html`, and restores your data after the app has exited
(restoring earlier would be undone by the app's shutdown write).

Everything is relative to *now*, per the fixture rule in `CLAUDE.md`, so a
capture at any hour of day keeps history, the now-line and the forward plan
inside the calendar frame.
