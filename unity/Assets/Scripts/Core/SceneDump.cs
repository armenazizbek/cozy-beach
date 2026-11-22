using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// -autoshot-scenedump: writes what is drawn, as JSON next to each screenshot (`name.scene.json`), so two builds can be
/// compared object by object and number by number instead of pixel by pixel (unity/NATIVE_PLAN.md section 4). Every
/// active renderer and light in the loaded scenes, and the Cozy shader globals. It reads only Unity objects, so the same
/// dump works whether they were made by the Three bridge or by native code.
/// </summary>
internal static class SceneDump
{
    private static readonly string[] MAT_VECTORS = { "_Color", "_Emissive", "_Flags", "_Flags2", "_Params" };
    private static readonly string[] MAT_FLOATS = { "_Rough", "_Metal", "_Cull", "_ShadowCull", "_ZWrite", "_ZTest", "_SrcBlend", "_DstBlend", "_ColorMask" };
    private static readonly string[] GLOBALS = { "_CozyFogColor", "_CozyFogParams", "_CozyTone", "_CozyHemiSky", "_CozyHemiGround", "_CozyHemiUp", "_CozyAmbient", "_CozyEnv" };

    private static readonly CultureInfo IC = CultureInfo.InvariantCulture;

    internal static void Write(string path)
    {
        var t0 = System.Diagnostics.Stopwatch.StartNew();
        var sb = new StringBuilder(1 << 20);
        sb.Append("{\n\"renderers\": [\n");
        var seen = new Dictionary<string, int>();
        var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var rows = new List<(string key, string json)>();
        foreach (var r in renderers)
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            string p = PathOf(r.transform);
            seen.TryGetValue(p, out int n);
            seen[p] = n + 1;
            rows.Add((p + "#" + n, RendererJson(r, p + "#" + n)));
        }
        rows.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
        for (int i = 0; i < rows.Count; i++) sb.Append(rows[i].json).Append(i + 1 < rows.Count ? ",\n" : "\n");
        sb.Append("],\n\"lights\": [\n");
        var lights = new List<string>();
        foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!l.enabled || !l.gameObject.activeInHierarchy) continue;
            lights.Add("{" +
                $"\"path\": {Str(PathOf(l.transform))}, \"type\": {Str(l.type.ToString())}, " +
                $"\"pos\": {V(l.transform.position)}, \"dir\": {V(l.transform.forward)}, " +
                $"\"color\": {V(l.color)}, \"intensity\": {F(l.intensity)}, \"range\": {F(l.range)}, " +
                $"\"spot\": [{F(l.spotAngle)}, {F(l.innerSpotAngle)}], \"shadows\": {Str(l.shadows.ToString())}, \"mask\": {l.cullingMask}" +
                "}");
        }
        lights.Sort(string.CompareOrdinal);
        sb.Append(string.Join(",\n", lights)).Append("\n],\n\"globals\": {");
        for (int i = 0; i < GLOBALS.Length; i++)
            sb.Append(i == 0 ? "" : ", ").Append(Str(GLOBALS[i])).Append(": ").Append(V(Shader.GetGlobalVector(GLOBALS[i])));
        sb.Append("},\n");
        sb.Append($"\"counts\": {{\"renderers\": {rows.Count}, \"lights\": {lights.Count}}},\n");
        sb.Append($"\"ms\": {t0.Elapsed.TotalMilliseconds.ToString("0", IC)}\n}}\n");
        File.WriteAllText(path, sb.ToString());
    }

    private static string RendererJson(Renderer r, string key)
    {
        Mesh mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
        int verts = mesh != null ? mesh.vertexCount : 0;
        long tris = 0;
        if (mesh != null)
            for (int s = 0; s < mesh.subMeshCount; s++)
                tris += mesh.GetTopology(s) == MeshTopology.Triangles ? (long)mesh.GetIndexCount(s) / 3 : 0;
        var b = r.bounds;
        var sb = new StringBuilder(512);
        sb.Append("{\"path\": ").Append(Str(key))
          .Append(", \"layer\": ").Append(r.gameObject.layer)
          .Append(", \"min\": ").Append(V(b.min)).Append(", \"max\": ").Append(V(b.max))
          .Append(", \"verts\": ").Append(verts).Append(", \"tris\": ").Append(tris)
          .Append(", \"vhash\": ").Append(Str(VertexHash(r, mesh)))
          .Append(", \"cast\": ").Append(Str(r.shadowCastingMode.ToString()))
          .Append(", \"receive\": ").Append(r.receiveShadows ? "true" : "false")
          .Append(", \"order\": ").Append(r.sortingOrder)
          .Append(", \"materials\": [");
        var mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(MaterialJson(mats[i]));
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static string MaterialJson(Material m)
    {
        if (m == null) return "null";
        var sb = new StringBuilder(256);
        sb.Append("{\"shader\": ").Append(Str(m.shader != null ? m.shader.name : "")).Append(", \"queue\": ").Append(m.renderQueue);
        foreach (var name in MAT_VECTORS) if (m.HasProperty(name)) sb.Append(", ").Append(Str(name)).Append(": ").Append(V(m.GetVector(name)));
        foreach (var name in MAT_FLOATS) if (m.HasProperty(name)) sb.Append(", ").Append(Str(name)).Append(": ").Append(F(m.GetFloat(name)));
        if (m.HasProperty("_MainTex"))
        {
            var t = m.GetTexture("_MainTex");
            sb.Append(", \"map\": ").Append(t == null || t == Texture2D.whiteTexture ? "null" : $"[{t.width}, {t.height}]");
        }
        return sb.Append('}').ToString();
    }

    /// <summary>
    /// The mesh's vertices in world space, each rounded to a millimetre and hashed, summed so the order of vertices does not
    /// matter: the same shape in the same place gives the same hash, however it was built.
    /// </summary>
    private static string VertexHash(Renderer r, Mesh mesh)
    {
        if (mesh == null) return "";
        if (!mesh.isReadable) return "unreadable";
        var verts = new List<Vector3>(mesh.vertexCount);
        mesh.GetVertices(verts);
        var m = r.localToWorldMatrix;
        ulong sum = 0;
        foreach (var v in verts)
        {
            var w = m.MultiplyPoint3x4(v);
            long x = (long)Math.Round(w.x * 1000), y = (long)Math.Round(w.y * 1000), z = (long)Math.Round(w.z * 1000);
            ulong h = unchecked((ulong)x * 0x9E3779B97F4A7C15UL ^ (ulong)y * 0xC2B2AE3D27D4EB4FUL ^ (ulong)z * 0x165667B19E3779F9UL);
            h ^= h >> 29;
            sum = unchecked(sum + h * 0xBF58476D1CE4E5B9UL);
        }
        return sum.ToString("x16");
    }

    /// <summary>The hierarchy path with each object's place among its siblings, so objects with the same name stay apart.</summary>
    private static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (var c = t; c != null; c = c.parent) parts.Add(c.parent != null ? $"{c.name}[{c.GetSiblingIndex()}]" : c.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string F(float f) => float.IsFinite(f) ? f.ToString("0.#####", IC) : "null";
    private static string V(Vector3 v) => $"[{F(v.x)}, {F(v.y)}, {F(v.z)}]";
    private static string V(Vector4 v) => $"[{F(v.x)}, {F(v.y)}, {F(v.z)}, {F(v.w)}]";
    private static string V(Color c) => $"[{F(c.r)}, {F(c.g)}, {F(c.b)}, {F(c.a)}]";

    private static string Str(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }
}
