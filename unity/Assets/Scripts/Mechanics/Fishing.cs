using System;
using System.Collections.Generic;

/// <summary>
/// Fishing from the bench at the end of the pier. A click casts; the float sits on the water, dips once or twice as something
/// noses the bait, then goes under: a click in time hooks the fish, holding the button reels it in, and it lands in the bucket.
/// Nothing can go wrong for good: a missed bite just means waiting for the next one. This class owns the float, the line, the
/// fish on its way in and the fish in the bucket; the game tells it where the rod's tip is and what the player does.
/// </summary>
public class Fishing
{
    /// <summary>'off' | 'idle' | 'cast' | 'wait' | 'bite' | 'reel' | 'land' | 'retrieve'</summary>
    public string phase = "off";
    /// <summary>the reel button is down</summary>
    public bool held;
    /// <summary>0..1 how far the hooked fish has been reeled in</summary>
    public double progress;
    /// <summary>seconds left of telling the player the last one got away</summary>
    public double missedT;

    /// <summary>height of the sea at a point, right now</summary>
    public Func<double, double, double> waterAt = (_x, _z) => Beach.WATER_LEVEL;
    /// <summary>which fish takes the bait</summary>
    public Func<SeaSpecies> pick = () => Catches.ROD[0];
    public Func<SeaSpecies, (THREE.Group root, THREE.Mesh tail)> makeFish;
    /// <summary>the fish is in the bucket</summary>
    public Action<SeaSpecies> onCatch = _ => { };
    /// <summary>'cast' | 'plop' | 'nibble' | 'bite' | 'hook' | 'reel' | 'catch' | 'away'</summary>
    public Action<string> sound = _ => { };
    /// <summary>a splash of this strength at this spot on the water</summary>
    public Action<THREE.Vector3, double> splash = (_p, _k) => { };

    /// <summary>in world space</summary>
    public readonly THREE.Group group = new THREE.Group { name = "Fishing" };
    private readonly THREE.Group bobber = new THREE.Group();
    private readonly THREE.Mesh line;
    private readonly Dictionary<string, (THREE.Group root, THREE.Mesh tail)> models = new Dictionary<string, (THREE.Group, THREE.Mesh)>();
    private readonly List<THREE.Group> inBucket = new List<THREE.Group>();
    private THREE.Group landing;
    private THREE.Mesh landingTail;

    private readonly THREE.Vector3 tip = new THREE.Vector3();
    private readonly THREE.Vector3 from = new THREE.Vector3();
    /// <summary>where the float was cast to, and where it is now</summary>
    private readonly THREE.Vector3 target = new THREE.Vector3();
    private readonly THREE.Vector3 at = new THREE.Vector3();
    private readonly THREE.Vector3 tmp = new THREE.Vector3();
    private static readonly THREE.Vector3 UP = new THREE.Vector3(0, 1, 0);
    private double phaseT, waitT, dip, reelTick, splashT;
    private readonly List<double> nibbles = new List<double>();
    private SeaSpecies hooked;
    private int caught;

    public Fishing()
    {
        // the float: white below, red on top, with a little mast
        var white = new THREE.MeshStandardMaterial { color = 0xf6f4ee, roughness = 0.4 };
        var red = new THREE.MeshStandardMaterial { color = 0xe2412f, roughness = 0.4 };
        var lower = new THREE.Mesh(new THREE.SphereGeometry(0.05, 14, 10), white);
        var upper = new THREE.Mesh(new THREE.SphereGeometry(0.051, 14, 6, 0, Math.PI * 2, 0, Math.PI / 2), red);
        var mast = new THREE.Mesh(new THREE.CylinderGeometry(0.006, 0.006, 0.09, 6), red);
        mast.position.y = 0.085;
        bobber.add(lower, upper, mast);
        line = new THREE.Mesh(new THREE.CylinderGeometry(0.0035, 0.0035, 1, 5), new THREE.MeshBasicMaterial { color = 0xf2f1ea });
        line.frustumCulled = false;
        group.add(bobber, line);
        group.visible = false;
    }

    public bool active => phase != "off";

    /// <summary>Sits down with the rod: nothing is cast yet.</summary>
    public void sit()
    {
        phase = "idle";
        held = false;
        progress = 0;
        missedT = 0;
        caught = 0;
        showBucket();
        group.visible = true;
    }

