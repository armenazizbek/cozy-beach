using System.Collections.Generic;

// Port of src/world/conform.ts and src/world/merge.ts

public static class Conform
{
    /// <summary>
    /// Bends a flat overlay (a ring or disc lying in the XZ plane) so it follows the real ground
    /// under it, instead of being sliced by slopes, mounds and holes.
    /// The mesh must sit at the ground point (cx, cy, cz) and may only be scaled in x and z.
    /// </summary>
    public static void conformToTerrain(THREE.Mesh mesh, Beach beach, double cx, double cy, double cz, double lift = 0.06)
    {
        var pos = mesh.geometry.attributes.position;
        double sx = mesh.scale.x, sz = mesh.scale.z;
        for (int i = 0; i < pos.count; i++)
        {
            double h = beach.heightAt(cx + pos.getX(i) * sx, cz + pos.getZ(i) * sz);
            pos.setY(i, h - cy + lift);
        }
        pos.needsUpdate = true;
        mesh.geometry.computeBoundingSphere();
    }
}

public static class Merge
{
    /// <summary>
    /// Welds the fixed parts of a prop into one mesh per material, so a chair made of ten boxes costs one draw instead of ten. Anything that
    /// moves, is clickable on its own or is drawn see-through is left alone: pass moving parts in `keep`, and whatever is inside them stays too.
    /// </summary>
    public static void mergeStatic(THREE.Object3D root, HashSet<THREE.Object3D> keep = null)
    {
        keep ??= new HashSet<THREE.Object3D>();
        root.updateWorldMatrix(true, true);
        var toRoot = root.matrixWorld.clone().invert();
        var sets = new Dictionary<string, (THREE.Material mat, bool cast, bool receive, List<THREE.Mesh> parts)>();
        var order = new List<string>();
        void walk(THREE.Object3D o)
        {
            foreach (var c in o.children)
            {
                if (keep.Contains(c) || c.userData.ContainsKey("kind") || !c.visible) continue;
                if (c is THREE.Mesh m && !(c is THREE.InstancedMesh) && !(c is THREE.SkinnedMesh))
                {
                    var mat = m.isMultiMaterial ? null : m.material;
                    bool fits = mat != null && mat.visible && !mat.transparent && c.matrixWorld.determinant() * root.matrixWorld.determinant() > 0 && c.userData.Count == 0;
                    if (fits)
                    {
                        string key = $"{mat.uuid}|{c.castShadow}|{c.receiveShadow}";
                        if (!sets.TryGetValue(key, out var set))
                        {
                            set = (mat, c.castShadow, c.receiveShadow, new List<THREE.Mesh>());
                            order.Add(key);
                        }
                        set.parts.Add(m);
                        sets[key] = set;
                    }
                }
                walk(c);
            }
        }
        walk(root);
        foreach (var key in order)
        {
            var (mat, cast, receive, parts) = sets[key];
            if (parts.Count < 2) continue;
            bool textured = mat is THREE.MeshStandardMaterial sm && sm.map != null || mat is THREE.MeshBasicMaterial bm && bm.map != null || mat is THREE.MeshLambertMaterial lm && lm.map != null;
            bool tinted = mat.vertexColors;
            var geos = new List<THREE.BufferGeometry>();
            foreach (var m in parts)
            {
                var g = m.geometry.index != null ? m.geometry.toNonIndexed() : m.geometry.clone();
                foreach (var name in new List<string>(g.attributes.Keys))
                {
                    if (name == "position" || name == "normal" || (name == "uv" && textured) || (name == "color" && tinted)) continue;
                    g.deleteAttribute(name);
                }
                g.morphAttributes.Clear();
                g.groups.Clear();
                g.applyMatrix4(new THREE.Matrix4().multiplyMatrices(toRoot, m.matrixWorld));
                geos.Add(g);
            }
            var joined = THREE.BufferGeometryUtils.mergeGeometries(geos);
            if (joined == null) continue;
            foreach (var m in parts) m.parent?.remove(m);
            var merged = new THREE.Mesh(joined, mat) { castShadow = cast, receiveShadow = receive };
            root.add(merged);
        }
    }
}
