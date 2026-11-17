using System;
using System.Collections.Generic;
using static Beach;

/// <summary>
/// The opening, played once before a new game: the Marigold in a storm at night with the village's gold in her hold, the reef
/// that took her, the same sea calm at dawn long afterwards, and the camp on the beach where the player takes over. It is all
/// played in the world itself; this class holds the ship, the camera's path, the captions and the weather for each moment.
/// </summary>
public class Opening
{
    /// <summary>where she strikes: well out, where the water is deep and dark enough to hide what is under her waterline</summary>
    private static readonly (double x, double z) REEF = (34, 240);
    /// <summary>when each shot ends, in seconds from the start</summary>
    private static readonly double[] CUTS = { 13.8, 22.4, 33.4, 43.4 };
    public const double LENGTH = 43.4;

    private static readonly (double from, double to, string text)[] CAPTIONS =
    {
        // each is on screen for as long as it takes to say (see Resources/Voice/opening-N.wav, in this order)
        (0.8, 7.5, "Forty bars of gold, melted down from every spoon and locket a village could spare."),
        (8.1, 13.2, "They trusted Captain Holt to carry it over the sea, aboard the Marigold."),
        (14.6, 17.9, "On the sixth night, the storm found her."),
        (18.5, 21.1, "She went onto the reef in the dark."),
        (23.3, 27.1, "The crew reached land. The gold did not."),
        (27.7, 32.9, "The sea kept it, and kept quiet about it, for a very long time."),
        (34.2, 37.7, "Now the tide has gone out on a quiet beach."),
        (38.3, 42.8, "You have a metal detector, a trowel, and nothing to rush."),
    };

    public readonly THREE.Group group = new THREE.Group();
    private readonly THREE.Group ship = new THREE.Group();
    private readonly THREE.Group sails = new THREE.Group();
    private readonly List<THREE.Mesh> lamps = new List<THREE.Mesh>();

    public double time { get; private set; }
    public bool done => time >= LENGTH;
    /// <summary>the time of day the sky should show now, and whether it is raining</summary>
    public double dayTime { get; private set; } = 0.8;
    public bool raining { get; private set; } = true;
    /// <summary>0..1, the light of a lightning flash dying away</summary>
    public double flash { get; private set; }
    /// <summary>the words on screen now, or null between captions</summary>
    public string caption { get; private set; }
    private int shot = -1;
    /// <summary>the narrator: each caption is also spoken. `volume` is the game's master volume</summary>
    private UnityEngine.AudioSource voice;
    private int spoken = -1;
    public double volume = 1;
    private bool cutting = false;
    private double nextFlash = 1.6;
    /// <summary>the bolt of the last strike, and where the next one should come down once the camera is in place</summary>
    private THREE.Mesh bolt;
    private readonly THREE.MeshBasicMaterial boltMat = new THREE.MeshBasicMaterial { color = 0xf4f8ff, transparent = true, opacity = 0, fog = false, side = THREE.Side.DoubleSide, depthWrite = false };
    private bool strike = false;
    /// <summary>seconds since the last strike, for the flicker and the shake that follow it</summary>
    private double sinceStrike = 10;
    /// <summary>0..1: how hard it is blowing</summary>
    public double gale => shot <= 1 ? 1 : 0;

    /// <summary>For the smoke test: in the first shot, looks down on the deck from above her quarter instead.</summary>
    internal int smokeView = 0;

    /// <summary>For the smoke test: lightning now.</summary>
    internal void smokeStrike() => nextFlash = 0;

    /// <summary>
    /// A bolt of lightning: a jagged line from the clouds to the sea some way off in front of the camera, with a fork
    /// leaving it part of the way down. Two crossed ribbons, so it has width from any side.
    /// </summary>
    private void buildBolt(THREE.Vector3 from, THREE.Vector3 look)
    {
        if (bolt != null) group.remove(bolt);
        double aim = Math.Atan2(look.x - from.x, look.z - from.z) + (JS.Random() - 0.5) * 1.5;
        double dist = 55 + JS.Random() * 60;
        double bx = from.x + Math.Sin(aim) * dist, bz = from.z + Math.Cos(aim) * dist;
        var pos = new List<double>();
        var idx = new List<int>();
        void ribbon(double x0, double y0, double z0, double x1, double z1, double y1, int steps, double width, double wander)
        {
            double px = x0, pz = z0;
            foreach (var (ax, az) in new[] { (1.0, 0.0), (0.0, 1.0) })
            {
                int start = pos.Count / 3;
                double sx = x0, sz = z0;
                var seed = Stones.rng((long)(Math.Abs(x0 * 31 + z0 * 17 + y0) * 100) + 7);
                for (int i = 0; i <= steps; i++)
                {
                    double t = (double)i / steps;
                    double x = x0 + (x1 - x0) * t + (i == 0 || i == steps ? 0 : (seed() - 0.5) * wander);
                    double z = z0 + (z1 - z0) * t + (i == 0 || i == steps ? 0 : (seed() - 0.5) * wander);
                    double y = y0 + (y1 - y0) * t;
                    double w = width * (1 - t * 0.6);
                    pos.AddRange(new[] { x - ax * w, y, z - az * w, x + ax * w, y, z + az * w });
                    if (i > 0)
                    {
                        int a = start + (i - 1) * 2;
                        idx.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
                    }
                    sx = x; sz = z;
                }
                px = sx; pz = sz;
            }
        }
        double top = 58 + JS.Random() * 14;
        ribbon(bx + (JS.Random() - 0.5) * 16, top, bz + (JS.Random() - 0.5) * 16, bx, bz, WATER_LEVEL, 13, 0.55, 7);
        // a fork that leaves the main stroke and dies out before it reaches the water
        double fy = top * (0.45 + JS.Random() * 0.2);
        ribbon(bx + (JS.Random() - 0.5) * 6, fy, bz + (JS.Random() - 0.5) * 6, bx + (JS.Random() - 0.5) * 34, bz + (JS.Random() - 0.5) * 34, fy * 0.25, 7, 0.28, 5);
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setIndex(idx);
        g.computeVertexNormals();
        bolt = new THREE.Mesh(g, boltMat);
        bolt.frustumCulled = false;
        bolt.renderOrder = 5;
        group.add(bolt);
    }

