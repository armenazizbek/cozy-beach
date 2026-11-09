using System;
using System.Collections.Generic;

namespace THREE
{
    public class Box3
    {
        public Vector3 min = new Vector3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        public Vector3 max = new Vector3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);

        public Box3() { }
        public Box3(Vector3 min, Vector3 max) { this.min.copy(min); this.max.copy(max); }

        public Box3 set(Vector3 min, Vector3 max) { this.min.copy(min); this.max.copy(max); return this; }
        public Box3 clone() => new Box3().copy(this);
        public Box3 copy(Box3 b) { min.copy(b.min); max.copy(b.max); return this; }
        public Box3 makeEmpty()
        {
            min.x = min.y = min.z = double.PositiveInfinity;
            max.x = max.y = max.z = double.NegativeInfinity;
            return this;
        }
        public bool isEmpty() => max.x < min.x || max.y < min.y || max.z < min.z;
        public Vector3 getCenter(Vector3 target) => isEmpty() ? target.set(0, 0, 0) : target.addVectors(min, max).multiplyScalar(0.5);
        public Vector3 getSize(Vector3 target) => isEmpty() ? target.set(0, 0, 0) : target.subVectors(max, min);
        public Box3 expandByPoint(Vector3 p) { min.min(p); max.max(p); return this; }
        public Box3 expandByScalar(double s) { min.addScalar(-s); max.addScalar(s); return this; }
        public Box3 setFromPoints(IEnumerable<Vector3> pts) { makeEmpty(); foreach (var p in pts) expandByPoint(p); return this; }
        public Box3 setFromCenterAndSize(Vector3 c, Vector3 size)
        {
            var half = size.clone().multiplyScalar(0.5);
            min.copy(c).sub(half);
            max.copy(c).add(half);
            return this;
        }

        private static readonly Vector3 _v = new Vector3();

        public Box3 setFromObject(Object3D o, bool precise = false) { makeEmpty(); return expandByObject(o, precise); }

        public Box3 expandByObject(Object3D o, bool precise = false)
        {
            o.updateWorldMatrix(false, false);
            if (o is Mesh m && m.geometry != null)
            {
                var pos = m.geometry.getAttribute("position");
                if (precise && pos != null)
                {
                    for (int i = 0; i < pos.count; i++)
                    {
                        _v.set(pos.getX(i), pos.getY(i), pos.getZ(i)).applyMatrix4(o.matrixWorld);
                        expandByPoint(_v);
                    }
                }
                else
                {
                    if (m.geometry.boundingBox == null) m.geometry.computeBoundingBox();
                    var b = m.geometry.boundingBox.clone().applyMatrix4(o.matrixWorld);
                    union(b);
                }
            }
            foreach (var c in o.children) expandByObject(c, precise);
            return this;
        }

        public bool containsPoint(Vector3 p) =>
            !(p.x < min.x || p.x > max.x || p.y < min.y || p.y > max.y || p.z < min.z || p.z > max.z);

        public bool intersectsBox(Box3 b) =>
            !(b.max.x < min.x || b.min.x > max.x || b.max.y < min.y || b.min.y > max.y || b.max.z < min.z || b.min.z > max.z);

        public Vector3 clampPoint(Vector3 p, Vector3 target) => target.copy(p).clamp(min, max);
        public double distanceToPoint(Vector3 p) => clampPoint(p, _v).distanceTo(p);
        public Box3 union(Box3 b) { min.min(b.min); max.max(b.max); return this; }
        public Box3 intersect(Box3 b) { min.max(b.min); max.min(b.max); if (isEmpty()) makeEmpty(); return this; }
        public Box3 translate(Vector3 o) { min.add(o); max.add(o); return this; }

