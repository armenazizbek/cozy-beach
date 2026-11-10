using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Shapes for the creatures out at sea: a shark's body lofted from separate width and height profiles, and fins that are thick at
/// the root and thin at the edges, with a rounded leading edge, a sharp trailing edge and a curved, swept outline.
/// </summary>
public static class SeaModels
{
    /// <summary>A smooth function through these (u, value) points, u rising from 0 to 1.</summary>
    public static Func<double, double> profile((double u, double v)[] pts)
    {
        var curve = new THREE.SplineCurve(pts.Select(p => new THREE.Vector2(p.u, p.v)).ToList());
        var table = curve.getPoints(400);
        return u =>
        {
            if (u <= table[0].x) return table[0].y;
            for (int i = 1; i < table.Count; i++)
            {
                if (table[i].x < u) continue;
                var a = table[i - 1];
                var b = table[i];
                return Math.Max(0, a.y + (b.y - a.y) * (u - a.x) / Math.Max(1e-9, b.x - a.x));
            }
            return Math.Max(0, table[table.Count - 1].y);
        };
    }

    /// <summary>
    /// A body lofted along z, nose at -len/2 and tail at +len/2. `halfW` and `halfH` give its half width and half height along it
    /// (u is 0 at the nose, 1 at the tail); the belly is a little flatter than the back. The tail end is closed with a cap.
    /// </summary>
    public static THREE.BufferGeometry body(double len, Func<double, double> halfW, Func<double, double> halfH, int along = 56, int around = 28)
    {
        var pos = new List<double>();
        var idx = new List<int>();
        for (int i = 0; i <= along; i++)
        {
            // rings close together at the nose and the tail, where the shape turns fastest
            double u = (1 - Math.Cos(Math.PI * i / along)) / 2;
            double z = -len / 2 + u * len;
            double w = halfW(u), h = halfH(u);
            for (int j = 0; j < around; j++)
            {
                double phi = (double)j / around * Math.PI * 2;
                double sy = Math.Sin(phi);
                pos.Add(Math.Cos(phi) * w);
                pos.Add(sy * h * (sy > 0 ? 1 : 0.84));
                pos.Add(z);
            }
        }
        for (int i = 0; i < along; i++)
        {
            for (int j = 0; j < around; j++)
            {
                int a = i * around + j, b = i * around + (j + 1) % around;
                idx.Add(a);
                idx.Add(b);
                idx.Add(a + around);
                idx.Add(b);
                idx.Add(b + around);
                idx.Add(a + around);
            }
        }
        // the cap at the tail end
        int centre = pos.Count / 3;
        pos.Add(0);
        pos.Add(0);
        pos.Add(len / 2);
        int last = along * around;
        for (int j = 0; j < around; j++)
        {
            idx.Add(last + j);
            idx.Add(last + (j + 1) % around);
            idx.Add(centre);
        }
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setIndex(idx);
        g.computeVertexNormals();
        return g;
    }

    /// <summary>
    /// A fin in its own space: the root runs along z at y = 0 (leading edge at `rootLE`, trailing edge at `rootTE`) and it reaches up
    /// to a tip at height `span` and z = `tipZ`. Its thickness is along x, `thick` at the root, thinning to nothing at the tip. The
    /// leading edge bows out (`lePow` above 1) and the trailing edge curves in (`tePow` below 1), as a shark's fins do. The root is
    /// sunk a little below y = 0, so a fin set on a body meets it without a gap.
    /// </summary>
    public static THREE.BufferGeometry fin(double span, double rootLE, double rootTE, double tipZ, double thick, double lePow = 1.4, double tePow = 0.55, int ns = 12, int nc = 10)
    {
        // the thickness of a rounded-nose foil along its chord, 0 at both ends and 1 at its thickest
        double foil(double x) => Math.Max(0, (0.2969 * Math.Sqrt(x) - 0.126 * x - 0.3516 * x * x + 0.2843 * x * x * x - 0.1036 * x * x * x * x) / 0.1);
        var pos = new List<double>();
        var idx = new List<int>();
        int ring = nc * 2;
        const double sink = 0.14;
        for (int i = 0; i <= ns; i++)
        {
            // the first station is below the root, inside the body
            double s = -sink + (1 + sink) * i / ns;
            double sc = Math.Max(0, s);
            double zl = rootLE + (tipZ - rootLE) * Math.Pow(sc, lePow);
            double zt = rootTE + (tipZ - rootTE) * Math.Pow(sc, tePow);
            double y = s * span;
            double t = thick * Math.Pow(1 - sc, 0.8) + 0.002;
            for (int k = 0; k < ring; k++)
            {
                // round the section: along the top from the leading to the trailing edge, then back along the bottom
                int kk = k <= nc ? k : ring - k;
                double x = (1 - Math.Cos(Math.PI * kk / nc)) / 2;
                double half = t * 0.5 * foil(x);
                pos.Add(k <= nc ? half : -half);
                pos.Add(y);
                pos.Add(zl + (zt - zl) * x);
            }
        }
        for (int i = 0; i < ns; i++)
        {
            for (int k = 0; k < ring; k++)
            {
                int a = i * ring + k, b = i * ring + (k + 1) % ring;
                idx.Add(a);
                idx.Add(a + ring);
                idx.Add(b);
                idx.Add(b);
                idx.Add(a + ring);
                idx.Add(b + ring);
            }
        }
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setIndex(idx);
        g.computeVertexNormals();
        return g;
    }

    /// <summary>Colours a shape by which way its surface faces: `top` where it faces up, `belly` where it faces down, blended in between.</summary>
    public static THREE.BufferGeometry countershade(THREE.BufferGeometry g, int top, int belly, double line = -0.1, double soft = 0.45)
    {
        g.computeVertexNormals();
        var nrm = g.attributes.normal;
        int n = nrm.count;
        var col = new float[n * 3];
        var cTop = new THREE.Color(top);
        var cBelly = new THREE.Color(belly);
        var c = new THREE.Color();
        for (int i = 0; i < n; i++)
        {
            double t = Math.Min(1, Math.Max(0, (nrm.getY(i) - line) / soft + 0.5));
            c.copy(cBelly).lerp(cTop, t * t * (3 - 2 * t));
            col[i * 3] = (float)c.r;
            col[i * 3 + 1] = (float)c.g;
            col[i * 3 + 2] = (float)c.b;
        }
        g.setAttribute("color", new THREE.BufferAttribute(col, 3));
        return g;
    }
}
