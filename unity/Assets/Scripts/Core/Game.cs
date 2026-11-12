using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using static Achievements;
using static Beach;
using static ItemPhotos;
using static Items;
using static Journal;
using static Mail;
using static MapView;
using static Rain;
using static Sandcastles;
using static SaveGame;
using static SeaCave;
using static SeaTreasure;
using static Settings;
using static StateManager;
using static Story;
using static Underground;
using static Underwater;

// Port of src/core/Game.ts

public class Game
{
    private const double DETECT_REACH = 4;
    /// <summary>coins for hitting a crab or a seagull with the gun or the bow</summary>
    private const double CRAB_REWARD = 1;
    /// <summary>a gull on the sand pays this; one shot in the air pays more</summary>
    private const double BIRD_REWARD = 2;
    private const double BIRD_AIR_REWARD = 5;
    private const double COCONUT_REWARD = 0.5;
    /// <summary>Above this the extra sharpness is barely visible, and the cost keeps climbing.</summary>
    private const double MAX_PIXEL_RATIO = 1.5;
    private static readonly List<THREE.Object3D> NO_TARGETS = new List<THREE.Object3D>();

    /// <summary>the chance that a bone sends the dog after a Lost & Found item instead of a coin or trash</summary>
    private const double DOG_LOST_CHANCE = 0.06;
    private const double SHARK_REWARD = 5;
    private const double FISH_REWARD = 1;
    private static readonly string[] LIGHT_ORDER = { "cycle", "golden", "day", "rain", "sunset", "night" };
    /// <summary>how long a full day lasts, in seconds (5 minutes)</summary>
    private const double DAY_SECONDS = 5 * 60;
    /// <summary>how often the game saves itself, in seconds of play</summary>
    private const double AUTOSAVE_SECONDS = 90;
    /// <summary>the shortest gap between two autosaves, whatever triggers them</summary>
    private const double AUTOSAVE_MIN_GAP = 15000;
    /// <summary>how far the grate's stone frame reaches from its middle (it is 2.34 by 1.94): no digging inside this</summary>
    private const double GRATE_R = 1.6;
    /// <summary>no find is buried closer than this to the grate, so none can be dug out from under the stone</summary>
    private const double GRATE_CLEAR = 2.6;
    /// <summary>the time of day each fixed mood is frozen at</summary>
    private static readonly Dictionary<string, double> MOOD_TIME = new Dictionary<string, double>
    {
        { "golden", 0.02 }, { "day", 0.28 }, { "rain", 0.4 }, { "sunset", 0.56 }, { "night", 0.78 },
    };
    private static readonly Dictionary<string, string> CATEGORY_LABEL = new Dictionary<string, string>
    {
        { "trash", "Recyclable" }, { "coin", "Coin" }, { "jewelry", "Jewelry" }, { "quest", "Lost & Found" }, { "shard", "Antique shard" },
    };

    private class FootState { public double x, z; public bool set; }
    private class DeckPos { public double x = 0, z = 0.5; }
    private class Interaction { public string label; public Action run; }
    private class LiftState { public bool up, down; }
    private class MapCursor { public double u = 0.5, v = 0.5; }
    private class HammockState { public int startDay; public double t; }
    private class CastleNoteState { public string msg = ""; public double at = 0; }
    private class LandingSpot { public double x, z; public bool island; }

    /// <summary>set when the scene was reloaded to load a saved game</summary>
    private readonly Snapshot PENDING;
    /// <summary>set when "New game" was chosen from the pause menu: skip the main menu and start straight away</summary>
    private readonly bool AUTOSTART;

    public readonly THREE.WebGLRenderer renderer;
    public readonly THREE.Scene scene = new THREE.Scene { name = "Main" };
    public readonly THREE.PerspectiveCamera camera = new THREE.PerspectiveCamera(50, 1, 0.1, 1000);
    public readonly StateManager state;
    public readonly AudioManager audio = new AudioManager();
    public readonly InputManager input;
    public readonly UIManager ui;
    public readonly CampModal campModal;
    public readonly Beach beach = new Beach();
    public readonly Ocean ocean;
    public readonly Sky sky;
    public readonly Camp camp = new Camp();
    public readonly Scenery scenery = new Scenery();
    public readonly Birds birds;
    public readonly SeaLife sea = new SeaLife();
    /// <summary>the pod that comes to swim beside the boat on a long run across open water</summary>
    public readonly Dolphins dolphins = new Dolphins();
    public readonly Whitecaps whitecaps = new Whitecaps();
    public readonly SeaTreasure treasure = new SeaTreasure();
    public readonly MapView mapView;
    private List<double[]> holes = new List<double[]>();
    private bool mapOpen = false;
    public readonly GolfCart cart;
    public readonly Boat boat;
    private string vehicle = null;
    private Tracks tracks;
    private double stepDist = 0;
    private FootState lastFoot = new FootState();
    private double tireDist = 0;
    private FootState lastCart = new FootState();
    private bool driving = false;
    /// <summary>where the player stands on the boat's deck (boat-local x, z)</summary>
    private DeckPos deck = new DeckPos { x = 0, z = 0.5 };
    private Interaction interaction2 = null;
    private double lookOffset = 0;
    private double dustTimer = 0;
    /// <summary>the view angle when nothing zooms or widens it; the player sets it in Settings</summary>
    private double baseFov => Math.Max(GameSettings.FOV_MIN, Math.Min(GameSettings.FOV_MAX, settings.fov));
    private Waypoint waypoint = null;
    private THREE.Mesh beam;
    public readonly Ecology ecology;
    public readonly LootSpawner spawner;
    public readonly DetectorTool detector = new DetectorTool();
    public readonly DigTool dig = new DigTool();
    public readonly Bullets bullets = new Bullets();
    public readonly Arrows arrows = new Arrows();
    private bool drawing = false;
    private double charge = 0;
    public readonly CleaningStation station;

    public string gameState = "EXPLORE";
    public GameSettings settings = loadSettings();
    private double menuT = 0;
    private int menuShot = 0;
    private bool menuSwitching = false;
    private THREE.Object3D menuTarget = null;
    private THREE.Vector3 menuFocus = new THREE.Vector3();
    private bool menuCamReady = false;
    public int tool = 1;
    public readonly Flags flags = new Flags();
    public readonly Dog dog;
    public readonly Stones stones;
    public readonly Grass grass;
    public readonly Sandcastles castles;
    /// <summary>every island in the sea; the first is the green one with the ruins</summary>
    public readonly Island[] islands = ISLANDS.Select(s => new Island(s)).ToArray();
    public Island island => islands[0];
    public readonly Underwater under = new Underwater();
    public readonly Achievements achievements = new Achievements();
    private double achT = 0;
    public readonly Underground cellar = new Underground();
    /// <summary>in the room under the hatch: the land is hidden and not updated, as when diving</summary>
    private bool underground = false;
    /// <summary>how far up the ladder the player has climbed</summary>
    private double ladderY = 0;
    /// <summary>little hearts that float up when the dog is petted</summary>
    private ParticleSystem hearts = new ParticleSystem(60, 0.17, -0.5, false, true);
    /// <summary>swimming on the sea floor: the land is hidden and is not updated while this is true</summary>
    private bool diving = false;
    private PlayerPose diveReturn = null;
    private THREE.Vector3 diveVel = new THREE.Vector3();
    private double diveY = -1.4;
    private object diveBg = null;
    private List<THREE.Object3D> hiddenByDive = new List<THREE.Object3D>();
    private LiftState lift = new LiftState();
    private string dogWas = "rest";
    public readonly Rain rain = new Rain();
    public readonly GamepadInput pad = new GamepadInput();
    /// <summary>the last thing the player touched was the controller</summary>
    public bool usingPad = false;
    /// <summary>the controller is holding the cleaning brush</summary>
    private bool padBrush = false;
    /// <summary>where the grate over the sun shaft lies on the beach</summary>
    private XZ grate = null;
    /// <summary>the dropdown whose choices are open for the controller</summary>
    private HTMLSelectElement padSelect = null;
    private HashSet<string> padKeys = new HashSet<string>();
    private bool padRun = false;
    private bool padL3Was = false;
    private bool padCalling = false;
    private MapCursor mapCursor = new MapCursor { u = 0.5, v = 0.5 };
    private HTMLElement mapCursorEl = null;
    private bool wasRaining = false;
    private int zoomIdx = 0;
    private bool binocView = false;
    /// <summary>the camera is raised to the eye, showing the viewfinder</summary>
    private bool camView = false;
    /// <summary>
    /// Underwater and in the room under the hatch the beach tools stay behind, but the camera can be taken out (3). True while
    /// it is in the hands there; <see cref="tool"/> keeps the beach tool for the way back.
    /// </summary>
    private bool altCamera => altTool == 3;
    /// <summary>what is in the hands down there: 0 the usual (the harpoon underwater, nothing under the dunes), 3 the camera, 11 the net</summary>
    private int altTool = 0;
    private bool holdingCamera => alt ? altCamera : tool == 3;
    private double netCooldown = 0;

    /// <summary>the fishing pier, the fishing itself (the float, the line, the catch) and the aquarium at camp</summary>
    public readonly Pier pier = new Pier();
    private readonly Fishing fishing = new Fishing();
    private Aquarium aquarium;
    /// <summary>sitting on the bench at the end of the pier, rod in hand</summary>
    private bool seated => fishing.active;
    /// <summary>the reel button is held down</summary>
    private bool fishHeld = false;
    private string tankKey = null;
    private readonly THREE.Vector3 rodTipAt = new THREE.Vector3();
    private readonly THREE.Color splashColor = new THREE.Color(0xdff3fa);
    /// <summary>a photo is taken right after this frame's picture is drawn, so it shows exactly what the player sees</summary>
    private bool photoPending = false;
    private double lastPhoto = -10000;
    /// <summary>the pinned photos last put on the board, so it is only redone when they change</summary>
    private string boardKey = null;
    private string rangeText = "—";
    private int rangeTick = 0;
    private THREE.Raycaster rayCaster = new THREE.Raycaster();
    private double shotCooldown = 0;
    private int crabsShot = 0;

    private bool started = false;
    private bool paused = false;
    private THREE.Clock clock = new THREE.Clock();
    private double time = 0;
    public readonly ViewModel view = new ViewModel();
    /// <summary>yaw uses three's rotation.y: 0 looks toward -z (the dunes and the camp), PI toward the sea</summary>
    private PlayerPose player = new PlayerPose { x = 0, z = CAMP_Z + 9, yaw = 0, pitch = -0.12 };
    private PlayerPose walkTo = null;
    private bool moving = false;
    private double dayTime = 0;
    private double eyeY = 0;
    private THREE.Vector3 feet = new THREE.Vector3();
    private BuriedItem hoveredItem = null;
    private string hoveredCamp = null;
    private BuriedItem inspectItem = null;
    private bool transitioning = false;
    private bool chillWarned = false;
    private HTMLElement canvas;
    private bool wasLocked = false;

    public Game(HTMLElement canvas, HTMLElement uiRoot)
    {
        this.canvas = canvas;
        PENDING = takePendingLoad();
        AUTOSTART = takeAutostart();
        state = new StateManager(PENDING?.data);
        mapView = new MapView(scenery.trunks);
        stones = new Stones(beach);
        grass = new Grass(beach);
        castles = new Sandcastles(beach);

        renderer = new THREE.WebGLRenderer(new THREE.WebGLRendererParameters { canvas = canvas, antialias = true });
        renderer.setPixelRatio(pixelRatio(true));
        renderer.shadowMap.enabled = true;
        renderer.shadowMap.type = THREE.ShadowMapType.PCFSoftShadowMap;
        renderer.toneMapping = THREE.ToneMapping.ACESFilmicToneMapping;

        scene.environment = new THREE.Texture { name = "RoomEnvironment" };
        scene.environmentIntensity = 0.3;

        sky = new Sky(renderer, scene);
        ocean = new Ocean(beach);
        ecology = new Ecology(beach);
        birds = new Birds(beach);
        spawner = new LootSpawner(beach, state);
        // finds on the sea floor rest on it, not on the beach far away
        spawner.ground = (x, z) => under.contains(x, z) ? under.floorAt(x, z) : beach.heightAt(x, z);
        under.onCatch = (_at, reward, label) => onFishSpeared(reward, label);
        aquarium = new Aquarium(under);
        camp.addProp(aquarium.group, "aquarium", Aquarium.AT.x, Aquarium.AT.z, 1.1, 1.15);
        fishing.makeFish = s => under.makeFish(s);
        fishing.waterAt = (x, z) => ocean.heightAt(x, z);
        fishing.pick = () => Catches.pickRod(dayTime, rain.amount > 0.4, state.aquarium, JS.Random);
        fishing.onCatch = s => onFishLanded(s);
        fishing.sound = name => audio.fishing(name);
        fishing.splash = (at, k) => dig.particles.burst(at, (int)(5 + 12 * k), 0.9 + k, 1.2 + k, 0.5, splashColor);
        station = new CleaningStation(renderer);

        scene.add(
            sky.group,
            beach.mesh,
            createSurround(),
            createSkirt(),
            scenery.group,
            birds.group,
            sea.group,
            dolphins.group,
            whitecaps.mesh,
            treasure.group,
            ocean.group,
            camp.group,
            ecology.group,
            spawner.group
        );
        foreach (var ov in detector.overlays) scene.add(ov);
        scene.add(
            dig.ring,
            dig.ringDark,
            dig.particles.points,
            bullets.group,
            arrows.group,
            flags.group,
            rain.group,
            stones.group,
            grass.group,
            castles.group,
            islands[0].group,
            islands[1].group,
            islands[2].group,
            under.group,
            cellar.group,
            hearts.points,
            view.proxy,
            camera
        );
        tracks = new Tracks(beach);
        scene.add(tracks.group);
        cart = new GolfCart(9, CAMP_Z + 9, Math.PI, beach);
        scene.add(cart.group);
        dog = new Dog(beach);
        scene.add(dog.group);
        placeDogHome();
        // a new game finds the boat tied up alongside the pier
        boat = new Boat(Pier.MOORING.x, Pier.MOORING.z, Pier.MOORING.heading);
        scene.add(boat.group, boat.wake.points);
        beam = new THREE.Mesh(
            new THREE.CylinderGeometry(0.18, 0.18, 60, 10, 1, true),
            new THREE.MeshBasicMaterial
            {
                color = 0xffd27a,
                transparent = true,
                opacity = 0.4,
                blending = THREE.Blending.AdditiveBlending,
                depthWrite = false,
                fog = false,
                side = THREE.Side.DoubleSide,
            }
        );
        beam.visible = false;
        scene.add(beam);
        camera.add(view.group);
        camera.rotation.order = "YXZ";
        camera.near = 0.05;
        camera.updateProjectionMatrix();

        input = new InputManager();
        input.overUI = () => DomHost.IsPointerOverUI();
        input.typing = () => DomHost.IsTyping();
        input.handlers = new InputHandlers
        {
            onClick = b => onClick(b),
            onPointerDown = b => onPointerDown(b),
            onPointerUp = (b, _) => onPointerUp(b),
            onPointerMove = (dx, dy, buttons) => onPointerMove(dx, dy, buttons),
            onWheel = d => onWheel(d),
            onKey = k =>
            {
                if (!padCalling) setUsingPad(false);
                onKey(k);
            },
        };
        pad.onButton = (b, down) => padButton(b, down);
        pad.onNav = d => padNav(d);
        pad.onActivity = () => setUsingPad(true);

        ui = new UIManager(uiRoot, state, new UIHooks
        {
            onTool = n => selectSlot(n),
            onInstantClean = () => station.instantClean(),
            onInspectBack = () => exitInspect(),
            onContinue = () => exitInspect(),
            onMute = () => audio.toggleMute(),
        });
        campModal = new CampModal(uiRoot, state, audio, new CampHooks
        {
            onSip = () => drinkCoffee(),
            onClose = () => closeCamp(),
            onLighting = () => cycleLighting(),
            onRecycled = _ => { },
            onAssembled = id => camp.showTrophy(id),
            onLuxury = () => applyLuxury(),
            toast = m => ui.toast(m),
        });

        station.onProgress = p => ui.setProgress(p);
        station.onComplete = () => onCleaned();
        state.subscribe(() => ecology.setScore(state.data.ecology));
        // the flag count shows next to the tool name
        state.subscribe(() =>
        {
            if (tool == 7 && started && !driving && !alt && gameState == "EXPLORE") ui.setTool(7);
        });

        ecology.setScore(state.data.ecology);
        foreach (var id in state.data.assembled) camp.showTrophy(id);
        cellar.cave.addSea(ocean.seaSheet(900, 700));
        cellar.cave.addClouds(sky.cloudMaterials);
        cellar.cave.addShark(sea.sharks[0].root);
        if (state.data.doorOpen) cellar.openDoor(true);
        cellar.cave.setTaken(state.data.hoardTaken);
        applyLuxury();
        void showDecor()
        {
            var d = state.data;
            var decorList = d.decor ?? new List<string>();
            int signIdx = d.signText >= 0 && d.signText < SIGN_TEXTS.Length ? d.signText : 0;
            camp.setDecor(decorList, SIGN_TEXTS[signIdx]);
            if (decorList.Count > 0) discover("camp_decor");
        }
        state.subscribe(showDecor);
        showDecor();
        state.subscribe(refreshPhotoBoard);
        refreshPhotoBoard();
        scene.add(pier.group, fishing.group);
        state.subscribe(refreshAquarium);
        refreshAquarium();
        achievements.onUnlock = a =>
        {
            audio.discover();
            ui.toast($"🏆 Achievement: {a.name}");
        };
        spawner.populate();
        dayTime = PENDING?.dayTime ?? 0;
        sky.setTime(dayTime);
        selectSlot(1);
        ui.setMode("EXPLORE");
        applySettings();
        applyDisplay(true);
        if (PENDING != null)
        {
            ui.skipIntro();
            restoreWorld(PENDING);
        }
        else if (AUTOSTART)
        {
            ui.skipIntro();
            started = true;
            ui.toast("A fresh start. Sweep the sand with your detector.");
        }
        else
        {
            enterMenu();
            // a save that could not be read used to drop you on the title screen with no word of why
            if (lastLoadFailed) ui.toast("That save could not be opened.");
        }

        scatterStones();
        ensureIslandFinds();
        ensureTreasureCaches();
        ensureCaptainChest();
        placeGrate();
        ensureDiveFinds();

        dogEnv = new DogEnv
        {
            playerX = 0,
            playerZ = 0,
            resolve = (x, z) =>
            {
                var r = resolveCart(x, z, 0.35);
                return (r.x, r.z);
            },
            bark = d => audio.bark(d),
            dust = p => dig.particles.burst(p, 7, 0.9, 1.3, 0.55, dogDustColor),
            print = (x, z, yaw, left) => tracks.paw(x, z, yaw, left),
        };

        document.addEventListener("pointerlockchange", _ =>
        {
            if (locked) setLockFailed(false);
            // (not when the mouse was let go because the player picked up a controller)
            if (!locked && !usingPad && started && gameState == "EXPLORE" && !transitioning && !mapOpen && !bookOpen && hammock == null) setPaused(true);
        });
        window.addEventListener("resize", _ => resize());
        resize();
        mapView.flags = flags.positions;
    }

    /// <summary>Gets everything ready behind the loading screen, then starts the game. Nothing runs yet, so nothing has to compete for the machine.</summary>
    public Task start()
    {
        try
        {
            var e = prepareEverythingCoroutine();
            while (e.MoveNext()) { }
        }
        catch
        {
            /* the game can still run: whatever was not prepared is simply built the first time it is seen */
        }
        finishStart();
        return Task.CompletedTask;
    }

    public IEnumerator startCoroutine()
    {
        var e = prepareEverythingCoroutine();
        while (true)
        {
            bool more;
            try
            {
                more = e.MoveNext();
            }
            catch (Exception ex)
            {
                // the game can still run: whatever was not prepared is built the first time it is seen
                UnityEngine.Debug.LogException(ex);
                break;
            }
            if (!more) break;
            yield return e.Current;
        }
        finishStart();
    }

    private void finishStart()
    {
        renderer.setAnimationLoop(() => frame());
        var boot = document.getElementById("boot");
        boot?.classList.add("gone");
        window.setTimeout(() => boot?.remove(), 700);
        loaded = true;
    }

    /// <summary>True once the loading screen has lifted.</summary>
    internal bool loaded { get; private set; }

    /// <summary>For the -autoshot smoke test (Core/AutoShot.cs): skips the title menu and intro card, as Begin would.</summary>
    internal void smokeTestBegin()
    {
        ui.hideMainMenu();
        beginPlay();
    }

    /// <summary>For the -autoshot smoke test: opens the achievements list (the screen with the most emoji).</summary>
    internal void smokeTestAchievements() => showAchievements();

    /// <summary>For the -autoshot-photo test: picks up the camera and raises it.</summary>
    internal void smokeTestRaiseCamera()
    {
        selectSlot(3);
        setCamView(true);
    }

    /// <summary>For the -autoshot-photo test: takes a photo, as a click with the camera raised does.</summary>
    internal void smokeTestTakePhoto()
    {
        lastPhoto = -10000;
        takePhoto();
    }

    internal void smokeTestLowerCamera() => setBinocView(false);

    internal int smokeTestPhotoCount => state.photos.Count;

    /// <summary>For the -autoshot-photo test: stands a few steps from the dog, looking at it.</summary>
    internal void smokeTestFaceDog(double dist)
    {
        // from the sea side, so the fire and the camp are behind the dog
        player.x = dog.x + 0.6;
        player.z = dog.z + dist;
        player.yaw = Math.Atan2(-(dog.x - player.x), -(dog.z - player.z));
        player.pitch = -0.22;
    }

    /// <summary>For the -autoshot-photo-alt test: goes under the sea or under the dunes, or back out, as the game does.</summary>
    internal void smokeTestGo(string where)
    {
        if (where == "dive")
        {
            state.data.ownsDiveGear = true;
            enterDive();
        }
        else if (where == "underground") enterUnderground();
        else if (diving) leaveDive();
        else if (underground) leaveUnderground();
    }

    /// <summary>For the -autoshot-photo-alt test: takes the camera out down there and raises it, or puts it away.</summary>
    internal void smokeTestAltCamera(bool on)
    {
        setAltCamera(on);
        if (on) setCamView(true);
    }

    /// <summary>For the -autoshot-settings test: changes the view angle for this run only (nothing is saved).</summary>
    internal void smokeTestFov(double fov)
    {
        settings.fov = fov;
        applySettings();
    }

    /// <summary>For the -autoshot-settings test: how far the same push forward tilts the view, plain and inverted, for the mouse and the stick.</summary>
    internal string smokeTestInvert()
    {
        double tilt(bool stick, bool invert)
        {
            bool m = settings.invertMouse, s = settings.invertStick;
            settings.invertMouse = settings.invertStick = invert;
            double before = player.pitch;
            applyLook(0, -40, stick);
            double moved = player.pitch - before;
            player.pitch = before;
            settings.invertMouse = m;
            settings.invertStick = s;
            return moved;
        }
        return $"mouse forward: plain {tilt(false, false):0.000}, inverted {tilt(false, true):0.000}; stick forward: plain {tilt(true, false):0.000}, inverted {tilt(true, true):0.000}";
    }

    internal void smokeTestSettings(bool open)
    {
        if (open) openSettings();
        else ui.closeSettings();
    }

    /// <summary>For the -autoshot-fish test: stands on the pier at this distance along it, looking this way.</summary>
    internal void smokeTestOnPier(double z, double yaw, double pitch)
    {
        if (seated) standUp();
        player.x = Pier.X;
        player.z = z;
        player.yaw = yaw;
        player.pitch = pitch;
    }

    /// <summary>For the -autoshot-fish test: a place on the beach to look at the pier and the boat from.</summary>
    internal void smokeTestSeePier()
    {
        player.x = Pier.X + 9;
        player.z = SHORE_Z - 6;
        player.yaw = Math.Atan2(-(Pier.X - 3 - player.x), -(Pier.Z0 + 12 - player.z));
        player.pitch = -0.08;
    }

    /// <summary>For the -autoshot-fish test: gets a rod and sits down on the bench.</summary>
    internal void smokeTestSit()
    {
        state.data.ownsRod = true;
        player.x = Pier.STAND.x;
        player.z = Pier.STAND.z;
        sitDown();
    }

    /// <summary>For the -autoshot-fish test: fishes as a player would: casts, strikes at the bite and reels in. Returns what is going on.</summary>
    internal string smokeTestFish()
    {
        if (!seated) return "not seated";
        if (fishing.phase == "idle" || fishing.phase == "bite") fishClick();
        fishHeld = fishing.phase == "reel";
        return $"{fishing.phase} {fishing.progress:0.00}, caught {state.data.fishCaught}, tank {state.aquarium.Count}";
    }

    internal void smokeTestStand() => standUp();

    /// <summary>For the -autoshot-fish test: takes the net out underwater and sweeps it, with a very long reach so it always finds something.</summary>
    internal string smokeTestNet()
    {
        state.data.ownsNet = true;
        setAltTool(11);
        camera.getWorldDirection(aimDir);
        var got = under.scoop(camera.position, aimDir, 60);
        if (got != null) state.addToAquarium(got.id);
        return $"net in hand: {altTool == 11}, caught: {got?.name ?? "nothing"}";
    }

    /// <summary>For the -autoshot-fish test: fills the aquarium (in the test's own save) and stands in front of it.</summary>
    internal void smokeTestAquarium(bool fill)
    {
        if (fill) foreach (var s in Catches.ALL) state.addToAquarium(s.id);
        double r = Aquarium.AT.rot;
        player.x = Aquarium.AT.x + Math.Sin(r) * 2.3;
        player.z = CAMP_Z + Aquarium.AT.z + Math.Cos(r) * 2.3;
        player.yaw = r;
        player.pitch = -0.2;
    }

    /// <summary>For the -autoshot-fish test: where the one golden fish is, and whether a net held a metre from it would take it.</summary>
    internal string smokeTestGold()
    {
        var at = under.goldAt();
        if (at == null) return "no golden fish in the sea";
        var seen = under.goldAt();
        var got = under.scoop(seen.clone().add(new THREE.Vector3(1, 0, 0)), new THREE.Vector3(-1, 0, 0), 2.6);
        if (got != null && got.id == "goldfish") state.addToAquarium(got.id);
        return $"golden fish at {at.x - 1200:0.0}, {at.y:0.0}, {at.z - 1200:0.0}; net a metre away took: {got?.name ?? "nothing"}";
    }

    /// <summary>For the -autoshot-sealife test: holds a leaping shark, a cruising one and a jumping fish in front of the pier, and looks at them.</summary>
    internal void smokeTestSeaLife(double yawOff, double pitch, double dist)
    {
        sea.smokeHold = (Pier.X + 0.5, Pier.Z1 + dist);
        player.x = Pier.X;
        player.z = Pier.Z1 - 1.5;
        player.yaw = Math.PI + yawOff;
        player.pitch = pitch;
    }

    internal string smokeTestExportPhoto() => state.photos.Count > 0 ? Photos.export(state.photos[state.photos.Count - 1].id) : null;

    /// <summary>For the -autoshot-photo test: saves the game under a fixed id, so it can be loaded again.</summary>
    internal bool smokeTestSave(string id) => writeSave(snapshot(), "Photo test", id) != null;

    /// <summary>For the -autoshot-photo test: stands in front of the photo board, looking at it.</summary>
    internal void smokeTestFacePhotoBoard()
    {
        var at = camp.photoBoardFront(3.2);
        player.x = at.x;
        player.z = at.z;
        player.yaw = at.yaw;
        player.pitch = -0.08;
    }