        public Box3 applyMatrix4(Matrix4 m)
        {
            if (isEmpty()) return this;
            var pts = new[]
            {
                new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z),
            };
            makeEmpty();
            foreach (var p in pts) expandByPoint(p.applyMatrix4(m));
            return this;
        }

        public Sphere getBoundingSphere(Sphere target)
        {
            if (isEmpty()) { target.center.set(0, 0, 0); target.radius = -1; return target; }
            getCenter(target.center);
            target.radius = getSize(_v).length() * 0.5;
            return target;
        }
    }

    public class Sphere
    {
        public Vector3 center = new Vector3();
        public double radius = -1;

        public Sphere() { }
        public Sphere(Vector3 c, double r) { center.copy(c); radius = r; }

        public Sphere set(Vector3 c, double r) { center.copy(c); radius = r; return this; }
        public Sphere clone() => new Sphere(center, radius);
        public Sphere copy(Sphere s) { center.copy(s.center); radius = s.radius; return this; }
        public bool isEmpty() => radius < 0;
        public bool containsPoint(Vector3 p) => p.distanceToSquared(center) <= radius * radius;
        public double distanceToPoint(Vector3 p) => p.distanceTo(center) - radius;
        public bool intersectsSphere(Sphere s) { var r = radius + s.radius; return s.center.distanceToSquared(center) <= r * r; }
        public Sphere applyMatrix4(Matrix4 m) { center.applyMatrix4(m); radius *= m.getMaxScaleOnAxis(); return this; }
        public Sphere translate(Vector3 o) { center.add(o); return this; }
    }

    public class Plane
    {
        public Vector3 normal = new Vector3(1, 0, 0);
        public double constant;

        public Plane() { }
        public Plane(Vector3 n, double c = 0) { normal.copy(n); constant = c; }

        public Plane set(Vector3 n, double c) { normal.copy(n); constant = c; return this; }
        public Plane setFromNormalAndCoplanarPoint(Vector3 n, Vector3 p) { normal.copy(n); constant = -p.dot(normal); return this; }
        public double distanceToPoint(Vector3 p) => normal.dot(p) + constant;
        public Vector3 projectPoint(Vector3 p, Vector3 target) => target.copy(p).addScaledVector(normal, -distanceToPoint(p));
    }

    public class Line3
    {
        public Vector3 start = new Vector3(), end = new Vector3();

        public Line3() { }
        public Line3(Vector3 s, Vector3 e) { start.copy(s); end.copy(e); }

        public Line3 set(Vector3 s, Vector3 e) { start.copy(s); end.copy(e); return this; }
        public Vector3 delta(Vector3 target) => target.subVectors(end, start);
        public double distance() => start.distanceTo(end);
        public double distanceSq() => start.distanceToSquared(end);
        public Vector3 at(double t, Vector3 target) => delta(target).multiplyScalar(t).add(start);

        private static readonly Vector3 _a = new Vector3(), _b = new Vector3();

        public double closestPointToPointParameter(Vector3 p, bool clampToLine)
        {
            _a.subVectors(p, start);
            _b.subVectors(end, start);
            double d = _b.dot(_b);
            double t = d == 0 ? 0 : _b.dot(_a) / d;
            if (clampToLine) t = MathUtils.clamp(t, 0, 1);
            return t;
        }

        public Vector3 closestPointToPoint(Vector3 p, bool clampToLine, Vector3 target) => at(closestPointToPointParameter(p, clampToLine), target);
    }

    public class Ray
    {
        public Vector3 origin = new Vector3();
        public Vector3 direction = new Vector3(0, 0, -1);

        public Ray() { }
        public Ray(Vector3 o, Vector3 d) { origin.copy(o); direction.copy(d); }

        public Ray set(Vector3 o, Vector3 d) { origin.copy(o); direction.copy(d); return this; }
        public Ray copy(Ray r) { origin.copy(r.origin); direction.copy(r.direction); return this; }
        public Ray clone() => new Ray(origin, direction);
        public Vector3 at(double t, Vector3 target) => target.copy(origin).addScaledVector(direction, t);
        public Ray applyMatrix4(Matrix4 m) { origin.applyMatrix4(m); direction.transformDirection(m); return this; }

        private static readonly Vector3 _v = new Vector3();

        public double distanceSqToPoint(Vector3 p)
        {
            double d = _v.subVectors(p, origin).dot(direction);
            if (d < 0) return origin.distanceToSquared(p);
            _v.copy(origin).addScaledVector(direction, d);
            return _v.distanceToSquared(p);
        }

        public double distanceToPoint(Vector3 p) => Math.Sqrt(distanceSqToPoint(p));

        public Vector3 closestPointToPoint(Vector3 p, Vector3 target)
        {
            target.subVectors(p, origin);
            double d = target.dot(direction);
            if (d < 0) return target.copy(origin);
            return target.copy(origin).addScaledVector(direction, d);
        }

        public bool intersectsSphere(Sphere s) => distanceSqToPoint(s.center) <= s.radius * s.radius;

        public Vector3 intersectSphere(Sphere s, Vector3 target)
        {
            _v.subVectors(s.center, origin);
            double tca = _v.dot(direction);
            double d2 = _v.dot(_v) - tca * tca;
            double r2 = s.radius * s.radius;
            if (d2 > r2) return null;
            double thc = Math.Sqrt(r2 - d2);
            double t0 = tca - thc, t1 = tca + thc;
            if (t1 < 0) return null;
            if (t0 < 0) return at(t1, target);
            return at(t0, target);
        }

        public double? distanceToPlane(Plane p)
        {
            double denom = p.normal.dot(direction);
            if (denom == 0) return p.distanceToPoint(origin) == 0 ? 0 : (double?)null;
            double t = -(origin.dot(p.normal) + p.constant) / denom;
            return t >= 0 ? t : (double?)null;
        }

        public Vector3 intersectPlane(Plane p, Vector3 target)
        {
            var t = distanceToPlane(p);
            return t == null ? null : at(t.Value, target);
        }

        public Vector3 intersectBox(Box3 box, Vector3 target)
        {
            double tmin, tmax, tymin, tymax, tzmin, tzmax;
            double invdirx = 1 / direction.x, invdiry = 1 / direction.y, invdirz = 1 / direction.z;
            if (invdirx >= 0) { tmin = (box.min.x - origin.x) * invdirx; tmax = (box.max.x - origin.x) * invdirx; }
            else { tmin = (box.max.x - origin.x) * invdirx; tmax = (box.min.x - origin.x) * invdirx; }
            if (invdiry >= 0) { tymin = (box.min.y - origin.y) * invdiry; tymax = (box.max.y - origin.y) * invdiry; }
            else { tymin = (box.max.y - origin.y) * invdiry; tymax = (box.min.y - origin.y) * invdiry; }
            if (tmin > tymax || tymin > tmax) return null;
            if (tymin > tmin || double.IsNaN(tmin)) tmin = tymin;
            if (tymax < tmax || double.IsNaN(tmax)) tmax = tymax;
            if (invdirz >= 0) { tzmin = (box.min.z - origin.z) * invdirz; tzmax = (box.max.z - origin.z) * invdirz; }
            else { tzmin = (box.max.z - origin.z) * invdirz; tzmax = (box.min.z - origin.z) * invdirz; }
            if (tmin > tzmax || tzmin > tmax) return null;
            if (tzmin > tmin || double.IsNaN(tmin)) tmin = tzmin;
            if (tzmax < tmax || double.IsNaN(tmax)) tmax = tzmax;
            if (tmax < 0) return null;
            return at(tmin >= 0 ? tmin : tmax, target);
        }

        public bool intersectsBox(Box3 b) => intersectBox(b, _v) != null;

        private static readonly Vector3 _diff = new Vector3(), _edge1 = new Vector3(), _edge2 = new Vector3(), _normal = new Vector3();

        /// <summary>Möller-Trumbore as Three.js writes it; backfaceCulling skips triangles seen from behind.</summary>
        public Vector3 intersectTriangle(Vector3 a, Vector3 b, Vector3 c, bool backfaceCulling, Vector3 target)
        {
            _edge1.subVectors(b, a);
            _edge2.subVectors(c, a);
            _normal.crossVectors(_edge1, _edge2);
            double DdN = direction.dot(_normal);
            int sign;
            if (DdN > 0)
            {
                if (backfaceCulling) return null;
                sign = 1;
            }
            else if (DdN < 0)
            {
                sign = -1;
                DdN = -DdN;
            }
            else return null;
            _diff.subVectors(origin, a);
            double DdQxE2 = sign * direction.dot(_edge2.crossVectors(_diff, _edge2));
            if (DdQxE2 < 0) return null;
            double DdE1xQ = sign * direction.dot(_edge1.cross(_diff));
            if (DdE1xQ < 0) return null;
            if (DdQxE2 + DdE1xQ > DdN) return null;
            double QdN = -sign * _diff.dot(_normal);
            if (QdN < 0) return null;
            return at(QdN / DdN, target);
        }

        public Ray lookAt(Vector3 v) { direction.copy(v).sub(origin).normalize(); return this; }
    }
}
