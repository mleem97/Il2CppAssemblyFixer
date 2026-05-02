See `scripts/generate-commit-audit.ps1` for generation details.

This file contains a non-destructive audit of every commit on `master`, including
a suggested Conventional Commit type and a suggested semantic version bump.

If you want adjustments to the heuristics (e.g., stricter matching, include PR numbers,
or different bump mapping), tell me and I will regenerate.
# Commit Audit

Analyzed branch: $Branch on 2026-05-02T16:37:33.5702874+02:00

| Commit | Date | Author | Original message | Suggested type | Suggested bump | Files changed |
|---|---|---|---|---|---|---:|
| $(c9650f05b8ed7e071bc59cdb24a235e4f78a0085.Substring(0,10)) | 2026-04-13 18:38:07 +0200 | leoms1408 | initial commit\t | chore | none | 5 |
| $(52c26dafc741b66b9a8d2cf1e036ee46759d9bcd.Substring(0,10)) | 2026-04-13 19:59:43 +0200 | Marvin | Add GitHub Actions workflow for .NET Core Desktop\t\tThis workflow builds, tests, signs, and packages a WPF or Windows Forms desktop application using .NET Core. | chore | none | 1 |
| $(6ec9051898937a36b11a9322d49a956fc67c8469.Substring(0,10)) | 2026-04-13 20:00:23 +0200 | Marvin | Enhance logging and tracking in Program.cs\t\tRefactor logging and error messages for clarity. Introduce StartTime and GlobalDuplicateCounter for tracking. | chore | none | 1 |
| $(e4c273c9261b1fc61c13b659cfc0b06c9540ed74.Substring(0,10)) | 2026-04-13 20:01:40 +0200 | Marvin | Update dotnet-desktop.yml | chore | none | 1 |
| $(e1ac365d00a6ed76784c267dd938bd6673a99933.Substring(0,10)) | 2026-04-13 20:02:26 +0200 | Marvin | Update Il2CppAssemblyFixer.csproj | chore | none | 1 |
| $(d433ac0a77d872c4fecd99e29381a993d476acbf.Substring(0,10)) | 2026-04-13 20:03:28 +0200 | Marvin | Modify workflow for .NET 6 and update build steps\t\tUpdated the GitHub Actions workflow to use .NET 6 instead of .NET 8, modified build steps for x64 and x86, and adjusted artifact handling. | chore | none | 1 |
| $(853b148c4222d52fd447c3c61f291b28eba64a79.Substring(0,10)) | 2026-04-13 20:06:26 +0200 | Marvin | Update project file for .NET 10 and platform support | chore | none | 1 |
| $(f85d8c6986d4d02c8f7f301e19e21522316c2048.Substring(0,10)) | 2026-04-13 20:06:46 +0200 | Marvin | Update workflow for .NET 10 build and release | chore | none | 1 |
| $(54652f19b196bc8999b1e2ae84bd27a68104f11a.Substring(0,10)) | 2026-04-13 20:09:30 +0200 | Marvin | Update project file for .NET 10 configuration | chore | none | 1 |
| $(41d9dec92bfef477f2962678e39809c619bd173e.Substring(0,10)) | 2026-04-13 20:09:58 +0200 | Marvin | Refactor Main method and improve logging | chore | none | 1 |
| $(d37a08cc9681ea64a3050191c0092962ce71c952.Substring(0,10)) | 2026-04-13 20:23:06 +0200 | Marvin | Enhance assembly processing and improve output messages | chore | none | 1 |
| $(e98a543f705c9144de80803ab8bdbd1c5fbddd82.Substring(0,10)) | 2026-04-13 20:23:47 +0200 | Marvin | Update Il2CppAssemblyFixer.csproj | chore | none | 1 |
| $(0f3ce8a5469c35cb4c4d54675b281e1a35631890.Substring(0,10)) | 2026-04-13 20:24:40 +0200 | Marvin | Update project file properties and package references | chore | none | 1 |
| $(92f3a65a234855c3b5f5cad8dd41e17a30a6dbfc.Substring(0,10)) | 2026-04-13 20:25:30 +0200 | Marvin | Update Program.cs | chore | none | 1 |
| $(d763a6d8436b0f8b8fd00f28bebb6162a19c9178.Substring(0,10)) | 2026-04-13 20:26:53 +0200 | Marvin | Change Microsoft.Win32.Registry package version\t\tUpdated Microsoft.Win32.Registry package version from 8.0.0 to 5.0.0. | chore | none | 1 |
| $(954466c55a72b45b430686e3ca1f6d3c88e96018.Substring(0,10)) | 2026-04-13 20:27:56 +0200 | Marvin | Enhance .NET 10 build workflow with caching and artifact updates\t\tUpdated the .NET 10 build workflow to include caching for NuGet packages, modified publish settings, and improved artifact handling. | chore | none | 1 |
| $(fb6064e723c75a5e1e16fb5c8db1d902c9b317d8.Substring(0,10)) | 2026-04-13 20:30:12 +0200 | Marvin | Update German comments and output messages | chore | none | 1 |
| $(3bbb405a53a995328f26ac4c2b3851013d1253ae.Substring(0,10)) | 2026-04-13 20:30:41 +0200 | Marvin | Update Program.cs | chore | none | 1 |
| $(6bef067056dce1b6a1f226ebc23724046f0e89e0.Substring(0,10)) | 2026-04-13 20:35:51 +0200 | Marvin | Refactor Program class and improve assembly processing | chore | none | 1 |
| $(b524da142618aa11d41ec17012ba146e0f60c345.Substring(0,10)) | 2026-04-13 20:38:08 +0200 | Marvin | Update project file with assembly name and properties\t\tAdded assembly name and updated project properties. | chore | none | 1 |
| $(46a9ee102056bbafdccf7bca0e23bd0ae0db167d.Substring(0,10)) | 2026-04-13 20:38:42 +0200 | Marvin | Refactor Program.cs for improved assembly processing | chore | none | 1 |
| $(118476f2034cc98c0bc5070816a393410c84e3cd.Substring(0,10)) | 2026-04-13 18:51:37 +0000 | copilot-swe-agent[bot] | feat: verbose structured logging, Windows registry guard, fixed summary alignment\t\tAgent-Logs-Url: https://github.com/mleem97/Il2CppAssemblyFixer/sessions/28a9df40-f6b7-46c6-827f-20c4d6e8a177\t\tCo-authored-by: mleem97 <52848568+mleem97@users.noreply.github.com>\t | feat | minor | 1 |
| $(05246d515b045de8747710a2af1c3a247c24ddb5.Substring(0,10)) | 2026-04-13 20:56:20 +0200 | Marvin | Merge pull request #1 from mleem97/copilot/fix-badimageformatexception\t\tfeat: verbose structured logging + Windows registry guard | fix | patch | 0 |
| $(fdc57cef2f5f5d460d232f6802211962d057325e.Substring(0,10)) | 2026-04-13 19:11:57 +0000 | copilot-swe-agent[bot] | fix: release job no longer skipped on master pushes\t\tAgent-Logs-Url: https://github.com/mleem97/Il2CppAssemblyFixer/sessions/d562e9da-ee45-4e40-933b-c68fa94fca52\t\tCo-authored-by: mleem97 <52848568+mleem97@users.noreply.github.com>\t | fix | patch | 1 |
| $(f6fa0bcd868df0637c598cac8def718fd68778db.Substring(0,10)) | 2026-04-13 21:13:18 +0200 | Marvin | Merge pull request #2 from mleem97/copilot/fix-release-not-loaded-issue\t\tfix: release job no longer skipped on master pushes | fix | patch | 0 |
| $(69341bf266631807ee7a57e6cf6063437b29d065.Substring(0,10)) | 2026-04-15 16:37:40 +0000 | copilot-swe-agent[bot] | feat: add MelonLoader Plugin for automatic duplicate-type fix before mods load\t\tAgent-Logs-Url: https://github.com/mleem97/Il2CppAssemblyFixer/sessions/ae9f6ade-e13f-4405-8f20-cf19ca1657c6\t\tCo-authored-by: mleem97 <52848568+mleem97@users.noreply.github.com>\t | feat | minor | 6 |
| $(0047c57221948062578b07cbace4493cd18783ac.Substring(0,10)) | 2026-04-16 11:44:46 +0200 | Marvin | Merge pull request #3 from mleem97/copilot/create-dll-for-melonloader-mod\t\tfeat: add MelonPlugin for automatic duplicate-type fix before mods load | fix | patch | 0 |
| $(ed5c5cc42f4b24f408a68a3cb7f129412dc3a804.Substring(0,10)) | 2026-04-16 16:14:35 +0000 | copilot-swe-agent[bot] | fix: remove all duplicate type definitions, not just <>O-named ones\t\tAgent-Logs-Url: https://github.com/mleem97/Il2CppAssemblyFixer/sessions/63f7366e-1b45-40ce-9272-366698e1048a\t\tCo-authored-by: mleem97 <52848568+mleem97@users.noreply.github.com>\t | fix | patch | 2 |
| $(fb8be5f192ab396f080aee1b8b885851e07934d3.Substring(0,10)) | 2026-04-17 01:27:48 +0200 | Marvin | Merge pull request #4 from mleem97/copilot/fix-melon-loading-issue\t\tFix: remove all duplicate type definitions, not just `<>O`-named ones | fix | patch | 0 |
| $(c1bcc281498cffd40cdbeb4a92ff0491619998bb.Substring(0,10)) | 2026-04-17 21:04:17 +0000 | copilot-swe-agent[bot] | feat: add SkipAssemblies filter, backup/restore, deploy-shim flag; add UnityExplorerUnity6Shim project\t\tAgent-Logs-Url: https://github.com/mleem97/Il2CppAssemblyFixer/sessions/0d69ee2d-bbac-4735-8d7a-3c5604f7fd9f\t\tCo-authored-by: mleem97 <52848568+mleem97@users.noreply.github.com>\t | feat | minor | 5 |
| $(ac197ee0dfdd95b522aa8f9b902985f428a6e906.Substring(0,10)) | 2026-04-17 21:05:35 +0000 | copilot-swe-agent[bot] | docs: rewrite README with full setup guide, new flags, backup system, and shim instructions\t\tAgent-Logs-Url: https://github.com/mleem97/Il2CppAssemblyFixer/sessions/0d69ee2d-bbac-4735-8d7a-3c5604f7fd9f\t\tCo-authored-by: mleem97 <52848568+mleem97@users.noreply.github.com>\t | docs | none | 1 |
| $(c66382575e42edc43718521fba94734c0626bfab.Substring(0,10)) | 2026-04-17 21:09:32 +0000 | copilot-swe-agent[bot] | Add backup system, assembly filter, restore/deploy-shim flags, and UnityExplorerUnity6Shim mod\t\tAgent-Logs-Url: https://github.com/mleem97/Il2CppAssemblyFixer/sessions/0d69ee2d-bbac-4735-8d7a-3c5604f7fd9f\t\tCo-authored-by: mleem97 <52848568+mleem97@users.noreply.github.com>\t | chore | none | 1 |
| $(79c491b78789b72aa152aa2e979a08b50ae6dbb4.Substring(0,10)) | 2026-04-18 00:00:04 +0200 | Marvin | Add backup system, assembly filter, restore/deploy-shim flags, and UnityExplorerUnity6Shim mod (#5) | chore | none | 0 |
| $(fd77e3014156dad192276aea8a059271f6538e79.Substring(0,10)) | 2026-04-25 01:54:56 +0200 | Marvin | Revise README with clearer project description\t\tUpdated project description for clarity and added a badge. | docs | none | 1 |
| $(7d04404650d6cb9aed1ad8be4653c3c17488432d.Substring(0,10)) | 2026-05-01 20:50:37 +0200 | Marvin | Revise README for improved clarity and structure\t\tUpdated README to enhance clarity and organization, added sections on installation, usage, and known issues. | docs | none | 1 |
| $(3dd1b8898b616e6d897ac1823decc83f16d1837f.Substring(0,10)) | 2026-05-02 15:37:23 +0200 | Marvin | chore: remove UnityExplorer shim; fix(dnlib): safer duplicate-removal; feat(build): add build.ps1\t | feat | minor | 6 |
| $(d3f5a857910fa4d46b5f75e546d114f37cfbaf89.Substring(0,10)) | 2026-05-02 15:40:05 +0200 | Marvin | chore: add AGENTS.md, CHANGELOG.md and commit-msg hooks (conventional commits enforcement)\t | chore | none | 4 |
| $(5676c9485e231fa6377781f8fe18f376dcc3724c.Substring(0,10)) | 2026-05-02 16:36:57 +0200 | Marvin | feat: add scripts for automatic version tagging and commit message rewriting\t | feat | minor | 3 |
