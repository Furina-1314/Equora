using System.IO;
using Equora.App.NativeInterop;
using Equora.App.Services;
using Equora.App.ViewModels;
using TaskStatus = Equora.App.NativeInterop.TaskStatus;
using Xunit;

namespace Equora.App.Tests;

/// <summary>
/// C# → C ABI → SQLite 全链路冒烟(M0 验收项:互操作边界真实可用)。
/// 依赖构建时复制的 equora_capi.dll/sqlite3.dll。
/// </summary>
public class NativeInteropTests : IDisposable
{
    private readonly AppDataService _service;

    public NativeInteropTests()
    {
        var dbPath = Path.Combine(Path.GetTempPath(),
            $"equora-test-{Guid.NewGuid():N}.db");
        _service = new AppDataService(dbPath, "device-tests");
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void PingReturnsValuePlusOne()
    {
        Assert.Equal(42, _service.Ping(41));
        Assert.Equal(0, _service.Ping(-1));
    }

    [Fact]
    public void NativeVersionIsReported()
    {
        Assert.Contains("Equora", _service.NativeVersion, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaIsMigratedToLatest()
    {
        Assert.Equal(3, _service.SchemaVersion); // schema v3
    }

    [Fact]
    public void TaskCrudRoundtrip()
    {
        var created = _service.CreateTask(new TaskDraft
        {
            Title = "写周报",
            Note = "本周进展",
            EstimateMinutes = 45,
            DueAt = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000),
        });

        Assert.NotEmpty(created.Id);
        Assert.Equal(1, created.Revision);
        Assert.Equal("写周报", created.Title);
        Assert.Equal(45, created.EstimateMinutes);
        Assert.NotNull(created.DueAt);

        var fetched = _service.GetTask(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal("本周进展", fetched!.Note);

        var done = _service.UpdateTask(fetched with { Status = TaskStatus.Done });
        Assert.Equal(2, done.Revision);
        Assert.Equal(TaskStatus.Done, _service.GetTask(created.Id)!.Status);

        _service.DeleteTask(created.Id);
        Assert.Null(_service.GetTask(created.Id));
        Assert.Equal(1, _service.ListTasks(includeDeleted: true).Count);
    }

    [Fact]
    public void StaleRevisionRaisesConflict()
    {
        var t = _service.CreateTask(new TaskDraft { Title = "并发" });
        var stale = t;
        _service.UpdateTask(t with { Title = "新标题" });

        var ex = Assert.Throws<EquoraException>(() => _service.UpdateTask(stale));
        Assert.Equal(4, ex.Code); // Conflict
    }

    [Fact]
    public void BlankTitleIsRejected()
    {
        var ex = Assert.Throws<EquoraException>(
            () => _service.CreateTask(new TaskDraft { Title = "   " }));
        Assert.Equal(2, ex.Code); // InvalidArgument
    }

    [Fact]
    public void ChineseUtf8SurvivesRoundtrip()
    {
        const string title = "专注 45 分钟:复习《模拟电子技术》§3.2 ✅";
        var t = _service.CreateTask(new TaskDraft { Title = title });
        Assert.Equal(title, _service.GetTask(t.Id)!.Title);
    }
}

public class HomeViewModelTests
{
    [Fact]
    public void RunDiagnosticsReportsNativeBoundary()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"equora-vm-{Guid.NewGuid():N}.db");
        using var service = new AppDataService(dbPath, "device-tests");
        var vm = new HomeViewModel(service);

        vm.RunDiagnosticsCommand.Execute(null);

        Assert.Equal(42, service.Ping(41));
        Assert.Equal("自检通过 ✓", vm.StatusText);
        Assert.NotEmpty(vm.Diagnostics);
    }
}
