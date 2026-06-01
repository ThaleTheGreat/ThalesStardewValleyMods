# Mod Patcher Author Guide

Mod Patcher content packs use `patches.json`. Do not use `content.json` for Mod Patcher packs.

## Pack layout

```text
[MP] Example Patch Pack/
├─ manifest.json
├─ patches.json
└─ LICENSE
```

## Supported actions

Mod Patcher 1.3.0 supports these actions:

- `PatchMod`
- `PatchRuntime`
- `PatchInteraction`

No legacy action aliases are supported.

## PatchMod

Use `PatchMod` to patch files or resources inside another installed mod.

Common fields:

| Field | Meaning |
|---|---|
| `Action` | Must be `PatchMod`. |
| `TargetMod` | SMAPI `UniqueID` of the mod being patched. |
| `TargetPath` | Path to the file inside the target mod folder. |
| `FromFile` | Path to a replacement file inside this MP pack. |
| `FromVanillaUi` | Generate a replacement from a vanilla Stardew UI texture. |
| `OutputWidth` / `OutputHeight` | Size for generated `FromVanillaUi` output. |

Example:

```json
{
  "Format": "1.0.0",
  "Changes": [
    {
      "Action": "PatchMod",
      "TargetMod": "ModderDrew.UtilityPocket",
      "TargetPath": "assets/UtilityPocketHUD.png",
      "FromVanillaUi": "MenuBox",
      "OutputWidth": 64,
      "OutputHeight": 64
    }
  ]
}
```

### FromVanillaUi

Supported values:

- `MenuBox`

Generated files are stored under:

```text
generated/<content pack UniqueID>/
```

Generated menu textures use a stable content-based filename that includes a signature of the active vanilla menu texture. If a player switches interface/recolor mods, Mod Patcher generates the correct variant. If they switch back to a previously used menu texture, Mod Patcher reuses the existing generated file instead of creating duplicates.

## PatchRuntime

Use `PatchRuntime` for Harmony/reflection-style runtime patches.

### Method patches

Use C# and Harmony terminology where possible. Method patches currently support `void` and `bool` methods. For `bool` methods, `Prefix.Return` sets the result. For `void` methods, `Prefix.Return` is ignored. In both cases, `Prefix.SkipOriginal` controls whether the original method runs.

Common fields:

| Field | Meaning |
|---|---|
| `Action` | Must be `PatchRuntime`. |
| `Name` | Human-readable patch name for logs. |
| `Type` | Full C# type name to patch. |
| `Method` | Method name to patch. |
| `PatchAllOverloads` | Patch every overload with this method name. |
| `Prefix` | Harmony prefix behavior. |
| `Prefix.Return` | Value assigned to the method result when the target method returns `bool`. |
| `Prefix.SkipOriginal` | When true, the original method is skipped. |

Example:

```json
{
  "Format": "1.0.0",
  "Changes": [
    {
      "Action": "PatchRuntime",
      "Name": "Non Destructive NPCs Redux",
      "Type": "StardewValley.GameLocation",
      "Method": "characterDestroyObjectWithinRectangle",
      "PatchAllOverloads": true,
      "Prefix": {
        "Return": false,
        "SkipOriginal": true
      }
    }
  ]
}
```

### Runtime proxy patches

Use `RequireAnySource`, `RequireAnyTarget`, and `Bridge` only when a patch needs to connect live data from one group of mods to runtime behavior in another group of mods.

Common fields:

| Field | Meaning |
|---|---|
| `RequireAnySource` | At least one of these SMAPI mod IDs must be loaded. |
| `RequireAnyTarget` | At least one of these SMAPI mod IDs must be loaded. |
| `Sources` | Optional source declarations using C# terms like `UniqueID`, `Type`, `Field`, and `Property`. |
| `Targets` | Optional target declarations using C# and Harmony terms like `UniqueID`, `Type`, `Method`, and `Prefix`. |
| `Bridge` | Runtime proxy settings such as `ProxyMode`, `SyncBack`, and `Cleanup`. |

The current runtime proxy implementation supports the pocket-item to auto-tool-selector pattern used by `[MP] Pockets Auto Tool Patches`. Use method patches for simple vanilla/SMAPI method changes.

## PatchInteraction

Use `PatchInteraction` for data-defined player/game interactions.

Common fields:

| Field | Meaning |
|---|---|
| `Action` | Must be `PatchInteraction`. |
| `When` | Input, game event, held tool/item, location, time, and target matching. |
| `Limit` | Optional once-per-day limits. |
| `Effects` | Effects to apply after the interaction matches. |
| `Config` | Optional config entries exposed through GMCM. |

Example:

```json
{
  "Format": "1.0.0",
  "Changes": [
    {
      "Action": "PatchInteraction",
      "Name": "Rock Pickaxe Interaction",
      "When": {
        "Input": "UseToolButton",
        "HeldToolType": "StardewValley.Tools.Pickaxe",
        "Target": {
          "Type": "NPC",
          "Names": ["Rock", "boxosoup.rock_rock"],
          "MaxTileDistance": 1.5
        }
      },
      "Limit": {
        "OncePerDay": true,
        "Key": "RockPickaxeInteraction"
      },
      "Effects": [
        {
          "Type": "ChangeFriendship",
          "Target": "MatchedNPC",
          "Amount": 5
        },
        {
          "Type": "ShowDialogue",
          "Text": "{{i18n:dialogue.likes-pickaxe}}"
        }
      ]
    }
  ]
}
```

### PatchInteraction events and effects

Supported `When.Event` values currently include:

| Event | Meaning |
|---|---|
| `ButtonPressed` | Runs when the configured input is pressed. |
| `Warped` | Runs after the player warps into the configured location. |
| `RenderedWorld` | Runs while drawing the world in the configured location. |

Useful `When` fields:

| Field | Meaning |
|---|---|
| `Location` | Current/new location name to match. |
| `TimeAtOrAfter` | Stardew time or config token, such as `{{config:CoverStartsAt}}`. |
| `DayOfWeekConfig` | Map of Stardew day names to bool config keys. |
| `DelayTicks` | Delay before applying delayed effects. |

Additional effects currently include:

| Effect type | Meaning |
|---|---|
| `AskMoneyQuestion` | Shows a vanilla question dialogue, checks money, and applies response effects. |
| `RememberForDay` | Marks the interaction as completed for the current day. |
| `Warp` | Warps the player to a location/tile. |
| `DrawSprite` | Draws a game texture at a tile during `RenderedWorld`. |

`Config` entries support `Bool` and `Number` options. Number options use `DefaultNumber`, `Min`, `Max`, and `Interval`.

## Naming

Use `[MP]` for Mod Patcher content packs. Use spaced human-facing folder and package names. Keep technical IDs stable and unspaced where needed.
