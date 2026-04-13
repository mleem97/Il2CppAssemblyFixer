# Il2CppAssemblyFixer

Fixes **duplicate type definitions** in Il2CppInterop-generated assemblies that prevent MelonLoader mods from loading.

## The Problem

After certain Unity version updates (e.g. Unity 6000.3.x → 6000.4.x), MelonLoader's Il2CppInterop assembly generator can produce DLLs containing duplicate type definitions (typically the compiler-generated `<>O` delegate cache type). The .NET runtime refuses to load these malformed assemblies, causing **all mods** to fail with:

```
System.BadImageFormatException: Duplicate type with name '<>O' in assembly 'UnityEngine.CoreModule'
```

## How It Works

The tool scans all `.dll` files in the `Il2CppAssemblies` folder, detects duplicate type definitions (both top-level and nested), removes the duplicates, and writes the fixed assemblies back.

## Usage

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

## Building from Source

Requires [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0) or later.

```bash
cd Il2CppAssemblyFixer
dotnet build -c Release
```

The built executable will be at `bin/Release/net6.0/Il2CppAssemblyFixer.exe`.

To publish a self-contained single-file executable:

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```