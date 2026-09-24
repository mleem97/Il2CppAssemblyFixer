# Quickstart — Il2CppAssemblyFixer

> Repairs corrupted IL2CPP assembly metadata in **Unity 6** games running **MelonLoader v0.7.2+**.

Repo: [https://github.com/mleem97/Il2CppAssemblyFixer](https://github.com/mleem97/Il2CppAssemblyFixer) · Version: `0.1.0` · Lizenz: Apache-2.0.

## 1. Klonen

```bash
git clone git@github.com:mleem97/Il2CppAssemblyFixer.git
cd Il2CppAssemblyFixer
```

## 2. Bauen / Starten

Je nach Tech-Stack **einen** Weg wählen:

```bash
# .NET
dotnet build -c Release
dotnet run --project src/

# Node / pnpm
pnpm install
pnpm build
pnpm start

# Python
python -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt
python -m <modul>
```

## 3. Testen

```bash
dotnet test            # .NET
pnpm test              # Node
pytest                 # Python
```

Details stehen in [README.md](README.md) und [docs/INDEX.md](docs/INDEX.md).
Bei Problemen: Issue anlegen ([Issues](https://github.com/mleem97/Il2CppAssemblyFixer/issues)) oder [CONTRIBUTING.md](CONTRIBUTING.md) lesen.
