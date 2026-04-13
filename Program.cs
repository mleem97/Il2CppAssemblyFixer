using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using Microsoft.Win32;

namespace Il2CppAssemblyFixer;

class Program
{
    const string GameFolder = "Data Center";

    static int Main(string[] args)
    {
        Console.WriteLine("=== Il2Cpp Assembly Fixer (dnlib Enhanced) ===");

        // 1. MelonLoader AGF Regeneration ausführen
        RunMelonLoaderRegen();

        // Parameter parsen
        bool rewrite = args.Any(a => a == "--rewrite");
        string targetDir = args.Where(a => !a.StartsWith("--")).FirstOrDefault() ?? AutoDetectAssembliesPath();

        if (targetDir == null || !Directory.Exists(targetDir))
        {
            Console.Error.WriteLine("Usage: Il2CppAssemblyFixer.exe [--rewrite] <PathToAssemblies>");
            Console.WriteLine("\nOptionen:");
            Console.WriteLine("  --rewrite   Erzwingt das Neuschreiben aller DLLs (Metadaten-Normalisierung)");
            WaitForKey();
            return 1;
        }

        Console.WriteLine($"Target Directory: {targetDir}");
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
                Console.Error.WriteLine($"ERROR processing {Path.GetFileName(dllPath)}: {ex.Message}");
            }
        }

        Console.WriteLine($"--- Fertig! ---");
        Console.WriteLine($"Bearbeitete Dateien: {fixedFiles}");
        Console.WriteLine($"Entfernte Duplikate: {totalRemoved}");
        
        WaitForKey();
        return 0;
    }

    static void RunMelonLoaderRegen()
    {
        Console.WriteLine("Starte MelonLoader AGF Regeneration...");
        try
        {
            // Sucht nach der MelonLoader.Installer.exe oder nutzt den CLI Befehl falls im Pfad
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "MelonLoader.Installer.exe", // Falls im gleichen Ordner
                Arguments = "--melonloader.agfregenerate",
                UseShellExecute = true,
                CreateNoWindow = false
            };
            
            // Hinweis: Dies setzt voraus, dass die Datei existiert oder über PATH erreichbar ist.
            // Alternativ kann hier der absolute Pfad zur Game.exe mit dem Argument eingefügt werden.
            Console.WriteLine("Führe --melonloader.agfregenerate aus...");
            // Process.Start(psi)?.WaitForExit(); 
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Hinweis: AGF Regeneration konnte nicht automatisch gestartet werden: {ex.Message}");
        }
    }

    static int ProcessAssembly(string dllPath, bool forceRewrite)
    {
        string fileName = Path.GetFileName(dllPath);
        // Datei in Byte-Array laden um Locks zu vermeiden
        byte[] data = File.ReadAllBytes(dllPath);
        var module = ModuleDefMD.Load(data);
        
        int removedCount = 0;

        // Phase 1: Scan nach <>O Typen (aus Skript 1)
        var specialTypes = module.GetTypes().Where(t => t.Name.Contains("<>O")).ToList();
        if (specialTypes.Count > 0)
        {
            Console.WriteLine($"  [{fileName}] Gefundene '<>O' Typen: {specialTypes.Count}");
        }

        // Phase 2: Top-Level Duplikate
        var seenTopLevel = new HashSet<string>();
        var toRemoveTop = new List<TypeDef>();
        foreach (var type in module.Types)
        {
            if (!seenTopLevel.Add(type.FullName))
                toRemoveTop.Add(type);
        }

        foreach (var dup in toRemoveTop)
        {
            module.Types.Remove(dup);
            removedCount++;
        }

        // Phase 3: Nested Duplikate
        foreach (var type in module.GetTypes())
        {
            removedCount += RemoveDuplicateNestedTypes(type);
        }

        // Phase 4: Speichern
        if (removedCount > 0 || forceRewrite)
        {
            if (removedCount > 0) Console.WriteLine($"  [{fileName}] Entferne {removedCount} Duplikate...");
            if (forceRewrite) Console.WriteLine($"  [{fileName}] Rewrite erzwungen.");

            string temp = dllPath + ".tmp";
            module.Write(temp);
            module.Dispose();
            
            File.Delete(dllPath);
            File.Move(temp, dllPath);
            return removedCount == 0 ? 1 : removedCount; // Return > 0 falls rewrite
        }
        
        module.Dispose();
        return 0;
    }

    static int RemoveDuplicateNestedTypes(TypeDef type)
    {
        if (type.NestedTypes.Count < 2) return 0;
        
        var seen = new HashSet<string>();
        var toRemove = new List<TypeDef>();
        
        foreach (var nested in type.NestedTypes)
        {
            if (!seen.Add(nested.Name.String))
                toRemove.Add(nested);
        }

        foreach (var dup in toRemove)
        {
            type.NestedTypes.Remove(dup);
        }

        int count = toRemove.Count;
        // Rekursiv für tiefere Verschachtelungen
        foreach (var nested in type.NestedTypes)
        {
            count += RemoveDuplicateNestedTypes(nested);
        }

        return count;
    }

    static string AutoDetectAssembliesPath()
    {
        var folders = GetSteamLibraryFolders();
        foreach (var lib in folders)
        {
            string candidate = Path.Combine(lib, "steamapps", "common", GameFolder, "MelonLoader", "Il2CppAssemblies");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    static List<string> GetSteamLibraryFolders()
    {
        var folders = new List<string>();
        try
        {
            string steamPath = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
                               ?? (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath");
            
            if (steamPath != null)
            {
                folders.Add(steamPath);
                string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdfPath))
                {
                    foreach (string line in File.ReadAllLines(vdfPath))
                    {
                        if (line.Trim().StartsWith("\"path\""))
                        {
                            int start = line.IndexOf('"', 6) + 1;
                            int end = line.LastIndexOf('"');
                            if (start > 0 && end > start)
                            {
                                string path = line.Substring(start, end - start).Replace("\\\\", "\\");
                                if (Directory.Exists(path)) folders.Add(path);
                            }
                        }
                    }
                }
            }
        }
        catch { }
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
