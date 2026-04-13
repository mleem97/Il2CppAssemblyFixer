using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

// Explicit Aliases for both libraries
using DN = dnlib.DotNet;
using Cecil = Mono.Cecil;

namespace Il2CppAssemblyFixer;

class Program
{
    const string GameFolder = "Data Center";

    static int Main(string[] args)
    {
        Console.WriteLine("=== Il2Cpp Assembly Fixer x64 (NET 10) ===");
        Console.WriteLine("Libraries: dnlib & Mono.Cecil included.");

        // 1. Mandatory MelonLoader AGF Regeneration
        RunMelonLoaderRegen();

        bool rewrite = args.Any(a => a == "--rewrite");
        string targetDir = args.Where(a => !a.StartsWith("--")).FirstOrDefault() ?? AutoDetectPath();

        if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
        {
            Console.Error.WriteLine("ERROR: Target directory not found.");
            return 1;
        }

        Console.WriteLine($"Processing directory: {targetDir}");
        var dllFiles = Directory.GetFiles(targetDir, "*.dll");
        
        int fixedCount = 0;
        foreach (var dllPath in dllFiles)
        {
            try {
                if (ProcessAssembly(dllPath, rewrite)) fixedCount++;
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed {Path.GetFileName(dllPath)}: {ex.Message}");
            }
        }

        Console.WriteLine($"\nFinished. Fixed {fixedCount} assemblies.");
        return 0;
    }

    static void RunMelonLoaderRegen()
    {
        Console.WriteLine("Running MelonLoader AGF Regeneration...");
        try {
            string installer = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MelonLoader.Installer.exe");
            if (File.Exists(installer)) {
                Process.Start(new ProcessStartInfo(installer, "--melonloader.agfregenerate") { UseShellExecute = true })?.WaitForExit();
            }
        } catch { /* Ignore installer errors */ }
    }

    static bool ProcessAssembly(string path, bool forceRewrite)
    {
        byte[] data = File.ReadAllBytes(path);
        bool modified = false;

        // --- PHASE 1: dnlib Duplicate Removal ---
        using (var module = DN.ModuleDefMD.Load(data))
        {
            var seen = new HashSet<string>();
            var toRemove = new List<DN.TypeDef>();

            foreach (var type in module.GetTypes())
            {
                // Target the specific "<>O" duplicates causing the crash
                if (!seen.Add(type.FullName) && type.Name.Contains("<>O"))
                    toRemove.Add(type);
            }

            if (toRemove.Count > 0)
            {
                Console.WriteLine($"  [{Path.GetFileName(path)}] Removing {toRemove.Count} duplicates.");
                foreach (var t in toRemove)
                {
                    if (t.IsNested) t.DeclaringType.NestedTypes.Remove(t);
                    else module.Types.Remove(t);
                }
                
                using var ms = new MemoryStream();
                module.Write(ms);
                data = ms.ToArray();
                modified = true;
            }
        }

        // --- PHASE 2: Mono.Cecil Metadata Normalization ---
        // Always run if forced or if dnlib made changes
        if (forceRewrite || modified)
        {
            using var msIn = new MemoryStream(data);
            using var asm = Cecil.AssemblyDefinition.ReadAssembly(msIn, new Cecil.ReaderParameters { ReadingMode = Cecil.ReadingMode.Immediate });
            using var msOut = new MemoryStream();
            asm.Write(msOut);
            data = msOut.ToArray();
            modified = true;
        }

        if (modified) File.WriteAllBytes(path, data);
        return modified;
    }

    static string AutoDetectPath()
    {
        try {
            string sP = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null)
                        ?? (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null);
            if (sP != null) {
                string path = Path.Combine(sP, "steamapps", "common", GameFolder, "MelonLoader", "Il2CppAssemblies");
                if (Directory.Exists(path)) return path;
            }
        } catch { }
        return null;
    }
}
