# Festival of the Mundane Redux

**Festival of the Mundane Redux** updates MouseyPounds and Mr. Podunkian's *Festival of the Mundane* for modern Stardew Valley / SMAPI while preserving the original idea: on Fall 27, the Shadow people hold their own Spirit's Eve-style festival in the Sewers.

During the festival, the Sewer map is replaced with a festival version containing shadow NPCs, festival dialogue, special lighting, a Hat Mouse vendor, and the "Pin the Nose on the Goblin" minigame.

## Credits

Original mod concept, writing, festival map, minigame, and core implementation by **MouseyPounds and Mr. Podunkian**.

Redux update by **ThaleTheLich**.

This Redux is primarily intended as a compatibility and maintenance update, not a full redesign of the original festival. Dino

## Requirements

- Stardew Valley 1.6+
- SMAPI 4.0+

No Content Patcher or Json Assets dependency is required.

## Installation

1. Install SMAPI.
2. Copy the `ShadowFestival` folder into your Stardew Valley `Mods` folder.
3. Launch the game through SMAPI.
4. On Fall 27, enter the Sewers to visit the Festival of the Mundane.

## What the mod does

- Adds the Festival of the Mundane to the Sewers on Fall 27.
- Sends the Wizard's festival letter on Fall 26.
- Temporarily replaces the Sewer map during the festival day.
- Adds festival dialogue for shadow NPCs.
- Adds a mask/disguise mechanic: without a calming shadow mask, most shadow NPCs will throw the player out.
- Adds a Hat Mouse festival shop inside the festival.
- Adds the Pin the Nose on the Goblin minigame and prizes.
- Adds festival lighting, firefly/glow effects, and map atmosphere.
- Adds calming shadow masks that protect the player from Shadow Brute, Shadow Shaman, Shadow Guy, and Shadow Girl damage while worn.
- Adds dinosaur clothing to the mouse shop inside the festival.

## Festival shop

The festival shop is separate from Krobus's vanilla shop.

The Redux Hat Mouse festival shop can sell:

- Original festival masks:
  - Imposing Mask
  - Shamanic Mask
  - Shady Mask
  - Shady Bowed Mask
  - Strange Bun Hat
- Vanilla festival-style hats from the original mod's stock
- Added dinosaur outfit pieces:
  - Dinosaur Boots - Green
  - Dinosaur Boots - Red
  - Dinosaur Shirt
  - Dinosaur Shirt (Alternate)
  - Dinosaur Hat, if available in the current game data
  - Dinosaur Pants, if available in the current game data

The mod does **not** replace, restock, or customize Krobus's shop. Krobus's normal shop behavior should remain vanilla.

## Changes from the original mod

### Updated for Stardew Valley 1.6 and SMAPI 4

The original project targeted older Stardew Valley / SMAPI versions and used legacy APIs such as `IAssetLoader` and `IAssetEditor`. Redux updates the code to SMAPI 4-style content handling through `AssetRequested` and targets `net6.0`.

### Removed Json Assets dependency

The original mod depended on Json Assets for its custom hats. Redux registers the festival hats directly through SMAPI data edits instead, so Json Assets is no longer required.

### Updated Harmony usage

The original mod used Harmony 1.x. Redux updates the patching layer to Harmony 2.x while retaining the original calming-mask behavior against shadow enemies.

### Preserved the original festival structure

Redux keeps the original Fall 27 festival day, Wizard letter, Sewer festival map replacement, Hat Mouse vendor, shadow disguise mechanic, Krobus festival dialogue, and Pin the Nose on the Goblin minigame.

### Added dinosaur outfit support

Redux adds dinosaur-themed shirts and boots through `Data/Shirts` and `Data/Boots`, with their textures loaded by the mod. These items are stocked through the festival Hat Mouse shop.

### Removed unnecessary vanilla shop interference

The original mod included a Forest shop filtering hook to remove festival hats from Hat Mouse's normal shop. Redux removes that menu hook so the mod no longer edits unrelated vanilla shop menus after they open. The festival hats remain unavailable from the Forest shop.

### Kept Krobus shop vanilla

Redux will apply festival-specific Krobus dialogue on the festival day, matching the original festival flavor, but it does not replace Krobus's shop stock, or shop logic.

### Krobus image
The Redux changes Krobus to load their Trench Coat sprite and portrait during the festival.

###Thematics
The Redux changes the song that plays during the festival.

### Safer festival interaction handling

Redux only intercepts action-button clicks while the player is in the Sewer on the actual festival day and no menu is already open. This prevents festival interaction code from interfering with normal Sewer or shop behavior outside the festival.

### Build/package cleanup

Redux uses a modern SDK-style `.csproj`, keeps source files split by responsibility, and includes the required assets, `i18n`, `data.json`, `manifest.json`, and license in the build output.

## Building from source

From the source folder, run:

```powershell
dotnet build
```

The build output should contain the compiled `ShadowFestival.dll` along with the manifest, data file, i18n files, and assets required by the mod.

## Compatibility notes

- The mod uses the same UniqueID as the original: `MouseyPounds.ShadowFestival`.
- Do not install the original and Redux versions at the same time.
- The festival Sewer map is only provided on Fall 27.
- The added hats are registered globally so Stardew Valley can recognize them as valid hat items, but they are stocked by the Redux festival vendor rather than Krobus's shop.
- Krobus's vanilla shop should remain untouched.

## License

See `LICENSE`.
