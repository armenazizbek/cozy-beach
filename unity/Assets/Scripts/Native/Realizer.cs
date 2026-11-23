using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

/// <summary>
/// Turns a built description of part of the world into native Unity objects (unity/NATIVE_PLAN.md 2.11, S5).
///
/// The game's builders describe what they make as a tree of plain C# objects: groups, meshes, sprites, points, lines,
/// instanced and skinned meshes, point lights, each with its local transform in game numbers, its geometry and its material
/// description. That tree is the plan's "Proto": it can be made off the main thread (Prebuilt), and it is the same tree
/// the Three bridge used to copy into Unity every frame (its classes move under the native kit by a rename in S25).
/// The Realizer copies it once, on the main thread, into GameObjects with MeshKit meshes, MaterialKit materials and the
/// bridge's renderer settings, and hands back the map from each description to its Transform, for the code that moves
/// things afterwards. <see cref="RealizeSliced"/> spreads the work over frames within a time budget.
///
/// Realized objects are the game's own from then on: the module that placed them keeps them in line with their
/// descriptions (Realized.Follow and Apply, or a LiveTree).
/// </summary>
public sealed class Realized
{
    public readonly Dictionary<THREE.Object3D, Transform> map = new Dictionary<THREE.Object3D, Transform>();
    public Transform root;

    /// <summary>The Transform made for a description (null if it was not part of this tree).</summary>
    public Transform this[THREE.Object3D o] => o != null && map.TryGetValue(o, out var t) ? t : null;

    public Renderer RendererOf(THREE.Object3D o) => this[o]?.GetComponent<Renderer>();

    public Light LightOf(THREE.Object3D o) => this[o]?.GetComponent<Light>();

    /// <summary>A followed part, with the local pose last copied onto it (copied again only when it changed).</summary>
    private sealed class Part
    {
        public THREE.Object3D o;
        public Transform t;
        public bool posed;
        /// <summary>the visibility and the description's parent last applied (Unity is asked only when they change)</summary>
        public int shown = -1;
        public THREE.Object3D parent;
        public double px, py, pz, qx, qy, qz, qw, sx, sy, sz;
    }

    private readonly List<Part> followed = new List<Part>();

    /// <summary>
    /// Names the parts of a placed module that move or show and hide while it runs: <see cref="Apply"/> copies their pose and
    /// visibility from the description onto their GameObjects. Only these are touched each frame, never the whole tree.
    /// </summary>
    public Realized Follow(params THREE.Object3D[] parts) => Follow((IEnumerable<THREE.Object3D>)parts);

    public Realized Follow(IEnumerable<THREE.Object3D> parts)
    {
        foreach (var o in parts)
        {
            var t = this[o];
            if (t != null) followed.Add(new Part { o = o, t = t });
        }
        return this;
    }

    /// <summary>Forgets what was last applied (something else switched GameObjects on or off): the next Apply sets it all.</summary>
    public void Invalidate()
    {
        foreach (var part in followed)
        {
            part.shown = -1;
            part.parent = null;
            part.posed = false;
        }
    }

    /// <summary>Copies every followed part's pose and visibility onto its GameObject (once per update of the module).</summary>
    public void Apply()
    {
        foreach (var part in followed)
        {
            var o = part.o;
            var t = part.t;
            if (t == null) continue;
            // a part moved to another parent in the description (a coconut falling from its palm) moves with it
            if (!ReferenceEquals(part.parent, o.parent))
            {
                part.parent = o.parent;
                var pt = this[o.parent];
                if (pt != null && !ReferenceEquals(t.parent, pt)) t.SetParent(pt, false);
            }
            int vis = o.visible ? 1 : 0;
            if (part.shown != vis)
            {
                part.shown = vis;
                if (t.gameObject.activeSelf != o.visible) t.gameObject.SetActive(o.visible);
            }
            if (!o.visible) continue;
            // the pose is copied when it changed since the last copy (a still part costs a comparison, not three Transform writes)
            if (!o.matrixAutoUpdate) Realizer.Pose(o, t);
            else
            {
                var p = o.position; var q = o.quaternion; var sc = o.scale;
                if (!part.posed || p.x != part.px || p.y != part.py || p.z != part.pz || q._x != part.qx || q._y != part.qy || q._z != part.qz || q._w != part.qw
                    || sc.x != part.sx || sc.y != part.sy || sc.z != part.sz)
                {
                    Realizer.Pose(o, t);
                    part.posed = true;
                    part.px = p.x; part.py = p.y; part.pz = p.z;
                    part.qx = q._x; part.qy = q._y; part.qz = q._z; part.qw = q._w;
                    part.sx = sc.x; part.sy = sc.y; part.sz = sc.z;
                }
            }
            // a point light also carries its colour, strength, reach and shadows (torches flicker, lamps are switched)
            if (o is THREE.PointLight pl)
            {
                var l = t.GetComponent<Light>();
                if (l == null) continue;
                Lights.Set(l, new Color((float)pl.color.r, (float)pl.color.g, (float)pl.color.b), (float)pl.intensity, t.gameObject.layer);
                float range = pl.distance > 0 ? (float)pl.distance : 1000f;
                if (l.range != range) l.range = range;
                Lights.Shadows(l, pl.castShadow);
            }
        }
    }
}

