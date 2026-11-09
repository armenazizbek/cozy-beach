using System;
using static Beach;

// Port of src/world/Ocean.ts (the GLSL patches are in Resources/Shaders/CozyOcean.shader and CozyOceanFoam.shader)

public class Ocean
{
    private const double BEACH_W = HALF * 2;

    private static THREE.CanvasTexture foamTexture()
    {
        var c = new HTMLCanvasElement(256, 64);
        var g = c.getContext("2d");
        var grad = g.createLinearGradient(0, 0, 0, 64);
        grad.addColorStop(0, "rgba(255,255,255,0.95)");
        grad.addColorStop(0.35, "rgba(255,255,255,0.55)");
        grad.addColorStop(1, "rgba(255,255,255,0)");
        g.fillStyle = grad;
        g.fillRect(0, 0, 256, 64);
        g.globalCompositeOperation = "destination-out";
        for (int i = 0; i < 90; i++)
        {
            g.fillStyle = $"rgba(0,0,0,{JS.Str(0.2 + JS.Random() * 0.5)})";
            g.beginPath();
            g.arc(JS.Random() * 256, 8 + JS.Random() * 56, 2 + JS.Random() * 6, 0, Math.PI * 2);
            g.fill();
        }
        var t = new THREE.CanvasTexture(c);
        t.wrapS = THREE.Wrapping.RepeatWrapping;
        t.repeat.set(6, 1);
        return t;
    }

    /// <summary>
    /// The swell: three long waves rolling in toward the shore. The shader and `swellAt` below must agree, so that what floats rides the water.
    /// Each wave is [amplitude, kx, kz, speed].
    /// </summary>
    private static readonly double[][] SWELL =
    {
        new[] { 0.1, 0.1, 0.16, 0.9 },
        new[] { 0.07, -0.17, 0.23, 1.2 },
        new[] { 0.045, 0.31, 0.21, 1.6 },
    };

    /// <summary>A tiling field of soft bumps: its slopes in red and green, its height in blue. The water scrolls this past itself to make ripples.</summary>
    private static THREE.DataTexture rippleTexture()
    {
        const int N = 256;
        Func<double, double, double> lattice(int cells, double seed)
        {
            double rnd(int i, int j)
            {
                double v = Math.Sin(((i + cells) % cells) * 127.1 + ((j + cells) % cells) * 311.7 + seed) * 43758.5453;
                return v - Math.Floor(v);
            }
            double fade(double u) => u * u * (3 - 2 * u);
            return (x, y) =>
            {
                double fx = x * cells, fy = y * cells;
                int i = (int)Math.Floor(fx), j = (int)Math.Floor(fy);
                double u = fade(fx - i), w = fade(fy - j);
                double a = rnd(i, j) + (rnd(i + 1, j) - rnd(i, j)) * u;
                double b = rnd(i, j + 1) + (rnd(i + 1, j + 1) - rnd(i, j + 1)) * u;
                return a + (b - a) * w;
            };
        }
        var layers = new[] { lattice(5, 1.3), lattice(11, 4.7), lattice(23, 9.1), lattice(47, 2.2) };
        var weight = new[] { 1, 0.5, 0.25, 0.12 };
        var h = new double[N * N];
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        for (int j = 0; j < N; j++)
        for (int i = 0; i < N; i++)
        {
            double v = 0;
            for (int k = 0; k < layers.Length; k++) v += layers[k]((double)i / N, (double)j / N) * weight[k];
            h[j * N + i] = v;
            lo = Math.Min(lo, v);
            hi = Math.Max(hi, v);
        }
        var data = new byte[N * N * 4];
        double at(int i, int j) => h[((j + N) % N) * N + ((i + N) % N)];
        for (int j = 0; j < N; j++)
        for (int i = 0; i < N; i++)
        {
            double dx = (at(i + 1, j) - at(i - 1, j)) * 6, dy = (at(i, j + 1) - at(i, j - 1)) * 6;
            int o = (j * N + i) * 4;
            data[o] = (byte)Math.Max(0, Math.Min(255, (dx * 0.5 + 0.5) * 255));
            data[o + 1] = (byte)Math.Max(0, Math.Min(255, (dy * 0.5 + 0.5) * 255));
            data[o + 2] = (byte)((h[j * N + i] - lo) / (hi - lo) * 255);
            data[o + 3] = 255;
        }
        var t = new THREE.DataTexture(data, N, N, THREE.PixelFormat.RGBAFormat);
        t.wrapS = t.wrapT = THREE.Wrapping.RepeatWrapping;
        t.magFilter = THREE.TextureFilter.LinearFilter;
        t.minFilter = THREE.TextureFilter.LinearMipmapLinearFilter;
        t.generateMipmaps = true;
        t.anisotropy = 4;
        t.needsUpdate = true;
        return t;
    }

    /// <summary>how far the tide rises and falls from the middle</summary>
    private const double TIDE = 0.08;

