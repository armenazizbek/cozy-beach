using System;
using System.Collections.Generic;

namespace THREE
{
    public enum Side { FrontSide = 0, BackSide = 1, DoubleSide = 2 }

    public enum Blending { NoBlending = 0, NormalBlending = 1, AdditiveBlending = 2, SubtractiveBlending = 3, MultiplyBlending = 4, CustomBlending = 5 }

    public enum Wrapping { RepeatWrapping = 1000, ClampToEdgeWrapping = 1001, MirroredRepeatWrapping = 1002 }

    public enum TextureFilter { NearestFilter = 1003, NearestMipmapNearestFilter = 1004, NearestMipmapLinearFilter = 1005, LinearFilter = 1006, LinearMipmapNearestFilter = 1007, LinearMipmapLinearFilter = 1008 }

    public static class ColorSpace
    {
        public const string NoColorSpace = "";
        public const string SRGBColorSpace = "srgb";
        public const string LinearSRGBColorSpace = "srgb-linear";
    }

    public class Uniform
    {
        public object value;
        public Uniform(object v) { value = v; }

        public double f { get => Convert.ToDouble(value); set => this.value = value; }
    }

    /// <summary>
    /// Base material. Fields mirror Three.js; the bridge reads them every frame and updates the Unity material when they change.
    /// </summary>
    public class Material
    {
        /// <summary>The bridge's cache entry for this material (MaterialCache), kept here so the sync need not look it up each frame.</summary>
        internal object bridgeEntry;
        internal int bridgeKind, bridgeGen;

        private static int _nextId;
        public readonly int id = _nextId++;
        public string uuid = MathUtils.generateUUID();
        public string name = "";
        public virtual string type => "Material";
        public Side side = Side.FrontSide;
        public Side? shadowSide;
        public bool transparent;
        public double opacity = 1;
        public bool depthWrite = true, depthTest = true;
        public Blending blending = Blending.NormalBlending;
        public bool visible = true;
        public double alphaTest;
        public bool toneMapped = true;
        public bool fog = true;
        public bool vertexColors;
        public bool colorWrite = true;
        public bool polygonOffset;
        public double polygonOffsetFactor, polygonOffsetUnits;
        public bool premultipliedAlpha;
        public bool dithering;
        public bool lights = true;
        public Dictionary<string, object> userData = new Dictionary<string, object>();
        public int version;
        internal bool disposed;

        /// <summary>
        /// The Unity shader to draw with, when a Three.js material was customised with onBeforeCompile (the port puts that GLSL
        /// in a matching HLSL shader). Null means the standard one for this material type.
        /// </summary>
        public string customShader;
        /// <summary>Extra values the custom shader reads (what onBeforeCompile put into shader.uniforms).</summary>
        public Dictionary<string, Uniform> customUniforms = new Dictionary<string, Uniform>();

        public bool needsUpdate { set { if (value) version++; } get => false; }

        public virtual Material clone()
        {
            var m = (Material)MemberwiseClone();
            CloneInto(m);
            return m;
        }

        protected virtual void CloneInto(Material m)
        {
            typeof(Material).GetField("id").SetValue(m, _nextId++);
            m.uuid = MathUtils.generateUUID();
            m.userData = new Dictionary<string, object>(userData);
            m.customUniforms = new Dictionary<string, Uniform>();
            foreach (var kv in customUniforms) m.customUniforms[kv.Key] = new Uniform(kv.Value.value);
            m.disposed = false;
            m.bridgeEntry = null; // a clone is a new material and gets its own Unity material
        }

        public void dispose()
        {
            disposed = true;
            Bridge.MaterialCache.Release(this);
        }
    }

    public class MeshStandardMaterial : Material
    {
        public override string type => "MeshStandardMaterial";
        public Color color = new Color(0xffffff);
        public double roughness = 1, metalness = 0;
        public Color emissive = new Color(0x000000);
        public double emissiveIntensity = 1;
        public Texture map, emissiveMap, alphaMap, bumpMap, normalMap, roughnessMap, metalnessMap, aoMap, lightMap, envMap;
        public double bumpScale = 1, aoMapIntensity = 1, envMapIntensity = 1, lightMapIntensity = 1;
        public Vector2 normalScale = new Vector2(1, 1);
        public bool flatShading;
        public bool wireframe;

