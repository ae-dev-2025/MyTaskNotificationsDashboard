using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

// End-to-end checks for the Task Dashboard running on ANDROID.
//
// Android WebView exposes only page-level CDP (no browser contexts), which
// Playwright cannot attach to — so this is a minimal raw CDP client driving
// the app through Runtime.evaluate and capturing Page.captureScreenshot.
// Interactions are dispatched via DOM APIs (click(), value + change events),
// the same way the Blazor bindings receive real input.
//
// Setup:
//   adb shell pidof com.aedev2025.taskdashboard              -> <pid>
//   adb forward tcp:9333 localabstract:webview_devtools_remote_<pid>
//
// Usage: dotnet run --project tools/AndroidTest -- <suite|verify> [screenshotDir]
//   suite  — resets data, exercises tasks/calendar/blocked/dashboard; leaves state
//   verify — after force-stop + relaunch: asserts that state persisted

var mode = args.Length > 0 ? args[0] : "suite";
var shotDir = args.Length > 1 ? args[1] : ".";
var failures = 0;

using var cdp = new Cdp("http://localhost:9333");
var screenshotsBroken = false;

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

// ---- tiny DOM helpers ----

async Task Click(string selector) =>
    await cdp.EvalAsync($"document.querySelector({JsonSerializer.Serialize(selector)}).click()");

async Task SetValue(string selector, string value) =>
    await cdp.EvalAsync(
        $"(() => {{ const el = document.querySelector({JsonSerializer.Serialize(selector)});" +
        $" el.value = {JsonSerializer.Serialize(value)};" +
        " el.dispatchEvent(new Event('change', { bubbles: true })); })()");

// Blazor's @bind listens for 'change', but @oninput handlers — the preset
// search box — only ever see 'input'. Dispatch that one when the value must
// reach an @oninput binding.
async Task TypeValue(string selector, string value) =>
    await cdp.EvalAsync(
        $"(() => {{ const el = document.querySelector({JsonSerializer.Serialize(selector)});" +
        $" el.value = {JsonSerializer.Serialize(value)};" +
        " el.dispatchEvent(new Event('input', { bubbles: true })); })()");

async Task WaitFor(string expression, string what, int timeoutMs = 8000)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeoutMs)
    {
        if (await cdp.EvalAsync($"!!({expression})") is JsonElement { ValueKind: JsonValueKind.True })
        {
            return;
        }

        await Task.Delay(200);
    }

    throw new Exception($"timed out waiting for {what}");
}

async Task<int> Count(string selector) =>
    (await cdp.EvalAsync($"document.querySelectorAll({JsonSerializer.Serialize(selector)}).length")).GetInt32();

async Task<string> Text(string selector) =>
    (await cdp.EvalAsync(
        $"(document.querySelector({JsonSerializer.Serialize(selector)})?.innerText ?? '').replace(/\\s+/g, ' ').trim()"))
    .GetString() ?? "";

async Task NavTo(string href, string heading)
{
    await Click($"a[href=\"{href}\"]");
    await WaitFor($"document.querySelector('h1')?.innerText === {JsonSerializer.Serialize(heading)}", $"h1 {heading}");
}

async Task AddTask(string title, string? priority, string? estimate)
{
    await Click(".task-toolbar button");
    await WaitFor("document.querySelector('.modal-panel')", "modal open");
    await SetValue("#task-title", title);
    if (priority is not null) await SetValue("#task-priority", priority);
    if (estimate is not null) await SetValue("#task-estimate", estimate);
    await Click(".modal-panel button[type=submit]");
    await WaitFor("!document.querySelector('.modal-panel')", "modal closed");
}

async Task Shot(string name)
{
    // Screenshots are evidence, not assertions: some real-device WebViews
    // never answer Page.captureScreenshot. Skip rather than fail the run; use
    // adb screencap externally when a picture matters.
    if (screenshotsBroken)
    {
        return;
    }

    try
    {
        // Create the directory here rather than trusting the caller: a missing
        // one used to surface as "unavailable on this WebView", which blamed
        // the device for a path that simply did not exist and made a working
        // WebView look broken.
        Directory.CreateDirectory(shotDir);
        var png = await cdp.ScreenshotAsync();
        await File.WriteAllBytesAsync(Path.Combine(shotDir, name), png);
    }
    catch (Exception e)
    {
        screenshotsBroken = true;
        Console.WriteLine($"note  screenshots unavailable, skipping the rest ({e.Message.Split('\n')[0].Trim()})");
    }
}

