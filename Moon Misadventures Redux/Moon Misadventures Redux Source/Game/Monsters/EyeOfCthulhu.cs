using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewValley;
using StardewValley.Monsters;
using ThaleTheGreat.MoonMisadventures.Game.Locations;

namespace ThaleTheGreat.MoonMisadventures.Game.Monsters
{
    [XmlType("Mods_ThaleTheGreat_MoonMisadventures_EyeOfCthulhu")]
    public class EyeOfCthulhu : Monster
    {
        private const int SpriteWidth = 55;
        private const int SpriteHeight = 83;
        private const int PhaseTwoHealthThreshold = 1400;
        private const int ServantInterval = 110;
        private const int HoverDuration = 360;
        private const int DashDuration = 40;
        private const int RecoveryDuration = 45;
        private const int MaxServants = 6;
        private const float HoverSpeed = 5f;
        private const float DashSpeed = 20f;

        public readonly NetInt Phase = new(1);
        public readonly NetInt AttackState = new();
        public readonly NetInt AttackTimer = new();
        public readonly NetInt DashCount = new();
        public readonly NetFloat VelocityX = new();
        public readonly NetFloat VelocityY = new();

        private bool completionHandled;

        public EyeOfCthulhu()
        {
        }

        public EyeOfCthulhu(Vector2 position)
            : base("Bat", position)
        {
            Name = "EyeOfCthulhu";
            displayName = I18n.Monster_EyeOfCthulhu_Name();
            Health = MaxHealth = 2800;
            DamageToFarmer = 15;
            resilience.Value = 0;
            speed = 0;
            Slipperiness = 0;
            IsWalkingTowardPlayer = false;
            HideShadow = true;
            isGlider.Value = true;
            forceOneTileWide.Value = false;
            objectsToDrop.Clear();
            reloadSprite();
        }

        protected override void initNetFields()
        {
            base.initNetFields();
            NetFields.AddField(Phase, nameof(Phase));
            NetFields.AddField(AttackState, nameof(AttackState));
            NetFields.AddField(AttackTimer, nameof(AttackTimer));
            NetFields.AddField(DashCount, nameof(DashCount));
            NetFields.AddField(VelocityX, nameof(VelocityX));
            NetFields.AddField(VelocityY, nameof(VelocityY));
        }

        public override void reloadSprite(bool onlyAppearance = false)
        {
            Sprite = new AnimatedSprite(
                Mod.instance.Helper.ModContent.GetInternalAssetName("assets/bosses/eye-of-cthulhu.png").BaseName,
                0,
                SpriteWidth,
                SpriteHeight
            );
            Sprite.SourceRect = new Rectangle(0, 0, SpriteWidth, SpriteHeight);
            HideShadow = true;
        }

        public override List<Item> getExtraDropItems()
        {
            return new List<Item>();
        }

        public override int takeDamage(int damage, int xTrajectory, int yTrajectory, bool isBomb, double addedPrecision, Farmer who)
        {
            int dealt = base.takeDamage(damage, 0, 0, isBomb, addedPrecision, who);
            if (Health <= 0)
                CompleteEncounter();
            return dealt;
        }

        protected override void localDeathAnimation()
        {
            CompleteEncounter();
            currentLocation?.localSound("monsterdead");
        }

        protected override void sharedDeathAnimation()
        {
        }

        private void CompleteEncounter()
        {
            if (completionHandled || !Game1.IsMasterGame)
                return;

            completionHandled = true;
            if (currentLocation is AsteroidsDungeon dungeon)
                dungeon.CompleteBossEncounter();
        }

        public override void behaviorAtGameTick(GameTime time)
        {
            if (!Game1.IsMasterGame || currentLocation == null || Health <= 0)
                return;

            Farmer target = Player;
            if (target == null)
                return;

            if (Phase.Value == 1 && Health <= PhaseTwoHealthThreshold)
            {
                Phase.Value = 2;
                AttackState.Value = 0;
                AttackTimer.Value = 0;
                DashCount.Value = 0;
                currentLocation.playSound("serpentDie");
            }

            if (Phase.Value == 1)
                UpdatePhaseOne(target);
            else
                UpdatePhaseTwo(target);

            position.Set(new Vector2(position.X + VelocityX.Value, position.Y + VelocityY.Value));
            rotation = (float)Math.Atan2(VelocityY.Value, VelocityX.Value) + MathHelper.PiOver2;
        }

