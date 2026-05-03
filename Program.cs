using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

// Explicit aliases to avoid ambiguity between dnlib and Mono.Cecil
using DN = dnlib.DotNet;
using DNEmit = dnlib.DotNet.Emit;
using Cecil = Mono.Cecil;

namespace Il2CppAssemblyFixer;

class Program
{
    const string GameFolder = "Data Center";

    // If this file sits next to the EXE it overrides auto-detection entirely.
    const string ConfigFileName = "game-path.txt";

    // Top-level folder names under which Steam libraries are commonly found on any drive.
    static readonly string[] SteamRootCandidates =
    {
        "Steam",
        "SteamLibrary",
        "Steam Library",
        "SteamGames",
        "Games",
        @"Games\Steam",
        @"Games\SteamLibrary",
        @"Program Files\Steam",
        @"Program Files (x86)\Steam",
        @"Program Files\SteamLibrary",
        @"Program Files (x86)\SteamLibrary",
    };

    // Parent folder names for non-Steam / custom installs (no steamapps\common prefix).
    // Checked on every drive letter AND inside the user-profile special dirs below.
    static readonly string[] NonSteamParentCandidates =
    {
        "",                           // game folder directly at drive root: D:\Data Center
        "Games",
        "MyGames",
        "My Games",
        "PC Games",
        "PCGames",
        "GameFiles",
        "Spiele",
        "Spielebibliothek",
        @"Program Files",
        @"Program Files (x86)",
        "Apps",
        "Applications",
        "Software",
    };

    // User-profile base directories to search for non-Steam installs (populated at runtime).
    static IEnumerable<string> UserProfileSearchRoots()
    {
        string? profile = Environment.GetEnvironmentVariable("USERPROFILE")
                       ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            yield return profile;
            yield return Path.Combine(profile, "Desktop");
            yield return Path.Combine(profile, "Downloads");
            yield return Path.Combine(profile, "Documents");
            yield return Path.Combine(profile, "Documents", "Games");
            yield return Path.Combine(profile, "Documents", "My Games");
        }

