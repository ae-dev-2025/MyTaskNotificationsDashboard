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
//   presets       — preset CRUD, the add-dialog picker, and save-as-preset
//   presetsverify — after an app restart: asserts presets persisted
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

async Task FillModal(string? title, string? deadline, string? priority, string? estimate, string? notBefore = null)
{
    if (title is not null) await modal.GetByLabel("Title").FillAsync(title);
    if (deadline is not null) await modal.GetByLabel("Deadline").FillAsync(deadline);
    if (priority is not null) await modal.GetByLabel("Priority").SelectOptionAsync(priority);
    if (estimate is not null) await modal.GetByLabel("Estimate (mins)").FillAsync(estimate);
    if (notBefore is not null) await modal.GetByLabel("Not before").FillAsync(notBefore);
}

// Exact, because Playwright matches an accessible name by substring: the edit
// dialog's "Save as preset" button would otherwise resolve alongside "Save"
// and every save in every mode would fail on a strict-mode violation.
async Task SaveModal() =>
    await modal.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

async Task CancelModal() =>
    await modal.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

async Task AddTask(string title, string? deadline, string? priority, string? estimate, string? notBefore = null)
{
    await OpenAddModal();
    await FillModal(title, deadline, priority, estimate, notBefore);
    await SaveModal();
    await Expect(modal).ToHaveCountAsync(0);
}

// Reads a calendar slot's planned [start, end] as minutes-of-day from its
// tooltip. Returns null when the slot isn't on the visible week.
async Task<(int Start, int End)?> SlotTimes(string exactTitle)
{
    var raw = await page.EvaluateAsync<string>(
        """
        (title) => {
            const el = [...document.querySelectorAll('.cal-slot')].find(e =>
                (e.querySelector('.cal-block-title')?.innerText ?? '').trim() === title);
            if (!el) return '';
            const m = (el.getAttribute('title') || '').match(/planned (\d\d):(\d\d)–(\d\d):(\d\d)/);
            return m ? `${+m[1] * 60 + +m[2]},${+m[3] * 60 + +m[4]}` : '';
        }
        """, exactTitle);
    if (string.IsNullOrEmpty(raw)) return null;
    var parts = raw.Split(',');
    return (int.Parse(parts[0]), int.Parse(parts[1]));
}

