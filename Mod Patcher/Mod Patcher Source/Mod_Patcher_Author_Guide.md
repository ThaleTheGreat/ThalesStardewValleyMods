# Mod Patcher Author Guide

This guide explains how to create addon packs for **Mod Patcher**.

Mod Patcher is a SMAPI framework for Stardew Valley that lets addon packs patch loose files loaded through another mod's SMAPI mod-content pipeline. It is designed for files that Content Patcher cannot normally target because they are internal mod-local assets loaded by a DLL through `Helper.ModContent.Load`.

Mod Patcher does **not** replace files on disk. It redirects the runtime file lookup so the target mod receives your addon pack's file instead of its own local file.

## Quick example

Addon packs should use `[MP]` as the folder prefix.

```text
Mods/
  [MP] Asset Patch/
    manifest.json
    content.json
    assets/
      Icon.png
```

`manifest.json`:

```json
{
  "Name": "[MP] Asset Patch",
  "Author": "YourName",
  "Version": "1.0.0",
  "Description": "Patches an asset through Mod Patcher.",
  "UniqueID": "YourName.AssetPatch",
  "ContentPackFor": {
    "UniqueID": "ThaleTheGreat.ModPatcher",
    "MinimumVersion": "1.0.0"
  },
  "MinimumApiVersion": "4.0.0",
  "Dependencies": [
    {
      "UniqueID": "Name.ExampleMod",
      "IsRequired": true
    }
  ],
  "UpdateKeys": [
    "Nexus:0000"
  ]
}
```

`content.json`:

```json
{
    "Changes": [
    {
      "LogName": "Patch example icon",
      "Action": "PatchMod",
      "TargetMod": "Name.ExampleMod",
      "TargetPath": "Assets/Icon.png",
      "FromFile": "assets/Icon.png"
    }
  ]
}
```

## What Mod Patcher does

Mod Patcher:

- reads addon packs that use `ContentPackFor` with `ThaleTheGreat.ModPatcher`;
- reads each pack's `content.json`;
- registers asset patches from a target mod file path to an addon pack file path;
- uses Harmony to hook SMAPI's mod-content file lookup;
- returns the addon pack file when a matching target mod file is requested;
- leaves the original target mod folder unchanged.

## When to use Mod Patcher

Use Mod Patcher when all of these are true:

- The file you want to patch is a loose file inside another mod's folder.
- The target mod loads that file through SMAPI's mod-content system.
- The file is not reachable through normal Content Patcher targeting.
- You want a no-disk-replacement solution.

Do not use Mod Patcher when:

- Content Patcher can patch the asset normally.
- The target mod loads the file through raw file I/O like `File.ReadAllBytes`.
- The target file is embedded inside a DLL.
- The target is a vanilla game asset.
- The target mod already provides a config option or API for the change.

## What Mod Patcher can patch

Mod Patcher can patch loose files loaded through SMAPI's mod-content system, including:

- `.png`
- `.json`
- `.tmx`
- `.tbin`
- `.xnb`
- `.wav`
- `.ogg`
- `.txt`
- `.yaml`
- other loose files loaded through SMAPI mod content

The file type is less important than the loading path. Mod Patcher works when the target mod asks SMAPI to load the file from its own mod folder.

Mod Patcher will not catch:

- `File.ReadAllBytes(...)`
- `File.ReadAllText(...)`
- `File.OpenRead(...)`
- `Texture2D.FromStream(...)` used with raw streams
- `Assembly.GetManifestResourceStream(...)`
- files embedded inside a DLL
- vanilla game content assets loaded through `Helper.GameContent`

## Mod Patcher vs Content Patcher

Content Patcher and Mod Patcher solve different problems.

Content Patcher is for public game/content assets. It can load, edit, and replace assets like portraits, maps, data assets, and spritesheets through SMAPI's public content events. USE THIS IF ABLE TO!

Mod Patcher is for hidden/internal mod-local asset loads. It patches a file another mod loads from its own folder through SMAPI's mod-content helper.

| Case | Content Patcher | Mod Patcher |
|---|---:|---:|
| Patch `Portraits/Abigail` | Yes | No |
| Patch `Maps/Town` | Yes | No |
| Patch `Data/Objects` | Yes | No |
| Patch a public game asset through `AssetRequested` | Yes | No |
| Patch another mod's `Helper.ModContent.Load("Assets/Icon.png")` | Usually no | Yes |
| Patch raw `File.ReadAllBytes` loads | No | No |
| Edit files on disk | No | No |

The short version:

```text
Content Patcher patches public content assets.
Mod Patcher patches internal SMAPI mod-content file loads.
```

## Fields

| Field | Required | Description |
|---|---:|---|
| `LogName` | Recommended | Friendly name shown in logs. |
| `Action` | Yes | Must be `PatchMod`. |
| `TargetMod` | Yes | UniqueID of the mod whose local mod-content file should be patched. |
| `TargetPath` | Yes | Relative path requested by the target mod. |
| `FromFile` | Yes | Relative path inside your addon pack to load instead. |

No backup fields are needed. Mod Patcher does not replace files on disk.

No condition fields are needed for normal packs. Use `Dependencies` in `manifest.json` to require the target mod.

## How TargetPath works

`TargetPath` should match the path the target mod passes to `Helper.ModContent.Load`.

For example, if the target mod does this:

```csharp
Helper.ModContent.Load<Texture2D>("Assets/Icon.png");
```

your patch should use:

```json
"TargetPath": "Assets/Icon.png"
```

