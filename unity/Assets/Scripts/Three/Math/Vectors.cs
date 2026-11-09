using System;

namespace THREE
{
    public static class MathUtils
    {
        public const double DEG2RAD = Math.PI / 180.0;
        public const double RAD2DEG = 180.0 / Math.PI;

        public static double clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));
        public static double lerp(double x, double y, double t) => (1 - t) * x + t * y;
        public static double inverseLerp(double x, double y, double v) => x != y ? (v - x) / (y - x) : 0;
        public static double damp(double x, double y, double lambda, double dt) => lerp(x, y, 1 - Math.Exp(-lambda * dt));
        public static double mapLinear(double x, double a1, double a2, double b1, double b2) => b1 + (x - a1) * (b2 - b1) / (a2 - a1);
        public static double euclideanModulo(double n, double m) => ((n % m) + m) % m;
        public static double smoothstep(double x, double min, double max)
        {
            if (x <= min) return 0;
            if (x >= max) return 1;
            x = (x - min) / (max - min);
            return x * x * (3 - 2 * x);
        }
        public static double smootherstep(double x, double min, double max)
        {
            if (x <= min) return 0;
            if (x >= max) return 1;
            x = (x - min) / (max - min);
            return x * x * x * (x * (x * 6 - 15) + 10);
        }
        public static double randFloat(double low, double high) => low + JS.Random() * (high - low);
        public static double randFloatSpread(double range) => range * (0.5 - JS.Random());
        public static int randInt(int low, int high) => low + (int)Math.Floor(JS.Random() * (high - low + 1));
        public static double degToRad(double d) => d * DEG2RAD;
        public static double radToDeg(double r) => r * RAD2DEG;
        public static bool isPowerOfTwo(int v) => (v & (v - 1)) == 0 && v != 0;
        public static string generateUUID() => Guid.NewGuid().ToString();
    }

    public class Vector2
    {
        public double x, y;

        public Vector2(double x = 0, double y = 0) { this.x = x; this.y = y; }

        public double width { get => x; set => x = value; }
        public double height { get => y; set => y = value; }

        public Vector2 set(double x, double y) { this.x = x; this.y = y; return this; }
        public Vector2 setScalar(double s) { x = s; y = s; return this; }
        public Vector2 clone() => new Vector2(x, y);
        public Vector2 copy(Vector2 v) { x = v.x; y = v.y; return this; }
        public Vector2 add(Vector2 v) { x += v.x; y += v.y; return this; }
        public Vector2 addScalar(double s) { x += s; y += s; return this; }
        public Vector2 addVectors(Vector2 a, Vector2 b) { x = a.x + b.x; y = a.y + b.y; return this; }
        public Vector2 addScaledVector(Vector2 v, double s) { x += v.x * s; y += v.y * s; return this; }
        public Vector2 sub(Vector2 v) { x -= v.x; y -= v.y; return this; }
        public Vector2 subVectors(Vector2 a, Vector2 b) { x = a.x - b.x; y = a.y - b.y; return this; }
        public Vector2 multiply(Vector2 v) { x *= v.x; y *= v.y; return this; }
        public Vector2 multiplyScalar(double s) { x *= s; y *= s; return this; }
        public Vector2 divideScalar(double s) => multiplyScalar(1 / s);
        public Vector2 negate() { x = -x; y = -y; return this; }
        public double dot(Vector2 v) => x * v.x + y * v.y;
        public double cross(Vector2 v) => x * v.y - y * v.x;
        public double lengthSq() => x * x + y * y;
        public double length() => Math.Sqrt(x * x + y * y);
        public Vector2 normalize() { var l = length(); return divideScalar(l == 0 ? 1 : l); }
        public double angle() => Math.Atan2(-y, -x) + Math.PI;
        public double distanceTo(Vector2 v) => Math.Sqrt(distanceToSquared(v));
        public double distanceToSquared(Vector2 v) { var dx = x - v.x; var dy = y - v.y; return dx * dx + dy * dy; }
        public Vector2 setLength(double l) => normalize().multiplyScalar(l);
        public Vector2 lerp(Vector2 v, double a) { x += (v.x - x) * a; y += (v.y - y) * a; return this; }
        public Vector2 lerpVectors(Vector2 a, Vector2 b, double t) { x = a.x + (b.x - a.x) * t; y = a.y + (b.y - a.y) * t; return this; }
        public Vector2 rotateAround(Vector2 c, double angle)
        {
            double cs = Math.Cos(angle), sn = Math.Sin(angle);
            double dx = x - c.x, dy = y - c.y;
            x = dx * cs - dy * sn + c.x;
            y = dx * sn + dy * cs + c.y;
            return this;
        }
        public bool equals(Vector2 v) => v.x == x && v.y == y;
        public override string ToString() => $"({x}, {y})";
    }

    public class Vector3
    {
        public double x, y, z;

        public Vector3(double x = 0, double y = 0, double z = 0) { this.x = x; this.y = y; this.z = z; }

        public Vector3 set(double x, double y, double z) { this.x = x; this.y = y; this.z = z; return this; }
        public Vector3 setScalar(double s) { x = s; y = s; z = s; return this; }
        public Vector3 setX(double v) { x = v; return this; }
        public Vector3 setY(double v) { y = v; return this; }
        public Vector3 setZ(double v) { z = v; return this; }
        public Vector3 setComponent(int i, double v) { if (i == 0) x = v; else if (i == 1) y = v; else z = v; return this; }
        public double getComponent(int i) => i == 0 ? x : i == 1 ? y : z;
        public Vector3 clone() => new Vector3(x, y, z);
        public Vector3 copy(Vector3 v) { x = v.x; y = v.y; z = v.z; return this; }
        public Vector3 add(Vector3 v) { x += v.x; y += v.y; z += v.z; return this; }
        public Vector3 addScalar(double s) { x += s; y += s; z += s; return this; }
        public Vector3 addVectors(Vector3 a, Vector3 b) { x = a.x + b.x; y = a.y + b.y; z = a.z + b.z; return this; }
        public Vector3 addScaledVector(Vector3 v, double s) { x += v.x * s; y += v.y * s; z += v.z * s; return this; }
        public Vector3 sub(Vector3 v) { x -= v.x; y -= v.y; z -= v.z; return this; }
        public Vector3 subScalar(double s) { x -= s; y -= s; z -= s; return this; }
        public Vector3 subVectors(Vector3 a, Vector3 b) { x = a.x - b.x; y = a.y - b.y; z = a.z - b.z; return this; }
        public Vector3 multiply(Vector3 v) { x *= v.x; y *= v.y; z *= v.z; return this; }
        public Vector3 multiplyScalar(double s) { x *= s; y *= s; z *= s; return this; }
        public Vector3 multiplyVectors(Vector3 a, Vector3 b) { x = a.x * b.x; y = a.y * b.y; z = a.z * b.z; return this; }
        public Vector3 divide(Vector3 v) { x /= v.x; y /= v.y; z /= v.z; return this; }
        public Vector3 divideScalar(double s) => multiplyScalar(1 / s);

        public Vector3 applyEuler(Euler e) => applyQuaternion(_q.setFromEuler(e));
        public Vector3 applyAxisAngle(Vector3 axis, double angle) => applyQuaternion(_q.setFromAxisAngle(axis, angle));
        private static readonly Quaternion _q = new Quaternion();

        public Vector3 applyMatrix3(Matrix3 m)
        {
            double ox = x, oy = y, oz = z;
            var e = m.elements;
            x = e[0] * ox + e[3] * oy + e[6] * oz;
            y = e[1] * ox + e[4] * oy + e[7] * oz;
            z = e[2] * ox + e[5] * oy + e[8] * oz;
            return this;
        }

        public Vector3 applyNormalMatrix(Matrix3 m) => applyMatrix3(m).normalize();

        public Vector3 applyMatrix4(Matrix4 m)
        {
            double ox = x, oy = y, oz = z;
            var e = m.elements;
            double w = 1 / (e[3] * ox + e[7] * oy + e[11] * oz + e[15]);
            x = (e[0] * ox + e[4] * oy + e[8] * oz + e[12]) * w;
            y = (e[1] * ox + e[5] * oy + e[9] * oz + e[13]) * w;
            z = (e[2] * ox + e[6] * oy + e[10] * oz + e[14]) * w;
            return this;
        }

        public Vector3 applyQuaternion(Quaternion q)
        {
            double vx = x, vy = y, vz = z;
            double qx = q.x, qy = q.y, qz = q.z, qw = q.w;
            double tx = 2 * (qy * vz - qz * vy);
            double ty = 2 * (qz * vx - qx * vz);
            double tz = 2 * (qx * vy - qy * vx);
            x = vx + qw * tx + qy * tz - qz * ty;
            y = vy + qw * ty + qz * tx - qx * tz;
            z = vz + qw * tz + qx * ty - qy * tx;
            return this;
        }

        public Vector3 project(Camera camera) => applyMatrix4(camera.matrixWorldInverse).applyMatrix4(camera.projectionMatrix);
        public Vector3 unproject(Camera camera) => applyMatrix4(camera.projectionMatrixInverse).applyMatrix4(camera.matrixWorld);

        public Vector3 transformDirection(Matrix4 m)
        {
            double ox = x, oy = y, oz = z;
            var e = m.elements;
            x = e[0] * ox + e[4] * oy + e[8] * oz;
            y = e[1] * ox + e[5] * oy + e[9] * oz;
            z = e[2] * ox + e[6] * oy + e[10] * oz;
            return normalize();
        }

        public Vector3 min(Vector3 v) { x = Math.Min(x, v.x); y = Math.Min(y, v.y); z = Math.Min(z, v.z); return this; }
        public Vector3 max(Vector3 v) { x = Math.Max(x, v.x); y = Math.Max(y, v.y); z = Math.Max(z, v.z); return this; }
        public Vector3 clamp(Vector3 lo, Vector3 hi)
        {
            x = Math.Max(lo.x, Math.Min(hi.x, x));
            y = Math.Max(lo.y, Math.Min(hi.y, y));
            z = Math.Max(lo.z, Math.Min(hi.z, z));
            return this;
        }
        public Vector3 clampScalar(double lo, double hi)
        {
            x = Math.Max(lo, Math.Min(hi, x));
            y = Math.Max(lo, Math.Min(hi, y));
            z = Math.Max(lo, Math.Min(hi, z));
            return this;
        }
        public Vector3 clampLength(double lo, double hi)
        {
            var l = length();
            return divideScalar(l == 0 ? 1 : l).multiplyScalar(Math.Max(lo, Math.Min(hi, l)));
        }
        public Vector3 floor() { x = Math.Floor(x); y = Math.Floor(y); z = Math.Floor(z); return this; }
        public Vector3 ceil() { x = Math.Ceiling(x); y = Math.Ceiling(y); z = Math.Ceiling(z); return this; }
        public Vector3 round() { x = JS.Round(x); y = JS.Round(y); z = JS.Round(z); return this; }
        public Vector3 negate() { x = -x; y = -y; z = -z; return this; }
        public double dot(Vector3 v) => x * v.x + y * v.y + z * v.z;
        public double lengthSq() => x * x + y * y + z * z;
        public double length() => Math.Sqrt(x * x + y * y + z * z);
        public double manhattanLength() => Math.Abs(x) + Math.Abs(y) + Math.Abs(z);
        public Vector3 normalize() { var l = length(); return divideScalar(l == 0 ? 1 : l); }
        public Vector3 setLength(double l) => normalize().multiplyScalar(l);
        public Vector3 lerp(Vector3 v, double a) { x += (v.x - x) * a; y += (v.y - y) * a; z += (v.z - z) * a; return this; }
        public Vector3 lerpVectors(Vector3 a, Vector3 b, double t)
        {
            x = a.x + (b.x - a.x) * t;
            y = a.y + (b.y - a.y) * t;
            z = a.z + (b.z - a.z) * t;
            return this;
        }
        public Vector3 cross(Vector3 v) => crossVectors(this, v);
        public Vector3 crossVectors(Vector3 a, Vector3 b)
        {
            double ax = a.x, ay = a.y, az = a.z, bx = b.x, by = b.y, bz = b.z;
            x = ay * bz - az * by;
            y = az * bx - ax * bz;
            z = ax * by - ay * bx;
            return this;
        }
        public Vector3 projectOnVector(Vector3 v)
        {
            var d = v.lengthSq();
            if (d == 0) return set(0, 0, 0);
            var s = v.dot(this) / d;
            return copy(v).multiplyScalar(s);
        }
        public Vector3 projectOnPlane(Vector3 n) { _v.copy(this).projectOnVector(n); return sub(_v); }
        private static readonly Vector3 _v = new Vector3();
        public Vector3 reflect(Vector3 n) => sub(_v.copy(n).multiplyScalar(2 * dot(n)));
        public double angleTo(Vector3 v)
        {
            var d = Math.Sqrt(lengthSq() * v.lengthSq());
            if (d == 0) return Math.PI / 2;
            var t = dot(v) / d;
            return Math.Acos(Math.Max(-1, Math.Min(1, t)));
        }
        public double distanceTo(Vector3 v) => Math.Sqrt(distanceToSquared(v));
        public double distanceToSquared(Vector3 v) { double dx = x - v.x, dy = y - v.y, dz = z - v.z; return dx * dx + dy * dy + dz * dz; }
        public double manhattanDistanceTo(Vector3 v) => Math.Abs(x - v.x) + Math.Abs(y - v.y) + Math.Abs(z - v.z);
        public Vector3 setFromSphericalCoords(double radius, double phi, double theta)
        {
            var s = Math.Sin(phi) * radius;
            x = s * Math.Sin(theta);
            y = Math.Cos(phi) * radius;
            z = s * Math.Cos(theta);
            return this;
        }
        public Vector3 setFromCylindricalCoords(double radius, double theta, double y)
        {
            x = radius * Math.Sin(theta);
            this.y = y;
            z = radius * Math.Cos(theta);
            return this;
        }
        public Vector3 setFromMatrixPosition(Matrix4 m) { var e = m.elements; x = e[12]; y = e[13]; z = e[14]; return this; }
        public Vector3 setFromMatrixScale(Matrix4 m)
        {
            var sx = setFromMatrixColumn(m, 0).length();
            var sy = setFromMatrixColumn(m, 1).length();
            var sz = setFromMatrixColumn(m, 2).length();
            x = sx; y = sy; z = sz;
            return this;
        }
        public Vector3 setFromMatrixColumn(Matrix4 m, int i) { var e = m.elements; x = e[i * 4]; y = e[i * 4 + 1]; z = e[i * 4 + 2]; return this; }
        public Vector3 fromBufferAttribute(BufferAttribute a, int i) { x = a.getX(i); y = a.getY(i); z = a.getZ(i); return this; }
        public Vector3 fromArray(double[] a, int o = 0) { x = a[o]; y = a[o + 1]; z = a[o + 2]; return this; }
        public Vector3 fromArray(float[] a, int o = 0) { x = a[o]; y = a[o + 1]; z = a[o + 2]; return this; }
        public double[] toArray() => new[] { x, y, z };
        public bool equals(Vector3 v) => v.x == x && v.y == y && v.z == z;
        public Vector3 randomDirection()
        {
            var theta = JS.Random() * Math.PI * 2;
            var u = JS.Random() * 2 - 1;
            var c = Math.Sqrt(1 - u * u);
            x = c * Math.Cos(theta);
            y = u;
            z = c * Math.Sin(theta);
            return this;
        }
        public Vector3 random() { x = JS.Random(); y = JS.Random(); z = JS.Random(); return this; }
        public UnityEngine.Vector3 ToUnity() => new UnityEngine.Vector3((float)x, (float)y, (float)-z);
        public override string ToString() => $"({x}, {y}, {z})";
    }

    public class Vector4
    {
        public double x, y, z, w;
        public Vector4(double x = 0, double y = 0, double z = 0, double w = 1) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public Vector4 set(double x, double y, double z, double w) { this.x = x; this.y = y; this.z = z; this.w = w; return this; }
        public Vector4 copy(Vector4 v) { x = v.x; y = v.y; z = v.z; w = v.w; return this; }
        public Vector4 clone() => new Vector4(x, y, z, w);
        public Vector4 multiplyScalar(double s) { x *= s; y *= s; z *= s; w *= s; return this; }
        public Vector4 applyMatrix4(Matrix4 m)
        {
            double ox = x, oy = y, oz = z, ow = w;
            var e = m.elements;
            x = e[0] * ox + e[4] * oy + e[8] * oz + e[12] * ow;
            y = e[1] * ox + e[5] * oy + e[9] * oz + e[13] * ow;
            z = e[2] * ox + e[6] * oy + e[10] * oz + e[14] * ow;
            w = e[3] * ox + e[7] * oy + e[11] * oz + e[15] * ow;
            return this;
        }
    }
}
