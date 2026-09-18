using System.Runtime.InteropServices;

namespace Equora.App.NativeInterop;

// P4 新增 ABI 的互操作定义(布局与 equora_capi.h 严格对应,字段顺序不得改动)。
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct EqTaskFilter
    {
        public int due_state;
        public long due_after;
        public long due_before;
        public long updated_after;
        public long updated_before;
        public nint project_id;
        public nint tag_id;
        public nint search;
        public int status_count;
        public nint statuses;
        public int excluded_count;
        public nint excluded_statuses;
        public int include_deleted;
        public int sort;
        public int limit;
        public int offset;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqProjectInput
    {
        public nint Id;
        public nint Name;
        public nint Color;
        public nint Goal;
        public int Status;
        public long Revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqProjectView
    {
        public nint Id;
        public nint Name;
        public nint Color;
        public nint Goal;
        public int Status;
        public long ArchivedAt;
        public int HasArchived;
        public long CreatedAt;
        public long UpdatedAt;
        public long Revision;
        public long DeletedAt;
        public int HasDeleted;
        public nint LastDeviceId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqTagInput
    {
        public nint Id;
        public nint Name;
        public nint Color;
        public long Revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqTagView
    {
        public nint Id;
        public nint Name;
        public nint Color;
        public long CreatedAt;
        public long UpdatedAt;
        public long Revision;
        public long DeletedAt;
        public int HasDeleted;
        public nint LastDeviceId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqChecklistInput
    {
        public nint Id;
        public nint TaskId;
        public nint Content;
        public int IsChecked;
        public int SortOrder;
        public long Revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqChecklistView
    {
        public nint Id;
        public nint TaskId;
        public nint Content;
        public int IsChecked;
        public int SortOrder;
        public long CreatedAt;
        public long UpdatedAt;
        public long Revision;
        public long DeletedAt;
        public int HasDeleted;
        public nint LastDeviceId;
    }

    internal static string? Str(this nint p) => p == 0 ? null : Marshal.PtrToStringUTF8(p);

    // ---- 查询 / 字符串句柄 / 设备 ID / 日志 ----

    [LibraryImport("equora_capi")]
    internal static partial int eq_task_query(IntPtr core, in EqTaskFilter filter,
        out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_string_data(IntPtr handle);

    [LibraryImport("equora_capi")]
    internal static partial void eq_string_destroy(IntPtr handle);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_core_device_id(IntPtr core, out IntPtr handle,
        out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_log_init(string filePathUtf8, int level);

    [LibraryImport("equora_capi")]
    internal static partial int eq_log_shutdown();

    // ---- 项目 ----

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_project_create(IntPtr core, in EqProjectInput input,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_project_get(IntPtr core, string idUtf8,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_project_update(IntPtr core, in EqProjectInput input,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_project_set_archived(IntPtr core, string idUtf8,
        int archived, out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_project_set_deleted(IntPtr core, string idUtf8,
        int deleted, out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_project_list(IntPtr core, int includeArchived,
        int includeDeleted, out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_project_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_project_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_project_list_destroy(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_project_view(IntPtr handle);

    [LibraryImport("equora_capi")]
    internal static partial void eq_project_handle_destroy(IntPtr handle);

    // ---- 标签 ----

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_tag_create(IntPtr core, in EqTagInput input,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_tag_get(IntPtr core, string idUtf8,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_tag_update(IntPtr core, in EqTagInput input,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_tag_set_deleted(IntPtr core, string idUtf8, int deleted,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_tag_list(IntPtr core, int includeDeleted,
        out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_tag_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_tag_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_tag_list_destroy(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_tag_view(IntPtr handle);

    [LibraryImport("equora_capi")]
    internal static partial void eq_tag_handle_destroy(IntPtr handle);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_task_add_tag(IntPtr core, string taskIdUtf8,
        string tagIdUtf8, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_task_remove_tag(IntPtr core, string taskIdUtf8,
        string tagIdUtf8, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_task_tags(IntPtr core, string taskIdUtf8,
        out IntPtr list, out EqError error);

    // ---- 检查项 ----

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_checklist_add(IntPtr core, string taskIdUtf8,
        string contentUtf8, out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_checklist_update(IntPtr core, in EqChecklistInput input,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_checklist_remove(IntPtr core, string idUtf8,
        out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_checklist_list(IntPtr core, string taskIdUtf8,
        out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_checklist_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_checklist_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_checklist_view(IntPtr handle);

    [LibraryImport("equora_capi")]
    internal static partial void eq_checklist_handle_destroy(IntPtr handle);

    [LibraryImport("equora_capi")]
    internal static partial void eq_checklist_list_destroy(IntPtr list);

    // ---- 备份与导入导出 ----

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_backup_create(IntPtr core, string dirUtf8,
        out IntPtr pathHandle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_backup_verify(string pathUtf8, out int ok,
        out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_backup_restore(IntPtr core, string pathUtf8,
        out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_export_tasks_json(IntPtr core, string pathUtf8,
        out int count, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_export_tasks_csv(IntPtr core, string pathUtf8,
        out int count, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_import_tasks_json(IntPtr core, string pathUtf8,
        out int imported, out int skipped, out EqError error);
}
