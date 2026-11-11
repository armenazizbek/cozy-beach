using System;
using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Smoke test for builds, off unless asked for:
///   CozyTreasureHunter.exe -autoshot "C:\some\folder" -screen-width 1280 -screen-height 720 -screen-fullscreen 0
/// It waits for the loading screen, saves 1-title.png and 1b-achievements.png, starts a game as Begin would, saves 2-camp.png and 3-sea.png,
/// writes the frame rate to the player log and quits. Saves go to a "saves" folder inside the shot folder, so real saves are
/// never read or written.
/// </summary>
public class AutoShot : MonoBehaviour
{
    private string dir;

    /// <summary>True while the smoke test runs; start-up checkpoints are only logged then.</summary>
    internal static bool Active { get; private set; }

    internal static void Trace(string what)
    {
        if (Active) Debug.Log($"[AutoShot] {Time.realtimeSinceStartup:0.00}s {what}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-autoshot");
        if (i < 0) return;
        Active = true;
        string dir = i + 1 < args.Length && !args[i + 1].StartsWith("-")
            ? args[i + 1]
            : Path.Combine(Application.persistentDataPath, "autoshot");
        Directory.CreateDirectory(dir);
        Storage.UseFolder(Path.Combine(dir, "saves"));
        Photos.exportRoot = Path.Combine(dir, "pictures");

        var go = new GameObject("AutoShot");
        DontDestroyOnLoad(go);
        go.AddComponent<AutoShot>().dir = dir;
    }

