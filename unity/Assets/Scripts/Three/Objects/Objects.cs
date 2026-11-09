using System;
using System.Collections.Generic;

namespace THREE
{
    public class Face
    {
        public int a, b, c, materialIndex;
        public Vector3 normal = new Vector3();
    }

    public class Intersection
    {
        public double distance;
        public Vector3 point;
        public Object3D @object;
        public Face face;
        public int faceIndex = -1;
        public int instanceId = -1;
        public int index = -1;
        public Vector2 uv;
        public double distanceToRay;
    }

    public class Raycaster
    {
        public Ray ray = new Ray();
        public double near = 0, far = double.PositiveInfinity;
        public Layers layers = new Layers();
        public Camera camera;
        public double pointsThreshold = 1, lineThreshold = 1;

        public Raycaster() { }
        public Raycaster(Vector3 origin, Vector3 direction, double near = 0, double far = double.PositiveInfinity)
        {
            ray.set(origin, direction);
            this.near = near;
            this.far = far;
        }

        public void set(Vector3 origin, Vector3 direction) => ray.set(origin, direction);

        /// <summary>A ray from the camera through a point in normalised device coordinates (-1..1).</summary>
        public void setFromCamera(Vector2 coords, Camera cam)
        {
            camera = cam;
            cam.updateWorldMatrix(true, false);
            if (cam is PerspectiveCamera)
            {
                ray.origin.setFromMatrixPosition(cam.matrixWorld);
                ray.direction.set(coords.x, coords.y, 0.5).unproject(cam).sub(ray.origin).normalize();
            }
            else
            {
                ray.origin.set(coords.x, coords.y, -1).unproject(cam);
                ray.direction.set(0, 0, -1).transformDirection(cam.matrixWorld);
            }
        }

        public List<Intersection> intersectObject(Object3D o, bool recursive = true, List<Intersection> target = null)
        {
            target ??= new List<Intersection>();
            Walk(o, recursive, target);
            target.Sort((a, b) => a.distance.CompareTo(b.distance));
            return target;
        }

        public List<Intersection> intersectObjects(IEnumerable<Object3D> objs, bool recursive = true, List<Intersection> target = null)
        {
            target ??= new List<Intersection>();
            foreach (var o in objs) Walk(o, recursive, target);
            target.Sort((a, b) => a.distance.CompareTo(b.distance));
            return target;
        }

        private void Walk(Object3D o, bool recursive, List<Intersection> target)
        {
            if (o.layers.test(layers)) o.raycast(this, target);
            if (recursive) foreach (var c in o.children) Walk(c, true, target);
        }
    }

    public class Mesh : Object3D
    {
        public override string type => "Mesh";
        public BufferGeometry geometry;
        private Material _material;
        public Material[] materials;
        public Dictionary<string, double> morphTargetDictionary;
        public double[] morphTargetInfluences;

        public Material material
        {
            get => _material ?? (materials != null && materials.Length > 0 ? materials[0] : null);
            set { _material = value; materials = null; }
        }

        public Mesh() : this(new BufferGeometry(), new MeshBasicMaterial()) { }
        public Mesh(BufferGeometry g, Material m = null) { geometry = g ?? new BufferGeometry(); _material = m ?? new MeshBasicMaterial(); }
        public Mesh(BufferGeometry g, Material[] ms) { geometry = g ?? new BufferGeometry(); materials = ms; }

        public bool isMultiMaterial => materials != null && _material == null;

        public Material MaterialFor(int groupIndex) => isMultiMaterial ? (groupIndex < materials.Length ? materials[groupIndex] : null) : _material;

        public override Object3D copy(Object3D src, bool recursive = true)
        {
            base.copy(src, recursive);
            var m = (Mesh)src;
            geometry = m.geometry;
            _material = m._material;
            materials = m.materials;
            return this;
        }

        public override Object3D clone(bool recursive = true) => new Mesh(geometry, material).copy(this, recursive);

        private static readonly Matrix4 _inverse = new Matrix4();
        private static readonly Ray _ray = new Ray();
        private static readonly Sphere _sphere = new Sphere();
        private static readonly Vector3 _vA = new Vector3(), _vB = new Vector3(), _vC = new Vector3(), _hit = new Vector3();

