# Warp Master Framework Author Guide

This guide only covers Warp Master Framework integration. It does not define naming, packaging, or prefix rules for other mods.

This guide is for Stardew Valley mod authors who want their warp changes to cooperate with Warp Master Framework instead of directly replacing a map, overwriting another mod's warps, or forcing users to edit warp points manually.

## What Warp Master Framework does

Warp Master Framework provides a shared data asset that other mods can patch to move, retarget, add, or delete supported warp points. The framework merges those author-provided overrides with the player's saved Warp Master edits, then applies the combined warp state at runtime.

Use this when your mod needs to adjust an existing entrance, door, exit tile, shortcut, map transition, or destination in a way that should remain compatible with other map/layout mods.

## Required dependency

Your compatibility pack should require Warp Master Framework.

If you are using Content Patcher to provide the warp overrides, your pack should also require Content Patcher.

Example `manifest.json` for a Content Patcher compatibility pack:

```json
{
  "Name": "Example Warp Master Patch",
  "Author": "YourName",
  "Version": "1.0.0",
  "Description": "Adds Warp Master Framework warp overrides for Example Mod.",
  "UniqueID": "YourName.ExampleWarpMasterPatch",
  "ContentPackFor": {
    "UniqueID": "Pathoschild.ContentPatcher"
  },
  "Dependencies": [
    {
      "UniqueID": "ThaleTheGreat.WarpMasterFramework",
      "IsRequired": true
    }
  ]
}
```

## Framework asset keys

Warp Master Framework exposes these content assets:

| Asset | Purpose |
|---|---|
| `Mods/ThaleTheGreat.WarpMasterFramework/WarpOverrides` | Patch this to provide your warp changes. |
| `Mods/ThaleTheGreat.WarpMasterFramework/OriginalWarps` | Read/export reference data for detected warps. Do not patch this. |
| `Mods/ThaleTheGreat.WarpMasterFramework/ModDetails` | Read/export reference data for installed mods. Do not patch this. |

Most author packs only patch `Mods/ThaleTheGreat.WarpMasterFramework/WarpOverrides`.

## Recommended workflow

1. Install the mod setup you want to support.
2. Launch a save with Warp Master Framework installed.
3. Use the Warp Master editor or the SMAPI console command:

```text
wmf_export
```

4. Open the generated files in the Warp Master Framework `exports` folder:
   - `original-warps.json`
   - `warp-overrides-template.json`
5. Copy the relevant warp entry from the export.
6. Put that entry into your Content Patcher pack's `content.json`.
7. Give every override a stable, unique `Id`.
8. Test in a save with the target map/mod installed.

The export files are reference material. Do not ship the whole export unless every entry is intentional.

## Content Patcher patch format

Patch the `Overrides` list inside the framework asset.

Example `content.json`:

```json
{
  "Format": "2.9.0",
  "Changes": [
    {
      "Action": "EditData",
      "Target": "Mods/ThaleTheGreat.WarpMasterFramework/WarpOverrides",
      "TargetField": [ "Overrides" ],
      "Entries": {
        "YourName.ExamplePatch.Town_10_20": {
          "Id": "YourName.ExamplePatch.Town_10_20",
          "SourceMap": "Town",
          "WarpType": "Warp",
          "OriginalX": 10,
          "OriginalY": 20,
          "OriginalTargetMap": "Farm",
          "OriginalTargetX": 64,
          "OriginalTargetY": 15,
          "NewX": 11,
          "NewY": 20,
          "TargetMap": "Farm",
          "TargetX": 64,
          "TargetY": 15
        }
      }
    }
  ]
}
```

Content Patcher can edit a nested list by using `TargetField`, and list entries are keyed by their `Id` field. Keep your `Id` unique so another pack does not overwrite your entry.

## Override fields

| Field | Required | Meaning |
|---|---:|---|
| `Id` | Yes | Unique ID for this override entry. Use your UniqueID as a prefix. |
| `SourceMap` | Yes | The map/location containing the source warp tile. |
| `WarpType` | Recommended | `Warp` for normal `GameLocation.warps`; `Door` for tile-property warps. Defaults to `Warp`. |
| `OriginalX` | Yes | Original source tile X. This identifies the warp to modify. |
| `OriginalY` | Yes | Original source tile Y. This identifies the warp to modify. |
| `OriginalTargetMap` | Strongly recommended | Original destination map. Used for stable matching. |
| `OriginalTargetX` | Strongly recommended | Original destination tile X. Used for stable matching. |
| `OriginalTargetY` | Strongly recommended | Original destination tile Y. Used for stable matching. |
| `NewX` | Optional | New source tile X. If omitted, uses `OriginalX`. |
| `NewY` | Optional | New source tile Y. If omitted, uses `OriginalY`. |
| `TargetMap` | Optional | New destination map. If omitted, uses `OriginalTargetMap`. |
| `TargetX` | Optional | New destination tile X. If omitted with `TargetY`, uses original target. |
| `TargetY` | Optional | New destination tile Y. If omitted with `TargetX`, uses original target. |
| `Delete` | Optional | `true` removes the matched warp instead of moving/retargeting it. |
| `DoorLayerName` | Door only | Map layer containing the tile property, usually `Buildings`, `Back`, or `Front`. |
| `DoorPropertyName` | Door only | Tile property name, usually `Action`, `TouchAction`, or `Warp`. |
| `DoorTokenOrder` | Door only | Use `xymap` for `Warp <x> <y> <map>`, or `mapxy` for `Warp <map> <x> <y>`. |
| `DoorCommand` | Door only | Command name such as `Warp`, `MagicWarp`, or `LockedDoorWarp`. |
| `DoorExtraTokens` | Door only | Extra tokens after the destination, such as time/condition tokens for locked doors. |

