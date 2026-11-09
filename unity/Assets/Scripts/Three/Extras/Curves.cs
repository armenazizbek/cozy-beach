using System;
using System.Collections.Generic;

namespace THREE
{
    internal static class Interp
    {
        public const double EPS = 2.220446049250313e-16; // JavaScript's Number.EPSILON

        public static double CatmullRom(double t, double p0, double p1, double p2, double p3)
        {
            double v0 = (p2 - p0) * 0.5, v1 = (p3 - p1) * 0.5, t2 = t * t, t3 = t * t2;
            return (2 * p1 - 2 * p2 + v0 + v1) * t3 + (-3 * p1 + 3 * p2 - 2 * v0 - v1) * t2 + v0 * t + p1;
        }

        public static double QuadraticBezier(double t, double p0, double p1, double p2)
        {
            double k = 1 - t;
            return k * k * p0 + 2 * k * t * p1 + t * t * p2;
        }

        public static double CubicBezier(double t, double p0, double p1, double p2, double p3)
        {
            double k = 1 - t;
            return k * k * k * p0 + 3 * k * k * t * p1 + 3 * k * t * t * p2 + t * t * t * p3;
        }
    }

    /// <summary>A 3D curve (Three's Curve&lt;Vector3&gt;).</summary>
    public abstract class Curve3
    {
        public int arcLengthDivisions = 200;
        private List<double> cacheArcLengths;
        public bool needsUpdate;

        public abstract Vector3 getPoint(double t, Vector3 target = null);

        public Vector3 getPointAt(double u, Vector3 target = null) => getPoint(getUtoTmapping(u), target);

        public List<Vector3> getPoints(int divisions = 5)
        {
            var pts = new List<Vector3>();
            for (int d = 0; d <= divisions; d++) pts.Add(getPoint((double)d / divisions));
            return pts;
        }

        public List<Vector3> getSpacedPoints(int divisions = 5)
        {
            var pts = new List<Vector3>();
            for (int d = 0; d <= divisions; d++) pts.Add(getPointAt((double)d / divisions));
            return pts;
        }

        public double getLength()
        {
            var l = getLengths();
            return l[l.Count - 1];
        }

        public List<double> getLengths(int divisions = -1)
        {
            if (divisions < 0) divisions = arcLengthDivisions;
            if (cacheArcLengths != null && cacheArcLengths.Count == divisions + 1 && !needsUpdate) return cacheArcLengths;
            needsUpdate = false;
            var cache = new List<double> { 0 };
            var last = getPoint(0);
            double sum = 0;
            for (int p = 1; p <= divisions; p++)
            {
                var current = getPoint((double)p / divisions);
                sum += current.distanceTo(last);
                cache.Add(sum);
                last = current;
            }
            cacheArcLengths = cache;
            return cache;
        }

        public void updateArcLengths() { needsUpdate = true; getLengths(); }

        public virtual double getUtoTmapping(double u, double distance = 0)
        {
            var arcLengths = getLengths();
            int il = arcLengths.Count, i = 0;
            double target = distance != 0 ? distance : u * arcLengths[il - 1];
            int low = 0, high = il - 1;
            while (low <= high)
            {
                i = (int)Math.Floor(low + (high - low) / 2.0);
                double comparison = arcLengths[i] - target;
                if (comparison < 0) low = i + 1;
                else if (comparison > 0) high = i - 1;
                else { high = i; break; }
            }
            i = high;
            if (i < 0) i = 0;
            if (arcLengths[i] == target) return (double)i / (il - 1);
            if (i + 1 >= il) return 1;
            double before = arcLengths[i], after = arcLengths[i + 1];
            double frac = (target - before) / (after - before);
            return (i + frac) / (il - 1);
        }

        public virtual Vector3 getTangent(double t, Vector3 target = null)
        {
            const double delta = 0.0001;
            double t1 = t - delta, t2 = t + delta;
            if (t1 < 0) t1 = 0;
            if (t2 > 1) t2 = 1;
            var p1 = getPoint(t1);
            var p2 = getPoint(t2);
            return (target ?? new Vector3()).copy(p2).sub(p1).normalize();
        }

        public Vector3 getTangentAt(double u, Vector3 target = null) => getTangent(getUtoTmapping(u), target);

        public struct Frames
        {
            public Vector3[] tangents, normals, binormals;
        }

