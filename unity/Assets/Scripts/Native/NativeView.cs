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
/// The renderer hands the main scene here instead of to its scene copy (WebGLRenderer.render); the cleaning bench and the
/// item photos still go through the bridge until S25.
/// </summary>
public static class NativeView
{
    private sealed class Rig
    {
        public Transform rig;
        public Camera cam;
    }

    private static readonly Dictionary<THREE.Camera, Rig> rigs = new Dictionary<THREE.Camera, Rig>();

    /// <summary>True for the scenes drawn natively (the main world).</summary>
    public static bool Owns(THREE.Scene scene) => scene != null && ReferenceEquals(scene, World.Scene);

    /// <summary>Forgets the cameras (a reload destroys their GameObjects with the scene).</summary>
    internal static void Clear() => rigs.Clear();

    /// <summary>The Unity camera made for a Three camera, or null.</summary>
    public static Camera CameraFor(THREE.Camera camera) => camera != null && rigs.TryGetValue(camera, out var r) && r.cam != null ? r.cam : null;

    /// <summary>Gets a scene's frame ready (globals, camera pose) and returns the Unity camera that draws it.</summary>
    internal static Camera Prepare(THREE.Scene scene, THREE.Camera camera, THREE.WebGLRenderer renderer)
    {
        Globals(scene, renderer);
        var r = RigFor(camera, scene);
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

    private static Rig RigFor(THREE.Camera camera, THREE.Scene scene)
    {
        if (rigs.TryGetValue(camera, out var r) && r.rig != null && r.cam != null) return r;
        r = new Rig { rig = Draw.Group(World.Root, camera.name) };
        r.rig.name = Draw.Label(camera.name, camera.type);
        var eye = new GameObject("Camera eye (" + scene.name + ")");
        r.cam = eye.AddComponent<Camera>();
        r.cam.allowHDR = false;
        r.cam.allowMSAA = true;
        r.cam.useOcclusionCulling = false;
        B.UrpSettings.SetupCamera(r.cam);
        if (Object.FindAnyObjectByType<AudioListener>() == null) eye.AddComponent<AudioListener>();
        rigs[camera] = r;
        return r;
    }

    /// <summary>The Cozy shaders' globals for a scene, as the bridge set them (SceneSync.ApplyGlobals), from its Atmosphere.</summary>
    private static void Globals(THREE.Scene scene, THREE.WebGLRenderer renderer)
    {
        var at = Atmosphere.Of(scene);
        Shader.SetGlobalFloat(B.SceneSync._CozyThreeZ, 1f);
        Shader.SetGlobalVector(B.SceneSync._CozyHemiSky, (Vector4)at.hemiSky);
        Shader.SetGlobalVector(B.SceneSync._CozyHemiGround, (Vector4)at.hemiGround);
        Shader.SetGlobalVector(B.SceneSync._CozyHemiUp, at.hemiUp.HasValue ? GameSpace.ToUnity(at.hemiUp.Value) : Vector3.up);
        Shader.SetGlobalVector(B.SceneSync._CozyAmbient, (Vector4)at.ambient);
        var env = B.SceneSync.EnvironmentTuning;
        env.z = scene.environment != null ? (float)scene.environmentIntensity : 0f;
        Shader.SetGlobalVector(B.SceneSync._CozyEnv, env);
        if (scene.fog is THREE.FogExp2 f2)
        {
            var c = f2.color.ToUnitySRGB();
            Shader.SetGlobalVector(B.SceneSync._CozyFogColor, new Vector4(c.r, c.g, c.b, 1));
            Shader.SetGlobalVector(B.SceneSync._CozyFogParams, new Vector4(0, 0, (float)f2.density, 2));
        }
        else if (scene.fog != null)
        {
            var c = scene.fog.color.ToUnitySRGB();
            Shader.SetGlobalVector(B.SceneSync._CozyFogColor, new Vector4(c.r, c.g, c.b, 1));
            Shader.SetGlobalVector(B.SceneSync._CozyFogParams, new Vector4((float)scene.fog.near, (float)scene.fog.far, 0, 1));
        }
        else Shader.SetGlobalVector(B.SceneSync._CozyFogParams, Vector4.zero);
        Shader.SetGlobalVector(B.SceneSync._CozyTone, new Vector4(renderer.toneMapping == THREE.ToneMapping.ACESFilmicToneMapping ? 1 : 0, (float)renderer.toneMappingExposure, 0, 0));
        Shader.SetGlobalVector(B.SceneSync._CozyScreen, new Vector4(Screen.width, Screen.height, (float)renderer.getPixelRatio(), 0));
        RenderSettings.sun = at.sun;
    }
}