        private void UpdatePhaseOne(Farmer target)
        {
            if (AttackState.Value == 0)
            {
                Vector2 desiredPosition = target.Position + new Vector2(0f, -256f);
                AccelerateToward(desiredPosition, 0.16f, HoverSpeed);
                AttackTimer.Value++;

                if (AttackTimer.Value % ServantInterval == 0)
                    SpawnServant(target);

                if (AttackTimer.Value >= HoverDuration)
                {
                    AttackTimer.Value = 0;
                    BeginDash(target);
                    AttackState.Value = 1;
                }
                return;
            }

            AttackTimer.Value++;
            if (AttackTimer.Value >= DashDuration)
            {
                AttackState.Value = 0;
                AttackTimer.Value = 0;
                VelocityX.Value *= 0.75f;
                VelocityY.Value *= 0.75f;
            }
        }

        private void UpdatePhaseTwo(Farmer target)
        {
            switch (AttackState.Value)
            {
                case 0:
                    AccelerateToward(target.Position + new Vector2(0f, -192f), 0.25f, HoverSpeed + 2f);
                    AttackTimer.Value++;
                    if (AttackTimer.Value >= RecoveryDuration)
                    {
                        AttackTimer.Value = 0;
                        BeginDash(target);
                        AttackState.Value = 1;
                    }
                    break;

                case 1:
                    AttackTimer.Value++;
                    if (AttackTimer.Value >= DashDuration)
                    {
                        AttackTimer.Value = 0;
                        DashCount.Value++;
                        VelocityX.Value *= 0.75f;
                        VelocityY.Value *= 0.75f;
                        AttackState.Value = DashCount.Value >= 3 ? 2 : 0;
                    }
                    break;

                default:
                    VelocityX.Value *= 0.9f;
                    VelocityY.Value *= 0.9f;
                    AttackTimer.Value++;
                    if (AttackTimer.Value >= RecoveryDuration)
                    {
                        AttackTimer.Value = 0;
                        DashCount.Value = 0;
                        AttackState.Value = 0;
                        SpawnServant(target);
                    }
                    break;
            }
        }

        private void BeginDash(Farmer target)
        {
            Point targetPixel = target.StandingPixel;
            Point bossPixel = StandingPixel;
            Vector2 direction = new Vector2(targetPixel.X - bossPixel.X, targetPixel.Y - bossPixel.Y);
            if (direction.LengthSquared() < 0.001f)
                direction = Vector2.UnitY;
            direction.Normalize();
            VelocityX.Value = direction.X * DashSpeed;
            VelocityY.Value = direction.Y * DashSpeed;
            currentLocation.playSound("batScreech");
        }

        private void SpawnServant(Farmer target)
        {
            int servantCount = currentLocation.characters.OfType<ServantOfCthulhu>().Count(servant => servant.Health > 0);
            if (servantCount >= MaxServants)
                return;

            Vector2 direction = target.Position - Position;
            if (direction.LengthSquared() < 0.001f)
                direction = Vector2.UnitY;
            direction.Normalize();

            ServantOfCthulhu servant = new(Position + direction * 64f)
            {
                InitialVelocity = direction * 8f
            };
            currentLocation.characters.Add(servant);
            currentLocation.playSound("batFlap");
        }

        private void AccelerateToward(Vector2 target, float acceleration, float maximumSpeed)
        {
            Vector2 desired = target - Position;
            if (desired.LengthSquared() > 0.001f)
            {
                desired.Normalize();
                desired *= maximumSpeed;
            }

            VelocityX.Value = MathHelper.Lerp(VelocityX.Value, desired.X, acceleration);
            VelocityY.Value = MathHelper.Lerp(VelocityY.Value, desired.Y, acceleration);
        }

        protected override void updateAnimation(GameTime time)
        {
            int firstFrame = Phase.Value == 1 ? 0 : 3;
            Sprite.Animate(time, firstFrame, 3, 200f);
        }

        public override Rectangle GetBoundingBox()
        {
            return new Rectangle((int)Position.X - 32, (int)Position.Y - 32, 176, 176);
        }

        public override void drawAboveAllLayers(SpriteBatch b)
        {
            if (!Utility.isOnScreen(Position, 256))
                return;

            Vector2 drawPosition = Game1.GlobalToLocal(Game1.viewport, Position) + new Vector2(64f, 88f);
            float layerDepth = Math.Max(0f, drawOnTop ? 0.991f : (StandingPixel.Y + 8f) / 10000f);
            b.Draw(
                Sprite.Texture,
                drawPosition,
                Sprite.SourceRect,
                Color.White,
                rotation,
                new Vector2(SpriteWidth / 2f, SpriteHeight / 2f),
                4f,
                SpriteEffects.None,
                layerDepth
            );
        }
    }
}
