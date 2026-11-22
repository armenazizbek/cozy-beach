using System;
using System.Collections.Generic;
using UnityEngine;
using B = THREE.Bridge;

/// <summary>
/// MeshKit against the bridge (S3): for every kind of geometry the game makes, MeshKit's mesh must hold exactly the arrays the
/// bridge's MeshCache built for the same geometry (the bridge draws with the world-root convention since S2).
/// </summary>
internal static class SelfCheckMeshKit
{
    internal static IEnumerable<(string, Func<string>)> Checks()
    {
        yield return ("MeshKit.ToMesh matches the bridge for every generator", Generators);
        yield return ("MeshKit.ToMesh groups, colours, extras, draw range", Specials);
        yield return ("MeshKit.ToLines matches the bridge", Lines);
        yield return ("MeshKit.BakeInstances matches the bridge", Instances);
        yield return ("MeshKit.PointQuads matches the bridge", PointsCheck);
        yield return ("MeshKit.Write (off-thread path) matches ToMesh", WriteCheck);
    }

    private static List<(string name, THREE.BufferGeometry g)> Table()
    {
        var shape = new THREE.Shape();
        shape.moveTo(0, 0); shape.lineTo(1, 0); shape.lineTo(1, 0.6); shape.lineTo(0.4, 1); shape.lineTo(0, 0.6); shape.lineTo(0, 0);
        var curve = new THREE.CatmullRomCurve3(new List<THREE.Vector3>
            { new THREE.Vector3(0, 0, 0), new THREE.Vector3(0.3, 1, 0.1), new THREE.Vector3(-0.2, 2, 0.4), new THREE.Vector3(0.5, 3, 0.2) });
        var lathe = new List<THREE.Vector2> { new THREE.Vector2(0, 0), new THREE.Vector2(0.5, 0.2), new THREE.Vector2(0.4, 1), new THREE.Vector2(0.1, 1.3) };
        return new List<(string, THREE.BufferGeometry)>
        {
            ("box", new THREE.BoxGeometry(1, 2, 3, 2, 3, 4)),
            ("cylinder", new THREE.CylinderGeometry(0.2, 0.3, 2, 8, 3)),
            ("open cylinder", new THREE.CylinderGeometry(0.5, 0.5, 1, 12, 1, true)),
            ("cone", new THREE.ConeGeometry(0.4, 1.2, 7)),
            ("sphere", new THREE.SphereGeometry(1, 16, 12)),
            ("half sphere", new THREE.SphereGeometry(0.8, 10, 8, 0, Math.PI * 2, 0, Math.PI / 2)),
            ("torus", new THREE.TorusGeometry(1, 0.3, 8, 16)),
            ("plane", new THREE.PlaneGeometry(2, 3, 4, 5)),
            ("circle", new THREE.CircleGeometry(1, 12)),
            ("ring", new THREE.RingGeometry(0.5, 1, 16, 2)),
            ("lathe", new THREE.LatheGeometry(lathe, 10)),
            ("tube", new THREE.TubeGeometry(curve, 14, 0.2, 8)),
            ("extrude", new THREE.ExtrudeGeometry(shape, new THREE.ExtrudeGeometryOptions { depth = 0.3, bevelEnabled = true, bevelThickness = 0.05, bevelSize = 0.04 })),
            ("shape", new THREE.ShapeGeometry(shape)),
            ("icosahedron", new THREE.IcosahedronGeometry(1, 1)),
            ("octahedron", new THREE.OctahedronGeometry(0.7)),
            ("dodecahedron", new THREE.DodecahedronGeometry(0.9)),
            ("capsule", new THREE.CapsuleGeometry(0.3, 1, 4, 8)),
            ("rounded box", new THREE.RoundedBoxGeometry(1, 0.5, 0.8, 2, 0.1)),
            ("non-indexed sphere", new THREE.SphereGeometry(0.5, 8, 6).toNonIndexed()),
            ("transformed box", new THREE.BoxGeometry(1, 1, 1).rotateX(0.4).rotateY(1.1).translate(0.3, -2, 5).scale(1, 2, -1)),
        };
    }