    /// <summary>
    /// For the -autoshot-tour smoke test: opens one of the text-heavy screens ("camp:board", "camp:sell", "book", "mail",
    /// "help", "credits") so it can be photographed, and closes it again with <see cref="smokeTestClose"/>.
    /// </summary>
    internal void smokeTestOpen(string what)
    {
        if (what.StartsWith("camp:")) openCamp(what.Substring(5), false);
        else if (what == "book") openBook();
        else if (what == "mail")
        {
            // a new game has no letters yet; post a few so the reader has something to show (only in the test's own save)
            state.data.letters ??= new List<string>();
            foreach (var l in LETTERS.Take(4)) if (!state.data.letters.Contains(l.id)) state.data.letters.Add(l.id);
            openMail();
        }
        else if (what == "help") ui.showHelp();
        else if (what == "credits") ui.showCredits();
        else if (what == "pause") setPaused(true);
        else if (what == "settings") openSettings();
        else if (what == "saves") ui.openSaves("save", saveHooks());
        else if (what == "saves-load") ui.openSaves("load", saveHooks());
        else if (what == "achievements") showAchievements();
        else if (what == "map") setMap(true);
        else if (what == "result") ui.showResult("Silver Locket", "Somebody carried this close for a long time. The hinge still works.", "Worth 85g", "Jewelry");
        else if (what == "inspect")
        {
            ui.showInspect("Silver Locket");
            ui.setProgress(0.4);
        }
        else if (what == "intro") ui.showIntro(() => { });
        else if (what == "toast")
        {
            ui.toast("🏆 Achievement: First Find");
            ui.setPrompt("Open the camper");
        }
    }

    /// <summary>
    /// For the -autoshot-holes test: digs a hole as deep as the trowel can make one, well below the water line, and stands
    /// the player beside it looking in. Returns how deep it is and how far its floor lies under the sea's surface.
    /// </summary>
    internal string smokeTestHole(double x, double z)
    {
        for (int i = 0; i < 8; i++) beach.digAt(x, z, 1.3, 0.5);
        player.x = x;
        player.z = z - 3.2;
        player.yaw = Math.PI;
        player.pitch = -0.62;
        return $"hole at z {z:0}: {beach.depthAt(x, z):0.00} m deep, floor {ocean.surfaceY - beach.heightAt(x, z):0.00} m under the sea's surface";
    }

    /// <summary>For the -autoshot-holes test: parks the cart on a hole and says how far it sits below the sand around it.</summary>
    internal string smokeTestCartOnHole(double x, double z)
    {
        cart.x = x;
        cart.z = z;
        cart.heading = 0;
        cart.speed = 0;
        cart.update(0, null, (px, pz) => new Resolved { x = px, z = pz, hit = false });
        player.x = x + 4.5;
        player.z = z;
        player.yaw = Math.PI / 2;
        player.pitch = -0.2;
        return $"cart over a {beach.depthAt(x, z):0.00} m hole sits {Beach.baseHeight(x, z) - 0.02 - cart.group.position.y:0.00} m lower than on flat sand";
    }

    /// <summary>
    /// For the -autoshot-birds test: fires a real bullet at a gull that is standing or flying, from a few metres off, and says
    /// what the purse held before. The shot lands within a frame or two; the test then reads the purse again.
    /// </summary>
    internal string smokeTestShootBird(bool flying)
    {
        var hit = birds.targets().FirstOrDefault(o => birds.standing(o) != flying);
        if (hit == null) return $"no {(flying ? "flying" : "standing")} gull around";
        var at = hit.getWorldPosition(new THREE.Vector3());
        var from = at.clone().add(new THREE.Vector3(0.5, flying ? -2.5 : 1.5, -4));
        bullets.spawn(from, at.clone().sub(from).normalize());
        return $"shot at a {(flying ? "flying" : "standing")} gull; coins before: {state.data.coins}";
    }

    internal double smokeTestCoins => state.data.coins;

    /// <summary>
    /// For the -autoshot-rain test: digs a deep hole and builds a finished sandcastle beside it, up the beach where the sea
    /// cannot reach them, then sets it raining (or back to the day cycle). Returns how deep the hole is and how many castles stand.
    /// </summary>
    internal string smokeTestRain(bool start)
    {
        for (int i = 0; i < 6; i++) beach.digAt(RAIN_TEST_X, RAIN_TEST_Z, 1.1, 0.5);
        holes.Add(new[] { RAIN_TEST_X, RAIN_TEST_Z });
        castles.add(RAIN_TEST_X - 3.5, RAIN_TEST_Z, 3);
        player.x = RAIN_TEST_X - 1;
        player.z = RAIN_TEST_Z - 6;
        player.yaw = Math.PI - 0.25;
        player.pitch = -0.45;
        state.data.lighting = "rain";
        return smokeTestRainStatus();
    }

    private const double RAIN_TEST_X = 10, RAIN_TEST_Z = -10;

    internal string smokeTestRainStatus() =>
        $"hole {beach.depthAt(RAIN_TEST_X, RAIN_TEST_Z):0.00} m deep, {castles.castles.Count} castle(s), {holes.Count} hole mark(s) on the map";

    internal void smokeTestRainStop() => state.data.lighting = "cycle";

    /// <summary>For the -autoshot-crust test: takes a find of this kind to the workbench, still in its sand, or back out (null).</summary>
    internal void smokeTestCrust(string defId)
    {
        if (defId == null)
        {
            exitInspect();
            return;
        }
        var def = Items.ITEM_DEFS.First(d => d.id == defId);
        enterInspect(new BuriedItem { id = -1, def = def, x = player.x, z = player.z, targetDepth = 0.3, revealed = true });
    }

    /// <summary>For the -autoshot-crust test: brushes a band across the middle of the find, as a held mouse would. Returns how clean it is.</summary>
    internal double smokeTestScrub()
    {
        for (int i = 0; i <= 60; i++)
        {
            double t = i / 60.0;
            station.pointer(new THREE.Vector2(-0.25 + t * 0.5, 0.05 * Math.Sin(t * 12)), true, 800);
        }
        return station.progress;
    }

    /// <summary>For the -autoshot-island test: stands this far from the island's middle at this angle, looking at its middle.</summary>
    internal void smokeTestIsland(double dist, double angle, double pitch, int which = 0)
    {
        var at = ISLANDS[which];
        player.x = at.x + Math.Cos(angle) * dist;
        player.z = at.z + Math.Sin(angle) * dist;
        player.yaw = Math.Atan2(-(at.x - player.x), -(at.z - player.z));
        player.pitch = pitch;
    }

    /// <summary>For the -autoshot-islands test: stands at a spot given from an island's landmark, looking at the landmark.</summary>
    internal string smokeTestLandmark(int which, double dx, double dz, double pitch)
    {
        var isl = islands[which];
        player.x = isl.ruins.x + dx;
        player.z = isl.ruins.z + dz;
        player.yaw = Math.Atan2(dx, dz);
        player.pitch = pitch;
        return $"{isl.spec.name}: landmark at {isl.ruins.x:0.0}, {isl.ruins.z:0.0}, ground {beach.heightAt(isl.ruins.x, isl.ruins.z):0.00}; standing at ground {beach.heightAt(player.x, player.z):0.00}; finds {string.Join(" ", isl.findSpots().Select(f => $"{f.id}@{f.x:0},{f.z:0}"))}";
    }

    /// <summary>For the -autoshot-islands test: puts the boat somewhere and the player at its wheel.</summary>
    internal string smokeTestBoatAt(double x, double z, double heading)
    {
        boat.x = x;
        boat.z = z;
        boat.heading = heading;
        boat.speed = 0;
        if (vehicle != "boat") enterVehicle("boat");
        var l = landingSpot();
        return $"boat at {x:0.0}, {z:0.0}: depth {ocean.surfaceY - beach.heightAt(x, z):0.00}, landing {(l.island ? "island" : "beach")} at {l.x:0.0}, {l.z:0.0}, near {nearShore()}";
    }

    /// <summary>For the -autoshot-dive test: puts the diver at a spot under the sea (metres from its middle), this high off the floor, looking this way.</summary>
    internal string smokeTestDiveAt(double lx, double lz, double up, double yaw, double pitch)
    {
        player.x = UW.x + lx;
        player.z = UW.z + lz;
        diveY = under.floorAt(player.x, player.z) + up;
        diveVel.set(0, 0, 0);
        player.yaw = yaw;
        player.pitch = pitch;
        return $"diver at {lx:0}, {lz:0}: floor {under.floorAt(player.x, player.z):0.0}, eye {diveY:0.0}";
    }

    /// <summary>For the -autoshot-dolphins test: takes the wheel out on open water, calls the pod and looks to one side.</summary>
    internal string smokeTestDolphins(double look)
    {
        if (vehicle != "boat")
        {
            boat.x = 40;
            boat.z = 120;
            boat.heading = Math.PI;
            enterVehicle("boat");
            dolphins.smokeCall();
        }
        lookOffset = look;
        return $"{dolphins.smokeState()}; boat at {boat.x:0}, {boat.z:0}, speed {boat.speed:0.0}";
    }

    /// <summary>For the -autoshot-islands test: a row of finds as they look clean, held up in front of the viewer.</summary>
    internal void smokeTestShowItems(string[] ids)
    {
        double fx = -Math.Sin(player.yaw), fz = -Math.Cos(player.yaw), rx = Math.Cos(player.yaw), rz = -Math.Sin(player.yaw);
        for (int i = 0; i < ids.Length; i++)
        {
            var m = LootGenerator.buildItem(ids[i]);
            double side = (i - (ids.Length - 1) / 2.0) * 0.62;
            m.scale.setScalar(0.62);
            m.position.set(camera.position.x + fx * 2.3 + rx * side, camera.position.y - 0.05 + (i % 2) * 0.0, camera.position.z + fz * 2.3 + rz * side);
            // tipped up toward the viewer, so the top of each one shows
            m.rotation.order = "YXZ";
            m.rotation.y = player.yaw;
            m.rotation.x = 0.75;
            scene.add(m);
        }
    }

    /// <summary>For the -autoshot-islands test: steps off the boat as E does, and says where that put the player.</summary>
    internal string smokeTestLeaveBoat()
    {
        exitVehicle();
        var isl = islandAt(player.x, player.z);
        return $"stepped off: vehicle {vehicle ?? "none"}, at {player.x:0.0}, {player.z:0.0}, ground {beach.heightAt(player.x, player.z):0.00}, on {(isl != null ? isl.spec.name : "no island")}";
    }

    /// <summary>
    /// For the -autoshot-shadow test: stands on the island at this distance and angle from its middle, facing away from the
    /// sun and looking down, so the player's own shadow lies in front on the ground and the stones.
    /// </summary>
    internal void smokeTestOwnShadow(double dist, double angle)
    {
        player.x = ISLAND.x + Math.Cos(angle) * dist;
        player.z = ISLAND.z + Math.Sin(angle) * dist;
        var sun = sky.sun.position.clone().sub(sky.sun.target.position);
        player.yaw = Math.Atan2(sun.x, sun.z);
        player.pitch = -0.85;
    }

    /// <summary>For the -autoshot-shadow test: stands with the sun at the back, just in front of the n-th biggest rock on the island, looking at it.</summary>
    internal string smokeTestShadowOnRock(int n, double gap)
    {
        var rock = island.colliders.OrderByDescending(c => c.r).Skip(n).FirstOrDefault();
        if (rock == null) return "no rock";
        var sun = sky.sun.position.clone().sub(sky.sun.target.position);
        double len = JS.Hypot(sun.x, sun.z);
        player.x = rock.x + sun.x / len * (rock.r + gap);
        player.z = rock.z + sun.z / len * (rock.r + gap);
        player.yaw = Math.Atan2(sun.x, sun.z);
        player.pitch = -0.35;
        return $"rock {n} r {rock.r:0.0}, sun height {sun.y / sun.length():0.00}";
    }

    /// <summary>For the -autoshot-saves test: starts a brand new game, as New Game in the pause menu does.</summary>
    internal void smokeTestNewGame() => newGame();

    internal bool smokeTestStarted => started;

    /// <summary>For the -autoshot-saves test: autosaves now, whatever the timer says.</summary>
    internal void smokeTestAutosave()
    {
        lastAuto = double.NegativeInfinity;
        autoSave();
    }

    /// <summary>For the -autoshot-saves test: this game's id and number, and every save in the list.</summary>
    internal string smokeTestSaves() =>
        $"this game: {state.data.gameId ?? "(none yet)"} (Game {state.data.gameNo}); saves: " +
        string.Join("; ", listSaves().Select(m => $"{m.name} [{m.id}] {describe(m)}"));

    /// <summary>For the -autoshot-photo test: what the F and E prompts would say right now, shown or not.</summary>
    internal string smokeTestPrompt() => $"F: {interaction?.label ?? "nothing"}, E: {interaction2?.label ?? "nothing"}";

    /// <summary>For the -autoshot-ui test: shows the game as it looks with a controller in the hands, or back as with the keyboard.</summary>
    internal void smokeTestPad(bool on) => setUsingPad(on);

    internal void smokeTestClose(string what)
    {
        if (what.StartsWith("camp:")) closeCamp();
        else if (what == "book" || what == "mail") closeBook();
        else if (what == "pause") setPaused(false);
        else if (what == "settings") ui.closeSettings();
        else if (what == "saves") ui.closeSaves();
        else if (what == "map") setMap(false);
        else if (what == "result" || what == "inspect") ui.hideInspect();
        else if (what == "intro") ui.skipIntro();
        else if (what == "toast") ui.setPrompt(null);
        else document.querySelector(".pause")?.remove();
    }

    /// <summary>For the -autoshot-pad test: the menu a controller would drive and the buttons it can reach, with their boxes.</summary>
    internal string smokeTestPadInfo()
    {
        var root = overlayRoot();
        if (root == null) return "no menu";
        var items = padFocusables(root);
        return $"menu '{root.className}', {items.Count} reachable: " + string.Join("; ", items.Take(8).Select(e =>
        {
            var r = e.getBoundingClientRect();
            var label = (e.textContent ?? "").Trim();
            return $"{(label.Length > 14 ? label.Substring(0, 14) : label)} [{r.left:0},{r.top:0} {r.width:0}x{r.height:0}]";
        }));
    }

    /// <summary>For the -autoshot smoke test: where the player stands.</summary>
    internal string smokeTestWhere() => $"x {player.x:0.0}, z {player.z:0.0}, state {gameState}{(paused ? " (paused)" : "")}";

    /// <summary>
    /// For the -autoshot-minimap smoke test: a few map-only dig holes around the player (some overlapping, none dug into the
    /// sand) and a far destination, so the minimap shows holes, the dashed line and its edge markers.
    /// </summary>
    internal void smokeTestMinimapSetup()
    {
        double[][] offs = { new[] { 3.0, 0.0 }, new[] { 3.9, 0.4 }, new[] { 0.0, 6.0 }, new[] { -5.0, -2.0 }, new[] { -5.6, -2.6 }, new[] { 8.0, 8.0 }, new[] { -12.0, 9.0 }, new[] { 14.0, -6.0 } };
        foreach (var o in offs) holes.Add(new[] { player.x + o[0], player.z + o[1] });
        setWaypoint(new Waypoint { x = player.x + 45, z = player.z + 40 });
    }

    internal void smokeTestMinimapClear() => setWaypoint(null);

    /// <summary>For the -autoshot smoke test: turns the player to look somewhere.</summary>
    internal void smokeTestLook(double yaw, double pitch)
    {
        player.yaw = yaw;
        player.pitch = pitch;
    }

    /// <summary>
    /// Lights are built into shaders, so a place with a different set of lights needs every material rebuilt, and things are only sent to the
    /// graphics chip the first time they are drawn. Both are done here once, so the game never has to stop for them while playing.
    /// </summary>
    private IEnumerator prepareEverythingCoroutine()
    {
        var bar = document.getElementById("boot-bar");
        var places = new (bool camp, bool near, bool under, bool cellar, bool cave)[]
        {
            (true, false, false, false, false),
            (false, false, true, false, false),
            (false, false, false, true, true),
            (false, false, false, true, false),
            (true, true, false, false, false),
        };
        int steps = places.Length + 2;
        void done(int n)
        {
            if (bar != null) bar.style.width = $"{JS.Str((double)n / steps * 100)}%";
        }
        for (int i = 0; i < places.Length; i++)
        {
            var p = places[i];
            camp.group.visible = p.camp;
            cart.group.visible = p.camp;
            under.group.visible = p.under;
            cellar.group.visible = p.cellar;
            cellar.setSide(p.cave);
            camp.setLightsNear(p.near);
            renderer.compile(scene, camera);
            done(i + 1);
            yield return null;
        }
        // the last place was the camp as it stands at the start
        under.group.visible = false;
        cellar.group.visible = false;
        station.prepare(renderer, spawner.group);
        campModal.photos = takeItemPhotos(QUEST_DEFS.Where(q => q.need.defId != null).Select(q => q.need.defId));
        done(places.Length + 1);
        yield return null;
        sendEverythingToGpu();
        done(steps);
    }

    /// <summary>Draws the whole world once into a one-pixel image, with everything shown: all that is uploaded, and it costs almost nothing to fill.</summary>
    private void sendEverythingToGpu()
    {
        var shown = new List<THREE.Object3D>();
        var unculled = new List<THREE.Object3D>();
        scene.traverse(o =>
        {
            if (!o.visible)
            {
                o.visible = true;
                shown.Add(o);
            }
            if (o.frustumCulled)
            {
                o.frustumCulled = false;
                unculled.Add(o);
            }
        });
        // their torches stay out, so this uses the shaders made for the camp
        var dark = new List<THREE.Object3D>();
        foreach (var world in new[] { under.group, cellar.group })
        {
            world.traverse(o =>
            {
                if (o is THREE.Light && o.visible)
                {
                    o.visible = false;
                    dark.Add(o);
                }
            });
        }
        var target = new THREE.WebGLRenderTarget(1, 1);
        try
        {
            renderer.setRenderTarget(target);
            for (int lod = 2; lod >= 0; lod--)
            {
                beach.setAllDetail(lod);
                renderer.render(scene, camera);
            }
        }
        finally
        {
            renderer.setRenderTarget(null);
            target.dispose();
            foreach (var o in shown) o.visible = false;
            foreach (var o in unculled) o.frustumCulled = true;
            foreach (var o in dark) o.visible = true;
        }
    }

    /// <summary>
    /// The picture's pixels per pixel of the UI's page. The page is stretched to the screen (window.devicePixelRatio is that
    /// stretch), so this is what keeps the 3D picture at the resolution it always had: the screen's own pixels when "Sharp
    /// picture" is on (up to MAX_PIXEL_RATIO times Windows' 100% size), Windows' 100% size when it is off.
    /// </summary>
    private double pixelRatio(bool sharp)
    {
        double os = Math.Max(0.5, window.osPixelRatio);
        return window.devicePixelRatio / os * (sharp ? Math.Min(os, MAX_PIXEL_RATIO) : 1);
    }

    private void resize()
    {
        // the page's stretch changes with the window, and the picture's pixel ratio with it
        if (settings != null) renderer.setPixelRatio(pixelRatio(settings.sharp) * settings.renderScale);
        int w = (int)Math.Max(1, window.innerWidth);
        int h = (int)Math.Max(1, window.innerHeight);
        renderer.setSize(w, h, false);
        camera.aspect = (double)w / h;
        camera.updateProjectionMatrix();
        station.resize(w, h);
    }

    // ---------------------------------------------------------------- input

    private void onClick(int button)
    {
        if (!started || transitioning || paused || driving) return;
        if (hammock != null)
        {
            leaveHammock(false);
            return;
        }
        if (seated)
        {
            if (button == 0 && gameState == "EXPLORE")
            {
                if (!locked && !usingPad) requestLock(true);
                // a bite is struck the moment the button goes down (see onPointerDown); the release casts or brings the line in
                if (fishing.phase == "idle" || fishing.phase == "wait") fishClick();
            }
            return;
        }
        if (underground)
        {
            if (button == 0 && gameState == "EXPLORE" && !locked && !usingPad) requestLock(true);
            if (altCamera && gameState == "EXPLORE") cameraClick(button);
            return;
        }
        if (diving && altCamera && gameState == "EXPLORE")
        {
            if (button == 0 && !locked && !usingPad) requestLock(true);
            cameraClick(button);
            return;
        }
        if (diving && altTool == 11 && gameState == "EXPLORE")
        {
            if (button != 0) return;
            if (!locked && !usingPad) requestLock(true);
            swingNet();
            return;
        }
        if (diving)
        {
            // underwater a click either picks up what you are looking at or looses the harpoon
            if (button == 0 && gameState == "EXPLORE")
            {
                if (!locked && !usingPad) requestLock(true);
                if (hoveredItem != null) useFind(hoveredItem);
                else fireHarpoon();
            }
            return;
        }
        if (button == 2)
        {
            if (gameState == "EXPLORE" && tool == 8) knockCastle();
            else if (binocView || camView) setBinocView(false);
            return;
        }
        if (button != 0) return;
        if (gameState == "INSPECT") return;
        // try to capture the mouse for smooth look; if the browser refuses, the free cursor still works
        if (gameState == "EXPLORE" && !locked && !usingPad) requestLock(true);
        if (gameState == "EXPLORE" && tool == 5)
        {
            if (!binocView) setBinocView(true);
            else zoomIdx = 1 - zoomIdx;
            audio.ui();
            return;
        }
        if (gameState == "EXPLORE" && tool == 3)
        {
            cameraClick(0);
            return;
        }
        if (gameState == "EXPLORE" && tool == 6) return; // the bow works on press and release
        if (hoveredItem != null && gameState == "EXPLORE")
        {
            useFind(hoveredItem);
            return;
        }
        if (hoveredCamp != null)
        {
            useCampObject(hoveredCamp);
            return;
        }
        if (gameState == "EXPLORE" && tool == 2 && toolHit != null) tryDig(toolHit);
        if (gameState == "EXPLORE" && tool == 4) shoot();
        if (gameState == "EXPLORE" && tool == 7) plantFlag();
        if (gameState == "EXPLORE" && tool == 8) buildCastle();
    }

    private void plantFlag()
    {
        if (state.data.flags <= 0)
        {
            audio.error();
            ui.toast("Out of flags. Pick one up with F, or buy more at the camper shop.");
            return;
        }
        var at = toolHit;
        if (at == null) return;
        if (beach.heightAt(at.x, at.z) < ocean.surfaceY + 0.1)
        {
            audio.error();
            ui.toast("Flags need dry sand.");
            return;
        }
        state.useFlag();
        if (state.data.flags <= 0)
        {
            // that was the last one: the flag tool leaves the list and the next tool takes its place
            var order = toolOrder();
            int idx5 = order.IndexOf(5);
            selectSlot(idx5 >= 0 && idx5 + 1 < order.Count ? order[idx5 + 1] : order[0]);
        }
        flags.add(at.x, at.z, beach.heightAt(at.x, at.z) - 0.05);
        discover("flag_first");
        audio.thud();
        dig.particles.burst(at.clone().setY(at.y + 0.05), 8, 1, 1.2, 0.5, new THREE.Color(0xe6ccb2));
        ui.toast($"Flag planted ({state.data.flags} left). It is on your maps.");
    }

    private bool canUseBow() =>
        started && !paused && !driving && !alt && !transitioning && gameState == "EXPLORE" && tool == 6;

    private void onPointerDown(int button)
    {
        if (seated)
        {
            if (button != 0 || !started || paused || gameState != "EXPLORE") return;
            fishHeld = true;
            if (fishing.phase == "bite") fishClick();
            return;
        }
        if (!canUseBow()) return;
        if (button == 2 && drawing)
        {
            cancelDraw(); // right-click lets the string down again
        }
        else if (button == 0 && !drawing)
        {
            drawing = true;
            charge = 0;
            audio.bowDraw();
        }
    }

    private void onPointerUp(int button)
    {
        if (button == 0) fishHeld = false;
        if (button != 0 || !drawing) return;
        double power = charge;
        cancelDraw();
        if (power >= 0.18) shootArrow(power);
    }

    private void cancelDraw()
    {
        drawing = false;
        charge = 0;
        view.setDraw(0);
        ui.setDraw(0);
    }

    private void shootArrow(double power)
    {
        var ray = input.ray(camera, true);
        var beachHit = beach.raycast(ray.ray.origin, ray.ray.direction);
        double aimDist = Math.Min(beachHit != null ? beachHit.distanceTo(ray.ray.origin) : 70, 70);
        var aim = ray.ray.origin.clone().addScaledVector(ray.ray.direction, aimDist);
        camera.updateMatrixWorld(true);
        var origin = camera.localToWorld(new THREE.Vector3(0.05, -0.1, -0.7));
        var dir = aim.sub(origin).normalize();
        // a lob that climbs a little at long range, since arrows drop
        dir.y += 0.012 * (1 - power);
        dir.normalize();
        arrows.spawn(origin, dir, 14 + power * 38);
        view.releaseBow();
        audio.bowRelease(power);
        discover("bow_first");
    }

    private void onPointerMove(double dx, double dy, int buttons)
    {
        if (!started || paused) return;
        if (gameState == "INSPECT")
        {
            if ((buttons & 2) != 0) station.rotate(dx * 0.0045, dy * 0.0045);
            // a real push of the mouse takes the brush back from the controller; a nudge of the desk does not
            if (padBrush && Math.Abs(dx) + Math.Abs(dy) > 6) setUsingPad(false);
            return;
        }
        // mouse look: with the pointer locked, or right-drag while the cursor is free
        if (Math.Abs(dx) + Math.Abs(dy) > 1) setUsingPad(false);
        if (locked || (buttons & 2) != 0) applyLook(dx, dy, false);
    }

    /// <summary>Turns the view by a mouse-sized amount of movement (pixels); the controller's right stick feeds it too.</summary>
    private void applyLook(double dx, double dy, bool fromStick)
    {
        // inverted look: forward on the mouse or the stick looks down
        if (fromStick ? settings.invertStick : settings.invertMouse) dy = -dy;
        if (driving)
        {
            lookOffset = Math.Min(1.5, Math.Max(-1.5, lookOffset - dx * 0.0022));
            player.pitch = Math.Min(1.2, Math.Max(-1.0, player.pitch - dy * 0.0022));
            return;
        }
        walkTo = null;
        double zk = camera.fov / baseFov;
        double sens = settings.sensitivity;
        player.yaw -= dx * 0.0022 * zk * sens;
        player.pitch = Math.Min(1.45, Math.Max(-1.45, player.pitch - dy * 0.0022 * zk * sens));
    }

    /// <summary>Captures the mouse for looking around, so no pointer shows while playing. Quietly does nothing if the browser says no.</summary>
    private void grabMouse()
    {
        if (usingPad || locked) return;
        requestLock(false);
    }

    private int lockErrors = 0;

    /// <summary>
    /// Asks to capture the mouse.
    /// </summary>
    private void requestLock(bool fromClick)
    {
        try
        {
            input.requestPointerLock();
            lockErrors = 0;
        }
        catch
        {
            if (fromClick && ++lockErrors >= 2) setLockFailed(true);
        }
    }

    private void setLockFailed(bool on)
    {
        lockFailed = on;
        if (!on) lockErrors = 0;
        // with no mouse capture the cursor has to stay visible, since it is what steers
        document.body.classList.toggle("nolock", on);
    }

    /// <summary>the browser refused to capture the mouse, so the cursor itself has to do the steering</summary>
    private bool lockFailed = false;

    /// <summary>True only when the mouse could not be captured (an old browser) and the cursor has to steer by itself.</summary>
    /// <summary>
    /// Sand the rain must leave as it is: a find lying in its hole until it is picked up, and the captain's hatch and the
    /// pit it sits in, which is the way down to the cellar.
    /// </summary>
    private List<(double x, double z, double r)> rainKeep()
    {
        var keep = new List<(double x, double z, double r)>();
        foreach (var it in spawner.items)
        {
            if (it.def.id == "captain_chest" && it.revealed) keep.Add((it.x, it.z, 2.8));
            else if (it.revealed && !it.collected) keep.Add((it.x, it.z, 1.4));
        }
        return keep;
    }

    private bool cursorSteer => !locked && !usingPad && lockFailed;

    private double lastWheel = 0;

    /// <summary>The d-pad's left slot: flags, bow and gun share it, and each press moves to the next one the player has.</summary>
    private void padOtherTool()
    {
        var others = ui.padOtherTools();
        if (others.Count == 0)
        {
            ui.toast("No other tools yet. The camper shop sells flags, a bow and a toy pistol.");
            return;
        }
        int i = others.IndexOf(tool);
        selectSlot(others[(i + 1) % others.Count]);
    }

    /// <summary>The tools the player can scroll through, in order. Flags only while there are some left.</summary>
    private List<int> toolOrder()
    {
        var order = new List<int> { 1, 2, 3, 5 };
        if (state.data.flags > 0) order.Add(7);
        if (state.data.ownsBucket) order.Add(8);
        if (state.data.ownsBow) order.Add(6);
        if (state.data.ownsGun) order.Add(4);
        if (vehicle == "boat") order.RemoveRange(0, 2);
        return order;
    }

