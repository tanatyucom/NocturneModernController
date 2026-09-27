using System.Collections.Generic;

namespace NocturneModernController
{
    internal static class BuiltInControllerActions
    {
        internal const string OpenSettings = "nocturne-modern-controller.open-settings";

        internal static void Register(bool settingsGuiAvailable)
        {
            bool ja = ControllerSettings.UseJapanese;
            if (settingsGuiAvailable)
            {
                ModernControllerApi.RegisterAction(new ControllerActionDefinition
                {
                    ModId = "NocturneModernController",
                    ActionId = OpenSettings,
                    DisplayName = ja ? "設定画面を開く" : "Open Settings",
                    Description = ja ? "統合キーコンフィグを開きます。" : "Open the integrated controller settings window.",
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
