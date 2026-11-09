using System;
using System.Collections.Generic;

namespace THREE
{
    public class Layers
    {
        public int mask = 1;
        public void set(int c) => mask = 1 << c;
        public void enable(int c) => mask |= 1 << c;
        public void disable(int c) => mask &= ~(1 << c);
        public void enableAll() => mask = -1;
        public bool test(Layers l) => (mask & l.mask) != 0;
        public bool isEnabled(int c) => (mask & (1 << c)) != 0;
    }

    /// <summary>
    /// Three.js Object3D: a node in the scene graph with a transform in Three's right-handed space. The Unity bridge mirrors it into a
    /// GameObject each frame (see Bridge/SceneSync.cs).
    /// </summary>
    public class Object3D
    {
        private static int _nextId;
        public readonly int id = _nextId++;
        public string uuid = MathUtils.generateUUID();
        public string name = "";
        public virtual string type => "Object3D";
        public Object3D parent;
        public readonly List<Object3D> children = new List<Object3D>();
        public Vector3 up = new Vector3(0, 1, 0);
        public readonly Vector3 position = new Vector3();
        public readonly Euler rotation = new Euler();
        public readonly Quaternion quaternion = new Quaternion();
        public readonly Vector3 scale = new Vector3(1, 1, 1);
        public readonly Matrix4 matrix = new Matrix4();
        public readonly Matrix4 matrixWorld = new Matrix4();
        public bool matrixAutoUpdate = true;
        public bool matrixWorldAutoUpdate = true;
        public bool matrixWorldNeedsUpdate;
        public Layers layers = new Layers();
        public bool visible = true;
        public bool castShadow;
        public bool receiveShadow;
        public bool frustumCulled = true;
        public int renderOrder;
        public Dictionary<string, object> userData = new Dictionary<string, object>();

        /// <summary>The Unity side of this object, made the first time it is synced.</summary>
        internal Bridge.Node node;

        public Object3D()
        {
            rotation._onChange = () => quaternion.setFromEuler(rotation, false);
            quaternion._onChange = () => rotation.setFromQuaternion(quaternion, null, false);
        }

        public virtual bool isCamera => false;
        public virtual bool isLight => false;

        public void applyMatrix4(Matrix4 m)
        {
            if (matrixAutoUpdate) updateMatrix();
            matrix.premultiply(m);
            matrix.decompose(position, quaternion, scale);
        }

        public Object3D applyQuaternion(Quaternion q) { quaternion.premultiply(q); return this; }

        public void setRotationFromAxisAngle(Vector3 axis, double angle) => quaternion.setFromAxisAngle(axis, angle);
        public void setRotationFromEuler(Euler e) => quaternion.setFromEuler(e, true);
        public void setRotationFromMatrix(Matrix4 m) => quaternion.setFromRotationMatrix(m);
        public void setRotationFromQuaternion(Quaternion q) => quaternion.copy(q);

        private static readonly Quaternion _q1 = new Quaternion();
        private static readonly Vector3 _xAxis = new Vector3(1, 0, 0), _yAxis = new Vector3(0, 1, 0), _zAxis = new Vector3(0, 0, 1);

        public Object3D rotateOnAxis(Vector3 axis, double angle) { _q1.setFromAxisAngle(axis, angle); quaternion.multiply(_q1); return this; }
        public Object3D rotateOnWorldAxis(Vector3 axis, double angle) { _q1.setFromAxisAngle(axis, angle); quaternion.premultiply(_q1); return this; }
        public Object3D rotateX(double a) => rotateOnAxis(_xAxis, a);
        public Object3D rotateY(double a) => rotateOnAxis(_yAxis, a);
        public Object3D rotateZ(double a) => rotateOnAxis(_zAxis, a);

        private static readonly Vector3 _v1 = new Vector3();

        public Object3D translateOnAxis(Vector3 axis, double distance)
        {
            _v1.copy(axis).applyQuaternion(quaternion);
            position.add(_v1.multiplyScalar(distance));
            return this;
        }
        public Object3D translateX(double d) => translateOnAxis(_xAxis, d);
        public Object3D translateY(double d) => translateOnAxis(_yAxis, d);
        public Object3D translateZ(double d) => translateOnAxis(_zAxis, d);

        private static readonly Matrix4 _m1 = new Matrix4();

        public Vector3 localToWorld(Vector3 v)
        {
            updateWorldMatrix(true, false);
            return v.applyMatrix4(matrixWorld);
        }

        public Vector3 worldToLocal(Vector3 v)
        {
            updateWorldMatrix(true, false);
            return v.applyMatrix4(_m1.copy(matrixWorld).invert());
        }

        private static readonly Vector3 _target = new Vector3(), _position = new Vector3();
        private static readonly Quaternion _q2 = new Quaternion();

        public void lookAt(double x, double y, double z) => lookAt(_target.set(x, y, z));

