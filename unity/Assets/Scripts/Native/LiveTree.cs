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
    private readonly List<THREE.Material> mats = new List<THREE.Material>();

    public LiveTree(THREE.Object3D desc, Transform parent)
    {
        this.desc = desc;
        this.parent = parent;
        Sync();
    }

    public Transform Root => live?.root;

    /// <summary>Brings the GameObjects in line with the description.</summary>
    public void Sync()
    {
        long now = Shape(desc);
        if (live == null || now != shape)
        {
            Destroy();
            shape = now;
            live = Realizer.Realize(desc, parent, World.Layer);
            live.Follow(live.map.Keys);
            mats.Clear();
            var seen = new HashSet<THREE.Material>();
            foreach (var o in live.map.Keys)
                if (o is THREE.Mesh m && m.material != null && seen.Add(m.material)) mats.Add(m.material);
        }
        live.Apply();
        foreach (var m in mats) MaterialKit.Push(m);
    }

    /// <summary>Takes the GameObjects away (the tree left the world).</summary>
    public void Destroy()
    {
        if (live?.root != null) Object.Destroy(live.root.gameObject);
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
