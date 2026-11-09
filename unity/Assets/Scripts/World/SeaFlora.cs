using System;
using System.Collections.Generic;
using System.Linq;

// Port of src/world/SeaFlora.ts

public static class SeaFlora
{
    private static readonly THREE.MeshStandardMaterial flora = new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.8, side = THREE.Side.DoubleSide };
    private static readonly THREE.Vector3 Y = new THREE.Vector3(0, 1, 0);

    /// <summary>Keeps only the attributes every piece shares, so pieces can be merged into one mesh.</summary>
    private static THREE.BufferGeometry clean(THREE.BufferGeometry g)
    {
        g.deleteAttribute("uv");
        return g;
    }

    /// <summary>Colours a piece from `lo` at its lowest point to `hi` at `top`.</summary>
    private static THREE.BufferGeometry paint(THREE.BufferGeometry g, THREE.Color lo, THREE.Color hi, double top, double bottom = 0)
    {
        var p = g.attributes.position;
        var col = new float[p.count * 3];
        var c = new THREE.Color();
        for (int i = 0; i < p.count; i++)
        {
            double t = Math.Min(1, Math.Max(0, (p.getY(i) - bottom) / Math.Max(0.001, top - bottom)));
            c.copy(lo).lerp(hi, t);
            col[i * 3] = (float)c.r;
            col[i * 3 + 1] = (float)c.g;
            col[i * 3 + 2] = (float)c.b;
        }
        g.setAttribute("color", new THREE.BufferAttribute(col, 3));
        return g;
    }

    private static THREE.Mesh finish(List<THREE.BufferGeometry> parts)
    {
        var geo = THREE.BufferGeometryUtils.mergeGeometries(parts, false);
        geo.computeVertexNormals();
        return new THREE.Mesh(geo, flora);
    }

    /// <summary>A flat, tapering leaf that curves over and twists a little.</summary>
    private static THREE.BufferGeometry blade(double len, double width, double bend, double twist, int segs, Func<double> rand)
    {
        var pos = new List<double>();
        var idx = new List<int>();
        double phase = rand() * 6;
        for (int i = 0; i <= segs; i++)
        {
            double t = (double)i / segs;
            double w = width / 2 * (1 - Math.Pow(t, 1.7) * 0.92) * (0.85 + 0.15 * Math.Sin(t * 9 + phase));
            double cx = bend * len * t * t + Math.Sin(t * 5 + phase) * 0.04 * len * t;
            double cy = t * len * (1 - 0.12 * t * Math.Abs(bend));
            double cz = Math.Sin(t * twist + phase) * 0.05 * len * t;
            double a = Math.Sin(t * twist * 0.8 + phase) * 0.9;
            pos.AddRange(new[] { cx - Math.Cos(a) * w, cy, cz - Math.Sin(a) * w, cx + Math.Cos(a) * w, cy, cz + Math.Sin(a) * w });
            if (i < segs)
            {
                int k = i * 2;
                idx.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 });
            }
        }
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setIndex(idx);
        g.computeVertexNormals();
        return g;
    }

    private static THREE.Mesh tuft(int count, double len0, double len1, double width, double spread, int segs, double twist, THREE.Color lo, THREE.Color hi, Func<double> rand)
    {
        var parts = new List<THREE.BufferGeometry>();
        double top = 0;
        var made = new List<THREE.BufferGeometry>();
        for (int i = 0; i < count; i++)
        {
            double l = len0 + rand() * (len1 - len0);
            var g = blade(l, width * (0.8 + rand() * 0.5), (rand() - 0.5) * 0.7, twist, segs, rand);
            g.rotateY(rand() * Math.PI * 2);
            double tx = (rand() - 0.5) * spread, tz = (rand() - 0.5) * spread;
            g.translate(tx, 0, tz);
            top = Math.Max(top, l);
            made.Add(g);
        }
        foreach (var g in made)
        {
            var shade = new THREE.Color().copy(lo).offsetHSL((rand() - 0.5) * 0.04, 0, (rand() - 0.5) * 0.08);
            parts.Add(paint(g, shade, hi, top));
        }
        return finish(parts);
    }

    /// <summary>A low clump of thin grass-like blades.</summary>
    public static THREE.Mesh seagrass(Func<double> rand) =>
        tuft(9 + (int)Math.Floor(rand() * 7), 0.45, 1.1, 0.06, 0.35, 5, 3, new THREE.Color(0x2c6b3c), new THREE.Color(0xa6d86a), rand);

    /// <summary>Tall, wide golden-green ribbons that sway.</summary>
    public static THREE.Mesh kelp(Func<double> rand) =>
        tuft(3 + (int)Math.Floor(rand() * 3), 2.4, 4.4, 0.3, 0.3, 14, 4, new THREE.Color(0x3c5a22), new THREE.Color(0xc4cf4a), rand);

    /// <summary>A short clump of broad, bright blades (eel grass).</summary>
    public static THREE.Mesh eelgrass(Func<double> rand) =>
        tuft(5 + (int)Math.Floor(rand() * 3), 0.9, 1.7, 0.16, 0.3, 8, 2, new THREE.Color(0x287a4a), new THREE.Color(0x7fe08a), rand);

    private static void branch(List<THREE.BufferGeometry> parts, THREE.Vector3 from, THREE.Vector3 dir, double len, double r, int depth, Func<double> rand)
    {
        var g = clean(new THREE.CylinderGeometry(r * 0.66, r, len, 7, 1));
        g.translate(0, len / 2, 0);
        g.applyQuaternion(new THREE.Quaternion().setFromUnitVectors(Y, dir));
        g.translate(from.x, from.y, from.z);
        parts.Add(g);
        var end = from.clone().addScaledVector(dir, len);
        if (depth <= 0)
        {
            var t = clean(new THREE.SphereGeometry(r * 0.7, 7, 5));
            t.translate(end.x, end.y, end.z);
            parts.Add(t);
            return;
        }
        int n = rand() < 0.35 ? 3 : 2;
        for (int i = 0; i < n; i++)
        {
            double ax = rand() - 0.5, az = rand() - 0.5;
            var axis = new THREE.Vector3(ax, 0, az).normalize();
            var d = dir.clone().applyAxisAngle(axis, 0.4 + rand() * 0.45);
            d.y += 0.25;
            branch(parts, end, d.normalize(), len * (0.7 + rand() * 0.12), r * 0.66, depth - 1, rand);
        }
    }

    /// <summary>Antler-shaped branching coral that thins toward pale tips.</summary>
    public static THREE.Mesh staghorn(Func<double> rand, THREE.Color lo, THREE.Color hi)
    {
        var parts = new List<THREE.BufferGeometry>();
        int n = 2 + (int)Math.Floor(rand() * 2);
        for (int i = 0; i < n; i++)
        {
            double a = rand() * Math.PI * 2;
            var dir = new THREE.Vector3(Math.Cos(a) * 0.35, 1, Math.Sin(a) * 0.35).normalize();
            double fx = (rand() - 0.5) * 0.25, fz = (rand() - 0.5) * 0.25;
            branch(parts, new THREE.Vector3(fx, 0, fz), dir, 0.34 + rand() * 0.1, 0.07, 3, rand);
        }
        double top = 0;
        foreach (var g in parts)
        {
            g.computeBoundingBox();
            top = Math.Max(top, g.boundingBox.max.y);
        }
        foreach (var g in parts) paint(g, lo, hi, top);
        return finish(parts);
    }

    /// <summary>A rounded dome covered in winding grooves.</summary>
    public static THREE.Mesh brainCoral(Func<double> rand, THREE.Color lo, THREE.Color hi)
    {
        var g = clean(new THREE.SphereGeometry(0.5, 30, 20, 0, Math.PI * 2, 0, Math.PI / 2));
        var p = g.attributes.position;
        var col = new float[p.count * 3];
        double ph = rand() * 10;
        var c = new THREE.Color();
        for (int i = 0; i < p.count; i++)
        {
            double x = p.getX(i), y = p.getY(i), z = p.getZ(i);
            double d = Math.Sin(24 * x + 4 * Math.Sin(15 * z + ph) + 8 * y) * Math.Sin(20 * z + 3 * Math.Sin(13 * x + ph) + 6 * y);
            double k = 1 + 0.045 * d;
            p.setXYZ(i, x * k, y * k * 0.75, z * k);
            c.copy(lo).lerp(hi, d * 0.5 + 0.5);
            col[i * 3] = (float)c.r;
            col[i * 3 + 1] = (float)c.g;
            col[i * 3 + 2] = (float)c.b;
        }
        g.setAttribute("color", new THREE.BufferAttribute(col, 3));
        return finish(new List<THREE.BufferGeometry> { g });
    }

    /// <summary>A ring of open tubes of different heights.</summary>
    public static THREE.Mesh tubeCoral(Func<double> rand, THREE.Color lo, THREE.Color hi)
    {
        var parts = new List<THREE.BufferGeometry>();
        int n = 6 + (int)Math.Floor(rand() * 5);
        for (int i = 0; i < n; i++)
        {
            double h = 0.3 + rand() * 0.5;
            double r = 0.055 + rand() * 0.035;
            double a = rand() * Math.PI * 2;
            double d = 0.05 + rand() * 0.22;
            var tube = clean(new THREE.CylinderGeometry(r, r * 1.15, h, 10, 1, true));
            tube.translate(0, h / 2, 0);
            var rim = clean(new THREE.TorusGeometry(r, r * 0.22, 6, 12));
            rim.rotateX(Math.PI / 2);
            rim.translate(0, h, 0);
            double lx = (rand() - 0.5) * 0.35, lz = (rand() - 0.5) * 0.35;
            var lean = new THREE.Euler(lx, 0, lz);
            foreach (var g in new[] { tube, rim })
            {
                g.applyMatrix4(new THREE.Matrix4().makeRotationFromEuler(lean));
                g.translate(Math.Cos(a) * d, 0, Math.Sin(a) * d);
                paint(g, lo, hi, 0.8);
                parts.Add(g);
            }
        }
        return finish(parts);
    }

    private static THREE.CanvasTexture fanTex;

    private static THREE.CanvasTexture fanTexture()
    {
        if (fanTex != null) return fanTex;
        var cv = new HTMLCanvasElement(256, 256);
        var c = cv.getContext("2d");
        c.strokeStyle = "#fff";
        c.lineCap = "round";
        const double cx = 128;
        const double cy = 252;
        (double x, double y) polar(double r, double a) => (cx + Math.Sin(a) * r, cy - Math.Cos(a) * r);
        void moveTo((double x, double y) p) => c.moveTo(p.x, p.y);
        void lineTo((double x, double y) p) => c.lineTo(p.x, p.y);
        // a short stalk, then rays that spread out and fork, joined by wavy cross-strands: a lacy net in the shape of a fan
        c.lineWidth = 9;
        c.beginPath();
        c.moveTo(cx, 256);
        lineTo(polar(48, 0));
        c.stroke();
        const double spread = 1.0;
        const int rays = 11;
        for (int i = 0; i < rays; i++)
        {
            double a = -spread + 2 * spread * i / (rays - 1);
            c.lineWidth = 4;
            c.beginPath();
            moveTo(polar(46, a * 0.3));
            lineTo(polar(120, a * 0.85));
            lineTo(polar(236, a));
            c.stroke();
            // a fork between this ray and the next
            if (i < rays - 1)
            {
                double m = a + spread / (rays - 1);
                c.lineWidth = 3;
                c.beginPath();
                moveTo(polar(125, m * 0.9));
                lineTo(polar(236, m));
                c.stroke();
            }
        }
        c.lineWidth = 3;
        for (int r = 70; r < 240; r += 24)
        {
            c.beginPath();
            for (int k = 0; k <= 40; k++)
            {
                double a = -spread * (0.55 + r / 520.0) + 2 * spread * (0.55 + r / 520.0) * k / 40;
                double rr = r + Math.Sin(k * 1.9 + r) * 2.5;
                var (x, y) = polar(rr, a);
                if (k == 0) c.moveTo(x, y);
                else c.lineTo(x, y);
            }
            c.stroke();
        }
        fanTex = new THREE.CanvasTexture(cv) { colorSpace = THREE.ColorSpace.SRGBColorSpace };
        return fanTex;
    }

    /// <summary>A lacy sea fan that stands on a short stalk.</summary>
    public static THREE.Group seaFan(int color)
    {
        var g = new THREE.Group();
        var fan = new THREE.Mesh(
            new THREE.PlaneGeometry(1.25, 1.25),
            new THREE.MeshStandardMaterial { map = fanTexture(), color = color, alphaTest = 0.45, side = THREE.Side.DoubleSide, roughness = 0.8 });
        fan.position.y = 0.62;
        g.add(fan);
        return g;
    }
}
