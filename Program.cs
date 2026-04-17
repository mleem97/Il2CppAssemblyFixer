using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Win32;

// Explicit aliases to avoid ambiguity between dnlib and Mono.Cecil
using DN = dnlib.DotNet;
using Cecil = Mono.Cecil;

namespace Il2CppAssemblyFixer;

class Program
{
    const string GameFolder = "Data Center";

    // ── Counters for final summary ─────────────────────────────────────────
    static int _assembliesProcessed = 0;
    static int _assembliesModified  = 0;
    static int _typesRemoved        = 0;
    static int _rewritesPerformed   = 0;
    static int _errors              = 0;

    // ── Assembly filter ────────────────────────────────────────────────────
    static bool _processAll = false;

    static readonly HashSet<string> SkipAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        // Unity Core — never touch
        "UnityEngine.CoreModule.dll",
        "UnityEngine.UIElementsModule.dll",
        "UnityEngine.IMGUIModule.dll",
        "UnityEngine.TextCoreModule.dll",
        "UnityEngine.InputSystem.dll",
        "UnityEngine.AssetBundleModule.dll",
        "UnityEngine.SceneManagement.dll",
        // Interop & Loader
        "Il2CppInterop.Runtime.dll",
        "Il2Cppmscorlib.dll",
        "netstandard.dll",
        "mscorlib.dll",
        // UnityExplorer & UniverseLib
        "UnityExplorer.ML.IL2CPP.CoreCLR.dll",
        "UniverseLib.ML.IL2CPP.Interop.dll",
    };

    static bool ShouldProcessAssembly(string path)
    {
        string name = Path.GetFileName(path);
        if (SkipAssemblies.Contains(name)) return false;
        // Primary: game code
        if (name.Equals("Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase))   return true;
        if (name.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase))   return true;
        // Secondary: all other non-Unity/Interop DLLs when --all is specified
        return _processAll;
    }

    // ── Structured log helpers ─────────────────────────────────────────────
    static void Info   (string msg) => Console.WriteLine($"[INFO]    {msg}");
    static void Debug  (string msg) => Console.WriteLine($"[DEBUG]   {msg}");
    static void Warn   (string msg) => Console.WriteLine($"[WARN]    {msg}");
    static void Success(string msg) => Console.WriteLine($"[SUCCESS] {msg}");
    static void Error  (string msg) => Console.Error  .WriteLine($"[ERROR]   {msg}");

    // ── Entry point ────────────────────────────────────────────────────────
    static int Main(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║        Il2Cpp Assembly Fixer  –  .NET 10  (dnlib + Cecil)║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        Info("Startup complete.");
        Debug($"Arguments received: [{string.Join(", ", args)}]");

        // ── Step 1: MelonLoader AGF Regeneration ──────────────────────────
        RunMelonLoaderRegen();

        // ── Resolve target directory ──────────────────────────────────────
        bool   forceRewrite = args.Any(a => a.Equals("--rewrite", StringComparison.OrdinalIgnoreCase));
        string targetDir    = args.Where(a => !a.StartsWith("--")).FirstOrDefault()
                              ?? AutoDetectPath();

        if (forceRewrite) Info("Flag --rewrite detected: all assemblies will be rewritten via Mono.Cecil.");

        _processAll = args.Any(a => a.Equals("--all", StringComparison.OrdinalIgnoreCase));
        if (_processAll) Info("Flag --all: all non-skipped assemblies will be processed.");

        if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
        {
            Error("Target directory not found or not specified.");
            Error($"Resolved path: '{targetDir ?? "<null>"}'");
            PrintSummary();
            return 1;
        }

        Info($"Target directory resolved: {targetDir}");

        bool restoreBackups = args.Any(a => a.Equals("--restore", StringComparison.OrdinalIgnoreCase));
        if (restoreBackups)
        {
            RestoreAllBackups(targetDir);
            return 0;
        }

        // ── Step 3: Assembly discovery ────────────────────────────────────
        string[] dllFiles = DiscoverAssemblies(targetDir);
        if (dllFiles.Length == 0)
        {
            Warn("No .dll files found in the target directory. Nothing to do.");
            PrintSummary();
            return 0;
        }

        // ── Process each assembly ─────────────────────────────────────────
        foreach (string dllPath in dllFiles)
        {
            _assembliesProcessed++;
            try
            {
                ProcessAssembly(dllPath, forceRewrite);
            }
            catch (Exception ex)
            {
                _errors++;
                Error($"Unhandled exception while processing '{Path.GetFileName(dllPath)}':");
                Error($"  {ex.GetType().FullName}: {ex.Message}");
                Error($"  Stack trace:\n{ex.StackTrace}");
            }
        }

        PrintSummary();

        bool deployShim = args.Any(a => a.Equals("--deploy-shim", StringComparison.OrdinalIgnoreCase));
        if (deployShim)
        {
            try   { DeployRuntimeShim(targetDir); }
            catch (Exception ex)
            {
                _errors++;
                Error($"Failed to deploy runtime shim: {ex.Message}");
            }
        }

        return _errors > 0 ? 2 : 0;
    }

    // ── Step 1: Trigger MelonLoader AGF regeneration ───────────────────────
    static void RunMelonLoaderRegen()
    {
        Info("Step 1 – Checking for MelonLoader.Installer.exe …");

        string installer = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MelonLoader.Installer.exe");
        Debug($"Installer path: {installer}");

        if (!File.Exists(installer))
        {
            Warn("MelonLoader.Installer.exe not found – skipping AGF regeneration.");
            return;
        }

        Info("MelonLoader.Installer.exe found. Launching with --melonloader.agfregenerate …");
        try
        {
            var psi = new ProcessStartInfo(installer, "--melonloader.agfregenerate")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start returned null.");

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError .ReadToEnd();
            proc.WaitForExit();

            Debug($"Installer stdout:\n{(string.IsNullOrWhiteSpace(stdout) ? "<empty>" : stdout.TrimEnd())}");
            if (!string.IsNullOrWhiteSpace(stderr))
                Warn($"Installer stderr:\n{stderr.TrimEnd()}");

            if (proc.ExitCode == 0)
                Success($"MelonLoader regeneration completed (exit code 0).");
            else
                Warn($"MelonLoader installer exited with code {proc.ExitCode}.");
        }
        catch (Exception ex)
        {
            _errors++;
            Error($"Failed to run MelonLoader.Installer.exe: {ex.Message}");
            Error($"Stack trace:\n{ex.StackTrace}");
        }
    }

    // ── Step 2: Auto-detect game path via Windows Registry ────────────────
    [SupportedOSPlatform("windows")]
    static string ReadSteamInstallPath()
    {
        var keys = new[]
        {
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
        };

        foreach (string key in keys)
        {
            Debug($"Querying registry: {key}\\InstallPath");
            string value = Registry.GetValue(key, "InstallPath", null) as string;
            if (!string.IsNullOrEmpty(value))
            {
                Success($"Steam InstallPath found via registry key: {key}");
                Debug($"Steam path: {value}");
                return value;
            }
            Warn($"Registry key not found or empty: {key}");
        }
        return null;
    }

    static string AutoDetectPath()
    {
        Info("Step 2 – Auto-detecting game path via Windows Registry …");

        if (!OperatingSystem.IsWindows())
        {
            Warn("Not running on Windows – registry auto-detection skipped.");
            return null;
        }

        try
        {
            string steamPath = ReadSteamInstallPath();
            if (steamPath == null)
            {
                Warn("Steam install path could not be determined from the registry.");
                return null;
            }

            string candidate = Path.Combine(steamPath, "steamapps", "common", GameFolder,
                                            "MelonLoader", "Il2CppAssemblies");
            Debug($"Candidate Il2CppAssemblies path: {candidate}");

            if (Directory.Exists(candidate))
            {
                Success($"Il2CppAssemblies directory found: {candidate}");
                return candidate;
            }

            Warn($"Candidate path does not exist: {candidate}");
        }
        catch (Exception ex)
        {
            _errors++;
            Error($"Exception during registry auto-detection: {ex.Message}");
            Error($"Stack trace:\n{ex.StackTrace}");
        }
        return null;
    }

    // ── Step 3: Discover assemblies ────────────────────────────────────────
    static string[] DiscoverAssemblies(string directory)
    {
        Info($"Step 3 – Scanning for .dll files in: {directory}");
        string[] files = Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(ShouldProcessAssembly)
            .OrderBy(f => f)
            .ToArray();
        Info($"Found {files.Length} .dll file(s) to process.");
        foreach (string f in files)
            Debug($"  Discovered: {f}");
        return files;
    }

    // ── Steps 4 & 5: Process a single assembly ─────────────────────────────
    static void ProcessAssembly(string path, bool forceRewrite)
    {
        string fileName = Path.GetFileName(path);
        Info($"─── Processing: {fileName} ───");

        byte[] data     = File.ReadAllBytes(path);
        bool   modified = false;

        // ── Phase 4: dnlib – duplicate type removal ───────────────────────
        Debug($"[dnlib] Loading assembly: {fileName}");
        using (var module = DN.ModuleDefMD.Load(data))
        {
            var seen     = new HashSet<string>();
            var toRemove = new List<DN.TypeDef>();

            // Recursively collect all types (module.GetTypes() is already recursive)
            int scanned = 0;
            foreach (DN.TypeDef type in module.GetTypes())
            {
                scanned++;
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
                    Debug($"[dnlib] Duplicate detected: '{fullName}'");
                    toRemove.Add(type);
                }
            }

            Debug($"[dnlib] Types scanned: {scanned}  |  Duplicates queued for removal: {toRemove.Count}");

            if (toRemove.Count > 0)
            {
                foreach (DN.TypeDef t in toRemove)
                {
                    string removedName = t.FullName;
                    if (t.IsNested)
                        t.DeclaringType.NestedTypes.Remove(t);
                    else
                        module.Types.Remove(t);

                    _typesRemoved++;
                    Success($"[dnlib] Removed duplicate type: '{removedName}'");
                }

                Debug($"[dnlib] Writing modified module to memory …");
                using var ms = new MemoryStream();
                module.Write(ms);
                data     = ms.ToArray();
                modified = true;
                Success($"[dnlib] {toRemove.Count} duplicate(s) removed from '{fileName}'.");
            }
            else
            {
                Info($"[dnlib] No duplicate types found in '{fileName}'.");
            }
        }

        // ── Phase 5: Mono.Cecil – metadata normalization & rewrite ─────────
        if (forceRewrite || modified)
        {
            string reason = forceRewrite && modified ? "--rewrite flag + dnlib changes"
                          : forceRewrite             ? "--rewrite flag"
                          :                            "dnlib modifications";

            Info($"[Cecil] Rewriting '{fileName}' (reason: {reason}) …");
            try
            {
                using var msIn  = new MemoryStream(data);
                var readerParams = new Cecil.ReaderParameters { ReadingMode = Cecil.ReadingMode.Immediate };
                using var asm   = Cecil.AssemblyDefinition.ReadAssembly(msIn, readerParams);
                using var msOut = new MemoryStream();
                asm.Write(msOut);
                data = msOut.ToArray();
                _rewritesPerformed++;
                Success($"[Cecil] Metadata normalization complete for '{fileName}'.");
            }
            catch (Exception ex)
            {
                _errors++;
                Error($"[Cecil] Rewrite failed for '{fileName}': {ex.Message}");
                Error($"Stack trace:\n{ex.StackTrace}");
                return; // Do not overwrite the file with potentially broken data
            }

            Debug($"Writing {data.Length:N0} bytes back to: {path}");
            BackupIfNeeded(path);
            File.WriteAllBytes(path, data);
            _assembliesModified++;
            Success($"Saved: {fileName}");
        }
        else
        {
            Info($"No changes required for '{fileName}' – skipped.");
        }
    }

    // ── Backup helpers ─────────────────────────────────────────────────────
    static void BackupIfNeeded(string path)
    {
        string backup = path + ".bak";
        if (!File.Exists(backup))
        {
            File.Copy(path, backup);
            Info($"Backup created: {Path.GetFileName(backup)}");
        }
        else
        {
            Debug($"Backup already exists, skipping: {Path.GetFileName(backup)}");
        }
    }

    static void RestoreAllBackups(string dir)
    {
        Info("Restoring all .bak files...");
        foreach (string bak in Directory.GetFiles(dir, "*.dll.bak"))
        {
            string original = bak[..^4]; // removes ".bak"
            File.Copy(bak, original, overwrite: true);
            File.Delete(bak);
            Success($"Restored: {Path.GetFileName(original)}");
        }
    }

    // ── Shim deployment ────────────────────────────────────────────────────
    static void DeployRuntimeShim(string il2CppAssembliesDir)
    {
        // Two directories up → game root → Mods
        string gameRoot = Path.GetFullPath(Path.Combine(il2CppAssembliesDir, "..", ".."));
        string modsDir  = Path.Combine(gameRoot, "Mods");
        Directory.CreateDirectory(modsDir);

        string shimName = "UnityExplorerUnity6Shim.dll";
        string source   = Path.Combine(AppContext.BaseDirectory, shimName);
        string dest     = Path.Combine(modsDir, shimName);

        if (!File.Exists(source))
            throw new FileNotFoundException(
                $"Shim DLL not found. Build Project 2 first and place '{shimName}' next to this tool.",
                source);

        File.Copy(source, dest, overwrite: true);
        Success($"Runtime Shim deployed → {dest}");
    }

    // ── Final summary ──────────────────────────────────────────────────────
    static void PrintSummary()
    {
        // Each data line is:  "║  <label padded to 26> : <value padded to 30> ║"
        // Box inner width = 58  →  border chars '║' + 58 chars + '║'
        const int LabelW = 28;
        const int ValueW = 28;
        string Row(string label, int value) =>
            $"║  {label.PadRight(LabelW)}: {value.ToString().PadRight(ValueW)}║";

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                      FINAL SUMMARY                      ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════╣");
        Console.WriteLine(Row("Assemblies processed",    _assembliesProcessed));
        Console.WriteLine(Row("Assemblies modified",     _assembliesModified));
        Console.WriteLine(Row("Duplicate types removed", _typesRemoved));
        Console.WriteLine(Row("Cecil rewrites performed",_rewritesPerformed));
        Console.WriteLine(Row("Errors encountered",      _errors));
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        if (_errors == 0)
            Success("All operations completed without errors.");
        else
            Warn($"{_errors} error(s) occurred. Review [ERROR] lines above for details.");
    }
}
