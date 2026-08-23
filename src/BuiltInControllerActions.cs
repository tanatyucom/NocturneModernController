using System.Collections.Generic;

namespace NocturneModernController
{
    internal static class BuiltInControllerActions
    {
        internal const string Dash = "nocturne-modern-controller.dash";
        internal const string DashKeep = "nocturne-modern-controller.dash-keep";
        internal const string QuickHeal = "nocturne-modern-controller.quick-heal";
        internal const string ForceEncounter = "nocturne-modern-controller.force-encounter";
        internal const string OpenSettings = "nocturne-modern-controller.open-settings";

        internal static void Register(bool settingsGuiAvailable)
        {
            ModernControllerApi.RegisterAction(new ControllerActionDefinition
            {
                ModId = "NocturneModernController",
                ActionId = Dash,
                DisplayName = "ダッシュ",
                Description = "押している間、FIELD/DUNGEONの移動速度を上げます。",
                Contexts = ControllerContext.Field,
                Behavior = ControllerActionBehavior.Hold,
                DefaultBindings = new List<ControllerDefaultBinding>
                {
                    new() { Context = ControllerContext.Field, Buttons = new() { ControllerButton.LT } },
                    new() { Context = ControllerContext.Field, Buttons = new() { ControllerButton.RT } }
                }
            });
            ModernControllerApi.RegisterAction(new ControllerActionDefinition
            {
                ModId = "NocturneModernController",
                ActionId = DashKeep,
                DisplayName = "ダッシュ固定切替",
                Description = "ダッシュ固定のON/OFFを切り替えます。",
                Contexts = ControllerContext.Field,
                Behavior = ControllerActionBehavior.Press,
                DefaultBindings = new List<ControllerDefaultBinding>
                {
                    new() { Context = ControllerContext.Field, Buttons = new() { ControllerButton.LT, ControllerButton.RT } }
                }
            });
            ModernControllerApi.RegisterAction(new ControllerActionDefinition
            {
                ModId = "NocturneModernController",
                ActionId = QuickHeal,
                DisplayName = "クイックヒール",
                Description = "所持スキルとMPを使って仲間全員を回復します。",
                Contexts = ControllerContext.Field,
                Behavior = ControllerActionBehavior.Press,
                DefaultBindings = new List<ControllerDefaultBinding>
                {
                    new() { Context = ControllerContext.Field, Buttons = new() { ControllerButton.RB } }
                }
            });
            ModernControllerApi.RegisterAction(new ControllerActionDefinition
            {
                ModId = "NocturneModernController",
                ActionId = ForceEncounter,
                DisplayName = "強制エンカウント",
                Description = "通常エンカウント可能な場所で標準の遭遇判定を発生させます。",
                Contexts = ControllerContext.Field,
                Behavior = ControllerActionBehavior.Press,
                DefaultBindings = new List<ControllerDefaultBinding>
                {
                    new() { Context = ControllerContext.Field, Buttons = new() { ControllerButton.X } }
                }
            });
            if (settingsGuiAvailable)
            {
                ModernControllerApi.RegisterAction(new ControllerActionDefinition
                {
                    ModId = "NocturneModernController",
                    ActionId = OpenSettings,
                    DisplayName = "設定画面を開く",
                    Description = "統合キーコンフィグを開きます。",
                    Contexts = ControllerContext.All,
                    Behavior = ControllerActionBehavior.LongPress,
                    DefaultBindings = new List<ControllerDefaultBinding>
                    {
                        new() { Context = ControllerContext.Field, Buttons = new() { ControllerButton.Select } },
                        new() { Context = ControllerContext.Battle, Buttons = new() { ControllerButton.Select } },
                        new() { Context = ControllerContext.Puzzle, Buttons = new() { ControllerButton.Select } },
                        new() { Context = ControllerContext.WorldMap, Buttons = new() { ControllerButton.Select } },
                        new() { Context = ControllerContext.Menu, Buttons = new() { ControllerButton.Select } }
                    }
                });
            }
        }
    }
}
