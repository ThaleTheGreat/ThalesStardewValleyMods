using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;

namespace ThaleTheGreat.ItRubsTheMayoIn;

internal sealed class ModEntry : Mod
{
    private const string SaveDataKey = "ItRubsTheMayoInState";
    private const int RareMayonnaiseQuoteMinimumPreviousThrows = 10;
    private const int RareMayonnaiseQuoteChance = 50;

    private Config Config = new();
    private SaveState State = new();
    private readonly Random Random = new();

    public override void Entry(IModHelper helper)
    {
        Config = helper.ReadConfig<Config>();

        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        State = Helper.Data.ReadSaveData<SaveState>(SaveDataKey) ?? new SaveState();
        SanitizeState();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        State = new SaveState();
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || Game1.activeClickableMenu is not null || Game1.eventUp || !e.Button.IsActionButton())
            return;

        if (Game1.currentLocation is not Farm farm)
            return;

        Building? building = farm.getBuildingAt(e.Cursor.GrabTile);
        if (!IsWell(building))
            return;

        Item? heldItem = Game1.player.CurrentItem;
        if (heldItem is null)
            return;

        bool correctItem = State.ExpectingMayonnaise
            ? MatchesAnyItemId(heldItem, Config.MayonnaiseItemIds)
            : MatchesBookContextTag(heldItem, Config.BookContextTag);

        if (!correctItem)
            return;

        Helper.Input.Suppress(e.Button);
        Game1.player.reduceActiveItemByOne();

        bool wasExpectingMayonnaise = State.ExpectingMayonnaise;
        string quote;

        if (wasExpectingMayonnaise)
        {
            bool canUseRareQuote = State.MayonnaiseThrowsGiven >= RareMayonnaiseQuoteMinimumPreviousThrows;
            State.MayonnaiseThrowsGiven++;

            quote = canUseRareQuote && Random.Next(RareMayonnaiseQuoteChance) == 0
                ? T("quote.mayonnaise.rare")
                : TakeNextQuote(State.RemainingMayonnaiseQuotes, Config.MayonnaiseQuotes);
        }
        else
        {
            quote = TakeNextQuote(State.RemainingBookQuotes, Config.BookQuotes);
        }

        State.ExpectingMayonnaise = !State.ExpectingMayonnaise;
        Helper.Data.WriteSaveData(SaveDataKey, State);

        if (!string.IsNullOrWhiteSpace(quote))
            Game1.drawObjectDialogue(quote);
    }

    private void SanitizeState()
    {
        State.RemainingMayonnaiseQuotes.RemoveAll(static quote => string.IsNullOrWhiteSpace(quote));
        State.RemainingBookQuotes.RemoveAll(static quote => string.IsNullOrWhiteSpace(quote));
        Config.MayonnaiseItemIds = Config.MayonnaiseItemIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsWell(Building? building)
    {
        if (building is null)
            return false;

        return string.Equals(building.buildingType.Value, "Well", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesBookContextTag(Item item, string configuredTag)
    {
        if (string.IsNullOrWhiteSpace(configuredTag))
            return false;

        string tag = configuredTag.Trim();
        return item.HasContextTag(tag);
    }

    private static bool MatchesAnyItemId(Item item, IEnumerable<string> configuredIds)
    {
        foreach (string configuredId in configuredIds)
        {
            if (MatchesItemId(item, configuredId))
                return true;
        }

        return false;
    }

    private static bool MatchesItemId(Item item, string configuredId)
    {
        if (string.IsNullOrWhiteSpace(configuredId))
            return false;

        string normalized = configuredId.Trim();
        return string.Equals(item.QualifiedItemId, normalized, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.ItemId, normalized, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.QualifiedItemId, $"(O){normalized}", StringComparison.OrdinalIgnoreCase);
    }

    private string TakeNextQuote(List<string> remainingQuotes, List<string> sourceQuotes)
    {
        if (remainingQuotes.Count == 0)
            RefillQuoteQueue(remainingQuotes, sourceQuotes);

        if (remainingQuotes.Count == 0)
            return string.Empty;

        string quote = remainingQuotes[0];
        remainingQuotes.RemoveAt(0);
        return LocalizeQuote(quote);
    }

    private string LocalizeQuote(string quote)
    {
        return quote switch
        {
            "Say it, don't spray it brother. Dang." => T("quote.mayonnaise.1"),
            "I need a towel now." => T("quote.mayonnaise.2"),
            "There look, I'm putting the Mayonnaise on the skin. I'm rubbing it in." => T("quote.mayonnaise.3"),
            "Where's my supplies. Yeah, come on man, I thought we had a deal." => T("quote.mayonnaise.4"),
            "Yee! Auto Trader!" => T("quote.book.1"),
            "Ohh August, I don't got this one." => T("quote.book.2"),
            "There's some deals in here." => T("quote.book.3"),
            "Check this out. 71' Cuda. Plum Crazy Purple." => T("quote.book.4"),
            _ => quote
        };
    }

    private string T(string key)
    {
        return Helper.Translation.Get(key).ToString();
    }

    private void RefillQuoteQueue(List<string> target, List<string> source)
    {
        target.Clear();
        target.AddRange(source.Where(static quote => !string.IsNullOrWhiteSpace(quote)));

        for (int i = target.Count - 1; i > 0; i--)
        {
            int j = Random.Next(i + 1);
            (target[i], target[j]) = (target[j], target[i]);
        }
    }
}
