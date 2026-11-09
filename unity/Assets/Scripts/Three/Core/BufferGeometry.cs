using System;
using System.Collections.Generic;

namespace THREE
{
    public class BufferAttribute
    {
        public float[] array;
        public int itemSize;
        public bool normalized;
        public int version;
        public string name = "";
        /// <summary>For instanced attributes: how many instances share one value.</summary>
        public int meshPerAttribute = 1;
        public bool isInstanced;

        public BufferAttribute(float[] array, int itemSize, bool normalized = false)
        {
            this.array = array;
            this.itemSize = itemSize;
            this.normalized = normalized;
        }

        public BufferAttribute(double[] array, int itemSize, bool normalized = false) : this(ToFloat(array), itemSize, normalized) { }
        public BufferAttribute(int[] array, int itemSize, bool normalized = false) : this(ToFloat(array), itemSize, normalized) { }

        internal static float[] ToFloat(double[] a)
        {
            var f = new float[a.Length];
            for (int i = 0; i < a.Length; i++) f[i] = (float)a[i];
            return f;
        }

        internal static float[] ToFloat(int[] a)
        {
            var f = new float[a.Length];
            for (int i = 0; i < a.Length; i++) f[i] = a[i];
            return f;
        }

        internal static float[] ToFloat(IList<double> a)
        {
            var f = new float[a.Count];
            for (int i = 0; i < a.Count; i++) f[i] = (float)a[i];
            return f;
        }

        public int count => array.Length / itemSize;

        public bool needsUpdate { set { if (value) version++; } get => false; }

        public BufferAttribute setUsage(int usage) => this;

        public double getX(int i) => array[i * itemSize];
        public double getY(int i) => array[i * itemSize + 1];
        public double getZ(int i) => array[i * itemSize + 2];
        public double getW(int i) => array[i * itemSize + 3];
        public double getComponent(int i, int c) => array[i * itemSize + c];
        public BufferAttribute setX(int i, double v) { array[i * itemSize] = (float)v; return this; }
        public BufferAttribute setY(int i, double v) { array[i * itemSize + 1] = (float)v; return this; }
        public BufferAttribute setZ(int i, double v) { array[i * itemSize + 2] = (float)v; return this; }
        public BufferAttribute setW(int i, double v) { array[i * itemSize + 3] = (float)v; return this; }
        public BufferAttribute setComponent(int i, int c, double v) { array[i * itemSize + c] = (float)v; return this; }
        public BufferAttribute setXY(int i, double x, double y)
        {
            int o = i * itemSize;
            array[o] = (float)x; array[o + 1] = (float)y;
            return this;
        }
        public BufferAttribute setXYZ(int i, double x, double y, double z)
        {
            int o = i * itemSize;
            array[o] = (float)x; array[o + 1] = (float)y; array[o + 2] = (float)z;
            return this;
        }
        public BufferAttribute setXYZW(int i, double x, double y, double z, double w)
        {
            int o = i * itemSize;
            array[o] = (float)x; array[o + 1] = (float)y; array[o + 2] = (float)z; array[o + 3] = (float)w;
            return this;
        }

        public BufferAttribute set(float[] values, int offset = 0)
        {
            Array.Copy(values, 0, array, offset, values.Length);
            return this;
        }

        public BufferAttribute copyArray(float[] values) { Array.Copy(values, array, Math.Min(values.Length, array.Length)); return this; }

        public virtual BufferAttribute clone()
        {
            var a = new BufferAttribute((float[])array.Clone(), itemSize, normalized) { name = name, meshPerAttribute = meshPerAttribute, isInstanced = isInstanced };
            return a;
        }

        private static readonly Vector3 _v = new Vector3();

        public BufferAttribute applyMatrix4(Matrix4 m)
        {
            for (int i = 0; i < count; i++)
            {
                _v.set(getX(i), getY(i), getZ(i)).applyMatrix4(m);
                setXYZ(i, _v.x, _v.y, _v.z);
            }
            return this;
        }

        public BufferAttribute applyNormalMatrix(Matrix3 m)
        {
            for (int i = 0; i < count; i++)
            {
                _v.set(getX(i), getY(i), getZ(i)).applyNormalMatrix(m);
                setXYZ(i, _v.x, _v.y, _v.z);
            }
            return this;
        }