    /// <summary>scroll swaps between the detector and the trowel</summary>
    private void onWheel(double d)
    {
        double now = JS.Now();
        if (!started || paused || driving || seated || gameState != "EXPLORE" || now - lastWheel < 180) return;
        lastWheel = now;
        if (alt)
        {
            // down there the wheel goes round what there is: the harpoon, the net and the camera
            cycleAltTool(d > 0 ? 1 : -1);
            return;
        }
        var order = toolOrder();
        int i = Math.Max(0, order.IndexOf(tool));
        selectSlot(order[(i + (d > 0 ? 1 : order.Count - 1)) % order.Count]);
    }

    /// <summary>away from the beach altogether: diving, or in the room under the hatch</summary>
    private bool alt => diving || underground;

    private bool locked => input.locked;

    /// <summary>The island map pauses the world like the pause menu does.</summary>
    private void setMap(bool on)
    {
        if (on == mapOpen) return;
        mapOpen = on;
        paused = on;
        if (on) discover("map_first");
        input.keys.Clear();
        if (on)
        {
            document.exitPointerLock();
            audio.setOceanLevel(0.02);
        }
        else
        {
            audio.setOceanLevel(0.06);
            grabMouse();
        }
        var alts = new List<MapAlt>();
        if (state.data.hasTreasureMap)
        {
            alts.Add(new MapAlt
            {
                name = "Treasure map",
                draw = (ctx, size) => mapView.drawTreasure(ctx, size, new MapPlayer { x = player.x, z = player.z, yaw = player.yaw }, time, waypoint, new Waypoint { x = cart.x, z = cart.z }, new Waypoint { x = boat.x, z = boat.z }, mapView.treasures),
            });
        }
        if (state.data.hasBottleMap)
        {
            alts.Add(new MapAlt
            {
                name = "Bottle map",
                draw = (ctx, size) => mapView.drawTreasure(ctx, size, new MapPlayer { x = player.x, z = player.z, yaw = player.yaw }, time, waypoint, new Waypoint { x = cart.x, z = cart.z }, new Waypoint { x = boat.x, z = boat.z }, mapView.captain, true),
            });
        }
        ui.showMap(
            on,
            (ctx, size) => mapView.drawBig(ctx, size, new MapPlayer { x = player.x, z = player.z, yaw = player.yaw }, holes, waypoint, new Waypoint { x = cart.x, z = cart.z }, new Waypoint { x = boat.x, z = boat.z }),
            (u, v) => pickDestination(u, v),
            alts
        );
    }

    /// <summary>A click on the full map sets (or clears) the destination. u and v are 0..1 across the map.</summary>
    private void pickDestination(double u, double v)
    {
        double x = MAP_VIEW.cx - MAP_VIEW.half + u * 2 * MAP_VIEW.half;
        double z = MAP_VIEW.cz - MAP_VIEW.half + v * 2 * MAP_VIEW.half;
        if (waypoint != null && JS.Hypot(waypoint.x - x, waypoint.z - z) < 6)
        {
            setWaypoint(null);
            ui.toast("Destination removed.");
            return;
        }
        setWaypoint(new Waypoint { x = x, z = z });
        ui.toast("Destination marked. Follow the dashed line or the glowing beam.");
    }

    private void setWaypoint(Waypoint w)
    {
        waypoint = w;
        beam.visible = w != null;
        if (w != null) beam.position.set(w.x, Math.Max(beach.heightAt(w.x, w.z), ocean.surfaceY) + 30, w.z);
    }

    private void setPaused(bool on)
    {
        if (on == paused) return;
        paused = on;
        ui.showPause(on, new PauseHooks
        {
            onResume = () => setPaused(false),
            onNew = () => newGame(),
            onSettings = () => openSettings(),
            onAchievements = () => showAchievements(),
            onMenu = () => backToMenu(),
            saves = saveHooks(),
        });
        audio.setOceanLevel(on ? 0.02 : gameState == "INSPECT" ? 0.02 : 0.06);
        audio.setMusicLevel(on ? 0.08 : 0, 1);
        if (on) document.exitPointerLock();
        else grabMouse();
        input.keys.Clear();
    }

    private void onKey(string code)
    {
        if (menuKey(code)) return;
        if (!started) return;

        if (hammock != null)
        {
            if (code != "KeyN") leaveHammock(false);
            return;
        }
        if (code == "KeyN")
        {
            audio.toggleMute();
            return;
        }
        if (code == "KeyM")
        {
            if (mapOpen) setMap(false);
            else if (!paused && !alt && gameState == "EXPLORE" && !transitioning) setMap(true);
            return;
        }
        if (code == "Escape")
        {
            if (bookOpen) closeBook();
            else if (mapOpen) setMap(false);
            else if (paused) setPaused(false);
            else if (gameState == "EXPLORE" && !transitioning) setPaused(true);
            else if (gameState == "CAMP") closeCamp();
            else if (gameState == "INSPECT") exitInspect();
            return;
        }
        if (mapOpen && code == "KeyT")
        {
            ui.toggleMap();
            return;
        }
        if (bookOpen)
        {
            if (code == "ArrowLeft" || code == "KeyA") ui.bookTurn(-1);
            else if (code == "ArrowRight" || code == "KeyD") ui.bookTurn(1);
            return;
        }
        if (paused) return;
        if (gameState == "INSPECT")
        {
            if (code == "Escape") exitInspect();
            else if (code == "Space" && ui.isResultOpen()) exitInspect();
            return;
        }
        if (underground)
        {
            if (code == "KeyF") interaction?.run?.Invoke();
            else if (code == "Digit3") setAltCamera(!altCamera);
            else if (code.StartsWith("Digit")) ui.toast("There is nothing to dig or shoot down here. Only the camera (3) works.");
            return;
        }
        if (diving)
        {
            // Q swims back up; F picks up what is in reach
            if (code == "KeyQ") leaveDive();
            else if (code == "KeyF") interaction?.run?.Invoke();
            else if (code == "Digit1") setAltTool(0);
            else if (code == "Digit2") setAltTool(altTool == 11 ? 0 : 11);
            else if (code == "Digit3") setAltCamera(!altCamera);
            else if (code.StartsWith("Digit")) ui.toast("Down here you have the harpoon (1), the net (2) and the camera (3).");
            return;
        }
        if (seated)
        {
            // on the bench only getting up is left on the keys; the mouse does the fishing
            if ((code == "KeyF" || code == "KeyE") && gameState == "EXPLORE" && !transitioning) standUp();
            return;
        }
        if (driving && code != "KeyF" && code != "KeyE") return;
        switch (code)
        {
            case "KeyF":
                if (gameState == "EXPLORE" && !transitioning) interaction?.run?.Invoke();
                break;
            case "KeyE":
                if (gameState != "EXPLORE" || transitioning) break;
                if (interaction2 != null) interaction2.run();
                else if (vehicle == "boat")
                {
                    audio.error();
                    ui.toast("You cannot leave the boat out here. Park it right at the beach first, so you can climb back aboard.");
                }
                break;
            case "Digit1": selectSlot(1); break;
            case "Digit2": selectSlot(2); break;
            case "Digit3": selectSlot(3); break;
            case "Digit4": selectSlot(4); break;
            case "Digit5": selectSlot(5); break;
            case "Digit6": selectSlot(6); break;
            case "Digit7": selectSlot(7); break;
            case "Digit8": selectSlot(8); break;
            case "Space":
                if (gameState == "CAMP") break;
                selectSlot(tool == 1 ? 2 : 1);
                break;
            case "Escape":
                if (gameState == "CAMP") closeCamp();
                break;
        }
    }

    /// <summary>Raises the binoculars to the eyes (the zoomed view) or lowers them back into the hands.</summary>
    private void setBinocView(bool on)
    {
        if (drawing) cancelDraw();
        bool v = on && tool == 5;
        binocView = v;
        // lowering the binoculars lowers the camera too, so every "put it down" in the game covers both
        if (!on) camView = false;
        if (v) discover("binocs_first");
        if (v) zoomIdx = 0;
        view.setRaised(v || camView);
    }

    /// <summary>Raises the camera to the eye (the viewfinder) or lowers it back into the hands.</summary>
    private void setCamView(bool on)
    {
        if (drawing) cancelDraw();
        binocView = false;
        camView = on && holdingCamera;
        view.setRaised(camView);
        audio.ui();
    }

    /// <summary>A click with the camera in the hands: the first raises it to the eye, the next ones take photos; the right button lowers it.</summary>
    private void cameraClick(int button)
    {
        if (button == 2)
        {
            if (camView) setBinocView(false);
        }
        else if (button == 0)
        {
            if (!camView) setCamView(true);
            else takePhoto();
        }
    }

    /// <summary>Underwater or under the dunes: takes the camera out, or puts it away again (the harpoon comes back underwater).</summary>
    private void setAltCamera(bool on) => setAltTool(on ? 3 : 0);

    /// <summary>
    /// Underwater or under the dunes: what is in the hands. 0 is the usual (the harpoon underwater, empty hands under the dunes),
    /// 3 the camera, 11 the hand net (underwater only, once bought).
    /// </summary>
    private void setAltTool(int n)
    {
        if (!alt || transitioning || gameState != "EXPLORE" || n == altTool) return;
        if (n == 11 && !diving) return;
        if (n == 11 && !state.data.ownsNet)
        {
            audio.error();
            ui.toast("You do not have a hand net yet. The camper shop sells one.");
            return;
        }
        setBinocView(false);
        altTool = n;
        if (n == 3)
        {
            view.setTool(3);
            ui.setBadgeText("📷", "Camera");
        }
        else if (n == 11)
        {
            view.setTool(11);
            ui.setBadgeText("🐠", "Net");
        }
        else if (diving) applyDiveHands();
        else
        {
            view.setTool(0);
            ui.setTool(0);
        }
        audio.ui();
    }

    /// <summary>The next thing to hold down there: the harpoon, the net (once bought) and the camera in turn underwater; the camera or nothing under the dunes.</summary>
    private void cycleAltTool(int dir)
    {
        var order = new List<int> { 0 };
        if (diving && state.data.ownsNet) order.Add(11);
        order.Add(3);
        int i = Math.Max(0, order.IndexOf(altTool));
        setAltTool(order[(i + dir + order.Count) % order.Count]);
    }

    /// <summary>A sweep of the net at whatever is close in front of the diver.</summary>
    private void swingNet()
    {
        double now = JS.Now();
        if (now - netCooldown < 600) return;
        netCooldown = now;
        view.doSwing();
        audio.fishing("net");
        camera.updateMatrixWorld(true);
        camera.getWorldDirection(aimDir);
        var got = under.scoop(camera.position, aimDir);
        if (got == null) return;
        discover("net_first");
        if (got.id == "goldfish" && state.addToAquarium(got.id))
        {
            audio.discover();
            ui.toast("🐠 You caught the Golden Fish! The only one in the whole sea.");
        }
        else if (state.addToAquarium(got.id))
        {
            audio.discover();
            ui.toast($"🐠 New for the aquarium: {got.name}");
        }
        else
        {
            audio.ui();
            ui.toast($"You already have a {got.name}, so you let it go.");
        }
    }

    // ---------------------------------------------------------------- fishing from the pier

    /// <summary>Sits down on the bench at the end of the pier, rod in hand.</summary>
    private void sitDown()
    {
        if (seated || driving || alt || hammock != null || gameState != "EXPLORE" || transitioning) return;
        setBinocView(false);
        if (drawing) cancelDraw();
        walkTo = null;
        fishHeld = false;
        player.x = Pier.SEAT.x;
        player.z = Pier.SEAT.z;
        player.yaw = Pier.SEAT.yaw;
        player.pitch = -0.12;
        detector.setActive(false);
        dig.setActive(false);
        view.proxy.visible = false;
        view.setTool(10);
        ui.setTool(0);
        ui.setBadgeText("🎣", "Fishing rod");
        fishing.sit();
        audio.ui();
    }

    /// <summary>Gets up from the bench: the beach tool comes back.</summary>
    private void standUp()
    {
        if (!seated) return;
        fishing.stand();
        fishHeld = false;
        ui.setDraw(0);
        player.x = Pier.STAND.x;
        player.z = Pier.STAND.z;
        player.pitch = -0.1;
        eyeY = 0;
        view.proxy.visible = true;
        view.setTool(tool);
        ui.setTool(tool);
        detector.setActive(tool == 1);
        dig.setActive(tool == 2);
        camera.fov = baseFov;
        camera.updateProjectionMatrix();
        audio.ui();
    }

    /// <summary>A press of the button with the rod: casts toward where the player looks, brings the line in, or strikes at a bite.</summary>
    private void fishClick()
    {
        // looking up casts further out, looking down drops the float close in
        double dist = Math.Min(10.5, Math.Max(4.5, 7.5 + player.pitch * 6));
        double ax = Pier.SEAT.x - Math.Sin(player.yaw) * dist;
        double az = Math.Max(Pier.Z1 + 1.5, Pier.SEAT.z - Math.Cos(player.yaw) * dist);
        fishing.click(ax, az);
    }

    /// <summary>Sitting on the bench: free to look about over the sea while the float does its work.</summary>
    private void updateFishing(double dt)
    {
        var p = player;
        moving = false;
        // the bench faces the sea, and so does whoever sits on it
        double rel = Math.Atan2(Math.Sin(p.yaw - Pier.SEAT.yaw), Math.Cos(p.yaw - Pier.SEAT.yaw));
        p.yaw = Pier.SEAT.yaw + Math.Min(1.3, Math.Max(-1.3, rel));
        p.pitch = Math.Min(0.7, Math.Max(-0.95, p.pitch));
        p.x = Pier.SEAT.x;
        p.z = Pier.SEAT.z;
        eyeY = Pier.SEAT.y;
        feet.set(p.x, Pier.DECK, p.z);
        camera.position.set(p.x, eyeY + Math.Sin(time * 1.1) * 0.004, p.z);
        camera.rotation.set(p.pitch, p.yaw, 0);
        view.update(dt, false, feet, p.yaw, p.pitch);
        applyToolFov(dt);
        camera.updateMatrixWorld(true);
        view.rodTip.getWorldPosition(rodTipAt);
        fishing.held = fishHeld && gameState == "EXPLORE";
        fishing.update(dt, time, rodTipAt);
        ui.setDraw(fishing.phase == "reel" ? Math.Max(0.02, fishing.progress) : 0);
    }

    /// <summary>The fish is in the bucket: it is paid for, and the first of each kind goes to the aquarium.</summary>
    private void onFishLanded(SeaSpecies s)
    {
        state.landFish(s);
        discover("fish_first");
        if (state.addToAquarium(s.id))
        {
            audio.discover();
            ui.toast($"🎣 New for the aquarium: {s.name}");
        }
        else
        {
            audio.coin();
            ui.toast($"🎣 {s.name}");
        }
    }

    /// <summary>Shows in the tank at camp what has been caught alive so far.</summary>
    private void refreshAquarium()
    {
        var have = state.aquarium;
        var key = string.Join(",", have);
        if (key == tankKey) return;
        tankKey = key;
        aquarium.setCaught(have);
        under.setGoldCaught(have.Contains("goldfish"));
        if (have.Count > 0) discover("aquarium_first");
    }

    private void selectSlot(int n)
    {
        if (!started && n != 1) return;
        if (seated) return;
        if (alt)
        {
            if (n == 3) setAltCamera(!altCamera);
            else if (diving && n == 1) setAltTool(0);
            else if (diving && n == 2) setAltTool(altTool == 11 ? 0 : 11);
            else ui.toast(underground ? "There is nothing to dig or shoot down here. Only the camera works." : "Down here you have the harpoon, the net and the camera.");
            return;
        }
        if (n == 8 && !state.data.ownsBucket)
        {
            ui.toast("You do not have a bucket yet. The camper shop sells one.");
            return;
        }
        if (n == 7 && state.data.flags <= 0)
        {
            ui.toast("You have no marker flags. The camper shop sells packs of 5.");
            return;
        }
        if (n == 6 && !state.data.ownsBow)
        {
            ui.toast("You do not own a bow yet. The camper shop sells one.");
            return;
        }
        if (n == 4 && !state.data.ownsGun)
        {
            ui.toast("You do not own a gun yet. The camper shop sells a toy pistol.");
            return;
        }
        if (vehicle == "boat" && (n == 1 || n == 2 || n == 7 || n == 8))
        {
            ui.toast("Out on the boat you can only use the binoculars, the camera or a weapon.");
            return;
        }
        if (gameState == "INSPECT" || transitioning) return;
        if (gameState == "CAMP") closeCamp();
        tool = n;
        setBinocView(false);
        view.setTool(n);
        ui.setTool(n);
        detector.setActive(n == 1);
        dig.setActive(n == 2);
        audio.ui();
    }

    // ---------------------------------------------------------------- photos

    /// <summary>Asks for a photo of the next frame (see <see cref="capturePhoto"/>).</summary>
    private void takePhoto()
    {
        double now = JS.Now();
        if (now - lastPhoto < 700 || photoPending) return;
        if (state.photos.Count >= Photos.MAX_PHOTOS)
        {
            audio.error();
            ui.toast("Your album is full. Throw a few photos away at the photo board in camp.");
            return;
        }
        lastPhoto = now;
        photoPending = true;
    }

    /// <summary>Takes the photo: this frame's view of the world, without the hands or the page on top.</summary>
    private void capturePhoto()
    {
        photoPending = false;
        if (!(camView && holdingCamera && gameState == "EXPLORE")) return;
        // underwater the page lays a soft blue over the whole view; the photo gets the same, so it shows what the diver saw
        var tex = diving
            ? renderer.CapturePhoto(camera, 900, new UnityEngine.Color32(15, 85, 125, 255), 0.24f)
            : renderer.CapturePhoto(camera);
        string id = tex != null ? Photos.save(tex) : null;
        if (id == null)
        {
            audio.error();
            ui.toast("The camera jammed. Try again.");
            return;
        }
        audio.shutter();
        ui.photoFlash();
        dog.photographed();
        state.addPhoto(id, photoCaption());
        discover("photo_first");
    }

    /// <summary>Written under each photo: the day, the time of day and where it was taken.</summary>
    private string photoCaption()
    {
        double t = dayTime;
        string when = rain.amount > 0.4 ? "a rainy day"
            : t < 0.08 || t > 0.94 ? "dawn"
            : t < 0.2 ? "morning"
            : t < 0.42 ? "midday"
            : t < 0.5 ? "afternoon"
            : t < 0.64 ? "sunset"
            : "night";
        if (underground) return $"Day {state.data.day} · under the dunes";
        string where = diving ? "under the sea"
            : onIsland ? (islandAt(player.x, player.z).kind == 1 ? "the lagoon cay" : islandAt(player.x, player.z).kind == 2 ? "the ember rock" : "the island")
            : vehicle == "boat" ? "out at sea"
            : JS.Hypot(player.x, player.z - CAMP_Z) < 16 ? "camp"
            : "the beach";
        return $"Day {state.data.day} · {when} · {where}";
    }

    /// <summary>Puts the pinned photos on the camp's photo board, oldest first, when they have changed.</summary>
    private void refreshPhotoBoard()
    {
        var pinned = state.photos.Where(p => p.pinned).Take(Photos.BOARD_SLOTS).ToList();
        var key = string.Join(",", pinned.Select(p => p.id));
        if (key == boardKey) return;
        boardKey = key;
        camp.setPhotos(pinned.Select(p => (UnityEngine.Texture)Photos.get(p.id)).Where(t => t != null).ToList());
    }

    // ---------------------------------------------------------------- shooting

    private void shoot()
    {
        double now = JS.Now();
        if (now - shotCooldown < 260) return;
        shotCooldown = now;
        view.fire();
        audio.shot();
        discover("gun_first");
        // the bullet leaves the muzzle and flies toward whatever the crosshair is on
        var ray = input.ray(camera, true);
        var beachHit = beach.raycast(ray.ray.origin, ray.ray.direction);
        double aimDist = Math.Min(beachHit != null ? beachHit.distanceTo(ray.ray.origin) : 60, 60);
        var aim = ray.ray.origin.clone().addScaledVector(ray.ray.direction, aimDist);
        camera.updateMatrixWorld(true);
        var muzzle = view.muzzle.getWorldPosition(new THREE.Vector3());
        bullets.spawn(muzzle, aim.sub(muzzle).normalize());
    }

    /// <summary>A bullet or arrow strikes a sandcastle: chunks fly off, and enough hits bring it down.</summary>
    private void hitCastle(THREE.Object3D obj, THREE.Vector3 at, double power)
    {
        var c = castles.castleOf(obj);
        if (c == null) return;
        var p = at ?? new THREE.Vector3(c.x, beach.heightAt(c.x, c.z) + 0.5, c.z);
        dig.particles.burst(p, 18, 1.8, 1.8, 0.8, new THREE.Color(0xdcc38f));
        audio.thud();
        discover("castle_shot");
        if (castles.hit(c, at, power))
        {
            dig.particles.burst(new THREE.Vector3(c.x, beach.heightAt(c.x, c.z) + 0.4, c.z), 34, 2.2, 2, 1, new THREE.Color(0xdcc38f));
            castles.remove(c);
            audio.crumble();
        }
    }

    private void onBulletHit(THREE.Object3D obj, THREE.Vector3 impact = null, double power = 0.14)
    {
        if (castles.castleOf(obj) != null)
        {
            hitCastle(obj, impact, power);
            return;
        }
        if (obj.userData.Flag("shark") || obj.userData.Flag("fish"))
        {
            var res = sea.hit(obj);
            if (res == null) return;
            audio.splash(5);
            if (res.kind == "fish")
            {
                state.addCoins(FISH_REWARD);
                discover("fish_caught");
                ui.toast($"Caught a fish! +{JS.Str(FISH_REWARD)}g");
            }
            else if (res.killed)
            {
                state.addCoins(SHARK_REWARD);
                discover("shark_shot");
                ui.toast($"The shark is down! +{JS.Str(SHARK_REWARD)}g");
            }
            else
            {
                ui.toast("A hit! The shark bolts. One more shot should do it.");
            }
            return;
        }
        if (obj.userData.Flag("coconut"))
        {
            var atNut = scenery.shootNut(obj);
            if (atNut != null)
            {
                dig.particles.burst(atNut, 18, 1.6, 1.4, 0.7, new THREE.Color(0xc9b28a));
                audio.splat();
                state.addCoins(COCONUT_REWARD);
                discover("coconut_shot");
                ui.toast($"Coconut down! +{JS.Str(COCONUT_REWARD)}g");
            }
            return;
        }
        // a gull's hit sphere carries the gull itself (Birds.blank); the port looked for a "bird" mark that nothing sets, so no
        // shot at a gull ever counted
        if (obj.userData.Flag("gull"))
        {
            var atBird = birds.shoot(obj);
            if (atBird != null)
            {
                dig.particles.burst(atBird, 40, 2.4, 1.2, 1.6, new THREE.Color(0xf6f4ee));
                audio.gull(4);
                double reward = birds.lastShotAirborne ? BIRD_AIR_REWARD : BIRD_REWARD;
                state.addCoins(reward);
                discover("bird_shot");
                ui.toast($"Poof! A flurry of feathers. +{JS.Str(reward)}g");
            }
            return;
        }
        var at = ecology.shoot(obj);
        if (at == null) return;
        at.y += 0.15;
        dig.particles.burst(at, 28, 2.2, 3, 0.9, new THREE.Color(0xe8704f));
        audio.splat();
        crabsShot++;
        state.addCoins(CRAB_REWARD);
        discover("crab_shot");
        ui.toast($"Got one! +{JS.Str(CRAB_REWARD)}g ({crabsShot})");
    }

    // ---------------------------------------------------------------- digging

    private THREE.Vector3 toolHit = null;

    private void tryDig(THREE.Vector3 hit)
    {
        if (onIsland)
        {
            audio.error();
            ui.toast(noDigHint());
            return;
        }
        if (!beach.canDig(hit.z))
        {
            ui.toast("Leave the dunes in peace. Dig down on the open beach.");
            audio.error();
            return;
        }
        if (state.data.warmth <= 0)
        {
            ui.toast("You are too chilly to dig. Drink some coffee at the campfire to warm up.");
            audio.error();
            return;
        }
        if (grate != null && JS.Hypot(hit.x - grate.x, hit.z - grate.z) < GRATE_R)
        {
            ui.toast("The old stone grate is set firm in the sand. There is no digging under it.");
            audio.error();
            return;
        }
        dig.dig(hit.x, hit.z, beach, state);
        tidyGrateGround();
        discover("dig_first");
        if (!holes.Any(h => JS.Hypot(h[0] - hit.x, h[1] - hit.z) < 0.9)) holes.Add(new[] { hit.x, hit.z });
        view.doSwing();
        audio.crunch();
        var fresh = spawner.checkReveals();
        grass.trimDug();
        foreach (var it in fresh)
        {
            dig.particles.burst(new THREE.Vector3(it.x, beach.heightAt(it.x, it.z) + 0.2, it.z), 24, 1.6, 3.4, 1, new THREE.Color(0xf3e2b8));
            audio.coin();
            ui.toast("Something is in the hole. Click it!");
        }
        double max = state.maxWarmth;
        if (!chillWarned && state.data.warmth / max < 0.25)
        {
            chillWarned = true;
            ui.toast("Getting chilly… a warm drink awaits at camp.");
        }
        if (state.data.warmth / max > 0.6) chillWarned = false;
    }

    // ---------------------------------------------------------------- inspect

    /// <summary>What using a find does: most are picked up, but the captain's hatch has to be opened first.</summary>
    private void useFind(BuriedItem it)
    {
        if (it.def.id == "captain_chest")
        {
            if (!it.opened)
            {
                spawner.openHatch(it);
                audio.thud();
                discover("captain_hatch");
                dig.particles.burst(new THREE.Vector3(it.x, beach.heightAt(it.x, it.z) + 0.1, it.z), 14, 1, 1.4, 0.6, new THREE.Color(0xe6ccb2));
            }
            else
            {
                enterUnderground();
            }
            return;
        }
        enterInspect(it);
    }

    private void enterInspect(BuriedItem item)
    {
        setBinocView(false);
        transitioning = true;
        document.exitPointerLock();
        ui.fade(() =>
        {
            inspectItem = item;
            gameState = "INSPECT";
            station.enter(item, state.brushPx);
            ui.showInspect(item.def.name);
            ui.setProgress(station.progress);
            ui.setMode("INSPECT");
            audio.setOceanLevel(0.02);
            detector.setActive(false);
            dig.setActive(false);
            ui.setHint(null);
            window.setTimeout(() => transitioning = false, 350);
        });
    }

    private void onCleaned()
    {
        var item = inspectItem;
        if (item == null) return;
        var def = item.def;
        audio.discover();
        if (def.id == "message_bottle")
        {
            // the bottle itself is not kept: what matters is the map rolled up inside
            spawner.markCollected(item);
            takeBottleMap();
            ui.showResult(def.name, def.lore, "A map is tucked inside. Open the map (M) and press T to read it.", "Message in a bottle");
            return;
        }
        var res = state.addFound(def.id);
        var stored = res.item;
        if (ISLAND_ITEM_IDS.Contains(def.id)) discover("island_find");
        if (DIVE_ITEM_IDS.Contains(def.id)) discover("dive_find");
        if (def.id == "treasure_cache") discover("x_marks");
        discover(def.category == "trash" ? "find_trash" : def.category == "coin" ? "find_coin" : def.category == "jewelry" ? "find_jewel" : def.category == "shard" ? "find_shard" : "quest_first");
        spawner.markCollected(item);
        string worth;
        if (def.category == "shard" && def.shardSet != null)
        {
            int n = state.data.shards.TryGetValue(def.shardSet, out var shList) && shList != null ? shList.Count : 0;
            var set = SHARD_SETS.First(s => s.id == def.shardSet);
            worth = $"{n}/3 shards of the {set.name}";
        }
        else if (res.quest != null)
        {
            worth = res.quest.reward > 0
                ? $"Delivered to {res.quest.giver}: +{JS.Str(res.quest.reward)}g"
                : $"{res.quest.progress}/{res.quest.need} found for {res.quest.giver}";
            if (res.quest.reward > 0)
            {
                audio.coin();
                sendLetter(res.quest.id);
            }
        }
        else
        {
            string bonus = state.tier("cleaning") >= 2 ? "  (+25% brush bonus)" : "";
            worth = $"Worth {JS.Str(stored != null ? stored.value : def.value)}g{bonus}";
        }
        ui.showResult(def.name, def.lore, worth, CATEGORY_LABEL[def.category]);
    }

