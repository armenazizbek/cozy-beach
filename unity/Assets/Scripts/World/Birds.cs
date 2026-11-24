using System;
using System.Collections.Generic;
using System.Linq;
using static Beach;

// Port of src/world/Birds.ts

/// <summary>Seagulls: some glide in lazy circles over the shore, others stroll on the wet sand and flap away when approached.</summary>
public class Birds
{
    private static double rand(double a, double b) => a + JS.Random() * (b - a);

    private class Gull
    {
        /// <summary>the gull's parts as built (descriptions), turned into GameObjects once by Place</summary>
        public THREE.Group root, legs, left, right;
        /// <summary>the wings tucked along the body, for a bird that is standing</summary>
        public THREE.Group folded;
        /// <summary>its GameObjects, posed from the numbers below each frame (unity/UI_PLAN.md B1)</summary>
        public UnityEngine.Transform rootT, legsT, foldedT, leftT, rightT;
        /// <summary>where it is and how it is turned, in the game's world; `yxz` once it has flown (Three's rotation order)</summary>
        public double x, y, z, rx, ry, rz;
        public bool yxz, shown = true;
        /// <summary>the wings' angle, and whether it stands (legs and folded wings) or flies (open wings)</summary>
        public double wing;
        public bool grounded;
        public Target target;
        /// <summary>'fly' | 'ground' | 'flee' | 'away'</summary>
        public string mode;
        // circling flight
        public double cx, cz, r, ang, omega, alt, phase;
        // on the ground
        public double vx, vz, timer, climb;
        /// <summary>how close a visitor can come before this gull takes off; each one is a little different</summary>
        public double alert;
        public THREE.Mesh hit;
    }

    public readonly THREE.Group group = new THREE.Group();
    private readonly List<Gull> gulls = new List<Gull>();

    /// <summary>
    /// The flock's GameObjects. The gulls are built as descriptions and realised once; after that each gull's place, turn,
    /// wings and visibility are numbers on the Gull, and Pose sets its GameObjects from them (unity/UI_PLAN.md B1). The aim
    /// (Target) and the binoculars (Picking.NearestDrawn) no longer look at the descriptions.
    /// </summary>
    private UnityEngine.Transform root;

    /// <summary>The gulls' GameObjects under the world root, once placed.</summary>
    public UnityEngine.Transform Root => root;

    /// <summary>Puts the flock into the world as native objects.</summary>
    public void Place()
    {
        var live = Realizer.Realize(group, World.Land, World.Layer);
        root = live.root;
        foreach (var g in gulls)
        {
            g.rootT = live[g.root];
            g.legsT = live[g.legs];
            g.foldedT = live[g.folded];
            g.leftT = live[g.left];
            g.rightT = live[g.right];
        }
    }

    /// <summary>Sets a gull's GameObjects from its numbers. A hidden gull is left where it was, as the scene copy left it.</summary>
    private static void Pose(Gull g)
    {
        if (g.rootT == null) return;
        if (g.rootT.gameObject.activeSelf != g.shown) g.rootT.gameObject.SetActive(g.shown);
        if (!g.shown) return;
        g.rootT.localPosition = GameSpace.V(g.x, g.y, g.z);
        g.rootT.localRotation = GameSpace.Euler(g.rx, g.ry, g.rz, g.yxz ? "YXZ" : "XYZ");
        if (g.legsT.gameObject.activeSelf != g.grounded) g.legsT.gameObject.SetActive(g.grounded);
        if (g.foldedT.gameObject.activeSelf != g.grounded) g.foldedT.gameObject.SetActive(g.grounded);
        if (g.rightT.gameObject.activeSelf == g.grounded) g.rightT.gameObject.SetActive(!g.grounded);
        if (g.leftT.gameObject.activeSelf == g.grounded) g.leftT.gameObject.SetActive(!g.grounded);
        if (!g.grounded)
        {
            g.rightT.localRotation = GameSpace.Euler(0, 0, g.wing);
            g.leftT.localRotation = GameSpace.Euler(0, 0, -g.wing);
        }
    }
    private readonly THREE.BufferGeometry wingGeo;
    private readonly THREE.MeshStandardMaterial white = new THREE.MeshStandardMaterial { color = 0xf6f4ee, roughness = 0.8 };
    private readonly THREE.MeshStandardMaterial grey = new THREE.MeshStandardMaterial { color = 0xb9bec4, roughness = 0.8 };
    private readonly THREE.MeshStandardMaterial dark = new THREE.MeshStandardMaterial { color = 0x2a2c30, roughness = 0.8 };
    private readonly THREE.MeshStandardMaterial beakMat = new THREE.MeshStandardMaterial { color = 0xf2a526, roughness = 0.6 };
    private double cryTimer = 4;
    private readonly Beach beach;