        public Frames computeFrenetFrames(int segments, bool closed)
        {
            var normal = new Vector3();
            var tangents = new Vector3[segments + 1];
            var normals = new Vector3[segments + 1];
            var binormals = new Vector3[segments + 1];
            var vec = new Vector3();
            var mat = new Matrix4();
            for (int i = 0; i <= segments; i++) tangents[i] = getTangentAt((double)i / segments, new Vector3());
            normals[0] = new Vector3();
            binormals[0] = new Vector3();
            double min = double.MaxValue;
            double tx = Math.Abs(tangents[0].x), ty = Math.Abs(tangents[0].y), tz = Math.Abs(tangents[0].z);
            if (tx <= min) { min = tx; normal.set(1, 0, 0); }
            if (ty <= min) { min = ty; normal.set(0, 1, 0); }
            if (tz <= min) normal.set(0, 0, 1);
            vec.crossVectors(tangents[0], normal).normalize();
            normals[0].crossVectors(tangents[0], vec);
            binormals[0].crossVectors(tangents[0], normals[0]);
            for (int i = 1; i <= segments; i++)
            {
                normals[i] = normals[i - 1].clone();
                binormals[i] = binormals[i - 1].clone();
                vec.crossVectors(tangents[i - 1], tangents[i]);
                if (vec.length() > Interp.EPS)
                {
                    vec.normalize();
                    double theta = Math.Acos(MathUtils.clamp(tangents[i - 1].dot(tangents[i]), -1, 1));
                    normals[i].applyMatrix4(mat.makeRotationAxis(vec, theta));
                }
                binormals[i].crossVectors(tangents[i], normals[i]);
            }
            if (closed)
            {
                double theta = Math.Acos(MathUtils.clamp(normals[0].dot(normals[segments]), -1, 1));
                theta /= segments;
                if (tangents[0].dot(vec.crossVectors(normals[0], normals[segments])) > 0) theta = -theta;
                for (int i = 1; i <= segments; i++)
                {
                    normals[i].applyMatrix4(mat.makeRotationAxis(tangents[i], theta * i));
                    binormals[i].crossVectors(tangents[i], normals[i]);
                }
            }
            return new Frames { tangents = tangents, normals = normals, binormals = binormals };
        }
    }

    public class CatmullRomCurve3 : Curve3
    {
        public List<Vector3> points;
        public bool closed;
        public string curveType;
        public double tension;
        private static readonly Vector3 tmp = new Vector3();

        public CatmullRomCurve3(IList<Vector3> points = null, bool closed = false, string curveType = "centripetal", double tension = 0.5)
        {
            this.points = points != null ? new List<Vector3>(points) : new List<Vector3>();
            this.closed = closed;
            this.curveType = curveType;
            this.tension = tension;
        }

        private struct Poly
        {
            public double c0, c1, c2, c3;
            public void Init(double x0, double x1, double t0, double t1)
            {
                c0 = x0; c1 = t0; c2 = -3 * x0 + 3 * x1 - 2 * t0 - t1; c3 = 2 * x0 - 2 * x1 + t0 + t1;
            }
            public void InitCatmullRom(double x0, double x1, double x2, double x3, double tension) => Init(x1, x2, tension * (x2 - x0), tension * (x3 - x1));
            public void InitNonuniform(double x0, double x1, double x2, double x3, double dt0, double dt1, double dt2)
            {
                double t1 = (x1 - x0) / dt0 - (x2 - x0) / (dt0 + dt1) + (x2 - x1) / dt1;
                double t2 = (x2 - x1) / dt1 - (x3 - x1) / (dt1 + dt2) + (x3 - x2) / dt2;
                t1 *= dt1;
                t2 *= dt1;
                Init(x1, x2, t1, t2);
            }
            public double Calc(double t) { double t2 = t * t; return c0 + c1 * t + c2 * t2 + c3 * t2 * t; }
        }

