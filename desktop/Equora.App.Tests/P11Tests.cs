using Equora.App.NativeInterop;
using Xunit;

namespace Equora.App.Tests;

/// <summary>P11:Native Messaging 帧编解码、校验和、限制状态映射与域名匹配。</summary>
public class NativeMessagingTests
{
    [Fact]
    public void FrameRoundtripPreservesJson()
    {
        var message = new { type = "query", protocolVersion = 1, nonce = 42UL };
        var frame = NativeMessaging.Encode(message);

        // 长度前缀为小端。
        var length = frame[0] | (frame[1] << 8) | (frame[2] << 16) | (frame[3] << 24);
        Assert.Equal(frame.Length - 4, length);

        var doc = NativeMessaging.Decode(frame);
        Assert.NotNull(doc);
        Assert.Equal("query", doc!.RootElement.GetProperty("type").GetString());
        Assert.Equal(42UL, doc.RootElement.GetProperty("nonce").GetUInt64());
    }

    [Fact]
    public void DecodeRejectsBadFrames()
    {
        Assert.Null(NativeMessaging.Decode(new byte[3]));                 // 太短
        Assert.Null(NativeMessaging.Decode(new byte[] { 0xFF, 0, 0, 0 })); // 超限长度
        var huge = new byte[8];
        huge[0] = 0x10; // 声明 16 字节但实际没有
        Assert.Null(NativeMessaging.Decode(huge));
    }

    [Fact]
    public void ChecksumIsDeterministicAndSensitive()
    {
        Assert.Equal(NativeMessaging.Checksum("{\"a\":1}"),
            NativeMessaging.Checksum("{\"a\":1}"));
        Assert.NotEqual(NativeMessaging.Checksum("{\"a\":1}"),
            NativeMessaging.Checksum("{\"a\":2}"));
    }
}

public class FocusGateTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 19, 9, 0, 0, TimeSpan.FromHours(8));

    private static FocusSessionDto RunningSession(string? plannedEnd = null) => new()
    {
        Id = "session-1",
        Mode = FocusModeDto.Pomodoro,
        PlannedStart = Start,
        PlannedEnd = plannedEnd is null
            ? null
            : DateTimeOffset.Parse(plannedEnd),
        ActualStart = Start,
        State = SessionStateDto.Running,
    };

    [Fact]
    public void NoSessionMeansNotFocusing()
    {
        var state = FocusGate.FromSession(null, null, null, null);
        Assert.False(state.Focusing);
        Assert.Empty(state.BlockedDomains);
    }

    [Fact]
    public void RunningSessionWithProfileProducesGate()
    {
        var state = FocusGate.FromSession(RunningSession(), "写方案",
            profileAllowedSites: "[\"docs.example.com\"]",
            profileBlockedSites: "[\"weibo.com\", \"https://www.bilibili.com/video\", \"weibo.com\"]");
        Assert.True(state.Focusing);
        Assert.Equal("session-1", state.SessionId);
        Assert.Equal("写方案", state.TaskTitle);
        Assert.Equal(new[] { "docs.example.com" }, state.AllowedDomains);
        // 规范化:去协议/路径/www、去重。
        Assert.Equal(new[] { "bilibili.com", "weibo.com" },
            state.BlockedDomains.OrderBy(d => d).ToArray());
    }

    [Fact]
    public void InvalidProfileJsonFallsBackToEmpty()
    {
        var state = FocusGate.FromSession(RunningSession(), null,
            profileAllowedSites: "{not json", profileBlockedSites: null);
        Assert.True(state.Focusing);
        Assert.Empty(state.BlockedDomains);
    }
}

public class DomainMatchingTests
{
    private static readonly string[] Allowed = { "docs.example.com" };
    private static readonly string[] Blocked = { "weibo.com", "v.qq.com" };

    [Fact]
    public void SubdomainsMatchAndAllowWins()
    {
        Assert.True(FocusGate.IsBlocked("m.weibo.com", Array.Empty<string>(), Blocked));
        Assert.True(FocusGate.IsBlocked("weibo.com", Array.Empty<string>(), Blocked));
        Assert.True(FocusGate.IsBlocked("v.qq.com/x", Allowed, Blocked));
        Assert.False(FocusGate.IsBlocked("docs.example.com/wiki", Allowed, Blocked));
        Assert.False(FocusGate.IsBlocked("sub.docs.example.com", Allowed, Blocked));
    }

    [Fact]
    public void NoFalsePositives()
    {
        Assert.False(FocusGate.IsBlocked("notweibo.com", Allowed, Blocked));
        Assert.False(FocusGate.IsBlocked("qq.com", Allowed, Blocked)); // 仅 v.qq.com 受限
        Assert.False(FocusGate.IsBlocked("example.com", Allowed, Blocked));
    }

    [Fact]
    public void NormalizeHandlesSchemePathPortAndWww()
    {
        Assert.Equal("example.com", FocusGate.Normalize("https://www.Example.com/path?q=1"));
        Assert.Equal("example.com", FocusGate.Normalize("example.com:8080"));
        Assert.Equal("a.b.c", FocusGate.Normalize("a.b.c"));
    }
}
