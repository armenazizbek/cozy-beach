using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Unity meshes from the game's procedural geometry (unity/NATIVE_PLAN.md 2.2, S3). The geometry is still made by the
/// generator code the game always used (boxes, cylinders, lathes, tubes, extrusions, merges: plain C# maths in Three/,
/// which moves under MeshKit by a rename in S25), so vertices come out exactly as before. MeshKit turns it into Unity meshes
/// once, with the world-root convention (game numbers unconverted, original winding), in the same vertex layout the bridge
/// used and the Cozy shaders read:
///   TEXCOORD0 uv, COLOR colour, TEXCOORD2.. extra attributes in name order (four floats each);
///   for baked instances TEXCOORD1 instance colour, TEXCOORD2 an instanced attribute, TEXCOORD3 instance origin + index,
///   TEXCOORD4 the vertex before its instance matrix, TEXCOORD5/6 the instance's x and z axes.
/// Everything here may run off the main thread up to the point a UnityEngine.Mesh is made; <see cref="Write"/> fills a
/// writable MeshData, which a worker may do (the loader, S5).
/// </summary>
public static class MeshKit
{
    private static readonly HashSet<string> STANDARD = new HashSet<string> { "position", "normal", "uv", "color", "skinIndex", "skinWeight" };

    /// <summary>Names of the extra attributes, in the order they go into TEXCOORD2 onwards (as the bridge orders them).</summary>
    public static List<string> ExtraNames(THREE.BufferGeometry g)
    {
        var names = new List<string>();
        foreach (var k in g.attributes.Keys) if (!STANDARD.Contains(k)) names.Add(k);
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>
    /// A mesh of triangles. With `groups` the geometry's groups become submeshes (one material each, a multi-material mesh);
    /// otherwise its draw range is one submesh.
    /// </summary>
    public static Mesh ToMesh(THREE.BufferGeometry g, bool groups = false, string name = null)
    {
        var mesh = new Mesh { name = name ?? (string.IsNullOrEmpty(g.name) ? g.type : g.name) };
        Fill(mesh, g, groups ? Topology.Groups : Topology.Triangles);
        return mesh;
    }

    /// <summary>A mesh of lines (LineSegments) or a line strip (Line).</summary>
    public static Mesh ToLines(THREE.BufferGeometry g, bool segments = true, string name = null)
    {
        var mesh = new Mesh { name = name ?? "Lines" };
        Fill(mesh, g, segments ? Topology.Lines : Topology.LineStrip);
        return mesh;
    }

    /// <summary>Refills a mesh made by <see cref="ToLines"/> after its geometry changed (drips falling).</summary>
    public static void RefillLines(Mesh mesh, THREE.BufferGeometry g, bool segments = true)
    {
        mesh.Clear();
        Fill(mesh, g, segments ? Topology.Lines : Topology.LineStrip);
    }

    /// <summary>Refills a mesh made by <see cref="ToMesh"/> after its geometry changed (a dug beach chunk, a rebuilt ring).</summary>
    public static void Refill(Mesh mesh, THREE.BufferGeometry g, bool groups = false)
    {
        mesh.Clear();
        Fill(mesh, g, groups ? Topology.Groups : Topology.Triangles);
    }

    /// <summary>
    /// Rewrites one extra attribute's vertex channel only (the ocean's sea-floor depth, refreshed as the view moves), in the
    /// TEXCOORD slot <see cref="ToMesh"/> gave it: the rest of the mesh is left as it is. False if the attribute has no slot.
    /// </summary>
    public static bool RefillExtra(Mesh mesh, THREE.BufferGeometry g, string attribute)
    {
        int slot = 2;
        int n = g.attributes.position?.count ?? 0;
        foreach (var attrName in ExtraNames(g))
        {
            if (slot > 7) return false;
            var a = g.attributes[attrName];
            if (a.isInstanced || a.count != n) continue;
            if (attrName == attribute)
            {
                var v4 = new List<Vector4>(n);
                int s = a.itemSize;
                for (int i = 0; i < n; i++)
                    v4.Add(new Vector4(a.array[i * s], s > 1 ? a.array[i * s + 1] : 0, s > 2 ? a.array[i * s + 2] : 0, s > 3 ? a.array[i * s + 3] : 0));
                mesh.SetUVs(slot, v4);
                return true;
            }
            slot++;
        }
        return false;
    }

    private enum Topology { Triangles, Groups, Lines, LineStrip }

    private static void Fill(Mesh mesh, THREE.BufferGeometry g, Topology topo)
    {
        var pos = g.attributes.position;
        int n = pos?.count ?? 0;
        if (n == 0) return;
        mesh.indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        var v3 = new List<Vector3>(n);
        for (int i = 0; i < n; i++) v3.Add(new Vector3(pos.array[i * 3], pos.array[i * 3 + 1], pos.array[i * 3 + 2]));
        mesh.SetVertices(v3);
        var nor = g.attributes.normal;
        if (nor != null && nor.count == n)
        {
            v3.Clear();
            for (int i = 0; i < n; i++) v3.Add(new Vector3(nor.array[i * 3], nor.array[i * 3 + 1], nor.array[i * 3 + 2]));
            mesh.SetNormals(v3);
        }
        var uv = g.attributes.uv;
        if (uv != null && uv.count == n)
        {
            var v2 = new List<Vector2>(n);
            for (int i = 0; i < n; i++) v2.Add(new Vector2(uv.array[i * 2], uv.array[i * 2 + 1]));
            mesh.SetUVs(0, v2);
        }
        var col = g.attributes.color;
        if (col != null && col.count == n)
        {
            var cols = new List<Color>(n);
            int s = col.itemSize;
            for (int i = 0; i < n; i++) cols.Add(new Color(col.array[i * s], col.array[i * s + 1], col.array[i * s + 2], s > 3 ? col.array[i * s + 3] : 1));
            mesh.SetColors(cols);
        }
        int slot = 2;
        foreach (var attrName in ExtraNames(g))
        {
            if (slot > 7) break;
            var a = g.attributes[attrName];
            if (a.isInstanced || a.count != n) continue;
            var v4 = new List<Vector4>(n);
            int s = a.itemSize;
            for (int i = 0; i < n; i++)
                v4.Add(new Vector4(a.array[i * s], s > 1 ? a.array[i * s + 1] : 0, s > 2 ? a.array[i * s + 2] : 0, s > 3 ? a.array[i * s + 3] : 0));
            mesh.SetUVs(slot, v4);
            slot++;
        }
        var si = g.attributes.skinIndex;
        var sw = g.attributes.skinWeight;
        if (si != null && sw != null && si.count == n)
        {
            var bw = new BoneWeight[n];
            for (int i = 0; i < n; i++)
            {
                bw[i] = new BoneWeight
                {
                    boneIndex0 = (int)si.array[i * 4], boneIndex1 = (int)si.array[i * 4 + 1], boneIndex2 = (int)si.array[i * 4 + 2], boneIndex3 = (int)si.array[i * 4 + 3],
                    weight0 = sw.array[i * 4], weight1 = sw.array[i * 4 + 1], weight2 = sw.array[i * 4 + 2], weight3 = sw.array[i * 4 + 3],
                };
            }
            mesh.boneWeights = bw;
        }
        var tris = new List<int>();
        if (topo == Topology.Lines || topo == Topology.LineStrip)
        {
            int total = g.index != null ? g.index.count : n;
            for (int i = 0; i < total; i++) tris.Add(g.index != null ? (int)g.index.array[i] : i);
            mesh.subMeshCount = 1;
            mesh.SetIndices(tris, topo == Topology.Lines ? MeshTopology.Lines : MeshTopology.LineStrip, 0, false);
        }
        else if (topo == Topology.Groups && g.groups.Count > 0)
        {
            mesh.subMeshCount = g.groups.Count;
            for (int gi = 0; gi < g.groups.Count; gi++)
            {
                Triangles(g, n, g.groups[gi].start, g.groups[gi].count, tris);
                mesh.SetTriangles(tris, gi, false);
            }
        }
        else
        {
            mesh.subMeshCount = 1;
            Triangles(g, n, g.drawRange.start, g.drawRange.count, tris);
            mesh.SetTriangles(tris, 0, false);
        }
        mesh.RecalculateBounds();
    }

    private static void Triangles(THREE.BufferGeometry g, int n, int start, int count, List<int> tris)
    {
        tris.Clear();
        int total = g.index != null ? g.index.count : n;
        int end = (int)Math.Min(total, (long)start + count);
        start = Math.Max(0, start);
        for (int i = start; i + 2 < end; i += 3)
        {
            int a, b, c;
            if (g.index != null) { a = (int)g.index.array[i]; b = (int)g.index.array[i + 1]; c = (int)g.index.array[i + 2]; }
            else { a = i; b = i + 1; c = i + 2; }
            if (a >= n || b >= n || c >= n) continue;
            // the original winding: under the world root Unity flips culling itself (its matrix has a negative determinant)
            tris.Add(a); tris.Add(b); tris.Add(c);
        }
    }

    /// <summary>
    /// Many copies of one geometry baked into one mesh (what an InstancedMesh was): `matrices` holds 16 numbers per instance
    /// (column-major, Three's layout), `colors` 3 per instance or null, `instanced` an optional per-instance attribute.
    /// </summary>
    public static Mesh BakeInstances(THREE.BufferGeometry g, float[] matrices, int count, float[] colors = null, THREE.BufferAttribute instanced = null, string name = null)
    {
        var mesh = new Mesh { name = name ?? "Instanced " + g.type };
        Bake(mesh, g, matrices, count, colors, instanced);
        return mesh;
    }

    /// <summary>Re-bakes a mesh from <see cref="BakeInstances"/> after its instances changed.</summary>
    public static void Bake(Mesh mesh, THREE.BufferGeometry g, float[] matrices, int count, float[] colors = null, THREE.BufferAttribute instanced = null)
    {
        var pos = g.attributes.position;
        var nor = g.attributes.normal;
        var uv = g.attributes.uv;
        var col = g.attributes.color;
        int n = pos.count;
        mesh.Clear();
        int total = n * count;
        mesh.indexFormat = total > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        var v3 = new List<Vector3>(total); var bn = new List<Vector3>(total); var v2 = new List<Vector2>(total); var cols = new List<Color>(total);
        var b1 = new List<Vector4>(total); var b2 = new List<Vector4>(total); var b3 = new List<Vector4>(total);
        var b4 = new List<Vector4>(total); var b5 = new List<Vector4>(total); var b6 = new List<Vector4>(total);
        var tris = new List<int>();
        var m = new THREE.Matrix4();
        var nm = new THREE.Matrix3();
        var p = new THREE.Vector3();
        for (int k = 0; k < count; k++)
        {
            for (int q = 0; q < 16; q++) m.elements[q] = matrices[k * 16 + q];
            nm.getNormalMatrix(m);
            float cr = 1, cg = 1, cb = 1;
            if (colors != null) { cr = colors[k * 3]; cg = colors[k * 3 + 1]; cb = colors[k * 3 + 2]; }
            float ia = 0, ib = 0, ic = 0, id = 0;
            if (instanced != null)
            {
                int s = instanced.itemSize;
                ia = instanced.array[k * s];
                if (s > 1) ib = instanced.array[k * s + 1];
                if (s > 2) ic = instanced.array[k * s + 2];
                if (s > 3) id = instanced.array[k * s + 3];
            }
            var origin = new Vector4((float)m.elements[12], (float)m.elements[13], (float)m.elements[14], k);
            var col0 = new Vector4((float)m.elements[0], (float)m.elements[1], (float)m.elements[2], 0);
            var col2 = new Vector4((float)m.elements[8], (float)m.elements[9], (float)m.elements[10], 0);
            for (int i = 0; i < n; i++)
            {
                p.set(pos.array[i * 3], pos.array[i * 3 + 1], pos.array[i * 3 + 2]).applyMatrix4(m);
                v3.Add(new Vector3((float)p.x, (float)p.y, (float)p.z));
                if (nor != null)
                {
                    p.set(nor.array[i * 3], nor.array[i * 3 + 1], nor.array[i * 3 + 2]).applyNormalMatrix(nm);
                    bn.Add(new Vector3((float)p.x, (float)p.y, (float)p.z));
                }
                if (uv != null) v2.Add(new Vector2(uv.array[i * 2], uv.array[i * 2 + 1]));
                if (col != null)
                {
                    int s = col.itemSize;
                    cols.Add(new Color(col.array[i * s], col.array[i * s + 1], col.array[i * s + 2], s > 3 ? col.array[i * s + 3] : 1));
                }
                b1.Add(new Vector4(cr, cg, cb, 1));
                b2.Add(new Vector4(ia, ib, ic, id));
                b3.Add(origin);
                b4.Add(new Vector4(pos.array[i * 3], pos.array[i * 3 + 1], pos.array[i * 3 + 2], 0));
                b5.Add(col0);
                b6.Add(col2);
            }
            int baseV = k * n;
            int idxCount = g.index != null ? g.index.count : n;
            for (int t = 0; t + 2 < idxCount; t += 3)
            {
                int a, b, c;
                if (g.index != null) { a = (int)g.index.array[t]; b = (int)g.index.array[t + 1]; c = (int)g.index.array[t + 2]; }
                else { a = t; b = t + 1; c = t + 2; }
                tris.Add(baseV + a); tris.Add(baseV + b); tris.Add(baseV + c);
            }
        }
        mesh.SetVertices(v3);
        if (bn.Count == v3.Count) mesh.SetNormals(bn);
        if (v2.Count == v3.Count) mesh.SetUVs(0, v2);
        if (cols.Count == v3.Count) mesh.SetColors(cols);
        mesh.SetUVs(1, b1);
        mesh.SetUVs(2, b2);
        mesh.SetUVs(3, b3);
        mesh.SetUVs(4, b4);
        mesh.SetUVs(5, b5);
        mesh.SetUVs(6, b6);
        mesh.SetTriangles(tris, 0, false);
        mesh.RecalculateBounds();
    }

    /// <summary>
    /// Points as quads the Points and Particles shaders turn to face the camera: four vertices per point at the same place,
    /// TEXCOORD0 the corner, COLOR its colour, extra attributes from TEXCOORD2; bounds grown by 4 m as the points spread.
    /// </summary>
    public static Mesh PointQuads(THREE.BufferGeometry g, string name = null)
    {
        var mesh = new Mesh { name = name ?? "Points" };
        mesh.MarkDynamic();
        RefillPoints(mesh, g);
        return mesh;
    }

    /// <summary>Refills a mesh from <see cref="PointQuads"/> from its geometry's draw range.</summary>
    public static void RefillPoints(Mesh mesh, THREE.BufferGeometry g)
    {
        var pos = g.attributes.position;
        int n = pos?.count ?? 0;
        int start = Math.Max(0, g.drawRange.start), end = (int)Math.Min(n, (long)g.drawRange.start + g.drawRange.count);
        int cnt = Math.Max(0, end - start);
        mesh.Clear();
        if (cnt == 0) return;
        mesh.indexFormat = cnt * 4 > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        var v3 = new List<Vector3>(cnt * 4); var v2 = new List<Vector2>(cnt * 4); var cols = new List<Color>(cnt * 4);
        var col = g.attributes.color;
        for (int i = start; i < end; i++)
        {
            var p = new Vector3(pos.array[i * 3], pos.array[i * 3 + 1], pos.array[i * 3 + 2]);
            v3.Add(p); v3.Add(p); v3.Add(p); v3.Add(p);
            v2.Add(new Vector2(0, 0)); v2.Add(new Vector2(1, 0)); v2.Add(new Vector2(1, 1)); v2.Add(new Vector2(0, 1));
            if (col != null)
            {
                int s = col.itemSize;
                var c = new Color(col.array[i * s], col.array[i * s + 1], col.array[i * s + 2], s > 3 ? col.array[i * s + 3] : 1);
                cols.Add(c); cols.Add(c); cols.Add(c); cols.Add(c);
            }
        }
        mesh.SetVertices(v3);
        mesh.SetUVs(0, v2);
        if (cols.Count == v3.Count) mesh.SetColors(cols);
        int slot = 2;
        foreach (var attrName in ExtraNames(g))
        {
            if (slot > 7) break;
            var a = g.attributes[attrName];
            if (a.count != n) continue;
            var v4 = new List<Vector4>(cnt * 4);
            int s = a.itemSize;
            for (int i = start; i < end; i++)
            {
                var q = new Vector4(a.array[i * s], s > 1 ? a.array[i * s + 1] : 0, s > 2 ? a.array[i * s + 2] : 0, s > 3 ? a.array[i * s + 3] : 0);
                v4.Add(q); v4.Add(q); v4.Add(q); v4.Add(q);
            }
            mesh.SetUVs(slot, v4);
            slot++;
        }
        var tris = new List<int>(cnt * 6);
        for (int i = 0; i < cnt; i++)
        {
            int b = i * 4;
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
        }
        mesh.SetTriangles(tris, 0, false);
        mesh.RecalculateBounds();
        var bounds = mesh.bounds;
        bounds.Expand(4f);
        mesh.bounds = bounds;
    }

    /// <summary>
    /// Fills a writable MeshData (Mesh.AllocateWritableMeshData) with a plain triangle mesh: position, normal, uv and colour
    /// when present. Safe on a worker thread; the loader (S5) applies it on the main thread. Meshes with extra attributes,
    /// skinning or several groups go through <see cref="ToMesh"/> instead.
    /// </summary>
    public static bool Write(Mesh.MeshData d, THREE.BufferGeometry g)
    {
        var pos = g.attributes.position;
        int n = pos?.count ?? 0;
        if (n == 0 || ExtraNames(g).Count > 0 || g.attributes.skinIndex != null) return false;
        var nor = g.attributes.normal; var uv = g.attributes.uv; var col = g.attributes.color;
        bool hasN = nor != null && nor.count == n, hasUv = uv != null && uv.count == n, hasC = col != null && col.count == n;
        // one stream per attribute, numbered without gaps
        int sN = hasN ? 1 : -1, sC = hasC ? (hasN ? 2 : 1) : -1, sUv = hasUv ? 1 + (hasN ? 1 : 0) + (hasC ? 1 : 0) : -1;
        var layout = new List<VertexAttributeDescriptor> { new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0) };
        if (hasN) layout.Add(new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, sN));
        if (hasC) layout.Add(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4, sC));
        if (hasUv) layout.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, sUv));
        d.SetVertexBufferParams(n, layout.ToArray());
        var vp = d.GetVertexData<Vector3>(0);
        for (int i = 0; i < n; i++) vp[i] = new Vector3(pos.array[i * 3], pos.array[i * 3 + 1], pos.array[i * 3 + 2]);
        if (hasN)
        {
            var vn = d.GetVertexData<Vector3>(sN);
            for (int i = 0; i < n; i++) vn[i] = new Vector3(nor.array[i * 3], nor.array[i * 3 + 1], nor.array[i * 3 + 2]);
        }
        if (hasC)
        {
            var vc = d.GetVertexData<Color>(sC);
            int s = col.itemSize;
            for (int i = 0; i < n; i++) vc[i] = new Color(col.array[i * s], col.array[i * s + 1], col.array[i * s + 2], s > 3 ? col.array[i * s + 3] : 1);
        }
        if (hasUv)
        {
            var vu = d.GetVertexData<Vector2>(sUv);
            for (int i = 0; i < n; i++) vu[i] = new Vector2(uv.array[i * 2], uv.array[i * 2 + 1]);
        }
        var tris = new List<int>();
        Triangles(g, n, g.drawRange.start, g.drawRange.count, tris);
        bool wide = n > 65000;
        d.SetIndexBufferParams(tris.Count, wide ? IndexFormat.UInt32 : IndexFormat.UInt16);
        if (wide) { var ix = d.GetIndexData<int>(); for (int i = 0; i < tris.Count; i++) ix[i] = tris[i]; }
        else { var ix = d.GetIndexData<ushort>(); for (int i = 0; i < tris.Count; i++) ix[i] = (ushort)tris[i]; }
        d.subMeshCount = 1;
        d.SetSubMesh(0, new SubMeshDescriptor(0, tris.Count));
        return true;
    }
}
