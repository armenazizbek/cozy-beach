using System;
using System.Collections.Generic;

/// <summary>What a target belongs to, which says who handles the hit (Game.onBulletHit).</summary>
public enum TargetKind { Castle, SeaLife, Coconut, Gull, Crab }

/// <summary>
/// Something the bow and the pistol can hit (unity/UI_PLAN.md B1): where it is, how close a shot has to pass, what kind it
/// is and the thing it belongs to. Arrows, bullets and the hit handler only see this, not the module's own objects, so a
/// module can keep its creatures as GameObjects, descriptions or plain numbers without them knowing.
/// </summary>
public sealed class Target
{
    public readonly TargetKind kind;
    /// <summary>the module's own handle for it, which the module's hit function takes back</summary>
    public readonly object owner;
    /// <summary>how close the path of a shot must pass to hit it, in metres</summary>
    public readonly double radius;
    private readonly Action<THREE.Vector3> where;

    public Target(TargetKind kind, object owner, double radius, Action<THREE.Vector3> where)
    {
        this.kind = kind;
        this.owner = owner;
        this.radius = radius;
        this.where = where;
    }

    /// <summary>Writes where it is now, in the game's world, into `into`.</summary>
    public void Where(THREE.Vector3 into) => where(into);

    private static readonly Dictionary<THREE.Object3D, Target> ofObject = new Dictionary<THREE.Object3D, Target>();

    /// <summary>
    /// The target for a hit mesh of a module that still keeps its creatures as descriptions: its world position, and the
    /// hitRadius its userData gives (0.42 when none). Made once per mesh.
    /// </summary>
    public static Target Of(TargetKind kind, THREE.Object3D o)
    {
        if (!ofObject.TryGetValue(o, out var t))
        {
            t = new Target(kind, o, o.userData.Num("hitRadius", 0.42), v => o.getWorldPosition(v));
            ofObject[o] = t;
        }
        return t;
    }
}