        public BufferAttribute transformDirection(Matrix4 m)
        {
            for (int i = 0; i < count; i++)
            {
                _v.set(getX(i), getY(i), getZ(i)).transformDirection(m);
                setXYZ(i, _v.x, _v.y, _v.z);
            }
            return this;
        }
    }

    public class Float32BufferAttribute : BufferAttribute
    {
        public Float32BufferAttribute(float[] a, int itemSize, bool normalized = false) : base(a, itemSize, normalized) { }
        public Float32BufferAttribute(double[] a, int itemSize, bool normalized = false) : base(a, itemSize, normalized) { }
        public Float32BufferAttribute(IList<double> a, int itemSize, bool normalized = false) : base(ToFloat(a), itemSize, normalized) { }
        public Float32BufferAttribute(int count, int itemSize) : base(new float[count * itemSize], itemSize) { }
    }

    public class Uint16BufferAttribute : BufferAttribute
    {
        public Uint16BufferAttribute(int[] a, int itemSize) : base(a, itemSize) { }
        public Uint16BufferAttribute(IList<int> a, int itemSize) : base(ToInts(a), itemSize) { }
        private static float[] ToInts(IList<int> a)
        {
            var f = new float[a.Count];
            for (int i = 0; i < a.Count; i++) f[i] = a[i];
            return f;
        }
    }

    public class InstancedBufferAttribute : BufferAttribute
    {
        public InstancedBufferAttribute(float[] a, int itemSize, bool normalized = false, int meshPerAttribute = 1) : base(a, itemSize, normalized)
        {
            this.meshPerAttribute = meshPerAttribute;
            isInstanced = true;
        }
    }

    public static class Usage
    {
        public const int StaticDrawUsage = 35044;
        public const int DynamicDrawUsage = 35048;
    }

    /// <summary>`geometry.attributes.position` and friends, as in Three.js.</summary>
    public class AttributeMap : Dictionary<string, BufferAttribute>
    {
        private BufferAttribute Get(string k) => TryGetValue(k, out var a) ? a : null;
        public BufferAttribute position => Get("position");
        public BufferAttribute normal => Get("normal");
        public BufferAttribute uv => Get("uv");
        public BufferAttribute uv1 => Get("uv1");
        public BufferAttribute color => Get("color");
        public BufferAttribute skinIndex => Get("skinIndex");
        public BufferAttribute skinWeight => Get("skinWeight");
    }

    public struct GeometryGroup
    {
        public int start, count, materialIndex;
    }

    public class BufferGeometry
    {
        private static int _nextId;
        public readonly int id = _nextId++;
        public string uuid = MathUtils.generateUUID();
        public string name = "";
        public string type = "BufferGeometry";
        public BufferAttribute index;
        public AttributeMap attributes = new AttributeMap();
        public Dictionary<string, List<BufferAttribute>> morphAttributes = new Dictionary<string, List<BufferAttribute>>();
        public List<GeometryGroup> groups = new List<GeometryGroup>();
        public Box3 boundingBox;
        public Sphere boundingSphere;
        public (int start, int count) drawRange = (0, int.MaxValue);
        public Dictionary<string, object> userData = new Dictionary<string, object>();
        /// <summary>Bumped whenever the shape changes, so the Unity mesh is rebuilt.</summary>
        internal int structureVersion;
        internal bool disposed;

        public BufferAttribute getIndex() => index;

        public BufferGeometry setIndex(BufferAttribute a) { index = a; structureVersion++; return this; }
        public BufferGeometry setIndex(IList<int> a)
        {
            var f = new float[a.Count];
            for (int i = 0; i < a.Count; i++) f[i] = a[i];
            index = new BufferAttribute(f, 1);
            structureVersion++;
            return this;
        }
        public BufferGeometry setIndex(int[] a) => setIndex((IList<int>)a);

        public BufferAttribute getAttribute(string name) => attributes.TryGetValue(name, out var a) ? a : null;
        public BufferGeometry setAttribute(string name, BufferAttribute a) { attributes[name] = a; structureVersion++; return this; }
        public BufferGeometry deleteAttribute(string name) { attributes.Remove(name); structureVersion++; return this; }
        public bool hasAttribute(string name) => attributes.ContainsKey(name);