    public Opening()
    {
        buildShip();
        buildReef();
        group.add(ship);
    }

    // ---------------------------------------------------------------- the Marigold

    /// <summary>
    /// The Marigold, bow toward -z and the waterline at y = 0: an old three-masted merchantman, hand-built in <see cref="OldShip"/>
    /// (planked hull, beakhead and figurehead, gun ports, stern gallery, rigging, patched canvas, lanterns at the mastheads and stern).
    /// </summary>
    private void buildShip()
    {
        OldShip.build(ship, sails, lamps);
        ship.rotation.order = "YXZ";
    }

    /// <summary>The reef: black rocks standing just clear of the water.</summary>
    private void buildReef()
    {
        var mat = new THREE.MeshStandardMaterial { color = 0x23262a, roughness = 1, flatShading = true };
        var geo = new THREE.DodecahedronGeometry(1, 1);
        foreach (var (dx, dz, s) in new[] { (-3.5, -6.0, 2.6), (1.5, -8.5, 3.4), (5.5, -5.0, 2.2), (-7.0, -9.5, 1.8), (8.5, -9.0, 2.8), (-1.0, -12.0, 2.0) })
        {
            var m = new THREE.Mesh(geo, mat);
            m.scale.set(s, s * 0.7, s * 1.2);
            m.rotation.set(dx, dz, s);
            m.position.set(REEF.x + dx, WATER_LEVEL - s * 0.15, REEF.z + dz);
            group.add(m);
        }
    }

    // ---------------------------------------------------------------- playing it

    /// <summary>Jumps to the end.</summary>
    public void skip() => time = LENGTH;

    /// <summary>Speaks caption number i, if its recording is there.</summary>
    private void say(int i)
    {
        var clip = UnityEngine.Resources.Load<UnityEngine.AudioClip>("Voice/opening-" + (i + 1));
        AutoShot.Trace(clip != null ? $"narrator: line {i + 1}, {clip.length:0.0} s" : $"narrator: no recording for line {i + 1}");
        if (clip == null) return;
        if (voice == null)
        {
            var go = new UnityEngine.GameObject("OpeningVoice");
            voice = go.AddComponent<UnityEngine.AudioSource>();
            voice.spatialBlend = 0;
        }
        voice.Stop();
        voice.clip = clip;
        voice.volume = (float)Math.Min(1, volume);
        voice.Play();
    }

    /// <summary>The opening is over or was skipped: the narrator stops mid-word if need be.</summary>
    public void dispose()
    {
        if (voice == null) return;
        voice.Stop();
        UnityEngine.Object.Destroy(voice.gameObject);
        voice = null;
    }

