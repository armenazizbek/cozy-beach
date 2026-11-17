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

    /// <summary>Eases toward `target` and moves the rain. `cam` is where the rain should fall around.</summary>
    public void update(double dt, double target, THREE.Vector3 cam)
    {
        amount += (target - amount) * Math.Min(1, dt * 0.35);
        if (Math.Abs(amount - target) < 0.002) amount = target;
        bool on = amount > 0.01;
        lines.visible = on;
        if (!on) return;
        mat.opacity = amount * (0.5 + gale * 0.25);
        double fall = 22 + gale * 10, slant = 3 + gale * 15;
        // the pattern leans into the wind, and slides down its own axis at the speed the drops fall along it
        group.rotation.z = -Math.Atan2(slant, fall);
        phase = (phase + Math.Sqrt(fall * fall + slant * slant) * dt) % HEIGHT;
        lines.position.y = -phase;
        group.position.set(cam.x, cam.y - 4, cam.z);
    }
}
