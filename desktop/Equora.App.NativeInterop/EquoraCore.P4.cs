using System.Runtime.InteropServices;

namespace Equora.App.NativeInterop;

/// <summary>P4 扩展:项目/标签/检查项/查询/备份/导入导出/设备 ID/日志。</summary>
public sealed partial class EquoraCore
{
    // ---- 设备 ID ----

    public string DeviceId
    {
        get
        {
            var rc = NativeMethods.eq_core_device_id(_core, out var handle, out var error);
            EquoraException.ThrowIfFailed(rc, error, "device_id");
            try
            {
                return NativeMethods.eq_string_data(handle).Str() ?? "";
            }
            finally
            {
                NativeMethods.eq_string_destroy(handle);
            }
        }
    }

    // ---- 日志 ----

    public static void InitLog(string filePath, int level = 2 /* Info */)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        NativeMethods.eq_log_init(filePath, level);
    }

    // ---- 查询 ----

    // 读取批量任务列表的共享助手(与 ListTasks 相同的视图解析)。
    private IReadOnlyList<TaskDto> ReadTaskList(IntPtr list)
    {
        try
        {
            var result = new List<TaskDto>();
            var count = NativeMethods.eq_task_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_task_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                result.Add(FromView(Marshal.PtrToStructure<NativeMethods.EqTaskView>(ptr)));
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_task_list_destroy(list);
        }
    }

    public IReadOnlyList<TaskDto> QueryTasks(TaskQuery query)
    {
        using var scope = new QueryScope(query);
        var rc = NativeMethods.eq_task_query(_core, in scope.Filter, out var list,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "task_query");
        return ReadTaskList(list);
    }

    /// <summary>持有查询的原生缓冲(状态数组/字符串),Dispose 前有效。</summary>
    private sealed class QueryScope : IDisposable
    {
        private readonly List<Utf8NativeString> _strings = new();
        private readonly List<IntPtr> _buffers = new();

        public NativeMethods.EqTaskFilter Filter;

        public QueryScope(TaskQuery q)
        {
            Filter = new NativeMethods.EqTaskFilter
            {
                due_state = 0,
                sort = (int)q.Sort,
                include_deleted = q.IncludeDeleted ? 1 : 0,
                limit = q.Limit ?? 0,
                offset = q.Offset,
            };
            ApplySmartList(q);

            if (q.ProjectId is not null) Filter.project_id = PinString(q.ProjectId);
            if (q.TagId is not null) Filter.tag_id = PinString(q.TagId);
            if (!string.IsNullOrEmpty(q.Search)) Filter.search = PinString(q.Search);

            if (q.Statuses is { Count: > 0 })
            {
                Filter.statuses = PinIntArray(q.Statuses.Select(s => (int)s));
                Filter.status_count = q.Statuses.Count;
            }
            if (q.SmartList == SmartListKind.Overdue)
            {
                // 逾期 = 排除完成/取消,原生层不再重复表达。
            }
        }

        private void ApplySmartList(TaskQuery q)
        {
            if (q.SmartList is null) return;

            var now = q.NowMs();
            var offset = q.LocalOffsetMinutes();
            long dayStart = LocalDayStart(now, offset);

            switch (q.SmartList)
            {
                case SmartListKind.Inbox:
                    Filter.status_count = 0; // 通过 Statuses 已设置;此处不再处理
                    break;
                case SmartListKind.Today:
                    Filter.due_state = 1;
                    Filter.due_after = dayStart;
                    Filter.due_before = dayStart + 86_400_000;
                    Filter.sort = (int)TaskSort.DueAsc;
                    break;
                case SmartListKind.Upcoming7:
                    Filter.due_state = 1;
                    Filter.due_after = now;
                    Filter.due_before = dayStart + 7L * 86_400_000;
                    Filter.sort = (int)TaskSort.DueAsc;
                    break;
                case SmartListKind.Overdue:
                    Filter.due_state = 1;
                    Filter.due_before = now;
                    Filter.excluded_count = 2;
                    Filter.excluded_statuses = PinIntArray(new[] { 4, 5 }); // Done, Cancelled
                    Filter.sort = (int)TaskSort.DueAsc;
                    break;
                case SmartListKind.NoDate:
                    Filter.due_state = 2;
                    Filter.sort = (int)TaskSort.PriorityDesc;
                    break;
                case SmartListKind.Scheduled:
                    Filter.due_state = 1;
                    Filter.sort = (int)TaskSort.DueAsc;
                    break;
                case SmartListKind.CompletedToday:
                    Filter.updated_after = dayStart;
                    Filter.updated_before = dayStart + 86_400_000;
                    goto case SmartListKind.Completed;
                case SmartListKind.Completed:
                    Filter.statuses = PinIntArray(new[] { 4, 5 });
                    Filter.status_count = 2;
                    Filter.sort = (int)TaskSort.UpdatedDesc;
                    break;
                case SmartListKind.Waiting:
                    Filter.statuses = PinIntArray(new[] { 3 });
                    Filter.status_count = 1;
                    break;
            }
        }

        // 与原生 domain::utc::localDayStartUtc 相同的日界计算。
        private static long LocalDayStart(long nowMs, int offsetMinutes)
        {
            var shifted = nowMs + offsetMinutes * 60_000L;
            var day = (long)Math.Floor((double)shifted / 86_400_000);
            return day * 86_400_000 - offsetMinutes * 60_000L;
        }

        private nint PinString(string s)
        {
            var holder = new Utf8NativeString(s);
            _strings.Add(holder);
            return holder.Pointer;
        }

        private nint PinIntArray(IEnumerable<int> values)
        {
            var array = values.ToArray();
            var buffer = Marshal.AllocHGlobal(array.Length * sizeof(int));
            Marshal.Copy(array, 0, buffer, array.Length);
            _buffers.Add(buffer);
            return buffer;
        }

        public void Dispose()
        {
            foreach (var b in _buffers) Marshal.FreeHGlobal(b);
            _buffers.Clear();
            foreach (var s in _strings) s.Dispose();
            _strings.Clear();
        }
    }

    // ---- 项目 ----

    public ProjectDto CreateProject(string name, string color = "", string goal = "")
    {
        using var input = new ProjectInputScope(name, color, goal, revision: 0, id: null);
        var rc = NativeMethods.eq_project_create(_core, in input.Value, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "project_create");
        return ReadProjectHandle(handle);
    }

    public ProjectDto? GetProject(string id)
    {
        var rc = NativeMethods.eq_project_get(_core, id, out var handle, out var error);
        if (rc == 3) return null;
        EquoraException.ThrowIfFailed(rc, error, "project_get");
        return ReadProjectHandle(handle);
    }

    public ProjectDto UpdateProject(ProjectDto project)
    {
        using var input = new ProjectInputScope(project.Name, project.Color, project.Goal,
            project.Revision, project.Id);
        var rc = NativeMethods.eq_project_update(_core, in input.Value, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "project_update");
        return ReadProjectHandle(handle);
    }

    public ProjectDto SetProjectArchived(string id, bool archived)
    {
        var rc = NativeMethods.eq_project_set_archived(_core, id, archived ? 1 : 0,
            out var handle, out var error);
        EquoraException.ThrowIfFailed(rc, error, "project_set_archived");
        return ReadProjectHandle(handle);
    }

    public void DeleteProject(string id)
    {
        var rc = NativeMethods.eq_project_set_deleted(_core, id, 1, out _, out var error);
        EquoraException.ThrowIfFailed(rc, error, "project_set_deleted");
    }

    public IReadOnlyList<ProjectDto> ListProjects(bool includeArchived = true)
    {
        var rc = NativeMethods.eq_project_list(_core, includeArchived ? 1 : 0, 0,
            out var list, out var error);
        EquoraException.ThrowIfFailed(rc, error, "project_list");
        try
        {
            var result = new List<ProjectDto>();
            var count = NativeMethods.eq_project_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_project_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                result.Add(FromProjectView(
                    Marshal.PtrToStructure<NativeMethods.EqProjectView>(ptr)));
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_project_list_destroy(list);
        }
    }

    private static ProjectDto ReadProjectHandle(IntPtr handle)
    {
        try
        {
            var ptr = NativeMethods.eq_project_view(handle);
            if (ptr == IntPtr.Zero) throw new EquoraException(1, "project view unavailable");
            return FromProjectView(Marshal.PtrToStructure<NativeMethods.EqProjectView>(ptr));
        }
        finally
        {
            NativeMethods.eq_project_handle_destroy(handle);
        }
    }

    private static ProjectDto FromProjectView(NativeMethods.EqProjectView v) => new()
    {
        Id = v.Id.Str() ?? "",
        Name = v.Name.Str() ?? "",
        Color = v.Color.Str() ?? "",
        Goal = v.Goal.Str() ?? "",
        Status = (ProjectStatusDto)v.Status,
        ArchivedAt = v.HasArchived != 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(v.ArchivedAt) : null,
        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(v.CreatedAt),
        UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(v.UpdatedAt),
        Revision = v.Revision,
        IsDeleted = v.HasDeleted != 0,
        LastDeviceId = v.LastDeviceId.Str() ?? "",
    };

    private sealed class ProjectInputScope : IDisposable
    {
        private readonly List<Utf8NativeString> _owned = new();
        public NativeMethods.EqProjectInput Value;

        public ProjectInputScope(string name, string color, string goal, long revision,
            string? id)
        {
            Value = new NativeMethods.EqProjectInput
            {
                Id = Pin(id),
                Name = Pin(name),
                Color = Pin(string.IsNullOrEmpty(color) ? null : color),
                Goal = Pin(string.IsNullOrEmpty(goal) ? null : goal),
                Status = 0,
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
            foreach (var h in _owned) h.Dispose();
            _owned.Clear();
        }
    }

    // ---- 标签 ----

    public TagDto CreateTag(string name, string color = "")
    {
        using var input = new TagInputScope(name, color, 0, null);
        var rc = NativeMethods.eq_tag_create(_core, in input.Value, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "tag_create");
        return ReadTagHandle(handle);
    }

    public TagDto? GetTag(string id)
    {
        var rc = NativeMethods.eq_tag_get(_core, id, out var handle, out var error);
        if (rc == 3) return null;
        EquoraException.ThrowIfFailed(rc, error, "tag_get");
        return ReadTagHandle(handle);
    }

    public TagDto UpdateTag(TagDto tag)
    {
        using var input = new TagInputScope(tag.Name, tag.Color, tag.Revision, tag.Id);
        var rc = NativeMethods.eq_tag_update(_core, in input.Value, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "tag_update");
        return ReadTagHandle(handle);
    }

    public void DeleteTag(string id)
    {
        var rc = NativeMethods.eq_tag_set_deleted(_core, id, 1, out _, out var error);
        EquoraException.ThrowIfFailed(rc, error, "tag_set_deleted");
    }

    public IReadOnlyList<TagDto> ListTags()
    {
        var rc = NativeMethods.eq_tag_list(_core, 0, out var list, out var error);
        EquoraException.ThrowIfFailed(rc, error, "tag_list");
        try
        {
            var result = new List<TagDto>();
            var count = NativeMethods.eq_tag_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_tag_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                result.Add(FromTagView(Marshal.PtrToStructure<NativeMethods.EqTagView>(ptr)));
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_tag_list_destroy(list);
        }
    }

    public void AddTagToTask(string taskId, string tagId)
    {
        var rc = NativeMethods.eq_task_add_tag(_core, taskId, tagId, out var error);
        EquoraException.ThrowIfFailed(rc, error, "task_add_tag");
    }

    public void RemoveTagFromTask(string taskId, string tagId)
    {
        var rc = NativeMethods.eq_task_remove_tag(_core, taskId, tagId, out var error);
        EquoraException.ThrowIfFailed(rc, error, "task_remove_tag");
    }

    public IReadOnlyList<TagDto> TagsForTask(string taskId)
    {
        var rc = NativeMethods.eq_task_tags(_core, taskId, out var list, out var error);
        EquoraException.ThrowIfFailed(rc, error, "task_tags");
        try
        {
            var result = new List<TagDto>();
            var count = NativeMethods.eq_tag_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_tag_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                result.Add(FromTagView(Marshal.PtrToStructure<NativeMethods.EqTagView>(ptr)));
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_tag_list_destroy(list);
        }
    }

    private static TagDto ReadTagHandle(IntPtr handle)
    {
        try
        {
            var ptr = NativeMethods.eq_tag_view(handle);
            if (ptr == IntPtr.Zero) throw new EquoraException(1, "tag view unavailable");
            return FromTagView(Marshal.PtrToStructure<NativeMethods.EqTagView>(ptr));
        }
        finally
        {
            NativeMethods.eq_tag_handle_destroy(handle);
        }
    }

    private static TagDto FromTagView(NativeMethods.EqTagView v) => new()
    {
        Id = v.Id.Str() ?? "",
        Name = v.Name.Str() ?? "",
        Color = v.Color.Str() ?? "",
        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(v.CreatedAt),
        UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(v.UpdatedAt),
        Revision = v.Revision,
        IsDeleted = v.HasDeleted != 0,
    };

    private sealed class TagInputScope : IDisposable
    {
        private readonly List<Utf8NativeString> _owned = new();
        public NativeMethods.EqTagInput Value;

        public TagInputScope(string name, string color, long revision, string? id)
        {
            Value = new NativeMethods.EqTagInput
            {
                Id = Pin(id),
                Name = Pin(name),
                Color = Pin(string.IsNullOrEmpty(color) ? null : color),
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
            foreach (var h in _owned) h.Dispose();
            _owned.Clear();
        }
    }

    // ---- 检查项 ----

    public ChecklistItemDto AddChecklistItem(string taskId, string content)
    {
        var rc = NativeMethods.eq_checklist_add(_core, taskId, content, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "checklist_add");
        return ReadChecklistHandle(handle);
    }

    public ChecklistItemDto UpdateChecklistItem(ChecklistItemDto item)
    {
        using var id = new Utf8NativeString(item.Id);
        using var taskId = new Utf8NativeString(item.TaskId);
        using var content = new Utf8NativeString(item.Content);
        var input = new NativeMethods.EqChecklistInput
        {
            Id = id.Pointer,
            TaskId = taskId.Pointer,
            Content = content.Pointer,
            IsChecked = item.IsChecked ? 1 : 0,
            SortOrder = item.SortOrder,
            Revision = item.Revision,
        };
        var rc = NativeMethods.eq_checklist_update(_core, in input, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "checklist_update");
        return ReadChecklistHandle(handle);
    }

    public void RemoveChecklistItem(string id)
    {
        var rc = NativeMethods.eq_checklist_remove(_core, id, out var error);
        EquoraException.ThrowIfFailed(rc, error, "checklist_remove");
    }

    public IReadOnlyList<ChecklistItemDto> ListChecklist(string taskId)
    {
        var rc = NativeMethods.eq_checklist_list(_core, taskId, out var list, out var error);
        EquoraException.ThrowIfFailed(rc, error, "checklist_list");
        try
        {
            var result = new List<ChecklistItemDto>();
            var count = NativeMethods.eq_checklist_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_checklist_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                result.Add(FromChecklistView(
                    Marshal.PtrToStructure<NativeMethods.EqChecklistView>(ptr)));
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_checklist_list_destroy(list);
        }
    }

    private static ChecklistItemDto ReadChecklistHandle(IntPtr handle)
    {
        try
        {
            var ptr = NativeMethods.eq_checklist_view(handle);
            if (ptr == IntPtr.Zero) throw new EquoraException(1, "checklist view unavailable");
            return FromChecklistView(
                Marshal.PtrToStructure<NativeMethods.EqChecklistView>(ptr));
        }
        finally
        {
            NativeMethods.eq_checklist_handle_destroy(handle);
        }
    }

    private static ChecklistItemDto FromChecklistView(NativeMethods.EqChecklistView v) => new()
    {
        Id = v.Id.Str() ?? "",
        TaskId = v.TaskId.Str() ?? "",
        Content = v.Content.Str() ?? "",
        IsChecked = v.IsChecked != 0,
        SortOrder = v.SortOrder,
        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(v.CreatedAt),
        UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(v.UpdatedAt),
        Revision = v.Revision,
    };

    // ---- 备份与导入导出 ----

    /// <summary>创建一致性备份,返回备份文件路径。</summary>
    public string CreateBackup(string directory)
    {
        var rc = NativeMethods.eq_backup_create(_core, directory, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "backup_create");
        try
        {
            return NativeMethods.eq_string_data(handle).Str() ?? "";
        }
        finally
        {
            NativeMethods.eq_string_destroy(handle);
        }
    }

    public bool VerifyBackup(string path)
    {
        var rc = NativeMethods.eq_backup_verify(path, out var ok, out var error);
        EquoraException.ThrowIfFailed(rc, error, "backup_verify");
        return ok != 0;
    }

    public void RestoreBackup(string path)
    {
        var rc = NativeMethods.eq_backup_restore(_core, path, out var error);
        EquoraException.ThrowIfFailed(rc, error, "backup_restore");
    }

    public int ExportTasksJson(string path)
    {
        var rc = NativeMethods.eq_export_tasks_json(_core, path, out var count,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "export_json");
        return count;
    }

    public int ExportTasksCsv(string path)
    {
        var rc = NativeMethods.eq_export_tasks_csv(_core, path, out var count,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "export_csv");
        return count;
    }

    public (int Imported, int Skipped) ImportTasksJson(string path)
    {
        var rc = NativeMethods.eq_import_tasks_json(_core, path, out var imported,
            out var skipped, out var error);
        EquoraException.ThrowIfFailed(rc, error, "import_json");
        return (imported, skipped);
    }
}