    private static string Generators()
    {
        foreach (var (name, g) in Table())
        {
            var bridge = B.MeshCache.Get(g, B.MeshCache.PLAIN);
            var mine = MeshKit.ToMesh(g);
            var diff = Compare(bridge, mine);
            UnityEngine.Object.Destroy(mine);
            if (diff != null) return name + ": " + diff;
        }
        return null;
    }

    private static string Specials()
    {
        // merged with groups (a multi-material mesh)
        var merged = THREE.BufferGeometryUtils.mergeGeometries(new List<THREE.BufferGeometry>
            { new THREE.BoxGeometry(1, 1, 1), new THREE.SphereGeometry(0.5, 8, 6).translate(0, 1, 0), new THREE.CylinderGeometry(0.1, 0.1, 1, 6) }, true);
        var d = Compare(B.MeshCache.Get(merged, B.MeshCache.GROUPS), MeshKit.ToMesh(merged, groups: true));
        if (d != null) return "groups: " + d;
        // vertex colours and two extra attributes, as the shaders read them
        var g = new THREE.SphereGeometry(0.6, 10, 8);
        int n = g.attributes.position.count;
        var col = new float[n * 3]; var e1 = new float[n]; var e2 = new float[n * 2];
        var rnd = new System.Random(7);
        for (int i = 0; i < col.Length; i++) col[i] = (float)rnd.NextDouble();
        for (int i = 0; i < e1.Length; i++) e1[i] = (float)rnd.NextDouble();
        for (int i = 0; i < e2.Length; i++) e2[i] = (float)rnd.NextDouble();
        g.setAttribute("color", new THREE.Float32BufferAttribute(col, 3));
        g.setAttribute("aPhase", new THREE.Float32BufferAttribute(e1, 1));
        g.setAttribute("aBend", new THREE.Float32BufferAttribute(e2, 2));
        d = Compare(B.MeshCache.Get(g, B.MeshCache.PLAIN), MeshKit.ToMesh(g));
        if (d != null) return "colours and extras: " + d;
        // a draw range
        var dr = new THREE.PlaneGeometry(1, 1, 6, 6);
        dr.setDrawRange(6, 30);
        d = Compare(B.MeshCache.Get(dr, B.MeshCache.PLAIN), MeshKit.ToMesh(dr));
        return d == null ? null : "draw range: " + d;
    }

