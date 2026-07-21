using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

// End-to-end UI tests for the Task Dashboard, driven over the Chrome DevTools
// Protocol against the RUNNING Windows app.
//
// Setup: launch the app with WebView2 remote debugging enabled, e.g.
//   $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS='--remote-debugging-port=9333'
//   & TaskDashboard\bin\Debug\net10.0-windows10.0.19041.0\win-x64\TaskDashboard.exe
//
// Usage: dotnet run --project tools/UiTest -- <mode> [screenshotDir]
//   suite         — resets the list, then exercises every task feature; leaves a known state
//   calendar      — resets the list, seeds a planning fixture, asserts the week view
//   verify        — after an app restart: asserts the suite's end state persisted
//   blocked       — blocked-time CRUD + planner routing around blocks
//   blockedverify — after an app restart: asserts blocked periods persisted
//   migrated      — after seeding a legacy v1 tasks.json: asserts it loaded intact
// Recommended order: calendar, suite, restart, verify, blocked, restart,
// blockedverify, migration seed + restart, migrated.

var mode = args.Length > 0 ? args[0] : "suite";
var shotDir = args.Length > 1 ? args[1] : ".";
var failures = 0;

using var pw = await Playwright.CreateAsync();
var browser = await pw.Chromium.ConnectOverCDPAsync("http://localhost:9333");
var page = browser.Contexts[0].Pages[0];
await page.WaitForSelectorAsync("h1");

// Every mode starts from the Tasks page, wherever the app was left.
await page.GetByRole(AriaRole.Link, new() { Name = "Tasks", Exact = true }).ClickAsync();
await page.WaitForSelectorAsync("h1");

var modal = page.Locator(".modal-panel");
var footer = page.Locator(".task-footer");

ILocator Row(string text) => page.Locator(".task-item", new() { HasTextString = text });

async Task Shot(string name) =>
    await page.ScreenshotAsync(new() { Path = Path.Combine(shotDir, name), FullPage = true });

async Task Step(string name, Func<Task> body)
{
    try
    {
        await body();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception e)
    {
        failures++;
        var first = e.Message.Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "unknown";
        Console.WriteLine($"FAIL  {name}: {first.Trim()}");
    }
}

async Task OpenAddModal() =>
    await page.GetByRole(AriaRole.Button, new() { Name = "Add task", Exact = true }).ClickAsync();

async Task FillModal(string? title, string? deadline, string? priority, string? estimate)
{
    if (title is not null) await modal.GetByLabel("Title").FillAsync(title);
    if (deadline is not null) await modal.GetByLabel("Deadline").FillAsync(deadline);
    if (priority is not null) await modal.GetByLabel("Priority").SelectOptionAsync(priority);
    if (estimate is not null) await modal.GetByLabel("Estimate (mins)").FillAsync(estimate);
}

async Task SaveModal() =>
    await modal.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

async Task CancelModal() =>
    await modal.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

async Task AddTask(string title, string? deadline, string? priority, string? estimate)
{
    await OpenAddModal();
    await FillModal(title, deadline, priority, estimate);
    await SaveModal();
    await Expect(modal).ToHaveCountAsync(0);
}

async Task ResetAllTasks()
{
    while (await page.Locator(".task-item").CountAsync() > 0)
    {
        await page.Locator(".task-item").First
            .GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Delete") }).ClickAsync();
        await page.WaitForTimeoutAsync(200);
    }
    await Expect(page.Locator(".task-empty")).ToHaveCountAsync(1);
}

async Task NavTo(string link, string heading)
{
    await page.GetByRole(AriaRole.Link, new() { Name = link, Exact = true }).ClickAsync();
    await Expect(page.Locator("h1")).ToHaveTextAsync(heading);
}

if (mode == "verify")
{
    await Step("persisted: exactly one task after restart", () =>
        Expect(page.Locator(".task-item")).ToHaveCountAsync(1));
    await Step("persisted: title survived restart", () =>
        Expect(Row("Buy milk")).ToHaveCountAsync(1));
    await Step("persisted: Low badge survived restart", () =>
        Expect(Row("Buy milk").Locator(".badge.priority")).ToHaveTextAsync(new Regex("Low")));
    await Step("persisted: estimate survived restart", () =>
        Expect(Row("Buy milk").Locator(".estimate")).ToHaveTextAsync(new Regex("15m")));
    await Step("persisted: footer totals restored", () =>
        Expect(footer).ToContainTextAsync("1 task left"));
    await Shot("ui-verify-restart.png");
    Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
    return failures == 0 ? 0 : 1;
}

