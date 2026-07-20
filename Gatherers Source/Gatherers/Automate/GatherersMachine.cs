using Microsoft.Xna.Framework;
using Pathoschild.Stardew.Automate;
using StardewValley;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.Gatherers.Automate;

public sealed class GatherersMachine : IMachine
{
    private readonly Chest Chest;

    public GameLocation Location { get; }
    public Rectangle TileArea { get; }
    public string MachineTypeID { get; } = "ThaleTheGreat.Gatherers/GathererStorage";

    public GatherersMachine(Chest chest, GameLocation location, in Vector2 tile)
    {
        Chest = chest;
        Location = location;
        TileArea = new Rectangle((int)tile.X, (int)tile.Y, 1, 1);
    }

    public MachineState GetState()
    {
        return Chest.Items.Any(IsValidOutput) ? MachineState.Done : MachineState.Processing;
    }

    public ITrackedStack GetOutput()
    {
        Item item = Chest.Items.First(IsValidOutput);
        return new TrackedItem(item).OnEmpty((_, removed) => Chest.Items.Remove(removed));
    }

    public bool SetInput(IStorage input)
    {
        return false;
    }

    private static bool IsValidOutput(Item? item)
    {
        if (item is null)
            return false;

        if (item.QualifiedItemId == "(O)433")
            return true;

        return item.Category is SObject.VegetableCategory or SObject.FruitsCategory or SObject.GreensCategory;
    }
}
