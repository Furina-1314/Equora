using Microsoft.Win32;
using System.Xml.Linq;

namespace Equora.App.Services;

public sealed record InstalledApplication(string Name, string ProcessName)
{
    public override string ToString() => $"{Name} · {ProcessName}";
}

public static class InstalledApplicationCatalog
{
    public static IReadOnlyList<InstalledApplication> Read()
    {
        var result = new List<InstalledApplication>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var id in uninstall.GetSubKeyNames())
            {
                using var key = uninstall.OpenSubKey(id);
                if (key?.GetValue("DisplayName") is not string name) continue;
                var icon = Environment.ExpandEnvironmentVariables(key.GetValue("DisplayIcon") as string ?? "").Trim('"');
                var end = icon.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (end < 0) continue;
                var process = Path.GetFileName(icon[..(end + 4)]).Trim('"');
                if (process.Contains("unins", StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(new(name, process));
            }
        }
        try
        {
            var manager = new Windows.Management.Deployment.PackageManager();
            foreach (var package in manager.FindPackagesForUser(""))
            {
                try
                {
                    if (package.IsFramework || package.IsResourcePackage) continue;
                    var manifest = XDocument.Load(Path.Combine(package.InstalledLocation.Path, "AppxManifest.xml"));
                    foreach (var app in manifest.Descendants().Where(e => e.Name.LocalName == "Application"))
                    {
                        var executable = app.Attribute("Executable")?.Value;
                        if (executable?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
                            result.Add(new(package.DisplayName, Path.GetFileName(executable)));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
            }
        }
        catch (System.Runtime.InteropServices.COMException) { }
        return result.Where(a => !SafetyWhitelist.IsProtected(a.ProcessName)).DistinctBy(a => (a.Name, a.ProcessName))
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