        /// <summary>Turns the object so its +z faces the point (cameras and lights turn their -z to it), in world space.</summary>
        public void lookAt(Vector3 v)
        {
            var tgt = v == _target ? _target : _target.copy(v);
            var parentObj = parent;
            updateWorldMatrix(true, false);
            _position.setFromMatrixPosition(matrixWorld);
            if (isCamera || isLight) _m1.lookAt(_position, tgt, up);
            else _m1.lookAt(tgt, _position, up);
            quaternion.setFromRotationMatrix(_m1);
            if (parentObj != null)
            {
                _m1.extractRotation(parentObj.matrixWorld);
                _q2.setFromRotationMatrix(_m1);
                quaternion.premultiply(_q2.invert());
            }
        }

        public Object3D add(params Object3D[] objs)
        {
            foreach (var o in objs)
            {
                if (o == null || o == this) continue;
                o.removeFromParent();
                o.parent = this;
                children.Add(o);
            }
            return this;
        }

        public Object3D remove(params Object3D[] objs)
        {
            foreach (var o in objs)
            {
                if (o == null) continue;
                if (children.Remove(o))
                {
                    o.parent = null;
                    Bridge.SceneSync.Detach(o);
                }
            }
            return this;
        }

        public Object3D removeFromParent()
        {
            parent?.remove(this);
            return this;
        }

        public Object3D clear()
        {
            foreach (var c in children.ToArray()) remove(c);
            return this;
        }

        /// <summary>Re-parents while keeping the world transform.</summary>
        public Object3D attach(Object3D o)
        {
            updateWorldMatrix(true, false);
            _m1.copy(matrixWorld).invert();
            if (o.parent != null)
            {
                o.parent.updateWorldMatrix(true, false);
                _m1.multiply(o.parent.matrixWorld);
            }
            o.applyMatrix4(_m1);
            o.removeFromParent();
            o.parent = this;
            children.Add(o);
            o.updateWorldMatrix(false, true);
            return this;
        }

        public Object3D getObjectByName(string n)
        {
            if (name == n) return this;
            foreach (var c in children)
            {
                var r = c.getObjectByName(n);
                if (r != null) return r;
            }
            return null;
        }

        public Object3D getObjectById(int i)
        {
            if (id == i) return this;
            foreach (var c in children)
            {
                var r = c.getObjectById(i);
                if (r != null) return r;
            }
            return null;
        }

        public Vector3 getWorldPosition(Vector3 target)
        {
            updateWorldMatrix(true, false);
            return target.setFromMatrixPosition(matrixWorld);
        }

        private static readonly Vector3 _scaleTmp = new Vector3(), _posTmp = new Vector3();
        private static readonly Quaternion _quatTmp = new Quaternion();

        public Quaternion getWorldQuaternion(Quaternion target)
        {
            updateWorldMatrix(true, false);
            matrixWorld.decompose(_posTmp, target, _scaleTmp);
            return target;
        }

        public Vector3 getWorldScale(Vector3 target)
        {
            updateWorldMatrix(true, false);
            matrixWorld.decompose(_posTmp, _quatTmp, target);
            return target;
        }

        public virtual Vector3 getWorldDirection(Vector3 target)
        {
            updateWorldMatrix(true, false);
            var e = matrixWorld.elements;
            return target.set(e[8], e[9], e[10]).normalize();
        }

        public virtual void raycast(Raycaster raycaster, List<Intersection> intersects) { }

        public void traverse(Action<Object3D> fn)
        {
            fn(this);
            var list = children.ToArray();
            foreach (var c in list) c.traverse(fn);
        }

        public void traverseVisible(Action<Object3D> fn)
        {
            if (!visible) return;
            fn(this);
            var list = children.ToArray();
            foreach (var c in list) c.traverseVisible(fn);
        }

        public void traverseAncestors(Action<Object3D> fn)
        {
            if (parent == null) return;
            fn(parent);
            parent.traverseAncestors(fn);
        }

        // The position, rotation and scale the matrix was last composed from. Three.js recomposes every object's matrix (and so
        // every world matrix below it) on every render; here an object that has not moved keeps both, which is the same result
        // for a fraction of the work (thousands of objects on the beach never move).
        private double _cpx = double.NaN, _cpy, _cpz, _cqx, _cqy, _cqz, _cqw, _csx, _csy, _csz;

        public void updateMatrix()
        {
            matrix.compose(position, quaternion, scale);
            matrixWorldNeedsUpdate = true;
            Bridge.BridgeStats.composed++;
            _cpx = position.x; _cpy = position.y; _cpz = position.z;
            _cqx = quaternion._x; _cqy = quaternion._y; _cqz = quaternion._z; _cqw = quaternion._w;
            _csx = scale.x; _csy = scale.y; _csz = scale.z;
        }

