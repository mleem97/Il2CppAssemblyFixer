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
        Console.WriteLine("=== Il2CppAssemblyFixer ===");
        Console.WriteLine("Fixes duplicate type definitions in Il2CppInterop-generated assemblies.");
        Console.WriteLine();

        string targetDir;

        if (args.Length > 0)
        {
            targetDir = args[0];
        }
        else
        {
            targetDir = AutoDetectAssembliesPath();
            if (targetDir == null)
            {
                Console.Error.WriteLine("Could not auto-detect the game installation.");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: Il2CppAssemblyFixer <path-to-Il2CppAssemblies>");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Example:");
                Console.Error.WriteLine("  Il2CppAssemblyFixer \"C:\\SteamLibrary\\steamapps\\common\\Data Center\\MelonLoader\\Il2CppAssemblies\"");
                WaitForKey();
                return 1;
            }
        }

        if (!Directory.Exists(targetDir))
        {
            Console.Error.WriteLine($"Directory not found: {targetDir}");
            WaitForKey();
            return 1;
        }

        Console.WriteLine($"Target: {targetDir}");
        Console.WriteLine();

        var dllFiles = Directory.GetFiles(targetDir, "*.dll");
        int fixedFiles = 0;
        int totalDuplicatesRemoved = 0;

        foreach (var dllPath in dllFiles)
        {
            try
            {
                int removed = FixAssembly(dllPath);
                if (removed > 0)
                {
                    fixedFiles++;
                    totalDuplicatesRemoved += removed;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  ERROR {Path.GetFileName(dllPath)}: {ex.Message}");
            }
        }

        Console.WriteLine();
        if (fixedFiles > 0)
            Console.WriteLine($"Done! Fixed {fixedFiles} file(s), removed {totalDuplicatesRemoved} duplicate type(s).");
        else
            Console.WriteLine("Done. No duplicate types found — assemblies are clean.");

        WaitForKey();
        return 0;
    }

    static string AutoDetectAssembliesPath()
    {
        // Try to find Steam library folders from the registry, then locate the game.
        var libraryFolders = GetSteamLibraryFolders();

        foreach (var lib in libraryFolders)
        {
            string candidate = Path.Combine(lib, "steamapps", "common", GameFolder,
                                            "MelonLoader", "Il2CppAssemblies");
            if (Directory.Exists(candidate))
            {
                Console.WriteLine($"Auto-detected game at: {Path.GetDirectoryName(Path.GetDirectoryName(candidate))}");
                return candidate;
            }
        }

        return null;
    }

    static List<string> GetSteamLibraryFolders()
    {
        var folders = new List<string>();

        try
        {
            // Read Steam install path from registry.
            string steamPath = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                "InstallPath", null) as string;

            steamPath ??= Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
                "InstallPath", null) as string;

            if (steamPath != null)
            {
                folders.Add(steamPath);

                // Parse libraryfolders.vdf for additional library paths.
                string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdfPath))
                {
                    foreach (string line in File.ReadAllLines(vdfPath))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("\"path\""))
                        {
                            // Format: "path"    "D:\\SteamLibrary"
                            int firstQuote = trimmed.IndexOf('"', 5);
                            int lastQuote = trimmed.LastIndexOf('"');
                            if (firstQuote >= 0 && lastQuote > firstQuote + 1)
                            {
                                string path = trimmed[(firstQuote + 1)..lastQuote]
                                    .Replace("\\\\", "\\");
                                if (Directory.Exists(path))
                                    folders.Add(path);
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Registry access can fail on non-Windows or restricted environments.
        }

        // Common default locations as fallback.
        string[] commonPaths =
        {
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
            @"D:\SteamLibrary",
            @"E:\SteamLibrary",
            @"F:\SteamLibrary",
        };

        foreach (var p in commonPaths)
        {
            if (Directory.Exists(p) && !folders.Contains(p))
                folders.Add(p);
        }

        return folders;
    }

    static int FixAssembly(string dllPath)
    {
        byte[] rawBytes = File.ReadAllBytes(dllPath);

        var module = ModuleDefMD.Load(rawBytes, new ModuleCreationOptions
        {
            TryToLoadPdbFromDisk = false
        });

        int totalRemoved = 0;

        totalRemoved += RemoveDuplicateTopLevelTypes(module);

        foreach (var type in module.GetTypes().ToList())
        {
            totalRemoved += RemoveDuplicateNestedTypes(type);
        }

        if (totalRemoved > 0)
        {
            string fileName = Path.GetFileName(dllPath);
            Console.WriteLine($"  FIXED {fileName}: removed {totalRemoved} duplicate type(s)");

            var options = new dnlib.DotNet.Writer.ModuleWriterOptions(module)
            {
                Logger = DummyLogger.NoThrowInstance
            };

            string tempPath = dllPath + ".tmp";
            module.Write(tempPath, options);
            module.Dispose();

            File.Delete(dllPath);
            File.Move(tempPath, dllPath);
        }
        else
        {
            module.Dispose();
        }

        return totalRemoved;
    }

    static int RemoveDuplicateTopLevelTypes(ModuleDefMD module)
    {
        var seen = new HashSet<string>();
        var toRemove = new List<TypeDef>();

        foreach (var type in module.Types)
        {
            string key = type.FullName;
            if (!seen.Add(key))
                toRemove.Add(type);
        }

        foreach (var dup in toRemove)
            module.Types.Remove(dup);

        return toRemove.Count;
    }

    static int RemoveDuplicateNestedTypes(TypeDef type)
    {
        if (type.NestedTypes.Count < 2)
            return 0;

        var seen = new HashSet<string>();
        var toRemove = new List<TypeDef>();

        foreach (var nested in type.NestedTypes)
        {
            string key = nested.Name.String;
            if (!seen.Add(key))
                toRemove.Add(nested);
        }

        foreach (var dup in toRemove)
            type.NestedTypes.Remove(dup);

        return toRemove.Count;
    }

    static void WaitForKey()
    {
        if (!Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey(true);
        }
    }
}
