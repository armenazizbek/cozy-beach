using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Picking in the world with PhysX (unity/NATIVE_PLAN.md 2.9, S7): what the pointer or the screen centre is on, and how far
/// away the binoculars' range is. It answers what THREE.Raycaster.intersectObjects answered for the same description trees,
/// by the same rules:
/// <list type="bullet">
/// <item>every plain mesh gets a MeshCollider on the <see cref="Layer"/> layer, made from its description's own triangles
/// (draw range or material groups as Three walks them), posed from its matrixWorld after updating it as Mesh.raycast does,
/// so invisible hit spheres and hidden objects are hit just as before;</item>
/// <item>PhysX only finds which meshes a ray passes through; Three's own triangle test (Mesh.raycast) then gives the hits on
/// those few, so sides, material groups and the near and far limits follow Three's rules exactly;</item>
/// <item>points, lines, sprites, instanced and skinned meshes keep their exact CPU test (a handful of objects).</item>
/// </list>
/// Colliders of meshes no query has asked about
/// for a while are dropped.
/// </summary>
public static class Picking
{
    /// <summary>The Unity layer the pick colliders live on (no camera draws it; scenes take layers from 8 up).</summary>
    public const int Layer = 30;

    private sealed class Shape
    {
        public Mesh mesh;
        public int version;
        public bool multi;
        public Vector3[] verts;
        public int[] tris;
        /// <summary>per collider triangle: the material group it came from (-1: the mesh's one material)</summary>
        public int[] group;
    }

    private sealed class Entry
    {
        public THREE.Mesh desc;
        public GameObject go;
        public MeshCollider col;
        public Shape shape;
        public int stamp;
        public readonly double[] last = new double[16];
    }

    private static readonly Dictionary<THREE.Mesh, Entry> entries = new Dictionary<THREE.Mesh, Entry>();
    private static readonly Dictionary<Collider, Entry> byCollider = new Dictionary<Collider, Entry>();
    private static readonly Dictionary<(THREE.BufferGeometry, bool), Shape> shapes = new Dictionary<(THREE.BufferGeometry, bool), Shape>();
    private static readonly List<THREE.Object3D> others = new List<THREE.Object3D>();
    private static RaycastHit[] buf = new RaycastHit[256];
    private static Transform root;
    private static int stamp;

    /// <summary>Forgets every collider (a reload destroys them with the world).</summary>
    internal static void Clear()
    {
        entries.Clear();
        byCollider.Clear();
        shapes.Clear();
        if (root != null) UnityEngine.Object.Destroy(root.gameObject);
        root = null;
        drawnColliders.Clear();
        drawnOf.Clear();
        drawnPose.Clear();
        if (drawnRoot != null) UnityEngine.Object.Destroy(drawnRoot.gameObject);
        drawnRoot = null;
    }

    /// <summary>What intersectObject(o, true) answered: the hits on a tree, nearest first.</summary>
    public static List<THREE.Intersection> Intersect(THREE.Raycaster rc, THREE.Object3D o) => Intersect(rc, new[] { o });

    /// <summary>What intersectObjects(objs, true) answered: the hits on these trees, nearest first.</summary>
    public static List<THREE.Intersection> Intersect(THREE.Raycaster rc, IEnumerable<THREE.Object3D> objs)
    {
        var result = new List<THREE.Intersection>();
        if (World.Scene == null) return rc.intersectObjects(objs, true, result);
        stamp++;
        others.Clear();
        foreach (var o in objs) Collect(o, rc);
        if ((stamp & 255) == 0) Purge();
        Physics.SyncTransforms();

        // the colliders stand in Unity's axes (z turned), outside the mirrored world root: PhysX takes no negative scale
        var o0 = rc.ray.origin;
        var dir = rc.ray.direction;
        var uo = new Vector3((float)o0.x, (float)o0.y, -(float)o0.z);
        var ud = new Vector3((float)dir.x, (float)dir.y, -(float)dir.z).normalized;
        float far = double.IsPositiveInfinity(rc.far) ? Mathf.Infinity : (float)rc.far + 0.01f;

        // PhysX finds the meshes the ray passes through (either side of any face); Three's own triangle test then gives the
        // exact hits on just those, by Three's rules for sides, groups and the near and far limits
        bool backs = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        int n;
        try
        {
            while ((n = Physics.RaycastNonAlloc(uo, ud, buf, far, 1 << Layer, QueryTriggerInteraction.Ignore)) == buf.Length)
                buf = new RaycastHit[buf.Length * 2];
        }
        finally { Physics.queriesHitBackfaces = backs; }
        for (int i = 0; i < n; i++)
            if (byCollider.TryGetValue(buf[i].collider, out var e) && e.stamp == stamp) e.desc.raycast(rc, result);

        // the few objects with a test of their own
        foreach (var o in others) o.raycast(rc, result);
        result.Sort((a, b) => a.distance.CompareTo(b.distance));
        return result;
    }

