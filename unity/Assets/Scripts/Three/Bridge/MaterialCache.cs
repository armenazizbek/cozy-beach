using System;
using System.Collections.Generic;
using UnityEngine;
using U = UnityEngine;
using UnityEngine.Rendering;

namespace THREE.Bridge
{
    /// <summary>
    /// Keeps one Unity material per Three material (and per kind of object it is drawn on), and copies the fields across each
    /// frame they change. The shaders in Resources/Shaders read these properties.
    /// </summary>
    internal static class MaterialCache
    {
        /// <summary>Instanced is a baked InstancedMesh: same as Mesh, plus its per-instance colours (TEXCOORD1).</summary>
        public enum Kind { Mesh, Points, Sprite, Line, Instanced }

        private class Entry
        {
            public UnityEngine.Material mat;
            public long hash = long.MinValue;
            public int frame = -1;
            public string shader;
        }

        private static readonly Dictionary<(THREE.Material, Kind), Entry> cache = new Dictionary<(THREE.Material, Kind), Entry>();

        /// <summary>Destroys every Unity material made so far (see WebGLRenderer.Teardown); shaders stay loaded.</summary>
        public static void Clear()
        {
            foreach (var e in cache.Values) if (e.mat != null) UnityEngine.Object.Destroy(e.mat);
            gen++;
            cache.Clear();
        }
        private static readonly Dictionary<string, Shader> shaders = new Dictionary<string, Shader>();
        private static int frame;
        public static void NextFrame() => frame++;

        /// <summary>Goes up when the cache is emptied, so entries remembered on materials are looked up again.</summary>
        private static int gen = 1;

        public static void Release(THREE.Material m)
        {
            m.bridgeEntry = null;
            foreach (Kind k in Enum.GetValues(typeof(Kind)))
            {
                if (cache.TryGetValue((m, k), out var e))
                {
                    if (e.mat != null) UnityEngine.Object.Destroy(e.mat);
                    cache.Remove((m, k));
                }
            }
        }

        private static bool loadedResourceShaders;

        public static Shader FindShader(string name)
        {
            if (!loadedResourceShaders)
            {
                loadedResourceShaders = true;
                foreach (var rs in Resources.LoadAll<Shader>("Shaders"))
                    if (rs != null && !string.IsNullOrEmpty(rs.name)) shaders[rs.name] = rs;
            }
            if (shaders.TryGetValue(name, out var s) && s != null) return s;
            s = Shader.Find(name);
            if (s == null)
            {
                Debug.LogWarning($"Shader '{name}' not found, using Cozy/Standard");
                s = Shader.Find("Cozy/Standard");
            }
            shaders[name] = s;
            return s;
        }

        private static string ShaderFor(THREE.Material m, Kind kind)
        {
            if (!string.IsNullOrEmpty(m.customShader)) return m.customShader;
            if (kind == Kind.Points) return "Cozy/Points";
            if (kind == Kind.Sprite) return "Cozy/Sprite";
            switch (m)
            {
                case MeshBasicMaterial _:
                case LineBasicMaterial _:
                case PointsMaterial _:
                case SpriteMaterial _:
                    return "Cozy/Basic";
                case ShaderMaterial _:
                    return "Cozy/Basic";
                default:
                    return "Cozy/Standard";
            }
        }

        public static UnityEngine.Material Get(THREE.Material m, Kind kind)
        {
            if (m == null) return null;
            Entry e;
            if (m.bridgeEntry is Entry known && m.bridgeKind == (int)kind && m.bridgeGen == gen) e = known;
            else
            {
                var key = (m, kind);
                if (!cache.TryGetValue(key, out e))
                {
                    var sh = ShaderFor(m, kind);
                    e = new Entry { mat = new UnityEngine.Material(FindShader(sh)) { name = m.type + " " + m.name }, shader = sh };
                    cache[key] = e;
                }
                m.bridgeEntry = e;
                m.bridgeKind = (int)kind;
                m.bridgeGen = gen;
            }
            if (e.frame == frame) return e.mat;
            e.frame = frame;
            long h = Hash(m);
            bool custom = m is ShaderMaterial || m.customUniforms.Count > 0;
            if (h != e.hash)
            {
                e.hash = h;
                Apply(m, e.mat, kind);
            }
            if (custom) ApplyUniforms(m, e.mat);
            return e.mat;
        }