    private static string Lines()
    {
        var pts = new float[] { 0, 0, 0, 1, 2, 3, -1, 0.5f, 2, 4, 4, -4, 0, 1, 0, 2, 2, 2 };
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pts, 3));
        var d = Compare(B.MeshCache.Get(g, B.MeshCache.LINES), MeshKit.ToLines(g, true));
        if (d != null) return "segments: " + d;
        d = Compare(B.MeshCache.Get(g, B.MeshCache.LINESTRIP), MeshKit.ToLines(g, false));
        return d == null ? null : "strip: " + d;
    }

    private static string Instances()
    {
        var geo = new THREE.ConeGeometry(0.1, 0.4, 5);
        var im = new THREE.InstancedMesh(geo, new THREE.MeshStandardMaterial(), 12);
        var m = new THREE.Matrix4();
        var q = new THREE.Quaternion();
        for (int i = 0; i < 12; i++)
        {
            q.setFromEuler(new THREE.Euler(i * 0.3, i * 0.7, -i * 0.2));
            m.compose(new THREE.Vector3(i, i * 0.5, -i * 2), q, new THREE.Vector3(1 + i * 0.1, 1, 0.8));
            im.setMatrixAt(i, m);
            im.setColorAt(i, new THREE.Color(0.1 * i, 0.5, 1 - 0.05 * i));
        }
        var bridge = B.MeshCache.GetBaked(im);
        var mine = MeshKit.BakeInstances(geo, im.instanceMatrix.array, 12, im.instanceColor.array);
        return Compare(bridge, mine);
    }

    private static string PointsCheck()
    {
        var g = new THREE.BufferGeometry();
        var rnd = new System.Random(3);
        var p = new float[30 * 3]; var c = new float[30 * 3]; var s = new float[30];
        for (int i = 0; i < p.Length; i++) { p[i] = (float)(rnd.NextDouble() * 10 - 5); c[i] = (float)rnd.NextDouble(); }
        for (int i = 0; i < s.Length; i++) s[i] = (float)rnd.NextDouble();
        g.setAttribute("position", new THREE.Float32BufferAttribute(p, 3));
        g.setAttribute("color", new THREE.Float32BufferAttribute(c, 3));
        g.setAttribute("aSize", new THREE.Float32BufferAttribute(s, 1));
        g.setDrawRange(4, 20);
        var pts = new THREE.Points(g, new THREE.PointsMaterial());
        return Compare(B.MeshCache.GetPoints(pts), MeshKit.PointQuads(g));
    }

    private static string WriteCheck()
    {
        foreach (var (name, g) in Table())
        {
            var data = Mesh.AllocateWritableMeshData(1);
            if (!MeshKit.Write(data[0], g)) { data.Dispose(); return name + ": not writable"; }
            var mesh = new Mesh();
            Mesh.ApplyAndDisposeWritableMeshData(data, mesh);
            mesh.RecalculateBounds();
            var d = Compare(MeshKit.ToMesh(g), mesh, uvSlotsFrom: 2);
            UnityEngine.Object.Destroy(mesh);
            if (d != null) return name + ": " + d;
        }
        return null;
    }

    /// <summary>Null when the two meshes hold the same arrays (within float noise), else what differs.</summary>
    private static string Compare(Mesh a, Mesh b, int uvSlotsFrom = 8)
    {
        if (a.vertexCount != b.vertexCount) return $"vertices {a.vertexCount} vs {b.vertexCount}";
        if (a.subMeshCount != b.subMeshCount) return $"submeshes {a.subMeshCount} vs {b.subMeshCount}";
        string v = Same(a.vertices, b.vertices, "position") ?? Same(a.normals, b.normals, "normal");
        if (v != null) return v;
        var ca = a.colors; var cb = b.colors;
        if (ca.Length != cb.Length) return $"colours {ca.Length} vs {cb.Length}";
        for (int i = 0; i < ca.Length; i++) if (((Vector4)ca[i] - (Vector4)cb[i]).sqrMagnitude > 1e-10f) return $"colour {i}";
        for (int slot = 0; slot < 8; slot++)
        {
            if (slot >= uvSlotsFrom && slot > 0) break;
            var ua = new List<Vector4>(); var ub = new List<Vector4>();
            a.GetUVs(slot, ua); b.GetUVs(slot, ub);
            if (ua.Count != ub.Count) return $"uv{slot} {ua.Count} vs {ub.Count}";
            for (int i = 0; i < ua.Count; i++) if ((ua[i] - ub[i]).sqrMagnitude > 1e-10f) return $"uv{slot}[{i}] {ua[i]} vs {ub[i]}";
        }
        for (int s = 0; s < a.subMeshCount; s++)
        {
            if (a.GetTopology(s) != b.GetTopology(s)) return $"topology of {s}";
            var ia = a.GetIndices(s); var ib = b.GetIndices(s);
            if (ia.Length != ib.Length) return $"indices of {s}: {ia.Length} vs {ib.Length}";
            for (int i = 0; i < ia.Length; i++) if (ia[i] != ib[i]) return $"index {i} of submesh {s}: {ia[i]} vs {ib[i]}";
        }
        if ((a.bounds.center - b.bounds.center).sqrMagnitude > 1e-8f || (a.bounds.size - b.bounds.size).sqrMagnitude > 1e-8f) return "bounds";
        return null;
    }

    private static string Same(Vector3[] a, Vector3[] b, string what)
    {
        if (a.Length != b.Length) return $"{what} count {a.Length} vs {b.Length}";
        for (int i = 0; i < a.Length; i++) if ((a[i] - b[i]).sqrMagnitude > 1e-10f) return $"{what}[{i}] {a[i]} vs {b[i]}";
        return null;
    }
}
