using System;
using System.Threading.Tasks;

// Port of src/mechanics/CleaningStation.ts

/// <summary>Isolated macro workbench scene where items are brushed clean.</summary>
public class CleaningStation
{
    public const double CLEAN_THRESHOLD = 0.92;
    private const double SAMPLE_EVERY_MS = 150;

    public readonly THREE.Scene scene = new THREE.Scene { name = "Cleaning bench" };
    public readonly THREE.PerspectiveCamera camera = new THREE.PerspectiveCamera(40, 1, 0.1, 50);
    public readonly ParticleSystem particles = new ParticleSystem(300, 0.07, 1.5, false);
    public double progress = 0;
    /// <summary>'idle' | 'brushing' | 'finishing' | 'done'</summary>
    public string phase = "idle";
    public Action<double> onProgress = _ => { };
    public Action onComplete = () => { };

    private readonly THREE.Group pivot = new THREE.Group();
    /// <summary>where the item is turning to; the pivot eases toward it so dragging feels smooth</summary>
    private readonly THREE.Quaternion target = new THREE.Quaternion().setFromEuler(new THREE.Euler(0.9, 0.4, 0, "YXZ"));
    private readonly THREE.Quaternion qa = new THREE.Quaternion();
    private readonly THREE.Vector3 axis = new THREE.Vector3();
    private CrustRig rig;
    private BuriedItem item;
    private double brushPx = 16;
    /// <summary>a stick covers ground far more slowly than a mouse, so with a controller the brush is wider</summary>
    public double brushScale = 1;
    private THREE.Vector2 lastUV;
    private readonly THREE.Vector2 lastUVVec = new THREE.Vector2();
    private readonly HTMLCanvasElement sampleCv = new HTMLCanvasElement(64, 64);
    private readonly CanvasRenderingContext2D sampleCtx;
    private readonly CanvasGradient brushGrad;
    private double sinceSample = 0;
    private readonly THREE.Raycaster raycaster = new THREE.Raycaster();
    private readonly ParticleSystem sparkles = new ParticleSystem(240, 0.12, -0.5, true);
    private readonly THREE.Color dust = new THREE.Color(0xc9ae80);
    private readonly THREE.Color gold = new THREE.Color(0xffe27a);
    private readonly THREE.Group brushMesh;
    private double brushT = 0, time = 0, fadeAcc = 0;

    public CleaningStation(THREE.WebGLRenderer renderer)
    {
        // the bench is lit by Three's RoomEnvironment too; the shaders stand in for it (see SceneSync.EnvironmentTuning)
        scene.environment = new THREE.Texture { name = "RoomEnvironment" };
        scene.environmentIntensity = 0.55;
        scene.background = new THREE.Color(0x2b2118);

        var bench = new THREE.Mesh(new THREE.CylinderGeometry(3.2, 3.4, 0.3, 48), new THREE.MeshStandardMaterial { color = 0x7a5538, roughness = 0.85 });
        bench.position.y = -1.75;
        bench.receiveShadow = true;
        var cloth = new THREE.Mesh(new THREE.CylinderGeometry(1.9, 1.9, 0.04, 48), new THREE.MeshStandardMaterial { color = 0xb8473b, roughness = 1 });
        cloth.position.y = -1.58;
        cloth.receiveShadow = true;
        scene.add(bench, cloth);

        var warm = new THREE.DirectionalLight(0xffe0b0, 1.5);
        warm.position.set(3, 5, 4);
        warm.castShadow = true;
        warm.shadow.mapSize.set(1024, 1024);
        var rim = new THREE.PointLight(0x9fc3ff, 18, 14, 2);
        rim.position.set(-4, 2, -3);
        scene.add(warm, rim, new THREE.AmbientLight(0xffffff, 0.35), pivot, particles.points, sparkles.points);

        brushMesh = new THREE.Group();
        var bristles = new THREE.Mesh(new THREE.BoxGeometry(0.3, 0.12, 0.14), new THREE.MeshStandardMaterial { color = 0xe4d5ae, roughness = 1 });
        var back = new THREE.Mesh(new THREE.BoxGeometry(0.34, 0.1, 0.16), new THREE.MeshStandardMaterial { color = 0x8a6240, roughness = 0.7 });
        back.position.y = 0.11;
        var handle = new THREE.Mesh(new THREE.CylinderGeometry(0.035, 0.04, 0.9, 8), new THREE.MeshStandardMaterial { color = 0x8a6240, roughness = 0.7 });
        handle.position.set(0.3, 0.45, 0);
        handle.rotation.z = -0.9;
        brushMesh.add(bristles, back, handle);
        brushMesh.visible = false;
        scene.add(brushMesh);

        camera.position.set(0, 3.1, 4.6);
        camera.lookAt(0, -0.2, 0);

        sampleCtx = sampleCv.getContext("2d");
        brushGrad = sampleCtx.createRadialGradient(0, 0, 0, 0, 0, 1);
        brushGrad.addColorStop(0, "rgba(0,0,0,0.9)");
        brushGrad.addColorStop(0.65, "rgba(0,0,0,0.5)");
        brushGrad.addColorStop(1, "rgba(0,0,0,0)");
    }