    /// <summary>Gets up: the line comes in and the catch goes home with the player.</summary>
    public void stand()
    {
        phase = "off";
        held = false;
        progress = 0;
        caught = 0;
        showBucket();
        if (landing != null) landing.visible = false;
        group.visible = false;
    }

    /// <summary>
    /// A click with the rod. `aimX`, `aimZ` is the spot on the water the player is looking toward, used when it is a cast.
    /// </summary>
    public void click(double aimX, double aimZ)
    {
        if (phase == "idle")
        {
            target.set(aimX, waterAt(aimX, aimZ), aimZ);
            from.copy(at);
            phase = "cast";
            phaseT = 0;
            sound("cast");
        }
        else if (phase == "wait")
        {
            // nothing on yet: bring it in, to cast somewhere else
            from.copy(at);
            phase = "retrieve";
            phaseT = 0;
        }
        else if (phase == "bite")
        {
            phase = "reel";
            progress = 0.12;
            reelTick = 0;
            splashT = 0;
            sound("hook");
            splash(at, 1);
        }
    }

    private void startWait(bool first)
    {
        phase = "wait";
        phaseT = 0;
        // long enough to settle into, short enough not to bore; a little quicker after a miss
        waitT = first ? 4 + JS.Random() * 8 : 3 + JS.Random() * 5;
        nibbles.Clear();
        int n = JS.Random() < 0.65 ? (JS.Random() < 0.35 ? 2 : 1) : 0;
        for (int i = 0; i < n; i++) nibbles.Add(1.2 + JS.Random() * Math.Max(0.5, waitT - 2.4));
        dip = 0;
    }

    private void showBucket()
    {
        for (int i = 0; i < inBucket.Count; i++) inBucket[i].visible = i < caught;
    }

    private (THREE.Group root, THREE.Mesh tail) model(SeaSpecies s)
    {
        if (!models.TryGetValue(s.id, out var m))
        {
            m = makeFish(s);
            m.root.scale.setScalar(0.42);
            m.root.visible = false;
            group.add(m.root);
            models[s.id] = m;
        }
        return m;
    }

    /// <summary>A fish for the bucket: standing on its nose in the water, tail up.</summary>
    private void addToBucket(SeaSpecies s)
    {
        caught++;
        if (inBucket.Count < 4 && inBucket.Count < caught)
        {
            var (root, _) = makeFish(s);
            int i = inBucket.Count;
            root.scale.setScalar(0.3);
            root.position.copy(Pier.BUCKET).add(tmp.set(Math.Cos(i * 1.9) * 0.07, 0.2, Math.Sin(i * 1.9) * 0.07));
            // nose down, leaning on the rim
            root.rotation.set(Math.PI / 2 + 0.25, i * 1.9, 0);
            group.add(root);
            inBucket.Add(root);
        }
        showBucket();
    }

