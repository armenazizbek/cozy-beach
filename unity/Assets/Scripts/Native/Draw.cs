using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Native objects made the way the bridge made them (unity/NATIVE_PLAN.md 2.2, S4): one GameObject per object, a MeshFilter and
/// MeshRenderer for what is drawn, the same renderer settings (no light or reflection probes, shadows as asked), the same
/// names ("Pier (Group)", "Mesh"), so the scene dumps of the old and new game compare object by object.
/// Everything is placed in game numbers: local transforms under its parent, inside <see cref="World.Root"/>.
/// </summary>
public static class Draw
{
    /// <summary>The name the bridge gave the GameObject of an object with this name and type.</summary>
    public static string Label(string name, string type) => string.IsNullOrEmpty(name) ? type : name + " (" + type + ")";

    /// <summary>An empty GameObject under `parent` (a Three Group).</summary>
    public static Transform Group(Transform parent, string name = null)
    {
        var t = new GameObject(Label(name, "Group")).transform;
        if (parent != null) t.SetParent(parent, false);
        t.gameObject.layer = World.LayerUnder(parent);
        return t;
    }

    /// <summary>
    /// A drawn object: `mesh` with `material` under `parent`. Shadows follow <see cref="ShadowPolicy"/> (small casters drop out on
    /// the weaker presets, as before); `order` is Three's renderOrder; `unculled` is Three's frustumCulled = false.
    /// </summary>
    public static MeshRenderer Mesh(Transform parent, Mesh mesh, Material material, string name = null, bool cast = false, bool receive = false,
        int order = 0, bool unculled = false, string type = "Mesh")
    {
        var go = new GameObject(Label(name, type));
        go.transform.SetParent(parent, false);
        go.layer = World.LayerUnder(parent);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        Setup(r);
        if (material != null) r.sharedMaterial = material;
        r.sortingOrder = Mathf.Clamp(order, -32768, 32767);
        if (unculled) r.localBounds = Huge;
        r.receiveShadows = receive;
        ShadowPolicy.Track(r, cast);
        return r;
    }

    /// <summary>A drawn object with one material per submesh (a multi-material mesh).</summary>
    public static MeshRenderer Mesh(Transform parent, Mesh mesh, Material[] materials, string name = null, bool cast = false, bool receive = false,
        int order = 0, bool unculled = false)
    {
        var r = Mesh(parent, mesh, (Material)null, name, cast, receive, order, unculled);
        r.sharedMaterials = materials;
        return r;
    }

    /// <summary>The renderer settings the bridge gave every renderer.</summary>
    public static void Setup(Renderer r)
    {
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.allowOcclusionWhenDynamic = false;
    }

    /// <summary>The bounds the bridge gave objects that are never culled.</summary>
    public static readonly Bounds Huge = new Bounds(Vector3.zero, Vector3.one * 100000f);
}

/// <summary>
/// Which renderers cast shadows (SceneSync.SetShadowFlags, native): what the game asked for, except that on the weaker presets
/// small things (smaller than UrpSettings.SmallCasterCutoff across their bounding box, scale included) cast none. Renderers
/// are looked at again when the preset changes.
/// </summary>
public static class ShadowPolicy
{
    private static readonly List<(Renderer r, bool wanted)> tracked = new List<(Renderer, bool)>();
    private static int version = -1;

    /// <summary>Starts deciding this renderer's shadow from now on; `cast` is what the object asks for.</summary>
    public static void Track(Renderer r, bool cast)
    {
        tracked.Add((r, cast));
        Decide(r, cast);
    }

    /// <summary>Changes what a tracked renderer asks for.</summary>
    public static void Want(Renderer r, bool cast)
    {
        for (int i = 0; i < tracked.Count; i++)
            if (ReferenceEquals(tracked[i].r, r)) { tracked[i] = (r, cast); break; }
        Decide(r, cast);
    }

    /// <summary>Once a frame: when the preset changed, every tracked renderer is decided again.</summary>
    public static void Tick()
    {
        if (version == THREE.Bridge.UrpSettings.CasterVersion) return;
        version = THREE.Bridge.UrpSettings.CasterVersion;
        for (int i = tracked.Count - 1; i >= 0; i--)
        {
            if (tracked[i].r == null) { tracked.RemoveAt(i); continue; }
            Decide(tracked[i].r, tracked[i].wanted);
        }
    }

    private static void Decide(Renderer r, bool wanted)
    {
        bool cast = wanted;
        float cutoff = THREE.Bridge.UrpSettings.SmallCasterCutoff;
        if (cast && cutoff > 0)
        {
            var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
            var s = r.transform.localScale;
            float big = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            if (mesh != null && mesh.bounds.size.magnitude * big < cutoff) cast = false;
        }
        var mode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
        if (r.shadowCastingMode != mode) r.shadowCastingMode = mode;
    }
}
