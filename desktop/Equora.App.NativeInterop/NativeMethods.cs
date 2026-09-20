using System.Runtime.InteropServices;

namespace Equora.App.NativeInterop;

// 与 native/Schedule.CApi/include/equora/capi/equora_capi.h 严格对应的互操作定义。
// 字段顺序与类型不得改动 —— 布局即 ABI。
internal static partial class NativeMethods
{
    public const int EqErrorCapacity = 256;

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct EqError
    {
        public int Code;
        public fixed byte Message[EqErrorCapacity];

        public readonly string MessageText
        {
            get
            {
                fixed (byte* p = Message)
                {
                    int len = 0;
                    while (len < EqErrorCapacity && p[len] != 0) len++;
                    return System.Text.Encoding.UTF8.GetString(p, len);
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqTaskInput
    {
        public nint Id;          // char* 可空
        public nint Title;       // char* 必填
        public nint Note;        // char* 可空
        public int Status;
        public int Priority;
        public int Importance;
        public long DueAt;
        public int HasDue;
        public int EstimateMinutes;
        public int HasEstimate;
        public int ActualMinutes;
        public nint ProjectId;   // char*,HasProject 为 0 时忽略
        public int HasProject;
        public long Revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqTaskView
    {
        public nint Id;
        public nint Title;
        public nint Note;
        public int Status;
        public int Priority;
        public int Importance;
        public long DueAt;
        public int HasDue;
        public int EstimateMinutes;
        public int HasEstimate;
        public int ActualMinutes;
        public nint ProjectId;
        public int HasProject;
        public long CreatedAt;
        public long UpdatedAt;
        public long Revision;
        public long DeletedAt;
        public int HasDeleted;
        public nint LastDeviceId;

        public readonly string? String(nint p) => p == 0 ? null : Marshal.PtrToStringUTF8(p);
    }

    [LibraryImport("equora_capi")]
    internal static partial int eq_api_version();

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr eq_version_string();

    [LibraryImport("equora_capi")]
    internal static partial int eq_ping(int value);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr eq_core_create(string? dbPathUtf8, string? deviceIdUtf8,
        out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial void eq_core_destroy(IntPtr core);

    [LibraryImport("equora_capi")]
    internal static partial int eq_core_schema_version(IntPtr core, out int version, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_task_view(IntPtr handle);

    [LibraryImport("equora_capi")]
    internal static partial void eq_task_handle_destroy(IntPtr handle);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_task_create(IntPtr core, in EqTaskInput input,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_task_get(IntPtr core, string idUtf8, int includeDeleted,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_task_update(IntPtr core, in EqTaskInput input,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_task_set_deleted(IntPtr core, string idUtf8, int deleted,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_task_permanently_delete(IntPtr core, string idUtf8, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_task_list_all(IntPtr core, int includeDeleted,
        out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_task_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_task_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_task_list_destroy(IntPtr list);
}
