using UnityEngine;

/// <summary>
/// The root everything in the main world hangs under (unity/NATIVE_PLAN.md 2.1): a GameObject scaled (1, 1, -1), so the game's
/// right-handed numbers are written into local transforms unchanged (see <see cref="GameSpace"/>). Every scene (the world,
/// the cleaning bench, the item photos) has one (<see cref="RootOf"/>).
/// </summary>
public static class World
{
    private static THREE.Scene main;

    /// <summary>The main world's description (the Three scene the game builds; native objects are made from it).</summary>
    public static THREE.Scene Scene => main;

    /// <summary>The Unity layer the main world is drawn on (its camera draws only this layer).</summary>
    public static int Layer => main == null ? 0 : main.unityLayer;

    /// <summary>
    /// The layer an object hung under `parent` belongs on: the parent's, or the world's for objects hung straight under a
    /// scene's root (roots themselves keep the default layer).
    /// </summary>
    public static int LayerUnder(Transform parent)
    {
        if (parent == null) return Layer;
        if (main != null && ReferenceEquals(parent, Root)) return Layer;
        return parent.parent == null ? Layer : parent.gameObject.layer;
    }

    private static Transform land;

    /// <summary>
    /// Where the native world above the sea floor hangs (under the root): everything the game hides while diving
    /// (Game.hideLand switches it off as one). Its own children keep their own visibility, so a module that shows itself
    /// every frame cannot undo the dive.
    /// </summary>
    public static Transform Land
    {
        get
        {
            if (land == null && main != null) land = Draw.Group(Root, "Land");
            return land;
        }
    }

    /// <summary>The main world's root; null before the game is built.</summary>
    public static Transform Root => main == null ? null : RootOf(main);

    private static readonly System.Collections.Generic.Dictionary<THREE.Scene, GameObject> roots = new System.Collections.Generic.Dictionary<THREE.Scene, GameObject>();

    /// <summary>The root of one of the game's scenes (the world, the cleaning bench, the item photos), scaled (1, 1, -1).</summary>
    public static Transform RootOf(THREE.Scene scene)
    {
        if (!roots.TryGetValue(scene, out var go) || go == null)
        {
            go = new GameObject(string.IsNullOrEmpty(scene.name) ? "Scene" : scene.name);
            go.transform.localScale = new Vector3(1, 1, -1);
            roots[scene] = go;
        }
        return go.transform;
    }

    /// <summary>Forgets the scene roots (a reload destroys them with the scene).</summary>
    internal static void Clear() => roots.Clear();

    /// <summary>Called once by Game when its scene exists.</summary>
    internal static void Bind(THREE.Scene scene)
    {
        main = scene;
        land = null;
    }

}
