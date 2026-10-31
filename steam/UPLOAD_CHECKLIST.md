# Steam upload checklist

Everything in this folder is made for the sizes Steam asks for. Tick things off as you upload.

## Store page art (Steamworks > Store Presence > Graphical Assets)

| What Steam asks for | Size | File |
|---|---|---|
| Header capsule | 460 x 215 | `art/header-capsule.png` |
| Small capsule | 231 x 87 | `art/small-capsule.png` |
| Main capsule | 616 x 353 | `art/main-capsule.png` |
| Vertical capsule | 374 x 448 | `art/vertical-capsule.png` |
| Library capsule | 600 x 900 | `art/library-capsule.png` |
| Library hero | 3840 x 1240 | `art/library-hero.png` (no text on purpose; the logo goes on top) |
| Library logo | 1280 x 720, transparent | `art/library-logo.png` |
| Page background | 1438 x 810 | `art/page-background.png` |
| Community / client icon | 184 x 184 | `art/community-icon.png` |
| Screenshots (at least 5) | 1920 x 1080 | `screenshots/` (8 pictures) |

The 8 screenshots, in the order I would show them: golden-hour camp, camp at night, beach and sea, boat and island, coral reef, sunken ship, underground room, the stairs to the sky.

## Achievements (Steamworks > Stats & Achievements)

- 35 achievements. Create each one with the API name from `../STEAM.md`.
- Icons: `achievements/<API_NAME>.png` (earned) and `achievements/<API_NAME>_locked.png` (not yet earned), 256 x 256.
- Names and descriptions are in `../STEAM.md`.

## Text

- All store text is in `STORE_PAGE.md`: the short description, the long description, tags, system requirements, the content survey answers and the price.

## What is still needed

- [ ] Icons for the two newest achievements, `DOOR_OPEN` and `HOARD` (both the earned and the locked version).
- [ ] A trailer (optional but strongly recommended): 30 to 60 seconds. A good plan: dig and clean, the camp at night, the boat to the island, diving with the turtle and octopus, the underground door, and the title. You can record gameplay with OBS; I can also set up a free-roaming camera mode to make a smooth fly-through if you want.
- [ ] The App ID (when your Steamworks account is approved) and the achievements created on Steam.
- [ ] Steam Auto-Cloud turned on (see `../STEAM.md`, step 4).
- [ ] The depot build uploaded with `npm run dist:steam` (see `../STEAM.md`, step 5).
- [ ] The store page and the build submitted for review.
- [ ] A "Coming soon" page live at least two weeks before release.

## Notes

- The screenshots show the world only: no menus or on-screen text, so they work in any language.
- The captures were rendered at full size straight from the game, so they look as sharp as it really is.
