using System.Text.Json;

namespace Equora.App.Services;

public sealed record AppPreferences
{
    public int Theme { get; init; }
    public string Accent { get; init; } = "#C32F37";
    public bool ActivityMonitoring { get; init; }
    public string BlockedApps { get; init; } = "game.exe,steam.exe";
    public bool CloseToTray { get; init; }
    public int PomodoroRounds { get; init; } = 4;
    public int BreakMinutes { get; init; } = 5;
    public SemesterSettings Semester { get; init; } = new();
    public IReadOnlyList<SemesterSettings> ArchivedSemesters { get; init; } = Array.Empty<SemesterSettings>();
}

/// <summary>Works in both unpackaged desktop builds and MSIX installations.</summary>
public sealed class PreferencesStore(string path)
{
    public AppPreferences Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(AppPreferences preferences)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }
}