    // The sea surface follows the viewer: a grid centred on the camera, dense underfoot and stretching
    // coarse toward the horizon. Detail sits where it is seen, instead of being spread evenly across water
    // too far off (and too lost in the haze) to resolve it.
    private const double SEA_REACH = 560;
    private const int SEA_GRID = 40;
    private const double SEA_FALLOFF = 1.5;

    /// <summary>A flat grid in the XZ plane, centred on the origin, whose spacing grows toward the edges.</summary>
    private static (THREE.BufferGeometry geo, float[] localX, float[] localZ) buildSeaGrid()
    {
        int k = SEA_GRID, side = 2 * k + 1, n = side * side;
        var position = new float[n * 3];
        var normal = new float[n * 3];
        var uv = new float[n * 2];
        var localX = new float[n];
        var localZ = new float[n];
        double at(int i)
        {
            double u = (double)(i - k) / k;
            return SEA_REACH * JS.Sign(u) * Math.Pow(Math.Abs(u), SEA_FALLOFF);
        }
        int v = 0;
        for (int iz = 0; iz < side; iz++)
        {
            double z = at(iz);
            for (int ix = 0; ix < side; ix++)
            {
                double x = at(ix);
                position[v * 3] = (float)x;
                position[v * 3 + 2] = (float)z;
                normal[v * 3 + 1] = 1;
                uv[v * 2] = (float)ix / (side - 1);
                uv[v * 2 + 1] = (float)iz / (side - 1);
                localX[v] = (float)x;
                localZ[v] = (float)z;
                v++;
            }
        }
        var index = new int[2 * k * 2 * k * 6];
        int o = 0;
        for (int iz = 0; iz < 2 * k; iz++)
        for (int ix = 0; ix < 2 * k; ix++)
        {
            int a = iz * side + ix, b = a + 1, c = a + side, d = c + 1;
            // wound so the surface faces up (+y)
            index[o++] = a; index[o++] = c; index[o++] = b;
            index[o++] = b; index[o++] = c; index[o++] = d;
        }
        var geo = new THREE.BufferGeometry();
        geo.setAttribute("position", new THREE.BufferAttribute(position, 3));
        geo.setAttribute("normal", new THREE.BufferAttribute(normal, 3));
        geo.setAttribute("uv", new THREE.BufferAttribute(uv, 2));
        geo.setIndex(index);
        return (geo, localX, localZ);
    }

    public readonly THREE.Group group = new THREE.Group();
    private readonly Beach beach;
    private readonly THREE.Mesh water;
    private readonly THREE.Uniform time = new THREE.Uniform(0.0);
    private readonly THREE.Mesh foam;
    private readonly THREE.CanvasTexture foamTex;
    private readonly THREE.Uniform foamUniform = new THREE.Uniform(new THREE.Vector4());
    private double level = WATER_LEVEL;
    private readonly float[] seaLocalX, seaLocalZ, seaDepth;
    private readonly THREE.BufferAttribute seaDepthAttr;
    private double seaAtX = double.PositiveInfinity, seaAtZ = double.PositiveInfinity;

    /// <summary>current height of the water surface</summary>
    public double surfaceY => level;
    private const int foamCols = 300;
    /// <summary>the shoreline at the lowest and the highest the tide goes, worked out once; each frame reads between the two</summary>
    private readonly float[] shoreLow = new float[foamCols + 1];
    private readonly float[] shoreHigh = new float[foamCols + 1];

    private double timeValue => Convert.ToDouble(time.value);

    /// <summary>The swell alone, without the tide, at a point on the open sea.</summary>
    public double swellAt(double x, double z, double? t = null)
    {
        double tt = t ?? timeValue;
        double h = 0;
        foreach (var s in SWELL) h += s[0] * Math.Sin(x * s[1] + z * s[2] + tt * s[3]);
        return h;
    }

    /// <summary>The height of the water at a point, swell included. The swell dies away in the shallows.</summary>
    public double heightAt(double x, double z)
    {
        double depth = WATER_LEVEL - beach.heightAt(x, z);
        double k = Math.Min(1, Math.Max(0, (depth - 0.05) / 0.65));
        return level + swellAt(x, z) * k * k * (3 - 2 * k);
    }

