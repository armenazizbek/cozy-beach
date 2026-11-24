using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static Help;
using static PadText;
using static SaveGame;
using static StateManager;
using static Story;

// Port of src/ui/UIManager.ts

public class UIHooks
{
    public Action<int> onTool;
    public Action onInstantClean;
    public Action onInspectBack;
    public Action onContinue;
    public Func<bool> onMute;
}

public class SaveHooks
{
    public Func<List<SaveMeta>> list;
    /// <summary>saves as a new entry, or over `overwriteId`; returns the entry or null on failure</summary>
    public Func<string, string, SaveMeta> save;
    public Action<string> load;
    public Action<string> remove;
}

public class MenuHooks
{
    public bool hasSaves;
    /// <summary>loads the newest save of all</summary>
    public Action onContinue;
    public Action onAchievements;
    public Action onNew;
    public Action onLoad;
    public Action onSettings;
    public Action onExit;
}

public class PauseHooks
{
    public Action onAchievements;
    public Action onResume;
    public Action onNew;
    public Action onSettings;
    public Action onMenu;
    public SaveHooks saves;
}

public class AchievementItem
{
    public string name;
    public string desc;
    public string icon;
    public bool done;
    public bool secret;
}

public class AchView : AchievementItem { }

public class BookPage : JournalPage
{
    public bool isNew;
    public bool found;
}

public class BookPageView : BookPage
{
    public string when { get => group; set => group = value; }
}

public class MailLetter : Letter
{
    public bool isNew;
}

public class MailLetterView : MailLetter
{
    public string heading { get => greeting; set => greeting = value; }
    public string body { get => text; set => text = value; }
    public double gift;
}

public class MapAlt
{
    public string name;
    public Action<CanvasRenderingContext2D, double> draw;
}

public class UIManager
{
    private const int MINI_PX = 150;
    private const string __APP_VERSION__ = "1.0.0";
    private const string __BUILD_YEAR__ = "2026";

    /// <summary>Tool badge drawings that match the tools in hand: the metal detector, the trowel and the binoculars.</summary>
    /// <summary>Every tool drawing, for the editor's icon baker (IconAtlas).</summary>
    public static string[] ToolIcons => new[] { DETECTOR_ICON, TROWEL_ICON, SCOOP_ICON, SHOVEL_ICON, BINOC_ICON, CAMERA_ICON };

    private const string DETECTOR_ICON =
        "<svg viewBox=\"0 0 32 32\" width=\"1.1em\" height=\"1.1em\" style=\"vertical-align:-0.2em\"><ellipse cx=\"9.5\" cy=\"25.5\" rx=\"7.5\" ry=\"3.6\" fill=\"#1f2124\" stroke=\"#e6b422\" stroke-width=\"1.8\"/><circle cx=\"9.5\" cy=\"25.5\" r=\"2.1\" fill=\"#3a4048\"/><path d=\"M9.5 25L21 11.5L23.5 5.5L27.5 7\" fill=\"none\" stroke=\"#9aa3ad\" stroke-width=\"2.4\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/><path d=\"M25.5 3.5C28.5 3.8 30 6.2 29.2 9\" fill=\"none\" stroke=\"#1f2124\" stroke-width=\"2.6\" stroke-linecap=\"round\"/><path d=\"M21.8 10.5L23.2 6.8\" stroke=\"#1f2124\" stroke-width=\"4\" stroke-linecap=\"round\"/><path d=\"M14.2 19.5L15.8 17.6\" stroke=\"#e6b422\" stroke-width=\"3.2\" stroke-linecap=\"round\"/><g transform=\"translate(18.8 13.8) rotate(-49)\"><rect x=\"-3.4\" y=\"-3.2\" width=\"6.8\" height=\"5.2\" rx=\"1.2\" fill=\"#3a4048\" stroke=\"#1f2124\" stroke-width=\"1\"/><rect x=\"-2\" y=\"-2\" width=\"4\" height=\"2.4\" rx=\"0.4\" fill=\"#39ff88\"/></g></svg>";

    private const string TROWEL_ICON =
        "<svg viewBox=\"0 0 32 32\" width=\"1.1em\" height=\"1.1em\" style=\"vertical-align:-0.2em\"><path d=\"M3 29C2 20 7 13 14 11L21 18C19 25 12 30 3 29Z\" fill=\"#c9cfd6\" stroke=\"#5f6771\" stroke-width=\"1.4\" stroke-linejoin=\"round\"/><path d=\"M4.5 27.5L16 16\" stroke=\"#8d95a0\" stroke-width=\"1.4\" stroke-linecap=\"round\"/><path d=\"M16 16L22 10\" stroke=\"#8d95a0\" stroke-width=\"2.6\"/><path d=\"M21 11L23 9\" stroke=\"#dfe3e8\" stroke-width=\"5\"/><path d=\"M23.5 8.5L28.5 3.5\" stroke=\"#e0732f\" stroke-width=\"5.4\" stroke-linecap=\"round\"/></svg>";

    private const string SCOOP_ICON =
        "<svg viewBox=\"0 0 32 32\" width=\"1.1em\" height=\"1.1em\" style=\"vertical-align:-0.2em\"><path d=\"M3 18L19 11L25 21C21 30 8 31 3 25Z\" fill=\"#dfe4ea\" stroke=\"#5f6771\" stroke-width=\"1.4\" stroke-linejoin=\"round\"/><g fill=\"#5f6771\"><circle cx=\"9\" cy=\"21\" r=\"1.3\"/><circle cx=\"14\" cy=\"19\" r=\"1.3\"/><circle cx=\"12\" cy=\"25\" r=\"1.3\"/><circle cx=\"18\" cy=\"23\" r=\"1.3\"/><circle cx=\"7\" cy=\"26\" r=\"1.1\"/></g><path d=\"M19 13L22 9\" stroke=\"#9aa3ad\" stroke-width=\"2.6\"/><path d=\"M22 9L28 3.5\" stroke=\"#1f2124\" stroke-width=\"5.4\" stroke-linecap=\"round\"/></svg>";

    private const string SHOVEL_ICON =
        "<svg viewBox=\"0 0 32 32\" width=\"1.1em\" height=\"1.1em\" style=\"vertical-align:-0.2em\"><path d=\"M5 17L13 14L19 20C18 28 10 31 5 29C3 25 3 20 5 17Z\" fill=\"#c9cfd6\" stroke=\"#5f6771\" stroke-width=\"1.4\" stroke-linejoin=\"round\"/><path d=\"M12 19L25 6\" stroke=\"#9aa3ad\" stroke-width=\"2.6\" stroke-linecap=\"round\"/><path d=\"M22 9L26.5 4.5\" stroke=\"#1f2124\" stroke-width=\"4.4\" stroke-linecap=\"round\"/><path d=\"M23.5 3L29 8.5\" stroke=\"#1f2124\" stroke-width=\"3.4\" stroke-linecap=\"round\"/></svg>";

    private const string BINOC_ICON =
        "<svg viewBox=\"0 0 32 32\" width=\"1.1em\" height=\"1.1em\" style=\"vertical-align:-0.2em\"><rect x=\"6\" y=\"4\" width=\"6\" height=\"4\" rx=\"1.2\" fill=\"#15181a\"/><rect x=\"20\" y=\"4\" width=\"6\" height=\"4\" rx=\"1.2\" fill=\"#15181a\"/><rect x=\"11\" y=\"10\" width=\"10\" height=\"4.5\" rx=\"1\" fill=\"#15181a\"/><circle cx=\"16\" cy=\"12.2\" r=\"2.2\" fill=\"#3a4440\" stroke=\"#15181a\" stroke-width=\"1\"/><path d=\"M5.5 7.5H12.5L13.8 22H3.5Z\" fill=\"#2b3430\" stroke=\"#15181a\" stroke-width=\"1.3\" stroke-linejoin=\"round\"/><path d=\"M19.5 7.5H26.5L28.5 22H18.2Z\" fill=\"#2b3430\" stroke=\"#15181a\" stroke-width=\"1.3\" stroke-linejoin=\"round\"/><ellipse cx=\"8.6\" cy=\"23.5\" rx=\"5.4\" ry=\"3.2\" fill=\"#15181a\"/><ellipse cx=\"23.4\" cy=\"23.5\" rx=\"5.4\" ry=\"3.2\" fill=\"#15181a\"/><ellipse cx=\"8.6\" cy=\"23.8\" rx=\"3.8\" ry=\"2.1\" fill=\"#6aa8d8\"/><ellipse cx=\"23.4\" cy=\"23.8\" rx=\"3.8\" ry=\"2.1\" fill=\"#6aa8d8\"/></svg>";

    private const string CAMERA_ICON =
        "<svg viewBox=\"0 0 32 32\" width=\"1.1em\" height=\"1.1em\" style=\"vertical-align:-0.2em\"><rect x=\"3\" y=\"9\" width=\"26\" height=\"18\" rx=\"3\" fill=\"#26282c\"/><path d=\"M3 13V12C3 10.3 4.3 9 6 9H26C27.7 9 29 10.3 29 12V13Z\" fill=\"#c9ced4\"/><rect x=\"6\" y=\"6\" width=\"7\" height=\"4\" rx=\"1\" fill=\"#c9ced4\"/><rect x=\"23\" y=\"6.5\" width=\"3.5\" height=\"3\" rx=\"1\" fill=\"#d94b3e\"/><circle cx=\"17\" cy=\"18.5\" r=\"7\" fill=\"#c9ced4\"/><circle cx=\"17\" cy=\"18.5\" r=\"5.4\" fill=\"#121416\"/><circle cx=\"17\" cy=\"18.5\" r=\"3.4\" fill=\"#5b86b0\"/><circle cx=\"15.6\" cy=\"17.1\" r=\"1.1\" fill=\"#dbe9f5\"/><rect x=\"5.5\" y=\"14.5\" width=\"4.5\" height=\"2.6\" rx=\"0.6\" fill=\"#f4f1e8\"/></svg>";

    /// <summary>Shows a tool's icon: an emoji, or one of the drawings above.</summary>
    private static void showIcon(UiNode target, string icon)
    {
        if (icon.StartsWith("<")) target.Svg(icon);
        else target.Text = icon;
    }

