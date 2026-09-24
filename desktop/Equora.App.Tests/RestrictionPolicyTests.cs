using System.Diagnostics;
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
    public void EquoraProcessesCannotBeRestricted()
    {
        // 自保护:规则入口(Validate)与求值出口(Reason)双重拦截,手动改配置文件也无法生效。
        Assert.Throws<ArgumentException>(() =>
            RestrictionPolicy.Validate(new UsageRule { Target = "Equora.App.exe", DuringFocus = true }));
        Assert.Throws<ArgumentException>(() =>
            RestrictionPolicy.Validate(new UsageRule { Target = "com.equora.nativehost.exe", HasSchedule = true }));
        Assert.Throws<ArgumentException>(() =>
            RestrictionPolicy.Validate(new UsageRule { Target = "EQUORA-SERVER", DuringFocus = true }));
        Assert.Null(RestrictionPolicy.Reason(
            new UsageRule { Kind = "app", Target = "equora.app.exe", DuringFocus = true }, At(10), true, 0));
        Assert.Null(RestrictionPolicy.Reason(
            new UsageRule { Kind = "app", Target = "COM.EQUORA.NATIVEHOST.exe", HasSchedule = true, StartMinute = 0, EndMinute = 0 }, At(10), false, 0));
        Assert.Null(RestrictionPolicy.Reason(
            new UsageRule { Kind = "app", Target = "equora-server", DailyMinutes = 1 }, At(10), false, 3600));
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
    [Fact]
    public void AppTemporaryAllowanceIsOncePerDayPerProcessAndSurvivesRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "equora-allow-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = At(10);
            var store = new RestrictionStore(directory);
            Assert.False(store.AppAllowance("game.exe", now).Used);
            Assert.Equal(now.AddMinutes(5), store.GrantAppAllowance("game.exe", now));
            Assert.Null(new RestrictionStore(directory).GrantAppAllowance("game.exe", now.AddMinutes(6)));
            Assert.Equal(now.AddMinutes(5), new RestrictionStore(directory).AppAllowance("game.exe", now).Until);
            // 当天过期后仍算已用;次日机会自动刷新,可再次发放。
            Assert.True(store.AppAllowance("game.exe", now.AddMinutes(6)).Used);
            Assert.False(store.AppAllowance("game.exe", now.AddDays(1)).Used);
            Assert.NotNull(store.GrantAppAllowance("game.exe", now.AddDays(1)));
            Assert.Equal(now.AddDays(1).AddMinutes(5), store.GrantAppAllowance("other.exe", now.AddDays(1)));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void WriteRetriesWhenDestinationIsBrieflyHeldOpen()
    {
        var directory = Path.Combine(Path.GetTempPath(), "equora-rules-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new RestrictionStore(directory);
            store.Pulse(false, null);
            var path = Path.Combine(directory, "restriction-heartbeat.json");
            // 读方不共享 Delete 时替换持续被拒：写入必须重试到预算耗尽，而不是立即失败。
            using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var stopwatch = Stopwatch.StartNew();
            Exception? exhausted = null;
            try { store.Pulse(true, null); }
            catch (Exception ex) { exhausted = ex; }
            Assert.True(exhausted is IOException or UnauthorizedAccessException, $"unexpected: {exhausted}");
            Assert.True(stopwatch.ElapsedMilliseconds >= 100, "write gave up without retrying");
            hold.Dispose();
            store.Pulse(true, null);
            Assert.True(new RestrictionStore(directory).IsDesktopActive(DateTimeOffset.Now));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void WriteReplacesFileWhileReaderHoldsItOpenWithDeleteSharing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "equora-rules-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new RestrictionStore(directory);
            store.Pulse(false, null);
            var path = Path.Combine(directory, "restriction-heartbeat.json");
            using (var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                store.Pulse(false, DateTimeOffset.Now.AddMinutes(1));
                using var reader = new StreamReader(hold);
                Assert.NotEmpty(reader.ReadToEnd());
            }
            Assert.NotNull(new RestrictionStore(directory).Heartbeat!.AllowUntil);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
