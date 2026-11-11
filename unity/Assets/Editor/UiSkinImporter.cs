#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the UI skin (Assets/Resources/UI/Skin, drawn by unity/Tools/UiSkin/make_skin.py): kept at their own
/// size, uncompressed, no mip maps, clamped, so the 9-slice cuts in Style.uss land on the pixels they name and edges stay clean.
/// </summary>
public class UiSkinImporter : AssetPostprocessor
{
    private const string FOLDER = "Assets/Resources/UI/Skin";

    private void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(FOLDER + "/")) return;
        Configure((TextureImporter)assetImporter);
    }

    private static bool Configure(TextureImporter ti)
    {
        bool ok = ti.textureType == TextureImporterType.Default && ti.npotScale == TextureImporterNPOTScale.None && !ti.mipmapEnabled
                  && ti.alphaIsTransparency && ti.wrapMode == TextureWrapMode.Clamp && ti.filterMode == FilterMode.Bilinear
                  && ti.textureCompression == TextureImporterCompression.Uncompressed && ti.sRGBTexture;
        if (ok) return false;
        ti.textureType = TextureImporterType.Default;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.sRGBTexture = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        return true;
    }

    /// <summary>Run by every build: makes sure each picture in the skin folder was imported with the settings above.</summary>
    [MenuItem("Cozy Treasure Hunter/Reimport UI Skin")]
    public static void Apply()
    {
        if (!Directory.Exists(FOLDER))
        {
            Debug.LogWarning($"UI skin: {FOLDER} is missing; run unity/Tools/UiSkin/make_skin.py first.");
            return;
        }
        AssetDatabase.Refresh();
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { FOLDER }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti && Configure(ti)) ti.SaveAndReimport();
        }
    }
}
#endif
