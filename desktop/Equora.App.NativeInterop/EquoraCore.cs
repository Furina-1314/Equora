using System.Runtime.InteropServices;

namespace Equora.App.NativeInterop;

/// <summary>
/// 原生核心(EqCore)的安全封装:负责句柄生命周期、DTO 转换与错误翻译。
/// 同一实例非线程安全 —— 与 UI 线程绑定使用;跨线程场景由上层排队。
/// </summary>
public sealed partial class EquoraCore : IDisposable
{
    private IntPtr _core;

    private EquoraCore(IntPtr core) => _core = core;

    /// <summary>原生层是否可用(冒烟检测,在加载 DLL 失败时给出清晰错误)。</summary>
    public static string GetNativeVersion()
    {
        var p = NativeMethods.eq_version_string();
        return p == IntPtr.Zero ? "<null>" : Marshal.PtrToStringUTF8(p) ?? "<null>";
    }

    public static int Ping(int value) => NativeMethods.eq_ping(value);

    /// <summary>打开(必要时创建)数据库并自动应用迁移。</summary>
    public static EquoraCore Open(string dbPath, string? deviceId = null)
    {
        var core = NativeMethods.eq_core_create(dbPath, deviceId ?? "local",
            out var error);
        if (core == IntPtr.Zero)
        {
            throw new EquoraException(error.Code, $"open '{dbPath}': {error.MessageText}");
        }
        return new EquoraCore(core);
    }

    public int SchemaVersion
    {
        get
        {
            var rc = NativeMethods.eq_core_schema_version(_core, out var version, out var error);
            EquoraException.ThrowIfFailed(rc, error, "schema_version");
            return version;
        }
    }

    public TaskDto CreateTask(TaskDraft draft)
    {
        using var input = new NativeInputScope(draft, revision: 0, id: null);
        var rc = NativeMethods.eq_task_create(_core, in input.Value, out var handle, out var error);
        EquoraException.ThrowIfFailed(rc, error, "task_create");
        return ReadHandle(handle);
    }

    public TaskDto? GetTask(string id, bool includeDeleted = false)
    {
        var rc = NativeMethods.eq_task_get(_core, id, includeDeleted ? 1 : 0,
            out var handle, out var error);
        if (rc == 3 /* NotFound */)
        {
            return null;
        }
        EquoraException.ThrowIfFailed(rc, error, "task_get");
        return ReadHandle(handle);
    }

    /// <summary>乐观并发更新:任务的 Revision 必须等于库中当前值。</summary>
    public TaskDto UpdateTask(TaskDto task)
    {
        var draft = new TaskDraft
        {
            Title = task.Title,
            Note = task.Note,
            Status = task.Status,
            Priority = task.Priority,
            Importance = task.Importance,
            DueAt = task.DueAt,
            EstimateMinutes = task.EstimateMinutes,
            ProjectId = task.ProjectId,
        };
        var input = new NativeInputScope(draft, revision: task.Revision, id: task.Id);
        using (input)
        {
            var rc = NativeMethods.eq_task_update(_core, in input.Value, out var handle,
                out var error);
            EquoraException.ThrowIfFailed(rc, error, "task_update");
            return ReadHandle(handle);
        }
    }

    public void DeleteTask(string id) => SetDeleted(id, true);

    public void RestoreTask(string id) => SetDeleted(id, false);

    private void SetDeleted(string id, bool deleted)
    {
        var rc = NativeMethods.eq_task_set_deleted(_core, id, deleted ? 1 : 0,
            out _, out var error);
        EquoraException.ThrowIfFailed(rc, error, "task_set_deleted");
    }

