using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Objects;
using ThaleTheGreat.Gatherers.Services;

namespace ThaleTheGreat.Gatherers.Patches;

internal static class ChestDrawPatch
{
    internal static bool Prefix(Chest __instance, SpriteBatch spriteBatch, int x, int y, float alpha)
    {
        bool isHarvestStatue = StorageMarker.IsHarvestStatue(__instance);
        bool isParrotPot = StorageMarker.IsParrotPot(__instance);
        if (!isHarvestStatue && !isParrotPot)
            return true;

        bool filled = __instance.Items.Any(item => item is not null);
        string asset = isHarvestStatue
            ? filled ? "Mods/ThaleTheGreat.Gatherers/HarvestStatueFilled" : "Mods/ThaleTheGreat.Gatherers/HarvestStatueEmpty"
            : filled ? "Mods/ThaleTheGreat.Gatherers/ParrotPotFilled" : "Mods/ThaleTheGreat.Gatherers/ParrotPotEmpty";

        float drawX = x;
        float drawY = y;
        if (__instance.localKickStartTile.HasValue)
        {
            drawX = Utility.Lerp(__instance.localKickStartTile.Value.X, drawX, __instance.kickProgress);
            drawY = Utility.Lerp(__instance.localKickStartTile.Value.Y, drawY, __instance.kickProgress);
        }

        float layerDepth = Math.Max(0f, ((drawY + 1f) * 64f - 24f) / 10000f) + drawX * 0.00001f;
        if (__instance.localKickStartTile.HasValue)
        {
            spriteBatch.Draw(
                Game1.shadowTexture,
                Game1.GlobalToLocal(Game1.viewport, new Vector2((drawX + 0.5f) * 64f, (drawY + 0.5f) * 64f)),
                Game1.shadowTexture.Bounds,
                Color.Black * 0.5f,
                0f,
                new Vector2(Game1.shadowTexture.Bounds.Center.X, Game1.shadowTexture.Bounds.Center.Y),
                4f,
                SpriteEffects.None,
                0.0001f);
            drawY -= (float)Math.Sin(__instance.kickProgress * Math.PI) * 0.5f;
        }

        Texture2D texture = Game1.content.Load<Texture2D>(asset);
        Vector2 position = Game1.GlobalToLocal(
            Game1.viewport,
            new Vector2(
                drawX * 64f + (__instance.shakeTimer > 0 ? Game1.random.Next(-1, 2) : 0),
                (drawY - 1f) * 64f));
        spriteBatch.Draw(texture, position, new Rectangle(0, 0, 16, 32), __instance.Tint * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth);
        return false;
    }
}
