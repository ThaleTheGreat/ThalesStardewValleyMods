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
    private const int RareMayonnaiseQuoteChance = 100;
    private const string RareMayonnaiseQuote = "At least it ain't a road flare this time.";

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
            : MatchesItemId(heldItem, Config.SecretNoteItemId);

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
                ? RareMayonnaiseQuote
                : TakeNextQuote(State.RemainingMayonnaiseQuotes, Config.MayonnaiseQuotes);
        }
        else
        {
            quote = TakeNextQuote(State.RemainingSecretNoteQuotes, Config.SecretNoteQuotes);
        }

        State.ExpectingMayonnaise = !State.ExpectingMayonnaise;
        Helper.Data.WriteSaveData(SaveDataKey, State);

        if (!string.IsNullOrWhiteSpace(quote))
            Game1.drawObjectDialogue(quote);
    }

    private void SanitizeState()
    {
        State.RemainingMayonnaiseQuotes.RemoveAll(static quote => string.IsNullOrWhiteSpace(quote));
        State.RemainingSecretNoteQuotes.RemoveAll(static quote => string.IsNullOrWhiteSpace(quote));
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
        return quote;
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