// Gap between two slots in minutes, tolerant of a midnight crossing.
static int GapMinutes(int firstEnd, int secondStart) =>
    secondStart >= firstEnd ? secondStart - firstEnd : secondStart + 1440 - firstEnd;

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
        await modal.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(modal.Locator(".validation-errors li").First).ToContainTextAsync("Give the period a label.");
        await modal.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
    });

    await Step("blocked: add recurring Sleep, window kept clear of now", async () =>
    {
        // Clock-relative window (now+3h .. now+11h) so the fixture can never
        // swallow the current time, whatever hour the suite runs at.
        await page.GetByRole(AriaRole.Button, new() { Name = "Add blocked time" }).ClickAsync();
        await modal.GetByLabel("Label").FillAsync("Sleep");
        await modal.GetByLabel("Start time").FillAsync(DateTime.Now.AddHours(3).ToString("HH:mm"));
        await modal.GetByLabel("End time").FillAsync(DateTime.Now.AddHours(11).ToString("HH:mm"));
        await modal.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(page.Locator(".blocked-item", new() { HasTextString = "Sleep" })).ToHaveCountAsync(1);
        await Expect(page.Locator(".blocked-item", new() { HasTextString = "Sleep" }))
            .ToContainTextAsync("Every day,");
    });

    await Step("blocked: add a one-off Lunch break", async () =>
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add blocked time" }).ClickAsync();
        await modal.GetByLabel("Label").FillAsync("Lunch break");
        await modal.GetByLabel("Repeats").SelectOptionAsync(new SelectOptionValue { Label = "One-off" });
        await modal.GetByLabel("Start", new() { Exact = true }).FillAsync(Dt(lunchStart));
        await modal.GetByLabel("End", new() { Exact = true }).FillAsync(Dt(lunchEnd));
        await modal.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
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

    await Step("planner: slot exists and never overlaps blocked time", async () =>
    {
        // The invariant that holds at any hour: the planned slot may land
        // after the lunch block or on a later day — including next week's
        // grid, when the lunch block pushes it past a Sunday midnight. The
        // overlap check itself is column-local, so it is valid on whichever
        // week the slot renders.
        if (await page.Locator(".cal-slot", new() { HasTextString = "Deep work" }).CountAsync() == 0)
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Next week ›" }).ClickAsync();
            await page.WaitForTimeoutAsync(400);
        }

        var verdict = await page.EvaluateAsync<string>(
            """
            () => {
                const slots = [...document.querySelectorAll('.cal-slot')];
                const slot = slots.find(e => e.innerText.includes('Deep work'));
                if (!slot) {
                    return 'MISSING; slots present: [' +
                        slots.map(e => e.innerText.replace(/\s+/g, ' ')).join(' | ') + ']';
                }
                const s = slot.getBoundingClientRect();
                // Blocks at or under the clamp are midnight fragments inflated
                // by the calendar's readability floor (MinBlockPx, currently
                // 20px) — their pixels overstate their real duration, so they
                // can't prove a genuine overlap. Keep this in step with
                // CalendarPage.MinBlockPx.
                const hit = [...slot.parentElement.querySelectorAll('.cal-blocked')]
                    .filter(b => b.getBoundingClientRect().height > 21)
                    .find(b => {
                        const r = b.getBoundingClientRect();
                        return r.top < s.bottom - 1 && r.bottom > s.top + 1;
                    });
                return hit
                    ? 'OVERLAP with ' + hit.innerText.replace(/\s+/g, ' ') +
                      ' slot=' + Math.round(s.top) + '..' + Math.round(s.bottom) +
                      ' block=' + Math.round(hit.getBoundingClientRect().top) + '..' +
                      Math.round(hit.getBoundingClientRect().bottom)
                    : 'OK';
            }
            """);
        if (verdict != "OK")
        {
            throw new Exception(verdict);
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

if (mode == "presets")
{
    ILocator PresetRow(string text) => page.Locator(".preset-item", new() { HasTextString = text });

    await Step("presets: page reachable via nav link", () => NavTo("Presets", "Presets"));

    await Step("presets: reset any existing presets", async () =>
    {
        while (await page.Locator(".preset-item").CountAsync() > 0)
        {
            await page.Locator(".preset-item").First
                .GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Delete") }).ClickAsync();
            await page.WaitForTimeoutAsync(200);
        }
        await Expect(page.Locator(".preset-empty")).ToHaveCountAsync(1);
    });

    await Step("presets: validation rejects a missing title", async () =>
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add preset" }).ClickAsync();
        await modal.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(modal.Locator(".validation-errors li").First).ToContainTextAsync("Give the preset a title.");
        await modal.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Expect(modal).ToHaveCountAsync(0);
    });

    await Step("presets: add Weekly review", async () =>
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add preset" }).ClickAsync();
        await modal.GetByLabel("Title").FillAsync("Weekly review");
        await modal.GetByLabel("Priority").SelectOptionAsync("Normal");
        await modal.GetByLabel("Estimate (mins)").FillAsync("60");
        await modal.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(PresetRow("Weekly review")).ToHaveCountAsync(1);
        await Expect(PresetRow("Weekly review").Locator(".estimate")).ToContainTextAsync("1h");
    });

    await Step("presets: add Daily standup", async () =>
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add preset" }).ClickAsync();
        await modal.GetByLabel("Title").FillAsync("Daily standup");
        await modal.GetByLabel("Priority").SelectOptionAsync("Urgent");
        await modal.GetByLabel("Estimate (mins)").FillAsync("15");
        await modal.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(page.Locator(".preset-item")).ToHaveCountAsync(2);
        await Expect(PresetRow("Daily standup").Locator(".badge.priority")).ToHaveTextAsync(new Regex("Urgent"));
    });

    await Step("presets: list is ordered by title, not insertion", async () =>
    {
        // Daily standup was added second; ordering by title must still put it
        // first, so the picker and this page cannot disagree.
        await Expect(page.Locator(".preset-item").First.Locator(".preset-title"))
            .ToHaveTextAsync("Daily standup");
    });

    await Step("presets: edit updates the stored values", async () =>
    {
        await PresetRow("Weekly review")
            .GetByRole(AriaRole.Button, new() { Name = "Edit" }).ClickAsync();
        await Expect(modal.GetByLabel("Title")).ToHaveValueAsync("Weekly review");
        await modal.GetByLabel("Estimate (mins)").FillAsync("90");
        await modal.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(PresetRow("Weekly review").Locator(".estimate")).ToContainTextAsync("1h 30m");
    });
    await Shot("pre-01-list.png");

    await Step("picker: add dialog offers the presets", async () =>
    {
        await NavTo("Tasks", "Tasks");
        await ResetAllTasks();
        await OpenAddModal();
        await Expect(modal.Locator(".preset-picker")).ToHaveCountAsync(1);
        await Expect(modal.Locator(".preset-option")).ToHaveCountAsync(2);
    });

    await Step("picker: search filters the list", async () =>
    {
        await modal.GetByLabel("Start from a preset").FillAsync("week");
        await Expect(modal.Locator(".preset-option")).ToHaveCountAsync(1);
        await Expect(modal.Locator(".preset-option")).ToContainTextAsync("Weekly review");
    });

    await Step("picker: a query matching nothing says so", async () =>
    {
        await modal.GetByLabel("Start from a preset").FillAsync("nothing matches this");
        await Expect(modal.Locator(".preset-option")).ToHaveCountAsync(0);
        await Expect(modal.Locator(".preset-none")).ToHaveCountAsync(1);
    });

    await Step("picker: choosing a preset fills title, priority and estimate", async () =>
    {
        await modal.GetByLabel("Start from a preset").FillAsync("daily");
        await modal.Locator(".preset-option").First.ClickAsync();
        await Expect(modal.GetByLabel("Title")).ToHaveValueAsync("Daily standup");
        await Expect(modal.GetByLabel("Priority")).ToHaveValueAsync("Urgent");
        await Expect(modal.GetByLabel("Estimate (mins)")).ToHaveValueAsync("15");
    });

    await Step("picker: dates are left alone for the user to set", async () =>
    {
        // A preset carries no dates; applying one must not clear a deadline
        // the user already typed, nor invent one.
        await Expect(modal.GetByLabel("Deadline")).ToHaveValueAsync("");
        await Expect(modal.GetByLabel("Not before")).ToHaveValueAsync("");
    });
    await Shot("pre-02-picker.png");

    await Step("picker: saving creates the task from the preset", async () =>
    {
        await SaveModal();
        await Expect(modal).ToHaveCountAsync(0);
        await Expect(Row("Daily standup")).ToHaveCountAsync(1);
        await Expect(Row("Daily standup").Locator(".badge.priority")).ToHaveTextAsync(new Regex("Urgent"));
        await Expect(Row("Daily standup").Locator(".estimate")).ToContainTextAsync("15m");
    });

    await Step("edit dialog: no picker, but offers save as preset", async () =>
    {
        await Row("Daily standup").GetByRole(AriaRole.Button, new() { Name = "Edit" }).ClickAsync();
        await Expect(modal.Locator(".preset-picker")).ToHaveCountAsync(0);
        await Expect(modal.Locator(".btn-save-preset")).ToHaveCountAsync(1);
    });

    await Step("save as preset: captures the task's shape", async () =>
    {
        await modal.GetByLabel("Title").FillAsync("Ad-hoc chore");
        await modal.Locator(".btn-save-preset").ClickAsync();
        await Expect(modal).ToHaveCountAsync(0);
        await NavTo("Presets", "Presets");
        await Expect(PresetRow("Ad-hoc chore")).ToHaveCountAsync(1);
        await Expect(page.Locator(".preset-item")).ToHaveCountAsync(3);
    });

    await Step("save as preset: left the task itself unchanged", async () =>
    {
        // The button captures a shape for next time; it is not a second Save.
        await NavTo("Tasks", "Tasks");
        await Expect(Row("Daily standup")).ToHaveCountAsync(1);
        await Expect(Row("Ad-hoc chore")).ToHaveCountAsync(0);
    });

    Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
    return failures == 0 ? 0 : 1;
}

