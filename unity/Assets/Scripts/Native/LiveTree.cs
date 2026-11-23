using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// A small description tree kept on screen natively while the game keeps changing it (unity/NATIVE_PLAN.md S11): a sandcastle
/// that grows, crumbles and washes away. <see cref="Sync"/> re-realizes the tree when parts were added or removed, then copies
/// every part's pose and visibility and pushes every material, so the GameObjects show the description as it is now. For
/// trees of a few dozen parts; big modules name only their moving parts instead (Realized.Follow).
/// </summary>
public sealed class LiveTree
{
    private readonly THREE.Object3D desc;
    private readonly Transform parent;
    private Realized live;
    private long shape = long.MinValue;
    /// <summary>the description's treeVersion when the shape was last checked</summary>
    private int stampSeen = -1;
    private readonly List<(THREE.Material m, MaterialKit.On on)> mats = new List<(THREE.Material, MaterialKit.On)>();
    /// <summary>drawn parts whose geometry may change, with the Unity mesh made for it and the geometry's version then</summary>
    private readonly List<(THREE.Object3D o, Mesh mesh, int version)> shapes = new List<(THREE.Object3D, Mesh, int)>();

    /// <summary>The GameObjects made for the tree's descriptions (null before the first sync).</summary>
    public Realized Live => live;

    private readonly bool claim;
    /// <summary>the Unity layer of the scene the tree belongs to (-1: the main world's)</summary>
    private readonly int layer;

    public LiveTree(THREE.Object3D desc, Transform parent) : this(desc, parent, false) { }

    /// <summary>
    /// With `claim` every description in the tree is marked as drawn (Object3D.native), so NativeView does not make a second
    /// copy of a part hung straight in a small scene (the cleaning bench's parts, the tools in hand). A part that leaves the
    /// tree gives the claim up again.
    /// </summary>
    public LiveTree(THREE.Object3D desc, Transform parent, bool claim, int layer = -1)
    {
        this.desc = desc;
        this.parent = parent;
        this.claim = claim;
        this.layer = layer;
        Sync();
    }

    public Transform Root => live?.root;

    /// <summary>The tree's name (its description's), for the frame's timings.</summary>
    public string Name => !string.IsNullOrEmpty(desc.name) ? desc.name
        : desc.children.Count > 0 ? $"{desc.type}[{(string.IsNullOrEmpty(desc.children[0].name) ? desc.children[0].type : desc.children[0].name)} +{desc.children.Count - 1}]@{(parent != null ? parent.name : "")}" : desc.type;

    /// <summary>
    /// Puts a description into the world natively and keeps it in line at draw time (what a module's Place does, for the loose
    /// pieces Game used to add to the scene itself).
    /// </summary>
    public static LiveTree Place(THREE.Object3D desc, Transform parent, bool claim = false, int layer = -1)
    {
        var t = new LiveTree(desc, parent, claim, layer);
        NativeFrame.BeforeDraw += t.Sync;
        return t;
    }

