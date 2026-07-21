# Blazor To-Do

A to-do list app built with **Blazor WebAssembly** (.NET 10). Runs entirely in the
browser — there is no server component and no database.

## Features

- Add, rename, and delete tasks
- Mark tasks done (double-click a task to rename it inline)
- Filter by All / Active / Completed
- Remaining-task count and "clear completed"
- Tasks persist in browser `localStorage`, so they survive a refresh

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
| `TodoApp/Services/TodoService.cs` | List state and `localStorage` persistence |
| `TodoApp/Pages/Home.razor` | The UI, served at `/` |

## Notes

Persistence is per-browser because a standalone WebAssembly app has no backend —
tasks are not synced across devices. Adding a hosted API project would be the
next step if you want that.

Serialization uses a source-generated `JsonSerializerContext` rather than
reflection, since Blazor WebAssembly trims assemblies on publish and
reflection-based serialization can break at runtime in ways a debug build
won't reveal.

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
