using Equora.App.Services;
using Xunit;

namespace Equora.App.Tests;

public class RestrictionPauseTests
{
    [Fact]
    public void PauseEndsAtDeadlineAndDoesNotLeaveStaleState()
    {
        var now = DateTimeOffset.UtcNow;
        var pause = new RestrictionPause();
        pause.Start(now);
        Assert.Equal(TimeSpan.FromMinutes(15), pause.Remaining(now));
        Assert.False(pause.TryExpire(now.AddMinutes(15).AddMilliseconds(-1)));
        Assert.Equal(TimeSpan.FromSeconds(1), pause.Remaining(now.AddMinutes(15).AddMilliseconds(-1)));
        Assert.True(pause.TryExpire(now.AddMinutes(15)));
        Assert.Null(pause.Until);
        Assert.Equal(TimeSpan.Zero, pause.Remaining(now.AddMinutes(15)));
        Assert.False(pause.TryExpire(now.AddMinutes(16)));
    }

    [Fact]
    public void ResumeAfterSleepExpiresImmediately()
    {
        var now = DateTimeOffset.UtcNow;
        var pause = new RestrictionPause();
        pause.Start(now);
        Assert.True(pause.TryExpire(now.AddHours(1)));
        Assert.Equal(TimeSpan.Zero, pause.Remaining(now.AddHours(1)));
    }
}