    private void exitInspect()
    {
        if (gameState != "INSPECT" || transitioning) return;
        transitioning = true;
        ui.fade(() =>
        {
            station.exit();
            inspectItem = null;
            gameState = "EXPLORE";
            ui.hideInspect();
            ui.setMode("EXPLORE");
            audio.setOceanLevel(diving ? 0.015 : 0.06);
            autoSave();
            if (diving) applyDiveHands();
            else
            {
                ui.setTool(tool);
                view.setTool(tool);
                detector.setActive(tool == 1);
                dig.setActive(tool == 2);
            }
            window.setTimeout(() => transitioning = false, 350);
            grabMouse();
        });
    }

    // ---------------------------------------------------------------- camp

    private void useCampObject(string kind)
    {
        var tabs = new Dictionary<string, string>
        {
            { "fire", "rest" }, { "bin", "recycle" }, { "board", "board" }, { "bench", "sell" },
            { "lantern", null }, { "camper", "upgrades" }, { "radio", null }, { "coffee", null },
            { "book", null }, { "post", null }, { "hammock", null }, { "sign", null }, { "photos", "photos" }, { "aquarium", "aquarium" },
        };
        if (kind == "coffee")
        {
            drinkCoffee();
            return;
        }
        if (kind == "book")
        {
            openBook();
            return;
        }
        if (kind == "post")
        {
            openMail();
            return;
        }
        if (kind == "hammock")
        {
            enterHammock();
            return;
        }
        if (kind == "sign")
        {
            state.cycleSign();
            audio.ui();
            return;
        }
        if (kind == "radio")
        {
            bool on = !audio.radioOn;
            audio.setRadio(on);
            camp.setRadio(on);
            if (on) discover("radio_on");
            audio.ui();
            ui.toast(on ? $"📻 {audio.trackName}" : "Radio off.");
            return;
        }
        if (tabs.TryGetValue(kind, out var tab) && tab != null)
        {
            openCamp(tab, false);
            return;
        }
        // the lantern is just a light: on or off
        lanternForce = !lanternOn;
        audio.ui();
    }

    /// <summary>walk: stroll over to the fire first (hotkey 3); clicking a camp prop opens it where you stand</summary>
    private void openCamp(string tab, bool walk = true)
    {
        if (transitioning) return;
        setBinocView(false);
        document.exitPointerLock();
        if (gameState == "EXPLORE")
        {
            if (walk && JS.Hypot(player.x, player.z - CAMP_Z) > 6)
            {
                walkTo = new PlayerPose { x = 0.5, z = CAMP_Z + 5, yaw = 0, pitch = -0.2 };
            }
            gameState = "CAMP";
            detector.setActive(false);
            dig.setActive(false);
            view.setTool(0);
            ui.setTool(0);
            ui.setMode("CAMP");
        }
        audio.ui();
        campModal.open(tab);
    }

    private void closeCamp()
    {
        campModal.close();
        if (gameState != "CAMP") return;
        autoSave();
        walkTo = null;
        gameState = "EXPLORE";
        ui.setMode("EXPLORE");
        ui.setTool(tool);
        view.setTool(tool);
        detector.setActive(tool == 1);
        dig.setActive(tool == 2);
        grabMouse();
    }

    private void cycleLighting()
    {
        if (!state.lanternUnlocked) return;
        int idx = Array.IndexOf(LIGHT_ORDER, state.data.lighting);
        string next = LIGHT_ORDER[(idx + 1) % LIGHT_ORDER.Length];
        state.setLighting(next);
        audio.ui();
    }

    /// <summary>Advances the clock (or eases to a frozen time of day) and lights the world to match.</summary>
    private void updateDayNight(double dt)
    {
        string mode = state.data.lighting;
        bool sleeping = hammock != null;
        if (mode == "cycle" || sleeping)
        {
            double next = dayTime + dt * (sleeping ? hammockSpeed : 1) / DAY_SECONDS;
            if (next >= 1)
            {
                // a new morning
                state.data.day++;
                state.emit();
                ui.toast($"A new day begins. Day {state.data.day}");
            }
            dayTime = next % 1;
        }
        else
        {
            double target = MOOD_TIME.TryGetValue(mode, out var mt) ? mt : 0.28;
            double diff = target - dayTime;
            diff -= JS.Round(diff);
            dayTime = (dayTime + diff * Math.Min(1, dt * 1.5) + 1) % 1;
        }
        // rainy days: a spell of rain on about one day in three, only while the day cycle runs (Rain.rainWindow)
        var win = rainWindow(state.data.day);
        bool raining = mode == "rain" || (mode == "cycle" && win != null && dayTime > win.start && dayTime < win.end);
        rain.update(dt, raining ? 1 : 0, camera.position);
        if (raining != wasRaining)
        {
            wasRaining = raining;
            if (started) ui.toast(raining ? "Clouds roll in... it begins to rain." : "The rain eases off.");
            if (raining) discover("rain_first");
        }
        sky.rain = rain.amount;
        if (sky.isDark) discover("night_first");
        audio.setRain(rain.amount);
        sky.focus.set(player.x, 0, player.z);
        sky.setTime(dayTime);
        ocean.setNight(sky.starAmount);
        double t = dayTime;
        ui.setClock(rain.amount > 0.4 ? "🌧️" : t < 0.08 || t > 0.94 ? "🌅" : t < 0.5 ? "☀️" : t < 0.64 ? "🌇" : "🌙", state.data.day);
    }

    /// <summary>null: lit by itself after dark; otherwise the player's choice</summary>
    private bool? lanternForce = null;

    private bool lanternOn => lanternForce ?? sky.isDark;

    // ---------------------------------------------------------------- controller

    private void setUsingPad(bool on)
    {
        if (on == usingPad) return;
        usingPad = on;
        document.body.classList.toggle("usingpad", on);
        ui.setPadMode(on);
        if (on) document.exitPointerLock();
        else mapCursorEl?.remove();
    }

    /// <summary>Holds a virtual key for the controller, without disturbing a key the keyboard is already holding.</summary>
    private void setPadKey(string code, bool on)
    {
        var keys = input.keys;
        if (on)
        {
            if (!keys.Contains(code))
            {
                keys.Add(code);
                padKeys.Add(code);
            }
        }
        else if (padKeys.Contains(code))
        {
            keys.Remove(code);
            padKeys.Remove(code);
        }
    }

    private void padKey(string code)
    {
        padCalling = true;
        try
        {
            input.handlers.onKey?.Invoke(code);
        }
        finally
        {
            padCalling = false;
        }
    }

    /// <summary>The menu or card the controller should drive right now, if any.</summary>
    private HTMLElement overlayRoot()
    {
        var list = document.querySelectorAll<HTMLElement>(".mainmenu, .pause:not(.bigmap), .camp:not(.hidden), .intro:not(.gone)");
        return list.Count > 0 ? list[list.Count - 1] : null;
    }

    /// <summary>A dropdown cannot be opened from code, so a controller gets its choices as a short list of buttons under the row.</summary>
    private void openPadOptions(HTMLSelectElement sel)
    {
        var list = document.createElement("div");
        list.className = "set-options";
        for (int idx = 0; idx < sel.options.Count; idx++)
        {
            int i = idx;
            var o = sel.options[i];
            var b = document.createElement<HTMLButtonElement>("button");
            b.className = i == sel.selectedIndex ? "set-option on" : "set-option";
            b.textContent = o.text ?? o.textContent;
            b.onclick = () =>
            {
                bool changed = i != sel.selectedIndex;
                sel.selectedIndex = i;
                closePadOptions(list);
                if (changed) sel.onchange?.Invoke(new Event { type = "change", target = sel });
            };
            list.append(b);
        }
        padSelect = sel;
        var row = sel.closest(".set-row") ?? sel;
        var parent = row.parentElement;
        if (parent != null)
        {
            var next = row.nextElementSibling;
            if (next != null) parent.insertBefore(list, next);
            else parent.append(list);
        }
        list.querySelector<HTMLElement>(".on")?.focus();
        list.scrollIntoView();
        audio.ui();
    }

    private void closePadOptions(HTMLElement list)
    {
        list.remove();
        padSelect?.focus();
        padSelect = null;
        audio.ui();
    }

    private List<HTMLElement> padFocusables(HTMLElement root)
    {
        // while a list of choices is open, only its choices can be reached
        var scope = root.querySelector<HTMLElement>(".set-options") ?? root;
        return scope.querySelectorAll<HTMLElement>("button, input, select").Where(e =>
        {
            if (e is HTMLButtonElement btn && btn.disabled) return false;
            if (e is HTMLInputElement inp && inp.disabled) return false;
            if (e is HTMLSelectElement sel && sel.disabled) return false;
            // a slider inside a settings row: the row is what is chosen
            if (e.classList.contains("nofocus")) return false;
            // a text box is for the mouse and the keyboard: walked onto, it would swallow every key after
            if (e is HTMLInputElement text && text.type == "text") return false;
            var r = e.getBoundingClientRect();
            return r.width > 0 && r.height > 0;
        }).ToList();
    }

    /// <summary>The part of a menu that scrolls, if it is long enough to need it.</summary>
    private HTMLElement padScroller(HTMLElement root)
    {
        var all = root.querySelectorAll<HTMLElement>(".camp-body, .help-body, .ach-list, .set-rows, .save-list");
        return all.FirstOrDefault(e => e.scrollHeight > e.clientHeight + 1);
    }

    private void padNav(string d)
    {
        var root = overlayRoot();
        if (root == null) return;
        var items = padFocusables(root);
        if (items.Count == 0) return;
        var cur = items.FirstOrDefault(e => e == document.activeElement);
        if (cur == null)
        {
            items[0].focus();
            return;
        }
        // from outside a scrolled list, up first rolls the list back before leaving for what is above it
        var scroller = padScroller(root);
        if (scroller != null && d == "up" && scroller.scrollTop > 0 && !scroller.contains(cur))
        {
            scroller.scrollTop = Math.Max(0, scroller.scrollTop - 150);
            return;
        }
        // a setting: left and right change it
        if (cur.onstep != null && (d == "left" || d == "right"))
        {
            cur.onstep(d == "left" ? -1 : 1);
            audio.ui();
            return;
        }
        if (cur is HTMLInputElement rangeInp && rangeInp.type == "range" && (d == "left" || d == "right"))
        {
            double stepVal = double.TryParse(rangeInp.step, NumberStyles.Float, CultureInfo.InvariantCulture, out var st) && st > 0 ? st : 1;
            double minVal = double.TryParse(rangeInp.min, NumberStyles.Float, CultureInfo.InvariantCulture, out var mn) ? mn : 0;
            double maxVal = double.TryParse(rangeInp.max, NumberStyles.Float, CultureInfo.InvariantCulture, out var mx) ? mx : 100;
            double curVal = double.TryParse(rangeInp.value, NumberStyles.Float, CultureInfo.InvariantCulture, out var cv) ? cv : minVal;
            double nextVal = Math.Max(minVal, Math.Min(maxVal, curVal + (d == "left" ? -stepVal : stepVal)));
            rangeInp.value = JS.Str(nextVal);
            rangeInp.oninput?.Invoke(new Event { type = "input", target = rangeInp });
            return;
        }
        var c = cur.getBoundingClientRect();
        double cx = c.left + c.width / 2;
        double cy = c.top + c.height / 2;
        bool vertical = d == "up" || d == "down";
        var options = new List<(HTMLElement el, double along, double across)>();
        foreach (var it in items)
        {
            if (it == cur) continue;
            var r = it.getBoundingClientRect();
            double dx = r.left + r.width / 2 - cx;
            double dy = r.top + r.height / 2 - cy;
            double along = d == "up" ? -dy : d == "down" ? dy : d == "left" ? -dx : dx;
            if (along > 2) options.Add((it, along, vertical ? Math.Abs(dx) : Math.Abs(dy)));
        }
        HTMLElement best = null;
        double bestScore = double.PositiveInfinity;
        // up and down go to the very next row, whatever is in it: a small checkbox off to one side must not be skipped for a wide slider further on
        double nearest = options.Count > 0 ? options.Min(o => o.along) : 0;
        foreach (var o in options)
        {
            if (vertical && o.along > nearest + 14) continue;
            double score = vertical ? o.across : o.along + o.across * 2.2;
            if (score < bestScore)
            {
                bestScore = score;
                best = o.el;
            }
        }
        if (best != null)
        {
            best.focus();
            best.scrollIntoView();
            audio.ui();
        }
        else if (scroller != null && (d == "up" || d == "down"))
        {
            // nothing further to move to: a page of plain text, like the notes on the board, just scrolls
            scroller.scrollTop = Math.Max(0, scroller.scrollTop + (d == "down" ? 150 : -150));
        }
    }

    private static readonly Regex BACK_RE = new Regex(@"^\W*(done|back|close|cancel|resume|leave)", RegexOptions.IgnoreCase);

    /// <summary>The button that leaves a menu (Back, Done, Close, Cancel, Resume), if it has one.</summary>
    private HTMLButtonElement backButton(HTMLElement root)
    {
        var back = root.querySelectorAll<HTMLButtonElement>("button").FirstOrDefault(b => BACK_RE.IsMatch(b.textContent ?? ""));
        return back != null && !back.disabled ? back : null;
    }

    private void padBack(HTMLElement root)
    {
        var back = backButton(root);
        if (back != null) back.click();
        else padKey("Escape");
    }

    /// <summary>Presses the chosen button of a menu (A on a controller, Enter or Space on the keyboard).</summary>
    private void menuAccept(HTMLElement root)
    {
        var cur = document.activeElement;
        if (cur is HTMLSelectElement sel) openPadOptions(sel);
        else if (cur != null && root.contains(cur) && cur.tagName != "BODY") cur.click();
        else
        {
            var f = padFocusables(root);
            if (f.Count > 0) f[0].focus();
        }
    }

    /// <summary>The cards that open over the main menu or the pause screen and close by themselves.</summary>
    private static bool isDialog(HTMLElement root)
    {
        var c = root.classList;
        return c.contains("saves") || c.contains("helpscreen") || c.contains("achievements") || c.contains("credits") || c.contains("farewell");
    }

    /// <summary>
    /// The keyboard in a menu: the arrows or W A S D move the highlight, Enter or Space presses it, Q and E turn the pages of
    /// the camp and of Help, Esc closes a card. True when the key was used up here.
    /// </summary>
    private bool menuKey(string code)
    {
        var root = overlayRoot();
        if (root == null) return false;
        string dir = code == "ArrowUp" || code == "KeyW" ? "up"
            : code == "ArrowDown" || code == "KeyS" ? "down"
            : code == "ArrowLeft" || code == "KeyA" ? "left"
            : code == "ArrowRight" || code == "KeyD" ? "right" : null;
        if (dir != null)
        {
            // left and right turn the pages of the journal and of letters (onKey)
            if (bookOpen && (dir == "left" || dir == "right")) return false;
            padNav(dir);
            return true;
        }
        if (code == "Enter" || code == "NumpadEnter" || code == "Space")
        {
            menuAccept(root);
            return true;
        }
        if (code == "KeyQ" || code == "KeyE")
        {
            int step = code == "KeyE" ? 1 : -1;
            if (campModal.isOpen && root.classList.contains("camp")) campModal.stepTab(step);
            else if (ui.helpStep(step)) audio.ui();
            return true;
        }
        if (code == "Escape")
        {
            // the pause screen, the camp and the journal keep their own Esc (onKey)
            if (started && !isDialog(root)) return false;
            backButton(root)?.click();
            return true;
        }
        return false;
    }

    /// <summary>The button last chosen in each open menu, so that closing a card returns to the button that opened it.</summary>
    private readonly Dictionary<HTMLElement, HTMLElement> chosenIn = new Dictionary<HTMLElement, HTMLElement>();

    /// <summary>
    /// Every frame: an open menu always has one button chosen, whatever the player holds, and the line along the bottom of
    /// the screen says what the keys or the controller's buttons do there.
    /// </summary>
    private void menuTick()
    {
        var root = overlayRoot();
        if (root == null)
        {
            chosenIn.Clear();
            if (mapOpen)
            {
                var keys = new List<Glyphs.Prompt>
                {
                    new Glyphs.Prompt("Move", new[] { "LS" }, new string[0]),
                    new Glyphs.Prompt("Mark a place", new[] { "A" }, new[] { "Click" }),
                };
                if (ui.mapHasTabs) keys.Add(new Glyphs.Prompt("Other map", new[] { "LB", "RB" }, new[] { "T" }));
                keys.Add(new Glyphs.Prompt("Close", new[] { "B" }, new[] { "Esc" }));
                ui.setMenuBar(keys);
            }
            else ui.setMenuBar(null);
            return;
        }
        var items = padFocusables(root);
        var cur = document.activeElement;
        if (cur != null && items.Contains(cur)) chosenIn[root] = cur;
        else if (items.Count > 0 && !DomHost.IsTyping())
        {
            // back from a card that was opened over this menu: the button that opened it is chosen again. Otherwise a menu
            // opens on the button it marks (Resume, Cancel), the camp on its current page, anything else on its first button
            HTMLElement want = chosenIn.TryGetValue(root, out var last) && items.Contains(last) ? last : null;
            want ??= root.querySelector<HTMLElement>(".default") ?? root.querySelector<HTMLElement>(".tab.on");
            if (want == null || !items.Contains(want)) want = items[0];
            want.focus();
            cur = want;
            if (chosenIn.Count > 12)
                foreach (var gone in chosenIn.Keys.Where(k => !k.isConnected).ToList()) chosenIn.Remove(gone);
        }
        bool camp = root.classList.contains("camp");
        var list = new List<Glyphs.Prompt>();
        if (camp) list.Add(new Glyphs.Prompt("Page", new[] { "LB", "RB" }, new[] { "Q", "E" }));
        else if (root.classList.contains("helpscreen")) list.Add(new Glyphs.Prompt("Topic", new[] { "LB", "RB" }, new[] { "Q", "E" }));
        else if (bookOpen && root.classList.contains("bookview") && !root.classList.contains("teaser"))
            list.Add(new Glyphs.Prompt("Turn the page", new[] { "LB", "RB" }, new[] { "A", "D" }));
        if (cur != null && cur.onstep != null) list.Add(new Glyphs.Prompt("Change", new[] { "D-pad left", "D-pad right" }, new[] { "A", "D" }));
        if (padScroller(root) != null) list.Add(new Glyphs.Prompt("Scroll", new[] { "RS" }, new[] { "Scroll" }));
        if (items.Count > 0) list.Add(new Glyphs.Prompt("Select", new[] { "A" }, new[] { "Enter" }));
        var back = backButton(root);
        string leave = back != null ? (back.textContent ?? "").Trim() : started ? "Close" : null;
        if (leave != null) list.Add(new Glyphs.Prompt(leave.Length <= 10 ? leave : "Back", new[] { "B" }, new[] { "Esc" }));
        ui.setMenuBar(list, camp);
    }

    private void padButton(string b, bool down)
    {
        setUsingPad(true);

        if (hammock != null)
        {
            if (down) leaveHammock(false);
            return;
        }
        var root = overlayRoot();
        if (root != null)
        {
            if (!down) return;
            if (b == "a") menuAccept(root);
            else if (b == "b")
            {
                var open = root.querySelector<HTMLElement>(".set-options");
                if (open != null) closePadOptions(open);
                else padBack(root);
            }
            else if ((b == "lb" || b == "rb") && campModal.isOpen) campModal.stepTab(b == "rb" ? 1 : -1);
            else if ((b == "lb" || b == "rb") && ui.helpStep(b == "rb" ? 1 : -1)) audio.ui();
            else if (b == "lb" && bookOpen) ui.bookTurn(-1);
            else if (b == "rb" && bookOpen) ui.bookTurn(1);
            else if (b == "start" && paused) padKey("Escape");
            return;
        }
        if (mapOpen)
        {
            if (!down) return;
            if (b == "lb" || b == "rb") ui.toggleMap();
            else if (b == "b" || b == "y" || b == "start") padKey("Escape");
            else if (b == "a")
            {
                pickDestination(mapCursor.u, mapCursor.v);
                ui.repaintMap();
            }
            return;
        }
        if (gameState == "INSPECT")
        {
            if (!down) return;
            if (b == "a" || b == "start")
            {
                if (ui.isResultOpen()) padKey("Space");
                else if (b == "start") padKey("Escape");
            }
            else if (b == "b") padKey("Escape");
            else if (b == "y")
            {
                var btn = document.querySelectorAll<HTMLButtonElement>("button:not(.hidden)").FirstOrDefault(x => x.textContent == "Instant Clean");
                btn?.click();
            }
            return;
        }
        if (!started) return;
        if (b == "rt" || b == "lt")
        {
            if (driving || paused) return;
            int btn = b == "rt" ? 0 : 2;
            padCalling = true;
            try
            {
                if (down) input.handlers.onPointerDown?.Invoke(btn);
                else
                {
                    input.handlers.onPointerUp?.Invoke(btn, false);
                    input.handlers.onClick?.Invoke(btn);
                }
            }
            finally
            {
                padCalling = false;
            }
            return;
        }
        if (underground)
        {
            if (down && b == "a") padKey("KeyF");
            else if (down && b == "left") setAltCamera(!altCamera);
            else if (down && b == "start") padKey("Escape");
            else if (down && b == "back") padKey("KeyN");
            return;
        }
        if (diving)
        {
            // swimming: the bumpers go up and down, the rest keep their usual jobs
            if (b == "rb") lift.up = down;
            else if (b == "lb") lift.down = down;
            else if (down && b == "a") padKey("KeyF");
            else if (down && b == "x") padKey("KeyQ");
            else if (down && b == "left") cycleAltTool(1);
            else if (down && b == "start") padKey("Escape");
            else if (down && b == "back") padKey("KeyN");
            return;
        }
        if (!down) return;
        switch (b)
        {
            case "a": padKey("KeyF"); break;
            case "x": padKey("KeyE"); break;
            case "y": padKey("KeyM"); break;
            case "start": padKey("Escape"); break;
            case "back": padKey("KeyN"); break;
            case "r3": if (!driving) padKey("Space"); break;
            case "lb": onWheel(-1); break;
            case "rb": onWheel(1); break;
            case "up": padKey("Digit1"); break;
            case "right": padKey("Digit2"); break;
            case "down": padKey("Digit5"); break;
            case "left": padOtherTool(); break;
        }
    }

    /// <summary>The controller lets go of the cleaning brush: the mouse has the pointer again and the trigger no longer counts as held.</summary>
    private void releasePadBrush()
    {
        if (!padBrush) return;
        padBrush = false;
        input.padPointer = false;
        input.buttons &= ~1;
        station.brushScale = 1;
    }

    /// <summary>Per-frame controller work: menu focus, looking, driving and the map cursor.</summary>
    private void padUpdate(double dt)
    {
        if (!pad.connected || !usingPad)
        {
            foreach (var c in padKeys.ToArray()) setPadKey(c, false);
            releasePadBrush();
            return;
        }
        var root = overlayRoot();
        if (root != null)
        {
            foreach (var c in padKeys.ToArray()) setPadKey(c, false);
            // (menuTick sees to it that a button is chosen) the right stick scrolls whatever long list is showing
            if (pad.look.y != 0)
            {
                var sc = padScroller(root);
                if (sc != null) sc.scrollTop = Math.Max(0, sc.scrollTop + pad.look.y * dt * 700);
            }
            return;
        }
        if (mapOpen)
        {
            var c = document.querySelector<HTMLCanvasElement>(".bigmap-canvas");
            if (c != null)
            {
                mapCursor.u = Math.Min(1, Math.Max(0, mapCursor.u + pad.move.x * dt * 0.7));
                mapCursor.v = Math.Min(1, Math.Max(0, mapCursor.v + pad.move.y * dt * 0.7));
                if (mapCursorEl == null)
                {
                    mapCursorEl = document.createElement("div");
                    mapCursorEl.className = "map-cursor";
                    document.body.append(mapCursorEl);
                }
                var r = c.getBoundingClientRect();
                mapCursorEl.style.left = $"{JS.Str(r.left + mapCursor.u * r.width)}px";
                mapCursorEl.style.top = $"{JS.Str(r.top + mapCursor.v * r.height)}px";
            }
            return;
        }
        if (mapCursorEl != null)
        {
            mapCursorEl.remove();
            mapCursorEl = null;
        }
        if (gameState == "INSPECT")
        {
            // left stick turns the find, right stick moves the brush, RT scrubs
            var m = pad.move;
            var l = pad.look;
            if (!padBrush)
            {
                padBrush = true;
                input.padPointer = true;
                input.pointer.set(0, 0);
                station.brushScale = 1.7;
            }
            station.rotate(m.x * dt * 2.2, m.y * dt * 1.8);
            // the same speed on screen both ways, and never off the find
            double aspect = window.innerWidth / Math.Max(1, window.innerHeight);
            input.pointer.x = Math.Max(-1, Math.Min(1, input.pointer.x + l.x * dt * 1.6 / aspect));
            input.pointer.y = Math.Max(-1, Math.Min(1, input.pointer.y - l.y * dt * 1.6));
            if (l.x != 0 || l.y != 0) station.keepOnItem(input.pointer);
            if (pad.rt > 0.3)
            {
                input.buttons |= 1;
                // holding the trigger is enough: a stick is not a mouse, and it can rest while the find turns under the brush
                input.speed = JS.Max(input.speed, 120, JS.Hypot(l.x, l.y) * 500, JS.Hypot(m.x, m.y) * 500);
            }
            else input.buttons &= ~1;
            return;
        }
        releasePadBrush();
        foreach (var c in new[] { "ArrowRight", "ArrowLeft", "ArrowDown", "ArrowUp" }) setPadKey(c, false);
        if (!started || paused || transitioning || gameState != "EXPLORE") return;

        // looking: the right stick, eased so small pushes aim finely
        var lk = pad.look;
        if (lk.x != 0 || lk.y != 0)
        {
            double sx = JS.Sign(lk.x) * Math.Pow(Math.Abs(lk.x), 1.6);
            double sy = JS.Sign(lk.y) * Math.Pow(Math.Abs(lk.y), 1.6);
            applyLook(sx * dt * 950, sy * dt * 650, true);
        }
        // L3 is a tap: it latches running while the stick is held, and letting go of the stick ends the run
        bool l3 = pad.isDown("l3");
        if (l3 && !padL3Was) padRun = true;
        padL3Was = l3;
        if (driving || (pad.move.x == 0 && pad.move.y == 0)) padRun = false;
        setPadKey("ShiftLeft", padRun);
        if (driving)
        {
            var mv = pad.move;
            setPadKey("KeyW", pad.rt > 0.15 || mv.y < -0.5);
            setPadKey("KeyS", pad.lt > 0.15 || mv.y > 0.5);
            setPadKey("KeyA", mv.x < -0.35);
            setPadKey("KeyD", mv.x > 0.35);
            setPadKey("Space", pad.isDown("b") || pad.isDown("r3"));
        }
        else
        {
            foreach (var c in new[] { "KeyW", "KeyS", "KeyA", "KeyD", "Space" }) setPadKey(c, false);
        }
    }

    // ---------------------------------------------------------------- main menu and settings

    private void applySettings()
    {
        var st = settings;
        audio.setVolumes(st.master, st.music, st.ambience);
        renderer.setPixelRatio(pixelRatio(st.sharp) * st.renderScale);
        resize();
        bool changed = renderer.shadowMap.enabled != st.shadows;
        renderer.shadowMap.enabled = st.shadows;
        sky.sun.castShadow = st.shadows;
        // the view angle: shown at once unless something is zooming it, and the hands keep the size they were drawn for
        if (!binocView && !drawing && gameState != "MENU")
        {
            camera.fov = baseFov;
            camera.updateProjectionMatrix();
        }
        double hands = Math.Tan(THREE.MathUtils.degToRad(baseFov / 2)) / Math.Tan(THREE.MathUtils.degToRad(GameSettings.FOV_DEFAULT / 2));
        view.group.scale.set(hands, hands, 1);
        if (changed)
        {
            // lit materials were compiled with or without shadows, so ask for a rebuild
            scene.traverse(o =>
            {
                if (o is THREE.Mesh m)
                {
                    if (m.materials != null) foreach (var x in m.materials) x.needsUpdate = true;
                    if (m.material != null) m.material.needsUpdate = true;
                }
            });
        }
    }