public static class Realizer
{
    /// <summary>Default main-thread budget per frame while loading (S1 measured ~2.3 us per object: about 1,700 objects a frame).</summary>
    public const float BUDGET_MS = 4f;

    /// <summary>Realizes a whole tree now. `layer` is the Unity layer of the scene it belongs to.</summary>
    public static Realized Realize(THREE.Object3D root, Transform parent, int layer) => Realize(root, parent, layer, true);

    /// <summary>`claim` false leaves the descriptions unmarked (Object3D.native stays null), for trees whose parts may move to another scene.</summary>
    internal static Realized Realize(THREE.Object3D root, Transform parent, int layer, bool claim)
    {
        var job = new Job(root, parent, layer) { claim = claim };
        while (job.Step(float.PositiveInfinity)) { }
        return job.result;
    }

    /// <summary>Realizes a tree a slice at a time, `budgetMs` of main-thread time per frame.</summary>
    public static IEnumerator RealizeSliced(THREE.Object3D root, Transform parent, int layer, Action<Realized> done, float budgetMs = BUDGET_MS)
    {
        var job = new Job(root, parent, layer);
        while (job.Step(budgetMs)) yield return null;
        done?.Invoke(job.result);
    }

    /// <summary>The Unity mesh made for each geometry, so geometry shared by several objects is one mesh.</summary>
    private static readonly Dictionary<THREE.BufferGeometry, Mesh> meshes = new Dictionary<THREE.BufferGeometry, Mesh>();
    private static readonly Dictionary<(THREE.BufferGeometry, bool), Mesh> groupMeshes = new Dictionary<(THREE.BufferGeometry, bool), Mesh>();

    /// <summary>Forgets shared meshes (a reload destroys them with the scene's objects).</summary>
    public static void Clear()
    {
        meshes.Clear();
        groupMeshes.Clear();
    }

    private static readonly int _SpriteCenter = Shader.PropertyToID("_SpriteCenter");

    private sealed class Job
    {
        public readonly Realized result = new Realized();
        private readonly Stack<(THREE.Object3D o, Transform parent)> todo = new Stack<(THREE.Object3D, Transform)>();
        private readonly List<THREE.SkinnedMesh> skinned = new List<THREE.SkinnedMesh>();
        private readonly int layer;
        public bool claim = true;

        public Job(THREE.Object3D root, Transform parent, int layer)
        {
            this.layer = layer;
            todo.Push((root, parent));
        }

        /// <summary>Makes objects until the budget is spent; false when the whole tree is done.</summary>
        public bool Step(float budgetMs)
        {
            var sw = Stopwatch.StartNew();
            while (todo.Count > 0)
            {
                var (o, parent) = todo.Pop();
                var t = Make(o, parent);
                if (result.root == null) result.root = t;
                // children in their order (the stack takes them last first)
                for (int i = o.children.Count - 1; i >= 0; i--)
                    if (!(o.children[i] is THREE.Camera)) todo.Push((o.children[i], t));
                if (sw.Elapsed.TotalMilliseconds > budgetMs) return todo.Count > 0 || skinned.Count > 0;
            }
            // skinned meshes last: their bones must all exist
            foreach (var sk in skinned) Skin(sk);
            skinned.Clear();
            return false;
        }

