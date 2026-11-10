using System;
using System.Collections.Generic;
using System.Linq;

// Port of src/mechanics/Hand.ts

public static class Hand
{
    /// <summary>How far the fingers sit from the centre of whatever they hold.</summary>
    public static double gripRadius(double r) => Math.Max(r, 0.012) + 0.011;

    /// <summary>Where the wrist sits in the hand's own space. With `slant` it moves back along the rod, in line with a handle held like a handshake.</summary>
    public static THREE.Vector3 wristAt(double r, double slant)
    {
        double R = gripRadius(r);
        return slant > 0 ? new THREE.Vector3(-slant, R * 0.35, -R * 0.1) : new THREE.Vector3(0, R * 0.3, -(R + 0.03));
    }

    /// <summary>A bone of the hand: a capsule that tapers from `ra` at a to `rb` at b.</summary>
    private struct Bone
    {
        public double ax, ay, az, bx, by, bz, ra, rb;
    }

    private static Bone bone(THREE.Vector3 a, THREE.Vector3 b, double ra, double rb = double.NaN) =>
        new Bone { ax = a.x, ay = a.y, az = a.z, bx = b.x, by = b.y, bz = b.z, ra = ra, rb = double.IsNaN(rb) ? ra : rb };

    /// <summary>Signed distance from a point to a bone: negative inside it.</summary>
    private static double toBone(in Bone s, double x, double y, double z)
    {
        double dx = s.bx - s.ax, dy = s.by - s.ay, dz = s.bz - s.az;
        double px = x - s.ax, py = y - s.ay, pz = z - s.az;
        double t = Math.Min(1, Math.Max(0, (px * dx + py * dy + pz * dz) / (dx * dx + dy * dy + dz * dz)));
        double qx = px - dx * t, qy = py - dy * t, qz = pz - dz * t;
        return Math.Sqrt(qx * qx + qy * qy + qz * qz) - (s.ra + (s.rb - s.ra) * t);
    }

    /// <summary>The union of two shapes, with the crease between them filled in over a distance of about `k`.</summary>
    public static double blendShapes(double a, double b, double k)
    {
        double h = Math.Max(k - Math.Abs(a - b), 0) / k;
        return Math.Min(a, b) - h * h * k / 4;
    }

    private static double merged(List<Bone> bones, double k, double x, double y, double z)
    {
        double d = double.PositiveInfinity;
        foreach (var s in bones) d = blendShapes(d, toBone(s, x, y, z), k);
        return d;
    }