        public void addGroup(int start, int count, int materialIndex = 0) => groups.Add(new GeometryGroup { start = start, count = count, materialIndex = materialIndex });
        public void clearGroups() => groups.Clear();
        public void setDrawRange(int start, int count) { drawRange = (start, count); structureVersion++; }

        public BufferGeometry applyMatrix4(Matrix4 m)
        {
            var pos = attributes.position;
            if (pos != null) { pos.applyMatrix4(m); pos.needsUpdate = true; }
            var nor = attributes.normal;
            if (nor != null)
            {
                var nm = new Matrix3().getNormalMatrix(m);
                nor.applyNormalMatrix(nm);
                nor.needsUpdate = true;
            }
            if (boundingBox != null) computeBoundingBox();
            if (boundingSphere != null) computeBoundingSphere();
            return this;
        }

        public BufferGeometry applyQuaternion(Quaternion q) => applyMatrix4(new Matrix4().makeRotationFromQuaternion(q));
        public BufferGeometry rotateX(double a) => applyMatrix4(new Matrix4().makeRotationX(a));
        public BufferGeometry rotateY(double a) => applyMatrix4(new Matrix4().makeRotationY(a));
        public BufferGeometry rotateZ(double a) => applyMatrix4(new Matrix4().makeRotationZ(a));
        public BufferGeometry translate(double x, double y, double z) => applyMatrix4(new Matrix4().makeTranslation(x, y, z));
        public BufferGeometry scale(double x, double y, double z) => applyMatrix4(new Matrix4().makeScale(x, y, z));

        public BufferGeometry lookAt(Vector3 v)
        {
            var o = new Object3D();
            o.lookAt(v);
            o.updateMatrix();
            return applyMatrix4(o.matrix);
        }

        public BufferGeometry center()
        {
            computeBoundingBox();
            var off = boundingBox.getCenter(new Vector3()).negate();
            return translate(off.x, off.y, off.z);
        }

        public BufferGeometry setFromPoints(IList<Vector3> points)
        {
            var arr = new float[points.Count * 3];
            for (int i = 0; i < points.Count; i++)
            {
                arr[i * 3] = (float)points[i].x;
                arr[i * 3 + 1] = (float)points[i].y;
                arr[i * 3 + 2] = (float)points[i].z;
            }
            setAttribute("position", new Float32BufferAttribute(arr, 3));
            return this;
        }

        public BufferGeometry setFromPoints(IList<Vector2> points)
        {
            var arr = new float[points.Count * 3];
            for (int i = 0; i < points.Count; i++)
            {
                arr[i * 3] = (float)points[i].x;
                arr[i * 3 + 1] = (float)points[i].y;
            }
            setAttribute("position", new Float32BufferAttribute(arr, 3));
            return this;
        }

        public void computeBoundingBox()
        {
            boundingBox ??= new Box3();
            boundingBox.makeEmpty();
            var pos = attributes.position;
            if (pos == null) return;
            var v = new Vector3();
            for (int i = 0; i < pos.count; i++)
            {
                v.set(pos.getX(i), pos.getY(i), pos.getZ(i));
                boundingBox.expandByPoint(v);
            }
        }

        public void computeBoundingSphere()
        {
            boundingSphere ??= new Sphere();
            var pos = attributes.position;
            if (pos == null) { boundingSphere.radius = -1; return; }
            var box = new Box3();
            var v = new Vector3();
            for (int i = 0; i < pos.count; i++) box.expandByPoint(v.set(pos.getX(i), pos.getY(i), pos.getZ(i)));
            box.getCenter(boundingSphere.center);
            double maxR = 0;
            for (int i = 0; i < pos.count; i++)
            {
                v.set(pos.getX(i), pos.getY(i), pos.getZ(i));
                maxR = Math.Max(maxR, boundingSphere.center.distanceToSquared(v));
            }
            boundingSphere.radius = Math.Sqrt(maxR);
        }