if (mode == "presetsverify")
{
    await Step("persisted: presets survived restart", async () =>
    {
        await NavTo("Presets", "Presets");
        await Expect(page.Locator(".preset-item")).ToHaveCountAsync(3);
        await Expect(page.Locator(".preset-item", new() { HasTextString = "Daily standup" })).ToHaveCountAsync(1);
        await Expect(page.Locator(".preset-item", new() { HasTextString = "Weekly review" })).ToHaveCountAsync(1);
    });

    await Step("persisted: edited estimate survived restart", async () =>
    {
        await Expect(page.Locator(".preset-item", new() { HasTextString = "Weekly review" }).Locator(".estimate"))
            .ToContainTextAsync("1h 30m");
    });

    await Step("persisted: the picker still offers them after restart", async () =>
    {
        await NavTo("Tasks", "Tasks");
        await OpenAddModal();
        await Expect(modal.Locator(".preset-option")).ToHaveCountAsync(3);
        await CancelModal();
    });

    Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
    return failures == 0 ? 0 : 1;
}

if (mode == "capture")
{
    // Produces the docs/ demo screenshots: every page in both themes, plus the
    // edit modal. Expects the app to have been launched over demo data seeded
    // by docs/capture.ps1 — this mode only drives and photographs, it never
    // writes. Unlike the test modes, any failure is fatal: a partial set of
    // screenshots must not silently overwrite a complete one.
    Directory.CreateDirectory(shotDir);

    async Task Capture(string theme)
    {
        // applyTheme with an explicit argument stamps data-bs-theme without
        // touching the persisted preference.
        await page.EvaluateAsync($"taskDashboard.applyTheme('{theme}')");
        await page.WaitForTimeoutAsync(400);

        await NavTo("Dashboard", "Dashboard");
        await page.WaitForTimeoutAsync(600);
        await Shot($"1-dashboard-{theme}.png");

        await NavTo("Tasks", "Tasks");
        await page.WaitForTimeoutAsync(400);
        await Shot($"2-tasks-{theme}.png");

        await Row("Prepare the demo slides")
            .GetByRole(AriaRole.Button, new() { Name = "Edit" }).ClickAsync();
        await Expect(modal).ToBeVisibleAsync();
        await page.WaitForTimeoutAsync(300);
        await Shot($"3-edit-modal-{theme}.png");
        await CancelModal();
        await Expect(modal).ToHaveCountAsync(0);

        await NavTo("Calendar", "Calendar");
        await page.WaitForTimeoutAsync(600);
        // Frame the hours around the now-line rather than a fixed noon, so a
        // capture at any hour keeps history, the marker and the plan in view.
        await page.EvaluateAsync(
            """
            () => {
                const s = document.querySelector('.cal-scroll');
                if (!s) return;
                const h = new Date().getHours();
                s.scrollTop = Math.max(0, Math.min((h - 5) * 48, s.scrollHeight));
            }
            """);
        await page.WaitForTimeoutAsync(400);
        await Shot($"4-calendar-{theme}.png");

        await NavTo("Blocked time", "Blocked time");
        await page.WaitForTimeoutAsync(400);
        await Shot($"5-blocked-time-{theme}.png");

        await NavTo("Notifications", "Notifications");
        await page.WaitForTimeoutAsync(400);
        await Shot($"6-notifications-{theme}.png");

        await NavTo("Presets", "Presets");
        await page.WaitForTimeoutAsync(400);
        await Shot($"7-presets-{theme}.png");

        await NavTo("Settings", "Settings");
        await page.WaitForTimeoutAsync(400);
        await Shot($"9-settings-{theme}.png");

        // The picker is the point of presets, and it only exists inside the
        // add dialog, so photograph it there rather than settling for the
        // management page alone.
        await NavTo("Tasks", "Tasks");
        await page.WaitForTimeoutAsync(300);
        await OpenAddModal();
        await Expect(modal).ToBeVisibleAsync();
        await modal.GetByLabel("Start from a preset").FillAsync("re");
        await page.WaitForTimeoutAsync(300);
        await Shot($"8-preset-picker-{theme}.png");
        await CancelModal();
        await Expect(modal).ToHaveCountAsync(0);
    }

    await Capture("light");
    await Capture("dark");
    await page.EvaluateAsync("taskDashboard.applyTheme('system')");
    Console.WriteLine($"CAPTURED 18 screenshots to {shotDir}");
    return 0;
}

