using Equora.App.Services;
using Equora.App.ViewModels;

namespace Equora.App;

/// <summary>
/// 轻量服务定位:应用级单例。
/// 数据服务持有原生上下文;撤销栈按页面会话隔离。
/// </summary>
internal static class AppServices
{
    public static AppDataService Data { get; } =
        new(AppPaths.DatabasePath, Environment.MachineName);

    public static IUndoService Undo { get; } = new UndoService();

    public static TasksViewModel Tasks { get; } = new(Data, Data, Undo);

    public static CalendarViewModel Calendar { get; } = new(Data, Data, Undo);

    public static HomeViewModel Home { get; } = new(Data);
}