        public void computeVertexNormals()
        {
            var pos = attributes.position;
            if (pos == null) return;
            var nor = attributes.normal;
            if (nor == null || nor.count != pos.count)
            {
                nor = new BufferAttribute(new float[pos.count * 3], 3);
                setAttribute("normal", nor);
            }
            else Array.Clear(nor.array, 0, nor.array.Length);
            var pA = new Vector3(); var pB = new Vector3(); var pC = new Vector3();
            var cb = new Vector3(); var ab = new Vector3();
            if (index != null)
            {
                var nA = new Vector3(); var nB = new Vector3(); var nC = new Vector3();
                for (int i = 0, il = index.count; i < il; i += 3)
                {
                    int vA = (int)index.getX(i), vB = (int)index.getX(i + 1), vC = (int)index.getX(i + 2);
                    pA.set(pos.getX(vA), pos.getY(vA), pos.getZ(vA));
                    pB.set(pos.getX(vB), pos.getY(vB), pos.getZ(vB));
                    pC.set(pos.getX(vC), pos.getY(vC), pos.getZ(vC));
                    cb.subVectors(pC, pB); ab.subVectors(pA, pB); cb.cross(ab);
                    nA.set(nor.getX(vA), nor.getY(vA), nor.getZ(vA)).add(cb);
                    nB.set(nor.getX(vB), nor.getY(vB), nor.getZ(vB)).add(cb);
                    nC.set(nor.getX(vC), nor.getY(vC), nor.getZ(vC)).add(cb);
                    nor.setXYZ(vA, nA.x, nA.y, nA.z);
                    nor.setXYZ(vB, nB.x, nB.y, nB.z);
                    nor.setXYZ(vC, nC.x, nC.y, nC.z);
                }
            }
            else
            {
                for (int i = 0, il = pos.count; i < il; i += 3)
                {
                    pA.set(pos.getX(i), pos.getY(i), pos.getZ(i));
                    pB.set(pos.getX(i + 1), pos.getY(i + 1), pos.getZ(i + 1));
                    pC.set(pos.getX(i + 2), pos.getY(i + 2), pos.getZ(i + 2));
                    cb.subVectors(pC, pB); ab.subVectors(pA, pB); cb.cross(ab);
                    nor.setXYZ(i, cb.x, cb.y, cb.z);
                    nor.setXYZ(i + 1, cb.x, cb.y, cb.z);
                    nor.setXYZ(i + 2, cb.x, cb.y, cb.z);
                }
            }
            normalizeNormals();
            nor.needsUpdate = true;
        }

        public void normalizeNormals()
        {
            var n = attributes.normal;
            var v = new Vector3();
            for (int i = 0; i < n.count; i++)
            {
                v.set(n.getX(i), n.getY(i), n.getZ(i)).normalize();
                n.setXYZ(i, v.x, v.y, v.z);
            }
        }

        public BufferGeometry toNonIndexed()
        {
            var g = new BufferGeometry();
            if (index == null)
            {
                foreach (var kv in attributes) g.setAttribute(kv.Key, kv.Value.clone());
                foreach (var gr in groups) g.addGroup(gr.start, gr.count, gr.materialIndex);
                return g;
            }
            int n = index.count;
            foreach (var kv in attributes)
            {
                var src = kv.Value;
                int s = src.itemSize;
                var arr = new float[n * s];
                for (int i = 0; i < n; i++)
                {
                    int vi = (int)index.array[i];
                    for (int c = 0; c < s; c++) arr[i * s + c] = src.array[vi * s + c];
                }
                g.setAttribute(kv.Key, new BufferAttribute(arr, s, src.normalized));
            }
            foreach (var gr in groups) g.addGroup(gr.start, gr.count, gr.materialIndex);
            return g;
        }

        public virtual BufferGeometry clone() => new BufferGeometry().copy(this);

        public BufferGeometry copy(BufferGeometry src)
        {
            index = src.index?.clone();
            attributes = new AttributeMap();
            foreach (var kv in src.attributes) attributes[kv.Key] = kv.Value.clone();
            groups = new List<GeometryGroup>(src.groups);
            boundingBox = src.boundingBox?.clone();
            boundingSphere = src.boundingSphere?.clone();
            drawRange = src.drawRange;
            name = src.name;
            structureVersion++;
            return this;
        }

