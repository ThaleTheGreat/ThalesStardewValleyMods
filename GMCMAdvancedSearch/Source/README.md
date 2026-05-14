# GMCM Advanced Search

GMCM Advanced Search is a SMAPI utility mod for Stardew Valley that adds a searchable in-game menu for finding settings across mods that use Generic Mod Config Menu.

Instead of searching only by mod name, GMCM Advanced Search searches the actual option metadata: option labels, tooltips, field IDs, and config key paths. This makes it easier to find a setting when you remember what it does, but not which mod added it.

## Requirements

- Stardew Valley 1.6+
- SMAPI 4.0.0+
- Generic Mod Config Menu

## Installation

1. Install SMAPI.
2. Install Generic Mod Config Menu.
3. Download GMCM Advanced Search.
4. Unzip the mod folder into your Stardew Valley `Mods` folder.
5. Launch the game through SMAPI.

The installed folder should look like this:

```text
Stardew Valley/
  Mods/
    GMCMAdvancedSearch/
      manifest.json
      GMCMAdvancedSearch.dll
      LICENSE
      README.md
```

## Usage

Press **F2** by default to open the search menu.

Type a word or phrase related to the option you want to find. For example:

- `cat`
- `speed`
- `quality`
- `hotkey`
- `farm`
- `controller`

Matching results are shown as GMCM-style rows. Click a result to open that mod's config page in Generic Mod Config Menu.

Press **Esc** or controller back to close the search menu.

## What It Searches

GMCM Advanced Search can search:

- GMCM option labels
- GMCM option tooltips
- GMCM field IDs
- `config.json` key paths

It does not use mod names or UniqueIDs as search matches, so results should point to actual settings rather than just mods with matching names.

## Options

GMCM Advanced Search has its own Generic Mod Config Menu page.

### Open Search Menu

Changes the hotkey used to open the search menu.

Default: **F2**

### Show UniqueID

Shows each result's mod UniqueID under the result.

Default: **Off**

### Show Advanced Details

Shows extra metadata under each result, including section/page, tooltip text, field ID, config key path, and option type.

Default: **Off**

### Show Mod Tooltips

Shows a tooltip when hovering over a result. The tooltip includes the mod name and mod description, matching the kind of information shown by GMCM's mod list.

Default: **On**

### Include Content Packs

Includes GMCM-registered content packs in search results.

Default: **On**

### Include config.json Fallback

Searches `config.json` keys when full GMCM option metadata cannot be read.

Default: **On**

### Search Config Values

Also indexes simple `config.json` values. This can produce noisier results, so it is disabled by default.

Default: **Off**

### Debug Logging

Logs extra indexing and reflection details to the SMAPI console for troubleshooting.

Default: **Off**

## Recolor and UI Compatibility

GMCM Advanced Search is designed to visually fit with Generic Mod Config Menu.

The menu uses Stardew/GMCM-style UI drawing and vanilla cursor textures for the scrollbar, so it should work cleanly with UI recolor mods such as earthy recolors or other mods that adjust the game's menu colors and accents.

## Notes

GMCM's internal option data is not exposed through a simple public search API, so this mod builds its index by reading available GMCM metadata and using `config.json` fallback scanning when needed.

Some mods expose more searchable metadata than others. If a mod's option labels or tooltips cannot be read, GMCM Advanced Search may still find its settings through config key paths.

## License

GMCM Advanced Search is released under the MIT License. See [LICENSE](LICENSE) for the full license text.
