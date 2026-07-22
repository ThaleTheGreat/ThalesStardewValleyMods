using Pathoschild.Stardew.Automate;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using ThaleTheGreat.GatherersAutomateIntegration.Automate;
using ThaleTheGreat.GatherersAutomateIntegration.Integration;

namespace ThaleTheGreat.GatherersAutomateIntegration;

public sealed class ModEntry : Mod
{
    public override void Entry(IModHelper helper)
    {
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        try
        {
            IGatherersApi? gatherers = Helper.ModRegistry.GetApi<IGatherersApi>("ThaleTheGreat.Gatherers");
            if (gatherers is null)
            {
                Monitor.Log("Gatherers did not provide its API. The Automate integration was not registered.", LogLevel.Error);
                return;
            }

            IAutomateAPI? automate = Helper.ModRegistry.GetApi<IAutomateAPI>("Pathoschild.Automate");
            if (automate is null)
            {
                Monitor.Log("Automate did not provide its API. The Gatherers integration was not registered.", LogLevel.Error);
                return;
            }

            automate.AddFactory(new GatherersAutomationFactory(gatherers));
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to register the Gatherers Automate integration: {ex}", LogLevel.Error);
        }
    }
}