    private IEnumerator Start()
    {
        Debug.Log($"[AutoShot] writing to {dir}");
        float t0 = Time.realtimeSinceStartup;
        while ((GameHost.Instance == null || GameHost.Instance.game == null || !GameHost.Instance.game.loaded)
               && Time.realtimeSinceStartup - t0 < 180)
            yield return null;
        var game = GameHost.Instance != null ? GameHost.Instance.game : null;
        if (game == null || !game.loaded)
        {
            Debug.LogError("[AutoShot] the game did not finish loading within 180 s");
            yield return Shot("0-stuck.png");
            Quit(1);
            yield break;
        }
        Debug.Log($"[AutoShot] loaded after {Time.realtimeSinceStartup - t0:0.0} s");

        // let the boot screen fade and the title camera settle
        yield return Wait(6);
        yield return Shot("1-title.png");

        // -autoshot-load <id>: load that save (from this test's own saves folder) the way Load Game does, and photograph it
        var args = Environment.GetCommandLineArgs();
        int li = Array.IndexOf(args, "-autoshot-load");
        if (li >= 0 && li + 1 < args.Length)
        {
            string id = args[li + 1];
            var snap = SaveGame.readSave(id);
            Debug.Log(snap != null
                ? $"[AutoShot] save {id} reads: coins {snap.data.coins}, day {snap.data.day}"
                : $"[AutoShot] save {id} does NOT read");
            SaveGame.requestLoad(id);
            var old = game;
            GameHost.Reload();
            t0 = Time.realtimeSinceStartup;
            while ((GameHost.Instance == null || GameHost.Instance.game == null || GameHost.Instance.game == old || !GameHost.Instance.game.loaded)
                   && Time.realtimeSinceStartup - t0 < 180)
                yield return null;
            game = GameHost.Instance != null ? GameHost.Instance.game : null;
            if (game == null || game == old) { Debug.LogError("[AutoShot] the game did not come back after loading"); Quit(1); yield break; }
            Debug.Log($"[AutoShot] reloaded with the save after {Time.realtimeSinceStartup - t0:0.0} s; load failed flag: {SaveGame.lastLoadFailed}");
            yield return Wait(5);
            yield return Shot("1c-loaded.png");
            // the same measurements in a played save, which has more on the beach than a new game
            yield return Measure("save standing");
            game.input.held.Add("KeyW");
            yield return Wait(1);
            yield return Measure("save walking");
            game.input.held.Clear();
            Quit(0);
            yield break;
        }

        // -autoshot-pad: drive the title menu with a virtual controller (D-pad down from New Game, then A) and check Settings opens
        if (Array.IndexOf(args, "-autoshot-pad") >= 0)
        {
            var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            IEnumerator Press(UnityEngine.InputSystem.LowLevel.GamepadButton b)
            {
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(b));
                yield return Wait(0.15f);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState());
                yield return Wait(0.35f);
                Debug.Log("[AutoShot] pad " + b + " -> focused: " + (document.activeElement?.textContent ?? "(nothing)"));
                Debug.Log("[AutoShot] pad sees " + game.smokeTestPadInfo());
            }
            Debug.Log("[AutoShot] pad sees " + game.smokeTestPadInfo());
            yield return Press(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown);
            yield return Press(UnityEngine.InputSystem.LowLevel.GamepadButton.South);
            yield return Wait(0.8f);
            Debug.Log("[AutoShot] pad: settings open = " + (document.querySelector(".settings-card") != null));
            yield return Shot("1p-pad.png");
            yield return Press(UnityEngine.InputSystem.LowLevel.GamepadButton.East);
            yield return Wait(0.5f);
            Debug.Log("[AutoShot] pad: settings closed by B = " + (document.querySelector(".settings-card") == null));
            UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
        }

        game.smokeTestAchievements();
        yield return Wait(1);
        yield return Shot("1b-achievements.png");
        document.querySelector(".achievements")?.remove();

        game.smokeTestBegin();
        yield return Wait(4);
        yield return Measure("camp");
        yield return Shot("2-camp.png");

        if (Array.IndexOf(args, "-autoshot-walk") >= 0)
        {
            // stand still facing along the beach, then walk the same way, to compare the two
            game.smokeTestLook(Math.PI / 2, -0.05);
            yield return Wait(2);
            yield return Measure("standing");
            Debug.Log("[AutoShot] before walking: " + game.smokeTestWhere());
            game.input.held.Add("KeyW");
            yield return Wait(1);
            yield return Measure("walking");
            yield return Shot("2b-walking.png");
            game.input.held.Add("ShiftLeft");
            yield return Measure("running");
            game.input.held.Clear();
            Debug.Log("[AutoShot] after walking: " + game.smokeTestWhere());
        }

        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-tour") >= 0)
        {
            // the screens with the most text, for checking wrapping and scrolling
            foreach (var what in new[] { "camp:board", "camp:sell", "camp:upgrades", "camp:rest", "book", "mail", "help", "credits" })
            {
                game.smokeTestOpen(what);
                yield return Wait(1.5f);
                yield return Shot("tour-" + what.Replace(':', '-') + ".png");
                game.smokeTestClose(what);
                yield return Wait(0.8f);
            }
        }

        // -autoshot-birds: a bullet at a standing gull and one at a flying gull must both pay; then the long line of keys
        // shown while swimming, which must stay clear of the tool in the corner
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-birds") >= 0)
        {
            foreach (var flying in new[] { false, true })
            {
                Debug.Log("[AutoShot] " + game.smokeTestShootBird(flying));
                yield return Wait(1);
                Debug.Log($"[AutoShot] coins after: {game.smokeTestCoins}");
            }
            game.smokeTestGo("dive");
            yield return Wait(3.5f);
            yield return Shot("10-dive-hint.png");
            game.smokeTestPad(true);
            yield return Wait(1.5f);
            yield return Shot("10-dive-hint-pad.png");
            game.smokeTestPad(false);
            game.smokeTestGo("out");
            yield return Wait(3);
        }

        // -autoshot-shadow: the player's own shadow, on the island's grass and over its stones
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-shadow") >= 0)
        {
            int k = 0;
            foreach (var (dist, angle) in new[] { (12.0, 0.4), (15.0, 2.2), (10.0, -2.4), (17.0, -1.6), (8.0, 1.2) })
            {
                game.smokeTestOwnShadow(dist, angle);
                yield return Wait(2);
                yield return Shot($"16-shadow-{k++}.png");
            }
            for (int n = 0; n < 3; n++)
            {
                Debug.Log("[AutoShot] " + game.smokeTestShadowOnRock(n, 1.6));
                yield return Wait(2);
                yield return Shot($"16-shadow-rock-{n}.png");
            }
        }

        // -autoshot-island: the island from its beach, from the slope and from across the water on the near side
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-island") >= 0)
        {
            foreach (var (name, dist, angle, pitch) in new[] { ("shore", 17.0, -1.6, -0.12), ("slope", 9.0, 0.6, -0.2), ("west", 14.0, 3.0, -0.1), ("near", 30.0, -1.57, -0.05) })
            {
                game.smokeTestIsland(dist, angle, pitch);
                yield return Wait(2.5f);
                yield return Shot($"15-island-{name}.png");
            }
            // the big map from there: the camp's badge across the water, and the player on the island
            game.smokeTestOpen("map");
            yield return Wait(1.3f);
            yield return Shot("15-island-map.png");
            game.smokeTestClose("map");
        }

        // -autoshot-islands: the two far islands. Each from the boat, from its shore and at its landmark; the lagoon from inside,
        // with the step ashore; then the big map with all three on it
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-islands") >= 0)
        {
            var lag = Beach.LAGOON;
            var emb = Beach.EMBER;
            // heading: the boat's forward is (-sin, -cos), so this one looks from the gap side in at the cay
            double gx = Math.Cos(Beach.LAGOON_GAP), gz = Math.Sin(Beach.LAGOON_GAP);
            Debug.Log("[AutoShot] " + game.smokeTestBoatAt(lag.x + gx * 62, lag.z + gz * 62, Math.Atan2(gx, gz)));
            yield return Wait(3);
            yield return Shot("17-lagoon-from-sea.png");
            Debug.Log("[AutoShot] " + game.smokeTestBoatAt(lag.x + gx * lag.R * 0.66, lag.z + gz * lag.R * 0.66, Math.Atan2(gx, gz)));
            yield return Wait(2);
            yield return Shot("17-lagoon-gap.png");
            Debug.Log("[AutoShot] " + game.smokeTestBoatAt(lag.x + 3, lag.z - 2, Math.Atan2(gx, gz)));
            yield return Wait(2);
            yield return Shot("17-lagoon-inside.png");
            // across the lagoon, close enough in to the far shore to step off
            for (double d = 6; d < 18; d += 1)
            {
                string msg = game.smokeTestBoatAt(lag.x - gx * d, lag.z - gz * d, Math.Atan2(gx, gz));
                if (!msg.EndsWith("near True")) continue;
                Debug.Log("[AutoShot] " + msg);
                break;
            }
            yield return Wait(1);
            Debug.Log("[AutoShot] " + game.smokeTestPrompt());
            Debug.Log("[AutoShot] " + game.smokeTestLeaveBoat());
            yield return Wait(2.5f);
            yield return Shot("17-lagoon-ashore.png");
            Debug.Log("[AutoShot] " + game.smokeTestLandmark(1, gx * 7.5, gz * 7.5, -0.12));
            yield return Wait(2.5f);
            yield return Shot("17-lagoon-camp.png");
            Debug.Log("[AutoShot] " + game.smokeTestLandmark(1, -gz * 5 + gx * 3, gx * 5 + gz * 3, -0.3));
            yield return Wait(2.5f);
            yield return Shot("17-lagoon-camp-side.png");
            game.smokeTestIsland(lag.R * 0.86, Beach.LAGOON_GAP + 1.9, -0.1, 1);
            yield return Wait(2.5f);
            yield return Shot("17-lagoon-shore.png");
            Debug.Log("[AutoShot] " + game.smokeTestWhere());

            Debug.Log("[AutoShot] " + game.smokeTestBoatAt(emb.x + 30, emb.z - 52, Math.Atan2(30, -52)));
            yield return Wait(3);
            yield return Shot("18-ember-from-sea.png");
            for (double d = emb.R * 1.2; d > emb.R * 0.5; d -= 1)
            {
                string msg = game.smokeTestBoatAt(emb.x + 0.406 * d, emb.z - 0.914 * d, Math.Atan2(0.406, -0.914));
                if (!msg.EndsWith("near True")) continue;
                Debug.Log("[AutoShot] " + msg);
                break;
            }
            yield return Wait(1);
            Debug.Log("[AutoShot] " + game.smokeTestPrompt());
            Debug.Log("[AutoShot] " + game.smokeTestLeaveBoat());
            yield return Wait(2.5f);
            yield return Shot("18-ember-ashore.png");
            game.smokeTestIsland(emb.R * 0.8, -1.15, -0.02, 2);
            yield return Wait(2.5f);
            yield return Shot("18-ember-columns.png");
            game.smokeTestIsland(emb.R * 0.72, 1.3, 0.1, 2);
            yield return Wait(2.5f);
            yield return Shot("18-ember-cone.png");
            Debug.Log("[AutoShot] " + game.smokeTestLandmark(2, 4.9, 1.4, -0.45));
            yield return Wait(2.5f);
            yield return Shot("18-ember-spring.png");
            Debug.Log("[AutoShot] " + game.smokeTestLandmark(2, -1.4, -5.0, -0.3));
            yield return Wait(2.5f);
            yield return Shot("18-ember-cairn.png");
            game.smokeTestOpen("map");
            yield return Wait(1.3f);
            yield return Shot("19-islands-map.png");
            game.smokeTestClose("map");
            // the first island and the beach from the open sea between them, to see how the far ones sit on the horizon
            Debug.Log("[AutoShot] " + game.smokeTestBoatAt(0, 120, Math.Atan2(-196, -172)));
            yield return Wait(3);
            yield return Shot("19-horizon-east.png");
            Debug.Log("[AutoShot] " + game.smokeTestBoatAt(0, 120, Math.Atan2(204, -192)));
            yield return Wait(3);
            yield return Shot("19-horizon-west.png");
            game.smokeTestShowItems(new[] { "spyglass", "nautilus", "red_coral", "sun_disc", "fire_opal", "obsidian_blade" });
            yield return Wait(1);
            yield return Shot("20-far-finds.png");
            // the edge of the sea: the boat must stop there, and the sky must still be overhead
            Debug.Log("[AutoShot] " + game.smokeTestBoatAt(280, 410, Math.Atan2(-1, -1)));
            yield return Wait(2);
            yield return Shot("20-sea-corner.png");
        }

        // -autoshot-crust: finds of different shapes on the workbench, still in their sand; the sand must follow each shape
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-crust") >= 0)
        {
            var crustIds = Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-islands") >= 0
                ? new[] { "spyglass", "nautilus", "red_coral", "sun_disc", "fire_opal", "obsidian_blade" }
                : new[] { "doubloon", "brass_key", "signet", "glass_bottle", "flip_flop", "pocket_watch" };
            foreach (var id in crustIds)
            {
                game.smokeTestCrust(id);
                yield return Wait(1.6f);
                yield return Shot($"14-crust-{id}.png");
                if (id == "doubloon")
                {
                    Debug.Log($"[AutoShot] brushed a band across the doubloon: {game.smokeTestScrub():0.00} clean");
                    yield return Wait(0.5f);
                    yield return Shot("14-crust-doubloon-brushed.png");
                }
                game.smokeTestCrust(null);
                yield return Wait(1);
            }
        }

        // -autoshot-saves: three autosaves in one game, then a new game and two more; each game must keep its own two
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-saves") >= 0)
        {
            for (int i = 0; i < 3; i++)
            {
                game.smokeTestAutosave();
                yield return Wait(0.3f);
            }
            Debug.Log("[AutoShot] game one: " + game.smokeTestSaves());
            game.smokeTestNewGame();
            float w0 = Time.realtimeSinceStartup;
            while ((GameHost.Instance == null || GameHost.Instance.game == null || GameHost.Instance.game == game || !GameHost.Instance.game.loaded)
                   && Time.realtimeSinceStartup - w0 < 180)
                yield return null;
            game = GameHost.Instance.game;
            yield return Wait(3);
            if (!game.smokeTestStarted) game.smokeTestBegin();
            yield return Wait(2);
            for (int i = 0; i < 2; i++)
            {
                game.smokeTestAutosave();
                yield return Wait(0.3f);
            }
            Debug.Log("[AutoShot] game two: " + game.smokeTestSaves());
            game.smokeTestOpen("saves-load");
            yield return Wait(1.3f);
            yield return Shot("13-load-list.png");
            game.smokeTestClose("saves");
        }

        // -autoshot-rain: a deep hole and a sandcastle in a shower; the rain must fill the one and wash the other away
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-rain") >= 0)
        {
            Debug.Log("[AutoShot] before the rain: " + game.smokeTestRain(true));
            yield return Wait(4);
            yield return Shot("12-rain-start.png");
            foreach (var at in new[] { 20, 45, 70 })
            {
                yield return Wait(at == 20 ? 16 : 25);
                Debug.Log($"[AutoShot] after {at} s of rain: " + game.smokeTestRainStatus());
                yield return Shot($"12-rain-{at}s.png");
            }
            game.smokeTestRainStop();
        }

        // -autoshot-pier: the walk down to the pier and along it, which must be free of palms
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-pier") >= 0)
        {
            foreach (var back in new[] { 14.0, 4.0 })
            {
                game.smokeTestOnPier(Pier.Z0 - back, Math.PI, -0.04);
                yield return Wait(2);
                yield return Shot($"11-pier-from-{back:0}m.png");
            }
        }

        // -autoshot-holes: deep holes up the beach, on the wet sand and at the water's edge (none may show water), then the
        // cart parked across one
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-holes") >= 0)
        {
            foreach (var z in new[] { 5.0, 33.0, 37.5 })
            {
                Debug.Log("[AutoShot] " + game.smokeTestHole(6, z));
                yield return Wait(2);
                yield return Shot($"9-hole-z{z:0}.png");
            }
            Debug.Log("[AutoShot] " + game.smokeTestCartOnHole(6, 5));
            yield return Wait(2);
            yield return Shot("9-cart-on-hole.png");
        }

        // -autoshot-ui: every menu and the HUD, as they look with the keyboard and then with a controller
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-ui") >= 0)
        {
            foreach (var mode in new[] { "kb", "pad" })
            {
                game.smokeTestPad(mode == "pad");
                yield return Wait(0.5f);
                foreach (var what in new[] { "toast", "pause", "settings", "saves", "achievements", "help", "map", "camp:upgrades", "camp:sell", "result", "inspect", "intro" })
                {
                    game.smokeTestOpen(what);
                    yield return Wait(1.3f);
                    yield return Shot($"ui-{mode}-{what.Replace(':', '-')}.png");
                    game.smokeTestClose(what);
                    yield return Wait(0.6f);
                }
            }
            game.smokeTestPad(false);
        }

        // -autoshot-photo: raise the camera, take three photos, look at the board and the album, then save, load and look again
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-photo") >= 0)
        {
            // the dog from a distance: lying down until the camera comes up, then sitting and posing
            game.smokeTestFaceDog(9);
            yield return Wait(3);
            yield return Shot("4a-dog-far.png");
            game.smokeTestRaiseCamera();
            yield return Wait(3);
            yield return Shot("4a-dog-far-pose.png");
            game.smokeTestFaceDog(3);
            yield return Wait(2.5f);
            game.smokeTestTakePhoto();
            yield return Wait(0.6f);
            yield return Shot("4a-dog-close-pose.png");
            // right beside the dog its "Pet the dog" prompt is due: it must not show through the viewfinder, and must come
            // back when the camera is lowered
            game.smokeTestFaceDog(1.5);
            yield return Wait(1.2f);
            Debug.Log("[AutoShot] in reach while the camera is up (its prompt is hidden): " + game.smokeTestPrompt());
            yield return Shot("4a-prompt-hidden.png");
            game.smokeTestLowerCamera();
            yield return Wait(1);
            yield return Shot("4a-prompt-back.png");
            game.smokeTestRaiseCamera();
            yield return Wait(1.5f);
            Debug.Log("[AutoShot] photo copied to: " + (game.smokeTestExportPhoto() ?? "(failed)"));
            game.smokeTestLook(0, -0.12);
            yield return Wait(1.5f);
            yield return Shot("4-viewfinder.png");
            foreach (var yaw in new[] { 0.0, Math.PI, Math.PI / 2 })
            {
                game.smokeTestLook(yaw, -0.05);
                yield return Wait(1.2f);
                game.smokeTestTakePhoto();
                yield return Wait(1.2f);
            }
            Debug.Log($"[AutoShot] photos in the album: {game.smokeTestPhotoCount}");
            game.smokeTestLowerCamera();
            game.smokeTestFacePhotoBoard();
            yield return Wait(2);
            yield return Shot("4b-board.png");
            game.smokeTestOpen("camp:photos");
            yield return Wait(1.5f);
            yield return Shot("4c-album.png");
            game.smokeTestClose("camp:photos");
            yield return Wait(0.5f);
            Debug.Log("[AutoShot] photo save written: " + game.smokeTestSave("phototest"));
            Photos.clearCache();
            SaveGame.requestLoad("phototest");
            var before = game;
            GameHost.Reload();
            t0 = Time.realtimeSinceStartup;
            while ((GameHost.Instance == null || GameHost.Instance.game == null || GameHost.Instance.game == before || !GameHost.Instance.game.loaded)
                   && Time.realtimeSinceStartup - t0 < 180)
                yield return null;
            game = GameHost.Instance.game;
            yield return Wait(4);
            Debug.Log($"[AutoShot] photos after loading: {game.smokeTestPhotoCount}");
            game.smokeTestFacePhotoBoard();
            yield return Wait(2);
            yield return Shot("4d-board-loaded.png");
        }

        // -autoshot-sealife: a shark at the top of its leap, another cruising, and fish at the surface and in the air, close up
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-sealife") >= 0)
        {
            game.smokeTestSeaLife(0, 0.05, 9);
            yield return Wait(2.5f);
            yield return Shot("8-sea-wide.png");
            game.smokeTestSeaLife(0.12, -0.02, 6);
            yield return Wait(1.5f);
            yield return Shot("8-shark-close.png");
            game.smokeTestSeaLife(-0.42, -0.28, 6);
            yield return Wait(1.5f);
            yield return Shot("8-fish-close.png");
        }

        // -autoshot-fish: the pier and the moored boat, fishing from the bench, the net underwater, and the aquarium
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-fish") >= 0)
        {
            game.smokeTestSeePier();
            yield return Wait(2);
            yield return Shot("7-pier-from-beach.png");
            game.smokeTestOnPier(41, Math.PI, -0.1);
            yield return Wait(2);
            yield return Shot("7-pier-walk.png");
            game.smokeTestOnPier(58, Math.PI, -0.25);
            yield return Wait(2);
            yield return Shot("7-pier-end.png");
            Debug.Log("[AutoShot] on the pier: " + game.smokeTestWhere());
            game.smokeTestSit();
            yield return Wait(1.5f);
            yield return Shot("7-fish-1-seated.png");
            // fish as a player would until two are in the bucket, or a minute has gone by
            float fishT0 = Time.realtimeSinceStartup;
            string last = "";
            bool shotWait = false, shotReel = false;
            while (Time.realtimeSinceStartup - fishT0 < 70)
            {
                string now = game.smokeTestFish();
                if (now.Substring(0, 4) != (last.Length >= 4 ? last.Substring(0, 4) : "")) Debug.Log("[AutoShot] fishing: " + now);
                last = now;
                if (!shotWait && now.StartsWith("wait"))
                {
                    shotWait = true;
                    yield return Wait(1.2f);
                    yield return Shot("7-fish-2-waiting.png");
                }
                if (!shotReel && now.StartsWith("reel"))
                {
                    shotReel = true;
                    yield return Wait(0.8f);
                    game.smokeTestFish();
                    yield return Shot("7-fish-3-reeling.png");
                }
                if (now.Contains("caught 2")) break;
                yield return Wait(0.1f);
            }
            Debug.Log("[AutoShot] fishing ended: " + game.smokeTestFish());
            game.smokeTestLook(Math.PI - 0.9, -0.8);
            yield return Wait(1.5f);
            yield return Shot("7-fish-4-bucket.png");
            game.smokeTestStand();
            yield return Wait(1);
            Debug.Log("[AutoShot] stood up: " + game.smokeTestWhere());
            // the net, underwater
            game.smokeTestGo("dive");
            yield return Wait(3);
            Debug.Log("[AutoShot] " + game.smokeTestNet());
            yield return Wait(1.2f);
            yield return Shot("7-net-in-hand.png");
            for (int i = 0; i < 4; i++) Debug.Log("[AutoShot] " + game.smokeTestNet());
            Debug.Log("[AutoShot] " + game.smokeTestGold());
            yield return Wait(0.5f);
            Debug.Log("[AutoShot] after: " + game.smokeTestGold());
            game.smokeTestGo("out");
            yield return Wait(3);
            // the aquarium with what was caught, then with everything
            game.smokeTestAquarium(false);
            yield return Wait(2.5f);
            yield return Shot("7-aquarium-some.png");
            game.smokeTestAquarium(true);
            yield return Wait(3);
            yield return Shot("7-aquarium-full.png");
            game.smokeTestOpen("camp:aquarium");
            yield return Wait(1.5f);
            yield return Shot("7-aquarium-page.png");
            game.smokeTestClose("camp:aquarium");
            yield return Wait(0.5f);
            game.smokeTestOpen("camp:upgrades");
            yield return Wait(1.5f);
            yield return Shot("7-shop.png");
            game.smokeTestClose("camp:upgrades");
            yield return Wait(0.5f);
        }

        // -autoshot-settings: the settings screen, then the same view at the narrowest, the usual and the widest field of view
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-settings") >= 0)
        {
            game.smokeTestSettings(true);
            yield return Wait(1.5f);
            yield return Shot("6-settings.png");
            game.smokeTestSettings(false);
            Debug.Log("[AutoShot] invert look: " + game.smokeTestInvert());
            foreach (var fov in new[] { 40.0, 50.0, 70.0 })
            {
                game.smokeTestFov(fov);
                yield return Wait(1.5f);
                yield return Shot($"6-fov-{fov:0}.png");
            }
            game.smokeTestFov(50);
        }

        // -autoshot-photo-alt: the camera under the sea and in the room under the dunes
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-photo-alt") >= 0)
        {
            foreach (var place in new[] { "dive", "underground" })
            {
                game.smokeTestGo(place);
                yield return Wait(3);
                yield return Shot($"5-{place}-1-arrived.png");
                game.smokeTestAltCamera(true);
                yield return Wait(0.3f);
                game.smokeTestLowerCamera();
                yield return Wait(1.2f);
                yield return Shot($"5-{place}-2-camera-in-hand.png");
                game.smokeTestAltCamera(false);
                game.smokeTestAltCamera(true);
                yield return Wait(1.2f);
                yield return Shot($"5-{place}-3-viewfinder.png");
                int had = game.smokeTestPhotoCount;
                game.smokeTestTakePhoto();
                yield return Wait(1.2f);
                Debug.Log($"[AutoShot] {place}: photos {had} -> {game.smokeTestPhotoCount}, copied to {game.smokeTestExportPhoto() ?? "(failed)"}");
                game.smokeTestAltCamera(false);
                yield return Wait(1);
                yield return Shot($"5-{place}-4-put-away.png");
                game.smokeTestGo("out");
                yield return Wait(3);
                Debug.Log($"[AutoShot] back from {place}: {game.smokeTestWhere()}");
            }
            yield return Shot("5-back-on-the-beach.png");
        }

        // three.js yaw: 0 looks toward -z (the camp), PI toward +z (the sea)
        game.smokeTestLook(Math.PI, -0.05);
        yield return Wait(3);
        yield return Measure("sea");
        yield return Shot("3-sea.png");

        Quit(0);
    }

    private static IEnumerator Wait(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end) yield return null;
    }

    private static IEnumerator Measure(string label)
    {
        int frames = 0, timed = 0;
        float start = Time.realtimeSinceStartup, worst = 0;
        double mainMs = 0, renderMs = 0, gpuMs = 0;
        var ft = new FrameTiming[1];
        THREE.Bridge.BridgeStats.Reset();
        while (Time.realtimeSinceStartup - start < 3)
        {
            yield return null;
            frames++;
            worst = Mathf.Max(worst, Time.unscaledDeltaTime);
            // where Unity's frame went: the main thread, the render thread, the GPU (needs Frame Timing Stats, see ProjectSetup)
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, ft) > 0)
            {
                timed++;
                mainMs += ft[0].cpuMainThreadFrameTime - ft[0].cpuMainThreadPresentWaitTime;
                renderMs += ft[0].cpuRenderThreadFrameTime;
                gpuMs += ft[0].gpuFrameTime;
            }
        }
        float secs = Time.realtimeSinceStartup - start;
        int n = Math.Max(1, timed);
        Debug.Log($"[AutoShot] {label}: {frames / secs:0.0} fps average, slowest frame {worst * 1000:0} ms; " +
                  $"Unity main thread {mainMs / n:0.00} ms, render thread {renderMs / n:0.00} ms, GPU {gpuMs / n:0.00} ms; " +
                  THREE.Bridge.BridgeStats.Report());
        Debug.Log($"[AutoShot] {label} slowest parts (ms/frame): {THREE.Bridge.BridgeStats.SectionReport()}");
    }

    private IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        var path = Path.Combine(dir, name);
        ScreenCapture.CaptureScreenshot(path);
        yield return null;
        yield return null;
        Debug.Log($"[AutoShot] saved {path}");
    }

    private static void Quit(int code)
    {
        Debug.Log("[AutoShot] done");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit(code);
#endif
    }
}
