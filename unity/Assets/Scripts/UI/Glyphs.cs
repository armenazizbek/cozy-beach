using System.Collections.Generic;
using System.Text.RegularExpressions;
using static PadText;

/// <summary>
/// Keys and controller buttons drawn as pictures instead of being spelled out: a key cap for F, the green A button, a
/// trigger, a mouse with its left button lit. The pictures are in Resources/UI/Skin and the classes in Style.uss (.kc).
/// </summary>
public static class Glyphs
{
    /// <summary>One key or button, named the way the game's hints name it: "F", "Space", "Click", "RT", "A", "left stick", "D-pad left".</summary>
    public static HTMLElement make(string name, bool onPad)
    {
        var e = document.createElement("span");
        set(e, name, onPad);
        return e;
    }

    /// <summary>A key that changes with what the player is holding: `kb` on the keyboard, `padName` on a controller.</summary>
    public static HTMLElement key(string kb, string padName)
    {
        var e = make(pad.on ? padName : kb, pad.on);
        e.dataset["kb"] = kb;
        e.dataset["pad"] = padName;
        return e;
    }

    /// <summary>Redraws every key made by <see cref="key"/> after the player picks up or puts down the controller.</summary>
    public static void refresh(bool onPad)
    {
        foreach (var e in document.querySelectorAll<HTMLElement>(".kc"))
        {
            var kb = e.dataset["kb"];
            var p = e.dataset["pad"];
            if (kb == null || p == null) continue;
            set(e, onPad ? p : kb, onPad);
        }
    }

    public static void set(HTMLElement e, string name, bool onPad)
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
        if (e.classList.contains("mid")) cls += " mid";
        if (e.className != cls) e.className = cls;
        if (e.textContent != text) e.textContent = text;
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

    /// <summary>
    /// Lays a hint out with its keys drawn: "Click to fire the harpoon · F picks up a find" becomes a mouse picture and
    /// "Fire the harpoon", then an F key cap and "picks up a find". The text is the hint as the game writes it (already
    /// reworded for a controller by PadText when one is in use).
    /// </summary>
    public static void fill(HTMLElement target, string text, bool onPad)
    {
        target.replaceChildren();
        if (string.IsNullOrEmpty(text)) return;
        foreach (var raw in text.Split(new[] { " · " }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            if (part.Length == 0) continue;
            var seg = document.createElement("div");
            seg.className = "hint-seg";
            var lead = (onPad ? LEAD_PAD : LEAD_KB).Match(part);
            if (lead.Success)
            {
                seg.append(make(lead.Groups[1].Value, onPad), words(capital(lead.Groups[2].Value)));
                target.append(seg);
                continue;
            }
            int at = 0;
            foreach (Match m in (onPad ? PAD_KEYS : KB_KEYS).Matches(part))
            {
                var before = part.Substring(at, m.Index - at).TrimEnd();
                if (before.Length > 0) seg.append(words(before));
                bool first = true;
                foreach (var k in m.Value.Split(new[] { " / " }, System.StringSplitOptions.None))
                {
                    var g = document.createElement("span");
                    if (first && before.Length > 0) g.classList.add("mid");
                    set(g, k, onPad);
                    seg.append(g);
                    first = false;
                }
                at = m.Index + m.Length;
            }
            var rest = part.Substring(at).TrimStart();
            if (rest.Length > 0) seg.append(words(rest));
            target.append(seg);
        }
    }

    private static HTMLElement words(string t)
    {
        var s = document.createElement("span");
        s.textContent = t;
        return s;
    }

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

    public static void fillBar(HTMLElement bar, IList<Prompt> items, bool onPad)
    {
        bar.replaceChildren();
        foreach (var it in items)
        {
            if ((onPad ? it.pad : it.kb).Length == 0) continue;
            var e = document.createElement("div");
            e.className = "pb-item";
            foreach (var k in onPad ? it.pad : it.kb) e.append(make(k, onPad));
            var l = document.createElement("span");
            l.className = "pb-label";
            l.textContent = it.label;
            e.append(l);
            bar.append(e);
        }
    }
}
