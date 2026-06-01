using System.Collections.Generic;

namespace WarpMasterFramework
{
    public class ModConfig
    {
        public bool EnableVisualEditor { get; set; } = true;
        public bool EnableFrameworkOverrides { get; set; } = true;
        public string VisualEditorKey { get; set; } = "F9";

        // UI-only setting (does NOT affect warp marker hover label text on the map)
        public string UiTextColor { get; set; } = "Black";

        public bool EnableDebugLogging { get; set; } = false;

        // Tooltips for warp markers in the visual editor
        // Valid values: "Hide", "Hover", "Show"
        public string SourceTooltipMode { get; set; } = "Hover";
        public string TargetTooltipMode { get; set; } = "Hover";
    }
}
