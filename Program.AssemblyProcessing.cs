using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DN = dnlib.DotNet;
using Cecil = Mono.Cecil;
using Il2CppAssemblyFixer.Shared;

namespace Il2CppAssemblyFixer;

static partial class Program
{
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
}
