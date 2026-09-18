using Equora.App.NativeInterop;
using Equora.App.Services;
using Xunit;

namespace Equora.App.Tests;

public class QueryDebugTests : IDisposable
{
    private readonly AppDataService _service;

    public QueryDebugTests()
    {
        _service = new AppDataService(Path.Combine(Path.GetTempPath(),
            $"equora-qd-{Guid.NewGuid():N}.db"), "device-tests");
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void TodayFilterConstruction()
    {
        var t = _service.CreateTask(new TaskDraft
        {
            Title = "今天稍后",
            DueAt = DateTimeOffset.Now.AddMinutes(30),
        });
        var today = _service.QueryTasks(new TaskQuery { SmartList = SmartListKind.Today });
        Assert.Contains(today, x => x.Id == t.Id);
    }

    [Fact]
    public void PastTaskNotInToday()
    {
        var past = _service.CreateTask(new TaskDraft
        {
            Title = "三十小时前",
            DueAt = DateTimeOffset.Now.AddHours(-30),
        });
        var today = _service.QueryTasks(new TaskQuery { SmartList = SmartListKind.Today });
        Assert.DoesNotContain(today, x => x.Id == past.Id);
    }
}
