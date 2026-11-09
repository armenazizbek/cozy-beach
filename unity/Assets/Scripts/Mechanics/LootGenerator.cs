using System;
using System.Collections.Generic;
using System.Linq;

// Port of src/mechanics/LootGenerator.ts: every find's model, built in code.

public class CrustRig
{
    public THREE.Group root;
    public THREE.Mesh crust;
    public HTMLCanvasElement canvas;
    public THREE.CanvasTexture texture;
    public double size;
}

public static class LootGenerator
{
    private static THREE.MeshStandardMaterial metal(int color, double rough = 0.3, double m = 0.9) =>
        new THREE.MeshStandardMaterial { color = color, roughness = rough, metalness = m };

    private static THREE.Mesh add(THREE.Group g, THREE.BufferGeometry geo, THREE.Material mat, double x = 0, double y = 0, double z = 0)
    {
        var m = new THREE.Mesh(geo, mat);
        m.position.set(x, y, z);
        m.castShadow = true;
        g.add(m);
        return m;
    }

    private static THREE.TorusGeometry torusFlat(double r, double tube, double arc = Math.PI * 2)
    {
        var t = new THREE.TorusGeometry(r, tube, 10, 36, arc);
        t.rotateX(Math.PI / 2);
        return t;
    }

    private static List<THREE.Vector2> V2(IEnumerable<double[]> pts) => pts.Select(p => new THREE.Vector2(p[0], p[1])).ToList();

    /// <summary>A flat piece cut from a closed outline with optional holes, with softly rounded edges. It lies in the x-z plane.</summary>
    private static THREE.BufferGeometry plate(IList<double[]> outline, IList<List<double[]>> holes, double depth, double bevel = 0.012)
    {
        var sh = new THREE.Shape(V2(outline));
        foreach (var h in holes) sh.holes.Add(new THREE.Path(V2(h)));
        var g = new THREE.ExtrudeGeometry(sh, new THREE.ExtrudeGeometryOptions { depth = depth, bevelEnabled = true, bevelSize = bevel, bevelThickness = bevel, bevelSegments = 2, curveSegments = 16 });
        g.rotateX(-Math.PI / 2);
        g.translate(0, -depth / 2, 0);
        return g;
    }

    /// <summary>Points on an ellipse, for building outlines and holes.</summary>
    private static List<double[]> ellipse(double cx, double cy, double rx, double ry, double from = 0, double to = Math.PI * 2, int n = 28) =>
        Enumerable.Range(0, n + 1).Select(i =>
        {
            double a = from + (to - from) * i / n;
            return new[] { cx + Math.Cos(a) * rx, cy + Math.Sin(a) * ry };
        }).ToList();

    /// <summary>Gives a piece one colour, so differently coloured pieces can be merged into one mesh.</summary>
    private static THREE.BufferGeometry tint(THREE.BufferGeometry g, int color) => Ecology.paint(g, color);

    private class CoinStyle
    {
        public int color;
        public double rough;
        public double? size;
    }

    private static readonly Dictionary<string, CoinStyle> COIN_STYLE = new Dictionary<string, CoinStyle>
    {
        { "penny", new CoinStyle { color = 0xb87333, rough = 0.38 } },
        { "quarter", new CoinStyle { color = 0xd9dde2, rough = 0.28 } },
        { "doubloon", new CoinStyle { color = 0xffc933, rough = 0.2 } },
        { "dime", new CoinStyle { color = 0xcfd4d9, rough = 0.3, size = 0.78 } },
        { "nickel", new CoinStyle { color = 0xa8b0b7, rough = 0.35, size = 0.88 } },
        { "silver_dollar", new CoinStyle { color = 0xe8ecef, rough = 0.24, size = 1.15 } },
        { "real", new CoinStyle { color = 0xc6cbd0, rough = 0.42, size = 1.0 } },
        { "denarius", new CoinStyle { color = 0x8f6f45, rough = 0.5, size = 0.85 } },
        { "sovereign", new CoinStyle { color = 0xffd85a, rough = 0.16, size = 0.95 } },
        { "piece_eight", new CoinStyle { color = 0xcfcab0, rough = 0.45, size = 1.05 } },
    };

    private static void buildCoin(THREE.Group g, string id)
    {
        var s = COIN_STYLE[id];
        var mat = metal(s.color, s.rough);
        g.scale.setScalar(s.size ?? 1);
        add(g, new THREE.CylinderGeometry(0.35, 0.35, 0.06, 32), mat);
        foreach (var y in new[] { 0.032, -0.032 }) add(g, torusFlat(0.31, 0.017), mat, 0, y, 0);
        // embossed centre
        add(g, new THREE.CylinderGeometry(0.15, 0.15, 0.02, 20), mat, 0, 0.04, 0);
        add(g, new THREE.OctahedronGeometry(0.07), mat, 0, 0.065, 0);
        add(g, new THREE.CylinderGeometry(0.12, 0.12, 0.02, 20), mat, 0, -0.04, 0);
    }

    /// <summary>A soda-can pull-tab: a rounded plate with a big finger ring, a small rivet hole and a slot, bent very slightly.</summary>
    private static void buildPullTab(THREE.Group g)
    {
        var mat = metal(0xd3d7da, 0.28);
        var left = ellipse(-0.2, 0, 0.19, 0.17, Math.PI / 2, 3 * Math.PI / 2, 24);
        var right = ellipse(0.22, 0, 0.12, 0.1, -Math.PI / 2, Math.PI / 2, 16);
        var body = add(g, plate(left.Concat(right).ToList(), new List<List<double[]>> { ellipse(-0.2, 0, 0.115, 0.085), ellipse(0.2, 0, 0.032, 0.032, 0, Math.PI * 2, 14), ellipse(0.03, 0, 0.055, 0.02, 0, Math.PI * 2, 14) }, 0.03, 0.01), mat);
        body.rotation.z = 0.04;
        // a raised rim round the finger ring, and the little tongue that pried the can open
        var rim = add(g, new THREE.TorusGeometry(0.14, 0.012, 6, 28), mat, -0.2, 0.025, 0);
        rim.rotation.x = Math.PI / 2;
        rim.scale.set(1, 0.9, 1);
        var tongue = add(g, new THREE.BoxGeometry(0.1, 0.025, 0.09), mat, 0.33, 0.012, 0);
        tongue.rotation.z = -0.15;
    }

    /// <summary>A long rusty nail with a flat head and a bend in the shaft, tapering to a point.</summary>
    private static void buildNail(THREE.Group g)
    {
        var rust = new THREE.MeshStandardMaterial { color = 0x8a4b22, roughness = 0.9, metalness = 0.35 };
        var dark = new THREE.MeshStandardMaterial { color = 0x5e3216, roughness = 0.95, metalness = 0.2 };
        var path = new THREE.CatmullRomCurve3(new[]
        {
            new THREE.Vector3(-0.3, 0, 0), new THREE.Vector3(-0.1, 0, 0), new THREE.Vector3(0.12, 0, 0.0), new THREE.Vector3(0.26, 0, 0.0),
            new THREE.Vector3(0.36, 0, 0.07), new THREE.Vector3(0.42, 0, 0.16),
        });
        var shaft = new THREE.TubeGeometry(path, 40, 0.036, 8);
        // thick near the head, a sharp point at the far end
        var p = shaft.attributes.position;
        const int ring = 9;
        for (int i = 0; i < p.count; i++)
        {
            double t = Math.Floor((double)i / ring) / 40;
            var c = path.getPoint(Math.Min(1, t));
            double k = t < 0.86 ? 1 - t * 0.1 : Math.Max(0.12, 0.91 - (t - 0.86) * 5.6);
            p.setXYZ(i, c.x + (p.getX(i) - c.x) * k, c.y + (p.getY(i) - c.y) * k, c.z + (p.getZ(i) - c.z) * k);
        }
        shaft.computeVertexNormals();
        add(g, shaft, rust);
        var head = add(g, new THREE.CylinderGeometry(0.085, 0.07, 0.03, 16), dark, -0.31, 0, 0);
        head.rotation.z = Math.PI / 2;
        var dome = add(g, new THREE.SphereGeometry(0.07, 12, 8, 0, Math.PI * 2, 0, Math.PI / 2), dark, -0.325, 0, 0);
        dome.rotation.z = Math.PI / 2;
        // scabs of rust along the shaft
        foreach (var t in new[] { 0.15, 0.3, 0.5 })
        {
            var c = path.getPoint(t);
            add(g, new THREE.SphereGeometry(0.032, 6, 5), dark, c.x, 0.012, c.z).scale.set(1.6, 0.8, 1);
        }
    }

    private static void buildTinCan(THREE.Group g)
    {
        var geo = new THREE.CylinderGeometry(0.2, 0.2, 0.42, 28, 18, true);
        var p = geo.attributes.position;
        for (int i = 0; i < p.count; i++)
        {
            double x = p.getX(i), y = p.getY(i), z = p.getZ(i);
            double ang = Math.Atan2(z, x);
            double ridge = 1 + Math.Sin(y * 48) * 0.05;
            double dent = 1 - Math.Max(0, Math.Sin(ang * 2 + y * 6)) * 0.22 * (0.4 + Math.Abs(y));
            p.setXYZ(i, x * ridge * dent, y, z * ridge * (0.85 + 0.15 * dent));
        }
        geo.computeVertexNormals();
        var mat = new THREE.MeshStandardMaterial { color = 0x9aa0a6, roughness = 0.5, metalness = 0.8, side = THREE.Side.DoubleSide };
        var can = add(g, geo, mat);
        can.rotation.z = Math.PI / 2;
        can.rotation.y = 0.3;
        foreach (var s in new[] { -1, 1 })
        {
            var cap = add(g, new THREE.CylinderGeometry(0.18, 0.18, 0.015, 20), mat, s * 0.21, 0, 0);
            cap.rotation.z = Math.PI / 2;
        }
        var band = add(g, new THREE.CylinderGeometry(0.205, 0.205, 0.14, 24, 1, true, 0.4, 3.6),
            new THREE.MeshStandardMaterial { color = 0xb53b30, roughness = 0.7, metalness = 0.2, side = THREE.Side.DoubleSide });
        band.rotation.z = Math.PI / 2;
    }