    /// <summary>Brings the GameObjects in line with the description.</summary>
    public void Sync()
    {
        // a hidden tree (the world under the sea while on land) costs nothing but switching its root off
        if (!desc.visible && live != null)
        {
            if (live.root.gameObject.activeSelf)
            {
                live.root.gameObject.SetActive(false);
                // switched off behind Apply's back: the next Apply sets every part again
                live.Invalidate();
            }
            return;
        }
        // hung under a part that is switched off (the land, while diving or underground): nothing of it shows, so it waits;
        // the sync before the frame it shows again brings it all up to date
        if (live != null && live.root != null && live.root.parent != null && !live.root.parent.gameObject.activeInHierarchy) return;
        // the shape can only have changed if something in the tree gained or lost a child since it was last checked
        bool reshape = live == null;
        if (reshape || desc.treeVersion != stampSeen)
        {
            stampSeen = desc.treeVersion;
            long now = Shape(desc);
            if (now != shape)
            {
                shape = now;
                reshape = true;
            }
        }
        if (reshape)
        {
            // unless asked to claim (Object3D.native stays null): a part that leaves this tree for another scene (a find carried
            // to the cleaning bench) must still be drawn there
            var under = live?.root != null && live.root.parent != null ? live.root.parent : parent;
            if (claim && live != null) foreach (var o in live.map.Keys) o.native = null;
            Destroy();
            live = Realizer.Realize(desc, under, layer >= 0 ? layer : World.Layer, claim);
            live.Follow(live.map.Keys);
            mats.Clear();
            var seen = new HashSet<(THREE.Material, MaterialKit.On)>();
            void add(THREE.Material m, MaterialKit.On on)
            {
                if (m != null && seen.Add((m, on))) mats.Add((m, on));
            }
            // each material with the kind of object it is drawn on (sprites, points and lines have shaders of their own)
            foreach (var o in live.map.Keys)
            {
                switch (o)
                {
                    case THREE.InstancedMesh im: add(im.material, MaterialKit.On.Instanced); break;
                    case THREE.Mesh m: add(m.material, MaterialKit.On.Mesh); break;
                    case THREE.Points p: add(p.material, MaterialKit.On.Points); break;
                    case THREE.Line l: add(l.material, MaterialKit.On.Line); break;
                    case THREE.Sprite sp: add(sp.material, MaterialKit.On.Sprite); break;
                }
            }
            shapes.Clear();
            foreach (var kv in live.map)
            {
                if (kv.Key is THREE.InstancedMesh || kv.Key is THREE.SkinnedMesh) continue;
                var g = Geometry(kv.Key);
                var mf = kv.Value.GetComponent<MeshFilter>();
                if (g != null && mf != null && mf.sharedMesh != null) shapes.Add((kv.Key, mf.sharedMesh, g.ContentVersion()));
            }
        }
        live.Apply();
        foreach (var (m, on) in mats) MaterialKit.PushOnce(m, on);
        // geometry rewritten since it was drawn (drips falling, a pool filling): its mesh is refilled as the bridge re-sent it
        for (int i = 0; i < shapes.Count; i++)
        {
            var (o, mesh, v) = shapes[i];
            var g = Geometry(o);
            int ver = g.ContentVersion();
            if (ver == v) continue;
            switch (o)
            {
                case THREE.Points _: MeshKit.RefillPoints(mesh, g); break;
                case THREE.Line l: MeshKit.RefillLines(mesh, g, l is THREE.LineSegments); break;
                case THREE.Mesh m: MeshKit.Refill(mesh, g, m.isMultiMaterial && g.groups.Count > 0); break;
            }
            shapes[i] = (o, mesh, ver);
        }
    }

    private static THREE.BufferGeometry Geometry(THREE.Object3D o) => o switch
    {
        THREE.Mesh m => m.geometry,
        THREE.Points p => p.geometry,
        THREE.Line l => l.geometry,
        _ => null,
    };

    /// <summary>Takes a tree from <see cref="Place"/> out of the world: no more syncing, its GameObjects gone.</summary>
    public void Remove()
    {
        NativeFrame.BeforeDraw -= Sync;
        if (claim && live != null) foreach (var o in live.map.Keys) o.native = null;
        Destroy();
    }

    /// <summary>Takes the GameObjects away (the tree left the world).</summary>
    public void Destroy()
    {
        if (live?.root != null)
        {
            // Destroy waits for the end of the frame; switched off now, it is gone from renders later in this frame too (the
            // item photos take one find after another in a single frame)
            live.root.gameObject.SetActive(false);
            Object.Destroy(live.root.gameObject);
        }
        live = null;
    }

    /// <summary>A number that changes when any part is added, removed or moved to another parent.</summary>
    private static long Shape(THREE.Object3D o)
    {
        long h = RuntimeHelpers.GetHashCode(o);
        foreach (var c in o.children) h = unchecked(h * 1000003L + Shape(c));
        return unchecked(h * 31 + o.children.Count);
    }
}
