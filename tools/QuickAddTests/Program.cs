using TaskDashboard.Models;
using TaskDashboard.Services;

// Unit tests for the quick-add parser.
//
//   dotnet run --project tools/QuickAddTests
//
// The first tests in this repo that need no running app: QuickAdd is pure, so
// every case runs against a FROZEN clock. That is the point. Every clock bug
// this project has hit came from an end-to-end fixture that only failed at a
// particular hour or on a particular weekday; here "friday at 5pm said on a
// friday afternoon" is just another test, not something to wait a week for.
//
// Frozen now: Wednesday 29 July 2026, 14:30 local.

var now = new DateTimeOffset(2026, 7, 29, 14, 30, 0, TimeSpan.Zero);
var failures = 0;

void Check(string name, bool condition, string? detail = null)
{
    if (condition)
    {
        Console.WriteLine($"PASS  {name}");
        return;
    }

    failures++;
    Console.WriteLine($"FAIL  {name}{(detail is null ? "" : ": " + detail)}");
}

void Title(string name, string input, string expected)
{
    var actual = QuickAdd.Parse(input, now).Title;
    Check(name, actual == expected, $"title was \"{actual}\", expected \"{expected}\"");
}

void Deadline(string name, string input, DateTime expected)
{
    var actual = QuickAdd.Parse(input, now).Deadline;
    Check(name, actual?.DateTime == expected,
        $"deadline was {actual?.DateTime.ToString("yyyy-MM-dd HH:mm") ?? "null"}, expected {expected:yyyy-MM-dd HH:mm}");
}

// Separate from Deadline() on purpose: "expected no deadline" cannot be
// expressed as a DateTime, and writing it as default(DateTime) compares null
// against 0001-01-01 and fails whatever the parser does.
void NoDeadline(string name, string input)
{
    var actual = QuickAdd.Parse(input, now).Deadline;
    Check(name, actual is null, $"deadline was {actual?.DateTime.ToString("yyyy-MM-dd HH:mm")}");
}

void NotBefore(string name, string input, DateTime expected)
{
    var actual = QuickAdd.Parse(input, now).NotBefore;
    Check(name, actual?.DateTime == expected,
        $"not-before was {actual?.DateTime.ToString("yyyy-MM-dd HH:mm") ?? "null"}, expected {expected:yyyy-MM-dd HH:mm}");
}

void Priority(string name, string input, TaskPriority? expected)
{
    var actual = QuickAdd.Parse(input, now).Priority;
    Check(name, actual == expected, $"priority was {actual?.ToString() ?? "null"}, expected {expected?.ToString() ?? "null"}");
}

void Estimate(string name, string input, int? expected)
{
    var actual = QuickAdd.Parse(input, now).EstimatedMinutes;
    Check(name, actual == expected, $"estimate was {actual?.ToString() ?? "null"}, expected {expected?.ToString() ?? "null"}");
}

Console.WriteLine("---- nothing to find ----");

Title("plain title survives untouched", "Buy milk", "Buy milk");
Check("plain title reports no fields", !QuickAdd.Parse("Buy milk", now).FoundFields);
Title("empty input yields empty title", "", "");
Title("runs of whitespace collapse", "Buy    milk", "Buy milk");

Console.WriteLine("---- priority ----");

Priority("!high", "Ship it !high", TaskPriority.High);
Priority("!urgent", "Ship it !urgent", TaskPriority.Urgent);
Priority("!low", "Ship it !low", TaskPriority.Low);
Priority("!normal", "Ship it !normal", TaskPriority.Normal);
Priority("initial !u", "Ship it !u", TaskPriority.Urgent);
Priority("initial !l", "Ship it !l", TaskPriority.Low);
Priority("case is ignored", "Ship it !HIGH", TaskPriority.High);
Priority("unknown word after ! is not a priority", "Ship it !soon", null);
Priority("numbers are not priorities", "Ship it !1", null);
Title("priority is removed from the title", "Ship it !high", "Ship it");
Title("an unmatched ! stays in the title", "Ship it !soon", "Ship it !soon");
Priority("only the first priority wins", "Ship !high !low", TaskPriority.High);