    private static void buildRing(THREE.Group g, string id)
    {
        var gold = metal(0xf2c14e, 0.22);
        // the band stands upright, a little thicker where the stone sits
        add(g, new THREE.TorusGeometry(0.3, 0.065, 18, 48), gold);
        var shoulder = add(g, new THREE.SphereGeometry(0.1, 14, 10), gold, 0, 0.3, 0);
        shoulder.scale.set(1, 1.1, 1.4);
        if (id == "signet")
        {
            add(g, new THREE.CylinderGeometry(0.17, 0.17, 0.07, 6), gold, 0, 0.36, 0);
            var crest = add(g, new THREE.OctahedronGeometry(0.07), metal(0xd8a62d, 0.18), 0, 0.42, 0);
            crest.scale.y = 0.5;
        }
        else
        {
            // a claw setting holding a cut ruby: a flat table and crown above a pointed pavilion
            var ruby = new THREE.MeshStandardMaterial { color = 0xc8102e, roughness = 0.12, metalness = 0.15, emissive = 0x5a0010, emissiveIntensity = 0.45 };
            add(g, new THREE.CylinderGeometry(0.15, 0.1, 0.05, 8), gold, 0, 0.39, 0);
            add(g, new THREE.CylinderGeometry(0.085, 0.135, 0.075, 8), ruby, 0, 0.455, 0);
            add(g, new THREE.CylinderGeometry(0.135, 0.01, 0.12, 8), ruby, 0, 0.36, 0);
            for (int i = 0; i < 4; i++)
            {
                double a = i / 4.0 * Math.PI * 2 + Math.PI / 4;
                var claw = add(g, new THREE.CylinderGeometry(0.014, 0.018, 0.09, 6), gold, Math.Cos(a) * 0.125, 0.42, Math.Sin(a) * 0.125);
                claw.rotation.set(Math.Sin(a) * 0.3, 0, -Math.Cos(a) * 0.3);
            }
        }
    }

    private static void buildCompass(THREE.Group g)
    {
        var silver = metal(0xcfd2d6, 0.25);
        add(g, new THREE.CylinderGeometry(0.34, 0.34, 0.12, 36), silver);
        add(g, torusFlat(0.34, 0.025), silver, 0, 0.06, 0);
        add(g, new THREE.CylinderGeometry(0.29, 0.29, 0.01, 36), new THREE.MeshStandardMaterial { color = 0xf2ecd8, roughness = 0.9 }, 0, 0.062, 0);
        for (int i = 0; i < 8; i++)
        {
            double a = i / 8.0 * Math.PI * 2;
            var tick = add(g, new THREE.BoxGeometry(0.02, 0.006, i % 2 == 1 ? 0.04 : 0.07), new THREE.MeshStandardMaterial { color = 0x3a3a3a }, Math.Cos(a) * 0.25, 0.068, Math.Sin(a) * 0.25);
            tick.rotation.y = -a + Math.PI / 2;
        }
        var red = add(g, new THREE.ConeGeometry(0.035, 0.24, 4), new THREE.MeshStandardMaterial { color = 0xd32f2f, roughness = 0.4 }, 0.12, 0.08, 0);
        red.rotation.z = -Math.PI / 2;
        var white = add(g, new THREE.ConeGeometry(0.035, 0.24, 4), new THREE.MeshStandardMaterial { color = 0xf5f5f5, roughness = 0.4 }, -0.12, 0.08, 0);
        white.rotation.z = Math.PI / 2;
        add(g, new THREE.SphereGeometry(0.03, 10, 8), silver, 0, 0.085, 0);
        add(g, new THREE.CylinderGeometry(0.3, 0.3, 0.02, 36), new THREE.MeshPhysicalMaterial
        {
            color = 0xcfeaff, transparent = true, opacity = 0.28, roughness = 0.02, transmission = 0.6, ior = 1.45, thickness = 0.05,
        }, 0, 0.11, 0);
        add(g, new THREE.CylinderGeometry(0.045, 0.045, 0.1, 12), silver, 0, 0.0, -0.4).rotation.x = Math.PI / 2;
        add(g, torusFlat(0.07, 0.016), silver, 0, 0, -0.5);
    }

    private static void buildKey(THREE.Group g)
    {
        var brass = metal(0xb5883a, 0.3);
        add(g, torusFlat(0.15, 0.04), brass, -0.38, 0, 0);
        add(g, torusFlat(0.07, 0.022), brass, -0.38, 0, 0);
        foreach (var a in new[] { 0, Math.PI / 2, Math.PI, Math.PI * 1.5 })
            add(g, new THREE.SphereGeometry(0.035, 8, 6), brass, -0.38 + Math.Cos(a) * 0.19, 0, Math.Sin(a) * 0.19);
        var shaft = add(g, new THREE.CylinderGeometry(0.03, 0.03, 0.75, 12), brass, 0.1, 0, 0);
        shaft.rotation.z = Math.PI / 2;
        add(g, new THREE.BoxGeometry(0.1, 0.045, 0.16), brass, 0.38, 0, 0.09);
        add(g, new THREE.BoxGeometry(0.08, 0.045, 0.11), brass, 0.25, 0, 0.075);
        add(g, new THREE.CylinderGeometry(0.045, 0.045, 0.04, 12), brass, -0.18, 0, 0).rotation.z = Math.PI / 2;
    }

    private static void buildShard(THREE.Group g, string setId, int idx)
    {
        double arc = Math.PI * 2 / 3 + 0.12;
        double start = idx * (Math.PI * 2 / 3);
        if (setId == "astrolabe")
        {
            var brass = metal(0xc9973f, 0.3);
            var mesh = add(g, torusFlat(0.5, 0.055, arc), brass);
            mesh.rotation.y = -start;
            var inner = add(g, torusFlat(0.36, 0.025, arc * 0.9), brass);
            inner.rotation.y = -start - 0.1;
            for (int i = 0; i < 3; i++)
            {
                double a = start + 0.25 + i * 0.55;
                var star = add(g, new THREE.OctahedronGeometry(0.055), metal(0xffe29a, 0.2), Math.Cos(a) * 0.5, 0.055, -Math.Sin(a) * 0.5);
                star.scale.y = 0.6;
            }
        }
        else
        {
            var bronze = metal(0xd2a24a, 0.28);
            var wedge = add(g, new THREE.CylinderGeometry(0.5, 0.5, 0.07, 32, 1, false, start, arc), bronze);
            ((THREE.MeshStandardMaterial)wedge.material).side = THREE.Side.DoubleSide;
            var rim = add(g, torusFlat(0.47, 0.022, arc), bronze, 0, 0.04, 0);
            rim.rotation.y = -start;
            double mid = start + arc / 2;
            add(g, new THREE.SphereGeometry(0.06, 10, 8), new THREE.MeshStandardMaterial { color = 0xf0e4c4, roughness = 0.6 }, Math.Cos(mid) * 0.28, 0.06, -Math.Sin(mid) * 0.28);
        }
    }

    private static void buildBottleCap(THREE.Group g)
    {
        var mat = metal(0xc43d2f, 0.4, 0.6);
        add(g, new THREE.CylinderGeometry(0.2, 0.2, 0.04, 24), mat);
        for (int i = 0; i < 21; i++)
        {
            double a = i / 21.0 * Math.PI * 2;
            add(g, new THREE.BoxGeometry(0.03, 0.06, 0.045), mat, Math.Cos(a) * 0.2, 0.02, Math.Sin(a) * 0.2).rotation.y = -a;
        }
        add(g, new THREE.CylinderGeometry(0.12, 0.12, 0.01, 18), metal(0xf0e8d0, 0.5, 0.2), 0, 0.025, 0);
    }

    /// <summary>An empty green glass bottle on its side, with a worn paper label and a chipped lip.</summary>
    private static void buildBottle(THREE.Group g)
    {
        var glass = new THREE.MeshStandardMaterial { color = 0x3d9a56, roughness = 0.1, metalness = 0.1, transparent = true, opacity = 0.62, side = THREE.Side.DoubleSide, depthWrite = false };
        var paper = new THREE.MeshStandardMaterial { color = 0xe7dcc0, roughness = 0.9, side = THREE.Side.DoubleSide };
        var pts = V2(new[]
        {
            new[] { 0, -0.28 }, new[] { 0.07, -0.28 }, new[] { 0.1, -0.265 }, new[] { 0.112, -0.23 }, new[] { 0.112, 0.06 }, new[] { 0.106, 0.12 }, new[] { 0.085, 0.17 },
            new[] { 0.06, 0.22 }, new[] { 0.043, 0.27 }, new[] { 0.04, 0.36 }, new[] { 0.048, 0.375 }, new[] { 0.05, 0.4 }, new[] { 0.044, 0.405 },
        });
        // the bottle's axis runs along x, neck toward +x
        var bottle = add(g, new THREE.LatheGeometry(pts, 24), glass, 0, 0.112, 0);
        bottle.rotation.z = -Math.PI / 2;
        bottle.renderOrder = 2;
        var label = add(g, new THREE.CylinderGeometry(0.118, 0.118, 0.17, 24, 1, true, 0.4, 4.4), paper, -0.05, 0.112, 0);
        label.rotation.z = Math.PI / 2;
        var stripe = add(g, new THREE.CylinderGeometry(0.119, 0.119, 0.04, 24, 1, true, 0.4, 4.4), new THREE.MeshStandardMaterial { color = 0xa33a2a, roughness = 0.8, side = THREE.Side.DoubleSide }, -0.05, 0.112, 0);
        stripe.rotation.z = Math.PI / 2;
        // a little dirt in the bottom of the glass
        var silt = add(g, new THREE.SphereGeometry(0.1, 10, 6), new THREE.MeshStandardMaterial { color = 0x8a7a55, roughness = 1 }, -0.22, 0.05, 0);
        silt.scale.set(0.6, 0.45, 0.9);
    }

