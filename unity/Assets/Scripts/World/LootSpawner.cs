using System;
using System.Collections.Generic;
using System.Linq;
using static Beach;

// Port of src/world/LootSpawner.ts

public class BuriedItem
{
    public int id;
    public ItemDef def;
    public double x, z, targetDepth;
    public bool revealed, collected;
    public THREE.Group mesh;
    /// <summary>persistent brush progress so leaving the workbench never loses it</summary>
    public HTMLCanvasElement crustCanvas;
    public double popT;
    /// <summary>the captain's hatch has been lifted, showing the strongbox</summary>
    public bool opened;
}

public class LootSpawner
{
    private static double rand(double a, double b) => a + JS.Random() * (b - a);

    private class KeepOut
    {
        public double x, z, r;
        public BuriedItem except;
    }

    public readonly THREE.Group group = new THREE.Group();
    public List<BuriedItem> items = new List<BuriedItem>();
    private int nextId = 1;
    /// <summary>circles nothing may be buried in: under the grate, and around the hatch</summary>
    private readonly List<KeepOut> keepOut = new List<KeepOut>();
    /// <summary>the buried chests of the treasure map only exist for someone who holds the map</summary>
    public bool secretsOpen = false;
    /// <summary>the captain's strongbox only exists for someone who holds the map from the bottle</summary>
    public bool bottleOpen = false;
    /// <summary>how high the ground is at a point</summary>
    public Func<double, double, double> ground;
    private readonly Beach beach;
    private readonly StateManager state;

    public LootSpawner(Beach beach, StateManager state)
    {
        this.beach = beach;
        this.state = state;
        ground = (x, z) => beach.heightAt(x, z);
    }

    private bool locked(BuriedItem it) =>
        (it.def.id == "treasure_cache" && !secretsOpen) || (it.def.id == "captain_chest" && !bottleOpen);

    /// <summary>Buries every find on the beach once, at the start. Nothing is ever re-rolled.</summary>
    public void populate()
    {
        items = new List<BuriedItem>();
        var picks = new List<ItemDef>();
        var common = Items.ITEM_DEFS.Where(d => d.weight > 0).ToArray();
        double total = common.Sum(d => d.weight);
        ItemDef roll()
        {
            double r = JS.Random() * total;
            foreach (var d in common)
            {
                r -= d.weight;
                if (r <= 0) return d;
            }
            return common[0];
        }
        for (int i = 0; i < 330; i++) picks.Add(roll());

        // every quest item still wanted, and every shard of every unfinished set, so nothing is unfindable
        foreach (var id in state.activeQuestDefIds())
            if (Items.DEF_BY_ID.TryGetValue(id, out var def)) picks.Add(def);
        foreach (var set in Items.SHARD_SETS)
        {
            if (state.data.assembled.Contains(set.id)) continue;
            for (int i = 0; i < 3; i++)
                if (!state.hasShard(set.id, i)) picks.Add(Items.DEF_BY_ID[$"{set.id}_{i + 1}"]);
        }
        // a few extra treasures for the patient
        foreach (var id in new[] { "doubloon", "doubloon", "sovereign", "denarius", "silver_dollar", "ruby_band", "signet" }) picks.Add(Items.DEF_BY_ID[id]);

        var placed = items.Select(i => (i.x, i.z)).ToList();
        foreach (var def in picks)
        {
            double x = 0, z = 0;
            for (int tries = 0; tries < 30; tries++)
            {
                x = rand(-(HALF - 4), HALF - 4);
                if (def.zone == "wet") z = rand(WET_Z + 0.5, SHORE_Z - 3);
                else if (def.zone == "dry") z = rand(DIG_MIN_Z + 1.5, 6);
                else z = rand(DIG_MIN_Z + 1, SHORE_Z - 3);
                double cx = x, cz = z;
                if (placed.All(p => JS.Hypot(p.x - cx, p.z - cz) > 2.2)) break;
            }
            placed.Add((x, z));
            items.Add(new BuriedItem { id = nextId++, def = def, x = x, z = z, targetDepth = rand(def.depth[0], def.depth[1]), revealed = false, collected = false, popT = 0 });
        }
    }

