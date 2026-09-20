using Equora.App.Services;
using Xunit;

namespace Equora.App.Tests;

public class RestrictionPolicyTests
{
    private static DateTimeOffset At(int hour, int minute = 0) => new(2026, 9, 20, hour, minute, 0, TimeSpan.FromHours(8));
    [Theory]
    [InlineData(21, 59, false)] [InlineData(22, 0, true)] [InlineData(0, 0, true)] [InlineData(6, 0, false)]
    public void OvernightScheduleHasExclusiveEnd(int hour, int minute, bool restricted)
    {
        var rule = new UsageRule { Kind = "website", Target = "example.com", HasSchedule = true, StartMinute = 1320, EndMinute = 360 };
        Assert.Equal(restricted, RestrictionPolicy.Reason(rule, At(hour, minute), false, 0) is not null);
    }
    [Fact]
    public void FocusQuotaAndScheduleAreIndependentConditions()
    {
        var rule = new UsageRule { Kind = "website", Target = "example.com", DuringFocus = true, DailyMinutes = 1 };
        Assert.Null(RestrictionPolicy.Reason(rule, At(10), false, 59.9));
        Assert.NotNull(RestrictionPolicy.Reason(rule, At(10), true, 0));
        Assert.NotNull(RestrictionPolicy.Reason(rule, At(10), false, 60));
        Assert.Null(RestrictionPolicy.Reason(rule with { Enabled = false }, At(10), true, 100));
        Assert.NotNull(RestrictionPolicy.Reason(rule with { HasSchedule = true, StartMinute = 0, EndMinute = 0 }, At(10), false, 0));
    }
    [Fact]
    public void DomainsMatchOnlyLabelBoundaries()
    {
        Assert.Equal("example.com", RestrictionPolicy.NormalizeTarget("website", "https://EXAMPLE.com/path"));
        Assert.True(RestrictionPolicy.Matches("website", "example.com", "sub.example.com"));
        Assert.False(RestrictionPolicy.Matches("website", "example.com", "notexample.com"));
        Assert.False(RestrictionPolicy.Matches("website", "example.com", "example.com.evil.test"));
        Assert.Throws<ArgumentException>(() => RestrictionPolicy.Validate(new UsageRule { Target = "explorer.exe", DuringFocus = true }));
        Assert.Throws<ArgumentException>(() => RestrictionPolicy.Validate(new UsageRule { Target = "game.exe" }));
        Assert.Equal("game.exe", RestrictionPolicy.Validate(new UsageRule { Target = "GAME", DuringFocus = true }).Target);
    }
    [Fact]
    public void UsagePersistsAndResetsAtLocalMidnightAndLeaseExpires()
    {
        var directory = Path.Combine(Path.GetTempPath(), "equora-rules-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new RestrictionStore(directory);
            Assert.False(store.IsDesktopActive(DateTimeOffset.Now));
            store.Pulse(true, null);
            Assert.True(store.IsDesktopActive(DateTimeOffset.Now));
            Assert.False(store.IsDesktopActive(DateTimeOffset.Now.AddSeconds(13)));
            store.Save(new RestrictionConfiguration { Rules = [new UsageRule { Target = "game.exe", DailyMinutes = 1 }] });
            Assert.Single(new RestrictionStore(directory).Load().Rules);
            store.AddUsage("app", new Dictionary<string, double> { ["game.exe"] = 59 }, At(23, 59));
            store.AddUsage("app", new Dictionary<string, double> { ["game.exe"] = 1 }, At(23, 59));
            Assert.Equal(60, new RestrictionStore(directory).Usage("app", At(23, 59))["game.exe"]);
            Assert.Empty(store.Usage("app", At(0).AddDays(1)));
            Assert.Empty(store.Usage("website", At(23)));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
