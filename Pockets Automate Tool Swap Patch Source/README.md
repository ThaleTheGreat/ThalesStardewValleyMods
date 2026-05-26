# Pockets Automate Tool Swap Patch

Compatibility patch for Pockets and Automate Tool Swap.

When Automate Tool Swap asks for a tool or item that is not in the normal player inventory, this patch checks Pockets inventories. If a matching pocket item is found, it appends a temporary invisible inventory slot beyond the normal backpack, places a proxy copy there, selects that slot for the original click/use, then removes the slot after the use input is released. The original pocket item is not moved out of Pockets.

For stackable items, the patch subtracts the consumed amount from the original pocket stack after the proxy is used. For tools, the patch syncs the used proxy back into the original pocket slot so tool state changes are preserved. This avoids requiring an empty backpack slot and avoids replacing the currently selected slot.

## Requirements

- SMAPI
- Pockets
- Automate Tool Swap

## Notes

This patch does not redistribute code from either mod. It uses Harmony and reflection at runtime to bridge their inventories.
