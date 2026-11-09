using System;

namespace THREE
{
    public class Quaternion
    {
        internal double _x, _y, _z, _w;
        internal Action _onChange;

        public Quaternion(double x = 0, double y = 0, double z = 0, double w = 1) { _x = x; _y = y; _z = z; _w = w; }

        public double x { get => _x; set { _x = value; _onChange?.Invoke(); } }
        public double y { get => _y; set { _y = value; _onChange?.Invoke(); } }
        public double z { get => _z; set { _z = value; _onChange?.Invoke(); } }
        public double w { get => _w; set { _w = value; _onChange?.Invoke(); } }

        public Quaternion set(double x, double y, double z, double w) { _x = x; _y = y; _z = z; _w = w; _onChange?.Invoke(); return this; }
        public Quaternion clone() => new Quaternion(_x, _y, _z, _w);
        public Quaternion copy(Quaternion q) { _x = q._x; _y = q._y; _z = q._z; _w = q._w; _onChange?.Invoke(); return this; }
        public Quaternion identity() => set(0, 0, 0, 1);

        public Quaternion setFromEuler(Euler e, bool update = true)
        {
            double x = e._x, y = e._y, z = e._z;
            double c1 = Math.Cos(x / 2), c2 = Math.Cos(y / 2), c3 = Math.Cos(z / 2);
            double s1 = Math.Sin(x / 2), s2 = Math.Sin(y / 2), s3 = Math.Sin(z / 2);
            switch (e._order)
            {
                case "XYZ":
                    _x = s1 * c2 * c3 + c1 * s2 * s3; _y = c1 * s2 * c3 - s1 * c2 * s3;
                    _z = c1 * c2 * s3 + s1 * s2 * c3; _w = c1 * c2 * c3 - s1 * s2 * s3; break;
                case "YXZ":
                    _x = s1 * c2 * c3 + c1 * s2 * s3; _y = c1 * s2 * c3 - s1 * c2 * s3;
                    _z = c1 * c2 * s3 - s1 * s2 * c3; _w = c1 * c2 * c3 + s1 * s2 * s3; break;
                case "ZXY":
                    _x = s1 * c2 * c3 - c1 * s2 * s3; _y = c1 * s2 * c3 + s1 * c2 * s3;
                    _z = c1 * c2 * s3 + s1 * s2 * c3; _w = c1 * c2 * c3 - s1 * s2 * s3; break;
                case "ZYX":
                    _x = s1 * c2 * c3 - c1 * s2 * s3; _y = c1 * s2 * c3 + s1 * c2 * s3;
                    _z = c1 * c2 * s3 - s1 * s2 * c3; _w = c1 * c2 * c3 + s1 * s2 * s3; break;
                case "YZX":
                    _x = s1 * c2 * c3 + c1 * s2 * s3; _y = c1 * s2 * c3 + s1 * c2 * s3;
                    _z = c1 * c2 * s3 - s1 * s2 * c3; _w = c1 * c2 * c3 - s1 * s2 * s3; break;
                case "XZY":
                    _x = s1 * c2 * c3 - c1 * s2 * s3; _y = c1 * s2 * c3 - s1 * c2 * s3;
                    _z = c1 * c2 * s3 + s1 * s2 * c3; _w = c1 * c2 * c3 + s1 * s2 * s3; break;
            }
            if (update) _onChange?.Invoke();
            return this;
        }

        public Quaternion setFromAxisAngle(Vector3 axis, double angle)
        {
            double h = angle / 2, s = Math.Sin(h);
            _x = axis.x * s; _y = axis.y * s; _z = axis.z * s; _w = Math.Cos(h);
            _onChange?.Invoke();
            return this;
        }

        public Quaternion setFromRotationMatrix(Matrix4 m)
        {
            var te = m.elements;
            double m11 = te[0], m12 = te[4], m13 = te[8];
            double m21 = te[1], m22 = te[5], m23 = te[9];
            double m31 = te[2], m32 = te[6], m33 = te[10];
            double trace = m11 + m22 + m33;
            if (trace > 0)
            {
                double s = 0.5 / Math.Sqrt(trace + 1.0);
                _w = 0.25 / s; _x = (m32 - m23) * s; _y = (m13 - m31) * s; _z = (m21 - m12) * s;
            }
            else if (m11 > m22 && m11 > m33)
            {
                double s = 2.0 * Math.Sqrt(1.0 + m11 - m22 - m33);
                _w = (m32 - m23) / s; _x = 0.25 * s; _y = (m12 + m21) / s; _z = (m13 + m31) / s;
            }
            else if (m22 > m33)
            {
                double s = 2.0 * Math.Sqrt(1.0 + m22 - m11 - m33);
                _w = (m13 - m31) / s; _x = (m12 + m21) / s; _y = 0.25 * s; _z = (m23 + m32) / s;
            }
            else
            {
                double s = 2.0 * Math.Sqrt(1.0 + m33 - m11 - m22);
                _w = (m21 - m12) / s; _x = (m13 + m31) / s; _y = (m23 + m32) / s; _z = 0.25 * s;
            }
            _onChange?.Invoke();
            return this;
        }

