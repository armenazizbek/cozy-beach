using THREE;
// inside THREE these names meant its own types; kept so after the move to NativeRender
using Color = THREE.Color;
using Vector2 = THREE.Vector2;
using Vector3 = THREE.Vector3;
using Vector4 = THREE.Vector4;
using Quaternion = THREE.Quaternion;
using Camera = THREE.Camera;
using Texture = THREE.Texture;
using Material = THREE.Material;
using Mesh = THREE.Mesh;
using Light = THREE.Light;
using Sprite = THREE.Sprite;
using Plane = THREE.Plane;
using Ray = THREE.Ray;
using ColorSpace = THREE.ColorSpace;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using U = UnityEngine;

namespace NativeRender
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

    /// <summary>
    /// Draws the game's scenes with Unity (unity/UI_PLAN.md B2): each scene through its native view (NativeView) and a Unity
    /// camera, to the screen or, off screen, into a texture of its own (the item photos); the render settings (scale,
    /// shadows) go to URP; photos are read back from the GPU without a stall. The host calls Tick once a frame with the
    /// game's frame to run.
    /// </summary>
    public class GameRenderer
    {
        public ShadowMapSettings shadowMap = new ShadowMapSettings();
        public ToneMapping toneMapping = ToneMapping.NoToneMapping;
        public double toneMappingExposure = 1;
        public bool autoClear = true;
        public readonly Dictionary<string, object> info = new Dictionary<string, object>();
        private double pixelRatio = 1;
        private int width, height;
        private readonly bool offscreen;
        private WebGLRenderTarget target;
        private U.RenderTexture own;
        private Color clearColor = new Color(0x000000);
        private double clearAlpha = 1;
        private readonly HashSet<U.Camera> usedThisFrame = new HashSet<U.Camera>();
        private readonly HashSet<U.Camera> known = new HashSet<U.Camera>();
        private int screenRenders;
        private static int frames;

        /// <summary>Whichever renderer draws to the screen (the bridge drives its frame).</summary>
        public static GameRenderer Main { get; private set; }

        /// <summary>
        /// Forgets the whole running game before its scene is reloaded (Load Game, New Game, Main Menu). In the browser a page
        /// reload wiped everything; here the old renderer kept driving the old game's frame inside the new scene, and the caches
        /// kept Unity meshes, materials and objects of the destroyed scene, which froze or closed the game.
        /// </summary>
        public static void Teardown()
        {
            Main = null;
            World.Clear();
            MaterialCache.Clear();
            TextureCache.Clear();
            Atmosphere.Clear();
            NativeFrame.Clear();
            NativeView.Clear();
            Picking.Clear();
        }

        /// <param name="onScreen">true for the game's own renderer, which draws to the screen; false draws into a texture</param>
        public GameRenderer(bool onScreen)
        {
            offscreen = !onScreen;
            width = Screen.width;
            height = Screen.height;
            if (!offscreen) Main = this;
        }

        internal int Width => offscreen ? width : Screen.width;
        internal int Height => offscreen ? height : Screen.height;

        public void setPixelRatio(double r)
        {
            pixelRatio = r;
            // r is picture pixels per pixel of the UI's 1920x1080 page, which is stretched to the screen by
            // window.devicePixelRatio; URP's render scale is per screen pixel, so the stretch is taken out. Handing r over
            // as it was rendered a 1440p screen at 1.33x and a 4K one at up to 2x, and a 720p one at 0.67x.
            if (!offscreen) UrpSettings.SetRenderScale(r / Math.Max(0.01, Page.PixelRatio));
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


        public void setRenderTarget(WebGLRenderTarget rt) => target = rt;

        /// <summary>Called by the host each Unity frame: runs the game's frame (null before the game has started), then retires cameras that were not used.</summary>
        public void Tick(Action frame)
        {
            frames++;
            screenRenders = 0;
            usedThisFrame.Clear();
            long t0 = FrameStats.Ticks();
            frame?.Invoke();
            FrameStats.tickMs += FrameStats.Ms(t0);
            FrameStats.frames++;
            foreach (var c in known)
            {
                if (c == null) continue;
                bool on = usedThisFrame.Contains(c);
                if (c.enabled != on) c.enabled = on;
            }
        }

        public void dispose()
        {
            if (own != null)
            {
                own.Release();
                U.Object.Destroy(own);
                own = null;
            }
        }

        public void render(Object3D sceneObj, Camera camera)
        {
            var scene = sceneObj as Scene;
            if (scene == null)
            {
                // a bare group: draw it on its own in a scene of its own
                scene = new Scene();
                scene.add(sceneObj);
            }
            // native objects finish their frame (NativeFrame) before anything is drawn
            NativeFrame.Run();
            long t0 = FrameStats.Ticks();
            // every scene draws itself natively (Native/NativeView.cs): no scene copy, only the frame's globals and camera
            U.Camera cam;
            using (FrameStats.Time("draw:view")) cam = NativeView.Prepare(scene, camera, this);
            known.Add(cam);
            FrameStats.syncMs += FrameStats.Ms(t0);
            ConfigureCamera(cam, camera, scene);
            UrpSettings.SetShadows(shadowMap.enabled);
            if (target != null || offscreen)
            {
                var rt = target != null ? target.Ensure() : Own();
                cam.enabled = false;
                cam.targetTexture = null;
                UrpSettings.RenderTo(cam, rt);
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

        /// <summary>
        /// Takes a photograph through a camera that was drawn this frame: the world only, no page on top. It renders at the
        /// screen's shape and keeps the middle 4:3 of it, the part the camera's viewfinder frames. The pixels come back
        /// from the GPU a frame or two later (AsyncGPUReadback), so the game does not stop and wait for them; `done` gets the
        /// photo then, or null when it could not be taken. In the smoke tests it waits at once, so a run is the same each time.
        /// </summary>
        internal void CapturePhoto(Camera camera, Action<Texture2D> done, int height = 900, Color32? tint = null, float tintAmount = 0)
        {
            var cam = NativeView.CameraFor(camera);
            if (cam == null)
            {
                done(null);
                return;
            }
            float aspect = (float)Math.Max(0.5, Math.Min(4, camera is PerspectiveCamera pc ? pc.aspect : 16.0 / 9));
            int h = height;
            int w = Mathf.RoundToInt(h * aspect);
            int cw = Math.Min(w, Mathf.RoundToInt(h * 4f / 3f));
            int ch = Math.Min(h, Mathf.RoundToInt(cw * 3f / 4f));
            var desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 24) { sRGB = true, msaaSamples = 4 };
            var rt = U.RenderTexture.GetTemporary(desc);
            U.RenderTexture resolved = null;
            var prevTarget = cam.targetTexture;
            try
            {
                cam.aspect = aspect;
                UrpSettings.RenderTo(cam, rt);
                // the multisampled picture resolved into a plain one, which the GPU can hand back
                resolved = U.RenderTexture.GetTemporary(new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 0) { sRGB = true, msaaSamples = 1 });
                Graphics.Blit(rt, resolved);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not take a photo: " + e.Message);
                if (resolved != null) U.RenderTexture.ReleaseTemporary(resolved);
                done(null);
                return;
            }
            finally
            {
                cam.targetTexture = prevTarget;
                cam.ResetAspect();
                U.RenderTexture.ReleaseTemporary(rt);
            }
            bool finished = false;
            void finish(AsyncGPUReadbackRequest r)
            {
                if (finished) return;
                finished = true;
                U.RenderTexture.ReleaseTemporary(resolved);
                if (r.hasError)
                {
                    Debug.LogWarning("Could not take a photo: the picture did not come back from the GPU");
                    done(null);
                    return;
                }
                var src = r.GetData<byte>().ToArray();
                var px = new byte[cw * ch * 3];
                for (int i = 0, o = 0; o < px.Length; i += 4, o += 3)
                {
                    px[o] = src[i];
                    px[o + 1] = src[i + 1];
                    px[o + 2] = src[i + 2];
                }
                if (tint.HasValue && tintAmount > 0)
                {
                    // a colour the page lays over the view (the blue of being underwater), mixed in the way the page mixes it
                    int keep = Mathf.RoundToInt((1 - tintAmount) * 256), add = 256 - keep;
                    int tr = tint.Value.r * add, tg = tint.Value.g * add, tb = tint.Value.b * add;
                    for (int i = 0; i + 2 < px.Length; i += 3)
                    {
                        px[i] = (byte)((px[i] * keep + tr) >> 8);
                        px[i + 1] = (byte)((px[i + 1] * keep + tg) >> 8);
                        px[i + 2] = (byte)((px[i + 2] * keep + tb) >> 8);
                    }
                }
                var photo = new Texture2D(cw, ch, TextureFormat.RGB24, false, false) { wrapMode = TextureWrapMode.Clamp };
                photo.LoadRawTextureData(px);
                photo.Apply();
                done(photo);
            }
            var req = AsyncGPUReadback.Request(resolved, 0, (w - cw) / 2, cw, (h - ch) / 2, ch, 0, 1, TextureFormat.RGBA32, finish);
            if (TestClock.Fixed)
            {
                req.WaitForCompletion();
                finish(req);
            }
        }

        /// <summary>A copy of the last frame this renderer drew (the item photos read their pictures with it).</summary>
        public U.Texture Snapshot()
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
