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
`dashboard`, `tracking`, `realism`, `theme`, `themeverify`, `capture`.

The `*verify` modes must run **after an app restart** — they assert the state
the preceding mode left behind actually persisted. A full regression is every
mode, with a kill/relaunch before each verify.

`capture` is not a test — it drives the demo screenshots under `docs/` and is
normally invoked through `docs/capture.ps1` (see *Demo assets* below). Unlike
the test modes it **fails hard on any step**, so a partial set of screenshots
can never overwrite a complete one.

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
- Calendar blocks have a **20px minimum render height** (`MinBlockPx` in
  `CalendarPage.razor`), so pixel-overlap assertions must ignore sub-clamp
  fragments or they report phantom overlaps. That constant is load-bearing:
  the planner-overlap assertion in `tools/UiTest` discounts blocks small
  enough to have been clamp-inflated, so **move its threshold in step** if
  the floor ever changes again.

## Architecture

`Services/` holds the logic worth protecting. The decision-making parts are
pure and deterministic; only storage and notification *delivery* touch the
outside world:

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
  `Changed` after every save; `Dashboard.razor`, `CalendarPage.razor` and
  `NotificationCoordinator` subscribe so nothing shows a stale plan.
  `LoadAsync`/`SaveAsync` are serialized by a `SemaphoreSlim`, and each save
  writes a **uniquely named** temp file. Both are scars: NavMenu and the
  active page both call `LoadAsync` at startup, the loaded flag was only set
  after an await, so both ran the migration path and raced a fixed temp-file
  name — the losing `File.Move` threw and the Dashboard came up empty. The
  loaded re-check lives *inside* the lock, and the gate is **not reentrant**,
  so the private save path must never be called while holding it.
- **`NotificationScheduling.cs`** — pure core that turns the task list + lead
  time + now into the reminders to fire (`DueSoon` at deadline-minus-lead,
  `Overdue` at the deadline). Ids are **FNV-1a over a stable key**, not
  `string.GetHashCode`, which is randomized per process and so would not line
  up across launches; a re-sync overwrites rather than duplicates.
- **`NotificationCoordinator.cs`** — the only impure notification piece:
  requests permission and syncs at startup, re-syncs on every `Changed`, and
  refreshes the always-live current-task notification on a **one-minute
  timer** because "now" advances even when nothing is edited.

Notification delivery is per-platform behind `INotificationService`:
`Platforms/Android/AndroidNotificationService.cs` (+ `TaskAlarmReceiver`) uses
real `AlarmManager` alarms with `SetAndAllowWhileIdle` — deliberately not
exact alarms, so no exact-alarm permission is needed — and a
`SharedPreferences` key-diff cancels orphaned ones. Windows uses
`AppNotificationManager` toasts on an in-process timer; **unpackaged Windows
cannot schedule toasts for a closed app**, so Windows reminders only fire
while the app runs and the ongoing current-task notification is Android-only.
Both features are gated by their own persisted toggles in `DashboardData`.

Storage is a versioned envelope (`DashboardData`, v2). v1 was a bare task
array; the loader migrates and rewrites on launch. New optional properties
need no migration — absent fields deserialize as null/default. Serialization
is **source-generated** (`TodoJsonContext`) because Android release builds
trim and AOT-compile; never switch to reflection-based `JsonSerializer`.

UI is Blazor components in `Components/`, with `TaskFields`/`TaskModal`
shared between add and edit so the two can't drift.

`CalendarPage.razor` splits concurrent entries into **side-by-side lanes**
(`AssignLanes`), computed per overlap cluster so two independent pairs each
get their own share of the column; blocked-time shading stays full-width
behind at `z-index:0`. Lane geometry is emitted as an inline `LaneStyle`, not
CSS, because the widths depend on the cluster's lane count. Lanes are
assigned from the **post-clamp** heights on purpose, so what the assignment
sees is what the user sees.

## Styling

All colors come from CSS variables defined in `wwwroot/app.css` — light on
`:root`, dark under `[data-bs-theme=dark]`. **Add a token rather than a hex
value** in component CSS. Bootstrap 5.3.3 is bundled, so `data-bs-theme`
themes its controls natively; `taskDashboard.applyTheme` in `index.html`
resolves `system` and follows OS changes.

