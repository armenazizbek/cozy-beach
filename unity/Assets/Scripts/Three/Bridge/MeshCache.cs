using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace THREE.Bridge
{
    /// <summary>
    /// Turns BufferGeometry into Unity meshes. Positions and normals get z negated and triangles are wound the other way round,
    /// which is how a right-handed Three.js model looks the same in Unity's left-handed space.
    ///
    /// Vertex streams: TEXCOORD0 uv, COLOR colour, TEXCOORD1 instance colour (rgb) + instance alpha slot (w),
    /// TEXCOORD2..7 any other attributes (sorted by name, four floats each), TEXCOORD3 is also used for the instance origin when an
    /// InstancedMesh is baked. Custom shaders document which slots they read.
    /// </summary>
    internal static class MeshCache
    {
        private class Entry
        {
            public UnityEngine.Mesh mesh;
            public int version = int.MinValue;
            public int structure = int.MinValue;
            public int vertexCount = -1;
        }

        // keys: geometry plus a variant (plain, multi-material, points, lines, linestrip, baked instances)
        private static readonly Dictionary<(BufferGeometry, int), Entry> cache = new Dictionary<(BufferGeometry, int), Entry>();
        private static readonly Dictionary<InstancedMesh, Entry> baked = new Dictionary<InstancedMesh, Entry>();
        private static readonly Dictionary<Points, Entry> points = new Dictionary<Points, Entry>();

        public const int PLAIN = 0, GROUPS = 1, LINES = 2, LINESTRIP = 3;

        /// <summary>Goes up when the cache is emptied, so entries remembered on geometries are looked up again.</summary>
        private static int gen = 1;

        public static void Release(BufferGeometry g)
        {
            g.bridgeEntry = null;
            var dead = new List<(BufferGeometry, int)>();
            foreach (var k in cache.Keys) if (k.Item1 == g) dead.Add(k);
            foreach (var k in dead)
            {
                if (cache[k].mesh != null) UnityEngine.Object.Destroy(cache[k].mesh);
                cache.Remove(k);
            }
        }

        public static void Release(Object3D o)
        {
            if (o is InstancedMesh im && baked.TryGetValue(im, out var e))
            {
                if (e.mesh != null) UnityEngine.Object.Destroy(e.mesh);
                baked.Remove(im);
            }
            if (o is Points p && points.TryGetValue(p, out var pe))
            {
                if (pe.mesh != null) UnityEngine.Object.Destroy(pe.mesh);
                points.Remove(p);
            }
        }

        /// <summary>Destroys every Unity mesh made so far (see WebGLRenderer.Teardown).</summary>
        public static void Clear()
        {
            foreach (var e in cache.Values) if (e.mesh != null) UnityEngine.Object.Destroy(e.mesh);
            foreach (var e in baked.Values) if (e.mesh != null) UnityEngine.Object.Destroy(e.mesh);
            foreach (var e in points.Values) if (e.mesh != null) UnityEngine.Object.Destroy(e.mesh);
            gen++;
            cache.Clear();
            baked.Clear();
            points.Clear();
        }

        public static UnityEngine.Mesh Get(BufferGeometry g, int variant)
        {
            Entry e;
            if (g.bridgeEntry is Entry known && g.bridgeVariant == variant && g.bridgeGen == gen) e = known;
            else
            {
                var key = (g, variant);
                if (!cache.TryGetValue(key, out e))
                {
                    e = new Entry { mesh = new UnityEngine.Mesh { name = g.type } };
                    cache[key] = e;
                }
                g.bridgeEntry = e;
                g.bridgeVariant = variant;
                g.bridgeGen = gen;
            }
            int v = g.ContentVersion();
            if (v != e.version)
            {
                long t0 = BridgeStats.Ticks();
                Build(g, variant, e);
                e.version = v;
                BridgeStats.Built(string.IsNullOrEmpty(g.name) ? g.type : g.name, g.attributes.position?.count ?? 0, BridgeStats.Ms(t0));
            }
            return e.mesh;
        }

        private static readonly List<UnityEngine.Vector3> v3 = new List<UnityEngine.Vector3>();
        private static readonly List<UnityEngine.Vector2> v2 = new List<UnityEngine.Vector2>();
        private static readonly List<UnityEngine.Vector4> v4 = new List<UnityEngine.Vector4>();
        private static readonly List<UnityEngine.Color> cols = new List<UnityEngine.Color>();
        private static readonly List<int> tris = new List<int>();

        private static readonly HashSet<string> standard = new HashSet<string> { "position", "normal", "uv", "color", "skinIndex", "skinWeight" };

        /// <summary>Names of the extra attributes, in the order they go into TEXCOORD2 onwards.</summary>
        public static List<string> ExtraNames(BufferGeometry g)
        {
            var names = new List<string>();
            foreach (var k in g.attributes.Keys) if (!standard.Contains(k)) names.Add(k);
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        private static void Build(BufferGeometry g, int variant, Entry e)
        {
            var mesh = e.mesh;
            var pos = g.attributes.position;
            int n = pos?.count ?? 0;
            bool structural = g.structureVersion != e.structure || n != e.vertexCount;
            if (structural) mesh.Clear();
            e.structure = g.structureVersion;
            e.vertexCount = n;
            if (n == 0) return;
            mesh.indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            v3.Clear();
            for (int i = 0; i < n; i++) v3.Add(new UnityEngine.Vector3(pos.array[i * 3], pos.array[i * 3 + 1], -pos.array[i * 3 + 2]));
            mesh.SetVertices(v3);
            var nor = g.attributes.normal;
            if (nor != null && nor.count == n)
            {
                v3.Clear();
                for (int i = 0; i < n; i++) v3.Add(new UnityEngine.Vector3(nor.array[i * 3], nor.array[i * 3 + 1], -nor.array[i * 3 + 2]));
                mesh.SetNormals(v3);
            }
            var uv = g.attributes.uv;
            if (uv != null && uv.count == n)
            {
                v2.Clear();
                for (int i = 0; i < n; i++) v2.Add(new UnityEngine.Vector2(uv.array[i * 2], uv.array[i * 2 + 1]));
                mesh.SetUVs(0, v2);
            }
            var col = g.attributes.color;
            if (col != null && col.count == n)
            {
                cols.Clear();
                int s = col.itemSize;
                for (int i = 0; i < n; i++)
                    cols.Add(new UnityEngine.Color(col.array[i * s], col.array[i * s + 1], col.array[i * s + 2], s > 3 ? col.array[i * s + 3] : 1));
                mesh.SetColors(cols);
            }
            int slot = 2;
            foreach (var name in ExtraNames(g))
            {
                if (slot > 7) break;
                var a = g.attributes[name];
                if (a.isInstanced || a.count != n) continue;
                v4.Clear();
                int s = a.itemSize;
                for (int i = 0; i < n; i++)
                    v4.Add(new UnityEngine.Vector4(a.array[i * s], s > 1 ? a.array[i * s + 1] : 0, s > 2 ? a.array[i * s + 2] : 0, s > 3 ? a.array[i * s + 3] : 0));
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
            if (structural || g.index != null)
            {
                if (variant == LINES || variant == LINESTRIP)
                {
                    tris.Clear();
                    int total = g.index != null ? g.index.count : n;
                    for (int i = 0; i < total; i++) tris.Add(g.index != null ? (int)g.index.array[i] : i);
                    mesh.subMeshCount = 1;
                    mesh.SetIndices(tris, variant == LINES ? MeshTopology.Lines : MeshTopology.LineStrip, 0, false);
                }
                else if (variant == GROUPS && g.groups.Count > 0)
                {
                    mesh.subMeshCount = g.groups.Count;
                    for (int gi = 0; gi < g.groups.Count; gi++)
                    {
                        var gr = g.groups[gi];
                        FillTriangles(g, n, gr.start, gr.count);
                        mesh.SetTriangles(tris, gi, false);
                    }
                }
                else
                {
                    mesh.subMeshCount = 1;
                    FillTriangles(g, n, g.drawRange.start, g.drawRange.count);
                    mesh.SetTriangles(tris, 0, false);
                }
            }
            mesh.RecalculateBounds();
        }

        private static void FillTriangles(BufferGeometry g, int n, int start, int count)
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
                // reversed winding for the mirrored space
                tris.Add(a); tris.Add(c); tris.Add(b);
            }
        }

        /// <summary>
        /// An InstancedMesh becomes one mesh with every instance already placed, so it needs no GPU instancing. TEXCOORD1 carries the
        /// instance colour (rgb), TEXCOORD3 the instance origin (xyz) and index (w); instanced attributes go into TEXCOORD2.
        /// </summary>
        public static UnityEngine.Mesh GetBaked(InstancedMesh im)
        {
            if (!baked.TryGetValue(im, out var e))
            {
                e = new Entry { mesh = new UnityEngine.Mesh { name = "Instanced " + im.geometry.type } };
                e.mesh.MarkDynamic();
                baked[im] = e;
            }
            int v = im.geometry.ContentVersion() * 31 + im.instanceMatrix.version * 17 + (im.instanceColor?.version ?? 0) * 13 + im.count * 7;
            foreach (var a in im.geometry.attributes.Values) if (a.isInstanced) v += a.version * 3;
            if (v == e.version) return e.mesh;
            e.version = v;
            long t0 = BridgeStats.Ticks();
            Bake(im, e.mesh);
            BridgeStats.Built("Instanced " + (string.IsNullOrEmpty(im.name) ? im.geometry.type : im.name), e.mesh.vertexCount, BridgeStats.Ms(t0));
            return e.mesh;
        }

        private static readonly List<UnityEngine.Vector3> bn = new List<UnityEngine.Vector3>();
        private static readonly List<UnityEngine.Vector4> b1 = new List<UnityEngine.Vector4>(), b2 = new List<UnityEngine.Vector4>(), b3 = new List<UnityEngine.Vector4>(),
            b4 = new List<UnityEngine.Vector4>(), b5 = new List<UnityEngine.Vector4>(), b6 = new List<UnityEngine.Vector4>();

        private static void Bake(InstancedMesh im, UnityEngine.Mesh mesh)
        {
            var g = im.geometry;
            var pos = g.attributes.position;
            var nor = g.attributes.normal;
            var uv = g.attributes.uv;
            var col = g.attributes.color;
            int n = pos.count, count = Math.Min(im.count, im.instanceMatrix.count);
            BufferAttribute instAttr = null;
            foreach (var name in ExtraNames(g)) if (g.attributes[name].isInstanced) { instAttr = g.attributes[name]; break; }
            mesh.Clear();
            int total = n * count;
            mesh.indexFormat = total > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            v3.Clear(); bn.Clear(); v2.Clear(); cols.Clear(); b1.Clear(); b2.Clear(); b3.Clear(); b4.Clear(); b5.Clear(); b6.Clear(); tris.Clear();
            var m = new Matrix4();
            var nm = new Matrix3();
            var p = new Vector3();
            var e = im.instanceMatrix.array;
            for (int k = 0; k < count; k++)
            {
                for (int q = 0; q < 16; q++) m.elements[q] = e[k * 16 + q];
                nm.getNormalMatrix(m);
                float cr = 1, cg = 1, cb = 1;
                if (im.instanceColor != null) { cr = im.instanceColor.array[k * 3]; cg = im.instanceColor.array[k * 3 + 1]; cb = im.instanceColor.array[k * 3 + 2]; }
                float ia = 0, ib = 0, ic = 0, id = 0;
                if (instAttr != null)
                {
                    int s = instAttr.itemSize;
                    ia = instAttr.array[k * s];
                    if (s > 1) ib = instAttr.array[k * s + 1];
                    if (s > 2) ic = instAttr.array[k * s + 2];
                    if (s > 3) id = instAttr.array[k * s + 3];
                }
                var origin = new UnityEngine.Vector4((float)m.elements[12], (float)m.elements[13], (float)-m.elements[14], k);
                // the instance's x and z axes, in Three's space, so a shader can move a vertex in the instance's own frame
                var col0 = new UnityEngine.Vector4((float)m.elements[0], (float)m.elements[1], (float)m.elements[2], 0);
                var col2 = new UnityEngine.Vector4((float)m.elements[8], (float)m.elements[9], (float)m.elements[10], 0);
                for (int i = 0; i < n; i++)
                {
                    p.set(pos.array[i * 3], pos.array[i * 3 + 1], pos.array[i * 3 + 2]).applyMatrix4(m);
                    v3.Add(new UnityEngine.Vector3((float)p.x, (float)p.y, (float)-p.z));
                    if (nor != null)
                    {
                        p.set(nor.array[i * 3], nor.array[i * 3 + 1], nor.array[i * 3 + 2]).applyNormalMatrix(nm);
                        bn.Add(new UnityEngine.Vector3((float)p.x, (float)p.y, (float)-p.z));
                    }
                    if (uv != null) v2.Add(new UnityEngine.Vector2(uv.array[i * 2], uv.array[i * 2 + 1]));
                    if (col != null)
                    {
                        int s = col.itemSize;
                        cols.Add(new UnityEngine.Color(col.array[i * s], col.array[i * s + 1], col.array[i * s + 2], s > 3 ? col.array[i * s + 3] : 1));
                    }
                    b1.Add(new UnityEngine.Vector4(cr, cg, cb, 1));
                    b2.Add(new UnityEngine.Vector4(ia, ib, ic, id));
                    b3.Add(origin);
                    b4.Add(new UnityEngine.Vector4(pos.array[i * 3], pos.array[i * 3 + 1], pos.array[i * 3 + 2], 0));
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
                    tris.Add(baseV + a); tris.Add(baseV + c); tris.Add(baseV + b);
                }
            }
            mesh.SetVertices(v3);
            if (bn.Count == v3.Count) mesh.SetNormals(bn);
            if (v2.Count == v3.Count) mesh.SetUVs(0, v2);
            if (cols.Count == v3.Count) mesh.SetColors(cols);
            mesh.SetUVs(1, b1);
            mesh.SetUVs(2, b2);
            mesh.SetUVs(3, b3);
            // TEXCOORD4: the vertex before the instance matrix (Three axes); TEXCOORD5 and 6: the instance's x and z axes
            mesh.SetUVs(4, b4);
            mesh.SetUVs(5, b5);
            mesh.SetUVs(6, b6);
            mesh.SetTriangles(tris, 0, false);
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// Points become small quads the Points shader turns to face the camera: every point is four vertices at the same position,
        /// TEXCOORD0 the corner (0..1), COLOR its colour, extra attributes from TEXCOORD2.
        /// </summary>
        public static UnityEngine.Mesh GetPoints(Points pts)
        {
            if (!points.TryGetValue(pts, out var e))
            {
                e = new Entry { mesh = new UnityEngine.Mesh { name = "Points" } };
                e.mesh.MarkDynamic();
                points[pts] = e;
            }
            int v = pts.geometry.ContentVersion();
            if (v == e.version) return e.mesh;
            e.version = v;
            var g = pts.geometry;
            var pos = g.attributes.position;
            int n = pos?.count ?? 0;
            int start = Math.Max(0, g.drawRange.start), end = (int)Math.Min(n, (long)g.drawRange.start + g.drawRange.count);
            int cnt = Math.Max(0, end - start);
            bool structural = cnt * 4 != e.vertexCount;
            var mesh = e.mesh;
            if (structural) mesh.Clear();
            e.vertexCount = cnt * 4;
            if (cnt == 0) return mesh;
            mesh.indexFormat = cnt * 4 > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            v3.Clear(); v2.Clear(); cols.Clear();
            var col = g.attributes.color;
            for (int i = start; i < end; i++)
            {
                var p = new UnityEngine.Vector3(pos.array[i * 3], pos.array[i * 3 + 1], -pos.array[i * 3 + 2]);
                v3.Add(p); v3.Add(p); v3.Add(p); v3.Add(p);
                if (structural)
                {
                    v2.Add(new UnityEngine.Vector2(0, 0)); v2.Add(new UnityEngine.Vector2(1, 0));
                    v2.Add(new UnityEngine.Vector2(1, 1)); v2.Add(new UnityEngine.Vector2(0, 1));
                }
                if (col != null)
                {
                    int s = col.itemSize;
                    var c = new UnityEngine.Color(col.array[i * s], col.array[i * s + 1], col.array[i * s + 2], s > 3 ? col.array[i * s + 3] : 1);
                    cols.Add(c); cols.Add(c); cols.Add(c); cols.Add(c);
                }
            }
            mesh.SetVertices(v3);
            if (structural) mesh.SetUVs(0, v2);
            if (cols.Count == v3.Count) mesh.SetColors(cols);
            int slot = 2;
            foreach (var name in ExtraNames(g))
            {
                if (slot > 7) break;
                var a = g.attributes[name];
                if (a.count != n) continue;
                v4.Clear();
                int s = a.itemSize;
                for (int i = start; i < end; i++)
                {
                    var q = new UnityEngine.Vector4(a.array[i * s], s > 1 ? a.array[i * s + 1] : 0, s > 2 ? a.array[i * s + 2] : 0, s > 3 ? a.array[i * s + 3] : 0);
                    v4.Add(q); v4.Add(q); v4.Add(q); v4.Add(q);
                }
                mesh.SetUVs(slot, v4);
                slot++;
            }
            if (structural)
            {
                tris.Clear();
                for (int i = 0; i < cnt; i++)
                {
                    int b = i * 4;
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                }
                mesh.SetTriangles(tris, 0, false);
            }
            // points spread out in the shader, so keep generous bounds
            mesh.RecalculateBounds();
            var bounds = mesh.bounds;
            bounds.Expand(4f);
            mesh.bounds = bounds;
            return mesh;
        }
    }
}
