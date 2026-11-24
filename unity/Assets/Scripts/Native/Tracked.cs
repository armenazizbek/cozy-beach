using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// A description tree put into the world once, with only the parts a module names brought in line before each frame
/// (unity/UI_PLAN.md B1). It does what the whole-tree copy (LiveTree, now removed) did for those parts and nothing for the rest:
/// <list type="bullet">
/// <item>realised when it is made, under `parent`, on the scene's layer, claimed or not;</item>
/// <item>made again only when the tree's shape changed (something added or taken away);</item>
/// <item>hidden (the tree itself, or the part it hangs under switched off): nothing but switching it off;</item>
/// <item>each frame: the named parts shown or hidden and posed, their point lights set, the named geometry refilled
/// when it was rewritten, and every material of the tree pushed.</item>
/// </list>
/// `parts` is asked again after each build, so it can name parts that were added since.
/// </summary>
public sealed class Tracked
{
    private readonly THREE.Object3D desc;
    private readonly Transform parent;
    private readonly bool claim;
    private readonly int layer;
    private readonly Func<IEnumerable<THREE.Object3D>> parts;
    private readonly Func<IEnumerable<THREE.Object3D>> rewritten;
    private Realized live;
    private int stamp = int.MinValue;
    private long shape;
    private readonly List<(THREE.Object3D o, Transform t)> moving = new List<(THREE.Object3D, Transform)>();
    private readonly List<(THREE.PointLight l, Light u)> lit = new List<(THREE.PointLight, Light)>();
    private readonly List<(THREE.Material m, MaterialKit.On on)> mats = new List<(THREE.Material, MaterialKit.On)>();
    private readonly List<(THREE.Object3D o, Mesh mesh, int version)> refills = new List<(THREE.Object3D, Mesh, int)>();

    /// <param name="parts">what moves, shows or hides (the tree's own root is always kept in line)</param>
    /// <param name="rewritten">whose geometry is rewritten after it is made (particle points, drips), refilled when it changes</param>
    public Tracked(THREE.Object3D desc, Transform parent, Func<IEnumerable<THREE.Object3D>> parts,
        Func<IEnumerable<THREE.Object3D>> rewritten = null, bool claim = false, int layer = -1)
    {
        this.desc = desc;
        this.parent = parent;
        this.parts = parts;
        this.rewritten = rewritten;
        this.claim = claim;
        this.layer = layer;
        // realised at once, as the whole-tree copy was: its GameObjects take their place among their siblings now, and a
        // scene drawn this very frame (a find's photo) has them
        Sync();
    }

    /// <summary>Puts a tree into the world and keeps it in line before each frame.</summary>
    public static Tracked Place(THREE.Object3D desc, Transform parent, Func<IEnumerable<THREE.Object3D>> parts,
        Func<IEnumerable<THREE.Object3D>> rewritten = null, bool claim = false, int layer = -1)
    {
        var t = new Tracked(desc, parent, parts, rewritten, claim, layer);
        NativeFrame.BeforeDraw += t.Sync;
        return t;
    }

    public Transform Root => live?.root;

    /// <summary>The tree's name (its description's), for the frame's timings.</summary>
    public string Name => !string.IsNullOrEmpty(desc.name) ? desc.name
        : desc.children.Count > 0 ? $"{desc.type}[{(string.IsNullOrEmpty(desc.children[0].name) ? desc.children[0].type : desc.children[0].name)} +{desc.children.Count - 1}]@{(parent != null ? parent.name : "")}" : desc.type;

    /// <summary>A hash of a tree's shape (which objects hang where), so a change of shape can be told from a change of pose.</summary>
    public static long Shape(THREE.Object3D o)
    {
        long h = RuntimeHelpers.GetHashCode(o);
        foreach (var c in o.children) h = unchecked(h * 1000003L + Shape(c));
        return unchecked(h * 31 + o.children.Count);
    }

    /// <summary>Every part of a tree, its root first.</summary>
    public static List<THREE.Object3D> Subtree(THREE.Object3D o)
    {
        var list = new List<THREE.Object3D>();
        o.traverse(x => list.Add(x));
        return list;
    }

    /// <summary>The parts of a tree whose geometry can be rewritten and refilled (not instanced, not skinned).</summary>
    private static IEnumerable<THREE.Object3D> Drawn(THREE.Object3D o)
    {
        foreach (var x in Subtree(o))
            if (!(x is THREE.InstancedMesh) && !(x is THREE.SkinnedMesh) && (x is THREE.Mesh || x is THREE.Points || x is THREE.Line)) yield return x;
    }

    /// <summary>
    /// A small tree whose parts all move or change (a pool of arrows, a ring, particles, the opening's ship): every part is
    /// kept in line and every drawn geometry refilled when it is rewritten.
    /// </summary>
    public static Tracked PlaceAll(THREE.Object3D desc, Transform parent, bool claim = false, int layer = -1) =>
        Place(desc, parent, () => Subtree(desc), () => Drawn(desc), claim, layer);

