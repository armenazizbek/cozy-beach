using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Self-checks for <see cref="Picking"/> (S7): rays cast at the loaded world must find what THREE.Raycaster finds, the
/// same nearest object at the same distance, for the camp's clickables and for everything the rangefinder measures to.
/// </summary>
internal static class SelfCheckPicking
{
    internal static IEnumerable<(string, Func<string>)> Checks()
    {
        yield return ("Picking matches Raycaster on the camp's clickables", Camp);
        yield return ("Picking matches Raycaster on the rangefinder's targets", Range);
        yield return ("The rangefinder reads the same off the drawn GameObjects", DrawnRange);
    }

    private static Game G => GameHost.Instance != null ? GameHost.Instance.game : null;

    private static readonly Random rnd = new Random(77);
    private static double R(double a) => (rnd.NextDouble() * 2 - 1) * a;

    private static string Camp()
    {
        var g = G;
        if (g == null) return "no game";
        var targets = g.camp.clickables;
        if (targets.Count == 0) return "no clickables";
        var rays = new List<(THREE.Vector3 o, THREE.Vector3 d)>();
        for (int i = 0; i < 400; i++)
        {
            var c = targets[i % targets.Count];
            c.updateWorldMatrix(true, false);
            var at = new THREE.Vector3().setFromMatrixPosition(c.matrixWorld);
            var to = new THREE.Vector3(at.x + R(1.5), at.y + 0.6 + R(1), at.z + R(1.5));
            double ang = rnd.NextDouble() * Math.PI * 2, dist = 2 + rnd.NextDouble() * 12;
            var from = new THREE.Vector3(to.x + Math.Cos(ang) * dist, 0.6 + rnd.NextDouble() * 2.5, to.z + Math.Sin(ang) * dist);
            rays.Add((from, new THREE.Vector3().subVectors(to, from).normalize()));
        }
        return Compare(rays, targets, 0);
    }

    private static string Range()
    {
        var g = G;
        if (g == null) return "no game";
        var targets = g.RangeTargets();
        var rays = new List<(THREE.Vector3 o, THREE.Vector3 d)>();
        for (int i = 0; i < 400; i++)
        {
            var from = new THREE.Vector3(R(70), 1.5 + rnd.NextDouble() * 12, R(90));
            var d = new THREE.Vector3(R(1), -0.05 - rnd.NextDouble() * 0.3, R(1)).normalize();
            rays.Add((from, d));
        }
        return Compare(rays, targets, 0.5);
    }

