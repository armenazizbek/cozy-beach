using UnityEngine;

/// <summary>
/// The root everything in the main world hangs under (unity/NATIVE_PLAN.md 2.1): a GameObject scaled (1, 1, -1), so the game's
/// right-handed numbers are written into local transforms unchanged (see <see cref="GameSpace"/>). While the Three bridge
/// still draws part of the world it is the same GameObject the bridge hangs the main scene under, so native objects can be
/// parented to bridge objects and the other way round with no conversion between them.
/// </summary>
public static class World
{
    private static THREE.Scene main;

    /// <summary>The main world's description (the Three scene the bridge still copies the unconverted part of).</summary>
    public static THREE.Scene Scene => main;

    /// <summary>The main world's root; null before the game is built.</summary>
    public static Transform Root => main == null ? null : RootOf(main);

    /// <summary>The root of one of the bridge's scenes (the world, the cleaning bench, the item photos).</summary>
    public static Transform RootOf(THREE.Scene scene) => THREE.Bridge.SceneSync.RootFor(scene).transform;

    /// <summary>Called once by Game when its scene exists.</summary>
    internal static void Bind(THREE.Scene scene) => main = scene;

    /// <summary>
    /// The GameObject the bridge made for a Three object, to hang native children from (null until the object was first
    /// synced, or after the bridge let it go).
    /// </summary>
    public static Transform Of(THREE.Object3D o) => THREE.Bridge.SceneSync.TransformOf(o);
}