    public void resize(double w, double h)
    {
        camera.aspect = w / h;
        camera.updateProjectionMatrix();
    }

    /// <summary>Readies the bench ahead of time (Unity compiles shaders on import, so this only builds a sample once).</summary>
    public Task prepare(THREE.WebGLRenderer renderer, THREE.Object3D finds)
    {
        var sample = LootGenerator.wrapWithCrust(LootGenerator.buildItem(Items.ITEM_DEFS[0].id), LootGenerator.newCrustCanvas());
        pivot.add(sample.root);
        pivot.remove(sample.root);
        return Task.CompletedTask;
    }

    public void enter(BuriedItem it, double brush)
    {
        exit();
        item = it;
        brushPx = brush;
        it.crustCanvas ??= LootGenerator.newCrustCanvas();
        var clean = LootGenerator.buildItem(it.def.id);
        clean.traverse(o => o.castShadow = true);
        rig = LootGenerator.wrapWithCrust(clean, it.crustCanvas);
        pivot.add(rig.root);
        pivot.position.set(0, -0.15, 0);
        target.setFromEuler(new THREE.Euler(0.9, 0.4, 0, "YXZ"));
        pivot.quaternion.copy(target);
        lastUV = null;
        sampleNow();
        phase = progress >= CLEAN_THRESHOLD ? "finishing" : "brushing";
        brushMesh.visible = true;
        brushMesh.position.set(0, 4, 0);
    }

    public void exit()
    {
        if (rig != null) pivot.remove(rig.root);
        rig = null;
        item = null;
        phase = "idle";
        brushMesh.visible = false;
    }

    public void rotate(double dyaw, double dpitch)
    {
        // turn around the screen's own axes, so the item follows the drag in any orientation and can be flipped over
        target.premultiply(qa.setFromAxisAngle(axis.set(0, 1, 0), dyaw));
        target.premultiply(qa.setFromAxisAngle(axis.set(1, 0, 0), dpitch));
        target.normalize();
    }

    private readonly THREE.Vector2 ndcV = new THREE.Vector2();
    private readonly THREE.Vector3 centre = new THREE.Vector3();

    /// <summary>Pulls a stick-driven brush back onto the find, so it can never wander off the edge out of sight.</summary>
    public void keepOnItem(THREE.Vector2 ndc)
    {
        if (rig == null) return;
        bool hits(THREE.Vector2 p)
        {
            raycaster.setFromCamera(p, camera);
            return raycaster.intersectObject(rig.crust, false).Count > 0;
        }
        if (hits(ndc)) return;
        rig.crust.getWorldPosition(centre).project(camera);
        var inside = new THREE.Vector2(centre.x, centre.y);
        var outside = ndc.clone();
        var mid = new THREE.Vector2();
        for (int i = 0; i < 7; i++)
        {
            mid.copy(inside).add(outside).multiplyScalar(0.5);
            if (hits(mid)) inside.copy(mid);
            else outside.copy(mid);
        }
        ndc.copy(inside);
    }

    /// <summary>Pointer position in NDC; moves the brush mesh and, when `down`, scrubs.</summary>
    public bool pointer(THREE.Vector2 ndc, bool down, double speed)
    {
        ndcV.copy(ndc);
        if (rig == null || phase == "idle") return false;
        raycaster.setFromCamera(ndc, camera);
        var all = raycaster.intersectObject(rig.crust, false);
        var hit = all.Count > 0 ? all[0] : null;
        brushMesh.visible = hit != null || phase == "brushing";
        if (hit == null || hit.uv == null)
        {
            lastUV = null;
            return false;
        }
        brushMesh.position.copy(hit.point).addScaledVector(axis.copy(hit.face.normal).transformDirection(rig.crust.matrixWorld), 0.08);
        if (!down || phase != "brushing")
        {
            lastUV = null;
            return false;
        }
        paint(hit.uv);
        brushT = 1;
        int n = Math.Min(4, 1 + (int)Math.Floor(speed / 500));
        for (int i = 0; i < n; i++) particles.burst(hit.point, 1, 0.6, 0.4, 0.7, dust);
        return true;
    }

