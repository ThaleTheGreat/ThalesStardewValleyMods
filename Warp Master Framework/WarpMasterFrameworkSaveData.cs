using System;
using System.Collections.Generic;

namespace WarpMasterFramework
{
    public class WarpMasterFrameworkSaveData
    {
        public Dictionary<string, List<WarpPointData>> WarpModifications { get; set; }
            = new Dictionary<string, List<WarpPointData>>(StringComparer.OrdinalIgnoreCase);
    }
}
