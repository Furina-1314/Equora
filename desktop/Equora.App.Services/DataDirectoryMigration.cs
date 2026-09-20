namespace Equora.App.Services;

public static class DataDirectoryMigration
{
    public static void CopySnapshot(string source, string destination, Func<string, string> backup)
    {
        source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        destination = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        if (destination.Equals(source, StringComparison.OrdinalIgnoreCase) || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请选择当前数据目录之外的空文件夹。");
        Directory.CreateDirectory(destination);
        if (Directory.EnumerateFileSystemEntries(destination).Any()) throw new ArgumentException("目标文件夹必须为空，以免覆盖已有数据。");
        var snapshot = backup(destination);
        var database = Path.Combine(destination, "equora.db");
        if (!Path.GetFullPath(snapshot).Equals(database, StringComparison.OrdinalIgnoreCase)) File.Move(snapshot, database);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("equora.db", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tmp") || name is "data-location.txt" or "restriction-heartbeat.json") continue;
            File.Copy(file, Path.Combine(destination, name));
        }
        var backups = Path.Combine(source, "backups");
        if (Directory.Exists(backups))
        {
            var target = Path.Combine(destination, "backups"); Directory.CreateDirectory(target);
            foreach (var file in Directory.EnumerateFiles(backups)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
    }
}
