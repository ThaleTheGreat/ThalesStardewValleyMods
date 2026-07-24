using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ThaleTheGreat.MoonMisadventures.Game.Items;
using ThaleTheGreat.MoonMisadventures.VirtualProperties;
using StardewValley;
using StardewValley.Enchantments;
using StardewValley.Menus;
using StardewValley.Monsters;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace ThaleTheGreat.MoonMisadventures.Patches
{
    [HarmonyPatch( typeof( InventoryPage ), MethodType.Constructor, new Type[] { typeof( int ), typeof( int ), typeof( int ), typeof( int ) } )]
    public static class InventoryPageNecklaceConstructorPatch
    {
        public const int NecklaceSlotId = 494990101;

        private static readonly ConditionalWeakTable<InventoryPage, ClickableComponent> NecklaceSlots = new();

        public static void Postfix( InventoryPage __instance )
        {
            if ( NecklaceSlots.TryGetValue( __instance, out _ ) )
                return;

            Rectangle bounds = GetNecklaceSlotBounds( __instance );
            var necklaceSlot = new ClickableComponent( bounds, "Necklace" )
            {
                myID = NecklaceSlotId,
                fullyImmutable = true,
            };

            NecklaceSlots.Add( __instance, necklaceSlot );
            __instance.allClickableComponents.Add( necklaceSlot );
            LinkControllerNeighbors( __instance, necklaceSlot );
        }

        public static ClickableComponent? GetNecklaceSlot( InventoryPage page )
        {
            if ( !NecklaceSlots.TryGetValue( page, out ClickableComponent necklaceSlot ) )
                return null;

            if ( !page.allClickableComponents.Contains( necklaceSlot ) )
                page.allClickableComponents.Add( necklaceSlot );

            Rectangle bounds = GetNecklaceSlotBounds( page );
            if ( necklaceSlot.bounds != bounds )
            {
                necklaceSlot.bounds = bounds;
                LinkControllerNeighbors( page, necklaceSlot );
            }

            return necklaceSlot;
        }

        private static Rectangle GetNecklaceSlotBounds( InventoryPage page )
        {
            const int uiScale = 4;
            const int equipmentOriginX = 48;
            const int necklaceGridX = 68;
            const int necklaceGridY = 0;

            var bounds = new Rectangle(
                page.xPositionOnScreen + equipmentOriginX + necklaceGridX * uiScale,
                page.yPositionOnScreen + IClickableMenu.borderWidth + IClickableMenu.spaceToClearTopBorder + 256 - 12 + necklaceGridY * uiScale,
                64,
                64 );

            while ( page.equipmentIcons.Any( component => component.bounds.Intersects( bounds ) ) )
                bounds.Y += bounds.Height;

            return bounds;
        }

        private static void LinkControllerNeighbors( InventoryPage page, ClickableComponent necklaceSlot )
        {
            ClickableComponent? left = page.equipmentIcons
                .Where( component => component.bounds.Center.Y == necklaceSlot.bounds.Center.Y
                    && component.bounds.Center.X < necklaceSlot.bounds.Center.X )
                .OrderByDescending( component => component.bounds.Center.X )
                .FirstOrDefault();
            ClickableComponent? right = page.equipmentIcons
                .Where( component => component.bounds.Center.Y == necklaceSlot.bounds.Center.Y
                    && component.bounds.Center.X > necklaceSlot.bounds.Center.X )
                .OrderBy( component => component.bounds.Center.X )
                .FirstOrDefault();
            ClickableComponent? up = page.equipmentIcons
                .Where( component => component.bounds.Center.X == necklaceSlot.bounds.Center.X
                    && component.bounds.Center.Y < necklaceSlot.bounds.Center.Y )
                .OrderByDescending( component => component.bounds.Center.Y )
                .FirstOrDefault();
            ClickableComponent? down = page.equipmentIcons
                .Where( component => component.bounds.Center.X == necklaceSlot.bounds.Center.X
                    && component.bounds.Center.Y > necklaceSlot.bounds.Center.Y )
                .OrderBy( component => component.bounds.Center.Y )
                .FirstOrDefault();

            necklaceSlot.leftNeighborID = left?.myID ?? -1;
            necklaceSlot.rightNeighborID = right?.myID ?? -1;
            necklaceSlot.upNeighborID = up?.myID ?? -1;
            necklaceSlot.downNeighborID = down?.myID ?? -1;

            if ( left != null )
                left.rightNeighborID = NecklaceSlotId;
            if ( right != null )
                right.leftNeighborID = NecklaceSlotId;
            if ( up != null )
                up.downNeighborID = NecklaceSlotId;
            if ( down != null )
                down.upNeighborID = NecklaceSlotId;
        }
    }

    [HarmonyPatch( typeof( InventoryPage ), nameof( InventoryPage.performHoverAction ) )]
    public static class InventoryPageNecklaceHoverPatch
    {
        public static void Postfix( InventoryPage __instance, int x, int y, ref Item ___hoveredItem, ref string ___hoverText, ref string ___hoverTitle )
        {
            ClickableComponent? necklaceSlot = InventoryPageNecklaceConstructorPatch.GetNecklaceSlot( __instance );

            if (necklaceSlot is null)
                return;

            if ( necklaceSlot.containsPoint( x, y ) && Game1.player.get_necklaceItem().Value != null )
            {
                var necklaceItem = Game1.player.get_necklaceItem().Value;
                ___hoveredItem = necklaceItem;
                ___hoverText = necklaceItem.getDescription();
                ___hoverTitle = necklaceItem.DisplayName;
            }
        }
    }
    [HarmonyPatch( typeof( InventoryPage ), nameof( InventoryPage.receiveLeftClick ) )]
    public static class InventoryPageNecklaceLeftClickPatch
    {
        public static bool Prefix( InventoryPage __instance, int x, int y )
        {
            ClickableComponent? necklaceSlot = InventoryPageNecklaceConstructorPatch.GetNecklaceSlot( __instance );

            if (necklaceSlot is null)
                return true;
            if ( necklaceSlot.containsPoint( x, y ) )
            {
                var necklaceItem = Game1.player.get_necklaceItem();
                if ( Game1.player.CursorSlotItem == null || Game1.player.CursorSlotItem is Necklace )
                {
                    Item tmp = Mod.instance.Helper.Reflection.GetMethod( __instance, "takeHeldItem" ).Invoke<Item>();
                    Item held = necklaceItem.Value;
                    if ( held != null )
                        ( held as Necklace ).onUnequip( Game1.player );
                    held = Utility.PerformSpecialItemGrabReplacement( held );
                    Mod.instance.Helper.Reflection.GetMethod( __instance, "setHeldItem" ).Invoke( held );
                    necklaceItem.Value = tmp;

                    LevelUpMenu.RevalidateHealth( Game1.player );

                    if ( necklaceItem.Value != null )
                    {
                        ( necklaceItem.Value as Necklace ).onEquip( Game1.player );
                        Game1.playSound( "crit" );
                    }
                    else if ( Game1.player.CursorSlotItem != null )
                        Game1.playSound( "dwop" );
                }
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch( typeof( InventoryPage ), nameof( InventoryPage.draw ) )]
    public static class InventoryPageNecklaceDrawPatch
    {
        public static void Postfix( InventoryPage __instance, SpriteBatch b )
        {
            ClickableComponent? necklaceSlot = InventoryPageNecklaceConstructorPatch.GetNecklaceSlot( __instance );

            if ( necklaceSlot is null )
                return;

            b.Draw(
                Game1.menuTexture,
                necklaceSlot.bounds,
                Game1.getSourceRectForStandardTileSheet( Game1.menuTexture, 10 ),
                Color.White );

            Item? necklace = Game1.player.get_necklaceItem().Value;
            if ( necklace != null )
            {
                necklace.drawInMenu(
                    b,
                    new Vector2( necklaceSlot.bounds.X, necklaceSlot.bounds.Y ),
                    necklaceSlot.scale,
                    1f,
                    0.866f,
                    StackDrawType.Hide );
            }
            else
            {
                b.Draw( Assets.NecklaceBg, necklaceSlot.bounds, null, Color.White );
            }
        }
    }

    [HarmonyPatch( typeof( Hoe ), nameof( Hoe.DoFunction ) )]
    public static class HoeWaterTilledWithNecklacePatch
    {
        private static void OnFeatureAdded( Vector2 key, TerrainFeature value )
        {
            if ( value is HoeDirt hd )
                hd.state.Value = HoeDirt.watered;
        }

        public static void Prefix(GameLocation location, Farmer who )
        {
            if ( who.HasNecklace( Necklace.Type.Water ) )
                location.terrainFeatures.OnValueAdded += OnFeatureAdded;
        }

        public static void Postfix(GameLocation location, Farmer who )
        {
            if ( who.HasNecklace( Necklace.Type.Water ) )
                location.terrainFeatures.OnValueAdded -= OnFeatureAdded;
        }
    }

    [HarmonyPatch]
    public static class MonsterTakeDamagePatch
    {
        internal static bool applyingShock = false;

        public static IEnumerable<MethodBase> TargetMethods()
        {
            var subclasses = from asm in AppDomain.CurrentDomain.GetAssemblies().Where( a => !a.FullName.Contains( "Steamworks.NET") && !a.IsDynamic)
                             from type in asm.GetExportedTypes()
                             where type.IsSubclassOf( typeof( Monster ) )
                             select type;

            var ps = new Type[] { typeof( int ), typeof( int ), typeof( int ), typeof( bool ), typeof( double ), typeof( Farmer ) };

            yield return AccessTools.Method( typeof( Monster ), nameof( Monster.takeDamage ), ps );
            foreach ( var subclass in subclasses )
            {
                var meth = subclass.GetMethod( nameof( Monster.takeDamage ), ps );
                if ( meth != null && meth.DeclaringType == subclass )
                    yield return meth;
            }
        }

        public static void Postfix( Monster __instance, Farmer who )
        {
            if ( __instance.Health <= 0 )
            {
                if ( who.HasNecklace( Necklace.Type.Looting ) )
                    who.Money += __instance.MaxHealth / 8;
            }
            else
            {
                if ( who.HasNecklace( Necklace.Type.Shocking ) && !applyingShock )
                {
                    __instance.get_shocked().Value = 1000 + Game1.random.Next( 3 ) * 500;
                    __instance.set_shocker( who );
                }
            }
        }
    }

    [HarmonyPatch( typeof( Monster ), nameof( Monster.update ) )]
    public static class MonsterShockDamagePatch
    {
        public static void Postfix( Monster __instance, GameTime time, GameLocation location )
        {
            var shock = __instance.get_shocked();
            if ( shock.Value >= 0 )
            {
                int shockBefore = shock.Value;
                shock.Value -= time.ElapsedGameTime.Milliseconds;
                if ( shock.Value % 500 > shockBefore % 500 )
                {
                    if ( __instance.get_shocker() != null ) // Only happens for the player who shocked them since it isn't a net var, meaning this won't trigger for everyone
                    {
                        MonsterTakeDamagePatch.applyingShock = true;
                        int amt = __instance.takeDamage( ( int ) ( __instance.MaxHealth * 0.15 ), 0, 0, false, 0, __instance.get_shocker() );
                        if ( amt != -1 )
                        {
                            location.removeDamageDebris( __instance );
                            var monsterBox = __instance.GetBoundingBox();
                            location.debris.Add( new Debris( amt, new Vector2( monsterBox.Center.X + 16, monsterBox.Center.Y ), Color.Purple, 1, __instance ) );
                            if ( __instance.get_shocker() != null )
                            {
                                foreach ( BaseEnchantment enchantment2 in __instance.get_shocker().enchantments )
                                {
                                    enchantment2.OnDealtDamage( __instance, location, __instance.get_shocker(), false, amt );
                                }
                            }
                        }
                        MonsterTakeDamagePatch.applyingShock = false;
                    }
                }
            }
        }
    }

    [HarmonyPatch]
    public static class MonsterDrawPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            var subclasses = from asm in AppDomain.CurrentDomain.GetAssemblies().Where( a => !a.FullName.Contains( "Steamworks.NET" ) && !a.IsDynamic )
                             from type in asm.GetExportedTypes()
                             where type.IsSubclassOf( typeof( Monster ) )
                             select type;

            var ps = new Type[] { typeof( SpriteBatch ) };

            yield return AccessTools.Method( typeof( Monster ), nameof( Monster.draw ), ps );
            foreach ( var subclass in subclasses )
            {
                var meth = subclass.GetMethod( nameof( Monster.draw ), ps );
                if ( meth != null && meth.DeclaringType == subclass )
                    yield return meth;
            }
        }

        public static void Postfix( Monster __instance, SpriteBatch b )
        {
            int shocked = __instance.get_shocked().Value;
            if ( shocked >= 0 )
            {
                int shocking = 500 - shocked % 500;
                var src = new Rectangle( 647, 1103, 16, 16 );
                b.Draw( Game1.mouseCursors, __instance.getLocalPosition(Game1.viewport) - ( __instance.Position - __instance.GetBoundingBox().Center.ToVector2() ), src, Color.White * (shocking / 250f), ( float )( Game1.ticks * 15 * Math.PI / 180 ), new Vector2( 8, 8 ), Game1.pixelZoom, SpriteEffects.None, (__instance.StandingPixel.Y + 8) / 10000f );
            }
        }
    }
}
