using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Network;
using StardewValley.Tools;

namespace ThaleTheGreat.MoonMisadventures.Game.Locations
{
    [XmlType( "Mods_ThaleTheGreat_MoonMisadventures_LunarFarm" )]
    public class LunarFarm : LunarLocation
    {
        public readonly NetBool grownCrystal = new();

        public LunarFarm()
        {
        }

        public LunarFarm( IModContentHelper content )
        : base( content, "MoonFarm", "MoonFarm" )
        {
        }

        protected override void initNetFields()
        {
            base.initNetFields();
            NetFields.AddField( grownCrystal, "grownCrystal" );

            grownCrystal.InterpolationEnabled = false;
            grownCrystal.fieldChangeVisibleEvent += delegate { OpenFarmHouse(); };
        }

        protected override void resetLocalState()
        {
            base.resetLocalState();

            if ( grownCrystal.Value )
                OpenFarmHouse();
        }

        private void OpenFarmHouse()
        {
            if ( Map == null )
                return;

            int ts = Map.TileSheets.IndexOf( Map.TileSheets.First( t => t.Id == "tf_darkdimension_sheet" ) );
            global::ThaleTheGreat.MoonMisadventures.MapTileHelper.SetMapTileIndex( this,  13, 9, 120, "Front", ts );
            global::ThaleTheGreat.MoonMisadventures.MapTileHelper.SetMapTile( this,  13, 10, 149, "Buildings", null, ts );

            global::ThaleTheGreat.MoonMisadventures.MapTileHelper.SetMapTileIndex( this,  7, 8, 52, "Buildings", ts );
            global::ThaleTheGreat.MoonMisadventures.MapTileHelper.SetMapTileIndex( this,  7, 9, 81, "Buildings", ts );
            global::ThaleTheGreat.MoonMisadventures.MapTileHelper.SetMapTile( this,  7, 10, 110, "Buildings", "Warp 9 10 Custom_MM_MoonFarmHouse", ts );
        }

        public override void TransferDataFromSavedLocation( GameLocation l )
        {
            var other = l as LunarFarm;
            Animals.MoveFrom( other.Animals );
            foreach ( var animal in Animals.Values )
            {
                animal.reload( this );
            }

            grownCrystal.Value = other.grownCrystal.Value;

            base.TransferDataFromSavedLocation( l );
        }

        public override bool performAction( string action, Farmer who, xTile.Dimensions.Location tileLocation )
        {
            if ( action == "FarmHouseCrystalLock" )
            {
                if ( who.ActiveObject?.ItemId == ItemIds.MythiciteOre )
                {
                    Game1.playSound( "questcomplete" );
                    who.reduceActiveItemByOne();
                    grownCrystal.Value = true;
                }
                else
                {
                    Game1.drawObjectDialogue(I18n.Message_Farm_CrystalLock());
                }
            }
            return base.performAction( action, who, tileLocation );
        }
    }
}
