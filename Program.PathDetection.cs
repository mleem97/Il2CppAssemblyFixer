using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Il2CppAssemblyFixer;

static partial class Program
{
    static IEnumerable<string> ParseLibraryFoldersVdf(string steamRoot)
    {
        string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
        {
            Debug($"libraryfolders.vdf not found: {vdf}");
            yield break;
        }

        Debug($"Parsing: {vdf}");
        var rx = new Regex(@"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase);
        foreach (string line in File.ReadLines(vdf))
        {
            Match m = rx.Match(line);
            if (!m.Success) continue;
            string lib = m.Groups[1].Value.Replace(@"\\", @"\");
            Debug($"  VDF library: {lib}");
            yield return lib;
        }
    }

    static string? TryLibrary(string libraryRoot)
    {
        string candidate = Path.Combine(libraryRoot, "steamapps", "common", GameFolder, "MelonLoader", "Il2CppAssemblies");
        return Directory.Exists(candidate) ? candidate : null;
    }

    static string? TryGameFolder(string parentDir)
    {
        if (string.IsNullOrEmpty(parentDir)) return null;
        string candidate = Path.Combine(parentDir, GameFolder, "MelonLoader", "Il2CppAssemblies");
        return Directory.Exists(candidate) ? candidate : null;
    }

    static string? AutoDetectPath()
    {
        Info("Step 2 – Auto-detecting game installation path …");
        string? found = ReadConfigOverridePath()
            ?? TrySteamRootFromEnvironment()
            ?? TryUnixSteamDetection()
            ?? ScanDrivesForSteamLayout()
            ?? ScanDrivesForCustomLayout()
            ?? ScanUserProfileDirectories();

        if (found != null) return found;
        Warn("Game installation not found automatically.");
        Warn($"Tip: create '{ConfigFileName}' next to this EXE and put the game path inside.");
        return null;
    }

    static string? ReadConfigOverridePath()
    {
        string cfgPath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        if (!File.Exists(cfgPath)) return null;

        string custom = File.ReadAllText(cfgPath).Trim().Trim('"');
        Info($"Config override found ({ConfigFileName}): {custom}");
        if (Directory.Exists(custom))
        {
            if (custom.EndsWith("Il2CppAssemblies", StringComparison.OrdinalIgnoreCase)) return custom;
            string sub = Path.Combine(custom, "MelonLoader", "Il2CppAssemblies");
            if (Directory.Exists(sub)) return sub;
        }

        Warn($"Path in {ConfigFileName} does not exist or is invalid: {custom}");
        return null;
    }

    static string? TrySteamRootFromEnvironment()
    {
        string? steamRoot = Environment.GetEnvironmentVariable("STEAM_HOME")
            ?? Environment.GetEnvironmentVariable("STEAM_DIR");
        if (string.IsNullOrWhiteSpace(steamRoot)) return null;

        string? found = TryLibrary(steamRoot);
        if (found != null) { Success($"Found (Steam env): {found}"); return found; }

        foreach (string lib in ParseLibraryFoldersVdf(steamRoot))
        {
            found = TryLibrary(lib);
            if (found != null) { Success($"Found (Steam env VDF): {found}"); return found; }
        }
        return null;
    }

    static string? TryUnixSteamDetection()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return null;

        foreach (string root in LinuxSteamRoots())
        {
            string? found = TryLibrary(root);
            if (found != null) { Success($"Found (Unix Steam): {found}"); return found; }

            foreach (string lib in ParseLibraryFoldersVdf(root))
            {
                found = TryLibrary(lib);
                if (found != null) { Success($"Found (Unix VDF): {found}"); return found; }
            }
        }
        return null;
    }

    static string? ScanDrivesForSteamLayout()
    {
        Info("Scanning drives for Steam-layout paths …");
        foreach (char drive in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            string driveRoot = $@"{drive}:\";
            if (!Directory.Exists(driveRoot)) continue;

            foreach (string folder in SteamRootCandidates)
            {
                string? found = TryLibrary(Path.Combine(driveRoot, folder));
                if (found != null) { Success($"Found (Steam scan): {found}"); return found; }
            }
        }
        return null;
    }

    static string? ScanDrivesForCustomLayout()
    {
        Info("Scanning drives for non-Steam / custom installation paths …");
        foreach (char drive in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            string driveRoot = $@"{drive}:\";
            if (!Directory.Exists(driveRoot)) continue;

            foreach (string folder in NonSteamParentCandidates)
            {
                string parent = string.IsNullOrEmpty(folder) ? driveRoot : Path.Combine(driveRoot, folder);
                string? found = TryGameFolder(parent);
                if (found != null) { Success($"Found (custom scan): {found}"); return found; }
            }
        }
        return null;
    }

    static string? ScanUserProfileDirectories()
    {
        Info("Checking user-profile directories …");
        foreach (string root in UserProfileSearchRoots())
        {
            if (!Directory.Exists(root)) continue;

            string? found = TryGameFolder(root);
            if (found != null) { Success($"Found (user profile): {found}"); return found; }

            foreach (string folder in NonSteamParentCandidates)
            {
                if (string.IsNullOrEmpty(folder)) continue;
                found = TryGameFolder(Path.Combine(root, folder));
                if (found != null) { Success($"Found (user profile): {found}"); return found; }
            }
        }
        return null;
    }

    static IEnumerable<string> LinuxSteamRoots()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, "snap", "steam", "common", ".steam", "root");
        yield return "/usr/share/steam";
        yield return "/opt/steam";
    }

    static IEnumerable<string> UserProfileSearchRoots()
    {
        string? profile = Environment.GetEnvironmentVariable("USERPROFILE") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            yield return profile;
            yield return Path.Combine(profile, "Desktop");
            yield return Path.Combine(profile, "Downloads");
            yield return Path.Combine(profile, "Documents");
            yield return Path.Combine(profile, "Documents", "Games");
            yield return Path.Combine(profile, "Documents", "My Games");
        }

        string? localApp = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localApp)) yield return localApp;
    }
}
