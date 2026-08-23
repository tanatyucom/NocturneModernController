using System;
using System.Collections.Generic;

namespace NocturneModernController
{
    internal sealed class BuiltInFeatureProvider : IModernFeatureProvider
    {
        internal static BuiltInFeatureProvider Instance { get; } = new();

        public string ProviderId => "nocturne_modern_controller";
        public string ProviderName => "Nocturne Modern Controller";
        public string Version => "1.0.0";

        public IReadOnlyList<FeatureMetadata> GetFeatures()
        {
            ControllerSettings settings = ControllerSettings.Current;
            return new[]
            {
                Feature("right_stick_camera", "Right Stick Camera",
                    "右スティックでダンジョンの旋回・カメラ上下を操作します。",
                    "QoL", settings.RightStickEnabled, 10),
                Feature("dash", "Dash",
                    "ダンジョンとワールドマップで移動速度を上げます。",
                    "QoL", settings.DashEnabled, 20),
                Feature("quick_heal", "Quick Heal",
                    "探索中に回復スキルを使ってパーティをまとめて回復します。",
                    "QoL", settings.QuickHealEnabled, 30),
                Feature("force_encounter", "Force Encounter",
                    "通常エンカウント可能な場所で戦闘開始を要求します。",
                    "Gameplay Change", settings.ForceEncounterEnabled, 40),
                Feature("smart_auto", "Smart Auto Battle",
                    "弱点・耐性・MP・通常攻撃予測を使って標準Autoのコマンドを選択します。",
                    "Gameplay Change", settings.SmartAutoEnabled, 50)
            };
        }

        public bool SetFeatureEnabled(string featureId, bool enabled)
        {
            ControllerSettings settings = ControllerSettings.Current;
            switch (featureId.ToLowerInvariant())
            {
                case "right_stick_camera": settings.RightStickEnabled = enabled; break;
                case "dash": settings.DashEnabled = enabled; break;
                case "quick_heal": settings.QuickHealEnabled = enabled; break;
                case "force_encounter": settings.ForceEncounterEnabled = enabled; break;
                case "smart_auto":
                    settings.SmartAutoEnabled = enabled;
                    if (!enabled) SmartAutoBattleRuntime.Shutdown();
                    break;
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
            Version = "1.0.0"
        };
    }
}
