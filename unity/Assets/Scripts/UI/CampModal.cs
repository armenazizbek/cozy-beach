using System;
using System.Collections.Generic;
using System.Linq;
using static Items;
using static PadText;
using static StateManager;

// Port of src/ui/CampModal.ts

public class CampHooks
{
    public Action onSip;
    public Action onClose;
    public Action onLighting;
    public Action<int> onRecycled;
    public Action<string> onAssembled;
    public Action onLuxury;
    public Action<string> toast;
}

public class CampModal
{
    private static readonly string[][] TABS = new string[][]
    {
        new[] { "rest", "☕ Rest" },
        new[] { "recycle", "♻️ Recycle" },
        new[] { "sell", "🪙 Sell & Assemble" },
        new[] { "board", "📌 Lost & Found" },
        new[] { "upgrades", "🧰 Upgrades" },
        new[] { "luxury", "👑 Luxury" },
        new[] { "shelf", "🏺 Trophy Shelf" },
        new[] { "photos", "📷 Photos" },
        new[] { "aquarium", "🐟 Aquarium" },
    };

    private static readonly Dictionary<string, string> LIGHT_NAMES = new Dictionary<string, string>
    {
        ["cycle"] = "Day & night cycle",
        ["golden"] = "Golden Morning",
        ["day"] = "Bright Day",
        ["rain"] = "Rainy Day",
        ["sunset"] = "Sunset",
        ["night"] = "Cozy Night",
    };

    // native (UI/Native/UiNode.cs): the same elements and classes the page had
    private UiNode panel = new UiNode("div");
    private UiNode body = new UiNode("div");
    private UiNode tabs = new UiNode("div");
    private string tab = "rest";
    private bool _open = false;
    private string shownTab = null;
    /// <summary>the photo shown large at the top of the Photos page, or null</summary>
    private string bigPhoto = null;
    /// <summary>the photo whose "Throw away" was pressed once and now asks to be pressed again</summary>
    private string confirmDrop = null;
    /// <summary>a picture of each Lost & Found item, taken while the game loads</summary>
    public Dictionary<string, UnityEngine.Texture> photos = new Dictionary<string, UnityEngine.Texture>();

    private StateManager state;
    private AudioManager audio;
    private CampHooks hooks;

    public CampModal(
        HTMLElement parent,
        StateManager state,
        AudioManager audio,
        CampHooks hooks)
    {
        this.state = state;
        this.audio = audio;
        this.hooks = hooks;
        this.panel.ClassName = "camp hidden";
        var head = new UiNode("div");
        head.ClassName = "camp-head";
        head.Add(new UiNode("h2", null, "Base Camp"));
        var close = new UiNode("button");
        close.ClassName = "btn ghost small";
        close.Text = "Close";
        close.OnClick = () => this.hooks.onClose();
        head.Add(close);
        this.tabs.ClassName = "tabs";
        this.body.ClassName = "camp-body";
        this.panel.Add(head, this.tabs, this.body);
        this.panel.MountIn(parent.ve);
        state.subscribe(() =>
        {
            if (this._open) this.render();
        });
    }

    public bool isOpen => this._open;

    public void open(string tab = "rest")
    {
        this.tab = tab;
        this._open = true;
        this.panel.RemoveClass("hidden");
        this.render();
    }

    public void close()
    {
        this._open = false;
        this.panel.AddClass("hidden");
    }

    private UiNode btn(string label, Action fn, string cls = "", bool disabled = false)
    {
        var b = new UiNode("button");
        b.ClassName = $"btn {cls}";
        b.Text = label;
        b.Disabled = disabled;
        b.OnClick = () =>
        {
            fn();
        };
        return b;
    }

    /// <summary>Moves to the next page along (a controller's bumpers).</summary>
    public void stepTab(int dir)
    {
        var ids = TABS.Select(t => t[0]).Where(id => id != "luxury" || this.state.data.hoardTaken).ToList();
        this.tab = ids[(ids.IndexOf(this.tab) + dir + ids.Count) % ids.Count];
        this.audio.ui();
        var was = MenuNav.Current;
        if (was.dom != null) was.dom.blur();
        else was.node?.Blur();
        this.render();
        tabOn()?.Focus();
    }

