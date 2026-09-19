using System.Text.RegularExpressions;

namespace Equora.App.Services;

/// <summary>解析命中的片段(用于提交前预览高亮)。</summary>
public sealed record CaptureSpan(int Start, int Length, CaptureKind Kind);

public enum CaptureKind
{
    Date,
    TimeOfDay,
    Duration,
    Recurrence,
    Priority,
    Tag,
    Project,
}

/// <summary>快速输入解析结果:提交前可编辑的草稿。</summary>
public sealed record CaptureDraft
{
    public required string Title { get; init; }
    public DateTimeOffset? DueAt { get; init; }
    public bool HasExplicitTime { get; init; }
    public int? EstimateMinutes { get; init; }
    public string? RecurrenceText { get; init; }
    public int PriorityValue { get; init; } = 2; // Normal
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public string? ProjectName { get; init; }
    public IReadOnlyList<CaptureSpan> Spans { get; init; } = Array.Empty<CaptureSpan>();

    public string Preview()
    {
        var parts = new List<string>();
        if (DueAt is not null)
        {
            parts.Add(DueAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        }
        if (RecurrenceText is not null) parts.Add(RecurrenceText);
        if (EstimateMinutes is not null) parts.Add($"{EstimateMinutes} 分钟");
        if (PriorityValue != 2) parts.Add($"优先级 {PriorityValue}/4");
        if (Tags.Count > 0) parts.Add("#" + string.Join(" #", Tags));
        if (ProjectName is not null) parts.Add("@" + ProjectName);
        return parts.Count == 0 ? "作为普通任务收集" : string.Join(" · ", parts);
    }
}

/// <summary>
/// 自然语言快速输入解析(子集:相对日期、钟点、时长、简单重复、#标签、@项目、!优先级)。
/// 解析失败或无命中时原文即标题 —— 绝不丢弃内容。
/// 放置决策:纯文本输入辅助、无持久化,归服务层;同步阶段不需要跨端共享(记录于 docs)。
/// </summary>
public static partial class QuickCaptureParser
{
    [GeneratedRegex(@"(今天|今晚|明天|明晚|后天|大后天)")]
    private static partial Regex DayRegex();

    [GeneratedRegex(@"下?周([一二三四五六日天])")]
    private static partial Regex WeekdayRegex();