        public override Vector3 getPoint(double t, Vector3 target = null)
        {
            var point = target ?? new Vector3();
            int l = points.Count;
            double p = (l - (closed ? 0 : 1)) * t;
            int intPoint = (int)Math.Floor(p);
            double weight = p - intPoint;
            if (closed) intPoint += intPoint > 0 ? 0 : ((int)Math.Floor((double)Math.Abs(intPoint) / l) + 1) * l;
            else if (weight == 0 && intPoint == l - 1)
            {
                intPoint = l - 2;
                weight = 1;
            }
            Vector3 p0, p3;
            if (closed || intPoint > 0) p0 = points[(intPoint - 1) % l];
            else
            {
                tmp.subVectors(points[0], points[1]).add(points[0]);
                p0 = tmp;
            }
            var p1 = points[intPoint % l];
            var p2 = points[(intPoint + 1) % l];
            if (closed || intPoint + 2 < l) p3 = points[(intPoint + 2) % l];
            else
            {
                tmp.subVectors(points[l - 1], points[l - 2]).add(points[l - 1]);
                p3 = tmp;
            }
            Poly px = default, py = default, pz = default;
            if (curveType == "centripetal" || curveType == "chordal")
            {
                double pow = curveType == "chordal" ? 0.5 : 0.25;
                double dt0 = Math.Pow(p0.distanceToSquared(p1), pow);
                double dt1 = Math.Pow(p1.distanceToSquared(p2), pow);
                double dt2 = Math.Pow(p2.distanceToSquared(p3), pow);
                if (dt1 < 1e-4) dt1 = 1.0;
                if (dt0 < 1e-4) dt0 = dt1;
                if (dt2 < 1e-4) dt2 = dt1;
                px.InitNonuniform(p0.x, p1.x, p2.x, p3.x, dt0, dt1, dt2);
                py.InitNonuniform(p0.y, p1.y, p2.y, p3.y, dt0, dt1, dt2);
                pz.InitNonuniform(p0.z, p1.z, p2.z, p3.z, dt0, dt1, dt2);
            }
            else
            {
                px.InitCatmullRom(p0.x, p1.x, p2.x, p3.x, tension);
                py.InitCatmullRom(p0.y, p1.y, p2.y, p3.y, tension);
                pz.InitCatmullRom(p0.z, p1.z, p2.z, p3.z, tension);
            }
            return point.set(px.Calc(weight), py.Calc(weight), pz.Calc(weight));
        }
    }

    public class QuadraticBezierCurve3 : Curve3
    {
        public Vector3 v0, v1, v2;
        public QuadraticBezierCurve3(Vector3 v0 = null, Vector3 v1 = null, Vector3 v2 = null) { this.v0 = v0 ?? new Vector3(); this.v1 = v1 ?? new Vector3(); this.v2 = v2 ?? new Vector3(); }
        public override Vector3 getPoint(double t, Vector3 target = null) =>
            (target ?? new Vector3()).set(Interp.QuadraticBezier(t, v0.x, v1.x, v2.x), Interp.QuadraticBezier(t, v0.y, v1.y, v2.y), Interp.QuadraticBezier(t, v0.z, v1.z, v2.z));
    }

    public class CubicBezierCurve3 : Curve3
    {
        public Vector3 v0, v1, v2, v3;
        public CubicBezierCurve3(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3) { this.v0 = v0; this.v1 = v1; this.v2 = v2; this.v3 = v3; }
        public override Vector3 getPoint(double t, Vector3 target = null) =>
            (target ?? new Vector3()).set(Interp.CubicBezier(t, v0.x, v1.x, v2.x, v3.x), Interp.CubicBezier(t, v0.y, v1.y, v2.y, v3.y), Interp.CubicBezier(t, v0.z, v1.z, v2.z, v3.z));
    }

    public class LineCurve3 : Curve3
    {
        public Vector3 v1, v2;
        public LineCurve3(Vector3 v1, Vector3 v2) { this.v1 = v1; this.v2 = v2; }
        public override Vector3 getPoint(double t, Vector3 target = null)
        {
            var p = target ?? new Vector3();
            if (t == 1) return p.copy(v2);
            return p.copy(v2).sub(v1).multiplyScalar(t).add(v1);
        }
        public override double getUtoTmapping(double u, double distance = 0) => u;
        public override Vector3 getTangent(double t, Vector3 target = null) => (target ?? new Vector3()).subVectors(v2, v1).normalize();
    }

    /// <summary>A 2D curve (Three's Curve&lt;Vector2&gt;).</summary>
    public abstract class Curve2
    {
        public int arcLengthDivisions = 200;
        private List<double> cacheArcLengths;
        public bool needsUpdate;

