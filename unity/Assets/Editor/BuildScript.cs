#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Command-line and menu build entry points. Both write `Builds/Windows/CozyTreasureHunter.exe`.
///   BuildScript.BuildWindows      the release build: IL2CPP (C# compiled to native code) when Unity's IL2CPP module is
///                                 installed, otherwise Mono with a warning
///   BuildScript.BuildWindowsMono  a quick Mono build for testing changes; several minutes faster to make, slower to run
/// </summary>
public static class BuildScript
{
    private const string SCENE_PATH = "Assets/Scenes/Main.unity";
    private const string WINDOWS_OUT = "Builds/Windows/CozyTreasureHunter.exe";

    [MenuItem("Cozy Treasure Hunter/Build Windows (x64)")]
    public static void BuildWindows() => Build(preferIl2Cpp: true);

    [MenuItem("Cozy Treasure Hunter/Build Windows (x64, quick Mono)")]
    public static void BuildWindowsMono() => Build(preferIl2Cpp: false);

    private static void Build(bool preferIl2Cpp)
    {
        ProjectSetup.Apply();
        EmojiSpriteAssetBuilder.Rebuild();
        bool il2cpp = preferIl2Cpp && Il2CppInstalled();
        if (preferIl2Cpp && !il2cpp)
            Debug.LogWarning("IL2CPP is not installed for Windows, so this build uses Mono. Add \"Windows Build Support (IL2CPP)\" in Unity Hub for the faster build.");
        UseBackend(il2cpp);

        var dir = Path.GetDirectoryName(WINDOWS_OUT);
        // Unity will not build IL2CPP over a Mono build or the other way round, so a change of backend starts the
        // (generated, git-ignored) output folder afresh
        bool wasMono = Directory.Exists(Path.Combine(dir, "MonoBleedingEdge"));
        bool wasIl2Cpp = File.Exists(Path.Combine(dir, "GameAssembly.dll"));
        if ((il2cpp && wasMono) || (!il2cpp && wasIl2Cpp)) Directory.Delete(dir, true);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) scenes = new[] { SCENE_PATH };

        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = WINDOWS_OUT,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(opts);
        var summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
        {
            WriteThirdPartyNotices(dir);
            Debug.Log($"Windows build succeeded ({(il2cpp ? "IL2CPP" : "Mono")}): {Path.GetFullPath(WINDOWS_OUT)} ({summary.totalSize / (1024 * 1024)} MB)");
        }
        else
        {
            throw new Exception($"Windows build failed: {summary.result} ({summary.totalErrors} errors)");
        }
    }

    /// <summary>True when Unity Hub's "Windows Build Support (IL2CPP)" module is installed for this editor.</summary>
    private static bool Il2CppInstalled()
    {
        var engine = BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.StandaloneWindows64, BuildOptions.None);
        return !string.IsNullOrEmpty(engine) && Directory.Exists(Path.Combine(engine, "Variations", "win64_player_nondevelopment_il2cpp"));
    }

    private static void UseBackend(bool il2cpp)
    {
        var target = NamedBuildTarget.Standalone;
        PlayerSettings.SetScriptingBackend(target, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
        if (!il2cpp) return;
        PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Release);
        // saves are read with Newtonsoft JSON through reflection, so the game's own types must not be stripped away
        PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.Minimal);
    }

    /// <summary>The UI's colour emoji are Microsoft's Fluent Emoji, whose MIT licence must travel with the game.</summary>
    private static void WriteThirdPartyNotices(string buildDir)
    {
        const string FLUENT = "Tools/Emoji/LICENSE-fluentui-emoji.txt";
        if (!File.Exists(FLUENT)) return;
        File.WriteAllText(Path.Combine(buildDir, "ThirdPartyNotices.txt"),
            "Cozy Treasure Hunter uses the following third-party material.\n\n" +
            "Fluent Emoji (https://github.com/microsoft/fluentui-emoji), the colour emoji in the menus and HUD:\n\n" +
            File.ReadAllText(FLUENT));
    }
}
#endif