    /// <summary>Turns a distance function into one closed mesh (surface nets), with normals taken from the function so it shades perfectly smooth.</summary>
    public static THREE.BufferGeometry solidSurface(Func<double, double, double, double> f, THREE.Vector3 lo, THREE.Vector3 hi, double step)
    {
        int nx = (int)Math.Ceiling((hi.x - lo.x) / step) + 1;
        int ny = (int)Math.Ceiling((hi.y - lo.y) / step) + 1;
        int nz = (int)Math.Ceiling((hi.z - lo.z) / step) + 1;
        int at(int i, int j, int k) => i + nx * (j + ny * k);
        var field = new float[nx * ny * nz];
        // blocks that a coarse sample shows are nowhere near the surface are filled in without asking the function about every point
        const int B = 4;
        double reach = Math.Sqrt(3) * B * step / 2 + 0.01;
        for (int bk = 0; bk < nz; bk += B)
        for (int bj = 0; bj < ny; bj += B)
        for (int bi = 0; bi < nx; bi += B)
        {
            double mid = f(lo.x + (bi + B / 2.0) * step, lo.y + (bj + B / 2.0) * step, lo.z + (bk + B / 2.0) * step);
            bool far = Math.Abs(mid) > reach;
            for (int k = bk; k < Math.Min(nz, bk + B); k++)
            for (int j = bj; j < Math.Min(ny, bj + B); j++)
            for (int i = bi; i < Math.Min(nx, bi + B); i++)
                field[at(i, j, k)] = (float)(far ? mid : f(lo.x + i * step, lo.y + j * step, lo.z + k * step));
        }
        // one vertex in every cell the surface passes through, at the average of where it crosses the cell's edges
        var cell = new int[field.Length];
        for (int i = 0; i < cell.Length; i++) cell[i] = -1;
        var pos = new List<double>();
        var corner = new float[8];
        var bits = new[] { 1, 2, 4 };
        for (int k = 0; k < nz - 1; k++)
        for (int j = 0; j < ny - 1; j++)
        for (int i = 0; i < nx - 1; i++)
        {
            int inside = 0;
            for (int c = 0; c < 8; c++)
            {
                corner[c] = field[at(i + (c & 1), j + ((c >> 1) & 1), k + ((c >> 2) & 1))];
                if (corner[c] < 0) inside++;
            }
            if (inside == 0 || inside == 8) continue;
            double sx = 0, sy = 0, sz = 0;
            int n = 0;
            for (int c = 0; c < 8; c++)
            {
                foreach (var bit in bits)
                {
                    if ((c & bit) != 0 || (corner[c] < 0) == (corner[c | bit] < 0)) continue;
                    double t = corner[c] / (corner[c] - corner[c | bit]);
                    sx += (c & 1) + (bit == 1 ? t : 0);
                    sy += ((c >> 1) & 1) + (bit == 2 ? t : 0);
                    sz += ((c >> 2) & 1) + (bit == 4 ? t : 0);
                    n++;
                }
            }
            cell[at(i, j, k)] = pos.Count / 3;
            pos.Add(lo.x + (i + sx / n) * step);
            pos.Add(lo.y + (j + sy / n) * step);
            pos.Add(lo.z + (k + sz / n) * step);
        }
        // one quad across every grid edge the surface crosses, joining the four cells around it
        var idx = new List<int>();
        void quad(bool ins, int a, int b, int c, int d)
        {
            if (ins) { idx.Add(a); idx.Add(b); idx.Add(c); idx.Add(a); idx.Add(c); idx.Add(d); }
            else { idx.Add(a); idx.Add(c); idx.Add(b); idx.Add(a); idx.Add(d); idx.Add(c); }
        }
        for (int k = 1; k < nz - 1; k++)
        for (int j = 1; j < ny - 1; j++)
        for (int i = 1; i < nx - 1; i++)
        {
            bool v = field[at(i, j, k)] < 0;
            if (v != field[at(i + 1, j, k)] < 0) quad(v, cell[at(i, j - 1, k - 1)], cell[at(i, j, k - 1)], cell[at(i, j, k)], cell[at(i, j - 1, k)]);
            if (v != field[at(i, j + 1, k)] < 0) quad(v, cell[at(i - 1, j, k - 1)], cell[at(i - 1, j, k)], cell[at(i, j, k)], cell[at(i, j, k - 1)]);
            if (v != field[at(i, j, k + 1)] < 0) quad(v, cell[at(i - 1, j - 1, k)], cell[at(i, j - 1, k)], cell[at(i, j, k)], cell[at(i - 1, j, k)]);
        }
        var nor = new float[pos.Count];
        const double e = 0.001;
        for (int i = 0; i < pos.Count; i += 3)
        {
            double x = pos[i], y = pos[i + 1], z = pos[i + 2];
            // four samples at the corners of a little tetrahedron give the same slope as six along the axes
            double a = f(x + e, y - e, z - e), b = f(x - e, y - e, z + e), c = f(x - e, y + e, z - e), d = f(x + e, y + e, z + e);
            double gx = a - b - c + d, gy = -a - b + c + d, gz = -a + b - c + d;
            double inv = 1 / JS.Or(Math.Sqrt(gx * gx + gy * gy + gz * gz), 1);
            nor[i] = (float)(gx * inv);
            nor[i + 1] = (float)(gy * inv);
            nor[i + 2] = (float)(gz * inv);
        }
        var geo = new THREE.BufferGeometry();
        geo.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        geo.setAttribute("normal", new THREE.BufferAttribute(nor, 3));
        geo.setIndex(idx);
        return geo;
    }

    private class Finger
    {
        public double x, rad, len;
    }

    /// <summary>Where the wrist sits in the space of <see cref="buildCameraGrip"/>: out to the side of the camera's end, below it and behind it.</summary>
    public static readonly THREE.Vector3 cameraWrist = new THREE.Vector3(0.05, -0.055, 0.052);

