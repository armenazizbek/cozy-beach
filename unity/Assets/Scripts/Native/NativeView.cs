using System.Collections.Generic;
using UnityEngine;
using B = THREE.Bridge;

/// <summary>
/// The main world's frame without the bridge's scene copy (unity/NATIVE_PLAN.md S8). Everything in the world draws itself
/// natively; what is left for the frame is:
/// <list type="bullet">
/// <item>the Cozy shaders' globals (fog, tone mapping, the hemisphere and ambient light and the environment from the scene's
/// <see cref="Atmosphere"/>, the screen) and RenderSettings.sun;</item>
/// <item>the camera: a rig under the world root at the camera's local pose (what hangs from the camera, the tools in hand,
/// hangs here) and a Unity camera on an eye outside the root, placed in Unity's axes.</item>
/// </list>
/// The renderer hands every scene here (WebGLRenderer.render); the cleaning bench and the item photos also get their parts
/// and lights kept in line (SceneParts). Since S25 there is no scene copy at all.
/// </summary>
public static class NativeView
{
    private sealed class Rig
    {
        public Transform rig;
        public Camera cam;
    }

    private static readonly int _CozyFogColor = Shader.PropertyToID("_CozyFogColor"), _CozyFogParams = Shader.PropertyToID("_CozyFogParams"),
        _CozyTone = Shader.PropertyToID("_CozyTone"), _CozyHemiSky = Shader.PropertyToID("_CozyHemiSky"), _CozyHemiGround = Shader.PropertyToID("_CozyHemiGround"),
        _CozyHemiUp = Shader.PropertyToID("_CozyHemiUp"), _CozyAmbient = Shader.PropertyToID("_CozyAmbient"), _CozyEnv = Shader.PropertyToID("_CozyEnv"),
        _CozyScreen = Shader.PropertyToID("_CozyScreen"), _CozyThreeZ = Shader.PropertyToID("_CozyThreeZ");

    /// <summary>The environment light standing in for Three's RoomEnvironment: average radiance, how much brighter above, intensity.</summary>
    public static Vector4 EnvironmentTuning = new Vector4(1.8f, 0.45f, 1f, 0f);

    private static readonly Dictionary<THREE.Camera, Rig> rigs = new Dictionary<THREE.Camera, Rig>();

    /// <summary>Forgets the cameras (a reload destroys their GameObjects with the scene).</summary>
    internal static void Clear()
    {
        rigs.Clear();
        scenes.Clear();
    }

    /// <summary>The Unity camera made for a Three camera, or null.</summary>
    public static Camera CameraFor(THREE.Camera camera) => camera != null && rigs.TryGetValue(camera, out var r) && r.cam != null ? r.cam : null;

    /// <summary>Gets a scene's frame ready (globals, camera pose) and returns the Unity camera that draws it.</summary>
    internal static Camera Prepare(THREE.Scene scene, THREE.Camera camera, THREE.WebGLRenderer renderer)
    {
        // the smaller scenes (the cleaning bench, the item photos) keep their parts and lights in line here (SceneParts)
        if (!ReferenceEquals(scene, World.Scene)) PartsOf(scene).Sync();
        Globals(scene, renderer);
        var r = RigFor(camera, scene, renderer);
        // the rig stands where the camera stands in the scene (its local pose under the world root)
        camera.updateMatrixWorld(true);
        Realizer.Pose(camera, r.rig);
        // what hangs from the camera (the tools in hand, a claimed native tree) hangs from the rig
        foreach (var c in camera.children)
            if (c.native != null && !ReferenceEquals(c.native.parent, r.rig)) c.native.SetParent(r.rig, false);
        // the eye, in Unity's axes (the camera itself lives outside the mirrored root)
        var p = new THREE.Vector3(); var q = new THREE.Quaternion(); var s = new THREE.Vector3();
        camera.matrixWorld.decompose(p, q, s);
        r.cam.transform.SetPositionAndRotation(p.ToUnity(), q.ToUnity());
        return r.cam;
    }

    private static Rig RigFor(THREE.Camera camera, THREE.Scene scene, THREE.WebGLRenderer renderer)
    {
        if (rigs.TryGetValue(camera, out var r) && r.rig != null && r.cam != null) return r;
        r = new Rig { rig = Draw.Group(World.RootOf(scene), camera.name) };
        r.rig.name = Draw.Label(camera.name, camera.type);
        var eye = new GameObject("Camera eye (" + scene.name + ")");
        r.cam = eye.AddComponent<Camera>();
        r.cam.allowHDR = false;
        r.cam.allowMSAA = true;
        r.cam.useOcclusionCulling = false;
        B.UrpSettings.SetupCamera(r.cam);
        if (ReferenceEquals(renderer, THREE.WebGLRenderer.Main) && Object.FindAnyObjectByType<AudioListener>() == null) eye.AddComponent<AudioListener>();
        rigs[camera] = r;
        return r;
    }