if (mode == "theme")
{
    async Task<string> HtmlTheme() =>
        await page.EvaluateAsync<string>("document.documentElement.getAttribute('data-bs-theme') ?? ''");

    await Step("theme: an effective theme is applied on startup", async () =>
    {
        var t = await HtmlTheme();
        if (t is not ("light" or "dark")) throw new Exception($"data-bs-theme='{t}'");
    });

    await Step("theme: normalize preference to System", async () =>
    {
        for (var i = 0; i < 3; i++)
        {
            if ((await page.Locator(".theme-toggle").InnerTextAsync()).Contains("System")) return;
            await page.Locator(".theme-toggle").ClickAsync();
            await page.WaitForTimeoutAsync(250);
        }
        throw new Exception("could not cycle back to System");
    });

    await Step("theme: Light forces light", async () =>
    {
        await page.Locator(".theme-toggle").ClickAsync();
        await Expect(page.Locator(".theme-toggle")).ToContainTextAsync("Light");
        if (await HtmlTheme() != "light") throw new Exception($"attr={await HtmlTheme()}");
    });

    await Step("theme: Dark forces dark and recolors surfaces", async () =>
    {
        await page.Locator(".theme-toggle").ClickAsync();
        await Expect(page.Locator(".theme-toggle")).ToContainTextAsync("Dark");
        if (await HtmlTheme() != "dark") throw new Exception($"attr={await HtmlTheme()}");
        await NavTo("Dashboard", "Dashboard");
        var bg = await page.EvaluateAsync<string>(
            "getComputedStyle(document.querySelector('.tile')).backgroundColor");
        if (!bg.Contains("34, 38, 43")) throw new Exception($"tile background stayed {bg}"); // --surface dark = #22262b
    });
    await Shot("theme-01-dark-dashboard.png");

    await Step("theme: calendar renders in dark", async () =>
    {
        await NavTo("Calendar", "Calendar");
        await page.EvalOnSelectorAsync(".cal-scroll",
            "el => { el.scrollTop = Math.max(0, (new Date().getHours() - 1.5) * 48); }");
        var bg = await page.EvaluateAsync<string>(
            "getComputedStyle(document.querySelector('.cal-day.today')).backgroundColor");
        if (bg.Contains("247, 251, 255")) throw new Exception("today column still light-tinted");
    });
    await Shot("theme-02-dark-calendar.png");

    // Deliberately left on Dark: themeverify asserts it survives a restart.
    Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
    return failures == 0 ? 0 : 1;
}

if (mode == "themeverify")
{
    await Step("theme: dark preference survived restart", async () =>
    {
        var t = await page.EvaluateAsync<string>("document.documentElement.getAttribute('data-bs-theme') ?? ''");
        if (t != "dark") throw new Exception($"data-bs-theme='{t}' after restart");
        await Expect(page.Locator(".theme-toggle")).ToContainTextAsync("Dark");
    });

    await Step("theme: restored to System", async () =>
    {
        await page.Locator(".theme-toggle").ClickAsync();
        await Expect(page.Locator(".theme-toggle")).ToContainTextAsync("System");
        var t = await page.EvaluateAsync<string>("document.documentElement.getAttribute('data-bs-theme') ?? ''");
        if (t is not ("light" or "dark")) throw new Exception($"attr='{t}'");
    });

    Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
    return failures == 0 ? 0 : 1;
}

