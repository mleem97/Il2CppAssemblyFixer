using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MelonLoader;
using MelonLoader.Utils;
using DN = dnlib.DotNet;
using Cecil = Mono.Cecil;
using Il2CppAssemblyFixer.Shared;

[assembly: MelonInfo(typeof(Il2CppAssemblyFixerPlugin.FixerPlugin), "Il2CppAssemblyFixer", "1.50.3", "mleem97",
    "https://github.com/mleem97/Il2CppAssemblyFixer")]
[assembly: MelonGame]

namespace Il2CppAssemblyFixerPlugin;

/// <summary>
/// MelonPlugin that repairs Il2Cpp-generated assemblies before any MelonMod is loaded.
/// </summary>
public class FixerPlugin : MelonPlugin
{
    private const string ManifestFileName = ".il2cppfixer-manifest";
    private static FileLogger? _logFile;
    private static FixerConfig? _config;

    private sealed class ProcessingStats
    {
        public int Scanned { get; set; }
        public int Processed { get; set; }
        public int Fixed { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }
        public int RemovedTypes { get; set; }
        public List<Telemetry.AssemblyDetail> Details { get; } = new();
    }

    public override void OnPreInitialization()
    {
        try
        {
            RunFixer();
        }
        catch (Exception ex)
        {
            Err($"[Il2CppAssemblyFixer] Fatal: {ex.GetType().Name}: {ex.Message}");
            MelonLogger.Error("[Il2CppAssemblyFixer] Plugin aborted to keep MelonLoader running.");
        }
    }

    private static void Msg(string message)
    {
        MelonLogger.Msg(message);
        _logFile?.WriteLine($"[INFO]  {message}");
    }

    private static void Warn(string message)
    {
        MelonLogger.Warning(message);
        _logFile?.WriteLine($"[WARN]  {message}");
    }

    private static void Err(string message)
    {
        MelonLogger.Error(message);
        _logFile?.WriteLine($"[ERROR] {message}");
    }

    private static void RunFixer()
    {
        InitializeConfigAndLogging();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        LogHeader();

        string? assembliesDir = ResolveAssembliesDirectory();
        if (!ValidateAssembliesDirectory(assembliesDir)) return;

        Dictionary<string, string> manifest = LoadManifest(assembliesDir!);
        bool manifestDirty;
        ProcessingStats stats = ProcessAssemblies(assembliesDir!, manifest, out manifestDirty);
        SaveManifestIfNeeded(assembliesDir!, manifest, manifestDirty);
        LogSummary(stats);

        sw.Stop();
        SendTelemetry(stats, sw.ElapsedMilliseconds);
        _logFile?.Dispose();
    }

    private static void InitializeConfigAndLogging()
    {
        string? mlDir = SafeGetMelonLoaderDir();
        if (string.IsNullOrEmpty(mlDir)) return;

        string pluginDir = SafeGetPluginDirectory() ?? string.Empty;
        _config = FixerConfig.LoadOrCreate(
            mlDir,
            warning => Warn($"[Il2CppAssemblyFixer] {warning}"),
            pluginDir,
            AppDomain.CurrentDomain.BaseDirectory);

        if (_config.Logging.WriteLogFile)
            _logFile = new FileLogger(mlDir, _config.Logging.LogFileName, "Il2CppAssemblyFixer (Plugin)");
    }

    private static string? SafeGetPluginDirectory()
    {
        try
        {
            return Path.GetDirectoryName(typeof(FixerPlugin).Assembly.Location);
        }
        catch (Exception)
        {
            // Assembly.Location can be unavailable for unusual loaders; config seeding
            // still works from AppDomain.CurrentDomain.BaseDirectory.
            return null;
        }
    }

    private static void LogHeader()
    {
        Msg("═══════════════════════════════════════════════════════════");
        Msg("  Il2CppAssemblyFixer – scanning Il2CppAssemblies …");
        Msg("═══════════════════════════════════════════════════════════");
        if (_logFile?.Path != null) Msg($"[Il2CppAssemblyFixer] Log file: {_logFile.Path}");
        if (_config == null) return;

        Msg(_config.Telemetry.Enabled
            ? $"[Il2CppAssemblyFixer] Telemetry enabled → {_config.Telemetry.Endpoint} ({_config.Telemetry.Format})"
            : "[Il2CppAssemblyFixer] Telemetry disabled (opt-in via fixer_config.json).");
    }

    private static bool ValidateAssembliesDirectory(string? assembliesDir)
    {
        if (!string.IsNullOrEmpty(assembliesDir) && Directory.Exists(assembliesDir))
        {
            Msg($"[Il2CppAssemblyFixer] Scanning: {assembliesDir}");
            return true;
        }

        Warn($"[Il2CppAssemblyFixer] Il2CppAssemblies directory not found: '{assembliesDir ?? "<null>"}'");
        Warn("[Il2CppAssemblyFixer] If mods fail to load, run Il2CppAssemblyFixer.exe manually.");
        _logFile?.Dispose();
        return false;
    }