    /// <summary>The Cozy shaders' globals for a scene, as the bridge set them (as the bridge's scene copy did), from its Atmosphere.</summary>
    private static void Globals(THREE.Scene scene, THREE.WebGLRenderer renderer)
    {
        var at = Atmosphere.Of(scene);
        Shader.SetGlobalFloat(_CozyThreeZ, 1f);
        Shader.SetGlobalVector(_CozyHemiSky, (Vector4)at.hemiSky);
        Shader.SetGlobalVector(_CozyHemiGround, (Vector4)at.hemiGround);
        Shader.SetGlobalVector(_CozyHemiUp, at.hemiUp.HasValue ? GameSpace.ToUnity(at.hemiUp.Value) : Vector3.up);
        Shader.SetGlobalVector(_CozyAmbient, (Vector4)at.ambient);
        var env = EnvironmentTuning;
        env.z = scene.environment != null ? (float)scene.environmentIntensity : 0f;
        Shader.SetGlobalVector(_CozyEnv, env);
        if (scene.fog is THREE.FogExp2 f2)
        {
            var c = f2.color.ToUnitySRGB();
            Shader.SetGlobalVector(_CozyFogColor, new Vector4(c.r, c.g, c.b, 1));
            Shader.SetGlobalVector(_CozyFogParams, new Vector4(0, 0, (float)f2.density, 2));
        }
        else if (scene.fog != null)
        {
            var c = scene.fog.color.ToUnitySRGB();
            Shader.SetGlobalVector(_CozyFogColor, new Vector4(c.r, c.g, c.b, 1));
            Shader.SetGlobalVector(_CozyFogParams, new Vector4((float)scene.fog.near, (float)scene.fog.far, 0, 1));
        }
        else Shader.SetGlobalVector(_CozyFogParams, Vector4.zero);
        Shader.SetGlobalVector(_CozyTone, new Vector4(renderer.toneMapping == THREE.ToneMapping.ACESFilmicToneMapping ? 1 : 0, (float)renderer.toneMappingExposure, 0, 0));
        Shader.SetGlobalVector(_CozyScreen, new Vector4(Screen.width, Screen.height, (float)renderer.getPixelRatio(), 0));
        RenderSettings.sun = at.sun;
    }

    private static readonly Dictionary<THREE.Scene, SceneParts> scenes = new Dictionary<THREE.Scene, SceneParts>();

    private static SceneParts PartsOf(THREE.Scene scene)
    {
        if (!scenes.TryGetValue(scene, out var p)) scenes[scene] = p = new SceneParts(scene);
        return p;
    }

    /// <summary>
    /// A small scene drawn natively as a whole (S25): each part hung straight in it that nobody drew yet becomes a claimed
    /// LiveTree (a find set up for its photo, the bench's rim light), its sun is a Unity light outside the mirrored root,
    /// and its ambient and hemisphere lights go into its Atmosphere, all as the bridge's scene copy did .
    /// </summary>
    private sealed class SceneParts
    {
        private readonly THREE.Scene scene;
        private readonly Dictionary<THREE.Object3D, LiveTree> trees = new Dictionary<THREE.Object3D, LiveTree>();
        private readonly Dictionary<THREE.DirectionalLight, Light> suns = new Dictionary<THREE.DirectionalLight, Light>();
        private readonly List<THREE.Object3D> gone = new List<THREE.Object3D>();

        public SceneParts(THREE.Scene scene) { this.scene = scene; }

        public void Sync()
        {
            // world positions for the lights (the bridge brought the scene's matrices up to date before copying it)
            scene.updateMatrixWorld();
            var at = Atmosphere.Of(scene);
            at.ClearLights();
            at.sun = null;
            double sunPower = -1;
            int layer = scene.unityLayer;
            foreach (var c in scene.children)
            {
                switch (c)
                {
                    case THREE.HemisphereLight h:
                        if (!h.visible) break;
                        var up = new THREE.Vector3();
                        h.getWorldPosition(up);
                        at.AddHemisphere(new Color((float)h.color.r, (float)h.color.g, (float)h.color.b), new Color((float)h.groundColor.r, (float)h.groundColor.g, (float)h.groundColor.b),
                            (float)h.intensity, new Vector3((float)up.x, (float)up.y, (float)up.z));
                        break;
                    case THREE.AmbientLight a:
                        if (a.visible) at.AddAmbient(new Color((float)a.color.r, (float)a.color.g, (float)a.color.b), (float)a.intensity);
                        break;
                    case THREE.DirectionalLight d:
                    {
                        if (!suns.TryGetValue(d, out var l) || l == null) suns[d] = l = Lights.Make(LightType.Directional, null, d.name);
                        if (l.gameObject.activeSelf != d.visible) l.gameObject.SetActive(d.visible);
                        if (!d.visible) break;
                        var from = new THREE.Vector3(); var to = new THREE.Vector3();
                        d.getWorldPosition(from);
                        d.target.updateWorldMatrix(true, false);
                        d.target.getWorldPosition(to);
                        Lights.Aim(l, new Vector3((float)from.x, (float)from.y, (float)from.z), new Vector3((float)to.x, (float)to.y, (float)to.z));
                        Lights.Set(l, new Color((float)d.color.r, (float)d.color.g, (float)d.color.b), (float)d.intensity, layer);
                        Lights.Shadows(l, d.castShadow);
                        // the scene's sun, chosen as the bridge chose it
                        if (d.castShadow && d.intensity > sunPower) { at.sun = l; sunPower = d.intensity; }
                        else if (at.sun == null && d.intensity > sunPower) { at.sun = l; sunPower = d.intensity; }
                        break;
                    }
                    case THREE.Camera _:
                        break;
                    default:
                        // a part some module already draws natively (the bench's claimed trees) is left to it
                        if (c.native != null && !trees.ContainsKey(c)) break;
                        if (!trees.TryGetValue(c, out var t)) trees[c] = t = new LiveTree(c, World.RootOf(scene), true, layer);
                        else t.Sync();
                        break;
                }
            }
            // parts that left the scene (the previous find after its photo) go with their GameObjects
            gone.Clear();
            foreach (var kv in trees) if (!ReferenceEquals(kv.Key.parent, scene)) gone.Add(kv.Key);
            foreach (var o in gone)
            {
                trees[o].Remove();
                trees.Remove(o);
            }
            foreach (var kv in suns)
                if (!ReferenceEquals(kv.Key.parent, scene) && kv.Value != null && kv.Value.gameObject.activeSelf) kv.Value.gameObject.SetActive(false);
        }
    }
}