        /// <summary>True when position, rotation or scale differ from what the matrix was last composed from.</summary>
        private bool LocalChanged() =>
            position.x != _cpx || position.y != _cpy || position.z != _cpz ||
            quaternion._x != _cqx || quaternion._y != _cqy || quaternion._z != _cqz || quaternion._w != _cqw ||
            scale.x != _csx || scale.y != _csy || scale.z != _csz;

        public virtual void updateMatrixWorld(bool force = false)
        {
            if (matrixAutoUpdate && LocalChanged()) updateMatrix();
            if (matrixWorldNeedsUpdate || force)
            {
                if (matrixWorldAutoUpdate)
                {
                    if (parent == null) matrixWorld.copy(matrix);
                    else matrixWorld.multiplyMatrices(parent.matrixWorld, matrix);
                }
                matrixWorldNeedsUpdate = false;
                force = true;
            }
            foreach (var c in children) c.updateMatrixWorld(force);
        }

        public virtual void updateWorldMatrix(bool updateParents, bool updateChildren)
        {
            if (updateParents && parent != null) parent.updateWorldMatrix(true, false);
            // raycasts call this on every mesh they test (hover and aim, every frame): recompose only what moved
            if (matrixAutoUpdate && LocalChanged()) updateMatrix();
            if (matrixWorldAutoUpdate)
            {
                if (parent == null) matrixWorld.copy(matrix);
                else matrixWorld.multiplyMatrices(parent.matrixWorld, matrix);
            }
            if (updateChildren) foreach (var c in children) c.updateWorldMatrix(false, true);
        }

        /// <summary>Copies this node's own fields (not children) from another of the same kind.</summary>
        public virtual Object3D copy(Object3D src, bool recursive = true)
        {
            name = src.name;
            up.copy(src.up);
            position.copy(src.position);
            rotation._order = src.rotation._order;
            quaternion.copy(src.quaternion);
            scale.copy(src.scale);
            matrix.copy(src.matrix);
            matrixWorld.copy(src.matrixWorld);
            matrixAutoUpdate = src.matrixAutoUpdate;
            visible = src.visible;
            castShadow = src.castShadow;
            receiveShadow = src.receiveShadow;
            frustumCulled = src.frustumCulled;
            renderOrder = src.renderOrder;
            layers.mask = src.layers.mask;
            userData = new Dictionary<string, object>(src.userData);
            if (recursive) foreach (var c in src.children) add(c.clone());
            return this;
        }

        public virtual Object3D clone(bool recursive = true) => new Object3D().copy(this, recursive);
    }

    public class Group : Object3D
    {
        public override string type => "Group";
        public override Object3D clone(bool recursive = true) => new Group().copy(this, recursive);
    }

    public class Fog
    {
        public Color color;
        public double near, far;
        public Fog(int color, double near = 1, double far = 1000) { this.color = new Color(color); this.near = near; this.far = far; }
        public Fog(Color color, double near = 1, double far = 1000) { this.color = color.clone(); this.near = near; this.far = far; }
        public virtual Fog clone() => new Fog(color, near, far);
    }

    public class FogExp2 : Fog
    {
        public double density;
        public FogExp2(int color, double density = 0.00025) : base(color) { this.density = density; }
        public FogExp2(Color color, double density = 0.00025) : base(color) { this.density = density; }
        public override Fog clone() => new FogExp2(color, density);
    }

    public class Scene : Object3D
    {
        public override string type => "Scene";
        /// <summary>A Color, a Texture, or null.</summary>
        public object background;
        public Texture environment;
        public double environmentIntensity = 1;
        public double backgroundIntensity = 1;
        public double backgroundBlurriness;
        public Fog fog;
        public Material overrideMaterial;
        /// <summary>Which Unity layer this scene's objects live on, so separate scenes do not show up in each other's cameras.</summary>
        internal int unityLayer;
        private static int nextLayer = 8;
        internal static void ResetLayers() => nextLayer = 8;

        public Scene()
        {
            unityLayer = nextLayer < 31 ? nextLayer++ : 31;
        }

        public override Object3D clone(bool recursive = true) => new Scene().copy(this, recursive);
    }

    public class Clock
    {
        public bool autoStart = true;
        public double startTime, oldTime, elapsedTime;
        public bool running;

        public Clock(bool autoStart = true) { this.autoStart = autoStart; }

        public void start()
        {
            startTime = JS.Now();
            oldTime = startTime;
            elapsedTime = 0;
            running = true;
        }

        public void stop() { getElapsedTime(); running = false; autoStart = false; }

        public double getElapsedTime() { getDelta(); return elapsedTime; }

        public double getDelta()
        {
            double diff = 0;
            if (autoStart && !running) { start(); return 0; }
            if (running)
            {
                double now = JS.Now();
                diff = (now - oldTime) / 1000;
                oldTime = now;
                elapsedTime += diff;
            }
            return diff;
        }
    }
}