if (mode == "realism")
{
    static string Dt(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm");

    async Task SetBreak(string minutes)
    {
        await NavTo("Blocked time", "Blocked time");
        await page.GetByLabel("Break between tasks (minutes)").FillAsync(minutes);
    }

    await Step("prep: clear blocked periods, break at 15", async () =>
    {
        await NavTo("Blocked time", "Blocked time");
        while (await page.Locator(".blocked-item").CountAsync() > 0)
        {
            await page.Locator(".blocked-item").First
                .GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Delete") }).ClickAsync();
            await page.WaitForTimeoutAsync(200);
        }
        await page.GetByLabel("Break between tasks (minutes)").FillAsync("15");
    });

    await Step("prep: seed two 30-minute tasks", async () =>
    {
        await NavTo("Tasks", "Tasks");
        await ResetAllTasks();
        await AddTask("First job", null, null, "30");
        await AddTask("Second job", null, null, "30");
    });

    // A slot the visible week doesn't hold may be on next week's grid — a
    // late-evening run pushes the second task past midnight, and on Sundays
    // midnight is the week boundary. Same trap as the not-before fixture.
    async Task<(int Start, int End)> SlotTimesAnyWeek(string exactTitle)
    {
        var times = await SlotTimes(exactTitle);
        if (times is null)
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Next week ›" }).ClickAsync();
            await page.WaitForTimeoutAsync(300);
            times = await SlotTimes(exactTitle);
            await page.GetByRole(AriaRole.Button, new() { Name = "Today", Exact = true }).ClickAsync();
            await page.WaitForTimeoutAsync(300);
        }

        return times ?? throw new Exception($"{exactTitle} slot missing from this week and the next");
    }

    await Step("breaks: 15-minute gap between consecutive tasks", async () =>
    {
        await NavTo("Calendar", "Calendar");
        var first = await SlotTimesAnyWeek("First job");
        var second = await SlotTimesAnyWeek("Second job");
        var gap = GapMinutes(first.End, second.Start);
        if (gap < 15) throw new Exception($"gap was {gap} min, expected >= 15");
    });

    await Step("breaks: raising the setting to 30 widens the gap", async () =>
    {
        await SetBreak("30");
        await NavTo("Calendar", "Calendar");
        var first = await SlotTimesAnyWeek("First job");
        var second = await SlotTimesAnyWeek("Second job");
        var gap = GapMinutes(first.End, second.Start);
        if (gap < 30) throw new Exception($"gap was {gap} min after setting 30");
    });

    await Step("split: edit modal turns one task into two parts", async () =>
    {
        await NavTo("Tasks", "Tasks");
        await AddTask("Big job", null, null, "60");
        await Row("Big job").GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Edit") }).ClickAsync();
        await Expect(modal.Locator(".split-row")).ToHaveCountAsync(1);
        await modal.Locator(".btn-split").ClickAsync();
        await Expect(modal).ToHaveCountAsync(0);
        await Expect(Row("Big job (1/2)")).ToHaveCountAsync(1);
        await Expect(Row("Big job (2/2)")).ToHaveCountAsync(1);
        await Expect(Row("Big job (1/2)").Locator(".estimate")).ToContainTextAsync("30m");
        await Expect(Row("Big job (2/2)").Locator(".estimate")).ToContainTextAsync("30m");
    });

    await Step("not-before: a High task still waits for its earliest start", async () =>
    {
        await AddTask("Waiting job", null, "High", "30", Dt(DateTime.Today.AddDays(1).AddHours(12)));
        await NavTo("Calendar", "Calendar");

        async Task<string> Placement() => await page.EvaluateAsync<string>(
            """
            () => {
                const inToday = [...document.querySelectorAll('.cal-day.today .cal-slot')]
                    .some(e => e.innerText.includes('Waiting job'));
                const onWeek = [...document.querySelectorAll('.cal-slot')]
                    .some(e => e.innerText.includes('Waiting job'));
                return `${inToday},${onWeek}`;
            }
            """);

        var thisWeek = await Placement();
        if (thisWeek.StartsWith("true"))
        {
            throw new Exception($"planned on today despite a not-before of tomorrow: {thisWeek}");
        }

        // Tomorrow is on next week's grid whenever today is the last day of the
        // displayed week — every Sunday. Being clock-relative is not enough on
        // its own; the assertion has to follow the task across the week bound
        // or it fails one day in seven for a plan that is perfectly correct.
        if (thisWeek == "false,false")
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Next week ›" }).ClickAsync();
            // Let the re-render land before reading — with a fuller task list
            // the immediate evaluate raced the render and read the old grid.
            await page.WaitForTimeoutAsync(400);
            var nextWeek = await Placement();
            await page.GetByRole(AriaRole.Button, new() { Name = "Today", Exact = true }).ClickAsync();

            if (nextWeek != "false,true")
            {
                throw new Exception($"not on this week, and next week = {nextWeek}; expected false,true");
            }
        }
    });

    await Step("capacity: impossible deadline raises the banner", async () =>
    {
        await NavTo("Tasks", "Tasks");
        await AddTask("Crunch job", Dt(DateTime.Now.AddMinutes(30)), "Urgent", "120");
        await NavTo("Dashboard", "Dashboard");
        await Expect(page.Locator(".capacity-banner")).ToHaveCountAsync(1);
        await Expect(page.Locator(".capacity-banner")).ToContainTextAsync("can't finish before the deadline");
    });

    await Step("capacity: deleting the offender clears the banner", async () =>
    {
        await NavTo("Tasks", "Tasks");
        await Row("Crunch job").GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Delete") }).ClickAsync();
        await NavTo("Dashboard", "Dashboard");
        await Expect(page.Locator(".capacity-banner")).ToHaveCountAsync(0);
    });

    await Step("cleanup: break restored to 15", () => SetBreak("15"));
    await Shot("realism-01.png");

    Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
    return failures == 0 ? 0 : 1;
}