    private string shownDisplay = "";

    /// <summary>Fullscreen, windowed or borderless, and the window size.</summary>
    private void applyDisplay(bool atStart)
    {
        var st = settings;
        string key = $"{st.displayMode}|{st.windowSize}";
        if (key == shownDisplay) return;
        shownDisplay = key;
        var parts = (st.windowSize ?? "1280x720").Split('x');
        int width = parts.Length > 0 && int.TryParse(parts[0], out var pw) && pw > 0 ? pw : 1280;
        int height = parts.Length > 1 && int.TryParse(parts[1], out var ph) && ph > 0 ? ph : 720;
        if (st.displayMode == "windowed")
        {
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
        }
        else if (st.displayMode == "borderless")
        {
            var res = Screen.currentResolution;
            Screen.SetResolution(res.width > 0 ? res.width : width, res.height > 0 ? res.height : height, FullScreenMode.FullScreenWindow);
        }
        else
        {
            var res = Screen.currentResolution;
            Screen.SetResolution(res.width > 0 ? res.width : width, res.height > 0 ? res.height : height, FullScreenMode.ExclusiveFullScreen);
        }
    }

    private void openSettings(Action onClose = null)
    {
        ui.openSettings(
            settings,
            s =>
            {
                settings = s;
                applySettings();
                applyDisplay(false);
                saveSettings(s);
            },
            onClose
        );
    }

    /// <summary>The title screen: the island carries on living behind it while a slow camera drifts around.</summary>
    private void enterMenu()
    {
        gameState = "MENU";
        started = false;
        view.group.visible = false;
        view.proxy.visible = false;
        detector.setActive(false);
        dig.setActive(false);
        ui.skipIntro();
        ui.setMode("MENU");
        menuT = 0;
        menuShot = 0;
        pickMenuTarget();
        showMenu();
    }

    private void showMenu()
    {
        ui.showMainMenu(new MenuHooks
        {
            hasSaves = listSaves().Count > 0,
            // the newest save of all, whatever game it belongs to
            onContinue = () =>
            {
                var newest = listSaves().FirstOrDefault();
                if (newest != null) loadGame(newest.id);
            },
            onAchievements = () => showAchievements(),
            onNew = () => menuNewGame(),
            onLoad = () => ui.openSaves("load", saveHooks()),
            onSettings = () => openSettings(),
            onExit = () =>
            {
                // as in the desktop app: the game really closes. The browser's farewell card ("you can close this tab")
                // only ever made sense in a tab, and here it flashed up while the game shut down.
                window.close();
            },
        });
    }

    /// <summary>"New Game": the controls card first, then the adventure begins.</summary>
    private void menuNewGame()
    {
        ui.hideMainMenu();
        ui.showIntro(() => beginPlay());
    }

    private void beginPlay()
    {
        ui.fade(() =>
        {
            gameState = "EXPLORE";
            ui.setMode("EXPLORE");
            started = true;
            // a new game starts at the foot of the camp, looking at it
            player.x = 0;
            player.z = CAMP_Z + 9;
            player.yaw = 0;
            player.pitch = -0.12;
            eyeY = 0;
            view.proxy.visible = true;
            camera.fov = baseFov;
            camera.updateProjectionMatrix();
            selectSlot(1);
            ui.toast("Sweep the sand with your detector. Listen for the beep.");
            // the click on Begin was a moment ago, so the game can take the mouse right away and the pointer never shows
            grabMouse();
        });
    }

    /// <summary>Chooses what the next menu shot looks at: a crab on the beach, or a shark close to shore.</summary>
    private void pickMenuTarget()
    {
        menuTarget = null;
        menuCamReady = false;
        if (menuShot == 1)
        {
            var crabs = ecology.crabs.Where(c => c.alive).ToList();
            if (crabs.Count > 0)
            {
                menuTarget = crabs[(int)Math.Floor(JS.Random() * crabs.Count)].mesh;
                menuFocus.copy(menuTarget.getWorldPosition(new THREE.Vector3()));
            }
        }
        else if (menuShot == 2)
        {
            var shark = sea.sharks.FirstOrDefault(s => s.alive);
            if (shark != null)
            {
                // bring one in close enough to watch from the shore
                shark.x = (JS.Random() - 0.5) * 24;
                shark.z = SHORE_Z + 20;
                shark.heading = Math.PI / 2 + (JS.Random() - 0.5) * 0.6;
                menuTarget = shark.root;
                menuFocus.copy(shark.root.getWorldPosition(new THREE.Vector3()));
            }
        }
    }

    private void updateMenuCamera(double dt)
    {
        menuT += dt;
        if (!menuSwitching && menuT > 17)
        {
            menuSwitching = true;
            ui.fade(() =>
            {
                menuShot = (menuShot + 1) % 3;
                menuT = 0;
                menuSwitching = false;
                pickMenuTarget();
            });
        }
        var cam = camera;
        var look = new THREE.Vector3();
        var want = new THREE.Vector3();
        double t = menuT;
        double follow = 0; // how quickly the camera catches up with what it is watching; 0 = not smoothed
        if (menuShot == 0 || menuTarget == null)
        {
            // a slow circle round the camp, with the sea behind it
            double a = 0.35 + t * 0.07;
            double x = 3 + Math.Cos(a) * 15;
            double z = CAMP_Z + 4 + Math.Sin(a) * 15;
            want.set(x, beach.heightAt(x, z) + 3.4 + Math.Sin(t * 0.3) * 0.4, z);
            look.set(0, beach.heightAt(0, CAMP_Z) + 1.8, CAMP_Z);
        }
        else if (menuShot == 1)
        {
            // low and close, drifting along behind a crab: it is the smoothed focus, not the crab, that the camera tracks
            var tp = menuTarget.getWorldPosition(new THREE.Vector3());
            menuFocus.lerp(tp, 1 - Math.Exp(-dt * 1.6));
            double a = t * 0.12;
            var f = menuFocus;
            want.set(f.x + Math.Cos(a) * 3.4, beach.heightAt(f.x, f.z) + 0.95, f.z + Math.Sin(a) * 3.4);
            look.copy(f).add(new THREE.Vector3(0, 0.2, 0));
            follow = 2.2;
        }
        else
        {
            // from the water's edge, watching a shark swim by
            var tp = menuTarget.getWorldPosition(new THREE.Vector3());
            menuFocus.lerp(tp, 1 - Math.Exp(-dt * 1.2));
            var f = menuFocus;
            double cx = Math.Max(-18, Math.Min(18, f.x * 0.35));
            want.set(cx, beach.heightAt(cx, SHORE_Z - 4) + 1.9, SHORE_Z - 4);
            look.copy(f).add(new THREE.Vector3(0, 0.5, 0));
            follow = 2.2;
        }
        if (follow > 0 && !menuCamReady)
        {
            cam.position.copy(want);
            menuCamReady = true;
        }
        else if (follow > 0)
        {
            cam.position.lerp(want, 1 - Math.Exp(-dt * follow));
        }
        else
        {
            cam.position.copy(want);
        }
        cam.lookAt(look);
    }

    // ---------------------------------------------------------------- save / load

    private Snapshot snapshot()
    {
        // saved while diving, the game comes back on the beach where the dive began
        var pl = alt && diveReturn != null ? diveReturn : player;
        return new Snapshot
        {
            v = 1,
            savedAt = JS.DateNow(), // wall-clock time, like Date.now() in the browser: the save list shows and sorts by it
            data = JsonConvert.DeserializeObject<GameData>(JsonConvert.SerializeObject(state.data, SaveGame.Json), SaveGame.Json),
            player = new PlayerPose { x = pl.x, z = pl.z, yaw = pl.yaw, pitch = pl.pitch },
            tool = tool,
            radio = audio.radioOn,
            radioTrack = audio.trackNumber,
            dayTime = dayTime,
            size = BEACH_SEGMENTS,
            holes = holes,
            waypoint = waypoint != null ? new XZ { x = waypoint.x, z = waypoint.z } : null,
            flags = flags.positions.Select(p => new[] { p[0], p[1] }).ToList(),
            castles = castles.data(),
            dog = new DogSave { target = dog.item != null ? spawner.items.IndexOf(dog.item) : -1 },
            cart = new VehicleSave { x = cart.x, z = cart.z, heading = cart.heading },
            boat = new VehicleSave { x = boat.x, z = boat.z, heading = boat.heading },
            chests = treasure.takenFlags(),
            items = spawner.exportItems(),
            dug = beach.exportDug(),
        };
    }

    private SaveHooks saveHooks()
    {
        return new SaveHooks
        {
            list = () => listSaves(),
            save = (name, id) => saveGame(name, id),
            load = id => loadGame(id),
            remove = id => deleteSave(id),
        };
    }

    /// <summary>The achievements that depend on how much you have done, rather than on one thing happening.</summary>
    private void checkAchievements()
    {
        if (!started || gameState == "MENU") return;
        var d = state.data;
        var a = achievements;
        int finds = d.found.Values.Sum();
        if (finds >= 10) a.unlock("FINDS_10");
        if (finds >= 50) a.unlock("FINDS_50");
        if (finds >= 150) a.unlock("FINDS_150");
        if ((d.questsDone?.Count ?? 0) >= 1) a.unlock("QUEST_FIRST");
        if (QUEST_DEFS.All(q => d.questsDone != null && d.questsDone.Contains(q.id))) a.unlock("QUEST_ALL");
        if ((d.assembled?.Count ?? 0) >= 2) a.unlock("ASSEMBLED");
        if (d.coins >= 2000) a.unlock("RICH");
        if (d.ownsBoatKey) a.unlock("BOAT");
        if ((d.decor?.Count ?? 0) >= DECOR.Length) a.unlock("DECOR");
        if (d.day >= 7) a.unlock("WEEK");
    }

    /// <summary>The list of achievements on screen, with the earned ones marked.</summary>
    private void showAchievements()
    {
        ui.showAchievements(ACHIEVEMENTS.Select(x => new AchView
        {
            name = x.name,
            desc = x.desc,
            icon = x.icon,
            done = achievements.has(x.id),
            secret = x.secret,
        }).ToList());
    }

    private double autoT = 0;
    private double lastAuto = double.NegativeInfinity;

    /// <summary>
    /// Saves the world into this game's own autosaves, the older of its two each time; other games' autosaves are never
    /// touched. Quiet: no message. It skips the menu, sleeping and the ending, and never runs twice in quick succession.
    /// </summary>
    private void autoSave()
    {
        autoT = 0;
        double now = JS.Now();
        if (now - lastAuto < AUTOSAVE_MIN_GAP) return;
        if (!started || gameState == "MENU" || hammock != null) return;
        lastAuto = now;
        claimGameId(state.data);
        writeSave(snapshot(), "Autosave", nextAutosaveId(state.data.gameId), true);
    }

    /// <summary>Writes the whole world as a new save, or over an existing one.</summary>
    private SaveMeta saveGame(string name, string id = null)
    {
        claimGameId(state.data);
        var meta = writeSave(snapshot(), name, id);
        if (meta == null)
        {
            ui.toast("Could not save. The browser storage is full or blocked.");
            return null;
        }
        audio.coin();
        ui.toast(!string.IsNullOrEmpty(id) ? $"Saved over \"{meta.name}\"." : $"Saved as \"{meta.name}\".");
        return meta;
    }

    private void loadGame(string id)
    {
        requestLoad(id);
        GameHost.Reload();
    }

    /// <summary>A fresh start is simply a fresh page; nothing is loaded unless you ask for it.</summary>
    private void newGame()
    {
        requestAutostart();
        GameHost.Reload();
    }

    private void backToMenu()
    {
        GameHost.Reload();
    }

    /// <summary>Called once on start-up after a load: puts the saved world back.</summary>
    private void restoreWorld(Snapshot s)
    {
        // a save from a smaller, older map keeps your progress but not the old holes and finds
        bool sameMap = s.size == BEACH_SEGMENTS;
        if (sameMap)
        {
            spawner.importItems(s.items);
            beach.importDug(s.dug);
            holes = s.holes ?? new List<double[]>();
            setWaypoint(s.waypoint != null ? new Waypoint { x = s.waypoint.x, z = s.waypoint.z } : null);
            if (s.cart != null)
            {
                cart.x = s.cart.x;
                cart.z = s.cart.z;
                cart.heading = s.cart.heading;
                cart.update(0, null, (x, z) => new Resolved { x = x, z = z, hit = false });
            }
            treasure.setTaken(s.chests ?? new List<bool>());
            if (s.boat != null)
            {
                boat.x = s.boat.x;
                boat.z = s.boat.z;
                boat.heading = s.boat.heading;
            }
            spawner.checkReveals();
            var job = s.dog != null && s.dog.target >= 0 && s.dog.target < spawner.items.Count ? spawner.items[s.dog.target] : null;
            if (job != null && !job.collected) dog.startMission(job, true);
            player.x = s.player.x;
            player.z = s.player.z;
        }
        else
        {
            player.x = 0;
            player.z = CAMP_Z + 9;
        }
        flags.setAll(s.flags ?? new List<double[]>(), (x, z) => beach.heightAt(x, z) - 0.05);
        castles.setAll(s.castles ?? new List<CastleSave>());
        player.yaw = s.player.yaw;
        player.pitch = s.player.pitch;
        started = true;
        selectSlot(s.tool);
        audio.setTrack(s.radioTrack ?? 0);
        if (s.radio)
        {
            audio.setRadio(true);
            camp.setRadio(true);
        }
        ui.toast(sameMap ? "Game loaded. Welcome back." : "Game loaded. The map has grown, so the beach was laid out fresh.");
    }

    // ---------------------------------------------------------------- coffee

    /// <summary>A warm drink from the thermos: refills Warmth. The beach itself never changes.</summary>
    private void drinkCoffee()
    {
        if (state.data.warmth >= state.maxWarmth)
        {
            ui.toast("You are already warm and cozy.");
            return;
        }
        state.restoreWarmth();
        discover("coffee_first");
        audio.sip();
        ui.toast("Ahh. Warm again.");
    }

    // ---------------------------------------------------------------- frame

    /// <summary>Bow draw strength and the binocular zoom, which both change the field of view.</summary>
    private void applyToolFov(double dt)
    {
        if (drawing && canUseBow())
        {
            charge = Math.Min(1, charge + dt / 0.9);
            view.setDraw(charge);
            ui.setDraw(charge);
        }
        else if (drawing)
        {
            cancelDraw();
        }
        // a slight tunnel of focus as the bow is drawn
        double wantFov = binocView && gameState == "EXPLORE"
            ? (zoomIdx == 0 ? 16 : 7)
            : baseFov - (drawing ? charge * 7 : 0);
        if (Math.Abs(camera.fov - wantFov) > 0.05)
        {
            camera.fov += (wantFov - camera.fov) * Math.Min(1, dt * 9);
            camera.updateProjectionMatrix();
        }
    }

    private void updateCamera(double dt)
    {
        if (hammock != null)
        {
            updateHammock(dt);
            return;
        }
        if (underground)
        {
            updateUnderground(dt);
            return;
        }
        if (diving)
        {
            updateDive(dt);
            return;
        }
        if (driving)
        {
            updateDriving(dt);
            return;
        }
        if (vehicle == "boat")
        {
            updateDeck(dt);
            return;
        }
        if (seated)
        {
            updateFishing(dt);
            return;
        }
        var p = player;
        moving = false;
        double px0 = p.x;
        double pz0 = p.z;
        // No pointer lock available? Steer by holding the cursor away from the screen centre, like a gamepad stick.
        if (started && gameState == "EXPLORE" && cursorSteer && walkTo == null)
        {
            double x = input.pointer.x;
            double y = input.pointer.y;
            double dz(double v, double zone) => Math.Abs(v) < zone ? 0 : JS.Sign(v) * (Math.Abs(v) - zone) / (1 - zone);
            double zk = camera.fov / baseFov;
            p.yaw -= dz(x, 0.15) * 2.6 * dt * zk * settings.sensitivity;
            p.pitch = Math.Min(1.45, Math.Max(-1.45, p.pitch + dz(y, 0.2) * 1.5 * dt * zk * settings.sensitivity * (settings.invertMouse ? -1 : 1)));
        }
        if (started && gameState == "EXPLORE")
        {
            var k = input.keys;
            var pm = pad.move;
            double f = Math.Max(-1, Math.Min(1, (k.Contains("KeyW") ? 1 : 0) - (k.Contains("KeyS") ? 1 : 0) - pm.y));
            double s = Math.Max(-1, Math.Min(1, (k.Contains("KeyD") ? 1 : 0) - (k.Contains("KeyA") ? 1 : 0) + pm.x));
            if (f != 0 || s != 0)
            {
                walkTo = null;
                double len = Math.Max(1, JS.Hypot(f, s));
                double sp = (k.Contains("ShiftLeft") ? 5.2 : 3.2) * dt;
                // three's rotation.y: forward is (-sin, -cos), right is (cos, -sin)
                double fx = -Math.Sin(p.yaw);
                double fz = -Math.Cos(p.yaw);
                double rx = Math.Cos(p.yaw);
                double rz = -Math.Sin(p.yaw);
                p.x += ((fx * f + rx * s) / len) * sp;
                p.z += ((fz * f + rz * s) / len) * sp;
                moving = true;
            }
        }
        else if (walkTo != null)
        {
            var w = walkTo;
            double k = 1 - Math.Exp(-dt * 2.5);
            double dyaw = w.yaw - p.yaw;
            dyaw = Math.Atan2(Math.Sin(dyaw), Math.Cos(dyaw));
            p.x += (w.x - p.x) * k;
            p.z += (w.z - p.z) * k;
            p.yaw += dyaw * k;
            p.pitch += (w.pitch - p.pitch) * k;
            moving = JS.Hypot(w.x - p.x, w.z - p.z) > 0.3;
        }
        // on the island the limit is the water's edge; on the beach it is the shore and the dunes
        var isl = islandAt(p.x, p.z);
        bool onIsl = isl != null;
        if (onIsl)
        {
            if (beach.heightAt(p.x, p.z) < WATER_LEVEL + 0.25)
            {
                p.x = px0;
                p.z = pz0;
            }
            foreach (var s in isl.colliders)
            {
                double d = JS.Hypot(p.x - s.x, p.z - s.z);
                double min = s.r + 0.3;
                if (d < min)
                {
                    p.x = s.x + ((p.x - s.x) / (d != 0 ? d : 1)) * min;
                    p.z = s.z + ((p.z - s.z) / (d != 0 ? d : 1)) * min;
                }
            }
            if (isl.kind == 1)
            {
                discover("lagoon_first");
                if (JS.Hypot(p.x - isl.ruins.x, p.z - isl.ruins.z) < 6) discover("lagoon_camp");
            }
            else if (isl.kind == 2)
            {
                discover("ember_first");
                if (JS.Hypot(p.x - isl.ruins.x, p.z - isl.ruins.z) < 6.5) discover("ember_spring");
            }
            else
            {
                discover("island_first");
                if (JS.Hypot(p.x - isl.ruins.x, p.z - isl.ruins.z) < 7) discover("island_ruins");
            }
        }
        else
        {
            // keep out of the sea, off the edge, and out of the fire
            p.x = Math.Min(HALF - 2, Math.Max(-(HALF - 2), p.x));
            p.z = Math.Max(-(HALF - 2), p.z);
            double maxZ = SHORE_Z - 3;
            if (p.z > maxZ)
            {
                // the pier carries on out over the water: once on it, the only ways are along it and back
                double half = Pier.HALF_W - 0.3;
                bool wasOut = pz0 > maxZ && Math.Abs(px0 - Pier.X) <= half + 0.01;
                if (wasOut || Math.Abs(p.x - Pier.X) <= half)
                {
                    p.x = Math.Min(Pier.X + half, Math.Max(Pier.X - half, p.x));
                    p.z = Math.Min(Pier.Z1 - 0.45, p.z);
                    // the bench and the bucket at the end
                    foreach (var o in new (double x, double z, double r)[] { (Pier.SEAT.x, Pier.SEAT.z + 0.05, 0.62), (Pier.BUCKET.x, Pier.BUCKET.z, 0.4) })
                    {
                        double d = JS.Hypot(p.x - o.x, p.z - o.z);
                        if (d < o.r)
                        {
                            p.x = o.x + ((p.x - o.x) / (d != 0 ? d : 1)) * o.r;
                            p.z = o.z + ((p.z - o.z) / (d != 0 ? d : 1)) * o.r;
                        }
                    }
                    if (p.z > SHORE_Z + 2) discover("pier_first");
                }
                else p.z = maxZ;
            }
        }
        foreach (var o in camp.obstacles)
        {
            double d = JS.Hypot(p.x - o.x, p.z - o.z);
            if (d < o.r)
            {
                p.x = o.x + ((p.x - o.x) / (d != 0 ? d : 1)) * o.r;
                p.z = o.z + ((p.z - o.z) / (d != 0 ? d : 1)) * o.r;
            }
        }
        // the parked cart is long, so it blocks walking with a nose, a middle and a tail
        foreach (var off in new[] { -1.1, 0.0, 1.1 })
        {
            double cx = cart.x - Math.Sin(cart.heading) * off;
            double cz = cart.z - Math.Cos(cart.heading) * off;
            double d = JS.Hypot(p.x - cx, p.z - cz);
            if (d < 1.05)
            {
                p.x = cx + ((p.x - cx) / (d != 0 ? d : 1)) * 1.05;
                p.z = cz + ((p.z - cz) / (d != 0 ? d : 1)) * 1.05;
            }
        }
        foreach (var s in castles.colliders)
        {
            double d = JS.Hypot(p.x - s.x, p.z - s.z);
            double min = s.r + 0.3;
            if (d < min)
            {
                p.x = s.x + ((p.x - s.x) / (d != 0 ? d : 1)) * min;
                p.z = s.z + ((p.z - s.z) / (d != 0 ? d : 1)) * min;
            }
        }
        foreach (var s in stones.colliders)
        {
            double d = JS.Hypot(p.x - s.x, p.z - s.z);
            double min = s.r + 0.3;
            if (d < min)
            {
                p.x = s.x + ((p.x - s.x) / (d != 0 ? d : 1)) * min;
                p.z = s.z + ((p.z - s.z) / (d != 0 ? d : 1)) * min;
            }
        }
        foreach (var tr in scenery.trunks)
        {
            double d = JS.Hypot(p.x - tr.x, p.z - tr.z);
            if (d < 0.6)
            {
                p.x = tr.x + ((p.x - tr.x) / (d != 0 ? d : 1)) * 0.6;
                p.z = tr.z + ((p.z - tr.z) / (d != 0 ? d : 1)) * 0.6;
            }
        }
        double fd = JS.Hypot(p.x, p.z - CAMP_Z);
        if (fd < 1.6)
        {
            p.x = (p.x / (fd != 0 ? fd : 1)) * 1.6;
            p.z = CAMP_Z + ((p.z - CAMP_Z) / (fd != 0 ? fd : 1)) * 1.6;
        }
        double ground = beach.heightAt(p.x, p.z);
        // on the pier the boards are the ground
        bool onPier = Pier.over(p.x, p.z);
        if (onPier) ground = Math.Max(ground, Pier.deckAt(p.z));
        if (eyeY == 0) eyeY = ground + 1.65;
        eyeY += (ground + 1.65 - eyeY) * Math.Min(1, dt * 12);
        feet.set(p.x, ground, p.z);
        camera.position.set(p.x, eyeY + (moving ? Math.Sin(time * 9) * 0.025 : 0), p.z);
        camera.rotation.set(p.pitch, p.yaw, 0);
        view.update(dt, moving, feet, p.yaw, p.pitch);
        // binoculars narrow the field of view
        applyToolFov(dt);
        if (!onPier) leaveFootprints();
    }

    // ---------------------------------------------------------------- the room under the hatch

    private void enterUnderground()
    {
        if (transitioning || alt) return;
        transitioning = true;
        if (drawing) cancelDraw();
        setBinocView(false);
        ui.fade(() =>
        {
            diveReturn = new PlayerPose { x = player.x, z = player.z, yaw = player.yaw, pitch = player.pitch };
            underground = true;
            altTool = 0;
            hideLand(true);
            spawner.group.visible = false;
            cellar.setActive(true);
            sky.setUnderground(true);
            player.x = cellar.entry.x;
            player.z = cellar.entry.z;
            player.yaw = cellar.entry.yaw;
            player.pitch = 0;
            eyeY = UG.y + 1.65;
            ladderY = 0;
            walkTo = null;
            view.setTool(0);
            ui.setTool(0);
            detector.setActive(false);
            dig.setActive(false);
            ui.setUnderground(true);
            audio.thud();
            audio.setOceanLevel(0.006);
            discover("underground_first");
            window.setTimeout(() => transitioning = false, 350);
        });
    }

    private void leaveUnderground()
    {
        if (transitioning || !underground) return;
        transitioning = true;
        ui.fade(() =>
        {
            var r = diveReturn;
            setBinocView(false);
            altTool = 0;
            underground = false;
            cellar.setActive(false);
            sky.setUnderground(false);
            sky.openAir = 0;
            spawner.group.visible = true;
            hideLand(false);
            if (r != null)
            {
                player.x = r.x;
                player.z = r.z;
                player.yaw = r.yaw;
                player.pitch = r.pitch;
            }
            diveReturn = null;
            eyeY = 0;
            lastFoot.set = false;
            view.setTool(tool);
            ui.setTool(tool);
            detector.setActive(tool == 1);
            dig.setActive(tool == 2);
            ui.setUnderground(false);
            audio.thud();
            audio.setOceanLevel(0.06);
            window.setTimeout(() => transitioning = false, 350);
        });
    }

    /// <summary>Walking about the room: like walking the beach, but with walls, furniture and the stairs.</summary>
    private void updateUnderground(double dt)
    {
        var p = player;
        moving = false;
        if (started && gameState == "EXPLORE")
        {
            var k = input.keys;
            var pm = pad.move;
            double f = Math.Max(-1, Math.Min(1, (k.Contains("KeyW") ? 1 : 0) - (k.Contains("KeyS") ? 1 : 0) - pm.y));
            double s = Math.Max(-1, Math.Min(1, (k.Contains("KeyD") ? 1 : 0) - (k.Contains("KeyA") ? 1 : 0) + pm.x));
            // facing the ladder, forward and back climb it instead of walking
            bool climbing = cellar.onLadder(p.x, p.z) && (ladderY > 0.02 || (f > 0 && -Math.Sin(p.yaw) > 0.3));
            if (climbing)
            {
                ladderY = Math.Min(cellar.ladderH, Math.Max(0, ladderY + f * 1.6 * dt));
                if (f != 0) moving = true;
            }
            double fw = climbing ? 0 : f;
            if (fw != 0 || s != 0)
            {
                double len = Math.Max(1, JS.Hypot(fw, s));
                double sp = (k.Contains("ShiftLeft") ? 5.2 : 3.2) * dt;
                p.x += ((-Math.Sin(p.yaw) * fw + Math.Cos(p.yaw) * s) / len) * sp;
                p.z += ((-Math.Cos(p.yaw) * fw - Math.Sin(p.yaw) * s) / len) * sp;
                moving = true;
            }
        }
        // up the ladder you cannot step off sideways
        if (ladderY > 0.02)
        {
            p.x = Math.Max(p.x, 7.1);
            p.z = Math.Min(-3.3, Math.Max(-4.7, p.z));
        }
        else if (!cellar.onLadder(p.x, p.z))
        {
            ladderY = 0;
        }
        foreach (var col in cellar.colliders)
        {
            double d = JS.Hypot(p.x - col.x, p.z - col.z);
            if (d < col.r)
            {
                p.x = col.x + ((p.x - col.x) / (d != 0 ? d : 1)) * col.r;
                p.z = col.z + ((p.z - col.z) / (d != 0 ? d : 1)) * col.r;
            }
        }
        var c = cellar.clamp(p.x, p.z);
        p.x = c.x;
        p.z = c.z;
        cellar.setBeam(isMidday);
        // the outside comes in as you go down the stair to the cave
        double air = Math.Min(1, Math.Max(0, (p.x - 11) / 4));
        sky.openAir += (air - sky.openAir) * Math.Min(1, dt * 3);
        cellar.setSide(p.x > 11);
        cellar.cave.setDaylight(1 - sky.starAmount);
        double ground = UG.y + cellar.floorAt(p.x, p.z) + ladderY;
        eyeY += (ground + 1.65 - eyeY) * Math.Min(1, dt * 12);
        feet.set(p.x, ground, p.z);
        camera.position.set(p.x, eyeY + (moving ? Math.Sin(time * 9) * 0.025 : 0), p.z);
        camera.rotation.set(p.pitch, p.yaw, 0);
        view.update(dt, moving, feet, p.yaw, p.pitch);
        applyToolFov(dt);
    }

