using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewValley;
using StardewValley.Monsters;

namespace ThaleTheGreat.MoonMisadventures.Game.Monsters
{
    [XmlType("Mods_ThaleTheGreat_MoonMisadventures_ServantOfCthulhu")]
    public class ServantOfCthulhu : Monster
    {
        private const int SpriteWidth = 10;
        private const int SpriteHeight = 16;
        private const float MaximumSpeed = 8f;

        public readonly NetFloat VelocityX = new();
        public readonly NetFloat VelocityY = new();

        public Vector2 InitialVelocity
        {
            set
            {
                VelocityX.Value = value.X;
                VelocityY.Value = value.Y;
            }
        }

        public ServantOfCthulhu()
        {
        }

        public ServantOfCthulhu(Vector2 position)
            : base("Bat", position)
        {
            Name = "ServantOfCthulhu";
            displayName = I18n.Monster_ServantOfCthulhu_Name();
            Health = MaxHealth = 8;
            DamageToFarmer = 12;
            resilience.Value = 0;
            speed = 0;
            Slipperiness = 0;
            IsWalkingTowardPlayer = false;
            HideShadow = true;
            isGlider.Value = true;
            objectsToDrop.Clear();
            reloadSprite();
        }

        protected override void initNetFields()
        {
            base.initNetFields();
            NetFields.AddField(VelocityX, nameof(VelocityX));
            NetFields.AddField(VelocityY, nameof(VelocityY));
        }

        public override void reloadSprite(bool onlyAppearance = false)
        {
            Sprite = new AnimatedSprite(
                Mod.instance.Helper.ModContent.GetInternalAssetName("assets/bosses/servant-of-cthulhu.png").BaseName,
                0,
                SpriteWidth,
                SpriteHeight
            );
            HideShadow = true;
        }

        public override List<Item> getExtraDropItems()
        {
            return new List<Item>();
        }

        public override void behaviorAtGameTick(GameTime time)
        {
            if (!Game1.IsMasterGame || currentLocation == null || Health <= 0)
                return;

            Farmer target = Player;
            if (target == null)
                return;

            Vector2 desired = target.Position - Position;
            if (desired.LengthSquared() > 0.001f)
            {
                desired.Normalize();
                desired *= MaximumSpeed;
            }

            VelocityX.Value = MathHelper.Lerp(VelocityX.Value, desired.X, 0.1f);
            VelocityY.Value = MathHelper.Lerp(VelocityY.Value, desired.Y, 0.1f);
            position.Set(new Vector2(position.X + VelocityX.Value, position.Y + VelocityY.Value));
            rotation = (float)Math.Atan2(VelocityY.Value, VelocityX.Value) + MathHelper.PiOver2;
        }

        protected override void updateAnimation(GameTime time)
        {
            Sprite.Animate(time, 0, 2, 200f);
        }

        public override Rectangle GetBoundingBox()
        {
            return new Rectangle((int)Position.X + 12, (int)Position.Y + 12, 40, 40);
        }

        public override void drawAboveAllLayers(SpriteBatch b)
        {
            if (!Utility.isOnScreen(Position, 128))
                return;

            Vector2 drawPosition = Game1.GlobalToLocal(Game1.viewport, Position) + new Vector2(32f, 32f);
            b.Draw(
                Sprite.Texture,
                drawPosition,
                Sprite.SourceRect,
                Color.White,
                rotation,
                new Vector2(SpriteWidth / 2f, SpriteHeight / 2f),
                4f,
                SpriteEffects.None,
                Math.Max(0f, drawOnTop ? 0.991f : (StandingPixel.Y + 8f) / 10000f)
            );
        }
    }
}