if (mode == "blocked")
{
    static string Dt(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm");
    var lunchStart = DateTime.Now.AddMinutes(10);
    var lunchEnd = DateTime.Now.AddMinutes(40);

    await Step("blocked: page reachable via nav link", () => NavTo("Blocked time", "Blocked time"));

    await Step("blocked: reset any existing periods", async () =>
    {
        while (await page.Locator(".blocked-item").CountAsync() > 0)
        {
            await page.Locator(".blocked-item").First
                .GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Delete") }).ClickAsync();
            await page.WaitForTimeoutAsync(200);
        }
        await Expect(page.Locator(".blocked-empty")).ToHaveCountAsync(1);
    });

    await Step("blocked: validation rejects a missing label", async () =>
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add blocked time" }).ClickAsync();
        await modal.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(modal.Locator(".validation-errors li").First).ToContainTextAsync("Give the period a label.");
        await modal.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
    });

    await Step("blocked: add recurring Sleep with default nightly window", async () =>
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add blocked time" }).ClickAsync();
        await modal.GetByLabel("Label").FillAsync("Sleep");
        await modal.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(page.Locator(".blocked-item", new() { HasTextString = "Sleep" })).ToHaveCountAsync(1);
        await Expect(page.Locator(".blocked-item", new() { HasTextString = "Sleep" }))
            .ToContainTextAsync("Every day, 23:00 – 07:00 (next day)");
    });

    await Step("blocked: add a one-off Lunch break", async () =>
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add blocked time" }).ClickAsync();
        await modal.GetByLabel("Label").FillAsync("Lunch break");
        await modal.GetByLabel("Repeats").SelectOptionAsync(new SelectOptionValue { Label = "One-off" });
        await modal.GetByLabel("Start", new() { Exact = true }).FillAsync(Dt(lunchStart));
        await modal.GetByLabel("End", new() { Exact = true }).FillAsync(Dt(lunchEnd));
        await modal.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(page.Locator(".blocked-item")).ToHaveCountAsync(2);
    });
    await Shot("blk-01-list.png");

    await Step("seed: one unestimated-deadline-free task to plan", async () =>
    {
        await NavTo("Tasks", "Tasks");
        await ResetAllTasks();
        await AddTask("Deep work", null, null, "30");
    });

    await Step("calendar: blocked shading rendered for Sleep and Lunch", async () =>
    {
        await NavTo("Calendar", "Calendar");
        var sleepBlocks = await page.Locator(".cal-blocked", new() { HasTextString = "Sleep" }).CountAsync();
        if (sleepBlocks < 7)
        {
            throw new Exception($"expected >=7 Sleep shading blocks across the week, found {sleepBlocks}");
        }
        await Expect(page.Locator(".cal-day.today .cal-blocked", new() { HasTextString = "Lunch break" }))
            .ToHaveCountAsync(1);
    });

    await Step("planner: task routed to after the lunch block", async () =>
    {
        var lunch = page.Locator(".cal-day.today .cal-blocked", new() { HasTextString = "Lunch break" });
        var slot = page.Locator(".cal-day.today .cal-slot", new() { HasTextString = "Deep work" });
        await Expect(slot).ToHaveCountAsync(1);

        var lunchBox = await lunch.BoundingBoxAsync() ?? throw new Exception("no lunch box");
        var slotBox = await slot.BoundingBoxAsync() ?? throw new Exception("no slot box");
        if (slotBox.Y < lunchBox.Y + lunchBox.Height - 2)
        {
            throw new Exception(
                $"Deep work starts at y={slotBox.Y:0} but lunch runs to y={lunchBox.Y + lunchBox.Height:0} — not routed around");
        }
    });

    // Scroll the grid so the interesting region is in frame.
    await page.EvalOnSelectorAsync(".cal-scroll",
        "el => { el.scrollTop = Math.max(0, (new Date().getHours() - 1.5) * 48); }");
    await Shot("blk-02-calendar.png");

    Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
    return failures == 0 ? 0 : 1;
}

if (mode == "blockedverify")
{
    await Step("persisted: blocked periods survived restart", async () =>
    {
        await NavTo("Blocked time", "Blocked time");
        await Expect(page.Locator(".blocked-item")).ToHaveCountAsync(2);
        await Expect(page.Locator(".blocked-item", new() { HasTextString = "Sleep" })).ToHaveCountAsync(1);
        await Expect(page.Locator(".blocked-item", new() { HasTextString = "Lunch break" })).ToHaveCountAsync(1);
    });

    await Step("persisted: tasks survived restart alongside blocks", async () =>
    {
        await NavTo("Tasks", "Tasks");
        await Expect(Row("Deep work")).ToHaveCountAsync(1);
    });

    Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
    return failures == 0 ? 0 : 1;
}

