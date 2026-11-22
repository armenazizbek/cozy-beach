using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Realizer against the bridge (S4, S5): the whole live world is realized into a hidden copy, and every renderer the
/// bridge made must have a twin with the same world bounds, mesh size, materials, shadows and draw order.
/// </summary>
internal static class SelfCheckRealizer
{
    internal static IEnumerable<(string, Func<string>)> Checks()
    {
        yield return ("Realizer twins every renderer the bridge made of the world", Twin);
    }

    private static string Twin()
    {
        var scene = World.Scene;
        if (scene == null) return "no world";
        var root = new GameObject("selfcheck twin").transform;
        root.localScale = new Vector3(1, 1, -1);
        int layer = scene.unityLayer;
        try
        {
            var map = new Dictionary<THREE.Object3D, Transform>();
            foreach (var c in scene.children)
            {
                if (c is THREE.Camera || c.native != null) continue;
                var r = Realizer.Realize(c, root, layer, false);
                foreach (var kv in r.map) map[kv.Key] = kv.Value;
            }
            int compared = 0, failed = 0;
            unculledInBridge = 0;
            var problems = new List<string>();
            foreach (var kv in map)
            {
                var b = World.Of(kv.Key);
                if (b == null || !b.gameObject.activeInHierarchy) continue;
                var rb = b.GetComponent<Renderer>();
                if (rb == null || !rb.enabled) continue;
                compared++;
                var rt = kv.Value.GetComponent<Renderer>();
                string why = Diff(rb, rt, kv.Value);
                if (why != null)
                {
                    failed++;
                    if (problems.Count < 6) problems.Add($"{b.name}: {why}");
                }
            }
            Debug.Log($"[SelfCheck] twin: {map.Count} objects realized, {compared} drawn ones compared with the bridge's, {failed} differ; {unculledInBridge} are never culled in the bridge (bounds not compared)");
            return failed == 0 ? null : $"{failed} of {compared} differ: " + string.Join("; ", problems);
        }
        finally
        {
            UnityEngine.Object.Destroy(root.gameObject);
        }
    }

    private static int unculledInBridge;

    private static string Diff(Renderer b, Renderer t, Transform tt)
    {
        if (t == null) return "no renderer in the twin";
        if (!tt.gameObject.activeInHierarchy) return "twin hidden";
        if (b.GetType() != t.GetType()) return $"{b.GetType().Name} vs {t.GetType().Name}";
        var mb = b is SkinnedMeshRenderer sb ? sb.sharedMesh : b.GetComponent<MeshFilter>()?.sharedMesh;
        var mt = t is SkinnedMeshRenderer st ? st.sharedMesh : t.GetComponent<MeshFilter>()?.sharedMesh;
        if ((mb == null) != (mt == null) || (mb != null && mb.vertexCount != mt.vertexCount)) return $"mesh {mb?.vertexCount} vs {mt?.vertexCount} vertices";
        // the bridge never re-reads the inside of a static group, so the warm-up's frustumCulled = false (Game.sendEverythingToGpu)
        // sticks there: those renderers keep the 100 km "never culled" bounds; the twin has the real ones (same pixels)
        if (b.localBounds.extents.x > 10000f) unculledInBridge++;
        else if (!(b is SkinnedMeshRenderer))
        {
            var x = b.bounds; var y = t.bounds;
            float tol = 2e-3f * Mathf.Max(1f, x.extents.magnitude);
            if ((x.min - y.min).magnitude > tol || (x.max - y.max).magnitude > tol) return $"bounds {x.min}..{x.max} vs {y.min}..{y.max}";
        }
        var ma = b.sharedMaterials; var mt2 = t.sharedMaterials;
        if (ma.Length != mt2.Length) return $"{ma.Length} vs {mt2.Length} materials";
        for (int i = 0; i < ma.Length; i++)
            if (!ReferenceEquals(ma[i], mt2[i]) && !(ma[i] != null && mt2[i] != null && ma[i].shader == mt2[i].shader && ma[i].renderQueue == mt2[i].renderQueue && ma[i].name == mt2[i].name))
                return $"material {i}: {ma[i]?.name} vs {mt2[i]?.name}";
        if (b.shadowCastingMode != t.shadowCastingMode) return $"cast {b.shadowCastingMode} vs {t.shadowCastingMode}";
        if (b.receiveShadows != t.receiveShadows) return "receive";
        if (b.sortingOrder != t.sortingOrder) return $"order {b.sortingOrder} vs {t.sortingOrder}";
        if (b.gameObject.layer != t.gameObject.layer) return "layer";
        return null;
    }
}