    private static ProcessingStats ProcessAssemblies(
        string assembliesDir,
        Dictionary<string, string> manifest,
        out bool manifestDirty)
    {
        string[] dlls = Directory.GetFiles(assembliesDir, "*.dll", SearchOption.TopDirectoryOnly);
        var stats = new ProcessingStats { Scanned = dlls.Length };
        manifestDirty = false;

        foreach (string dll in dlls)
            manifestDirty |= ProcessOneAssembly(dll, manifest, stats);

        return stats;
    }

    private static bool ProcessOneAssembly(string dll, Dictionary<string, string> manifest, ProcessingStats stats)
    {
        string fileName = Path.GetFileName(dll);
        if (IsCached(fileName, dll, manifest, stats)) return false;

        stats.Processed++;
        try
        {
            int removed = FixAssembly(dll);
            stats.RemovedTypes += removed;
            if (removed > 0)
                RecordFixedAssembly(fileName, removed, stats);

            manifest[fileName] = HashFile(dll);
            return true;
        }
        catch (Exception ex)
        {
            stats.Errors++;
            Err($"[Il2CppAssemblyFixer] Error processing '{fileName}': {ex.Message}");
            return false;
        }
    }

    private static bool IsCached(string fileName, string dll, Dictionary<string, string> manifest, ProcessingStats stats)
    {
        string currentHash;
        try
        {
            currentHash = HashFile(dll);
        }
        catch (Exception ex)
        {
            stats.Errors++;
            Warn($"[Il2CppAssemblyFixer] Cannot hash '{fileName}': {ex.Message}");
            return true;
        }

        if (!manifest.TryGetValue(fileName, out string? savedHash) || savedHash != currentHash)
            return false;

        stats.Skipped++;
        return true;
    }

    private static void RecordFixedAssembly(string fileName, int removed, ProcessingStats stats)
    {
        stats.Fixed++;
        Msg($"[Il2CppAssemblyFixer] Fixed {removed} duplicate(s) in: {fileName}");
        stats.Details.Add(new Telemetry.AssemblyDetail
        {
            Name = fileName,
            TypesRemoved = removed,
            Rewritten = true,
            Conservative = false,
        });
    }

    private static void SaveManifestIfNeeded(string assembliesDir, Dictionary<string, string> manifest, bool manifestDirty)
    {
        if (!manifestDirty) return;

        try
        {
            SaveManifest(assembliesDir, manifest);
        }
        catch (Exception ex)
        {
            Warn($"[Il2CppAssemblyFixer] Could not write manifest: {ex.Message}");
        }
    }

    private static void LogSummary(ProcessingStats stats)
    {
        Msg($"[Il2CppAssemblyFixer] Summary – scanned: {stats.Scanned}  " +
            $"processed: {stats.Processed}  fixed: {stats.Fixed}  " +
            $"skipped (cached): {stats.Skipped}  errors: {stats.Errors}  " +
            $"types removed: {stats.RemovedTypes}");
        Msg("═══════════════════════════════════════════════════════════");
    }

    private static void SendTelemetry(ProcessingStats stats, long durationMs)
    {
        if (_config == null) return;

        var evt = new Telemetry.Event
        {
            Variant = "plugin",
            Version = typeof(FixerPlugin).Assembly.GetName().Version?.ToString() ?? "unknown",
            AssembliesScanned = stats.Scanned,
            AssembliesModified = stats.Fixed,
            AssembliesSkipped = stats.Skipped,
            TypesRemoved = stats.RemovedTypes,
            RewritesPerformed = stats.Fixed,
            Errors = stats.Errors,
            DurationMs = durationMs,
            MachineKey = _config.Telemetry.AnonymousId,
            Assemblies = stats.Details,
        };
        Telemetry.PopulateEnvironment(evt);
        Telemetry.PopulateMelonInfo(evt);
        Telemetry.DeriveOutcome(evt);
        Telemetry.Send(_config.Telemetry, evt, line => Msg($"[Il2CppAssemblyFixer] {line}"));
    }

    private static string? SafeGetMelonLoaderDir()
    {
        try
        {
            string mlDir = MelonEnvironment.MelonLoaderDirectory;
            if (!string.IsNullOrEmpty(mlDir)) return mlDir;
        }
        catch (Exception)
        {
            // Older MelonLoader versions might not expose MelonEnvironment.
        }

        string fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MelonLoader");
        return Directory.Exists(fallback) ? fallback : null;
    }

    private static Dictionary<string, string> LoadManifest(string dir)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(dir, ManifestFileName);
        if (!File.Exists(path)) return dict;

