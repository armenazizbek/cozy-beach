using System;
using static Beach;

// Port of src/mechanics/DigTool.ts

public class DigTool
{
    private static readonly THREE.Color SAND_DRY = new THREE.Color("#e6ccb2");
    private static readonly THREE.Color SAND_WET = new THREE.Color("#9c7a56");

    public readonly THREE.Group group = new THREE.Group();
    public readonly THREE.Mesh ring;
    /// <summary>a dark band under the bright ring so it shows on pale sand</summary>
    public readonly THREE.Mesh ringDark;
    public readonly ParticleSystem particles = new ParticleSystem(260, 0.11, 9, false);
    private readonly THREE.Group tool = new THREE.Group();
    private readonly THREE.Mesh blade;
    private double swing = 0;

    public DigTool()
    {
        blade = new THREE.Mesh(
            new THREE.SphereGeometry(0.32, 16, 10, 0, Math.PI * 2, 0, Math.PI / 2),
            new THREE.MeshStandardMaterial { color = 0xe0623a, roughness = 0.5, metalness = 0.1, side = THREE.Side.DoubleSide });
        blade.rotation.x = Math.PI;
        blade.scale.set(1, 0.7, 1.2);
        blade.castShadow = true;
        var handle = new THREE.Mesh(new THREE.CylinderGeometry(0.045, 0.05, 1.3, 8), new THREE.MeshStandardMaterial { color = 0x8a6240, roughness = 0.8 });
        handle.position.set(0, 0.55, -0.55);
        handle.rotation.x = -0.9;
        handle.castShadow = true;
        tool.add(blade, handle);
        group.add(tool);

        ring = new THREE.Mesh(new THREE.RingGeometry(0.93, 1, 64).rotateX(-Math.PI / 2),
            new THREE.MeshBasicMaterial { color = 0xfff1c9, transparent = true, opacity = 0.8, side = THREE.Side.DoubleSide, depthWrite = false });
        ring.renderOrder = 5;
        ringDark = new THREE.Mesh(new THREE.RingGeometry(0.92, 1.025, 64).rotateX(-Math.PI / 2),
            new THREE.MeshBasicMaterial { color = 0x2b1a0c, transparent = true, opacity = 0.32, side = THREE.Side.DoubleSide, depthWrite = false });
        ringDark.renderOrder = 4;
    }

    public void setActive(bool on)
    {
        group.visible = on;
        ring.visible = on;
        ringDark.visible = on;
    }

    /// <summary>Called every frame while the trowel is the active tool.</summary>
    public void hover(THREE.Vector3 hit, StateManager state, bool canDig, Beach beach)
    {
        if (hit == null)
        {
            group.visible = false;
            ring.visible = false;
            ringDark.visible = false;
            return;
        }
        group.visible = true;
        ring.visible = true;
        ringDark.visible = true;
        int tier = state.tier("digging");
        var mat = (THREE.MeshStandardMaterial)blade.material;
        mat.color.set(tier == 1 ? 0xe0623a : tier == 2 ? 0xaab4bd : 0xc9973f);
        mat.metalness = tier == 1 ? 0.1 : 0.8;
        group.scale.setScalar(0.7 + tier * 0.25);
        group.position.set(hit.x, hit.y + 0.28, hit.z);
        ring.position.set(hit.x, hit.y, hit.z);
        ring.scale.set(state.digRadius, 1, state.digRadius);
        ringDark.position.set(hit.x, hit.y, hit.z);
        ringDark.scale.set(state.digRadius, 1, state.digRadius);
        Conform.conformToTerrain(ring, beach, hit.x, hit.y, hit.z, 0.066);
        Conform.conformToTerrain(ringDark, beach, hit.x, hit.y, hit.z, 0.058);
        ((THREE.MeshBasicMaterial)ring.material).color.set(canDig ? 0xffffff : 0xff4a3a);
    }

    /// <summary>Performs one dig.</summary>
    public void dig(double x, double z, Beach beach, StateManager state)
    {
        bool wet = z > WET_Z;
        beach.digAt(x, z, state.digRadius, state.digStep);
        state.spendWarmth(state.digCost(wet));
        swing = 1;
        var p = new THREE.Vector3(x, beach.heightAt(x, z) + 0.1, z);
        var col = SAND_DRY.clone().lerp(SAND_WET, wet ? 0.8 : JS.Random() * 0.4);
        particles.burst(p, 16 + (int)Math.Floor(state.digRadius * 14), 1.4 + state.digRadius, 3.4, 0.8, col);
    }

    public void update(double dt)
    {
        swing = Math.Max(0, swing - dt * 4);
        tool.rotation.x = -swing * 0.7;
        tool.position.y = -swing * 0.08;
        particles.update(dt);
    }
}