    /// <summary>The whole page is rebuilt whenever the game state changes; this keeps the controller's place and the scroll through that.</summary>
    private void render()
    {
        var active = MenuNav.Current.node;
        var focused = active != null && this.panel.Contains(active) && active != this.panel ? active : null;
        List<UiNode> buttons() => MenuNav.Find(this.panel.ve, v => v.ClassListContains("t-button")).Select(e => e.node).Where(n => n != null).ToList();
        var label = focused?.FullText ?? "";
        var sameLabel = focused != null ? buttons().Where(b => b.FullText == label).ToList().IndexOf(focused) : -1;
        var scroll = this.shownTab == this.tab ? this.body.ScrollTop : 0;
        this.shownTab = this.tab;
        this.paint();
        this.body.ScrollTop = scroll;
        if (focused != null && !this.panel.Contains(focused))
        {
            var same = buttons().Where(b => b.FullText == label && !b.Disabled).ToList();
            var target = (sameLabel >= 0 && sameLabel < same.Count ? same[sameLabel] : null)
                ?? (same.Count > 0 ? same[0] : null)
                ?? tabOn();
            target?.Focus();
        }
    }

    /// <summary>The tab of the page that is open (.tab.on).</summary>
    private UiNode tabOn() => MenuNav.Find(this.panel.ve, v => v.ClassListContains("tab") && v.ClassListContains("on")).Select(e => e.node).FirstOrDefault();

    private void paint()
    {
        this.tabs.Clear();
        // the tabs wrap between the two keys that turn the pages
        var tabWrap = new UiNode("div");
        tabWrap.ClassName = "tabs-wrap";
        foreach (var entry in TABS)
        {
            string id = entry[0], label = entry[1];
            if (id == "luxury" && !this.state.data.hoardTaken) continue;
            var t = this.btn(label, () =>
            {
                this.audio.ui();
                this.tab = id;
                this.render();
            }, $"tab {(id == this.tab ? "on" : "")}");
            tabWrap.Add(t);
        }
        this.tabs.Add(Glyphs.KeyNode("Q", "LB"), tabWrap, Glyphs.KeyNode("E", "RB"));
        this.body.Clear();
        var d = this.state.data;
        var coins = new UiNode("div");
        coins.ClassName = "coins-line";
        coins.Text = $"🪙 {d.coins}g";
        this.body.Add(coins);
        switch (this.tab)
        {
            case "rest": this.renderRest(); break;
            case "recycle": this.renderRecycle(); break;
            case "sell": this.renderSell(); break;
            case "board": this.renderBoard(); break;
            case "upgrades": this.renderUpgrades(); break;
            case "luxury": this.renderLuxury(); break;
            case "shelf": this.renderShelf(); break;
            case "photos": this.renderPhotos(); break;
            case "aquarium": this.renderAquarium(); break;
        }
    }

    private UiNode p(string text, string cls = "")
    {
        var p = new UiNode("p");
        p.ClassName = cls;
        p.Text = padText(text);
        return p;
    }

    private void renderRest()
    {
        var mug = TIER_INFO["gear"].names[this.state.tier("gear") - 1];
        this.body.Add(
            this.p($"Pour a warm cup from your {mug}. It restores your Warmth to full."),
            this.btn("☕ Sip from Thermos", () => this.hooks.onSip(), "big")
        );
        var lantern = new UiNode("div");
        lantern.ClassName = "section";
        var lightLabel = LIGHT_NAMES.TryGetValue(this.state.data.lighting ?? "cycle", out var ln) ? ln : "";
        lantern.Add(this.p($"Time of day: {lightLabel}"));
        if (this.state.lanternUnlocked)
        {
            lantern.Add(this.btn("🏮 Switch lighting", () => this.hooks.onLighting()));
        }
        else
        {
            lantern.Add(this.p("Upgrade to the Brass Espresso Flask to change the lighting.", "faint"));
        }
        this.body.Add(lantern);
    }

    private void renderRecycle()
    {
        var trash = this.state.data.inventory.Where(i => i.category == "trash").ToList();
        this.body.Add(this.p($"Beach Ecology Score: {this.state.data.ecology}  (grass tufts and crabs return as it grows)"));
        if (trash.Count == 0)
        {
            this.body.Add(this.p("No trash in your backpack. Pull-tabs, nails and cans you clean up will go here.", "faint"));
            return;
        }
        var list = new UiNode("div");
        list.ClassName = "list";
        foreach (var t in trash) list.Add(this.row($"{t.name}", $"{t.value}g"));
        var total = trash.Sum(i => i.value);
        this.body.Add(
            list,
            this.btn($"♻️ Recycle all ({trash.Count}) for {total}g", () =>
            {
                var r = this.state.recycleAll();
                this.audio.coin();
                this.hooks.onRecycled(r.pieces);
                this.hooks.toast($"Recycled {r.pieces} pieces for {r.coins}g. The beach thanks you!");
            }, "big")
        );
    }

