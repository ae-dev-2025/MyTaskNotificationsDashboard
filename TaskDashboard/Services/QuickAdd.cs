using TaskDashboard.Models;

namespace TaskDashboard.Services;

/// <summary>What a quick-add line yielded. Every field is optional: the parser
/// only reports what it recognised, and the caller decides what to do with the
/// rest.</summary>
public readonly record struct QuickAddResult(
    string Title,
    DateTimeOffset? Deadline,
    DateTimeOffset? NotBefore,
    TaskPriority? Priority,
    int? EstimatedMinutes)
{
    /// <summary>True when the line carried anything beyond a plain title, so a
    /// caller can avoid announcing a parse that did nothing.</summary>
    public bool FoundFields =>
        Deadline is not null || NotBefore is not null
        || Priority is not null || EstimatedMinutes is not null;
}

/// <summary>
/// Turns one typed line — "Submit timesheet by fri 17:00 !high 45m" — into task
/// fields. Pure and deterministic: <paramref name="now"/> is always a parameter,
/// never read from the clock inside, so every relative phrase ("tomorrow",
/// "fri") can be tested at a frozen instant rather than only at the hour that
/// happens to expose a bug.
///
/// The parser is deliberately <em>conservative</em>. A token is only consumed
/// when its shape is unambiguous — "30m" is an estimate, a bare "3" never is —
/// and anything unrecognised stays in the title untouched. Silently eating
/// words out of a title is worse than recognising nothing at all.
///
/// Dates are read <b>day-first</b> (31/7 is July) and times as 24-hour unless
/// suffixed am/pm. That matches how the app renders dates everywhere
/// ("ddd d MMM", "HH:mm") rather than the WebView's own locale.
/// </summary>
public static class QuickAdd
{
    /// <summary>Words that introduce a deadline — the latest acceptable moment.</summary>
    private static readonly string[] DeadlineWords = ["by", "due"];

    /// <summary>Words that introduce a not-before — the earliest allowed start.</summary>
    private static readonly string[] NotBeforeWords = ["after", "from"];

    /// <summary>Which end of a bare day a phrase means when no time is given.
    /// A deadline of "friday" means the end of Friday; an earliest start of
    /// "friday" means the beginning of it. The asymmetry is the point: a
    /// deadline is a ceiling and a floor is a floor.</summary>
    private enum DayEdge
    {
        Start,
        End,
    }

    public static QuickAddResult Parse(string input, DateTimeOffset now)
    {
        var words = (input ?? string.Empty).Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var consumed = new bool[words.Length];
        TaskPriority? priority = null;
        int? minutes = null;
        DateTimeOffset? deadline = null;
        DateTimeOffset? notBefore = null;

        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];

            if (priority is null && TryPriority(word, out var parsedPriority))
            {
                priority = parsedPriority;
                consumed[i] = true;
                continue;
            }

            if (minutes is null && TryDuration(word, out var parsedMinutes))
            {
                minutes = parsedMinutes;
                consumed[i] = true;
                continue;
            }

            var wantsDeadline = DeadlineWords.Contains(word, StringComparer.OrdinalIgnoreCase);
            var wantsFloor = NotBeforeWords.Contains(word, StringComparer.OrdinalIgnoreCase);
            if (!wantsDeadline && !wantsFloor)
            {
                continue;
            }

            // Already have this one? Leave the word in the title rather than
            // overwrite — "move the meeting by friday" should not lose "by".
            if ((wantsDeadline && deadline is not null) || (wantsFloor && notBefore is not null))
            {
                continue;
            }

            var edge = wantsDeadline ? DayEdge.End : DayEdge.Start;
            if (!TryWhen(words, i + 1, now, edge, out var when, out var used))
            {
                // "Stand by for launch" — nothing date-shaped follows, so the
                // keyword is just a word.
                continue;
            }

            if (wantsDeadline)
            {
                deadline = when;
            }
            else
            {
                notBefore = when;
            }

            consumed[i] = true;
            for (var k = 0; k < used; k++)
            {
                consumed[i + 1 + k] = true;
            }

