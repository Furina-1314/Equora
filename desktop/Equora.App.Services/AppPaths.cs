namespace Equora.App.Services;

/// <summary>应用数据目录与数据库路径(单机模式)。</summary>
public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Equora");

    public static string DatabasePath { get; } = Path.Combine(DataDirectory, "equora.db");

    public static string BackupsDirectory { get; } = Path.Combine(DataDirectory, "backups");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }
}
