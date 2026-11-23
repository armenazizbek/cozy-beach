using System.Collections.Generic;
using System;

// Port of src/world/Rain.ts

public class RainWindow
{
    public double start, end;
}

/// <summary>Rain streaks that stay around the camera, plus the smoothed 0..1 amount of rain.</summary>
public class Rain
{
    private const int DROPS = 1600;
    private const double BOX = 15;
    private const double HEIGHT = 18;

    /// <summary>
    /// Whether a day has a rainy spell, and when in the day it falls: about one day in three (it was one in seven), never on
    /// the first day, for about a third of the day. It always arrives and leaves by the same hash so a saved game agrees.
    /// </summary>
    /// <summary>the share of days with rain, and how much of such a day the rain lasts</summary>
    public const double RAINY_DAYS = 0.34, RAIN_SPELL = 0.32;

    public static RainWindow rainWindow(int day)
    {
        if (day <= 1) return null;
        // this hash spreads the rainy days evenly: the first falls on day 5, and no dry spell is longer than 12 days
        double h = Math.Sin(day * 91.7 + 17.3) * 43758.5453;
        double r = h - Math.Floor(h);
        if (r > RAINY_DAYS) return null;
        double s = Math.Sin(day * 269.5 + 183.3) * 43758.5453;
        double start = 0.08 + (s - Math.Floor(s)) * 0.3;
        return new RainWindow { start = start, end = start + RAIN_SPELL };
    }

    public readonly THREE.Group group = new THREE.Group();
    /// <summary>0 dry .. 1 pouring</summary>
    public double amount = 0;
    /// <summary>0 for ordinary rain; 1 for a storm, when it comes down harder and is blown almost sideways</summary>
    public double gale = 0;
    private readonly THREE.LineSegments lines;
    /// <summary>rain around a shelter (the old house), fixed in the world with a hole where the roof is, so a player
    /// indoors sees it come down past the door and the windows and not inside the room</summary>
    private THREE.LineSegments shelterLines;
    private readonly THREE.Group shelterGroup = new THREE.Group();
    private double shelterX, shelterZ;
    private readonly THREE.LineBasicMaterial mat = new THREE.LineBasicMaterial { color = 0xcfe0ee, transparent = true, opacity = 0, depthWrite = false, fog = false };

    // The drops are built once and never rewritten: a fixed pattern of streaks, twice, one above the other (each one
    // HEIGHT tall), hung under a group that follows the camera, leans with the wind and slides along its own long axis.
    // Sliding it by less than HEIGHT and wrapping makes the pattern fall for ever. This used to move 1,600 drops by hand
    // and upload 3,200 vertices every frame.
    private double phase;

    public Rain()
    {
        const double STREAK = 0.75;
        var pos = new float[DROPS * 2 * 6];
        for (int i = 0; i < DROPS; i++)
        {
            double x = (JS.Random() * 2 - 1) * BOX, y = JS.Random() * HEIGHT, z = (JS.Random() * 2 - 1) * BOX;
            for (int copy = 0; copy < 2; copy++)
            {
                int o = (copy * DROPS + i) * 6;
                pos[o] = (float)x; pos[o + 1] = (float)(y + copy * HEIGHT); pos[o + 2] = (float)z;
                pos[o + 3] = (float)x; pos[o + 4] = (float)(y + copy * HEIGHT + STREAK); pos[o + 5] = (float)z;
            }
        }
        var geo = new THREE.BufferGeometry();
        geo.setAttribute("position", new THREE.BufferAttribute(pos, 3));
        lines = new THREE.LineSegments(geo, mat) { frustumCulled = false, visible = false };
        group.add(lines);
    }

    /// <summary>
    /// Builds the rain seen from under a roof: drops all round (x, z) out to the edge of the rain box, but none where
    /// <paramref name="covered"/> says the roof is. Add <see cref="shelter"/> to the scene beside <see cref="group"/>.
    /// </summary>
    public THREE.Group buildShelter(double x, double z, Func<double, double, bool> covered)
    {
        const double STREAK = 0.75;
        shelterX = x;
        shelterZ = z;
        var pts = new List<float>(DROPS * 12);
        int made = 0;
        for (int tries = 0; made < DROPS && tries < DROPS * 8; tries++)
        {
            double dx = (JS.Random() * 2 - 1) * BOX, dz = (JS.Random() * 2 - 1) * BOX;
            if (covered(x + dx, z + dz)) continue;
            double y = JS.Random() * HEIGHT;
            for (int copy = 0; copy < 2; copy++)
            {
                pts.Add((float)dx); pts.Add((float)(y + copy * HEIGHT)); pts.Add((float)dz);
                pts.Add((float)dx); pts.Add((float)(y + copy * HEIGHT + STREAK)); pts.Add((float)dz);
            }
            made++;
        }
        var geo = new THREE.BufferGeometry();
        geo.setAttribute("position", new THREE.BufferAttribute(pts.ToArray(), 3));
        shelterLines = new THREE.LineSegments(geo, mat) { frustumCulled = false, visible = false };
        shelterGroup.add(shelterLines);
        return shelterGroup;
    }

    /// <summary>Eases toward `target` and moves the rain. `cam` is where the rain should fall around.</summary>
    /// <param name="where">"open": falls round the camera; "shelter": the camera is under the roof built by
    /// <see cref="buildShelter"/>, so it falls round that instead; "none": not seen at all (under the sea, in the cellar)</param>
    /// <summary>The rain's GameObjects (native since unity/NATIVE_PLAN.md S11), once placed.</summary>
    private Realized live, shelterLive;

    /// <summary>Puts the rain into the world as native objects (after <see cref="buildShelter"/>).</summary>
    public void Place()
    {
        live = Realizer.Realize(group, World.Land, World.Layer).Follow(group, lines);
        if (shelterLines != null) shelterLive = Realizer.Realize(shelterGroup, World.Land, World.Layer).Follow(shelterGroup, shelterLines);
    }

    public void update(double dt, double target, THREE.Vector3 cam, string where = "open")
    {
        Fall(dt, target, cam, where);
        live?.Apply();
        shelterLive?.Apply();
        if (live != null) MaterialKit.Push(mat, MaterialKit.On.Line);
    }

    private void Fall(double dt, double target, THREE.Vector3 cam, string where)
    {
        amount += (target - amount) * Math.Min(1, dt * 0.35);
        if (Math.Abs(amount - target) < 0.002) amount = target;
        bool on = amount > 0.01;
        bool sheltered = where == "shelter" && shelterLines != null;
        lines.visible = on && where == "open";
        if (shelterLines != null) shelterLines.visible = on && sheltered;
        if (!on) return;
        mat.opacity = amount * (0.5 + gale * 0.25);
        double fall = 22 + gale * 10, slant = 3 + gale * 15;
        // the pattern leans into the wind, and slides down its own axis at the speed the drops fall along it
        group.rotation.z = -Math.Atan2(slant, fall);
        phase = (phase + Math.Sqrt(fall * fall + slant * slant) * dt) % HEIGHT;
        lines.position.y = -phase;
        group.position.set(cam.x, cam.y - 4, cam.z);
        if (sheltered)
        {
            // hardly any lean here: a drop blown far sideways would end up inside the room
            shelterGroup.rotation.z = group.rotation.z * 0.25;
            shelterLines.position.y = -phase;
            shelterGroup.position.set(shelterX, cam.y - 4, shelterZ);
        }
    }
}
