using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// An element of a screen's UXML (unity/UI_PLAN.md U1): `tag` is the element it stands for (div, span, button, h2, ...,
/// the same tag classes Style.uss is written for) and `text` its own words. <see cref="Ux.Load"/> adopts a whole tree, so
/// every element behaves as the ones built in code (UiNode) do.
/// </summary>
[UxmlElement]
public partial class El : UiNode
{
    /// <summary>the UXML's `tag` and `text` attributes (the node's own tag and words are set from them by <see cref="Adopt"/>)</summary>
    [UxmlAttribute("tag")] public string elTag { get; set; } = "div";
    [UxmlAttribute("text")] public string elText { get; set; }

    public El() : base(true) { }

    internal void Adopted() => Init(elTag, null, elText);
}

/// <summary>
/// Screens and list items from UXML (Resources/UI/Screens/*.uxml): loaded, adopted into UiNode trees, ready for a
/// controller to find its parts by name.
/// </summary>
public static class Ux
{
    private static readonly Dictionary<string, VisualTreeAsset> assets = new Dictionary<string, VisualTreeAsset>();

    /// <summary>A new copy of a UXML file's single top element, adopted (a screen, or a list item template).</summary>
    public static UiNode Load(string name)
    {
        if (!assets.TryGetValue(name, out var asset) || asset == null)
        {
            asset = Resources.Load<VisualTreeAsset>("UI/Screens/" + name);
            if (asset == null) throw new InvalidOperationException($"no UXML at Resources/UI/Screens/{name}");
            assets[name] = asset;
        }
        var holder = asset.Instantiate();
        var top = holder.Children().FirstOrDefault() ?? throw new InvalidOperationException($"UXML {name} is empty");
        var node = Adopt(top);
        node.ve.RemoveFromHierarchy();
        return node;
    }

    /// <summary>Every top element of a UXML file, adopted, in order (a layer whose parts sit side by side, such as the HUD).</summary>
    public static List<UiNode> LoadAll(string name)
    {
        if (!assets.TryGetValue(name, out var asset) || asset == null)
        {
            asset = Resources.Load<VisualTreeAsset>("UI/Screens/" + name);
            if (asset == null) throw new InvalidOperationException($"no UXML at Resources/UI/Screens/{name}");
            assets[name] = asset;
        }
        var holder = asset.Instantiate();
        var tops = holder.Children().ToList();
        var nodes = tops.Select(Adopt).ToList();
        foreach (var n in nodes) n.ve.RemoveFromHierarchy();
        return nodes;
    }

    /// <summary>The element with this name in any of these trees.</summary>
    public static UiNode Find(IEnumerable<UiNode> roots, string name)
    {
        foreach (var r in roots)
        {
            if (r.ve.name == name) return r;
            var n = r.Q(name);
            if (n != null) return n;
        }
        throw new InvalidOperationException($"no element named '{name}'");
    }

    /// <summary>Puts a part made in code where a placeholder of the UXML stands, and takes the placeholder away.</summary>
    public static void Replace(UiNode placeholder, UiNode part)
    {
        placeholder.Before(part);
        placeholder.Remove();
    }

    /// <summary>Adopts an El and everything under it, in document order.</summary>
    public static UiNode Adopt(VisualElement v)
    {
        if (!(v is El el)) throw new InvalidOperationException($"UXML element {v.GetType().Name} '{v.name}' is not an El");
        // the children first, as they are: adopting the parent may move them into a scroll box
        var children = ((VisualElement)el).Children().ToList();
        // the element is a UiNode already: it only takes on its tag, words and behaviour, and its children are kept
        el.Adopted();
        foreach (var c in children)
        {
            if (c is El) el.AdoptChild(Adopt(c));
        }
        return el;
    }
}