        public Quaternion setFromUnitVectors(Vector3 vFrom, Vector3 vTo)
        {
            double r = vFrom.dot(vTo) + 1;
            if (r < 1e-8)
            {
                r = 0;
                if (Math.Abs(vFrom.x) > Math.Abs(vFrom.z)) { _x = -vFrom.y; _y = vFrom.x; _z = 0; _w = r; }
                else { _x = 0; _y = -vFrom.z; _z = vFrom.y; _w = r; }
            }
            else
            {
                _x = vFrom.y * vTo.z - vFrom.z * vTo.y;
                _y = vFrom.z * vTo.x - vFrom.x * vTo.z;
                _z = vFrom.x * vTo.y - vFrom.y * vTo.x;
                _w = r;
            }
            return normalize();
        }

        public double angleTo(Quaternion q) => 2 * Math.Acos(Math.Abs(MathUtils.clamp(dot(q), -1, 1)));

        public Quaternion rotateTowards(Quaternion q, double step)
        {
            var angle = angleTo(q);
            if (angle == 0) return this;
            var t = Math.Min(1, step / angle);
            slerp(q, t);
            return this;
        }

        public Quaternion invert() => conjugate();
        public Quaternion conjugate() { _x *= -1; _y *= -1; _z *= -1; _onChange?.Invoke(); return this; }
        public double dot(Quaternion v) => _x * v._x + _y * v._y + _z * v._z + _w * v._w;
        public double lengthSq() => _x * _x + _y * _y + _z * _z + _w * _w;
        public double length() => Math.Sqrt(lengthSq());

        public Quaternion normalize()
        {
            double l = length();
            if (l == 0) { _x = 0; _y = 0; _z = 0; _w = 1; }
            else { l = 1 / l; _x *= l; _y *= l; _z *= l; _w *= l; }
            _onChange?.Invoke();
            return this;
        }

        public Quaternion multiply(Quaternion q) => multiplyQuaternions(this, q);
        public Quaternion premultiply(Quaternion q) => multiplyQuaternions(q, this);

        public Quaternion multiplyQuaternions(Quaternion a, Quaternion b)
        {
            double qax = a._x, qay = a._y, qaz = a._z, qaw = a._w;
            double qbx = b._x, qby = b._y, qbz = b._z, qbw = b._w;
            _x = qax * qbw + qaw * qbx + qay * qbz - qaz * qby;
            _y = qay * qbw + qaw * qby + qaz * qbx - qax * qbz;
            _z = qaz * qbw + qaw * qbz + qax * qby - qay * qbx;
            _w = qaw * qbw - qax * qbx - qay * qby - qaz * qbz;
            _onChange?.Invoke();
            return this;
        }

        public Quaternion slerp(Quaternion qb, double t)
        {
            if (t == 0) return this;
            if (t == 1) return copy(qb);
            double x = _x, y = _y, z = _z, w = _w;
            double cosHalfTheta = w * qb._w + x * qb._x + y * qb._y + z * qb._z;
            if (cosHalfTheta < 0)
            {
                _w = -qb._w; _x = -qb._x; _y = -qb._y; _z = -qb._z;
                cosHalfTheta = -cosHalfTheta;
            }
            else
            {
                _w = qb._w; _x = qb._x; _y = qb._y; _z = qb._z;
            }
            if (cosHalfTheta >= 1.0) { _w = w; _x = x; _y = y; _z = z; return this; }
            double sqrSinHalfTheta = 1.0 - cosHalfTheta * cosHalfTheta;
            if (sqrSinHalfTheta <= double.Epsilon)
            {
                double s = 1 - t;
                _w = s * w + t * _w; _x = s * x + t * _x; _y = s * y + t * _y; _z = s * z + t * _z;
                normalize();
                return this;
            }
            double sinHalfTheta = Math.Sqrt(sqrSinHalfTheta);
            double halfTheta = Math.Atan2(sinHalfTheta, cosHalfTheta);
            double ratioA = Math.Sin((1 - t) * halfTheta) / sinHalfTheta, ratioB = Math.Sin(t * halfTheta) / sinHalfTheta;
            _w = w * ratioA + _w * ratioB; _x = x * ratioA + _x * ratioB; _y = y * ratioA + _y * ratioB; _z = z * ratioA + _z * ratioB;
            _onChange?.Invoke();
            return this;
        }

        public Quaternion slerpQuaternions(Quaternion a, Quaternion b, double t) => copy(a).slerp(b, t);
        public bool equals(Quaternion q) => q._x == _x && q._y == _y && q._z == _z && q._w == _w;

