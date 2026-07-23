using StardewModdingAPI;

namespace ThaleTheGreat.MoonMisadventures
{
    public static class I18n
    {
        private static ITranslationHelper Translation = null!;

        public static void Init(ITranslationHelper translation)
        {
            Translation = translation;
        }

        public static string Building_Obelisk_Description()
        {
            return Translation.Get("building.obelisk.description").ToString();
        }

        public static string Building_Obelisk_Name()
        {
            return Translation.Get("building.obelisk.name").ToString();
        }

        public static string Config_FlashingUfo_Description()
        {
            return Translation.Get("config.flashing-ufo.description").ToString();
        }

        public static string Config_FlashingUfo_Name()
        {
            return Translation.Get("config.flashing-ufo.name").ToString();
        }

        public static string Config_CobaltMythiciteTransmutation_Description()
        {
            return Translation.Get("config.cobalt-mythicite-transmutation.description").ToString();
        }

        public static string Config_CobaltMythiciteTransmutation_Name()
        {
            return Translation.Get("config.cobalt-mythicite-transmutation.name").ToString();
        }

        public static string FarmAnimal_LunarChicken()
        {
            return Translation.Get("farm-animal.lunar-chicken").ToString();
        }

        public static string FarmAnimal_LunarCow()
        {
            return Translation.Get("farm-animal.lunar-cow").ToString();
        }

        public static string ForceField()
        {
            return Translation.Get("force-field").ToString();
        }

        public static string Item_LunarKey_Name()
        {
            return Translation.Get("item.lunar-key.name").ToString();
        }

        public static string Item_Necklace_Cooling_Description()
        {
            return Translation.Get("item.necklace.Cooling.description").ToString();
        }

        public static string Item_Necklace_Cooling_Name()
        {
            return Translation.Get("item.necklace.Cooling.name").ToString();
        }

        public static string Item_Necklace_Health_Description()
        {
            return Translation.Get("item.necklace.Health.description").ToString();
        }

        public static string Item_Necklace_Health_Name()
        {
            return Translation.Get("item.necklace.Health.name").ToString();
        }

        public static string Item_Necklace_Looting_Description()
        {
            return Translation.Get("item.necklace.Looting.description").ToString();
        }

        public static string Item_Necklace_Looting_Name()
        {
            return Translation.Get("item.necklace.Looting.name").ToString();
        }

        public static string Item_Necklace_Lunar_Description()
        {
            return Translation.Get("item.necklace.Lunar.description").ToString();
        }

        public static string Item_Necklace_Lunar_Name()
        {
            return Translation.Get("item.necklace.Lunar.name").ToString();
        }

        public static string Item_Necklace_Sea_Description()
        {
            return Translation.Get("item.necklace.Sea.description").ToString();
        }

        public static string Item_Necklace_Sea_Name()
        {
            return Translation.Get("item.necklace.Sea.name").ToString();
        }

        public static string Item_Necklace_Shocking_Description()
        {
            return Translation.Get("item.necklace.Shocking.description").ToString();
        }

        public static string Item_Necklace_Shocking_Name()
        {
            return Translation.Get("item.necklace.Shocking.name").ToString();
        }

        public static string Item_Necklace_Speed_Description()
        {
            return Translation.Get("item.necklace.Speed.description").ToString();
        }

        public static string Item_Necklace_Speed_Name()
        {
            return Translation.Get("item.necklace.Speed.name").ToString();
        }

        public static string Item_Necklace_Water_Description()
        {
            return Translation.Get("item.necklace.Water.description").ToString();
        }

        public static string Item_Necklace_Water_Name()
        {
            return Translation.Get("item.necklace.Water.name").ToString();
        }

        public static string Location_Asteroids()
        {
            return Translation.Get("location.asteroids").ToString();
        }

        public static string Location_AsteroidsEntrance()
        {
            return Translation.Get("location.asteroids-entrance").ToString();
        }