if (mode == "dashboard")
{
    await Step("prep: clear blocked periods for a deterministic plan", async () =>
    {
        await NavTo("Blocked time", "Blocked time");
        while (await page.Locator(".blocked-item").CountAsync() > 0)
        {
            await page.Locator(".blocked-item").First
                .GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Delete") }).ClickAsync();
            await page.WaitForTimeoutAsync(200);
        }
    });

    await Step("prep: seed three planned tasks and one completed", async () =>
    {
        await NavTo("Tasks", "Tasks");
        await ResetAllTasks();
        // High priority pins this one to the front of the plan regardless of
        // estimate-based tie-breaks.
        await AddTask("Current thing", null, "High", "60");
        await AddTask("Next thing", null, null, "30");
        await AddTask("Third thing", null, null, "30");
        await AddTask("Already done", null, null, null);
        await Row("Already done").Locator("input[type=checkbox]").ClickAsync();
        await Expect(page.Locator(".task-item.done")).ToHaveCountAsync(1);
    });

    await Step("dashboard: reachable via nav link", async () =>
    {
        await page.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true }).ClickAsync();
        await Expect(page.Locator("h1")).ToHaveTextAsync("Dashboard");
    });

    await Step("tiles: counts and estimate total", async () =>
    {
        await Expect(page.Locator(".tile").First.Locator(".tile-value")).ToHaveTextAsync("3");
        await Expect(page.Locator(".tiles")).ToContainTextAsync("2h");       // 60+30+30
        await Expect(page.Locator(".tile").Nth(3).Locator(".tile-value")).ToHaveTextAsync("1"); // done today
    });

    await Step("now: the highest-priority task is what you should be doing", () =>
        Expect(page.Locator(".panel-now")).ToContainTextAsync("Current thing"));

    await Step("up next: remaining tasks in plan order", async () =>
    {
        await Expect(page.Locator(".panel-next li").Nth(0)).ToContainTextAsync("Next thing");
        await Expect(page.Locator(".panel-next li").Nth(1)).ToContainTextAsync("Third thing");
    });

    await Step("done: completed task listed with its time", () =>
        Expect(page.Locator(".panel-done")).ToContainTextAsync("Already done"));

    await Shot("dash-01.png");

    Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
    return failures == 0 ? 0 : 1;
}

