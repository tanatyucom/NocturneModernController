using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace NocturneModernDash
{
    // Optional integration with Nocturne Modern Controller, bound by reflection
    // so NocturneModernDash.dll has no reference to the Controller DLL and
    // loads (standalone) without it. Same approach as NocturneForceEncounter
    // and NocturneQuickHeal, with the actions described as data because Dash
    // registers two (Dash, Dash Keep).
    //
    // When Controller is present and exposes the members below, both actions
    // are registered as Controller actions (key config, saved bindings) and
    // Dash as a feature provider (Settings card). If anything is missing or
    // throws, TryCreate returns null and the mod stays standalone.
    internal sealed class ModernControllerIntegration
    {
        internal const string ControllerNamespace = "NocturneModernController";

        // Same ActionIds as Controller's former built-in actions, so existing
        // player bindings keep working.
        internal const string DashActionId = "nocturne-modern-controller.dash";
        internal const string DashKeepActionId = "nocturne-modern-controller.dash-keep";
        internal const string ProviderId = "nocturne_modern_dash";
        internal const string FeatureId = "dash";

        private sealed class ActionSpec
        {
            internal ActionSpec(string actionId, string behavior, string nameJa, string nameEn,
                string descriptionJa, string descriptionEn, params string[][] defaultChords)
            {
                ActionId = actionId;
                Behavior = behavior;
                NameJa = nameJa;
                NameEn = nameEn;
                DescriptionJa = descriptionJa;
                DescriptionEn = descriptionEn;
                DefaultChords = defaultChords;
            }

            internal string ActionId { get; }
            internal string Behavior { get; }
            internal string NameJa { get; }
            internal string NameEn { get; }
            internal string DescriptionJa { get; }
            internal string DescriptionEn { get; }
            internal string[][] DefaultChords { get; }
        }

        // Mirrors Controller's former BuiltInControllerActions entries.
        private static readonly ActionSpec[] Actions =
        {
            new(DashActionId, "Hold", "ダッシュ", "Dash",
                "押している間、FIELD/DUNGEONの移動速度を上げます。", "Increase movement speed in FIELD/DUNGEON while held.",
                new[] { "LT" }, new[] { "RT" }),
            new(DashKeepActionId, "Press", "ダッシュ固定切替", "Toggle Dash Keep",
                "ダッシュ固定のON/OFFを切り替えます。", "Toggle persistent dash on or off.",
                new[] { "LT", "RT" })
        };

        private readonly MethodInfo _isHeld;
        private readonly object _fieldContext;
        private readonly PropertyInfo? _isSettingsOpen;
        private readonly PropertyInfo? _useJapaneseUi;
        private readonly MethodInfo _registerAction;
        private readonly MethodInfo _registerFeatureProvider;
        private readonly Type _actionDefinitionType;
        private readonly Type _defaultBindingType;
        private readonly Type _behaviorType;
        private readonly Type _buttonType;
        private readonly Type _featureMetadataType;
        private readonly Type _providerInterface;

        private ModernControllerIntegration(
            MethodInfo isHeld, object fieldContext, PropertyInfo? isSettingsOpen, PropertyInfo? useJapaneseUi,
            MethodInfo registerAction, MethodInfo registerFeatureProvider, Type actionDefinitionType,
            Type defaultBindingType, Type behaviorType, Type buttonType, Type featureMetadataType, Type providerInterface)
        {
            _isHeld = isHeld;
            _fieldContext = fieldContext;
            _isSettingsOpen = isSettingsOpen;
            _useJapaneseUi = useJapaneseUi;
            _registerAction = registerAction;
            _registerFeatureProvider = registerFeatureProvider;
            _actionDefinitionType = actionDefinitionType;
            _defaultBindingType = defaultBindingType;
            _behaviorType = behaviorType;
            _buttonType = buttonType;
            _featureMetadataType = featureMetadataType;
            _providerInterface = providerInterface;
        }

        internal static ModernControllerIntegration? TryCreate(
            Assembly? controller, out string reason, string ns = ControllerNamespace)
        {
            if (controller == null)
            {
                reason = "Nocturne Modern Controller is not installed";
                return null;
            }
            try
            {
                Type Required(string name) =>
                    controller.GetType(ns + "." + name) ?? throw new MissingMemberException(name);

                Type api = Required("ModernControllerApi");
                Type context = Required("ControllerContext");
                Type actionDefinition = Required("ControllerActionDefinition");
                Type defaultBinding = Required("ControllerDefaultBinding");
                Type behavior = Required("ControllerActionBehavior");
                Type button = Required("ControllerButton");
                Type featureMetadata = Required("FeatureMetadata");
                Type provider = Required("IModernFeatureProvider");

                MethodInfo isHeld = api.GetMethod("IsHeld", new[] { typeof(string), context, typeof(int) })
                    ?? throw new MissingMemberException("ModernControllerApi.IsHeld");
                MethodInfo registerAction = api.GetMethod("RegisterAction", new[] { actionDefinition })
                    ?? throw new MissingMemberException("ModernControllerApi.RegisterAction");
                MethodInfo registerProvider = api.GetMethod("RegisterFeatureProvider", new[] { provider })
                    ?? throw new MissingMemberException("ModernControllerApi.RegisterFeatureProvider");

                reason = "Nocturne Modern Controller integration active";
                return new ModernControllerIntegration(
                    isHeld,
                    Enum.Parse(context, "Field"),
                    api.GetProperty("IsSettingsOpen", typeof(bool)),
                    api.GetProperty("UseJapaneseUi", typeof(bool)),
                    registerAction,
                    registerProvider,
                    actionDefinition,
                    defaultBinding,
                    behavior,
                    button,
                    featureMetadata,
                    provider);
            }
            catch (Exception exception)
            {
                reason = "Nocturne Modern Controller API not compatible (" +
                    exception.GetType().Name + ": " + exception.Message + ")";
                return null;
            }
        }

        // Missing on older Controller versions: then Settings is treated as closed.
        internal bool IsSettingsOpen => _isSettingsOpen != null && (bool)_isSettingsOpen.GetValue(null)!;

        // Missing on older Controller versions: then the Windows UI culture decides.
        internal bool UseJapaneseUi => _useJapaneseUi != null
            ? (bool)_useJapaneseUi.GetValue(null)!
            : System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja";

        internal bool IsHeld(string actionId) => (bool)_isHeld.Invoke(null, new[] { actionId, _fieldContext, 0 })!;

        internal void Register(Func<bool> getEnabled, Func<bool, bool> setEnabled, string version)
        {
            bool ja = UseJapaneseUi;
            foreach (ActionSpec spec in Actions)
            {
                object definition = Activator.CreateInstance(_actionDefinitionType)!;
                Set(definition, "ModId", "NocturneModernDash");
                Set(definition, "ActionId", spec.ActionId);
                Set(definition, "DisplayName", ja ? spec.NameJa : spec.NameEn);
                Set(definition, "Description", ja ? spec.DescriptionJa : spec.DescriptionEn);
                Set(definition, "Contexts", _fieldContext);
                Set(definition, "Behavior", Enum.Parse(_behaviorType, spec.Behavior));

                IList defaultBindings = (IList)Get(definition, "DefaultBindings");
                foreach (string[] chord in spec.DefaultChords)
                {
                    object binding = Activator.CreateInstance(_defaultBindingType)!;
                    Set(binding, "Context", _fieldContext);
                    IList buttons = (IList)Get(binding, "Buttons");
                    foreach (string button in chord)
                    {
                        buttons.Add(Enum.Parse(_buttonType, button));
                    }
                    defaultBindings.Add(binding);
                }
                _registerAction.Invoke(null, new[] { definition });
            }

            object provider = typeof(System.Reflection.DispatchProxy)
                .GetMethod(nameof(System.Reflection.DispatchProxy.Create))!
                .MakeGenericMethod(_providerInterface, typeof(FeatureProviderProxy))
                .Invoke(null, null)!;
            ((FeatureProviderProxy)provider).Initialize(this, getEnabled, setEnabled, version);
            _registerFeatureProvider.Invoke(null, new[] { provider });
        }

        // Controller's FeatureMetadata[] for the Settings "MOD Features" card.
        internal object CreateFeatureArray(bool enabled, string version)
        {
            bool ja = UseJapaneseUi;
            object feature = Activator.CreateInstance(_featureMetadataType)!;
            Set(feature, "Id", FeatureId);
            Set(feature, "Name", "Dash");
            Set(feature, "Description", ja
                ? "ダンジョンとワールドマップで移動速度を上げます。"
                : "Increase movement speed in dungeons and on the world map.");
            Set(feature, "Category", "QoL");
            Set(feature, "Enabled", enabled);
            Set(feature, "SortOrder", 20);
            Set(feature, "Version", version);
            Array features = Array.CreateInstance(_featureMetadataType, 1);
            features.SetValue(feature, 0);
            return features;
        }

        private static void Set(object target, string property, object value) =>
            (target.GetType().GetProperty(property) ?? throw new MissingMemberException(target.GetType().Name + "." + property))
                .SetValue(target, value);

        private static object Get(object target, string property) =>
            (target.GetType().GetProperty(property) ?? throw new MissingMemberException(target.GetType().Name + "." + property))
                .GetValue(target)!;
    }

    // Implements Controller's IModernFeatureProvider at runtime (DispatchProxy
    // needs a public, non-sealed proxy type).
    public class FeatureProviderProxy : System.Reflection.DispatchProxy
    {
        private ModernControllerIntegration? _integration;
        private Func<bool> _getEnabled = () => false;
        private Func<bool, bool> _setEnabled = _ => false;
        private string _version = string.Empty;

        internal void Initialize(
            ModernControllerIntegration integration, Func<bool> getEnabled, Func<bool, bool> setEnabled, string version)
        {
            _integration = integration;
            _getEnabled = getEnabled;
            _setEnabled = setEnabled;
            _version = version;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "get_ProviderId" => ModernControllerIntegration.ProviderId,
            "get_ProviderName" => "Nocturne Modern Dash",
            "get_Version" => _version,
            "GetFeatures" => _integration!.CreateFeatureArray(_getEnabled(), _version),
            "SetFeatureEnabled" => string.Equals(args?[0] as string, ModernControllerIntegration.FeatureId,
                                       StringComparison.OrdinalIgnoreCase) && _setEnabled((bool)args![1]!),
            _ => throw new NotSupportedException(targetMethod?.Name)
        };
    }

    internal static class ControllerAssembly
    {
        internal static Assembly? Find() => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly =>
            string.Equals(assembly.GetName().Name, ModernControllerIntegration.ControllerNamespace,
                StringComparison.OrdinalIgnoreCase));
    }
}