        try
        {
            foreach (string line in File.ReadAllLines(path))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0 && tab < line.Length - 1)
                    dict[line.Substring(0, tab)] = line.Substring(tab + 1);
            }
        }
        catch (Exception)
        {
            // Corrupt manifest – just rebuild it.
            dict.Clear();
        }
        return dict;
    }

    private static void SaveManifest(string dir, Dictionary<string, string> manifest)
    {
        string path = Path.Combine(dir, ManifestFileName);
        var lines = manifest
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}\t{kv.Value}");
        File.WriteAllLines(path, lines);
    }

    private static string HashFile(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        byte[] hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash);
    }

    private static string? ResolveAssembliesDirectory()
    {
        string? fromEnvironment = ResolveAssembliesFromEnvironment();
        if (fromEnvironment != null) return fromEnvironment;

        string appBase = AppDomain.CurrentDomain.BaseDirectory;
        string fallback = Path.Combine(appBase, "MelonLoader", "Il2CppAssemblies");
        if (Directory.Exists(fallback)) return fallback;

        return ResolveAssembliesFromPluginLocation() ?? fallback;
    }

    private static string? ResolveAssembliesFromEnvironment()
    {
        try
        {
            string mlDir = MelonEnvironment.MelonLoaderDirectory;
            if (string.IsNullOrEmpty(mlDir)) return null;
            string candidate = Path.Combine(mlDir, "Il2CppAssemblies");
            return Directory.Exists(candidate) ? candidate : null;
        }
        catch (Exception)
        {
            // Older MelonLoader versions might not expose MelonEnvironment.
            return null;
        }
    }

    private static string? ResolveAssembliesFromPluginLocation()
    {
        try
        {
            string? dir = Path.GetDirectoryName(typeof(FixerPlugin).Assembly.Location);
            for (int i = 0; i < 5 && !string.IsNullOrEmpty(dir); i++)
            {
                string candidate = Path.Combine(dir, "MelonLoader", "Il2CppAssemblies");
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch (Exception)
        {
            // Some loaders can hide Assembly.Location; the conventional fallback remains available.
        }
        return null;
    }

    private static int FixAssembly(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        using var module = DN.ModuleDefMD.Load(data);

        Dictionary<DN.TypeDef, int> referenceCounts = TypeReferenceCounter.Build(module);
        List<DN.TypeDef> toRemove = FindDuplicateTypesToRemove(module, referenceCounts);
        if (toRemove.Count == 0) return 0;

        RemoveDuplicateTypes(module, toRemove);
        data = WriteModuleToBytes(module);
        data = TryNormalizeWithCecil(path, data);
        File.WriteAllBytes(path, data);
        return toRemove.Count;
    }

    private static List<DN.TypeDef> FindDuplicateTypesToRemove(DN.ModuleDefMD module, Dictionary<DN.TypeDef, int> referenceCounts)
    {
        var toRemove = new List<DN.TypeDef>();
        foreach (var group in module.GetTypes().GroupBy(type => type.FullName, StringComparer.Ordinal))
        {
            List<DN.TypeDef> duplicates = group.ToList();
            if (!CanRemoveDuplicateGroup(group.Key, duplicates, referenceCounts)) continue;

            foreach (DN.TypeDef duplicate in duplicates.Where(type => !referenceCounts.TryGetValue(type, out int count) || count == 0))
                toRemove.Add(duplicate);
        }
        return toRemove;
    }

    private static bool CanRemoveDuplicateGroup(string groupName, List<DN.TypeDef> duplicates, Dictionary<DN.TypeDef, int> referenceCounts)
    {
        if (duplicates.Count < 2) return false;

        int referencedCopies = duplicates.Count(type => referenceCounts.TryGetValue(type, out int count) && count > 0);
        if (referencedCopies <= 1) return true;

        Warn($"[Il2CppAssemblyFixer] Duplicate group '{groupName}' has {referencedCopies} referenced copies; skipping unsafe removal.");
        return false;
    }

    private static void RemoveDuplicateTypes(DN.ModuleDefMD module, List<DN.TypeDef> toRemove)
    {
        foreach (DN.TypeDef type in toRemove)
        {
            if (type.IsNested) type.DeclaringType.NestedTypes.Remove(type);
            else module.Types.Remove(type);
        }
    }

    private static byte[] WriteModuleToBytes(DN.ModuleDefMD module)
    {
        using var msAfterDnlib = new MemoryStream();
        module.Write(msAfterDnlib);
        return msAfterDnlib.ToArray();
    }

    private static byte[] TryNormalizeWithCecil(string path, byte[] data)
    {
        try
        {
            using var msIn = new MemoryStream(data);
            var readerParams = new Cecil.ReaderParameters { ReadingMode = Cecil.ReadingMode.Immediate };
            using var asmDef = Cecil.AssemblyDefinition.ReadAssembly(msIn, readerParams);
            using var msOut = new MemoryStream();
            asmDef.Write(msOut);
            return msOut.ToArray();
        }
        catch (Exception cecilEx)
        {
            Warn($"[Il2CppAssemblyFixer] Cecil normalization skipped for '{Path.GetFileName(path)}': {cecilEx.Message}");
            return data;
        }
    }
}
