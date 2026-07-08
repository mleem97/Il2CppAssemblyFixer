using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Il2CppAssemblyFixer.Shared;

namespace Il2CppAssemblyFixer;

static partial class Program
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

    static int _errors;
    static FileLogger? _logFile;
    static FixerConfig? _config;

    static int Main(string[] args)
    {
        PrintBanner(args);
        LogMelonLoaderRegenHint();

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

    static void LogMelonLoaderRegenHint()
    {
        Info("Step 1 – MelonLoader AGF regeneration check …");
        string installer = GetInstallerPath();
        Debug($"Installer path: {installer}");

        if (!File.Exists(installer))
        {
            Warn("MelonLoader.Installer.exe not found – skipping AGF regeneration hint.");
            return;
        }

        Info("MelonLoader.Installer.exe found. Automatic execution is disabled to avoid OS command execution.");
        Info($"Run manually if regeneration is required: \"{installer}\" --melonloader.agfregenerate");
    }

    static string GetInstallerPath()
    {
        string baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        string installer = Path.GetFullPath(Path.Combine(baseDir, Path.GetFileName(InstallerFileName)));
        if (!installer.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Installer path resolved outside application directory.");
        return installer;
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
