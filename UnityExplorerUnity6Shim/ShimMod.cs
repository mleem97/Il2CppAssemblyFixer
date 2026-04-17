using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(UnityExplorerUnity6Shim.ShimMod),
    "UnityExplorerUnity6Shim", "1.0.0", "mleem97")]
[assembly: MelonGame("Waseku", "Data Center")]

namespace UnityExplorerUnity6Shim;

/// <summary>
/// MelonLoader mod that patches UnityExplorer for Unity 6 compatibility:
/// <list type="bullet">
///   <item>Fixes SceneHandle-Bug (Scene.GetNameInternal API break)</item>
///   <item>Works around Method Unstripping failures (SceneManager.GetAllScenes)</item>
///   <item>Provides an F8 hotkey for a safe scene dump</item>
/// </list>
/// </summary>
public sealed class ShimMod : MelonMod
{
    private Harmony _harmony = null!;

    public override void OnInitializeMelon()
    {
        _harmony = new Harmony("mleem97.unityexplorershim");

        PatchSceneHandler();
        MelonLogger.Msg("[Shim] UnityExplorer Unity 6 Shim active. F8 = Scene-Dump.");
    }

    public override void OnDeinitializeMelon()
    {
        _harmony.UnpatchSelf();
    }

    // ── Patch 1: suppress SceneHandler.Init() crash ──────────────────────
    private void PatchSceneHandler()
    {
        try
        {
            var targetType = AccessTools.TypeByName("UnityExplorer.ObjectExplorer.SceneHandler");
            if (targetType == null)
            {
                MelonLogger.Warning("[Shim] SceneHandler type not found – patch skipped.");
                return;
            }

            var initMethod = AccessTools.Method(targetType, "Init");
            if (initMethod == null)
            {
                MelonLogger.Warning("[Shim] SceneHandler.Init not found – patch skipped.");
                return;
            }

            var prefix = AccessTools.Method(typeof(ShimMod), nameof(SceneHandlerInitPrefix));
            _harmony.Patch(initMethod, prefix: new HarmonyMethod(prefix));
            MelonLogger.Msg("[Shim] SceneHandler.Init patched (SceneHandle-Fix active).");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[Shim] SceneHandler patch failed: {ex.Message}");
        }
    }

    // Prefix: skip the original Init, run our Unity-6-safe version instead
    private static bool SceneHandlerInitPrefix()
    {
        try
        {
            MelonLogger.Msg("[Shim] SceneHandler.Init intercepted – running Unity6-safe init.");
            LogAllScenes();
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[Shim] Safe init failed: {ex.Message}");
        }
        return false; // do NOT call original
    }

    // ── F8: manual scene dump ─────────────────────────────────────────────
    public override void OnUpdate()
    {
        try
        {
            if (Keyboard.current?.f8Key?.wasPressedThisFrame == true)
                LogAllScenes();
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[Shim] OnUpdate: {ex.Message}");
        }
    }

    // ── Safe scene enumeration (no GetAllScenes, no SceneHandle) ─────────
    internal static void LogAllScenes()
    {
        try
        {
            int count = SceneManager.sceneCount;
            MelonLogger.Msg($"[Shim] Scene count (sceneCount): {count}");

            for (int i = 0; i < count; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);

                // scene.name is safe — scene.handle is logged only, never cast to int
                MelonLogger.Msg($"  [{i}] name={scene.name} " +
                                $"loaded={scene.isLoaded} " +
                                $"rootCount={scene.rootCount}");

                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                    MelonLogger.Msg($"       Root: {root.name}");
            }

            // DontDestroyOnLoad objects via Resources (no GetAllScenes needed)
            var ddol = Resources.FindObjectsOfTypeAll<GameObject>();
            MelonLogger.Msg($"[Shim] FindObjectsOfTypeAll<GameObject>: {ddol.Length} objects");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[Shim] LogAllScenes failed: {ex.Message}");
        }
    }
}