    public Birds(Beach beach)
    {
        this.beach = beach;
        var shape = new THREE.Shape();
        shape.moveTo(0, 0.17);
        shape.lineTo(0.55, 0.14);
        shape.lineTo(1.3, -0.04);
        shape.lineTo(1.22, -0.12);
        shape.lineTo(0.55, -0.1);
        shape.lineTo(0, -0.16);
        shape.closePath();
        wingGeo = new THREE.ExtrudeGeometry(shape, new THREE.ExtrudeGeometryOptions { depth = 0.015, bevelEnabled = false });
        wingGeo.rotateX(-Math.PI / 2);

        for (int i = 0; i < 14; i++) addFlying();
        for (int i = 0; i < 12; i++) addGround();
    }

    private Gull makeGull()
    {
        var root = new THREE.Group();
        var body = new THREE.Mesh(new THREE.SphereGeometry(0.16, 12, 8), white);
        body.scale.set(0.85, 0.8, 1.9);
        body.castShadow = true;
        var head = new THREE.Mesh(new THREE.SphereGeometry(0.085, 10, 8), white);
        head.castShadow = true; // like the body, so that they weld into one mesh below
        head.position.set(0, 0.07, 0.34);
        var beak = new THREE.Mesh(new THREE.ConeGeometry(0.03, 0.12, 6), beakMat);
        beak.rotation.x = Math.PI / 2;
        beak.position.set(0, 0.05, 0.46);
        var eyeL = new THREE.Mesh(new THREE.SphereGeometry(0.012, 6, 4), dark);
        eyeL.position.set(0.05, 0.09, 0.4);
        var eyeR = (THREE.Mesh)eyeL.clone();
        eyeR.position.x = -0.05;
        var tail = new THREE.Mesh(new THREE.BoxGeometry(0.16, 0.02, 0.2), grey);
        tail.position.set(0, 0.0, -0.36);
        // the grey back that gulls have, and a rounded breast
        var mantle = new THREE.Mesh(new THREE.SphereGeometry(1, 12, 8), grey);
        mantle.scale.set(0.14, 0.06, 0.3);
        mantle.position.set(0, 0.09, -0.06);
        var breast = new THREE.Mesh(new THREE.SphereGeometry(0.12, 10, 8), white);
        breast.castShadow = true;
        breast.position.set(0, 0.0, 0.2);
        // the gull's parts never move against each other: each set of them is a static group (Object3D.syncStatic), so the
        // scene copy only follows the gull itself and its wings
        var still = new THREE.Group { syncStatic = true };
        still.add(body, head, beak, eyeL, eyeR, tail, mantle, breast);
        root.add(still);
        // orange legs, only visible while standing
        var legs = new THREE.Group { syncStatic = true };
        foreach (var x in new[] { -0.05, 0.05 })
        {
            var leg = new THREE.Mesh(new THREE.CylinderGeometry(0.011, 0.009, 0.2, 6), beakMat);
            leg.position.set(x, -0.18, 0.02);
            var foot = new THREE.Mesh(new THREE.BoxGeometry(0.05, 0.008, 0.09), beakMat);
            foot.position.set(x, -0.28, 0.05);
            legs.add(leg, foot);
        }
        root.add(legs);

        // standing, the wings lie folded along the flanks with dark tips crossing at the tail
        var folded = new THREE.Group { syncStatic = true };
        foreach (var sx in new[] { -1, 1 })
        {
            var w = new THREE.Mesh(new THREE.SphereGeometry(1, 10, 8), grey);
            w.scale.set(0.05, 0.085, 0.3);
            w.position.set(sx * 0.12, 0.04, -0.08);
            w.rotation.set(0.06, sx * 0.05, sx * -0.1);
            var tip = new THREE.Mesh(new THREE.ConeGeometry(0.03, 0.2, 6), dark);
            tip.rotation.x = -Math.PI / 2;
            tip.position.set(sx * 0.07, 0.04, -0.46);
            folded.add(w, tip);
        }
        root.add(folded);

        THREE.Group wing()
        {
            var w = new THREE.Group { syncStatic = true };
            // the wings cast into the sun's shadow map too, so a gull's shadow has its wings (beating as they beat), not just its body
            var m = new THREE.Mesh(wingGeo, grey) { castShadow = true };
            var tip = new THREE.Mesh(new THREE.BoxGeometry(0.2, 0.018, 0.1), dark) { castShadow = true };
            tip.position.set(1.2, 0.005, -0.06);
            w.add(m, tip);
            return w;
        }
        var right = wing();
        right.position.set(0.1, 0.05, 0.02);
        var left = wing();
        left.position.set(-0.1, 0.05, 0.02);
        left.scale.x = -1;
        root.add(right, left);
        // a gull's fixed parts are welded into one mesh per material (about 16 draws became 7), in the shadow map too
        Merge.mergeStatic(still);
        Merge.mergeStatic(legs);
        Merge.mergeStatic(folded);
        root.scale.setScalar(1.5);
        group.add(root);
        return new Gull { root = root, legs = legs, left = left, right = right, folded = folded };
    }