        private static long Mix(long h, double v) => unchecked(h * 1000003L ^ BitConverter.DoubleToInt64Bits(v));
        private static long MixC(long h, Color c) => Mix(Mix(Mix(h, c.r), c.g), c.b);

        private static long Hash(THREE.Material m)
        {
            long h = 17;
            h = Mix(h, (int)m.side); h = Mix(h, m.transparent ? 1 : 0); h = Mix(h, m.opacity); h = Mix(h, m.depthWrite ? 1 : 0);
            h = Mix(h, m.depthTest ? 1 : 0); h = Mix(h, (int)m.blending); h = Mix(h, m.alphaTest); h = Mix(h, m.toneMapped ? 1 : 0);
            h = Mix(h, m.fog ? 1 : 0); h = Mix(h, m.vertexColors ? 1 : 0); h = Mix(h, m.colorWrite ? 1 : 0); h = Mix(h, m.polygonOffset ? 1 : 0);
            h = Mix(h, m.polygonOffsetFactor); h = Mix(h, m.polygonOffsetUnits); h = Mix(h, m.visible ? 1 : 0); h = Mix(h, m.version);
            h = Mix(h, m.shadowSide.HasValue ? (int)m.shadowSide.Value : -1);
            switch (m)
            {
                case MeshStandardMaterial s:
                    h = MixC(h, s.color); h = Mix(h, s.roughness); h = Mix(h, s.metalness); h = MixC(h, s.emissive); h = Mix(h, s.emissiveIntensity);
                    h = Mix(h, s.flatShading ? 1 : 0); h = Mix(h, s.envMapIntensity); h = Mix(h, s.bumpScale);
                    h = MixTex(h, s.map); h = MixTex(h, s.emissiveMap); h = MixTex(h, s.alphaMap); h = MixTex(h, s.bumpMap); h = MixTex(h, s.roughnessMap);
                    if (s is MeshPhysicalMaterial p) { h = Mix(h, p.transmission); h = Mix(h, p.clearcoat); h = Mix(h, p.ior); }
                    break;
                case MeshLambertMaterial l:
                    h = MixC(h, l.color); h = MixC(h, l.emissive); h = Mix(h, l.emissiveIntensity); h = Mix(h, l.flatShading ? 1 : 0);
                    h = MixTex(h, l.map); h = MixTex(h, l.emissiveMap); h = MixTex(h, l.alphaMap);
                    break;
                case MeshBasicMaterial b:
                    h = MixC(h, b.color); h = MixTex(h, b.map); h = MixTex(h, b.alphaMap);
                    break;
                case SpriteMaterial sp:
                    h = MixC(h, sp.color); h = MixTex(h, sp.map); h = Mix(h, sp.rotation); h = Mix(h, sp.sizeAttenuation ? 1 : 0);
                    break;
                case PointsMaterial pm:
                    h = MixC(h, pm.color); h = MixTex(h, pm.map); h = Mix(h, pm.size); h = Mix(h, pm.sizeAttenuation ? 1 : 0);
                    break;
                case LineBasicMaterial lb:
                    h = MixC(h, lb.color);
                    break;
            }
            return h;
        }

        private static long MixTex(long h, THREE.Texture t)
        {
            if (t == null) return Mix(h, -1);
            h = Mix(h, t.id);
            h = Mix(h, t.version);
            h = Mix(h, t.repeat.x); h = Mix(h, t.repeat.y); h = Mix(h, t.offset.x); h = Mix(h, t.offset.y); h = Mix(h, t.rotation);
            h = Mix(h, (int)t.wrapS); h = Mix(h, (int)t.wrapT);
            return h;
        }

