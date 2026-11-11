using System;
using System.Collections.Generic;
using static Beach;

/// <summary>
/// A pod of dolphins that comes to ride beside the boat. Nothing brings them but a steady run across open, deep water: after a
/// while they come up from behind, take their places either side of the bow and keep pace, rolling at the surface and leaping
/// clear of it. They stay for half a minute or so, or until the boat slows down or nears land, and then they drop away.
/// They are company, not game: nothing can hit them.
/// </summary>
public class Dolphins
{
    private const double LEN = 2.6;
    /// <summary>the share of each rise-and-dive cycle spent on the way up and over</summary>
    private const double RISE = 0.55;
    /// <summary>how far under the surface the middle of the body runs between rises: shallow, so the fin cuts the water</summary>
    private const double RUN_DEPTH = 0.22;

    private class Dolphin
    {
        public THREE.Group root, flukes;
        /// <summary>where it likes to swim: metres to the right of the boat and ahead of it</summary>
        public double side, ahead;
        /// <summary>0..1 through its rise and dive, how long one takes, and how high this one goes</summary>
        public double cycle, period, leap;
        public double x, z, heading, size, wobble;
        /// <summary>height of its middle above the surface last frame, to see it break the water</summary>
        public double lastY = -1;
        /// <summary>the calf copies the timing of the one it swims beside</summary>
        public Dolphin mother;
    }

    public readonly THREE.Group group = new THREE.Group();
    private readonly List<Dolphin> pod = new List<Dolphin>();
    private readonly ParticleSystem splashes = new ParticleSystem(240, 0.13, 7, false);
    private readonly THREE.Color white = new THREE.Color(0xeaf7ff);
    /// <summary>0 while they are away, 1 once they are up beside the boat</summary>
    private double presence = 0;
    private bool escorting = false;
    /// <summary>seconds of steady sailing still to go before they turn up</summary>
    private double wait = 10;
    /// <summary>seconds they will still keep the boat company</summary>
    private double stay = 0;
    /// <summary>how far they have drifted on ahead of a boat that has slowed down</summary>
    private double lead = 0;
    private bool greeted = false;

    /// <summary>True while the pod is up beside the boat.</summary>
    public bool withBoat => presence > 0.5;

