using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

// Aliase zur Vermeidung von Namenskonflikten zwischen dnlib und Cecil
using DN = dnlib.DotNet;
using Cecil = Mono.Cecil;

namespace Il2CppAssemblyFixer;

class Program
{
    const string GameFolder = "Data Center";

    static int Main(string[] args)
    {
        Console.WriteLine("=== Il2Cpp Assembly Fixer x64 (NET 10) ===");
        Console.WriteLine("Modus: dnlib (Fixing) + Mono.Cecil (Metadata Normalization)");
        Console.WriteLine("----------------------------------------------------------");

        // 1. MelonLoader AGF Regeneration
        RunMelonLoaderRegen();

        // Parameter parsen
        bool rewrite = args.Any(a => a == "--rewrite");
        string targetDir = args.Where(a => !a.StartsWith("--")).FirstOrDefault() ?? AutoDetectAssembliesPath();

        if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
        {
            Console.Error.WriteLine("FEHLER: Zielverzeichnis konnte nicht gefunden werden.");
            Console.WriteLine("Usage: Il2CppAssemblyFixer.exe [--rewrite] <Pfad>");
            WaitForKey();
            return 1;
        }

        Console.WriteLine($"Zielverzeichnis: {targetDir}");
        var dllFiles = Directory.GetFiles(targetDir, "*.dll");
        
        int fixedFiles = 0;
        int totalRemoved = 0;

        foreach (var dllPath in dllFiles)
        {
            try
            {
                int removed = ProcessAssembly(dllPath, rewrite);
                if (removed > 0 || rewrite)
                {
                    fixedFiles++;
                    totalRemoved += Math.Max(0, removed);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR bei {Path.GetFileName(dllPath)}: {ex.Message}");
            }
        }

        Console.WriteLine("\n--- ZUSAMMENFASSUNG ---");
        Console.WriteLine($"Bearbeitete Dateien: {fixedFiles}");
        Console.WriteLine($"Entfernte Duplikate: {totalRemoved}");
        
        WaitForKey();
        return 0;
    }

    static void RunMelonLoaderRegen()
    {
        Console.WriteLine("Schritt 1: MelonLoader AGF Regeneration...");
        try
        {
            string installerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MelonLoader.Installer.exe");
            
            if (File.Exists(installerPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = installerPath, 
                    Arguments = "--melonloader.agfregenerate",
                    UseShellExecute = true
                };
                var proc = Process.Start(psi);
                proc?.WaitForExit();
                Console.WriteLine("Regeneration abgeschlossen.");
            }
            else
            {
                Console.WriteLine("Hinweis: MelonLoader.Installer.exe nicht gefunden. Überspringe.");
            }
        }
        catch (Exception ex) { Console.WriteLine($"Info: AGF Regen übersprungen ({ex.Message})"); }
    }

    static int ProcessAssembly(string dllPath, bool forceRewrite)
    {
        string fileName = Path.GetFileName(dllPath);
        byte[] data = File.ReadAllBytes(dllPath);
        int removed = 0;

        // --- PHASE A: Fixen mit dnlib ---
        using (var module = DN.ModuleDefMD.Load(data))
        {
            // Suche nach <>O Typen
            var oTypes = module.GetTypes().Where(t => t.Name.Contains("<>O")).ToList();
            if (oTypes.Count > 0) Console.WriteLine($"  [{fileName}] '{oTypes.Count}' <>O-Typen entdeckt.");

            // Top-Level Duplikate
            var seenTop = new HashSet<string>();
            var toRemoveTop = new List<DN.TypeDef>();
            foreach (var type in module.Types)
                if (!seenTop.Add(type.FullName)) toRemoveTop.Add(type);
            
            foreach (var t in toRemoveTop) { module.Types.Remove(t); removed++; }

            // Nested Duplikate
            foreach (var type in module.GetTypes().ToList())
                removed += RemoveDuplicateNested(type);

            if (removed > 0)
            {
                Console.WriteLine($"  [{fileName}] {removed} Duplikate entfernt.");
                data = SaveDnlibModule(module);
            }
        }

        // --- PHASE B: Rewrite mit Mono.Cecil (Normalisierung) ---
        if (forceRewrite)
        {
            Console.WriteLine($"  [{fileName}] Metadaten-Rewrite via Mono.Cecil...");
            data = PerformCecilRewrite(data);
        }

        if (removed > 0 || forceRewrite)
        {
            File.WriteAllBytes(dllPath, data);
            return removed > 0 ? removed : 1;
        }

        return 0;
    }

    static int RemoveDuplicateNested(DN.TypeDef type)
    {
        if (type.NestedTypes.Count < 2) return 0;
        var seen = new HashSet<string>();
        var toRemove = new List<DN.TypeDef>();
        foreach (var n in type.NestedTypes)
            if (!seen.Add(n.Name.String)) toRemove.Add(n);
        
        foreach (var t in toRemove) type.NestedTypes.Remove(t);
        
        int count = toRemove.Count;
        foreach (var n in type.NestedTypes) count += RemoveDuplicateNested(n);
        return count;
    }

    static byte[] SaveDnlibModule(DN.ModuleDefMD module)
    {
        using var ms = new MemoryStream();
        module.Write(ms);
        return ms.ToArray();
    }

    static byte[] PerformCecilRewrite(byte[] data)
    {
        using var msIn = new MemoryStream(data);
        var readerParams = new Cecil.ReaderParameters { ReadingMode = Cecil.ReadingMode.Immediate };
        using var assembly = Cecil.AssemblyDefinition.ReadAssembly(msIn, readerParams);
        
        using var msOut = new MemoryStream();
        assembly.Write(msOut);
        return msOut.ToArray();
    }

    static string AutoDetectAssembliesPath()
    {
        foreach (var lib in GetSteamLibraryFolders())
        {
            string path = Path.Combine(lib, "steamapps", "common", GameFolder, "MelonLoader", "Il2CppAssemblies");
            if (Directory.Exists(path)) return path;
        }
        return null;
    }

    static List<string> GetSteamLibraryFolders()
    {
        var folders = new List<string>();
        try
        {
            // Korrigiert für .NET 10: defaultValue (null) hinzugefügt
            string sP = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null)
                        ?? (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null);
            
            if (sP != null)
            {
                folders.Add(sP);
                string vdf = Path.Combine(sP, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    foreach (var line in File.ReadAllLines(vdf))
                    {
                        if (line.Trim().StartsWith("\"path\""))
                        {
                            int start = line.IndexOf('"', 6) + 1;
                            int end = line.LastIndexOf('"');
                            if (start > 0 && end > start)
                            {
                                string p = line.Substring(start, end - start).Replace("\\\\", "\\");
                                if (Directory.Exists(p)) folders.Add(p);
                            }
                        }
                    }
                }
            }
        } 
        catch { /* Ignorieren */ }
        return folders.Distinct().ToList();
    }

    static void WaitForKey()
    {
        if (!Console.IsInputRedirected)
        {
            Console.WriteLine("\nDrücke eine beliebige Taste zum Beenden...");
            Console.ReadKey(true);
        }
    }
}
