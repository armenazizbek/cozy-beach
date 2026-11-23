using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes the UI's SVG drawings (the tool icons and the shop pictures) into Resources/UI/Icons.bytes with the game's own
/// rasteriser, so the game shows the same pixels without drawing them at runtime (UI/Native/IconAtlas.cs, unity/UI_PLAN.md U1).
/// BuildScript runs it before every build; Tidefinder > Bake UI icons runs it by hand.
/// </summary>
public static class IconBaker
{
    private const string OUT = "Assets/Resources/UI/Icons.bytes";

    [MenuItem("Tidefinder/Bake UI icons")]
    public static void Bake()
    {
        var markups = new List<string>(UIManager.ToolIcons);
        markups.AddRange(ShopArt.All());
        var seen = new HashSet<ulong>();
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(0);
            int count = 0;
            foreach (var m in markups)
            {
                ulong key = IconAtlas.Key(m);
                if (!seen.Add(key)) continue;
                var rgba = SvgRasterizer.RasterizeRgba(m, out int tw, out int th);
                if (rgba == null) continue;
                w.Write(key);
                w.Write(tw);
                w.Write(th);
                w.Write(rgba);
                count++;
            }
            w.Seek(0, SeekOrigin.Begin);
            w.Write(count);
            w.Flush();
            var bytes = ms.ToArray();
            // unchanged drawings leave the file untouched, so a build does not dirty the repository
            if (!File.Exists(OUT) || !SameBytes(File.ReadAllBytes(OUT), bytes))
            {
                File.WriteAllBytes(OUT, bytes);
                AssetDatabase.ImportAsset(OUT);
            }
            Debug.Log($"[IconBaker] {count} drawings baked into {OUT} ({bytes.Length / 1024} KB)");
        }
    }

    private static bool SameBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}
