using Microsoft.Xna.Framework.Graphics;

using ThaleTheGreat.MoonMisadventures.Game.Locations;

using StardewModdingAPI;
using StardewModdingAPI.Events;

using StardewValley;

namespace ThaleTheGreat.MoonMisadventures
{
    internal static class Assets
    {
        public static Texture2D LunarKey;

        public static Texture2D LaunchBackground;
        public static Texture2D LaunchUfo;
        public static Texture2D LaunchMoon;

        public static Texture2D Ufo;

        public static Texture2D AsteroidsSmall;
        public static Texture2D AsteroidsBig;

        public static Texture2D HoeDirt;

        public static Texture2D NecklaceBg;

        public static Texture2D Laser;

        public static Texture2D EyeBossBar;
        public static Texture2D EyeBossIconPhaseOne;
        public static Texture2D EyeBossIconPhaseTwo;

        internal static void Load( IModContentHelper content )
        {
            Assets.LunarKey = content.Load<Texture2D>( "assets/key.png" );

            Assets.LaunchBackground = content.Load<Texture2D>( "assets/launch.png" );
            Assets.LaunchUfo = content.Load<Texture2D>( "assets/ufo-small.png" );
            Assets.LaunchMoon = content.Load<Texture2D>( "assets/moon.png" );

            Assets.Ufo = content.Load<Texture2D>( "assets/ufo-big.png" );

            Assets.AsteroidsSmall = content.Load<Texture2D>( "assets/asteroids-small.png" );
            Assets.AsteroidsBig = content.Load<Texture2D>( "assets/asteroids-large.png" );
            
            Assets.HoeDirt = content.Load<Texture2D>( "assets/hoedirt.png" );

            Assets.NecklaceBg = content.Load<Texture2D>( "assets/necklace-bg.png" );

            Laser = content.Load<Texture2D>("assets/laser.png");

            EyeBossBar = content.Load<Texture2D>("assets/bosses/boss-bar.png");
            EyeBossIconPhaseOne = content.Load<Texture2D>("assets/bosses/boss-icon-phase1.png");
            EyeBossIconPhaseTwo = content.Load<Texture2D>("assets/bosses/boss-icon-phase2.png");
        }
    }
}
