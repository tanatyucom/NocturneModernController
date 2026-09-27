using System;
using System.Collections.Generic;

namespace NocturneModernController
{
    internal sealed class BuiltInFeatureProvider : IModernFeatureProvider
    {
        internal static BuiltInFeatureProvider Instance { get; } = new();

        public string ProviderId => "nocturne_modern_controller";
        public string ProviderName => "Nocturne Modern Controller";
        public string Version => "3.0.0";

        public IReadOnlyList<FeatureMetadata> GetFeatures()
        {
            ControllerSettings settings = ControllerSettings.Current;
            bool ja = ControllerSettings.UseJapanese;
            return new[]
            {
                Feature("right_stick_camera", "Right Stick Camera",
                    ja ? "右スティックでダンジョンの旋回・カメラ上下を操作します。" : "Use the right stick for dungeon turning and vertical camera control.",
                    "QoL", settings.RightStickEnabled, 10)
            };
        }

        public bool SetFeatureEnabled(string featureId, bool enabled)
        {
            ControllerSettings settings = ControllerSettings.Current;
            switch (featureId.ToLowerInvariant())
            {
                case "right_stick_camera": settings.RightStickEnabled = enabled; break;
                default: return false;
            }
            ControllerSettings.Save();
            return true;
        }

        private static FeatureMetadata Feature(
            string id, string name, string description, string category,
            bool enabled, int sortOrder) => new()
        {
            Id = id,
            Name = name,
            Description = description,
            Category = category,
            Enabled = enabled,
            SortOrder = sortOrder,
            Version = "3.0.0"
        };
    }
}