    private Gull blank(string mode)
    {
        var g = makeGull();
        var hit = new THREE.Mesh(new THREE.SphereGeometry(0.6, 8, 6), new THREE.MeshBasicMaterial { visible = false });
        hit.userData["hitRadius"] = 1.0;
        g.root.add(hit);
        g.hit = hit;
        g.mode = mode;
        g.phase = JS.Random() * 6;
        g.alert = rand(10, 15);
        hit.userData["gull"] = g;
        // the hit sphere sits at the gull's middle: the target is where the gull is
        g.target = new Target(TargetKind.Gull, g, 1.0, v => v.set(g.x, g.y, g.z));
        return g;
    }

    private void addFlying()
    {
        var g = blank("fly");
        g.cx = rand(-55, 55);
        g.cz = rand(0, 60);
        g.r = rand(14, 42);
        g.ang = JS.Random() * Math.PI * 2;
        g.omega = rand(5, 8) / g.r * (JS.Random() < 0.5 ? 1 : -1);
        g.alt = rand(12, 30);
        gulls.Add(g);
    }

    private void landSpot(Gull g)
    {
        double x = rand(-62, 62);
        double z = rand(WET_Z + 1, SHORE_Z - 3);
        g.x = x;
        g.y = beach.heightAt(x, z) + 0.2;
        g.z = z;
        g.mode = "ground";
        g.timer = rand(1, 4);
        g.vx = g.vz = 0;
        g.shown = true;
    }

    private void addGround()
    {
        var g = blank("ground");
        landSpot(g);
        gulls.Add(g);
    }

    private Gull held;

    /// <summary>For the -autoshot-gullshadow test: keeps one circling gull hovering at this spot and height; returns where it is.</summary>
    internal THREE.Vector3 smokeHold(double x, double z, double alt)
    {
        held = gulls.First(g => g.mode == "fly");
        held.cx = x;
        held.cz = z;
        held.r = 0.01;
        held.alt = alt;
        held.omega = 0.0001;
        return new THREE.Vector3(held.x, held.y, held.z);
    }

    /// <summary>Every gull that is currently around, to aim at.</summary>
    public List<Target> targets() => gulls.Where(g => g.mode != "away" && g.shown).Select(g => g.target).ToList();

    /// <summary>Whether the gull a target stands for is on the sand (and not in the air).</summary>
    public bool standing(object gull) => (gull as Gull)?.mode == "ground";

    /// <summary>whether the gull that was last shot was in the air, which is the harder shot</summary>
    public bool lastShotAirborne = false;

    /// <summary>A gull is startled right out of sight for a good while. Returns where it was.</summary>
    public THREE.Vector3 shoot(object gull)
    {
        var g = gull as Gull;
        if (g == null || g.mode == "away") return null;
        var at = new THREE.Vector3(g.x, g.y, g.z);
        lastShotAirborne = g.mode == "fly" || g.mode == "flee";
        g.mode = "away";
        g.timer = rand(40, 70);
        g.shown = false;
        g.rx = 0;
        Pose(g);
        return at;
    }