if (mode == "probe")
{
    Console.WriteLine($"url:        {(await cdp.EvalAsync("location.href")).GetString()}");
    Console.WriteLine($"readyState: {(await cdp.EvalAsync("document.readyState")).GetString()}");
    Console.WriteLine($"title:      {(await cdp.EvalAsync("document.title")).GetString()}");
    Console.WriteLine($"h1:         {(await cdp.EvalAsync("document.querySelector('h1')?.innerText ?? '(none)'")).GetString()}");
    Console.WriteLine($"#app html:  {(await cdp.EvalAsync("(document.getElementById('app')?.innerHTML ?? '(no #app)').slice(0, 300)")).GetString()}");
    Console.WriteLine($"error ui:   {(await cdp.EvalAsync("getComputedStyle(document.getElementById('blazor-error-ui') ?? document.body).display")).GetString()}");
    await Shot("droid-probe.png");
    return 0;
}

if (mode == "verify")
{
    await Step("persisted: tasks survived force-stop and relaunch", async () =>
    {
        await WaitFor("document.querySelector('h1')", "app rendered");
        await NavTo("tasks", "Tasks");
        // The suite leaves three tasks: two completed (Done on droid, and
        // Android task via the dashboard Done button) plus Second task running.
        if (await Count(".task-item") != 3) throw new Exception($"expected 3 tasks, got {await Count(".task-item")}");
        if (await Count(".task-item.done") != 2) throw new Exception("expected both completed tasks to survive as done");
        await WaitFor("[...document.querySelectorAll('.task-title')].some(e => e.innerText.includes('Android task'))", "Android task row");
    });

    await Step("persisted: in-progress state survived relaunch", async () =>
    {
        if (await Count(".task-item.started") != 1) throw new Exception("expected one in-progress row");
        await WaitFor("[...document.querySelectorAll('.task-item.started')].some(e => e.innerText.includes('Second task'))", "Second task still running");
    });

    await Step("persisted: blocked period survived relaunch", async () =>
    {
        await NavTo("blocked-time", "Blocked time");
        if (await Count(".blocked-item") != 1) throw new Exception($"expected 1 blocked period, got {await Count(".blocked-item")}");
        var text = await Text(".blocked-item");
        if (!text.Contains("Sleep")) throw new Exception($"expected Sleep, got: {text}");
    });

    await Shot("droid-9-verify.png");
    Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
    return failures == 0 ? 0 : 1;
}

if (mode == "zoomshot")
{
    // Not a test: photographs the calendar zoomed in with the nav hidden, for
    // eyeballing readability work on a real device. Leaves the app as found.
    await WaitFor("document.querySelector('h1')", "app rendered");
    await Click("a[href=\"calendar\"]");
    await WaitFor("document.querySelector('.cal-grid')", "calendar rendered");
    await Click(".nav-collapse");
    await WaitFor("getComputedStyle(document.querySelector('.sidebar')).display === 'none'", "nav hidden");
    await Click(".cal-view-day");
    await WaitFor("document.querySelectorAll('.cal-day').length === 1", "day view");
    // Zoom first, then aim: placing the viewport at the final scale keeps this
    // idempotent — a previous run may have left the calendar already zoomed,
    // in which case the slider event is a no-op and no scroll correction runs.
    await cdp.EvalAsync(
        "(() => { const s = document.querySelector('.cal-zoom-slider');" +
        " s.value = '96'; s.dispatchEvent(new Event('input', { bubbles: true })); })()");
    await WaitFor("Math.abs(document.querySelector('.cal-grid').getBoundingClientRect().height - 24 * 96) < 3", "zoomed");
    await Task.Delay(300);
    await cdp.EvalAsync(
        "(() => { const sc = document.querySelector('.cal-scroll');" +
        " sc.scrollTop = (new Date().getHours() + 0.5) * 96 - sc.clientHeight / 2; })()");
    await Task.Delay(400);
    await Shot("droid-zoom.png");
    await Click(".nav-collapse");
    Console.WriteLine("zoomshot captured");
    return 0;
}

// ---- suite ----

await Step("app: dashboard is the root page", async () =>
{
    await WaitFor("document.querySelector('h1')", "app rendered");
    // The app may have been left on any page by a previous run or a human —
    // go home first, then assert.
    await Click("a[href=\"\"]");
    await WaitFor("document.querySelector('h1')?.innerText === 'Dashboard'", "dashboard heading");
});

await Step("reset: clear blocked periods", async () =>
{
    await NavTo("blocked-time", "Blocked time");
    while (await Count(".blocked-item") > 0)
    {
        await Click(".blocked-item .btn-outline-danger");
        await Task.Delay(300);
    }
});

