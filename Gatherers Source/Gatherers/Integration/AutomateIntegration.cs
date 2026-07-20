using Pathoschild.Stardew.Automate;
using StardewModdingAPI;
using ThaleTheGreat.Gatherers.Automate;

namespace ThaleTheGreat.Gatherers.Integration;

internal sealed class AutomateIntegration
{
    private readonly IModRegistry ModRegistry;
    private readonly IMonitor Monitor;

    public AutomateIntegration(IModRegistry modRegistry, IMonitor monitor)
    {
        ModRegistry = modRegistry;
        Monitor = monitor;
    }

    public void Register()
    {
        try
        {
            IAutomateAPI? automate = ModRegistry.GetApi<IAutomateAPI>("Pathoschild.Automate");
            if (automate is null)
                return;

            automate.AddFactory(new GatherersAutomationFactory());
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to register Automate integration: {ex}", LogLevel.Error);
        }
    }
}