    private UiNode row(string left, string right, UiNode extra = null)
    {
        var r = new UiNode("div");
        r.ClassName = "item-row";
        var a = new UiNode("span");
        a.Text = left;
        var b = new UiNode("span");
        b.ClassName = "right";
        b.Text = right;
        r.Add(a, b);
        if (extra != null) r.Add(extra);
        return r;
    }

    private void renderSell()
    {
        var sellable = this.state.data.inventory.Where(i => i.category == "coin" || i.category == "jewelry").ToList();
        this.body.Add(this.p("Sell cleaned coins and jewelry. Collect all 3 shards of an antique and glue them together for a big payout."));
        var list = new UiNode("div");
        list.ClassName = "list";
        if (sellable.Count == 0) list.Add(this.p("Nothing to sell yet.", "faint"));
        foreach (var it in sellable)
        {
            var uid = it.uid;
            list.Add(
                this.row(it.name, $"{it.value}g", this.btn("Sell", () =>
                {
                    this.state.sell(uid);
                    this.audio.coin();
                }, "small"))
            );
        }
        this.body.Add(list);
        if (sellable.Count > 1)
        {
            var total = sellable.Sum(i => i.value);
            this.body.Add(this.btn($"Sell all for {total}g", () =>
            {
                this.state.sellCoinsAndRings();
                this.audio.coin();
            }));
        }

        var h = new UiNode("h3");
        h.Text = "Workbench: antique shards";
        this.body.Add(h);
        foreach (var set in SHARD_SETS)
        {
            var setId = set.id;
            var setName = set.name;
            var owned = this.state.data.shards.TryGetValue(setId, out var sList) && sList != null ? sList : new List<int>();
            var done = this.state.data.assembled.Contains(setId);
            var row = new UiNode("div");
            row.ClassName = "shard-row";
            var pips = string.Join(" ", new[] { 0, 1, 2 }.Select(i => owned.Contains(i) || done ? "◆" : "◇"));
            row.Add(this.p($"{setName}   {pips}   {(done ? "Assembled" : $"{owned.Count}/3")}"));
            if (!done)
            {
                row.Add(this.btn($"🔧 Assemble for {set.payout}g", () =>
                {
                    var pay = this.state.assemble(setId);
                    if (pay > 0)
                    {
                        this.audio.discover();
                        this.hooks.onAssembled(setId);
                        this.hooks.toast($"The {setName} is whole again! +{JS.Str(pay)}g");
                    }
                }, "small", !this.state.canAssemble(setId)));
            }
            this.body.Add(row);
        }
    }

    private void renderBoard()
    {
        this.body.Add(this.p("Sticky notes from the locals. Find and clean the item and it is delivered for you, with the reward paid at once."));
        var tilts = new double[] { -5, 3.5, -2.5, 6, -4, 2 };
        for (int i = 0; i < QUEST_DEFS.Length; i++)
        {
            var q = QUEST_DEFS[i];
            var done = this.state.data.questsDone.Contains(q.id);
            var prog = this.state.questProgress(q);
            var note = new UiNode("div");
            note.ClassName = $"note {(done ? "done" : "")}";
            var defId = q.need.defId;
            var src = defId != null && this.photos != null && this.photos.TryGetValue(defId, out var tex) ? tex : null;
            if (src != null)
            {
                // a polaroid of what is missing, pinned to the note at its own slight angle
                var photo = new UiNode("div");
                photo.ClassName = "polaroid";
                photo.ve.style.rotate = new UnityEngine.UIElements.StyleRotate(new UnityEngine.UIElements.Rotate(new UnityEngine.UIElements.Angle((float)tilts[i % 6], UnityEngine.UIElements.AngleUnit.Degree)));
                var img = new UiNode("img").Image(src);
                var name = new UiNode("span");
                name.Text = DEF_BY_ID.TryGetValue(defId ?? "", out var def) ? def.name : "";
                photo.Add(img, name);
                note.Add(photo);
                note.AddClass("has-photo");
            }
            note.Add(
                this.p($"“{q.text}”"),
                this.p($"— {q.giver}   ·   Reward: {q.reward}g   ·   {(done ? "Already found ✓ reward paid" : $"{prog}/{q.need.count} found")}", "faint")
            );
            this.body.Add(note);
        }
    }

