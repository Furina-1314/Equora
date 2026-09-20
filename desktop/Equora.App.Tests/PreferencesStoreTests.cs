using Equora.App.Services;
using Xunit;

namespace Equora.App.Tests;

public class PreferencesStoreTests
{
    [Fact]
    public void ThemeAccentAndPrivacySurviveRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"equora-preferences-{Guid.NewGuid():N}.json");
        try
        {
            var expected = new AppPreferences { Theme = 2, Accent = "#0078D4", ActivityMonitoring = true, BlockedApps = "test.exe" };
            new PreferencesStore(path).Save(expected);
            Assert.Equal(expected, new PreferencesStore(path).Load());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CorruptFileFallsBackToPrivacySafeDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"equora-preferences-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{unfinished");
            var result = new PreferencesStore(path).Load();
            Assert.False(result.ActivityMonitoring);
            Assert.Equal(0, result.Theme);
            Assert.Equal("#C32F37", result.Accent);
        }
        finally { File.Delete(path); }
    }
}