        public Quaternion random()
        {
            double theta1 = 2 * Math.PI * JS.Random(), theta2 = 2 * Math.PI * JS.Random();
            double x0 = JS.Random(), r1 = Math.Sqrt(1 - x0), r2 = Math.Sqrt(x0);
            return set(r1 * Math.Sin(theta1), r1 * Math.Cos(theta1), r2 * Math.Sin(theta2), r2 * Math.Cos(theta2));
        }

        /// <summary>The same rotation as seen by Unity's left-handed axes (z mirrored).</summary>
        public UnityEngine.Quaternion ToUnity() => new UnityEngine.Quaternion((float)-_x, (float)-_y, (float)_z, (float)_w);
    }

    public class Euler
    {
        internal double _x, _y, _z;
        internal string _order;
        internal Action _onChange;
        public const string DEFAULT_ORDER = "XYZ";

        public Euler(double x = 0, double y = 0, double z = 0, string order = DEFAULT_ORDER) { _x = x; _y = y; _z = z; _order = order; }

        public double x { get => _x; set { _x = value; _onChange?.Invoke(); } }
        public double y { get => _y; set { _y = value; _onChange?.Invoke(); } }
        public double z { get => _z; set { _z = value; _onChange?.Invoke(); } }
        public string order { get => _order; set { _order = value; _onChange?.Invoke(); } }

        public Euler set(double x, double y, double z) { _x = x; _y = y; _z = z; _onChange?.Invoke(); return this; }
        public Euler set(double x, double y, double z, string order) { _x = x; _y = y; _z = z; _order = order; _onChange?.Invoke(); return this; }
        public Euler clone() => new Euler(_x, _y, _z, _order);
        public Euler copy(Euler e) { _x = e._x; _y = e._y; _z = e._z; _order = e._order; _onChange?.Invoke(); return this; }

        public Euler setFromRotationMatrix(Matrix4 m, string order = null, bool update = true)
        {
            var te = m.elements;
            double m11 = te[0], m12 = te[4], m13 = te[8];
            double m21 = te[1], m22 = te[5], m23 = te[9];
            double m31 = te[2], m32 = te[6], m33 = te[10];
            order ??= _order;
            switch (order)
            {
                case "XYZ":
                    _y = Math.Asin(MathUtils.clamp(m13, -1, 1));
                    if (Math.Abs(m13) < 0.9999999) { _x = Math.Atan2(-m23, m33); _z = Math.Atan2(-m12, m11); }
                    else { _x = Math.Atan2(m32, m22); _z = 0; }
                    break;
                case "YXZ":
                    _x = Math.Asin(-MathUtils.clamp(m23, -1, 1));
                    if (Math.Abs(m23) < 0.9999999) { _y = Math.Atan2(m13, m33); _z = Math.Atan2(m21, m22); }
                    else { _y = Math.Atan2(-m31, m11); _z = 0; }
                    break;
                case "ZXY":
                    _x = Math.Asin(MathUtils.clamp(m32, -1, 1));
                    if (Math.Abs(m32) < 0.9999999) { _y = Math.Atan2(-m31, m33); _z = Math.Atan2(-m12, m22); }
                    else { _y = 0; _z = Math.Atan2(m21, m11); }
                    break;
                case "ZYX":
                    _y = Math.Asin(-MathUtils.clamp(m31, -1, 1));
                    if (Math.Abs(m31) < 0.9999999) { _x = Math.Atan2(m32, m33); _z = Math.Atan2(m21, m11); }
                    else { _x = 0; _z = Math.Atan2(-m12, m22); }
                    break;
                case "YZX":
                    _z = Math.Asin(MathUtils.clamp(m21, -1, 1));
                    if (Math.Abs(m21) < 0.9999999) { _x = Math.Atan2(-m23, m22); _y = Math.Atan2(-m31, m11); }
                    else { _x = 0; _y = Math.Atan2(m13, m33); }
                    break;
                case "XZY":
                    _z = Math.Asin(-MathUtils.clamp(m12, -1, 1));
                    if (Math.Abs(m12) < 0.9999999) { _x = Math.Atan2(m32, m22); _y = Math.Atan2(m13, m11); }
                    else { _x = Math.Atan2(-m23, m33); _y = 0; }
                    break;
            }
            _order = order;
            if (update) _onChange?.Invoke();
            return this;
        }

        private static readonly Matrix4 _m = new Matrix4();

        public Euler setFromQuaternion(Quaternion q, string order = null, bool update = true)
        {
            _m.makeRotationFromQuaternion(q);
            return setFromRotationMatrix(_m, order, update);
        }

        public Euler setFromVector3(Vector3 v, string order = null) => set(v.x, v.y, v.z, order ?? _order);

        public Euler reorder(string newOrder)
        {
            var q = new Quaternion().setFromEuler(this);
            return setFromQuaternion(q, newOrder);
        }

        public bool equals(Euler e) => e._x == _x && e._y == _y && e._z == _z && e._order == _order;
    }
}
