namespace Equora.App.Services;

/// <summary>应用数据目录与数据库路径(单机模式)。</summary>
public static class AppPaths
{
    public static string ConfigurationDirectory { get; } =
        CommandLineDataDirectory() ??
#if DEBUG
        Environment.GetEnvironmentVariable("EQUORA_TEST_DATA_DIRECTORY") ??
#endif
        Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Equora");

    private static string? CommandLineDataDirectory()
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, "--data-directory");
        if (index < 0) return null;
        if (index + 1 >= arguments.Length || !Path.IsPathFullyQualified(arguments[index + 1]))
            throw new ArgumentException("--data-directory 需要一个绝对目录路径。");
        return Path.GetFullPath(arguments[index + 1]);
    }

    public static string ResolveDataDirectory()
    {
        var locator = Path.Combine(ConfigurationDirectory, "data-location.txt");
        if (!File.Exists(locator)) return ConfigurationDirectory;
        var selected = File.ReadAllText(locator).Trim();
        if (!Path.IsPathFullyQualified(selected) || !Directory.Exists(selected))
            throw new IOException("配置的数据目录不可用，请恢复该目录或检查 data-location.txt。");
        return Path.GetFullPath(selected);
    }
    public static string DataDirectory { get; } = ResolveDataDirectory();
    public static void SelectForNextStart(string directory)
    {
        Directory.CreateDirectory(ConfigurationDirectory);
        var locator = Path.Combine(ConfigurationDirectory, "data-location.txt");
        File.WriteAllText(locator + ".tmp", Path.GetFullPath(directory));
        File.Move(locator + ".tmp", locator, true);
    }

    public static string DatabasePath { get; } = Path.Combine(DataDirectory, "equora.db");

    public static string BackupsDirectory { get; } = Path.Combine(DataDirectory, "backups");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }
}
