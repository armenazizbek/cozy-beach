#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Configures the Unity project on first open: creates the URP pipeline asset, enables both Input System backends,
/// registers the Cozy shaders, sets linear colour space and adds Assets/Scenes/Main.unity to the build.
/// </summary>
[InitializeOnLoad]
public static class ProjectSetup
{
    private const string PIPE_DIR = "Assets/Settings";
    private const string PIPE_PATH = "Assets/Settings/CozyURP.asset";
    private const string SCENE_DIR = "Assets/Scenes";
    private const string SCENE_PATH = "Assets/Scenes/Main.unity";

    static ProjectSetup()
    {
        EditorApplication.delayCall += EnsureSetup;
    }

    [MenuItem("Cozy Treasure Hunter/Apply Project Setup")]
    public static void Apply()
    {
        EnsureSetup();
        Debug.Log("Cozy Treasure Hunter project setup applied.");
    }

    private static void EnsureSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        bool changed = false;

        // 1. Player settings: product name, linear colour space, Input System = Both (2)
        if (PlayerSettings.productName != "Cozy Treasure Hunter")
        {
            PlayerSettings.companyName = "Cozy Beach";
            PlayerSettings.productName = "Cozy Treasure Hunter";
            PlayerSettings.runInBackground = true;
            changed = true;
        }
        if (PlayerSettings.colorSpace != ColorSpace.Linear)
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            changed = true;
        }
        if (EnsureInputHandlingBoth()) changed = true;
        // per-frame CPU and GPU timings, read by the -autoshot smoke test (practically free)
        if (!PlayerSettings.enableFrameTimingStats)
        {
            PlayerSettings.enableFrameTimingStats = true;
            changed = true;
        }

        // 2. URP pipeline asset
        if (EnsureUrpAsset()) changed = true;

        // 3. Always-included shaders from Assets/Resources/Shaders
        if (EnsureAlwaysIncludedShaders()) changed = true;

        // 4. Main scene & build settings
        if (EnsureMainScene()) changed = true;

        if (changed) AssetDatabase.SaveAssets();
    }

    private static bool EnsureInputHandlingBoth()
    {
        var obj = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
        if (obj == null) return false;
        var so = new SerializedObject(obj);
        var prop = so.FindProperty("activeInputHandler");
        if (prop == null || prop.intValue == 2) return false;
        prop.intValue = 2; // 0 = old Input Manager, 1 = Input System package, 2 = Both
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    private static bool EnsureUrpAsset()
    {
        if (!AssetDatabase.IsValidFolder(PIPE_DIR)) AssetDatabase.CreateFolder("Assets", "Settings");

        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PIPE_PATH);
        bool created = false;
        if (asset == null)
        {
            asset = UniversalRenderPipelineAsset.Create(CreateRendererData());
            asset.name = "CozyURP";
            AssetDatabase.CreateAsset(asset, PIPE_PATH);
            created = true;
        }
        if (EnsureDefaultRenderer(asset)) created = true;

        asset.supportsHDR = false;
        asset.msaaSampleCount = 4;
        asset.shadowDistance = 60f;
        asset.shadowCascadeCount = 2;
        EditorUtility.SetDirty(asset);

        bool assigned = false;
        if (GraphicsSettings.defaultRenderPipeline != asset)
        {
            GraphicsSettings.defaultRenderPipeline = asset;
            assigned = true;
        }
        if (QualitySettings.renderPipeline != asset)
        {
            QualitySettings.renderPipeline = asset;
            assigned = true;
        }
        return created || assigned;
    }

    private const string RENDERER_PATH = "Assets/Settings/CozyURP_Renderer.asset";

    /// <summary>
    /// A URP asset needs a renderer saved as its own asset, or URP draws nothing ("Default Renderer is missing").
    /// <c>UniversalRenderPipelineAsset.Create()</c> on its own leaves that slot empty, so fill it here.
    /// </summary>
    private static bool EnsureDefaultRenderer(UniversalRenderPipelineAsset asset)
    {
        var so = new SerializedObject(asset);
        var list = so.FindProperty("m_RendererDataList");
        if (list == null) return false;
        if (list.arraySize > 0 && list.GetArrayElementAtIndex(0).objectReferenceValue != null
            && AssetDatabase.Contains(list.GetArrayElementAtIndex(0).objectReferenceValue)) return false;

        ScriptableRendererData data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RENDERER_PATH);
        if (data == null)
        {
            data = CreateRendererData();
            if (!AssetDatabase.Contains(data)) AssetDatabase.CreateAsset(data, RENDERER_PATH);
        }
        if (list.arraySize == 0) list.InsertArrayElementAtIndex(0);
        list.GetArrayElementAtIndex(0).objectReferenceValue = data;
        var idx = so.FindProperty("m_DefaultRendererIndex");
        if (idx != null) idx.intValue = 0;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        return true;
    }

    /// <summary>Makes the Universal Renderer the same way URP's own "Create > URP Asset" menu does, saved next to the pipeline asset.</summary>
    private static ScriptableRendererData CreateRendererData()
    {
        var existing = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RENDERER_PATH);
        if (existing != null) return existing;

        // URP's helper is internal; it also fills in the post-processing resources
        var make = typeof(UniversalRenderPipelineAsset).GetMethod("CreateRendererAsset",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (make != null)
        {
            try
            {
                if (make.Invoke(null, new object[] { RENDERER_PATH, RendererType.UniversalRenderer, false, "Renderer" }) is ScriptableRendererData made)
                    return made;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"ProjectSetup: URP's renderer helper failed ({e.InnerException?.Message ?? e.Message}); creating the renderer directly.");
            }
        }

        var data = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(data, RENDERER_PATH);
        ResourceReloader.ReloadAllNullIn(data, UniversalRenderPipelineAsset.packagePath);
        return data;
    }

    private static bool EnsureAlwaysIncludedShaders()
    {
        var gsObj = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
        if (gsObj == null) return false;
        var so = new SerializedObject(gsObj);
        var list = so.FindProperty("m_AlwaysIncludedShaders");
        if (list == null) return false;

        var shaders = Resources.LoadAll<Shader>("Shaders");
        bool added = false;
        foreach (var sh in shaders)
        {
            if (sh == null) continue;
            bool exists = false;
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == sh)
                {
                    exists = true;
                    break;
                }
            }
            if (!exists)
            {
                int idx = list.arraySize;
                list.InsertArrayElementAtIndex(idx);
                list.GetArrayElementAtIndex(idx).objectReferenceValue = sh;
                added = true;
            }
        }
        if (added) so.ApplyModifiedPropertiesWithoutUndo();
        return added;
    }

    private static bool EnsureMainScene()
    {
        if (!AssetDatabase.IsValidFolder(SCENE_DIR)) AssetDatabase.CreateFolder("Assets", "Scenes");

        bool changed = false;
        if (!File.Exists(SCENE_PATH))
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("GameHost");
            go.AddComponent<GameHost>();
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            changed = true;
        }

        var existing = EditorBuildSettings.scenes;
        if (!existing.Any(s => s.path == SCENE_PATH))
        {
            var list = existing.ToList();
            list.Insert(0, new EditorBuildSettingsScene(SCENE_PATH, true));
            EditorBuildSettings.scenes = list.ToArray();
            changed = true;
        }
        return changed;
    }
}
#endif