    /// <summary>The aquarium: every kind of sea creature there is to catch alive, with the ones already in the tank ticked.</summary>
    private void renderAquarium()
    {
        var have = this.state.aquarium;
        this.body.Add(this.p($"A small one of every sea creature you catch alive lives in the tank at camp. In the tank: {have.Count} of {Catches.ALL.Length}."));
        void section(string title, string how, SeaSpecies[] list)
        {
            var h = new UiNode("h3");
            h.Text = title;
            this.body.Add(h, this.p(how, "faint"));
            var box = new UiNode("div");
            box.ClassName = "list";
            foreach (var s in list)
            {
                bool got = have.Contains(s.id);
                box.Add(this.row(got ? $"✓ {s.name}" : $"? · {s.hint}", got ? "in the tank" : ""));
            }
            this.body.Add(box);
        }
        section("From the pier", this.state.data.ownsRod ? "Caught with the fishing rod, from the bench at the end of the pier." : "Caught with a fishing rod, from the bench at the end of the pier. The camper shop sells one.", Catches.ROD);
        section("From the reef", this.state.data.ownsNet ? "Caught with the hand net while diving." : "Caught with a hand net while diving. The camper shop sells one.", Catches.REEF);
        section("The rarest", "One golden fish in the whole sea. Very fast: chase it in bursts and corner it.", new[] { Catches.GOLD });
        section("From the sea floor", "Also caught with the hand net while diving.", Catches.ANIMALS);
    }

    /// <summary>The camera's album: every photo, newest first, to look at, pin to the board by the fire, or throw away.</summary>
    private void renderPhotos()
    {
        var photos = this.state.photos;
        this.body.Add(this.p($"Your album holds {photos.Count} of {Photos.MAX_PHOTOS} photos. Pinned photos hang on the board by the fire ({this.state.pinnedCount} of {Photos.BOARD_SLOTS} places taken)."));
        if (photos.Count == 0)
        {
            this.body.Add(this.p("No photos yet. Pick up the camera with 3 (or the left of the d-pad), click to raise it, and click again to take a picture.", "faint"));
            return;
        }
        UiNode picture(PhotoInfo ph)
        {
            var tex = Photos.get(ph.id);
            if (tex == null)
            {
                var missing = new UiNode("div");
                missing.ClassName = "photo-missing";
                missing.Text = "This picture could not be found.";
                return missing;
            }
            return new UiNode("img").Image(tex);
        }
        // a copy in the player's own Pictures folder, to keep or to share
        void saveCopy(string photoId)
        {
            var folder = Photos.export(photoId);
            if (folder != null)
            {
                this.audio.coin();
                this.hooks.toast(Photos.lastToSteam ? "Saved to your Steam screenshots" : $"Saved to {folder}");
            }
            else
            {
                this.audio.error();
                this.hooks.toast("Could not save the photo to your Pictures folder.");
            }
        }
        var big = this.bigPhoto != null ? photos.FirstOrDefault(x => x.id == this.bigPhoto) : null;
        if (big != null)
        {
            var view = new UiNode("div");
            view.ClassName = "photo-big";
            var cap = new UiNode("span");
            cap.ClassName = "photo-cap";
            cap.Text = big.caption ?? "";
            var bigId = big.id;
            var bigRow = new UiNode("div");
            bigRow.ClassName = "photo-btns";
            bigRow.Add(
                this.btn("Save to Pictures", () => saveCopy(bigId), "small"),
                this.btn("Close the photo", () =>
                {
                    this.audio.ui();
                    this.bigPhoto = null;
                    this.render();
                }, "small ghost"));
            view.Add(picture(big), cap, bigRow);
            this.body.Add(view);
        }
        var grid = new UiNode("div");
        grid.ClassName = "photo-grid";
        bool boardFull = this.state.pinnedCount >= Photos.BOARD_SLOTS;
        foreach (var ph in Enumerable.Reverse(photos).ToList())
        {
            var id = ph.id;
            var card = new UiNode("div");
            card.ClassName = $"photo-card {(ph.pinned ? "pinned" : "")}";
            var cap = new UiNode("span");
            cap.ClassName = "photo-cap";
            cap.Text = ph.caption ?? "";
            var row = new UiNode("div");
            row.ClassName = "photo-btns";
            row.Add(
                this.btn("View", () =>
                {
                    this.audio.ui();
                    this.bigPhoto = id;
                    this.confirmDrop = null;
                    this.render();
                    this.body.ScrollTop = 0;
                }, "small"),
                this.btn(ph.pinned ? "📌 Unpin" : "📌 Pin", () =>
                {
                    this.confirmDrop = null;
                    if (this.state.togglePin(id)) this.audio.ui();
                    else
                    {
                        this.audio.error();
                        this.hooks.toast($"The board is full. Unpin a photo first ({Photos.BOARD_SLOTS} fit).");
                    }
                }, "small", !ph.pinned && boardFull),
                this.btn("Save to Pictures", () => saveCopy(id), "small"),
                this.btn(this.confirmDrop == id ? "Sure? Throw away" : "🗑️ Throw away", () =>
                {
                    if (this.confirmDrop != id)
                    {
                        this.audio.ui();
                        this.confirmDrop = id;
                        this.render();
                        return;
                    }
                    this.confirmDrop = null;
                    if (this.bigPhoto == id) this.bigPhoto = null;
                    this.audio.crunch();
                    this.state.removePhoto(id);
                    Photos.discard(id);
                }, "small ghost")
            );
            card.Add(picture(ph), cap, row);
            grid.Add(card);
        }
        this.body.Add(grid);
    }

