using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using ThaleTheGreat.MoonMisadventures.Game.Monsters;

namespace ThaleTheGreat.MoonMisadventures.Game
{
    internal static class BossHealthBar
    {
        private static Rectangle GetFrame(Texture2D texture, int verticalFrames, int frameY)
        {
            int frameHeight = texture.Height / verticalFrames;
            return new Rectangle(0, frameHeight * frameY, texture.Width, frameHeight);
        }

        internal static void Draw(SpriteBatch spriteBatch, EyeOfCthulhu boss)
        {
            Texture2D barTexture = Assets.EyeBossBar;
            Texture2D iconTexture = boss.Phase.Value == 1 ? Assets.EyeBossIconPhaseOne : Assets.EyeBossIconPhaseTwo;
            const int barWidth = 456;
            const int barHeight = 22;
            const int frameCount = 6;
            Point fillOffset = new(32, 24);

            float healthRatio = MathHelper.Clamp(boss.Health / (float)boss.MaxHealth, 0f, 1f);
            int filledWidth = (int)(barWidth * healthRatio);
            filledWidth -= filledWidth % 2;

            Toolbar? toolbar = null;
            foreach (IClickableMenu menu in Game1.onScreenMenus)
            {
                if (menu is Toolbar foundToolbar)
                {
                    toolbar = foundToolbar;
                    break;
                }
            }
            int bottomY = Game1.uiViewport.Height;
            if (toolbar != null && toolbar.yPositionOnScreen - toolbar.toolbarTextSource.Height > Game1.uiViewport.Height - 100)
                bottomY = toolbar.yPositionOnScreen - toolbar.toolbarTextSource.Height - 20;

            Rectangle barBounds = new(
                (Game1.uiViewport.Width - barWidth) / 2,
                bottomY - 61,
                barWidth,
                barHeight
            );
            Vector2 framePosition = new(barBounds.X - fillOffset.X, barBounds.Y - fillOffset.Y);

            spriteBatch.Draw(barTexture, framePosition, GetFrame(barTexture, frameCount, 3), Color.White * 0.2f);

            if (filledWidth > 0)
            {
                Rectangle fillSource = GetFrame(barTexture, frameCount, 2);
                fillSource.X += fillOffset.X;
                fillSource.Y += fillOffset.Y;
                fillSource.Width = 2;
                fillSource.Height = barHeight;
                spriteBatch.Draw(
                    barTexture,
                    new Vector2(barBounds.X, barBounds.Y),
                    fillSource,
                    Color.White,
                    0f,
                    Vector2.Zero,
                    new Vector2(filledWidth / 2f, 1f),
                    SpriteEffects.None,
                    0f
                );

                Rectangle fillEndSource = GetFrame(barTexture, frameCount, 1);
                fillEndSource.X += fillOffset.X;
                fillEndSource.Y += fillOffset.Y;
                fillEndSource.Width = 2;
                fillEndSource.Height = barHeight;
                spriteBatch.Draw(barTexture, new Vector2(barBounds.X + filledWidth - 2, barBounds.Y), fillEndSource, Color.White);
            }

            spriteBatch.Draw(barTexture, framePosition, GetFrame(barTexture, frameCount, 0), Color.White);
            spriteBatch.Draw(
                iconTexture,
                framePosition + new Vector2(30f, 34f),
                null,
                Color.White,
                0f,
                new Vector2(iconTexture.Width / 2f, iconTexture.Height / 2f),
                1f,
                SpriteEffects.None,
                0f
            );
        }
    }
}
