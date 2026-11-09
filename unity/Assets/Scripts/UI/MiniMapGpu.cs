using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The parts of the round mini-map that cover the whole circle, drawn by the GPU: the dark sea, the painted island (one texture
/// that is turned and slid with style transforms, so it costs nothing per frame) and the dig holes (a vector layer redrawn only
/// when a hole is dug). The browser drew all of this on its canvas every frame; on the CPU here that cost about a millisecond
/// a frame while walking. The small markers stay on the canvas (MapView.drawMini), which now sits above these layers inside
/// the same circle, and the rim is a plain border on top.
/// </summary>
internal class MiniMapGpu
{
    private readonly HTMLCanvasElement canvas;
    private readonly VisualElement clip, world, holesLayer, icons, ring;
    private readonly double x0, z0, sc;
    private readonly float inset;
    private IList<double[]> holes;
    private int holesCount = -1;

    private static readonly Color HOLE = new Color32(95, 60, 34, 217); // rgba(95,60,34,0.85)
    private const float HOLE_R = 0.9f; // metres

    public VisualElement Host => canvas.ve;

    public MiniMapGpu(HTMLCanvasElement canvas, double size, HTMLCanvasElement baseMap, double extX0, double extZ0, double ext)
    {
        this.canvas = canvas;
        x0 = extX0;
        z0 = extZ0;
        sc = size / 60; // 60 metres across, as drawMini
        inset = 2; // the browser clipped to a circle 2 px inside the edge

        float r = (float)(size / 2) - inset;
        clip = Layer();
        clip.style.left = clip.style.top = clip.style.right = clip.style.bottom = inset;
        clip.style.overflow = Overflow.Hidden;
        clip.style.borderTopLeftRadius = clip.style.borderTopRightRadius = clip.style.borderBottomLeftRadius = clip.style.borderBottomRightRadius = r;
        clip.style.backgroundColor = (Color)new Color32(0x0d, 0x1b, 0x26, 255);

        float worldPx = (float)(ext * sc);
        world = Layer();
        world.style.width = world.style.height = worldPx;
        world.style.backgroundImage = new StyleBackground(ToTexture(baseMap));

        holesLayer = Layer();
        holesLayer.style.width = holesLayer.style.height = worldPx;
        holesLayer.generateVisualContent += DrawHoles;
        world.Add(holesLayer);
        clip.Add(world);

        // the canvas with the markers, inside the circle so markers at the edge are cut off as before
        icons = Layer();
        icons.style.left = icons.style.top = -inset;
        icons.style.width = icons.style.height = (float)size;
        clip.Add(icons);

        // the rim: a 3 px line centred 2 px inside the edge
        ring = Layer();
        ring.style.left = ring.style.top = ring.style.right = ring.style.bottom = 0.5f;
        ring.style.borderTopWidth = ring.style.borderRightWidth = ring.style.borderBottomWidth = ring.style.borderLeftWidth = 3;
        var rc = new Color(1f, 248 / 255f, 230 / 255f, 0.95f);
        ring.style.borderTopColor = ring.style.borderRightColor = ring.style.borderBottomColor = ring.style.borderLeftColor = rc;
        float rr = (float)(size / 2) - 0.5f;
        ring.style.borderTopLeftRadius = ring.style.borderTopRightRadius = ring.style.borderBottomLeftRadius = ring.style.borderBottomRightRadius = rr;

        canvas.ve.Add(clip);
        canvas.ve.Add(ring);
        canvas.textureTarget = icons;
    }

    private static VisualElement Layer()
    {
        var v = new VisualElement { pickingMode = PickingMode.Ignore };
        v.style.position = Position.Absolute;
        v.style.left = 0;
        v.style.top = 0;
        return v;
    }

    private static Texture2D ToTexture(HTMLCanvasElement c)
    {
        var tex = new Texture2D(c.width, c.height, TextureFormat.RGBA32, false)
        {
            name = "MiniMapBase",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        tex.SetPixelData(c.ToUnityRGBA(true), 0);
        tex.Apply(false, true);
        return tex;
    }

    /// <summary>Turns and slides the island so the player is in the middle and looking up (the same transform drawMini uses).</summary>
    public void Place(double px, double pz, double angle, double r)
    {
        float lx = (float)((px - x0) * sc), ly = (float)((pz - z0) * sc);
        float c = (float)r - inset;
        world.style.transformOrigin = new TransformOrigin(new Length(lx), new Length(ly), 0);
        world.style.translate = new Translate(new Length(c - lx), new Length(c - ly), 0);
        world.style.rotate = new Rotate(new Angle((float)(angle * 180 / Math.PI), AngleUnit.Degree));
    }

    /// <summary>Redraws the holes layer only when holes were dug (or the list was replaced).</summary>
    public void SetHoles(IList<double[]> list)
    {
        if (ReferenceEquals(list, holes) && list.Count == holesCount) return;
        holes = list;
        holesCount = list.Count;
        holesLayer.MarkDirtyRepaint();
    }

    private void DrawHoles(MeshGenerationContext mgc)
    {
        if (holes == null || holes.Count == 0) return;
        var p = mgc.painter2D;
        p.fillColor = HOLE;
        float rad = (float)(HOLE_R * sc);
        p.BeginPath();
        foreach (var h in holes)
        {
            var c = new Vector2((float)((h[0] - x0) * sc), (float)((h[1] - z0) * sc));
            p.MoveTo(c + new Vector2(rad, 0));
            p.Arc(c, rad, 0f, 360f);
            p.ClosePath();
        }
        p.Fill();
    }
}