    /// <summary>Puts the picture of what a shop cell sells at its left, with its name, words and button beside it.</summary>
    private UiNode pictured(UiNode cell, string art)
    {
        var svg = ShopArt.svg(art);
        if (svg == null) return cell;
        var text = new UiNode("div");
        text.ClassName = "ucell-text";
        foreach (var ch in cell.Children.ToList()) text.Add(ch);
        var pic = new UiNode("div");
        pic.ClassName = "ucell-pic";
        pic.Svg(svg);
        cell.Add(pic, text);
        cell.AddClass("pictured");
        return cell;
    }

    /// <summary>For the -autoshot-shop test: scrolls the page.</summary>
    internal void smokeScroll(double y) => this.body.ScrollTop = y;

    private void renderUpgrades()
    {
        var keys = new string[] { "detector", "digging", "cleaning", "gear" };
        var grid = new UiNode("div");
        grid.ClassName = "upgrades";
        foreach (var k in keys)
        {
            var key = k;
            var info = TIER_INFO[key];
            var cur = this.state.tier(key);
            var col = new UiNode("div");
            col.ClassName = "ucol";
            var h = new UiNode("h3");
            h.Text = info.label;
            col.Add(h);
            for (int t = 1; t <= 3; t++)
            {
                var tierIdx = t;
                var c = new UiNode("div");
                c.ClassName = $"ucell {(t <= cur ? "owned" : "")} {(t == cur ? "current" : "")}";
                var name = new UiNode("b");
                name.Text = info.names[t - 1];
                c.Add(name, this.p(info.desc[t - 1], "faint"));
                if (t == cur + 1)
                {
                    var cost = this.state.upgradeCost(key).Value;
                    c.Add(this.btn($"Buy {cost}g", () =>
                    {
                        if (this.state.buyUpgrade(key))
                        {
                            this.audio.discover();
                            this.hooks.toast($"Upgraded: {info.names[tierIdx - 1]}");
                        }
                        else
                        {
                            this.audio.error();
                            this.hooks.toast($"You need {cost}g for that.");
                        }
                    }, "small", this.state.data.coins < cost));
                }
                else if (t <= cur)
                {
                    c.Add(this.p(t == cur ? "Equipped" : "Owned", "tag"));
                }
                col.Add(this.pictured(c, $"{key}_{t}"));
            }
            grid.Add(col);
        }
        this.body.Add(grid);

        var fun = new UiNode("div");
        fun.ClassName = "section";
        var fh = new UiNode("h3");
        fh.Text = "Shop extras";
        var cell = new UiNode("div");
        cell.ClassName = $"ucell {(this.state.data.ownsGun ? "owned" : "")}";
        var gunName = new UiNode("b");
        gunName.Text = "Toy Pistol";
        cell.Add(gunName, this.p("Purely for fun: pop at crabs, seagulls and sharks. Pays nothing. Pick it with the scroll wheel once you own it.", "faint"));
        if (this.state.data.ownsGun)
        {
            cell.Add(this.p("Owned", "tag"));
        }
        else
        {
            cell.Add(this.btn($"Buy {GUN_COST}g", () =>
            {
                if (this.state.buyGun())
                {
                    this.audio.discover();
                    this.hooks.toast("You bought the Toy Pistol! Scroll to pick it up.");
                }
                else
                {
                    this.audio.error();
                    this.hooks.toast($"You need {GUN_COST}g for that.");
                }
            }, "small", this.state.data.coins < GUN_COST));
        }
        var keyCell = new UiNode("div");
        keyCell.ClassName = $"ucell {(this.state.data.ownsCartKey ? "owned" : "")}";
        var keyName = new UiNode("b");
        keyName.Text = "Golf Cart Key";
        keyCell.Add(keyName, this.p("The golf cart will not start without it. Press F beside the cart to drive it.", "faint"));
        if (this.state.data.ownsCartKey)
        {
            keyCell.Add(this.p("Owned", "tag"));
        }
        else
        {
            keyCell.Add(this.btn($"Buy {CART_KEY_COST}g", () =>
            {
                if (this.state.buyCartKey())
                {
                    this.audio.discover();
                    this.hooks.toast("You bought the golf cart key! Press F beside the cart.");
                }
                else
                {
                    this.audio.error();
                    this.hooks.toast($"You need {CART_KEY_COST}g for that.");
                }
            }, "small", this.state.data.coins < CART_KEY_COST));
        }
        var boatCell = new UiNode("div");
        boatCell.ClassName = $"ucell {(this.state.data.ownsBoatKey ? "owned" : "")}";
        var boatName = new UiNode("b");
        boatName.Text = "Boat Key";
        boatCell.Add(boatName, this.p("The boat will not start without it. Press F beside the boat, down at the shore, to take the helm.", "faint"));
        if (this.state.data.ownsBoatKey)
        {
            boatCell.Add(this.p("Owned", "tag"));
        }
        else
        {
            boatCell.Add(this.btn($"Buy {BOAT_KEY_COST}g", () =>
            {
                if (this.state.buyBoatKey())
                {
                    this.audio.discover();
                    this.hooks.toast("You bought the boat key! Head down to the shore.");
                }
                else
                {
                    this.audio.error();
                    this.hooks.toast($"You need {BOAT_KEY_COST}g for that.");
                }
            }, "small", this.state.data.coins < BOAT_KEY_COST));
        }
        var bowCell = new UiNode("div");
        bowCell.ClassName = $"ucell {(this.state.data.ownsBow ? "owned" : "")}";
        var bowName = new UiNode("b");
        bowName.Text = "Hunting Bow";
        bowCell.Add(bowName, this.p("Hold click to draw, release to shoot. Unlimited arrows. Pick it with the scroll wheel.", "faint"));
        if (this.state.data.ownsBow)
        {
            bowCell.Add(this.p("Owned", "tag"));
        }
        else
        {
            bowCell.Add(this.btn($"Buy {BOW_COST}g", () =>
            {
                if (this.state.buyBow())
                {
                    this.audio.discover();
                    this.hooks.toast("You bought the bow! Scroll to pick it up.");
                }
                else
                {
                    this.audio.error();
                    this.hooks.toast($"You need {BOW_COST}g for that.");
                }
            }, "small", this.state.data.coins < BOW_COST));
        }
        var flagCell = new UiNode("div");
        flagCell.ClassName = "ucell";
        var flagName = new UiNode("b");
        flagName.Text = "Marker Flags";
        flagCell.Add(
            flagName,
            this.p($"A pack of {FLAG_PACK_SIZE} flags to plant wherever you like. They show on the mini-map and the island map. Press 7 or scroll to take them out; F beside a flag picks it up again.", "faint"),
            this.p($"In your bag: {this.state.data.flags}", "tag")
        );
        flagCell.Add(this.btn($"Buy {FLAG_PACK_SIZE} flags · {FLAG_PACK_COST}g", () =>
        {
            if (this.state.buyFlags())
            {
                this.audio.discover();
                this.hooks.toast($"You bought {FLAG_PACK_SIZE} flags! Press 7 to take them out.");
            }
            else
            {
                this.audio.error();
                this.hooks.toast($"You need {FLAG_PACK_COST}g for that.");
            }
        }, "small", this.state.data.coins < FLAG_PACK_COST));
        var diveCell = new UiNode("div");
        diveCell.ClassName = $"ucell {(this.state.data.ownsDiveGear ? "owned" : "")}";
        var diveName = new UiNode("b");
        diveName.Text = "Diving Gear";
        diveCell.Add(
            diveName,
            this.p("A mask, snorkel and fins, and a harpoon gun. Stand near the water and press F to dive in, and Q to come up. Down there you can find starfish, shipwreck pieces and gold, and spear fish for 1g each. Only the harpoon works underwater.", "faint")
        );
        if (this.state.data.ownsDiveGear)
        {
            diveCell.Add(this.p("Owned", "tag"));
        }
        else
        {
            diveCell.Add(this.btn($"Buy {DIVE_COST}g", () =>
            {
                if (this.state.buyDiveGear())
                {
                    this.audio.discover();
                    this.hooks.toast("You bought the diving gear! Wade to the water\u2019s edge and press F to dive in.");
                }
                else
                {
                    this.audio.error();
                    this.hooks.toast($"You need {DIVE_COST}g for that.");
                }
            }, "small", this.state.data.coins < DIVE_COST));
        }
        var decorCells = DECOR.Select(d =>
        {
            var owned = (this.state.data.decor ?? new List<string>()).Contains(d.id);
            var c = new UiNode("div");
            c.ClassName = $"ucell {(owned ? "owned" : "")}";
            var nm = new UiNode("b");
            nm.Text = d.name;
            c.Add(nm, this.p(d.blurb, "faint"));
            if (owned) c.Add(this.p("Placed in your camp", "tag"));
            else
            {
                var dId = d.id;
                var dName = d.name;
                var dCost = d.cost;
                c.Add(this.btn($"Buy {dCost}g", () =>
                {
                    if (this.state.buyDecor(dId))
                    {
                        this.audio.discover();
                        this.hooks.toast($"{dName} is up in your camp.");
                    }
                    else
                    {
                        this.audio.error();
                        this.hooks.toast($"You need {dCost}g for that.");
                    }
                }, "small", this.state.data.coins < dCost));
            }
            return this.pictured(c, $"decor_{d.id}");
        }).ToList();
        var bucketCell = new UiNode("div");
        bucketCell.ClassName = $"ucell {(this.state.data.ownsBucket ? "owned" : "")}";
        var bucketName = new UiNode("b");
        bucketName.Text = "Bucket and Spade";
        bucketCell.Add(
            bucketName,
            this.p("Build sandcastles on the beach. Click the sand to start a mound and click it again to add walls, towers and a flag. Right-click knocks a castle down. Rain and the shore wear them away. Press 8 or scroll to take it out.", "faint")
        );
        if (this.state.data.ownsBucket)
        {
            bucketCell.Add(this.p("Owned", "tag"));
        }
        else
        {
            bucketCell.Add(this.btn($"Buy {BUCKET_COST}g", () =>
            {
                if (this.state.buyBucket())
                {
                    this.audio.discover();
                    this.hooks.toast("You bought the bucket! Press 8 to take it out and build.");
                }
                else
                {
                    this.audio.error();
                    this.hooks.toast($"You need {BUCKET_COST}g for that.");
                }
            }, "small", this.state.data.coins < BUCKET_COST));
        }
        // the fishing rod and the diver's net: both bought once
        UiNode gearCell(string art, string title, string text, bool owned, double cost, Func<bool> buy, string bought)
        {
            var c = new UiNode("div");
            c.ClassName = "ucell";
            var name = new UiNode("b");
            name.Text = title;
            c.Add(name, this.p(text, "faint"));
            if (owned) c.Add(this.p("Owned", "tag"));
            else
            {
                c.Add(this.btn($"Buy {cost}g", () =>
                {
                    if (buy())
                    {
                        this.audio.discover();
                        this.hooks.toast(bought);
                    }
                    else
                    {
                        this.audio.error();
                        this.hooks.toast($"You need {cost}g for that.");
                    }
                }, "small", this.state.data.coins < cost));
            }
            return this.pictured(c, art);
        }
        var rodCell = gearCell("rod", "Fishing Rod", "Sit on the bench at the end of the pier and fish. Cast, wait for the float to go under, then reel it in. Every new kind of fish gets a place in your aquarium.",
            this.state.data.ownsRod, Catches.ROD_COST, () => this.state.buyRod(), "You bought the fishing rod! The bench is at the end of the pier.");
        var netCell = gearCell("net", "Hand Net", "Catches fish and other sea creatures alive while you dive, for the aquarium. Underwater, press 2 to take it out. Needs the diving gear.",
            this.state.data.ownsNet, Catches.NET_COST, () => this.state.buyNet(), "You bought the hand net! Take it out underwater with 2.");
        var boneCell = new UiNode("div");
        boneCell.ClassName = "ucell";
        var boneName = new UiNode("b");
        boneName.Text = "Dog Bone";
        boneCell.Add(
            boneName,
            this.p($"Give a bone to the camp dog (F beside it) and it runs off to sniff out a buried coin or piece of trash, then barks until you dig it up. Without a bone the dog will not go. One bone per trip. The shop only has {BONE_STOCK} bones to sell in a whole game, so use them wisely.", "faint"),
            this.p($"In your bag: {this.state.data.bones} · Left in the shop: {this.state.bonesLeft} of {BONE_STOCK}", "tag")
        );
        var soldOut = this.state.bonesLeft <= 0;
        boneCell.Add(this.btn(soldOut ? "Sold out" : $"Buy a bone · {BONE_COST}g", () =>
        {
            if (this.state.buyBone())
            {
                this.audio.discover();
                this.hooks.toast("You bought a bone! Give it to the dog at camp.");
            }
            else
            {
                this.audio.error();
                this.hooks.toast($"You need {BONE_COST}g for that.");
            }
        }, "small", soldOut || this.state.data.coins < BONE_COST));
        this.pictured(keyCell, "cart_key");
        this.pictured(boatCell, "boat_key");
        this.pictured(bowCell, "bow");
        this.pictured(flagCell, "flags");
        this.pictured(bucketCell, "bucket");
        this.pictured(diveCell, "dive");
        this.pictured(boneCell, "bone");
        this.pictured(cell, "gun");
        fun.Add(fh, keyCell, boatCell, bowCell, flagCell, bucketCell, rodCell, diveCell, netCell);
        foreach (var dc in decorCells) fun.Add(dc);
        fun.Add(boneCell, cell);
        this.body.Add(fun);
    }

