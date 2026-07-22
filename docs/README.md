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

**CI does this on every pull request.** The `demo` job in
`.github/workflows/release.yml` runs the freshly built app, recaptures all
ten screenshots, rebuilds the tour, and commits the result back to the PR
branch — so `docs/` can never drift from the code under review. The
single-file `tour.embed.html` is attached to each run as the
`TaskDashboard-demo-tour` artifact.

To run the same thing locally (after building the Windows TFM):

```powershell
.\docs\capture.ps1          # add -Embed for the single-file variant
```

The script backs up your real `tasks.json`, seeds clock-relative demo data —
an in-progress task anchoring the now-line, completions in the hours behind
it, a queue across all four priorities planning out ahead, recurring
Sleep/Lunch blocks — drives the app over CDP via `tools/UiTest`'s `capture`
mode, rebuilds `tour.html`, and restores your data after the app has exited
(restoring earlier would be undone by the app's shutdown write).

Everything is relative to *now*, per the fixture rule in `CLAUDE.md`, so a
capture at any hour of day keeps history, the now-line and the forward plan
inside the calendar frame.
