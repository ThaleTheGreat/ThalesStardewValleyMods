# Asset Patcher Author Guide

This guide explains how to create addon packs for **Asset Patcher**.

Asset Patcher is a SMAPI framework for Stardew Valley that lets addon packs replace loose files inside another installed mod folder at launch. It is for files that are not exposed through Content Patcher or normal SMAPI asset replacement because the target mod loads the file directly from its own folder.

Asset Patcher changes files on disk. It creates original backups automatically, applies replacements again after target mod updates, and lets players restore backups through Generic Mod Config Menu or console commands.

## Contents

- [Introduction](#introduction)
- [What Asset Patcher can replace](#what-asset-patcher-can-replace)
- [Asset Patcher vs Content Patcher](#asset-patcher-vs-content-patcher)
- [Addon pack structure](#addon-pack-structure)
- [manifest.json](#manifestjson)
- [content.json](#contentjson)
- [Replacement fields](#replacement-fields)
- [Examples](#examples)
- [Backups and restores](#backups-and-restores)
- [Troubleshooting](#troubleshooting)
- [Best practices](#best-practices)
- [FAQ](#faq)

## Introduction

Asset Patcher does not add content by itself. It loads addon packs that point to `ThaleTheGreat.AssetPatcher`, reads their `content.json`, and copies replacement files into selected target mod folders.

Use Asset Patcher when a target mod has a loose internal file that cannot be reached through normal Content Patcher or SMAPI asset patching.

Do not use Asset Patcher for vanilla game files, normal Content Patcher targets, or files better handled by the target mod's config options or API.

## What Asset Patcher can replace

Asset Patcher can replace loose files inside an installed mod folder, including:

- `.png`
- `.json`
- `.tmx`
- `.tbin`
- `.xnb`
- `.wav`
- `.ogg`
- `.txt`
- `.yaml`
- other loose files in a mod folder

Asset Patcher cannot replace files embedded inside a DLL. It also should not be used to replace files outside installed mod folders.

## Asset Patcher vs Content Patcher

Content Patcher changes game assets through SMAPI's content pipeline. Asset Patcher replaces loose files inside another installed mod folder on disk.

Use Content Patcher for normal Stardew Valley assets like:

```text
Portraits/Abigail
Maps/Town
Data/Objects
LooseSprites/Cursors
```

Use Asset Patcher for loose internal mod files like:

```text
Mods/Example Mod/Assets/Icon.png
Mods/Example Mod/assets/data.json
Mods/Example Mod/maps/custom-map.tmx
Mods/Example Mod/audio/sound.ogg
```

If Content Patcher can reach the file cleanly, use Content Patcher. Use Asset Patcher only for files that are not exposed through normal patching.

## Addon pack structure

An Asset Patcher addon pack is a SMAPI content pack. It has a `manifest.json`, a `content.json`, and usually an `assets` folder containing replacement files.

Use `[AP]` as the folder prefix.

```text
Mods/
  [AP] Asset Replacement/
    manifest.json
    content.json
    assets/
      Icon.png
```

## manifest.json

The manifest tells SMAPI that Asset Patcher is the framework for this pack. The target mod should be listed as a required dependency.

```json
{
  "Name": "[AP] Asset Replacement",
  "Author": "YourName",
  "Version": "1.0.0",
  "Description": "Replaces an asset through Asset Patcher.",
  "UniqueID": "YourName.AssetPatch",
  "ContentPackFor": {
    "UniqueID": "ThaleTheGreat.AssetPatcher",
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

Change these fields for your own pack:

- `Name`
- `Author`
- `Version`
- `Description`
- `UniqueID`
- `Dependencies`
- `UpdateKeys`

Do not add Asset Patcher again under `Dependencies`. `ContentPackFor` already declares Asset Patcher as the framework for the addon pack.

## content.json

The `content.json` file tells Asset Patcher which files to replace.

```json
{
  "Format": "1.0.0",
  "Replacements": [
    {
      "LogName": "Replace example icon",
      "Action": "ReplaceFile",
      "TargetMod": "Name.ExampleMod",
      "TargetPath": "Assets/Icon.png",
      "FromFile": "assets/Icon.png"
    }
  ]
}
```

Each object in `Replacements` describes one file replacement.

## Replacement fields

| Field | Required | Description |
|---|---:|---|
| `LogName` | Recommended | Friendly name shown in logs. |
| `Action` | Yes | Must be `ReplaceFile`. |
| `TargetMod` | Yes | UniqueID of the mod whose file should be replaced. |
| `TargetPath` | Yes | Relative path inside the target mod folder. |
| `FromFile` | Yes | Relative path inside your addon pack to copy from. |

Asset Patcher handles backup creation and replacement checks automatically. Addon packs do not need extra fields for those behaviors.

Backup names are extension-aware:

```text
Icon.png -> Icon.original.png
data.json -> data.original.json
```

Asset Patcher validates paths and refuses unsafe paths that try to escape the target mod folder.

## Examples

### Replace one PNG

This replaces `Assets/Icon.png` inside the `Name.ExampleMod` mod folder with the addon pack file `assets/Icon.png`.

`manifest.json`:

```json
{
  "Name": "[AP] Asset Replacement",
  "Author": "YourName",
  "Version": "1.0.0",
  "Description": "Replaces an asset through Asset Patcher.",
  "UniqueID": "YourName.AssetPatch",
  "ContentPackFor": {
    "UniqueID": "ThaleTheGreat.AssetPatcher",
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
  "Format": "1.0.0",
  "Replacements": [
    {
      "LogName": "Replace example icon",
      "Action": "ReplaceFile",
      "TargetMod": "Name.ExampleMod",
      "TargetPath": "Assets/Icon.png",
      "FromFile": "assets/Icon.png"
    }
  ]
}
```

Folder layout:

```text
Mods/
  [AP] Asset Replacement/
    manifest.json
    content.json
    assets/
      Icon.png
```

### Replace multiple files

```json
{
  "Format": "1.0.0",
  "Replacements": [
    {
      "LogName": "Replace first texture",
      "Action": "ReplaceFile",
      "TargetMod": "Name.ExampleMod",
      "TargetPath": "Assets/First.png",
      "FromFile": "assets/First.png"
    },
    {
      "LogName": "Replace second texture",
      "Action": "ReplaceFile",
      "TargetMod": "Name.ExampleMod",
      "TargetPath": "Assets/Second.png",
      "FromFile": "assets/Second.png"
    }
  ]
}
```

### Replace a JSON file

```json
{
  "Format": "1.0.0",
  "Replacements": [
    {
      "LogName": "Replace target mod data file",
      "Action": "ReplaceFile",
      "TargetMod": "Name.ExampleMod",
      "TargetPath": "assets/data.json",
      "FromFile": "assets/data.json"
    }
  ]
}
```

## Backups and restores

Asset Patcher automatically creates a backup before replacing a target file.

Example:

```text
Assets/Icon.png
Assets/Icon.original.png
```

If the backup already exists, Asset Patcher keeps the existing backup and does not overwrite it. This preserves the player's original copy of the target file.

If the target mod updates and restores the original file, leaving the addon pack installed lets Asset Patcher apply the replacement again on the next launch.

Players can restore backups through Asset Patcher's Generic Mod Config Menu page.

Console commands are also available:

```text
ap_restore <TargetModID> <TargetPath>
ap_restore_all
```

Example:

```text
ap_restore Name.ExampleMod Assets/Icon.png
```

A successful restore copies the backup back over the target file, deletes the backup file, and removes it from Asset Patcher's restore list.

## Troubleshooting

### My replacement did not apply

Check these first:

- Is Asset Patcher installed?
- Is your addon pack installed in `Mods`?
- Does your addon pack `manifest.json` point to `ThaleTheGreat.AssetPatcher`?
- Is the target mod installed?
- Does the target mod dependency use `"IsRequired": true`?
- Does `TargetMod` exactly match the target mod's manifest UniqueID?
- Does `TargetPath` exactly match the file location inside the target mod folder?
- Does `FromFile` exist inside your addon pack?
- Is the replacement already identical to the target file?

Enable Asset Patcher debug logging in GMCM for details.

### The target mod updated

Leave the addon pack installed. Asset Patcher will compare the target file to your replacement and apply the replacement again if needed.

### I removed the addon pack, but the replacement stayed

Removing an addon pack stops future replacements, but it does not automatically restore files already replaced. Use Asset Patcher's GMCM restore page or console commands to restore backups.

### The target file is inside a DLL

Asset Patcher cannot replace embedded DLL resources. The target must be a loose file inside the installed mod folder.

## Best practices

- Use `[AP]` as the folder prefix for Asset Patcher addon packs.
- Keep each pack focused on one target mod or one small compatibility purpose.
- Use clear `LogName` values.
- Only replace files you understand.
- Avoid replacing broad groups of files.
- Do not use absolute paths.
- Do not try to escape the target mod folder.
- Keep replacement assets in an `assets` folder.
- Test after target mod updates.
- Include a short note on your mod page explaining exactly which files are replaced.

## FAQ

### Is this the same as Content Patcher?

No. Content Patcher changes assets through SMAPI's content pipeline. Asset Patcher physically replaces loose files in another installed mod folder. Use Content Patcher when it can do the job; use Asset Patcher only when the target file is not available through normal patching.

### Does Asset Patcher edit DLL files?

No. Asset Patcher is for loose files like PNG, JSON, maps, audio, or similar files in a mod folder. It does not patch code.

### Can Asset Patcher replace vanilla game files?

No. Asset Patcher is intended for installed mod folders, not Stardew Valley's vanilla `Content` folder.

### What happens if two addon packs replace the same file?

The last applied replacement wins, based on SMAPI's content pack load order. Avoid having multiple addon packs target the same file unless you know which one should win.

### Can players remove my addon pack later?

Yes. Removing the addon pack stops future replacements. To undo an already-applied replacement, players should use Asset Patcher's GMCM restore page or console commands.

### Should I include the target mod's original file in my addon pack?

No. Only include your replacement file. Asset Patcher creates the backup from the player's installed copy of the target mod.