    /// <summary>Wing angle in radians; positive lifts the tips.</summary>
    private void setWings(Gull g, double a)
    {
        g.grounded = g.mode == "ground";
        g.wing = a;
    }

    /// <summary>onCry(distance) is called when a gull squawks so the sound can be placed.</summary>
    public void update(double dt, double t, double px, double pz, Action<double> onCry)
    {
        foreach (var g in gulls)
        {
            if (g.mode == "fly")
            {
                g.ang += g.omega * dt;
                double dir = JS.Sign(g.omega);
                double x = g.cx + Math.Cos(g.ang) * g.r;
                double z = g.cz + Math.Sin(g.ang) * g.r;
                double y = g.alt + Math.Sin(t * 0.5 + g.phase) * 1.4;
                g.yxz = true;
                g.ry = Math.Atan2(-Math.Sin(g.ang) * dir, Math.Cos(g.ang) * dir);
                g.rz = -dir * 0.3;
                g.x = x;
                g.y = y;
                g.z = z;
                // flap, then glide, then flap again
                double flapAmt = Math.Max(0, Math.Sin(t * 0.35 + g.phase));
                setWings(g, 0.12 + Math.Sin(t * 6.5 + g.phase) * 0.5 * flapAmt);
            }
            else if (g.mode == "ground")
            {
                g.timer -= dt;
                if (g.timer <= 0)
                {
                    g.timer = rand(1.5, 4.5);
                    if (JS.Random() < 0.5) g.vx = g.vz = 0;
                    else
                    {
                        double a = JS.Random() * Math.PI * 2;
                        g.vx = Math.Cos(a) * 0.45;
                        g.vz = Math.Sin(a) * 0.45;
                    }
                }
                double nx = g.x + g.vx * dt, nz = g.z + g.vz * dt;
                if (nz < WET_Z || nz > SHORE_Z - 2 || Math.Abs(nx) > 64)
                {
                    g.vx *= -1;
                    g.vz *= -1;
                    nx = g.x;
                    nz = g.z;
                }
                bool moving = g.vx != 0 || g.vz != 0;
                g.x = nx;
                g.y = beach.heightAt(nx, nz) + 0.42 + (moving ? Math.Abs(Math.Sin(t * 8 + g.phase)) * 0.02 : 0);
                g.z = nz;
                if (moving)
                {
                    double target = Math.Atan2(g.vx, g.vz);
                    double d0 = target - g.ry;
                    d0 = Math.Atan2(Math.Sin(d0), Math.Cos(d0));
                    g.ry += d0 * Math.Min(1, dt * 4);
                }
                g.rz = 0;
                setWings(g, -1.15);
                // startled by a visitor
                double d = JS.Hypot(px - nx, pz - nz);
                if (d < g.alert)
                {
                    g.mode = "flee";
                    g.climb = 0;
                    double away = Math.Atan2(nx - px, nz - pz);
                    g.vx = Math.Sin(away) * 5.5;
                    g.vz = Math.Cos(away) * 5.5;
                    g.ry = away;
                    onCry(d);
                }
            }
            else if (g.mode == "flee")
            {
                g.climb += dt * 4;
                g.x += g.vx * dt;
                g.z += g.vz * dt;
                g.y += Math.Min(6, g.climb) * dt;
                g.rx = -0.35;
                setWings(g, Math.Sin(t * 16 + g.phase) * 0.8);
                if (g.y > 16)
                {
                    g.mode = "away";
                    g.timer = rand(25, 45);
                    g.shown = false;
                    g.rx = 0;
                }
            }
            else
            {
                g.timer -= dt;
                if (g.timer <= 0) landSpot(g);
            }
        }

        // a distant squawk now and then, from one of the circling birds
        cryTimer -= dt;
        if (cryTimer <= 0)
        {
            cryTimer = rand(6, 16);
            var flyers = gulls.Where(g => g.mode == "fly").ToList();
            if (flyers.Count > 0)
            {
                var g = flyers[(int)Math.Floor(JS.Random() * flyers.Count)];
                onCry(JS.Hypot(g.x - px, g.z - pz));
            }
        }
        // gulls too far away to make out are not drawn
        foreach (var g in gulls) if (g.mode != "away") g.shown = JS.Hypot(g.x - px, g.z - pz) < 140;
        foreach (var g in gulls) Pose(g);
    }
}