## Normal warp examples

### Move a source tile without changing the destination

```json
{
  "Id": "YourName.ExamplePatch.TownExitMove",
  "SourceMap": "Town",
  "WarpType": "Warp",
  "OriginalX": 10,
  "OriginalY": 20,
  "OriginalTargetMap": "Farm",
  "OriginalTargetX": 64,
  "OriginalTargetY": 15,
  "NewX": 11,
  "NewY": 20
}
```

### Retarget a warp without moving the source tile

```json
{
  "Id": "YourName.ExamplePatch.TownExitRetarget",
  "SourceMap": "Town",
  "WarpType": "Warp",
  "OriginalX": 10,
  "OriginalY": 20,
  "OriginalTargetMap": "Farm",
  "OriginalTargetX": 64,
  "OriginalTargetY": 15,
  "TargetMap": "BusStop",
  "TargetX": 12,
  "TargetY": 23
}
```

### Delete a warp

```json
{
  "Id": "YourName.ExamplePatch.RemoveOldExit",
  "SourceMap": "Town",
  "WarpType": "Warp",
  "OriginalX": 10,
  "OriginalY": 20,
  "OriginalTargetMap": "Farm",
  "OriginalTargetX": 64,
  "OriginalTargetY": 15,
  "Delete": true
}
```

### Add a new normal warp

Use this only after testing. For a new warp, choose a stable source tile and set the original and current destination to the destination you want.

```json
{
  "Id": "YourName.ExamplePatch.NewTownShortcut",
  "SourceMap": "Town",
  "WarpType": "Warp",
  "OriginalX": 50,
  "OriginalY": 30,
  "OriginalTargetMap": "Forest",
  "OriginalTargetX": 20,
  "OriginalTargetY": 5,
  "NewX": 50,
  "NewY": 30,
  "TargetMap": "Forest",
  "TargetX": 20,
  "TargetY": 5
}
```

## Door and tile-property warp examples

Use `WarpType: "Door"` for tile properties like:

```text
Action Warp <x> <y> <map>
TouchAction Warp <map> <x> <y>
TouchAction MagicWarp <map> <x> <y>
Action LockedDoorWarp <x> <y> <map> <extra tokens...>
```

### Retarget an Action warp

```json
{
  "Id": "YourName.ExamplePatch.ActionDoorRetarget",
  "SourceMap": "Town",
  "WarpType": "Door",
  "OriginalX": 10,
  "OriginalY": 20,
  "OriginalTargetMap": "Farm",
  "OriginalTargetX": 64,
  "OriginalTargetY": 15,
  "TargetMap": "BusStop",
  "TargetX": 12,
  "TargetY": 23,
  "DoorLayerName": "Buildings",
  "DoorPropertyName": "Action",
  "DoorTokenOrder": "xymap",
  "DoorCommand": "Warp"
}
```

### Retarget a TouchAction MagicWarp

```json
{
  "Id": "YourName.ExamplePatch.MagicDoorRetarget",
  "SourceMap": "CustomLocation",
  "WarpType": "Door",
  "OriginalX": 8,
  "OriginalY": 12,
  "OriginalTargetMap": "WizardHouse",
  "OriginalTargetX": 4,
  "OriginalTargetY": 8,
  "TargetMap": "Town",
  "TargetX": 32,
  "TargetY": 67,
  "DoorLayerName": "Back",
  "DoorPropertyName": "TouchAction",
  "DoorTokenOrder": "mapxy",
  "DoorCommand": "MagicWarp"
}
```

### Preserve extra LockedDoorWarp tokens

If the original tile property has extra tokens after the destination, copy them into `DoorExtraTokens`.