    /// <summary>A J-shaped fishing hook lying flat: an eye, a straight shank, a round bend and a barbed point that comes back toward the eye.</summary>
    private static void buildHook(THREE.Group g)
    {
        var mat = new THREE.MeshStandardMaterial { color = 0x8a5a38, roughness = 0.85, metalness = 0.5 };
        var rust = new THREE.MeshStandardMaterial { color = 0x6b3f22, roughness = 0.95, metalness = 0.3 };
        const double R = 0.14, cx = 0.1;
        var pts = new List<THREE.Vector3>();
        for (int i = 0; i <= 6; i++) pts.Add(new THREE.Vector3(-0.26 + i / 6.0 * (cx + 0.26), 0, -R));
        for (int i = 1; i <= 20; i++)
        {
            double th = -Math.PI / 2 + i / 20.0 * Math.PI * 1.08;
            pts.Add(new THREE.Vector3(cx + Math.Cos(th) * R, 0, Math.Sin(th) * R));
        }
        var path = new THREE.CatmullRomCurve3(pts, false, "catmullrom", 0.2);
        var wire = new THREE.TubeGeometry(path, 90, 0.022, 8);
        // thinner toward the point
        var wp = wire.attributes.position;
        for (int i = 0; i < wp.count; i++)
        {
            double t = Math.Floor(i / 9.0) / 90;
            var c = path.getPoint(Math.Min(1, t));
            double k = t > 0.8 ? Math.Max(0.3, 1 - (t - 0.8) * 3.2) : 1;
            wp.setXYZ(i, c.x + (wp.getX(i) - c.x) * k, c.y + (wp.getY(i) - c.y) * k, c.z + (wp.getZ(i) - c.z) * k);
        }
        wire.computeVertexNormals();
        add(g, wire, mat);
        // the sharp tip and a barb on the inside of the point
        var end = pts[pts.Count - 1];
        var prev = pts[pts.Count - 3];
        var dir = end.clone().sub(prev).normalize();
        var tip = add(g, new THREE.ConeGeometry(0.014, 0.07, 8), mat, end.x + dir.x * 0.03, 0, end.z + dir.z * 0.03);
        tip.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir);
        var barb = add(g, new THREE.ConeGeometry(0.014, 0.06, 6), rust, end.x - dir.x * 0.04 + dir.z * 0.025, 0, end.z - dir.z * 0.04 - dir.x * 0.025);
        barb.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), new THREE.Vector3(-dir.x * 0.5 + dir.z * 0.6, 0, -dir.z * 0.5 - dir.x * 0.6).normalize());
        // the eye at the top of the shank
        add(g, torusFlat(0.038, 0.012), mat, -0.3, 0, -R);
        add(g, new THREE.CylinderGeometry(0.026, 0.026, 0.02, 10), rust, -0.22, 0, -R).rotation.z = Math.PI / 2;
    }

    /// <summary>A flat band that follows a curve, like a rubber strap: `width` across, `thick` deep, always lying face up.</summary>
    private static THREE.BufferGeometry ribbon(THREE.Curve3 curve, double width, double thick, int segs = 30)
    {
        var pos = new List<double>();
        var idx = new List<int>();
        var up = new THREE.Vector3(0, 1, 0);
        var t = new THREE.Vector3();
        var side = new THREE.Vector3();
        var nrm = new THREE.Vector3();
        for (int i = 0; i <= segs; i++)
        {
            var p = curve.getPoint((double)i / segs);
            curve.getTangent((double)i / segs, t);
            side.crossVectors(up, t).normalize();
            if (side.lengthSq() < 1e-4) side.set(1, 0, 0);
            nrm.crossVectors(t, side).normalize();
            foreach (var (a, b) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                pos.Add(p.x + side.x * a * (width / 2) + nrm.x * b * (thick / 2));
                pos.Add(p.y + side.y * a * (width / 2) + nrm.y * b * (thick / 2));
                pos.Add(p.z + side.z * a * (width / 2) + nrm.z * b * (thick / 2));
            }
        }
        for (int i = 0; i < segs; i++)
        for (int k = 0; k < 4; k++)
        {
            int a = i * 4 + k, b = i * 4 + (k + 1) % 4;
            idx.AddRange(new[] { a, a + 4, b, b, a + 4, b + 4 });
        }
        // close the two ends
        idx.AddRange(new[] { 0, 1, 2, 0, 2, 3, segs * 4, segs * 4 + 2, segs * 4 + 1, segs * 4, segs * 4 + 3, segs * 4 + 2 });
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setIndex(idx);
        g.computeVertexNormals();
        return g;
    }

    /// <summary>A beach flip-flop: a layered rubber sole shaped like a left foot, with a moulded footbed, and a yellow Y strap that comes up through the toe post.</summary>
    private static void buildFlipFlop(THREE.Group g)
    {
        // the footprint: [z, outer edge, inner edge], toe at +z. A narrow heel, a slim arch, a wide ball and a broad big toe.
        var prof = new[]
        {
            new[] { -0.45, 0.0, 0.0 }, new[] { -0.42, 0.095, -0.09 }, new[] { -0.34, 0.125, -0.105 }, new[] { -0.2, 0.105, -0.08 }, new[] { -0.06, 0.12, -0.085 }, new[] { 0.1, 0.16, -0.125 },
            new[] { 0.24, 0.175, -0.155 }, new[] { 0.36, 0.15, -0.165 }, new[] { 0.44, 0.1, -0.13 }, new[] { 0.48, 0.03, -0.06 }, new[] { 0.49, 0.0, 0.0 },
        };
        List<THREE.Vector3> side(int k) => prof.Select(p => new THREE.Vector3(p[k], 0, -p[0])).ToList();
        var inner = side(2);
        var loopPts = side(1).Concat(inner.Skip(1).Take(inner.Count - 2).Reverse()).ToList();
        var loop = new THREE.CatmullRomCurve3(loopPts, true, "centripetal");
        var outline = loop.getPoints(90).Select(v => new[] { v.x, v.z }).ToList();
        List<double[]> scaled(double k) => outline.Select(o => new[] { o[0] * k, o[1] * k }).ToList();
        var navy = new THREE.MeshStandardMaterial { color = 0x164a90, roughness = 0.9 };
        var blue = new THREE.MeshStandardMaterial { color = 0x2f86d6, roughness = 0.85 };
        var light = new THREE.MeshStandardMaterial { color = 0x7fbcec, roughness = 0.8 };
        var none = new List<List<double[]>>();
        // outsole, midsole and the footbed, each a little smaller than the one beneath
        add(g, plate(scaled(1.04), none, 0.03, 0.008), navy).position.y = 0.0;
        add(g, plate(scaled(1.0), none, 0.045, 0.012), blue).position.y = 0.04;
        add(g, plate(scaled(0.9), none, 0.025, 0.008), light).position.y = 0.075;
        // the heel cup and the arch support, moulded into the footbed
        var cup = add(g, new THREE.SphereGeometry(0.12, 16, 10), light, 0.0, 0.088, -0.33);
        cup.scale.set(1.0, 0.3, 0.9);
        var arch = add(g, new THREE.SphereGeometry(0.1, 14, 8), light, -0.0, 0.088, -0.1);
        arch.scale.set(0.8, 0.22, 1.5);
        // grooves across the ball of the foot, and a worn patch where the heel presses
        var groove = new THREE.MeshStandardMaterial { color = 0x2a6fb4, roughness = 0.9 };
        for (int i = 0; i < 6; i++)
        {
            var gr = add(g, new THREE.BoxGeometry(0.24, 0.008, 0.014), groove, 0.02, 0.095, 0.1 + i * 0.045);
            gr.rotation.y = -0.1;
        }
        // the strap: two flat bands from the sides of the sole, rising and meeting at the toe post
        var yellow = new THREE.MeshStandardMaterial { color = 0xf3c62c, roughness = 0.7 };
        var post = new THREE.Vector3(0.0, 0.09, 0.27);
        foreach (var (x, sx) in new[] { (0.15, 1), (-0.15, -1) })
        {
            var path = new THREE.CatmullRomCurve3(new[]
            {
                new THREE.Vector3(x * 0.95, 0.08, 0.03),
                new THREE.Vector3(x * 0.9, 0.14, 0.075),
                new THREE.Vector3(x * 0.55, 0.17, 0.17),
                new THREE.Vector3(sx * 0.07, 0.13, post.z - 0.03),
                new THREE.Vector3(0, 0.1, post.z),
            });
            add(g, ribbon(path, 0.05, 0.022, 36), yellow);
            // the strap pushes through the sole with a round plug on each side
            add(g, new THREE.CylinderGeometry(0.035, 0.035, 0.02, 12), yellow, x * 0.95, 0.092, 0.03);
        }
        // the knob where the straps meet, and the toe post it is anchored by
        add(g, new THREE.SphereGeometry(0.04, 12, 8), yellow, 0, 0.1, post.z).scale.set(1.1, 0.7, 1.1);
        add(g, new THREE.CylinderGeometry(0.028, 0.034, 0.08, 10), yellow, 0, 0.06, post.z);
    }

    private static void buildBolt(THREE.Group g)
    {
        var mat = new THREE.MeshStandardMaterial { color = 0x8a4b22, roughness = 0.9, metalness = 0.5 };
        var head = add(g, new THREE.CylinderGeometry(0.09, 0.09, 0.06, 6), mat, -0.18, 0, 0);
        head.rotation.z = Math.PI / 2;
        var shaft = add(g, new THREE.CylinderGeometry(0.04, 0.04, 0.34, 10), mat, 0.02, 0, 0);
        shaft.rotation.z = Math.PI / 2;
        for (int i = 0; i < 6; i++)
        {
            var ring = add(g, new THREE.TorusGeometry(0.042, 0.006, 5, 10), mat, -0.04 + i * 0.045, 0, 0);
            ring.rotation.y = Math.PI / 2;
        }
    }

    /// <summary>The plastic carrier from a six-pack: a thin sheet with six round holes, joined along the middle.</summary>
    private static void buildPlasticRing(THREE.Group g)
    {
        var mat = new THREE.MeshStandardMaterial { color = 0xdfeef4, roughness = 0.35, transparent = true, opacity = 0.82, side = THREE.Side.DoubleSide };
        const double r = 0.095;
        var cols = new[] { -0.21, 0, 0.21 };
        var rows = new[] { -0.1, 0.1 };
        var holes = rows.SelectMany(z => cols.Select(x => ellipse(x, z, r, r, 0, Math.PI * 2, 24))).ToList();
        // the outline hugs the holes: a rounded rectangle with a notch between each pair of rings
        var outline = new List<double[]>();
        const double R = 0.13;
        void corner(double cx, double cz, double a0) => outline.AddRange(ellipse(cx, cz, R, R, a0, a0 + Math.PI / 2, 8));
        corner(0.21, 0.1, 0);
        corner(-0.21, 0.1, Math.PI / 2);
        corner(-0.21, -0.1, Math.PI);
        corner(0.21, -0.1, 3 * Math.PI / 2);
        add(g, plate(outline, holes, 0.022, 0.006), mat);
        // the two little finger tabs at the ends
        foreach (var sx in new[] { -1, 1 }) add(g, plate(ellipse(sx * 0.37, 0, 0.05, 0.03, 0, Math.PI * 2, 12), new List<List<double[]>>(), 0.02, 0.005), mat);
    }

    private static void buildWatch(THREE.Group g)
    {
        var silver = metal(0xcfd2d6, 0.25);
        add(g, new THREE.CylinderGeometry(0.26, 0.26, 0.09, 36), silver);
        add(g, torusFlat(0.26, 0.02), silver, 0, 0.045, 0);
        add(g, new THREE.CylinderGeometry(0.22, 0.22, 0.01, 36), new THREE.MeshStandardMaterial { color = 0xf4efe0, roughness = 0.8 }, 0, 0.048, 0);
        for (int i = 0; i < 12; i++)
        {
            double a = i / 12.0 * Math.PI * 2;
            add(g, new THREE.BoxGeometry(0.015, 0.006, i % 3 == 0 ? 0.05 : 0.03), new THREE.MeshStandardMaterial { color = 0x3a3a3a }, Math.Cos(a) * 0.18, 0.055, Math.Sin(a) * 0.18).rotation.y = -a + Math.PI / 2;
        }
        add(g, new THREE.BoxGeometry(0.012, 0.006, 0.14), new THREE.MeshStandardMaterial { color = 0x222222 }, 0.0, 0.06, -0.06);
        add(g, new THREE.BoxGeometry(0.1, 0.006, 0.012), new THREE.MeshStandardMaterial { color = 0x222222 }, 0.04, 0.06, 0.0);
        add(g, new THREE.CylinderGeometry(0.04, 0.04, 0.06, 10), silver, 0, 0, -0.31).rotation.x = Math.PI / 2;
        add(g, torusFlat(0.06, 0.014), silver, 0, 0, -0.4);
        // short chain
        for (int i = 0; i < 6; i++) add(g, torusFlat(0.03, 0.007), silver, Math.Sin(i * 0.9) * 0.08, 0, -0.5 - i * 0.045).rotation.y = i * 0.6;
    }

    private static void buildPearls(THREE.Group g)
    {
        var pearl = new THREE.MeshPhysicalMaterial { color = 0xf6efe6, roughness = 0.2, metalness = 0.1, clearcoat = 1, clearcoatRoughness = 0.1 };
        const int n = 18;
        for (int i = 0; i < n; i++)
        {
            double a = (double)i / n * Math.PI * 2;
            double r = 0.06 + 0.012 * Math.Sin((double)i / n * Math.PI);
            add(g, new THREE.SphereGeometry(r, 14, 10), pearl, Math.Cos(a) * 0.32, 0, Math.Sin(a) * 0.32);
        }
        add(g, new THREE.BoxGeometry(0.1, 0.05, 0.06), metal(0xf2c14e, 0.25), 0.32, 0.0, 0.0);
    }

    /// <summary>Sunglasses lying open on their backs: rounded dark lenses, a sturdy frame, a nose bridge and two long arms.</summary>
    private static void buildSunglasses(THREE.Group g)
    {
        var frame = new THREE.MeshStandardMaterial { color = 0x1c1c20, roughness = 0.35, metalness = 0.2 };
        var lens = new THREE.MeshStandardMaterial { color = 0x1b2a36, roughness = 0.08, metalness = 0.3, transparent = true, opacity = 0.88 };
        // a rounded, slightly squared lens that is wider at the top
        List<double[]> lensShape(double k) => ellipse(0, 0.015, 0.165 * k, 0.1 * k, 0, Math.PI, 16).Concat(ellipse(0.0, -0.02, 0.15 * k, 0.1 * k, Math.PI, Math.PI * 2, 16)).ToList();
        foreach (var sx in new[] { -1, 1 })
        {
            double x = sx * 0.2;
            var f = add(g, plate(lensShape(1.05), new List<List<double[]>> { lensShape(0.82) }, 0.045, 0.008), frame, x, 0, 0);
            f.scale.set(1, 1, 1);
            add(g, plate(lensShape(0.84), new List<List<double[]>>(), 0.012, 0.004), lens, x, 0.01, 0);
            // a hinge block and the long arm, bent slightly outward
            add(g, new THREE.BoxGeometry(0.05, 0.045, 0.05), frame, sx * 0.355, 0, 0.0);
            var arm = add(g, new THREE.BoxGeometry(0.035, 0.03, 0.42), frame, sx * 0.375, 0, 0.23);
            arm.rotation.y = sx * -0.07;
            add(g, new THREE.SphereGeometry(0.022, 8, 6), frame, sx * 0.39, 0, 0.44);
            // a nose pad
            add(g, new THREE.SphereGeometry(0.02, 8, 6), frame, sx * 0.05, -0.02, 0.07).scale.set(0.8, 0.6, 1.4);
        }
        var bridge = add(g, new THREE.TorusGeometry(0.06, 0.02, 8, 14, Math.PI), frame, 0, 0, -0.01);
        bridge.rotation.x = Math.PI / 2;
        add(g, new THREE.BoxGeometry(0.46, 0.02, 0.03), frame, 0, 0, -0.085);
    }

    private static void buildHarmonica(THREE.Group g)
    {
        var silver = metal(0xcfd2d6, 0.25);
        var dark = new THREE.MeshStandardMaterial { color = 0x25272b, roughness = 0.7 };
        add(g, new THREE.BoxGeometry(0.56, 0.11, 0.1), silver);
        add(g, new THREE.BoxGeometry(0.58, 0.02, 0.12), silver, 0, 0.065, 0);
        add(g, new THREE.BoxGeometry(0.58, 0.02, 0.12), silver, 0, -0.065, 0);
        for (int i = 0; i < 10; i++) add(g, new THREE.BoxGeometry(0.03, 0.05, 0.02), dark, -0.22 + i * 0.049, 0, 0.055);
        add(g, new THREE.BoxGeometry(0.04, 0.16, 0.12), silver, -0.3, 0, 0);
        add(g, new THREE.BoxGeometry(0.04, 0.16, 0.12), silver, 0.3, 0, 0);
    }

    private static void buildWeddingBand(THREE.Group g)
    {
        var gold = metal(0xf2c14e, 0.18);
        add(g, new THREE.TorusGeometry(0.3, 0.06, 18, 48), gold);
        var gem = add(g, new THREE.OctahedronGeometry(0.1),
            new THREE.MeshPhysicalMaterial { color = 0xf6fbff, transmission = 0.95, ior = 2.4, roughness = 0.02, thickness = 0.3, metalness = 0 }, 0, 0.38, 0);
        gem.scale.y = 1.2;
        foreach (var (x, z) in new[] { (0.06, 0.06), (-0.06, 0.06), (0.06, -0.06), (-0.06, -0.06) }) add(g, new THREE.CylinderGeometry(0.01, 0.01, 0.08, 6), gold, x, 0.33, z);
    }

    /// <summary>A wooden toy sailboat: a carved hull, a deck, a cabin, a mast with a red mainsail and white jib, and a little flag.</summary>
    private static void buildBoat(THREE.Group g)
    {
        var wood = new THREE.MeshStandardMaterial { color = 0xa6663a, roughness = 0.75 };
        var deckMat = new THREE.MeshStandardMaterial { color = 0xd9b27a, roughness = 0.8 };
        var white = new THREE.MeshStandardMaterial { color = 0xf4f0e6, roughness = 0.8, side = THREE.Side.DoubleSide };
        var red = new THREE.MeshStandardMaterial { color = 0xd9382c, roughness = 0.8, side = THREE.Side.DoubleSide };
        // the hull: a side profile (bow to the right), pinched toward the bow when seen from above
        var prof = new THREE.Shape();
        prof.moveTo(-0.42, 0.14);
        prof.quadraticCurveTo(-0.44, -0.02, -0.28, -0.07);
        prof.quadraticCurveTo(0.0, -0.1, 0.24, -0.06);
        prof.quadraticCurveTo(0.44, -0.02, 0.52, 0.18);
        prof.lineTo(0.5, 0.2);
        prof.quadraticCurveTo(0.1, 0.14, -0.42, 0.14);
        THREE.BufferGeometry hullGeo = new THREE.ExtrudeGeometry(prof, new THREE.ExtrudeGeometryOptions { depth = 0.26, bevelEnabled = true, bevelSize = 0.03, bevelThickness = 0.04, bevelSegments = 4, curveSegments = 20 });
        hullGeo.translate(0, 0, -0.13);
        var hp = hullGeo.attributes.position;
        for (int i = 0; i < hp.count; i++)
        {
            double x = hp.getX(i);
            double k = x > 0 ? 1 - 0.7 * Math.Pow(Math.Min(1, x / 0.55), 1.6) : 1 - 0.18 * Math.Pow(Math.Min(1, -x / 0.45), 2);
            hp.setZ(i, hp.getZ(i) * k);
        }
        // weld the pieces so the hull shades smoothly, like turned wood, instead of in flat facets
        hullGeo.deleteAttribute("normal");
        hullGeo.deleteAttribute("uv");
        hullGeo = THREE.BufferGeometryUtils.mergeVertices(hullGeo, 1e-4);
        hullGeo.computeVertexNormals();
        add(g, hullGeo, wood);
        // the deck and a small cabin at the stern
        add(g, new THREE.BoxGeometry(0.62, 0.012, 0.22), deckMat, -0.04, 0.2, 0);
        add(g, new THREE.BoxGeometry(0.15, 0.07, 0.15), wood, -0.3, 0.238, 0);
        // the mast, boom and sails
        add(g, new THREE.CylinderGeometry(0.014, 0.018, 0.78, 8), wood, 0.06, 0.58, 0);
        add(g, new THREE.CylinderGeometry(0.01, 0.01, 0.34, 6), wood, -0.11, 0.38, 0).rotation.z = Math.PI / 2;
        var main = new THREE.Shape();
        main.moveTo(0, 0);
        main.lineTo(0, 0.66);
        main.quadraticCurveTo(-0.1, 0.3, -0.27, 0.0);
        main.closePath();
        add(g, new THREE.ShapeGeometry(main), red, 0.05, 0.38, 0.005);
        var jib = new THREE.Shape();
        jib.moveTo(0, 0);
        jib.lineTo(0, 0.54);
        jib.quadraticCurveTo(0.12, 0.2, 0.3, 0.0);
        jib.closePath();
        add(g, new THREE.ShapeGeometry(jib), white, 0.075, 0.38, -0.005);
        // a tiny flag at the top of the mast
        var flag = new THREE.Shape();
        flag.moveTo(0, 0);
        flag.lineTo(0.09, 0.03);
        flag.lineTo(0, 0.06);
        flag.closePath();
        add(g, new THREE.ShapeGeometry(flag), red, 0.065, 0.93, 0);
    }

    private static void buildBlackPearl(THREE.Group g)
    {
        var gold = metal(0xd9aa45, 0.25);
        var pearl = new THREE.MeshStandardMaterial { color = 0x1b1f24, roughness = 0.12, metalness = 0.55, emissive = 0x06201f, emissiveIntensity = 0.6 };
        add(g, new THREE.SphereGeometry(0.26, 24, 18), pearl, 0, 0.12, 0);
        add(g, new THREE.CylinderGeometry(0.2, 0.09, 0.14, 16), gold, 0, -0.17, 0);
        for (int a = 0; a < 4; a++)
        {
            double ang = a * Math.PI / 2;
            var claw = add(g, new THREE.CylinderGeometry(0.012, 0.012, 0.22, 6), gold, Math.Cos(ang) * 0.17, -0.02, Math.Sin(ang) * 0.17);
            claw.rotation.set(Math.Sin(ang) * 0.35, 0, -Math.Cos(ang) * 0.35);
        }
    }

    /// <summary>A weathered stone idol: a seated figure with rounded, carved forms, a heavy-browed face, folded arms, a stepped headdress and moss in the damp places.</summary>
    private static void buildIdol(THREE.Group g)
    {
        var parts = new List<THREE.BufferGeometry>();
        const int stone = 0x9a9488, deep = 0x77716a, dark = 0x1f1c19, cream = 0xd9d1bd;
        // a box with rounded edges and corners
        THREE.BufferGeometry rbox(double w, double h, double d, double r)
        {
            var geo = new THREE.BoxGeometry(w, h, d, 6, 6, 6);
            var p = geo.attributes.position;
            var v = new THREE.Vector3();
            var c = new THREE.Vector3();
            for (int i = 0; i < p.count; i++)
            {
                v.fromBufferAttribute(p, i);
                c.set(Math.Max(-(w / 2 - r), Math.Min(w / 2 - r, v.x)), Math.Max(-(h / 2 - r), Math.Min(h / 2 - r, v.y)), Math.Max(-(d / 2 - r), Math.Min(d / 2 - r, v.z)));
                v.sub(c);
                if (v.lengthSq() > 0) v.normalize().multiplyScalar(r);
                p.setXYZ(i, c.x + v.x, c.y + v.y, c.z + v.z);
            }
            return geo;
        }
        void put(THREE.BufferGeometry geo, int color, double x, double y, double z, double sx = 1, double sy = 1, double sz = 1)
        {
            geo.scale(sx, sy, sz);
            geo.translate(x, y, z);
            parts.Add(tint(geo, color));
        }
        // plinth and the body, a rounded tapering mass seated on it
        put(rbox(0.72, 0.12, 0.54, 0.035), deep, 0, -0.5, 0);
        var body = new THREE.LatheGeometry(V2(new[]
        {
            new[] { 0, -0.44 }, new[] { 0.3, -0.44 }, new[] { 0.32, -0.3 }, new[] { 0.29, -0.05 }, new[] { 0.25, 0.14 }, new[] { 0.2, 0.27 }, new[] { 0.11, 0.33 }, new[] { 0, 0.34 },
        }), 28);
        put(body, stone, 0, 0, 0, 1, 1, 0.78);
        put(rbox(0.64, 0.17, 0.4, 0.07), stone, 0, 0.22, 0);
        // arms folded across the belly, with hands
        foreach (var sx in new[] { -1, 1 })
        {
            var arm = new THREE.TubeGeometry(
                new THREE.CatmullRomCurve3(new[] { new THREE.Vector3(sx * 0.3, 0.22, 0.04), new THREE.Vector3(sx * 0.31, 0.04, 0.16), new THREE.Vector3(sx * 0.2, -0.08, 0.27), new THREE.Vector3(sx * 0.06, -0.1, 0.3) }),
                20, 0.055, 8);
            parts.Add(tint(arm, deep));
            put(rbox(0.1, 0.07, 0.09, 0.025), stone, sx * 0.05, -0.1, 0.3);
        }
        // a row of carved diamonds across the chest
        for (int i = 0; i < 5; i++)
        {
            var d = rbox(0.05, 0.05, 0.03, 0.01);
            d.rotateZ(Math.PI / 4);
            put(d, deep, (i - 2) * 0.085, 0.17 - Math.Abs(i - 2) * 0.025, 0.2);
        }
        // neck, and the head: a tall rounded block
        put(new THREE.CylinderGeometry(0.11, 0.13, 0.12, 14), deep, 0, 0.38, 0);
        put(rbox(0.46, 0.52, 0.42, 0.13), stone, 0, 0.68, 0);
        // brow, eyes, nose, mouth, chin and ears
        put(rbox(0.48, 0.08, 0.14, 0.035), deep, 0, 0.77, 0.19);
        foreach (var sx in new[] { -1, 1 })
        {
            var eye = new THREE.SphereGeometry(0.05, 12, 8);
            put(eye, dark, sx * 0.115, 0.69, 0.2, 1, 0.75, 0.6);
            put(rbox(0.07, 0.05, 0.05, 0.02), stone, sx * 0.115, 0.64, 0.2);
            put(rbox(0.065, 0.22, 0.1, 0.025), stone, sx * 0.26, 0.66, 0);
            put(rbox(0.055, 0.1, 0.07, 0.022), stone, sx * 0.26, 0.52, 0);
        }
        put(rbox(0.1, 0.22, 0.12, 0.035), deep, 0, 0.62, 0.23);
        put(rbox(0.24, 0.045, 0.06, 0.016), dark, 0, 0.47, 0.205);
        for (int i = 0; i < 4; i++) put(rbox(0.032, 0.038, 0.03, 0.01), cream, (i - 1.5) * 0.05, 0.485, 0.215);
        put(rbox(0.22, 0.08, 0.1, 0.03), stone, 0, 0.41, 0.18);
        // a three-tier headdress with studs round the lowest band
        put(new THREE.CylinderGeometry(0.28, 0.3, 0.09, 18), deep, 0, 0.98, 0);
        put(new THREE.CylinderGeometry(0.21, 0.24, 0.09, 16), stone, 0, 1.07, 0);
        put(new THREE.CylinderGeometry(0.12, 0.15, 0.1, 14), deep, 0, 1.16, 0);
        put(new THREE.SphereGeometry(0.06, 10, 8), stone, 0, 1.26, 0);
        for (int i = 0; i < 10; i++)
        {
            double a = i / 10.0 * Math.PI * 2;
            put(new THREE.SphereGeometry(0.03, 8, 6), cream, Math.Cos(a) * 0.29, 0.98, Math.Sin(a) * 0.29);
        }
        var merged = THREE.BufferGeometryUtils.mergeGeometries(parts, false);
        // weather it: a slight lumpiness, damp moss on the low and upward-facing parts
        merged.computeVertexNormals();
        var pp = merged.attributes.position;
        var n = merged.attributes.normal;
        var col = merged.attributes.color;
        var moss = new THREE.Color(0x6f8048);
        var cc = new THREE.Color();
        for (int i = 0; i < pp.count; i++)
        {
            double x = pp.getX(i), y = pp.getY(i), z = pp.getZ(i);
            double lump = Math.Sin(x * 23 + y * 11) * Math.Sin(y * 19 + z * 17) * Math.Sin(z * 21 + x * 13);
            double dd = 0.007 * lump;
            pp.setXYZ(i, x + n.getX(i) * dd, y + n.getY(i) * dd, z + n.getZ(i) * dd);
            cc.fromBufferAttribute(col, i);
            double patch = Math.Sin(x * 9 + z * 7) * Math.Sin(y * 8 + x * 5);
            double damp = Math.Max(0, n.getY(i)) * 0.6 + Math.Max(0, -0.1 - y) * 0.9;
            double m = Math.Max(0, Math.Min(1, (patch * 0.5 + damp - 0.2) * 1.4));
            cc.lerp(moss, m * 0.65);
            cc.multiplyScalar(0.9 + lump * 0.08);
            col.setXYZ(i, cc.r, cc.g, cc.b);
        }
        merged.computeVertexNormals();
        var mesh = new THREE.Mesh(merged, new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.95 });
        mesh.castShadow = true;
        g.add(mesh);
    }

    /// <summary>A pink conch: a stepped spire at one end, a big body whorl with knobs on its shoulder, spiral ridges, a flared lip and a narrow canal.</summary>
    private static void buildConch(THREE.Group g)
    {
        // the outline of the shell from the apex (axis y = 0) to the canal (y = 1.05), radius first
        var pts = new List<double[]> { new double[] { 0, 0 } };
        for (int i = 0; i < 5; i++)
        {
            double y0 = 0.04 + i * 0.085, r0 = 0.03 + i * 0.03;
            pts.Add(new[] { r0, y0 });
            pts.Add(new[] { r0 + 0.04, y0 + 0.07 });
        }
        foreach (var p in new[] { new[] { 0.25, 0.5 }, new[] { 0.3, 0.56 }, new[] { 0.31, 0.64 }, new[] { 0.28, 0.76 }, new[] { 0.2, 0.88 }, new[] { 0.1, 0.98 }, new[] { 0.055, 1.05 }, new[] { 0, 1.07 } }) pts.Add(p);
        var lathe = new THREE.LatheGeometry(V2(pts), 36);
        var lp = lathe.attributes.position;
        var col = new float[lp.count * 3];
        var peach = new THREE.Color(0xf4bcab);
        var rose = new THREE.Color(0xe0907f);
        var pale = new THREE.Color(0xfbe3d8);
        var c = new THREE.Color();
        for (int i = 0; i < lp.count; i++)
        {
            double y = lp.getY(i);
            double ang = Math.Atan2(lp.getZ(i), lp.getX(i));
            double band = 0.5 + 0.5 * Math.Sin(y * 34 + ang * 1.5);
            c.copy(peach).lerp(rose, band * 0.7).lerp(pale, Math.Max(0, 0.5 - y) * 0.3);
            col[i * 3] = (float)c.r; col[i * 3 + 1] = (float)c.g; col[i * 3 + 2] = (float)c.b;
        }
        lathe.deleteAttribute("uv");
        lathe.setAttribute("color", new THREE.BufferAttribute(col, 3));
        lathe.computeVertexNormals();
        var parts = new List<THREE.BufferGeometry> { lathe };
        // spiral ridges winding round the body
        double radiusAt(double y)
        {
            for (int i = 1; i < pts.Count; i++)
            {
                if (y <= pts[i][1])
                {
                    double r0 = pts[i - 1][0], y0 = pts[i - 1][1], r1 = pts[i][0], y1 = pts[i][1];
                    return r0 + (r1 - r0) * (y - y0) / Math.Max(1e-4, y1 - y0);
                }
            }
            return 0;
        }
        foreach (var off in new[] { 0, Math.PI })
        {
            var path = new THREE.CatmullRomCurve3(Enumerable.Range(0, 60).Select(i =>
            {
                double y = 0.12 + i / 59.0 * 0.86;
                double a = off + y * 15;
                double r = radiusAt(y) + 0.008;
                return new THREE.Vector3(Math.Cos(a) * r, y, Math.Sin(a) * r);
            }).ToList());
            parts.Add(tint(new THREE.TubeGeometry(path, 90, 0.012, 6), 0xd98978));
        }
        // knobs round the shoulder
        for (int i = 0; i < 9; i++)
        {
            double a = i / 9.0 * Math.PI * 2;
            var k = new THREE.ConeGeometry(0.035, 0.1, 6);
            k.rotateZ(-Math.PI / 2);
            k.rotateY(-a);
            k.translate(Math.Cos(a) * 0.3, 0.57, Math.Sin(a) * 0.3);
            parts.Add(tint(k, 0xe9a595));
        }
        var body = new THREE.Mesh(THREE.BufferGeometryUtils.mergeGeometries(parts, false), new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.45 });
        body.castShadow = true;
        // lay it on its side: the apex to the left, the canal to the right
        var shell = new THREE.Group();
        shell.add(body);
        // the flared lip along one side of the body whorl, glossy pink inside
        var lipMat = new THREE.MeshStandardMaterial { color = 0xf59a86, roughness = 0.25, side = THREE.Side.DoubleSide };
        var lip = new THREE.Mesh(new THREE.SphereGeometry(0.3, 24, 14, 0, Math.PI, 0, Math.PI), lipMat);
        lip.scale.set(0.55, 1.15, 1);
        lip.position.set(0.02, 0.72, 0.0);
        lip.rotation.y = Math.PI / 2;
        shell.add(lip);
        var inner = new THREE.Mesh(new THREE.CircleGeometry(0.27, 24), new THREE.MeshStandardMaterial { color = 0xffc9b8, roughness = 0.2, side = THREE.Side.DoubleSide });
        inner.scale.set(0.55, 1.1, 1);
        inner.position.set(0.15, 0.72, 0.0);
        inner.rotation.y = Math.PI / 2;
        shell.add(inner);
        shell.rotation.z = -Math.PI / 2;
        shell.scale.setScalar(0.75);
        shell.position.set(-0.4, 0.2, 0);
        g.add(shell);
    }

    /// <summary>A wooden treasure chest: a planked box and a domed lid, bound in iron straps, with corner plates and a gold lock.</summary>
    private static void buildCache(THREE.Group g)
    {
        var wood = new THREE.MeshStandardMaterial { color = 0x7a4a24, roughness = 0.85 };
        var dark = new THREE.MeshStandardMaterial { color = 0x4a2c14, roughness = 0.9 };
        var band = new THREE.MeshStandardMaterial { color = 0x2c2f34, roughness = 0.45, metalness = 0.7 };
        var gold = metal(0xf2c14e, 0.25);
        const double W = 0.72, D = 0.46, BH = 0.3;
        // the box, with plank lines
        add(g, new THREE.BoxGeometry(W, BH, D), wood, 0, 0, 0);
        foreach (var y in new[] { -0.05, 0.05 }) add(g, new THREE.BoxGeometry(W + 0.004, 0.008, D + 0.004), dark, 0, y, 0);
        // the lid: half a cylinder lying along x, with flat half-disc ends so it is closed
        var lid = add(g, new THREE.CylinderGeometry(D / 2, D / 2, W, 24, 1, false, 0, Math.PI), wood, 0, BH / 2, 0);
        lid.rotation.z = Math.PI / 2;
        foreach (var sx in new[] { -1, 1 })
        {
            var cap = add(g, new THREE.CircleGeometry(D / 2, 24, 0, Math.PI), wood, sx * (W / 2 + 0.001), BH / 2, 0);
            cap.rotation.y = sx * (Math.PI / 2);
        }
        // iron straps over the lid and down the front, back and sides of the box
        foreach (var x in new[] { -0.24, 0.24 })
        {
            var arch = add(g, new THREE.CylinderGeometry(D / 2 + 0.012, D / 2 + 0.012, 0.06, 24, 1, false, 0, Math.PI), band, x, BH / 2, 0);
            arch.rotation.z = Math.PI / 2;
            foreach (var sz in new[] { -1, 1 }) add(g, new THREE.BoxGeometry(0.06, BH + 0.01, 0.014), band, x, 0, sz * (D / 2 + 0.003));
            add(g, new THREE.BoxGeometry(0.06, 0.014, D + 0.01), band, x, -BH / 2 + 0.002, 0);
        }
        // corner plates and studs
        foreach (var sx in new[] { -1, 1 })
        foreach (var sz in new[] { -1, 1 })
        {
            add(g, new THREE.BoxGeometry(0.05, 0.12, 0.05), band, sx * (W / 2 - 0.005), -0.09, sz * (D / 2 - 0.005));
            add(g, new THREE.SphereGeometry(0.014, 6, 5), gold, sx * (W / 2 + 0.002), 0.05, sz * (D / 2 - 0.05));
        }
        // the lock plate and hasp on the front
        add(g, new THREE.BoxGeometry(0.13, 0.15, 0.025), gold, 0, 0.1, D / 2 + 0.014);
        add(g, new THREE.BoxGeometry(0.09, 0.09, 0.03), band, 0, 0.1, D / 2 + 0.03);
        add(g, new THREE.CylinderGeometry(0.014, 0.014, 0.04, 8), gold, 0, 0.1, D / 2 + 0.05).rotation.x = Math.PI / 2;
    }

    /// <summary>A glass bottle lying on its side, sealed with a cork and wax, with a rolled map inside.</summary>
    private static void buildMessageBottle(THREE.Group g)
    {
        var glass = new THREE.MeshStandardMaterial { color = 0x7fc29a, roughness = 0.1, metalness = 0.05, transparent = true, opacity = 0.45, side = THREE.Side.DoubleSide, depthWrite = false };
        var paper = new THREE.MeshStandardMaterial { color = 0xe9d9a8, roughness = 0.9 };
        var ink = new THREE.MeshStandardMaterial { color = 0xb89a5c, roughness = 0.9 };
        var cork = new THREE.MeshStandardMaterial { color = 0xb98b57, roughness = 0.9 };
        var wax = new THREE.MeshStandardMaterial { color = 0xa02a2a, roughness = 0.5 };
        var pts = V2(new[]
        {
            new[] { 0, -0.3 }, new[] { 0.07, -0.3 }, new[] { 0.115, -0.28 }, new[] { 0.13, -0.24 }, new[] { 0.13, 0.1 }, new[] { 0.125, 0.16 }, new[] { 0.105, 0.22 },
            new[] { 0.075, 0.27 }, new[] { 0.052, 0.31 }, new[] { 0.046, 0.4 }, new[] { 0.054, 0.41 }, new[] { 0.056, 0.435 }, new[] { 0.05, 0.44 },
        });
        // the bottle's axis runs along x, neck toward -x
        var bottle = add(g, new THREE.LatheGeometry(pts, 24), glass, 0, 0.14, 0);
        bottle.rotation.z = Math.PI / 2;
        bottle.renderOrder = 2;
        // the rolled map lies in the body, with a few turns showing along it
        var scroll = add(g, new THREE.CylinderGeometry(0.05, 0.05, 0.36, 14), paper, 0.04, 0.14, 0);
        scroll.rotation.z = Math.PI / 2;
        foreach (var x in new[] { -0.1, -0.02, 0.06, 0.14, 0.22 }) add(g, new THREE.TorusGeometry(0.052, 0.005, 5, 14), ink, x, 0.14, 0).rotation.y = Math.PI / 2;
        var plug = add(g, new THREE.CylinderGeometry(0.047, 0.04, 0.1, 12), cork, -0.425, 0.14, 0);
        plug.rotation.z = Math.PI / 2;
        var seal = add(g, new THREE.SphereGeometry(0.052, 10, 8), wax, -0.485, 0.14, 0);
        seal.scale.set(0.45, 1, 1);
        var rib = add(g, new THREE.TorusGeometry(0.052, 0.01, 6, 14), new THREE.MeshStandardMaterial { color = 0xc0392b, roughness = 0.7 }, -0.33, 0.14, 0);
        rib.rotation.y = Math.PI / 2;
    }

    /// <summary>A trapdoor set into the sand: a frame, weathered planks with iron straps and a ring. Open, the door stands up and the strongbox sits below.</summary>
    private static void buildHatch(THREE.Group g, bool open)
    {
        var frameMat = new THREE.MeshStandardMaterial { color = 0x4a3322, roughness = 0.95 };
        var plankMats = new[] { 0x6b4a30, 0x5e4129, 0x735234, 0x57402a }.Select(c => new THREE.MeshStandardMaterial { color = c, roughness = 0.95 }).ToArray();
        var iron = new THREE.MeshStandardMaterial { color = 0x2c2f34, roughness = 0.5, metalness = 0.7 };
        const double S = 1.0;
        foreach (var (x, z, w, d) in new[] { (0.0, -S / 2, S + 0.16, 0.1), (0, S / 2, S + 0.16, 0.1), (-S / 2, 0, 0.1, S), (S / 2, 0, 0.1, S) })
            add(g, new THREE.BoxGeometry(w, 0.07, d), frameMat, x, 0.02, z);
        var door = new THREE.Group();
        double plankW = (S - 0.04) / 5;
        for (int i = 0; i < 5; i++)
        {
            var p = add(door, new THREE.BoxGeometry(plankW - 0.01, 0.045, S - 0.08), plankMats[i % plankMats.Length], -S / 2 + 0.04 + plankW * (i + 0.5), 0, (S - 0.08) / 2);
            p.rotation.y = (i % 2 == 1 ? 1 : -1) * 0.004;
        }
        foreach (var z in new[] { 0.2, 0.78 }) add(door, new THREE.BoxGeometry(S - 0.06, 0.02, 0.08), iron, 0, 0.032, z);
        var ring = add(door, new THREE.TorusGeometry(0.07, 0.014, 6, 16), iron, 0, 0.07, 0.5);
        ring.rotation.x = open ? 0 : -0.5;
        foreach (var x in new[] { -0.3, 0.3 }) add(door, new THREE.CylinderGeometry(0.025, 0.025, 0.09, 8), iron, x, 0.03, 0.02).rotation.z = Math.PI / 2;
        if (!open)
        {
            door.position.set(0, 0.06, -S / 2 + 0.04);
            g.add(door);
            return;
        }
        // the door hinges up at the back, and a dark shaft opens up with the top of a ladder at the front
        var pit = add(g, new THREE.BoxGeometry(S - 0.06, 0.02, S - 0.06), new THREE.MeshStandardMaterial { color = 0x0a0705, roughness = 1 }, 0, 0.0, 0);
        pit.receiveShadow = true;
        door.position.set(0, 0.07, -S / 2 + 0.02);
        door.rotation.x = -1.75;
        g.add(door);
        var rope = new THREE.MeshStandardMaterial { color = 0x6b4a30, roughness = 0.95 };
        foreach (var x in new[] { -0.25, 0.25 })
        {
            var rail = add(g, new THREE.CylinderGeometry(0.025, 0.025, 0.5, 8), rope, x, 0.16, 0.36);
            rail.rotation.x = 0.0;
        }
        foreach (var y in new[] { 0.0, 0.14, 0.28 }) add(g, new THREE.CylinderGeometry(0.018, 0.018, 0.5, 6), rope, 0, y + 0.04, 0.36).rotation.z = Math.PI / 2;
    }

    /// <summary>A real five-armed starfish: a rounded star that is thick in the middle and tapers to the tips, with a ridge of bumps along each arm.</summary>
    private static void buildStarfish(THREE.Group g)
    {
        const int arms = 5;
        double radius(double th) => 0.1 + 0.3 * Math.Pow(Math.Abs(Math.Cos(arms / 2.0 * th)), 3.2);
        double topH(double f, double armness) =>
            (0.045 + 0.05 * (1 - f * f)) * (0.55 + 0.45 * (1 - f)) * (0.6 + 0.4 * armness + 0.3 * (1 - f)) * Math.Pow(1 - f * f * f, 0.6) * 1.25;
        const int rings = 10, around = 100;
        var top = new THREE.Color(0xef7f2e);
        var ridge = new THREE.Color(0xf6b25a);
        var belly = new THREE.Color(0xf3d6a6);
        var pos = new List<double>();
        var col = new List<double>();
        var idx = new List<int>();
        var c = new THREE.Color();
        // two surfaces share the rim: the domed back (s = 1) and the flatter underside (s = -1)
        foreach (var s in new[] { 1, -1 })
        {
            int bas = pos.Count / 3;
            for (int k = 0; k <= rings; k++)
            {
                double f = (double)k / rings;
                for (int j = 0; j < around; j++)
                {
                    double th = (double)j / around * Math.PI * 2;
                    double r = radius(th) * f;
                    double armness = Math.Pow(Math.Abs(Math.Cos(arms / 2.0 * th)), 3.2);
                    double h = s == 1 ? topH(f, armness) : -topH(f, armness) * 0.36;
                    pos.Add(Math.Cos(th) * r); pos.Add(h + 0.05); pos.Add(Math.Sin(th) * r);
                    if (s == 1) c.copy(top).lerp(ridge, Math.Pow(armness, 3) * (1 - f * 0.5) * 0.9);
                    else c.copy(belly);
                    col.Add(c.r); col.Add(c.g); col.Add(c.b);
                }
            }
            for (int k = 0; k < rings; k++)
            for (int j = 0; j < around; j++)
            {
                int a = bas + k * around + j, b = bas + k * around + (j + 1) % around, cc = a + around, d = b + around;
                if (s == 1) idx.AddRange(new[] { a, b, cc, b, d, cc });
                else idx.AddRange(new[] { a, cc, b, b, cc, d });
            }
        }
        var geo = new THREE.BufferGeometry();
        geo.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        geo.setAttribute("color", new THREE.Float32BufferAttribute(col, 3));
        geo.setIndex(idx);
        geo.computeVertexNormals();
        g.add(new THREE.Mesh(geo, new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.75 }));
        // the little bumps along each arm, and the sieve plate in the middle
        var bump = new THREE.MeshStandardMaterial { color = 0xfbe0b0, roughness = 0.6 };
        for (int i = 0; i < arms; i++)
        {
            double th = (double)i / arms * Math.PI * 2;
            for (int k = 0; k < 6; k++)
            {
                double r = 0.1 + k * 0.045, sc = 1 - k * 0.1;
                var m = add(g, new THREE.SphereGeometry(0.014 * sc + 0.004, 8, 6), bump, Math.Cos(th) * r, 0.05 + topH(r / 0.4, 1) + 0.004, Math.Sin(th) * r);
                m.scale.y = 0.8;
            }
        }
        add(g, new THREE.CircleGeometry(0.03, 10), new THREE.MeshStandardMaterial { color = 0xffe6b8, roughness = 0.5 }, 0, 0.05 + topH(0.02, 0.2) + 0.004, 0).rotation.x = -Math.PI / 2;
    }

    private static void buildShipBell(THREE.Group g)
    {
        var brass = metal(0xc79a3a, 0.3);
        brass.side = THREE.Side.DoubleSide;
        // the wall of the bell has a thickness: out along the outside, round the lip, and back up the inside
        var pts = V2(new[]
        {
            new[] { 0.0, 0.36 }, new[] { 0.07, 0.35 }, new[] { 0.1, 0.3 }, new[] { 0.14, 0.16 }, new[] { 0.2, 0.02 }, new[] { 0.27, -0.1 }, new[] { 0.31, -0.17 }, new[] { 0.325, -0.2 }, new[] { 0.29, -0.2 }, new[] { 0.27, -0.16 },
            new[] { 0.2, -0.07 }, new[] { 0.14, 0.04 }, new[] { 0.09, 0.2 }, new[] { 0.0, 0.3 },
        });
        add(g, new THREE.LatheGeometry(pts, 32), brass, 0, 0, 0);
        // a raised band around the waist and the lip
        add(g, new THREE.TorusGeometry(0.2, 0.012, 6, 32), brass, 0, 0.0, 0).rotation.x = Math.PI / 2;
        add(g, new THREE.TorusGeometry(0.31, 0.014, 6, 32), brass, 0, -0.17, 0).rotation.x = Math.PI / 2;
        var hang = add(g, new THREE.TorusGeometry(0.06, 0.016, 8, 14), brass, 0, 0.4, 0);
        hang.rotation.y = Math.PI / 2;
        add(g, new THREE.SphereGeometry(0.055, 12, 10), metal(0x5d4720, 0.5), 0, -0.12, 0);
    }

    private static void buildPlate(THREE.Group g)
    {
        var white = new THREE.MeshStandardMaterial { color = 0xf5f3ee, roughness = 0.25 };
        var blue = new THREE.MeshStandardMaterial { color = 0x2f5fa8, roughness = 0.3 };
        add(g, new THREE.CylinderGeometry(0.36, 0.26, 0.05, 28), white, 0, 0, 0);
        add(g, new THREE.TorusGeometry(0.33, 0.012, 6, 28), blue, 0, 0.028, 0).rotation.x = Math.PI / 2;
        add(g, new THREE.CylinderGeometry(0.17, 0.17, 0.012, 24), blue, 0, 0.028, 0);
        for (int i = 0; i < 6; i++)
        {
            double a = i / 6.0 * Math.PI * 2;
            add(g, new THREE.SphereGeometry(0.036, 8, 6), blue, Math.Cos(a) * 0.23, 0.03, Math.Sin(a) * 0.23);
        }
    }

    /// <summary>One valve of an oyster: a ridged, flattened dome that is a little lopsided, like a real shell.</summary>
    private static THREE.Mesh oysterValve(THREE.Material mat, bool flip)
    {
        var geo = new THREE.SphereGeometry(0.3, 28, 14, 0, Math.PI * 2, 0, Math.PI / 2);
        var p = geo.attributes.position;
        for (int i = 0; i < p.count; i++)
        {
            double x = p.getX(i), y = p.getY(i), z = p.getZ(i);
            double ang = Math.Atan2(z, x);
            double ridge = 1 + 0.05 * Math.Cos(16 * ang) * (1 - y / 0.3) + 0.04 * Math.Sin(5 * ang);
            p.setXYZ(i, x * ridge * 1.05, y * (flip ? -0.42 : 0.42), z * ridge * 0.86);
        }
        geo.computeVertexNormals();
        return new THREE.Mesh(geo, mat) { castShadow = true };
    }

    private static void buildOyster(THREE.Group g)
    {
        var shell = new THREE.MeshStandardMaterial { color = 0x8c8574, roughness = 0.95, side = THREE.Side.DoubleSide };
        var inner = new THREE.MeshStandardMaterial { color = 0xeae0f2, roughness = 0.2, metalness = 0.25, side = THREE.Side.DoubleSide };
        var pearl = new THREE.MeshStandardMaterial { color = 0xfaf6ee, roughness = 0.1, metalness = 0.3 };
        var low = oysterValve(shell, true);
        low.position.y = 0.1;
        var lowIn = oysterValve(inner, true);
        lowIn.scale.setScalar(0.93);
        lowIn.position.y = 0.12;
        // the other valve hangs open on its hinge at the back
        var hinge = new THREE.Group();
        hinge.position.set(0, 0.11, -0.24);
        hinge.rotation.x = -0.75;
        var lid = oysterValve(shell, false);
        lid.position.set(0, 0, 0.24);
        var lidIn = oysterValve(inner, false);
        lidIn.scale.setScalar(0.93);
        lidIn.position.set(0, -0.015, 0.24);
        hinge.add(lid, lidIn);
        g.add(low, lowIn, hinge);
        var p = add(g, new THREE.SphereGeometry(0.07, 16, 12), pearl, 0, 0.17, 0.02);
        p.scale.y = 0.95;
    }

    private static void buildGoldBar(THREE.Group g)
    {
        var gold = metal(0xf5c542, 0.2);
        var stamp = metal(0xc99a2a, 0.45);
        // an ingot is a trapezoid prism, wider at the bottom, with softly rounded edges
        var sh = new THREE.Shape();
        sh.moveTo(-0.12, 0);
        sh.lineTo(0.12, 0);
        sh.lineTo(0.082, 0.085);
        sh.lineTo(-0.082, 0.085);
        sh.closePath();
        var geo = new THREE.ExtrudeGeometry(sh, new THREE.ExtrudeGeometryOptions { depth = 0.46, bevelEnabled = true, bevelSize = 0.014, bevelThickness = 0.014, bevelSegments = 2 });
        geo.translate(0, 0, -0.23);
        void bar(double x, double y, double z, double r = 0)
        {
            var b = add(g, geo, gold, x, y, z);
            b.rotation.y = r;
            add(g, new THREE.BoxGeometry(0.08, 0.006, 0.12), stamp, x, y + 0.092, z).rotation.y = r;
        }
        bar(-0.14, 0, 0.0, 0.04);
        bar(0.14, 0, 0.02, -0.03);
        bar(0, 0.1, 0.0, 0.05);
    }

    /// <summary>Builds the clean, shiny procedural model for an item id.</summary>
    public static THREE.Group buildItem(string defId)
    {
        Items.DEF_BY_ID.TryGetValue(defId, out var def);
        var g = new THREE.Group();
        // the captain's hatch is a scene piece rather than a find, so it has no definition and sits on the ground as it was built
        bool piece = defId == "captain_hatch" || defId == "captain_hatch_open";
        if (def == null && !piece) return g;
        switch (defId)
        {
            case "pulltab": buildPullTab(g); break;
            case "nail": buildNail(g); break;
            case "tincan": buildTinCan(g); break;
            case "treasure_cache": buildCache(g); break;
            case "captain_hatch": buildHatch(g, false); break;
            case "captain_hatch_open": buildHatch(g, true); break;
            case "message_bottle": buildMessageBottle(g); break;
            case "starfish": buildStarfish(g); break;
            case "ship_bell": buildShipBell(g); break;
            case "porcelain_plate": buildPlate(g); break;
            case "sea_oyster": buildOyster(g); break;
            case "gold_bar": buildGoldBar(g); break;
            case "black_pearl": buildBlackPearl(g); break;
            case "stone_idol": buildIdol(g); break;
            case "conch": buildConch(g); break;
            case "piece_eight":
            case "penny": case "quarter": case "doubloon": case "dime": case "nickel":
            case "silver_dollar": case "real": case "denarius": case "sovereign":
                buildCoin(g, defId);
                break;
            case "bottle_cap": buildBottleCap(g); break;
            case "glass_bottle": buildBottle(g); break;
            case "fishing_hook": buildHook(g); break;
            case "flip_flop": buildFlipFlop(g); break;
            case "rusty_bolt": buildBolt(g); break;
            case "plastic_ring": buildPlasticRing(g); break;
            case "pocket_watch": buildWatch(g); break;
            case "pearl_necklace": buildPearls(g); break;
            case "sunglasses": buildSunglasses(g); break;
            case "harmonica": buildHarmonica(g); break;
            case "wedding_band": buildWeddingBand(g); break;
            case "toy_boat": buildBoat(g); break;
            case "signet": case "ruby_band": buildRing(g, defId); break;
            case "compass": buildCompass(g); break;
            case "brass_key": buildKey(g); break;
            default:
                if (def != null && def.shardSet != null && def.shardIndex != null) buildShard(g, def.shardSet, def.shardIndex.Value);
                break;
        }
        if (piece)
        {
            var keep = new THREE.Group();
            keep.add(g);
            return keep;
        }
        // centre on its bounding box so the dirt crust encloses it evenly
        var box = new THREE.Box3().setFromObject(g);
        var c = box.getCenter(new THREE.Vector3());
        var root = new THREE.Group();
        g.position.sub(c);
        root.add(g);
        return root;
    }

    private static readonly THREE.MeshStandardMaterial SAND = new THREE.MeshStandardMaterial { color = 0x9c825b, roughness = 0.95, metalness = 0 };

    /// <summary>Sand-caked version of an item, shown lying in the dug hole.</summary>
    public static THREE.Group buildDirty(string defId)
    {
        var g = buildItem(defId);
        g.traverse(o =>
        {
            if (o is THREE.Mesh m)
            {
                m.material = SAND;
                m.castShadow = true;
                m.receiveShadow = true;
            }
        });
        return g;
    }

    public static HTMLCanvasElement newCrustCanvas()
    {
        var c = new HTMLCanvasElement(256, 256);
        var g = c.getContext("2d");
        g.fillStyle = "#fff";
        g.fillRect(0, 0, 256, 256);
        return c;
    }

    /// <summary>
    /// Wraps a clean item in one unified ellipsoid dirt crust. The crust's UVs are the
    /// regular sphere mapping, so brushing never depends on the item's own (messy) UVs.
    /// </summary>
    public static CrustRig wrapWithCrust(THREE.Group item, HTMLCanvasElement canvas, double fitSize = 2.0)
    {
        var root = new THREE.Group();
        var box = new THREE.Box3().setFromObject(item);
        var size = box.getSize(new THREE.Vector3());
        double maxDim = JS.Max(size.x, size.y, size.z, 0.01);
        double k = fitSize / maxDim;
        var inner = new THREE.Group();
        inner.add(item);
        inner.scale.setScalar(k);
        root.add(inner);

        var half = size.clone().multiplyScalar(0.5 * k);
        double maxHalf = JS.Max(half.x, half.y, half.z);
        double rx = Math.Max(half.x * 1.5, maxHalf * 0.2);
        double ry = Math.Max(half.y * 1.5, maxHalf * 0.2);
        double rz = Math.Max(half.z * 1.5, maxHalf * 0.2);

        var texture = new THREE.CanvasTexture(canvas);
        var crust = new THREE.Mesh(new THREE.SphereGeometry(1, 64, 64),
            new THREE.MeshStandardMaterial { color = 0x9c825b, roughness = 0.95, transparent = true, alphaMap = texture, side = THREE.Side.DoubleSide, depthWrite = false });
        crust.scale.set(rx, ry, rz);
        crust.castShadow = true;
        root.add(crust);
        return new CrustRig { root = root, crust = crust, canvas = canvas, texture = texture, size = fitSize };
    }
}
