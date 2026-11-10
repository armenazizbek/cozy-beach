using System;
using System.Collections.Generic;
using static Beach;

/// <summary>
/// The wooden fishing pier: a ramp up from the sand, a deck on posts out over the water, a rope rail down one side and, at the
/// far end, a bench to fish from with a bucket beside it. The boat is moored along its open side. Everything is in world space.
/// </summary>
public class Pier
{
    /// <summary>the middle line of the pier, and half its width</summary>
    public const double X = -12, HALF_W = 1.3;
    /// <summary>where it starts on the sand and where it ends out at sea</summary>
    public const double Z0 = 36.5, Z1 = 62;
    /// <summary>the height of the deck, well clear of the highest tide and swell</summary>
    public const double DECK = 0.62;
    /// <summary>how long the ramp from the sand up to the deck is</summary>
    private const double RAMP = 3;

    private static readonly double sand0 = baseHeight(X, Z0);

    /// <summary>Height of the boards at a point along the pier: the ramp first, then the level deck.</summary>
    public static double deckAt(double z)
    {
        double k = Math.Min(1, Math.Max(0, (z - Z0) / RAMP));
        k = k * k * (3 - 2 * k);
        return sand0 + 0.04 + (DECK - sand0 - 0.04) * k;
    }

    /// <summary>True over the boards, `margin` in from their edges.</summary>
    public static bool over(double x, double z, double margin = 0) => Math.Abs(x - X) <= HALF_W - margin && z >= Z0 && z <= Z1 - margin;

    /// <summary>True where the pier and its posts are in a boat's way.</summary>
    public static bool blocks(double x, double z) => Math.Abs(x - X) < HALF_W + 0.3 && z > Z0 && z < Z1 + 0.3;

    /// <summary>Where the boat is tied up in a new game: along the open side, bow to the sea.</summary>
    public static readonly (double x, double z, double heading) MOORING = (X - HALF_W - 1.35, 47, Math.PI);

    /// <summary>The bench at the end: where the eyes are when sitting on it, and the way it faces (out to sea).</summary>
    public static readonly (double x, double y, double z, double yaw) SEAT = (X + 0.4, DECK + 1.08, Z1 - 1.25, Math.PI);
    /// <summary>where the player stands after getting up</summary>
    public static readonly (double x, double z) STAND = (X + 0.4, Z1 - 2.4);
    /// <summary>the bucket beside the bench: the middle of its floor</summary>
    public static readonly THREE.Vector3 BUCKET = new THREE.Vector3(X - 0.55, DECK, Z1 - 0.5);

    /// <summary>The spot on the deck to step onto from a boat alongside, or null when the boat is not at the pier.</summary>
    public static (double x, double z)? landingFor(double boatX, double boatZ)
    {
        if (boatZ < Z0 + RAMP || boatZ > Z1 + 2.5 || Math.Abs(boatX - X) > HALF_W + 4.5) return null;
        return (X, Math.Min(Z1 - 0.8, boatZ));
    }

    public readonly THREE.Group group = new THREE.Group { name = "Pier" };

