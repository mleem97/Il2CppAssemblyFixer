using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using Microsoft.Win32;

namespace Il2CppAssemblyFixer
{
    class Program
    {
        // Name of the game folder from your logs
        const string GameFolderName = "Data Center";

        static void Main(string[] args)
        {
            Console.WriteLine("=== Il2Cpp Duplicate Type Fixer ===");
            
            // 1. Determine Path
            string targetDir = args.Length > 0 ? args[0] : AutoDetectPath();

            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            {
                Console.WriteLine("Error: Could not find Il2CppAssemblies folder.");
                Console.WriteLine("Please drag and drop the 'Il2CppAssemblies' folder onto this EXE.");
                Console.ReadLine();
                return;
            }

            Console.WriteLine($"Target: {targetDir}");
            var files = Directory.GetFiles(targetDir, "*.dll");

            foreach (var file in files)
            {
                FixAssembly(file);
            }

            Console.WriteLine("\nDone! You can now start the game.");
            System.Threading.Thread.Sleep(3000);
        }

        static void FixAssembly(string path)
        {
            try
            {
                // Load the module
                byte[] data = File.ReadAllBytes(path);
                var module = ModuleDefMD.Load(data);
                bool modified = false;

                // Dictionary to track full names of types we've seen
                var seenTypes = new HashSet<string>();
                var toRemove = new List<TypeDef>();

                // We iterate through all types in the assembly
                foreach (var type in module.GetTypes())
                {
                    // The log specifically complains about types named "<>O"
                    // but we check for any duplicates to be safe
                    if (!seenTypes.Add(type.FullName))
                    {
                        if (type.Name.Contains("<>O"))
                        {
                            toRemove.Add(type);
                        }
                    }
                }

                if (toRemove.Count > 0)
                {
                    Console.WriteLine($"Processing {Path.GetFileName(path)}: Removing {toRemove.Count} duplicate types...");
                    
                    foreach (var type in toRemove)
                    {
                        if (type.IsNested)
                        {
                            type.DeclaringType.NestedTypes.Remove(type);
                        }
                        else
                        {
                            module.Types.Remove(type);
                        }
                    }
                    modified = true;
                }

                if (modified)
                {
                    string tempFile = path + ".tmp";
                    module.Write(tempFile);
                    module.Dispose();
                    File.Delete(path);
                    File.Move(tempFile, path);
                }
                else
                {
                    module.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fix {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        static string AutoDetectPath()
        {
            // Tries to find the path based on your log directory
            string steamPath = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null);
            if (steamPath != null)
            {
                string path = Path.Combine(steamPath, "steamapps", "common", GameFolderName, "MelonLoader", "Il2CppAssemblies");
                if (Directory.Exists(path)) return path;
            }
            return "";
        }
    }
}
