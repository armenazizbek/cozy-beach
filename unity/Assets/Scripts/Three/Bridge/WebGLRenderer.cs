using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using U = UnityEngine;

namespace THREE
{
    public enum ToneMapping { NoToneMapping = 0, LinearToneMapping = 1, ReinhardToneMapping = 2, CineonToneMapping = 3, ACESFilmicToneMapping = 4, AgXToneMapping = 6, NeutralToneMapping = 7 }

    public enum ShadowMapType { BasicShadowMap = 0, PCFShadowMap = 1, PCFSoftShadowMap = 2, VSMShadowMap = 3 }

    public class ShadowMapSettings
    {
        public bool enabled;
        public ShadowMapType type = ShadowMapType.PCFShadowMap;
        public bool autoUpdate = true;
        public bool needsUpdate;
    }

    public class WebGLRendererParameters
    {
        public object canvas;
        public bool antialias;
        public bool preserveDrawingBuffer;
        public bool alpha;
        public string powerPreference;
    }

    /// <summary>What the game reads from renderer.domElement: its size, and a snapshot for photos.</summary>
    public class RendererCanvas
    {
        internal WebGLRenderer owner;
        public int width => owner.Width;
        public int height => owner.Height;

        /// <summary>A copy of the last frame this renderer drew (in place of a data URL).</summary>
        public U.Texture toDataURL(string type = "image/png", double quality = 1) => owner.Snapshot();
    }

    /// <summary>
    /// The Three.js renderer, standing in front of Unity's: render() mirrors the scene into GameObjects and points a Unity camera at
    /// it. The game's own renderer draws to the screen; one made without a canvas draws into a texture of its own.
    /// </summary>
    public class WebGLRenderer
    {
        public ShadowMapSettings shadowMap = new ShadowMapSettings();
        public ToneMapping toneMapping = ToneMapping.NoToneMapping;
        public double toneMappingExposure = 1;
        public string outputColorSpace = ColorSpace.SRGBColorSpace;
        public bool autoClear = true;
        public readonly RendererCanvas domElement;
        public readonly Dictionary<string, object> info = new Dictionary<string, object>();
        private double pixelRatio = 1;
        private int width, height;
        private readonly bool offscreen;
        private WebGLRenderTarget target;
        private U.RenderTexture own;
        private Color clearColor = new Color(0x000000);
        private double clearAlpha = 1;
        private Action loop;
        private readonly HashSet<U.Camera> usedThisFrame = new HashSet<U.Camera>();
        private readonly HashSet<U.Camera> known = new HashSet<U.Camera>();
        private int screenRenders;
        private static int frames;

        /// <summary>Whichever renderer draws to the screen (the bridge drives its frame).</summary>
        public static WebGLRenderer Main { get; private set; }

        /// <summary>
        /// Forgets the whole running game before its scene is reloaded (Load Game, New Game, Main Menu). In the browser a page
        /// reload wiped everything; here the old renderer kept driving the old game's frame inside the new scene, and the caches
        /// kept Unity meshes, materials and objects of the destroyed scene, which froze or closed the game.
        /// </summary>
        public static void Teardown()
        {
            if (Main != null) Main.loop = null;
            Main = null;
            Bridge.SceneSync.Clear();
            Bridge.MeshCache.Clear();
            Bridge.MaterialCache.Clear();
            Bridge.TextureCache.Clear();
        }

        public WebGLRenderer(WebGLRendererParameters p = null)
        {
            offscreen = p?.canvas == null;
            domElement = new RendererCanvas { owner = this };
            width = Screen.width;
            height = Screen.height;
            if (!offscreen) Main = this;
        }

        internal int Width => offscreen ? width : Screen.width;
        internal int Height => offscreen ? height : Screen.height;

        public void setPixelRatio(double r)
        {
            pixelRatio = r;
            Bridge.UrpSettings.SetRenderScale(r);
        }

        public double getPixelRatio() => pixelRatio;

        public void setSize(int w, int h, bool updateStyle = true)
        {
            width = Math.Max(1, w);
            height = Math.Max(1, h);
            if (own != null && (own.width != width || own.height != height))
            {
                own.Release();
                U.Object.Destroy(own);
                own = null;
            }
        }

        public Vector2 getSize(Vector2 target) => target.set(Width, Height);

        public void setClearColor(Color c, double alpha = 1) { clearColor.copy(c); clearAlpha = alpha; }
        public void setClearColor(int hex, double alpha = 1) { clearColor.setHex(hex); clearAlpha = alpha; }
        public Color getClearColor(Color target) => target.copy(clearColor);

        public void setRenderTarget(WebGLRenderTarget rt) => target = rt;
        public WebGLRenderTarget getRenderTarget() => target;

        public void setAnimationLoop(Action cb) => loop = cb;