    // ---------------------------------------------------------------- diving

    /// <summary>True where the water is close enough to slip in: the shallow edge of the beach, or the island's coast.</summary>
    private bool canDive()
    {
        if (vehicle != null || driving || hammock != null || seated || alt || !started) return false;
        if (onIsland) return beach.heightAt(player.x, player.z) < WATER_LEVEL + 0.8;
        return player.z > SHORE_Z - 8;
    }

    /// <summary>Adds any sea-floor finds a save does not have yet (a save from before diving, say).</summary>
    private void ensureDiveFinds()
    {
        var have = new Dictionary<string, int>();
        foreach (var i in spawner.items)
        {
            if (DIVE_ITEM_IDS.Contains(i.def.id))
            {
                have[i.def.id] = (have.TryGetValue(i.def.id, out var cnt) ? cnt : 0) + 1;
            }
        }
        foreach (var s in under.findSpots())
        {
            int n = have.TryGetValue(s.id, out var cnt) ? cnt : 0;
            if (n > 0)
            {
                have[s.id] = n - 1;
                continue;
            }
            if (DEF_BY_ID.TryGetValue(s.id, out var def) && def != null) spawner.addOpen(def, s.x, s.z);
        }
        spawner.checkReveals();
        showFindsFor(false);
    }

    /// <summary>Land finds are drawn on land and sea-floor finds only underwater.</summary>
    private void showFindsFor(bool underwater)
    {
        foreach (var it in spawner.items)
        {
            if (it.mesh != null) it.mesh.visible = under.contains(it.x, it.z) == underwater;
        }
    }

    /// <summary>Everything on land is hidden while diving, and put back afterwards; the sky and the finds stay.</summary>
    private void hideLand(bool on)
    {
        if (on)
        {
            var keep = new HashSet<THREE.Object3D> { sky.group, camera, under.group, cellar.group, spawner.group };
            hiddenByDive = scene.children.Where(o => o.visible && !keep.Contains(o)).ToList();
            foreach (var o in hiddenByDive) o.visible = false;
        }
        else
        {
            foreach (var o in hiddenByDive) o.visible = true;
            hiddenByDive = new List<THREE.Object3D>();
        }
        showFindsFor(on);
    }

    private void applyDiveHands()
    {
        view.setSwim(true);
        view.setTool(9);
        ui.setTool(0);
        ui.setBadgeText("🔱", "Harpoon");
        detector.setActive(false);
        dig.setActive(false);
    }

    private void enterDive()
    {
        if (transitioning || alt) return;
        if (!state.data.ownsDiveGear)
        {
            audio.error();
            ui.toast("You need diving gear to go under. The camper shop sells a mask, snorkel, fins and a harpoon.");
            return;
        }
        transitioning = true;
        if (drawing) cancelDraw();
        setBinocView(false);
        ui.fade(() =>
        {
            diveReturn = new PlayerPose { x = player.x, z = player.z, yaw = player.yaw, pitch = player.pitch };
            diving = true;
            altTool = 0;
            hideLand(true);
            under.setActive(true);
            sky.setUnderwater(true);
            diveBg = scene.background;
            scene.background = new THREE.Color(0x1b7aa6);
            player.x = UW_ENTRY.x;
            player.z = UW_ENTRY.z;
            player.yaw = 0;
            player.pitch = -0.25;
            diveY = -1.3;
            diveVel.set(0, 0, 0);
            walkTo = null;
            applyDiveHands();
            ui.setDiving(true);
            audio.splash(2);
            audio.setOceanLevel(0.015);
            discover("dive_first");
            window.setTimeout(() => transitioning = false, 350);
        });
    }

    private void leaveDive()
    {
        if (transitioning || !diving) return;
        transitioning = true;
        ui.fade(() =>
        {
            var r = diveReturn;
            setBinocView(false);
            altTool = 0;
            diving = false;
            under.setActive(false);
            sky.setUnderwater(false);
            scene.background = diveBg;
            hideLand(false);
            if (r != null)
            {
                player.x = r.x;
                player.z = r.z;
                player.yaw = r.yaw;
                player.pitch = r.pitch;
            }
            diveReturn = null;
            lift.up = lift.down = false;
            diveVel.set(0, 0, 0);
            eyeY = 0;
            lastFoot.set = false;
            view.setSwim(false);
            view.setTool(tool);
            ui.setTool(tool);
            detector.setActive(tool == 1);
            dig.setActive(tool == 2);
            camera.rotation.z = 0;
            ui.setDiving(false);
            audio.splash(2);
            audio.setOceanLevel(0.06);
            window.setTimeout(() => transitioning = false, 350);
        });
    }

    /// <summary>Swimming: move along the way you look, Space rises, C sinks.</summary>
    private void updateDive(double dt)
    {
        var p = player;
        if (started && gameState == "EXPLORE")
        {
            var k = input.keys;
            var pm = pad.move;
            double f = Math.Max(-1, Math.Min(1, (k.Contains("KeyW") ? 1 : 0) - (k.Contains("KeyS") ? 1 : 0) - pm.y));
            double s = Math.Max(-1, Math.Min(1, (k.Contains("KeyD") ? 1 : 0) - (k.Contains("KeyA") ? 1 : 0) + pm.x));
            double v = (k.Contains("Space") || lift.up ? 1 : 0) - (k.Contains("KeyC") || k.Contains("ControlLeft") || lift.down ? 1 : 0);
            double cp = Math.Cos(p.pitch);
            var want = new THREE.Vector3(
                -Math.Sin(p.yaw) * cp * f + Math.Cos(p.yaw) * s,
                Math.Sin(p.pitch) * f + v * 0.85,
                -Math.Cos(p.yaw) * cp * f - Math.Sin(p.yaw) * s
            );
            if (want.length() > 1) want.normalize();
            want.multiplyScalar(k.Contains("ShiftLeft") ? 3.8 : 2.4);
            diveVel.lerp(want, Math.Min(1, dt * 2.5));
        }
        else
        {
            diveVel.multiplyScalar(Math.Max(0, 1 - dt * 3));
        }
        moving = diveVel.length() > 0.3;
        p.x += diveVel.x * dt;
        p.z += diveVel.z * dt;
        diveY += diveVel.y * dt;
        double lim = UW.half - 3;
        p.x = Math.Min(UW.x + lim, Math.Max(UW.x - lim, p.x));
        p.z = Math.Min(UW.z + lim, Math.Max(UW.z - lim, p.z));
        double floor = under.floorAt(p.x, p.z);
        diveY = Math.Min(UW_SURFACE - 0.9, Math.Max(floor + 0.8, diveY));
        // a slow rise and fall, like the kick of the fins
        double bob = Math.Sin(time * (moving ? 1.7 : 0.9)) * (moving ? 0.05 : 0.03);
        camera.position.set(p.x, diveY + bob, p.z);
        camera.rotation.set(p.pitch, p.yaw, Math.Sin(time * 0.7) * 0.012);
        feet.set(p.x, diveY - 1.6, p.z);
        view.update(dt, moving, feet, p.yaw, p.pitch);
        applyToolFov(dt);
        if (JS.Hypot(p.x - under.wreck.x, p.z - under.wreck.z) < 9) discover("wreck_found");
    }

    /// <summary>Looses a spear along the crosshair.</summary>
    private void fireHarpoon()
    {
        camera.updateMatrixWorld(true);
        var ray = input.ray(camera, true);
        var aim = ray.ray.origin.clone().addScaledVector(ray.ray.direction, 12);
        var origin = camera.localToWorld(new THREE.Vector3(0.14, -0.12, -0.9));
        if (!under.fire(origin, aim.sub(origin))) return;
        view.fire();
        audio.splash(2);
    }

    private void onFishSpeared(double reward, string label)
    {
        state.addCoins(reward);
        discover("harpoon_first");
        if (label != "fish") discover("sea_creature");
        audio.coin();
        ui.toast($"Speared a {label}! +{JS.Str(reward)}g");
    }

    /// <summary>A print every stride while the player is on foot, pointing the way they are moving.</summary>
    private void leaveFootprints()
    {
        var p = player;
        var last = lastFoot;
        if (!last.set)
        {
            last.x = p.x;
            last.z = p.z;
            last.set = true;
            return;
        }
        double dx = p.x - last.x;
        double dz = p.z - last.z;
        double d = JS.Hypot(dx, dz);
        last.x = p.x;
        last.z = p.z;
        if (d > 2 || d < 1e-4) return; // a teleport or no movement
        stepDist += d;
        double stride = input.keys.Contains("ShiftLeft") ? 1.0 : 0.78;
        if (stepDist >= stride)
        {
            stepDist = 0;
            tracks.step(p.x, p.z, Math.Atan2(-dx, -dz));
        }
    }

    // ---------------------------------------------------------------- golf cart

    /// <summary>Keeps a round vehicle out of palms, camp props, the fire and the sea.</summary>
    private Resolved resolveCart(double x, double z, double radius)
    {
        bool hit = false;
        void push(double cx, double cz, double r)
        {
            double d = JS.Hypot(x - cx, z - cz);
            if (d < r)
            {
                x = cx + ((x - cx) / (d != 0 ? d : 1)) * r;
                z = cz + ((z - cz) / (d != 0 ? d : 1)) * r;
                hit = true;
            }
        }
        foreach (var t in scenery.trunks) push(t.x, t.z, radius + 0.3);
        foreach (var s in stones.colliders) push(s.x, s.z, s.r + radius * 0.8);
        foreach (var s in castles.colliders) push(s.x, s.z, s.r + radius * 0.6);
        foreach (var o in camp.obstacles) push(o.x, o.z, o.r + radius * 0.7);
        push(0, CAMP_Z, 1.6 + radius * 0.6);
        double nx = Math.Min(HALF - 2, Math.Max(-(HALF - 2), x));
        double nz = Math.Min(SHORE_Z - 4, Math.Max(-(HALF - 2), z));
        if (nx != x || nz != z) hit = true;
        return new Resolved { x = nx, z = nz, hit = hit };
    }

    private void enterVehicle(string v)
    {
        setBinocView(false);
        driving = true;
        vehicle = v;
        lookOffset = 0;
        player.pitch = -0.08;
        view.setTool(0);
        view.proxy.visible = false;
        detector.setActive(false);
        dig.setActive(false);
        ui.setTool(0);
        audio.ui();
        audio.setEngine(true, 0, v == "boat");
        discover(v == "boat" ? "boat_drive" : "cart_drive");
    }

    private void exitVehicle()
    {
        string v = vehicle;
        if (v == "boat")
        {
            // you can only step out where it is shallow enough to wade to the beach
            if (!nearShore())
            {
                ui.toast("Park the boat closer to the shore to step off, so you can climb back aboard.");
                audio.error();
                return;
            }
            var spot = landingSpot();
            player.x = spot.x;
            player.z = spot.z;
        }
        else
        {
            // step out on the left side of the cart, clear of its body
            double lx = -Math.Cos(cart.heading);
            double lz = Math.Sin(cart.heading);
            player.x = cart.x + lx * 2.0;
            player.z = cart.z + lz * 2.0;
            cart.speed = 0;
        }
        double heading = v == "boat" ? boat.heading : cart.heading;
        driving = false;
        vehicle = null;
        boat.speed = 0;
        audio.setEngine(false);
        player.yaw = heading + lookOffset;
        view.proxy.visible = true;
        view.setTool(tool);
        ui.setTool(tool);
        detector.setActive(tool == 1);
        dig.setActive(tool == 2);
        camera.fov = baseFov;
        camera.updateProjectionMatrix();
        audio.ui();
    }

    /// <summary>Keeps the boat afloat: it cannot move onto sand, and it slides along the coast instead of stopping dead.</summary>
    private Resolved resolveBoat(double nx, double nz)
    {
        var b = boat;
        double level = ocean.surfaceY;
        double fx = -Math.Sin(b.heading);
        double fz = -Math.Cos(b.heading);
        double rx = Math.Cos(b.heading);
        double rz = -Math.Sin(b.heading);
        // depth of water under the shallowest point of the hull; at least -0.02 means it floats
        double clearance(double x, double z)
        {
            if (Math.Abs(x) > seaHalfWidth(z) || z > SEA.z) return double.NegativeInfinity;
            double m = double.PositiveInfinity;
            foreach (var pt in new (double al, double side)[] { (0, 0), (2.3, 0), (-2.3, 0), (0, 0.9), (0, -0.9) })
            {
                double hx = x + fx * pt.al + rx * pt.side, hz = z + fz * pt.al + rz * pt.side;
                // the pier is as solid to a hull as the sand is
                if (Pier.blocks(hx, hz)) return double.NegativeInfinity;
                m = Math.Min(m, level - beach.heightAt(hx, hz));
            }
            return m;
        }
        bool floats(double x, double z) => clearance(x, z) >= 0.02;
        if (floats(nx, nz)) return new Resolved { x = nx, z = nz, hit = false };
        if (floats(nx, b.z)) return new Resolved { x = nx, z = b.z, hit = true };
        if (floats(b.x, nz)) return new Resolved { x = b.x, z = nz, hit = true };
        // already aground (turned onto the sand, or pushed in by a wave): any move towards deeper water is allowed,
        // so the boat can always work itself free instead of sticking for good
        double here = clearance(b.x, b.z);
        if (here < 0.02)
        {
            var best = (x: b.x, z: b.z, c: here);
            var tries = new List<(double x, double z)> { (nx, nz), (nx, b.z), (b.x, nz) };
            for (int i = 0; i < 12; i++) tries.Add((b.x + Math.Cos((i * Math.PI) / 6) * 0.25, b.z + Math.Sin((i * Math.PI) / 6) * 0.25));
            foreach (var tr in tries)
            {
                double c = clearance(tr.x, tr.z);
                if (c > best.c + 1e-4) best = (tr.x, tr.z, c);
            }
            return new Resolved { x = best.x, z = best.z, hit = true };
        }
        return new Resolved { x = b.x, z = b.z, hit = true };
    }

    private void updateBoating(double dt)
    {
        var k = input.keys;
        var b = boat;
        if (cursorSteer)
        {
            double px = input.pointer.x;
            if (Math.Abs(px) > 0.35) lookOffset = Math.Min(1.5, Math.Max(-1.5, lookOffset - JS.Sign(px) * (Math.Abs(px) - 0.35) * 2.2 * dt));
        }
        b.update(
            dt,
            time,
            new DriveInput
            {
                throttle = (k.Contains("KeyW") ? 1 : 0) - (k.Contains("KeyS") ? 1 : 0),
                steer = (k.Contains("KeyA") ? 1 : 0) - (k.Contains("KeyD") ? 1 : 0),
                brake = k.Contains("Space"),
            },
            ocean.heightAt(b.x, b.z),
            (x, z) =>
            {
                var r = resolveBoat(x, z);
                if (r.hit && Math.Abs(b.speed) > 2.5) audio.thud();
                return r;
            }
        );
        var p = player;
        p.x = b.x;
        p.z = b.z;
        p.yaw = b.heading + lookOffset;
        camera.position.copy(b.seat());
        camera.rotation.set(p.pitch, p.yaw, 0);
        feet.set(b.x, ocean.surfaceY, b.z);
        moving = false;
        double sp = Math.Abs(b.speed) / 14;
        double fov = baseFov + sp * 12;
        if (Math.Abs(camera.fov - fov) > 0.05)
        {
            camera.fov += (fov - camera.fov) * Math.Min(1, dt * 5);
            camera.updateProjectionMatrix();
        }
        audio.setEngine(true, sp, true);
    }

    /// <summary>Standing on the boat's deck: free to look around and use weapons while it floats along.</summary>
    private void updateDeck(double dt)
    {
        var p = player;
        var b = boat;
        var k = input.keys;
        if (started && cursorSteer)
        {
            double x = input.pointer.x;
            double y = input.pointer.y;
            double dz(double v, double zone) => Math.Abs(v) < zone ? 0 : JS.Sign(v) * (Math.Abs(v) - zone) / (1 - zone);
            double zk = camera.fov / baseFov;
            p.yaw -= dz(x, 0.15) * 2.6 * dt * zk * settings.sensitivity;
            p.pitch = Math.Min(1.45, Math.Max(-1.45, p.pitch + dz(y, 0.2) * 1.5 * dt * zk * settings.sensitivity * (settings.invertMouse ? -1 : 1)));
        }
        // a few steps around the deck, relative to where the player is looking
        double f = Math.Max(-1, Math.Min(1, (k.Contains("KeyW") ? 1 : 0) - (k.Contains("KeyS") ? 1 : 0) - pad.move.y));
        double sd = Math.Max(-1, Math.Min(1, (k.Contains("KeyD") ? 1 : 0) - (k.Contains("KeyA") ? 1 : 0) + pad.move.x));
        moving = f != 0 || sd != 0;
        if (moving)
        {
            double yl = p.yaw - b.heading;
            double len = Math.Max(1, JS.Hypot(f, sd));
            deck.x += (((-Math.Sin(yl)) * f + Math.Cos(yl) * sd) / len) * 1.5 * dt;
            deck.z += (((-Math.Cos(yl)) * f + (-Math.Sin(yl)) * sd) / len) * 1.5 * dt;
        }
        deck.x = Math.Min(0.62, Math.Max(-0.62, deck.x));
        deck.z = Math.Min(1.75, Math.Max(-0.2, deck.z));
        b.group.updateWorldMatrix(true, false);
        var eye = b.group.localToWorld(new THREE.Vector3(deck.x, 1.95, deck.z));
        var floor = b.group.localToWorld(new THREE.Vector3(deck.x, 0.46, deck.z));
        p.x = floor.x;
        p.z = floor.z;
        camera.position.copy(eye);
        camera.rotation.set(p.pitch, p.yaw, 0);
        feet.copy(floor);
        view.update(dt, moving, feet, p.yaw, p.pitch);
        applyToolFov(dt);
    }

    /// <summary>Where the player would stand on the beach after stepping off the boat.</summary>
    private LandingSpot landingSpot()
    {
        // out by an island, the landing is on its shore
        foreach (var isl in islands)
        {
            if (JS.Hypot(boat.x - isl.spec.x, boat.z - isl.spec.z) >= isl.spec.R + 20) continue;
            var il = isl.landingFrom(boat.x, boat.z);
            if (il != null) return new LandingSpot { x = il.Value.x, z = il.Value.z, island = true };
        }
        // alongside the pier, the way off the boat is onto its boards
        var onto = Pier.landingFor(boat.x, boat.z);
        if (onto != null) return new LandingSpot { x = onto.Value.x, z = onto.Value.z, island = false };
        double shore = beach.shoreZ(boat.x, ocean.surfaceY);
        return new LandingSpot { x = Math.Min(HALF - 2, Math.Max(-(HALF - 2), boat.x)), z = Math.Min(SHORE_Z - 3, shore - 1.5), island = false };
    }

    /// <summary>
    /// True only when the boat is parked close enough to the beach that the player can walk back
    /// to it afterwards (boarding works from about 12 m), so nobody gets stranded ashore.
    /// </summary>
    private bool nearShore()
    {
        var l = landingSpot();
        return JS.Hypot(boat.x - l.x, boat.z - l.z) <= 9;
    }

    /// <summary>Lets go of the wheel and stands up on the deck, free to use a weapon.</summary>
    private void leaveHelm()
    {
        driving = false;
        if (tool == 1 || tool == 2 || tool == 7 || tool == 8) tool = 5;
        deck = new DeckPos { x = 0, z = 0.5 };
        player.yaw = boat.heading + lookOffset;
        view.proxy.visible = true;
        view.setTool(tool);
        ui.setTool(tool);
        detector.setActive(false);
        dig.setActive(false);
        camera.fov = baseFov;
        camera.updateProjectionMatrix();
        audio.ui();
    }

    /// <summary>Back to the wheel.</summary>
    private void takeHelm()
    {
        setBinocView(false);
        lookOffset = Math.Atan2(Math.Sin(player.yaw - boat.heading), Math.Cos(player.yaw - boat.heading));
        driving = true;
        view.setTool(0);
        view.proxy.visible = false;
        ui.setTool(0);
        audio.ui();
        audio.setEngine(true, 0, true);
    }

    private void updateDriving(double dt)
    {
        if (vehicle == "boat")
        {
            updateBoating(dt);
            return;
        }
        var k = input.keys;
        var c = cart;
        // with the cursor free, holding it near a screen edge glances left or right
        if (!locked)
        {
            double px = input.pointer.x;
            if (Math.Abs(px) > 0.35) lookOffset = Math.Min(1.5, Math.Max(-1.5, lookOffset - JS.Sign(px) * (Math.Abs(px) - 0.35) * 2.2 * dt));
        }
        c.update(
            dt,
            new DriveInput
            {
                throttle = (k.Contains("KeyW") ? 1 : 0) - (k.Contains("KeyS") ? 1 : 0),
                steer = (k.Contains("KeyA") ? 1 : 0) - (k.Contains("KeyD") ? 1 : 0),
                brake = k.Contains("Space"),
            },
            (x, z) =>
            {
                var r = resolveCart(x, z, 1.1);
                if (r.hit && Math.Abs(c.speed) > 2) audio.thud();
                return r;
            }
        );

        var p = player;
        p.x = c.x;
        p.z = c.z;
        p.yaw = c.heading + lookOffset;
        var seat = c.seat();
        camera.position.copy(seat);
        camera.rotation.set(p.pitch, p.yaw, 0);
        feet.set(c.x, seat.y - 1.4, c.z);
        moving = false;

        // speed feel: a wider view, a motor hum and a trail of sand
        double sp = Math.Abs(c.speed) / 10;
        double fov = baseFov + sp * 10;
        if (Math.Abs(camera.fov - fov) > 0.05)
        {
            camera.fov += (fov - camera.fov) * Math.Min(1, dt * 5);
            camera.updateProjectionMatrix();
        }
        audio.setEngine(true, sp);
        dustTimer -= dt;
        if (dustTimer <= 0 && Math.Abs(c.speed) > 2)
        {
            dustTimer = 0.06;
            double bx = c.x + Math.Sin(c.heading) * 1.2;
            double bz = c.z + Math.Cos(c.heading) * 1.2;
            foreach (var side in new[] { -0.8, 0.8 })
            {
                double sx = bx + Math.Cos(c.heading) * side;
                double sz = bz - Math.Sin(c.heading) * side;
                dig.particles.burst(new THREE.Vector3(sx, beach.heightAt(sx, sz) + 0.15, sz), 2, 0.9, 0.9, 0.7, new THREE.Color(0xe6ccb2));
            }
        }
        c.setHeadlights(sky.isDark);

        // tyre tracks, four marks every half metre or so
        var lc = lastCart;
        if (!lc.set)
        {
            lc.x = c.x;
            lc.z = c.z;
            lc.set = true;
        }
        tireDist += JS.Hypot(c.x - lc.x, c.z - lc.z);
        lc.x = c.x;
        lc.z = c.z;
        if (tireDist >= 0.55 && Math.Abs(c.speed) > 0.4)
        {
            tireDist = 0;
            double fx = -Math.Sin(c.heading);
            double fz = -Math.Cos(c.heading);
            double rx = Math.Cos(c.heading);
            double rz = -Math.Sin(c.heading);
            foreach (var axle in new (double along, double steer)[] { (-1.1, 0), (1.0, c.steerAngle * 0.5) })
            {
                foreach (var side in new[] { -0.82, 0.82 })
                {
                    tracks.tire(c.x + fx * axle.along + rx * side, c.z + fz * axle.along + rz * side, c.heading + axle.steer);
                }
            }
        }
    }

    /// <summary>The dog's resting place: a free spot near the fire, facing it.</summary>
    private void placeDogHome()
    {
        var spots = new List<(double x, double z)>();
        foreach (var r in new[] { 3.6, 4.4, 5.2 })
        {
            for (int a = 0; a < 16; a++) spots.Add((Math.Cos((a / 16.0) * Math.PI * 2) * r, CAMP_Z + Math.Sin((a / 16.0) * Math.PI * 2) * r));
        }
        spots.Sort((p, q) => JS.Hypot(p.x + 1.2, p.z - CAMP_Z - 3).CompareTo(JS.Hypot(q.x + 1.2, q.z - CAMP_Z - 3)));
        var chosen = spots.FirstOrDefault(s => camp.obstacles.All(o => JS.Hypot(s.x - o.x, s.z - o.z) > o.r + 0.9));
        if (chosen.x == 0 && chosen.z == 0 && spots.Count > 0) chosen = spots[0];
        dog.setHome(chosen.x, chosen.z, Math.Atan2(-(0 - chosen.x), -(CAMP_Z - chosen.z)));
    }

    /// <summary>The player offers a bone: the dog picks a buried coin or piece of trash (never a Lost & Found item) and runs off to it.</summary>
    private void giveBone()
    {
        if (dog.away) return;
        var buried = spawner.items.Where(i => !i.collected && !i.revealed).ToList();
        var cands = buried.Where(i => i.def.category == "coin" || i.def.category == "trash").ToList();
        // very rarely the dog's nose leads it to something from the Lost & Found board that is still wanted
        var wanted = new HashSet<string>(state.activeQuestDefIds());
        var lost = buried.Where(i => i.def.category == "quest" && wanted.Contains(i.def.id)).ToList();
        bool special = lost.Count > 0 && JS.Random() < DOG_LOST_CHANCE;
        if (cands.Count == 0 && !special)
        {
            ui.toast("The dog sniffs around the beach, but there is nothing left to find. Keep your bone.");
            audio.error();
            return;
        }
        // somewhere not too far from camp, so the trip is a short one
        var near = cands.Where(i => JS.Hypot(i.x, i.z - CAMP_Z) < 70).ToList();
        var pool = special ? lost : near.Count > 0 ? near : cands;
        var item = pool[(int)Math.Floor(JS.Random() * pool.Count)];
        state.useBone();
        discover("dog_bone");
        if (item.def.category == "quest") discover("dog_lost");
        dog.startMission(item);
        audio.yip();
        ui.toast(special ? "The dog gulps down the bone, sniffs the air... and bolts off, nose twitching. It has caught a very special scent!" : "The dog gulps down the bone, sniffs the air... and bolts off!");
    }

    /// <summary>Puts the stones down, well away from every buried find, so none can ever be hidden or blocked by one.</summary>
    private void scatterStones()
    {
        var clear = spawner.items.Where(i => !i.collected).Select(i => new Spot { x = i.x, z = i.z, r = 0 }).ToList();
        clear.Add(new Spot { x = 0, z = CAMP_Z, r = 13 });
        clear.Add(new Spot { x = cart.x, z = cart.z, r = 3.5 });
        stones.scatter(clear);
        grass.scatter(clear);
    }

    // ---------------------------------------------------------------- journal

    /// <summary>set while the player is lying in the hammock; time runs fast until the next morning</summary>
    private HammockState hammock = null;
    private double hammockSpeed = 1;

    private void enterHammock()
    {
        if (hammock != null || driving || gameState != "EXPLORE" || transitioning) return;
        setBinocView(false);
        if (drawing) cancelDraw();
        walkTo = null;
        hammock = new HammockState { startDay = state.data.day, t = 0 };
        hammockSpeed = 1;
        view.setTool(0);
        view.proxy.visible = false;
        detector.setActive(false);
        dig.setActive(false);
        ui.setTool(0);
        var h = camp.hammockSpot();
        player.x = h.x;
        player.z = h.z;
        player.yaw = h.yaw;
        camp.setHammockOccupied(true);
        ui.setSleep(true);
        discover("hammock_first");
        audio.ui();
    }

