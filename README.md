# Il2CppAssemblyFixer

Fixes **duplicate type definitions** in Il2CppInterop-generated assemblies that prevent MelonLoader mods from loading.

## The Problem

After certain Unity version updates (e.g. Unity 6000.3.x → 6000.4.x), MelonLoader's Il2CppInterop assembly generator can produce DLLs containing duplicate type definitions (typically the compiler-generated `<>O` delegate cache type). The .NET runtime refuses to load these malformed assemblies, causing **all mods** to fail with:

```
System.BadImageFormatException: Duplicate type with name '<>O' in assembly 'UnityEngine.CoreModule'
```

## How It Works

The tool scans all `.dll` files in the `Il2CppAssemblies` folder, detects duplicate type definitions (both top-level and nested), removes the duplicates, and writes the fixed assemblies back.

---

## Option A – MelonLoader Plugin (recommended)

`Il2CppAssemblyFixerPlugin.dll` is a **MelonPlugin** – it runs *before* any MelonMod is loaded, which means the fix is applied automatically every time you start the game, even after MelonLoader regenerates the Il2Cpp assemblies in the background.

### Installation

1. Download `Il2CppAssemblyFixerPlugin_<version>_MelonLoader.zip` from the [Releases](../../releases) page.
2. Extract **all three DLLs** from the ZIP into your game's `Plugins/` folder:
   ```
   <GameFolder>/
   └── Plugins/
       ├── Il2CppAssemblyFixerPlugin.dll   ← the plugin
       ├── dnlib.dll                        ← required dependency
       └── Mono.Cecil.dll                   ← required dependency
   ```
3. Launch the game normally – the plugin fixes any duplicates automatically on every start.

> **Why a Plugin and not a Mod?**  
> MelonLoader loads **Plugins** (`/Plugins/`) before it processes the Il2Cpp assemblies.  
> MelonLoader loads **Mods** (`/Mods/`) *after*, so by that point the `BadImageFormatException` has already occurred. The Plugin lifecycle hook `OnPreInitialization` fires early enough to repair the files before they are loaded.

---

## Option B – Standalone EXE (manual / fallback)

If you prefer to run the fix manually or without MelonLoader, use the standalone executable.

### Auto-detect (recommended)

Simply run the tool without arguments. It will automatically find your Steam installation and the Data Center game folder:

```
Il2CppAssemblyFixer.exe
```

### Manual path

If auto-detection fails, pass the path to your `Il2CppAssemblies` folder:

```
Il2CppAssemblyFixer.exe "D:\SteamLibrary\steamapps\common\Data Center\MelonLoader\Il2CppAssemblies"
```

### When to run

Run this tool **after every game update** that triggers MelonLoader's assembly regeneration:

1. Launch the game once and wait for MelonLoader to finish generating assemblies
2. Close the game
3. Run `Il2CppAssemblyFixer.exe`
4. Launch the game again — mods should now load correctly

### Available EXE downloads

| File | Platform | Notes |
|------|----------|-------|
| `Il2CppAssemblyFixer_<ver>_win-x64.zip` | Windows 64-bit | self-contained, no .NET required |
| `Il2CppAssemblyFixer_<ver>_win-x86.zip` | Windows 32-bit | self-contained, no .NET required |
| `Il2CppAssemblyFixer_<ver>_linux-x64.zip` | Linux 64-bit | requires .NET 10 runtime |

---

## Building from Source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0).

```bash
cd Il2CppAssemblyFixer
dotnet build -c Release
```

### Publish EXE (self-contained)

```bash
dotnet publish Il2CppAssemblyFixer.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

### Publish MelonLoader Plugin DLL

```bash
dotnet publish MelonPlugin/Il2CppAssemblyFixerPlugin.csproj -c Release --self-contained false
```
