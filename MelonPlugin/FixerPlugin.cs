using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using DN = dnlib.DotNet;
using Cecil = Mono.Cecil;

// MelonLoader plugin metadata – runs BEFORE MelonMods are loaded
[assembly: MelonInfo(typeof(Il2CppAssemblyFixerPlugin.FixerPlugin), "Il2CppAssemblyFixer", "1.0.0", "mleem97",
    "https://github.com/mleem97/Il2CppAssemblyFixer")]
[assembly: MelonGame]

namespace Il2CppAssemblyFixerPlugin;

/// <summary>
/// MelonPlugin that fixes BadImageFormatExceptions caused by duplicate type
/// definitions in Il2Cpp-generated assemblies before any MelonMod is loaded.
///
/// Lifecycle: OnPreInitialization fires before Il2Cpp assemblies are loaded by
/// MelonLoader, so we can repair the files on disk while they are still safe to
/// replace in place.
/// </summary>
public class FixerPlugin : MelonPlugin
{
    // ── Lifecycle hook ─────────────────────────────────────────────────────────
    public override void OnPreInitialization()
    {
        MelonLogger.Msg("═══════════════════════════════════════════════════════════");
        MelonLogger.Msg("  Il2CppAssemblyFixer – scanning for duplicate <>O types …");
        MelonLogger.Msg("═══════════════════════════════════════════════════════════");

        string assembliesDir = ResolveAssembliesDirectory();

        if (string.IsNullOrEmpty(assembliesDir) || !Directory.Exists(assembliesDir))
        {
            MelonLogger.Warning($"[Il2CppAssemblyFixer] Il2CppAssemblies directory not found: " +
                                $"'{assembliesDir ?? "<null>"}'");
            MelonLogger.Warning("[Il2CppAssemblyFixer] Skipping fix – if mods fail to load, " +
                                "run Il2CppAssemblyFixer.exe manually.");
            return;
        }

        MelonLogger.Msg($"[Il2CppAssemblyFixer] Scanning: {assembliesDir}");

        string[] dlls = Directory.GetFiles(assembliesDir, "*.dll", SearchOption.TopDirectoryOnly);
        int assembliesFixed = 0;
        int assembliesErrors = 0;

        foreach (string dll in dlls)
        {
            try
            {
                int removed = FixAssembly(dll);
                if (removed > 0)
                {
                    assembliesFixed++;
                    MelonLogger.Msg($"[Il2CppAssemblyFixer] Fixed {removed} duplicate(s) in: " +
                                    Path.GetFileName(dll));
                }
            }
            catch (Exception ex)
            {
                assembliesErrors++;
                MelonLogger.Error($"[Il2CppAssemblyFixer] Error processing " +
                                  $"'{Path.GetFileName(dll)}': {ex.Message}");
            }
        }

        if (assembliesFixed > 0)
            MelonLogger.Msg($"[Il2CppAssemblyFixer] Done – {assembliesFixed} assembly/assemblies " +
                            $"repaired. Errors: {assembliesErrors}");
        else if (assembliesErrors == 0)
            MelonLogger.Msg("[Il2CppAssemblyFixer] No duplicate types found – nothing to fix.");
        else
            MelonLogger.Warning($"[Il2CppAssemblyFixer] Finished with {assembliesErrors} error(s). " +
                                "Check log above.");

        MelonLogger.Msg("═══════════════════════════════════════════════════════════");
    }

    // ── Path resolution ────────────────────────────────────────────────────────

    /// <summary>
    /// Tries to locate the Il2CppAssemblies folder using MelonLoader's own
    /// environment helpers, with a fallback to a conventionally expected path.
    /// </summary>
    private static string ResolveAssembliesDirectory()
    {
        // Primary: MelonLoader environment API
        try
        {
            // MelonLoaderDirectory = <GameRoot>/MelonLoader
            // Il2CppAssemblies lives directly inside it
            string mlDir = MelonEnvironment.MelonLoaderDirectory;
            if (!string.IsNullOrEmpty(mlDir))
            {
                string candidate = Path.Combine(mlDir, "Il2CppAssemblies");
                if (Directory.Exists(candidate))
                    return candidate;
            }
        }
        catch
        {
            // Older MelonLoader versions might not expose MelonEnvironment
        }

        // Fallback: derive from the process's base directory
        string appBase = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(appBase, "MelonLoader", "Il2CppAssemblies");
    }

    // ── Core fix logic (same algorithm as the EXE, embedded for self-sufficiency) ──

    /// <summary>
    /// Removes all duplicate type definitions from a single assembly.
    /// </summary>
    /// <returns>Number of duplicate types removed (0 = nothing changed).</returns>
    private static int FixAssembly(string path)
    {
        byte[] data = File.ReadAllBytes(path);

        // ── Phase 1: dnlib – detect and remove all duplicate type definitions ────
        using var module = DN.ModuleDefMD.Load(data);

        var seen     = new HashSet<string>(StringComparer.Ordinal);
        var toRemove = new List<DN.TypeDef>();

        foreach (DN.TypeDef type in module.GetTypes())
        {
            string fullName = type.FullName;
            if (!seen.Add(fullName))
            {
                // Duplicate full name — keep first occurrence, queue rest for removal.
                // We remove ALL duplicate types (not just <>O-named ones) because:
                //   • No valid .NET assembly contains duplicate type definitions.
                //   • Il2Cpp can duplicate parent types (e.g. <>c) whose children
                //     (e.g. <>c/<>O) also share the same full name; keeping the
                //     duplicate parent while removing only the child still causes
                //     a BadImageFormatException at runtime.
                toRemove.Add(type);
            }
        }

        if (toRemove.Count == 0)
            return 0;

        foreach (DN.TypeDef t in toRemove)
        {
            if (t.IsNested)
                t.DeclaringType.NestedTypes.Remove(t);
            else
                module.Types.Remove(t);
        }

        using var msAfterDnlib = new MemoryStream();
        module.Write(msAfterDnlib);
        data = msAfterDnlib.ToArray();

        // ── Phase 2: Mono.Cecil – metadata normalization after structural changes ─
        try
        {
            using var msIn      = new MemoryStream(data);
            var readerParams    = new Cecil.ReaderParameters { ReadingMode = Cecil.ReadingMode.Immediate };
            using var asmDef    = Cecil.AssemblyDefinition.ReadAssembly(msIn, readerParams);
            using var msOut     = new MemoryStream();
            asmDef.Write(msOut);
            data = msOut.ToArray();
        }
        catch (Exception cecilEx)
        {
            // If Cecil normalization fails, the dnlib-cleaned data is still usable;
            // log the warning but proceed with writing it.
            MelonLogger.Warning($"[Il2CppAssemblyFixer] Cecil normalization skipped for " +
                                $"'{Path.GetFileName(path)}': {cecilEx.Message}");
        }

        File.WriteAllBytes(path, data);
        return toRemove.Count;
    }
}