    /// <summary>Gets up: the tools come back, warmth is full again, and the player stands beside the hammock.</summary>
    private void leaveHammock(bool morning)
    {
        if (hammock == null) return;
        hammock = null;
        hammockSpeed = 1;
        camp.setHammockOccupied(false);
        ui.setSleep(false);
        var h = camp.hammockSpot();
        player.x = h.exitX;
        player.z = h.exitZ;
        player.pitch = -0.1;
        view.proxy.visible = true;
        view.setTool(tool);
        ui.setTool(tool);
        detector.setActive(tool == 1);
        dig.setActive(tool == 2);
        camera.fov = baseFov;
        camera.updateProjectionMatrix();
        state.restoreWarmth();
        audio.ui();
        ui.toast(morning ? $"Good morning! Day {state.data.day}. You feel warm and rested." : "You get up. A bit of rest never hurt.");
    }

    /// <summary>The view from the hammock: looking up through the palms, swaying. Time races on until it is morning again.</summary>
    private void updateHammock(double dt)
    {
        var hm = hammock;
        hm.t += dt;
        hammockSpeed += (26 - hammockSpeed) * Math.Min(1, dt * 1.5);
        var h = camp.hammockSpot();
        double sway = Math.Sin(hm.t * 0.9);
        moving = false;
        camera.position.set(h.x, h.y + 0.5 + Math.Sin(hm.t * 1.7) * 0.015, h.z);
        camera.rotation.set(0.62 + Math.Sin(hm.t * 0.5) * 0.04, h.yaw + Math.Sin(hm.t * 0.35) * 0.08, sway * 0.05);
        feet.set(h.x, h.y - 0.4, h.z);
        // a new day has begun and the light is up: wake up
        if (state.data.day > hm.startDay && dayTime > 0.04) leaveHammock(true);
    }

    private bool bookOpen = false;
    /// <summary>what the reader overlay is showing: the journal or the post</summary>
    private string readerKind = "book";

    /// <summary>Writes a story into the journal the first time something happens.</summary>
    private void discover(string id)
    {
        if (started) achievements.page(id);
        if (!started || !state.unlockPage(id)) return;
        // the whole book is open from the start, so there is no announcement; the page just stops saying "to discover"
        if (BOOK_SHOW_ALL) return;
        if (PAGE_BY_ID.TryGetValue(id, out var page) && page != null) ui.toast($"📖 New journal page: {page.title}");
    }

    private void openBook()
    {
        if (bookOpen || gameState != "EXPLORE") return;
        bookOpen = true;
        readerKind = "book";
        paused = true;
        input.keys.Clear();
        document.exitPointerLock();
        audio.ui();
        audio.setOceanLevel(0.02);
        var unread = new HashSet<string>(state.data.unreadPages ?? new List<string>());
        var shown = BOOK_SHOW_ALL ? JOURNAL_PAGES.ToList() : JOURNAL_PAGES.Where(p => state.data.pages.Contains(p.id)).ToList();
        ui.showBook(
            shown.Select(p => new BookPage
            {
                id = p.id,
                group = p.group,
                icon = p.icon,
                title = p.title,
                text = p.text,
                initial = p.initial,
                isNew = unread.Contains(p.id),
                found = state.data.pages.Contains(p.id),
            }).ToList(),
            () => closeBook()
        );
    }

    /// <summary>The post at camp: read the letters the owners have sent.</summary>
    private void openMail()
    {
        if (bookOpen || gameState != "EXPLORE") return;
        bookOpen = true;
        readerKind = "mail";
        paused = true;
        input.keys.Clear();
        document.exitPointerLock();
        audio.ui();
        audio.setOceanLevel(0.02);
        var unread = new HashSet<string>(state.data.unreadLetters ?? new List<string>());
        var got = (state.data.letters ?? new List<string>())
            .Select(id => LETTER_BY_ID.TryGetValue(id, out var l) ? l : null)
            .Where(l => l != null)
            .ToList();
        ui.showMail(
            got.Select(l => new MailLetter
            {
                id = l.id,
                from = l.from,
                greeting = l.greeting,
                text = l.text,
                sign = l.sign,
                isNew = unread.Contains(l.id),
            }).ToList(),
            () => closeBook()
        );
    }

    /// <summary>The time around noon, when the sun stands high enough to reach down the shaft in the ceiling.</summary>
    private bool isMidday => dayTime > 0.2 && dayTime < 0.34;

    /// <summary>The great door takes the medallion as its key, and the astrolabe to catch the noon sun on the lock.</summary>
    private void tryGreatDoor()
    {
        if (bookOpen || gameState != "EXPLORE") return;
        discover("great_door");
        var made = state.data.assembled;
        if (!made.Contains("medallion"))
        {
            audio.thud();
            ui.toast("It will not move. The lock is round and flat, the size of a large medallion.");
        }
        else if (!made.Contains("astrolabe"))
        {
            audio.thud();
            ui.toast("The medallion fits the lock, but it will not turn. Rings are cut around it, like the rings of an old brass astrolabe.");
        }
        else if (!isMidday)
        {
            audio.thud();
            ui.toast("The medallion and the astrolabe fit, but the lock stays dark. Your astrolabe needs the sun: come back around midday.");
        }
        else
        {
            state.data.doorOpen = true;
            state.emit();
            cellar.openDoor();
            audio.discover();
            audio.thud();
            discover("door_open");
            ui.toast("The sun catches the astrolabe, the medallion turns, and the great door opens. Cold sea air comes up the stairs.");
        }
    }

    /// <summary>The end of the story: the gold, then a last card, and the game carries on.</summary>
    private void takeHoard()
    {
        if (bookOpen || gameState != "EXPLORE" || state.data.hoardTaken) return;
        var d = state.data;
        d.hoardTaken = true;
        state.addCoins(HOARD_VALUE);
        cellar.cave.setTaken(true);
        discover("hoard_found");
        bookOpen = true;
        readerKind = "story";
        paused = true;
        input.keys.Clear();
        document.exitPointerLock();
        audio.discover();
        audio.setEnding(true);
        int finds = d.found.Values.Sum();
        ui.showEnding(
            new List<string[]>
            {
                new[] { d.day == 1 ? "day on the beach" : "days on the beach", d.day.ToString() },
                new[] { "finds cleaned", finds.ToString() },
                new[] { "gold found", $"{JS.Str(HOARD_VALUE)}g" },
            },
            () =>
            {
                audio.setEnding(false);
                closeBook();
                autoSave();
                ui.toast("The camper has something new for you: a Luxury page in its shop.");
            }
        );
    }

    /// <summary>Puts everything the player has bought with the gold on show.</summary>
    private void applyLuxury()
    {
        bool has(string id) => state.data.luxury.Contains(id);
        view.setGolden(has("gold_detector"), has("gold_trowel"));
        if (has("gold_cart")) cart.setGilded();
        if (has("marigold_boat")) boat.setMarigold();
        if (has("monster_skull")) camp.showSkull();
        if (has("gold_bone")) dog.setGoldenBone();
    }

    private void openStory()
    {
        if (bookOpen || gameState != "EXPLORE") return;
        bookOpen = true;
        readerKind = "story";
        paused = true;
        input.keys.Clear();
        document.exitPointerLock();
        audio.ui();
        audio.setOceanLevel(0.006);
        discover("captain_journal");
        ui.showStory("The captain’s journal", CAPTAIN_PAGES, () => closeBook());
    }

    /// <summary>A letter has been written: it will reach the post after a short while.</summary>
    private void sendLetter(string noteId)
    {
        if (!LETTERS.Any(l => l.id == noteId)) return;
        state.queueLetter(noteId, 40 + JS.Random() * 30);
    }

    private void closeBook()
    {
        if (!bookOpen) return;
        bookOpen = false;
        paused = false;
        ui.hideBook();
        if (readerKind == "mail") state.markLettersRead();
        else if (readerKind == "book") state.markPagesRead();
        audio.ui();
        audio.setOceanLevel(underground ? 0.006 : 0.06);
        input.keys.Clear();
        grabMouse();
    }

    /// <summary>The radio's second button: tune to the next station, switching the radio on if it was off.</summary>
    private void changeSong()
    {
        audio.setTrack(audio.trackNumber + 1);
        if (!audio.radioOn)
        {
            audio.setRadio(true);
            camp.setRadio(true);
        }
        audio.ui();
        discover("radio_change");
        ui.toast($"📻 {audio.trackName}  ({audio.trackNumber + 1} of {audio.trackCount})");
    }

    private double mapPaintT = 0;
    private double mapListT = 0;
    private bool campLightsNear = true;
    private CastleNoteState lastCastleNote = new CastleNoteState();

    /// <summary>A short warning from the bucket, never repeated while the player keeps clicking.</summary>
    private void castleNote(string msg)
    {
        double now = JS.Now();
        if (msg == lastCastleNote.msg && now - lastCastleNote.at < 4000) return;
        lastCastleNote = new CastleNoteState { msg = msg, at = now };
        audio.error();
        ui.toast(msg);
    }

    /// <summary>The bucket: start a castle on the sand, or add the next stage to the one you are aiming at. Success is shown by the castle itself, not by messages.</summary>
    private void buildCastle()
    {
        var at = toolHit;
        if (at == null) return;
        double h = beach.heightAt(at.x, at.z);
        var near = castles.nearest(at.x, at.z, 1.7);
        void puff() => dig.particles.burst(new THREE.Vector3(at.x, h + 0.15, at.z), 16, 1.2, 1.6, 0.7, new THREE.Color(0xdcc38f));
        if (near != null)
        {
            if (!castles.upgrade(near)) return; // finished: the hint at the bottom already says so
            puff();
            audio.sandBuild();
            if (near.stage >= 3) discover("castle_grand");
            return;
        }
        if (h < WATER_LEVEL + 0.35) { castleNote("Too close to the water. Build a little higher up the sand."); return; }
        if (JS.Hypot(at.x, at.z - CAMP_Z) < 12) { castleNote("Too close to camp. Go out onto the open beach."); return; }
        if (castles.nearest(at.x, at.z, 2.6) != null) { castleNote("That is too close to another castle."); return; }
        if (castles.count >= MAX_CASTLES) { castleNote($"You have {MAX_CASTLES} castles already. Right-click one to knock it down."); return; }
        castles.add(at.x, at.z);
        puff();
        audio.sandBuild();
        discover("castle_first");
    }

    /// <summary>Knocks down the castle you are aiming at.</summary>
    private void knockCastle()
    {
        var at = toolHit;
        if (at == null) return;
        var c = castles.nearest(at.x, at.z, 1.7);
        if (c == null) return;
        dig.particles.burst(new THREE.Vector3(c.x, beach.heightAt(c.x, c.z) + 0.3, c.z), 28, 1.8, 1.6, 0.9, new THREE.Color(0xdcc38f));
        castles.remove(c);
        audio.crumble();
    }

    /// <summary>The island under a point, or null on the beach and out at sea.</summary>
    private Island islandAt(double x, double z)
    {
        foreach (var i in islands) if (i.contains(x, z)) return i;
        return null;
    }

    /// <summary>True while the player is on foot on one of the islands.</summary>
    private bool onIsland => started && vehicle == null && islandAt(player.x, player.z) != null;

    /// <summary>Why the trowel and the detector are no use on the island the player is standing on.</summary>
    private string noDigHint()
    {
        var isl = islandAt(player.x, player.z);
        return isl != null && isl.kind == 1 ? "The sand here is only a skin over old coral: there is nothing to dig. The cay's treasures lie out in the open."
            : isl != null && isl.kind == 2 ? "Black rock, all the way down. There is nothing to dig: the treasures here lie out in the open."
            : "The island is too rocky to dig. Its treasures lie out in the open.";
    }

    /// <summary>
    /// Four sealed chests are buried on the beach from the start. They are invisible to the detector and the trowel until the player
    /// takes the old map from the island's altar; the map then shows where each one lies.
    /// </summary>
    private void ensureTreasureCaches()
    {
        bool has = state.data.hasTreasureMap;
        spawner.secretsOpen = has;
        if (has) island.takeMap();
        // chests from an earlier version were buried deeper than they needed to be
        foreach (var i in spawner.items)
        {
            if (i.def.id == "treasure_cache" && !i.revealed && !i.collected) i.targetDepth = Math.Min(i.targetDepth, 0.45);
        }
        if (!spawner.items.Any(i => i.def.id == "treasure_cache"))
        {
            if (DEF_BY_ID.TryGetValue("treasure_cache", out var def) && def != null)
            {
                // one in each quarter of the open beach, away from the camp and from other finds
                var zones = new (double x0, double x1, double z0, double z1)[] { (-52, -14, -38, -6), (14, 52, -38, -6), (-52, -14, 2, 24), (14, 52, 2, 24) };
                foreach (var z in zones)
                {
                    for (int tries = 0; tries < 80; tries++)
                    {
                        double px = z.x0 + JS.Random() * (z.x1 - z.x0);
                        double pz = z.z0 + JS.Random() * (z.z1 - z.z0);
                        if (spawner.items.Any(i => JS.Hypot(i.x - px, i.z - pz) < 3)) continue;
                        spawner.addBuried(def, px, pz, 0.3 + JS.Random() * 0.15);
                        break;
                    }
                }
            }
        }
        spawner.checkReveals();
    }

    /// <summary>True when the player is within a few metres of a buried chest the treasure map marks.</summary>
    private bool nearCache()
    {
        var d = state.data;
        return spawner.items.Any(i => ((i.def.id == "treasure_cache" && d.hasTreasureMap) || (i.def.id == "captain_chest" && d.hasBottleMap)) && !i.collected && !i.revealed && JS.Hypot(i.x - player.x, i.z - player.z) < 7);
    }

    /// <summary>The captain's strongbox lies buried on the main beach from the start; only the bottle's map shows where.</summary>
    private void ensureCaptainChest()
    {
        spawner.bottleOpen = state.data.hasBottleMap;
        if (!spawner.items.Any(i => i.def.id == "captain_chest"))
        {
            if (DEF_BY_ID.TryGetValue("captain_chest", out var def) && def != null)
            {
                // the emptiest spot out of a good many tries; whatever still lies close is moved away just below
                var best = (x: 55.0, z: -10.0, clear: -1.0);
                for (int tries = 0; tries < 120; tries++)
                {
                    bool east = JS.Random() < 0.5;
                    double x = (east ? 1 : -1) * (50 + JS.Random() * 14);
                    double z = -32 + JS.Random() * 46;
                    double clear = double.PositiveInfinity;
                    foreach (var i in spawner.items) clear = Math.Min(clear, JS.Hypot(i.x - x, i.z - z));
                    if (clear > best.clear) best = (x, z, clear);
                }
                spawner.addBuried(def, best.x, best.z, 0.3 + JS.Random() * 0.15);
            }
        }
        // nothing else is ever buried near the hatch: any find that lies close to it is moved out of the way
        var hatch = spawner.items.FirstOrDefault(i => i.def.id == "captain_chest");
        if (hatch != null) spawner.keepClear(hatch.x, hatch.z, 5, hatch);
        spawner.checkReveals();
    }

    /// <summary>The top of the sun shaft: an old grate in the sand a few steps from the hatch, there to be noticed from the first day.</summary>
    private void placeGrate()
    {
        var hatch = spawner.items.FirstOrDefault(i => i.def.id == "captain_chest");
        if (hatch == null) return;
        bool clear(double x, double z) =>
            Math.Abs(x) < HALF - 4 &&
            stones.colliders.All(s => JS.Hypot(x - s.x, z - s.z) > s.r + 1.8) &&
            scenery.trunks.All(t => JS.Hypot(x - t.x, z - t.z) > 2);
        var spots = new (double dx, double dz)[] { (-1.85, 4.5), (1.85, 4.5), (-1.85, -4.5), (1.85, -4.5), (4.5, 1.85), (-4.5, 1.85), (4.5, -1.85), (-4.5, -1.85) };
        var found = spots[0];
        foreach (var sp in spots)
        {
            if (clear(hatch.x + sp.dx, hatch.z + sp.dz)) { found = sp; break; }
        }
        double gx = hatch.x + found.dx;
        double gz = hatch.z + found.dz;
        grate = new XZ { x = gx, z = gz };
        // the stone sits on sand nobody has dug: a hole made under it in an older save is filled in again
        tidyGrateGround();
        var g = buildGrate();
        g.position.set(gx, beach.heightAt(gx, gz), gz);
        scene.add(g);
        camp.obstacles.Add(new Camp.Solid { x = gx, z = gz, r = 1.3 });
        // nothing lies buried under the stone, or close enough to it that digging it out would mean digging under the stone
        spawner.keepClear(gx, gz, GRATE_CLEAR);
        spawner.checkReveals();
    }

    /// <summary>Keeps the sand under the grate as it was, with any hole dug beside it easing up to meet the stone.</summary>
    private void tidyGrateGround()
    {
        if (grate != null) beach.shapePit(grate.x, grate.z, 0, GRATE_R, GRATE_R + 0.15, GRATE_R + 1.3, 0);
    }

    /// <summary>The map from the bottle is now in the map menu.</summary>
    private void takeBottleMap()
    {
        // a hole dug there before the map was found does not count: the ground is made smooth again, so the hatch has to be dug for properly
        var hatch = spawner.items.FirstOrDefault(i => i.def.id == "captain_chest");
        if (hatch != null && !hatch.revealed)
        {
            beach.clearDug(hatch.x, hatch.z, 4);
            holes = holes.Where(h => JS.Hypot(h[0] - hatch.x, h[1] - hatch.z) > 4.5).ToList();
        }
        state.data.hasBottleMap = true;
        state.emit();
        spawner.bottleOpen = true;
        spawner.checkReveals();
        discover("bottle_map");
    }

    /// <summary>Picks the old map up off the altar.</summary>
    private void takeTreasureMap()
    {
        if (state.data.hasTreasureMap) return;
        state.data.hasTreasureMap = true;
        state.emit();
        island.takeMap();
        spawner.secretsOpen = true;
        spawner.checkReveals();
        audio.discover();
        discover("map_taken");
        ui.toast("You take an old map from the altar. Open the map (M) and press T to read it.");
    }

    /// <summary>The islands' own finds lie in the open from the start. Added once, and again for older saves that never had them.</summary>
    private void ensureIslandFinds()
    {
        // a save from before an island moved may hold finds out at the old spot: drop the ones not picked up yet
        spawner.items.RemoveAll(i => ISLAND_ITEM_IDS.Contains(i.def.id) && !i.collected && !islands.Any(isl => isl.findIds.Contains(i.def.id) && isl.contains(i.x, i.z)));
        var have = new Dictionary<string, int>();
        foreach (var i in spawner.items)
        {
            if (ISLAND_ITEM_IDS.Contains(i.def.id))
            {
                have[i.def.id] = (have.TryGetValue(i.def.id, out var cnt) ? cnt : 0) + 1;
            }
        }
        foreach (var s in islands.SelectMany(isl => isl.findSpots()))
        {
            int n = have.TryGetValue(s.id, out var cnt) ? cnt : 0;
            if (n > 0)
            {
                have[s.id] = n - 1;
                continue;
            }
            if (DEF_BY_ID.TryGetValue(s.id, out var def) && def != null) spawner.addOpen(def, s.x, s.z);
        }
        spawner.checkReveals();
    }

    private int petCount = 0;

    /// <summary>A bit of love for the dog: it shakes its head and wags, and little hearts float up.</summary>
    private void petDog()
    {
        if (dog.state != "rest") return;
        dog.pet();
        discover("dog_pet");
        audio.yip();
        var at = new THREE.Vector3(dog.x, beach.heightAt(dog.x, dog.z) + 0.75, dog.z);
        hearts.burst(at, 6, 0.4, 0.7, 1.9, new THREE.Color(0xff4d6d));
        var lines = new[]
        {
            "You scratch behind its ears. The dog shakes its head happily.",
            "Good dog! It leans into your hand and wags its whole body.",
            "The dog licks your hand and gives a happy little shake.",
        };
        ui.toast(lines[petCount++ % lines.Length]);
        if (state.data.bones <= 0 && petCount % 3 == 1) ui.toast("A bone from the camper shop would get it fetching.");
    }

    private DogEnv dogEnv;
    private THREE.Color dogDustColor = new THREE.Color(0xe6ccb2);
    private Waypoint dogMapPos = new Waypoint { x = 0, z = 0 };
    private static readonly Dictionary<string, double> CAMP_REACH = new Dictionary<string, double>
    {
        { "fire", 3.2 }, { "bin", 2.4 }, { "board", 2.8 }, { "bench", 2.4 }, { "lantern", 2.2 }, { "camper", 3.6 },
        { "radio", 2.6 }, { "coffee", 2.2 }, { "book", 2.6 }, { "post", 2.6 }, { "hammock", 2.8 }, { "sign", 2.6 }, { "photos", 2.8 }, { "aquarium", 2.8 },
    };

    private void updateDog(double dt, double t)
    {
        dogEnv.playerX = player.x;
        dogEnv.playerZ = player.z;
        // a raised camera makes a resting dog sit up and pose
        dog.cameraUp = camView && tool == 3 && gameState == "EXPLORE";
        dog.update(dt, t, dogEnv);
        if (dog.state == "find" && dogWas == "run")
        {
            discover("dog_found");
            ui.toast("Woof! The dog has found something. Follow the paw on your map and dig there.");
        }
        if (dog.state == "find" && dog.item != null && dog.item.collected)
        {
            dog.recall();
            audio.yip();
            ui.toast("Good dog! It trots back to camp.");
        }
        dogWas = dog.state;
        if (dog.away)
        {
            dogMapPos.x = dog.x;
            dogMapPos.z = dog.z;
            mapView.dog = dogMapPos;
        }
        else
        {
            mapView.dog = null;
        }
    }

    private THREE.Vector3 radioPos = new THREE.Vector3();
    private Interaction interaction = null;
    private THREE.Vector3 tmpPos = new THREE.Vector3();

    private THREE.Vector3 aimDir = new THREE.Vector3();
    private THREE.Vector3 aimTo = new THREE.Vector3();

    /// <summary>
    /// How well the crosshair covers something of about `r` metres at this spot: 1 dead centre, up to 2 at the edge of the
    /// allowance, null when it is looked away from. Standing right on top of it always counts.
    /// </summary>
    private double? aim(double x, double y, double z, double r)
    {
        var to = aimTo.set(x, y, z).sub(camera.position);
        double dist = to.length();
        if (dist < r * 0.6) return 1.5;
        double ang = Math.Acos(Math.Min(1, Math.Max(-1, to.dot(aimDir) / dist)));
        double tol = Math.Atan2(r, dist) + 0.2;
        return ang > tol ? (double?)null : 1 + ang / tol;
    }

    /// <summary>Works out what the player is standing close enough to use (the F prompt).</summary>
    private void updateInteraction()
    {
        view.setDigTier(state.tier("digging"));
        camera.getWorldDirection(aimDir);
        interaction = null;
        interaction2 = null;
        ui.setPrompt2(null);
        if (hammock != null)
        {
            ui.setPrompt(null);
            return;
        }
        if (!started || gameState != "EXPLORE" || transitioning)
        {
            ui.setPrompt(null);
            return;
        }
        if (seated)
        {
            // no prompt in the middle of the view while fishing; the hint line says how to get up
            interaction = new Interaction { label = "Stand up", run = () => standUp() };
            ui.setPrompt(null);
            return;
        }
        if (underground)
        {
            if (ladderY >= cellar.ladderH - 0.15) interaction = new Interaction { label = "Climb out through the hatch", run = () => leaveUnderground() };
            else if (cellar.nearDoor(player.x, player.z)) interaction = new Interaction { label = "Try the great door", run = () => tryGreatDoor() };
            else if (!state.data.hoardTaken && cellar.nearGold(player.x, player.z)) interaction = new Interaction { label = "Take the gold", run = () => takeHoard() };
            else if (cellar.nearBook(player.x, player.z)) interaction = new Interaction { label = "Read the captain’s journal", run = () => openStory() };
            ui.setPrompt(interaction?.label);
            ui.setPrompt2(null);
            return;
        }
        if (diving)
        {
            // F picks up a find within reach; Q always swims back up
            var cp = camera.position;
            BuriedItem near = null;
            double nd = 2.8;
            foreach (var it in spawner.items)
            {
                if (!it.revealed || it.collected || !under.contains(it.x, it.z)) continue;
                double d = JS.Hypot(cp.x - it.x, cp.y - under.floorAt(it.x, it.z), cp.z - it.z);
                if (d < nd)
                {
                    nd = d;
                    near = it;
                }
            }
            if (near != null)
            {
                var found = near;
                interaction = new Interaction { label = "Pick up and clean this find", run = () => enterInspect(found) };
            }
            ui.setPrompt(interaction?.label);
            ui.setPrompt2(null);
            return;
        }
        if (vehicle == "boat")
        {
            interaction = driving
                ? new Interaction { label = "Leave the wheel and stand on deck", run = () => leaveHelm() }
                : new Interaction { label = "Take the wheel", run = () => takeHelm() };
            // you can only get off where the water is shallow enough to wade to the beach
            if (nearShore()) interaction2 = new Interaction { label = landingSpot().island ? "Step off the boat onto the island" : "Step off the boat onto the beach", run = () => exitVehicle() };
            ui.setPrompt(interaction.label);
            ui.setPrompt2(interaction2?.label);
            return;
        }
        if (driving)
        {
            interaction = new Interaction { label = "Get out of the golf cart", run = () => exitVehicle() };
            ui.setPrompt(interaction.label);
            return;
        }
        double best = double.PositiveInfinity;
        if (JS.Hypot(player.x, player.z - CAMP_Z) < 16)
        {
            var reach = CAMP_REACH;
            var labels = new Dictionary<string, string>
            {
                { "fire", "Rest by the campfire" },
                { "bin", "Recycle your trash" },
                { "board", "Read the Lost & Found board" },
                { "aquarium", state.aquarium.Count > 0 ? $"Look at the aquarium ({state.aquarium.Count} of {Catches.ALL.Length})" : "Look at the aquarium" },
                { "photos", state.photos.Count > 0 ? $"Look at your photos ({state.photos.Count})" : "Look at the photo board" },
                { "bench", "Use the workbench" },
                { "lantern", lanternOn ? "Turn the lantern off" : "Turn the lantern on" },
                { "camper", "Open the camper (upgrades)" },
                { "radio", audio.radioOn ? $"Turn the radio off ({audio.trackName})" : "Turn the radio on" },
                { "coffee", "Drink the coffee (rest and warm up)" },
                { "hammock", "Lie in the hammock (sleep until morning)" },
                { "sign", "Change the sign" },
                { "post", (state.data.unreadLetters?.Count ?? 0) > 0 ? $"Check the post ({state.data.unreadLetters.Count} new)" : "Check the post" },
                { "book", (state.data.unreadPages?.Count ?? 0) > 0 ? $"Read the journal ({state.data.unreadPages.Count} new)" : "Read the journal" },
            };
            foreach (var obj in camp.clickables)
            {
                string kind = obj.userData.Str("kind");
                if (string.IsNullOrEmpty(kind) || !reach.ContainsKey(kind)) continue;
                obj.getWorldPosition(tmpPos);
                double score = JS.Hypot(player.x - tmpPos.x, player.z - tmpPos.z) / reach[kind];
                // the radio sits on the workbench, so it wins ties when you stand right next to it
                double? aimVal = score < 1 ? aim(tmpPos.x, tmpPos.y, tmpPos.z, Math.Max(0.7, reach[kind] * 0.4)) : null;
                double rank = (kind == "radio" || kind == "coffee" || kind == "book" || kind == "post" ? score * 0.6 : score) * (aimVal ?? 0);
                if (aimVal != null && rank < best)
                {
                    best = rank;
                    string kCopy = kind;
                    interaction = new Interaction { label = labels[kCopy], run = () => useCampObject(kCopy) };
                    interaction2 = kCopy == "radio" ? new Interaction { label = "Change the song", run = () => changeSong() } : null;
                }
            }
        }
        {
            double score = JS.Hypot(player.x - cart.x, player.z - cart.z) / 3.6;
            double? aimVal = score < 1 ? aim(cart.x, beach.heightAt(cart.x, cart.z) + 0.8, cart.z, 1.6) : null;
            if (aimVal != null && score * 0.8 * aimVal.Value < best)
            {
                best = score * 0.8 * aimVal.Value;
                interaction = state.data.ownsCartKey
                    ? new Interaction { label = "Drive the golf cart", run = () => enterVehicle("cart") }
                    : new Interaction
                    {
                        label = "Golf cart (needs a key)",
                        run = () =>
                        {
                            audio.error();
                            ui.toast("The golf cart will not start without a key. The camper shop sells one.");
                        },
                    };
            }
        }
        {
            double score = JS.Hypot(player.x - boat.x, player.z - boat.z) / 12;
            double? aimVal = score < 1 ? aim(boat.x, 0.5, boat.z, 4) : null;
            if (aimVal != null && score * 0.85 * aimVal.Value < best)
            {
                best = score * 0.85 * aimVal.Value;
                interaction = state.data.ownsBoatKey
                    ? new Interaction { label = "Drive the boat", run = () => enterVehicle("boat") }
                    : new Interaction
                    {
                        label = "Boat (needs a key)",
                        run = () =>
                        {
                            audio.error();
                            ui.toast("The boat will not start without a key. The camper shop sells one.");
                        },
                    };
            }
        }
        {
            int fi = flags.nearest(player.x, player.z, 2.2);
            if (fi >= 0 && 0.5 < best)
            {
                best = 0.5;
                interaction = new Interaction
                {
                    label = "Pick up this flag",
                    run = () =>
                    {
                        flags.remove(fi);
                        state.returnFlag();
                        audio.ui();
                        ui.toast($"Flag picked up ({state.data.flags} in your bag).");
                    },
                };
            }
        }
        {
            double score = JS.Hypot(player.x - dog.x, player.z - dog.z) / 2.8;
            double? aimVal = !dog.away && score < 1 ? aim(dog.x, beach.heightAt(dog.x, dog.z) + 0.4, dog.z, 0.8) : null;
            if (aimVal != null && score * 0.8 * aimVal.Value < best)
            {
                best = score * 0.8 * aimVal.Value;
                if (state.data.bones > 0) interaction = new Interaction { label = "Give the dog a bone", run = () => giveBone() };
                // the second button (E, or X on a controller) is always for a little love
                interaction2 = new Interaction { label = "Pet the dog", run = () => petDog() };
            }
        }
        if (grate != null && JS.Hypot(player.x - grate.x, player.z - grate.z) < 2.6 && 0.5 < best)
        {
            best = 0.5;
            interaction = new Interaction
            {
                label = "Look through the grate",
                run = () =>
                {
                    audio.ui();
                    ui.toast("An old iron grate, set in cut stone. Cold air rises through the bars. It seems there is something underground here.");
                },
            };
        }
        if (!state.data.hasTreasureMap && island.nearMap(player.x, player.z) && 0.3 < best)
        {
            best = 0.3;
            interaction = new Interaction { label = "Take the old map from the altar", run = () => takeTreasureMap() };
        }
        foreach (var it in spawner.items)
        {
            if (!it.revealed || it.collected) continue;
            double score = JS.Hypot(player.x - it.x, player.z - it.z) / 2.2;
            double? aimVal = score < 1 ? aim(it.x, beach.heightAt(it.x, it.z), it.z, it.def.id == "captain_chest" ? 1.5 : 0.6) : null;
            if (aimVal != null && score * aimVal.Value < best)
            {
                best = score * aimVal.Value;
                var itemCopy = it;
                interaction = itemCopy.def.id == "captain_chest"
                    ? new Interaction { label = itemCopy.opened ? "Climb down the ladder" : "Open the hatch", run = () => useFind(itemCopy) }
                    : new Interaction { label = "Pick up and clean this find", run = () => enterInspect(itemCopy) };
            }
        }
        if (JS.Hypot(player.x - Pier.SEAT.x, player.z - Pier.SEAT.z) < 2.1 && 0.35 < best)
        {
            best = 0.35;
            interaction = state.data.ownsRod
                ? new Interaction { label = "Sit down and fish", run = () => sitDown() }
                : new Interaction
                {
                    label = "Fishing bench (needs a rod)",
                    run = () =>
                    {
                        audio.error();
                        ui.toast("You need a fishing rod to fish here. The camper shop sells one.");
                    },
                };
        }
        if (interaction == null && canDive())
        {
            interaction = new Interaction { label = state.data.ownsDiveGear ? "Dive in" : "Dive in (needs diving gear)", run = () => enterDive() };
        }
        ui.setPrompt(interaction?.label);
        ui.setPrompt2(interaction2?.label);
    }