    /// <summary>Only there once the gold has been found.</summary>
    private void renderLuxury()
    {
        var grid = new UiNode("div");
        grid.ClassName = "section";
        var head = new UiNode("h4");
        head.Text = "Things the Marigold\u2019s gold can buy";
        grid.Add(head);
        foreach (var l in LUXURY)
        {
            var lId = l.id;
            var lName = l.name;
            var lCost = l.cost;
            var owned = this.state.data.luxury.Contains(lId);
            var cell = new UiNode("div");
            cell.ClassName = $"ucell {(owned ? "owned" : "")}";
            var name = new UiNode("b");
            name.Text = lName;
            cell.Add(name, this.p(l.desc));
            if (owned) cell.Add(this.p("Yours", "tag"));
            else
            {
                cell.Add(this.btn($"Buy · {lCost}g", () =>
                {
                    if (this.state.buyLuxury(lId))
                    {
                        this.audio.discover();
                        this.hooks.onLuxury();
                        this.hooks.toast($"{lName} is yours.");
                    }
                    else this.audio.error();
                    this.render();
                }, "small", this.state.data.coins < lCost));
            }
            grid.Add(this.pictured(cell, lId));
        }
        this.body.Add(grid);
    }

    private void renderShelf()
    {
        var d = this.state.data;
        this.body.Add(this.p("Assembled antiques sit on the shelf at your workbench."));
        foreach (var set in SHARD_SETS)
        {
            if (d.assembled.Contains(set.id))
            {
                var n = new UiNode("div");
                n.ClassName = "note";
                n.Add(this.p($"🏺 {set.name}"), this.p(set.lore, "faint"));
                this.body.Add(n);
            }
        }
        if (d.assembled.Count == 0) this.body.Add(this.p("Empty so far.", "faint"));
        var h = new UiNode("h3");
        h.Text = $"Discoveries: {d.found.Count} / {ITEM_DEFS.Length} kinds";
        this.body.Add(h);
        var list = new UiNode("div");
        list.ClassName = "list";
        foreach (var kv in d.found)
        {
            if (DEF_BY_ID.TryGetValue(kv.Key, out var def) && def != null)
            {
                list.Add(this.row(def.name, $"×{kv.Value}"));
            }
        }
        this.body.Add(list);
        var inv = d.inventory.Where(i => i.category != "trash").ToList();
        if (inv.Count > 0)
        {
            var hh = new UiNode("h3");
            hh.Text = "In your backpack";
            this.body.Add(hh);
            var l2 = new UiNode("div");
            l2.ClassName = "list";
            foreach (var i in inv) l2.Add(this.row(i.name, $"{i.value}g"));
            this.body.Add(l2);
        }
    }
}
