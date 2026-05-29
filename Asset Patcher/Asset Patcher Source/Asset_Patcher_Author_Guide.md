# Asset Patcher Author Guide

This guide explains how to create addon packs for **Asset Patcher**.

Asset Patcher is a SMAPI framework that lets small addon packs replace loose files inside another installed mod's folder at launch. It is meant for cases where a target mod loads a file directly from its own folder, so Content Patcher or normal SMAPI asset replacement cannot reach it.

Use Asset Patcher carefully. It physically replaces files on disk, creates an original backup, and can restore those backups through GMCM or console commands.

## Contents

- [Introduction](#introduction)
  - [What is Asset Patcher?](#what-is-asset-patcher)
  - [When should I use Asset Patcher?](#when-should-i-use-asset-patcher)
  - [What does an addon pack look like?](#what-does-an-addon-pack-look-like)
- [Get started](#get-started)
  - [Create the addon pack](#create-the-addon-pack)
  - [Manifest](#manifest)
  - [content.json](#contentjson)
- [Replacement format](#replacement-format)
  - [Top-level fields](#top-level-fields)
  - [Replacement fields](#replacement-fields)
  - [Conditions](#conditions)
- [Examples](#examples)
  - [Replace one PNG](#replace-one-png)
  - [Replace multiple files](#replace-multiple-files)
  - [Require multiple mods](#require-multiple-mods)
- [Backups and restores](#backups-and-restores)
- [Troubleshooting](#troubleshooting)
- [Best practices](#best-practices)
- [FAQ](#faq)

## Introduction

### What is Asset Patcher?

Asset Patcher is a SMAPI framework mod. It does not add content by itself. Instead, it reads addon packs installed for it and applies file replacements described in their `content.json` files.

An addon pack can say:

> If mod `Xan.MoreStats` is installed, copy my `assets/InfoTab.png` over `MoreStats/Assets/InfoTab.png`, but first create a backup named `InfoTab.original.png`.

Asset Patcher runs early during startup so the replacement can happen before the target mod loads the loose file.

### When should I use Asset Patcher?

Use Asset Patcher when all of these are true:

- You need to replace a loose file inside another mod's folder.
- The target file is not exposed through Content Patcher.
- The target mod does not provide a normal config option or API for replacing that file.
- The target file can safely be replaced on disk.

Do **not** use Asset Patcher for vanilla game files, normal Content Patcher targets, or files that are better handled by the target mod's own config/API.

### What does an addon pack look like?

An Asset Patcher addon pack is a normal SMAPI content pack. It has a `manifest.json`, a `content.json`, and usually an `assets` folder containing the replacement files.

```text
Mods/
  [AP] Your Addon Pack/
    manifest.json
    content.json
    assets/
      Example.png
```

## Get started

### Create the addon pack

1. Install SMAPI.
2. Install Asset Patcher.
3. Create a new folder in your `Mods` folder.
4. Name it with an `[AP]` prefix, like `[AP] More Stats Info Tab`.
5. Add a `manifest.json` file.
6. Add a `content.json` file.
7. Add your replacement files in an `assets` folder.

### Manifest

Your addon pack's `manifest.json` tells SMAPI that Asset Patcher should load it.

```json
{
  "Name": "[AP] Your Addon Pack",
  "Author": "YourName",
  "Version": "1.0.0",
  "Description": "Replaces one or more loose files through Asset Patcher.",
  "UniqueID": "YourName.YourAddonPack",
  "ContentPackFor": {
    "UniqueID": "ThaleTheGreat.AssetPatcher"
  },
  "MinimumApiVersion": "4.0.0",
  "UpdateKeys": [
    "Nexus:0000"
  ]
}
```

Change `Name`, `Author`, `Description`, `UniqueID`, and `UpdateKeys` for your own pack.

Do not change this part unless Asset Patcher's UniqueID changes:

```json
"ContentPackFor": {
  "UniqueID": "ThaleTheGreat.AssetPatcher"
}
```

### content.json

Your `content.json` tells Asset Patcher what to replace.

```json
{
  "Format": "1.0.0",
  "Replacements": []
}
```

Each entry in `Replacements` describes one file replacement.

## Replacement format

### Top-level fields

| Field | Required | Description |
|---|---:|---|
| `Format` | Yes | The Asset Patcher content format. Use `1.0.0`. |
| `Replacements` | Yes | A list of file replacements to apply. |

### Replacement fields

| Field | Required | Description |
|---|---:|---|
| `LogName` | Recommended | Friendly name shown in logs. |
| `Action` | Yes | Must be `ReplaceFile`. |
| `TargetMod` | Yes | UniqueID of the mod whose file should be replaced. |
| `TargetPath` | Yes | Relative path inside the target mod folder. |
| `FromFile` | Yes | Relative path inside your addon pack to copy from. |
| `CreateBackup` | No | Whether to create a backup before replacing. Defaults to `true`. |
| `ReapplyWhenTargetChanged` | No | Whether to copy again if the target mod updates/restores the file. Defaults to `true`. |
| `When` | No | Conditions that must match before replacement runs. |

Backup names are extension-aware:

```text
InfoTab.png -> InfoTab.original.png
somefile.json -> somefile.original.json
```

Asset Patcher validates paths and refuses unsafe paths that try to escape the target mod folder.

## Conditions

The `When` field can limit a replacement to certain installed mods.

| Field | Description |
|---|---|
| `HasMod` | One mod UniqueID or a list of UniqueIDs that must be installed. |
| `NotHasMod` | One mod UniqueID or a list of UniqueIDs that must not be installed. |

Example:

```json
"When": {
  "HasMod": [
    "Xan.MoreStats"
  ],
  "NotHasMod": [
    "SomeOtherAuthor.SomeConflictMod"
  ]
}
```

## Examples

### Replace one PNG

This replaces `Assets/InfoTab.png` inside the `Xan.MoreStats` mod folder with your addon pack's `assets/InfoTab.png`.

```json
{
  "Format": "1.0.0",
  "Replacements": [
    {
      "LogName": "Replace More Stats InfoTab.png",
      "Action": "ReplaceFile",
      "TargetMod": "Xan.MoreStats",
      "TargetPath": "Assets/InfoTab.png",
      "FromFile": "assets/InfoTab.png",
      "CreateBackup": true,
      "ReapplyWhenTargetChanged": true,
      "When": {
        "HasMod": [
          "Xan.MoreStats"
        ]
      }
    }
  ]
}
```

Folder layout:

```text
Mods/
  [AP] More Stats Info Tab/
    manifest.json
    content.json
    assets/
      InfoTab.png
```

### Replace multiple files

```json
{
  "Format": "1.0.0",
  "Replacements": [
    {
      "LogName": "Replace first texture",
      "Action": "ReplaceFile",
      "TargetMod": "ExampleAuthor.ExampleMod",
      "TargetPath": "Assets/First.png",
      "FromFile": "assets/First.png"
    },
    {
      "LogName": "Replace second texture",
      "Action": "ReplaceFile",
      "TargetMod": "ExampleAuthor.ExampleMod",
      "TargetPath": "Assets/Second.png",
      "FromFile": "assets/Second.png"
    }
  ]
}
```

### Require multiple mods

```json
{
  "Format": "1.0.0",
  "Replacements": [
    {
      "LogName": "Replace compatibility texture",
      "Action": "ReplaceFile",
      "TargetMod": "ExampleAuthor.ExampleMod",
      "TargetPath": "Assets/Icon.png",
      "FromFile": "assets/Icon.png",
      "When": {
        "HasMod": [
          "ExampleAuthor.ExampleMod",
          "OtherAuthor.RequiredMod"
        ]
      }
    }
  ]
}
```

## Backups and restores

Asset Patcher creates backups before replacing files when `CreateBackup` is true.

For example:

```text
Assets/InfoTab.png
Assets/InfoTab.original.png
```

If the backup already exists, Asset Patcher keeps it and does not overwrite it. This protects the original file from being replaced by a later patched version.

If the target mod updates and restores its original file, Asset Patcher can reapply your replacement on the next launch when `ReapplyWhenTargetChanged` is true.

Players can restore backups through Asset Patcher's GMCM page. Console commands are also available:

```text
ap_restore <TargetModID> <TargetPath>
ap_restore_all
```

Example:

```text
ap_restore Xan.MoreStats Assets/InfoTab.png
```

## Troubleshooting

### My replacement did not apply

Check these first:

- Is Asset Patcher installed?
- Is your addon pack installed in `Mods`?
- Does your addon pack `manifest.json` point to `ThaleTheGreat.AssetPatcher`?
- Does the target mod UniqueID exactly match the installed mod's manifest?
- Does `TargetPath` exactly match the file location inside the target mod folder?
- Does `FromFile` exist inside your addon pack?
- Is the target file locked or unavailable?
- Is the replacement already identical to the target file?

Enable Asset Patcher debug logging in GMCM for more detail.

### My backup was not created

Backups are only created when `CreateBackup` is true and the target file exists. If a backup already exists, Asset Patcher will not overwrite it.

### The target mod updated

Leave the addon pack installed. If `ReapplyWhenTargetChanged` is true, Asset Patcher will compare the target file to your replacement and apply it again when needed.

### I removed the addon pack, but the replacement stayed

Removing an addon pack stops future replacements, but it does not automatically restore already replaced files. Use the GMCM restore page or the console restore commands to restore backups.

## Best practices

- Use `[AP]` as the folder prefix for Asset Patcher addon packs.
- Keep packs focused on one target mod or one small compatibility purpose.
- Use clear `LogName` values.
- Only replace files you understand.
- Avoid replacing broad groups of files.
- Do not use `../` or absolute paths.
- Keep replacement assets in an `assets` folder.
- Test after target mod updates.
- Include a short note on your mod page explaining exactly which files are replaced.

## FAQ

### Is this the same as Content Patcher?

No. Content Patcher changes assets through SMAPI's content pipeline. Asset Patcher physically replaces loose files in another installed mod's folder. Use Content Patcher when it can do the job; use Asset Patcher only when the target file is not available through normal patching.

### Does Asset Patcher edit DLL files?

No. Asset Patcher is for loose files like PNG, JSON, or similar files in a mod folder. It does not patch code.

### Can Asset Patcher replace vanilla game files?

No. Asset Patcher is intended for installed mod folders, not Stardew Valley's vanilla `Content` folder.

### What happens if two addon packs replace the same file?

The last applied replacement wins, based on SMAPI's content pack load order. Avoid having multiple addon packs target the same file unless you know which one should win.

### Can players remove my addon pack later?

Yes. Removing the addon pack stops future replacements. To undo an already-applied replacement, players should use Asset Patcher's GMCM restore page or console restore commands.

### Should I include the target mod's original file in my addon pack?

No. Only include your replacement file. Asset Patcher creates the backup from the player's installed copy of the target mod.
