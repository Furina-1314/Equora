using Equora.App.NativeInterop;
using Equora.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TaskStatus = Equora.App.NativeInterop.TaskStatus;

namespace Equora.App.Controls;

/// <summary>
/// 快速收集:输入 → 解析预览(可编辑)→ 提交。解析永远保留原文:
/// 识别不出的部分作为任务标题,绝不丢弃内容。
/// </summary>
public sealed partial class QuickCaptureFlyout : UserControl
{
    private CaptureDraft? _draft;

    public QuickCaptureFlyout()
    {
        InitializeComponent();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _draft = QuickCaptureParser.Parse(InputBox.Text, DateTimeOffset.Now);
        TitlePreview.Text = _draft.Title.Length == 0 ? "(原文将作为标题)" : _draft.Title;
        FieldsPreview.Text = _draft.Preview();
        StatusLine.Text = "";
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            Submit();
            e.Handled = true;
        }
    }

    public void Submit()
    {
        var text = InputBox.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        var draft = _draft ?? QuickCaptureParser.Parse(text, DateTimeOffset.Now);

        // 重复文本暂存于备注(任务级重复展开在 P12 智能规划接入),保证信息不丢。
        var note = draft.RecurrenceText is not null ? $"重复:{draft.RecurrenceText}" : "";

        var created = AppServices.Data.CreateTask(new TaskDraft
        {
            Title = string.IsNullOrWhiteSpace(draft.Title) ? text.Trim() : draft.Title,
            Note = note,
            DueAt = draft.DueAt,
            EstimateMinutes = draft.EstimateMinutes,
            Priority = (Priority)draft.PriorityValue,
            Status = draft.DueAt is null ? TaskStatus.Inbox : TaskStatus.Planned,
        });

        foreach (var tagName in draft.Tags)
        {
            var tag = AppServices.Data.ListTags().FirstOrDefault(t => t.Name == tagName)
                      ?? AppServices.Data.CreateTag(tagName);
            AppServices.Data.AddTagToTask(created.Id, tag.Id);
        }

        AppServices.Tasks.Refresh();
        AppServices.Calendar.Refresh();
        StatusLine.Text = $"已收集:{created.Title}";
        InputBox.Text = "";
        _draft = null;
        TitlePreview.Text = "";
        FieldsPreview.Text = "";
    }

    /// <summary>在主窗口弹出快速收集(Ctrl+Shift+Space / Ctrl+N)。</summary>
    public static async Task ShowAsync(Window owner)
    {
        var control = new QuickCaptureFlyout();
        var dialog = new ContentDialog
        {
            Title = "快速收集",
            Content = control,
            PrimaryButtonText = "提交",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = owner.Content is FrameworkElement root ? root.XamlRoot : null,
        };
        dialog.PrimaryButtonClick += (_, _) => control.Submit();
        await dialog.ShowAsync();
    }
}
