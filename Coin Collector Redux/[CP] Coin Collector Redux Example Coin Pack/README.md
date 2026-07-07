# Coin Collector Redux Example Coin Pack

This is a one-coin Content Patcher example pack for **Coin Collector Redux**. It shows the complete minimum structure needed to add a custom coin without Dynamic Game Assets.

## What this pack adds

This pack adds one coin:

- **Traveler Copper Coin**
- Internal object ID: `ThaleTheGreat.CoinCollectorReduxExampleCoinPack_TravelerCopperCoin`
- Set name: `Traveler`
- Sell price: `150g`
- Rarity weight: `3.0`
- Spawn locations: any valid Coin Collector Redux diggable outdoor location

## Requirements

Install these first:

1. SMAPI
2. Content Patcher
3. Coin Collector Redux

Dynamic Game Assets is **not** required.

## Install

1. Download or clone this pack.
2. Put the folder named `[CP] Coin Collector Redux Example Coin Pack` into your Stardew Valley `Mods` folder.
3. Launch the game through SMAPI.
4. Coin Collector Redux will read the patched coin data from `ThaleTheGreat.CoinCollectorRedux/Coins`.

## Files

```text
[CP] Coin Collector Redux Example Coin Pack/
├── assets/
│   └── coins.png
├── content.json
├── manifest.json
├── LICENSE
└── README.md
```

## How it works

Coin Collector Redux exposes this custom data asset:

```text
ThaleTheGreat.CoinCollectorRedux/Coins
```

This pack uses Content Patcher to edit that asset. Coin Collector Redux then creates a native Stardew Valley 1.6 object entry for the coin through `Data/Objects`.

The pack also loads its coin texture as this custom asset:

```text
ThaleTheGreat.CoinCollectorReduxExampleCoinPack/CoinsTexture
```

The coin entry points at that texture with:

```json
"TexturePath": "ThaleTheGreat.CoinCollectorReduxExampleCoinPack/CoinsTexture",
"SpriteIndex": 0
```

## Coin data fields

The coin entry is in `content.json` under this target:

```json
"Target": "ThaleTheGreat.CoinCollectorRedux/Coins"
```

### `Id`

The native Stardew Valley object ID that Coin Collector Redux should create.

Use a stable, unique, unspaced ID. A good pattern is:

```text
AuthorName.PackName_CoinName
```

Example:

```json
"Id": "ThaleTheGreat.CoinCollectorReduxExampleCoinPack_TravelerCopperCoin"
```

Do not change this ID after release unless you are intentionally making a breaking save-compatibility change.

### `Name`

The internal object name used in `Data/Objects`.

Example:

```json
"Name": "TravelerCopperCoin"
```

### `SetName`

A grouping label for related coins.

Example:

```json
"SetName": "Traveler"
```

Coin Collector Redux automatically adds a context tag based on this value, such as:

```text
coin_set_traveler
```

### `DisplayName`

The in-game display name.

Example:

```json
"DisplayName": "Traveler Copper Coin"
```

### `Description`

The in-game item description.

Example:

```json
"Description": "A worn copper coin carried by a traveler long ago."
```

### `TexturePath`

The content asset path for the coin spritesheet.

This example loads `assets/coins.png` into:

```text
ThaleTheGreat.CoinCollectorReduxExampleCoinPack/CoinsTexture
```

The coin points at that same asset path.

### `SpriteIndex`

The sprite index on the texture sheet.

This example uses a single 16×16 sprite, so the index is `0`.

### `Locations`

A list of location names where the coin can spawn.

Use an empty list to allow the coin to spawn in any valid Coin Collector Redux location:

```json
"Locations": []
```

To restrict the coin to specific locations, list the location names:

```json
"Locations": [
  "Town",
  "Forest"
]
```

### `Rarity`

A relative spawn weight.

Higher values are more common relative to other coins. Lower values are rarer.

Example:

```json
"Rarity": 3.0
```

### `Price`

The sell price in gold.

Example:

```json
"Price": 150
```

### `Type`

The Stardew Valley object type. Coins generally use:

```json
"Type": "Minerals"
```

### `Category`

The Stardew Valley object category. This example uses the mineral category:

```json
"Category": -2
```

### `CreateObject`

When `true`, Coin Collector Redux creates a native `Data/Objects` entry for this coin.

Use `true` for normal coin packs:

```json
"CreateObject": true
```

Use `false` only when another mod already creates the object and this pack only wants Coin Collector Redux to bury/detect that existing object.

### `ContextTags`

Additional context tags added to the generated object.

Example:

```json
"ContextTags": [
  "item_coin",
  "coin_set_traveler",
  "coin_material_copper"
]
```

Coin Collector Redux already adds basic coin tags, but explicit tags are useful for compatibility with other mods.

## Making your own coin pack from this example

To make a new pack:

1. Rename the folder.
2. Update `manifest.json`:
   - `Name`
   - `Author`
   - `UniqueID`
   - `Description`
   - `UpdateKeys`
3. Update the texture asset path in `content.json`.
4. Replace `assets/coins.png` with your own 16×16 coin sprite or spritesheet.
5. Update the coin entry fields.
6. Keep coin IDs stable after release.

## Adding more coins

Add more entries under:

```json
"Entries"
```

Each entry needs a unique key and a unique `Id`.

Example structure:

```json
"Entries": {
  "TravelerCopperCoin": {
    "Id": "ThaleTheGreat.CoinCollectorReduxExampleCoinPack_TravelerCopperCoin"
  },
  "TravelerSilverCoin": {
    "Id": "ThaleTheGreat.CoinCollectorReduxExampleCoinPack_TravelerSilverCoin"
  }
}
```

If all coin sprites are on the same spritesheet, increase `SpriteIndex` for each coin.

## Uploading to GitHub

This folder can be uploaded directly as a GitHub repository.

Recommended repository contents:

```text
assets/coins.png
content.json
manifest.json
LICENSE
README.md
```

Before publishing a real release, replace the placeholder update key in `manifest.json`:

```json
"UpdateKeys": [
  "Nexus:0000"
]
```

Use the actual Nexus ID if the pack is uploaded to Nexus, or remove `UpdateKeys` if no update service is used.

## Compatibility notes

- This is a Content Patcher pack, so it does not include a DLL.
- This pack does not use Dynamic Game Assets.
- This pack depends on Coin Collector Redux.
- Multiple Content Patcher packs can add entries to `ThaleTheGreat.CoinCollectorRedux/Coins`.
- Coin Collector Redux handles the daily burying, metal detector pings, indicator projectile, and hoe pickup behavior.
