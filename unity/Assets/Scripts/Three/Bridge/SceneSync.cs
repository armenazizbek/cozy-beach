using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using U = UnityEngine;

namespace THREE.Bridge
{
    /// <summary>The Unity GameObject that stands for one Object3D.</summary>
    internal class Node
    {
        public GameObject go;
        public Transform tf;
        public Object3D owner;
        public MeshFilter mf;
        public MeshRenderer mr;
        public SkinnedMeshRenderer smr;
        public U.Light light;
        public U.Camera cam;
        public double px = double.NaN, py, pz, qx, qy, qz, qw, sx, sy, sz;
        public bool active = true;
        public int layer = -1;
        public int detachedFrame = -1;
        /// <summary>The scene load this node belongs to (SceneSync.generation); an older one points at destroyed GameObjects.</summary>
        public int gen = SceneSync.generation;
        /// <summary>The parent last given to tf, so the sync need not ask Unity every frame.</summary>
        public Transform parentTf;
        public double spriteCx = double.NaN, spriteCy = double.NaN;
        public U.Mesh lastMesh;
        public U.Material[] mats = new U.Material[0];
        public bool cast, receive;
        public int sortingOrder;
        public bool unculled;
        public Transform[] bones;
    }

    /// <summary>
    /// Mirrors a Three scene into Unity GameObjects. Called from WebGLRenderer.render each frame for the scene being drawn.
    /// </summary>
    internal static class SceneSync
    {
        private static readonly List<Node> detached = new List<Node>();
        private static readonly Dictionary<THREE.Scene, GameObject> roots = new Dictionary<THREE.Scene, GameObject>();
        private static GameObject limbo;
        private static int frame;
        /// <summary>Goes up on every reload (Clear); nodes from before then are made again.</summary>
        internal static int generation = 1;

        // lights gathered during a sync
        private static readonly List<(THREE.Light l, Node n)> lights = new List<(THREE.Light, Node)>();
        private static U.Vector4 hemiSky, hemiGround, ambient;
        private static U.Vector3 hemiUp;

        public static readonly int _CozyFogColor = Shader.PropertyToID("_CozyFogColor"), _CozyFogParams = Shader.PropertyToID("_CozyFogParams"),
            _CozyTone = Shader.PropertyToID("_CozyTone"), _CozyHemiSky = Shader.PropertyToID("_CozyHemiSky"), _CozyHemiGround = Shader.PropertyToID("_CozyHemiGround"),
            _CozyHemiUp = Shader.PropertyToID("_CozyHemiUp"), _CozyAmbient = Shader.PropertyToID("_CozyAmbient"), _CozyEnv = Shader.PropertyToID("_CozyEnv"),
            _CozyScreen = Shader.PropertyToID("_CozyScreen");

        /// <summary>The environment light standing in for Three's RoomEnvironment: average radiance, how much brighter above, intensity.</summary>
        public static U.Vector4 EnvironmentTuning = new U.Vector4(1.8f, 0.45f, 1f, 0f);

        /// <summary>Forgets the scene that is about to be unloaded (see WebGLRenderer.Teardown).</summary>
        public static void Clear()
        {
            generation++;
            detached.Clear();
            roots.Clear();
            lights.Clear();
            if (limbo != null) Object.Destroy(limbo);
            limbo = null;
        }

        public static void Detach(Object3D o)
        {
            if (o.node == null) return;
            o.node.detachedFrame = frame;
            detached.Add(o.node);
            if (limbo == null)
            {
                limbo = new GameObject("(removed objects)");
                limbo.SetActive(false);
                Object.DontDestroyOnLoad(limbo);
            }
            o.node.tf.SetParent(limbo.transform, false);
            o.node.parentTf = limbo.transform;
        }

        public static GameObject RootFor(THREE.Scene s)
        {
            if (!roots.TryGetValue(s, out var go) || go == null)
            {
                go = new GameObject(string.IsNullOrEmpty(s.name) ? "Scene" : s.name);
                roots[s] = go;
            }
            return go;
        }

        private static Node Create(Object3D o, Transform parent)
        {
            var n = new Node { owner = o };
            string label = string.IsNullOrEmpty(o.name) ? o.type : o.name + " (" + o.type + ")";
            n.go = new GameObject(label);
            n.tf = n.go.transform;
            n.tf.SetParent(parent, false);
            n.parentTf = parent;
            o.node = n;
            return n;
        }