if (mode == "migrated")
{
    await Step("migration: legacy v1 task loaded intact", async () =>
    {
        await Expect(page.Locator(".task-item")).ToHaveCountAsync(1);
        await Expect(Row("Legacy task")).ToHaveCountAsync(1);
        await Expect(Row("Legacy task").Locator(".badge.priority")).ToHaveTextAsync(new Regex("Low"));
        await Expect(Row("Legacy task").Locator(".estimate")).ToContainTextAsync("15m");
    });

    Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
    return failures == 0 ? 0 : 1;
}

if (mode == "calendar")
{
    static string Dt(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm");

    await Step("reset: delete any existing tasks", ResetAllTasks);

    await Step("seed: crunch task, tomorrow-noon deadline, no-deadline task", async () =>
    {
        // 120 min of work due in 30 min — the planner must flag this as missing
        // its deadline no matter when the test runs.
        await AddTask("Crunch task", Dt(DateTime.Now.AddMinutes(30)), "Urgent", "120");
        await AddTask("Write spec", Dt(DateTime.Today.AddDays(1).AddHours(12)), "High", "60");
        await AddTask("Email follow-up", null, "Normal", "30");
        await Expect(page.Locator(".task-item")).ToHaveCountAsync(3);
    });

    await Step("seed: complete one task for the done layer", async () =>
    {
        await Row("Email follow-up").Locator("input[type=checkbox]").ClickAsync();
        await Expect(page.Locator(".task-item.done")).ToHaveCountAsync(1);
    });

    await Step("calendar: reachable via nav link", async () =>
    {
        await page.GetByRole(AriaRole.Link, new() { Name = "Calendar" }).ClickAsync();
        await Expect(page.Locator("h1")).ToHaveTextAsync("Calendar");
    });

    await Step("calendar: planned slots rendered, earliest-deadline first", async () =>
    {
        await Expect(page.Locator(".cal-slot", new() { HasTextString = "Crunch task" }).First).ToBeVisibleAsync();
        await Expect(page.Locator(".cal-slot", new() { HasTextString = "Write spec" }).First).ToBeVisibleAsync();
        // Plan order: Crunch (soonest deadline) is today's first slot.
        await Expect(page.Locator(".cal-day.today .cal-slot").First).ToContainTextAsync("Crunch task");
    });

    await Step("calendar: impossible deadline is flagged", () =>
        Expect(page.Locator(".cal-slot.missed").First).ToContainTextAsync("Crunch task"));

    await Step("calendar: completed task appears as a done block", () =>
        Expect(page.Locator(".cal-done").First).ToContainTextAsync("Email follow-up"));

    await Step("calendar: deadline marker present", () =>
        Expect(page.Locator(".cal-deadline").First).ToBeVisibleAsync());

    await Step("calendar: now line drawn on today", () =>
        Expect(page.Locator(".cal-now-line")).ToHaveCountAsync(1));

    // Scroll the grid so the slots planned from "now" are actually in frame.
    await page.EvalOnSelectorAsync(".cal-scroll",
        "el => { el.scrollTop = Math.max(0, (new Date().getHours() - 1.5) * 48); }");
    await Shot("cal-01-week.png");

    await Step("calendar: week navigation moves off today and back", async () =>
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Next week ›" }).ClickAsync();
        await Expect(page.Locator(".cal-now-line")).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Button, new() { Name = "Today", Exact = true }).ClickAsync();
        await Expect(page.Locator(".cal-now-line")).ToHaveCountAsync(1);
    });
    await Shot("cal-02-back-today.png");

    Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
    return failures == 0 ? 0 : 1;
}

// ---- full suite ----

await Step("reset: delete any existing tasks", ResetAllTasks);
await Shot("ui-01-empty.png");

await Step("modal: opens with labeled fields and focuses title", async () =>
{
    await OpenAddModal();
    await Expect(modal).ToHaveCountAsync(1);
    foreach (var label in new[] { "Title", "Deadline", "Priority", "Estimate (mins)" })
    {
        await Expect(modal.Locator(".field label", new() { HasTextString = label })).ToHaveCountAsync(1);
    }
    await Expect(modal.GetByLabel("Title")).ToBeFocusedAsync();
});
await Shot("ui-02-modal-open.png");

await Step("validation: empty title keeps modal open, adds nothing", async () =>
{
    await SaveModal();
    await Expect(modal).ToHaveCountAsync(1);
    await Expect(modal.Locator(".validation-errors li").First).ToContainTextAsync("Give the task a title.");
    await Expect(page.Locator(".task-item")).ToHaveCountAsync(0);
});