    public Ocean(Beach beach)
    {
        this.beach = beach;
        var (wg, localX, localZ) = buildSeaGrid();
        seaLocalX = localX;
        seaLocalZ = localZ;
        // how deep the sea is under each point: the colour, the clearness and the swell all follow the sea bed.
        // the grid follows the viewer, so this is refreshed as it moves (see update) rather than measured once.
        seaDepth = new float[localX.Length];
        for (int i = 0; i < seaDepth.Length; i++) seaDepth[i] = 30;
        seaDepthAttr = new THREE.BufferAttribute(seaDepth, 1);
        seaDepthAttr.setUsage(THREE.Usage.DynamicDrawUsage);
        wg.setAttribute("aDepth", seaDepthAttr);
        var mat = new THREE.MeshStandardMaterial
        {
            color = 0x1573c4,
            emissive = 0x0a3a78,
            emissiveIntensity = 0.55,
            roughness = 0.22,
            metalness = 0.15,
            transparent = true,
        };
        var ripple = rippleTexture();
        mat.customShader = "Cozy/Ocean";
        mat.customUniforms["uTime"] = time;
        mat.customUniforms["uRipple"] = new THREE.Uniform(ripple);
        mat.customUniforms["uShallow"] = new THREE.Uniform(new THREE.Color(0x1aa0b6));
        mat.customUniforms["uMid"] = new THREE.Uniform(new THREE.Color(0x1573c4));
        mat.customUniforms["uDeep"] = new THREE.Uniform(new THREE.Color(0x0e55aa));
        water = new THREE.Mesh(wg, mat);
        water.position.set(0, WATER_LEVEL, 0); // moved to the viewer each frame in update()
        water.receiveShadow = true;
        // the swell lifts the surface in the shader, so keep it drawn whatever its grid bounds say
        water.frustumCulled = false;
        group.add(water);

        for (int i = 0; i <= foamCols; i++)
        {
            double x = -HALF + (double)i / foamCols * HALF * 2;
            shoreLow[i] = (float)beach.shoreZ(x, WATER_LEVEL - TIDE);
            shoreHigh[i] = (float)beach.shoreZ(x, WATER_LEVEL + TIDE);
        }
        // Foam strip hugging the contour where the water meets the sand.
        var fg = new THREE.PlaneGeometry(BEACH_W, 1, foamCols, 1);
        var foamPos = fg.attributes.position;
        var shoreSpan = new float[foamPos.count];
        for (int i = 0; i <= foamCols; i++)
        {
            double x = -HALF + (double)i / foamCols * HALF * 2;
            double low = shoreLow[i];
            double span = shoreHigh[i] - low;
            foamPos.setXYZ(i, x, 0, low);
            foamPos.setXYZ(i + foamCols + 1, x, 1, low);
            shoreSpan[i] = (float)span;
            shoreSpan[i + foamCols + 1] = (float)span;
        }
        fg.setAttribute("aShoreSpan", new THREE.BufferAttribute(shoreSpan, 1));
        foamTex = foamTexture();
        var foamMat = new THREE.MeshBasicMaterial { map = foamTex, transparent = true, depthWrite = false, opacity = 0.9, side = THREE.Side.DoubleSide };
        foamMat.customShader = "Cozy/OceanFoam";
        foamMat.customUniforms["uFoam"] = foamUniform;
        foam = new THREE.Mesh(fg, foamMat);
        foam.renderOrder = 2;
        foam.frustumCulled = false;
        group.add(foam);

        update(0);
    }

    /// <summary>A sheet of this same open sea, with the same waves and colours, for looking out at it from somewhere else.</summary>
    public THREE.Mesh seaSheet(double width, double length)
    {
        var geo = new THREE.PlaneGeometry(length, width, 70, 90);
        geo.rotateX(-Math.PI / 2);
        var d = new float[geo.attributes.position.count];
        for (int i = 0; i < d.Length; i++) d[i] = 30;
        geo.setAttribute("aDepth", new THREE.BufferAttribute(d, 1));
        return new THREE.Mesh(geo, water.material) { frustumCulled = false };
    }

    /// <summary>The water's own blue glow would look odd at night, so it fades with the dark (0 day .. 1 night).</summary>
    public void setNight(double n)
    {
        ((THREE.MeshStandardMaterial)water.material).emissiveIntensity = 0.55 * (1 - 0.85 * n);
    }

    public void update(double t, double camX = 0, double camZ = 0)
    {
        // slow tidal breathing; the waves and ripples themselves are drawn by the shader
        level = WATER_LEVEL + Math.Sin(t * 0.9) * 0.05 + Math.Sin(t * 0.37) * 0.03;
        water.position.y = level;
        time.value = t;

        // the surface grid follows the viewer; the sea-bed depth under it is refreshed only when it has moved
        if (water.visible)
        {
            water.position.x = camX;
            water.position.z = camZ;
            if (Math.Abs(camX - seaAtX) > 0.5 || Math.Abs(camZ - seaAtZ) > 0.5)
            {
                seaAtX = camX;
                seaAtZ = camZ;
                for (int i = 0; i < seaDepth.Length; i++) seaDepth[i] = (float)(WATER_LEVEL - beach.heightAt(seaLocalX[i] + camX, seaLocalZ[i] + camZ));
                seaDepthAttr.needsUpdate = true;
            }
        }

        // foam band follows the shoreline contour in the vertex shader
        double surge = 0.5 + 0.5 * Math.Sin(t * 0.9 + 1.1);
        double width = 0.6 + surge * 1.6;
        double tide = (level - WATER_LEVEL + TIDE) / (TIDE * 2);
        ((THREE.Vector4)foamUniform.value).set(t, tide, width, level + 0.02);
        foamTex.offset.x = t * 0.02;
        foam.material.opacity = 0.55 + surge * 0.4;
    }
}