        private Transform Make(THREE.Object3D o, Transform parent)
        {
            var go = new GameObject(Draw.Label(o.name, o.type));
            var t = go.transform;
            t.SetParent(parent, false);
            go.layer = layer;
            Pose(o, t);
            result.map[o] = t;
            if (claim) o.native = t;
            switch (o)
            {
                case THREE.InstancedMesh im: Instanced(im, go); break;
                case THREE.SkinnedMesh sk: skinned.Add(sk); break;
                case THREE.Mesh m: Plain(m, go); break;
                case THREE.Points p: Drawn(go, p, MeshKit.PointQuads(p.geometry), p.material, MaterialKit.On.Points); break;
                case THREE.Line l: Drawn(go, l, MeshKit.ToLines(l.geometry, l is THREE.LineSegments), l.material, MaterialKit.On.Line, shadows: false); break;
                case THREE.Sprite s: Sprite(s, go); break;
                case THREE.PointLight pl: PointLight(pl, go); break;
            }
            if (!o.visible) go.SetActive(false);
            return t;
        }

        private void Plain(THREE.Mesh m, GameObject go)
        {
            bool multi = m.isMultiMaterial && m.geometry.groups.Count > 0;
            var mesh = MeshFor(m.geometry, multi);
            if (multi)
            {
                var mats = new Material[m.geometry.groups.Count];
                bool any = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var d = m.MaterialFor(m.geometry.groups[i].materialIndex);
                    mats[i] = MaterialKit.Draws(d) ? MaterialKit.Get(d) : Invisible;
                    any |= MaterialKit.Draws(d);
                }
                if (!any) return;
                var r = Renderer(go, mesh, null, m);
                r.sharedMaterials = mats;
                return;
            }
            if (!MaterialKit.Draws(m.material)) return;
            Renderer(go, mesh, MaterialKit.Get(m.material), m);
        }

        private void Instanced(THREE.InstancedMesh im, GameObject go)
        {
            if (!MaterialKit.Draws(im.material)) return;
            THREE.BufferAttribute inst = null;
            foreach (var name in MeshKit.ExtraNames(im.geometry)) if (im.geometry.attributes[name].isInstanced) { inst = im.geometry.attributes[name]; break; }
            int count = Math.Min(im.count, im.instanceMatrix.count);
            var mesh = MeshKit.BakeInstances(im.geometry, im.instanceMatrix.array, count, im.instanceColor?.array, inst);
            Renderer(go, mesh, MaterialKit.Get(im.material, MaterialKit.On.Instanced), im);
        }

        private void Drawn(GameObject go, THREE.Object3D o, Mesh mesh, THREE.Material m, MaterialKit.On on, bool shadows = true)
        {
            if (!MaterialKit.Draws(m)) return;
            var r = Renderer(go, mesh, MaterialKit.Get(m, on), o, shadows);
        }

        private void Sprite(THREE.Sprite s, GameObject go)
        {
            if (!MaterialKit.Draws(s.material)) return;
            var r = Renderer(go, MeshFor(s.geometry, false), MaterialKit.Get(s.material, MaterialKit.On.Sprite), s, shadows: false);
            // the quad turns to face the camera in the shader, so its own bounds are too small
            r.localBounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            if (s.center.x != 0.5 || s.center.y != 0.5)
            {
                var block = new MaterialPropertyBlock();
                block.SetVector(_SpriteCenter, new Vector4((float)s.center.x, (float)s.center.y, 0, 0));
                r.SetPropertyBlock(block);
            }
        }

        private void PointLight(THREE.PointLight p, GameObject go)
        {
            var l = go.AddComponent<Light>();
            l.renderMode = LightRenderMode.ForcePixel;
            l.shadowBias = 0.02f;
            l.shadowNormalBias = 0.4f;
            l.type = LightType.Point;
            Lights.Set(l, new Color((float)p.color.r, (float)p.color.g, (float)p.color.b), (float)p.intensity, layer);
            l.range = p.distance > 0 ? (float)p.distance : 1000f;
            Lights.Shadows(l, p.castShadow);
        }

