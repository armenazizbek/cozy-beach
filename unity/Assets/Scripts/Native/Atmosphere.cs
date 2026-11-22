using UnityEngine;

/// <summary>
/// The light and air the Cozy shaders read as globals (unity/NATIVE_PLAN.md 2.5, S4): the hemisphere and ambient light (not
/// Unity lights at all), the stand-in for Three's room environment, fog mixed in sRGB, and tone mapping. Native code adds its
/// share here; while the Three bridge still draws part of a scene it adds the Three lights it finds and writes the sum
/// (SceneSync.ApplyGlobals), so both kinds of light count and neither overwrites the other.
/// Colours are linear and already times intensity, as the bridge summed them. Directions are in game space.
/// </summary>
public class Atmosphere
{
    private static readonly System.Collections.Generic.Dictionary<THREE.Scene, Atmosphere> all = new System.Collections.Generic.Dictionary<THREE.Scene, Atmosphere>();

    /// <summary>The atmosphere of a scene (the world, the cleaning bench, the item photos).</summary>
    public static Atmosphere Of(THREE.Scene scene)
    {
        if (!all.TryGetValue(scene, out var a)) all[scene] = a = new Atmosphere();
        return a;
    }

    /// <summary>Forgets every scene's atmosphere (a reload).</summary>
    public static void Clear() => all.Clear();

    // native hemisphere and ambient light, summed like Three's lights
    public Vector3 hemiSky, hemiGround;
    /// <summary>the hemisphere light's up, game space (Three's light position, normalised); null leaves the bridge's</summary>
    public Vector3? hemiUp;
    public Vector3 ambient;

    /// <summary>A native sun for RenderSettings.sun when the bridge has none.</summary>
    public Light sun;

    /// <summary>Adds a hemisphere light (Three HemisphereLight: sky colour, ground colour, both linear, and its intensity).</summary>
    public void AddHemisphere(Color skyLinear, Color groundLinear, float intensity, Vector3 upGame)
    {
        hemiSky += new Vector3(skyLinear.r, skyLinear.g, skyLinear.b) * intensity;
        hemiGround += new Vector3(groundLinear.r, groundLinear.g, groundLinear.b) * intensity;
        hemiUp = upGame.normalized;
    }

    public void AddAmbient(Color linear, float intensity) => ambient += new Vector3(linear.r, linear.g, linear.b) * intensity;

    /// <summary>Starts a frame's native share from nothing (native lights add themselves again each frame they shine).</summary>
    public void ClearLights()
    {
        hemiSky = hemiGround = ambient = Vector3.zero;
        hemiUp = null;
    }
}

/// <summary>Native Unity lights with the settings the bridge gave the lights it made (SceneSync.EnsureLight, SetLightCommon).</summary>
public static class Lights
{
    /// <summary>A light on its own GameObject; `parent` null for lights outside the world root (the sun, spot lights).</summary>
    public static Light Make(LightType type, Transform parent = null, string name = null)
    {
        var go = new GameObject(Draw.Label(name, type == LightType.Directional ? "DirectionalLight" : type == LightType.Spot ? "SpotLight" : "PointLight"));
        if (parent != null) go.transform.SetParent(parent, false);
        var l = go.AddComponent<Light>();
#if UNITY_EDITOR
        l.lightmapBakeType = LightmapBakeType.Realtime;
#endif
        l.renderMode = LightRenderMode.ForcePixel;
        l.shadowBias = 0.02f;
        l.shadowNormalBias = 0.4f;
        l.type = type;
        l.shadows = LightShadows.None;
        return l;
    }

    /// <summary>Colour (linear, as the game keeps it) and Three's intensity, handed over as the bridge did.</summary>
    public static void Set(Light l, Color linear, float intensity, int layer)
    {
        // Unity linearises Light.color, so it gets the sRGB value; the shaders then see colour x intensity in linear, as Three did
        var c = ColorUtil.ToSRGB(linear);
        if (l.color != c) l.color = c;
        if (l.intensity != intensity) l.intensity = intensity;
        l.cullingMask = 1 << layer;
    }

    /// <summary>Shadows on or off, soft or hard as the preset says.</summary>
    public static void Shadows(Light l, bool cast) =>
        l.shadows = cast ? (THREE.Bridge.UrpSettings.SoftShadows ? LightShadows.Soft : LightShadows.Hard) : LightShadows.None;

    /// <summary>
    /// Places a sun or spot light (which live outside the world root) at a game-space position, pointing at a game-space target.
    /// </summary>
    public static void Aim(Light l, Vector3 fromGame, Vector3 toGame)
    {
        l.transform.position = GameSpace.ToUnity(fromGame);
        var dir = GameSpace.ToUnity(toGame - fromGame);
        if (dir.sqrMagnitude > 1e-9f) l.transform.rotation = Quaternion.LookRotation(dir.normalized);
    }

    /// <summary>Range and cone of a spot light the way the bridge set them from Three's distance, angle and penumbra.</summary>
    public static void Cone(Light l, double distance, double angle, double penumbra)
    {
        l.range = distance > 0 ? (float)distance : 1000f;
        float outer = (float)(angle * 2 * Mathf.Rad2Deg);
        l.spotAngle = Mathf.Clamp(outer, 1f, 179f);
        l.innerSpotAngle = Mathf.Clamp((float)(outer * (1 - penumbra)), 0f, l.spotAngle);
    }
}