        public override void raycast(Raycaster raycaster, List<Intersection> intersects)
        {
            if (material == null || geometry == null) return;
            if (geometry.boundingSphere == null) geometry.computeBoundingSphere();
            updateWorldMatrix(true, false);
            _sphere.copy(geometry.boundingSphere).applyMatrix4(matrixWorld);
            if (!raycaster.ray.intersectsSphere(_sphere)) return;
            _inverse.copy(matrixWorld).invert();
            _ray.copy(raycaster.ray).applyMatrix4(_inverse);
            if (geometry.boundingBox == null) geometry.computeBoundingBox();
            if (!_ray.intersectsBox(geometry.boundingBox)) return;
            RaycastGeometry(raycaster, intersects, _ray, matrixWorld, -1);
        }

        internal void RaycastGeometry(Raycaster raycaster, List<Intersection> intersects, Ray localRay, Matrix4 world, int instanceId)
        {
            var pos = geometry.attributes.position;
            if (pos == null) return;
            var idx = geometry.index;
            int total = idx != null ? idx.count : pos.count;
            void Tri(int a, int b, int c, Material mat, int matIndex, int faceIndex)
            {
                if (mat == null) return;
                _vA.set(pos.getX(a), pos.getY(a), pos.getZ(a));
                _vB.set(pos.getX(b), pos.getY(b), pos.getZ(b));
                _vC.set(pos.getX(c), pos.getY(c), pos.getZ(c));
                Vector3 hit = mat.side == Side.BackSide
                    ? localRay.intersectTriangle(_vC, _vB, _vA, true, _hit)
                    : localRay.intersectTriangle(_vA, _vB, _vC, mat.side == Side.FrontSide, _hit);
                if (hit == null) return;
                var worldPoint = hit.clone().applyMatrix4(world);
                double dist = raycaster.ray.origin.distanceTo(worldPoint);
                if (dist < raycaster.near || dist > raycaster.far) return;
                var face = new Face { a = a, b = b, c = c, materialIndex = matIndex };
                var e1 = new Vector3().subVectors(_vC, _vB);
                var e2 = new Vector3().subVectors(_vA, _vB);
                face.normal.crossVectors(e1, e2).normalize();
                Vector2 uv = null;
                var uvAttr = geometry.attributes.uv;
                if (uvAttr != null)
                {
                    var bary = Barycentric(hit, _vA, _vB, _vC);
                    uv = new Vector2(
                        uvAttr.getX(a) * bary.x + uvAttr.getX(b) * bary.y + uvAttr.getX(c) * bary.z,
                        uvAttr.getY(a) * bary.x + uvAttr.getY(b) * bary.y + uvAttr.getY(c) * bary.z);
                }
                intersects.Add(new Intersection { distance = dist, point = worldPoint, @object = this, face = face, faceIndex = faceIndex, instanceId = instanceId, uv = uv });
            }
            if (isMultiMaterial && geometry.groups.Count > 0)
            {
                foreach (var g in geometry.groups)
                {
                    var mat = MaterialFor(g.materialIndex);
                    int end = Math.Min(g.start + g.count, total);
                    for (int i = Math.Max(0, g.start); i + 2 < end; i += 3)
                    {
                        int a = idx != null ? (int)idx.array[i] : i, b = idx != null ? (int)idx.array[i + 1] : i + 1, c = idx != null ? (int)idx.array[i + 2] : i + 2;
                        Tri(a, b, c, mat, g.materialIndex, i / 3);
                    }
                }
            }
            else
            {
                var mat = material;
                int start = Math.Max(0, geometry.drawRange.start);
                int end = (int)Math.Min(total, (long)geometry.drawRange.start + geometry.drawRange.count);
                for (int i = start; i + 2 < end; i += 3)
                {
                    int a = idx != null ? (int)idx.array[i] : i, b = idx != null ? (int)idx.array[i + 1] : i + 1, c = idx != null ? (int)idx.array[i + 2] : i + 2;
                    Tri(a, b, c, mat, 0, i / 3);
                }
            }
        }