    /// <summary>with the radio on the music plays everywhere; the tune follows the place the player is in</summary>
    private void updateMusic()
    {
        bool menu = gameState == "MENU";
        audio.setMenu(menu);
        if (menu)
        {
            audio.setMusicLevel(1, 1);
            return;
        }
        if (!audio.radioOn) return;
        audio.setPlace(underground ? 4 : diving ? 3 : sky.isDark ? 2 : 1);
        audio.setMusicLevel(paused ? 0.08 : 1, 1);
    }

    private void updateHover()
    {
        hoveredItem = null;
        hoveredCamp = null;
        toolHit = null;
        if (!started || driving || gameState == "INSPECT" || (campModal.isOpen && isOverPanel())) return;
        if (underground) return;
        if (diving)
        {
            // underwater only the finds can be looked at
            if (gameState != "EXPLORE") return;
            var hits = input.ray(camera, true).intersectObjects(spawner.pickables(), true);
            var hit = hits.Count > 0 ? hits[0] : null;
            if (hit != null && hit.distance < 6) hoveredItem = spawner.itemFromObject(hit.@object);
            return;
        }
        var ray = input.ray(camera, gameState == "EXPLORE");
        bool nearCamp = gameState != "EXPLORE" || JS.Hypot(player.x, player.z - CAMP_Z) < 22;
        var beachHit = beach.raycast(ray.ray.origin, ray.ray.direction, 12);
        double beachDist = beachHit != null ? beachHit.distanceTo(ray.ray.origin) : double.PositiveInfinity;
        var itemHits = gameState == "EXPLORE" ? ray.intersectObjects(spawner.pickables(), true) : null;
        var itemHit = itemHits != null && itemHits.Count > 0 ? itemHits[0] : null;
        var campHits = nearCamp ? ray.intersectObjects(camp.clickables, true) : null;
        var campHit = campHits != null && campHits.Count > 0 ? campHits[0] : null;
        if (itemHit != null && itemHit.distance < 6 && itemHit.distance < (campHit?.distance ?? double.PositiveInfinity))
        {
            hoveredItem = spawner.itemFromObject(itemHit.@object);
        }
        else if (campHit != null && campHit.distance < 8 && campHit.distance < beachDist + 0.5)
        {
            hoveredCamp = camp.kindOf(campHit.@object);
        }
        // the detector only sweeps the sand close to the player; the trowel reaches a little less far
        bool near = beachHit != null && (tool == 2 || tool == 7 || tool == 8
            ? beachDist < 5.5
            : JS.Hypot(beachHit.x - player.x, beachHit.z - player.z) < DETECT_REACH);
        if (gameState == "EXPLORE" && hoveredItem == null && hoveredCamp == null && near)
        {
            toolHit = beachHit;
        }
        if (onIsland && (tool == 1 || tool == 2)) toolHit = null;
    }

    private bool isOverPanel()
    {
        // the camp panel hugs the right edge of the screen
        double px = (input.pointer.x + 1) / 2 * window.innerWidth;
        return px > window.innerWidth - Math.Min(624, window.innerWidth * 0.94);
    }

    private THREE.Vector2 zeroNDC = new THREE.Vector2(0, 0);
    private MapPlayer mapPlayer = new MapPlayer { x = 0, z = 0, yaw = 0 };
    private Waypoint mapCart = new Waypoint { x = 0, z = 0 };
    private Waypoint mapBoat = new Waypoint { x = 0, z = 0 };

    /// <summary>Distance to whatever is under the binocular reticle. Measured every few frames.</summary>
    private void updateRange()
    {
        if (rangeTick++ % 6 != 0) return;
        camera.updateMatrixWorld(true);
        rayCaster.setFromCamera(zeroNDC, camera);
        var ray = rayCaster.ray;
        double best = double.PositiveInfinity;
        var beachHit = beach.raycast(ray.origin, ray.direction);
        if (beachHit != null) best = beachHit.distanceTo(ray.origin);
        // the sea surface
        if (ray.direction.y < -0.0005)
        {
            double t = (ocean.surfaceY - ray.origin.y) / ray.direction.y;
            if (t > 0 && t < best) best = t;
        }
        var things = new List<THREE.Object3D> { camp.group, scenery.group, boat.group, cart.group, sea.group, birds.group, ecology.group, treasure.group };
        var hit = rayCaster.intersectObjects(things, true).FirstOrDefault(h => h.@object.visible && h.distance > 0.5);
        if (hit != null && hit.distance < best) best = hit.distance;
        rangeText = best < 900 ? $"{Math.Round(best)} m" : "—";
    }

    private void updateHint()
    {
        bool finderOn = holdingCamera && camView && gameState == "EXPLORE" && started && !paused && !driving && hammock == null;
        ui.setViewfinder(finderOn, state.photos.Count, Photos.MAX_PHOTOS);
        ui.tickFlash();
        if (hammock != null)
        {
            ui.setHint("Resting... press any key to get up");
            ui.setCrosshair(false);
            return;
        }
        if (driving)
        {
            if (vehicle == "boat")
            {
                ui.setHint("W / S throttle and reverse · A / D steer · Space slows · F leaves the wheel (to use a weapon) · E steps off when parked at the beach");
                ui.setBadgeText("🚤", "Boat");
            }
            else
            {
                ui.setHint("W / S drive and reverse · A / D steer · Space brakes · F gets out");
                ui.setBadgeText("🛺", "Golf cart");
            }
            ui.setCrosshair(false);
            return;
        }
        if (seated)
        {
            ui.setBinoculars(false, "", "");
            ui.setHint(gameState == "EXPLORE" ? fishing.hint(usingPad) : null);
            ui.setCrosshair(false);
            return;
        }
        if (underground)
        {
            ui.setBinoculars(false, "", "");
            if (finderOn)
            {
                ui.setHint(null);
                ui.setCrosshair(false);
                return;
            }
            string camKey = usingPad ? "D-pad left" : "3";
            ui.setHint(gameState == "INSPECT" ? null
                : altCamera ? $"Click to raise the camera · {camKey} puts it away"
                : $"Climb the stairs and the ladder, then press F at the top to go back out · {camKey} takes out the camera");
            ui.setCrosshair(gameState == "EXPLORE");
            return;
        }
        if (diving)
        {
            ui.setBinoculars(false, "", "");
            if (finderOn)
            {
                ui.setHint(null);
                ui.setCrosshair(false);
                return;
            }
            if (altCamera) ui.setBadgeText("📷", "Camera");
            else if (altTool == 11) ui.setBadgeText("🐠", "Net");
            else ui.setBadgeText("🔱", "Harpoon");
            string camKey = usingPad ? "D-pad left" : "3";
            ui.setHint(
                gameState == "INSPECT" ? null
                    : altTool == 11 ? (usingPad
                        ? "Press RT to sweep the net at a creature close in front of you · D-pad left changes tool · X swims back up"
                        : "Click to sweep the net at a creature close in front of you · 1 harpoon · 3 camera · Q swims back up")
                    : altCamera ? (usingPad
                        ? $"Press RT to raise the camera · {camKey} brings back the harpoon · X swims back up"
                        : $"Click to raise the camera · {camKey} brings back the harpoon · Q swims back up")
                    : hoveredItem != null ? "Click to pick it up"
                    : under.reloading ? "Reloading..."
                    : usingPad ? "RT fires the harpoon · A picks up a find · X swims back up · left stick swims · RB up · LB down · D-pad left changes tool"
                    : state.data.ownsNet ? "Click to fire the harpoon · F picks up a find · Q swims back up · WASD swim · Space up · C down · 2 net · 3 camera"
                    : "Click to fire the harpoon · F picks up a find · Q swims back up · WASD swim · Space up · C down · 3 camera"
            );
            ui.setCrosshair(gameState == "EXPLORE");
            return;
        }
        bool binoc = tool == 5 && binocView && gameState == "EXPLORE";
        if (binoc) updateRange();
        ui.setBinoculars(binoc, $"x{JS.ToFixed(baseFov / camera.fov, 0)}", rangeText);
        if (binoc || finderOn)
        {
            ui.setHint(null);
            ui.setCrosshair(false);
            return;
        }
        string hint = null;
        if (hoveredItem != null) hint = hoveredItem.def.id == "captain_chest" ? (hoveredItem.opened ? "Click to climb down the ladder" : "Click to open the hatch") : "Click to take it to the workbench";
        else if (hoveredCamp != null)
        {
            var campHints = new Dictionary<string, string>
            {
                { "fire", "Click: rest at the campfire" }, { "bin", "Click: recycling bin" }, { "board", "Click: Lost & Found board" },
                { "bench", "Click: workbench (sell & assemble)" }, { "lantern", lanternOn ? "Click: turn the lantern off" : "Click: turn the lantern on" }, { "camper", "Click: camper (upgrades)" },
                { "radio", audio.radioOn ? "Click: turn the radio off" : "Click: turn the radio on" },
                { "coffee", "Click: drink the coffee" },
                { "book", "Click: read the journal" },
                { "post", "Click: check the post" },
                { "hammock", "Click: lie in the hammock" },
                { "sign", "Click: change the sign" },
                { "photos", "Click: photo board" },
                { "aquarium", "Click: aquarium" },
            };
            campHints.TryGetValue(hoveredCamp, out hint);
        }
        else if ((tool == 1 || tool == 2) && nearCache() && !onIsland)
        {
            hint = "A cross on your map is close. Sweep with the detector and dig where the beep is fastest.";
        }
        else if (onIsland && (tool == 1 || tool == 2))
        {
            hint = noDigHint();
        }
        else if (gameState == "EXPLORE" && tool == 8)
        {
            var c = toolHit != null ? castles.nearest(toolHit.x, toolHit.z, 1.6) : null;
            hint = c != null ? (c.stage >= 3 ? "A finished castle · right-click knocks it down" : "Click to add to this castle · right-click knocks it down") : "Click to start a sandcastle on the sand";
        }
        else if (gameState == "EXPLORE" && tool == 7)
        {
            hint = $"Click to plant a flag ({state.data.flags} in your bag) · F beside a flag picks it up";
        }
        else if (gameState == "EXPLORE" && tool == 6)
        {
            hint = drawing ? "Release to shoot · right-click to let the string down" : "Hold click to draw the bow, release to shoot";
        }
        else if (gameState == "EXPLORE" && tool == 5)
        {
            hint = "Click to raise the binoculars and look through them";
        }
        else if (gameState == "EXPLORE" && tool == 3)
        {
            hint = "Click to raise the camera and look through the viewfinder";
        }
        else if (gameState == "EXPLORE" && tool == 4)
        {
            hint = "Click to shoot · crabs scuttle away, so be quick";
        }
        else if (gameState == "EXPLORE" && toolHit != null && tool == 2)
        {
            if (!beach.canDig(toolHit.z)) hint = "Too close to the dunes";
            else if (state.data.warmth <= 0) hint = "Too chilly to dig. Warm up at the campfire";
            else hint = "Click to dig";
        }
        if (hint == null && started && gameState == "EXPLORE" && !locked && !usingPad)
        {
            hint = lockFailed ? "Move the mouse toward a screen edge to turn · click to use your tool" : "Click to take control of the mouse";
        }
        if (hint == null && gameState == "EXPLORE" && tool == 2 && toolHit == null) hint = "Look at the sand closer to dig";
        if (hint == null && gameState == "EXPLORE" && tool == 1 && toolHit == null) hint = "Aim at the sand near your feet to sweep";
        ui.setHint(gameState == "INSPECT" ? null : hint);
        ui.setCrosshair(gameState == "EXPLORE");
    }

    private void frame()
    {
        // losing the mouse pauses the game (Esc, Alt-Tab), but not when it was let go because the player picked up a controller
        if (wasLocked && !locked && !usingPad && started && gameState == "EXPLORE" && !transitioning && !mapOpen && !bookOpen && hammock == null) setPaused(true);
        wasLocked = locked;

        double rawDt = clock.getDelta();
        pad.poll(Math.Min(rawDt, 0.05));
        padUpdate(Math.Min(rawDt, 0.05));
        menuTick();
        if (paused)
        {
            if (mapOpen)
            {
                time += Math.Min(rawDt, 0.05);
                mapPaintT -= rawDt;
                if (mapPaintT <= 0)
                {
                    mapPaintT = 0.12;
                    ui.repaintMap();
                }
            }
            audio.detectorEnabled = false;
            if (vehicle != null) audio.setEngine(false);
            renderer.render(gameState == "INSPECT" ? station.scene : scene, gameState == "INSPECT" ? station.camera : camera);
            return;
        }
        double dt = Math.Min(rawDt, 0.05);
        time += dt;
        autoT += dt;
        if (autoT >= AUTOSAVE_SECONDS) autoSave();
        achT += dt;
        if (achT >= 1)
        {
            achT = 0;
            checkAchievements();
        }
        double t = time;
        input.tick(dt);

        using (THREE.Bridge.BridgeStats.Time("dayNight")) updateDayNight(dt);
        using (THREE.Bridge.BridgeStats.Time("sky")) sky.update(dt, camera.position.x, camera.position.z);
        if (underground)
        {
            using (THREE.Bridge.BridgeStats.Time("ocean")) ocean.update(t, camera.position.x, camera.position.z);
            cellar.update(t, dt);
        }
        else if (diving)
        {
            // the land is hidden: only the water around the diver is alive
            under.update(dt, t, camera.position);
            // the water, and what shows past it, follow the time of day
            (scene.background as THREE.Color)?.copy(sky.fog.color);
            under.setDaylight(0.18 + 0.82 * (1 - sky.starAmount));
        }
        else
        {
            using (THREE.Bridge.BridgeStats.Time("ocean")) ocean.update(t, camera.position.x, camera.position.z);
            using (THREE.Bridge.BridgeStats.Time("beach")) beach.updateDetail(camera.position.x, camera.position.z);
            // past the reach of the camp's lights (with a little give so they do not flicker at the edge)
            double fromCamp = JS.Hypot(camera.position.x, camera.position.z - CAMP_Z);
            if ((fromCamp > (campLightsNear ? 34 : 30)) == campLightsNear)
            {
                campLightsNear = !campLightsNear;
                camp.setLightsNear(campLightsNear);
            }
            using (THREE.Bridge.BridgeStats.Time("camp")) camp.update(dt, sky.fireBoost, lanternOn, sky.starAmount);
            if (JS.Hypot(player.x - Aquarium.AT.x, player.z - CAMP_Z - Aquarium.AT.z) < 28) aquarium.update(dt, time);
            using (THREE.Bridge.BridgeStats.Time("ecology")) ecology.update(dt, player.x, player.z);
            using (THREE.Bridge.BridgeStats.Time("scenery")) scenery.update(t, sky.fog.color, dt, camera.position.x, camera.position.z);
            using (THREE.Bridge.BridgeStats.Time("grass")) grass.update(t);
            using (THREE.Bridge.BridgeStats.Time("birds")) birds.update(dt, t, player.x, player.z, d => audio.gull(d));
            using (THREE.Bridge.BridgeStats.Time("tracks")) tracks.update(dt, sky.starAmount);
            using (THREE.Bridge.BridgeStats.Time("sea")) sea.update(dt, t, ocean.surfaceY, player.x, player.z, d => audio.splash(d));
            using (THREE.Bridge.BridgeStats.Time("dolphins"))
            {
                bool came = dolphins.update(dt, t, vehicle == "boat", boat.x, boat.z, boat.heading, boat.speed, ocean.surfaceY, (x, z) => ocean.swellAt(x, z), (x, z) => beach.heightAt(x, z), d => audio.splash(d));
                if (came)
                {
                    // the first time, a word about it; after that they simply turn up
                    if (!state.data.pages.Contains("dolphins")) ui.toast("Dolphins! A pod has come up to ride along beside the boat.");
                    discover("dolphins");
                }
            }
            using (THREE.Bridge.BridgeStats.Time("whitecaps")) whitecaps.update(dt, ocean.surfaceY, player.x, player.z, sky.starAmount, (x, z) => ocean.swellAt(x, z));
            var chest = treasure.update(dt, t, ocean.surfaceY, vehicle == "boat" ? (double?)boat.x : null, vehicle == "boat" ? (double?)boat.z : null);
            if (chest != null)
            {
                state.addCoins(CHEST_REWARD);
                discover("chest_first");
                sea.splash(new THREE.Vector3(chest.x, ocean.surfaceY, chest.z), 40);
                audio.discover();
                audio.splash(3);
                int left = treasure.remaining;
                ui.toast(left > 0 ? $"Treasure chest hauled aboard! +{JS.Str(CHEST_REWARD)}g. {left} more out at sea." : $"The last treasure chest! +{JS.Str(CHEST_REWARD)}g. You found them all.");
            }
            // when nobody is driving, the boat just bobs gently at its mooring
            if (!(vehicle == "boat" && driving))
            {
                using (THREE.Bridge.BridgeStats.Time("boat")) boat.update(dt, t, null, ocean.heightAt(boat.x, boat.z), (x, z) => vehicle == "boat" ? resolveBoat(x, z) : new Resolved { x = x, z = z, hit = false });
                if (vehicle == "boat")
                {
                    double sp = Math.Abs(boat.speed) / 14;
                    audio.setEngine(sp > 0.03, sp, true);
                }
            }
        }

        camp.setBookNew((state.data.unreadPages?.Count ?? 0) > 0);
        camp.setMailNew((state.data.unreadLetters?.Count ?? 0) > 0);
        if (started && gameState != "MENU")
        {
            var arrived = state.tickMail(dt * (hammock != null ? hammockSpeed : 1));
            foreach (var id in arrived)
            {
                string from = LETTER_BY_ID.TryGetValue(id, out var l) && l != null ? l.from : "someone";
                discover("mail_first");
                audio.discover();
                ui.toast($"📬 A letter has arrived at the camp post, from {from}");
            }
        }
        if (!alt) using (THREE.Bridge.BridgeStats.Time("dog")) updateDog(dt, t);
        if (gameState != "EXPLORE") ui.setPrompt(null);
        audio.detectorEnabled = false;
        if (gameState == "INSPECT")
        {
            var k = input.keys;
            double yaw = ((k.Contains("KeyE") || k.Contains("ArrowRight") ? 1 : 0) - (k.Contains("KeyQ") || k.Contains("ArrowLeft") ? 1 : 0)) * dt * 2.2;
            double pitch = ((k.Contains("ArrowDown") ? 1 : 0) - (k.Contains("ArrowUp") ? 1 : 0)) * dt * 1.8;
            if (yaw != 0 || pitch != 0) station.rotate(yaw, pitch);
            bool scrubbing = (input.buttons & 1) == 1;
            bool painted = station.pointer(input.pointer, scrubbing && input.speed > 15, input.speed);
            if (painted) audio.brush(input.speed);
            station.update(dt);
            renderer.render(station.scene, station.camera);
            return;
        }

        audio.detectorEnabled = gameState == "EXPLORE" && tool == 1 && !driving && !alt;
        if (gameState == "MENU") updateMenuCamera(dt);
        else using (THREE.Bridge.BridgeStats.Time("camera+move")) updateCamera(dt);
        using (THREE.Bridge.BridgeStats.Time("music")) updateMusic();
        using (THREE.Bridge.BridgeStats.Time("interaction")) updateInteraction();
        using (THREE.Bridge.BridgeStats.Time("hover")) updateHover();

        if (gameState == "EXPLORE")
        {
            if (driving || alt)
            {
                /* hands are on the wheel, or on the harpoon */
            }
            else if (tool == 1)
            {
                using (THREE.Bridge.BridgeStats.Time("detector")) detector.update(dt, toolHit, state, spawner, audio, beach);
                view.setSignal(detector.signal.strength, detector.signal.kind);
                using (THREE.Bridge.BridgeStats.Time("waveform")) detector.drawWaveform(ui.waveCanvas.getContext("2d"), 240, 84, t, state.tier("detector"));
            }
            else if (tool == 2)
            {
                dig.hover(toolHit, state, toolHit != null && beach.canDig(toolHit.z) && state.data.warmth > 0, beach);
            }
        }
        using (THREE.Bridge.BridgeStats.Time("dig")) dig.update(dt);
        using (THREE.Bridge.BridgeStats.Time("hearts")) hearts.update(dt);
        var targets = !alt && (arrows.busy || bullets.busy)
            ? ecology.crabTargets().Concat(birds.targets()).Concat(sea.targets()).Concat(scenery.targets()).Concat(castles.targets()).ToList()
            : NO_TARGETS;
        if (!alt)
        {
            arrows.update(dt, targets, beach, scenery.trunks, new ArrowHooks
            {
                onHit = (o, at) => onBulletHit(o, at, 0.2),
                onStick = () => audio.thud(),
            });
            bullets.update(dt, targets, beach, new BulletHooks
            {
                onHit = (o, at) => onBulletHit(o, at, 0.14),
                onGround = p => dig.particles.burst(p.setY(p.y + 0.05), 10, 1, 1.6, 0.5, new THREE.Color(0xe6ccb2)),
            });
        }
        using (THREE.Bridge.BridgeStats.Time("spawner")) spawner.update(dt, t, hoveredItem);
        if (!alt)
        {
            using (THREE.Bridge.BridgeStats.Time("flags")) flags.update(t);
            // the buried chests on the maps barely change, so a couple of looks a second is plenty
            mapListT -= dt;
            if (mapListT <= 0)
            {
                mapListT = 0.5;
                mapView.captain = state.data.hasBottleMap ? spawner.items.Where(i => i.def.id == "captain_chest" && !i.collected).Select(i => new[] { i.x, i.z }).ToList() : new List<double[]>();
                mapView.treasures = state.data.hasTreasureMap ? spawner.items.Where(i => i.def.id == "treasure_cache" && !i.collected).Select(i => new[] { i.x, i.z }).ToList() : new List<double[]>();
            }
            using (THREE.Bridge.BridgeStats.Time("island")) foreach (var isl in islands) isl.update(t, ocean.surfaceY, sky.fog.color, sky.fog.density, camera.position.x, camera.position.z, dt);
            if (rain.amount > 0.3 && beach.rainFill(dt, rain.amount, rainKeep()))
            {
                // a hole the rain has filled comes off the maps
                holes.RemoveAll(h => beach.depthAt(h[0], h[1]) < 0.05);
            }
            castles.update(dt, rain.amount, (x, z, byRain) =>
            {
                dig.particles.burst(new THREE.Vector3(x, beach.heightAt(x, z) + 0.2, z), 24, 1.6, 1.4, 0.9, new THREE.Color(0xdcc38f));
                audio.crumble();
                ui.toast(byRain ? "The rain has washed away one of your sandcastles." : "The sea has taken one of your sandcastles.");
                discover("castle_washed");
            });
        }
        if (waypoint != null)
        {
            (beam.material as THREE.MeshBasicMaterial).opacity = 0.3 + Math.Sin(t * 3) * 0.1;
            if (gameState == "EXPLORE" && JS.Hypot(waypoint.x - player.x, waypoint.z - player.z) < 3.5)
            {
                setWaypoint(null);
                audio.discover();
                ui.toast("You have reached your destination.");
            }
        }
        if (!alt)
        {
            mapPlayer.x = player.x;
            mapPlayer.z = player.z;
            mapPlayer.yaw = player.yaw;
            mapCart.x = cart.x;
            mapCart.z = cart.z;
            mapBoat.x = boat.x;
            mapBoat.z = boat.z;
            using (THREE.Bridge.BridgeStats.Time("minimap")) mapView.drawMini(ui.miniCanvas.getContext("2d"), 150, mapPlayer, holes, waypoint, mapCart, mapBoat);
        }
        using (THREE.Bridge.BridgeStats.Time("hint")) updateHint();
        canvas.classList.toggle("pointer", hoveredItem != null || hoveredCamp != null);
        if (audio.ready)
        {
            double fd = JS.Hypot(camera.position.x - camp.group.position.x, camera.position.z - camp.group.position.z);
            double near = Math.Max(0, 1 - fd / 12);
            if (near > 0 && JS.Random() < dt * (1.5 + near * 4)) audio.crackle(near);
        }
        using (THREE.Bridge.BridgeStats.Time("render(sync)")) renderer.render(scene, camera);
        if (photoPending) capturePhoto();
    }
}