    /// <summary>`rodTip` is where the line leaves the rod, in world space.</summary>
    public void update(double dt, double t, THREE.Vector3 rodTip)
    {
        if (phase == "off") return;
        tip.copy(rodTip);
        phaseT += dt;
        missedT = Math.Max(0, missedT - dt);
        bool lineOut = true;
        switch (phase)
        {
            case "idle":
                // the float hangs under the tip, swinging a little
                at.set(tip.x + Math.Sin(t * 1.7) * 0.03, tip.y - 0.55, tip.z + Math.Cos(t * 1.3) * 0.03);
                break;
            case "cast":
            {
                double k = Math.Min(1, phaseT / 0.75);
                at.lerpVectors(from, target, k);
                at.y += Math.Sin(k * Math.PI) * 2.2;
                if (k >= 1)
                {
                    sound("plop");
                    splash(target, 0.6);
                    startWait(true);
                }
                break;
            }
            case "wait":
            {
                for (int i = nibbles.Count - 1; i >= 0; i--)
                {
                    if (phaseT < nibbles[i]) continue;
                    nibbles.RemoveAt(i);
                    dip = 1;
                    sound("nibble");
                }
                dip = Math.Max(0, dip - dt * 3.2);
                target.y = waterAt(target.x, target.z);
                at.copy(target);
                at.y += 0.012 + Math.Sin(t * 2.3) * 0.006 - Math.Sin(dip * Math.PI) * 0.045;
                if (phaseT >= waitT)
                {
                    hooked = pick();
                    phase = "bite";
                    phaseT = 0;
                    sound("bite");
                    splash(at, 0.8);
                }
                break;
            }
            case "bite":
                // pulled right under, tugging
                target.y = waterAt(target.x, target.z);
                at.copy(target);
                at.x += Math.Sin(t * 23) * 0.03;
                at.z += Math.Cos(t * 19) * 0.03;
                at.y -= 0.13 + Math.Sin(t * 17) * 0.03;
                if (phaseT > 2.0)
                {
                    // too slow: it takes the bait and goes; the line stays out for the next one
                    sound("away");
                    missedT = 2.5;
                    startWait(false);
                }
                break;
            case "reel":
            {
                progress = held ? Math.Min(1, progress + dt * 0.4) : Math.Max(0.05, progress - dt * 0.1);
                // the fish runs from side to side, less as it tires and comes in
                double side = Math.Sin(t * 2.6) * 1.5 * (1 - progress);
                double nearX = Pier.SEAT.x, nearZ = Pier.Z1 + 1.1;
                at.set(target.x + (nearX - target.x) * progress + side, 0, target.z + (nearZ - target.z) * progress);
                at.y = waterAt(at.x, at.z) - 0.1;
                if (held)
                {
                    reelTick -= dt;
                    if (reelTick <= 0)
                    {
                        reelTick = 0.11;
                        sound("reel");
                    }
                }
                splashT -= dt;
                if (splashT <= 0)
                {
                    splashT = 0.35 + JS.Random() * 0.5;
                    splash(at, 0.5);
                }
                if (progress >= 1)
                {
                    var m = model(hooked);
                    landing = m.root;
                    landingTail = m.tail;
                    landing.visible = true;
                    from.copy(at);
                    phase = "land";
                    phaseT = 0;
                    held = false;
                    sound("catch");
                    splash(at, 1);
                }
                break;
            }
            case "land":
            {
                // out of the water in an arc, over the edge of the pier and into the bucket
                double k = Math.Min(1, phaseT / 0.9);
                tmp.copy(Pier.BUCKET);
                tmp.y += 0.3;
                at.lerpVectors(from, tmp, k);
                at.y += Math.Sin(k * Math.PI) * 1.5;
                if (landing != null)
                {
                    landing.position.copy(at);
                    landing.position.y -= 0.12;
                    landing.rotation.set(Math.PI / 2, t * 6, Math.Sin(t * 26) * 0.4);
                    landingTail.rotation.y = Math.Sin(t * 30) * 0.7;
                }
                if (k >= 1)
                {
                    if (landing != null) landing.visible = false;
                    addToBucket(hooked);
                    splash(tmp, 0.25);
                    var got = hooked;
                    hooked = null;
                    progress = 0;
                    phase = "idle";
                    phaseT = 0;
                    onCatch(got);
                }
                break;
            }
            case "retrieve":
            {
                double k = Math.Min(1, phaseT / 0.45);
                tmp.set(tip.x, tip.y - 0.55, tip.z);
                at.lerpVectors(from, tmp, k);
                at.y += Math.Sin(k * Math.PI) * 0.8;
                if (k >= 1) phase = "idle";
                break;
            }
        }
        bobber.visible = phase != "land";
        bobber.position.copy(at);
        // the line, from the rod's tip to the top of the float (or to the fish on its way in)
        line.visible = lineOut;
        tmp.copy(at);
        tmp.y += phase == "land" ? 0 : 0.05;
        double len = tmp.distanceTo(tip);
        if (len > 1e-4)
        {
            line.position.copy(tip).add(tmp).multiplyScalar(0.5);
            line.scale.set(1, len, 1);
            line.quaternion.setFromUnitVectors(UP, tmp.sub(tip).multiplyScalar(1 / len));
        }
    }

    /// <summary>What to tell the player just now, worded for the mouse or for a controller.</summary>
    public string hint(bool pad)
    {
        switch (phase)
        {
            case "idle": return pad ? "RT casts toward where you are looking · A stands up" : "Click to cast toward where you are looking · F stands up";
            case "wait":
                return missedT > 0 ? "It got away. Wait for the next bite"
                    : pad ? "Wait for the float to go under · RT reels in to cast again · A stands up" : "Wait for the float to go under · click to reel in and cast again · F stands up";
            case "bite": return pad ? "A bite! Press RT!" : "A bite! Click!";
            case "reel": return pad ? "Hold RT to reel it in" : "Hold click to reel it in";
            default: return null;
        }
    }
}