    /// <summary>Gets the colliders of a tree ready, as Raycaster.Walk visits it.</summary>
    private static void Collect(THREE.Object3D o, THREE.Raycaster rc)
    {
        if (o.layers.test(rc.layers))
        {
            if (o.GetType() == typeof(THREE.Mesh)) Track((THREE.Mesh)o);
            else if (o is THREE.Mesh || o is THREE.Points || o is THREE.Line || o is THREE.Sprite) others.Add(o);
        }
        foreach (var c in o.children) Collect(c, rc);
    }

    private static void Track(THREE.Mesh m)
    {
        if (m.material == null || m.geometry == null) return;
        if (!entries.TryGetValue(m, out var e) || e.go == null)
        {
            if (root == null)
            {
                root = new GameObject("Pick").transform;
                root.gameObject.layer = Layer;
            }
            var go = new GameObject(string.IsNullOrEmpty(m.name) ? "pick" : m.name) { layer = Layer };
            go.transform.SetParent(root, false);
            e = new Entry { desc = m, go = go, col = go.AddComponent<MeshCollider>() };
            e.col.cookingOptions = MeshColliderCookingOptions.None;
            entries[m] = e;
            byCollider[e.col] = e;
            for (int i = 0; i < 16; i++) e.last[i] = double.NaN;
        }
        e.stamp = stamp;
        var s = ShapeOf(m.geometry, m.isMultiMaterial && m.geometry.groups.Count > 0);
        if (!ReferenceEquals(e.shape, s) || e.col.sharedMesh != s.mesh)
        {
            e.shape = s;
            e.col.sharedMesh = null;
            e.col.sharedMesh = s.mesh;
        }
        // Mesh.raycast brings its world matrix up to date first, and so do we
        m.updateWorldMatrix(true, false);
        var me = m.matrixWorld.elements;
        bool moved = false;
        for (int i = 0; i < 16; i++) if (e.last[i] != me[i]) { moved = true; e.last[i] = me[i]; }
        if (moved)
        {
            var p = new THREE.Vector3(); var q = new THREE.Quaternion(); var sc = new THREE.Vector3();
            m.matrixWorld.decompose(p, q, sc);
            var t = e.go.transform;
            // the game pose turned into Unity's axes (z mirrored: the position's z and the rotation's x and y flip)
            t.localPosition = new Vector3((float)p.x, (float)p.y, -(float)p.z);
            t.localRotation = new Quaternion(-(float)q._x, -(float)q._y, (float)q._z, (float)q._w).normalized;
            t.localScale = new Vector3((float)sc.x, (float)sc.y, (float)sc.z);
        }
    }