```json
{
  "Id": "YourName.ExamplePatch.LockedDoorMove",
  "SourceMap": "Town",
  "WarpType": "Door",
  "OriginalX": 25,
  "OriginalY": 60,
  "OriginalTargetMap": "SeedShop",
  "OriginalTargetX": 6,
  "OriginalTargetY": 15,
  "NewX": 26,
  "NewY": 60,
  "TargetMap": "SeedShop",
  "TargetX": 6,
  "TargetY": 15,
  "DoorLayerName": "Buildings",
  "DoorPropertyName": "Action",
  "DoorTokenOrder": "xymap",
  "DoorCommand": "LockedDoorWarp",
  "DoorExtraTokens": "800 1700"
}
```

## Compatibility rules

- Use stable IDs: `YourUniqueID.Map_X_Y_Target` is a good pattern.
- Always include original source and original destination fields when modifying an existing warp.
- Prefer editing through `WarpOverrides` instead of directly replacing a map just to move a warp.
- Do not overwrite the whole `WarpOverrides` asset. Patch the `Overrides` list.
- Do not patch `OriginalWarps` or `ModDetails`; those are reference/export assets.
- Avoid broad deletes unless your mod clearly owns the old route.
- Test with any map/layout mod your patch claims to support.
- If your patch depends on another mod's map, add that mod as a dependency or gate the patch with Content Patcher conditions.

## Load order and conflicts

Content Patcher merges `EditData` changes from multiple packs. If two packs use the same override `Id`, the later one can replace the earlier one. If two different override IDs modify the same original warp, Warp Master Framework will receive both entries and the result depends on how those edits interact.

For compatibility packs, avoid trying to win load order. Instead, make each override target a specific original source and destination, and use clear dependencies when your patch is only valid with another mod installed.

## User settings that affect author patches

Warp Master Framework has a GMCM option for framework overrides. If a player disables framework overrides, author-provided `WarpOverrides` patches will not be applied. Saved user edits made through Warp Master can still exist independently.

## Troubleshooting checklist

If a patch does not apply:

1. Confirm Warp Master Framework is installed and loaded.
2. Confirm the user's GMCM setting for framework overrides is enabled.
3. Confirm your Content Patcher pack is loaded with no errors.
4. Confirm your patch targets exactly `Mods/ThaleTheGreat.WarpMasterFramework/WarpOverrides`.
5. Confirm you used `TargetField: [ "Overrides" ]`.
6. Confirm each entry has a unique `Id` and matching `Id` field inside the entry.
7. Confirm `SourceMap` matches the loaded location name used by the game.
8. Confirm original source and original destination values match the detected/exported warp.
9. For door warps, confirm the layer/property/token order matches the source tile property.
10. Run `wmf_export` and compare your entry to the current detected data.

## Minimal complete pack example

Folder:

```text
Example Warp Master Patch/
  manifest.json
  content.json
```

`manifest.json`:

```json
{
  "Name": "Example Warp Master Patch",
  "Author": "YourName",
  "Version": "1.0.0",
  "Description": "Adds Warp Master Framework compatibility for Example Mod.",
  "UniqueID": "YourName.ExampleWarpMasterPatch",
  "ContentPackFor": {
    "UniqueID": "Pathoschild.ContentPatcher"
  },
  "Dependencies": [
    {
      "UniqueID": "ThaleTheGreat.WarpMasterFramework",
      "IsRequired": true
    }
  ]
}
```

`content.json`:

```json
{
  "Format": "2.9.0",
  "Changes": [
    {
      "Action": "EditData",
      "Target": "Mods/ThaleTheGreat.WarpMasterFramework/WarpOverrides",
      "TargetField": [ "Overrides" ],
      "Entries": {
        "YourName.ExampleWarpMasterPatch.Town_10_20": {
          "Id": "YourName.ExampleWarpMasterPatch.Town_10_20",
          "SourceMap": "Town",
          "WarpType": "Warp",
          "OriginalX": 10,
          "OriginalY": 20,
          "OriginalTargetMap": "Farm",
          "OriginalTargetX": 64,
          "OriginalTargetY": 15,
          "NewX": 11,
          "NewY": 20,
          "TargetMap": "Farm",
          "TargetX": 64,
          "TargetY": 15
        }
      }
    }
  ]
}
```

## Warp Master validation checklist

Before publishing a Warp Master compatibility patch:

- The manifest requires `ThaleTheGreat.WarpMasterFramework`.
- The pack patches `Mods/ThaleTheGreat.WarpMasterFramework/WarpOverrides` and not the reference/export assets.
- The patch uses `TargetField: [ "Overrides" ]`.
- Every override has a stable, unique `Id`.
- Existing warps include original source and original destination fields from `wmf_export`.
- Door/tile-property warps include the correct layer, property name, token order, command, and any required extra tokens.
- The patch has been tested in game with the target map/mod setup installed.
- The generated export files are not shipped as active patch data unless every entry is intentional.
