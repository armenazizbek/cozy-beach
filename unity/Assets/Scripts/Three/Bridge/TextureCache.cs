using System.Collections.Generic;
using UnityEngine;

namespace THREE.Bridge
{
    /// <summary>Uploads Three textures (canvases and raw pixels) as Unity Texture2Ds, again whenever needsUpdate was set.</summary>
    internal static class TextureCache
    {
        private class Entry
        {
            public UnityEngine.Texture tex;
            public int version = int.MinValue;
            public int w, h;
            public bool srgb, mips;
        }

        private static readonly Dictionary<THREE.Texture, Entry> cache = new Dictionary<THREE.Texture, Entry>();

        /// <summary>Destroys every texture uploaded so far (see WebGLRenderer.Teardown). Render targets belong to their owners.</summary>
        public static void Clear()
        {
            foreach (var e in cache.Values) if (e.tex != null && !(e.tex is RenderTexture)) Object.Destroy(e.tex);
            cache.Clear();
        }

        public static void Release(THREE.Texture t)
        {
            if (cache.TryGetValue(t, out var e))
            {
                if (e.tex != null && !(e.tex is RenderTexture)) Object.Destroy(e.tex);
                cache.Remove(t);
            }
        }

        public static UnityEngine.Texture Get(THREE.Texture t)
        {
            if (t == null || t.disposed) return null;
            if (t.userData.TryGetValue("renderTarget", out var rtObj) && rtObj is WebGLRenderTarget rt) return rt.Ensure();
            if (!cache.TryGetValue(t, out var e))
            {
                e = new Entry();
                cache[t] = e;
            }
            if (e.version == t.version && e.tex != null)
            {
                ApplySampler(t, e.tex);
                return e.tex;
            }
            e.version = t.version;
            var (w, h) = t.Size();
            if (w == 0 || h == 0) return e.tex;
            bool srgb = t.colorSpace == ColorSpace.SRGBColorSpace;
            bool mips = t.generateMipmaps && (t.minFilter == TextureFilter.LinearMipmapLinearFilter || t.minFilter == TextureFilter.LinearMipmapNearestFilter
                || t.minFilter == TextureFilter.NearestMipmapLinearFilter || t.minFilter == TextureFilter.NearestMipmapNearestFilter);
            if (!(e.tex is Texture2D tex2) || e.w != w || e.h != h || e.srgb != srgb || e.mips != mips)
            {
                if (e.tex != null) Object.Destroy(e.tex);
                tex2 = new Texture2D(w, h, TextureFormat.RGBA32, mips, !srgb) { name = t.name };
                e.tex = tex2;
                e.w = w;
                e.h = h;
                e.srgb = srgb;
                e.mips = mips;
            }
            byte[] bytes = null;
            if (t.image is HTMLCanvasElement c) bytes = c.ToUnityRGBA(t.flipY);
            else if (t.image is ImageData d) bytes = Expand(d, t.flipY);
            if (bytes != null)
            {
                tex2.SetPixelData(bytes, 0);
                tex2.Apply(mips, false);
            }
            ApplySampler(t, tex2);
            return tex2;
        }

        private static byte[] Expand(ImageData d, bool flipY)
        {
            int w = d.width, h = d.height;
            var o = new byte[w * h * 4];
            int ch = d.format == PixelFormat.RedFormat || d.format == PixelFormat.AlphaFormat ? 1 : d.format == PixelFormat.RGBFormat ? 3 : 4;
            for (int y = 0; y < h; y++)
            {
                int sy = flipY ? h - 1 - y : y;
                for (int x = 0; x < w; x++)
                {
                    int si = (sy * w + x) * ch, di = (y * w + x) * 4;
                    if (si + ch > d.data.Length) continue;
                    if (ch == 1)
                    {
                        byte v = d.data[si];
                        o[di] = v; o[di + 1] = v; o[di + 2] = v; o[di + 3] = d.format == PixelFormat.AlphaFormat ? v : (byte)255;
                    }
                    else
                    {
                        o[di] = d.data[si]; o[di + 1] = d.data[si + 1]; o[di + 2] = d.data[si + 2];
                        o[di + 3] = ch == 4 ? d.data[si + 3] : (byte)255;
                    }
                }
            }
            return o;
        }

        private static void ApplySampler(THREE.Texture t, UnityEngine.Texture u)
        {
            var wu = t.wrapS == Wrapping.RepeatWrapping ? TextureWrapMode.Repeat : t.wrapS == Wrapping.MirroredRepeatWrapping ? TextureWrapMode.Mirror : TextureWrapMode.Clamp;
            var wv = t.wrapT == Wrapping.RepeatWrapping ? TextureWrapMode.Repeat : t.wrapT == Wrapping.MirroredRepeatWrapping ? TextureWrapMode.Mirror : TextureWrapMode.Clamp;
            if (u.wrapModeU != wu) u.wrapModeU = wu;
            if (u.wrapModeV != wv) u.wrapModeV = wv;
            var f = t.magFilter == TextureFilter.NearestFilter ? FilterMode.Point
                : t.minFilter == TextureFilter.LinearMipmapLinearFilter || t.minFilter == TextureFilter.NearestMipmapLinearFilter ? FilterMode.Trilinear : FilterMode.Bilinear;
            if (u.filterMode != f) u.filterMode = f;
            int aniso = Mathf.Clamp(t.anisotropy, 1, 16);
            if (u.anisoLevel != aniso) u.anisoLevel = aniso;
        }
    }
}
