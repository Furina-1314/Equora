using Equora.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Equora.App;

internal static class Appearance
{
    private static readonly PreferencesStore Store = new(Path.Combine(AppPaths.DataDirectory, "preferences.json"));
    public static AppPreferences Current { get; private set; } = Store.Load();

    public static bool TryColor(string value, out Color color)
    {
        color = default;
        if (value.Length != 7 || value[0] != '#' ||
            !uint.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb)) return false;
        color = Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    public static void Save(AppPreferences preferences)
    {
        Store.Save(preferences);
        Current = preferences;
        if (App.MainWindow?.Content is FrameworkElement root) Apply(root);
    }

    public static void Apply(FrameworkElement root)
    {
        root.RequestedTheme = Current.Theme switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default };
        if (!TryColor(Current.Accent, out var accent)) TryColor("#C32F37", out accent);
        // Mutate brush objects so existing pages update immediately. High contrast stays system-controlled.
        var theme = Application.Current.Resources.MergedDictionaries.Last();
        foreach (var name in new[] { "Light", "Default" })
        {
            var dictionary = (ResourceDictionary)theme.ThemeDictionaries[name];
            var color = accent;
            if (name == "Default")
                color = Color.FromArgb(255, (byte)(accent.R * .65 + 255 * .35),
                    (byte)(accent.G * .65 + 255 * .35), (byte)(accent.B * .65 + 255 * .35));
            var luminance = .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
            var ink = 1.05 / (luminance + .05) < 4.5 ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
            foreach (var key in dictionary.Keys.OfType<string>().ToArray())
            {
                if (key.StartsWith("ComboBox", StringComparison.Ordinal) || dictionary[key] is not SolidColorBrush brush) continue;
                if (key == "EquoraAccentBrush" || key.Contains("BackgroundSelected") ||
                    key.StartsWith("AccentFillColor") || key == "NavigationViewSelectionIndicatorForeground") brush.Color = color;
                if (key == "EquoraOnAccentBrush" || key.Contains("ForegroundSelected") ||
                    key.StartsWith("TextOnAccentFillColor")) brush.Color = ink;
            }
        }
    }

    private static double Linear(byte value)
    {
        var channel = value / 255d;
        return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
    }
}
