using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using DN = dnlib.DotNet;
using Cecil = Mono.Cecil;
using Il2CppAssemblyFixer.Shared;

namespace Il2CppAssemblyFixer;

class Program
{
    const string GameFolder = "Data Center";
    const string ConfigFileName = "game-path.txt";
    const string InstallerFileName = "MelonLoader.Installer.exe";

    static readonly string[] SteamRootCandidates =
    {
        "Steam", "SteamLibrary", "Steam Library", "SteamGames", "Games",
        @"Games\Steam", @"Games\SteamLibrary", @"Program Files\Steam",
        @"Program Files (x86)\Steam", @"Program Files\SteamLibrary",
        @"Program Files (x86)\SteamLibrary",
    };

    static readonly string[] NonSteamParentCandidates =
    {
        "", "Games", "MyGames", "My Games", "PC Games", "PCGames",
        "GameFiles", "Spiele", "Spielebibliothek", @"Program Files",
        @"Program Files (x86)", "Apps", "Applications", "Software",
    };

    static readonly HashSet<string> NeverTouchAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Il2CppInterop.Runtime.dll", "Il2Cppmscorlib.dll", "netstandard.dll",
        "mscorlib.dll", "UnityExplorer.ML.IL2CPP.CoreCLR.dll",
        "UniverseLib.ML.IL2CPP.Interop.dll",
    };

    static readonly HashSet<string> ConservativeAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "UnityEngine.CoreModule.dll", "UnityEngine.UIElementsModule.dll",
        "UnityEngine.IMGUIModule.dll", "UnityEngine.TextCoreModule.dll",
        "UnityEngine.InputSystem.dll", "UnityEngine.AssetBundleModule.dll",
        "UnityEngine.SceneManagement.dll",
    };

    static int _assembliesProcessed;
    static int _assembliesModified;
    static int _typesRemoved;
    static int _rewritesPerformed;
    static int _errors;
    static readonly List<Telemetry.AssemblyDetail> _assemblyDetails = new();
    static FileLogger? _logFile;
    static FixerConfig? _config;

    static int Main(string[] args)
    {
        PrintBanner(args);
        RunMelonLoaderRegen();

        bool forceRewrite = HasFlag(args, "--rewrite");
        string? targetDir = ResolveTargetDirectory(args, forceRewrite);
        if (!ValidateTargetDirectory(targetDir)) return 1;

        InitializeConfigAndLogging(targetDir!);
        var sw = Stopwatch.StartNew();

        if (MaybeRestoreBackups(args, targetDir!)) return 0;

        ProcessDiscoveredAssemblies(targetDir!, forceRewrite);
        PrintSummary();
        MaybeDeployRuntimeShim(args, targetDir!);

        sw.Stop();
        SendTelemetry(sw.ElapsedMilliseconds);
        _logFile?.Dispose();
        return _errors > 0 ? 2 : 0;
    }

    static bool HasFlag(string[] args, string flag)
    {
        return args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
    }

    static void PrintBanner(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║        Il2Cpp Assembly Fixer  –  .NET 10  (dnlib + Cecil)║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        Info("Startup complete.");
        Debug($"Arguments received: [{string.Join(", ", args)}]");
    }

    static string? ResolveTargetDirectory(string[] args, bool forceRewrite)
    {
        if (forceRewrite)
            Info("Flag --rewrite detected: all assemblies will be rewritten via Mono.Cecil.");

        return args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
            ?? AutoDetectPath();
    }

    static bool ValidateTargetDirectory(string? targetDir)
    {
        if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
        {
            Info($"Target directory resolved: {targetDir}");
            return true;
        }

        Error("Target directory not found or not specified.");
        Error($"Resolved path: '{targetDir ?? "<null>"}'");
        PrintSummary();
        return false;
    }

    static void InitializeConfigAndLogging(string targetDir)
    {
        string melonLoaderDir = Path.GetFullPath(Path.Combine(targetDir, ".."));
        _config = FixerConfig.LoadOrCreate(melonLoaderDir, msg => Warn(msg), AppContext.BaseDirectory);

        if (_config.Logging.WriteLogFile)
        {
            _logFile = new FileLogger(melonLoaderDir, _config.Logging.LogFileName, "Il2CppAssemblyFixer (EXE)");
            if (_logFile.Path != null) Info($"Log file: {_logFile.Path}");
        }

        Info(_config.Telemetry.Enabled
            ? $"Telemetry enabled → {_config.Telemetry.Endpoint} ({_config.Telemetry.Format})"
            : "Telemetry disabled (opt-in via fixer_config.json).");
    }

    static bool MaybeRestoreBackups(string[] args, string targetDir)
    {
        if (!HasFlag(args, "--restore")) return false;
        RestoreAllBackups(targetDir);
        _logFile?.Dispose();
        return true;
    }

    static void MaybeDeployRuntimeShim(string[] args, string targetDir)
    {
        if (!HasFlag(args, "--deploy-shim")) return;

        try { DeployRuntimeShim(targetDir); }
        catch (Exception ex)
        {
            _errors++;
            Error($"Failed to deploy runtime shim: {ex.Message}");
        }
    }

    static void ProcessDiscoveredAssemblies(string targetDir, bool forceRewrite)
    {
        string[] dllFiles = DiscoverAssemblies(targetDir);
        if (dllFiles.Length == 0)
        {
            Warn("No .dll files found in the target directory. Nothing to do.");
            return;
        }

        foreach (string dllPath in dllFiles)
            ProcessAssemblySafely(dllPath, forceRewrite);
    }

    static void ProcessAssemblySafely(string dllPath, bool forceRewrite)
    {
        _assembliesProcessed++;
        try
        {
            bool conservative = IsConservative(dllPath);
            ProcessAssembly(dllPath, forceRewrite && !conservative, conservative);
        }
        catch (Exception ex)
        {
            _errors++;
            Error($"Unhandled exception while processing '{Path.GetFileName(dllPath)}':");
            Error($"  {ex.GetType().FullName}: {ex.Message}");
            Error($"  Stack trace:\n{ex.StackTrace}");
        }
    }

    static void RunMelonLoaderRegen()
    {
        Info("Step 1 – Checking for MelonLoader.Installer.exe …");

        string installer = GetInstallerPath();
        Debug($"Installer path: {installer}");
        if (!File.Exists(installer))
        {
            Warn("MelonLoader.Installer.exe not found – skipping AGF regeneration.");
            return;
        }

        Info("MelonLoader.Installer.exe found. Launching with --melonloader.agfregenerate …");
        try { RunInstallerProcess(installer); }
        catch (Exception ex)
        {
            _errors++;
            Error($"Failed to run MelonLoader.Installer.exe: {ex.Message}");
            Error($"Stack trace:\n{ex.StackTrace}");
        }
    }

    static string GetInstallerPath()
    {
        string baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        string installer = Path.GetFullPath(Path.Combine(baseDir, Path.GetFileName(InstallerFileName)));
        if (!installer.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Installer path resolved outside application directory.");
        return installer;
    }

    static void RunInstallerProcess(string installer)
    {
        var psi = new ProcessStartInfo
        {
            FileName = installer,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--melonloader.agfregenerate");

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Process.Start returned null.");

        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        Debug($"Installer stdout:\n{(string.IsNullOrWhiteSpace(stdout) ? "<empty>" : stdout.TrimEnd())}");
        if (!string.IsNullOrWhiteSpace(stderr)) Warn($"Installer stderr:\n{stderr.TrimEnd()}");

        if (proc.ExitCode == 0) Success("MelonLoader regeneration completed (exit code 0).");
        else Warn($"MelonLoader installer exited with code {proc.ExitCode}.");
    }

    [SupportedOSPlatform("windows")]
    static string? ReadSteamInstallPath()
    {
        var keys = new[]
        {
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
            @"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam",
        };

        foreach (string key in keys)
        {
            Debug($"Querying registry: {key}\InstallPath");
            string? value = Registry.GetValue(key, "InstallPath", null) as string;
            if (!string.IsNullOrEmpty(value))
            {
                Success($"Steam InstallPath found: {key}");
                return value;
            }
        }
        return null;
    }

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
            ?? TryWindowsSteamDetection()
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

    static string? TryWindowsSteamDetection()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            string? steamRoot = ReadSteamInstallPath();
            if (steamRoot == null)
            {
                Warn("Steam registry key not found.");
                return null;
            }

            string? found = TryLibrary(steamRoot);
            if (found != null) { Success($"Found (registry): {found}"); return found; }

            foreach (string lib in ParseLibraryFoldersVdf(steamRoot))
            {
                found = TryLibrary(lib);
                if (found != null) { Success($"Found (VDF library): {found}"); return found; }
            }
            Warn("Not found in any Steam library from libraryfolders.vdf.");
        }
        catch (Exception ex) { Warn($"Registry/VDF error: {ex.Message}"); }
        return null;
    }

    static string? TryUnixSteamDetection()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return null;

        foreach (string root in LinuxSteamRoots())
        {
            string? found = TryLibrary(root);
            if (found != null) { Success($"Found (Linux Steam): {found}"); return found; }

            foreach (string lib in ParseLibraryFoldersVdf(root))
            {
                found = TryLibrary(lib);
                if (found != null) { Success($"Found (Linux VDF): {found}"); return found; }
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

    static string[] DiscoverAssemblies(string directory)
    {
        Info($"Step 3 – Scanning for .dll files in: {directory}");
        string[] files = Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(ShouldProcessAssembly)
            .OrderBy(f => f)
            .ToArray();
        Info($"Found {files.Length} .dll file(s) to process.");
        foreach (string f in files) Debug($"  Discovered: {f}");
        return files;
    }

    static bool ShouldProcessAssembly(string path) => !NeverTouchAssemblies.Contains(Path.GetFileName(path));
    static bool IsConservative(string path) => ConservativeAssemblies.Contains(Path.GetFileName(path));
    static bool IsCompilerGenerated(DN.TypeDef type) => type.Name.Length >= 2 && type.Name[0] == '<';

    static void ProcessAssembly(string path, bool forceRewrite, bool conservative = false)
    {
        string fileName = Path.GetFileName(path);
        Info($"─── Processing: {fileName}{(conservative ? "  [conservative]" : "")} ───");

        byte[] data = File.ReadAllBytes(path);
        bool modified = false;
        int removedHere = 0;
        bool rewrittenHere = false;

        Debug($"[dnlib] Loading assembly: {fileName}");
        using (var module = DN.ModuleDefMD.Load(data))
        {
            Dictionary<DN.TypeDef, int> referenceCounts = TypeReferenceCounter.Build(module);
            var toRemove = FindDuplicateTypesToRemove(module, referenceCounts, conservative);
            Debug($"[dnlib] Duplicates queued for removal: {toRemove.Count}");

            if (toRemove.Count > 0)
            {
                RemoveDuplicateTypes(module, toRemove, ref removedHere);
                data = WriteModuleToBytes(module);
                modified = true;
                Success($"[dnlib] {toRemove.Count} duplicate(s) removed from '{fileName}'.");
            }
            else
            {
                Info($"[dnlib] No duplicate types found in '{fileName}'.");
            }
        }

        if (!RewriteIfNeeded(path, fileName, forceRewrite, modified, ref data, ref rewrittenHere)) return;
        RecordAssemblyDetail(fileName, removedHere, rewrittenHere, conservative);
    }

    static List<DN.TypeDef> FindDuplicateTypesToRemove(DN.ModuleDefMD module, Dictionary<DN.TypeDef, int> referenceCounts, bool conservative)
    {
        var toRemove = new List<DN.TypeDef>();
        foreach (var group in module.GetTypes().GroupBy(type => type.FullName, StringComparer.Ordinal))
        {
            List<DN.TypeDef> duplicates = group.ToList();
            if (!ShouldRemoveDuplicateGroup(group.Key, duplicates, referenceCounts, conservative)) continue;

            foreach (DN.TypeDef duplicate in duplicates.Where(type => !referenceCounts.TryGetValue(type, out int count) || count == 0))
            {
                Debug($"[dnlib] Duplicate detected: '{duplicate.FullName}' (reference count: 0)");
                toRemove.Add(duplicate);
            }
        }
        return toRemove;
    }

    static bool ShouldRemoveDuplicateGroup(string groupName, List<DN.TypeDef> duplicates, Dictionary<DN.TypeDef, int> referenceCounts, bool conservative)
    {
        if (duplicates.Count < 2) return false;
        if (conservative && !duplicates.All(IsCompilerGenerated))
        {
            Debug($"[dnlib] Conservative: skipping non-compiler-generated duplicate group '{groupName}'.");
            return false;
        }

        int referencedCopies = duplicates.Count(type => referenceCounts.TryGetValue(type, out int count) && count > 0);
        if (referencedCopies <= 1) return true;

        Warn($"[dnlib] Duplicate group '{groupName}' has {referencedCopies} referenced copies; skipping unsafe removal.");
        return false;
    }

    static void RemoveDuplicateTypes(DN.ModuleDefMD module, List<DN.TypeDef> toRemove, ref int removedHere)
    {
        foreach (DN.TypeDef type in toRemove)
        {
            string removedName = type.FullName;
            if (type.IsNested) type.DeclaringType.NestedTypes.Remove(type);
            else module.Types.Remove(type);
            _typesRemoved++;
            removedHere++;
            Success($"[dnlib] Removed duplicate type: '{removedName}'");
        }
    }

    static byte[] WriteModuleToBytes(DN.ModuleDefMD module)
    {
        Debug("[dnlib] Writing modified module to memory …");
        using var ms = new MemoryStream();
        module.Write(ms);
        return ms.ToArray();
    }

    static bool RewriteIfNeeded(string path, string fileName, bool forceRewrite, bool modified, ref byte[] data, ref bool rewrittenHere)
    {
        if (!forceRewrite && !modified)
        {
            Info($"No changes required for '{fileName}' – skipped.");
            return true;
        }

        string reason = forceRewrite && modified ? "--rewrite flag + dnlib changes" : forceRewrite ? "--rewrite flag" : "dnlib modifications";
        Info($"[Cecil] Rewriting '{fileName}' (reason: {reason}) …");
        try
        {
            data = RewriteWithCecil(data, fileName);
            rewrittenHere = true;
        }
        catch (Exception ex)
        {
            _errors++;
            Error($"[Cecil] Rewrite failed for '{fileName}': {ex.Message}");
            Error($"Stack trace:\n{ex.StackTrace}");
            return false;
        }

        Debug($"Writing {data.Length:N0} bytes back to: {path}");
        BackupIfNeeded(path);
        File.WriteAllBytes(path, data);
        _assembliesModified++;
        Success($"Saved: {fileName}");
        return true;
    }

    static byte[] RewriteWithCecil(byte[] data, string fileName)
    {
        using var msIn = new MemoryStream(data);
        var readerParams = new Cecil.ReaderParameters { ReadingMode = Cecil.ReadingMode.Immediate };
        using var asm = Cecil.AssemblyDefinition.ReadAssembly(msIn, readerParams);
        using var msOut = new MemoryStream();
        asm.Write(msOut);
        _rewritesPerformed++;
        Success($"[Cecil] Metadata normalization complete for '{fileName}'.");
        return msOut.ToArray();
    }

    static void RecordAssemblyDetail(string fileName, int removedHere, bool rewrittenHere, bool conservative)
    {
        if (removedHere <= 0 && !rewrittenHere) return;
        _assemblyDetails.Add(new Telemetry.AssemblyDetail
        {
            Name = fileName,
            TypesRemoved = removedHere,
            Rewritten = rewrittenHere,
            Conservative = conservative,
        });
    }

    static void BackupIfNeeded(string path)
    {
        string backup = path + ".bak";
        if (!File.Exists(backup))
        {
            File.Copy(path, backup);
            Info($"Backup created: {Path.GetFileName(backup)}");
        }
        else Debug($"Backup already exists, skipping: {Path.GetFileName(backup)}");
    }

    static void RestoreAllBackups(string dir)
    {
        Info("Restoring all .bak files...");
        foreach (string bak in Directory.GetFiles(dir, "*.dll.bak"))
        {
            string original = bak[..^4];
            File.Copy(bak, original, overwrite: true);
            File.Delete(bak);
            Success($"Restored: {Path.GetFileName(original)}");
        }
    }

    static void DeployRuntimeShim(string il2CppAssembliesDir)
    {
        string gameRoot = Path.GetFullPath(Path.Combine(il2CppAssembliesDir, "..", ".."));
        string modsDir = Path.Combine(gameRoot, "Mods");
        Directory.CreateDirectory(modsDir);

        string shimName = "UnityExplorerUnity6Shim.dll";
        string source = Path.Combine(AppContext.BaseDirectory, shimName);
        string dest = Path.Combine(modsDir, shimName);

        if (!File.Exists(source))
            throw new FileNotFoundException($"Shim DLL not found. Build Project 2 first and place '{shimName}' next to this tool.", source);

        File.Copy(source, dest, overwrite: true);
        Success($"Runtime Shim deployed → {dest}");
    }

    static void SendTelemetry(long durationMs)
    {
        if (_config == null) return;
        var evt = new Telemetry.Event
        {
            Variant = "exe",
            Version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
            AssembliesScanned = _assembliesProcessed,
            AssembliesModified = _assembliesModified,
            AssembliesSkipped = Math.Max(0, _assembliesProcessed - _assembliesModified - _errors),
            TypesRemoved = _typesRemoved,
            RewritesPerformed = _rewritesPerformed,
            Errors = _errors,
            DurationMs = durationMs,
            MachineKey = _config.Telemetry.AnonymousId,
            Assemblies = _assemblyDetails,
        };
        Telemetry.PopulateEnvironment(evt);
        Telemetry.DeriveOutcome(evt);
        Telemetry.Send(_config.Telemetry, evt, line => Info(line));
    }

    static void PrintSummary()
    {
        const int LabelW = 28;
        const int ValueW = 28;
        string Row(string label, int value) => $"║  {label.PadRight(LabelW)}: {value.ToString().PadRight(ValueW)}║";

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                      FINAL SUMMARY                      ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════╣");
        Console.WriteLine(Row("Assemblies processed", _assembliesProcessed));
        Console.WriteLine(Row("Assemblies modified", _assembliesModified));
        Console.WriteLine(Row("Duplicate types removed", _typesRemoved));
        Console.WriteLine(Row("Cecil rewrites performed", _rewritesPerformed));
        Console.WriteLine(Row("Errors encountered", _errors));
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        if (_errors == 0) Success("All operations completed without errors.");
        else Warn($"{_errors} error(s) occurred. Review [ERROR] lines above for details.");
    }

    static void WriteLog(string line, bool toStderr)
    {
        if (toStderr) Console.Error.WriteLine(line);
        else Console.WriteLine(line);
        _logFile?.WriteLine(line);
    }

    static void Info(string msg) => WriteLog($"[INFO]    {msg}", false);
    static void Debug(string msg) => WriteLog($"[DEBUG]   {msg}", false);
    static void Warn(string msg) => WriteLog($"[WARN]    {msg}", false);
    static void Success(string msg) => WriteLog($"[SUCCESS] {msg}", false);
    static void Error(string msg) => WriteLog($"[ERROR]   {msg}", true);
}
