using Equora.App.Services;
using Equora.App.ViewModels;

namespace Equora.App;

/// <summary>
/// 轻量服务定位(P3):应用级单例。
/// P4 引入正式日志与配置后,再考虑替换为 Microsoft.Extensions.DependencyInjection。
/// </summary>
internal static class AppServices
{
    public static AppDataService Data { get; } =
        new(AppPaths.DatabasePath, Environment.MachineName);

    public static HomeViewModel Home { get; } = new(Data);
}