    public IReadOnlyList<TaskDto> ListTasks(bool includeDeleted = false)
    {
        var rc = NativeMethods.eq_task_list_all(_core, includeDeleted ? 1 : 0,
            out var list, out var error);
        EquoraException.ThrowIfFailed(rc, error, "task_list_all");

        var result = new List<TaskDto>();
        try
        {
            var count = NativeMethods.eq_task_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var viewPtr = NativeMethods.eq_task_list_get(list, i);
                if (viewPtr == IntPtr.Zero) continue;
                result.Add(FromView(Marshal.PtrToStructure<NativeMethods.EqTaskView>(viewPtr)));
            }
        }
        finally
        {
            NativeMethods.eq_task_list_destroy(list);
        }
        return result;
    }

    private static TaskDto ReadHandle(IntPtr handle)
    {
        try
        {
            var viewPtr = NativeMethods.eq_task_view(handle);
            if (viewPtr == IntPtr.Zero)
            {
                throw new EquoraException(1, "task view unavailable");
            }
            return FromView(Marshal.PtrToStructure<NativeMethods.EqTaskView>(viewPtr));
        }
        finally
        {
            NativeMethods.eq_task_handle_destroy(handle);
        }
    }

    private static TaskDto FromView(NativeMethods.EqTaskView v) => new()
    {
        Id = v.String(v.Id) ?? "",
        Title = v.String(v.Title) ?? "",
        Note = v.String(v.Note) ?? "",
        Status = (TaskStatus)v.Status,
        Priority = (Priority)v.Priority,
        Importance = v.Importance,
        DueAt = v.HasDue != 0 ? DateTimeOffset.FromUnixTimeMilliseconds(v.DueAt) : null,
        EstimateMinutes = v.HasEstimate != 0 ? v.EstimateMinutes : null,
        ActualMinutes = v.ActualMinutes,
        ProjectId = v.HasProject != 0 ? v.String(v.ProjectId) : null,
        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(v.CreatedAt),
        UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(v.UpdatedAt),
        Revision = v.Revision,
        IsDeleted = v.HasDeleted != 0,
        LastDeviceId = v.String(v.LastDeviceId) ?? "",
    };

    /// <summary>
    /// 任务输入的 UTF-8 非托管缓冲作用域:构造 EqTaskInput 并持有其字符串内存,
    /// Dispose 前指针有效 —— 原生调用必须在 using 作用域内完成。
    /// </summary>
    private sealed class NativeInputScope : IDisposable
    {
        private readonly List<Utf8NativeString> _owned = new();

        public NativeMethods.EqTaskInput Value;

        public NativeInputScope(TaskDraft d, long revision, string? id)
        {
            Value = new NativeMethods.EqTaskInput
            {
                Id = Pin(id),
                Title = Pin(d.Title),
                Note = Pin(string.IsNullOrEmpty(d.Note) ? null : d.Note),
                Status = (int)d.Status,
                Priority = (int)d.Priority,
                Importance = d.Importance,
                DueAt = d.DueAt?.ToUnixTimeMilliseconds() ?? 0,
                HasDue = d.DueAt is not null ? 1 : 0,
                EstimateMinutes = d.EstimateMinutes ?? 0,
                HasEstimate = d.EstimateMinutes is not null ? 1 : 0,
                ActualMinutes = 0,
                ProjectId = Pin(d.ProjectId),
                HasProject = d.ProjectId is not null ? 1 : 0,
                Revision = revision,
            };
        }

        private nint Pin(string? s)
        {
            if (s is null) return 0;
            var holder = new Utf8NativeString(s);
            _owned.Add(holder);
            return holder.Pointer;
        }

        public void Dispose()
        {
            foreach (var holder in _owned) holder.Dispose();
            _owned.Clear();
        }
    }

    /// <summary>UTF-8 非托管字符串的临时持有者,Dispose 后指针失效。</summary>
    private sealed class Utf8NativeString : IDisposable
    {
        public IntPtr Pointer { get; }

        public Utf8NativeString(string s)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            Pointer = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, Pointer, bytes.Length);
            Marshal.WriteByte(Pointer, bytes.Length, 0);
        }

        public void Dispose() => Marshal.FreeHGlobal(Pointer);
    }

    public void Dispose()
    {
        var core = Interlocked.Exchange(ref _core, IntPtr.Zero);
        if (core != IntPtr.Zero) NativeMethods.eq_core_destroy(core);
    }
}
