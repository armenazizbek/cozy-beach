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

    private HTMLElement panel = document.createElement("div");
    private HTMLElement body = document.createElement("div");
    private HTMLElement tabs = document.createElement("div");
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
        this.panel.className = "camp hidden";
        var head = document.createElement("div");
        head.className = "camp-head";
        head.innerHTML = "<h2>Base Camp</h2>";
        var close = document.createElement<HTMLButtonElement>("button");
        close.className = "btn ghost small";
        close.textContent = "Close";
        close.onclick = () => this.hooks.onClose();
        head.append(close);
        this.tabs.className = "tabs";
        this.body.className = "camp-body";
        this.panel.append(head, this.tabs, this.body);
        parent.append(this.panel);
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
        this.panel.classList.remove("hidden");
        this.render();
    }

    public void close()
    {
        this._open = false;
        this.panel.classList.add("hidden");
    }

    private HTMLButtonElement btn(string label, Action fn, string cls = "", bool disabled = false)
    {
        var b = document.createElement<HTMLButtonElement>("button");
        b.className = $"btn {cls}";
        b.textContent = label;
        b.disabled = disabled;
        b.onclick = () =>
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
        document.activeElement?.blur();
        this.render();
        this.panel.querySelector<HTMLElement>(".tab.on")?.focus();
    }

    /// <summary>The whole page is rebuilt whenever the game state changes; this keeps the controller's place and the scroll through that.</summary>
    private void render()
    {
        var active = document.activeElement;
        var focused = active != null && this.panel.contains(active) && active != this.panel ? active : null;
        List<HTMLElement> buttons() => this.panel.querySelectorAll<HTMLElement>("button");
        var label = focused?.textContent ?? "";
        var sameLabel = focused != null ? buttons().Where(b => b.textContent == label).ToList().IndexOf(focused) : -1;
        var scroll = this.shownTab == this.tab ? this.body.scrollTop : 0;
        this.shownTab = this.tab;
        this.paint();
        this.body.scrollTop = scroll;
        if (focused != null && !this.panel.contains(focused))
        {
            var same = buttons().Where(b => b.textContent == label && !(b is HTMLButtonElement hb && hb.disabled)).ToList();
            var target = (sameLabel >= 0 && sameLabel < same.Count ? same[sameLabel] : null)
                ?? (same.Count > 0 ? same[0] : null)
                ?? this.panel.querySelector<HTMLElement>(".tab.on");
            target?.focus();
        }
    }

    private void paint()
    {
        this.tabs.innerHTML = "";
        // the tabs wrap between the two keys that turn the pages
        var tabWrap = document.createElement("div");
        tabWrap.className = "tabs-wrap";
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
            tabWrap.append(t);
        }
        this.tabs.append(Glyphs.key("Q", "LB"), tabWrap, Glyphs.key("E", "RB"));
        this.body.innerHTML = "";
        var d = this.state.data;
        var coins = document.createElement("div");
        coins.className = "coins-line";
        coins.textContent = $"🪙 {d.coins}g";
        this.body.append(coins);
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

    private HTMLElement p(string text, string cls = "")
    {
        var p = document.createElement("p");
        p.className = cls;
        p.textContent = padText(text);
        return p;
    }

    private void renderRest()
    {
        var mug = TIER_INFO["gear"].names[this.state.tier("gear") - 1];
        this.body.append(
            this.p($"Pour a warm cup from your {mug}. It restores your Warmth to full."),
            this.btn("☕ Sip from Thermos", () => this.hooks.onSip(), "big")
        );
        var lantern = document.createElement("div");
        lantern.className = "section";
        var lightLabel = LIGHT_NAMES.TryGetValue(this.state.data.lighting ?? "cycle", out var ln) ? ln : "";
        lantern.append(this.p($"Time of day: {lightLabel}"));
        if (this.state.lanternUnlocked)
        {
            lantern.append(this.btn("🏮 Switch lighting", () => this.hooks.onLighting()));
        }
        else
        {
            lantern.append(this.p("Upgrade to the Brass Espresso Flask to change the lighting.", "faint"));
        }
        this.body.append(lantern);
    }

    private void renderRecycle()
    {
        var trash = this.state.data.inventory.Where(i => i.category == "trash").ToList();
        this.body.append(this.p($"Beach Ecology Score: {this.state.data.ecology}  (grass tufts and crabs return as it grows)"));
        if (trash.Count == 0)
        {
            this.body.append(this.p("No trash in your backpack. Pull-tabs, nails and cans you clean up will go here.", "faint"));
            return;
        }
        var list = document.createElement("div");
        list.className = "list";
        foreach (var t in trash) list.append(this.row($"{t.name}", $"{t.value}g"));
        var total = trash.Sum(i => i.value);
        this.body.append(
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

    private HTMLElement row(string left, string right, HTMLElement extra = null)
    {
        var r = document.createElement("div");
        r.className = "item-row";
        var a = document.createElement("span");
        a.textContent = left;
        var b = document.createElement("span");
        b.className = "right";
        b.textContent = right;
        r.append(a, b);
        if (extra != null) r.append(extra);
        return r;
    }

    private void renderSell()
    {
        var sellable = this.state.data.inventory.Where(i => i.category == "coin" || i.category == "jewelry").ToList();
        this.body.append(this.p("Sell cleaned coins and jewelry. Collect all 3 shards of an antique and glue them together for a big payout."));
        var list = document.createElement("div");
        list.className = "list";
        if (sellable.Count == 0) list.append(this.p("Nothing to sell yet.", "faint"));
        foreach (var it in sellable)
        {
            var uid = it.uid;
            list.append(
                this.row(it.name, $"{it.value}g", this.btn("Sell", () =>
                {
                    this.state.sell(uid);
                    this.audio.coin();
                }, "small"))
            );
        }
        this.body.append(list);
        if (sellable.Count > 1)
        {
            var total = sellable.Sum(i => i.value);
            this.body.append(this.btn($"Sell all for {total}g", () =>
            {
                this.state.sellCoinsAndRings();
                this.audio.coin();
            }));
        }

        var h = document.createElement("h3");
        h.textContent = "Workbench: antique shards";
        this.body.append(h);
        foreach (var set in SHARD_SETS)
        {
            var setId = set.id;
            var setName = set.name;
            var owned = this.state.data.shards.TryGetValue(setId, out var sList) && sList != null ? sList : new List<int>();
            var done = this.state.data.assembled.Contains(setId);
            var row = document.createElement("div");
            row.className = "shard-row";
            var pips = string.Join(" ", new[] { 0, 1, 2 }.Select(i => owned.Contains(i) || done ? "◆" : "◇"));
            row.append(this.p($"{setName}   {pips}   {(done ? "Assembled" : $"{owned.Count}/3")}"));
            if (!done)
            {
                row.append(this.btn($"🔧 Assemble for {set.payout}g", () =>
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
            this.body.append(row);
        }
    }

    private void renderBoard()
    {
        this.body.append(this.p("Sticky notes from the locals. Find and clean the item and it is delivered for you, with the reward paid at once."));
        var tilts = new double[] { -5, 3.5, -2.5, 6, -4, 2 };
        for (int i = 0; i < QUEST_DEFS.Length; i++)
        {
            var q = QUEST_DEFS[i];
            var done = this.state.data.questsDone.Contains(q.id);
            var prog = this.state.questProgress(q);
            var note = document.createElement("div");
            note.className = $"note {(done ? "done" : "")}";
            var defId = q.need.defId;
            var src = defId != null && this.photos != null && this.photos.TryGetValue(defId, out var tex) ? tex : null;
            if (src != null)
            {
                // a polaroid of what is missing, pinned to the note at its own slight angle
                var photo = document.createElement("div");
                photo.className = "polaroid";
                photo.style.setProperty("--tilt", $"{JS.Str(tilts[i % 6])}deg");
                var img = document.createElement<HTMLImageElement>("img");
                img.texture = src;
                img.alt = "";
                var name = document.createElement("span");
                name.textContent = DEF_BY_ID.TryGetValue(defId ?? "", out var def) ? def.name : "";
                photo.append(img, name);
                note.append(photo);
                note.classList.add("has-photo");
            }
            note.append(
                this.p($"“{q.text}”"),
                this.p($"— {q.giver}   ·   Reward: {q.reward}g   ·   {(done ? "Already found ✓ reward paid" : $"{prog}/{q.need.count} found")}", "faint")
            );
            this.body.append(note);
        }
    }

    /// <summary>The aquarium: every kind of sea creature there is to catch alive, with the ones already in the tank ticked.</summary>
    private void renderAquarium()
    {
        var have = this.state.aquarium;
        this.body.append(this.p($"A small one of every sea creature you catch alive lives in the tank at camp. In the tank: {have.Count} of {Catches.ALL.Length}."));
        void section(string title, string how, SeaSpecies[] list)
        {
            var h = document.createElement("h3");
            h.textContent = title;
            this.body.append(h, this.p(how, "faint"));
            var box = document.createElement("div");
            box.className = "list";
            foreach (var s in list)
            {
                bool got = have.Contains(s.id);
                box.append(this.row(got ? $"✓ {s.name}" : $"? · {s.hint}", got ? "in the tank" : ""));
            }
            this.body.append(box);
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
        this.body.append(this.p($"Your album holds {photos.Count} of {Photos.MAX_PHOTOS} photos. Pinned photos hang on the board by the fire ({this.state.pinnedCount} of {Photos.BOARD_SLOTS} places taken)."));
        if (photos.Count == 0)
        {
            this.body.append(this.p("No photos yet. Pick up the camera with 3 (or the left of the d-pad), click to raise it, and click again to take a picture.", "faint"));
            return;
        }
        HTMLElement picture(PhotoInfo ph)
        {
            var tex = Photos.get(ph.id);
            if (tex == null)
            {
                var missing = document.createElement("div");
                missing.className = "photo-missing";
                missing.textContent = "This picture could not be found.";
                return missing;
            }
            var img = document.createElement<HTMLImageElement>("img");
            img.texture = tex;
            img.alt = ph.caption ?? "";
            return img;
        }
        // a copy in the player's own Pictures folder, to keep or to share
        void saveCopy(string photoId)
        {
            var folder = Photos.export(photoId);
            if (folder != null)
            {
                this.audio.coin();
                this.hooks.toast($"Saved to {folder}");
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
            var view = document.createElement("div");
            view.className = "photo-big";
            var cap = document.createElement("span");
            cap.className = "photo-cap";
            cap.textContent = big.caption ?? "";
            var bigId = big.id;
            var bigRow = document.createElement("div");
            bigRow.className = "photo-btns";
            bigRow.append(
                this.btn("Save to Pictures", () => saveCopy(bigId), "small"),
                this.btn("Close the photo", () =>
                {
                    this.audio.ui();
                    this.bigPhoto = null;
                    this.render();
                }, "small ghost"));
            view.append(picture(big), cap, bigRow);
            this.body.append(view);
        }
        var grid = document.createElement("div");
        grid.className = "photo-grid";
        bool boardFull = this.state.pinnedCount >= Photos.BOARD_SLOTS;
        foreach (var ph in Enumerable.Reverse(photos).ToList())
        {
            var id = ph.id;
            var card = document.createElement("div");
            card.className = $"photo-card {(ph.pinned ? "pinned" : "")}";
            var cap = document.createElement("span");
            cap.className = "photo-cap";
            cap.textContent = ph.caption ?? "";
            var row = document.createElement("div");
            row.className = "photo-btns";
            row.append(
                this.btn("View", () =>
                {
                    this.audio.ui();
                    this.bigPhoto = id;
                    this.confirmDrop = null;
                    this.render();
                    this.body.scrollTop = 0;
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
            card.append(picture(ph), cap, row);
            grid.append(card);
        }
        this.body.append(grid);
    }

    /// <summary>Puts the picture of what a shop cell sells at its left, with its name, words and button beside it.</summary>
    private HTMLElement pictured(HTMLElement cell, string art)
    {
        var svg = ShopArt.svg(art);
        if (svg == null) return cell;
        var text = document.createElement("div");
        text.className = "ucell-text";
        foreach (var ch in cell.children.ToList()) text.append(ch);
        var pic = document.createElement("div");
        pic.className = "ucell-pic";
        pic.innerHTML = svg;
        cell.append(pic, text);
        cell.classList.add("pictured");
        return cell;
    }

    /// <summary>For the -autoshot-shop test: scrolls the page.</summary>
    internal void smokeScroll(double y) => this.body.scrollTop = y;

    private void renderUpgrades()
    {
        var keys = new string[] { "detector", "digging", "cleaning", "gear" };
        var grid = document.createElement("div");
        grid.className = "upgrades";
        foreach (var k in keys)
        {
            var key = k;
            var info = TIER_INFO[key];
            var cur = this.state.tier(key);
            var col = document.createElement("div");
            col.className = "ucol";
            var h = document.createElement("h3");
            h.textContent = info.label;
            col.append(h);
            for (int t = 1; t <= 3; t++)
            {
                var tierIdx = t;
                var c = document.createElement("div");
                c.className = $"ucell {(t <= cur ? "owned" : "")} {(t == cur ? "current" : "")}";
                var name = document.createElement("b");
                name.textContent = info.names[t - 1];
                c.append(name, this.p(info.desc[t - 1], "faint"));
                if (t == cur + 1)
                {
                    var cost = this.state.upgradeCost(key).Value;
                    c.append(this.btn($"Buy {cost}g", () =>
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
                    c.append(this.p(t == cur ? "Equipped" : "Owned", "tag"));
                }
                col.append(this.pictured(c, $"{key}_{t}"));
            }
            grid.append(col);
        }
        this.body.append(grid);

        var fun = document.createElement("div");
        fun.className = "section";
        var fh = document.createElement("h3");
        fh.textContent = "Shop extras";
        var cell = document.createElement("div");
        cell.className = $"ucell {(this.state.data.ownsGun ? "owned" : "")}";
        var gunName = document.createElement("b");
        gunName.textContent = "Toy Pistol";
        cell.append(gunName, this.p("Purely for fun: pop at crabs, seagulls and sharks. Pays nothing. Pick it with the scroll wheel once you own it.", "faint"));
        if (this.state.data.ownsGun)
        {
            cell.append(this.p("Owned", "tag"));
        }
        else
        {
            cell.append(this.btn($"Buy {GUN_COST}g", () =>
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
        var keyCell = document.createElement("div");
        keyCell.className = $"ucell {(this.state.data.ownsCartKey ? "owned" : "")}";
        var keyName = document.createElement("b");
        keyName.textContent = "Golf Cart Key";
        keyCell.append(keyName, this.p("The golf cart will not start without it. Press F beside the cart to drive it.", "faint"));
        if (this.state.data.ownsCartKey)
        {
            keyCell.append(this.p("Owned", "tag"));
        }
        else
        {
            keyCell.append(this.btn($"Buy {CART_KEY_COST}g", () =>
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
        var boatCell = document.createElement("div");
        boatCell.className = $"ucell {(this.state.data.ownsBoatKey ? "owned" : "")}";
        var boatName = document.createElement("b");
        boatName.textContent = "Boat Key";
        boatCell.append(boatName, this.p("The boat will not start without it. Press F beside the boat, down at the shore, to take the helm.", "faint"));
        if (this.state.data.ownsBoatKey)
        {
            boatCell.append(this.p("Owned", "tag"));
        }
        else
        {
            boatCell.append(this.btn($"Buy {BOAT_KEY_COST}g", () =>
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
        var bowCell = document.createElement("div");
        bowCell.className = $"ucell {(this.state.data.ownsBow ? "owned" : "")}";
        var bowName = document.createElement("b");
        bowName.textContent = "Hunting Bow";
        bowCell.append(bowName, this.p("Hold click to draw, release to shoot. Unlimited arrows. Pick it with the scroll wheel.", "faint"));
        if (this.state.data.ownsBow)
        {
            bowCell.append(this.p("Owned", "tag"));
        }
        else
        {
            bowCell.append(this.btn($"Buy {BOW_COST}g", () =>
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
        var flagCell = document.createElement("div");
        flagCell.className = "ucell";
        var flagName = document.createElement("b");
        flagName.textContent = "Marker Flags";
        flagCell.append(
            flagName,
            this.p($"A pack of {FLAG_PACK_SIZE} flags to plant wherever you like. They show on the mini-map and the island map. Press 7 or scroll to take them out; F beside a flag picks it up again.", "faint"),
            this.p($"In your bag: {this.state.data.flags}", "tag")
        );
        flagCell.append(this.btn($"Buy {FLAG_PACK_SIZE} flags · {FLAG_PACK_COST}g", () =>
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
        var diveCell = document.createElement("div");
        diveCell.className = $"ucell {(this.state.data.ownsDiveGear ? "owned" : "")}";
        var diveName = document.createElement("b");
        diveName.textContent = "Diving Gear";
        diveCell.append(
            diveName,
            this.p("A mask, snorkel and fins, and a harpoon gun. Stand near the water and press F to dive in, and Q to come up. Down there you can find starfish, shipwreck pieces and gold, and spear fish for 1g each. Only the harpoon works underwater.", "faint")
        );
        if (this.state.data.ownsDiveGear)
        {
            diveCell.append(this.p("Owned", "tag"));
        }
        else
        {
            diveCell.append(this.btn($"Buy {DIVE_COST}g", () =>
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
            var c = document.createElement("div");
            c.className = $"ucell {(owned ? "owned" : "")}";
            var nm = document.createElement("b");
            nm.textContent = d.name;
            c.append(nm, this.p(d.blurb, "faint"));
            if (owned) c.append(this.p("Placed in your camp", "tag"));
            else
            {
                var dId = d.id;
                var dName = d.name;
                var dCost = d.cost;
                c.append(this.btn($"Buy {dCost}g", () =>
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
        var bucketCell = document.createElement("div");
        bucketCell.className = $"ucell {(this.state.data.ownsBucket ? "owned" : "")}";
        var bucketName = document.createElement("b");
        bucketName.textContent = "Bucket and Spade";
        bucketCell.append(
            bucketName,
            this.p("Build sandcastles on the beach. Click the sand to start a mound and click it again to add walls, towers and a flag. Right-click knocks a castle down. Rain and the shore wear them away. Press 8 or scroll to take it out.", "faint")
        );
        if (this.state.data.ownsBucket)
        {
            bucketCell.append(this.p("Owned", "tag"));
        }
        else
        {
            bucketCell.append(this.btn($"Buy {BUCKET_COST}g", () =>
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
        HTMLElement gearCell(string art, string title, string text, bool owned, double cost, Func<bool> buy, string bought)
        {
            var c = document.createElement("div");
            c.className = "ucell";
            var name = document.createElement("b");
            name.textContent = title;
            c.append(name, this.p(text, "faint"));
            if (owned) c.append(this.p("Owned", "tag"));
            else
            {
                c.append(this.btn($"Buy {cost}g", () =>
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
        var boneCell = document.createElement("div");
        boneCell.className = "ucell";
        var boneName = document.createElement("b");
        boneName.textContent = "Dog Bone";
        boneCell.append(
            boneName,
            this.p($"Give a bone to the camp dog (F beside it) and it runs off to sniff out a buried coin or piece of trash, then barks until you dig it up. Without a bone the dog will not go. One bone per trip. The shop only has {BONE_STOCK} bones to sell in a whole game, so use them wisely.", "faint"),
            this.p($"In your bag: {this.state.data.bones} · Left in the shop: {this.state.bonesLeft} of {BONE_STOCK}", "tag")
        );
        var soldOut = this.state.bonesLeft <= 0;
        boneCell.append(this.btn(soldOut ? "Sold out" : $"Buy a bone · {BONE_COST}g", () =>
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
        fun.append(fh, keyCell, boatCell, bowCell, flagCell, bucketCell, rodCell, diveCell, netCell);
        foreach (var dc in decorCells) fun.append(dc);
        fun.append(boneCell, cell);
        this.body.append(fun);
    }

    /// <summary>Only there once the gold has been found.</summary>
    private void renderLuxury()
    {
        var grid = document.createElement("div");
        grid.className = "section";
        var head = document.createElement("h4");
        head.textContent = "Things the Marigold\u2019s gold can buy";
        grid.append(head);
        foreach (var l in LUXURY)
        {
            var lId = l.id;
            var lName = l.name;
            var lCost = l.cost;
            var owned = this.state.data.luxury.Contains(lId);
            var cell = document.createElement("div");
            cell.className = $"ucell {(owned ? "owned" : "")}";
            var name = document.createElement("b");
            name.textContent = lName;
            cell.append(name, this.p(l.desc));
            if (owned) cell.append(this.p("Yours", "tag"));
            else
            {
                cell.append(this.btn($"Buy · {lCost}g", () =>
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
            grid.append(this.pictured(cell, lId));
        }
        this.body.append(grid);
    }

    private void renderShelf()
    {
        var d = this.state.data;
        this.body.append(this.p("Assembled antiques sit on the shelf at your workbench."));
        foreach (var set in SHARD_SETS)
        {
            if (d.assembled.Contains(set.id))
            {
                var n = document.createElement("div");
                n.className = "note";
                n.append(this.p($"🏺 {set.name}"), this.p(set.lore, "faint"));
                this.body.append(n);
            }
        }
        if (d.assembled.Count == 0) this.body.append(this.p("Empty so far.", "faint"));
        var h = document.createElement("h3");
        h.textContent = $"Discoveries: {d.found.Count} / {ITEM_DEFS.Length} kinds";
        this.body.append(h);
        var list = document.createElement("div");
        list.className = "list";
        foreach (var kv in d.found)
        {
            if (DEF_BY_ID.TryGetValue(kv.Key, out var def) && def != null)
            {
                list.append(this.row(def.name, $"×{kv.Value}"));
            }
        }
        this.body.append(list);
        var inv = d.inventory.Where(i => i.category != "trash").ToList();
        if (inv.Count > 0)
        {
            var hh = document.createElement("h3");
            hh.textContent = "In your backpack";
            this.body.append(hh);
            var l2 = document.createElement("div");
            l2.className = "list";
            foreach (var i in inv) l2.append(this.row(i.name, $"{i.value}g"));
            this.body.append(l2);
        }
    }
}