    /// <summary>The same, synced by its owner from the next frame on (the scene view does that itself).</summary>
    public static Tracked All(THREE.Object3D desc, Transform parent, bool claim = false, int layer = -1) =>
        new Tracked(desc, parent, () => Subtree(desc), () => Drawn(desc), claim, layer);

    /// <summary>The GameObject made for a part of the tree (null before the first draw, or for a part welded away).</summary>
    public Transform this[THREE.Object3D o] => live?[o];

    private void Build()
    {
        var under = live?.root != null && live.root.parent != null ? live.root.parent : parent;
        if (claim && live != null) foreach (var o in live.map.Keys) o.native = null;
        Destroy();
        live = Realizer.Realize(desc, under, layer >= 0 ? layer : World.Layer, claim);
        stamp = desc.treeVersion;
        shape = Shape(desc);
        moving.Clear();
        lit.Clear();
        mats.Clear();
        refills.Clear();
        var seen = new HashSet<THREE.Object3D>();
        void add(THREE.Object3D o)
        {
            if (o == null || !seen.Add(o)) return;
            var t = live[o];
            if (t == null) return;
            moving.Add((o, t));
            if (o is THREE.PointLight pl && t.GetComponent<Light>() is Light u) lit.Add((pl, u));
        }
        add(desc);
        if (parts != null) foreach (var o in parts()) add(o);
        foreach (var kv in live.map)
        {
            THREE.Material m = null;
            var on = MaterialKit.On.Mesh;
            switch (kv.Key)
            {
                case THREE.InstancedMesh im: m = im.material; on = MaterialKit.On.Instanced; break;
                case THREE.Mesh mesh: m = mesh.material; break;
                case THREE.Points pts: m = pts.material; on = MaterialKit.On.Points; break;
                case THREE.Line line: m = line.material; on = MaterialKit.On.Line; break;
                case THREE.Sprite sp: m = sp.material; on = MaterialKit.On.Sprite; break;
            }
            if (m != null && !mats.Contains((m, on))) mats.Add((m, on));
        }
        if (rewritten != null)
            foreach (var o in rewritten())
            {
                var mf = o != null ? live[o]?.GetComponent<MeshFilter>() : null;
                var g = Geometry(o);
                if (mf != null && mf.sharedMesh != null && g != null) refills.Add((o, mf.sharedMesh, g.ContentVersion()));
            }
    }

    /// <summary>Brings the named parts in line with the description.</summary>
    public void Sync()
    {
        if (!desc.visible && live != null)
        {
            if (live.root.gameObject.activeSelf) live.root.gameObject.SetActive(false);
            return;
        }
        if (live != null && live.root != null && live.root.parent != null && !live.root.parent.gameObject.activeInHierarchy) return;
        if (live == null) Build();
        else if (desc.treeVersion != stamp)
        {
            stamp = desc.treeVersion;
            if (Shape(desc) != shape) Build();
        }
        foreach (var (o, t) in moving)
        {
            if (t == null) continue;
            if (t.gameObject.activeSelf != o.visible) t.gameObject.SetActive(o.visible);
            if (o.visible) Realizer.Pose(o, t);
        }
        foreach (var (l, u) in lit)
        {
            if (!l.visible) continue;
            Lights.Set(u, new Color((float)l.color.r, (float)l.color.g, (float)l.color.b), (float)l.intensity, u.gameObject.layer);
            float range = l.distance > 0 ? (float)l.distance : 1000f;
            if (u.range != range) u.range = range;
            Lights.Shadows(u, l.castShadow);
        }
        foreach (var (m, on) in mats) MaterialKit.PushOnce(m, on);
        for (int i = 0; i < refills.Count; i++)
        {
            var (o, mesh, v) = refills[i];
            var g = Geometry(o);
            int now = g.ContentVersion();
            if (now == v) continue;
            switch (o)
            {
                case THREE.Points _: MeshKit.RefillPoints(mesh, g); break;
                case THREE.Line l: MeshKit.RefillLines(mesh, g, l is THREE.LineSegments); break;
                case THREE.Mesh m: MeshKit.Refill(mesh, g, m.isMultiMaterial && g.groups.Count > 0); break;
            }
            refills[i] = (o, mesh, now);
        }
    }

    private static THREE.BufferGeometry Geometry(THREE.Object3D o) => o switch
    {
        THREE.Mesh m => m.geometry,
        THREE.Points p => p.geometry,
        THREE.Line l => l.geometry,
        _ => null,
    };

    /// <summary>Takes the tree out of the world: no more syncing, its GameObjects gone.</summary>
    public void Remove()
    {
        NativeFrame.BeforeDraw -= Sync;
        if (claim && live != null) foreach (var o in live.map.Keys) o.native = null;
        Destroy();
    }

    private void Destroy()
    {
        if (live?.root != null)
        {
            live.root.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(live.root.gameObject);
        }
        live = null;
    }
}