        private void Skin(THREE.SkinnedMesh sk)
        {
            var go = result[sk].gameObject;
            if (!MaterialKit.Draws(sk.material)) return;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            Draw.Setup(smr);
            smr.updateWhenOffscreen = true;
            var mesh = MeshKit.ToMesh(sk.geometry);
            if (sk.skeleton != null)
            {
                var sb = sk.skeleton.bones;
                var bones = new Transform[sb.Length];
                var binds = new Matrix4x4[sb.Length];
                for (int i = 0; i < sb.Length; i++)
                {
                    bones[i] = result[sb[i]];
                    binds[i] = sk.skeleton.boneInverses[i].clone().multiply(sk.bindMatrix).ToUnityRaw();
                }
                mesh.bindposes = binds;
                smr.bones = bones;
                smr.rootBone = go.transform;
            }
            smr.sharedMesh = mesh;
            smr.sharedMaterial = MaterialKit.Get(sk.material);
            // the dog has always received shadows: the bridge left Unity's default (on) whatever the description said, and the
            // look the rewrite keeps includes it
            smr.receiveShadows = true;
            smr.sortingOrder = Mathf.Clamp(sk.renderOrder, -32768, 32767);
            ShadowPolicy.Track(smr, sk.castShadow);
        }

        private MeshRenderer Renderer(GameObject go, Mesh mesh, Material mat, THREE.Object3D o, bool shadows = true)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            Draw.Setup(r);
            if (mat != null) r.sharedMaterial = mat;
            r.sortingOrder = Mathf.Clamp(o.renderOrder, -32768, 32767);
            if (!o.frustumCulled) r.localBounds = Draw.Huge;
            if (shadows)
            {
                r.receiveShadows = o.receiveShadow;
                ShadowPolicy.Track(r, o.castShadow);
            }
            return r;
        }
    }

    private static Mesh MeshFor(THREE.BufferGeometry g, bool groups)
    {
        if (groups)
        {
            if (!groupMeshes.TryGetValue((g, true), out var gm) || gm == null) groupMeshes[(g, true)] = gm = MeshKit.ToMesh(g, groups: true);
            return gm;
        }
        if (!meshes.TryGetValue(g, out var m) || m == null) meshes[g] = m = MeshKit.ToMesh(g);
        return m;
    }

    /// <summary>A Three object's local transform, in game numbers, onto a Transform under the world root.</summary>
    public static void Pose(THREE.Object3D o, Transform t)
    {
        if (o.matrixAutoUpdate)
        {
            t.localPosition = new Vector3((float)o.position.x, (float)o.position.y, (float)o.position.z);
            t.localRotation = Normalised(o.quaternion._x, o.quaternion._y, o.quaternion._z, o.quaternion._w);
            t.localScale = new Vector3((float)o.scale.x, (float)o.scale.y, (float)o.scale.z);
        }
        else
        {
            var p = new THREE.Vector3(); var q = new THREE.Quaternion(); var s = new THREE.Vector3();
            o.matrix.decompose(p, q, s);
            t.localPosition = new Vector3((float)p.x, (float)p.y, (float)p.z);
            t.localRotation = Normalised(q._x, q._y, q._z, q._w);
            t.localScale = new Vector3((float)s.x, (float)s.y, (float)s.z);
        }
    }

    private static Quaternion Normalised(double x, double y, double z, double w)
    {
        var q = new Quaternion((float)x, (float)y, (float)z, (float)w);
        float len = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        return len > 1e-6f ? new Quaternion(q.x / len, q.y / len, q.z / len, q.w / len) : Quaternion.identity;
    }

    private static Material invisible;

    /// <summary>For a group of a multi-material mesh whose material is not visible: draws nothing (as the bridge's).</summary>
    private static Material Invisible
    {
        get
        {
            if (invisible == null)
            {
                invisible = new Material(THREE.Render.MaterialCache.FindShader("Cozy/Basic"));
                invisible.SetFloat("_ColorMask", 0);
                invisible.SetFloat("_ZWrite", 0);
                invisible.renderQueue = 3000;
            }
            return invisible;
        }
    }
}