    /// <summary>
    /// A right hand holding the right-hand end of a camera, the way a camera is held: the palm against the end, the fingers flat across
    /// the front, the thumb flat across the back. In its own space the middle of the camera's end is at the origin, x runs out to the
    /// right, y is up and z comes back toward the photographer. `halfDepth` is half the camera's thickness and `top` how far its top is
    /// above the origin. With `shutter` the first finger lies along the top instead, its tip on the shutter button. `toElbow` is the unit
    /// direction the forearm leaves in. Mirror it in x for the left hand.
    /// </summary>
    public static THREE.Mesh buildCameraGrip(double halfDepth, double top, bool shutter, THREE.Vector3 toElbow, THREE.Material mat)
    {
        THREE.Vector3 v(double x, double y, double z) => new THREE.Vector3(x, y, z);
        var W = cameraWrist;
        // finger and thumb centres lie this far out, so their skin rests on the camera's faces
        double front = -(halfDepth + 0.0125), back = halfDepth + 0.0135;
        var palm = new List<Bone> { bone(W, W.clone().addScaledVector(toElbow, 0.09), 0.028, 0.03) };
        var digits = new List<List<Bone>>();

        // the fingers that wrap the front: from the knuckle at the camera's end, round its corner and flat along the front face
        double[] heights = shutter ? new[] { 0.02, -0.002, -0.024 } : new[] { 0.03, 0.009, -0.012, -0.032 };
        double[] rads = shutter ? new[] { 0.012, 0.0115, 0.0105 } : new[] { 0.0115, 0.012, 0.0115, 0.0105 };
        double[] reach = shutter ? new[] { 0.026, 0.022, 0.012 } : new[] { 0.022, 0.026, 0.022, 0.012 };
        for (int i = 0; i < heights.Length; i++)
        {
            double y = heights[i], r = rads[i];
            var k = v(0.039, y, -0.004);
            var p1 = v(0.03, y, front + 0.006);
            var p2 = v(0.006, y, front);
            var tip = v(-reach[i], y - 0.002, front + 0.001);
            palm.Add(bone(v(W.x - 0.004, W.y + 0.012 + i * 0.004, W.z - 0.006), k, 0.0155, 0.014));
            digits.Add(new List<Bone> { bone(k, p1, r, r * 0.96), bone(p1, p2, r * 0.96, r * 0.9), bone(p2, tip, r * 0.9, r * 0.82) });
        }
        if (shutter)
        {
            // the first finger goes up over the corner and lies along the top, its tip on the button
            const double r = 0.0115;
            var k = v(0.039, 0.036, 0.002);
            var p1 = v(0.03, top + 0.008, -0.004);
            var p2 = v(0.008, top + 0.0125, -0.006);
            var tip = v(-0.02, top + 0.0115, -0.002);
            palm.Add(bone(v(W.x - 0.004, W.y + 0.02, W.z - 0.004), k, 0.0155, 0.014));
            digits.Add(new List<Bone> { bone(k, p1, r, r * 0.96), bone(p1, p2, r * 0.96, r * 0.9), bone(p2, tip, r * 0.9, r * 0.82) });
        }

        // the thumb comes off the heel of the hand and lies flat across the back, pointing up and in
        var t0 = v(0.034, -0.014, back - 0.006);
        var t1 = v(0.012, 0.004, back);
        var t2 = v(-0.014, 0.016, back - 0.001);
        palm.Add(bone(v(W.x - 0.006, W.y + 0.006, W.z), t0, 0.021, 0.0175));
        // the pad on the little finger's side of the palm
        palm.Add(bone(W, v(0.043, -0.04, 0.008), 0.018, 0.016));
        var thumb = new List<Bone> { bone(t0, t1, 0.0165, 0.014), bone(t1, t2, 0.014, 0.0115) };

        double dist(double x, double y, double z)
        {
            double hand = merged(digits[0], 0.003, x, y, z);
            for (int i = 1; i < digits.Count; i++) hand = blendShapes(hand, merged(digits[i], 0.003, x, y, z), 0.006);
            hand = blendShapes(hand, merged(palm, 0.018, x, y, z), 0.012);
            return blendShapes(hand, merged(thumb, 0.004, x, y, z), 0.012);
        }

        var box = new THREE.Box3();
        foreach (var s in palm.Concat(thumb).Concat(digits.SelectMany(d => d)))
        {
            box.expandByPoint(v(s.ax, s.ay, s.az));
            box.expandByPoint(v(s.bx, s.by, s.bz));
        }
        box.expandByScalar(0.04);
        return new THREE.Mesh(solidSurface(dist, box.min, box.max, 0.004), mat);
    }