There is **no stock template chrome left** — a design audit found the sidebar
still wearing the Blazor project's navy-to-purple gradient with white nav
glyphs baked into their data URIs, which meant dark mode stopped at the edge
of the content area. The sidebar is now a tokenised surface, nav icons are
**CSS masks tinted by `currentColor`** so they track the nav text through
hover and active, and the active item is an accent fill with an accent rule
down its left edge. Don't reintroduce a hex value here; the fix was to route
links, primary buttons, focus rings, validation text and the Blazor error
banner through tokens. Primary buttons need **`--on-accent`** for their label
— dark mode lightens `--accent` far enough that white text falls to ~2.8:1.

Shared visual language lives in `app.css`, not in components: the
`.priority-*` palette and the page-title `h1` rule are global, because CSS
isolation had scoped two identical copies of the priority palette separately
and would have let them drift silently. `--now-line` is **violet, not pink**,
so the current-time marker cannot be mistaken for an urgent-task accent.

Meaning must never be carried by colour alone — this ships on Android, where
`title` tooltips do not exist. A calendar slot that cannot finish before its
deadline renders a visible *late* tag on the block's first line, not just a
red outline.

Scoped CSS gotcha that has bitten twice: **`.razor.css` rules do not apply to
elements rendered by child components** (`InputText`, `EditForm`, …) — they
lack the scope attribute. Use `::deep`, or put the class on a plain wrapper
element you render yourself.

`design-system/` holds self-contained previews of tokens and components in
both themes, published to a Claude Design project. They can't `<link>`
app.css, so they carry a copy — **regenerate it with
`design-system/sync-tokens.ps1` after changing tokens**, never by hand. The
previews used to inline per-theme hex and faithfully reproduced the same
hardcoded primary-button blue the audit flagged in the app, so the docs
blessed the bug instead of catching it.

## Demo assets

`docs/` holds a product tour — ten screenshots of the real Windows app (five
screens × both themes) plus a `tour.html` that swaps themes across every
frame at once. Recreate it **locally before opening a PR**, after building
the Windows TFM:

```powershell
.\docs\capture.ps1          # add -Embed for the single-file variant
```

The script backs up your real `tasks.json`, seeds clock-relative demo data,
launches the app with CDP enabled, drives `tools/UiTest`'s `capture` mode,
rebuilds the tour, and restores your data **only after the app has exited** —
restoring earlier is silently undone by the app's shutdown write, which is
the trap that motivated scripting this at all.

This is local-only by decision, and **the CI route is a dead end** — four
attempts established it. A hosted runner can build and even launch the app
(the publish is self-contained for exactly this reason), but the runner is
**elevated**, and the WebView2 loader documents dropping
`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` for elevated processes; the debug
port stayed closed even after forwarding the arguments through
`CoreWebView2EnvironmentOptions`, the supported API path. Don't re-excavate.
What survived the attempts is worth keeping: `MainPage.xaml.cs` forwards the
CDP arguments through that API rather than trusting loader behaviour, and
`capture.ps1` diagnoses which layer died instead of reporting a bare timeout.

## Workflow conventions

Branch → implement → build both TFMs → run the affected test modes → full
regression → update `README.md` → recreate the demo if the UI moved
(`docs/capture.ps1`) → commit → push → PR → **the user merges**. Never merge
without being asked.

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
validation. The Windows publish is **self-contained** (`SelfContained` +
`WindowsAppSDKSelfContained`) so the release notes' "unzip and run" claim is
true on a machine without the .NET or Windows App SDK runtimes — it costs
zip size, and that trade was made deliberately. The Android signing keystore
lives in repo secrets, never in git.

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
- `POST_NOTIFICATIONS` is declared in the manifest **and** requested at
  runtime; alarms are posted by a `BroadcastReceiver` so reminders still fire
  with the app closed.

## Out of scope by decision

Planner auto-splitting, calibration feeding the planner, re-merging split
parts, iOS/Mac Catalyst (TFMs commented out — needs a Mac build host), and
demo capture in CI (see *Demo assets*). Local notifications shipped; the
remaining roadmap item is **backend + sync**, then iOS once a Mac host
exists.