await Step("reset: clear tasks", async () =>
{
    await NavTo("tasks", "Tasks");
    while (await Count(".task-item") > 0)
    {
        await Click(".task-item .btn-outline-danger");
        await Task.Delay(300);
    }
    await WaitFor("document.querySelector('.task-empty')", "empty state");
});

await Step("add: task via the modal with priority and estimate", async () =>
{
    await AddTask("Android task", "High", "60");
    await WaitFor("[...document.querySelectorAll('.task-title')].some(e => e.innerText.includes('Android task'))", "row appears");
    var row = await Text(".task-item");
    if (!row.Contains("High") || !row.Contains("1h")) throw new Exception($"row missing badge/estimate: {row}");
});

await Step("add: second task", async () =>
{
    await AddTask("Second task", null, "30");
    if (await Count(".task-item") != 2) throw new Exception("expected 2 rows");
});

await Step("validation: empty title keeps modal open", async () =>
{
    await Click(".task-toolbar button");
    await WaitFor("document.querySelector('.modal-panel')", "modal open");
    await Click(".modal-panel button[type=submit]");
    await Task.Delay(400);
    await WaitFor("document.querySelector('.modal-panel .validation-errors li')", "validation message");
    await Click(".modal-actions button[type=button]");
    await WaitFor("!document.querySelector('.modal-panel')", "modal closed");
});

await Step("toggle: third task added and completed", async () =>
{
    await AddTask("Done on droid", null, null);
    await cdp.EvalAsync(
        "[...document.querySelectorAll('.task-item')].find(r => r.innerText.includes('Done on droid'))" +
        ".querySelector('input[type=checkbox]').click()");
    await WaitFor("document.querySelectorAll('.task-item.done').length === 1", "done row");
});
await Shot("droid-1-tasks.png");

await Step("presets: create one on the device", async () =>
{
    await NavTo("presets", "Presets");
    while (await Count(".preset-item") > 0)
    {
        await Click(".preset-item .btn-outline-danger");
        await Task.Delay(200);
    }

    await Click(".preset-toolbar button");
    await WaitFor("document.querySelector('.modal-panel')", "modal open");
    await SetValue("#preset-title", "Droid standup");
    await SetValue("#preset-priority", "Urgent");
    await SetValue("#preset-estimate", "25");
    await Click(".modal-panel button[type=submit]");
    await WaitFor("!document.querySelector('.modal-panel')", "modal closed");
    if (await Count(".preset-item") != 1) throw new Exception("expected 1 preset");
});

await Step("presets: the picker fills the add dialog", async () =>
{
    await NavTo("tasks", "Tasks");
    await Click(".task-toolbar button");
    await WaitFor("document.querySelector('.preset-picker')", "picker present");

    await TypeValue("#preset-search", "droid");
    await WaitFor("document.querySelectorAll('.preset-option').length === 1", "one match");

    await Click(".preset-option");
    await WaitFor("document.querySelector('#task-title').value === 'Droid standup'", "title filled");
    await WaitFor("document.querySelector('#task-estimate').value === '25'", "estimate filled");

    // Cancel: this step is about the picker, not about adding another task.
    await Click(".modal-actions button[type=button]");
    await WaitFor("!document.querySelector('.modal-panel')", "modal closed");
});

await Step("nav: sidebar hides and returns", async () =>
{
    await Click(".nav-collapse");
    await WaitFor("getComputedStyle(document.querySelector('.sidebar')).display === 'none'", "sidebar hidden");
    await Click(".nav-collapse");
    await WaitFor("getComputedStyle(document.querySelector('.sidebar')).display !== 'none'", "sidebar visible");
});

await Step("quick-add: a typed line fills the other fields", async () =>
{
    await NavTo("tasks", "Tasks");
    await Click(".task-toolbar button");
    await WaitFor("document.querySelector('.modal-panel')", "modal open");

    // The title box parses on input, not change, so dispatch the event Blazor
    // actually listens for here.
    await TypeValue("#task-title", "Droid quickadd by tomorrow 17:00 !urgent 25m");
    await WaitFor("document.querySelector('#task-priority').value === 'Urgent'", "priority filled");
    await WaitFor("document.querySelector('#task-estimate').value === '25'", "estimate filled");
    await WaitFor("document.querySelector('.quick-add-preview')", "preview shown");

    await Click(".modal-panel button[type=submit]");
    await WaitFor("!document.querySelector('.modal-panel')", "modal closed");
    await WaitFor(
        "[...document.querySelectorAll('.task-title')].some(e => e.innerText.trim() === 'Droid quickadd')",
        "task stored with the stripped title");

    // Remove it again: the verify mode asserts an exact task count, and this
    // step is about the wiring, not about leaving another row behind.
    await cdp.EvalAsync(
        "[...document.querySelectorAll('.task-item')]" +
        ".find(r => r.innerText.includes('Droid quickadd'))" +
        ".querySelector('.btn-outline-danger').click()");
    await WaitFor(
        "![...document.querySelectorAll('.task-title')].some(e => e.innerText.includes('Droid quickadd'))",
        "quick-add task removed");
});

