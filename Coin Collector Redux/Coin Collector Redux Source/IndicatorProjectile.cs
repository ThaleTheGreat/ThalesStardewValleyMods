using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Projectiles;
using StardewValley.TerrainFeatures;

namespace ThaleTheGreat.CoinCollectorRedux
{
    internal class IndicatorProjectile : BasicProjectile
    {
        public IndicatorProjectile(int damageToFarmer, int spriteIndex, int bouncesTillDestruct, int tailLength, float rotationVelocity, float xVelocity, float yVelocity, Vector2 startingPosition, string collisionSound, string firingSound, string explosionSound, bool damagesMonsters, bool spriteFromObjectSheet, GameLocation location, Character firer, BasicProjectile.onCollisionBehavior? collisionBehavior)
            : base(damageToFarmer, spriteIndex, bouncesTillDestruct, tailLength, rotationVelocity, xVelocity, yVelocity, startingPosition, collisionSound, firingSound, explosionSound, damagesMonsters, spriteFromObjectSheet, location, firer, collisionBehavior)
        {
        }

        public override void behaviorOnCollisionWithPlayer(GameLocation location, Farmer player)
        {
        }

        public override void behaviorOnCollisionWithTerrainFeature(TerrainFeature t, Vector2 tileLocation, GameLocation location)
        {
        }


        public override void behaviorOnCollisionWithOther(GameLocation location)
        {
        }

        public override void behaviorOnCollisionWithMonster(NPC n, GameLocation location)
        {
        }
    }
}
