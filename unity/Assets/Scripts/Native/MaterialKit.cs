using UnityEngine;
using B = THREE.Render;

/// <summary>
/// Unity materials from the game's material descriptions (unity/NATIVE_PLAN.md 2.4, S4). The descriptions are the plain data
/// classes the game always wrote (MeshStandardMaterial { color = 0x8a6a46, roughness = 1 }, ...; they move under MaterialKit
/// by a rename in S25), and a description becomes a Unity material on the Cozy shaders by exactly the rules the bridge used
/// (cull and shadow cull from side, blending, queue, flags, colours turned linear the Three way). Native objects and bridge
/// objects drawn with the same description share one Unity material.
///
/// The bridge re-checked every material every frame. Native code says when a description changed instead: after changing a
/// colour, opacity, glow or any other field of a description that is on screen, call <see cref="Push"/>.
/// </summary>
public static class MaterialKit
{
    /// <summary>What a material is drawn on (sprites, points and lines get their own shaders and never cull).</summary>
    public enum On { Mesh, Points, Sprite, Line, Instanced }

    /// <summary>The Unity material for a description; made the first time, shared after that.</summary>
    public static Material Get(THREE.Material m, On on = On.Mesh) => m == null ? null : B.MaterialCache.Get(m, (B.MaterialCache.Kind)on);

    /// <summary>Brings the Unity material up to date with its description (call after changing the description's fields).</summary>
    public static Material Push(THREE.Material m, On on = On.Mesh) => m == null ? null : B.MaterialCache.Get(m, (B.MaterialCache.Kind)on, force: true);

    /// <summary>
    /// A description is drawn only while it is visible (Three's material.visible): hit spheres use invisible materials, and
    /// get no renderer at all.
    /// </summary>
    public static bool Draws(THREE.Material m) => m != null && m.visible;
}
