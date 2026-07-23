using StardewModdingAPI;

namespace ThaleTheGreat.SurfingFestival
{
    public static class I18n
    {
        private static ITranslationHelper Translation = null!;

        public static void Init(ITranslationHelper translation)
        {
            Translation = translation;
        }

        public static string Dialog_PrizeMoney()
        {
            return Translation.Get("dialog.prize-money").ToString();
        }

        public static string Dialog_Shorts()
        {
            return Translation.Get("dialog.shorts").ToString();
        }

        public static string Dialog_Wood()
        {
            return Translation.Get("dialog.wood").ToString();
        }

        public static string Festival_Name()
        {
            return Translation.Get("festival.name").ToString();
        }

        public static string Item_Boost()
        {
            return Translation.Get("item.boost").ToString();
        }

        public static string Item_FirstPlaceProjectile()
        {
            return Translation.Get("item.first-place-projectile").ToString();
        }

        public static string Item_HomingProjectile()
        {
            return Translation.Get("item.homing-projectile").ToString();
        }

        public static string Item_Invincibility()
        {
            return Translation.Get("item.invincibility").ToString();
        }

        public static string Race_Instructions()
        {
            return Translation.Get("race.instructions").ToString();
        }

        public static string Race_LewisStart_0()
        {
            return Translation.Get("race.lewis-start.0").ToString();
        }

        public static string Race_LewisStart_1()
        {
            return Translation.Get("race.lewis-start.1").ToString();
        }

        public static string Race_LewisStart_2()
        {
            return Translation.Get("race.lewis-start.2").ToString();
        }

        public static string Race_Start_No()
        {
            return Translation.Get("race.start.no").ToString();
        }

        public static string Race_Start_Question()
        {
            return Translation.Get("race.start.question").ToString();
        }

        public static string Race_Start_Yes()
        {
            return Translation.Get("race.start.yes").ToString();
        }

        public static string Race_Winner_Emily()
        {
            return Translation.Get("race.winner.emily").ToString();
        }

        public static string Race_Winner_Harvey()
        {
            return Translation.Get("race.winner.harvey").ToString();
        }

        public static string Race_Winner_Maru()
        {
            return Translation.Get("race.winner.maru").ToString();
        }

        public static string Race_Winner_Player(object name)
        {
            return Translation.Get("race.winner.player", new { name }).ToString();
        }

        public static string Race_Winner_Shane()
        {
            return Translation.Get("race.winner.shane").ToString();
        }

        public static string Secret_Broke()
        {
            return Translation.Get("secret.broke").ToString();
        }

        public static string Secret_No()
        {
            return Translation.Get("secret.no").ToString();
        }

        public static string Secret_Purchased()
        {
            return Translation.Get("secret.purchased").ToString();
        }

        public static string Secret_Text()
        {
            return Translation.Get("secret.text").ToString();
        }

        public static string Secret_Yes()
        {
            return Translation.Get("secret.yes").ToString();
        }

        public static string Ui_Laps(object laps)
        {
            return Translation.Get("ui.laps", new { laps }).ToString();
        }

        public static string Ui_Ranking()
        {
            return Translation.Get("ui.ranking").ToString();
        }

        public static string Ui_Wood()
        {
            return Translation.Get("ui.wood").ToString();
        }
    }
}
