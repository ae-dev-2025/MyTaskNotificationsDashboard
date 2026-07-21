# My Task & Notifications Dashboard

A personal dashboard for tracking tasks and surfacing notifications about them,
built with **Blazor WebAssembly** (.NET 10).

> **Status: early.** The task list is built and working. Notifications and the
> dashboard views described under [Roadmap](#roadmap) are not implemented yet.
> Everything listed under [What works today](#what-works-today) is real and
> tested; nothing below that line is.

## What works today

Task management, running entirely in the browser:

- Add, rename, and delete tasks
- Mark tasks done — double-click a task to rename it inline
- Filter by All / Active / Completed
- Remaining-task count and "clear completed"
- Tasks persist in browser `localStorage`, so they survive a refresh

## Roadmap

The direction is a dashboard, not just a list:

- **Due dates and reminders** on tasks — the prerequisite for anything notification-shaped
- **In-app notifications** for due and overdue tasks
- **Browser notifications** via the Notification API, so reminders land outside the tab
- **Dashboard view** — summary tiles (due today, overdue, completed this week) alongside the list
- **Backend + sync** so tasks and notification state follow you across devices

## Running it

```bash
dotnet run --project TodoApp/TodoApp.csproj
```

Then open <http://localhost:5144>.

## Project layout

| Path | Purpose |
| --- | --- |
| `TodoApp/Models/TodoItem.cs` | The task model |
| `TodoApp/Models/TodoJsonContext.cs` | Source-generated JSON serializer |
| `TodoApp/Services/TodoService.cs` | Task state and `localStorage` persistence |
| `TodoApp/Pages/Home.razor` | The task UI, served at `/` |

The project directory is still named `TodoApp` from the original scaffold. It is
kept as-is for now so the assembly name and namespaces stay stable; renaming it
is a mechanical change best done in its own commit.

## Notes

Persistence is per-browser because a standalone WebAssembly app has no backend —
tasks are not synced across devices. That is also why cross-device sync sits on
the roadmap rather than being a small addition: it needs a server project.

Serialization uses a source-generated `JsonSerializerContext` rather than
reflection, since Blazor WebAssembly trims assemblies on publish and
reflection-based serialization can break at runtime in ways a debug build
won't reveal.

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

Bootstrap, bundled under `TodoApp/wwwroot/lib/bootstrap/`, is a separate work
distributed under the MIT License and retains its own terms.