Do not include `SMAPI/`, the mod ID, `Mods/`, or `Content/`.

## Troubleshooting

### Target mod lookup

Mod Patcher first checks SMAPI's loaded mod registry to find the target mod. If the registry lookup cannot provide a usable folder path, Mod Patcher falls back to scanning installed `manifest.json` files with lenient JSON parsing. This helps support real-world packs whose manifests contain comments or trailing commas accepted by SMAPI.


### My patch did not apply

Check these first:

- Is Mod Patcher installed?
- Is your addon pack installed in `Mods`?
- Does your addon pack `manifest.json` point to `ThaleTheGreat.ModPatcher`?
- Is the target mod installed?
- Does the target mod dependency use `"IsRequired": true`?
- Does `TargetMod` exactly match the target mod's manifest UniqueID?
- Does `TargetPath` exactly match the path passed to `Helper.ModContent.Load`?
- Does `FromFile` exist inside your addon pack?
- Did the target mod already cache the file before Mod Patcher could patch it?
- Does the target mod actually use SMAPI mod content instead of raw file I/O?

### The target mod uses raw file loading

Mod Patcher will not catch raw file loading. Use Asset Patcher for loose files loaded through raw file I/O.

### The target file is embedded in a DLL

Mod Patcher cannot patch embedded DLL resources. That requires a targeted code patch or a change to the target mod.

## FAQ

### Is Mod Patcher the same as Content Patcher?

No. Content Patcher patches public game/content assets through SMAPI's content events. Mod Patcher patches another mod's private mod-content file lookup at runtime.

### Why not just use Content Patcher?

Use Content Patcher whenever possible. It is safer, more compatible, and designed for normal content edits.

Mod Patcher is for a narrower case: another mod's DLL loads a loose local file through `Helper.ModContent.Load`, and Content Patcher cannot target that private local file as a normal asset.

### Does Mod Patcher replace files on disk?

No. Mod Patcher redirects the runtime file lookup. The target mod's original files remain unchanged.

### Does Mod Patcher need backups?

No. Since it does not replace files on disk, there are no backup files and no restore step.

### Does Mod Patcher work on any loose file?

No. The file must be loaded through SMAPI's mod-content system. Loose files loaded through raw file I/O are not intercepted.

### Should I require the target mod in manifest.json?

Yes. If your pack targets one mod, list that mod in `Dependencies` with `"IsRequired": true`.

### Should I put Mod Patcher in Dependencies?

No. Use `ContentPackFor` for Mod Patcher. Use `Dependencies` for the target mod or other required mods.

## Generated vanilla UI sources

Mod Patcher 1.1.0 adds generated vanilla UI sources for cases where a target mod loads a private image but the replacement should inherit the player's active UI recolor.

Use exactly one source field per change: `FromFile` or `FromVanillaUi`.

Supported `FromVanillaUi` values:

| Value | Description |
|---|---|
| `MenuBox` | Generates a PNG using Stardew's vanilla menu box texture. |

Example:

```json
{
  "Changes": [
    {
      "LogName": "Utility Pocket vanilla recolor HUD",
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

Generated files are written to Mod Patcher's `generated` folder and are used as the runtime replacement file. The generated filename uses a stable patch key plus a fingerprint of the active vanilla UI texture, so switching UI recolor mods creates the correct variant and switching back reuses the previously generated variant instead of creating duplicates.


## BridgeMods runtime bridge action

Mod Patcher 1.2.0 adds the `BridgeMods` action for data-only compatibility packs that need to bridge live runtime behavior between supported mods. Its bridge kind is `ReflectionProxyBridge`, a generic reflection/proxy bridge which can be extended through source/target declarations. Use `UseCase`, `SourceRole`, `TargetRole`, `Payload`, and `Operation` to describe the bridge generically instead of hardcoding source/target types.

`BridgeMods` does not activate unless at least one declared source mod and at least one declared target mod are loaded. All provider/target dependencies can be optional in the content pack manifest for mix-and-match compatibility.

Example:

```json
{
  "Changes": [
    {
      "Action": "BridgeMods",
      "Name": "Pockets Auto Tool Patches",
      "RequireAnySource": [
        "aedenthorn.Pockets",
        "ModderDrew.UtilityBelt",
        "ModderDrew.UtilityPocket"
      ],
      "RequireAnyTarget": [
        "Trapyy.AutomatetoolSwap",
        "aedenthorn.ToolSmartSwitch",
        "lolmaj.AutoToolSelect"
      ],
      "Sources": [
        { "ModID": "aedenthorn.Pockets", "Kind": "InventoryProvider" },
        { "ModID": "ModderDrew.UtilityBelt", "Kind": "InventoryProvider" },
        { "ModID": "ModderDrew.UtilityPocket", "Kind": "SingleItemProvider" }
      ],
      "Targets": [
        { "ModID": "Trapyy.AutomatetoolSwap", "Kind": "ToolSelector" },
        { "ModID": "aedenthorn.ToolSmartSwitch", "Kind": "ToolSelector" },
        { "ModID": "lolmaj.AutoToolSelect", "Kind": "ToolSelector" }
      ],
      "Bridge": {
        "Kind": "ReflectionProxyBridge",
        "UseCase": "RuntimeProxy",
        "SourceRole": "Provider",
        "TargetRole": "Consumer",
        "Payload": "Item",
        "Operation": "TemporaryProxy",
        "ProxyMode": "HiddenAppendedSlot",
        "SyncBack": true,
        "Cleanup": "AfterUse"
      }
    }
  ]
}
```