        private static readonly int _Color = Shader.PropertyToID("_Color"), _Emissive = Shader.PropertyToID("_Emissive"),
            _Rough = Shader.PropertyToID("_Rough"), _Metal = Shader.PropertyToID("_Metal"), _MainTex = Shader.PropertyToID("_MainTex"),
            _MainTex_ST = Shader.PropertyToID("_MainTex_ST"), _Flags = Shader.PropertyToID("_Flags"), _Flags2 = Shader.PropertyToID("_Flags2"),
            _AlphaMap = Shader.PropertyToID("_AlphaMap"), _EmissiveMap = Shader.PropertyToID("_EmissiveMap"), _BumpMap = Shader.PropertyToID("_BumpMap"),
            _RoughMap = Shader.PropertyToID("_RoughMap"), _Params = Shader.PropertyToID("_Params"), _Cull = Shader.PropertyToID("_Cull"),
            _ShadowCull = Shader.PropertyToID("_ShadowCull"), _ZWrite = Shader.PropertyToID("_ZWrite"), _ZTest = Shader.PropertyToID("_ZTest"),
            _SrcBlend = Shader.PropertyToID("_SrcBlend"), _DstBlend = Shader.PropertyToID("_DstBlend"), _SrcBlendA = Shader.PropertyToID("_SrcBlendA"),
            _DstBlendA = Shader.PropertyToID("_DstBlendA"), _ColorMask = Shader.PropertyToID("_ColorMask"), _OffsetFactor = Shader.PropertyToID("_OffsetFactor"),
            _OffsetUnits = Shader.PropertyToID("_OffsetUnits");

        private static U.Vector4 ST(THREE.Texture t) => t == null ? new U.Vector4(1, 1, 0, 0) : new U.Vector4((float)t.repeat.x, (float)t.repeat.y, (float)t.offset.x, (float)t.offset.y);