    /// <summary>The triangles Three would walk for a geometry, as a collider mesh (rebuilt when the geometry is rewritten).</summary>
    private static Shape ShapeOf(THREE.BufferGeometry g, bool multi)
    {
        int ver = g.ContentVersion();
        if (shapes.TryGetValue((g, multi), out var s) && s.mesh != null && s.version == ver) return s;
        s ??= new Shape();
        s.version = ver;
        s.multi = multi;
        var pos = g.attributes.position;
        int count = pos?.count ?? 0;
        var verts = new Vector3[count];
        for (int i = 0; i < count; i++) verts[i] = new Vector3((float)pos.getX(i), (float)pos.getY(i), -(float)pos.getZ(i));
        var idx = g.index;
        int total = pos == null ? 0 : idx != null ? idx.count : pos.count;
        var tris = new List<int>();
        var groups = new List<int>();
        void add(int i, int grp)
        {
            int a = idx != null ? (int)idx.array[i] : i, b = idx != null ? (int)idx.array[i + 1] : i + 1, c = idx != null ? (int)idx.array[i + 2] : i + 2;
            if (a >= count || b >= count || c >= count) return;
            // PhysX cannot take degenerate triangles; they could never be hit anyway
            if (Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).sqrMagnitude < 1e-20f) return;
            // z is mirrored, so the winding turns too
            tris.Add(a); tris.Add(c); tris.Add(b);
            groups.Add(grp);
        }
        if (multi)
        {
            foreach (var gr in g.groups)
            {
                int end = Math.Min(gr.start + gr.count, total);
                for (int i = Math.Max(0, gr.start); i + 2 < end; i += 3) add(i, gr.materialIndex);
            }
        }
        else
        {
            int st = Math.Max(0, g.drawRange.start);
            int end = (int)Math.Min(total, (long)g.drawRange.start + g.drawRange.count);
            for (int i = st; i + 2 < end; i += 3) add(i, -1);
        }
        if (s.mesh == null) s.mesh = new Mesh { name = "pick " + g.name };
        s.mesh.Clear();
        s.mesh.indexFormat = count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        s.mesh.vertices = verts;
        s.tris = tris.ToArray();
        s.group = groups.ToArray();
        s.verts = verts;
        s.mesh.SetTriangles(s.tris, 0, true);
        shapes[(g, multi)] = s;
        return s;
    }

    /// <summary>Drops the colliders of meshes the last 256 queries did not ask about (finds picked up, props replaced).</summary>
    private static void Purge()
    {
        List<THREE.Mesh> gone = null;
        foreach (var kv in entries)
            if (stamp - kv.Value.stamp > 256 || kv.Value.go == null) (gone ??= new List<THREE.Mesh>()).Add(kv.Key);
        if (gone == null) return;
        foreach (var m in gone)
        {
            var e = entries[m];
            entries.Remove(m);
            if (e.col != null) byCollider.Remove(e.col);
            if (e.go != null) UnityEngine.Object.Destroy(e.go);
        }
    }

    // ---------------------------------------------------------------- picking GameObjects (unity/UI_PLAN.md B1)

    /// <summary>The colliders made for drawn meshes, by renderer, and the other way round.</summary>
    private static readonly Dictionary<MeshRenderer, MeshCollider> drawnColliders = new Dictionary<MeshRenderer, MeshCollider>();
    private static readonly Dictionary<Collider, MeshRenderer> drawnOf = new Dictionary<Collider, MeshRenderer>();
    private static readonly List<MeshRenderer> drawnScratch = new List<MeshRenderer>();
    private static readonly HashSet<MeshRenderer> drawnWanted = new HashSet<MeshRenderer>();
    /// <summary>where each collider was last put, so one whose mesh has not moved is left alone (moving a mesh collider is costly)</summary>
    private static readonly Dictionary<MeshRenderer, Matrix4x4> drawnPose = new Dictionary<MeshRenderer, Matrix4x4>();
    private static Transform drawnRoot;

    /// <summary>
    /// How far along a ray (in the game's world) the nearest drawn mesh under `roots` is, past `near` metres; null when none
    /// is hit. It looks at the GameObjects themselves, with a collider on the <see cref="Layer"/> layer for each shown mesh
    /// (made on first use, following the renderer as it moves), so it needs no description. Hidden objects are not hit.
    /// </summary>
    public static double? NearestDrawn(Vector3 originGame, Vector3 dirGame, IEnumerable<Transform> roots, double near, double far = 2000)
    {
        var world = World.Root;
        if (world == null) return null;
        var origin = world.TransformPoint(originGame);
        var dir = world.TransformVector(dirGame).normalized; // TransformVector: the root mirrors z through its scale, which TransformDirection ignores
        var ray = new Ray(origin, dir);
        bool moved = false;
        drawnWanted.Clear();
        foreach (var r in roots)
        {
            if (r == null || !r.gameObject.activeInHierarchy) continue;
            r.GetComponentsInChildren(false, drawnScratch);
            foreach (var mr in drawnScratch)
            {
                if (!mr.enabled) continue;
                // only what the ray passes through its box can be hit: everything else needs no collider work at all (the
                // binoculars ask every few frames, and moving every shown mesh's collider each time made the view stutter)
                if (!mr.bounds.IntersectRay(ray, out float boxAt) || boxAt > far) continue;
                var mf = mr.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null || mesh.GetTopology(0) != MeshTopology.Triangles) continue;
                if (!drawnColliders.TryGetValue(mr, out var col) || col == null)
                {
                    // under the pickers' own root, not inside the game's objects (that would shift their children)
                    if (drawnRoot == null) drawnRoot = new GameObject("Picking (drawn)").transform;
                    var go = new GameObject("pick");
                    go.layer = Layer;
                    go.transform.SetParent(drawnRoot, false);
                    col = go.AddComponent<MeshCollider>();
                    col.sharedMesh = mesh;
                    drawnColliders[mr] = col;
                    drawnOf[col] = mr;
                }
                else if (col.sharedMesh != mesh)
                {
                    col.sharedMesh = mesh;
                    drawnPose.Remove(mr);
                }
                // posed as the renderer is now, when it has moved since
                var src = mr.transform;
                var pose = src.localToWorldMatrix;
                if (!drawnPose.TryGetValue(mr, out var was) || was != pose)
                {
                    col.transform.SetPositionAndRotation(src.position, src.rotation);
                    col.transform.localScale = src.lossyScale;
                    drawnPose[mr] = pose;
                    moved = true;
                }
                drawnWanted.Add(mr);
            }
        }
        // colliders that were just moved count for the ray only once physics has taken them in
        if (moved) Physics.SyncTransforms();
        // many of the game's surfaces are drawn from both sides (Three's DoubleSide), so the ray takes back faces too
        bool backs = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        int n = Physics.RaycastNonAlloc(ray, buf, (float)far, 1 << Layer, QueryTriggerInteraction.Collide);
        Physics.queriesHitBackfaces = backs;
        double best = double.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            if (!drawnOf.TryGetValue(buf[i].collider, out var mr) || mr == null || !drawnWanted.Contains(mr)) continue;
            if (buf[i].distance > near && buf[i].distance < best) best = buf[i].distance;
        }
        return double.IsPositiveInfinity(best) ? (double?)null : best;
    }

}