    [GeneratedRegex(@"(凌晨|早上|上午|中午|下午|傍晚|晚上)?(\d{1,2})[点:时:](\d{1,2})?分?半?")]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"([0-9一二两三四五六七八九十半]+)\s*(个)?(小时|钟头|h|H)(半)?|(\d+)\s*(分钟|分|m)|半个?小时")]
    private static partial Regex DurationRegex();

    // 注意分支顺序:每周X 必须先于 每(周|月),否则"每周五"被"每周"截断。
    [GeneratedRegex(@"(每周([一二三四五六日天])|每(个)?(工作日|日|天)|每(\d+)(天|周))")]
    private static partial Regex RecurRegex();

    [GeneratedRegex(@"#([^\s#]+)")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"@([^\s@]+)")]
    private static partial Regex ProjectRegex();

    [GeneratedRegex(@"!(紧急|高|重要|低)")]
    private static partial Regex PriorityRegex();

    public static CaptureDraft Parse(string text, DateTimeOffset now)
    {
        var spans = new List<CaptureSpan>();
        var removed = new List<(int Start, int End)>();

        DateTimeOffset? due = null;
        var hasTime = false;
        int? estimate = null;
        string? recur = null;
        int priority = 2;
        var tags = new List<string>();
        string? project = null;

        // 用户视角壁钟:直接使用注入时刻(不经 ToLocalTime 换算系统时区)。
        var localNow = now;

        // ---- 日期(先行,时间稍后叠加) ----
        var day = localNow.Date;
        var dayMatched = false;
        var eveningDay = false; // 今晚/明晚:无时段词的钟点按晚上解释
        foreach (Match m in DayRegex().Matches(text))
        {
            day = m.Value switch
            {
                "今天" or "今晚" => localNow.Date,
                "明天" or "明晚" => localNow.Date.AddDays(1),
                "后天" => localNow.Date.AddDays(2),
                _ => localNow.Date.AddDays(3),
            };
            dayMatched = true;
            eveningDay = m.Value is "今晚" or "明晚";
            spans.Add(new CaptureSpan(m.Index, m.Length, CaptureKind.Date));
            removed.Add((m.Index, m.Index + m.Length));
            break; // 只取第一个日期
        }

        foreach (Match m in WeekdayRegex().Matches(text))
        {
            var target = DayNumber(m.Groups[1].Value); // 1=Mon..6=Sat,0=Sun
            static int WeekIndex(int dow) => dow == 0 ? 6 : dow - 1; // 周一为 0
            var diff = WeekIndex(target) - WeekIndex((int)localNow.Date.DayOfWeek);
            if (m.Value.StartsWith('下')) diff += 7;
            else if (diff <= 0) diff += 7; // 本周已过顺延下周
            day = localNow.Date.AddDays(diff);
            dayMatched = true;
            spans.Add(new CaptureSpan(m.Index, m.Length, CaptureKind.Date));
            removed.Add((m.Index, m.Index + m.Length));
            break;
        }

        // ---- 钟点 ----
        foreach (Match m in TimeRegex().Matches(text))
        {
            var hour = int.Parse(m.Groups[2].Value);
            var minute = m.Groups[3].Success ? int.Parse(m.Groups[3].Value)
                : m.Value.EndsWith("半") ? 30 : 0;
            var period = m.Groups[1].Success ? m.Groups[1].Value : "";
            if (hour <= 12 && (period == "下午" || period == "晚上" || period == "傍晚" ||
                               (period.Length == 0 && eveningDay)))
            {
                hour += hour == 12 ? 0 : 12;
            }
            else if (hour == 12 && (period == "中午" || period == "上午"))
            {
                hour = 12;
            }
            else if (hour < 6 && period == "凌晨")
            {
                // 原样
            }
            if (hour is < 0 or > 23) continue;

            if (!dayMatched)
            {
                var candidate = localNow.Date.AddHours(hour).AddMinutes(minute);
                day = candidate <= localNow ? localNow.Date.AddDays(1) : localNow.Date;
                dayMatched = true;
            }
            due = new DateTimeOffset(day.AddHours(hour).AddMinutes(minute),
                localNow.Offset);
            hasTime = true;
            spans.Add(new CaptureSpan(m.Index, m.Length, CaptureKind.TimeOfDay));
            removed.Add((m.Index, m.Index + m.Length));
            break;
        }

        if (due is null && dayMatched)
        {
            due = new DateTimeOffset(day, localNow.Offset);
        }

        // ---- 时长 ----
        foreach (Match m in DurationRegex().Matches(text))
        {
            if (m.Value.Contains("小时") || m.Value.Contains("钟头") ||
                m.Value.Contains('h') || m.Value.Contains('H'))
            {
                var hours = m.Groups[1].Success ? ParseChineseOrNumber(m.Groups[1].Value) : 0.5;
                if (m.Groups[4].Success || m.Value.EndsWith("半")) hours += 0.5;
                estimate = (int)Math.Round(hours * 60);
            }
            else
            {
                estimate = int.Parse(m.Groups[5].Value);
            }
            spans.Add(new CaptureSpan(m.Index, m.Length, CaptureKind.Duration));
            removed.Add((m.Index, m.Index + m.Length));
            break;
        }

        // ---- 重复 ----
        foreach (Match m in RecurRegex().Matches(text))
        {
            recur = m.Value;
            spans.Add(new CaptureSpan(m.Index, m.Length, CaptureKind.Recurrence));
            removed.Add((m.Index, m.Index + m.Length));
            break;
        }

        // ---- 优先级 ----
        foreach (Match m in PriorityRegex().Matches(text))
        {
            priority = m.Groups[1].Value switch
            {
                "紧急" => 4,
                "高" or "重要" => 3,
                "低" => 1,
                _ => 2,
            };
            spans.Add(new CaptureSpan(m.Index, m.Length, CaptureKind.Priority));
            removed.Add((m.Index, m.Index + m.Length));
            break;
        }

        // ---- 标签(全部) ----
        foreach (Match m in TagRegex().Matches(text))
        {
            tags.Add(m.Groups[1].Value);
            spans.Add(new CaptureSpan(m.Index, m.Length, CaptureKind.Tag));
            removed.Add((m.Index, m.Index + m.Length));
        }

        // ---- 项目 ----
        foreach (Match m in ProjectRegex().Matches(text))
        {
            project = m.Groups[1].Value;
            spans.Add(new CaptureSpan(m.Index, m.Length, CaptureKind.Project));
            removed.Add((m.Index, m.Index + m.Length));
            break;
        }

        // 剩余文本作为标题(去掉被识别片段)。
        var title = RemoveSpans(text, removed);
        return new CaptureDraft
        {
            Title = title,
            DueAt = due,
            HasExplicitTime = hasTime,
            EstimateMinutes = estimate,
            RecurrenceText = recur,
            PriorityValue = priority,
            Tags = tags,
            ProjectName = project,
            Spans = spans.OrderBy(s => s.Start).ToList(),
        };
    }

    // 支持中文数字的时长解析(一~十、半、两;组合交给「半」后缀)。
    private static double ParseChineseOrNumber(string s) =>
        double.TryParse(s, out var n) ? n : s switch
        {
            "半" => 0.5, "一" => 1, "两" => 2, "二" => 2, "三" => 3, "四" => 4,
            "五" => 5, "六" => 6, "七" => 7, "八" => 8, "九" => 9, "十" => 10, _ => 0,
        };

    private static int DayNumber(string c) => c switch
    {
        "一" => 1, "二" => 2, "三" => 3, "四" => 4, "五" => 5, "六" => 6, _ => 0,
    };

    private static string RemoveSpans(string text, List<(int Start, int End)> removed)
    {
        removed.Sort((a, b) => a.Start.CompareTo(b.Start));
        var sb = new System.Text.StringBuilder();
        var cursor = 0;
        foreach (var (start, end) in removed)
        {
            if (start < cursor) continue;
            sb.Append(text[cursor..start]);
            cursor = end;
        }
        sb.Append(text[cursor..]);
        var result = Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim(' ', ',', ',', ';', ';');
        return result.Trim();
    }
}
