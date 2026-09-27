using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace NocturneSmartAutoBattle
{
    // Optional integration with Nocturne Modern Controller, bound by reflection
    // so NocturneSmartAutoBattle.dll has no reference to the Controller DLL and
    // loads (standalone) without it.
    //
    // Smart Auto has no Controller action: it rides on the game's own Auto
    // button. With Controller present it only publishes Settings cards: the
    // on/off toggle and, when Controller supports feature values
    // (IModernFeatureValueProvider), Mode and Speed selectors. If anything is
    // missing or throws, TryCreate returns null and the mod stays standalone.
    internal sealed class ModernControllerIntegration
    {
        internal const string ControllerNamespace = "NocturneModernController";

        internal const string ProviderId = "nocturne_smart_auto_battle";
        internal const string EnabledFeatureId = "smart_auto";
        internal const string ModeFeatureId = "smart_auto_mode";
        internal const string SpeedFeatureId = "smart_auto_speed";

        private readonly PropertyInfo? _isSettingsOpen;
        private readonly PropertyInfo? _useJapaneseUi;
        private readonly MethodInfo _registerFeatureProvider;
        private readonly Type _featureMetadataType;
        private readonly Type _providerInterface;
        private readonly Type? _valueProviderInterface;

        private ModernControllerIntegration(
            PropertyInfo? isSettingsOpen, PropertyInfo? useJapaneseUi, MethodInfo registerFeatureProvider,
            Type featureMetadataType, Type providerInterface, Type? valueProviderInterface)
        {
            _isSettingsOpen = isSettingsOpen;
            _useJapaneseUi = useJapaneseUi;
            _registerFeatureProvider = registerFeatureProvider;
            _featureMetadataType = featureMetadataType;
            _providerInterface = providerInterface;
            _valueProviderInterface = valueProviderInterface;
        }

        // True when Mode / Speed are published as selectors.
        internal bool SupportsValues => _valueProviderInterface != null;

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
                Type featureMetadata = Required("FeatureMetadata");
                Type provider = Required("IModernFeatureProvider");
                MethodInfo registerProvider = api.GetMethod("RegisterFeatureProvider", new[] { provider })
                    ?? throw new MissingMemberException("ModernControllerApi.RegisterFeatureProvider");

                // Feature values need Controller's value interface and the
                // selector metadata; older Controllers only get the toggle.
                Type? valueProvider = controller.GetType(ns + ".IModernFeatureValueProvider");
                if (valueProvider?.GetMethod("SetFeatureValue", new[] { typeof(string), typeof(string) }) == null ||
                    featureMetadata.GetProperty("AllowedValues", typeof(string[])) == null ||
                    featureMetadata.GetProperty("Value", typeof(string)) == null)
                {
                    valueProvider = null;
                }

                reason = "Nocturne Modern Controller integration active" +
                    (valueProvider == null ? "; Mode/Speed selectors unavailable in this Controller version" : string.Empty);
                return new ModernControllerIntegration(
                    api.GetProperty("IsSettingsOpen", typeof(bool)),
                    api.GetProperty("UseJapaneseUi", typeof(bool)),
                    registerProvider,
                    featureMetadata,
                    provider,
                    valueProvider);
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

        internal void Register(ISmartAutoSettingsAccess settings, string version)
        {
            Type proxyInterface = _valueProviderInterface == null
                ? _providerInterface
                : CombinedProviderInterface(_providerInterface, _valueProviderInterface);
            object provider = typeof(DispatchProxy)
                .GetMethod(nameof(DispatchProxy.Create))!
                .MakeGenericMethod(proxyInterface, typeof(FeatureProviderProxy))
                .Invoke(null, null)!;
            ((FeatureProviderProxy)provider).Initialize(this, settings, version);
            _registerFeatureProvider.Invoke(null, new[] { provider });
        }

        // Controller's FeatureMetadata[] for the Settings "MOD Features" cards.
        internal object CreateFeatureArray(SmartAutoSettings settings, string version)
        {
            bool ja = UseJapaneseUi;
            var features = new List<object>();

            object enabled = NewFeature(EnabledFeatureId, "Smart Auto Battle", ja
                ? "弱点・耐性・MP・通常攻撃予測を使って標準Autoのコマンドを選択します。"
                : "Choose standard Auto commands using weaknesses, resistances, MP, and attack predictions.",
                settings.Enabled, 50, version);
            features.Add(enabled);

            if (SupportsValues)
            {
                object mode = NewFeature(ModeFeatureId, ja ? "Smart Auto: モード" : "Smart Auto: Mode", ja
                    ? "スキル優先はSmart Autoの判断でコマンドを選びます。通常攻撃のみはゲーム標準Autoのままです。"
                    : "Skill Priority lets Smart Auto choose commands. Normal Attack Only keeps the game's standard Auto.",
                    settings.Enabled, 51, version);
                SetValues(mode, settings.Mode.ToString(), new Dictionary<string, string>
                {
                    [nameof(SmartAutoMode.NormalAttackOnly)] = ja ? "通常攻撃のみ" : "Normal Attack Only",
                    [nameof(SmartAutoMode.SkillPriority)] = ja ? "スキル優先" : "Skill Priority"
                });
                features.Add(mode);

                object speed = NewFeature(SpeedFeatureId, ja ? "Smart Auto: 速度" : "Smart Auto: Speed", ja
                    ? "標準AutoがONの戦闘中だけ適用されます。"
                    : "Applies only while the game's Auto is on in battle.",
                    settings.Enabled, 52, version);
                SetValues(speed, SmartAutoSettings.FormatSpeed(settings.Speed),
                    SmartAutoSettings.AllowedSpeeds.ToDictionary(
                        SmartAutoSettings.FormatSpeed,
                        value => "x" + SmartAutoSettings.FormatSpeed(value)));
                features.Add(speed);
            }

            Array result = Array.CreateInstance(_featureMetadataType, features.Count);
            for (int index = 0; index < features.Count; index++)
            {
                result.SetValue(features[index], index);
            }
            return result;
        }

        private object NewFeature(string id, string name, string description, bool enabled, int sortOrder, string version)
        {
            object feature = Activator.CreateInstance(_featureMetadataType)!;
            Set(feature, "Id", id);
            Set(feature, "Name", name);
            Set(feature, "Description", description);
            Set(feature, "Category", "Gameplay Change");
            Set(feature, "Enabled", enabled);
            Set(feature, "SortOrder", sortOrder);
            Set(feature, "Version", version);
            return feature;
        }

        private void SetValues(object feature, string value, Dictionary<string, string> labels)
        {
            Set(feature, "AllowedValues", labels.Keys.ToArray());
            Set(feature, "Value", value);
            // Labels are optional for Controller: without the property the
            // Settings window shows the raw values.
            _featureMetadataType.GetProperty("AllowedValueLabels", typeof(Dictionary<string, string>))
                ?.SetValue(feature, labels);
        }

        private static void Set(object target, string property, object value) =>
            (target.GetType().GetProperty(property) ?? throw new MissingMemberException(target.GetType().Name + "." + property))
                .SetValue(target, value);

        private static readonly Dictionary<(Type, Type), Type> CombinedInterfaces = new();

        // DispatchProxy implements one interface; a runtime interface that
        // inherits both of Controller's lets one proxy be both providers.
        // Built once per interface pair: DispatchProxy caches its proxy types,
        // and a second same-named dynamic interface would not match them.
        private static Type CombinedProviderInterface(Type provider, Type valueProvider)
        {
            lock (CombinedInterfaces)
            {
                if (!CombinedInterfaces.TryGetValue((provider, valueProvider), out Type? combined))
                {
                    combined = BuildCombinedProviderInterface(provider, valueProvider);
                    CombinedInterfaces[(provider, valueProvider)] = combined;
                }
                return combined;
            }
        }

        private static Type BuildCombinedProviderInterface(Type provider, Type valueProvider)
        {
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("NocturneSmartAutoBattle.ControllerProvider"), AssemblyBuilderAccess.Run);
            TypeBuilder type = assembly.DefineDynamicModule("ControllerProvider").DefineType(
                "NocturneSmartAutoBattle.ISmartAutoFeatureProvider",
                TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
            type.AddInterfaceImplementation(provider);
            type.AddInterfaceImplementation(valueProvider);
            return type.CreateType()!;
        }
    }

    // Reads and changes the settings on behalf of Controller's Settings window.
    internal interface ISmartAutoSettingsAccess
    {
        SmartAutoSettings Current { get; }
        bool SetEnabled(bool enabled);
        bool SetMode(SmartAutoMode mode);
        bool SetSpeed(float speed);
    }

    // Implements Controller's IModernFeatureProvider (and, when available,
    // IModernFeatureValueProvider) at runtime; DispatchProxy needs a public,
    // non-sealed proxy type.
    public class FeatureProviderProxy : DispatchProxy
    {
        private ModernControllerIntegration? _integration;
        private ISmartAutoSettingsAccess? _settings;
        private string _version = string.Empty;

        internal void Initialize(ModernControllerIntegration integration, ISmartAutoSettingsAccess settings, string version)
        {
            _integration = integration;
            _settings = settings;
            _version = version;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "get_ProviderId" => ModernControllerIntegration.ProviderId,
            "get_ProviderName" => "Nocturne Smart Auto Battle",
            "get_Version" => _version,
            "GetFeatures" => _integration!.CreateFeatureArray(_settings!.Current, _version),
            "SetFeatureEnabled" => SetEnabled(args?[0] as string, args?[1] is bool enabled && enabled),
            "SetFeatureValue" => SetValue(args?[0] as string, args?[1] as string),
            _ => throw new NotSupportedException(targetMethod?.Name)
        };

        // Only the Smart Auto card is an on/off feature. Mode / Speed are
        // changed through SetFeatureValue only.
        private bool SetEnabled(string? featureId, bool enabled) =>
            string.Equals(featureId, ModernControllerIntegration.EnabledFeatureId, StringComparison.OrdinalIgnoreCase) &&
            _settings!.SetEnabled(enabled);

        private bool SetValue(string? featureId, string? value)
        {
            if (string.Equals(featureId, ModernControllerIntegration.ModeFeatureId, StringComparison.OrdinalIgnoreCase))
            {
                return SmartAutoSettings.TryParseMode(value, out SmartAutoMode mode) && _settings!.SetMode(mode);
            }
            if (string.Equals(featureId, ModernControllerIntegration.SpeedFeatureId, StringComparison.OrdinalIgnoreCase))
            {
                return SmartAutoSettings.TryParseSpeed(value, out float speed) && _settings!.SetSpeed(speed);
            }
            return false;
        }
    }

    internal static class ControllerAssembly
    {
        internal static Assembly? Find() => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly =>
            string.Equals(assembly.GetName().Name, ModernControllerIntegration.ControllerNamespace,
                StringComparison.OrdinalIgnoreCase));
    }
}