await Step("calendar: zoom slider rescales the grid", async () =>
{
    await NavTo("calendar", "Calendar");
    await WaitFor("document.querySelector('.cal-grid')?.getBoundingClientRect().height > 1100", "grid at default scale");
    await cdp.EvalAsync(
        "(() => { const s = document.querySelector('.cal-zoom-slider');" +
        " s.value = '96'; s.dispatchEvent(new Event('input', { bubbles: true })); })()");
    await WaitFor("Math.abs(document.querySelector('.cal-grid').getBoundingClientRect().height - 24 * 96) < 3", "grid rescaled to 96px hours");
    await cdp.EvalAsync(
        "(() => { const s = document.querySelector('.cal-zoom-slider');" +
        " s.value = '48'; s.dispatchEvent(new Event('input', { bubbles: true })); })()");
    await WaitFor("Math.abs(document.querySelector('.cal-grid').getBoundingClientRect().height - 24 * 48) < 3", "grid back at default");
});

await Step("calendar: day and 3-day views narrow the span", async () =>
{
    await Click(".cal-view-day");
    await WaitFor("document.querySelectorAll('.cal-day').length === 1", "one day column");
    await Click(".cal-view-3day");
    await WaitFor("document.querySelectorAll('.cal-day').length === 3", "three day columns");
    await Click(".cal-view-week");
    await WaitFor("document.querySelectorAll('.cal-day').length === 7", "week restored");
});

await Step("settings: icons rail replaces full hide when enabled", async () =>
{
    await NavTo("settings", "Settings");
    await cdp.EvalAsync("[...document.querySelectorAll('.settings-toggle input')][1].click()");
    await Task.Delay(300);
    await Click(".nav-collapse");
    await WaitFor("document.querySelector('.page.nav-icons')", "icon rail active");
    await WaitFor("getComputedStyle(document.querySelector('.sidebar')).display !== 'none'", "sidebar still visible");
    await Click(".nav-collapse");
    await WaitFor("!document.querySelector('.page.nav-icons')", "expanded again");
    await cdp.EvalAsync("[...document.querySelectorAll('.settings-toggle input')][1].click()");
    await Task.Delay(300);
    await NavTo("tasks", "Tasks");
});

await Step("settings: zoom slider visibility round-trips", async () =>
{
    await NavTo("settings", "Settings");
    await Click(".settings-toggle input");
    await NavTo("calendar", "Calendar");
    await WaitFor("!document.querySelector('.cal-zoom-slider')", "slider hidden");
    await NavTo("settings", "Settings");
    await Click(".settings-toggle input");
    await NavTo("calendar", "Calendar");
    await WaitFor("document.querySelector('.cal-zoom-slider')", "slider shown");
});

await Step("blocked: add recurring Sleep period clear of now", async () =>
{
    // Clock-relative window (now+3h .. now+11h) so the fixture can never
    // swallow the current time, whatever hour the suite runs at.
    await NavTo("blocked-time", "Blocked time");
    await Click(".blocked-toolbar button");
    await WaitFor("document.querySelector('.modal-panel')", "modal open");
    await SetValue("#blocked-label", "Sleep");
    await SetValue("#blocked-start-time", DateTime.Now.AddHours(3).ToString("HH:mm"));
    await SetValue("#blocked-end-time", DateTime.Now.AddHours(11).ToString("HH:mm"));
    await Click(".modal-panel button[type=submit]");
    await WaitFor("!document.querySelector('.modal-panel')", "modal closed");
    if (await Count(".blocked-item") != 1) throw new Exception("expected 1 blocked period");
});

await Step("calendar: slots, now line, and sleep shading render", async () =>
{
    await NavTo("calendar", "Calendar");
    await WaitFor("document.querySelectorAll('.cal-slot').length >= 1", "planned slots");
    await WaitFor("document.querySelectorAll('.cal-now-line').length === 1", "now line");
    var sleepBlocks = await Count(".cal-blocked");
    if (sleepBlocks < 7) throw new Exception($"expected >=7 sleep blocks, got {sleepBlocks}");
    await cdp.EvalAsync("document.querySelector('.cal-scroll').scrollTop = Math.max(0, (new Date().getHours() - 1.5) * 48)");
});
await Shot("droid-2-calendar.png");