        protected override void CloneInto(Material m)
        {
            base.CloneInto(m);
            var s = (MeshStandardMaterial)m;
            s.color = color.clone();
            s.emissive = emissive.clone();
            s.normalScale = normalScale.clone();
        }
    }

    public class MeshPhysicalMaterial : MeshStandardMaterial
    {
        public override string type => "MeshPhysicalMaterial";
        public double clearcoat, clearcoatRoughness, transmission, ior = 1.5, thickness, sheen, sheenRoughness = 1, specularIntensity = 1, iridescence, reflectivity = 0.5;
        public Color sheenColor = new Color(0x000000);
        public Color specularColor = new Color(0xffffff);
        public Color attenuationColor = new Color(0xffffff);
        public double attenuationDistance = double.PositiveInfinity;
    }

    public class MeshLambertMaterial : Material
    {
        public override string type => "MeshLambertMaterial";
        public Color color = new Color(0xffffff);
        public Color emissive = new Color(0x000000);
        public double emissiveIntensity = 1;
        public Texture map, emissiveMap, alphaMap;
        public bool flatShading;
        public bool wireframe;

        protected override void CloneInto(Material m)
        {
            base.CloneInto(m);
            var s = (MeshLambertMaterial)m;
            s.color = color.clone();
            s.emissive = emissive.clone();
        }
    }

    public class MeshPhongMaterial : MeshLambertMaterial
    {
        public override string type => "MeshPhongMaterial";
        public Color specular = new Color(0x111111);
        public double shininess = 30;
    }

    public class MeshBasicMaterial : Material
    {
        public override string type => "MeshBasicMaterial";
        public Color color = new Color(0xffffff);
        public Texture map, alphaMap;
        public bool wireframe;

        public MeshBasicMaterial() { lights = false; }

        protected override void CloneInto(Material m)
        {
            base.CloneInto(m);
            ((MeshBasicMaterial)m).color = color.clone();
        }
    }

    public class SpriteMaterial : Material
    {
        public override string type => "SpriteMaterial";
        public Color color = new Color(0xffffff);
        public Texture map, alphaMap;
        public double rotation;
        public bool sizeAttenuation = true;

        public SpriteMaterial() { transparent = true; lights = false; }

        protected override void CloneInto(Material m)
        {
            base.CloneInto(m);
            ((SpriteMaterial)m).color = color.clone();
        }
    }

    public class PointsMaterial : Material
    {
        public override string type => "PointsMaterial";
        public Color color = new Color(0xffffff);
        public Texture map, alphaMap;
        public double size = 1;
        public bool sizeAttenuation = true;

        public PointsMaterial() { lights = false; }

        protected override void CloneInto(Material m)
        {
            base.CloneInto(m);
            ((PointsMaterial)m).color = color.clone();
        }
    }

    public class LineBasicMaterial : Material
    {
        public override string type => "LineBasicMaterial";
        public Color color = new Color(0xffffff);
        public double linewidth = 1;

        public LineBasicMaterial() { lights = false; }

        protected override void CloneInto(Material m)
        {
            base.CloneInto(m);
            ((LineBasicMaterial)m).color = color.clone();
        }
    }

    /// <summary>
    /// A material with hand-written shaders. In the port each GLSL program has an HLSL twin in Resources/Shaders, named by
    /// <see cref="Material.customShader"/>; the uniforms are passed through by name.
    /// </summary>
    public class ShaderMaterial : Material
    {
        public override string type => "ShaderMaterial";
        public Dictionary<string, Uniform> uniforms = new Dictionary<string, Uniform>();
        public string vertexShader = "", fragmentShader = "";
        public Dictionary<string, object> defines = new Dictionary<string, object>();
        public bool wireframe;

        public ShaderMaterial() { lights = false; fog = false; }

        protected override void CloneInto(Material m)
        {
            base.CloneInto(m);
            var s = (ShaderMaterial)m;
            s.uniforms = new Dictionary<string, Uniform>();
            foreach (var kv in uniforms) s.uniforms[kv.Key] = new Uniform(kv.Value.value is Color c ? c.clone() : kv.Value.value is Vector3 v ? v.clone() : kv.Value.value);
        }
    }

    public class RawShaderMaterial : ShaderMaterial
    {
        public override string type => "RawShaderMaterial";
    }
}