            i += used;
        }

        var title = string.Join(' ', words.Where((_, i) => !consumed[i]));
        return new QuickAddResult(title, deadline, notBefore, priority, minutes);
    }

    // ---- priority ----

    private static bool TryPriority(string word, out TaskPriority priority)
    {
        priority = default;
        if (word.Length < 2 || word[0] != '!')
        {
            return false;
        }

        // Names and their initials. Numbers are deliberately not supported:
        // whether !1 means Urgent or Low is a convention users have to be
        // taught, and a wrong guess silently mis-prioritises the task.
        switch (word[1..].ToLowerInvariant())
        {
            case "l" or "low": priority = TaskPriority.Low; return true;
            case "n" or "normal": priority = TaskPriority.Normal; return true;
            case "h" or "high": priority = TaskPriority.High; return true;
            case "u" or "urgent": priority = TaskPriority.Urgent; return true;
            default: return false;
        }
    }

    // ---- duration ----

    /// <summary>Reads "30m", "45min", "2h", "1h30", "1h30m". A unit is required:
    /// a bare number stays in the title, because "Call Mark about 3 things"
    /// must not acquire a three-minute estimate.</summary>
    private static bool TryDuration(string word, out int minutes)
    {
        minutes = 0;
        var text = word.ToLowerInvariant();

        var hourMark = text.IndexOf('h');
        if (hourMark > 0)
        {
            if (!int.TryParse(text[..hourMark], out var hours) || hours is < 0 or > 999)
            {
                return false;
            }

            var rest = text[(hourMark + 1)..].TrimEnd('m');
            if (rest.Length == 0)
            {
                minutes = hours * 60;
                return minutes > 0;
            }

            if (!int.TryParse(rest, out var extra) || extra is < 0 or > 59)
            {
                return false;
            }

            minutes = hours * 60 + extra;
            return minutes > 0;
        }

        foreach (var suffix in new[] { "mins", "min", "m" })
        {
            if (!text.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var head = text[..^suffix.Length];
            if (head.Length > 0 && int.TryParse(head, out var value) && value is > 0 and <= 100_000)
            {
                minutes = value;
                return true;
            }

            return false;
        }

        return false;
    }

    // ---- when ----

    /// <summary>Consumes a date phrase, a time, or both, starting at
    /// <paramref name="start"/>. Returns how many words it took so the caller
    /// can mark exactly those consumed.</summary>
    private static bool TryWhen(
        string[] words, int start, DateTimeOffset now, DayEdge edge,
        out DateTimeOffset when, out int used)
    {
        when = default;
        used = 0;

        var index = start;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        DateOnly? date = null;
        var fromWeekday = false;

        if (index < words.Length)
        {
            if (words[index].Equals("next", StringComparison.OrdinalIgnoreCase)
                && index + 1 < words.Length
                && TryWeekday(words[index + 1], out var nextDow))
            {
                date = SoonestWeekday(today, nextDow).AddDays(7);
                fromWeekday = true;
                index += 2;
            }
            else if (TryWeekday(words[index], out var dow))
            {
                date = SoonestWeekday(today, dow);
                fromWeekday = true;
                index++;
            }
            else if (words[index].Equals("today", StringComparison.OrdinalIgnoreCase))
            {
                date = today;
                index++;
            }
            else if (words[index].Equals("tomorrow", StringComparison.OrdinalIgnoreCase))
            {
                date = today.AddDays(1);
                index++;
            }
            else if (TryNumericDate(words[index], today, out var numeric))
            {
                date = numeric;
                index++;
            }
            else if (TryDayMonth(words, index, today, out var dayMonth, out var dayMonthUsed))
            {
                date = dayMonth;
                index += dayMonthUsed;
            }
        }

        TimeOnly? time = null;
        if (index < words.Length && TryTime(words[index], out var parsedTime))
        {
            time = parsedTime;
            index++;
        }

        if (date is null && time is null)
        {
            return false;
        }

        DateOnly day;
        TimeOnly clock;

        if (date is { } onDay)
        {
            day = onDay;
            clock = time ?? (edge == DayEdge.End ? new TimeOnly(23, 59) : TimeOnly.MinValue);
        }
        else
        {
            // Time with no day: today if it is still ahead, otherwise tomorrow.
            day = today;
            clock = time!.Value;
            if (day.ToDateTime(clock) <= now.LocalDateTime)
            {
                day = day.AddDays(1);
            }
        }

        var local = day.ToDateTime(clock);

        // "fri 09:00" said on a Friday afternoon means next Friday. Only roll a
        // weekday forward — an explicit date the user typed is taken at face
        // value, even if it has passed, so a typo is visible rather than
        // silently relocated a week later.
        if (fromWeekday && local <= now.LocalDateTime)
        {
            local = local.AddDays(7);
        }

        when = new DateTimeOffset(local, now.Offset);
        used = index - start;
        return used > 0;
    }

    private static DateOnly SoonestWeekday(DateOnly today, DayOfWeek target)
    {
        var delta = ((int)target - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(delta);
    }

    private static bool TryWeekday(string word, out DayOfWeek day)
    {
        switch (word.ToLowerInvariant())
        {
            case "mon" or "monday": day = DayOfWeek.Monday; return true;
            case "tue" or "tues" or "tuesday": day = DayOfWeek.Tuesday; return true;
            case "wed" or "weds" or "wednesday": day = DayOfWeek.Wednesday; return true;
            case "thu" or "thur" or "thurs" or "thursday": day = DayOfWeek.Thursday; return true;
            case "fri" or "friday": day = DayOfWeek.Friday; return true;
            case "sat" or "saturday": day = DayOfWeek.Saturday; return true;
            case "sun" or "sunday": day = DayOfWeek.Sunday; return true;
            default: day = default; return false;
        }
    }

    /// <summary>Reads "31/7", "31/07/2026", "31-7" as day-first, and
    /// "2026-07-31" as ISO. Day-first because that is how the app renders every
    /// date it shows; ISO because it is unambiguous and worth accepting.</summary>
    private static bool TryNumericDate(string word, DateOnly today, out DateOnly date)
    {
        date = default;
        var parts = word.Split('/', '-');

        if (parts.Length is < 2 or > 3 || parts.Any(p => p.Length == 0 || !p.All(char.IsAsciiDigit)))
        {
            return false;
        }

        int year, month, day;

        if (parts.Length == 3 && parts[0].Length == 4)
        {
            // ISO: 2026-07-31
            year = int.Parse(parts[0]);
            month = int.Parse(parts[1]);
            day = int.Parse(parts[2]);
        }
        else
        {
            day = int.Parse(parts[0]);
            month = int.Parse(parts[1]);
            year = parts.Length == 3 ? int.Parse(parts[2]) : today.Year;
            if (year < 100)
            {
                year += 2000;
            }
        }

        if (year is < 1 or > 9999 || month is < 1 or > 12
            || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateOnly(year, month, day);

        // A bare "31/7" that has already gone means next year, the same way a
        // bare weekday means the next one.
        if (parts.Length == 2 && date < today)
        {
            date = date.AddYears(1);
        }

        return true;
    }

    /// <summary>Reads "31 jul" and "31 july" — two words, day first.</summary>
    private static bool TryDayMonth(
        string[] words, int start, DateOnly today, out DateOnly date, out int used)
    {
        date = default;
        used = 0;

        if (start + 1 >= words.Length
            || !int.TryParse(words[start], out var day)
            || day is < 1 or > 31
            || !TryMonth(words[start + 1], out var month))
        {
            return false;
        }

        var year = today.Year;
        if (day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        if (date < today)
        {
            date = date.AddYears(1);
        }

        used = 2;
        return true;
    }

    private static bool TryMonth(string word, out int month)
    {
        switch (word.ToLowerInvariant().TrimEnd(','))
        {
            case "jan" or "january": month = 1; return true;
            case "feb" or "february": month = 2; return true;
            case "mar" or "march": month = 3; return true;
            case "apr" or "april": month = 4; return true;
            case "may": month = 5; return true;
            case "jun" or "june": month = 6; return true;
            case "jul" or "july": month = 7; return true;
            case "aug" or "august": month = 8; return true;
            case "sep" or "sept" or "september": month = 9; return true;
            case "oct" or "october": month = 10; return true;
            case "nov" or "november": month = 11; return true;
            case "dec" or "december": month = 12; return true;
            default: month = 0; return false;
        }
    }

    /// <summary>Reads "17:00", "5pm", "5:30pm", "9am". Bare numbers are refused:
    /// "by 5" is too easily part of a title to guess at.</summary>
    private static bool TryTime(string word, out TimeOnly time)
    {
        time = default;
        var text = word.ToLowerInvariant();

        var meridiem = 0;
        if (text.EndsWith("am", StringComparison.Ordinal))
        {
            meridiem = 1;
            text = text[..^2];
        }
        else if (text.EndsWith("pm", StringComparison.Ordinal))
        {
            meridiem = 2;
            text = text[..^2];
        }

        var parts = text.Split(':');
        if (parts.Length > 2 || parts.Any(p => p.Length == 0 || !p.All(char.IsAsciiDigit)))
        {
            return false;
        }

        // A bare hour is only a time when am/pm says so; "17:00" carries its own
        // proof in the colon.
        if (parts.Length == 1 && meridiem == 0)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var hour))
        {
            return false;
        }

        var minute = 0;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out minute) || minute is < 0 or > 59))
        {
            return false;
        }

        switch (meridiem)
        {
            case 1 when hour is < 1 or > 12:
            case 2 when hour is < 1 or > 12:
                return false;
            case 1:
                hour = hour == 12 ? 0 : hour;
                break;
            case 2:
                hour = hour == 12 ? 12 : hour + 12;
                break;
            default:
                if (hour is < 0 or > 23)
                {
                    return false;
                }

                break;
        }

        time = new TimeOnly(hour, minute);
        return true;
    }
}
