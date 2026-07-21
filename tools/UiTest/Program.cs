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
// Usage: dotnet run --project tools/UiTest -- <suite|verify> [screenshotDir]
//   suite  — resets the list, then exercises every feature; leaves a known state
//   verify — run after an app restart to assert the suite's end state persisted

var mode = args.Length > 0 ? args[0] : "suite";
var shotDir = args.Length > 1 ? args[1] : ".";
var failures = 0;

using var pw = await Playwright.CreateAsync();
var browser = await pw.Chromium.ConnectOverCDPAsync("http://localhost:9333");
var page = browser.Contexts[0].Pages[0];
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

// ---- full suite ----

await Step("reset: delete any existing tasks", async () =>
{
    while (await page.Locator(".task-item").CountAsync() > 0)
    {
        await page.Locator(".task-item").First
            .GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Delete") }).ClickAsync();
        await page.WaitForTimeoutAsync(200);
    }
    await Expect(page.Locator(".task-empty")).ToHaveCountAsync(1);
});
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
