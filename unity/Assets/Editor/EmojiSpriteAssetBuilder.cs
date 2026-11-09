#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.Text;

/// <summary>
/// Turns Assets/Art/Emoji/EmojiAtlas.png (made by unity/Tools/Emoji/update_emoji.py from Microsoft's Fluent Emoji) into a TextCore
/// sprite asset at Assets/Resources/UI/Emoji.asset, with one sprite per emoji keyed by its code point. DomHost hands it to the
/// UI's text settings, so emoji typed anywhere in the game's text draw in colour: Unity's text engine cannot draw colour emoji
/// fonts, which is why the system emoji came out as black outlines or empty boxes.
/// </summary>
public static class EmojiSpriteAssetBuilder
{
    private const string ATLAS = "Assets/Art/Emoji/EmojiAtlas.png";
    private const string LAYOUT = "Assets/Art/Emoji/EmojiAtlas.json";
    private const string OUT = "Assets/Resources/UI/Emoji.asset";

    /// <summary>How tall an emoji is next to the text, in em (the browser's colour emoji are about this size).</summary>
    private const float EM_SIZE = 1.15f;
    /// <summary>How much of the emoji sits above the baseline.</summary>
    private const float ASCENT = 0.84f;

    [Serializable]
    private class Layout
    {
        public int cell, padding, columns, width, height;
        public string[] unicodes;
    }

    [MenuItem("Cozy Treasure Hunter/Rebuild Emoji Sprite Asset")]
    public static void Rebuild()
    {
        if (!File.Exists(ATLAS) || !File.Exists(LAYOUT))
        {
            Debug.LogWarning($"Emoji: {ATLAS} is missing; run unity/Tools/Emoji/update_emoji.py first.");
            return;
        }
        var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(LAYOUT));
        var tex = ImportAtlas();
        if (tex == null) return;

        int size = layout.cell - 2 * layout.padding;
        var sa = ScriptableObject.CreateInstance<SpriteAsset>();
        sa.name = "Emoji";
        SetInternal(sa, "spriteSheet", tex);
        SetInternal(sa, "faceInfo", new FaceInfo
        {
            familyName = "Fluent Emoji",
            styleName = "Flat",
            // an emoji EM_SIZE em tall: one em of text is size / EM_SIZE pixels of the atlas
            pointSize = Mathf.RoundToInt(size / EM_SIZE),
            scale = 1,
            lineHeight = size,
            ascentLine = size * ASCENT,
            capLine = size * ASCENT,
            meanLine = size * 0.5f,
            baseline = 0,
            descentLine = -size * (1 - ASCENT),
        });

        var shader = Shader.Find("Hidden/TextCore/Sprite") ?? Shader.Find("Text/Sprite");
        Material mat = null;
        if (shader != null)
        {
            mat = new Material(shader) { name = "Emoji Material" };
            mat.SetTexture("_MainTex", tex);
            sa.material = mat;
        }

        for (int i = 0; i < layout.unicodes.Length; i++)
        {
            uint unicode = Convert.ToUInt32(layout.unicodes[i], 16);
            int col = i % layout.columns, row = i / layout.columns;
            // glyph rects count from the bottom of the texture, the layout from the top
            var rect = new GlyphRect(col * layout.cell + layout.padding, layout.height - (row * layout.cell + layout.padding) - size, size, size);
            var metrics = new GlyphMetrics(size, size, size * 0.04f, size * ASCENT, size * 1.08f);
            var glyph = new SpriteGlyph((uint)i, metrics, rect, 1f, 0);
            sa.spriteGlyphTable.Add(glyph);
            sa.spriteCharacterTable.Add(new SpriteCharacter(unicode, sa, glyph) { name = "u" + layout.unicodes[i] });
        }
        sa.UpdateLookupTables();

        if (!AssetDatabase.IsValidFolder("Assets/Resources/UI")) AssetDatabase.CreateFolder("Assets/Resources", "UI");
        AssetDatabase.DeleteAsset(OUT);
        AssetDatabase.CreateAsset(sa, OUT);
        if (mat != null) AssetDatabase.AddObjectToAsset(mat, sa);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(OUT, ImportAssetOptions.ForceUpdate);
        Debug.Log($"Emoji: {layout.unicodes.Length} sprites written to {OUT}");
    }

    private static Texture2D ImportAtlas()
    {
        AssetDatabase.ImportAsset(ATLAS, ImportAssetOptions.ForceSynchronousImport);
        var imp = AssetImporter.GetAtPath(ATLAS) as TextureImporter;
        if (imp == null)
        {
            Debug.LogWarning($"Emoji: {ATLAS} did not import as a texture.");
            return null;
        }
        bool dirty = false;
        void Set<T>(T now, T want, Action apply)
        {
            if (!Equals(now, want)) { apply(); dirty = true; }
        }
        Set(imp.textureType, TextureImporterType.Default, () => imp.textureType = TextureImporterType.Default);
        Set(imp.sRGBTexture, true, () => imp.sRGBTexture = true);
        Set(imp.alphaSource, TextureImporterAlphaSource.FromInput, () => imp.alphaSource = TextureImporterAlphaSource.FromInput);
        Set(imp.alphaIsTransparency, true, () => imp.alphaIsTransparency = true);
        // emoji are drawn at 16-40 px from 88 px pictures, so mipmaps keep them smooth
        Set(imp.mipmapEnabled, true, () => imp.mipmapEnabled = true);
        Set(imp.wrapMode, TextureWrapMode.Clamp, () => imp.wrapMode = TextureWrapMode.Clamp);
        Set(imp.filterMode, FilterMode.Trilinear, () => imp.filterMode = FilterMode.Trilinear);
        Set(imp.npotScale, TextureImporterNPOTScale.None, () => imp.npotScale = TextureImporterNPOTScale.None);
        Set(imp.textureCompression, TextureImporterCompression.CompressedHQ, () => imp.textureCompression = TextureImporterCompression.CompressedHQ);
        Set(imp.isReadable, false, () => imp.isReadable = false);
        if (dirty) imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(ATLAS);
    }

    /// <summary>The sprite sheet and face info have internal setters in Unity 6; the editor's own sprite-asset tools set them too.</summary>
    private static void SetInternal(object target, string property, object value)
    {
        var p = target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var set = p?.GetSetMethod(true);
        if (set == null) throw new MissingMemberException(target.GetType().Name, property);
        set.Invoke(target, new[] { value });
    }
}
#endif
