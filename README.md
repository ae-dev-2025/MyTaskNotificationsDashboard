# My Task & Notifications Dashboard

A personal dashboard for tracking tasks and surfacing notifications about them,
built with **.NET MAUI Blazor Hybrid** (.NET 10), targeting **Windows** and
**Android**.

> **Status: early.** Task management, the dashboard views and local
> notifications are all built and working in the native app; everything but
> notification delivery is covered by the end-to-end suites. What remains
> under [Roadmap](#roadmap) is cross-device sync and the iOS targets.

**See it without running it:** [docs/tour.html](docs/tour.html) is a
screen-by-screen tour with every screenshot in both themes
([docs/README.md](docs/README.md) covers regenerating it).

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
| **Windows** (10 build 1809+, x64) | [TaskDashboard-windows-x64.zip](https://github.com/ae-dev-2025/MyTaskNotificationsDashboard/releases/latest/download/TaskDashboard-windows-x64.zip) | Unzip anywhere, run `TaskDashboard.exe` — the build is self-contained, so no .NET or Windows App SDK runtime is needed. SmartScreen will warn because the build is unsigned — *More info → Run anyway* |
| **Android** (7.0+) | [TaskDashboard-android.apk](https://github.com/ae-dev-2025/MyTaskNotificationsDashboard/releases/latest/download/TaskDashboard-android.apk) | Open the APK on the phone and allow installing from unknown sources. Requires an up-to-date Android System WebView. Allow notifications when asked, or deadline reminders stay silent |

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
That bet has since paid off — Android reminders are real `AlarmManager` alarms
posted by a broadcast receiver, so they arrive with the app shut.

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
- **Quick add**: type the whole task on one line and the fields fill themselves.
  `Submit timesheet by fri 17:00 !high 45m` becomes a High-priority task called
  *Submit timesheet*, due Friday at 17:00, estimated at 45 minutes. The fields
  populate as you type so a mis-read is visible and correctable before saving,
  a field you set by hand is never overwritten by later typing, and anything
  unrecognised stays in the title. A **Quick add syntax** link in the dialog
  lists the whole grammar:

  | Syntax | Meaning |
  | --- | --- |
  | `by fri 17:00`, `due tomorrow` | Deadline |
  | `after mon 09:00`, `from tomorrow` | Earliest start the planner may use |
  | `!low` `!normal` `!high` `!urgent` (or `!l` `!n` `!h` `!u`) | Priority |
  | `45m` `2h` `1h30` | Estimate — a unit is required, so a bare number stays in the title |
  | `today` `tomorrow` `fri` `next tue` | Days; alone, a day means the end of it for a deadline and the start of it for an earliest start |
  | `31/7` `15 aug` `2026-08-15` | Dates, read **day-first** |
  | `17:00` `5pm` `5:30pm` | Times; a bare number is never read as a time |

  Quick add is offered when adding a task, not when editing one — an existing
  title is text you already committed to
- **Presets** for the tasks you add over and over: a saved title, priority and
  estimate. The add dialog searches them as you type — choose one and the
  fields fill, with Enter applying the top match. A preset deliberately stores
  **no dates**, because when a task is due is the part that differs every time
  you add it. Create them on the Presets page, or press **Save as preset** in
  a task's edit dialog to capture one you have already typed
- Each task carries a **deadline**, a **priority** (Low / Normal / High / Urgent), and an
  **estimated time to complete**; all three are optional except the title
- Overdue tasks are flagged, and the footer totals the estimated time still outstanding
- Mark tasks done, filter by All / Active / Completed, and clear completed
- A **week-timeline calendar** with an auto-planner: unfinished tasks are placed
  into upcoming time slots (earliest deadline first, then priority, then the
  shorter estimate), deadlines draw as markers, a slot that cannot finish before
  its deadline carries a **late** tag, completed tasks appear dimmed at the time
  they were finished, and a now-line tracks the current minute. Entries that run
  concurrently **split into side-by-side lanes** rather than covering each other
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
- **Day, 3-day and week views**: the calendar switches span from the
  toolbar. The narrow views anchor on today and give every column seven
  times or twice the width, which is the difference between a truncated
  title and a readable one on a tablet
- **Calendar zoom**: a vertical slider beside the grid (on the right)
  stretches or compresses the hours, and taller blocks spend the extra room
  on their titles — text wraps to as many whole lines as the block affords.
  Zooming keeps the moment at the centre of the view where you left it, and
  the slider can be hidden from the Settings page
- **Collapsible navigation**: a full-height handle on the sidebar's edge
  hides it for a wider calendar and brings it back with one click, always
  from the same spot
- **Dark mode**: follows the device's light/dark setting by default, with a
  System → Light → Dark override in the sidebar, persisted across restarts.
  Forcing Dark is recommended for always-on AMOLED displays
- **Estimate calibration**: once five completed tasks carry a real
  [started, finished] span and an estimate, the dashboard shows your personal
  accuracy factor (e.g. ×1.4 — tasks take 1.4× what you guess). Sub-5-minute
  completions and extreme ratios are excluded as noise. Display-only by
  design: the planner keeps using your raw estimates
- **Local notifications** — the reason this is a MAUI app. Each task with a
  deadline gets a heads-up reminder (an hour ahead by default, editable) and
  a second one when it falls due. Separately, an **always-on notification**
  can stay pinned with whatever the plan says you should be doing right now,
  refreshed as the clock moves. Both are toggleable on the **Notifications**
  page and both are on by default. Platform differences are real and worth
  knowing:
  - **Android** delivers reminders **even when the app is closed**, and is
    the platform that gets the always-on current-task notification
  - **Windows** shows reminders **while the app is running** — an unpackaged
    Windows app cannot schedule a toast for a closed app — and has no
    always-on notification
- Tasks persist to a JSON file in the app's private data directory, written
  atomically so an ill-timed crash cannot corrupt the list

## Roadmap

- **Local AI assistant**: an optional, downloaded on-device
  model for conversational task management ("push everything low-priority to
  next week"), schema-constrained so it can only emit actions the app already
  has, with the deterministic planner staying in charge of scheduling
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
| `TaskDashboard/Services/QuickAdd.cs` | The one-line task parser — pure, clock injected |
| `tools/QuickAddTests/` | Unit tests for the parser, run against a frozen clock |
| `TaskDashboard/Models/TaskPreset.cs` | A saved title, priority and estimate — no dates by design |
| `TaskDashboard/Components/Pages/PresetsPage.razor` | Preset management, at `/presets` |
| `TaskDashboard/Components/Pages/SettingsPage.razor` | Display preferences, at `/settings` |
| `TaskDashboard/Components/Pages/Notifications.razor` | Reminder settings: on/off, lead time, always-on current task |
| `TaskDashboard/Services/NotificationScheduling.cs` | Decides which reminders to fire and when — pure, shared by both platforms |
| `TaskDashboard/Services/NotificationCoordinator.cs` | Keeps delivered notifications in step with the data and the clock |
| `TaskDashboard/Platforms/Android/AndroidNotificationService.cs` | `AlarmManager` reminders + the ongoing current-task notification |
| `TaskDashboard/Platforms/Windows/WindowsNotificationService.cs` | Toast reminders while the app runs |
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

Which reminders should exist is decided by a single pure function shared by
both platforms, so Windows and Android cannot disagree about what is due.
Every edit re-syncs the schedule: each reminder carries a stable id derived
from the task, the kind of reminder and its fire time, so moving a deadline
replaces that task's alarm rather than stacking a second one on top of it, and
deleting a task cancels its alarm. Android asks for notification permission on
first launch; declining leaves the rest of the app fully usable.

The quick-add parser is the one piece with real unit tests, because it is the
one piece that is pure: it takes a string and the current time and returns
fields, touching nothing else. The clock is a parameter rather than something
it reads, so "friday at 5pm, said on a Friday afternoon" is an ordinary test
case instead of something that can only be observed on a Friday afternoon:

```bash
dotnet run --project tools/QuickAddTests
```

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

The previews style themselves with the same `var(--*)` tokens the app uses, and
each one carries a generated copy of the token declarations so it still renders
standalone once published. Regenerate that copy from `app.css` after changing a
token, then re-publish:

```powershell
./design-system/sync-tokens.ps1
```

Editing the previews' colours by hand is what let them reproduce the app's own
hardcoded-blue bug in both themes, so the docs agreed with the bug instead of
catching it.

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
