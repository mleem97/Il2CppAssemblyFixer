# Build script for Il2CppAssemblyFixer
# Builds the EXE, the MelonLoader plugin, and publishes a Linux self-contained binary.

param(
    [string]$Configuration = 'Release'
)

Write-Information "Building EXE (Windows x64, single-file)" -InformationAction Continue
dotnet publish Il2CppAssemblyFixer.csproj -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/win-x64

Write-Information "Building MelonLoader plugin (DLL)" -InformationAction Continue
dotnet build MelonPlugin\Il2CppAssemblyFixerPlugin.csproj -c $Configuration -o ./publish/plugin

Write-Information "Publishing EXE for Linux (linux-x64, single-file)" -InformationAction Continue
dotnet publish Il2CppAssemblyFixer.csproj -c $Configuration -r linux-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/linux-x64

Write-Information "Done. Artifacts are in ./publish (win-x64, linux-x64, plugin)" -InformationAction Continue
