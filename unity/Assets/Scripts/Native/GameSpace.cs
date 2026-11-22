using System;
using UnityEngine;

/// <summary>
/// The game's own coordinates and Unity's (unity/NATIVE_PLAN.md 2.1, option A). The game keeps Three.js's right-handed numbers:
/// yaw 0 looks toward -z and the sea is at +z. Everything in the world hangs under <see cref="World.Root"/>, which is scaled
/// (1, 1, -1), so local transforms are written in those numbers unchanged. Only what lives outside the root (cameras, the
/// sun, spot lights) and vectors handed to shaders cross into Unity's axes, through the helpers here.
///
/// Rules: inside the world use localPosition / localRotation / localScale; never Transform.position, .forward or LookAt on
/// objects under the root (those are in Unity's mirrored space). Rotations with more than one axis go through
/// <see cref="Euler"/>, because Three's default order (XYZ) is not Unity's (ZXY).
/// </summary>
public static class GameSpace
{
    /// <summary>A game-space point or direction in Unity's axes (z mirrored).</summary>
    public static Vector3 ToUnity(Vector3 v) => new Vector3(v.x, v.y, -v.z);

    public static Vector3 ToUnity(double x, double y, double z) => new Vector3((float)x, (float)y, (float)-z);

    /// <summary>A Unity-axes point or direction in game space.</summary>
    public static Vector3 FromUnity(Vector3 v) => new Vector3(v.x, v.y, -v.z);

    /// <summary>A game-space rotation as seen in Unity's axes (the mirror on both sides).</summary>
    public static Quaternion ToUnity(Quaternion q) => new Quaternion(-q.x, -q.y, q.z, q.w);

    public static Quaternion FromUnity(Quaternion q) => new Quaternion(-q.x, -q.y, q.z, q.w);

    /// <summary>A float vector from the game's doubles.</summary>
    public static Vector3 V(double x, double y, double z) => new Vector3((float)x, (float)y, (float)z);

    /// <summary>
    /// Three's Euler angles (radians) as a quaternion, in Three's order ("XYZ" by default, "YXZ" for the camera). Computed in
    /// double exactly as Quaternion.setFromEuler does, so the numbers match the old code.
    /// </summary>
    public static Quaternion Euler(double x, double y, double z, string order = "XYZ")
    {
        double c1 = Math.Cos(x / 2), c2 = Math.Cos(y / 2), c3 = Math.Cos(z / 2);
        double s1 = Math.Sin(x / 2), s2 = Math.Sin(y / 2), s3 = Math.Sin(z / 2);
        double qx, qy, qz, qw;
        switch (order)
        {
            case "YXZ":
                qx = s1 * c2 * c3 + c1 * s2 * s3; qy = c1 * s2 * c3 - s1 * c2 * s3;
                qz = c1 * c2 * s3 - s1 * s2 * c3; qw = c1 * c2 * c3 + s1 * s2 * s3; break;
            case "ZXY":
                qx = s1 * c2 * c3 - c1 * s2 * s3; qy = c1 * s2 * c3 + s1 * c2 * s3;
                qz = c1 * c2 * s3 + s1 * s2 * c3; qw = c1 * c2 * c3 - s1 * s2 * s3; break;
            case "ZYX":
                qx = s1 * c2 * c3 - c1 * s2 * s3; qy = c1 * s2 * c3 + s1 * c2 * s3;
                qz = c1 * c2 * s3 - s1 * s2 * c3; qw = c1 * c2 * c3 + s1 * s2 * s3; break;
            case "YZX":
                qx = s1 * c2 * c3 + c1 * s2 * s3; qy = c1 * s2 * c3 + s1 * c2 * s3;
                qz = c1 * c2 * s3 - s1 * s2 * c3; qw = c1 * c2 * c3 - s1 * s2 * s3; break;
            case "XZY":
                qx = s1 * c2 * c3 - c1 * s2 * s3; qy = c1 * s2 * c3 - s1 * c2 * s3;
                qz = c1 * c2 * s3 + s1 * s2 * c3; qw = c1 * c2 * c3 + s1 * s2 * s3; break;
            default: // XYZ
                qx = s1 * c2 * c3 + c1 * s2 * s3; qy = c1 * s2 * c3 - s1 * c2 * s3;
                qz = c1 * c2 * s3 + s1 * s2 * c3; qw = c1 * c2 * c3 - s1 * s2 * s3; break;
        }
        return new Quaternion((float)qx, (float)qy, (float)qz, (float)qw);
    }

    /// <summary>
    /// The rotation Three's Object3D.lookAt gives an object (not a camera) at `from` looking at `to`, both in the same game-space
    /// frame: its +z toward the target. The cross products are the same numbers as Unity's, so LookRotation does it.
    /// </summary>
    public static Quaternion LookAt(Vector3 from, Vector3 to, Vector3? up = null)
    {
        var d = to - from;
        return d.sqrMagnitude < 1e-12f ? Quaternion.identity : Quaternion.LookRotation(d, up ?? Vector3.up);
    }

    /// <summary>The same for a camera or a light, which look down their -z as in Three.</summary>
    public static Quaternion LookAtAsCamera(Vector3 from, Vector3 to, Vector3? up = null)
    {
        var d = from - to;
        return d.sqrMagnitude < 1e-12f ? Quaternion.identity : Quaternion.LookRotation(d, up ?? Vector3.up);
    }

    /// <summary>Where a Transform under the world root is, in game space (Three's getWorldPosition).</summary>
    public static Vector3 WorldPos(Transform t) => World.Root != null ? World.Root.InverseTransformPoint(t.position) : FromUnity(t.position);

    /// <summary>A game-space world point as a point local to `parent` (which hangs under the world root).</summary>
    public static Vector3 ToLocal(Transform parent, Vector3 gameWorld) =>
        parent.InverseTransformPoint(World.Root != null ? World.Root.TransformPoint(gameWorld) : ToUnity(gameWorld));

    /// <summary>Visits a Transform and everything under it (Three's traverse), parents before children.</summary>
    public static void Walk(Transform t, Action<Transform> visit)
    {
        visit(t);
        for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), visit);
    }
}
