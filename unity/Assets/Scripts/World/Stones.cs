using System;
using System.Collections.Generic;
using System.Linq;
using static Beach;

// Port of src/world/Stones.ts

public class Spot
{
    public double x, z, r;
    public Spot() { }
    public Spot(double x, double z, double r) { this.x = x; this.z = z; this.r = r; }
}

/// <summary>Stones and boulders scattered over the beach. Nothing is buried near them, so they never hide a find.</summary>
public class Stones
{
    /// <summary>tiny deterministic PRNG so the beach looks the same every visit</summary>
    public static Func<double> rng(long seed) => () =>
    {
        seed = (seed * 1664525 + 1013904223) % 4294967296;
        return seed / 4294967296.0;
    };

    public readonly THREE.Group group = new THREE.Group();
    /// <summary>the bigger stones, which block walking and driving</summary>
    public readonly List<Spot> colliders = new List<Spot>();
    private readonly List<THREE.BufferGeometry> shapes = new List<THREE.BufferGeometry>();
    private readonly List<THREE.MeshStandardMaterial> mats = new List<THREE.MeshStandardMaterial>();
    private readonly Beach beach;

    public Stones(Beach beach)
    {
        this.beach = beach;
        var rand = rng(91);
        // a handful of lumpy rock shapes that every stone reuses at different sizes
        for (int v = 0; v < 6; v++)
        {
            var geo = new THREE.IcosahedronGeometry(1, 2);
            var p = geo.attributes.position;
            var seen = new Dictionary<string, double>();
            for (int i = 0; i < p.count; i++)
            {
                string key = $"{JS.Round(p.getX(i) * 1000)},{JS.Round(p.getY(i) * 1000)},{JS.Round(p.getZ(i) * 1000)}";
                if (!seen.TryGetValue(key, out var k))
                {
                    k = 0.82 + rand() * 0.34;
                    seen[key] = k;
                }
                // squashed and lumpy, with a flatter bottom so it sits on the sand
                p.setXYZ(i, p.getX(i) * k * 1.12, Math.Max(p.getY(i), -0.45) * k * 0.72, p.getZ(i) * k);
            }
            geo.computeVertexNormals();
            shapes.Add(geo);
        }
        foreach (var c in new[] { 0x7a7872, 0x65635e, 0x948b7c, 0x5c5a57, 0x877c6b, 0x726859 })
            mats.Add(new THREE.MeshStandardMaterial { color = c, roughness = 0.95, flatShading = true });
    }

    /// <summary>
    /// Lays the stones down, keeping every one well clear of `keepClear` (buried finds, the camp, the cart and so on) so that a
    /// stone can never sit on top of something the player is meant to dig up.
    /// </summary>
    public void scatter(List<Spot> keepClear)
    {
        foreach (var m in group.children.ToArray()) group.remove(m);
        colliders.Clear();
        var rand = rng(1234);
        var placed = new List<Spot>();
        bool ok(double x, double z, double r)
        {
            if (Math.Abs(x) > HALF - 3 || z > SHORE_Z - 6 || z < -(HALF - 4)) return false;
            double h = beach.heightAt(x, z);
            if (h < WATER_LEVEL + 0.35) return false;
            // finds, camp and so on: a generous margin so the stone is never over something buried
            if (keepClear.Any(k => JS.Hypot(k.x - x, k.z - z) < k.r + r + 1.4)) return false;
            if (placed.Any(s => JS.Hypot(s.x - x, s.z - z) < s.r + r + 0.15)) return false;
            return true;
        }
        void lay(double x, double z, double r)
        {
            var mesh = new THREE.Mesh(shapes[(int)Math.Floor(rand() * shapes.Count)], mats[(int)Math.Floor(rand() * mats.Count)]);
            mesh.scale.set(r * (0.9 + rand() * 0.3), r * (0.75 + rand() * 0.3), r * (0.9 + rand() * 0.3));
            mesh.rotation.y = rand() * Math.PI * 2;
            // sunk a little into the sand
            mesh.position.set(x, beach.heightAt(x, z) + r * 0.12, z);
            mesh.castShadow = true;
            mesh.receiveShadow = true;
            group.add(mesh);
            placed.Add(new Spot(x, z, r));
            if (r >= 0.42) colliders.Add(new Spot(x, z, r * 0.85));
        }

        // a few big boulders at random spots over the whole beach, not too close to each other
        double x0 = -(HALF - 8), z0 = -42, z1 = SHORE_Z - 9;
        var spots = new List<Spot>();
        for (int tries = 0; spots.Count < 6 && tries < 600; tries++)
        {
            double r = 0.9 + JS.Random() * 0.7;
            double x = x0 + JS.Random() * 2 * (HALF - 8);
            double z = z0 + JS.Random() * (z1 - z0);
            if (spots.Any(q => JS.Hypot(q.x - x, q.z - z) < 14)) continue;
            if (!ok(x, z, r)) continue;
            lay(x, z, r);
            spots.Add(new Spot(x, z, r));
        }
        // and boulders out across the rest of the land, alone and in small heaps
        var far = rng(5678);
        for (int n = 0, tries = 0; n < 34 && tries < 3000; tries++)
        {
            double a = far() * Math.PI * 2, d = 70 + far() * 140;
            double x = Math.Sin(a) * d, z = -Math.Abs(Math.Cos(a)) * d + 25;
            double r = 0.5 + far() * 1.2;
            if (Math.Abs(x) < HALF && z > -HALF) continue;
            if (z > SHORE_Z - 6 || beach.heightAt(x, z) < WATER_LEVEL + 0.35 || !Scenery.onLand(x, z, r + 3)) continue;
            if (keepClear.Any(k => JS.Hypot(k.x - x, k.z - z) < k.r + r + 1.4)) continue;
            if (placed.Any(s => JS.Hypot(s.x - x, s.z - z) < s.r + r + 0.15)) continue;
            lay(x, z, r);
            int heap = far() < 0.4 ? 1 + (int)Math.Floor(far() * 3) : 0;
            for (int k = 0; k < heap; k++)
            {
                double ka = far() * Math.PI * 2, kr = 0.3 + far() * 0.5;
                double kx = x + Math.Cos(ka) * (r + kr), kz = z + Math.Sin(ka) * (r + kr);
                if (!placed.Any(s => JS.Hypot(s.x - kx, s.z - kz) < s.r + kr + 0.1)) lay(kx, kz, kr);
            }
            n++;
        }
    }
}
