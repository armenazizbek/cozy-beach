# Releasing on Steam

What the game already does for Steam, and what is left to do on Steam's side.

## What is built in

- **Achievements.** 33 achievements, listed at the bottom. The game keeps its own list (the Achievements button on the main and pause menus). When Steam starts the game, each achievement is also unlocked on Steam. Outside Steam it works the same, without any Steam features.
- **Cloud saves.** In the desktop app every save, the autosave and the achievement list are plain `.json` files in a `saves` folder inside the game's user-data folder, which Steam can copy to the cloud (see step 4). Saves an earlier version kept in the browser's storage are copied across the first time they are read.
- **Installer.** `npm run dist:win` makes an installer (`Cozy-Treasure-Hunter-Setup-<version>.exe`, with a desktop shortcut, a Start-menu entry, a choice of folder and an uninstaller) and a portable exe. Steam itself does not use the installer: it installs the game from the files you upload (step 5).

## Steps on Steam's side

1. Pay the one-time fee and create the game in **Steamworks**. You get an **App ID**.
2. While testing outside Steam, put the App ID in a file called `steam_appid.txt` next to the game's exe (one line, just the number). Do not ship this file to players.
3. **Achievements:** in Steamworks open *Stats & Achievements* and create one achievement for each row of the table below, using the **API name** exactly as written (for example `FIRST_DIG`). Give each a name, a description and an icon.
4. **Cloud saves:** open *Application > Cloud* and turn on *Steam Auto-Cloud*, then add a path:
   - Root: `WinAppDataRoaming`
   - Subdirectory: the game's folder under `%APPDATA%` plus `saves` (the app's name from `package.json`, so `cozy-treasure-hunter/saves`; check the real folder name on a machine that has run the game once)
   - Pattern: `*.json`
   - A quota of about 20 MB and 200 files is plenty.
5. **Uploading a build:** run `npm run dist:steam`. It makes the unpacked game folder at `release/win-unpacked`. Upload that folder as a depot with `steamcmd` or the Steamworks `ContentBuilder`, and set the launch option to `Cozy Treasure Hunter.exe`.
6. Fill in the store page (capsule art, screenshots, trailer, description) and the price, then submit for review.

## Notes

- The Steam module (`steamworks.js`) is an optional dependency. If it is missing or Steam is not running, the game just carries on without it.
- The game is not code-signed, so Windows SmartScreen may warn on the first run of the installer. Steam installs do not show that warning.
- Keep the licence notices that the build bundles (`LICENSE.electron.txt` and `LICENSES.chromium.html`).

## Achievements

| API name | Name | Description |
|---|---|---|
| `FIRST_DIG` | First Dig | Dig your first hole in the sand. |
| `FINDS_10` | Beachcomber | Find 10 things. |
| `FINDS_50` | Treasure Hunter | Find 50 things. |
| `FINDS_150` | Pack Rat | Find 150 things. |
| `QUEST_FIRST` | Finders, Returners | Return a lost item to its owner. |
| `QUEST_ALL` | Everything Back Home | Return every lost item on the board. |
| `ASSEMBLED` | Restorer | Piece both antiques back together. |
| `RICH` | Nest Egg | Hold 2000 coins at once. |
| `CHEST_BURIED` | X Marks the Spot | Dig up a sealed chest from the treasure map. |
| `CHEST_SEA` | Chest at Sea | Haul in a treasure chest from the water. |
| `MAIL` | You’ve Got Mail | Receive a letter at the camp post. |
| `HAMMOCK` | Naptime | Sleep in the hammock until morning. |
| `RADIO` | Tune In | Turn on the radio. |
| `DECOR` | Home Sweet Camp | Buy every camp decoration. |
| `WEEK` | A Week by the Sea | Reach day 7. |
| `NIGHT` | Night Owl | Be out on the beach after dark. |
| `RAIN` | Rainy Day | Be caught out in the rain. |
| `CASTLE` | Sandy Builder | Build a sandcastle. |
| `DOG_PET` | Good Dog | Give the dog some love. |
| `DOG_FIND` | Fetch! | Let the dog sniff out a find. |
| `BOAT` | Captain | Buy the boat key. |
| `ISLAND` | Landfall | Set foot on the island. |
| `RUINS` | Old Stones | Find the ruins on the island. |
| `MAP` | An Old Map | Take the map from the island’s altar. |
| `DIVE` | Into the Blue | Dive for the first time. |
| `WRECK` | The Sunken Ship | Find the shipwreck. |
| `HARPOON` | First Catch | Spear your first fish. |
| `CREATURE` | Strange Neighbours | Spear an octopus, seahorse, sea snake or crab. |
| `BOTTLE` | Message from the Deep | Find the bottle with the map inside. |
| `HATCH` | A Door Under the Sand | Dig up the hidden hatch. |
| `UNDERGROUND` | Under the Dunes | Climb down into the stone room. |
| `JOURNAL` | The Captain’s Story | Read the captain’s journal. |
| `DOOR` | To Be Continued… | Try the great door. (secret) |
