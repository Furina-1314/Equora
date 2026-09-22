namespace Equora.App.Services;

public sealed record SemesterSettings
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public bool Enabled { get; init; }
    public string Name { get; init; } = "新学期";
    public DateOnly StartDate { get; init; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly EndDate { get; init; } = DateOnly.FromDateTime(DateTime.Today.AddDays(125));
    public DateOnly FirstMonday => StartDate.AddDays(-((int)StartDate.DayOfWeek + 6) % 7);
    public int WeekCount => (EndDate.DayNumber - FirstMonday.DayNumber) / 7 + 1;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("请输入学期名。");
        if (EndDate < StartDate) throw new ArgumentException("学期结束日期不能早于起始日期。");
    }

    public int? WeekNumber(DateOnly date) => date < StartDate || date > EndDate
        ? null : (date.DayNumber - FirstMonday.DayNumber) / 7 + 1;

    public IReadOnlyList<int> ParseWeeks(string? text)
    {
        Validate();
        if (text is null) return Enumerable.Range(1, WeekCount).ToArray();
        var weeks = new SortedSet<int>();
        foreach (var part in text.Replace('，', ',').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var bounds = part.Split('-', StringSplitOptions.TrimEntries);
            if (bounds.Length > 2 || !int.TryParse(bounds[0], out var first) || first < 1 || first > WeekCount)
                throw new ArgumentException($"周次格式示例：1,3,5-8；范围为 1–{WeekCount}。");
            var last = first;
            if (bounds.Length == 2 && (!int.TryParse(bounds[1], out last) || last < first || last > WeekCount))
                throw new ArgumentException($"请输入有效周次范围（1–{WeekCount}）。");
            for (var week = first; week <= last; week++) weeks.Add(week);
        }
        if (weeks.Count == 0) throw new ArgumentException("请填写至少一个周次。");
        return weeks.ToArray();
    }

    public IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Slots(
        string? weeks, DayOfWeek weekday, TimeSpan start, TimeSpan end)
    {
        Validate();
        if (!Enabled) throw new ArgumentException("请先开启学期模式。");
        if (!Enum.IsDefined(weekday)) throw new ArgumentException("请选择星期。");
        if (start < TimeSpan.Zero || end >= TimeSpan.FromDays(1) || end <= start)
            throw new ArgumentException("结束时间必须晚于开始时间，且位于同一天。");
        var result = new List<(DateTimeOffset, DateTimeOffset)>();
        foreach (var week in ParseWeeks(weeks))
        {
            var date = FirstMonday.AddDays((week - 1) * 7 + ((int)weekday + 6) % 7);
            if (date < StartDate || date > EndDate) continue;
            var from = date.ToDateTime(TimeOnly.FromTimeSpan(start));
            var to = date.ToDateTime(TimeOnly.FromTimeSpan(end));
            if (TimeZoneInfo.Local.IsInvalidTime(from) || TimeZoneInfo.Local.IsInvalidTime(to))
                throw new ArgumentException($"{date:yyyy-MM-dd} 的所选时间在夏令时切换中不存在。");
            result.Add((new(from, TimeZoneInfo.Local.GetUtcOffset(from)), new(to, TimeZoneInfo.Local.GetUtcOffset(to))));
        }
        if (result.Count == 0) throw new ArgumentException("所选周次和星期在学期日期范围内没有可添加的时间段。");
        return result;
    }
}

public static class SemesterLifecycle
{
    public static AppPreferences ArchiveExpired(AppPreferences preferences, DateOnly today)
    {
        var semester = preferences.Semester;
        if (string.IsNullOrWhiteSpace(semester.Name) || semester.EndDate >= today) return preferences;
        return preferences with {
            ArchivedSemesters = preferences.ArchivedSemesters.Any(s => s.Id == semester.Id)
                ? preferences.ArchivedSemesters : preferences.ArchivedSemesters.Append(semester).ToArray(),
            Semester = new SemesterSettings { Enabled = semester.Enabled, Name = "", StartDate = today, EndDate = today.AddDays(125) }
        };
    }
}
