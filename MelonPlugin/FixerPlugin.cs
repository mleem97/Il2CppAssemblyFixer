using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MelonLoader;
using MelonLoader.Utils;
using DN = dnlib.DotNet;
using DNEmit = dnlib.DotNet.Emit;
using Cecil = Mono.Cecil;

// MelonLoader plugin metadata – runs BEFORE MelonMods are loaded
[assembly: MelonInfo(typeof(Il2CppAssemblyFixerPlugin.FixerPlugin), "Il2CppAssemblyFixer", "1.50.3", "mleem97",
    "https://github.com/mleem97/Il2CppAssemblyFixer")]
[assembly: MelonGame]

namespace Il2CppAssemblyFixerPlugin;

/// <summary>
/// MelonPlugin that repairs Il2Cpp-generated assemblies before any MelonMod is loaded.
///
/// What it fixes:
///   • BadImageFormatException from duplicate type defs ('&lt;&gt;O' delegate caches)
///   • ModuleWriterException on Unity.Collections.dll caused by TypeSpec-wrapped refs
///   • Subtle metadata corruption normalized via Mono.Cecil rewrite
///
/// Caching: each fixed file is hashed into a manifest in the Il2CppAssemblies dir.
/// On subsequent launches, files whose hash still matches are skipped completely –
/// so the plugin only does work after MelonLoader regenerates assemblies.
///
/// Safety: every operation is wrapped in try/catch; the plugin will NEVER throw
/// an exception out into MelonLoader. Worst case it logs a warning and continues.
/// </summary>
public class FixerPlugin : MelonPlugin
{
    private const string ManifestFileName = ".il2cppfixer-manifest";

    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }

    // ── Lifecycle hook ─────────────────────────────────────────────────────
    public override void OnPreInitialization()
    {
        // Outer guard: under no circumstances let an exception escape into MelonLoader.
        try
        {
            RunFixer();
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[Il2CppAssemblyFixer] Fatal: {ex.GetType().Name}: {ex.Message}");
            MelonLogger.Error("[Il2CppAssemblyFixer] Plugin aborted to keep MelonLoader running.");
        }
    }

    private static void RunFixer()
    {
        MelonLogger.Msg("═══════════════════════════════════════════════════════════");
        MelonLogger.Msg("  Il2CppAssemblyFixer – scanning Il2CppAssemblies …");
        MelonLogger.Msg("═══════════════════════════════════════════════════════════");

        string? assembliesDir = ResolveAssembliesDirectory();

        if (string.IsNullOrEmpty(assembliesDir) || !Directory.Exists(assembliesDir))
        {
            MelonLogger.Warning($"[Il2CppAssemblyFixer] Il2CppAssemblies directory not found: " +
                                $"'{assembliesDir ?? "<null>"}'");
            MelonLogger.Warning("[Il2CppAssemblyFixer] If mods fail to load, run Il2CppAssemblyFixer.exe manually.");
            return;
        }

        MelonLogger.Msg($"[Il2CppAssemblyFixer] Scanning: {assembliesDir}");

        // Load manifest of previously fixed files (hash per filename).
        Dictionary<string, string> manifest = LoadManifest(assembliesDir);
        bool manifestDirty = false;

        string[] dlls = Directory.GetFiles(assembliesDir, "*.dll", SearchOption.TopDirectoryOnly);
        int processed = 0, fixedCount = 0, skipped = 0, errors = 0, removedTypes = 0;

        foreach (string dll in dlls)
        {
            string fileName = Path.GetFileName(dll);
            string currentHash;
            try { currentHash = HashFile(dll); }
            catch (Exception ex)
            {
                errors++;
                MelonLogger.Warning($"[Il2CppAssemblyFixer] Cannot hash '{fileName}': {ex.Message}");
                continue;
            }

            // Skip files whose current hash already matches a previous fix.
            if (manifest.TryGetValue(fileName, out string? savedHash) && savedHash == currentHash)
            {
                skipped++;
                continue;
            }

            processed++;
            try
            {
                int removed = FixAssembly(dll);
                removedTypes += removed;
                if (removed > 0)
                {
                    fixedCount++;
                    MelonLogger.Msg($"[Il2CppAssemblyFixer] Fixed {removed} duplicate(s) in: {fileName}");
                }

                // Always update manifest with post-fix hash (so unchanged files skip next time).
                manifest[fileName] = HashFile(dll);
                manifestDirty = true;
            }
            catch (Exception ex)
            {
                errors++;
                MelonLogger.Error($"[Il2CppAssemblyFixer] Error processing '{fileName}': {ex.Message}");
            }
        }

        if (manifestDirty)
        {
            try { SaveManifest(assembliesDir, manifest); }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Il2CppAssemblyFixer] Could not write manifest: {ex.Message}");
            }
        }

        MelonLogger.Msg($"[Il2CppAssemblyFixer] Summary – scanned: {dlls.Length}  " +
                        $"processed: {processed}  fixed: {fixedCount}  " +
                        $"skipped (cached): {skipped}  errors: {errors}  " +
                        $"types removed: {removedTypes}");
        MelonLogger.Msg("═══════════════════════════════════════════════════════════");
    }

    // ── Manifest (per-file hash cache) ────────────────────────────────────

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
        catch
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

    // ── Path resolution ────────────────────────────────────────────────────

    private static string? ResolveAssembliesDirectory()
    {
        // Primary: MelonLoader environment API
        try
        {
            string mlDir = MelonEnvironment.MelonLoaderDirectory;
            if (!string.IsNullOrEmpty(mlDir))
            {
                string candidate = Path.Combine(mlDir, "Il2CppAssemblies");
                if (Directory.Exists(candidate)) return candidate;
            }
        }
        catch
        {
            // Older MelonLoader versions might not expose MelonEnvironment.
        }

        // Fallback 1: <process base>\MelonLoader\Il2CppAssemblies
        string appBase = AppDomain.CurrentDomain.BaseDirectory;
        string fallback = Path.Combine(appBase, "MelonLoader", "Il2CppAssemblies");
        if (Directory.Exists(fallback)) return fallback;

        // Fallback 2: walk up from the loaded plugin assembly looking for MelonLoader\Il2CppAssemblies
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
        catch { /* ignore */ }

        return fallback; // return the conventional path even if missing – caller will warn
    }

    // ── Core fix logic (same algorithm as the EXE, embedded for self-sufficiency) ──

    private static int FixAssembly(string path)
    {
        byte[] data = File.ReadAllBytes(path);

        // ── Phase 1: dnlib – detect and remove duplicate type definitions ──
        using var module = DN.ModuleDefMD.Load(data);

        Dictionary<DN.TypeDef, int> referenceCounts = BuildTypeReferenceCounts(module);
        var toRemove = new List<DN.TypeDef>();

        foreach (IGrouping<string, DN.TypeDef> group in module.GetTypes().GroupBy(t => t.FullName, StringComparer.Ordinal))
        {
            List<DN.TypeDef> duplicates = group.ToList();
            if (duplicates.Count < 2) continue;

            int referencedCopies = duplicates.Count(t => referenceCounts.TryGetValue(t, out int c) && c > 0);
            if (referencedCopies > 1)
            {
                MelonLogger.Warning($"[Il2CppAssemblyFixer] Duplicate group '{group.Key}' has " +
                                    $"{referencedCopies} referenced copies; skipping unsafe removal.");
                continue;
            }

            foreach (DN.TypeDef dup in duplicates.Where(t => !referenceCounts.TryGetValue(t, out int c) || c == 0))
                toRemove.Add(dup);
        }

        if (toRemove.Count == 0) return 0;

        foreach (DN.TypeDef t in toRemove)
        {
            if (t.IsNested) t.DeclaringType.NestedTypes.Remove(t);
            else            module.Types.Remove(t);
        }

        using var msAfterDnlib = new MemoryStream();
        module.Write(msAfterDnlib);
        data = msAfterDnlib.ToArray();

        // ── Phase 2: Mono.Cecil – metadata normalization after structural changes ──
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
            // dnlib-cleaned data is still usable; log warning and proceed.
            MelonLogger.Warning($"[Il2CppAssemblyFixer] Cecil normalization skipped for " +
                                $"'{Path.GetFileName(path)}': {cecilEx.Message}");
        }

        File.WriteAllBytes(path, data);
        return toRemove.Count;
    }

    private static Dictionary<DN.TypeDef, int> BuildTypeReferenceCounts(DN.ModuleDefMD module)
    {
        var counts = new Dictionary<DN.TypeDef, int>(new ReferenceComparer<DN.TypeDef>());
        foreach (DN.TypeDef type in module.GetTypes())
            counts[type] = 0;

        void Increment(DN.TypeDef? type)
        {
            if (type != null && counts.ContainsKey(type))
                counts[type]++;
        }

        void ScanTypeRef(DN.ITypeDefOrRef? typeRef)
        {
            switch (typeRef)
            {
                case DN.TypeDef typeDef:
                    Increment(typeDef);
                    break;
                case DN.TypeSpec typeSpec:
                    ScanTypeSig(typeSpec.TypeSig);
                    break;
            }
        }

        void ScanTypeSig(DN.TypeSig? sig)
        {
            while (sig != null)
            {
                switch (sig)
                {
                    case DN.TypeDefOrRefSig typeDefOrRefSig:
                        ScanTypeRef(typeDefOrRefSig.TypeDefOrRef);
                        return;

                    case DN.GenericInstSig genericInstSig:
                        ScanTypeSig(genericInstSig.GenericType);
                        foreach (DN.TypeSig argument in genericInstSig.GenericArguments)
                            ScanTypeSig(argument);
                        return;

                    case DN.FnPtrSig fnPtrSig:
                        ScanMethodSig(fnPtrSig.MethodSig);
                        return;

                    case DN.CModOptSig cModOptSig:  sig = cModOptSig.Next;  continue;
                    case DN.CModReqdSig cModReqdSig: sig = cModReqdSig.Next; continue;
                    case DN.PinnedSig pinnedSig:    sig = pinnedSig.Next;    continue;
                    case DN.PtrSig ptrSig:          sig = ptrSig.Next;       continue;
                    case DN.ByRefSig byRefSig:      sig = byRefSig.Next;     continue;
                    case DN.SZArraySig szArraySig:  sig = szArraySig.Next;   continue;
                    case DN.ArraySig arraySig:      sig = arraySig.Next;     continue;
                    case DN.SentinelSig sentinelSig: sig = sentinelSig.Next; continue;

                    default:
                        return;
                }
            }
        }

        void ScanMethodSig(DN.MethodSig? methodSig)
        {
            if (methodSig == null) return;
            ScanTypeSig(methodSig.RetType);
            foreach (DN.TypeSig parameter in methodSig.Params)
                ScanTypeSig(parameter);
        }

        void ScanFieldSig(DN.FieldSig? fieldSig)
        {
            if (fieldSig != null) ScanTypeSig(fieldSig.Type);
        }

        void ScanMethodBody(DN.MethodDef method)
        {
            DNEmit.CilBody body = method.Body;
            if (body == null) return;

            foreach (DNEmit.Local local in body.Variables)
                ScanTypeSig(local.Type);

            foreach (DNEmit.Instruction instruction in body.Instructions)
            {
                switch (instruction.Operand)
                {
                    case DN.ITypeDefOrRef typeDefOrRef:
                        ScanTypeRef(typeDefOrRef);
                        break;

                    case DN.MemberRef memberRef:
                        ScanTypeRef(memberRef.DeclaringType);
                        break;

                    case DN.IMethodDefOrRef methodDefOrRef:
                        ScanTypeRef(methodDefOrRef.DeclaringType);
                        break;

                    case DN.IField fieldRef:
                        ScanTypeRef(fieldRef.DeclaringType);
                        break;
                }
            }
        }

        foreach (DN.TypeDef type in module.GetTypes())
        {
            ScanTypeRef(type.BaseType);

            foreach (DN.InterfaceImpl iface in type.Interfaces)
                ScanTypeRef(iface.Interface);

            foreach (DN.CustomAttribute customAttribute in type.CustomAttributes)
                if (customAttribute.Constructor?.DeclaringType is DN.TypeDef attributeType)
                    Increment(attributeType);

            foreach (DN.FieldDef field in type.Fields)
                ScanFieldSig(field.FieldSig);

            foreach (DN.MethodDef method in type.Methods)
            {
                ScanMethodSig(method.MethodSig);

                foreach (DN.CustomAttribute customAttribute in method.CustomAttributes)
                    if (customAttribute.Constructor?.DeclaringType is DN.TypeDef attributeType)
                        Increment(attributeType);

                ScanMethodBody(method);
            }

            foreach (DN.EventDef eventDef in type.Events)
                ScanTypeRef(eventDef.EventType);
        }

        return counts;
    }
}
