using System;
using System.Collections.Generic;

/// <summary>
/// What every ship in the game is built from: small coloured solids (boxes, rods, spheres) and lofted shapes, welded into one
/// mesh per material so a whole ship costs a handful of draws. The colour of each piece is its vertex colour, so one wooden
/// material can carry tar, paint, brass and rope at once. Used by <see cref="OldShip"/> (the Marigold of the opening) and by
/// <see cref="ModernShips"/> (the freighters and liners that pass along the horizon at night).
/// </summary>
public class ShipKit
{
    private static readonly THREE.Vector3 Y_AXIS = new THREE.Vector3(0, 1, 0);

    private readonly List<THREE.BufferGeometry> parts = new List<THREE.BufferGeometry>();
    private readonly THREE.Object3D place = new THREE.Object3D();

    public int count => parts.Count;

    /// <summary>A ready-made geometry in one flat colour.</summary>
    public void add(THREE.BufferGeometry g, int color) => parts.Add(Ecology.paint(g, color));

    /// <summary>A geometry that already carries its own colours (and normals, and no uv).</summary>
    public void addPainted(THREE.BufferGeometry g) => parts.Add(g);

    private THREE.BufferGeometry placed(THREE.BufferGeometry g, double x, double y, double z, double rx, double ry, double rz)
    {
        place.position.set(x, y, z);
        place.rotation.set(rx, ry, rz);
        place.scale.set(1, 1, 1);
        place.updateMatrix();
        g.applyMatrix4(place.matrix);
        return g;
    }

    /// <summary>Any geometry, turned (x, then y, then z) and set down at a point, in one flat colour.</summary>
    public void put(THREE.BufferGeometry g, double x, double y, double z, int color, double rx = 0, double ry = 0, double rz = 0)
        => add(placed(g, x, y, z, rx, ry, rz), color);

    public void box(double w, double h, double d, double x, double y, double z, int color, double rx = 0, double ry = 0, double rz = 0)
        => add(placed(new THREE.BoxGeometry(w, h, d), x, y, z, rx, ry, rz), color);

    public void cyl(double rTop, double rBot, double h, int segs, double x, double y, double z, int color, double rx = 0, double ry = 0, double rz = 0)
        => add(placed(new THREE.CylinderGeometry(rTop, rBot, h, segs), x, y, z, rx, ry, rz), color);

    /// <summary>A sphere, optionally squashed or stretched along each axis.</summary>
    public void sphere(double r, double x, double y, double z, int color, double sx = 1, double sy = 1, double sz = 1, int ws = 8, int hs = 6)
    {
        var g = new THREE.SphereGeometry(r, ws, hs);
        g.scale(sx, sy, sz);
        g.translate(x, y, z);
        add(g, color);
    }