    public Dolphins()
    {
        var (body, flukes) = buildShapes();
        var skin = new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.28, metalness = 0.05 };
        var eyeMat = new THREE.MeshStandardMaterial { color = 0x0b0e12, roughness = 0.1, metalness = 0.3 };
        // two at the bow, two further back and wider, and a calf tucked in beside the first
        var places = new (double side, double ahead, double size)[] { (-3.6, 8.5, 1.0), (3.9, 7.0, 1.06), (-6.4, 2.5, 0.94), (6.6, 1.0, 1.0), (-5.0, 9.3, 0.6) };
        foreach (var (side, ahead, size) in places)
        {
            var root = new THREE.Group();
            root.rotation.order = "YXZ";
            var mesh = new THREE.Mesh(body, skin);
            mesh.castShadow = true;
            root.add(mesh);
            var tail = new THREE.Group();
            tail.position.set(0, 0, LEN / 2 - 0.07);
            var blades = new THREE.Mesh(flukes, skin);
            blades.castShadow = true;
            tail.add(blades);
            root.add(tail);
            foreach (var s in new[] { -1, 1 })
            {
                var eye = new THREE.Mesh(new THREE.SphereGeometry(0.024, 10, 8), eyeMat);
                eye.position.set(s * W(0.175) * 0.93, 0.025, zAt(0.175));
                root.add(eye);
            }
            root.scale.setScalar(size);
            group.add(root);
            pod.Add(new Dolphin { root = root, flukes = tail, side = side, ahead = ahead, size = size, cycle = JS.Random(), period = 2.4 + JS.Random() * 0.7, leap = 1.1, wobble = JS.Random() * 6 });
        }
        pod[4].mother = pod[0];
        group.add(splashes.points);
        group.visible = false;
    }

    // ---------------------------------------------------------------- shape

    private static double zAt(double u) => -LEN / 2 + u * LEN;

    // half the body's width and height along it, 0 at the tip of the beak and 1 at the tail: a slim beak, the round forehead
    // rising behind it, the deepest part a third of the way back, and a narrow stalk before the flukes
    private static readonly Func<double, double> W = SeaModels.profile(new[]
    {
        (0.0, 0.0), (0.02, 0.032), (0.085, 0.05), (0.125, 0.105), (0.2, 0.165), (0.34, 0.21), (0.5, 0.195), (0.65, 0.145),
        (0.8, 0.08), (0.92, 0.04), (1.0, 0.026),
    });
    private static readonly Func<double, double> H = SeaModels.profile(new[]
    {
        (0.0, 0.0), (0.02, 0.028), (0.085, 0.045), (0.12, 0.115), (0.2, 0.185), (0.34, 0.225), (0.5, 0.21), (0.65, 0.16),
        (0.8, 0.1), (0.92, 0.062), (1.0, 0.04),
    });

    /// <summary>
    /// The body with its curved dorsal fin and two flippers, slate blue above and pale below; and the flukes in their own space,
    /// which lie flat (a dolphin's tail beats up and down, not side to side).
    /// </summary>
    private static (THREE.BufferGeometry body, THREE.BufferGeometry flukes) buildShapes()
    {
        const int TOP = 0x54697f, BELLY = 0xe9eef1;
        THREE.BufferGeometry dark(THREE.BufferGeometry g) => SeaModels.countershade(g, TOP, TOP);
        var parts = new List<THREE.BufferGeometry> { SeaModels.countershade(SeaModels.body(LEN, W, H, 48, 24), TOP, BELLY, -0.22, 0.5) };
        var dorsal = SeaModels.fin(0.3, zAt(0.43), zAt(0.57), zAt(0.615), 0.05, 1.7, 0.4, 10, 8);
        dorsal.translate(0, H(0.5) * 0.93, 0);
        parts.Add(dark(dorsal));
        foreach (var side in new[] { -1, 1 })
        {
            var flipper = SeaModels.fin(0.36, zAt(0.27), zAt(0.35), zAt(0.43), 0.04, 1.4, 0.5, 10, 8);
            flipper.rotateZ(-side * (Math.PI / 2 + 0.55));
            flipper.translate(side * W(0.31) * 0.7, -H(0.31) * 0.55, 0);
            parts.Add(dark(flipper));
        }
        var body = THREE.BufferGeometryUtils.mergeGeometries(parts, false);
        body.computeVertexNormals();
        var blades = new List<THREE.BufferGeometry>();
        foreach (var side in new[] { -1, 1 })
        {
            var blade = SeaModels.fin(0.36, -0.15, 0.07, 0.22, 0.035, 1.3, 0.5, 8, 8);
            blade.rotateZ(-side * Math.PI / 2);
            blades.Add(blade);
        }
        var flukes = dark(THREE.BufferGeometryUtils.mergeGeometries(blades, false));
        return (body, flukes);
    }

    // ---------------------------------------------------------------- behaviour

    /// <summary>For the smoke test: brings the pod at once.</summary>
    internal void smokeCall() => wait = 0;

    internal string smokeState() => $"dolphins: {(escorting ? "with the boat" : "away")}, presence {presence:0.00}, stay {stay:0.0}s, wait {wait:0.0}s";

    /// <summary>Height of a dolphin's middle above the surface at a point in its cycle, and how fast that is changing per cycle.</summary>
    private static (double y, double dy) arc(double u, double leap)
    {
        if (u < RISE)
        {
            double s = u / RISE;
            return (-RUN_DEPTH + (leap + RUN_DEPTH) * Math.Sin(Math.PI * s), (leap + RUN_DEPTH) * Math.PI / RISE * Math.Cos(Math.PI * s));
        }
        double s2 = (u - RISE) / (1 - RISE);
        return (-RUN_DEPTH - 0.14 * Math.Sin(Math.PI * s2), -0.14 * Math.PI / (1 - RISE) * Math.Cos(Math.PI * s2));
    }

    /// <summary>
    /// Moves the pod. `sailing` is true while the player is aboard the boat; `ground` gives the height of the sea bed.
    /// Returns true once, at the moment the pod first comes up beside the boat.
    /// </summary>
    public bool update(double dt, double t, bool sailing, double bx, double bz, double heading, double speed, double level,
        Func<double, double, double> swell, Func<double, double, double> ground, Action<double> splashSound)
    {
        // they only come out where the sea is open and deep, to a boat that is going somewhere
        bool open = sailing && speed > 5 && bz > SHORE_Z + 45 && level - ground(bx, bz) > 2.2;
        if (open) foreach (var isl in ISLANDS) if (JS.Hypot(bx - isl.x, bz - isl.z) < isl.R + 22) open = false;
        if (!escorting)
        {
            if (open) wait -= dt;
            if (wait <= 0 && open && presence <= 0)
            {
                escorting = true;
                greeted = false;
                stay = 26 + JS.Random() * 22;
                lead = 0;
                foreach (var d in pod) d.lastY = -1;
            }
        }
        else
        {
            // they lose interest quickly in a boat that has stopped, or one that is heading into the shallows
            stay -= dt * (open ? 1 : 4);
            if (stay <= 0)
            {
                escorting = false;
                wait = 40 + JS.Random() * 50;
            }
        }
        presence = Math.Min(1, Math.Max(0, presence + (escorting ? dt / 4.5 : -dt / 3.5)));
        group.visible = presence > 0;
        splashes.update(dt);
        if (presence <= 0) return false;

        // beside a boat that has slowed, they carry on at their own pace and draw ahead
        double pace = Math.Max(5, speed);
        lead += Math.Max(0, pace - Math.Max(0, speed)) * dt;
        double fx = -Math.Sin(heading), fz = -Math.Cos(heading), rx = Math.Cos(heading), rz = -Math.Sin(heading);
        // coming, they close in from astern; going, they sink away
        double astern = Math.Pow(1 - presence, 2) * (escorting ? 30 : 0);
        double sunk = escorting ? 0 : (1 - presence) * 2.2;
        foreach (var d in pod)
        {
            double weave = Math.Sin(t * 0.45 + d.wobble) * 0.7;
            double ahead = d.ahead + lead - astern + Math.Sin(t * 0.3 + d.wobble * 2) * 0.8;
            double side = d.side + weave + (escorting ? 0 : Math.Sign(d.side) * (1 - presence) * 6);
            double tx = bx + fx * ahead + rx * side, tz = bz + fz * ahead + rz * side;
            if (d.lastY < -0.99 && escorting && presence < 0.05)
            {
                d.x = tx;
                d.z = tz;
                d.heading = heading;
            }
            // each follows its place a little loosely, so a turn of the wheel swings the pod round and not a rigid frame
            double k = Math.Min(1, dt * 3.5);
            d.x += (tx - d.x) * k;
            d.z += (tz - d.z) * k;
            double turn = Math.Atan2(Math.Sin(heading - d.heading), Math.Cos(heading - d.heading));
            d.heading += turn * Math.Min(1, dt * 3) + Math.Cos(t * 0.45 + d.wobble) * 0.05 * dt;

            if (d.mother != null)
            {
                // the calf surfaces just after its mother
                d.cycle = d.mother.cycle - 0.09;
                d.cycle -= Math.Floor(d.cycle);
                d.leap = d.mother.leap * 0.75;
            }
            else
            {
                d.cycle += dt / d.period;
                if (d.cycle >= 1)
                {
                    d.cycle -= 1;
                    // most rises are leaps clear of the water; some are only a roll that shows the back and the fin
                    d.leap = JS.Random() < 0.8 ? 0.85 + JS.Random() * 0.65 : 0.1 + JS.Random() * 0.1;
                }
            }
            var (y, dy) = arc(d.cycle, d.leap * d.size);
            double vy = dy / d.period;
            double surface = level + swell(d.x, d.z);
            d.root.position.set(d.x, surface + y - sunk, d.z);
            d.root.rotation.y = d.heading;
            d.root.rotation.x = Math.Max(-1.0, Math.Min(1.0, Math.Atan2(vy, pace) * 1.8));
            d.root.rotation.z = Math.Sin(t * 0.8 + d.wobble) * 0.07;
            d.flukes.rotation.x = Math.Sin(t * 9 + d.wobble) * 0.38;

            // spray where it breaks the surface, going up and coming down
            if (sunk < 0.3 && d.leap > 0.5 && d.lastY > -0.99 && (d.lastY < 0) != (y < 0))
            {
                bool leaving = y >= 0;
                var at = new THREE.Vector3(d.x - Math.Sin(d.heading) * (leaving ? 0.6 : 0.9), surface + 0.05, d.z - Math.Cos(d.heading) * (leaving ? 0.6 : 0.9));
                splashes.burst(at, leaving ? 9 : 16, leaving ? 1.1 : 1.8, leaving ? 2.2 : 3.4, 0.9, white);
                if (!leaving) splashSound(JS.Hypot(d.x - bx, d.z - bz) + 6);
            }
            d.lastY = y;
        }
        if (escorting && !greeted && presence > 0.6)
        {
            greeted = true;
            return true;
        }
        return false;
    }
}
