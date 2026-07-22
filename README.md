# My Task & Notifications Dashboard

A personal dashboard for tracking tasks and surfacing notifications about them,
built with **.NET MAUI Blazor Hybrid** (.NET 10), targeting **Windows** and
**Android**.

> **Status: early.** Task management is built, working, and tested end-to-end in
> the native app. Notifications and the dashboard views described under
> [Roadmap](#roadmap) are not implemented yet.

## ⚠️ Disclaimer

**This is not production software.** It is a personal project in the earliest
stage of development. It may crash, behave unpredictably, or lose any data you
put into it, and it receives no support, no security review, and no guarantee
of updates. **Use it entirely at your own risk.**

This software is provided *as is*, without warranty of any kind, and the
authors accept no liability for any damages arising from its use, to the
fullest extent permitted by law — see sections 15 and 16 of the
[GNU GPL v3](LICENSE) it is distributed under.

## Download

| Platform | Download | Install |
| --- | --- | --- |
| **Windows** (10 build 1809+, x64) | [TaskDashboard-windows-x64.zip](https://github.com/ae-dev-2025/MyTaskNotificationsDashboard/releases/latest/download/TaskDashboard-windows-x64.zip) | Unzip anywhere, run `TaskDashboard.exe`. SmartScreen will warn because the build is unsigned — *More info → Run anyway* |
| **Android** (7.0+) | [TaskDashboard-android.apk](https://github.com/ae-dev-2025/MyTaskNotificationsDashboard/releases/latest/download/TaskDashboard-android.apk) | Open the APK on the phone and allow installing from unknown sources. Requires an up-to-date Android System WebView |

Every merge to `main` automatically publishes a fresh
[release](https://github.com/ae-dev-2025/MyTaskNotificationsDashboard/releases).
Android builds are signed with the maintainer's release key (held in CI
secrets), so updates install over previous versions cleanly.

## Why .NET MAUI?

This project started as a Blazor WebAssembly web app. It was rebuilt as a .NET
MAUI app because the goal of the project is **task notifications, and on Android
that requires being a real app**: scheduled local notifications that fire when
the app is closed, notification permissions, and delivery through the system
tray are native-platform capabilities that a web page cannot reliably provide.

MAUI Blazor Hybrid keeps the cost of that switch low — the UI is the same Razor
component code the web version used, now hosted in a native WebView, so the app
gained native capabilities without a UI rewrite. iOS/Mac Catalyst targets can be
added later by restoring their target frameworks in
`TaskDashboard/TaskDashboard.csproj` (a Mac build host is required to build them).

## What works today

- A **dashboard home** answering the three questions the app exists for:
  **Now** (what the plan says you should be doing at this minute), **Up next**
  (the following planned slots), and **Done** (what you finished, when) — plus
  summary tiles for tasks left, estimated workload, overdue count, and
  completions today
- **Start tracking**: press Start on a task (from the dashboard's Now panel or
  any task row) and it becomes the thing you are doing — one task at a time,
  starting another switches. The planner anchors a started task at *now* for
  its remaining time (estimate minus elapsed) and plans everything else after
  it, and completing a started task records the real [started, finished] span,
  which the calendar's history layer draws instead of guessing from the
  estimate
- Add, edit, and delete tasks — double-click a task or press **Edit** to change it inline
- Each task carries a **deadline**, a **priority** (Low / Normal / High / Urgent), and an
  **estimated time to complete**; all three are optional except the title
- Overdue tasks are flagged, and the footer totals the estimated time still outstanding
- Mark tasks done, filter by All / Active / Completed, and clear completed
- A **week-timeline calendar** with an auto-planner: unfinished tasks are placed
  into upcoming time slots (earliest deadline first, then priority, then the
  shorter estimate), deadlines draw as markers, a slot that cannot finish before
  its deadline is flagged, completed tasks appear dimmed at the time they were
  finished, and a now-line tracks the current minute
- **Blocked time**: recurring weekly windows (sleep, work hours — windows may
  cross midnight) and one-off periods (appointments). The planner schedules
  around them, and they draw as striped shading on the calendar
- **Planner realism**: a mandatory, editable **break between tasks** (default
  15 min, set on the Blocked time page); tasks can carry a **not-before**
  earliest start the planner honors; a **Split** action in the edit dialog
  divides a task into 2–4 independent part-tasks (the planner never splits on
  its own); and the dashboard shows a **capacity warning** when planned work
  can't finish before its deadlines or doesn't fit the horizon. Every change
  refreshes the plan on all open pages immediately
- **Dark mode**: follows the device's light/dark setting by default, with a
  System → Light → Dark override in the sidebar, persisted across restarts.
  Forcing Dark is recommended for always-on AMOLED displays
- Tasks persist to a JSON file in the app's private data directory, written
  atomically so an ill-timed crash cannot corrupt the list

- **Estimate calibration**: once five completed tasks carry a real
  [started, finished] span and an estimate, the dashboard shows your personal
  accuracy factor (e.g. ×1.4 — tasks take 1.4× what you guess). Sub-5-minute
  completions and extreme ratios are excluded as noise. Display-only by
  design: the planner keeps using your raw estimates

## Roadmap

- **Local notifications** for tasks that are due soon or overdue — the reason
  this is a MAUI app — on both Android and Windows
- **Backend + sync** so tasks and notification state follow you across devices
- **iOS / Mac Catalyst** targets once a Mac build host is available

## Running it

Windows (from the repository root):

```bash
dotnet build TaskDashboard/TaskDashboard.csproj -f net10.0-windows10.0.19041.0 -t:Run
```

Android (requires a running emulator or a connected device):

```bash
dotnet build TaskDashboard/TaskDashboard.csproj -f net10.0-android -t:Run
```

Requires the .NET 10 SDK with the `maui-windows` and `android` workloads
(installed automatically with the Visual Studio MAUI workload).

## Project layout

| Path | Purpose |
| --- | --- |
| `TaskDashboard/Models/TodoItem.cs` | The task model, including deadline, priority and estimate |
| `TaskDashboard/Models/TaskPriority.cs` | Priority levels, serialized by name |
| `TaskDashboard/Models/TaskForm.cs` | Editable shape of a task, shared by the add and edit forms |
| `TaskDashboard/Models/TodoJsonContext.cs` | Source-generated JSON serializer |
| `TaskDashboard/Services/DashboardService.cs` | Tasks + blocked periods, single-file atomic persistence |
| `TaskDashboard/Models/BlockedPeriod.cs` | A recurring or one-off period the planner schedules around |
| `TaskDashboard/Components/TaskFields.razor` | The four labeled task inputs, used inside the dialog |
| `TaskDashboard/Components/TaskModal.razor` | The add/edit dialog shared by both flows |
| `TaskDashboard/Services/Planner.cs` | Deterministic auto-planner placing tasks into free time |
| `TaskDashboard/Components/Pages/Dashboard.razor` | The home page: Now / Up next / Done |
| `TaskDashboard/Components/Pages/Home.razor` | The task list UI, at `/tasks` |
| `TaskDashboard/Components/Pages/CalendarPage.razor` | The week-timeline calendar |
| `design-system/` | Self-contained component previews, published to Claude Design |
| `TaskDashboard/Components/Pages/BlockedTimePage.razor` | Blocked-time management |
| `TaskDashboard/MauiProgram.cs` | App bootstrap and dependency injection |
| `tools/UiTest/` | End-to-end UI tests for Windows (Playwright over CDP) |
| `tools/AndroidTest/` | End-to-end UI tests for Android (raw CDP over adb) |

## Notes

Tasks are stored per-device in `FileSystem.AppDataDirectory/tasks.json`. Saves
write to a temp file and swap it in, so a crash mid-write leaves the previous
list intact. Cross-device sync sits on the roadmap because it needs a server
component.

Serialization uses a source-generated `JsonSerializerContext` rather than
reflection, so persistence keeps working under the trimming and AOT compilation
MAUI applies to Android release builds.

The UI is tested by driving the running native app over the Chrome DevTools
Protocol, on both platforms:

**Windows** — launching with
`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333` exposes
the app's WebView2 to Playwright. The suite lives in `tools/UiTest`:

```bash
dotnet run --project tools/UiTest -- suite   # full feature suite (resets the list)
dotnet run --project tools/UiTest -- verify  # after an app restart: asserts persistence
```

**Android** — debug builds enable WebView content debugging, but Android
WebView only speaks page-level CDP, which Playwright cannot attach to, so
`tools/AndroidTest` is a small raw CDP client instead:

```bash
adb forward tcp:9333 localabstract:webview_devtools_remote_$(adb shell pidof com.aedev2025.taskdashboard)
dotnet run --project tools/AndroidTest -- suite   # tasks, calendar, blocked time, dashboard
dotnet run --project tools/AndroidTest -- verify  # after force-stop + relaunch
```

Gotcha worth knowing: the app requires a reasonably current Android System
WebView. The android-31 emulator image ships Chromium 91, which cannot parse
`blazor.webview.js` (.NET 10) — Blazor silently never starts and the page sits
at "Loading...". The android-35 image (Chromium 124) works, as will any real
device with an up-to-date WebView.

Physical devices work the same way (enable USB debugging, same `adb forward`).
Two device realities the tool now survives: a locked screen stalls CDP
mid-run (calls carry a 20 s timeout and the client reconnects, since a
timed-out receive aborts a .NET `ClientWebSocket`), and some real-device
WebViews (observed: Samsung, Chromium 150) never answer
`Page.captureScreenshot` — screenshots are skipped rather than failing the
run; use `adb shell screencap` when a picture matters. Deploying to an arm64
phone after an x86_64 emulator needs `-p:RuntimeIdentifiers=android-arm64`
(or a clean) to rebuild for the right ABI.

## Design system

The UI's tokens (light + dark), badges, tiles, panels and calendar blocks are
documented as self-contained previews in `design-system/` and published to a
[Claude Design](https://claude.ai/design) project ("Task Dashboard Design
System") for visual browsing and iteration.

## Development

This project is developed with [Claude Code](https://claude.com/claude-code),
Anthropic's CLI coding assistant, used as an assistive tool. All code is
reviewed and authored by the repository owner, who remains responsible for it.
Commits that were produced with its help say so in the commit message.

## License

Copyright (C) 2026 Adilet Eshimkanov

This program is free software: you can redistribute it and/or modify it under
the terms of the GNU General Public License as published by the Free Software
Foundation, either version 3 of the License, or (at your option) any later
version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY
WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
PARTICULAR PURPOSE. See the [GNU General Public License](LICENSE) for more
details.

Bootstrap, bundled under `TaskDashboard/wwwroot/lib/bootstrap/`, is a separate
work distributed under the MIT License and retains its own terms.
