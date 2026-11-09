using System.Text.RegularExpressions;

// Port of src/ui/PadText.ts

public static class PadText
{
    public class PadState
    {
        public bool on;
    }

    /// <summary>Whether the player is on a controller right now; hints and labels then name its buttons instead of keys.</summary>
    public static readonly PadState pad = new PadState();

    // (pattern, replacement, replace every match)
    private static readonly (Regex re, string to, bool all)[] SWAPS =
    {
        (new Regex(@"W / S throttle and reverse · A / D steer · Space slows · F leaves the wheel \(to use a weapon\) · E steps off when parked at the beach"), "RT / LT throttle and reverse · left stick steers · B slows · A leaves the wheel (to use a weapon) · X steps off when parked at the beach", false),
        (new Regex(@"W / S drive and reverse · A / D steer · Space brakes · F gets out"), "RT / LT drive and reverse · left stick steers · B brakes · A gets out", false),
        (new Regex(@"Move the mouse toward a screen edge to turn · click to use your tool"), "Right stick looks around · RT uses your tool", false),
        (new Regex(@"Press 7 or scroll to take them out; F beside a flag"), "Use LB / RB or d-pad left to take them out; A beside a flag", false),
        (new Regex(@"Press 7 to take them out"), "Press d-pad left to take them out", false),
        (new Regex(@"[Ss]croll to pick it up"), "Use LB / RB to pick it up", false),
        (new Regex(@"[Pp]ick it with the scroll wheel"), "Pick it with LB / RB", true),
        (new Regex(@"Click to fire the harpoon · F picks up a find · Q swims back up · WASD swim · Space up · C down"), "RT fires the harpoon · A picks up a find · X swims back up · left stick swims · RB up · LB down", false),
        (new Regex(@"Climb the stairs and the ladder, then press F at the top to go back out"), "Climb the stairs and the ladder, then press A at the top to go back out", false),
        (new Regex(@"Press F"), "Press A", true),
        (new Regex(@"any key"), "any button", true),
        (new Regex(@"right-click", RegexOptions.IgnoreCase), "LT", true),
        (new Regex(@"Hold click"), "Hold RT", true),
        (new Regex(@"Click it"), "Press RT on it", true),
        (new Regex(@"Click: "), "RT: ", true),
        (new Regex(@"Click to "), "Press RT to ", true),
        (new Regex(@"click to "), "press RT to ", true),
        (new Regex(@"\bF (beside|leaves|gets)"), "A $1", true),
        (new Regex(@"\bE steps"), "X steps", true),
        (new Regex(@"\(Esc\)"), "(B)", true),
        (new Regex(@"\(Space\)"), "(A)", true),
    };

    /// <summary>Rewrites a keyboard-and-mouse sentence for a controller. Unchanged while the keyboard is in use.</summary>
    public static string padText(string t)
    {
        if (!pad.on || t == null) return t;
        foreach (var (re, to, all) in SWAPS) t = all ? re.Replace(t, to) : re.Replace(t, to, 1);
        return t;
    }
}