        private static Vector3 Barycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var v0 = new Vector3().subVectors(c, a);
            var v1 = new Vector3().subVectors(b, a);
            var v2 = new Vector3().subVectors(p, a);
            double dot00 = v0.dot(v0), dot01 = v0.dot(v1), dot02 = v0.dot(v2), dot11 = v1.dot(v1), dot12 = v1.dot(v2);
            double denom = dot00 * dot11 - dot01 * dot01;
            if (denom == 0) return new Vector3(1, 0, 0);
            double inv = 1 / denom;
            double u = (dot11 * dot02 - dot01 * dot12) * inv, v = (dot00 * dot12 - dot01 * dot02) * inv;
            return new Vector3(1 - u - v, v, u);
        }
    }

    public class InstancedMesh : Mesh
    {
        public override string type => "InstancedMesh";
        public InstancedBufferAttribute instanceMatrix;
        public InstancedBufferAttribute instanceColor;
        public int count;
        public Sphere boundingSphere;
        public Box3 boundingBox;

        public InstancedMesh(BufferGeometry g, Material m, int count) : base(g, m)
        {
            this.count = count;
            var arr = new float[count * 16];
            for (int i = 0; i < count; i++)
            {
                arr[i * 16] = 1; arr[i * 16 + 5] = 1; arr[i * 16 + 10] = 1; arr[i * 16 + 15] = 1;
            }
            instanceMatrix = new InstancedBufferAttribute(arr, 16);
        }

        public void getMatrixAt(int i, Matrix4 m)
        {
            for (int k = 0; k < 16; k++) m.elements[k] = instanceMatrix.array[i * 16 + k];
        }

        public void setMatrixAt(int i, Matrix4 m)
        {
            for (int k = 0; k < 16; k++) instanceMatrix.array[i * 16 + k] = (float)m.elements[k];
        }

        public void getColorAt(int i, Color c)
        {
            if (instanceColor == null) { c.setRGB(1, 1, 1); return; }
            c.setRGB(instanceColor.array[i * 3], instanceColor.array[i * 3 + 1], instanceColor.array[i * 3 + 2]);
        }

        public void setColorAt(int i, Color c)
        {
            instanceColor ??= new InstancedBufferAttribute(Fill(instanceMatrix.count, 1f), 3);
            instanceColor.array[i * 3] = (float)c.r;
            instanceColor.array[i * 3 + 1] = (float)c.g;
            instanceColor.array[i * 3 + 2] = (float)c.b;
        }

        private static float[] Fill(int n, float v)
        {
            var a = new float[n * 3];
            for (int i = 0; i < a.Length; i++) a[i] = v;
            return a;
        }

        public void computeBoundingSphere()
        {
            if (geometry.boundingSphere == null) geometry.computeBoundingSphere();
            boundingSphere ??= new Sphere();
            var box = new Box3();
            var m = new Matrix4();
            for (int i = 0; i < count; i++)
            {
                getMatrixAt(i, m);
                var s = geometry.boundingSphere.clone().applyMatrix4(m);
                box.expandByPoint(new Vector3().copy(s.center).addScalar(s.radius));
                box.expandByPoint(new Vector3().copy(s.center).addScalar(-s.radius));
            }
            box.getBoundingSphere(boundingSphere);
        }

        public void computeBoundingBox()
        {
            if (geometry.boundingBox == null) geometry.computeBoundingBox();
            boundingBox ??= new Box3();
            boundingBox.makeEmpty();
            var m = new Matrix4();
            for (int i = 0; i < count; i++)
            {
                getMatrixAt(i, m);
                boundingBox.union(geometry.boundingBox.clone().applyMatrix4(m));
            }
        }

        public void dispose() { }

        private static readonly Matrix4 _inst = new Matrix4(), _world = new Matrix4(), _inv = new Matrix4();
        private static readonly Ray _r = new Ray();
        private static readonly Sphere _s = new Sphere();

        public override void raycast(Raycaster raycaster, List<Intersection> intersects)
        {
            if (material == null) return;
            if (geometry.boundingSphere == null) geometry.computeBoundingSphere();
            if (geometry.boundingBox == null) geometry.computeBoundingBox();
            updateWorldMatrix(true, false);
            for (int i = 0; i < count; i++)
            {
                getMatrixAt(i, _inst);
                _world.multiplyMatrices(matrixWorld, _inst);
                _s.copy(geometry.boundingSphere).applyMatrix4(_world);
                if (!raycaster.ray.intersectsSphere(_s)) continue;
                _inv.copy(_world).invert();
                _r.copy(raycaster.ray).applyMatrix4(_inv);
                if (!_r.intersectsBox(geometry.boundingBox)) continue;
                RaycastGeometry(raycaster, intersects, _r, _world.clone(), i);
            }
        }
    }

    public class Bone : Object3D
    {
        public override string type => "Bone";
        public override Object3D clone(bool recursive = true) => new Bone().copy(this, recursive);
    }

    public class Skeleton
    {
        public Bone[] bones;
        public Matrix4[] boneInverses;

        public Skeleton(Bone[] bones, Matrix4[] inverses = null)
        {
            this.bones = bones;
            if (inverses != null) boneInverses = inverses;
            else calculateInverses();
        }

        public void calculateInverses()
        {
            boneInverses = new Matrix4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].updateWorldMatrix(true, false);
                boneInverses[i] = bones[i].matrixWorld.clone().invert();
            }
        }
    }

    public class SkinnedMesh : Mesh
    {
        public override string type => "SkinnedMesh";
        public Skeleton skeleton;
        public Matrix4 bindMatrix = new Matrix4();
        public Matrix4 bindMatrixInverse = new Matrix4();

        public SkinnedMesh(BufferGeometry g, Material m) : base(g, m) { }

        public void bind(Skeleton s, Matrix4 bindMatrix = null)
        {
            skeleton = s;
            if (bindMatrix == null)
            {
                updateMatrixWorld(true);
                skeleton.calculateInverses();
                bindMatrix = matrixWorld;
            }
            this.bindMatrix.copy(bindMatrix);
            bindMatrixInverse.copy(bindMatrix).invert();
        }
    }

    public class Points : Object3D
    {
        public override string type => "Points";
        public BufferGeometry geometry;
        public Material material;

        public Points(BufferGeometry g, Material m) { geometry = g; material = m; }

        public override Object3D clone(bool recursive = true) => new Points(geometry, material).copy(this, recursive);
    }

    public class Line : Object3D
    {
        public override string type => "Line";
        public BufferGeometry geometry;
        public Material material;
        public Line(BufferGeometry g, Material m) { geometry = g; material = m; }
        public override Object3D clone(bool recursive = true) => new Line(geometry, material).copy(this, recursive);
    }

    public class LineSegments : Line
    {
        public override string type => "LineSegments";
        public LineSegments(BufferGeometry g, Material m) : base(g, m) { }
        public override Object3D clone(bool recursive = true) => new LineSegments(geometry, material).copy(this, recursive);
    }

    public class LineLoop : Line
    {
        public override string type => "LineLoop";
        public LineLoop(BufferGeometry g, Material m) : base(g, m) { }
    }

    /// <summary>A quad that always faces the camera, scaled by the object's scale (Three's Sprite).</summary>
    public class Sprite : Object3D
    {
        public override string type => "Sprite";
        public SpriteMaterial material;
        public Vector2 center = new Vector2(0.5, 0.5);
        internal static BufferGeometry quad;

        public Sprite(SpriteMaterial m = null)
        {
            material = m ?? new SpriteMaterial();
            if (quad == null)
            {
                quad = new BufferGeometry();
                quad.setAttribute("position", new Float32BufferAttribute(new double[] { -0.5, -0.5, 0, 0.5, -0.5, 0, 0.5, 0.5, 0, -0.5, 0.5, 0 }, 3));
                quad.setAttribute("uv", new Float32BufferAttribute(new double[] { 0, 0, 1, 0, 1, 1, 0, 1 }, 2));
                quad.setIndex(new[] { 0, 1, 2, 0, 2, 3 });
            }
        }

        public BufferGeometry geometry => quad;

        public override Object3D clone(bool recursive = true) => new Sprite(material).copy(this, recursive);

        private static readonly Vector3 _wp = new Vector3(), _ws = new Vector3();

        public override void raycast(Raycaster raycaster, List<Intersection> intersects)
        {
            // a sphere test around the sprite is close enough for clicks
            getWorldPosition(_wp);
            getWorldScale(_ws);
            double r = Math.Max(_ws.x, _ws.y) * 0.5;
            var s = new Sphere(_wp, r);
            var hit = raycaster.ray.intersectSphere(s, new Vector3());
            if (hit == null) return;
            double d = raycaster.ray.origin.distanceTo(hit);
            if (d < raycaster.near || d > raycaster.far) return;
            intersects.Add(new Intersection { distance = d, point = hit, @object = this });
        }
    }
}