    /// <summary>Buries a find at a chosen spot.</summary>
    public void addBuried(ItemDef def, double x, double z, double depth)
    {
        var it = new BuriedItem { id = nextId++, def = def, x = x, z = z, targetDepth = depth, revealed = false, collected = false, popT = 0 };
        items.Add(it);
        if (inKeepOut(it.x, it.z, it)) rebury(it);
    }

    /// <summary>
    /// Keeps every find out of a circle, now and from then on: whatever lies there, even one already
    /// dug up but not taken, is buried again a little way off. Nothing can end up stuck under the
    /// grate or beside the hatch, where it could never be dug out and the story could not be finished.
    /// </summary>
    public void keepClear(double x, double z, double r, BuriedItem except = null)
    {
        keepOut.Add(new KeepOut { x = x, z = z, r = r, except = except });
        foreach (var it in items)
        {
            if (it.collected || it.targetDepth < 0 || !inKeepOut(it.x, it.z, it)) continue;
            rebury(it);
        }
    }

    private bool inKeepOut(double x, double z, BuriedItem it) => keepOut.Any(k => k.except != it && JS.Hypot(x - k.x, z - k.z) < k.r);

    /// <summary>Moves a find to the nearest open spot on the diggable beach, outside every kept-clear circle.</summary>
    private void rebury(BuriedItem it)
    {
        disposeMesh(it);
        it.revealed = false;
        it.popT = 0;
        double zMin = it.def.zone == "wet" ? WET_Z + 0.5 : DIG_MIN_Z + 1;
        double zMax = it.def.zone == "dry" ? 6 : SHORE_Z - 3;
        bool free(double x, double z, double gap) =>
            Math.Abs(x) < HALF - 4 && z > zMin && z < zMax && !inKeepOut(x, z, it) &&
            items.All(o => o == it || o.collected || JS.Hypot(o.x - x, o.z - z) > gap);
        // widening rings around where it was; if the beach there is crowded, settle for less room between finds
        foreach (var gap in new[] { 1.5, 0.6, 0 })
        {
            for (int ring = 1; ring <= 60; ring++)
            {
                double rad = ring * 0.5;
                int n = 8 + ring * 2;
                double off = JS.Random() * Math.PI * 2;
                for (int k = 0; k < n; k++)
                {
                    double a = off + (double)k / n * Math.PI * 2;
                    double x = it.x + Math.Cos(a) * rad, z = it.z + Math.Sin(a) * rad;
                    if (!free(x, z, gap)) continue;
                    it.x = x;
                    it.z = z;
                    return;
                }
            }
        }
    }

    /// <summary>Adds a find that lies out in the open from the start, like the ones on the island.</summary>
    public void addOpen(ItemDef def, double x, double z)
    {
        items.Add(new BuriedItem { id = nextId++, def = def, x = x, z = z, targetDepth = -1, revealed = false, collected = false, popT = 0 });
    }

    public List<SavedItem> exportItems() =>
        items.Select(i => new SavedItem { defId = i.def.id, x = i.x, z = i.z, targetDepth = i.targetDepth, collected = i.collected }).ToList();

    /// <summary>Replaces every find with a saved layout; call checkReveals() after the holes are restored.</summary>
    public void importItems(List<SavedItem> list)
    {
        foreach (var it in items) disposeMesh(it);
        items = new List<BuriedItem>();
        foreach (var s in list)
        {
            if (!Items.DEF_BY_ID.TryGetValue(s.defId, out var def)) continue;
            items.Add(new BuriedItem { id = nextId++, def = def, x = s.x, z = s.z, targetDepth = s.targetDepth, revealed = false, collected = s.collected, popT = 0 });
        }
    }

    private void disposeMesh(BuriedItem it)
    {
        if (it.mesh != null)
        {
            group.remove(it.mesh);
            it.mesh = null;
        }
    }

    /// <summary>Closest still-buried item within `radius` of x,z.</summary>
    public (BuriedItem item, double d)? nearest(double x, double z, double radius)
    {
        (BuriedItem item, double d)? best = null;
        foreach (var it in items)
        {
            if (it.revealed || it.collected) continue;
            if (locked(it)) continue;
            double d = JS.Hypot(it.x - x, it.z - z);
            if (d < radius && (best == null || d < best.Value.d)) best = (it, d);
        }
        return best;
    }