        public static void Sync(THREE.Scene scene, THREE.Camera camera, WebGLRenderer renderer)
        {
            frame++;
            MaterialCache.NextFrame();
            using (BridgeStats.Time("sync.matrices"))
            {
                scene.updateMatrixWorld();
                if (camera.parent == null) camera.updateMatrixWorld();
            }
            lights.Clear();
            hemiSky = hemiGround = ambient = U.Vector4.zero;
            hemiUp = U.Vector3.up;
            var root = RootFor(scene);
            if (scene.node == null || scene.node.gen != generation || !ReferenceEquals(scene.node.go, root))
            {
                scene.node = new Node { go = root, tf = root.transform };
            }
            int layer = scene.unityLayer;
            PushTransform(scene, scene.node);
            using (BridgeStats.Time("sync.objects")) foreach (var c in scene.children) SyncNode(c, scene.node.tf, layer, scene);
            using (BridgeStats.Time("sync.lights")) ApplyLights(scene);
            using (BridgeStats.Time("sync.globals")) ApplyGlobals(scene, renderer);
            using (BridgeStats.Time("sync.cleanup")) CleanUp();
        }

        private static void PushTransform(Object3D o, Node n)
        {
            double px, py, pz, qx, qy, qz, qw, sx, sy, sz;
            if (o.matrixAutoUpdate)
            {
                px = o.position.x; py = o.position.y; pz = o.position.z;
                qx = o.quaternion._x; qy = o.quaternion._y; qz = o.quaternion._z; qw = o.quaternion._w;
                sx = o.scale.x; sy = o.scale.y; sz = o.scale.z;
            }
            else
            {
                var p = new THREE.Vector3();
                var q = new THREE.Quaternion();
                var s = new THREE.Vector3();
                o.matrix.decompose(p, q, s);
                px = p.x; py = p.y; pz = p.z; qx = q._x; qy = q._y; qz = q._z; qw = q._w; sx = s.x; sy = s.y; sz = s.z;
            }
            if (px != n.px || py != n.py || pz != n.pz)
            {
                n.px = px; n.py = py; n.pz = pz;
                n.tf.localPosition = new U.Vector3((float)px, (float)py, (float)-pz);
            }
            if (qx != n.qx || qy != n.qy || qz != n.qz || qw != n.qw)
            {
                n.qx = qx; n.qy = qy; n.qz = qz; n.qw = qw;
                var uq = new U.Quaternion((float)-qx, (float)-qy, (float)qz, (float)qw);
                float len = Mathf.Sqrt(uq.x * uq.x + uq.y * uq.y + uq.z * uq.z + uq.w * uq.w);
                if (len > 1e-6f) { uq.x /= len; uq.y /= len; uq.z /= len; uq.w /= len; }
                else uq = U.Quaternion.identity;
                n.tf.localRotation = uq;
            }
            if (sx != n.sx || sy != n.sy || sz != n.sz)
            {
                n.sx = sx; n.sy = sy; n.sz = sz;
                n.tf.localScale = new U.Vector3((float)sx, (float)sy, (float)sz);
            }
        }

        private static void SyncNode(Object3D o, Transform parentTf, int layer, THREE.Scene scene)
        {
            // an object kept from before a reload still points at its destroyed GameObject: give it a new one
            var n = o.node != null && o.node.gen == generation ? o.node : Create(o, parentTf);
            BridgeStats.objects++;
            if (o is THREE.Mesh || o is Points || o is Line || o is Sprite) BridgeStats.drawables++;
            if (n.detachedFrame >= 0) n.detachedFrame = -1;
            if (!ReferenceEquals(n.parentTf, parentTf))
            {
                n.tf.SetParent(parentTf, false);
                n.parentTf = parentTf;
            }
            bool vis = o.visible;
            if (n.active != vis)
            {
                n.active = vis;
                n.go.SetActive(vis);
            }
            if (!vis) return;
            if (n.layer != layer)
            {
                n.layer = layer;
                n.go.layer = layer;
            }
            PushTransform(o, n);
            switch (o)
            {
                case InstancedMesh im: SyncInstanced(im, n); break;
                case SkinnedMesh sk: SyncSkinned(sk, n); break;
                case THREE.Mesh m: SyncMesh(m, n); break;
                case Points p: SyncPoints(p, n); break;
                case Line l: SyncLine(l, n); break;
                case Sprite s: SyncSprite(s, n); break;
                case THREE.Light l: lights.Add((l, n)); break;
            }
            foreach (var c in o.children) SyncNode(c, n.tf, layer, scene);
        }