        /// <summary>Called by the host each Unity frame: runs the animation loop, then retires cameras that were not used.</summary>
        public void Tick()
        {
            frames++;
            screenRenders = 0;
            usedThisFrame.Clear();
            long t0 = Bridge.BridgeStats.Ticks();
            loop?.Invoke();
            Bridge.BridgeStats.tickMs += Bridge.BridgeStats.Ms(t0);
            Bridge.BridgeStats.frames++;
            foreach (var c in known)
            {
                if (c == null) continue;
                bool on = usedThisFrame.Contains(c);
                if (c.enabled != on) c.enabled = on;
            }
        }

        public Task compileAsync(Object3D scene, Camera camera, Object3D targetScene = null) => Task.CompletedTask;
        public void compile(Object3D scene, Camera camera) { }
        public void clear() { }
        public void dispose()
        {
            if (own != null)
            {
                own.Release();
                U.Object.Destroy(own);
                own = null;
            }
        }
        public void forceContextLoss() { }

        public void render(Object3D sceneObj, Camera camera)
        {
            var scene = sceneObj as Scene;
            if (scene == null)
            {
                // a bare group: draw it on its own in a scene of its own
                scene = new Scene();
                scene.add(sceneObj);
            }
            long t0 = Bridge.BridgeStats.Ticks();
            Bridge.SceneSync.Sync(scene, camera, this);
            Bridge.BridgeStats.syncMs += Bridge.BridgeStats.Ms(t0);
            var cam = EnsureCamera(camera, scene);
            ConfigureCamera(cam, camera, scene);
            Bridge.UrpSettings.SetShadows(shadowMap.enabled);
            if (target != null || offscreen)
            {
                var rt = target != null ? target.Ensure() : Own();
                cam.enabled = false;
                cam.targetTexture = null;
                Bridge.UrpSettings.RenderTo(cam, rt);
                return;
            }
            cam.targetTexture = null;
            cam.depth = screenRenders++;
            if (screenRenders > 1 && !autoClear) cam.clearFlags = CameraClearFlags.Nothing;
            usedThisFrame.Add(cam);
            cam.enabled = true;
        }

        private U.RenderTexture Own()
        {
            if (own == null)
            {
                own = new U.RenderTexture(Math.Max(1, width), Math.Max(1, height), 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                own.Create();
            }
            return own;
        }

        internal U.Texture Snapshot()
        {
            var src = target != null ? target.Ensure() : own;
            if (src == null) return null;
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, false);
            var prev = U.RenderTexture.active;
            U.RenderTexture.active = src;
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            copy.Apply();
            U.RenderTexture.active = prev;
            return copy;
        }

        private U.Camera EnsureCamera(Camera camera, Scene scene)
        {
            var node = camera.node;
            if (node == null)
            {
                // a camera outside the scene graph: give it a GameObject of its own at the top
                var go = new GameObject("Camera");
                node = new Bridge.Node { go = go, tf = go.transform, owner = camera };
                camera.node = node;
            }
            if (camera.parent == null)
            {
                camera.updateMatrixWorld();
                var p = new Vector3();
                var q = new Quaternion();
                var s = new Vector3();
                camera.matrixWorld.decompose(p, q, s);
                node.tf.position = p.ToUnity();
                node.tf.rotation = q.ToUnity();
            }
            if (node.cam == null)
            {
                node.cam = node.go.AddComponent<U.Camera>();
                node.cam.allowHDR = false;
                node.cam.allowMSAA = true;
                node.cam.useOcclusionCulling = false;
                Bridge.UrpSettings.SetupCamera(node.cam);
                if (!offscreen && Main == this && U.Object.FindAnyObjectByType<AudioListener>() == null) node.go.AddComponent<AudioListener>();
                known.Add(node.cam);
            }
            return node.cam;
        }

        private void ConfigureCamera(U.Camera cam, Camera camera, Scene scene)
        {
            if (camera is PerspectiveCamera pc)
            {
                cam.orthographic = false;
                cam.fieldOfView = (float)(2 * Math.Atan(Math.Tan(pc.fov * MathUtils.DEG2RAD / 2) / pc.zoom) * MathUtils.RAD2DEG);
                cam.nearClipPlane = (float)pc.near;
                cam.farClipPlane = (float)pc.far;
                cam.ResetAspect();
                if (offscreen || target != null) cam.aspect = (float)pc.aspect;
            }
            else if (camera is OrthographicCamera oc)
            {
                cam.orthographic = true;
                cam.orthographicSize = (float)((oc.top - oc.bottom) / 2 / oc.zoom);
                cam.nearClipPlane = (float)oc.near;
                cam.farClipPlane = (float)oc.far;
            }
            cam.cullingMask = 1 << scene.unityLayer;
            Color bg = scene.background as Color ?? clearColor;
            var c = bg.ToUnitySRGB(scene.background is Color ? 1 : clearAlpha);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = c;
        }
    }
}
