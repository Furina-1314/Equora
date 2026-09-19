namespace Equora.App.Services;

/// <summary>前台应用限制匹配(纯函数,可测):进程名 ↔ 黑/白名单。</summary>
public static class AppRuleMatcher
{
    /// <summary>判断进程是否受限。白名单优先于黑名单;黑名单为空 = 不限制。</summary>
    public static bool IsBlocked(string processName, IReadOnlyList<string> allowed,
        IReadOnlyList<string> blocked)
    {
        var name = processName.ToLowerInvariant();
        foreach (var a in allowed)
        {
            if (Match(name, a)) return false;
        }
        foreach (var b in blocked)
        {
            if (Match(name, b)) return true;
        }
        return false;
    }

    /// <summary>支持精确名(game.exe)或无扩展名(game 匹配 game.exe);不做子串匹配。</summary>
    private static bool Match(string processName, string rule)
    {
        var r = rule.Trim().ToLowerInvariant();
        if (r.Length == 0) return false;
        var bare = processName.EndsWith(".exe") ? processName[..^4] : processName;
        var ruleBare = r.EndsWith(".exe") ? r[..^4] : r;
        return processName == r || bare == ruleBare;
    }
}