        private static void EnsureRenderer(Node n)
        {
            if (n.mr != null) return;
            n.mf = n.go.AddComponent<MeshFilter>();
            n.mr = n.go.AddComponent<MeshRenderer>();
            n.mr.shadowCastingMode = ShadowCastingMode.Off;
            n.mr.receiveShadows = false;
            n.mr.lightProbeUsage = LightProbeUsage.Off;
            n.mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            n.mr.allowOcclusionWhenDynamic = false;
        }

        /// <summary>The usual single-material case, without building an array every frame.</summary>
        private static void SetMaterial(Node n, Renderer r, U.Material mat)
        {
            if (n.mats.Length == 1 && ReferenceEquals(n.mats[0], mat)) return;
            n.mats = new[] { mat };
            r.sharedMaterials = n.mats;
        }

        private static void SetMaterials(Node n, Renderer r, U.Material[] mats)
        {
            bool same = n.mats.Length == mats.Length;
            if (same) for (int i = 0; i < mats.Length; i++) if (n.mats[i] != mats[i]) { same = false; break; }
            if (same) return;
            n.mats = mats;
            r.sharedMaterials = mats;
        }

        private static void SetShadowFlags(Node n, Renderer r, Object3D o)
        {
            if (n.cast != o.castShadow)
            {
                n.cast = o.castShadow;
                r.shadowCastingMode = o.castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            if (n.receive != o.receiveShadow)
            {
                n.receive = o.receiveShadow;
                r.receiveShadows = o.receiveShadow;
            }
            if (n.sortingOrder != o.renderOrder)
            {
                n.sortingOrder = o.renderOrder;
                r.sortingOrder = Mathf.Clamp(o.renderOrder, -32768, 32767);
            }
        }

        private static readonly Bounds Huge = new Bounds(U.Vector3.zero, U.Vector3.one * 100000f);

        private static void SetCulling(Node n, Renderer r, Object3D o, U.Mesh mesh)
        {
            bool unculled = !o.frustumCulled;
            if (unculled == n.unculled && mesh == n.lastMesh) return;
            n.unculled = unculled;
            if (unculled) r.localBounds = Huge;
            else if (mesh != null) r.localBounds = mesh.bounds;
        }

        private static void SyncMesh(THREE.Mesh m, Node n)
        {
            EnsureRenderer(n);
            bool multi = m.isMultiMaterial && m.geometry.groups.Count > 0;
            var mesh = MeshCache.Get(m.geometry, multi ? MeshCache.GROUPS : MeshCache.PLAIN);
            SetCulling(n, n.mr, m, mesh);
            if (n.lastMesh != mesh)
            {
                n.lastMesh = mesh;
                n.mf.sharedMesh = mesh;
            }
            if (multi)
            {
                // compare group by group first; a new array only when something changed
                int count = m.geometry.groups.Count;
                bool same = n.mats.Length == count;
                for (int i = 0; i < count && same; i++)
                {
                    var tm = m.MaterialFor(m.geometry.groups[i].materialIndex);
                    same = ReferenceEquals(n.mats[i], tm != null && tm.visible ? MaterialCache.Get(tm, MaterialCache.Kind.Mesh) : Invisible);
                }
                if (!same)
                {
                    var mats = new U.Material[count];
                    for (int i = 0; i < count; i++)
                    {
                        var tm = m.MaterialFor(m.geometry.groups[i].materialIndex);
                        mats[i] = tm != null && tm.visible ? MaterialCache.Get(tm, MaterialCache.Kind.Mesh) : Invisible;
                    }
                    SetMaterials(n, n.mr, mats);
                }
            }
            else
            {
                var tm = m.material;
                SetMaterial(n, n.mr, tm != null && tm.visible ? MaterialCache.Get(tm, MaterialCache.Kind.Mesh) : Invisible);
            }
            SetShadowFlags(n, n.mr, m);
        }

        private static U.Material _invisible;
        private static readonly MaterialPropertyBlock spriteBlock = new MaterialPropertyBlock();
        private static readonly int _SpriteCenter = Shader.PropertyToID("_SpriteCenter");

        /// <summary>For materials with visible = false: draws nothing.</summary>
        private static U.Material Invisible
        {
            get
            {
                if (_invisible == null)
                {
                    _invisible = new U.Material(MaterialCache.FindShader("Cozy/Basic"));
                    _invisible.SetFloat("_ColorMask", 0);
                    _invisible.SetFloat("_ZWrite", 0);
                    _invisible.renderQueue = 3000;
                }
                return _invisible;
            }
        }

        private static void SyncInstanced(InstancedMesh im, Node n)
        {
            EnsureRenderer(n);
            var mesh = MeshCache.GetBaked(im);
            SetCulling(n, n.mr, im, mesh);
            if (n.lastMesh != mesh)
            {
                n.lastMesh = mesh;
                n.mf.sharedMesh = mesh;
            }
            var tm = im.material;
            SetMaterial(n, n.mr, tm != null && tm.visible ? MaterialCache.Get(tm, MaterialCache.Kind.Instanced) : Invisible);
            SetShadowFlags(n, n.mr, im);
        }

        private static void SyncSkinned(SkinnedMesh sk, Node n)
        {
            if (n.smr == null)
            {
                n.smr = n.go.AddComponent<SkinnedMeshRenderer>();
                n.smr.updateWhenOffscreen = true;
                n.smr.lightProbeUsage = LightProbeUsage.Off;
                n.smr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            var mesh = MeshCache.Get(sk.geometry, MeshCache.PLAIN);
            if (sk.skeleton != null)
            {
                var sb = sk.skeleton.bones;
                bool ready = true, same = n.bones != null && n.bones.Length == sb.Length && ReferenceEquals(mesh, n.lastMesh);
                for (int i = 0; i < sb.Length; i++)
                {
                    if (sb[i].node == null) { ready = false; break; }
                    if (same && !ReferenceEquals(n.bones[i], sb[i].node.tf)) same = false;
                }
                if (ready && !same)
                {
                    var bones = new Transform[sb.Length];
                    for (int i = 0; i < bones.Length; i++) bones[i] = sb[i].node.tf;
                    var binds = new U.Matrix4x4[bones.Length];
                    for (int i = 0; i < bones.Length; i++)
                    {
                        // Unity wants bone-from-mesh in mesh space: boneInverse * bindMatrix, conjugated into Unity's axes
                        var bind = sk.skeleton.boneInverses[i].clone().multiply(sk.bindMatrix);
                        binds[i] = bind.ToUnity();
                    }
                    mesh.bindposes = binds;
                    n.smr.bones = bones;
                    n.bones = bones;
                    n.smr.rootBone = n.tf;
                }
            }
            if (n.lastMesh != mesh)
            {
                n.lastMesh = mesh;
                n.smr.sharedMesh = mesh;
            }
            var tm = sk.material;
            SetMaterial(n, n.smr, tm != null && tm.visible ? MaterialCache.Get(tm, MaterialCache.Kind.Mesh) : Invisible);
            SetShadowFlags(n, n.smr, sk);
        }

        private static void SyncPoints(Points p, Node n)
        {
            EnsureRenderer(n);
            var mesh = MeshCache.GetPoints(p);
            SetCulling(n, n.mr, p, mesh);
            if (n.lastMesh != mesh)
            {
                n.lastMesh = mesh;
                n.mf.sharedMesh = mesh;
            }
            var tm = p.material;
            SetMaterial(n, n.mr, tm != null && tm.visible ? MaterialCache.Get(tm, MaterialCache.Kind.Points) : Invisible);
            SetShadowFlags(n, n.mr, p);
        }

        private static void SyncLine(Line l, Node n)
        {
            EnsureRenderer(n);
            var mesh = MeshCache.Get(l.geometry, l is LineSegments ? MeshCache.LINES : MeshCache.LINESTRIP);
            SetCulling(n, n.mr, l, mesh);
            if (n.lastMesh != mesh)
            {
                n.lastMesh = mesh;
                n.mf.sharedMesh = mesh;
            }
            var tm = l.material;
            SetMaterial(n, n.mr, tm != null && tm.visible ? MaterialCache.Get(tm, MaterialCache.Kind.Line) : Invisible);
        }

        private static void SyncSprite(Sprite s, Node n)
        {
            EnsureRenderer(n);
            var mesh = MeshCache.Get(s.geometry, MeshCache.PLAIN);
            if (n.lastMesh != mesh)
            {
                n.lastMesh = mesh;
                n.mf.sharedMesh = mesh;
                // the quad turns to face the camera in the shader, so its own bounds are too small
                n.mr.localBounds = new Bounds(U.Vector3.zero, U.Vector3.one * 2f);
            }
            var tm = s.material;
            var mat = tm != null && tm.visible ? MaterialCache.Get(tm, MaterialCache.Kind.Sprite) : Invisible;
            bool newMat = !(n.mats.Length == 1 && ReferenceEquals(n.mats[0], mat));
            SetMaterial(n, n.mr, mat);
            if (n.sortingOrder != s.renderOrder)
            {
                n.sortingOrder = s.renderOrder;
                n.mr.sortingOrder = s.renderOrder;
            }
            if (mat != Invisible && (newMat || s.center.x != n.spriteCx || s.center.y != n.spriteCy))
            {
                n.spriteCx = s.center.x;
                n.spriteCy = s.center.y;
                spriteBlock.Clear();
                n.mr.GetPropertyBlock(spriteBlock);
                spriteBlock.SetVector(_SpriteCenter, new U.Vector4((float)s.center.x, (float)s.center.y, 0, 0));
                n.mr.SetPropertyBlock(spriteBlock);
            }
        }

        private static readonly THREE.Vector3 _a = new THREE.Vector3(), _b = new THREE.Vector3();

        private static void ApplyLights(THREE.Scene scene)
        {
            U.Light sun = null;
            double sunPower = -1;
            foreach (var (l, n) in lights)
            {
                switch (l)
                {
                    case HemisphereLight h:
                    {
                        hemiSky += new U.Vector4((float)(h.color.r * h.intensity), (float)(h.color.g * h.intensity), (float)(h.color.b * h.intensity), 0);
                        hemiGround += new U.Vector4((float)(h.groundColor.r * h.intensity), (float)(h.groundColor.g * h.intensity), (float)(h.groundColor.b * h.intensity), 0);
                        h.getWorldPosition(_a);
                        _a.normalize();
                        hemiUp = new U.Vector3((float)_a.x, (float)_a.y, (float)-_a.z);
                        break;
                    }
                    case AmbientLight a:
                        ambient += new U.Vector4((float)(a.color.r * a.intensity), (float)(a.color.g * a.intensity), (float)(a.color.b * a.intensity), 0);
                        break;
                    case DirectionalLight d:
                    {
                        var ul = EnsureLight(n, LightType.Directional);
                        d.getWorldPosition(_a);
                        d.target.updateWorldMatrix(true, false);
                        d.target.getWorldPosition(_b);
                        var dir = new U.Vector3((float)(_b.x - _a.x), (float)(_b.y - _a.y), (float)-(_b.z - _a.z));
                        if (dir.sqrMagnitude > 1e-9f) n.tf.rotation = U.Quaternion.LookRotation(dir.normalized);
                        SetLightCommon(ul, l);
                        ul.shadows = d.castShadow ? LightShadows.Soft : LightShadows.None;
                        if (d.castShadow && d.intensity > sunPower) { sun = ul; sunPower = d.intensity; }
                        else if (sun == null && d.intensity > sunPower) { sun = ul; sunPower = d.intensity; }
                        break;
                    }
                    case SpotLight s:
                    {
                        var ul = EnsureLight(n, LightType.Spot);
                        s.getWorldPosition(_a);
                        s.target.updateWorldMatrix(true, false);
                        s.target.getWorldPosition(_b);
                        var dir = new U.Vector3((float)(_b.x - _a.x), (float)(_b.y - _a.y), (float)-(_b.z - _a.z));
                        if (dir.sqrMagnitude > 1e-9f) n.tf.rotation = U.Quaternion.LookRotation(dir.normalized);
                        SetLightCommon(ul, l);
                        ul.range = s.distance > 0 ? (float)s.distance : 1000f;
                        float outer = (float)(s.angle * 2 * Mathf.Rad2Deg);
                        ul.spotAngle = Mathf.Clamp(outer, 1f, 179f);
                        ul.innerSpotAngle = Mathf.Clamp((float)(outer * (1 - s.penumbra)), 0f, ul.spotAngle);
                        ul.shadows = s.castShadow ? LightShadows.Soft : LightShadows.None;
                        break;
                    }
                    case PointLight p:
                    {
                        var ul = EnsureLight(n, LightType.Point);
                        SetLightCommon(ul, l);
                        ul.range = p.distance > 0 ? (float)p.distance : 1000f;
                        ul.shadows = p.castShadow ? LightShadows.Soft : LightShadows.None;
                        break;
                    }
                }
            }
            RenderSettings.sun = sun;
        }

        private static U.Light EnsureLight(Node n, LightType type)
        {
            if (n.light == null)
            {
                n.light = n.go.AddComponent<U.Light>();
#if UNITY_EDITOR
                // editor-only property (lights made at runtime are realtime anyway); without the guard player builds fail to compile
                n.light.lightmapBakeType = LightmapBakeType.Realtime;
#endif
                n.light.renderMode = LightRenderMode.ForcePixel;
                n.light.shadowBias = 0.02f;
                n.light.shadowNormalBias = 0.4f;
            }
            if (n.light.type != type) n.light.type = type;
            return n.light;
        }

        private static void SetLightCommon(U.Light ul, THREE.Light l)
        {
            // Unity linearises Light.color, so hand it the sRGB value; the shader then sees colour * intensity in linear, as Three does
            var c = l.color.ToUnitySRGB();
            if (ul.color != c) ul.color = c;
            float i = (float)l.intensity;
            if (ul.intensity != i) ul.intensity = i;
            ul.cullingMask = 1 << l.node.layer;
        }

        private static void ApplyGlobals(THREE.Scene scene, WebGLRenderer renderer)
        {
            Shader.SetGlobalVector(_CozyHemiSky, hemiSky);
            Shader.SetGlobalVector(_CozyHemiGround, hemiGround);
            Shader.SetGlobalVector(_CozyHemiUp, hemiUp);
            Shader.SetGlobalVector(_CozyAmbient, ambient);
            var env = EnvironmentTuning;
            env.z = scene.environment != null ? (float)scene.environmentIntensity : 0f;
            Shader.SetGlobalVector(_CozyEnv, env);
            if (scene.fog is FogExp2 f2)
            {
                var c = f2.color.ToUnitySRGB();
                Shader.SetGlobalVector(_CozyFogColor, new U.Vector4(c.r, c.g, c.b, 1));
                Shader.SetGlobalVector(_CozyFogParams, new U.Vector4(0, 0, (float)f2.density, 2));
            }
            else if (scene.fog != null)
            {
                var c = scene.fog.color.ToUnitySRGB();
                Shader.SetGlobalVector(_CozyFogColor, new U.Vector4(c.r, c.g, c.b, 1));
                Shader.SetGlobalVector(_CozyFogParams, new U.Vector4((float)scene.fog.near, (float)scene.fog.far, 0, 1));
            }
            else Shader.SetGlobalVector(_CozyFogParams, new U.Vector4(0, 0, 0, 0));
            Shader.SetGlobalVector(_CozyTone, new U.Vector4(renderer.toneMapping == ToneMapping.ACESFilmicToneMapping ? 1 : 0, (float)renderer.toneMappingExposure, 0, 0));
            Shader.SetGlobalVector(_CozyScreen, new U.Vector4(Screen.width, Screen.height, (float)renderer.getPixelRatio(), 0));
        }

        private static void CleanUp()
        {
            if (detached.Count == 0) return;
            for (int i = detached.Count - 1; i >= 0; i--)
            {
                var n = detached[i];
                if (n.detachedFrame < 0) { detached.RemoveAt(i); continue; }
                if (frame - n.detachedFrame < 240) continue;
                detached.RemoveAt(i);
                // still out of the scene after a few seconds: let the GameObjects go (a new node is made if it comes back)
                if (n.owner != null && n.owner.parent == null) Forget(n.owner);
                else if (n.owner != null) continue;
                if (n.go != null) Object.Destroy(n.go);
            }
        }

        /// <summary>Clears the Unity side of an Object3D tree whose GameObjects were destroyed.</summary>
        public static void Forget(Object3D o)
        {
            o.traverse(c =>
            {
                c.node = null;
                MeshCache.Release(c);
            });
        }
    }
}