    /// <summary>
    /// Moves the opening on and puts the camera where this moment wants it. `fade` blacks the screen out and calls back at the
    /// darkest point (used at each cut); `thunder` is called with each flash of lightning.
    /// </summary>
    public void update(double dt, THREE.PerspectiveCamera cam, double seaLevel, Func<double, double, double> swell, Func<double, double, double> groundAt, Action<Action> fade, Action thunder)
    {
        if (shot < 0) enter(0);
        time += dt;
        // each cut goes through black; the next shot is set up while nothing can be seen
        if (!cutting && shot < CUTS.Length - 1 && time >= CUTS[shot] - 0.32)
        {
            cutting = true;
            int next = shot + 1;
            fade(() =>
            {
                cutting = false;
                enter(next);
            });
        }

        caption = null;
        for (int i = 0; i < CAPTIONS.Length; i++)
        {
            if (time < CAPTIONS[i].from || time >= CAPTIONS[i].to) continue;
            caption = CAPTIONS[i].text;
            if (i != spoken)
            {
                spoken = i;
                say(i);
            }
        }

        // lightning, only while the storm lasts: a strike is two or three quick flickers, then the dark again
        sinceStrike += dt;
        if (shot <= 1)
        {
            nextFlash -= dt;
            if (nextFlash <= 0)
            {
                sinceStrike = 0;
                strike = true;
                nextFlash = 1.7 + JS.Random() * 2.6;
                thunder();
            }
        }
        double f = sinceStrike;
        flash = shot > 1 ? 0 : f < 0.07 ? 1 : f < 0.13 ? 0.25 : f < 0.2 ? 0.85 : f < 0.27 ? 0.2 : f < 0.33 ? 0.55 : Math.Max(0, 0.4 - (f - 0.33) * 1.6);
        boltMat.opacity = f < 0.33 ? Math.Min(1, flash * 1.3) : 0;

        var look = new THREE.Vector3();
        if (shot == 0)
        {
            // running before the storm: the ship drives toward the reef, pitching and rolling, and the camera circles her low
            double s = time / CUTS[0];
            double sx = REEF.x + 6, sz = REEF.z + 66 - s * 48;
            // she rides on the sea where she is: rising and falling with it, never pushed down into it, so no water comes
            // aboard. Her pitch and roll are kept small enough that the rail never dips under the surface beside her
            double y = seaLevel + swell(sx, sz);
            ship.position.set(sx, y, sz);
            ship.rotation.set(Math.Sin(time * 1.25) * 0.1, 0.08 + Math.Sin(time * 0.5) * 0.05, Math.Sin(time * 0.9 + 1) * 0.17);
            double a = 2.2 - s * 1.1;
            cam.position.set(sx + Math.Cos(a) * 19, seaLevel + 2.8 + Math.Sin(time * 1.1) * 0.9, sz + Math.Sin(a) * 19 - 4);
            look.set(sx, y + 4, sz - 2);
            if (smokeView == 1) { cam.position.set(sx + 6, y + 8, sz + 10); look.set(sx, y + 1.5, sz - 1); }
            else if (smokeView == 2) { cam.position.set(sx + 9, y + 1.2, sz + 1); look.set(sx, y + 0.6, sz); }
        }
        else if (shot == 1)
        {
            // on the reef: she heels over and settles by the head while the seas break over her
            double s = Math.Min(1, (time - CUTS[0]) / (CUTS[1] - CUTS[0]));
            double e = s * s * (3 - 2 * s);
            double sx = REEF.x, sz = REEF.z - 1 - e * 1.5;
            ship.position.set(sx, seaLevel - 0.4 - e * 2.4 + Math.Sin(time * 2.2) * 0.12 * (1 - e), sz);
            ship.rotation.set(-0.12 - e * 0.38 + Math.Sin(time * 1.7) * 0.03, 0.25, 0.2 + e * 0.75);
            double a = 0.7 + s * 0.5;
            cam.position.set(sx + Math.Cos(a) * 15, seaLevel + 3.2 + s * 1.2, sz + Math.Sin(a) * 15);
            look.set(sx, seaLevel + 2.5 - e * 1.2, sz);
        }
        else if (shot == 2)
        {
            // long afterwards, at dawn: in over the calm water toward the beach
            double s = (time - CUTS[1]) / (CUTS[2] - CUTS[1]);
            double z = 150 - s * 70;
            cam.position.set(18 - s * 10, seaLevel + 5.5 - s * 2.6, z);
            look.set(0, 2, CAMP_Z);
        }
        else
        {
            // the camp: a slow turn round it, as on the title screen
            double s = time - CUTS[2];
            double a = 0.9 + s * 0.06;
            double x = 2 + Math.Cos(a) * 13, z = CAMP_Z + 5 + Math.Sin(a) * 13;
            cam.position.set(x, groundAt(x, z) + 3.0, z);
            look.set(0, groundAt(0, CAMP_Z) + 1.6, CAMP_Z);
        }
        if (shot <= 1)
        {
            // the camera rides the same sea, and a strike close by jolts it
            double jolt = Math.Max(0, 0.5 - sinceStrike) * 0.5;
            look.x += Math.Sin(time * 31) * jolt;
            look.y += Math.Sin(time * 0.9) * 0.5 + Math.Cos(time * 37) * jolt;
        }
        cam.lookAt(look);
        if (shot <= 1) cam.rotation.z += Math.Sin(time * 0.8) * 0.045;
        if (strike)
        {
            strike = false;
            buildBolt(cam.position, look);
        }
        // the lanterns gutter in the wind
        foreach (var l in lamps) l.scale.setScalar(0.85 + Math.Sin(time * 13 + l.position.y) * 0.15);
    }

    private void enter(int n)
    {
        shot = n;
        bool storm = n <= 1;
        dayTime = storm ? 0.8 : n == 2 ? 0.02 : 0.06;
        raining = storm;
        // the ship and the reef belong to the night of the wreck
        group.visible = storm;
        flash = 0;
    }
}
