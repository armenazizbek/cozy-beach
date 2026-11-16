using System;

/// <summary>
/// The old brass telescope on its wooden tripod, up on the dune of Lagoon Cay with the open sea in front of it. Someone set it
/// up here to watch the sky; the player walks up and presses F (or clicks it) to look through it (<see cref="TelescopeView"/>).
/// </summary>
public class Telescope
{
    public readonly double x, z, ground, yaw;
    public readonly THREE.Group group = new THREE.Group();
    /// <summary>where the eyepiece is, for aiming the mouse at it</summary>
    public readonly THREE.Vector3 eyepiece;

    /// <summary>how far up the tube points</summary>
    private const double ELEVATION = 0.42;
    private const double HEAD = 1.22;

    /// <param name="facing">which way the tube points: an angle round from +z toward +x, as the player's yaw</param>
    public Telescope(double x, double z, double groundY, double facing)
    {
        this.x = x;
        this.z = z;
        ground = groundY;
        yaw = facing;
        group.position.set(x, groundY, z);
        group.rotation.y = facing;

        var wood = new THREE.MeshStandardMaterial { color = 0x7a5634, roughness = 0.85 };
        var brass = new THREE.MeshStandardMaterial { color = 0xc49a4c, roughness = 0.32, metalness = 0.85 };
        var darkBrass = new THREE.MeshStandardMaterial { color = 0x8a6a30, roughness = 0.4, metalness = 0.8 };
        var leather = new THREE.MeshStandardMaterial { color = 0x4a3020, roughness = 0.9 };
        var glass = new THREE.MeshStandardMaterial { color = 0x1d3550, roughness = 0.05, metalness = 0.6 };

        THREE.Mesh put(THREE.Object3D parent, THREE.BufferGeometry geo, THREE.Material mat)
        {
            var m = new THREE.Mesh(geo, mat);
            m.castShadow = true;
            m.receiveShadow = true;
            parent.add(m);
            return m;
        }

        // three legs splayed out from the head, one toward the sea and two behind
        for (int i = 0; i < 3; i++)
        {
            double a = i / 3.0 * Math.PI * 2;
            const double spread = 0.52;
            double fx = Math.Sin(a) * spread, fz = Math.Cos(a) * spread;
            double len = Math.Sqrt(HEAD * HEAD + spread * spread) + 0.05;
            var leg = put(group, new THREE.CylinderGeometry(0.028, 0.038, len, 7), wood);
            leg.position.set(fx / 2, HEAD / 2 - 0.02, fz / 2);
            // tilt the leg from the head out to its foot
            leg.rotation.order = "YXZ";
            leg.rotation.y = a;
            leg.rotation.x = -Math.Atan2(spread, HEAD);
            var foot = put(group, new THREE.CylinderGeometry(0.045, 0.05, 0.05, 8), darkBrass);
            foot.position.set(fx, 0.02, fz);
        }
        // a brace half way down, as on a surveyor's tripod
        var brace = put(group, new THREE.TorusGeometry(0.27, 0.014, 6, 18), wood);
        brace.position.y = HEAD * 0.5;
        brace.rotation.x = Math.PI / 2;
        var head = put(group, new THREE.CylinderGeometry(0.09, 0.11, 0.1, 12), darkBrass);
        head.position.y = HEAD;
        var fork = put(group, new THREE.BoxGeometry(0.05, 0.2, 0.05), darkBrass);
        fork.position.set(0.08, HEAD + 0.12, 0);
        var fork2 = put(group, new THREE.BoxGeometry(0.05, 0.2, 0.05), darkBrass);
        fork2.position.set(-0.08, HEAD + 0.12, 0);

        // the tube tilted up toward the sky: a wide front with a dew cap, a leather grip, then the drawtubes and the eyepiece
        var pivot = new THREE.Group();
        pivot.position.y = HEAD + 0.18;
        pivot.rotation.x = Math.PI / 2 - ELEVATION;
        group.add(pivot);
        var tube = put(pivot, new THREE.CylinderGeometry(0.085, 0.07, 1.0, 16), brass);
        tube.position.y = 0.25;
        var cap = put(pivot, new THREE.CylinderGeometry(0.1, 0.095, 0.2, 16, 1, true), darkBrass);
        cap.position.y = 0.82;
        var lens = put(pivot, new THREE.CircleGeometry(0.088, 16), glass);
        lens.position.y = 0.76;
        lens.rotation.x = -Math.PI / 2;
        var grip = put(pivot, new THREE.CylinderGeometry(0.081, 0.078, 0.2, 16), leather);
        grip.position.y = 0.05;
        foreach (var ry in new[] { 0.62, 0.38, -0.08 })
        {
            var ring = put(pivot, new THREE.TorusGeometry(0.084, 0.012, 6, 18), darkBrass);
            ring.position.y = ry;
            ring.rotation.x = Math.PI / 2;
        }
        var draw1 = put(pivot, new THREE.CylinderGeometry(0.056, 0.056, 0.3, 14), brass);
        draw1.position.y = -0.38;
        var draw2 = put(pivot, new THREE.CylinderGeometry(0.04, 0.04, 0.22, 12), brass);
        draw2.position.y = -0.62;
        var eye = put(pivot, new THREE.CylinderGeometry(0.05, 0.035, 0.08, 12), darkBrass);
        eye.position.y = -0.76;

        // where the eyepiece ends up in the world
        double back = 0.78;
        double ey = HEAD + 0.18 - Math.Sin(ELEVATION) * back;
        double ez = -Math.Cos(ELEVATION) * back;
        eyepiece = new THREE.Vector3(x + Math.Sin(facing) * ez, groundY + ey, z + Math.Cos(facing) * ez);
    }

    /// <summary>True when the player is close enough to look through it.</summary>
    public bool near(double px, double pz) => JS.Hypot(px - x, pz - z) < 2.6;
}