await Step("modal: Escape closes without saving", async () =>
{
    await modal.GetByLabel("Title").PressAsync("Escape");
    await Expect(modal).ToHaveCountAsync(0);
    await Expect(page.Locator(".task-item")).ToHaveCountAsync(0);
});

await Step("add: title-only task via modal", async () =>
{
    await AddTask("milk", null, null, null);
    await Expect(Row("milk")).ToHaveCountAsync(1);
    await Expect(Row("milk").Locator(".badge.priority")).ToHaveTextAsync(new Regex("Normal"));
});

await Step("add: full task with deadline, priority, estimate", async () =>
{
    await AddTask("Quarterly report", "2026-08-15T17:30", "High", "150");
    await Expect(Row("Quarterly report").Locator(".badge.priority")).ToHaveTextAsync(new Regex("High"));
    await Expect(Row("Quarterly report").Locator(".deadline")).ToContainTextAsync("15 Aug");
    await Expect(Row("Quarterly report").Locator(".estimate")).ToContainTextAsync("2h 30m");
});

await Step("add: past deadline flags the row overdue", async () =>
{
    await AddTask("Pay overdue invoice", "2026-07-01T09:00", "Urgent", null);
    await Expect(Row("Pay overdue invoice").Locator(".overdue-flag")).ToHaveCountAsync(1);
    await Expect(page.Locator(".task-item.overdue")).ToHaveCountAsync(1);
});
await Shot("ui-03-three-tasks.png");

await Step("edit: modal opens prefilled with current values", async () =>
{
    await Row("milk").GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Edit") }).ClickAsync();
    await Expect(modal.GetByLabel("Title")).ToHaveValueAsync("milk");
    await Expect(modal).ToContainTextAsync("Edit task");
});

await Step("edit: save persists new title, priority and estimate", async () =>
{
    await FillModal("Buy milk", null, "Low", "15");
    await SaveModal();
    await Expect(Row("Buy milk")).ToHaveCountAsync(1);
    await Expect(Row("Buy milk").Locator(".badge.priority")).ToHaveTextAsync(new Regex("Low"));
    await Expect(Row("Buy milk").Locator(".estimate")).ToContainTextAsync("15m");
});

await Step("edit: cancel discards changes", async () =>
{
    await Row("Buy milk").GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Edit") }).ClickAsync();
    await FillModal("SHOULD NOT SAVE", null, null, null);
    await CancelModal();
    await Expect(Row("Buy milk")).ToHaveCountAsync(1);
    await Expect(Row("SHOULD NOT SAVE")).ToHaveCountAsync(0);
});

await Step("edit: double-click on task body opens the modal", async () =>
{
    await Row("Buy milk").Locator(".task-body").DblClickAsync();
    await Expect(modal.GetByLabel("Title")).ToHaveValueAsync("Buy milk");
    await CancelModal();
});

await Step("toggle: marking done strikes the row and updates footer", async () =>
{
    await Row("Pay overdue invoice").Locator("input[type=checkbox]").ClickAsync();
    await Expect(page.Locator(".task-item.done")).ToHaveCountAsync(1);
    await Expect(footer).ToContainTextAsync("2 tasks left");
});

await Step("filters: Active shows only unfinished", async () =>
{
    await page.GetByRole(AriaRole.Button, new() { Name = "Active", Exact = true }).ClickAsync();
    await Expect(page.Locator(".task-item")).ToHaveCountAsync(2);
    await Expect(Row("Pay overdue invoice")).ToHaveCountAsync(0);
});

await Step("filters: Completed shows only finished", async () =>
{
    await page.GetByRole(AriaRole.Button, new() { Name = "Completed", Exact = true }).ClickAsync();
    await Expect(page.Locator(".task-item")).ToHaveCountAsync(1);
});

await Step("filters: back to All", async () =>
{
    await page.GetByRole(AriaRole.Button, new() { Name = "All", Exact = true }).ClickAsync();
    await Expect(page.Locator(".task-item")).ToHaveCountAsync(3);
});

await Step("clear completed removes the done task", async () =>
{
    await page.GetByRole(AriaRole.Button, new() { Name = "Clear completed" }).ClickAsync();
    await Expect(page.Locator(".task-item")).ToHaveCountAsync(2);
});

await Step("delete removes a task", async () =>
{
    await Row("Quarterly report").GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Delete") }).ClickAsync();
    await Expect(page.Locator(".task-item")).ToHaveCountAsync(1);
    await Expect(Row("Buy milk")).ToHaveCountAsync(1);
});

await Step("footer: totals only the remaining estimate", () =>
    Expect(footer).ToContainTextAsync("1 task left"));
await Shot("ui-04-final.png");

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
