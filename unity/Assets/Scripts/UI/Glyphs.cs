using System.Collections.Generic;
using System.Text.RegularExpressions;
using static PadText;

/// <summary>
/// Keys and controller buttons drawn as pictures instead of being spelled out: a key cap for F, the green A button, a
/// trigger, a mouse with its left button lit. The pictures are in Resources/UI/Skin and the classes in Style.uss (.kc).
/// </summary>
public static class Glyphs
{
    /// <summary>Redraws every key made by <see cref="key"/> after the player picks up or puts down the controller.</summary>
    public static void refresh(bool onPad)
    {
        for (int i = nativeKeys.Count - 1; i >= 0; i--)
        {
            if (!nativeKeys[i].TryGetTarget(out var n)) { nativeKeys.RemoveAt(i); continue; }
            if (keyNames.TryGetValue(n, out var names)) Set(n, onPad ? names[1] : names[0], onPad);
        }
    }

    // ---------------------------------------------------------------- native (UI/Native/UiNode.cs)

    /// <summary>One key or button as a native element (the same classes and words as <see cref="make"/>).</summary>
    public static UiNode Make(string name, bool onPad)
    {
        var e = new UiNode("span");
        Set(e, name, onPad);
        return e;
    }

    public static void Set(UiNode e, string name, bool onPad)
    {
        var (cls, text) = look(name, onPad, e.Has("mid"));
        if (e.ClassName != cls) e.ClassName = cls;
        if (e.Text != text || e.Children.Count > 0) e.Text = text;
    }

    private static readonly System.Collections.Generic.List<System.WeakReference<UiNode>> nativeKeys = new System.Collections.Generic.List<System.WeakReference<UiNode>>();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UiNode, string[]> keyNames = new System.Runtime.CompilerServices.ConditionalWeakTable<UiNode, string[]>();

    /// <summary><see cref="key"/> as a native element: redrawn by <see cref="refresh"/> when the controller is picked up or put down.</summary>
    public static UiNode KeyNode(string kb, string padName)
    {
        var e = Make(pad.on ? padName : kb, pad.on);
        keyNames.Add(e, new[] { kb, padName });
        nativeKeys.Add(new System.WeakReference<UiNode>(e));
        return e;
    }

    /// <summary><see cref="fill"/> for a native element.</summary>
    public static void Fill(UiNode target, string text, bool onPad)
    {
        target.Clear();
        if (string.IsNullOrEmpty(text)) return;
        foreach (var raw in text.Split(new[] { " · " }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            if (part.Length == 0) continue;
            var seg = new UiNode("div", "hint-seg");
            var lead = (onPad ? LEAD_PAD : LEAD_KB).Match(part);
            if (lead.Success)
            {
                seg.Add(Make(lead.Groups[1].Value, onPad), Words(capital(lead.Groups[2].Value)));
                target.Add(seg);
                continue;
            }
            int at = 0;
            foreach (Match m in (onPad ? PAD_KEYS : KB_KEYS).Matches(part))
            {
                var before = part.Substring(at, m.Index - at).TrimEnd();
                if (before.Length > 0) seg.Add(Words(before));
                bool first = true;
                foreach (var k in m.Value.Split(new[] { " / " }, System.StringSplitOptions.None))
                {
                    var g = new UiNode("span");
                    if (first && before.Length > 0) g.AddClass("mid");
                    Set(g, k, onPad);
                    seg.Add(g);
                    first = false;
                }
                at = m.Index + m.Length;
            }
            var rest = part.Substring(at).TrimStart();
            if (rest.Length > 0) seg.Add(Words(rest));
            target.Add(seg);
        }
    }

    private static UiNode Words(string t) => new UiNode("span", null, t);

    /// <summary>The classes and the words a key or button is drawn with.</summary>
    private static (string cls, string text) look(string name, bool onPad, bool mid)
    {
        string n = (name ?? "").Trim();
        string low = n.ToLowerInvariant();
        string cls;
        string text = "";
        if (onPad)
        {
            switch (low)
            {
                case "a":
                case "b":
                case "x":
                case "y":
                    cls = "kc pad-" + low;
                    text = low.ToUpperInvariant();
                    break;
                case "left stick":
                case "ls":
                    cls = "kc pad-stick";
                    text = "L";
                    break;
                case "right stick":
                case "rs":
                    cls = "kc pad-stick";
                    text = "R";
                    break;
                case "d-pad": cls = "kc pad-dpad"; break;
                case "d-pad up": cls = "kc pad-dpad d-up"; break;
                case "d-pad down": cls = "kc pad-dpad d-down"; break;
                case "d-pad left": cls = "kc pad-dpad d-left"; break;
                case "d-pad right": cls = "kc pad-dpad d-right"; break;
                default:
                    cls = "kc pad-cap";
                    text = n;
                    break;
            }
        }
        else
        {
            switch (low)
            {
                case "click": cls = "kc mouse-l"; break;
                case "right-click": cls = "kc mouse-r"; break;
                case "scroll": cls = "kc mouse-m"; break;
                default:
                    cls = "kc key";
                    text = n;
                    break;
            }
        }
        // a key in the middle of a sentence keeps the gap before it
        if (mid) cls += " mid";
        return (cls, text);
    }

    // ---------------------------------------------------------------- hints

    /// <summary>What may follow a single letter for it to be a key ("F picks it up") and not a word ("A bite!").</summary>
    private const string VERBS = "stands|picks|gets|leaves|steps|swims|slows|brakes|puts|takes|brings|changes|fires|sweeps|reels|casts|beside|up|down";

    private static readonly Regex PAD_KEYS = new Regex(
        @"RT / LT|LB / RB|\bRT\b|\bLT\b|\bRB\b|\bLB\b|D-pad left|D-pad|[Ll]eft stick|[Rr]ight stick|(?<=[Pp]ress )[ABXY]\b|\b[ABXY]\b(?= (?:" + VERBS + @")\b)");

    private static readonly Regex KB_KEYS = new Regex(
        @"W / S|A / D|\bWASD\b|\bSpace\b|[Rr]ight-click|\b[Cc]lick\b|(?<=[Pp]ress )[A-Z]\b|\b[FEQC]\b(?= (?:" + VERBS + @")\b)|^\d(?= )");

    /// <summary>"Click to dig", "Click: rest at the campfire", "Press RT to dig": the key, then what it does.</summary>
    private static readonly Regex LEAD_KB = new Regex(@"^(click|right-click)(?: to |: )(.+)$", RegexOptions.IgnoreCase);
    private static readonly Regex LEAD_PAD = new Regex(@"^(?:press )?(RT|LT)(?: to |: )(.+)$", RegexOptions.IgnoreCase);

    private static string capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    // ---------------------------------------------------------------- the line of buttons under a menu

    public class Prompt
    {
        /// <summary>the keys on a controller and on the keyboard, and what they do</summary>
        public string[] pad, kb;
        public string label;

        public Prompt(string label, string[] pad, string[] kb)
        {
            this.label = label;
            this.pad = pad;
            this.kb = kb;
        }
    }

    /// <summary><see cref="fillBar"/> for a native bar.</summary>
    public static void FillBar(UiNode bar, IList<Prompt> items, bool onPad)
    {
        bar.Clear();
        foreach (var it in items)
        {
            if ((onPad ? it.pad : it.kb).Length == 0) continue;
            var e = new UiNode("div", "pb-item");
            foreach (var k in onPad ? it.pad : it.kb) e.Add(Make(k, onPad));
            e.Add(new UiNode("span", "pb-label", it.label));
            bar.Add(e);
        }
    }

}