Console.WriteLine("---- estimate ----");

Estimate("30m", "Write notes 30m", 30);
Estimate("45min", "Write notes 45min", 45);
Estimate("90mins", "Write notes 90mins", 90);
Estimate("2h", "Write notes 2h", 120);
Estimate("1h30", "Write notes 1h30", 90);
Estimate("1h30m", "Write notes 1h30m", 90);
Estimate("a bare number is never an estimate", "Call Mark about 3 things", null);
Title("that bare number stays in the title", "Call Mark about 3 things", "Call Mark about 3 things");
Estimate("a word ending in m is not an estimate", "Send the memo", null);
Estimate("minutes past 59 in the hour form are refused", "Write notes 1h75", null);
Title("estimate is removed from the title", "Write notes 30m", "Write notes");

Console.WriteLine("---- deadlines: relative days ----");

Deadline("by today", "Ship by today", new DateTime(2026, 7, 29, 23, 59, 0));
Deadline("by tomorrow", "Ship by tomorrow", new DateTime(2026, 7, 30, 23, 59, 0));
Deadline("a bare day means the end of it", "Ship by fri", new DateTime(2026, 7, 31, 23, 59, 0));
Deadline("day and time together", "Ship by fri 17:00", new DateTime(2026, 7, 31, 17, 0, 0));
Deadline("due works like by", "Ship due tomorrow", new DateTime(2026, 7, 30, 23, 59, 0));

Console.WriteLine("---- deadlines: weekday rollover ----");

// Today is Wednesday. "wed 17:00" is still ahead; "wed 09:00" has gone.
Deadline("today's weekday, still ahead", "Ship by wed 17:00", new DateTime(2026, 7, 29, 17, 0, 0));
Deadline("today's weekday, already past, rolls a week", "Ship by wed 09:00", new DateTime(2026, 8, 5, 9, 0, 0));
Deadline("soonest upcoming weekday", "Ship by tue", new DateTime(2026, 8, 4, 23, 59, 0));
Deadline("next <weekday> is the one after that", "Ship by next tue", new DateTime(2026, 8, 11, 23, 59, 0));
Deadline("long weekday names", "Ship by friday 17:00", new DateTime(2026, 7, 31, 17, 0, 0));

Console.WriteLine("---- deadlines: times ----");

Deadline("time alone, still ahead today", "Ship by 17:00", new DateTime(2026, 7, 29, 17, 0, 0));
Deadline("time alone, already gone, means tomorrow", "Ship by 09:00", new DateTime(2026, 7, 30, 9, 0, 0));
Deadline("pm suffix", "Ship by 5pm", new DateTime(2026, 7, 29, 17, 0, 0));
Deadline("am suffix rolls to tomorrow", "Ship by 9am", new DateTime(2026, 7, 30, 9, 0, 0));
Deadline("minutes with a meridiem", "Ship by 5:30pm", new DateTime(2026, 7, 29, 17, 30, 0));
Deadline("12pm is midday", "Ship by 12pm", new DateTime(2026, 7, 30, 12, 0, 0));
Deadline("12am is midnight", "Ship by 12am", new DateTime(2026, 7, 30, 0, 0, 0));
NoDeadline("a bare hour is not a time", "Ship by 5");
Title("so that bare hour stays in the title", "Ship by 5", "Ship by 5");

Console.WriteLine("---- deadlines: explicit dates ----");