    /// <summary>
    /// A right hand closed around a rod of radius `r`, as one solid surface with the start of the forearm grown out of it. In its own space
    /// the rod runs along x with the thumb toward +x, the back of the hand faces +y and the fingers reach toward +z from the wrist, then
    /// curl around the rod and back. `toElbow` is the unit direction the forearm leaves in. With `slant` the hand instead takes a handle that
    /// runs on from the arm, with the wrist that far back along it and the thumb lying along the handle toward +x.
    /// </summary>
    public static THREE.Mesh buildFist(double r, THREE.Vector3 toElbow, THREE.Material mat, double slant = 0)
    {
        double R = gripRadius(r);
        THREE.Vector3 v(double x, double y, double z) => new THREE.Vector3(x, y, z);
        THREE.Vector3 around(double x, double rad, double th) => v(x, Math.Cos(th) * rad, Math.Sin(th) * rad);
        var wrist = wristAt(r, slant);

        var fingers = new[]
        {
            new Finger { x = 0.03, rad = 0.0115, len = 0.085 },
            new Finger { x = 0.01, rad = 0.012, len = 0.092 },
            new Finger { x = -0.01, rad = 0.0115, len = 0.085 },
            new Finger { x = -0.03, rad = 0.0105, len = 0.07 },
        };
        // the arm always runs on out of the wrist
        var palm = new List<Bone> { bone(wrist, wrist.clone().addScaledVector(toElbow, 0.09), 0.028, 0.03) };
        List<List<Bone>> digits;
        List<Bone> thumb;
        List<Bone> curled(Finger f, THREE.Vector3 knuckle, double start, double end, double drift)
        {
            double th(double u) => start + (end - start) * u;
            var pts = new[] { knuckle, around(f.x - drift * 0.4, R, th(0.4)), around(f.x - drift * 0.72, R, th(0.72)), around(f.x - drift, R, th(1)) };
            return new List<Bone> { bone(pts[0], pts[1], f.rad, f.rad * 0.96), bone(pts[1], pts[2], f.rad * 0.96, f.rad * 0.9), bone(pts[2], pts[3], f.rad * 0.9, f.rad * 0.82) };
        }

        if (slant > 0)
        {
            // a handle in line with the arm: a flat back of the hand running forward along it, the knuckles to the outside, the fingers
            // curling under, the web of the thumb over the top and the thumb lying along the handle
            var knuckles = fingers.Select(f => v(f.x * 0.95 + 0.008, R * 0.8 + 0.008, R * 0.62)).ToArray();
            for (int i = 0; i < fingers.Length; i++) palm.Add(bone(v(wrist.x + fingers[i].x * 0.2, wrist.y + 0.004, wrist.z - fingers[i].x * 0.4), knuckles[i], 0.0135, 0.0125));
            var t0 = v(0.005, R * 0.25, -(R + 0.004));
            var t1 = v(0.04, R * 0.3, -(R + 0.006));
            var t2 = v(0.072, R * 0.1, -(R + 0.002));
            palm.Add(bone(v(wrist.x, wrist.y, wrist.z - 0.01), t0, 0.02, 0.017));
            palm.Add(bone(wrist, v(-0.045, -R * 0.7, R * 0.35), 0.017, 0.014));
            digits = fingers.Select((f, i) =>
            {
                double start = Math.Atan2(knuckles[i].z, knuckles[i].y);
                return curled(f, knuckles[i], start, start + Math.Min(3, f.len / R), 0.008);
            }).ToList();
            thumb = new List<Bone> { bone(t0, t1, 0.0165, 0.014), bone(t1, t2, 0.014, 0.0115) };
        }
        else
        {
            // a rod across the hand: the bones of the back of the hand and the two pads of the palm
            foreach (var f in fingers) palm.Add(bone(v(wrist.x + f.x * 0.6, wrist.y, wrist.z), v(f.x, R + 0.012, 0.02), 0.0155, 0.014));
            palm.Add(bone(v(wrist.x + 0.014, wrist.y, wrist.z), v(0.034, -R * 0.35, -(R + 0.012)), 0.021, 0.018));
            palm.Add(bone(v(wrist.x - 0.014, wrist.y, wrist.z), v(-0.03, -R * 0.35, -(R + 0.012)), 0.018, 0.016));
            digits = fingers.Select(f => curled(f, v(f.x, R + 0.012, 0.02), 0.35, 0.35 + Math.Min(3.3, f.len / R), 0)).ToList();
            // the thumb grows from the heel of the hand and closes the other way, over the fingers
            double tR = R + 0.01;
            double tEnd = Math.Max(2.7, 4.9 - 0.075 / tR);
            double tTh(double u) => 4.9 + (tEnd - 4.9) * u;
            var t0 = around(0.042, tR, tTh(0));
            var t1 = around(0.04, tR, tTh(0.5));
            var t2 = around(0.032, tR, tTh(1));
            thumb = new List<Bone> { bone(v(0.03, -R * 0.2, -(R + 0.012)), t0, 0.018, 0.0155), bone(t0, t1, 0.0155, 0.0135), bone(t1, t2, 0.0135, 0.0115) };
        }

        double dist(double x, double y, double z)
        {
            double hand = merged(digits[0], 0.003, x, y, z);
            for (int i = 1; i < digits.Count; i++) hand = blendShapes(hand, merged(digits[i], 0.003, x, y, z), 0.006);
            hand = blendShapes(hand, merged(palm, 0.018, x, y, z), 0.012);
            return blendShapes(hand, merged(thumb, 0.004, x, y, z), 0.012);
        }

        var box = new THREE.Box3();
        foreach (var s in palm.Concat(thumb).Concat(digits.SelectMany(d => d)))
        {
            box.expandByPoint(v(s.ax, s.ay, s.az));
            box.expandByPoint(v(s.bx, s.by, s.bz));
        }
        box.expandByScalar(0.04);
        return new THREE.Mesh(solidSurface(dist, box.min, box.max, 0.004), mat);
    }
}
