using StardewModdingAPI;

namespace ThaleTheGreat.TheftOfTheWinterStar
{
    public static class I18n
    {
        private static ITranslationHelper Translation = null!;

        public static void Init(ITranslationHelper translation)
        {
            Translation = translation;
        }

        public static string Event_LewisSpeech()
        {
            return Translation.Get("event.lewis-speech").ToString();
        }

        public static string FinalBoss_Speech()
        {
            return Translation.Get("final-boss.speech").ToString();
        }

        public static string FinalBoss_VictoryMessage()
        {
            return Translation.Get("final-boss.victory-message").ToString();
        }

        public static string MapMessages_ItemPuzzle()
        {
            return Translation.Get("map-messages.item-puzzle").ToString();
        }

        public static string MapMessages_LockedBoss()
        {
            return Translation.Get("map-messages.locked-boss").ToString();
        }

        public static string MapMessages_LockedDoor()
        {
            return Translation.Get("map-messages.locked-door").ToString();
        }

        public static string MapMessages_LockedEntrance()
        {
            return Translation.Get("map-messages.locked-entrance").ToString();
        }

        public static string MapMessages_Target()
        {
            return Translation.Get("map-messages.target").ToString();
        }

        public static string MapMessages_TrailCandyCane()
        {
            return Translation.Get("map-messages.trail-candy-cane").ToString();
        }

        public static string MapMessages_TrailLights()
        {
            return Translation.Get("map-messages.trail-lights").ToString();
        }

        public static string MapMessages_TrailOrnaments()
        {
            return Translation.Get("map-messages.trail-ornaments").ToString();
        }

        public static string MapMessages_TrailTree()
        {
            return Translation.Get("map-messages.trail-tree").ToString();
        }

        public static string MapMessages_Unlocked()
        {
            return Translation.Get("map-messages.unlocked").ToString();
        }

        public static string Recipe_FrostyStardrop_Name()
        {
            return Translation.Get("recipe.frosty-stardrop.name").ToString();
        }
    }
}