    private static THREE.Mesh box(double w, double h, double d, THREE.Material mat, double x, double y, double z)
    {
        var m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat);
        m.position.set(x, y, z);
        m.castShadow = true;
        m.receiveShadow = true;
        return m;
    }

    private static THREE.Mesh post(double r, double y0, double y1, THREE.Material mat, double x, double z)
    {
        var m = new THREE.Mesh(new THREE.CylinderGeometry(r, r * 1.08, y1 - y0, 10), mat);
        m.position.set(x, (y0 + y1) / 2, z);
        m.castShadow = true;
        m.receiveShadow = true;
        return m;
    }

    public Pier()
    {
        var plankA = new THREE.MeshStandardMaterial { color = 0xa9865c, roughness = 0.95 };
        var plankB = new THREE.MeshStandardMaterial { color = 0x9a7850, roughness = 0.95 };
        var dark = new THREE.MeshStandardMaterial { color = 0x5e4128, roughness = 0.9 };
        var rope = new THREE.MeshStandardMaterial { color = 0xd8c49a, roughness = 1 };
        var iron = new THREE.MeshStandardMaterial { color = 0x8a9299, roughness = 0.5, metalness = 0.6 };
        var water = new THREE.MeshStandardMaterial { color = 0x4f9fc0, roughness = 0.15, metalness = 0.1 };

        // the boards, laid across; no two quite alike
        int n = 0;
        for (double z = Z0 + 0.14; z < Z1; z += 0.31, n++)
        {
            double w = HALF_W * 2 + (n * 37 % 7 - 3) * 0.012;
            var b = box(w, 0.05, 0.28, n % 3 == 1 ? plankB : plankA, X + (n * 53 % 5 - 2) * 0.008, deckAt(z) - 0.025, z);
            b.rotation.y = (n * 29 % 9 - 4) * 0.0016;
            // the ramp's boards follow its slope
            b.rotation.x = -Math.Atan((deckAt(z + 0.15) - deckAt(z - 0.15)) / 0.3);
            group.add(b);
        }
        // two long beams under the level part of the deck
        double beamZ0 = Z0 + RAMP - 0.5;
        foreach (var sx in new[] { -1, 1 })
            group.add(box(0.13, 0.15, Z1 - beamZ0, dark, X + sx * (HALF_W - 0.28), DECK - 0.125, (beamZ0 + Z1) / 2));

        // posts in pairs, down to the sea floor; on the rail side every post stands tall to carry the rope
        var tall = new List<THREE.Vector3>();
        for (double z = Z0 + RAMP; z <= Z1 + 0.01; z += 4.5)
        {
            double zz = Math.Min(z, Z1 - 0.12);
            foreach (var sx in new[] { -1, 1 })
            {
                double x = X + sx * (HALF_W - 0.06);
                double floor = baseHeight(x, zz) - 0.5;
                double top = sx > 0 ? DECK + 0.85 : DECK + 0.1;
                group.add(post(0.1, floor, top, dark, x, zz));
                // a crosspiece under the deck ties each pair together
                if (sx > 0)
                {
                    group.add(box(HALF_W * 2 - 0.1, 0.12, 0.12, dark, X, DECK - 0.25, zz));
                    tall.Add(new THREE.Vector3(x, top - 0.1, zz));
                }
            }
        }
        // the rope rail, sagging a little between the tall posts
        for (int i = 0; i + 1 < tall.Count; i++)
        {
            var a = tall[i];
            var b = tall[i + 1];
            var mid = a.clone().add(b).multiplyScalar(0.5);
            mid.y -= 0.16;
            var m = new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(new[] { a, mid, b }), 10, 0.022, 6), rope);
            m.castShadow = true;
            group.add(m);
        }
        // two mooring posts on the open side, with a rope coiled round each
        foreach (var z in new[] { MOORING.z - 1.6, MOORING.z + 1.6 })
        {
            double x = X - HALF_W + 0.16;
            group.add(post(0.09, DECK - 0.05, DECK + 0.42, dark, x, z));
            var coil = new THREE.Mesh(new THREE.TorusGeometry(0.11, 0.025, 6, 14), rope);
            coil.rotation.x = Math.PI / 2;
            coil.position.set(x, DECK + 0.2, z);
            group.add(coil);
        }

        // the bench at the end, facing out to sea
        double bx = SEAT.x, bz = SEAT.z + 0.05;
        group.add(box(1.05, 0.07, 0.4, plankB, bx, DECK + 0.45, bz));
        foreach (var sx in new[] { -1, 1 })
            group.add(box(0.08, 0.42, 0.34, dark, bx + sx * 0.42, DECK + 0.21, bz));
        // a low back rest. The posts it sits on are deeper and shorter than the board, so no two faces of the bench lie in
        // the same plane (that made them flicker against each other).
        group.add(box(1.05, 0.16, 0.05, plankA, bx, DECK + 0.72, bz - 0.22));
        foreach (var sx in new[] { -1, 1 })
            group.add(box(0.06, 0.36, 0.08, dark, bx + sx * 0.42, DECK + 0.58, bz - 0.22));

        // the bucket beside it: a tin pail of sea water for the catch
        var pail = new THREE.Mesh(new THREE.CylinderGeometry(0.19, 0.15, 0.32, 16, 1, true), new THREE.MeshStandardMaterial { color = 0x9aa3ab, roughness = 0.45, metalness = 0.7, side = THREE.Side.DoubleSide });
        pail.position.set(BUCKET.x, BUCKET.y + 0.16, BUCKET.z);
        pail.castShadow = true;
        var pailFloor = new THREE.Mesh(new THREE.CylinderGeometry(0.15, 0.15, 0.02, 16), iron);
        pailFloor.position.set(BUCKET.x, BUCKET.y + 0.01, BUCKET.z);
        var pailWater = new THREE.Mesh(new THREE.CylinderGeometry(0.175, 0.175, 0.01, 16), water);
        pailWater.position.set(BUCKET.x, BUCKET.y + 0.24, BUCKET.z);
        var handle = new THREE.Mesh(new THREE.TorusGeometry(0.19, 0.008, 5, 16, Math.PI), iron);
        handle.position.set(BUCKET.x, BUCKET.y + 0.32, BUCKET.z);
        handle.rotation.set(0.5, 0.4, 0);
        group.add(pail, pailFloor, pailWater, handle);

        // everything here stands still, so pieces of the same wood become one mesh each
        Merge.mergeStatic(group);
    }
}