    public readonly UiCanvas waveCanvas = new UiCanvas("wave");
    public readonly CanvasRenderingContext2D waveCtx;
    private UiNode root;
    // the HUD and its cards are UXML (Resources/UI/Screens/Hud.uxml), found by name in the constructor
    private UiNode coins;
    private UiNode warmFill;
    private UiNode warmText;
    private UiNode mugName;
    private UiNode pack;
    private UiNode eco;
    private UiNode bones;
    private UiNode boneChip;
    private UiNode badge;
    private UiNode badgeIcon;
    private UiNode badgeName;
    /// <summary>the controller's d-pad tool picker, shown instead of the badge while a controller is in use</summary>
    private UiNode padTools;
    private Dictionary<string, UiNode> padSlots;
    private UiNode padCaption;
    /// <summary>every tool that shares the d-pad's left slot, in the order the presses go round them, the one in hand lit</summary>
    private UiNode padStack;
    /// <summary>"(d-pad left) Bow": what the next press of left gives, so it is plain that pressing again moves on</summary>
    private UiNode padNext;
    private int curTool = 1;
    private bool badgeOverride = false;
    private UiNode hint;
    private UiNode toasts;
    private UiNode fadeEl;
    private UiNode hud;
    private UiNode scope;
    private UiNode inspect;
    private UiNode inspectTitle;
    private UiNode inspectHelp;
    private UiNode progFill;
    private UiNode progText;
    private UiNode instantBtn;
    private UiNode result;
    private UiNode intro;
    private UiNode cross;
    private UiNode drawbar;
    private UiNode drawFill;
    private UiCanvas binoc = new UiCanvas("binoc hidden");
    private UiCanvas binocText = new UiCanvas("binoc-text hidden");
    private string binocFrameKey = "";
    /// <summary>the camera's viewfinder, drawn like the binocular view (it shares its full-screen class)</summary>
    private UiCanvas finder = new UiCanvas("binoc hidden");
    private string finderKey = "";
    /// <summary>the white flash as a photo is taken</summary>
    private UiNode flashEl;
    private double flashAt = -1;

    /// <summary>The zoom and distance under the binoculars, on a small canvas of its own (see setBinoculars).</summary>
    private void drawBinocReadout(string zoom, string range, int w, int h)
    {
        const int TW = 640, TH = 40, BASE = 28;
        if (this.binocText.Width != TW) this.binocText.Width = TW;
        if (this.binocText.Height != TH) this.binocText.Height = TH;
        var ve = this.binocText.ve;
        if (ve != null)
        {
            ve.pickingMode = UnityEngine.UIElements.PickingMode.Ignore;
            ve.style.position = UnityEngine.UIElements.Position.Absolute;
            ve.style.left = (w - TW) / 2f;
            ve.style.top = h - 62 - BASE;
            ve.style.width = TW;
            ve.style.height = TH;
        }
        var t = this.binocText.getContext("2d");
        t.clearRect(0, 0, TW, TH);
        t.font = "600 20px \"Trebuchet MS\", sans-serif";
        t.textAlign = "center";
        t.fillStyle = "rgba(255,248,230,0.95)";
        t.fillText($"{zoom}   ·   {range}", TW / 2.0, BASE);
    }
    private string binocKey = "";
    public readonly UiCanvas miniCanvas = new UiCanvas("minimap");
    private UiNode mapEl = null;
    private bool mapTabs = false;
    private Action mapToggle = null;
    private Action mapRepaint = null;
    private UiNode clock;
    private UiNode prompt;
    private UiNode prompt2;
    private UiNode prompt2Text;
    private UiNode promptText;
    private UiNode muteBtn;
    /// <summary>what the keys or the controller's buttons do in the menu that is open, along the bottom of the screen</summary>
    private UiNode padbar;
    private string padbarKey = "";
    private string hintKey = null;

    private StateManager state;
    private UIHooks hooks;

    public UIManager(UiNode root, StateManager state, UIHooks hooks)
    {
        this.root = root;
        this.state = state;
        this.hooks = hooks;
        this.waveCanvas.Width = 240;
        this.waveCanvas.Height = 84;
        this.waveCtx = this.waveCanvas.getContext("2d");
        // The prompts and the hint sit in the middle of a row as wide as the screen (Hud.uxml). They used to be centred
        // with "left: 50%; translate: -50%", but UI Toolkit works that half-width out once and keeps it: when the words
        // changed, the box grew to the right only, and a long hint ran under the tool in the corner.
        var parts = Ux.LoadAll("Hud");
        UiNode find(string n) => Ux.Find(parts, n);
        this.flashEl = find("flash");
        this.cross = find("cross");
        this.drawbar = find("drawbar");
        this.drawFill = find("drawFill");
        this.prompt = find("prompt");
        this.promptText = find("promptText");
        this.prompt2 = find("prompt2");
        this.prompt2Text = find("prompt2Text");
        this.hud = find("hud");
        this.coins = find("coins");
        this.mugName = find("mugName");
        this.warmText = find("warmText");
        this.warmFill = find("warmFill");
        this.clock = find("clock");
        this.pack = find("pack");
        this.eco = find("eco");
        this.boneChip = find("boneChip");
        this.bones = find("bones");
        this.muteBtn = find("mute");
        this.badge = find("badge");
        this.badgeIcon = find("badgeIcon");
        this.badgeName = find("badgeName");
        this.padTools = find("padTools");
        this.padSlots = new Dictionary<string, UiNode> { ["up"] = find("padUp"), ["right"] = find("padRight"), ["down"] = find("padDown"), ["left"] = find("padLeft") };
        this.padCaption = find("padCaption");
        this.padStack = find("padStack");
        this.padNext = find("padNext");
        this.scope = find("scope");
        this.hint = find("hint");
        this.toasts = find("toasts");
        this.inspect = find("inspect");
        this.inspectTitle = find("inspectTitle");
        this.inspectHelp = find("inspectHelp");
        this.progFill = find("progFill");
        this.progText = find("progText");
        this.instantBtn = find("instant");
        this.result = find("result");
        this.fadeEl = find("fade");
        this.intro = find("intro");
        this.padbar = find("padbar");
        this.buildHud();
        this.buildInspect();
        Ux.Replace(find("minimapSlot"), this.miniCanvas);
        Ux.Replace(find("waveSlot"), this.waveCanvas);
        // the key to press is a picture (a key cap, or the controller's button), in a holder that bobs gently
        // the keys as bound (Core/Controls.cs); they follow a rebind
        this.kbdF = Glyphs.Make(Controls.KeyName(Act.Use), false);
        this.kbdE = Glyphs.Make(Controls.KeyName(Act.Use2), false);
        Controls.Changed += () => this.setPadMode(pad.on);
        find("holdF").Add(this.kbdF);
        find("holdE").Add(this.kbdE);
        // the binocular and viewfinder canvases first, then the HUD's parts, in the page's order
        foreach (var c in new UiNode[] { this.binoc, this.binocText, this.finder }) c.MountIn(root.ve);
        foreach (var part in parts) part.MountIn(root.ve);
        state.subscribe(() => this.refresh());
        this.refresh();
    }

    private void buildHud()
    {
        this.muteBtn.OnClick = () =>
        {
            this.muteBtn.Text = this.hooks.onMute() ? "🔇" : "🔊";
        };
        this.miniCanvas.Width = this.miniCanvas.Height = MINI_PX;
    }

    private void buildInspect()
    {
        Ux.Find(new[] { this.inspect }, "inspectBack").OnClick = () => this.hooks.onInspectBack();
        this.instantBtn.OnClick = () => this.hooks.onInstantClean();
    }

    public void refresh()
    {
        this.updatePadTools();
        var d = this.state.data;
        this.coins.Text = $"{d.coins}g";
        var max = this.state.maxWarmth;
        this.warmFill.ve.style.width = UnityEngine.UIElements.Length.Percent((float)((d.warmth / max) * 100));
        this.warmFill.Toggle("low", d.warmth / max < 0.25);
        this.warmText.Text = $"{JS.Round(d.warmth)} / {max}";
        this.mugName.Text = TIER_INFO["gear"].names[this.state.tier("gear") - 1];
        this.pack.Text = $"{d.inventory.Count}";
        this.eco.Text = $"{d.ecology}";
        this.bones.Text = $"{d.bones}";
        this.boneChip.Toggle("hidden", d.bones <= 0);
        this.instantBtn.Toggle("hidden", this.state.tier("cleaning") < 3);
        // buying a digging upgrade changes the tool already in hand
        var dig = this.state.tier("digging");
        if (dig != this.shownDigTier)
        {
            this.shownDigTier = dig;
            if (this.curTool == 2 && !this.badgeOverride)
            {
                var dt = this.digTool();
                showIcon(this.badgeIcon, dt[0]);
                this.badgeName.Text = dt[1];
                this.updatePadTools();
            }
        }
    }

    private int shownDigTier = 0;

    /// <summary>The digging tool in hand follows the digging upgrade: trowel, sand scoop, beach shovel.</summary>
    private string[] digTool()
    {
        var t = this.state.tier("digging");
        return t >= 3 ? new[] { SHOVEL_ICON, "Beach Shovel" } : t == 2 ? new[] { SCOOP_ICON, "Sand Scoop" } : new[] { TROWEL_ICON, "Trowel" };
    }

    public void setTool(int n)
    {
        var info = new Dictionary<int, string[]>
        {
            [1] = new[] { DETECTOR_ICON, "Detector" },
            [2] = this.digTool(),
            [3] = new[] { CAMERA_ICON, "Camera" },
            [4] = new[] { "🔫", "Gun" },
            [5] = new[] { BINOC_ICON, "Binoculars" },
            [6] = new[] { "🏹", "Bow" },
            [7] = new[] { "🚩", "Flags" },
            [8] = new[] { "🪣", "Bucket" },
        };
        this.curTool = n;
        this.badgeOverride = false;
        this.badge.Toggle("hidden", n == 0 || pad.on);
        this.updatePadTools();
        if (n != 0 && info.TryGetValue(n, out var inf))
        {
            showIcon(this.badgeIcon, inf[0]);
            this.badgeName.Text = n == 7 ? $"Flags x{this.state.data.flags}" : inf[1];
        }
        this.scope.Toggle("hidden", n != 1);
        Page.Body.Toggle("scope-on", n == 1);
    }