        public abstract Vector2 getPoint(double t, Vector2 target = null);
        public Vector2 getPointAt(double u, Vector2 target = null) => getPoint(getUtoTmapping(u), target);

        public virtual List<Vector2> getPoints(int divisions = 5)
        {
            var pts = new List<Vector2>();
            for (int d = 0; d <= divisions; d++) pts.Add(getPoint((double)d / divisions));
            return pts;
        }

        public List<Vector2> getSpacedPoints(int divisions = 5)
        {
            var pts = new List<Vector2>();
            for (int d = 0; d <= divisions; d++) pts.Add(getPointAt((double)d / divisions));
            return pts;
        }

        public virtual double getLength()
        {
            var l = getLengths();
            return l[l.Count - 1];
        }

        public virtual List<double> getLengths(int divisions = -1)
        {
            if (divisions < 0) divisions = arcLengthDivisions;
            if (cacheArcLengths != null && cacheArcLengths.Count == divisions + 1 && !needsUpdate) return cacheArcLengths;
            needsUpdate = false;
            var cache = new List<double> { 0 };
            var last = getPoint(0);
            double sum = 0;
            for (int p = 1; p <= divisions; p++)
            {
                var current = getPoint((double)p / divisions);
                sum += current.distanceTo(last);
                cache.Add(sum);
                last = current;
            }
            cacheArcLengths = cache;
            return cache;
        }

        public virtual double getUtoTmapping(double u, double distance = 0)
        {
            var arcLengths = getLengths();
            int il = arcLengths.Count, i = 0;
            double target = distance != 0 ? distance : u * arcLengths[il - 1];
            int low = 0, high = il - 1;
            while (low <= high)
            {
                i = (int)Math.Floor(low + (high - low) / 2.0);
                double comparison = arcLengths[i] - target;
                if (comparison < 0) low = i + 1;
                else if (comparison > 0) high = i - 1;
                else { high = i; break; }
            }
            i = high;
            if (i < 0) i = 0;
            if (arcLengths[i] == target) return (double)i / (il - 1);
            if (i + 1 >= il) return 1;
            double before = arcLengths[i], after = arcLengths[i + 1];
            return (i + (target - before) / (after - before)) / (il - 1);
        }

        public virtual Vector2 getTangent(double t, Vector2 target = null)
        {
            const double delta = 0.0001;
            double t1 = Math.Max(0, t - delta), t2 = Math.Min(1, t + delta);
            var p1 = getPoint(t1);
            var p2 = getPoint(t2);
            return (target ?? new Vector2()).copy(p2).sub(p1).normalize();
        }

        public Vector2 getTangentAt(double u, Vector2 target = null) => getTangent(getUtoTmapping(u), target);
    }

    public class LineCurve : Curve2
    {
        public Vector2 v1, v2;
        public LineCurve(Vector2 v1, Vector2 v2) { this.v1 = v1; this.v2 = v2; }
        public override Vector2 getPoint(double t, Vector2 target = null)
        {
            var p = target ?? new Vector2();
            if (t == 1) return p.copy(v2);
            return p.copy(v2).sub(v1).multiplyScalar(t).add(v1);
        }
        public override double getUtoTmapping(double u, double distance = 0) => u;
        public override Vector2 getTangent(double t, Vector2 target = null) => (target ?? new Vector2()).subVectors(v2, v1).normalize();
    }

    public class QuadraticBezierCurve : Curve2
    {
        public Vector2 v0, v1, v2;
        public QuadraticBezierCurve(Vector2 v0, Vector2 v1, Vector2 v2) { this.v0 = v0; this.v1 = v1; this.v2 = v2; }
        public override Vector2 getPoint(double t, Vector2 target = null) =>
            (target ?? new Vector2()).set(Interp.QuadraticBezier(t, v0.x, v1.x, v2.x), Interp.QuadraticBezier(t, v0.y, v1.y, v2.y));
    }

    public class CubicBezierCurve : Curve2
    {
        public Vector2 v0, v1, v2, v3;
        public CubicBezierCurve(Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3) { this.v0 = v0; this.v1 = v1; this.v2 = v2; this.v3 = v3; }
        public override Vector2 getPoint(double t, Vector2 target = null) =>
            (target ?? new Vector2()).set(Interp.CubicBezier(t, v0.x, v1.x, v2.x, v3.x), Interp.CubicBezier(t, v0.y, v1.y, v2.y, v3.y));
    }

