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

using var cdp = await Cdp.ConnectAsync("http://localhost:9333");

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
    var png = await cdp.ScreenshotAsync();
    await File.WriteAllBytesAsync(Path.Combine(shotDir, name), png);
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
        // The suite leaves three: two active plus the completed "Done on droid".
        if (await Count(".task-item") != 3) throw new Exception($"expected 3 tasks, got {await Count(".task-item")}");
        if (await Count(".task-item.done") != 1) throw new Exception("expected the completed task to survive as done");
        await WaitFor("[...document.querySelectorAll('.task-title')].some(e => e.innerText.includes('Android task'))", "Android task row");
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

// ---- suite ----

await Step("app: dashboard is the root page", async () =>
{
    await WaitFor("document.querySelector('h1')", "app rendered");
    var h1 = await Text("h1");
    if (h1 != "Dashboard") throw new Exception($"expected Dashboard, got {h1}");
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

await Step("blocked: add nightly Sleep period", async () =>
{
    await NavTo("blocked-time", "Blocked time");
    await Click(".blocked-toolbar button");
    await WaitFor("document.querySelector('.modal-panel')", "modal open");
    await SetValue("#blocked-label", "Sleep");
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

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;

/// <summary>Minimal CDP client over the page-level websocket.</summary>
internal sealed class Cdp : IDisposable
{
    private readonly ClientWebSocket socket;
    private int nextId;

    private Cdp(ClientWebSocket socket) => this.socket = socket;

    public static async Task<Cdp> ConnectAsync(string httpEndpoint)
    {
        using var http = new HttpClient();
        var targets = JsonSerializer.Deserialize<JsonElement>(await http.GetStringAsync($"{httpEndpoint}/json"));
        var wsUrl = targets.EnumerateArray()
            .First(t => t.GetProperty("type").GetString() == "page")
            .GetProperty("webSocketDebuggerUrl").GetString()!;

        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
        return new Cdp(socket);
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

    public async Task<byte[]> ScreenshotAsync()
    {
        var result = await SendAsync("Page.captureScreenshot", new { });
        return Convert.FromBase64String(result.GetProperty("data").GetString()!);
    }

    private async Task<JsonElement> SendAsync(string method, object @params)
    {
        var id = ++nextId;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params });
        await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

        // Read frames until our reply arrives; events are interleaved and skipped.
        var buffer = new byte[1 << 16];
        while (true)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult frame;
            do
            {
                frame = await socket.ReceiveAsync(buffer, CancellationToken.None);
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

    public void Dispose() => socket.Dispose();
}