    /// <summary>Tools that share the d-pad's left slot: the camera, flags (while any are left), the bucket, the bow and the gun.</summary>
    public List<int> padOtherTools()
    {
        var d = this.state.data;
        var list = new List<int> { 3 };
        if (d.flags > 0) list.Add(7);
        if (d.ownsBucket) list.Add(8);
        if (d.ownsBow) list.Add(6);
        if (d.ownsGun) list.Add(4);
        list.RemoveAll(t => !this.toolAllowed(t));
        return list;
    }

    /// <summary>Set by the game: whether a tool can be used where the player is (on the boat and the islands only the camera and binoculars).</summary>
    public Func<int, bool> toolAllowed = _ => true;

    /// <summary>Draws the d-pad: a tool on each side, the one in hand lit up.</summary>
    private void updatePadTools()
    {
        var show = pad.on && this.curTool != 0 && !this.badgeOverride;
        this.padTools.Toggle("hidden", !show);
        if (!show) return;
        var icons = new Dictionary<int, string[]>
        {
            [1] = new[] { DETECTOR_ICON, "Detector" },
            [2] = this.digTool(),
            [5] = new[] { BINOC_ICON, "Binoculars" },
            [3] = new[] { CAMERA_ICON, "Camera" },
            [6] = new[] { "🏹", "Bow" },
            [4] = new[] { "🔫", "Gun" },
            [7] = new[] { "🚩", "Flags" },
            [8] = new[] { "🪣", "Bucket" },
        };
        var others = this.padOtherTools();
        int leftTool = others.Contains(this.curTool) ? this.curTool : (others.Count > 0 ? others[0] : 0);
        var slotTool = new Dictionary<string, int>
        {
            ["up"] = this.toolAllowed(1) ? 1 : 0,
            ["right"] = this.toolAllowed(2) ? 2 : 0,
            ["down"] = 5,
            ["left"] = leftTool,
        };
        foreach (var k in new[] { "up", "right", "down", "left" })
        {
            var t = slotTool[k];
            var slot = this.padSlots[k];
            showIcon(slot, t != 0 && icons.ContainsKey(t) ? icons[t][0] : "·");
            if (t == 7) slot.Add(new UiNode("small", null, $"x{this.state.data.flags}"));
            slot.Toggle("on", t != 0 && t == this.curTool);
            slot.Toggle("empty", t == 0);
        }
        // more than one tool behind the left slot: show them all, and what the next press brings
        bool cycle = others.Count > 1;
        this.padStack.Toggle("hidden", !cycle);
        this.padNext.Toggle("hidden", !cycle);
        if (cycle)
        {
            this.padStack.Clear();
            foreach (var t in others)
            {
                var ps = new UiNode("div", "ps");
                showIcon(ps, icons.TryGetValue(t, out var inf) ? inf[0] : "·");
                ps.Toggle("on", t == this.curTool);
                this.padStack.Add(ps);
            }
            // in hand: the press moves on to the one after; otherwise it picks up the one shown in the slot
            int nextTool = others.Contains(this.curTool) ? others[(others.IndexOf(this.curTool) + 1) % others.Count] : leftTool;
            this.padNext.Clear();
            var name = new UiNode("span", "pn-name");
            name.Text = "next: " + (nextTool == 7 ? $"Flags x{this.state.data.flags}" : icons.TryGetValue(nextTool, out var ni) ? ni[1] : "");
            this.padNext.Add(Glyphs.Make("D-pad left", true), name);
        }
        this.padCaption.Text = icons.TryGetValue(this.curTool, out var curInf) && !string.IsNullOrEmpty(curInf[1])
            ? (this.curTool == 7 ? $"Flags x{this.state.data.flags}" : curInf[1])
            : "";
    }

    public void setMode(string mode)
    {
        this.hud.Toggle("hidden", mode == "INSPECT" || mode == "MENU");
        this.scope.Toggle("dim", mode == "CAMP");
        if (mode == "INSPECT" || mode == "MENU")
        {
            this.scope.AddClass("hidden");
            Page.Body.RemoveClass("scope-on");
        }
    }

    public void showPause(bool on, PauseHooks hooks = null)
    {
        if (!on)
        {
            this.pauseEl?.Remove();
            this.pauseEl = null;
            this.closeSaves();
            return;
        }
        if (this.pauseEl != null || hooks == null) return;
        // one column of buttons, so a controller, the keyboard and the mouse all walk it the same way; the controls that
        // used to be listed here are in Help
        var p = Ux.Load("Pause");
        var actions = p.Must("actions");
        var info = p.Must("info");

        Action mainButtons = null;
        void confirmStep(string question, string yes, Action run)
        {
            actions.Clear();
            // Cancel comes first and is the one chosen, so a second press by mistake does no harm
            var no = new UiNode("button", "btn ghost default", "Cancel");
            no.OnClick = () => mainButtons();
            var ok = new UiNode("button", "btn", yes);
            ok.OnClick = () => run();
            actions.Add(node("p", "confirm-q", question), no, ok);
        }

        mainButtons = () =>
        {
            var count = hooks.saves.list().Count;
            info.Text = count == 0 ? "No saved games yet" : $"{count} saved game{(count == 1 ? "" : "s")}";
            actions.Clear();
            UiNode add(string label, Action fn, string cls = "ghost")
            {
                var b = new UiNode("button", $"btn {cls}", label);
                b.OnClick = fn;
                actions.Add(b);
                return b;
            }
            add("Resume", () => hooks.onResume(), "big default");
            add("Save Game", () => this.openSaves("save", hooks.saves, mainButtons));
            add("Load Game", () => this.openSaves("load", hooks.saves, mainButtons)).Disabled = count == 0;
            add("Settings", () => hooks.onSettings());
            add("Help", () => this.showHelp());
            add("Achievements", () => hooks.onAchievements());
            add("New Game", () => confirmStep("Start a brand new game? Anything you have not saved will be lost.", "Yes, start over", hooks.onNew));
            add("Main Menu", () => confirmStep("Return to the main menu? Anything you have not saved will be lost.", "Yes, go to the menu", hooks.onMenu));
        };
        mainButtons();
        this.pauseEl = p;
        p.MountIn(this.root.ve);
    }

    private UiNode sleepEl = null;
    private UiNode bookEl = null;
    private Action<int> bookTurnFn = null;
    private UiNode menuEl = null;
    private UiNode settingsEl = null;

    /// <summary>The main menu: the title and a list of plain words on the left, the living beach behind.</summary>
    public void showMainMenu(MenuHooks h)
    {
        this.hideMainMenu();
        // the layout is UXML (Resources/UI/Screens/MainMenu.uxml, MenuButton.uxml); the entries depend on the saves
        var m = Ux.Load("MainMenu");
        var list = m.Must("list");
        UiNode mk(string label, Action fn, bool disabled = false)
        {
            var b = Ux.Load("MenuButton");
            b.Must("label").Text = label;
            b.Disabled = disabled;
            b.OnClick = fn;
            list.Add(b);
            return b;
        }
        // the menu opens on what the player most likely wants: carrying on, if there is a game to carry on with
        if (h.hasSaves && h.onContinue != null) mk("Continue", h.onContinue).AddClass("default");
        var fresh = mk("New Game", h.onNew);
        mk("Load Game", h.onLoad, !h.hasSaves);
        if (!h.hasSaves) fresh.AddClass("default");
        mk("Settings", h.onSettings);
        mk("Help", () => this.showHelp());
        mk("Achievements", h.onAchievements);
        mk("Credits", () => this.showCredits());
        mk("Exit Game", h.onExit);
        m.Must("foot").Text = $"Version {__APP_VERSION__}";
        this.menuEl = m;
        m.MountIn(this.root.ve);
    }

    public void hideMainMenu()
    {
        this.menuEl?.Remove();
        this.menuEl = null;
    }

    private static double getSettingDouble(GameSettings s, string key) => key switch
    {
        "master" => s.master,
        "music" => s.music,
        "ambience" => s.ambience,
        "sensitivity" => s.sensitivity,
        "fov" => s.fov,
        "renderScale" => s.renderScale,
        _ => 0,
    };

    private static void setSettingDouble(GameSettings s, string key, double v)
    {
        switch (key)
        {
            case "master": s.master = v; break;
            case "music": s.music = v; break;
            case "ambience": s.ambience = v; break;
            case "sensitivity": s.sensitivity = v; break;
            case "fov": s.fov = v; break;
            case "renderScale": s.renderScale = v; break;
        }
    }

