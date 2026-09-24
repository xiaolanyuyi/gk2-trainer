using System.Diagnostics;
using Microsoft.Win32;

namespace GK2Trainer.App.Core;

public static class Paths
{
    public const string PluginFileName = "GK2Trainer.Plugin.dll";

    /// <summary>Folder name the game uses under LocalLow.</summary>
    public const string GameStorageFolder = "Graveyard Keeper 2";
    public const string CompanyFolder = "Lazy Bear Games";

    public static string Documents =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <summary>Unity's persistentDataPath for this game, where saves and the bridge live.</summary>
    public static string GameStorage => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low",
        CompanyFolder, GameStorageFolder);

    public static string BridgeDir => Path.Combine(GameStorage, "Trainer");
    public static string CommandFile => Path.Combine(BridgeDir, "command.json");
    public static string StateFile => Path.Combine(BridgeDir, "state.json");
    public static string BridgeLogFile => Path.Combine(BridgeDir, "log.txt");
    public static string SaveInfoFile => Path.Combine(GameStorage, "Steam_1.info");

    public static string AppData
    {
        get
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GK2Trainer");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string PluginsDir(string gameDir) => Path.Combine(gameDir, "BepInEx", "plugins");
    public static string BepInExDir(string gameDir) => Path.Combine(gameDir, "BepInEx");
    public static string BepInExLog(string gameDir) => Path.Combine(gameDir, "BepInEx", "LogOutput.log");

    /// <summary>Look for the game in the usual places (running process, stored path, Steam libraries).</summary>
    public static IEnumerable<string> CandidateGameDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Best source first: the running game tells us exactly where it is, which
        // also covers non-Steam installations.
        var running = RunningGameDirectory();
        if (running != null && seen.Add(running)) yield return running;

        var stored = AppSettings.Load().GameDirectory;
        if (!string.IsNullOrWhiteSpace(stored) && seen.Add(stored!)) yield return stored!;

        foreach (var library in SteamLibraries())
        {
            var candidate = Path.Combine(library, "steamapps", "common", "Graveyard Keeper 2");
            if (seen.Add(candidate)) yield return candidate;
        }
    }

    /// <summary>Directory of the running GraveyardKeeper2.exe, or null.</summary>
    public static string? RunningGameDirectory()
    {
        try
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("GraveyardKeeper2"))
            {
                var path = process.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path)) return Path.GetDirectoryName(path);
            }
        }
        catch
        {
            // Access to the module can be denied; fall back to other sources.
        }

        return null;
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var libraries = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!libraries.Contains(path, StringComparer.OrdinalIgnoreCase)) libraries.Add(path);
        }

        foreach (var root in new[]
                 {
                     @"HKEY_CURRENT_USER\Software\Valve\Steam",
                     @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                     @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
                 })
        {
            foreach (var name in new[] { "SteamPath", "InstallPath" })
            {
                try
                {
                    Add(Registry.GetValue(root, name, null) as string);
                }
                catch
                {
                    // Ignore missing keys.
                }
            }
        }

        foreach (var library in libraries.ToList())
        {
            var vdf = Path.Combine(library, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;

            try
            {
                foreach (System.Text.RegularExpressions.Match match in
                         System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                {
                    Add(match.Groups[1].Value.Replace("\\\\", "\\"));
                }
            }
            catch
            {
                // Ignore unreadable library files.
            }
        }

        return libraries;
    }
}

public sealed class GameInstall
{
    public required string Root { get; init; }
    public required string AppPath { get; init; }
    public required string PluginPath { get; init; }
    public required bool BepInExPresent { get; init; }
    public required bool PluginPresent { get; init; }
    public string? PluginVersion { get; init; }
    public bool PluginUpToDate { get; init; }
    public bool GameRunning { get; init; }

    public static GameInstall? Inspect(string root)
    {
        var exe = Path.Combine(root, "GraveyardKeeper2.exe");
        if (!File.Exists(exe)) return null;

        var plugin = Path.Combine(Paths.PluginsDir(root), Paths.PluginFileName);
        var pluginPresent = File.Exists(plugin);

        var upToDate = false;
        if (pluginPresent)
        {
            try
            {
                var deployed = File.ReadAllBytes(plugin);
                var embedded = PluginDeployer.ReadEmbeddedPlugin();
                upToDate = embedded != null && deployed.AsSpan().SequenceEqual(embedded);
            }
            catch
            {
                upToDate = false;
            }
        }

        return new GameInstall
        {
            Root = root,
            AppPath = exe,
            PluginPath = plugin,
            BepInExPresent = Directory.Exists(Paths.BepInExDir(root)),
            PluginPresent = pluginPresent,
            PluginVersion = pluginPresent ? FileVersionInfo.GetVersionInfo(plugin).FileVersion : null,
            PluginUpToDate = upToDate,
            GameRunning = IsGameRunning(),
        };
    }

    public static GameInstall? Find()
    {
        foreach (var candidate in Paths.CandidateGameDirectories())
        {
            var install = Inspect(candidate);
            if (install == null) continue;

            // Remember where the game is, so the next start works even when the
            // game is not running (the process is only a hint).
            try
            {
                var settings = AppSettings.Load();
                if (!string.Equals(settings.GameDirectory, install.Root, StringComparison.OrdinalIgnoreCase))
                {
                    settings.GameDirectory = install.Root;
                    settings.Save();
                }
            }
            catch
            {
                // Persisting the path is a convenience, not a requirement.
            }

            return install;
        }

        return null;
    }

    public static bool IsGameRunning() =>
        Process.GetProcessesByName("GraveyardKeeper2").Length > 0;

    /// <summary>Bridge connection state derived from how recently the plugin wrote state.json.</summary>
    public static TimeSpan StateAge()
    {
        try
        {
            if (!File.Exists(Paths.StateFile)) return TimeSpan.MaxValue;
            return DateTime.UtcNow - File.GetLastWriteTimeUtc(Paths.StateFile);
        }
        catch
        {
            return TimeSpan.MaxValue;
        }
    }
}
