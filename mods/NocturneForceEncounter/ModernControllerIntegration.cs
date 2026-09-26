using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace NocturneForceEncounter
{
    // Optional integration with Nocturne Modern Controller, bound by reflection
    // so NocturneForceEncounter.dll has no reference to the Controller DLL and
    // loads (standalone) without it.
    //
    // When Controller is present and exposes the members below, Force
    // Encounter is registered as a Controller action (key config, saved
    // bindings) and as a feature provider (Settings card). If anything is
    // missing or throws, TryCreate returns null and the mod stays standalone.
    internal sealed class ModernControllerIntegration
    {
        internal const string ControllerNamespace = "NocturneModernController";

        // Same ActionId as Controller's former built-in action, so existing
        // player bindings keep working.
        internal const string ActionId = "nocturne-modern-controller.force-encounter";
        internal const string ProviderId = "nocturne_force_encounter";
        internal const string FeatureId = "force_encounter";

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

        internal bool IsHeld() => (bool)_isHeld.Invoke(null, new[] { ActionId, _fieldContext, 0 })!;

        internal void Register(Func<bool> getEnabled, Func<bool, bool> setEnabled, string version)
        {
            bool ja = UseJapaneseUi;
            object definition = Activator.CreateInstance(_actionDefinitionType)!;
            Set(definition, "ModId", "NocturneForceEncounter");
            Set(definition, "ActionId", ActionId);
            Set(definition, "DisplayName", ja ? "強制エンカウント" : "Force Encounter");
            Set(definition, "Description", ja
                ? "通常エンカウント可能な場所で標準の遭遇判定を発生させます。"
                : "Request a standard encounter where normal encounters are available.");
            Set(definition, "Contexts", _fieldContext);
            Set(definition, "Behavior", Enum.Parse(_behaviorType, "Press"));

            object binding = Activator.CreateInstance(_defaultBindingType)!;
            Set(binding, "Context", _fieldContext);
            ((IList)Get(binding, "Buttons")).Add(Enum.Parse(_buttonType, "X"));
            ((IList)Get(definition, "DefaultBindings")).Add(binding);
            _registerAction.Invoke(null, new[] { definition });

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
            Set(feature, "Name", "Force Encounter");
            Set(feature, "Description", ja
                ? "通常エンカウント可能な場所で戦闘開始を要求します。"
                : "Request a battle only where normal encounters are available.");
            Set(feature, "Category", "Gameplay Change");
            Set(feature, "Enabled", enabled);
            Set(feature, "SortOrder", 40);
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
            "get_ProviderName" => "Nocturne Force Encounter",
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
