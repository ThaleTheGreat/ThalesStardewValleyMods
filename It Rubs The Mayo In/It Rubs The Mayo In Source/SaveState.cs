namespace ThaleTheGreat.ItRubsTheMayoIn;

internal sealed class SaveState
{
    public bool ExpectingMayonnaise { get; set; } = true;
    public int MayonnaiseThrowsGiven { get; set; }
    public List<string> RemainingMayonnaiseQuotes { get; set; } = new();
    public List<string> RemainingSecretNoteQuotes { get; set; } = new();
}
