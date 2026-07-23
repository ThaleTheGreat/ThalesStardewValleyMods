using Microsoft.Xna.Framework;
using xTile;

namespace ThaleTheGreat.MoonMisadventures.Game.Locations.DungeonLevelGenerators
{
    public class BossIslandDungeonLevelGenerator : BaseDungeonLevelGenerator
    {
        public override void Generate( AsteroidsDungeon location, ref Vector2 warpFromPrev, ref Vector2 warpFromNext )
        {
            Map place = Mod.instance.Helper.ModContent.Load< Map >( "assets/maps/MoonBossIsland.tmx" );
            int offsetX = ( location.Map.Layers[ 0 ].LayerWidth - place.Layers[ 0 ].LayerWidth ) / 2;
            int offsetY = ( location.Map.Layers[ 0 ].LayerHeight - place.Layers[ 0 ].LayerHeight ) / 2;

            location.ApplyMapOverride( place, "island_boss", new Rectangle( 0, 0, place.Layers[ 0 ].LayerWidth, place.Layers[ 0 ].LayerHeight ), new Rectangle( offsetX, offsetY, place.Layers[ 0 ].LayerWidth, place.Layers[ 0 ].LayerHeight ) );

            warpFromPrev = warpFromNext = new Vector2( 24 + offsetX, 43 + offsetY );

            PlaceNextWarp(location, offsetX + 23, offsetY + 17);

            location.SetBossArenaCenter(new Vector2(offsetX + 24, offsetY + 24));

            location.PlaceSpaceTiles();
        }
    }
}
