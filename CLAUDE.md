# Task Dashboard — working notes

A .NET MAUI Blazor Hybrid "Life Dashboard" (Windows + Android) that answers
*what am I meant to be doing, what will I do, what did I do*. See `README.md`
for the user-facing feature list; this file is the working context.

## Build and run

```bash
# Windows (also the target used for UI testing)
dotnet build TaskDashboard/TaskDashboard.csproj -f net10.0-windows10.0.19041.0
dotnet build TaskDashboard/TaskDashboard.csproj -f net10.0-windows10.0.19041.0 -t:Run

# Android emulator (x86_64)
dotnet build TaskDashboard/TaskDashboard.csproj -f net10.0-android -t:Run

# Android physical device (arm64) — the RID flag is required when switching
# from an emulator build, otherwise install fails with IncompatibleCpuAbi
dotnet build TaskDashboard/TaskDashboard.csproj -f net10.0-android -t:Run -p:RuntimeIdentifiers=android-arm64
```

Always **close the running app before rebuilding Windows** — a live instance
locks `TaskDashboard.exe` and the build fails with MSB3027.

## Testing — read this before changing UI

Both suites drive the **real running native app** over the Chrome DevTools
Protocol. There are no unit tests; these are the safety net.

### Windows — `tools/UiTest` (Playwright over CDP)

```powershell
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS='--remote-debugging-port=9333'
Start-Process TaskDashboard\bin\Debug\net10.0-windows10.0.19041.0\win-x64\TaskDashboard.exe
dotnet run --project tools/UiTest -- <mode> <screenshotDir>
```

Modes: `suite`, `verify`, `calendar`, `blocked`, `blockedverify`, `migrated`,
`dashboard`, `tracking`, `realism`, `theme`, `themeverify`.

The `*verify` modes must run **after an app restart** — they assert the state
the preceding mode left behind actually persisted. A full regression is every
mode, with a kill/relaunch before each verify.

### Android — `tools/AndroidTest` (raw CDP, no Playwright)

Android WebView exposes only page-level CDP, which Playwright refuses to
attach to, so this is a hand-rolled `ClientWebSocket` client driving the DOM
through `Runtime.evaluate`.

```powershell
adb forward tcp:9333 localabstract:webview_devtools_remote_$(adb shell pidof com.aedev2025.taskdashboard)
dotnet run --project tools/AndroidTest -- <suite|verify|probe> <screenshotDir>
```

Only one process can hold port 9333 — stop the Windows app first.

### Rules that keep the suites working

- **Never rename a test-visible CSS class** (`.task-item`, `.cal-slot`,
  `.tile-accuracy`, `.panel-now`, `.btn-start`, …). Restyle freely; renaming
  breaks selectors.
- **Fixtures must be clock-relative.** A hardcoded 23:00 blocked window
  silently swallowed test tasks when the suite ran at 23:10. Use
  `DateTime.Now.AddHours(n)`.
- Calendar blocks have a **14px minimum render height**, so pixel-overlap
  assertions must ignore sub-clamp fragments or they report phantom overlaps.

## Architecture

`Services/` holds the logic worth protecting — all pure and deterministic
except the storage layer:

- **`Planner.cs`** — the scheduling brain. Orders unstarted tasks by deadline
  → priority → shorter estimate → `CreatedAt` (stable tie-break), places them
  greedily from *now*, skipping blocked ranges, honoring `NotBefore`, leaving
  the configured break after each. An **in-progress task anchors the front of
  the plan** for its remaining time, deliberately ignoring blocked ranges
  because the work is already happening. Never splits tasks — splitting is a
  user action. `BlockedTime.Expand` turns `BlockedPeriod`s into `TimeRange`s.
- **`Estimation.cs`** — median of actual÷estimate over completed tasks with
  real spans; outliers filtered. **Display-only by product decision** — it
  must not feed the planner.
- **`DashboardService.cs`** — singleton owning tasks + blocked periods +
  settings. Single JSON file, **atomic write** (temp + swap). Raises
  `Changed` after every save; `Dashboard.razor` and `CalendarPage.razor`
  subscribe so no page shows a stale plan.

Storage is a versioned envelope (`DashboardData`, v2). v1 was a bare task
array; the loader migrates and rewrites on launch. New optional properties
need no migration — absent fields deserialize as null/default. Serialization
is **source-generated** (`TodoJsonContext`) because Android release builds
trim and AOT-compile; never switch to reflection-based `JsonSerializer`.

UI is Blazor components in `Components/`, with `TaskFields`/`TaskModal`
shared between add and edit so the two can't drift.

## Styling

All colors come from CSS variables defined in `wwwroot/app.css` — light on
`:root`, dark under `[data-bs-theme=dark]`. **Add a token rather than a hex
value** in component CSS. Bootstrap 5.3.3 is bundled, so `data-bs-theme`
themes its controls natively; `taskDashboard.applyTheme` in `index.html`
resolves `system` and follows OS changes.

Scoped CSS gotcha that has bitten twice: **`.razor.css` rules do not apply to
elements rendered by child components** (`InputText`, `EditForm`, …) — they
lack the scope attribute. Use `::deep`, or put the class on a plain wrapper
element you render yourself.

`design-system/` holds self-contained previews of tokens and components in
both themes, published to a Claude Design project.

## Workflow conventions

Branch → implement → build both TFMs → run the affected test modes → full
regression → update `README.md` → commit → push → PR → **the user merges**.
Never merge without being asked.

Commit messages: prose explaining *why*, wrapped at ~76 chars, ending with

```
Written with the assistance of Claude Code (Anthropic), an AI coding tool.
The code was reviewed and is authored by Adilet Eshimkanov, who remains
responsible for its contents.

Assisted-by: Claude Code (Anthropic) <https://claude.com/claude-code>
```

`Assisted-by:` rather than `Co-Authored-By:` is deliberate — the latter
asserts authorship that can't be legally true and conflicts with DCO/CLA
requirements.

Every push to `main` publishes a GitHub Release with a Windows zip and a
signed APK (`.github/workflows/release.yml`); PRs run the same build as
validation. The Android signing keystore lives in repo secrets, never in git.

## Environment quirks (Windows / PowerShell 5.1)

- Piping values into `gh secret set` **corrupts them** — use `--body`.
  Similarly `adb exec-out screencap > file.png` corrupts the PNG; use
  `adb shell screencap -p /sdcard/x.png` then `adb pull`.
- Double quotes inside a `git commit -m @'...'@` here-string split the
  argument. Avoid them in commit messages.
- Long `dotnet run` test invocations can exceed the tool timeout — redirect
  to a file and poll, or run in the background.

## Android notes

- Requires a **current Android System WebView**. The android-31 emulator image
  ships Chromium 91, which cannot parse .NET 10's `blazor.webview.js` — Blazor
  silently never boots and the page sits at "Loading…". Use android-35+.
  Symptom to recognize: `#app` still contains `Loading...`, no error UI.
- Samsung's WebView never answers `Page.captureScreenshot`; `AndroidTest`
  skips screenshots rather than failing. Use `adb shell screencap` instead.
- API 35 enforces edge-to-edge; `SafeAreaEdges="All"` on `MainPage.xaml` keeps
  the nav bar out from under the status bar. Don't remove it.
- A locked screen stalls CDP calls, so the driver has timeouts and reconnects.

## Out of scope by decision

Planner auto-splitting, calibration feeding the planner, re-merging split
parts, iOS/Mac Catalyst (TFMs commented out — needs a Mac build host), and
backend sync. Next roadmap item: **local notifications**.