        public static string Location_LandingArea()
        {
            return Translation.Get("location.landing-area").ToString();
        }

        public static string Location_LunarFarm()
        {
            return Translation.Get("location.lunar-farm").ToString();
        }

        public static string Location_MoonTemple()
        {
            return Translation.Get("location.moon-temple").ToString();
        }

        public static string Location_MountainTop()
        {
            return Translation.Get("location.mountain-top").ToString();
        }

        public static string Location_PlanetOverlook()
        {
            return Translation.Get("location.planet-overlook").ToString();
        }

        public static string Location_UfoInterior()
        {
            return Translation.Get("location.ufo-interior").ToString();
        }

        public static string Message_DirtTutorial()
        {
            return Translation.Get("message.dirt-tutorial").ToString();
        }

        public static string Message_Farm_CrystalLock()
        {
            return Translation.Get("message.farm.crystal-lock").ToString();
        }

        public static string Message_Gargoyle()
        {
            return Translation.Get("message.gargoyle").ToString();
        }

        public static string Message_Infuser_1()
        {
            return Translation.Get("message.infuser.1").ToString();
        }

        public static string Message_Infuser_2()
        {
            return Translation.Get("message.infuser.2").ToString();
        }

        public static string Message_Infuser_3()
        {
            return Translation.Get("message.infuser.3").ToString();
        }

        public static string Message_LunarTeleporterOffline()
        {
            return Translation.Get("message.lunar-teleporter-offline").ToString();
        }

        public static string Message_LunarTemple_Locked()
        {
            return Translation.Get("message.lunar-temple.locked").ToString();
        }

        public static string Message_NecklaceExchange()
        {
            return Translation.Get("message.necklace-exchange").ToString();
        }

        public static string Message_NecklaceExchange_Lacking()
        {
            return Translation.Get("message.necklace-exchange.lacking").ToString();
        }

        public static string Message_Planet_Jump()
        {
            return Translation.Get("message.planet.jump").ToString();
        }

        public static string Message_Ufo_Repair()
        {
            return Translation.Get("message.ufo.repair").ToString();
        }

        public static string Message_Ufo_Repair_Lacking()
        {
            return Translation.Get("message.ufo.repair.lacking").ToString();
        }

        public static string Message_Ufo_Travel()
        {
            return Translation.Get("message.ufo.travel").ToString();
        }

        public static string Monster_BoomEye_Name()
        {
            return Translation.Get("monster.boom-eye.name").ToString();
        }

        public static string MoonArtifactSlot()
        {
            return Translation.Get("moon-artifact-slot").ToString();
        }

        public static string Necklace()
        {
            return Translation.Get("necklace").ToString();
        }

        public static string Tool_AnimalGauntlets_Description()
        {
            return Translation.Get("tool.animal-gauntlets.description").ToString();
        }

        public static string Tool_AnimalGauntlets_Holding()
        {
            return Translation.Get("tool.animal-gauntlets.holding").ToString();
        }

        public static string Tool_AnimalGauntlets_Name()
        {
            return Translation.Get("tool.animal-gauntlets.name").ToString();
        }

        public static string Tool_LaserGun_Description()
        {
            return Translation.Get("tool.laser-gun.description").ToString();
        }

        public static string Tool_LaserGun_Name()
        {
            return Translation.Get("tool.laser-gun.name").ToString();
        }

        public static string Tooltip_Persists()
        {
            return Translation.Get("tooltip.persists").ToString();
        }

        public static string Monster_EyeOfCthulhu_Name()
        {
            return Translation.Get("monster.eye-of-cthulhu.name").ToString();
        }

        public static string Monster_ServantOfCthulhu_Name()
        {
            return Translation.Get("monster.servant-of-cthulhu.name").ToString();
        }

        public static string Message_BossDefeated()
        {
            return Translation.Get("message.boss-defeated").ToString();
        }

        public static string Message_BossExitLocked()
        {
            return Translation.Get("message.boss-exit-locked").ToString();
        }
    }
}