if (mode == "tracking")
{
    await Step("prep: clear blocked periods", async () =>
    {
        await NavTo("Blocked time", "Blocked time");
        while (await page.Locator(".blocked-item").CountAsync() > 0)
        {
            await page.Locator(".blocked-item").First
                .GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Delete") }).ClickAsync();
            await page.WaitForTimeoutAsync(200);
        }
    });

    await Step("prep: seed two tasks", async () =>
    {
        await NavTo("Tasks", "Tasks");
        await ResetAllTasks();
        await AddTask("Focus task", null, "High", "60");
        await AddTask("Other task", null, null, "30");
    });

    await Step("row start: marks the task in progress", async () =>
    {
        await Row("Focus task").GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Start") }).ClickAsync();
        await Expect(page.Locator(".task-item.started")).ToHaveCountAsync(1);
        await Expect(Row("Focus task").Locator(".badge.inprogress")).ToHaveCountAsync(1);
        await Expect(Row("Focus task").GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Stop") })).ToHaveCountAsync(1);
    });

    await Step("dashboard: Now shows the started task with elapsed time and actions", async () =>
    {
        await NavTo("Dashboard", "Dashboard");
        await Expect(page.Locator(".panel-now")).ToContainTextAsync("Focus task");
        await Expect(page.Locator(".panel-now")).ToContainTextAsync("m in");
        await Expect(page.Locator(".panel-now .btn-stop")).ToHaveCountAsync(1);
        await Expect(page.Locator(".panel-now .btn-done-now")).ToHaveCountAsync(1);
    });
    await Shot("trk-01-now-started.png");

    await Step("single-active: starting another task switches", async () =>
    {
        await NavTo("Tasks", "Tasks");
        await Row("Other task").GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Start") }).ClickAsync();
        await Expect(page.Locator(".task-item.started")).ToHaveCountAsync(1);
        await Expect(Row("Other task").Locator(".badge.inprogress")).ToHaveCountAsync(1);
        await Expect(Row("Focus task").Locator(".badge.inprogress")).ToHaveCountAsync(0);
    });

    await Step("row stop: abandons the start", async () =>
    {
        await Row("Other task").GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Stop") }).ClickAsync();
        await Expect(page.Locator(".task-item.started")).ToHaveCountAsync(0);
    });

    await Step("dashboard: planned slot offers Start, and it works", async () =>
    {
        await NavTo("Dashboard", "Dashboard");
        await Expect(page.Locator(".panel-now .btn-start")).ToHaveCountAsync(1);
        await Expect(page.Locator(".panel-now")).ToContainTextAsync("Focus task"); // High goes first in the plan
        await page.Locator(".panel-now .btn-start").ClickAsync();
        await Expect(page.Locator(".panel-now .btn-stop")).ToHaveCountAsync(1);
    });

    await Step("dashboard: Done completes the started task into history", async () =>
    {
        await page.Locator(".panel-now .btn-done-now").ClickAsync();
        await Expect(page.Locator(".panel-done")).ToContainTextAsync("Focus task");
        await Expect(page.Locator(".tile").Nth(3).Locator(".tile-value")).ToHaveTextAsync("1");
        await Expect(page.Locator(".panel-now")).Not.ToContainTextAsync("Focus task");
    });
    await Shot("trk-02-completed.png");

    await Step("calibration: accuracy tile present in learning state", async () =>
    {
        // Test completions span seconds, which the outlier filter rejects, so
        // after a reset the tile must be calibrating at 0/5.
        await Expect(page.Locator(".tile-accuracy")).ToHaveCountAsync(1);
        await Expect(page.Locator(".tile-accuracy")).ToContainTextAsync("0/5");
        await Expect(page.Locator(".tile-accuracy")).ToContainTextAsync("calibrating");
    });

    await Step("calendar: done block present with real span", async () =>
    {
        await NavTo("Calendar", "Calendar");
        await Expect(page.Locator(".cal-done").First).ToBeVisibleAsync();
    });

    Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
    return failures == 0 ? 0 : 1;
}

if (mode == "calendar")
{
    static string Dt(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm");

    await Step("prep: clear blocked periods so the plan starts at now", async () =>
    {
        await NavTo("Blocked time", "Blocked time");
        while (await page.Locator(".blocked-item").CountAsync() > 0)
        {
            await page.Locator(".blocked-item").First
                .GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Delete") }).ClickAsync();
            await page.WaitForTimeoutAsync(200);
        }
        await NavTo("Tasks", "Tasks");
    });

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
        // Plan order: Crunch (soonest deadline) is today's first slot.
        await Expect(page.Locator(".cal-day.today .cal-slot").First).ToContainTextAsync("Crunch task");

        // Write spec is planned after Crunch's two hours plus a break, so late
        // in the day it lands after midnight — and that is next week's grid
        // whenever today is the last day of the week. Follow it across rather
        // than calling a correct plan a failure; the same trap caught the
        // not-before fixture in realism.
        async Task<int> SpecSlots() =>
            await page.Locator(".cal-slot", new() { HasTextString = "Write spec" }).CountAsync();

        if (await SpecSlots() == 0)
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Next week ›" }).ClickAsync();
            await page.WaitForTimeoutAsync(400);
            var onNextWeek = await SpecSlots();
            await page.GetByRole(AriaRole.Button, new() { Name = "Today", Exact = true }).ClickAsync();
            await page.WaitForTimeoutAsync(400);

            if (onNextWeek == 0)
            {
                throw new Exception("Write spec is planned on neither this week nor the next");
            }
        }
    });

    await Step("calendar: impossible deadline is flagged", () =>
        Expect(page.Locator(".cal-slot.missed").First).ToContainTextAsync("Crunch task"));

    await Step("calendar: completed task appears as a done block", () =>
        Expect(page.Locator(".cal-done").First).ToContainTextAsync("Email follow-up"));

    await Step("calendar: deadline marker present", async () =>
    {
        // A deadline of now+30min crosses midnight late in the evening, and on
        // Sundays midnight is also the week boundary — the marker then draws on
        // next week's grid. Follow it there before declaring it missing.
        if (await page.Locator(".cal-deadline").CountAsync() == 0)
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Next week ›" }).ClickAsync();
            await page.WaitForTimeoutAsync(400);
            var onNextWeek = await page.Locator(".cal-deadline").CountAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Today", Exact = true }).ClickAsync();
            await page.WaitForTimeoutAsync(400);

            if (onNextWeek == 0)
            {
                throw new Exception("no deadline marker on this week or the next");
            }

            return;
        }

        await Expect(page.Locator(".cal-deadline").First).ToBeVisibleAsync();
    });

    await Step("calendar: now line drawn on today", () =>
        Expect(page.Locator(".cal-now-line")).ToHaveCountAsync(1));

    await Step("calendar: day and 3-day views narrow the span", async () =>
    {
        await page.Locator(".cal-view-day").ClickAsync();
        await Expect(page.Locator(".cal-day")).ToHaveCountAsync(1);
        await Expect(page.Locator(".cal-day.today")).ToHaveCountAsync(1);

        await page.Locator(".cal-view-3day").ClickAsync();
        await Expect(page.Locator(".cal-day")).ToHaveCountAsync(3);
        // Narrow views anchor on today, so its column leads the span.
        await Expect(page.Locator(".cal-day").First).ToHaveClassAsync(new Regex("today"));

        await page.Locator(".cal-view-week").ClickAsync();
        await Expect(page.Locator(".cal-day")).ToHaveCountAsync(7);
        await Expect(page.Locator(".cal-day.today")).ToHaveCountAsync(1);
    });

    await Step("settings: hides the zoom slider and brings it back", async () =>
    {
        await Expect(page.Locator(".cal-zoom-slider")).ToHaveCountAsync(1);

        await NavTo("Settings", "Settings");
        await page.GetByLabel("Show the calendar zoom slider").ClickAsync();
        await NavTo("Calendar", "Calendar");
        await Expect(page.Locator(".cal-zoom-slider")).ToHaveCountAsync(0);

        await NavTo("Settings", "Settings");
        await page.GetByLabel("Show the calendar zoom slider").ClickAsync();
        await NavTo("Calendar", "Calendar");
        await Expect(page.Locator(".cal-zoom-slider")).ToHaveCountAsync(1);
    });

    await Step("calendar: zoom slider rescales the grid", async () =>
    {
        async Task<double> GridHeight() =>
            await page.EvalOnSelectorAsync<double>(".cal-grid", "el => el.getBoundingClientRect().height");

        async Task SetZoom(string px) =>
            await page.EvalOnSelectorAsync(".cal-zoom-slider",
                $"el => {{ el.value = '{px}'; el.dispatchEvent(new Event('input', {{ bubbles: true }})); }}");

        var before = await GridHeight();
        await SetZoom("96");
        await page.WaitForTimeoutAsync(300);
        var after = await GridHeight();

        // Restore the default before asserting: later steps (and other modes)
        // do pixel maths that assumes the 48px scale.
        await SetZoom("48");
        await page.WaitForTimeoutAsync(300);

        if (Math.Abs(before - 24 * 48) > 2 || Math.Abs(after - 24 * 96) > 2)
        {
            throw new Exception($"grid height before/after zoom = {before}/{after}; expected 1152/2304");
        }
    });

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