    public class EllipseCurve : Curve2
    {
        public double aX, aY, xRadius, yRadius, aStartAngle, aEndAngle, aRotation;
        public bool aClockwise;

        public EllipseCurve(double aX = 0, double aY = 0, double xRadius = 1, double yRadius = 1, double aStartAngle = 0, double aEndAngle = Math.PI * 2, bool aClockwise = false, double aRotation = 0)
        {
            this.aX = aX; this.aY = aY; this.xRadius = xRadius; this.yRadius = yRadius;
            this.aStartAngle = aStartAngle; this.aEndAngle = aEndAngle; this.aClockwise = aClockwise; this.aRotation = aRotation;
        }

        public override Vector2 getPoint(double t, Vector2 target = null)
        {
            var point = target ?? new Vector2();
            double twoPi = Math.PI * 2;
            double deltaAngle = aEndAngle - aStartAngle;
            bool samePoints = Math.Abs(deltaAngle) < Interp.EPS;
            while (deltaAngle < 0) deltaAngle += twoPi;
            while (deltaAngle > twoPi) deltaAngle -= twoPi;
            if (deltaAngle < Interp.EPS) deltaAngle = samePoints ? 0 : twoPi;
            if (aClockwise && !samePoints)
            {
                if (deltaAngle == twoPi) deltaAngle = -twoPi;
                else deltaAngle -= twoPi;
            }
            double angle = aStartAngle + t * deltaAngle;
            double x = aX + xRadius * Math.Cos(angle), y = aY + yRadius * Math.Sin(angle);
            if (aRotation != 0)
            {
                double cos = Math.Cos(aRotation), sin = Math.Sin(aRotation);
                double tx = x - aX, ty = y - aY;
                x = tx * cos - ty * sin + aX;
                y = tx * sin + ty * cos + aY;
            }
            return point.set(x, y);
        }
    }

    public class ArcCurve : EllipseCurve
    {
        public ArcCurve(double aX, double aY, double aRadius, double aStartAngle, double aEndAngle, bool aClockwise = false)
            : base(aX, aY, aRadius, aRadius, aStartAngle, aEndAngle, aClockwise) { }
    }

    public class SplineCurve : Curve2
    {
        public List<Vector2> points;
        public SplineCurve(IList<Vector2> points = null) { this.points = points != null ? new List<Vector2>(points) : new List<Vector2>(); }

        public override Vector2 getPoint(double t, Vector2 target = null)
        {
            var point = target ?? new Vector2();
            double p = (points.Count - 1) * t;
            int intPoint = (int)Math.Floor(p);
            double weight = p - intPoint;
            var p0 = points[intPoint == 0 ? intPoint : intPoint - 1];
            var p1 = points[intPoint];
            var p2 = points[intPoint > points.Count - 2 ? points.Count - 1 : intPoint + 1];
            var p3 = points[intPoint > points.Count - 3 ? points.Count - 1 : intPoint + 2];
            return point.set(Interp.CatmullRom(weight, p0.x, p1.x, p2.x, p3.x), Interp.CatmullRom(weight, p0.y, p1.y, p2.y, p3.y));
        }
    }

    public class CurvePath : Curve2
    {
        public List<Curve2> curves = new List<Curve2>();
        public bool autoClose;
        private List<double> cacheLengths;

        public void add(Curve2 c) => curves.Add(c);

        public void closePath()
        {
            if (curves.Count == 0) return;
            var start = curves[0].getPoint(0);
            var end = curves[curves.Count - 1].getPoint(1);
            if (!start.equals(end)) curves.Add(new LineCurve(end, start));
        }

        public override Vector2 getPoint(double t, Vector2 target = null)
        {
            double d = t * getLength();
            var curveLengths = getCurveLengths();
            int i = 0;
            while (i < curveLengths.Count)
            {
                if (curveLengths[i] >= d)
                {
                    double diff = curveLengths[i] - d;
                    var curve = curves[i];
                    double segmentLength = curve.getLength();
                    double u = segmentLength == 0 ? 0 : 1 - diff / segmentLength;
                    return curve.getPointAt(u, target);
                }
                i++;
            }
            return null;
        }

        public override double getLength()
        {
            var l = getCurveLengths();
            return l.Count == 0 ? 0 : l[l.Count - 1];
        }

