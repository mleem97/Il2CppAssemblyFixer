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
    private static readonly DateTime StartTime = DateTime.Now;
    private static int GlobalDuplicateCounter = 0;

    static int Main(string[] args)
    {
        LogHeader();
        
        string targetDir;

        if (args.Length > 0)
        {
            targetDir = args[0];
            Console.WriteLine($"[INFO] Using command line argument: {targetDir}");
        }
        else
        {
            Console.WriteLine("[INFO] No arguments provided, attempting auto-detection...");
            targetDir = AutoDetectAssembliesPath();
            if (targetDir == null)
            {
                Console.Error.WriteLine("[ERROR] Auto-detection failed!");
                PrintUsage();
                WaitForKey();
                return 1;
            }
        }

        if (!Directory.Exists(targetDir))
        {
            Console.Error.WriteLine($"[ERROR] Target directory does not exist: {targetDir}");
            WaitForKey();
            return 1;
        }

        Console.WriteLine($"[INFO] Target directory confirmed: {targetDir}");
        Console.WriteLine($"[INFO] Scanning for DLL files...");
        
        var dllFiles = Directory.GetFiles(targetDir, "*.dll");
        Console.WriteLine($"[INFO] Found {dllFiles.Length} DLL files");
        
        int fixedFiles = 0;
        int totalDuplicatesRemoved = 0;

        foreach (var dllPath in dllFiles)
        {
            string fileName = Path.GetFileName(dllPath);
            Console.WriteLine($"\n{'='.PadRight(80, '=')}");
            Console.WriteLine($"[PROCESSING] DLL: {fileName} ({dllPath})");
            Console.WriteLine($"'='.PadRight(80, '=')");
            
            try
            {
                int removed = FixAssembly(dllPath);
                if (removed > 0)
                {
                    fixedFiles++;
                    totalDuplicatesRemoved += removed;
                    Console.WriteLine($"[SUCCESS] {fileName}: Removed {removed} duplicate(s)");
                }
                else
                {
                    Console.WriteLine($"[CLEAN] {fileName}: No duplicates found");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {fileName}: {ex.Message}");
                Console.Error.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            }
        }

        PrintSummary(fixedFiles, totalDuplicatesRemoved);
        WaitForKey();
        return 0;
    }

    // AutoDetect und GetSteamLibraryFolders bleiben gleich wie vorher...
    static string AutoDetectAssembliesPath() { /* ... gleicher Code ... */ }
    static List<string> GetSteamLibraryFolders() { /* ... gleicher Code ... */ }
    static void LogHeader() { /* ... gleicher Code ... */ }
    static void PrintUsage() { /* ... gleicher Code ... */ }

    static int FixAssembly(string dllPath)
    {
        string fileName = Path.GetFileName(dllPath);
        Console.WriteLine($"[LOAD] Loading {fileName}...");
        
        byte[] rawBytes = File.ReadAllBytes(dllPath);
        Console.WriteLine($"[LOAD] ✓ File loaded: {rawBytes.Length:N0} bytes");

        var module = ModuleDefMD.Load(rawBytes, new ModuleCreationOptions
        {
            TryToLoadPdbFromDisk = false
        });

        Console.WriteLine($"[MODULE] ✓ Loaded: {module.Name}");
        Console.WriteLine($"[MODULE] Types BEFORE cleanup: {module.Types.Count}");

        int totalRemoved = 0;

        // === TOP-LEVEL TYPES ===
        Console.WriteLine($"\n[🔍 TOP-LEVEL] Scanning {module.Types.Count} top-level types...");
        int topLevelRemoved = RemoveDuplicateTopLevelTypes(module);
        totalRemoved += topLevelRemoved;
        Console.WriteLine($"[🗑️  TOP-LEVEL] REMOVED {topLevelRemoved} top-level types");
        Console.WriteLine($"[📊 TOP-LEVEL] Types NOW: {module.Types.Count}");

        // === NESTED TYPES ===
        Console.WriteLine($"\n[🔍 NESTED] Scanning nested types in all types...");
        int nestedRemoved = 0;
        
        foreach (var type in module.GetTypes().ToList())
        {
            int typeNestedRemoved = RemoveDuplicateNestedTypes(type);
            nestedRemoved += typeNestedRemoved;
            totalRemoved += typeNestedRemoved;
            
            if (typeNestedRemoved > 0)
            {
                Console.WriteLine($"[📊 NESTED] {type.FullName}: -{typeNestedRemoved} nested types");
            }
        }
        
        Console.WriteLine($"[🗑️  NESTED] REMOVED {nestedRemoved} nested types total");
        Console.WriteLine($"[📊 FINAL] Total types after cleanup: {module.Types.Count} (-{totalRemoved})");

        if (totalRemoved > 0)
        {
            // === SAVE PROCESS ===
            Console.WriteLine($"\n[💾 SAVING] Writing cleaned assembly...");
            
            var options = new dnlib.DotNet.Writer.ModuleWriterOptions(module)
            {
                Logger = DummyLogger.NoThrowInstance
            };

            string tempPath = dllPath + ".tmp";
            Console.WriteLine($"[💾 TEMP] Creating: {tempPath}");
            
            module.Write(tempPath, options);
            long tempSize = new FileInfo(tempPath).Length;
            Console.WriteLine($"[💾 TEMP] ✓ Written: {tempSize:N0} bytes ({(tempSize < rawBytes.Length ? "SMALLER ✓" : "SAME SIZE")})");
            
            module.Dispose();
            Console.WriteLine("[MODULE] ✓ Disposed original module");

            Console.WriteLine($"[🗑️  DELETE] Removing original: {dllPath}");
            File.Delete(dllPath);
            Console.WriteLine($"[🗑️  DELETE] ✓ Original DELETED");

            Console.WriteLine($"[🔄 MOVE] Moving temp -> original: {tempPath} → {dllPath}");
            File.Move(tempPath, dllPath);
            long finalSize = new FileInfo(dllPath).Length;
            Console.WriteLine($"[🔄 MOVE] ✓ Final file: {finalSize:N0} bytes");
            
            GlobalDuplicateCounter += totalRemoved;
            Console.WriteLine($"[GLOBAL] Running total duplicates removed: {GlobalDuplicateCounter}");
        }
        else
        {
            Console.WriteLine("[SKIP] No changes needed");
            module.Dispose();
        }

        Console.WriteLine($"[✅ COMPLETE] {fileName}: {totalRemoved} duplicates removed");
        return totalRemoved;
    }

    static int RemoveDuplicateTopLevelTypes(ModuleDefMD module)
    {
        var seen = new HashSet<string>();
        var toRemove = new List<TypeDef>();
        List<string> duplicateNames = new List<string>();

        Console.WriteLine($"[🔍 TOP-LEVEL] HashSet capacity: {module.Types.Count}");
        
        int index = 0;
        foreach (var type in module.Types.ToList())
        {
            index++;
            string fullName = type.FullName;
            string shortName = type.Name.String;
            
            Console.WriteLine($"[{index,3:D3}] Checking: {shortName} ({fullName})");
            
            if (seen.Contains(fullName))
            {
                duplicateNames.Add(fullName);
                toRemove.Add(type);
                Console.WriteLine($"  →❌ DUPLICATE FOUND → WIRD GELÖSCHT!");
            }
            else
            {
                seen.Add(fullName);
                Console.WriteLine($"  →✅ Unique → kept");
            }
        }

        // === TOP-LEVEL DELETE EXECUTION ===
        Console.WriteLine($"\n[🗑️  TOP-LEVEL DELETE] {toRemove.Count} types zum Löschen:");
        for (int i = 0; i < toRemove.Count; i++)
        {
            var dup = toRemove[i];
            Console.WriteLine($"  {i+1,2:D2}. Lösche: {dup.FullName} (Scope: {dup.Scope})");
            module.Types.Remove(dup);
            Console.WriteLine($"     ✓ GELÖSCHT aus module.Types[{module.Types.Count}]");
        }

        return toRemove.Count;
    }

    static int RemoveDuplicateNestedTypes(TypeDef parentType)
    {
        if (parentType.NestedTypes.Count < 2)
            return 0;

        var seen = new HashSet<string>();
        var toRemove = new List<TypeDef>();
        List<string> duplicateNames = new List<string>();

        string parentName = parentType.FullName;
        Console.WriteLine($"\n[🔍 NESTED in {parentName}] {parentType.NestedTypes.Count} nested types:");

        int index = 0;
        foreach (var nested in parentType.NestedTypes.ToList())
        {
            index++;
            string name = nested.Name.String;
            string fullName = nested.FullName;
            
            Console.WriteLine($"  [{index,2:D2}] {parentName}.{name}");
            
            if (seen.Contains(name))
            {
                duplicateNames.Add(name);
                toRemove.Add(nested);
                Console.WriteLine($"    →❌ DUPLICATE → WIRD GELÖSCHT!");
            }
            else
            {
                seen.Add(name);
                Console.WriteLine($"    →✅ Unique → kept");
            }
        }

        // === NESTED DELETE EXECUTION ===
        if (toRemove.Count > 0)
        {
            Console.WriteLine($"[🗑️  NESTED DELETE] {parentName}: {toRemove.Count} nested types löschen:");
            for (int i = 0; i < toRemove.Count; i++)
            {
                var dup = toRemove[i];
                Console.WriteLine($"  {i+1,2:D2}. Lösche Nested: {dup.FullName}");
                parentType.NestedTypes.Remove(dup);
                Console.WriteLine($"     ✓ GELÖSCHT aus {parentType.Name}.NestedTypes[{parentType.NestedTypes.Count}]");
            }
        }

        return toRemove.Count;
    }

    static void PrintSummary(int fixedFiles, int totalDuplicatesRemoved)
    {
        TimeSpan duration = DateTime.Now - StartTime;
        Console.WriteLine();
        Console.WriteLine("═".PadRight(80, '═'));
        Console.WriteLine("                    🏁   FINAL SUMMARY   🏁");
        Console.WriteLine("═".PadRight(80, '═'));
        Console.WriteLine($"📁 Fixed files:           {fixedFiles,3}");
        Console.WriteLine($"🗑️  Total duplicates:     {totalDuplicatesRemoved,5}");
        Console.WriteLine($"⏱️  Processing time:      {duration.TotalSeconds,6:F1}s");
        Console.WriteLine($"📅 Started:               {StartTime:HH:mm:ss}");
        Console.WriteLine($"📅 Completed:             {DateTime.Now:HH:mm:ss}");
        Console.WriteLine("═".PadRight(80, '═'));
    }

    static void WaitForKey()
    {
        if (!Console.IsInputRedirected)
        {
            Console.WriteLine("\n🎉 Drücke eine beliebige Taste zum Beenden...");
            Console.ReadKey(true);
        }
    }
}
