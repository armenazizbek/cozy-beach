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
    /// Whether a day has a rainy spell, and when in the day it falls. Rain is rare: about one day in seven,
    /// never on the first day, and it always arrives and leaves by the same hash so a saved game agrees.
    /// </summary>
    public static RainWindow rainWindow(int day)
    {
        if (day <= 1) return null;
        double h = Math.Sin(day * 127.1 + 311.7) * 43758.5453;
        double r = h - Math.Floor(h);
        if (r > 0.14) return null;
        double s = Math.Sin(day * 269.5 + 183.3) * 43758.5453;
        double start = 0.08 + (s - Math.Floor(s)) * 0.32;
        return new RainWindow { start = start, end = start + 0.26 };
    }

    public readonly THREE.Group group = new THREE.Group();
    /// <summary>0 dry .. 1 pouring</summary>
    public double amount = 0;
    private readonly THREE.LineSegments lines;
    private readonly float[] pos;
    private readonly double[] seeds;
    private readonly THREE.LineBasicMaterial mat = new THREE.LineBasicMaterial { color = 0xcfe0ee, transparent = true, opacity = 0, depthWrite = false, fog = false };

    public Rain()
    {
        pos = new float[DROPS * 6];
        seeds = new double[DROPS * 3];
        for (int i = 0; i < DROPS; i++)
        {
            seeds[i * 3] = (JS.Random() * 2 - 1) * BOX;
            seeds[i * 3 + 1] = JS.Random() * HEIGHT;
            seeds[i * 3 + 2] = (JS.Random() * 2 - 1) * BOX;
        }
        var geo = new THREE.BufferGeometry();
        geo.setAttribute("position", new THREE.BufferAttribute(pos, 3));
        lines = new THREE.LineSegments(geo, mat) { frustumCulled = false, visible = false };
        group.add(lines);
    }

    /// <summary>Eases toward `target` and moves the drops. `cam` is where the rain should fall around.</summary>
    public void update(double dt, double target, THREE.Vector3 cam)
    {
        amount += (target - amount) * Math.Min(1, dt * 0.35);
        if (Math.Abs(amount - target) < 0.002) amount = target;
        bool on = amount > 0.01;
        lines.visible = on;
        if (!on) return;
        mat.opacity = amount * 0.5;
        const double fall = 22, slant = 3;
        for (int i = 0; i < DROPS; i++)
        {
            double y = seeds[i * 3 + 1] - fall * dt;
            double x = seeds[i * 3] - slant * dt;
            if (y < 0)
            {
                y += HEIGHT;
                x = (JS.Random() * 2 - 1) * BOX;
                seeds[i * 3 + 2] = (JS.Random() * 2 - 1) * BOX;
            }
            if (x < -BOX) x += BOX * 2;
            seeds[i * 3] = x;
            seeds[i * 3 + 1] = y;
            double z = seeds[i * 3 + 2];
            double wx = cam.x + x, wy = cam.y - 4 + y, wz = cam.z + z;
            pos[i * 6] = (float)wx;
            pos[i * 6 + 1] = (float)wy;
            pos[i * 6 + 2] = (float)wz;
            pos[i * 6 + 3] = (float)(wx + slant * 0.03);
            pos[i * 6 + 4] = (float)(wy + 0.7);
            pos[i * 6 + 5] = (float)wz;
        }
        lines.geometry.attributes.position.needsUpdate = true;
    }
}
