namespace ThaleTheGreat.ItRubsTheMayoIn;

internal sealed class Config
{
    public List<string> MayonnaiseItemIds { get; set; } = new()
    {
        "(O)306",
        "(O)307",
        "(O)308",
        "(O)807"
    };
    public string BookContextTag { get; set; } = "book_item";

    public List<string> MayonnaiseQuotes { get; set; } = new()
    {
        "Say it, don't spray it brother. Dang.",
        "I need a towel now.",
        "There look, I'm putting the Mayonnaise on the skin. I'm rubbing it in.",
        "Where's my supplies. Yeah, come on man, I thought we had a deal."
    };

    public List<string> BookQuotes { get; set; } = new()
    {
        "Yee! Auto Trader!",
        "Ohh August, I don't got this one.",
        "There's some deals in here.",
        "Check this out. 71' Cuda. Plum Crazy Purple."
    };
}
