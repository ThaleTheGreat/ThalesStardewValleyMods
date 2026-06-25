using System;
using System.Collections.Generic;

namespace ThaleTheGreat.IndustrializationForUtilityGridRedux;

internal sealed class ModConfig
{
    public Dictionary<string, MachineRuleConfig> MachineRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class MachineRuleConfig
{
    public bool Enabled { get; set; } = true;
    public int? PowerProduced { get; set; }
    public int? PowerConsumed { get; set; }
    public int? WaterProduced { get; set; }
    public int? WaterConsumed { get; set; }
}
