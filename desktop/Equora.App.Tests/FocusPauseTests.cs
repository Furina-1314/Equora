using Equora.App.Services;
using Xunit;

namespace Equora.App.Tests;
public class FocusPauseTests
{
    [Fact]
    public void PrimaryActionFreezesClockAcrossPausesAndPageReloads()
    {
        using var service = new AppDataService(Path.Combine(Path.GetTempPath(), $"equora-pause-{Guid.NewGuid():N}.db"), "pause-test");
        var start = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.Now.ToUnixTimeSeconds());
        var vm = new FocusViewModel(service, service) { Clock = start };
        Assert.Equal("开始", vm.PrimaryActionText);
        vm.ToggleSession();
        Assert.Equal("暂停", vm.PrimaryActionText);
        vm.Clock = start.AddMinutes(5);
        vm.ToggleSession();
        Assert.Equal("继续", vm.PrimaryActionText);
        Assert.Equal("20:00", vm.ClockText);
        vm.Clock = start.AddMinutes(8);
        vm.Load();
        Assert.Equal("20:00", vm.ClockText);
        vm.ToggleSession();
        Assert.Equal("20:00", vm.ClockText);
        vm.Clock = start.AddMinutes(9);
        Assert.Equal("19:00", vm.ClockText);
        vm.ToggleSession();
        vm.Clock = start.AddMinutes(12);
        Assert.Equal("19:00", vm.ClockText);
        vm.Complete();
        Assert.Equal("开始", vm.PrimaryActionText);
        Assert.Contains("有效专注 6 分钟", vm.StatusText);
    }
}