    private void paint(THREE.Vector2 uv)
    {
        var ctx = rig.canvas.getContext("2d");
        double S = rig.canvas.width;
        ctx.fillStyle = brushGrad;
        void stamp(double u, double vv)
        {
            double x = u * S, y = (1 - vv) * S;
            double theta = (1 - vv) * Math.PI;
            double ry = brushPx * brushScale;
            // a circle on the sphere is wider in UV space near the poles; sphere u spans 2*PI, v spans PI
            double rx = Math.Min(S / 2, ry * 2 / Math.Max(0.25, Math.Sin(theta)));
            ctx.save();
            ctx.translate(x, y);
            ctx.scale(rx, ry);
            ctx.beginPath();
            ctx.arc(0, 0, 1, 0, Math.PI * 2);
            ctx.fill();
            ctx.restore();
            // wrap seam
            if (x - rx < 0 || x + rx > S)
            {
                double off = x - rx < 0 ? S : -S;
                ctx.save();
                ctx.translate(x + off, y);
                ctx.scale(rx, ry);
                ctx.beginPath();
                ctx.arc(0, 0, 1, 0, Math.PI * 2);
                ctx.fill();
                ctx.restore();
            }
        }
        if (lastUV != null && Math.Abs(lastUV.x - uv.x) < 0.3)
        {
            double d = lastUV.distanceTo(uv) * S;
            int steps = (int)Math.Min(12, Math.Ceiling(d / Math.Max(4, brushPx * 0.5)));
            for (int i = 1; i <= steps; i++)
            {
                double k = (double)i / steps;
                stamp(lastUV.x + (uv.x - lastUV.x) * k, lastUV.y + (uv.y - lastUV.y) * k);
            }
        }
        else stamp(uv.x, uv.y);
        lastUV = lastUVVec.copy(uv);
        rig.texture.needsUpdate = true;
    }

    /// <summary>Area-weighted fraction of the crust that is now see-through.</summary>
    private void sampleNow()
    {
        if (rig == null)
        {
            progress = 0;
            return;
        }
        const int S = 64;
        sampleCtx.clearRect(0, 0, S, S);
        sampleCtx.drawImage(rig.canvas, 0, 0, S, S);
        var data = sampleCtx.getImageData(0, 0, S, S).data;
        double clean = 0, total = 0;
        const int N = 16; // logical 16x16 grid, each cell sampled at its centre area
        int cell = S / N;
        for (int gy = 0; gy < N; gy++)
        {
            double w = Math.Sin((gy + 0.5) / N * Math.PI);
            for (int gx = 0; gx < N; gx++)
            {
                double sum = 0;
                int cnt = 0;
                for (int y = gy * cell; y < (gy + 1) * cell; y++)
                for (int x = gx * cell; x < (gx + 1) * cell; x++)
                {
                    sum += data[(y * S + x) * 4];
                    cnt++;
                }
                clean += w * (1 - sum / cnt / 255);
                total += w;
            }
        }
        progress = Math.Min(1, clean / total);
        onProgress(progress);
    }

    /// <summary>Instantly clears the crust (Ultrasonic Bath).</summary>
    public void instantClean()
    {
        if (rig == null || phase != "brushing") return;
        var ctx = rig.canvas.getContext("2d");
        ctx.fillStyle = "#000";
        ctx.fillRect(0, 0, rig.canvas.width, rig.canvas.height);
        rig.texture.needsUpdate = true;
        sampleNow();
        beginFinish();
    }

    private void beginFinish()
    {
        phase = "finishing";
        fadeAcc = 0;
        brushMesh.visible = false;
        var p = new THREE.Vector3();
        for (int i = 0; i < 90; i++)
        {
            p.set((JS.Random() - 0.5) * 2.2, (JS.Random() - 0.5) * 1.2, (JS.Random() - 0.5) * 2.2);
            var vel = new THREE.Vector3((JS.Random() - 0.5) * 1.6, JS.Random() * 1.6, (JS.Random() - 0.5) * 1.6);
            sparkles.emit(p, vel, 1.2 + JS.Random() * 0.8, gold);
        }
    }

    public void update(double dt)
    {
        time += dt;
        if (rig != null)
        {
            pivot.quaternion.slerp(target, 1 - Math.Exp(-dt * 14));
            pivot.position.y = -0.15 + Math.Sin(time * 1.2) * 0.02;
        }
        brushT = Math.Max(0, brushT - dt * 6);
        brushMesh.rotation.z = Math.Sin(time * 40) * 0.25 * brushT;

        if (phase == "brushing")
        {
            sinceSample += dt * 1000;
            if (sinceSample >= SAMPLE_EVERY_MS)
            {
                sinceSample = 0;
                sampleNow();
                if (progress >= CLEAN_THRESHOLD) beginFinish();
            }
        }
        else if (phase == "finishing" && rig != null)
        {
            // complete the last few percent with a soft golden fade
            var ctx = rig.canvas.getContext("2d");
            ctx.fillStyle = "rgba(0,0,0,0.12)";
            ctx.fillRect(0, 0, rig.canvas.width, rig.canvas.height);
            rig.texture.needsUpdate = true;
            fadeAcc += dt;
            progress = Math.Min(1, progress + dt * 0.6);
            onProgress(progress);
            if (fadeAcc > 0.7)
            {
                ctx.fillStyle = "#000";
                ctx.fillRect(0, 0, rig.canvas.width, rig.canvas.height);
                rig.texture.needsUpdate = true;
                rig.crust.visible = false;
                progress = 1;
                onProgress(1);
                phase = "done";
                onComplete();
            }
        }
        particles.update(dt);
        sparkles.update(dt);
    }
}