        private static void Apply(THREE.Material m, UnityEngine.Material u, Kind kind)
        {
            // render state
            var cull = m.side == Side.DoubleSide ? CullMode.Off : m.side == Side.BackSide ? CullMode.Front : CullMode.Back;
            if (kind == Kind.Sprite || kind == Kind.Points || kind == Kind.Line) cull = CullMode.Off;
            u.SetFloat(_Cull, (float)cull);
            var ss = m.shadowSide ?? (m.side == Side.FrontSide ? Side.BackSide : m.side == Side.BackSide ? Side.FrontSide : Side.DoubleSide);
            u.SetFloat(_ShadowCull, (float)(ss == Side.DoubleSide ? CullMode.Off : ss == Side.BackSide ? CullMode.Front : CullMode.Back));
            u.SetFloat(_ZWrite, m.depthWrite ? 1 : 0);
            u.SetFloat(_ZTest, (float)(m.depthTest ? CompareFunction.LessEqual : CompareFunction.Always));
            BlendMode src = BlendMode.One, dst = BlendMode.Zero, srcA = BlendMode.One, dstA = BlendMode.Zero;
            var blending = m.blending;
            if (blending == Blending.NormalBlending && !m.transparent) blending = Blending.NoBlending;
            switch (blending)
            {
                case Blending.NormalBlending:
                    src = BlendMode.SrcAlpha; dst = BlendMode.OneMinusSrcAlpha; srcA = BlendMode.One; dstA = BlendMode.OneMinusSrcAlpha; break;
                case Blending.AdditiveBlending:
                    src = BlendMode.SrcAlpha; dst = BlendMode.One; srcA = BlendMode.SrcAlpha; dstA = BlendMode.One; break;
                case Blending.SubtractiveBlending:
                    src = BlendMode.Zero; dst = BlendMode.OneMinusSrcColor; srcA = BlendMode.Zero; dstA = BlendMode.One; break;
                case Blending.MultiplyBlending:
                    src = BlendMode.Zero; dst = BlendMode.SrcColor; srcA = BlendMode.Zero; dstA = BlendMode.SrcAlpha; break;
            }
            u.SetFloat(_SrcBlend, (float)src); u.SetFloat(_DstBlend, (float)dst);
            u.SetFloat(_SrcBlendA, (float)srcA); u.SetFloat(_DstBlendA, (float)dstA);
            u.SetFloat(_ColorMask, m.colorWrite && m.visible ? 15 : 0);
            u.SetFloat(_OffsetFactor, m.polygonOffset ? (float)m.polygonOffsetFactor : 0);
            u.SetFloat(_OffsetUnits, m.polygonOffset ? (float)m.polygonOffsetUnits : 0);
            u.renderQueue = m.transparent ? (int)RenderQueue.Transparent : m.alphaTest > 0 ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;

            // _Flags: x vertex colours (+1) and baked instance colours (+2), y map, z alpha map, w alpha test threshold
            // _Flags2: x fog, y tone mapped, z flat shading, w lit model (0 unlit, 1 standard, 2 lambert)
            float fog = m.fog ? 1 : 0, tone = m.toneMapped ? 1 : 0;
            THREE.Texture map = null, alphaMap = null, emissiveMap = null, bumpMap = null, roughMap = null;
            Color color = null, emissive = null;
            double emissiveIntensity = 1, rough = 1, metal = 0, envI = 1, bumpScale = 1, sizeOrRot = 0, sizeAtt = 1;
            bool flat = false;
            float model = 0;
            switch (m)
            {
                case MeshStandardMaterial s:
                    color = s.color; emissive = s.emissive; emissiveIntensity = s.emissiveIntensity; rough = s.roughness; metal = s.metalness;
                    map = s.map; alphaMap = s.alphaMap; emissiveMap = s.emissiveMap; bumpMap = s.bumpMap; roughMap = s.roughnessMap;
                    flat = s.flatShading; envI = s.envMapIntensity; bumpScale = s.bumpScale; model = 1;
                    sizeOrRot = s.roughnessMap != null ? 1 : 0;
                    break;
                case MeshLambertMaterial l:
                    color = l.color; emissive = l.emissive; emissiveIntensity = l.emissiveIntensity; map = l.map; alphaMap = l.alphaMap;
                    emissiveMap = l.emissiveMap; flat = l.flatShading; model = 2;
                    break;
                case MeshBasicMaterial b:
                    color = b.color; map = b.map; alphaMap = b.alphaMap;
                    break;
                case SpriteMaterial sp:
                    color = sp.color; map = sp.map; alphaMap = sp.alphaMap; sizeOrRot = sp.rotation; sizeAtt = sp.sizeAttenuation ? 1 : 0;
                    break;
                case PointsMaterial pm:
                    color = pm.color; map = pm.map; alphaMap = pm.alphaMap; sizeOrRot = pm.size; sizeAtt = pm.sizeAttenuation ? 1 : 0;
                    break;
                case LineBasicMaterial lb:
                    color = lb.color;
                    break;
            }
            if (!m.lights) model = 0;
            var c = color ?? new Color(0xffffff);
            u.SetVector(_Color, new U.Vector4((float)c.r, (float)c.g, (float)c.b, (float)m.opacity));
            var e = emissive ?? new Color(0x000000);
            u.SetVector(_Emissive, new U.Vector4((float)(e.r * emissiveIntensity), (float)(e.g * emissiveIntensity), (float)(e.b * emissiveIntensity), emissiveMap != null ? 1 : 0));
            u.SetFloat(_Rough, (float)rough);
            u.SetFloat(_Metal, (float)metal);
            u.SetVector(_Flags, new U.Vector4((m.vertexColors ? 1 : 0) + (kind == Kind.Instanced ? 2 : 0), map != null ? 1 : 0, alphaMap != null ? 1 : 0, (float)m.alphaTest));
            u.SetVector(_Flags2, new U.Vector4(fog, tone, flat ? 1 : 0, model));
            // _Params: x envMapIntensity, y bump scale (0 = none), z size / rotation, w size attenuation
            u.SetVector(_Params, new U.Vector4((float)envI, bumpMap != null ? (float)bumpScale : 0, (float)sizeOrRot, (float)sizeAtt));
            var mainTex = TextureCache.Get(map);
            u.SetTexture(_MainTex, mainTex != null ? mainTex : Texture2D.whiteTexture);
            u.SetVector(_MainTex_ST, ST(map ?? alphaMap));
            var at = TextureCache.Get(alphaMap);
            u.SetTexture(_AlphaMap, at != null ? at : Texture2D.whiteTexture);
            var et = TextureCache.Get(emissiveMap);
            u.SetTexture(_EmissiveMap, et != null ? et : Texture2D.whiteTexture);
            var bt = TextureCache.Get(bumpMap);
            u.SetTexture(_BumpMap, bt != null ? bt : Texture2D.blackTexture);
            var rt = TextureCache.Get(roughMap);
            u.SetTexture(_RoughMap, rt != null ? rt : Texture2D.whiteTexture);
            if (m is MeshPhysicalMaterial ph && ph.transmission > 0)
            {
                // no screen-space refraction here: glass shows as a clear, glossy see-through surface
                u.SetVector(_Color, new U.Vector4((float)c.r, (float)c.g, (float)c.b, (float)(m.opacity * (1 - ph.transmission * 0.75))));
                if (!m.transparent)
                {
                    u.SetFloat(_SrcBlend, (float)BlendMode.SrcAlpha); u.SetFloat(_DstBlend, (float)BlendMode.OneMinusSrcAlpha);
                    u.SetFloat(_SrcBlendA, (float)BlendMode.One); u.SetFloat(_DstBlendA, (float)BlendMode.OneMinusSrcAlpha);
                    u.renderQueue = (int)RenderQueue.Transparent;
                }
            }
        }