    /// <summary>
    /// Sliders, switches and choices. Each setting is one row that is chosen whole: left and right change it (so do a click
    /// on the arrows, a drag on the slider, or pressing the row). Every change is passed straight back so it takes effect at once.
    /// </summary>
    public void openSettings(GameSettings settings, Action<GameSettings> onChange, Action onClose = null)
    {
        this.closeSettings();
        // the layout is UXML (Settings.uxml, SettingRow.uxml); each setting adds its control to its row here
        var wrap = Ux.Load("Settings");
        var rows = wrap.Must("rows");
        UiNode rowEl(string label, string hintText)
        {
            var row = Ux.Load("SettingRow");
            row.Must("label").Markup = label;
            var hint = row.Must("hint");
            if (!string.IsNullOrEmpty(hintText)) hint.Markup = hintText;
            else hint.Remove();
            rows.Add(row);
            return row;
        }
        void slider(string label, string key, double min, double max, double step, Func<double, string> fmt)
        {
            var row = rowEl(label, null);
            var input = new UiRange("set-range") { Min = min, Max = max, Step = step };
            input.Value = JS.Str(getSettingDouble(settings, key));
            // the row is what is chosen; the slider inside it is only dragged
            input.NoFocus();
            var val = node("span", "set-val", fmt(getSettingDouble(settings, key)));
            void apply(double v)
            {
                setSettingDouble(settings, key, v);
                val.Text = fmt(v);
                onChange(settings);
            }
            input.OnInput = () =>
            {
                if (double.TryParse(input.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) apply(parsed);
            };
            row.OnStep = dir =>
            {
                double next = min + Math.Round((getSettingDouble(settings, key) + dir * step - min) / step) * step;
                next = Math.Round(Math.Max(min, Math.Min(max, next)) * 1000) / 1000;
                input.Value = JS.Str(next);
                apply(next);
            };
            row.Add(input, val);
        }
        void choice(string label, string[][] options, Func<string> get, Action<string> set)
        {
            var row = rowEl(label, null);
            var prev = new UiNode("span", "step-arrow left");
            var text = new UiNode("span", "step-val");
            var next = new UiNode("span", "step-arrow");
            int idx = Math.Max(0, Array.FindIndex(options, o => o[0] == get()));
            int timer = 0;
            void show() => text.Text = options.Length > 0 ? options[idx][1] : "";
            void go(int dir)
            {
                if (options.Length == 0) return;
                idx = (idx + dir + options.Length) % options.Length;
                show();
                // a choice here changes the screen itself, so it waits a moment: stepping past "Windowed" on the way to
                // another mode does not switch the screen twice
                if (timer != 0) GameTimers.Cancel(timer);
                timer = GameTimers.After(650, () =>
                {
                    timer = 0;
                    set(options[idx][0]);
                    onChange(settings);
                });
            }
            row.OnClick = () => go(1);
            row.OnStep = go;
            // the arrows' clicks stop at them (UiNode.OnClick), so the row does not step as well
            prev.OnClick = () => go(-1);
            next.OnClick = () => go(1);
            var box = new UiNode("span", "set-step");
            box.Add(prev, text, next);
            row.Add(box);
            show();
        }
        void toggle(string label, string hintText, string key)
        {
            bool get() => key == "shadows" ? settings.shadows : key == "vsync" ? settings.vsync : key == "invertMouse" ? settings.invertMouse : key == "invertStick" ? settings.invertStick : settings.sharp;
            var row = rowEl(label, hintText);
            var val = new UiNode("span", "set-val");
            void put(bool on)
            {
                if (key == "shadows") settings.shadows = on;
                else if (key == "vsync") settings.vsync = on;
                else if (key == "sharp") settings.sharp = on;
                else if (key == "invertMouse") settings.invertMouse = on;
                else if (key == "invertStick") settings.invertStick = on;
            }
            void show()
            {
                row.Toggle("on", get());
                val.Text = get() ? "On" : "Off";
            }
            row.OnClick = () =>
            {
                put(!get());
                show();
                onChange(settings);
            };
            row.OnStep = dir =>
            {
                if (get() == dir > 0) return;
                put(dir > 0);
                show();
                onChange(settings);
            };
            row.Add(new UiNode("span", "set-switch"), val);
            show();
        }
        string pct(double v) => $"{JS.Round(v * 100)}%";
        rows.Add(node("h3", "caps", "Sound"));
        slider("Master volume", "master", 0, 1, 0.05, pct);
        slider("Radio music", "music", 0, 1, 0.05, pct);
        slider("Sea and wind", "ambience", 0, 1, 0.05, pct);
        rows.Add(node("h3", "caps", "Controls"));
        slider("Mouse look speed", "sensitivity", 0.4, 2.5, 0.05, v => $"{JS.ToFixed(v, 2)}x");
        toggle("Invert mouse look", "pushing the mouse forward looks down", "invertMouse");
        toggle("Invert stick look", "pushing the right stick forward looks down", "invertStick");
        rows.Add(node("h3", "caps", "Display"));
        choice("Text and menu size", new[]
        {
            new[] { "1", "Normal" },
            new[] { "1.15", "Large" },
            new[] { "1.3", "Larger (Steam Deck)" },
        }, () => JS.Str(settings.uiScale), v => settings.uiScale = double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 1);
        choice("Screen mode", new[]
        {
            new[] { "fullscreen", "Fullscreen" },
            new[] { "windowed", "Windowed" },
            new[] { "borderless", "Windowed, no border" },
        }, () => settings.displayMode, v => settings.displayMode = v);
        // the display's own size (in exclusive fullscreen currentResolution is only the mode the game picked)
        var disp = UnityEngine.Screen.mainWindowDisplayInfo;
        int sw = disp.width > 0 ? disp.width : UnityEngine.Screen.currentResolution.width > 0 ? UnityEngine.Screen.currentResolution.width : UnityEngine.Screen.width;
        int sh = disp.height > 0 ? disp.height : UnityEngine.Screen.currentResolution.height > 0 ? UnityEngine.Screen.currentResolution.height : UnityEngine.Screen.height;
        var allSizes = new int[][]
        {
            new[] { 1280, 720 }, new[] { 1280, 800 }, new[] { 1366, 768 }, new[] { 1600, 900 },
            new[] { 1920, 1080 }, new[] { 2560, 1440 }, new[] { 3840, 2160 },
        };
        var sizes = allSizes.Where(wh => sw <= 0 || (wh[0] <= sw && wh[1] <= sh))
            .Select(wh => new[] { $"{wh[0]}x{wh[1]}", $"{wh[0]} × {wh[1]}" })
            .ToArray();
        choice("Window size", sizes, () => settings.windowSize, v => settings.windowSize = v);
        slider("Field of view", "fov", GameSettings.FOV_MIN, GameSettings.FOV_MAX, 1, v => $"{JS.Round(v)}°");
        toggle("VSync", "waits for the screen's refresh: no tearing", "vsync");
        choice("Frame limit (VSync off)", FramePacing.LIMITS.Select(n => new[] { JS.Str(n), n == 0 ? "No limit" : $"{n} fps" }).ToArray(),
            () => JS.Str(settings.fpsLimit), v => settings.fpsLimit = int.TryParse(v, out var n) ? n : 0);
        rows.Add(node("h3", "caps", "Graphics"));
        choice("Quality", new[]
        {
            new[] { "handheld", "Handheld (Steam Deck)" },
            new[] { "high", "High" },
            new[] { "medium", "Medium" },
            new[] { "low", "Low" },
        }, () => settings.quality, v => settings.quality = v);
        toggle("Shadows", "turn off for a smoother game on slower computers", "shadows");
        toggle("Sharp picture", "full screen resolution, heavier to draw", "sharp");
        slider("Render scale", "renderScale", 0.5, 1, 0.05, pct);
        // the keys (Core/Controls.cs): pressing a row waits for the next key, Esc keeps the old one
        rows.Add(node("h3", "caps", "Keys"));
        var keyRows = new List<(Act act, UiNode row, UiNode val)>();
        void showKeys()
        {
            foreach (var (act, row, val) in keyRows)
            {
                row.RemoveClass("listening");
                // a key another row uses too is named there, unless both still have the game's own keys (Space swaps the
                // tools, swims up and brakes on purpose)
                var also = Controls.SharedWith(act);
                row.Toggle("clash", also.Count > 0);
                val.Text = also.Count > 0 ? $"{Controls.KeyNames(act)} (also {string.Join(", ", also)})" : Controls.KeyNames(act);
            }
        }
        foreach (var (act, label) in Controls.Rebindable)
        {
            var row = rowEl(label, null);
            row.AddClass("key-row");
            var val = node("span", "set-val set-key", Controls.KeyNames(act));
            row.Add(val);
            keyRows.Add((act, row, val));
            row.OnClick = () =>
            {
                showKeys();
                row.AddClass("listening");
                val.Text = "Press a key (Esc keeps it)";
                Controls.Rebind(act, showKeys);
            };
        }
        var resetKeys = rowEl("Put every key back", "the keys the game came with");
        resetKeys.OnClick = () =>
        {
            Controls.ResetAll();
            showKeys();
        };
        wrap.Must("done").OnClick = () =>
        {
            this.closeSettings();
            onClose?.Invoke();
        };
        this.settingsEl = wrap;
        wrap.MountIn(this.root.ve);
    }

    public void closeSettings()
    {
        // a rebind still waiting for its key keeps the old key
        Controls.Cancel();
        this.settingsEl?.Remove();
        this.settingsEl = null;
    }

    /// <summary>The actions whose keys Help's paragraphs and lists name (Use, Second use, the map, swimming up).</summary>
    private static readonly Act[] HELP_ACTS = { Act.Use, Act.Use2, Act.Map, Act.Surface };

    /// <summary>The actions a row of Help's key table is about, by its key column.</summary>
    private static Act[] HelpKeyActs(string keys)
    {
        switch (keys)
        {
            case "W A S D": return new[] { Act.Forward, Act.Left, Act.Backward, Act.Right, Act.Run };
            case "F": return new[] { Act.Use };
            case "E": return new[] { Act.Use2 };
            case "1 and 2": return new[] { Act.ToolSwap };
            case "Space / C": return new[] { Act.Ascend, Act.Descend };
            default: return new Act[0];
        }
    }

    private Func<int, bool> helpStepFn = null;

    /// <summary>Moves to the next Help topic along (Q and E, or a controller's bumpers). False when Help is not open.</summary>
    public bool helpStep(int dir) => this.helpStepFn != null && this.helpStepFn(dir);

    /// <summary>How to play: tabs of short topics, from what the game is about to the controls and saving.</summary>
    public void showHelp()
    {
        // the layout is UXML (Help.uxml); the tabs and the topic are filled in here
        var wrap = Ux.Load("Help");
        var tabs = wrap.Must("tabs");
        var body = wrap.Must("body");
        int cur = 0;
        Action<int> show = null;
        var tabWrap = wrap.Must("tabWrap");
        var buttons = HELP_TOPICS.Select((t, i) =>
        {
            var idx = i;
            var b = node("button", $"btn tab{(i == 0 ? " on" : "")}", $"{t.icon} {t.title}");
            b.OnClick = () => show(idx);
            tabWrap.Add(b);
            return b;
        }).ToList();
        // the keys that turn the topics, either side of the tabs
        tabWrap.Before(Glyphs.KeyNode("Q", "LB"));
        tabs.Add(Glyphs.KeyNode("E", "RB"));
        show = (int i) =>
        {
            cur = i;
            for (int k = 0; k < buttons.Count; k++) buttons[k].Toggle("on", k == i);
            body.Clear();
            body.ScrollTop = 0;
            foreach (var blk in HELP_TOPICS[i].blocks)
            {
                // only the controls the player is using: the keyboard's list, or the controller's
                if (blk.h == "Keyboard and mouse" && PadText.pad.on) continue;
                if (blk.h == "Controller" && !PadText.pad.on) continue;
                if (!string.IsNullOrEmpty(blk.h)) body.Add(node("h3", "", blk.h));
                // the keys as the player bound them (Core/Controls.cs); the words of Help only name these
                if (!string.IsNullOrEmpty(blk.p)) body.Add(node("p", "help-p", padText(Controls.Say(blk.p, HELP_ACTS))));
                if (blk.list != null)
                {
                    var ul = new UiNode("ul", "help-list");
                    foreach (var item in blk.list) ul.Add(node("li", "", padText(Controls.Say(item, HELP_ACTS))));
                    body.Add(ul);
                }
                if (blk.keys != null)
                {
                    var keys = new UiNode("div", "keys");
                    foreach (var kv in blk.keys)
                    {
                        var row = new UiNode("div", "krow");
                        var acts = HelpKeyActs(kv[0]);
                        row.Add(node("b", "", Controls.Say(kv[0], acts)), node("span", "", Controls.Say(kv[1], acts)));
                        keys.Add(row);
                    }
                    body.Add(keys);
                }
            }
        };
        show(0);
        this.helpStepFn = dir =>
        {
            if (wrap.ve.panel == null) return false;
            show((cur + dir + buttons.Count) % buttons.Count);
            buttons[cur].Focus();
            return true;
        };
        wrap.Must("back").OnClick = () =>
        {
            wrap.Remove();
            this.helpStepFn = null;
        };
        wrap.MountIn(this.root.ve);
    }

    /// <summary>Every achievement, with the ones already earned lit up.</summary>
    public void showAchievements(IList<AchievementItem> list)
    {
        // the layout is UXML (Achievements.uxml, AchievementRow.uxml)
        var wrap = Ux.Load("Achievements");
        var done = list.Count(a => a.done);
        wrap.Must("fill").ve.style.width = UnityEngine.UIElements.Length.Percent((float)(list.Count > 0 ? JS.Round(done * 100.0 / list.Count) : 0));
        wrap.Must("count").Text = $"{done} of {list.Count} earned";
        var grid = wrap.Must("list");
        foreach (var a in list)
        {
            var row = Ux.Load("AchievementRow");
            if (a.done) row.AddClass("done");
            var hidden = a.secret && !a.done;
            row.Must("icon").Markup = hidden ? "❔" : a.icon;
            row.Must("name").Markup = hidden ? "Secret" : a.name;
            row.Must("desc").Markup = hidden ? "Keep exploring to find out." : a.desc;
            grid.Add(row);
        }
        wrap.Must("back").OnClick = () => wrap.Remove();
        wrap.MountIn(this.root.ve);
    }

    public void showAchievements(IList<AchView> list) => this.showAchievements(list.Cast<AchievementItem>().ToList());

    /// <summary>The credits: who made the game. Opened from the main menu.</summary>
    public void showCredits()
    {
        var wrap = Ux.Load("Credits");
        wrap.Must("version").Text = $"Version {__APP_VERSION__} · {__BUILD_YEAR__}";
        wrap.Must("back").OnClick = () => wrap.Remove();
        wrap.MountIn(this.root.ve);
    }

    /// <summary>What "Exit Game" shows: a web page cannot always close itself, so say goodbye nicely.</summary>
    public void showFarewell(Action onBack)
    {
        var wrap = Ux.Load("Farewell");
        wrap.Must("back").OnClick = () =>
        {
            wrap.Remove();
            onBack();
        };
        wrap.MountIn(this.root.ve);
    }

    /// <summary>The journal: two pages side by side (one on a narrow screen) with buttons to turn them.</summary>
    public void showBook(IList<BookPage> pages, Action onClose)
    {
        this.hideBook();
        // the layout is UXML (Book.uxml); the pages are filled in here
        var wrap = Ux.Load("Book");
        var spread = wrap.Must("spread");
        var prev = wrap.Must("prev");
        var count = wrap.Must("count");
        var next = wrap.Must("next");
        wrap.Must("close").OnClick = () => onClose();
        var per = Page.Width < 760 ? 1 : 2;
        int idxNew = -1;
        for (int k = 0; k < pages.Count; k++)
        {
            if (pages[k].isNew) { idxNew = k; break; }
        }
        int first = Math.Max(0, idxNew);
        first -= first % per;
        void render()
        {
            spread.Clear();
            for (int k = 0; k < per; k++)
            {
                var p = first + k < pages.Count ? pages[first + k] : null;
                var side = new UiNode("div", $"book-page {(k == 0 ? "left" : "right")}");
                if (p != null)
                {
                    var head = new UiNode("div", "bp-head");
                    head.Add(node("span", "bp-group", $"{p.icon}  {p.group}"));
                    if (p.isNew) head.Add(node("span", "bp-new", "New"));
                    else if (!p.found) head.Add(node("span", "bp-todo", "To discover"));
                    side.Add(head, node("h2", "", p.title), node("p", "", p.text), node("div", "bp-num", JS.Str(first + k + 1)));
                }
                spread.Add(side);
            }
            var last = Math.Min(pages.Count, first + per);
            count.Text = pages.Count > 0 ? $"{first + 1}{(last > first + 1 ? "–" + last : "")} of {pages.Count}" : "No pages yet";
            prev.Disabled = first <= 0;
            next.Disabled = first + per >= pages.Count;
        }
        void turn(int dir)
        {
            var n = first + dir * per;
            if (n < 0 || n >= pages.Count) return;
            first = n;
            render();
            var now = MenuNav.Current.node;
            if (now == prev || now == next)
            {
                // keep the focus on a button that still works, so a controller can keep turning
                if (now.Disabled) (dir > 0 ? prev : next).Focus();
            }
        }
        prev.OnClick = () => turn(-1);
        next.OnClick = () => turn(1);
        this.bookTurnFn = turn;
        render();
        this.bookEl = wrap;
        wrap.MountIn(this.root.ve);
    }

    public void showBook(IList<BookPageView> pages, Action onClose) => this.showBook(pages.Cast<BookPage>().ToList(), onClose);

    /// <summary>The camp post: one letter at a time on cream notepaper, newest first.</summary>
    public void showMail(IList<MailLetter> letters, Action onClose)
    {
        this.hideBook();
        // the layout is UXML (Letters.uxml); the letter is filled in here
        var wrap = Ux.Load("Letters");
        var sheet = wrap.Must("sheet");
        var prev = wrap.Must("prev");
        prev.Text = "‹ Newer";
        var count = wrap.Must("count");
        var next = wrap.Must("next");
        next.Text = "Older ›";
        wrap.Must("close").OnClick = () => onClose();
        // newest letters first
        var list = letters.Reverse().ToList();
        int idxNew = list.FindIndex(l => l.isNew);
        int i = Math.Max(0, idxNew);
        void render()
        {
            sheet.Clear();
            var l = i >= 0 && i < list.Count ? list[i] : null;
            if (l == null)
            {
                sheet.Add(node("p", "mail-empty", "No letters yet. Return something to its owner and they will write to you."));
                count.Text = "Empty post box";
            }
            else
            {
                var head = new UiNode("div", "mail-head");
                head.Add(node("span", "mail-from", $"From: {l.from}"));
                if (l.isNew) head.Add(node("span", "bp-new", "New"));
                head.Add(node("span", "mail-stamp", "✉"));
                sheet.Add(head, node("p", "mail-greeting", l.greeting), node("p", "mail-body", l.text), node("p", "mail-sign", l.sign));
                count.Text = $"Letter {i + 1} of {list.Count}";
            }
            prev.Disabled = i <= 0;
            next.Disabled = i >= list.Count - 1;
        }
        void turn(int dir)
        {
            // left (dir -1) means a newer letter, right an older one
            var n = i + dir;
            if (n < 0 || n >= list.Count) return;
            i = n;
            render();
            var now = MenuNav.Current.node;
            if ((now == prev || now == next) && now.Disabled)
            {
                (dir > 0 ? prev : next).Focus();
            }
        }
        prev.OnClick = () => turn(-1);
        next.OnClick = () => turn(1);
        this.bookTurnFn = turn;
        render();
        this.bookEl = wrap;
        wrap.MountIn(this.root.ve);
    }

    public void showMail(IList<MailLetterView> letters, Action onClose) => this.showMail(letters.Cast<MailLetter>().ToList(), onClose);

    /// <summary>The end of the story. Closing it returns to the game, which carries on.</summary>
    public void showEnding(IList<string[]> stats, Action onClose)
    {
        this.hideBook();
        // the layout is UXML (Ending.uxml); the facts are filled in here
        var wrap = Ux.Load("Ending");
        var facts = wrap.Must("facts");
        foreach (var pair in stats)
        {
            string label = pair[0], value = pair[1];
            facts.Add(new UiNode("div", "ending-stat").Add(node("b", null, value), node("span", null, label)));
        }
        wrap.Must("back").OnClick = () => onClose();
        this.bookEl = wrap;
        wrap.MountIn(this.root.ve);
    }

    public void showEnding(IList<(string, string)> stats, Action onClose)
    {
        this.showEnding(stats.Select(s => new[] { s.Item1, s.Item2 }).ToList(), onClose);
    }

    /// <summary>A handwritten journal read page by page, oldest entry first. Shares the post's paper look.</summary>
    public void showStory(string title, IList<StoryPage> pages, Action onClose)
    {
        this.hideBook();
        // the layout is UXML (Letters.uxml), as the post's; the page is filled in here
        var wrap = Ux.Load("Letters");
        var sheet = wrap.Must("sheet");
        var prev = wrap.Must("prev");
        prev.Text = "\u2039 Earlier";
        var count = wrap.Must("count");
        var next = wrap.Must("next");
        next.Text = "Later \u203a";
        wrap.Must("close").OnClick = () => onClose();
        int i = 0;
        void render()
        {
            sheet.Clear();
            var p = pages[i];
            var head = new UiNode("div", "mail-head");
            head.Add(node("span", "mail-from", title), node("span", "mail-stamp", "\ud83d\udcd6"));
            sheet.Add(head, node("p", "mail-greeting", p.heading), node("p", "mail-body", p.text), node("p", "mail-sign", p.sign));
            count.Text = $"Page {i + 1} of {pages.Count}";
            prev.Disabled = i <= 0;
            next.Disabled = i >= pages.Count - 1;
        }
        void turn(int dir)
        {
            var n = i + dir;
            if (n < 0 || n >= pages.Count) return;
            i = n;
            render();
            var now = MenuNav.Current.node;
            if ((now == prev || now == next) && now.Disabled)
            {
                (dir > 0 ? prev : next).Focus();
            }
        }
        prev.OnClick = () => turn(-1);
        next.OnClick = () => turn(1);
        this.bookTurnFn = turn;
        render();
        this.bookEl = wrap;
        wrap.MountIn(this.root.ve);
    }

    public void bookTurn(int dir)
    {
        this.bookTurnFn?.Invoke(dir);
    }

    /// <summary>A soft dark vignette with drifting Zs while the player sleeps in the hammock.</summary>
    public void setSleep(bool on)
    {
        if (!on)
        {
            this.sleepEl?.Remove();
            this.sleepEl = null;
            return;
        }
        if (this.sleepEl != null) return;
        var v = Ux.Load("Sleep");
        this.sleepEl = v;
        v.MountIn(this.root.ve);
    }

    public void hideBook()
    {
        this.bookEl?.Remove();
        this.bookEl = null;
        this.bookTurnFn = null;
    }

    /// <summary>The list of saves, for choosing what to load or what to overwrite.</summary>
    public void openSaves(string mode, SaveHooks hooks, Action onClose = null)
    {
        this.closeSaves();
        // the layout is UXML (Saves.uxml, SaveRow.uxml); the name box goes in above the list when saving
        var m = Ux.Load("Saves");
        m.Must("title").Text = mode == "save" ? "Save game" : "Load game";
        var list = m.Must("list");

        Action renderList = null;
        UiText nameInput = null;
        bool nameEdited = false;
        if (mode == "save")
        {
            var row = new UiNode("div", "save-new");
            nameInput = new UiText("save-name");
            nameInput.MaxLength = 40;
            nameInput.Placeholder = "Name this save";
            var create = new UiNode("button", "btn", "Save as new");
            row.Add(nameInput, create);
            list.Before(row);
            create.OnClick = () =>
            {
                var nm = !string.IsNullOrEmpty(nameInput.Value) ? nameInput.Value : $"Save {hooks.list().Count + 1}";
                var meta = hooks.save(nm, null);
                if (meta != null) renderList();
            };
            nameInput.Value = $"Save {hooks.list().Count + 1}";
            // the pre-filled "Save N" is only a suggestion: it must not rename a save that is overwritten
            var typed = nameInput;
            typed.OnInput = () => nameEdited = true;
            // a controller presses A on the box: Steam's on-screen keyboard types the name (a Deck has no real keyboard handy)
            typed.OnClick = () =>
            {
                SteamService.ShowKeyboard("Save name", typed.Value, 40, text =>
                {
                    if (text == null) return;
                    typed.Value = text;
                    nameEdited = true;
                });
            };
        }

        m.Must("note").Text = mode == "save" ? "Make as many saves as you like. Pick one below to save over it." : "Loading replaces your current game. Unsaved progress is lost.";
        m.Must("back").OnClick = () =>
        {
            this.closeSaves();
            onClose?.Invoke();
        };

        renderList = () =>
        {
            list.Clear();
            var saves = hooks.list();
            if (saves.Count == 0) list.Add(new UiNode("p", "save-empty", "No saved games yet."));
            foreach (var meta in saves)
            {
                var metaId = meta.id;
                var metaName = meta.name;
                var row = Ux.Load("SaveRow");
                row.Must("name").Markup = metaName;
                row.Must("desc").Markup = describe(meta);
                var main = row.Must("main");
                main.Text = mode == "save" ? "Overwrite" : "Load";
                main.OnClick = () =>
                {
                    if (mode == "save")
                    {
                        var trimmed = nameEdited && nameInput != null && nameInput.Value != null ? nameInput.Value.Trim() : "";
                        hooks.save(!string.IsNullOrEmpty(trimmed) ? trimmed : metaName, metaId);
                        renderList();
                    }
                    else hooks.load(metaId);
                };
                var del = row.Must("del");
                var armed = false;
                del.OnClick = () =>
                {
                    if (!armed)
                    {
                        armed = true;
                        del.Text = "Sure?";
                        GameTimers.After(2500, () =>
                        {
                            armed = false;
                            del.Text = "🗑";
                        });
                        return;
                    }
                    hooks.remove(metaId);
                    renderList();
                };
                list.Add(row);
            }
        };
        renderList();
        this.savesEl = m;
        m.MountIn(this.root.ve);
    }

    public void closeSaves()
    {
        this.savesEl?.Remove();
        this.savesEl = null;
    }

    private UiNode savesEl = null;
    private UiNode pauseEl = null;

    /// <summary>A second prompt, for the E key.</summary>
    public void setPrompt2(string label)
    {
        if (!string.IsNullOrEmpty(label)) this.prompt2Text.Text = label;
        this.prompt2.Toggle("on", !string.IsNullOrEmpty(label));
    }

    /// <summary>Shows the "press F" prompt for something the player is standing next to.</summary>
    public void setPrompt(string label)
    {
        if (!string.IsNullOrEmpty(label)) this.promptText.Text = label;
        this.prompt.Toggle("on", !string.IsNullOrEmpty(label));
    }

    private string clockIcon;
    private int clockDay = -1;

    public void setClock(string icon, int day)
    {
        if (icon == this.clockIcon && day == this.clockDay) return;
        this.clockIcon = icon;
        this.clockDay = day;
        this.clock.Text = $"{icon} Day {day}";
    }

    public CanvasRenderingContext2D miniCtx => this.miniCanvas.getContext("2d");

    /// <summary>Full-screen island map. `draw` paints onto the large canvas it is given.</summary>
    public void showMap(bool on, Action<CanvasRenderingContext2D, double> draw = null, Action<double, double> onPick = null, IList<MapAlt> alts = null)
    {
        if (!on)
        {
            this.mapEl?.Remove();
            this.mapEl = null;
            this.mapTabs = false;
            this.mapToggle = null;
            this.mapRepaint = null;
            return;
        }
        if (this.mapEl != null || draw == null) return;
        if (alts == null) alts = new List<MapAlt>();
        // the layout is UXML (Map.uxml); the tabs and the canvas are made here
        var wrap = Ux.Load("Map");
        var names = new List<string> { "Island map" };
        names.AddRange(alts.Select(x => x.name));
        int mode = 0;
        var title = wrap.Must("title");
        title.Markup = names[0];
        var tabs = wrap.Must("tabs");
        var tabBtns = names.Select((n, i) => node("button", $"btn tab{(i == 0 ? " on" : "")}", n)).ToList();
        if (alts.Count > 0)
        {
            foreach (var tb in tabBtns) tabs.Add(tb);
        }
        else
        {
            tabs.Remove();
        }
        int size = (int)Math.Min(600, Math.Floor(Math.Min(Page.Width, Page.Height) * 0.72));
        var c = new UiCanvas("bigmap-canvas");
        c.Width = c.Height = size;
        Ux.Replace(wrap.Must("canvasSlot"), c);
        var ctx2 = c.getContext("2d");
        void paint()
        {
            if (mode > 0) alts[mode - 1].draw(ctx2, size);
            else draw(ctx2, size);
        }
        paint();
        void setMode(int i)
        {
            mode = i;
            title.Text = names[i];
            for (int k = 0; k < tabBtns.Count; k++) tabBtns[k].Toggle("on", k == i);
            paint();
        }
        for (int i = 0; i < tabBtns.Count; i++)
        {
            int idx = i;
            tabBtns[i].OnClick = () => setMode(idx);
        }
        this.mapToggle = () => setMode((mode + 1) % names.Count);
        this.mapRepaint = () =>
        {
            if (mode > 0) paint();
        };
        // a click marks the place under it, in the map's own 0..1 across and down
        c.ve.RegisterCallback<UnityEngine.UIElements.ClickEvent>(e =>
        {
            var r = c.ve.worldBound;
            onPick?.Invoke((e.position.x - r.xMin) / r.width, (e.position.y - r.yMin) / r.height);
            paint();
        });
        c.ve.pickingMode = UnityEngine.UIElements.PickingMode.Position;
        this.mapEl = wrap;
        this.mapTabs = alts.Count > 0;
        wrap.MountIn(this.root.ve);
    }

    /// <summary>The big map's canvas while the map is open (the controller's cursor moves over it), else null.</summary>
    public UiCanvas bigMapCanvas => this.mapEl == null ? null : MenuNav.Find(this.mapEl.ve, v => v.ClassListContains("bigmap-canvas")).Select(e => e.node as UiCanvas).FirstOrDefault();

    /// <summary>True while the big map has a second map to flip to.</summary>
    public bool mapHasTabs => this.mapEl != null && this.mapTabs;

    /// <summary>Flips between the island map and the treasure map, if the player has one.</summary>
    public void toggleMap()
    {
        this.mapToggle?.Invoke();
    }

    /// <summary>Redraws the map that is showing (the treasure crosses pulse).</summary>
    public void repaintMap()
    {
        this.mapRepaint?.Invoke();
    }

    /// <summary>The minimap is of no use under the dunes.</summary>
    public void setUnderground(bool on)
    {
        Page.Body.Toggle("underground", on);
    }

    private UiNode diveTint = null;

    /// <summary>Underwater the minimap goes away and a soft blue vignette comes in at the edges.</summary>
    public void setDiving(bool on)
    {
        Page.Body.Toggle("diving", on);
        if (this.diveTint == null)
        {
            this.diveTint = new UiNode("div", "dive-tint");
            this.hud.Add(this.diveTint);
        }
        this.diveTint.Toggle("on", on);
    }

    /// <summary>Replaces the tool badge text (used while driving).</summary>
    public void setBadgeText(string icon, string name)
    {
        this.badgeOverride = true;
        this.updatePadTools();
        this.badge.RemoveClass("hidden");
        this.badgeIcon.Text = icon;
        this.badgeName.Text = name;
    }

    /// <summary>The two-lens binocular view with a range readout. Redraws only when something changed.</summary>
    public void setBinoculars(bool on, string zoom = "", string range = "")
    {
        // put away (nearly always): nothing to build or compare
        if (!on && this.binocKey == "off") return;
        var key = on ? $"{pad.on}|{zoom}|{range}|{JS.Str(Page.Width)}x{JS.Str(Page.Height)}" : "off";
        if (key == this.binocKey) return;
        this.binocKey = key;
        this.binoc.Toggle("hidden", !on);
        this.binocText.Toggle("hidden", !on);
        if (!on) return;
        int w = (int)Page.Width;
        int h = (int)Page.Height;
        // The browser redrew this whole screen-sized canvas whenever the zoom or the distance changed, which is nearly every
        // frame while looking around; drawn on the CPU here, that stuttered. The lenses, reticle and legend are redrawn only
        // when the screen or the controls change, and the readout has a small canvas of its own.
        this.drawBinocReadout(zoom, range, w, h);
        var frameKey = $"{pad.on}|{w}x{h}";
        if (frameKey == this.binocFrameKey) return;
        this.binocFrameKey = frameKey;
        if (this.binoc.Width != w) this.binoc.Width = w;
        if (this.binoc.Height != h) this.binoc.Height = h;
        var g = this.binoc.getContext("2d");
        g.clearRect(0, 0, w, h);
        var r = Math.Min(w * 0.27, h * 0.46);
        var off = r * 0.62;
        g.fillStyle = "rgba(4,6,8,0.97)";
        g.fillRect(0, 0, w, h);
        g.globalCompositeOperation = "destination-out";
        foreach (var dx in new[] { -off, off })
        {
            g.beginPath();
            g.arc(w / 2.0 + dx, h / 2.0, r, 0, Math.PI * 2);
            g.fill();
        }
        g.globalCompositeOperation = "source-over";
        g.strokeStyle = "rgba(30,34,40,0.9)";
        g.lineWidth = 5;
        foreach (var dx in new[] { -off, off })
        {
            g.beginPath();
            g.arc(w / 2.0 + dx, h / 2.0, r, 0, Math.PI * 2);
            g.stroke();
        }
        // reticle: a fine cross with ticks, centred between the lenses
        g.strokeStyle = "rgba(255,255,255,0.75)";
        g.lineWidth = 1.5;
        g.beginPath();
        g.moveTo(w / 2.0 - 36, h / 2.0);
        g.lineTo(w / 2.0 - 6, h / 2.0);
        g.moveTo(w / 2.0 + 6, h / 2.0);
        g.lineTo(w / 2.0 + 36, h / 2.0);
        g.moveTo(w / 2.0, h / 2.0 - 36);
        g.lineTo(w / 2.0, h / 2.0 - 6);
        g.moveTo(w / 2.0, h / 2.0 + 6);
        g.lineTo(w / 2.0, h / 2.0 + 36);
        g.stroke();
        for (int i = 1; i <= 4; i++)
        {
            g.beginPath();
            g.moveTo(w / 2.0 - 6 - i * 7, h / 2.0 - 4);
            g.lineTo(w / 2.0 - 6 - i * 7, h / 2.0 + 4);
            g.moveTo(w / 2.0 + 6 + i * 7, h / 2.0 - 4);
            g.lineTo(w / 2.0 + 6 + i * 7, h / 2.0 + 4);
            g.stroke();
        }
        // readout
        g.font = "600 20px \"Trebuchet MS\", sans-serif";
        g.textAlign = "center";
        g.fillStyle = "rgba(255,248,230,0.95)";

        // legend: how to use and how to put the binoculars down
        var items = pad.on
            ? new string[][] { new[] { "RT", "Change zoom" }, new[] { "LT", "Lower binoculars" }, new[] { "LB / RB", "Switch tool" } }
            : new string[][] { new[] { "Click", "Change zoom" }, new[] { "Right-click", "Lower binoculars" }, new[] { "Scroll", "Switch tool" } };
        g.font = "600 13px \"Trebuchet MS\", sans-serif";
        const string labelFont = "13px \"Trebuchet MS\", sans-serif";
        const double gap = 26;
        const double keyPad = 9;
        var parts = items.Select(it =>
        {
            string k = it[0], t = it[1];
            g.font = "700 13px \"Trebuchet MS\", sans-serif";
            var kw = g.measureText(k).width + keyPad * 2;
            g.font = labelFont;
            var tw = g.measureText(t).width;
            return (k, t, kw, tw, total: kw + 8 + tw);
        }).ToList();
        var totalW = parts.Sum(p => p.total) + gap * (parts.Count - 1);
        double x = (w - totalW) / 2.0;
        double y = h - 30;
        foreach (var p in parts)
        {
            g.fillStyle = "rgba(247,236,214,0.95)";
            g.beginPath();
            g.roundRect(x, y - 14, p.kw, 24, 7);
            g.fill();
            g.fillStyle = "#3b2a1e";
            g.font = "700 13px \"Trebuchet MS\", sans-serif";
            g.textAlign = "center";
            g.fillText(p.k, x + p.kw / 2.0, y + 3);
            g.fillStyle = "rgba(255,248,230,0.9)";
            g.font = labelFont;
            g.textAlign = "left";
            g.fillText(p.t, x + p.kw + 8, y + 3);
            x += p.total + gap;
        }
    }

    /// <summary>
    /// The camera's viewfinder: the middle 4:3 of the screen, which is what the photo keeps, with the rest dimmed, corner marks, a
    /// focus square and how to use it. Redrawn only when the screen, the controls or the number of photos change.
    /// </summary>
    public void setViewfinder(bool on, int count = 0, int max = 0)
    {
        int w = (int)Page.Width;
        int h = (int)Page.Height;
        var key = on ? $"{pad.on}|{count}|{w}x{h}" : "off";
        if (key == this.finderKey) return;
        this.finderKey = key;
        this.finder.Toggle("hidden", !on);
        // nothing but the picture while the camera is at the eye: the "F: pet the dog" prompts wait until it is lowered
        // (Style.uss: .viewfinder .prompt.on). The keys themselves still work.
        Page.Body.Toggle("viewfinder", on);
        if (!on) return;
        if (this.finder.Width != w) this.finder.Width = w;
        if (this.finder.Height != h) this.finder.Height = h;
        var g = this.finder.getContext("2d");
        g.clearRect(0, 0, w, h);
        double fw = Math.Min(w, h * 4.0 / 3.0), fh = Math.Min(h, fw * 3.0 / 4.0);
        double fx = (w - fw) / 2.0, fy = (h - fh) / 2.0;
        // outside the frame is not in the photo
        g.fillStyle = "rgba(8,8,10,0.55)";
        if (fx > 0)
        {
            g.fillRect(0, 0, fx, h);
            g.fillRect(fx + fw, 0, w - fx - fw, h);
        }
        if (fy > 0)
        {
            g.fillRect(fx, 0, fw, fy);
            g.fillRect(fx, fy + fh, fw, h - fy - fh);
        }
        // corner marks just inside the frame
        double m = Math.Max(18, fh * 0.05), len = Math.Max(26, fh * 0.07);
        g.strokeStyle = "rgba(255,255,255,0.9)";
        g.lineWidth = 3;
        g.beginPath();
        foreach (var (cx, sx) in new[] { (fx + m, 1), (fx + fw - m, -1) })
        {
            foreach (var (cy, sy) in new[] { (fy + m, 1), (fy + fh - m, -1) })
            {
                g.moveTo(cx, cy + sy * len);
                g.lineTo(cx, cy);
                g.lineTo(cx + sx * len, cy);
            }
        }
        g.stroke();
        // a small focus square in the middle
        double q = Math.Max(14, fh * 0.03);
        g.lineWidth = 1.5;
        g.strokeStyle = "rgba(255,255,255,0.75)";
        g.strokeRect(w / 2.0 - q, h / 2.0 - q, q * 2, q * 2);
        // a red dot and the photo count, along the top inside the frame
        g.beginPath();
        g.fillStyle = "#e5483a";
        g.arc(fx + m + 14, fy + m + 22, 6, 0, Math.PI * 2);
        g.fill();
        g.font = "600 16px \"Trebuchet MS\", sans-serif";
        g.textAlign = "right";
        g.fillStyle = "rgba(255,248,230,0.95)";
        g.fillText($"{count} / {max}", fx + fw - m - 6, fy + m + 28);
        // legend: how to take a photo and how to put the camera down
        var items = pad.on
            ? new string[][] { new[] { "RT", "Take photo" }, new[] { "LT", "Lower camera" }, new[] { "LB / RB", "Switch tool" } }
            : new string[][] { new[] { "Click", "Take photo" }, new[] { "Right-click", "Lower camera" }, new[] { "Scroll", "Switch tool" } };
        const string labelFont = "13px \"Trebuchet MS\", sans-serif";
        const string keyFont = "700 13px \"Trebuchet MS\", sans-serif";
        const double gap = 26;
        const double keyPad = 9;
        var parts = items.Select(it =>
        {
            g.font = keyFont;
            var kw = g.measureText(it[0]).width + keyPad * 2;
            g.font = labelFont;
            var tw = g.measureText(it[1]).width;
            return (k: it[0], t: it[1], kw, total: kw + 8 + tw);
        }).ToList();
        double x = (w - (parts.Sum(p => p.total) + gap * (parts.Count - 1))) / 2.0;
        double y = h - 30;
        foreach (var p in parts)
        {
            g.fillStyle = "rgba(247,236,214,0.95)";
            g.beginPath();
            g.roundRect(x, y - 14, p.kw, 24, 7);
            g.fill();
            g.fillStyle = "#3b2a1e";
            g.font = keyFont;
            g.textAlign = "center";
            g.fillText(p.k, x + p.kw / 2.0, y + 3);
            g.fillStyle = "rgba(255,248,230,0.9)";
            g.font = labelFont;
            g.textAlign = "left";
            g.fillText(p.t, x + p.kw + 8, y + 3);
            x += p.total + gap;
        }
    }

    /// <summary>A quick white flash over the whole screen as a photo is taken.</summary>
    public void photoFlash()
    {
        this.flashAt = JS.Now();
        this.flashEl.ve.style.opacity = 0.85f;
        this.flashEl.RemoveClass("hidden");
    }

    /// <summary>Fades the flash out; called every frame.</summary>
    public void tickFlash()
    {
        if (this.flashAt < 0) return;
        double k = (JS.Now() - this.flashAt) / 260.0;
        if (k >= 1)
        {
            this.flashAt = -1;
            this.flashEl.AddClass("hidden");
            return;
        }
        this.flashEl.ve.style.opacity = (float)(0.85 * (1 - k) * (1 - k));
    }

    /// <summary>The strength meter under the crosshair while a bow is drawn. 0 hides it.</summary>
    public void setDraw(double p)
    {
        this.drawbar.Toggle("hidden", p <= 0.001);
        this.drawFill.ve.style.width = UnityEngine.UIElements.Length.Percent((float)JS.Round(p * 100));
    }

    public void setCrosshair(bool on)
    {
        this.cross.Toggle("on", on);
    }

    private UiNode kbdF = null;
    /// <summary>For the -autoshot-keys test: the key the "use" prompt shows.</summary>
    internal string smokeKeyF => this.kbdF?.FullText ?? "";
    private UiNode kbdE = null;

    /// <summary>Switches prompts and hints between keyboard and controller wording.</summary>
    public void setPadMode(bool on)
    {
        pad.on = on;
        this.badge.Toggle("hidden", this.curTool == 0 || (on && !this.badgeOverride));
        this.updatePadTools();
        if (this.kbdF != null) Glyphs.Set(this.kbdF, on ? "A" : Controls.KeyName(Act.Use), on);
        if (this.kbdE != null) Glyphs.Set(this.kbdE, on ? "X" : Controls.KeyName(Act.Use2), on);
        // every other key drawn on screen follows
        Glyphs.refresh(on);
        this.hintKey = null;
        this.padbarKey = "";
        var help = this.inspectHelp;
        if (help != null) help.Text = on ? "Left stick turns the find · hold RT and move the right stick to brush it clean" : "Drag with the left mouse to scrub · Right-drag or Q / E / arrows to rotate";
        this.binocKey = "";
        this.finderKey = "";
    }

    public void setHint(string text)
    {
        var t = !string.IsNullOrEmpty(text) ? padText(text) : "";
        var key = (pad.on ? "p|" : "k|") + t;
        if (key != this.hintKey)
        {
            this.hintKey = key;
            // the keys a hint names are drawn as key caps; an empty hint keeps the old words while it fades out
            if (t.Length > 0) Glyphs.Fill(this.hint, t, pad.on);
        }
        this.hint.Toggle("show", t.Length > 0);
    }

    /// <summary>
    /// The line along the bottom of a menu that says what the buttons do ("A Select  B Back"). Null or empty hides it.
    /// `left` puts it at the bottom left, for the camp panel that fills the right of the screen.
    /// </summary>
    public void setMenuBar(IList<Glyphs.Prompt> items, bool left = false)
    {
        if (items == null || items.Count == 0)
        {
            if (!this.padbar.Has("hidden"))
            {
                this.padbar.AddClass("hidden");
                Page.Body.RemoveClass("menu-open");
            }
            this.padbarKey = "";
            return;
        }
        var key = (pad.on ? "p" : "k") + (left ? "<" : ">") + string.Join("|", items.Select(i => i.label + ":" + string.Join(",", pad.on ? i.pad : i.kb)));
        if (key == this.padbarKey) return;
        this.padbarKey = key;
        Glyphs.FillBar(this.padbar, items, pad.on);
        this.padbar.Toggle("left", left);
        this.padbar.RemoveClass("hidden");
        Page.Body.AddClass("menu-open");
    }

    /// <summary>Puts a full-screen layer (the telescope's eyepiece) over the HUD but under the toasts and the fade.</summary>
    public void addLayer(UiNode e) => e.MountBefore(this.toasts.ve);

    public void toast(string msg)
    {
        // the words as the page took them (entities decoded; text with markup never comes here)
        var t = new UiNode("div", "toast", padText(msg));
        this.toasts.Add(t);
        GameTimers.NextFrame(_ => t.AddClass("in"));
        GameTimers.After(3200, () =>
        {
            t.RemoveClass("in");
            GameTimers.After(500, () => t.Remove());
        });
    }

    /// <summary>Cross-fades through black, running `mid` at the darkest point.</summary>
    public void fade(Action mid)
    {
        this.fadeEl.AddClass("on");
        GameTimers.After(320, () =>
        {
            mid();
            GameTimers.NextFrame(_ => this.fadeEl.RemoveClass("on"));
        });
    }

    public void showInspect(string name, string customTitle = null)
    {
        this.inspectTitle.Text = customTitle ?? (name == "" ? "" : "Something encrusted in sand…");
        this.result.AddClass("hidden");
        this.inspect.RemoveClass("hidden");
        this.setProgress(0);
    }

    public void hideInspect()
    {
        this.inspect.AddClass("hidden");
        this.result.AddClass("hidden");
    }

    public void setProgress(double p)
    {
        this.progFill.ve.style.width = UnityEngine.UIElements.Length.Percent((float)JS.Round(p * 100));
        this.progText.Text = $"{JS.Round(p * 100)}%";
    }

    /// <summary>A new element holding these words.</summary>
    private static UiNode node(string tag, string cls, string html)
    {
        var n = new UiNode(tag, cls);
        n.Markup = html;
        return n;
    }

    public void showResult(string name, string lore, string valueText, string category, string button = "Add to Backpack")
    {
        this.inspectTitle.Text = name;
        this.result.Clear();
        // the layout is UXML (ResultCard.uxml); the words are filled in here
        var card = Ux.Load("ResultCard");
        card.Must("cat").Markup = category;
        card.Must("name").Markup = name;
        card.Must("lore").Markup = lore;
        card.Must("worth").Markup = valueText;
        card.Must("label").Markup = button;
        Ux.Replace(card.Must("keySlot"), Glyphs.KeyNode("Space", "A"));
        var b = card.Must("go");
        b.OnClick = () => this.hooks.onContinue();
        this.result.Add(card);
        this.result.RemoveClass("hidden");
    }

    public bool isResultOpen()
    {
        return !this.result.Has("hidden");
    }

    public void showIntro(Action onStart, Action onLoad = null)
    {
        this.intro.RemoveClass("gone");
        if (this.intro.ve.panel == null) this.intro.MountIn(this.root.ve);
        this.intro.Clear();
        // the layout is UXML (Intro.uxml); the hint, the detector's drawing and the Load button are code
        var card = Ux.Load("Intro");
        // the drawing was sized while its span was not yet on the page (16.36 px, as the page's parser sized it)
        var detector = new UiNode("span");
        detector.Svg(DETECTOR_ICON);
        Ux.Replace(card.Must("detectorSlot"), detector);
        card.Must("hint").Markup = $"{(pad.on ? "Press Start" : "Press Esc")} at any time for the pause menu. Help there explains every control.";
        var begin = card.Must("begin");
        var load = card.Must("load");
        if (onLoad == null)
        {
            load.Remove();
            load = null;
        }
        this.intro.Add(card);
        void go()
        {
            this.intro.AddClass("gone");
            GameTimers.After(700, () => this.intro.Remove());
            onStart();
        }
        begin.OnClick = go;
        if (load != null) load.OnClick = () => onLoad?.Invoke();
    }

    /// <summary>Used when a saved game is loaded straight into play.</summary>
    private UiNode cineCaption, cineText, cineSkip, cineFlash;

    /// <summary>Lightning in the opening: whitens the whole screen by this much, 0..1.</summary>
    public void setFlash(double amount)
    {
        if (this.cineFlash == null)
        {
            if (amount <= 0 || this.cineCaption == null) return;
            this.cineFlash = new UiNode("div", "cine-flash");
            // under the caption, so the words stay readable through the flash
            this.cineFlash.MountBefore(this.cineCaption.ve);
        }
        this.cineFlash.ve.style.opacity = (float)(Math.Round(Math.Max(0, Math.Min(1, amount)) * 100) / 100);
    }
    private string cineShown = null;

    /// <summary>
    /// The opening's words: one line along the bottom of the screen, which fades out and in when it changes; null shows none.
    /// `skipHint` is the small note in the corner on how to skip. Call with `on` false to take both away.
    /// </summary>
    public void setCaption(bool on, string text = null, string skipHint = null)
    {
        if (!on)
        {
            this.cineCaption?.Remove();
            this.cineSkip?.Remove();
            this.cineFlash?.Remove();
            this.cineCaption = this.cineText = this.cineSkip = this.cineFlash = null;
            this.cineShown = null;
            return;
        }
        if (this.cineCaption == null)
        {
            // the layout is UXML (Caption.uxml)
            var parts = Ux.LoadAll("Caption");
            this.cineCaption = Ux.Find(parts, "caption");
            this.cineText = Ux.Find(parts, "text");
            this.cineSkip = Ux.Find(parts, "skip");
            this.cineSkip.Markup = skipHint ?? "";
            this.cineCaption.MountIn(this.root.ve);
            this.cineSkip.MountIn(this.root.ve);
        }
        if (text == this.cineShown) return;
        this.cineShown = text;
        if (text == null)
        {
            this.cineCaption.RemoveClass("in");
            return;
        }
        this.cineText.Text = text;
        this.cineCaption.AddClass("in");
    }

    public void skipIntro()
    {
        this.intro.Remove();
    }

    public bool introOpen => this.intro.ve.panel != null && !this.intro.Has("gone");

    public UiNode container => this.root;

    /// <summary>Takes away the first element with this class (document.querySelector(".cls")?.remove() on the page).</summary>
    internal static void removeFirst(string cls)
    {
        MenuNav.Find(UiRoot.Root, v => v.ClassListContains(cls)).FirstOrDefault().node?.Remove();
    }
}