// Not-before is the one field the edit dialog used to drop, and it fails
// quietly: the planner simply declines to schedule the task, with nothing on
// the row to say why. Both consequences are asserted below.
var floorText = DateTime.Today.AddDays(1).AddHours(9).ToString("yyyy-MM-ddTHH:mm");

async Task OpenEdit(string title) =>
    await Row(title).GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Edit") }).ClickAsync();

await Step("not-before: survives an edit round-trip", async () =>
{
    await AddTask("Waits till nine", null, "Normal", "20", floorText);

    // Clear the shared form before opening the edit dialog. Without this the
    // value typed during the add is still sitting in the form, so the field
    // shows the right thing for the wrong reason and the assertion passes even
    // when the edit dialog loads nothing at all. Opening Add resets it.
    await OpenAddModal();
    await CancelModal();

    await OpenEdit("Waits till nine");
    await Expect(modal.GetByLabel("Not before")).ToHaveValueAsync(floorText);
    await SaveModal();

    // Saving without touching the field must not clear it.
    await OpenEdit("Waits till nine");
    await Expect(modal.GetByLabel("Not before")).ToHaveValueAsync(floorText);
    await CancelModal();
});

await Step("not-before: does not leak onto the next task edited", async () =>
{
    // Order is the whole test. The add dialog resets the shared form, so the
    // stale value has to be planted by an add that sets a floor, immediately
    // before editing a task that has none.
    await AddTask("Anytime task", null, "Normal", "20");
    await AddTask("Second waiter", null, "Normal", "20", floorText);

    await OpenEdit("Anytime task");
    await Expect(modal.GetByLabel("Not before")).ToHaveValueAsync("");
    await SaveModal();

    await OpenEdit("Anytime task");
    await Expect(modal.GetByLabel("Not before")).ToHaveValueAsync("");
    await CancelModal();
});

await Step("cleanup: remove the not-before fixtures", async () =>
{
    // Titles chosen so none contains another: Row matches by substring, and a
    // fixture that also matches its neighbour deletes the wrong row.
    foreach (var title in new[] { "Waits till nine", "Anytime task", "Second waiter" })
    {
        await Row(title).GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Delete") }).ClickAsync();
        await page.WaitForTimeoutAsync(200);
    }

    // The end state verify asserts against must be exactly what it was.
    await Expect(page.Locator(".task-item")).ToHaveCountAsync(1);
    await Expect(Row("Buy milk")).ToHaveCountAsync(1);
});

await Step("footer: totals only the remaining estimate", () =>
    Expect(footer).ToContainTextAsync("1 task left"));

await Step("nav: sidebar hides and returns", async () =>
{
    await page.GetByRole(AriaRole.Button, new() { Name = "Hide navigation", Exact = true }).ClickAsync();
    await Expect(page.Locator(".sidebar")).ToBeHiddenAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Show navigation", Exact = true }).ClickAsync();
    await Expect(page.Locator(".sidebar")).ToBeVisibleAsync();
});

await Step("nav: icons setting collapses to a rail instead", async () =>
{
    await NavTo("Settings", "Settings");
    await page.GetByLabel("Collapse the sidebar to icons").ClickAsync();
    await NavTo("Tasks", "Tasks");

    await page.GetByRole(AriaRole.Button, new() { Name = "Hide navigation", Exact = true }).ClickAsync();
    // The rail keeps the sidebar visible but narrow; the label text collapses
    // with the font, so the link's accessible name survives for the nav.
    await Expect(page.Locator(".page.nav-icons")).ToHaveCountAsync(1);
    await Expect(page.Locator(".sidebar")).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Show navigation", Exact = true }).ClickAsync();
    await Expect(page.Locator(".page.nav-icons")).ToHaveCountAsync(0);

    await NavTo("Settings", "Settings");
    await page.GetByLabel("Collapse the sidebar to icons").ClickAsync();
    await NavTo("Tasks", "Tasks");
});
await Shot("ui-04-final.png");

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
