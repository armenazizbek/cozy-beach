using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The UI's drawings, baked in the editor (unity/UI_PLAN.md 2.2, U1): the tool icons and the shop pictures used to be
/// rasterised from SVG while the game ran. The editor's icon baker (Assets/Editor/IconBaker.cs, run before every build)
/// rasterises each one with the same code and keeps the pixels in Resources/UI/Icons.bytes; here they become textures with
/// the settings the runtime rasteriser gave them, so the pictures are the same to the pixel and nothing is drawn at runtime.
/// </summary>
public static class IconAtlas
{
    private static Dictionary<ulong, (int w, int h, int offset)> index;
    private static byte[] data;
    private static readonly Dictionary<ulong, Texture2D> made = new Dictionary<ulong, Texture2D>();

    /// <summary>The key a drawing is kept under: a 64-bit FNV-1a hash of its markup.</summary>
    public static ulong Key(string markup)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in markup)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return h;
    }

    /// <summary>The baked picture for this markup, or null when it was not baked (then the runtime rasteriser draws it).</summary>
    public static Texture2D Get(string markup)
    {
        if (string.IsNullOrEmpty(markup)) return null;
        Load();
        ulong key = Key(markup);
        if (made.TryGetValue(key, out var tex) && tex != null) return tex;
        if (index == null || !index.TryGetValue(key, out var e)) return null;
        var bytes = new byte[e.w * e.h * 4];
        Buffer.BlockCopy(data, e.offset, bytes, 0, bytes.Length);
        tex = new Texture2D(e.w, e.h, TextureFormat.RGBA32, false)
        {
            name = "SvgIcon",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        tex.SetPixelData(bytes, 0);
        tex.Apply(false, true);
        made[key] = tex;
        return tex;
    }

    /// <summary>How many drawings are baked.</summary>
    public static int Count
    {
        get
        {
            Load();
            return index?.Count ?? 0;
        }
    }

    private static void Load()
    {
        if (index != null) return;
        index = new Dictionary<ulong, (int, int, int)>();
        var asset = Resources.Load<TextAsset>("UI/Icons");
        if (asset == null) return;
        data = asset.bytes;
        // [count] then per drawing: [key u64][w i32][h i32][w*h*4 bytes RGBA, first row at the bottom]
        int p = 0;
        int count = BitConverter.ToInt32(data, p); p += 4;
        for (int i = 0; i < count; i++)
        {
            ulong key = BitConverter.ToUInt64(data, p); p += 8;
            int w = BitConverter.ToInt32(data, p); p += 4;
            int h = BitConverter.ToInt32(data, p); p += 4;
            index[key] = (w, h, p);
            p += w * h * 4;
        }
    }
}
