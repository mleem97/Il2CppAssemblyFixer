# Build script for Il2CppAssemblyFixer
# Builds the EXE, the MelonLoader plugin, and publishes a Linux self-contained binary.

param(
    [string]$Configuration = 'Release'
)

Write-Host "Building EXE (Windows x64, single-file)"
dotnet publish Il2CppAssemblyFixer.csproj -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/win-x64

Write-Host "Building MelonLoader plugin (DLL)"
dotnet build MelonPlugin\Il2CppAssemblyFixerPlugin.csproj -c $Configuration -o ./publish/plugin

Write-Host "Publishing EXE for Linux (linux-x64, single-file)"
dotnet publish Il2CppAssemblyFixer.csproj -c $Configuration -r linux-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/linux-x64

Write-Host "Done. Artifacts are in ./publish (win-x64, linux-x64, plugin)"