        public void dispose()
        {
            disposed = true;
            Bridge.MeshCache.Release(this);
        }

        /// <summary>Sum of all attribute versions, so the bridge knows when the Unity mesh is stale.</summary>
        internal int ContentVersion()
        {
            int v = structureVersion * 7919;
            foreach (var a in attributes.Values) v += a.version;
            if (index != null) v += index.version * 31;
            return v;
        }
    }

    public static class BufferGeometryUtils
    {
        /// <summary>Joins geometries that share the same attributes into one (indices are rebased). With useGroups, one group per input.</summary>
        public static BufferGeometry mergeGeometries(IList<BufferGeometry> geos, bool useGroups = false)
        {
            if (geos.Count == 0) return null;
            bool indexed = geos[0].index != null;
            var names = new List<string>(geos[0].attributes.Keys);
            foreach (var g in geos)
            {
                if ((g.index != null) != indexed) return null;
                if (g.attributes.Count != names.Count) return null;
                foreach (var n in names) if (!g.attributes.ContainsKey(n)) return null;
            }
            var merged = new BufferGeometry();
            foreach (var n in names)
            {
                int size = geos[0].attributes[n].itemSize, total = 0;
                foreach (var g in geos) total += g.attributes[n].array.Length;
                var arr = new float[total];
                int off = 0;
                foreach (var g in geos)
                {
                    var a = g.attributes[n].array;
                    Array.Copy(a, 0, arr, off, a.Length);
                    off += a.Length;
                }
                merged.setAttribute(n, new BufferAttribute(arr, size, geos[0].attributes[n].normalized));
            }
            int offset = 0, groupStart = 0;
            if (indexed)
            {
                var idx = new List<int>();
                foreach (var g in geos)
                {
                    for (int i = 0; i < g.index.count; i++) idx.Add((int)g.index.array[i] + offset);
                    if (useGroups) merged.addGroup(groupStart, g.index.count, geos.IndexOf(g));
                    groupStart += g.index.count;
                    offset += g.attributes.position.count;
                }
                merged.setIndex(idx);
            }
            else if (useGroups)
            {
                for (int i = 0; i < geos.Count; i++)
                {
                    int c = geos[i].attributes.position.count;
                    merged.addGroup(groupStart, c, i);
                    groupStart += c;
                }
            }
            return merged;
        }

        /// <summary>Welds vertices whose attributes match to within the tolerance.</summary>
        public static BufferGeometry mergeVertices(BufferGeometry geo, double tolerance = 1e-4)
        {
            var pos = geo.attributes.position;
            int n = geo.index != null ? geo.index.count : pos.count;
            var names = new List<string>(geo.attributes.Keys);
            var map = new Dictionary<string, int>();
            var outAttr = new Dictionary<string, List<float>>();
            foreach (var nm in names) outAttr[nm] = new List<float>();
            var idx = new List<int>();
            double mult = 1 / tolerance;
            var sb = new System.Text.StringBuilder();
            int next = 0;
            for (int i = 0; i < n; i++)
            {
                int vi = geo.index != null ? (int)geo.index.array[i] : i;
                sb.Clear();
                foreach (var nm in names)
                {
                    var a = geo.attributes[nm];
                    for (int c = 0; c < a.itemSize; c++) sb.Append((long)Math.Round(a.array[vi * a.itemSize + c] * mult)).Append(',');
                }
                var key = sb.ToString();
                if (map.TryGetValue(key, out var found)) idx.Add(found);
                else
                {
                    foreach (var nm in names)
                    {
                        var a = geo.attributes[nm];
                        for (int c = 0; c < a.itemSize; c++) outAttr[nm].Add(a.array[vi * a.itemSize + c]);
                    }
                    map[key] = next;
                    idx.Add(next++);
                }
            }
            var res = new BufferGeometry();
            foreach (var nm in names) res.setAttribute(nm, new BufferAttribute(outAttr[nm].ToArray(), geo.attributes[nm].itemSize, geo.attributes[nm].normalized));
            res.setIndex(idx);
            return res;
        }
    }
}