        string? localApp = Environment.GetEnvironmentVariable("LOCALAPPDATA")
                        ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localApp))
            yield return localApp;
    }

    // Linux Steam roots (tried before the generic drive scan on non-Windows).
    static IEnumerable<string> LinuxSteamRoots()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, "snap", "steam", "common", ".steam", "root");
        yield return "/usr/share/steam";
        yield return "/opt/steam";
    }

    // ── Counters for final summary ─────────────────────────────────────────
    static int _assembliesProcessed = 0;
    static int _assembliesModified  = 0;
    static int _typesRemoved        = 0;
    static int _rewritesPerformed   = 0;
    static int _errors              = 0;

    // ── Assembly filter ────────────────────────────────────────────────────
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

    sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public bool Equals(T x, T y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }

    static bool ShouldProcessAssembly(string path)
    {
        return !SkipAssemblies.Contains(Path.GetFileName(path));
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

    // ── Step 2: Auto-detect game path ─────────────────────────────────────

    [SupportedOSPlatform("windows")]
    static string ReadSteamInstallPath()
    {
        var keys = new[]
        {
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
            @"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam",
        };

        foreach (string key in keys)
        {
            Debug($"Querying registry: {key}\\InstallPath");
            string? value = Registry.GetValue(key, "InstallPath", null) as string;
            if (!string.IsNullOrEmpty(value))
            {
                Success($"Steam InstallPath found: {key}");
                return value;
            }
        }
        return null;
    }

    // Parses Steam's libraryfolders.vdf and yields every configured library path.
    static IEnumerable<string> ParseLibraryFoldersVdf(string steamRoot)
    {
        string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
        {
            Debug($"libraryfolders.vdf not found: {vdf}");
            yield break;
        }

        Debug($"Parsing: {vdf}");
        var rx = new Regex(@"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase);
        foreach (string line in File.ReadLines(vdf))
        {
            Match m = rx.Match(line);
            if (m.Success)
            {
                // VDF uses double-backslash; unescape to a real path
                string lib = m.Groups[1].Value.Replace(@"\\", @"\");
                Debug($"  VDF library: {lib}");
                yield return lib;
            }
        }
    }

    // Checks <steamLibrary>\steamapps\common\<game>\MelonLoader\Il2CppAssemblies
    static string? TryLibrary(string libraryRoot)
    {
        string candidate = Path.Combine(libraryRoot, "steamapps", "common",
                                        GameFolder, "MelonLoader", "Il2CppAssemblies");
        return Directory.Exists(candidate) ? candidate : null;
    }

    // Checks <parentDir>\<game>\MelonLoader\Il2CppAssemblies  (non-Steam layout)
    static string? TryGameFolder(string parentDir)
    {
        if (string.IsNullOrEmpty(parentDir)) return null;
        string candidate = Path.Combine(parentDir, GameFolder, "MelonLoader", "Il2CppAssemblies");
        return Directory.Exists(candidate) ? candidate : null;
    }

    static string? AutoDetectPath()
    {
        Info("Step 2 – Auto-detecting game installation path …");

        // ── Step 0: config-file override (game-path.txt next to the EXE) ──────
        string cfgPath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        if (File.Exists(cfgPath))
        {
            string custom = File.ReadAllText(cfgPath).Trim().Trim('"');
            Info($"Config override found ({ConfigFileName}): {custom}");
            if (Directory.Exists(custom))
            {
                // Accept both the Il2CppAssemblies dir and the game root
                if (custom.EndsWith("Il2CppAssemblies", StringComparison.OrdinalIgnoreCase))
                    return custom;
                string sub = Path.Combine(custom, "MelonLoader", "Il2CppAssemblies");
                if (Directory.Exists(sub)) return sub;
            }
            Warn($"Path in {ConfigFileName} does not exist or is invalid: {custom}");
        }

        // ── Step 1 (Windows): Registry + libraryfolders.vdf ──────────────────
        if (OperatingSystem.IsWindows())
        {
            try
            {
                string? steamRoot = ReadSteamInstallPath();
                if (steamRoot != null)
                {
                    string? found = TryLibrary(steamRoot);
                    if (found != null) { Success($"Found (registry): {found}"); return found; }

                    foreach (string lib in ParseLibraryFoldersVdf(steamRoot))
                    {
                        found = TryLibrary(lib);
                        if (found != null) { Success($"Found (VDF library): {found}"); return found; }
                    }
                    Warn("Not found in any Steam library from libraryfolders.vdf.");
                }
                else
                {
                    Warn("Steam registry key not found.");
                }
            }
            catch (Exception ex) { Warn($"Registry/VDF error: {ex.Message}"); }
        }

        // ── Step 2 (Linux): known Steam roots + VDF ──────────────────────────
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            foreach (string root in LinuxSteamRoots())
            {
                string? found = TryLibrary(root);
                if (found != null) { Success($"Found (Linux Steam): {found}"); return found; }
                foreach (string lib in ParseLibraryFoldersVdf(root))
                {
                    found = TryLibrary(lib);
                    if (found != null) { Success($"Found (Linux VDF): {found}"); return found; }
                }
            }
        }

        // ── Step 3: all drives × Steam root candidates ────────────────────────
        Info("Scanning drives for Steam-layout paths …");
        foreach (char drive in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            string driveRoot = $@"{drive}:\";
            if (!Directory.Exists(driveRoot)) continue;

            foreach (string folder in SteamRootCandidates)
            {
                string? found = TryLibrary(Path.Combine(driveRoot, folder));
                if (found != null) { Success($"Found (Steam scan): {found}"); return found; }
            }
        }

        // ── Step 4: all drives × non-Steam / custom parent candidates ─────────
        Info("Scanning drives for non-Steam / custom installation paths …");
        foreach (char drive in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            string driveRoot = $@"{drive}:\";
            if (!Directory.Exists(driveRoot)) continue;

            foreach (string folder in NonSteamParentCandidates)
            {
                string parent = string.IsNullOrEmpty(folder)
                    ? driveRoot
                    : Path.Combine(driveRoot, folder);
                string? found = TryGameFolder(parent);
                if (found != null) { Success($"Found (custom scan): {found}"); return found; }
            }
        }

        // ── Step 5: user-profile special directories ──────────────────────────
        Info("Checking user-profile directories …");
        foreach (string root in UserProfileSearchRoots())
        {
            if (!Directory.Exists(root)) continue;

            // Direct child: %USERPROFILE%\Desktop\Data Center
            string? found = TryGameFolder(root);
            if (found != null) { Success($"Found (user profile): {found}"); return found; }

            // One level deeper with non-Steam parent names
            foreach (string folder in NonSteamParentCandidates)
            {
                if (string.IsNullOrEmpty(folder)) continue;
                found = TryGameFolder(Path.Combine(root, folder));
                if (found != null) { Success($"Found (user profile): {found}"); return found; }
            }
        }

        Warn("Game installation not found automatically.");
        Warn($"Tip: create '{ConfigFileName}' next to this EXE and put the game path inside.");
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

    static Dictionary<DN.TypeDef, int> BuildTypeReferenceCounts(DN.ModuleDefMD module)
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
            Dictionary<DN.TypeDef, int> referenceCounts = BuildTypeReferenceCounts(module);
            var toRemove = new List<DN.TypeDef>();

            int scanned = 0;
            foreach (IGrouping<string, DN.TypeDef> group in module.GetTypes().GroupBy(type => type.FullName, StringComparer.Ordinal))
            {
                List<DN.TypeDef> duplicates = group.ToList();
                scanned += duplicates.Count;

                if (duplicates.Count < 2)
                    continue;

                int referencedCopies = duplicates.Count(type => referenceCounts.TryGetValue(type, out int count) && count > 0);
                if (referencedCopies > 1)
                {
                    Warn($"[dnlib] Duplicate group '{group.Key}' has {referencedCopies} referenced copies; skipping unsafe removal.");
                    continue;
                }

                foreach (DN.TypeDef duplicate in duplicates.Where(type => !referenceCounts.TryGetValue(type, out int count) || count == 0))
                {
                    Debug($"[dnlib] Duplicate detected: '{duplicate.FullName}' (reference count: 0)");
                    toRemove.Add(duplicate);
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