        private static void ApplyUniforms(THREE.Material m, UnityEngine.Material u)
        {
            if (m is ShaderMaterial sm) foreach (var kv in sm.uniforms) SetUniform(u, kv.Key, kv.Value?.value);
            foreach (var kv in m.customUniforms) SetUniform(u, kv.Key, kv.Value?.value);
        }

        /// <summary>Uniform values by name. Vector3s are positions or directions, so they arrive in Unity's axes (z negated).</summary>
        public static void SetUniform(UnityEngine.Material u, string name, object v)
        {
            switch (v)
            {
                case null: return;
                case double d: u.SetFloat(name, (float)d); break;
                case float f: u.SetFloat(name, f); break;
                case int i: u.SetFloat(name, i); break;
                case bool b: u.SetFloat(name, b ? 1 : 0); break;
                case Color c: u.SetVector(name, new U.Vector4((float)c.r, (float)c.g, (float)c.b, 1)); break;
                case Vector2 v2: u.SetVector(name, new U.Vector4((float)v2.x, (float)v2.y, 0, 0)); break;
                case Vector3 v3: u.SetVector(name, new U.Vector4((float)v3.x, (float)v3.y, (float)-v3.z, 0)); break;
                case Vector4 v4: u.SetVector(name, new U.Vector4((float)v4.x, (float)v4.y, (float)v4.z, (float)v4.w)); break;
                case Matrix4 m4: u.SetMatrix(name, m4.ToUnity()); break;
                case THREE.Texture t:
                {
                    var tex = TextureCache.Get(t);
                    if (tex != null) u.SetTexture(name, tex);
                    u.SetVector(name + "_ST", ST(t));
                    break;
                }
                case double[] arr:
                {
                    var fa = new float[arr.Length];
                    for (int k = 0; k < arr.Length; k++) fa[k] = (float)arr[k];
                    u.SetFloatArray(name, fa);
                    break;
                }
            }
        }
    }
}
