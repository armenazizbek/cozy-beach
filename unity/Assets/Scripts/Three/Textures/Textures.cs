using System;

namespace THREE
{
    public class Texture
    {
        private static int _nextId;
        public readonly int id = _nextId++;
        public string uuid = MathUtils.generateUUID();
        public string name = "";
        /// <summary>An HTMLCanvasElement, an ImageData, or null.</summary>
        public object image;
        public Wrapping wrapS = Wrapping.ClampToEdgeWrapping, wrapT = Wrapping.ClampToEdgeWrapping;
        public TextureFilter magFilter = TextureFilter.LinearFilter, minFilter = TextureFilter.LinearMipmapLinearFilter;
        public int anisotropy = 1;
        public Vector2 offset = new Vector2(0, 0), repeat = new Vector2(1, 1), center = new Vector2(0, 0);
        public double rotation;
        public bool generateMipmaps = true;
        public bool flipY = true;
        public string colorSpace = ColorSpace.NoColorSpace;
        public int version;
        public Dictionary userData = new Dictionary();
        internal bool disposed;

        public class Dictionary : System.Collections.Generic.Dictionary<string, object> { }

        public Texture() { }
        public Texture(object image) { this.image = image; }

        public bool needsUpdate { set { if (value) version++; } get => false; }

        public virtual Texture clone()
        {
            var t = (Texture)MemberwiseClone();
            t.offset = offset.clone();
            t.repeat = repeat.clone();
            t.center = center.clone();
            return t;
        }

        public void dispose()
        {
            disposed = true;
            Bridge.TextureCache.Release(this);
        }

        /// <summary>Width and height of the image, or 0 when there is none.</summary>
        internal (int w, int h) Size()
        {
            if (image is HTMLCanvasElement c) return (c.width, c.height);
            if (image is ImageData d) return (d.width, d.height);
            return (0, 0);
        }
    }

    public class CanvasTexture : Texture
    {
        public CanvasTexture(HTMLCanvasElement canvas) : base(canvas) { version = 1; }
    }

    public enum PixelFormat { RGBAFormat = 1023, RGBFormat = 1022, RedFormat = 1028, AlphaFormat = 1021 }

    /// <summary>A texture from raw pixel values: bytes 0..255 (or floats 0..1) in RGBA order, first row at the bottom (flipY false).</summary>
    public class DataTexture : Texture
    {
        public DataTexture(byte[] data, int width, int height, PixelFormat format = PixelFormat.RGBAFormat)
        {
            image = new ImageData(width, height, data, format);
            flipY = false;
            generateMipmaps = false;
            magFilter = TextureFilter.NearestFilter;
            minFilter = TextureFilter.NearestFilter;
            version = 1;
        }

        public DataTexture(float[] data, int width, int height, PixelFormat format = PixelFormat.RGBAFormat)
        {
            var bytes = new byte[data.Length];
            for (int i = 0; i < data.Length; i++) bytes[i] = (byte)Math.Max(0, Math.Min(255, Math.Round(data[i] * 255)));
            image = new ImageData(width, height, bytes, format);
            flipY = false;
            generateMipmaps = false;
            magFilter = TextureFilter.NearestFilter;
            minFilter = TextureFilter.NearestFilter;
            version = 1;
        }
    }

    /// <summary>A render target the game can draw a scene into (the bridge backs it with a RenderTexture).</summary>
    public class WebGLRenderTarget
    {
        public int width, height;
        public Texture texture = new Texture();
        internal UnityEngine.RenderTexture rt;

        public WebGLRenderTarget(int width = 1, int height = 1)
        {
            this.width = width;
            this.height = height;
            texture.flipY = false;
            texture.userData["renderTarget"] = this;
        }

        public void setSize(int w, int h)
        {
            if (w == width && h == height) return;
            width = w;
            height = h;
            if (rt != null)
            {
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                rt = null;
            }
        }

        internal UnityEngine.RenderTexture Ensure()
        {
            if (rt == null)
            {
                rt = new UnityEngine.RenderTexture(Math.Max(1, width), Math.Max(1, height), 24, UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.sRGB);
                rt.antiAliasing = 1;
                rt.Create();
            }
            return rt;
        }

        public void dispose()
        {
            if (rt != null)
            {
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                rt = null;
            }
        }
    }
}