await Step("dashboard: Now shows the high-priority task, Done shows the completion", async () =>
{
    await NavTo("", "Dashboard");
    var now = await Text(".panel-now");
    if (!now.Contains("Android task")) throw new Exception($"Now panel: {now}");
    var done = await Text(".panel-done");
    if (!done.Contains("Done on droid")) throw new Exception($"Done panel: {done}");
    var nextPanel = await Text(".panel-next");
    if (!nextPanel.Contains("Second task")) throw new Exception($"Next panel: {nextPanel}");
});
await Shot("droid-3-dashboard.png");

await Step("tracking: start from the dashboard Now panel", async () =>
{
    await Click(".panel-now .btn-start");
    await WaitFor("document.querySelector('.panel-now .btn-stop')", "started state");
    var now = await Text(".panel-now");
    if (!now.Contains("Android task") || !now.Contains("m in")) throw new Exception($"Now panel: {now}");
});

await Step("tracking: complete from the dashboard", async () =>
{
    await Click(".panel-now .btn-done-now");
    await WaitFor("(document.querySelector('.panel-done')?.innerText ?? '').includes('Android task')", "completion in Done panel");
});

await Step("tracking: start the next planned task and leave it running", async () =>
{
    await WaitFor("document.querySelector('.panel-now .btn-start')", "next planned slot");
    var next = await Text(".panel-now");
    if (!next.Contains("Second task")) throw new Exception($"Now panel: {next}");
    await Click(".panel-now .btn-start");
    await WaitFor("document.querySelector('.panel-now .btn-stop')", "started state");
});

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;

/// <summary>Minimal CDP client over the page-level websocket. Reconnects on
/// demand: a timed-out receive aborts a ClientWebSocket permanently, and the
/// app's state lives in the page, not the connection, so a fresh socket is
/// always safe.</summary>
internal sealed class Cdp : IDisposable
{
    private readonly string httpEndpoint;
    private ClientWebSocket? socket;
    private int nextId;

    public Cdp(string httpEndpoint) => this.httpEndpoint = httpEndpoint;

    private async Task<ClientWebSocket> EnsureConnectedAsync()
    {
        if (socket is { State: WebSocketState.Open })
        {
            return socket;
        }

        socket?.Dispose();
        pageEnabled = false;

        using var http = new HttpClient();
        var targets = JsonSerializer.Deserialize<JsonElement>(await http.GetStringAsync($"{httpEndpoint}/json"));
        var wsUrl = targets.EnumerateArray()
            .First(t => t.GetProperty("type").GetString() == "page")
            .GetProperty("webSocketDebuggerUrl").GetString()!;

        socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
        return socket;
    }

    public async Task<JsonElement> EvalAsync(string expression)
    {
        var result = await SendAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true,
            awaitPromise = true,
        });

        if (result.TryGetProperty("exceptionDetails", out var ex))
        {
            throw new Exception($"JS error: {ex.GetProperty("text").GetString()} in: {expression[..Math.Min(80, expression.Length)]}");
        }

        return result.GetProperty("result").TryGetProperty("value", out var value) ? value : default;
    }

    private bool pageEnabled;

    public async Task<byte[]> ScreenshotAsync()
    {
        if (!pageEnabled)
        {
            await SendAsync("Page.enable", new { });
            pageEnabled = true;
        }

        var result = await SendAsync("Page.captureScreenshot", new { });
        return Convert.FromBase64String(result.GetProperty("data").GetString()!);
    }

    private async Task<JsonElement> SendAsync(string method, object @params)
    {
        var ws = await EnsureConnectedAsync();

        // A hard timeout so a stalled device (e.g. screen off mid-run) fails
        // the step instead of hanging the whole run. The abort poisons the
        // socket, but EnsureConnectedAsync replaces it on the next call.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var id = ++nextId;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params });
        await ws.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cts.Token);

        // Read frames until our reply arrives; events are interleaved and skipped.
        var buffer = new byte[1 << 16];
        while (true)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult frame;
            do
            {
                frame = await ws.ReceiveAsync(buffer, cts.Token);
                message.Write(buffer, 0, frame.Count);
            }
            while (!frame.EndOfMessage);

            var doc = JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(message.ToArray()));
            if (doc.TryGetProperty("id", out var replyId) && replyId.GetInt32() == id)
            {
                if (doc.TryGetProperty("error", out var error))
                {
                    throw new Exception($"CDP error: {error.GetProperty("message").GetString()}");
                }

                return doc.GetProperty("result");
            }
        }
    }

    public void Dispose() => socket?.Dispose();
}