        public List<double> getCurveLengths()
        {
            if (cacheLengths != null && cacheLengths.Count == curves.Count) return cacheLengths;
            var lengths = new List<double>();
            double sums = 0;
            foreach (var c in curves)
            {
                sums += c.getLength();
                lengths.Add(sums);
            }
            cacheLengths = lengths;
            return lengths;
        }

        public override List<Vector2> getPoints(int divisions = 12)
        {
            var points = new List<Vector2>();
            Vector2 last = null;
            foreach (var curve in curves)
            {
                int resolution = curve is EllipseCurve ? divisions * 2
                    : curve is LineCurve ? 1
                    : curve is SplineCurve sc ? divisions * sc.points.Count
                    : divisions;
                var pts = curve.getPoints(resolution);
                foreach (var point in pts)
                {
                    if (last != null && last.equals(point)) continue;
                    points.Add(point);
                    last = point;
                }
            }
            if (autoClose && points.Count > 1 && !points[points.Count - 1].equals(points[0])) points.Add(points[0]);
            return points;
        }
    }

    public class Path : CurvePath
    {
        public Vector2 currentPoint = new Vector2();

        public Path() { }
        public Path(IList<Vector2> points) { if (points != null) setFromPoints(points); }

        public Path setFromPoints(IList<Vector2> points)
        {
            moveTo(points[0].x, points[0].y);
            for (int i = 1; i < points.Count; i++) lineTo(points[i].x, points[i].y);
            return this;
        }

        public Path moveTo(double x, double y) { currentPoint.set(x, y); return this; }

        public Path lineTo(double x, double y)
        {
            curves.Add(new LineCurve(currentPoint.clone(), new Vector2(x, y)));
            currentPoint.set(x, y);
            return this;
        }

        public Path quadraticCurveTo(double cpx, double cpy, double x, double y)
        {
            curves.Add(new QuadraticBezierCurve(currentPoint.clone(), new Vector2(cpx, cpy), new Vector2(x, y)));
            currentPoint.set(x, y);
            return this;
        }

        public Path bezierCurveTo(double c1x, double c1y, double c2x, double c2y, double x, double y)
        {
            curves.Add(new CubicBezierCurve(currentPoint.clone(), new Vector2(c1x, c1y), new Vector2(c2x, c2y), new Vector2(x, y)));
            currentPoint.set(x, y);
            return this;
        }

        public Path splineThru(IList<Vector2> pts)
        {
            var all = new List<Vector2> { currentPoint.clone() };
            all.AddRange(pts);
            curves.Add(new SplineCurve(all));
            currentPoint.copy(pts[pts.Count - 1]);
            return this;
        }

        public Path arc(double x, double y, double r, double a0, double a1, bool clockwise = false)
        {
            absarc(x + currentPoint.x, y + currentPoint.y, r, a0, a1, clockwise);
            return this;
        }

        public Path absarc(double x, double y, double r, double a0, double a1, bool clockwise = false)
        {
            absellipse(x, y, r, r, a0, a1, clockwise);
            return this;
        }

        public Path ellipse(double x, double y, double rx, double ry, double a0, double a1, bool clockwise = false, double rotation = 0)
        {
            absellipse(x + currentPoint.x, y + currentPoint.y, rx, ry, a0, a1, clockwise, rotation);
            return this;
        }

        public Path absellipse(double x, double y, double rx, double ry, double a0, double a1, bool clockwise = false, double rotation = 0)
        {
            var curve = new EllipseCurve(x, y, rx, ry, a0, a1, clockwise, rotation);
            if (curves.Count > 0)
            {
                var first = curve.getPoint(0);
                if (!first.equals(currentPoint)) lineTo(first.x, first.y);
            }
            curves.Add(curve);
            currentPoint.copy(curve.getPoint(1));
            return this;
        }
    }

    public class Shape : Path
    {
        public List<Path> holes = new List<Path>();

        public Shape() { }
        public Shape(IList<Vector2> points) : base(points) { }

        public List<List<Vector2>> getPointsHoles(int divisions)
        {
            var res = new List<List<Vector2>>();
            foreach (var h in holes) res.Add(h.getPoints(divisions));
            return res;
        }

        public (List<Vector2> shape, List<List<Vector2>> holes) extractPoints(int divisions) => (getPoints(divisions), getPointsHoles(divisions));
    }
}
