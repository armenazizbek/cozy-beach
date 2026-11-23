using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A picture drawn in code on the UI, native (unity/NATIVE_PLAN.md 2.7, S32): what the page's &lt;canvas&gt; was
/// (HTMLCanvasElement): a <see cref="TexturePainter"/> whose pixels are uploaded to the element's background once a frame
/// when they changed, sized to the picture when its width or height is set.
/// </summary>
public sealed class UiCanvas : UiNode
{
    /// <summary>The pixels shown.</summary>
    public readonly TexturePainter painter;
    private Texture2D tex;
    private int uploaded = -1;
    private byte[] buf;
    private VisualElement target;

    private static readonly HashSet<UiCanvas> onPanel = new HashSet<UiCanvas>();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TexturePainter, UiCanvas> owners =
        new System.Runtime.CompilerServices.ConditionalWeakTable<TexturePainter, UiCanvas>();

    /// <summary>The canvas showing a painter, or null for a painter that is only a texture.</summary>
    public static UiCanvas Of(TexturePainter p) => p != null && owners.TryGetValue(p, out var c) ? c : null;

    public UiCanvas(string classes, int width = 300, int height = 150) : base("canvas", classes)
    {
        painter = new TexturePainter(width, height);
        owners.Add(painter, this);
        ve.RegisterCallback<AttachToPanelEvent>(_ => onPanel.Add(this));
        ve.RegisterCallback<DetachFromPanelEvent>(_ => onPanel.Remove(this));
    }

    public static implicit operator TexturePainter(UiCanvas c) => c?.painter;

    public CanvasRenderingContext2D getContext(string kind = "2d") => painter.getContext(kind);

    /// <summary>The picture's width in pixels; the element takes that width too (as the page's canvas did).</summary>
    public int Width
    {
        get => painter.width;
        set
        {
            painter.width = value;
            ve.style.width = painter.width;
        }
    }

    public int Height
    {
        get => painter.height;
        set
        {
            painter.height = value;
            ve.style.height = painter.height;
        }
    }

    /// <summary>
    /// Shows the pixels on this element instead of the canvas's own background (the minimap puts them on a layer above the
    /// map that the GPU draws). Null puts them back.
    /// </summary>
    public VisualElement TextureTarget
    {
        get => target;
        set
        {
            ve.style.backgroundImage = StyleKeyword.Null;
            target = value;
            if (tex != null) (target ?? ve).style.backgroundImage = new StyleBackground(tex);
        }
    }

    /// <summary>Uploads every canvas on the panel whose pixels changed (once a frame, from the UI's tick).</summary>
    public static void SyncAll()
    {
        foreach (var c in onPanel) c.Sync();
    }

    /// <summary>Forgets the canvases (a reload).</summary>
    internal static void Clear() => onPanel.Clear();

    private void Sync()
    {
        if (!painter.HasContext || uploaded == painter.version) return;
        int w = painter.width, h = painter.height;
        uploaded = painter.version;
        if (tex == null || tex.width != w || tex.height != h)
        {
            if (tex != null) UnityEngine.Object.Destroy(tex);
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = ClassName.Length > 0 ? ClassName : "UICanvas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            (target ?? ve).style.backgroundImage = new StyleBackground(tex);
        }
        int len = w * h * 4;
        if (buf == null || buf.Length != len) buf = new byte[len];
        painter.CopyUnityRGBA(true, buf);
        tex.SetPixelData(buf, 0);
        tex.Apply(false, false);
    }
}