    /// <summary>A rod of constant thickness between two points: a spar, a stay, a rail, a rope.</summary>
    public void rod(double x0, double y0, double z0, double x1, double y1, double z1, double r, int color, int segs = 4)
    {
        var d = new THREE.Vector3(x1 - x0, y1 - y0, z1 - z0);
        double len = d.length();
        if (len < 1e-6) return;
        var g = new THREE.CylinderGeometry(r, r, len, segs, 1);
        g.applyQuaternion(new THREE.Quaternion().setFromUnitVectors(Y_AXIS, d.clone().normalize()));
        g.translate((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2);
        add(g, color);
    }

    /// <summary>A tapering rod (thicker at its first end): a mast or a spar.</summary>
    public void spar(double x0, double y0, double z0, double x1, double y1, double z1, double r0, double r1, int color, int segs = 6)
    {
        var d = new THREE.Vector3(x1 - x0, y1 - y0, z1 - z0);
        double len = d.length();
        if (len < 1e-6) return;
        // the cylinder's top (+y) lies toward the second end
        var g = new THREE.CylinderGeometry(r1, r0, len, segs, 1);
        g.applyQuaternion(new THREE.Quaternion().setFromUnitVectors(Y_AXIS, d.clone().normalize()));
        g.translate((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2);
        add(g, color);
    }

    /// <summary>The kit welded into one mesh.</summary>
    public THREE.Mesh mesh(THREE.Material m, bool shadow = true)
    {
        var mesh = new THREE.Mesh(parts.Count == 0 ? new THREE.BufferGeometry() : THREE.BufferGeometryUtils.mergeGeometries(parts), m);
        mesh.castShadow = shadow;
        return mesh;
    }

    // ---------------------------------------------------------------- lofted shapes

    /// <summary>One colour, scaled: lets a colour be darkened or lightened.</summary>
    public static int shade(int color, double k)
    {
        int r = Math.Min(255, (int)(((color >> 16) & 255) * k));
        int g = Math.Min(255, (int)(((color >> 8) & 255) * k));
        int b = Math.Min(255, (int)((color & 255) * k));
        return (r << 16) | (g << 8) | b;
    }

    /// <summary>Two colours blended: t = 0 is a, t = 1 is b.</summary>
    public static int mix(int a, int b, double t)
    {
        t = Math.Min(1, Math.Max(0, t));
        int r = (int)(((a >> 16) & 255) * (1 - t) + ((b >> 16) & 255) * t);
        int g = (int)(((a >> 8) & 255) * (1 - t) + ((b >> 8) & 255) * t);
        int bl = (int)((a & 255) * (1 - t) + (b & 255) * t);
        return (r << 16) | (g << 8) | bl;
    }

    /// <summary>
    /// A hull skin: the two sides of a ship as one surface from the stem (u = 0) to the stern (u = 1) and from the rail (t = 0)
    /// down to the keel line (t = 1), closed at the bottom and at the stern. `half(u, t)` is the half-width of the hull there,
    /// `height(u, t)` its height above the waterline, and `color(u, t)` its colour. Bow toward -z, centred on the origin.
    /// The faces look outward on both sides.
    /// </summary>
    public static THREE.BufferGeometry loft(double length, int stations, int rows, Func<double, double, double> half, Func<double, double, double> height, Func<double, double, int> color,
        bool closeEnds = true, Func<double, double, double> zAt = null, double t0 = 0, double t1 = 1, double push = 0)
    {
        var pos = new List<double>();
        var col = new List<double>();
        var idx = new List<int>();
        int per = rows + 1;
        void vertex(double x, double y, double z, int c)
        {
            pos.Add(x); pos.Add(y); pos.Add(z);
            var cc = new THREE.Color(c);
            col.Add(cc.r); col.Add(cc.g); col.Add(cc.b);
        }
        foreach (var side in new[] { -1, 1 })
        {
            int start = pos.Count / 3;
            for (int i = 0; i <= stations; i++)
            {
                double u = (double)i / stations;
                for (int j = 0; j <= rows; j++)
                {
                    double t = t0 + (t1 - t0) * j / rows;
                    double h = half(u, t);
                    vertex(side * (h + (h > 0.01 ? push : 0)), height(u, t), -length / 2 + u * length + (zAt != null ? zAt(u, t) : 0), color(u, t));
                }
            }
            for (int i = 0; i < stations; i++)
            for (int j = 0; j < rows; j++)
            {
                int a = start + i * per + j, b = a + per;
                if (side > 0) idx.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                else idx.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
            }
        }
        if (closeEnds)
        {
            int other = (stations + 1) * per;
            // the bottom, from the lowest row of one side to the other's
            for (int i = 0; i < stations; i++)
            {
                int a = i * per + rows, b = a + per;
                idx.AddRange(new[] { a, other + a, b, b, other + a, other + b });
            }
            // the flat stern between the last rib of each side
            int left = stations * per, right = other + stations * per;
            for (int j = 0; j < rows; j++) idx.AddRange(new[] { left + j, right + j, left + j + 1, left + j + 1, right + j, right + j + 1 });
        }
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setAttribute("color", new THREE.Float32BufferAttribute(col, 3));
        g.setIndex(idx);
        g.computeVertexNormals();
        return g;
    }

    /// <summary>
    /// A flat deck between the two rails: `half(u)` is half its width and `y(u)` its height. Colour as given. Looks up.
    /// </summary>
    public static THREE.BufferGeometry deck(double length, double u0, double u1, int stations, Func<double, double> half, Func<double, double> y, int color)
    {
        var pos = new List<double>();
        var idx = new List<int>();
        for (int i = 0; i <= stations; i++)
        {
            double u = u0 + (u1 - u0) * i / stations;
            double z = -length / 2 + u * length;
            pos.AddRange(new[] { -half(u), y(u), z, half(u), y(u), z });
            if (i < stations) idx.AddRange(new[] { i * 2, i * 2 + 2, i * 2 + 1, i * 2 + 1, i * 2 + 2, i * 2 + 3 });
        }
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setIndex(idx);
        g.computeVertexNormals();
        return Ecology.paint(g, color);
    }

    /// <summary>
    /// A square sail hung from a yard: `w` wide at the head, `h` tall, `foot` is the width at its foot as a fraction of `w`, it
    /// bellies by `belly` toward -z (the way the wind pushes) and its foot curves up by `roach`. Cut in vertical panels of cloth,
    /// each a shade different, so it reads as stitched canvas. Hung with its head at y = 0 and its middle at z = 0.
    /// </summary>
    public static THREE.BufferGeometry sail(double w, double h, int panels, double belly, double roach, double foot, int color, double ragged = 0, int seed = 1)
    {
        var rng = Stones.rng(seed);
        var pos = new List<double>();
        var col = new List<double>();
        var idx = new List<int>();
        const int rowsN = 8;
        for (int p = 0; p < panels; p++)
        {
            double cloth = 0.9 + 0.1 * ((p * 7 + seed) % 3) / 2.0;
            var c = new THREE.Color(shade(color, cloth));
            int start = pos.Count / 3;
            for (int side = 0; side < 2; side++)
            {
                double fx = ((double)p + side) / panels * 2 - 1;
                for (int r = 0; r <= rowsN; r++)
                {
                    double fy = (double)r / rowsN;
                    double widthK = 1 - (1 - foot) * fy;
                    double x = fx * w / 2 * widthK;
                    // the belly is deepest in the middle and nearest the head; the foot curves up in the middle
                    double z = -belly * (1 - fx * fx) * (1 - 0.55 * fy * fy) * Math.Sin(Math.PI * Math.Min(1, 0.15 + fy * 0.9));
                    double y = -h * fy + roach * (1 - fx * fx) * fy * fy;
                    if (ragged > 0 && r == rowsN) y -= rng() * ragged;
                    pos.AddRange(new[] { x, y, z });
                    col.AddRange(new[] { c.r, c.g, c.b });
                }
            }
            // two columns of rowsN + 1 vertices: left edge then right edge of the panel
            int per = rowsN + 1;
            for (int r = 0; r < rowsN; r++)
            {
                int a = start + r, b = start + per + r;
                idx.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
            }
        }
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setAttribute("color", new THREE.Float32BufferAttribute(col, 3));
        g.setIndex(idx);
        g.computeVertexNormals();
        return g;
    }

    /// <summary>
    /// Any smooth surface: `f(a, b)` gives the point for a and b each running 0..1, drawn as an (nu x nv) grid with a colour at
    /// each vertex. Used for sails that are not square (a spanker, a jib), flags and the like. Draw it double-sided.
    /// </summary>
    public static THREE.BufferGeometry surface(Func<double, double, (double x, double y, double z)> f, int nu, int nv, Func<double, double, int> color)
    {
        var pos = new List<double>();
        var col = new List<double>();
        var idx = new List<int>();
        for (int i = 0; i <= nu; i++)
            for (int j = 0; j <= nv; j++)
            {
                double a = (double)i / nu, b = (double)j / nv;
                var p = f(a, b);
                pos.AddRange(new[] { p.x, p.y, p.z });
                var c = new THREE.Color(color(a, b));
                col.AddRange(new[] { c.r, c.g, c.b });
            }
        int per = nv + 1;
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
            {
                int a = i * per + j, b = a + per;
                idx.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setAttribute("color", new THREE.Float32BufferAttribute(col, 3));
        g.setIndex(idx);
        g.computeVertexNormals();
        return g;
    }

    /// <summary>A triangle (or any flat polygon fan) from points, with a colour. Faces both ways when drawn double-sided.</summary>
    public static THREE.BufferGeometry polygon(IList<double> xyz, int color)
    {
        int n = xyz.Count / 3;
        var idx = new List<int>();
        for (int i = 1; i < n - 1; i++) idx.AddRange(new[] { 0, i, i + 1 });
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(xyz, 3));
        g.setIndex(idx);
        g.computeVertexNormals();
        return Ecology.paint(g, color);
    }
}
