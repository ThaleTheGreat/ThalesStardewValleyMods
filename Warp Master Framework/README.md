# Warp Master Framework

Visual warp editor and framework for Stardew Valley warp overrides.

## Content Patcher override asset

Content Patcher packs can patch this asset:

```text
Mods/ThaleTheGreat.WarpMasterFramework/WarpOverrides
```

Shape:

```json
{
  "Format": "1.0.0",
  "Overrides": [
    {
      "Id": "Example_Farm_64_15",
      "SourceMap": "Farm",
      "WarpType": "Warp",
      "OriginalX": 64,
      "OriginalY": 15,
      "OriginalTargetMap": "BusStop",
      "OriginalTargetX": 0,
      "OriginalTargetY": 23,
      "NewX": 64,
      "NewY": 16,
      "TargetMap": "Town",
      "TargetX": 10,
      "TargetY": 10
    }
  ]
}
```

Set `"Delete": true` to remove a matched warp.

## Export

Run this SMAPI console command in-game:

```text
wmf_export
```

It writes these files into this mod's `exports` folder:

- `original-warps.json`
- `warp-overrides-template.json`

## Read-only data assets

The framework also provides these read-only data assets:

```text
Mods/ThaleTheGreat.WarpMasterFramework/OriginalWarps
Mods/ThaleTheGreat.WarpMasterFramework/ModDetails
```
