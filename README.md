# Nearby Chests

![version](https://img.shields.io/badge/version-0.1.0-blue) ![valheim](https://img.shields.io/badge/Valheim-1.0.21-orange) ![client](https://img.shields.io/badge/client--side-only-green)

A BepInEx plugin for Valheim.

Open your inventory and see everything stored in the chests around you, in one place, then take what you need without walking to each chest. Pick a recipe at a crafting station and pull its missing ingredients from those chests with one click.

Pairs well with [Recipe Search](https://github.com/Glutoblop/RecipeSearch) for finding the recipe first.

## Features
Adds a **Nearby** tab next to **Craft** / **Upgrade**. Selecting it shows everything at once:

- **Every recipe you know**: the crafting list switches to every recipe your character has unlocked, whichever station you're at (or none), with each recipe's ingredients. Recipe Search's search box works on it as usual.
  - Recipes your inventory plus the nearby chests can cover are listed bright (gold name) instead of greyed out; ones you can craft right now stay white.
- **Everything in nearby chests**: a panel under your inventory shows the combined contents of every chest within 30 m, as normal stacks, with its own search box. It scrolls when there's more than fits.
- **Filter by recipe**: in the Nearby tab the Craft button becomes **Filter chests**. Select a recipe, press it, and the chests panel shows only that recipe's ingredients. Press it again (or **Clear filter**) to see everything.
- **Required amounts**: below the chests panel, the selected/filtered recipe's ingredients are listed as *need / have / in chests* (green = you have enough, yellow = the chests can cover it, red = not enough).
- **You decide what to take**: drag stacks from the chests panel into your inventory, Shift-click to split, Ctrl-click to move a whole stack. Items come out of the real chests, nearest first.
- **Store too**: drag an item from your inventory onto the chests panel, or Ctrl-click it in your inventory, to put it in the nearby chests.
- **Hints**: in the crafting panel an ingredient amount turns yellow when you're short but nearby chests can cover it; its tooltip shows the nearby count.
- Craft from the normal Craft tab (at the right station) once you've pulled what you need.
- Skips chests another player has open, private chests that aren't yours, and chests inside wards you can't access. Tombstones are excluded by default.

## Configuration (`BepInEx/config/jordan.valheim.nearbychests.cfg`)
| Section | Key | Default | What it does |
|---|---|---|---|
| General | Enabled | true | Turn the plugin on or off. |
| General | Range | 30 | Chests within this many metres are included. |
| General | IncludeTombstones | false | Include tombstones in the pool. |
| General | IncludeCartsAndShips | true | Include cart and ship storage. |
| Panel | ToggleKey | None | Hotkey switching between the Nearby and Craft tabs. |
| Panel | RefreshInterval | 0.5 | Seconds between rescans while the inventory is open. |
| Panel | ClearSearchOnClose | true | Clear the search box when the inventory closes. |
| Panel | SearchOffsetX / SearchOffsetY | -12 / -8 | Search box position from the panel's top-right corner. |
| Panel | PanelOffsetX / PanelOffsetY | 0 / 0 | Nudge the chests panel from its spot under your inventory. |
| Crafting | RequirementHints | true | Yellow amounts + nearby counts on crafting ingredients. |
| Crafting | ClickIngredientToTake | false | Clicking a crafting ingredient takes the missing amount from chests automatically. |

## Install

### Easiest: Valheim Mod Installer
Download the [**Valheim Mod Installer**](https://github.com/Glutoblop/ValheimPlugins) and run it (`Start-Installer.bat` on Windows, `start-installer.sh` on Linux/macOS), point it at your Valheim folder, tick **Nearby Chests** and click **Install / Update**. It installs BepInEx for you if needed.

### Manual
Download from the [latest release](https://github.com/Glutoblop/NearbyChests/releases/latest) ([`NearbyChests.zip`](https://github.com/Glutoblop/NearbyChests/releases/latest/download/NearbyChests.zip): unzip into your Valheim folder), or:

1. Install [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) (5.4.2360 or newer) into your Valheim folder.
2. Download [`dist/NearbyChests.dll`](https://github.com/Glutoblop/NearbyChests/raw/main/dist/NearbyChests.dll).
3. Copy it to `<Valheim>/BepInEx/plugins/NearbyChests/NearbyChests.dll`.
4. Launch the game once; settings are written to `BepInEx/config/jordan.valheim.nearbychests.cfg`.

## Compatibility

| | |
|---|---|
| Plugin version | **0.1.0** |
| Last tested on Valheim | **1.0.21** (network version 41, Steam build 26249040) on 2026-11-06 |
| BepInExPack_Valheim | 5.4.2360 |
| Multiplayer | Client-side only: works on vanilla servers, other players don't need it |

Taking from a chest claims ownership of it the same way vanilla "Take all" does. Don't run it alongside other "craft from containers" mods.

Machine-readable versions live in [`plugin.json`](plugin.json).

## Building

Requires the .NET SDK (8+) and a Valheim install with BepInEx.

```
dotnet build -c Release -p:GameDir="C:\Path\To\Valheim"
```

The build copies the DLL into `<GameDir>/BepInEx/plugins/NearbyChests` for testing, and into `dist/` (committed, and what the installer downloads).

## Releasing an update

`release\Release.bat -Changes "what changed"` bumps the version, builds, commits, pushes and publishes a GitHub release.
