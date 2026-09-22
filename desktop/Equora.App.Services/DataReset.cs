namespace Equora.App.Services;

public static class DataReset
{
    public static async Task<bool> ConfirmAsync(Func<int, Task<bool>> confirm)
    {
        for (var step = 1; step <= 3; step++) if (!await confirm(step)) return false;
        return true;
    }

    // Move only files owned by Equora. The user-selected directory may contain other files.
    // Keeping a recovery snapshot also allows the complete move to roll back on a locked file.
    public static string Clear(string dataDirectory)
    {
        var root = Path.GetFullPath(dataDirectory);
        string[] names = ["equora.db", "equora.db-wal", "equora.db-shm", "preferences.json", "preferences.json.tmp",
            "restrictions.json", "restriction-heartbeat.json", "browser-heartbeat.json", "app-usage.json", "website-usage.json"];
        var backup = Path.Combine(root, "backups", "before-reset-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        var moved = new List<(string Source, string Destination)>();
        try
        {
            foreach (var name in names)
            {
                var source = Path.Combine(root, name);
                if (!File.Exists(source)) continue;
                var destination = Path.Combine(backup, name);
                File.Move(source, destination);
                moved.Add((source, destination));
            }
        }
        catch (Exception original)
        {
            var errors = new List<Exception> { original };
            foreach (var pair in moved.AsEnumerable().Reverse())
                try { File.Move(pair.Destination, pair.Source); } catch (Exception ex) { errors.Add(ex); }
            if (errors.Count > 1) throw new AggregateException($"清空未完成，恢复副本保留在 {backup}。", errors);
            throw;
        }
        return backup;
    }
}
