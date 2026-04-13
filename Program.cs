using System;
using System.Collections.Generic;
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
        Console.WriteLine("=== Il2CppAssemblyFixer x64 (NET 10) ===");
        
        string targetDir = args.Length > 0 ? args[0] : AutoDetectAssembliesPath();
        if (targetDir == null || !Directory.Exists(targetDir))
        {
            Console.Error.WriteLine("Usage: Il2CppAssemblyFixer.exe <Il2CppAssemblies>");
            WaitForKey();
            return 1;
        }

        Console.WriteLine($"Target: {targetDir}");
        var dllFiles = Directory.GetFiles(targetDir, "*.dll");
        
        int fixedFiles = 0, totalRemoved = 0;
        foreach (var dllPath in dllFiles)
        {
            try
            {
                int removed = FixAssembly(dllPath);
                if (removed > 0)
                {
                    fixedFiles++;
                    totalRemoved += removed;
                    Console.WriteLine($"FIXED {Path.GetFileName(dllPath)}: {removed} duplicates");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR {Path.GetFileName(dllPath)}: {ex.Message}");
            }
        }

        Console.WriteLine($"Done! Fixed {fixedFiles} files, {totalRemoved} duplicates removed.");
        WaitForKey();
        return 0;
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
            string steamPath = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            if (steamPath == null) steamPath = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath");
            
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
        
        string[] fallbacks = {"C:\\Program Files (x86)\\Steam", "D:\\SteamLibrary"};
        foreach (var p in fallbacks) if (Directory.Exists(p)) folders.Add(p);
        
        return folders;
    }

    static int FixAssembly(string dllPath)
    {
        var module = ModuleDefMD.Load(File.ReadAllBytes(dllPath));
        int totalRemoved = RemoveDuplicateTopLevelTypes(module);
        
        foreach (var type in module.GetTypes().ToList())
            totalRemoved += RemoveDuplicateNestedTypes(type);
        
        if (totalRemoved > 0)
        {
            string temp = dllPath + ".tmp";
            module.Write(temp);
            module.Dispose();
            File.Delete(dllPath);
            File.Move(temp, dllPath);
        }
        else module.Dispose();
        
        return totalRemoved;
    }

    static int RemoveDuplicateTopLevelTypes(ModuleDefMD module)
    {
        var seen = new HashSet<string>();
        var toRemove = new List<TypeDef>();
        foreach (var type in module.Types.ToList())
            if (!seen.Add(type.FullName)) toRemove.Add(type);
        foreach (var dup in toRemove) module.Types.Remove(dup);
        return toRemove.Count;
    }

    static int RemoveDuplicateNestedTypes(TypeDef type)
    {
        if (type.NestedTypes.Count < 2) return 0;
        var seen = new HashSet<string>();
        var toRemove = new List<TypeDef>();
        foreach (var nested in type.NestedTypes.ToList())
            if (!seen.Add(nested.Name.String)) toRemove.Add(nested);
        foreach (var dup in toRemove) type.NestedTypes.Remove(dup);
        return toRemove.Count;
    }

    static void WaitForKey()
    {
        if (!Console.IsInputRedirected)
        {
            Console.WriteLine("\nPress any key...");
            Console.ReadKey(true);
        }
    }
}
