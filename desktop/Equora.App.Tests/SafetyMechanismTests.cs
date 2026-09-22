using Equora.App.Services;
using Xunit;

namespace Equora.App.Tests;

public class SafetyWhitelistTests
{
    [Theory]
    [InlineData("explorer.exe")]           // 文件管理器
    [InlineData("cmd.exe")]                // 命令行(恢复入口)
    [InlineData("msmpeng.exe")]            // Defender
    [InlineData("taskmgr.exe")]            // 任务管理器
    [InlineData("SystemSettings.exe")]     // 系统设置
    [InlineData("narrator.exe")]           // 辅助功能
    [InlineData("powershell")]             // 无扩展名形式
    [InlineData("Equora.App.exe")]         // 自身(避免自锁)
    public void CriticalProcessesAreAlwaysProtected(string process)
    {
        Assert.True(SafetyWhitelist.IsProtected(process),
            $"「{process}」必须受安全白名单保护");
    }

    [Theory]
    [InlineData("game.exe")]
    [InlineData("steam.exe")]
    [InlineData("wechat.exe")]
    [InlineData("notepad.exe")]
    public void NonCriticalProcessesAreNotProtected(string process)
    {
        Assert.False(SafetyWhitelist.IsProtected(process));
    }

    [Fact]
    public void SanitizeStripsProtectedFromBlockedList()
    {
        var blocked = new[] { "game.exe", "explorer.exe", "steam", "taskmgr" };
        var safe = SafetyWhitelist.SanitizeBlockedList(blocked);
        Assert.Equal(new[] { "game.exe", "steam" }, safe);
    }

    [Fact]
    public void EmptyBlockedListStaysEmpty()
    {
        Assert.Empty(SafetyWhitelist.SanitizeBlockedList(Array.Empty<string>()));
    }
}

public class EmergencyUnlockTests
{
    [Fact]
    public void TriggerActivatesCooldownWindow()
    {
        var unlock = new EmergencyUnlock(TimeSpan.FromMinutes(15));
        Assert.False(unlock.IsActive());

        var now = DateTimeOffset.Now;
        Assert.True(unlock.Trigger(now) == true);
        Assert.True(unlock.IsActive(now));
        Assert.Equal(TimeSpan.FromMinutes(15), unlock.Remaining(now));
        // 冷却期内剩余时间递减。
        Assert.Equal(TimeSpan.FromMinutes(14), unlock.Remaining(now.AddMinutes(1)));
    }

    [Fact]
    public void ExpiresAfterCooldown()
    {
        var unlock = new EmergencyUnlock(TimeSpan.FromMinutes(1));
        var now = DateTimeOffset.Now;
        unlock.Trigger(now);
        Assert.False(unlock.IsActive(now.AddMinutes(2)));
        Assert.Equal(TimeSpan.Zero, unlock.Remaining(now.AddMinutes(2)));
    }

    [Fact]
    public void ReTriggerDuringActiveIsIdempotent()
    {
        var unlock = new EmergencyUnlock(TimeSpan.FromMinutes(15));
        var now = DateTimeOffset.Now;
        unlock.Trigger(now);
        unlock.Trigger(now.AddMinutes(5)); // 解锁期内再触发:不重置冷却
        Assert.Equal(TimeSpan.FromMinutes(10), unlock.Remaining(now.AddMinutes(5)));
        Assert.Equal(2, unlock.TriggerCount); // 但触发次数记录(审计)
    }

    [Fact]
    public void AfterExpiryCanTriggerAgain()
    {
        var unlock = new EmergencyUnlock(TimeSpan.FromMinutes(1));
        var now = DateTimeOffset.Now;
        unlock.Trigger(now);
        Assert.True((unlock.Trigger(now.AddMinutes(2)))); // 过期后可再触发
        Assert.True(unlock.IsActive(now.AddMinutes(2)));
    }
}

public class StartupRegistrationTests
{
    [Fact]
    public void RoundtripRegisterAndUnregister()
    {
        // 测试用值名,避免污染真实 Equora 启动项。
        const string testPath = @"C:\test\equora-test.exe";
        var valueName = $"Equora-Test-{Guid.NewGuid():N}";
        try
        {
            Assert.False(StartupRegistration.IsRegistered(valueName));
            StartupRegistration.SetRegistered(true, testPath, valueName);
            Assert.True(StartupRegistration.IsRegistered(valueName));
            StartupRegistration.SetRegistered(false, testPath, valueName);
            Assert.False(StartupRegistration.IsRegistered(valueName));
        }
        finally { StartupRegistration.SetRegistered(false, testPath, valueName); }
    }
}
