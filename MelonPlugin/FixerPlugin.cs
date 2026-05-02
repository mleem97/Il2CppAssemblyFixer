using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using MelonLoader;
using MelonLoader.Utils;
using DN = dnlib.DotNet;
using DNEmit = dnlib.DotNet.Emit;
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
    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public bool Equals(T x, T y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }

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

        Dictionary<DN.TypeDef, int> referenceCounts = BuildTypeReferenceCounts(module);
        var toRemove = new List<DN.TypeDef>();

        foreach (IGrouping<string, DN.TypeDef> group in module.GetTypes().GroupBy(type => type.FullName, StringComparer.Ordinal))
        {
            List<DN.TypeDef> duplicates = group.ToList();
            if (duplicates.Count < 2)
                continue;

            int referencedCopies = duplicates.Count(type => referenceCounts.TryGetValue(type, out int count) && count > 0);
            if (referencedCopies > 1)
            {
                MelonLogger.Warning($"[Il2CppAssemblyFixer] Duplicate group '{group.Key}' has {referencedCopies} referenced copies; skipping unsafe removal.");
                continue;
            }

            foreach (DN.TypeDef duplicate in duplicates.Where(type => !referenceCounts.TryGetValue(type, out int count) || count == 0))
            {
                toRemove.Add(duplicate);
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

                    case DN.CModOptSig cModOptSig:
                        sig = cModOptSig.Next;
                        continue;

                    case DN.CModReqdSig cModReqdSig:
                        sig = cModReqdSig.Next;
                        continue;

                    case DN.PinnedSig pinnedSig:
                        sig = pinnedSig.Next;
                        continue;

                    case DN.PtrSig ptrSig:
                        sig = ptrSig.Next;
                        continue;

                    case DN.ByRefSig byRefSig:
                        sig = byRefSig.Next;
                        continue;

                    case DN.SZArraySig szArraySig:
                        sig = szArraySig.Next;
                        continue;

                    case DN.ArraySig arraySig:
                        sig = arraySig.Next;
                        continue;

                    case DN.SentinelSig sentinelSig:
                        sig = sentinelSig.Next;
                        continue;

                    default:
                        return;
                }
            }
        }

        void ScanMethodSig(DN.MethodSig? methodSig)
        {
            if (methodSig == null)
                return;

            ScanTypeSig(methodSig.RetType);
            foreach (DN.TypeSig parameter in methodSig.Params)
                ScanTypeSig(parameter);
        }

        void ScanFieldSig(DN.FieldSig? fieldSig)
        {
            if (fieldSig != null)
                ScanTypeSig(fieldSig.Type);
        }

        void ScanMethodBody(DN.MethodDef method)
        {
            DNEmit.CilBody body = method.Body;
            if (body == null)
                return;

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
                        if (memberRef.DeclaringType is DN.TypeDef declaringType)
                            Increment(declaringType);
                        break;

                    case DN.IMethodDefOrRef methodDefOrRef:
                        if (methodDefOrRef.DeclaringType is DN.TypeDef methodDeclaringType)
                            Increment(methodDeclaringType);
                        break;

                    case DN.IField fieldRef:
                        if (fieldRef.DeclaringType is DN.TypeDef fieldDeclaringType)
                            Increment(fieldDeclaringType);
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
                if (customAttribute.Constructor != null && customAttribute.Constructor.DeclaringType is DN.TypeDef attributeType)
                    Increment(attributeType);

            foreach (DN.FieldDef field in type.Fields)
                ScanFieldSig(field.FieldSig);

            foreach (DN.MethodDef method in type.Methods)
            {
                ScanMethodSig(method.MethodSig);

                foreach (DN.CustomAttribute customAttribute in method.CustomAttributes)
                    if (customAttribute.Constructor != null && customAttribute.Constructor.DeclaringType is DN.TypeDef attributeType)
                        Increment(attributeType);

                ScanMethodBody(method);
            }

            foreach (DN.EventDef eventDef in type.Events)
                ScanTypeRef(eventDef.EventType);
        }

        return counts;
    }
}
