using System;

namespace THREE
{
    public class Camera : Object3D
    {
        public override string type => "Camera";
        public override bool isCamera => true;
        public Matrix4 matrixWorldInverse = new Matrix4();
        public Matrix4 projectionMatrix = new Matrix4();
        public Matrix4 projectionMatrixInverse = new Matrix4();

        public override Vector3 getWorldDirection(Vector3 target)
        {
            return base.getWorldDirection(target).negate();
        }

        public override void updateMatrixWorld(bool force = false)
        {
            base.updateMatrixWorld(force);
            matrixWorldInverse.copy(matrixWorld).invert();
        }

        public override void updateWorldMatrix(bool updateParents, bool updateChildren)
        {
            base.updateWorldMatrix(updateParents, updateChildren);
            matrixWorldInverse.copy(matrixWorld).invert();
        }
    }

    public class PerspectiveCamera : Camera
    {
        public override string type => "PerspectiveCamera";
        public double fov = 50, aspect = 1, near = 0.1, far = 2000, zoom = 1;

        public PerspectiveCamera(double fov = 50, double aspect = 1, double near = 0.1, double far = 2000)
        {
            this.fov = fov;
            this.aspect = aspect;
            this.near = near;
            this.far = far;
            updateProjectionMatrix();
        }

        public void updateProjectionMatrix()
        {
            double top = near * Math.Tan(MathUtils.DEG2RAD * 0.5 * fov) / zoom;
            double height = 2 * top, width = aspect * height, left = -0.5 * width;
            projectionMatrix.makePerspective(left, left + width, top, top - height, near, far);
            projectionMatrixInverse.copy(projectionMatrix).invert();
        }

        public override Object3D clone(bool recursive = true)
        {
            var c = new PerspectiveCamera(fov, aspect, near, far) { zoom = zoom };
            c.copy(this, recursive);
            return c;
        }
    }

    public class OrthographicCamera : Camera
    {
        public override string type => "OrthographicCamera";
        public double left, right, top, bottom, near, far, zoom = 1;

        public OrthographicCamera(double left = -1, double right = 1, double top = 1, double bottom = -1, double near = 0.1, double far = 2000)
        {
            this.left = left; this.right = right; this.top = top; this.bottom = bottom; this.near = near; this.far = far;
            updateProjectionMatrix();
        }

        public void updateProjectionMatrix()
        {
            double dx = (right - left) / (2 * zoom), dy = (top - bottom) / (2 * zoom);
            double cx = (right + left) / 2, cy = (top + bottom) / 2;
            projectionMatrix.makeOrthographic(cx - dx, cx + dx, cy + dy, cy - dy, near, far);
            projectionMatrixInverse.copy(projectionMatrix).invert();
        }
    }

    public class LightShadow
    {
        public Camera camera;
        public double bias, normalBias, radius = 1;
        public Vector2 mapSize = new Vector2(512, 512);
        public bool autoUpdate = true, needsUpdate;
        public int blurSamples = 8;
        public LightShadow(Camera c) { camera = c; }
    }

    public class Light : Object3D
    {
        public override string type => "Light";
        public override bool isLight => true;
        public Color color;
        public double intensity;
        public LightShadow shadow;

        public Light(int color, double intensity = 1) { this.color = new Color(color); this.intensity = intensity; }
        public Light(Color color, double intensity = 1) { this.color = color.clone(); this.intensity = intensity; }
        public void dispose() { }
    }

    public class AmbientLight : Light
    {
        public override string type => "AmbientLight";
        public AmbientLight(int color = 0xffffff, double intensity = 1) : base(color, intensity) { }
        public AmbientLight(Color color, double intensity = 1) : base(color, intensity) { }
    }

    public class HemisphereLight : Light
    {
        public override string type => "HemisphereLight";
        public Color groundColor;

        public HemisphereLight(int sky = 0xffffff, int ground = 0xffffff, double intensity = 1) : base(sky, intensity)
        {
            groundColor = new Color(ground);
            position.set(0, 1, 0);
            updateMatrix();
        }

        public HemisphereLight(Color sky, Color ground, double intensity = 1) : base(sky, intensity)
        {
            groundColor = ground.clone();
            position.set(0, 1, 0);
            updateMatrix();
        }
    }

    public class DirectionalLight : Light
    {
        public override string type => "DirectionalLight";
        public Object3D target = new Object3D();

        public DirectionalLight(int color = 0xffffff, double intensity = 1) : base(color, intensity) { Init(); }
        public DirectionalLight(Color color, double intensity = 1) : base(color, intensity) { Init(); }

        private void Init()
        {
            position.set(0, 1, 0);
            updateMatrix();
            shadow = new LightShadow(new OrthographicCamera(-5, 5, 5, -5, 0.5, 500));
        }
    }

    public class PointLight : Light
    {
        public override string type => "PointLight";
        public double distance, decay;

        public PointLight(int color = 0xffffff, double intensity = 1, double distance = 0, double decay = 2) : base(color, intensity)
        {
            this.distance = distance;
            this.decay = decay;
            shadow = new LightShadow(new PerspectiveCamera(90, 1, 0.5, 500));
        }

        public PointLight(Color color, double intensity = 1, double distance = 0, double decay = 2) : base(color, intensity)
        {
            this.distance = distance;
            this.decay = decay;
            shadow = new LightShadow(new PerspectiveCamera(90, 1, 0.5, 500));
        }

        public double power { get => intensity * 4 * Math.PI; set => intensity = value / (4 * Math.PI); }
    }

    public class SpotLight : Light
    {
        public override string type => "SpotLight";
        public Object3D target = new Object3D();
        public double distance, angle, penumbra, decay;

        public SpotLight(int color = 0xffffff, double intensity = 1, double distance = 0, double angle = Math.PI / 3, double penumbra = 0, double decay = 2)
            : base(color, intensity)
        {
            this.distance = distance;
            this.angle = angle;
            this.penumbra = penumbra;
            this.decay = decay;
            position.set(0, 1, 0);
            updateMatrix();
            shadow = new LightShadow(new PerspectiveCamera(50, 1, 0.5, 500));
        }
    }
}