    /// <summary>Reveals any item whose hole is now deep enough. Returns the newly revealed.</summary>
    public List<BuriedItem> checkReveals()
    {
        var fresh = new List<BuriedItem>();
        foreach (var it in items)
        {
            // once found, the hatch always sits at the bottom of a neat pit, however the sand around it was dug
            if (it.revealed && !it.collected && it.def.id == "captain_chest") tidyHatchPit(it);
            if (it.revealed || it.collected) continue;
            if (locked(it)) continue;
            // a buried chest is wide, so a hole dug next to the cross turns it up too, not only one dug exactly on top of it
            bool wide = it.def.id == "treasure_cache" || it.def.id == "captain_chest";
            double depth = beach.depthAt(it.x, it.z);
            if (wide)
                foreach (var (dx, dz) in new[] { (0.45, 0.0), (-0.45, 0.0), (0.0, 0.45), (0.0, -0.45) })
                    depth = Math.Max(depth, beach.depthAt(it.x + dx, it.z + dz) + 0.1);
            if (depth >= it.targetDepth)
            {
                it.revealed = true;
                it.popT = 0;
                if (it.def.id == "captain_chest") tidyHatchPit(it);
                // the strongbox is buried under a wooden hatch; everything else shows as sand-covered
                var mesh = it.def.id == "captain_chest" ? LootGenerator.buildItem("captain_hatch") : LootGenerator.buildDirty(it.def.id);
                var proxy = new THREE.Mesh(new THREE.SphereGeometry(0.65, 8, 6), new THREE.MeshBasicMaterial { visible = false });
                mesh.add(proxy);
                mesh.position.set(it.x, ground(it.x, it.z) + 0.05, it.z);
                mesh.rotation.y = JS.Random() * Math.PI * 2;
                mesh.userData["item"] = it;
                it.mesh = mesh;
                group.add(mesh);
                fresh.Add(it);
            }
        }
        return fresh;
    }

    /// <summary>Digs the sand around the hatch into a clean round pit with a flat floor, so the frame lies flush on it and no wall cuts through it.</summary>
    private void tidyHatchPit(BuriedItem it) => beach.shapePit(it.x, it.z, Math.Max(0.38, it.targetDepth + 0.05), 0.95, 1.9, 2.7);

    /// <summary>Lifts the captain's hatch: the closed door is swapped for the open one with the strongbox inside.</summary>
    public void openHatch(BuriedItem it)
    {
        var old = it.mesh;
        if (old == null || it.opened) return;
        var mesh = LootGenerator.buildItem("captain_hatch_open");
        var proxy = new THREE.Mesh(new THREE.SphereGeometry(0.75, 8, 6), new THREE.MeshBasicMaterial { visible = false });
        mesh.add(proxy);
        mesh.position.copy(old.position);
        mesh.rotation.copy(old.rotation);
        mesh.userData["item"] = it;
        group.remove(old);
        it.mesh = mesh;
        it.opened = true;
        group.add(mesh);
    }

    /// <summary>Meshes the player can click on right now.</summary>
    public List<THREE.Object3D> pickables() => items.Where(i => i.revealed && !i.collected && i.mesh != null).Select(i => (THREE.Object3D)i.mesh).ToList();

    public BuriedItem itemFromObject(THREE.Object3D obj)
    {
        var o = obj;
        while (o != null)
        {
            if (o.userData.TryGetValue("item", out var v) && v is BuriedItem b) return b;
            o = o.parent;
        }
        return null;
    }

    public void markCollected(BuriedItem it)
    {
        it.collected = true;
        disposeMesh(it);
    }

    public void update(double dt, double t, BuriedItem hovered)
    {
        foreach (var it in items)
        {
            if (it.mesh == null) continue;
            it.popT = Math.Min(1, it.popT + dt * 4);
            double pop = 0.5 + 0.5 * (1 - Math.Pow(1 - it.popT, 3));
            // the hatch is part of the ground, so it does not swell when looked at
            bool lit = it == hovered && it.def.id != "captain_chest";
            double hover = lit ? 1.15 + Math.Sin(t * 6) * 0.04 : 1;
            it.mesh.scale.setScalar(pop * hover);
            it.mesh.position.y = ground(it.x, it.z) + 0.05 + (lit ? 0.04 : 0);
        }
    }
}