    /// <summary>
    /// The binoculars' range from the GameObjects themselves (Picking.NearestDrawn) against the descriptions' (as before B1),
    /// as the player reads it: in whole metres. A few rays may differ where the description is hit inside a hidden group
    /// (a gull that has flown away) or on a back face, which the drawn objects do not count.
    /// </summary>
    private static string DrawnRange()
    {
        var g = G;
        if (g == null) return "no game";
        var targets = g.RangeTargets();
        // the modules that still keep descriptions: gulls and crabs are posed from numbers now (no description to compare with)
        var roots = g.RangeRoots().Where(r => r != g.birds.Root && r != g.ecology.Root && r != g.treasure.Root).ToArray();
        int hits = 0, bad = 0, n = 400;
        string first = null;
        for (int i = 0; i < n; i++)
        {
            var o = new THREE.Vector3(R(70), 1.5 + rnd.NextDouble() * 12, R(90));
            var d = new THREE.Vector3(R(1), -0.05 - rnd.NextDouble() * 0.3, R(1)).normalize();
            var rc = new THREE.Raycaster(o, d);
            var before = Picking.Intersect(rc, targets).FirstOrDefault(h => h.@object.visible && h.distance > 0.5);
            var now = Picking.NearestDrawn(GameSpace.V(o.x, o.y, o.z), GameSpace.V(d.x, d.y, d.z), roots, 0.5);
            double? a = before?.distance;
            if (a != null) hits++;
            bool same = (a == null && now == null) || (a != null && now != null && Math.Round(a.Value) == Math.Round(now.Value));
            if (same) continue;
            bad++;
            first ??= $"ray from {o.x:0.##},{o.y:0.##},{o.z:0.##}: before {(a == null ? "nothing" : $"{Name(before.@object)} at {a:0.##}")}, now {(now == null ? "nothing" : $"{now:0.##}")}";
        }
        UnityEngine.Debug.Log($"[SelfCheck] drawn range: {n} rays, {hits} hits, {bad} different" + (first != null ? $", first: {first}" : ""));
        return bad <= n / 100 ? (hits < n / 10 ? $"only {hits} of {n} rays hit anything" : null) : $"{bad} of {n} rays differ, first: {first}";
    }

    private static string Compare(List<(THREE.Vector3 o, THREE.Vector3 d)> rays, List<THREE.Object3D> targets, double near)
    {
        int hits = 0, bad = 0;
        string first = null;
        foreach (var (o, d) in rays)
        {
            var rc = new THREE.Raycaster(o, d) { near = near };
            var three = rc.intersectObjects(targets, true);
            var mine = Picking.Intersect(rc, targets);
            var a = three.Count > 0 ? three[0] : null;
            var b = mine.Count > 0 ? mine[0] : null;
            if (a != null) hits++;
            bool same = (a == null && b == null)
                || (a != null && b != null && Math.Abs(a.distance - b.distance) < 0.01 + a.distance * 1e-5);
            if (same) continue;
            bad++;
            first ??= $"ray from {o.x:0.##},{o.y:0.##},{o.z:0.##} dir {d.x:0.###},{d.y:0.###},{d.z:0.###}: Raycaster " +
                (a == null ? "nothing" : $"{Name(a.@object)} at {a.distance:0.####}") + ", Picking " +
                (b == null ? "nothing" : $"{Name(b.@object)} at {b.distance:0.####}") + Detail(rc, a, b);
        }
        UnityEngine.Debug.Log($"[SelfCheck] picking: {rays.Count} rays, {hits} hits, {bad} different");
        return bad == 0 ? (hits < rays.Count / 10 ? $"only {hits} of {rays.Count} rays hit anything" : null) : $"{bad} of {rays.Count} rays differ, first: {first}";
    }

    private static string Detail(THREE.Raycaster rc, THREE.Intersection a, THREE.Intersection b)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var x in new[] { a, b })
        {
            if (x == null) continue;
            var o = x.@object;
            var t = rc.intersectObject(o, false);
            var p = Picking.Intersect(rc, o);
            sb.Append($" | {Name(o)} {o.GetType().Name}: Three {t.Count} hits first {(t.Count > 0 ? t[0].distance.ToString("0.###") : "-")}, Picking {(p.Count > 0 ? p[0].distance.ToString("0.###") : "-")}");
            if (o is THREE.Mesh m && m.geometry != null)
            {
                m.geometry.computeBoundingSphere();
                m.geometry.computeBoundingBox();
                var t2 = rc.intersectObject(o, false);
                sb.Append($" fresh-bounds Three {t2.Count} first {(t2.Count > 0 ? t2[0].distance.ToString("0.###") : "-")}");
                sb.Append($" verts {m.geometry.attributes.position?.count} idx {m.geometry.index?.count} range {m.geometry.drawRange.start}+{m.geometry.drawRange.count} groups {m.geometry.groups.Count} multi {m.isMultiMaterial} side {m.material?.side} det {m.matrixWorld.determinant():0.###}");
            }
        }
        return sb.ToString();
    }

    private static string Name(THREE.Object3D o) => (string.IsNullOrEmpty(o.name) ? o.type : o.name) + (o.parent != null ? " in " + (string.IsNullOrEmpty(o.parent.name) ? o.parent.type : o.parent.name) : "");
}