Deadline("day-first slashes", "Ship by 31/7", new DateTime(2026, 7, 31, 23, 59, 0));
Deadline("day-first with year", "Ship by 31/7/2026", new DateTime(2026, 7, 31, 23, 59, 0));
Deadline("day-first with dashes", "Ship by 31-7", new DateTime(2026, 7, 31, 23, 59, 0));
Deadline("ISO is understood too", "Ship by 2026-08-15", new DateTime(2026, 8, 15, 23, 59, 0));
Deadline("day and month name", "Ship by 15 aug", new DateTime(2026, 8, 15, 23, 59, 0));
Deadline("long month name", "Ship by 15 august", new DateTime(2026, 8, 15, 23, 59, 0));
Deadline("date and time", "Ship by 15 aug 09:00", new DateTime(2026, 8, 15, 9, 0, 0));
Deadline("a bare date already gone means next year", "Ship by 1/1", new DateTime(2027, 1, 1, 23, 59, 0));
NoDeadline("an impossible date is not a date", "Ship by 31/2");
Deadline("an explicit past date is taken at face value", "Ship by 2026-01-01", new DateTime(2026, 1, 1, 23, 59, 0));

Console.WriteLine("---- not-before ----");

NotBefore("a bare day means the start of it", "Ship after fri", new DateTime(2026, 7, 31, 0, 0, 0));
NotBefore("from works like after", "Ship from tomorrow", new DateTime(2026, 7, 30, 0, 0, 0));
NotBefore("with a time", "Ship after fri 09:00", new DateTime(2026, 7, 31, 9, 0, 0));

Console.WriteLine("---- keywords that are just words ----");

Title("by with nothing date-shaped after it", "Stand by for launch", "Stand by for launch");
NoDeadline("and it sets no deadline", "Stand by for launch");
Title("by at the very end", "Something to swing by", "Something to swing by");
Title("after as an ordinary word", "Clean up after the party", "Clean up after the party");
Title("a second by stays in the title", "Move it by fri by hand", "Move it by hand");

Console.WriteLine("---- everything together ----");

var full = QuickAdd.Parse("Submit timesheet by fri 17:00 !high 45m", now);
Check("combined: title", full.Title == "Submit timesheet", $"was \"{full.Title}\"");
Check("combined: deadline", full.Deadline?.DateTime == new DateTime(2026, 7, 31, 17, 0, 0),
    $"was {full.Deadline?.DateTime.ToString() ?? "null"}");
Check("combined: priority", full.Priority == TaskPriority.High);
Check("combined: estimate", full.EstimatedMinutes == 45);
Check("combined: reports fields found", full.FoundFields);

var both = QuickAdd.Parse("Review PR after tomorrow 09:00 by fri 2h", now);
Check("both bounds: not-before", both.NotBefore?.DateTime == new DateTime(2026, 7, 30, 9, 0, 0),
    $"was {both.NotBefore?.DateTime.ToString() ?? "null"}");
Check("both bounds: deadline", both.Deadline?.DateTime == new DateTime(2026, 7, 31, 23, 59, 0),
    $"was {both.Deadline?.DateTime.ToString() ?? "null"}");
Check("both bounds: title", both.Title == "Review PR", $"was \"{both.Title}\"");
Check("both bounds: estimate", both.EstimatedMinutes == 120);

Console.WriteLine("---- the clock never moves the answer ----");

// The same line parsed at four instants across a week must differ only by the
// day it is anchored to — never by whether the suite happened to run on a
// Sunday, which is the trap the end-to-end fixtures kept falling into.
var instants = new[]
{
    new DateTimeOffset(2026, 7, 26, 23, 50, 0, TimeSpan.Zero), // Sunday, near midnight
    new DateTimeOffset(2026, 7, 27, 0, 5, 0, TimeSpan.Zero),   // Monday, just after
    new DateTimeOffset(2026, 12, 31, 18, 0, 0, TimeSpan.Zero), // year boundary
    new DateTimeOffset(2026, 2, 28, 12, 0, 0, TimeSpan.Zero),  // end of February
};

foreach (var instant in instants)
{
    var parsed = QuickAdd.Parse("Ship it by tomorrow !high 30m", instant);
    var expected = instant.LocalDateTime.Date.AddDays(1).AddHours(23).AddMinutes(59);
    Check($"tomorrow at {instant:yyyy-MM-dd HH:mm}",
        parsed.Deadline?.DateTime == expected && parsed.Title == "Ship it",
        $"deadline was {parsed.Deadline?.DateTime.ToString() ?? "null"}, expected {expected}");
}

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
