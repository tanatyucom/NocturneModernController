using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NocturneModernController;

// Provider Value API: request routing, provider dispatch and value labels.
internal static class FeatureValueApiTests
{
    internal static void Run()
    {
        ValueRequestGoesOnlyToValuePath();
        BoolRequestKeepsEnabledPath();
        EmptyValueIsBoolRequest();
        UnsupportedProviderLeavesValueRequestUnhandled();
        ValueRequestHandledOnlyOnSuccess();
        SetValueReachesProviderWithExactArguments();
        SetValueRejectsWhatItShould();
        UnknownProviderIsFalse();
        ThrowingProviderDoesNotBreakLoop();
        SetEnabledUnchanged();
        LabelsUsedWhenPresent();
        LabelsFallBackWhenAbsent();
        MismatchedLabelsDoNotCrash();
        MetadataJsonStaysCompatible();
    }

    // 1 + 2: a value request calls the value path once and never the bool path.
    private static void ValueRequestGoesOnlyToValuePath()
    {
        int enabledCalls = 0, valueCalls = 0;
        List<FeatureToggleRequest> unhandled = FeatureProviderDispatch.ApplyRequests(
            new[] { Request("p", "mode", enabled: false, value: "SkillPriority") },
            _ => { enabledCalls++; return true; },
            _ => { valueCalls++; return true; });
        Check(valueCalls == 1, "value request must call the value path once");
        Check(enabledCalls == 0, "value request must never call the enabled path");
        Check(unhandled.Count == 0, "handled value request must not remain");
    }

    // 3: a bool request calls the enabled path with its Enabled value.
    private static void BoolRequestKeepsEnabledPath()
    {
        var seen = new List<bool>();
        int valueCalls = 0;
        FeatureProviderDispatch.ApplyRequests(
            new[] { Request("p", "a", enabled: true), Request("p", "b", enabled: false) },
            request => { seen.Add(request.Enabled); return true; },
            _ => { valueCalls++; return true; });
        Check(seen.SequenceEqual(new[] { true, false }), "bool requests must pass Enabled unchanged");
        Check(valueCalls == 0, "bool requests must not call the value path");
    }

    private static void EmptyValueIsBoolRequest()
    {
        Check(!FeatureProviderDispatch.IsValueRequest(Request("p", "a", true, value: null)), "null Value is a bool request");
        Check(!FeatureProviderDispatch.IsValueRequest(Request("p", "a", true, value: "")), "empty Value is a bool request");
        Check(FeatureProviderDispatch.IsValueRequest(Request("p", "a", false, value: "x")), "non-empty Value is a value request");
    }

    // 4: a provider without IModernFeatureValueProvider leaves the request in place,
    // and its Enabled state is not touched.
    private static void UnsupportedProviderLeavesValueRequestUnhandled()
    {
        var provider = new BoolOnlyProvider("p", Feature("mode", allowed: new[] { "A", "B" }));
        FeatureToggleRequest request = Request("p", "mode", enabled: false, value: "B");
        List<FeatureToggleRequest> unhandled = FeatureProviderDispatch.ApplyRequests(
            new[] { request },
            r => FeatureProviderDispatch.SetEnabled(provider, r.FeatureId, r.Enabled),
            r => FeatureProviderDispatch.SetValue(provider, r.FeatureId, r.Value));
        Check(unhandled.Count == 1 && ReferenceEquals(unhandled[0], request), "unsupported value request must remain");
        Check(provider.EnabledCalls.Count == 0, "unsupported value request must not disable the feature");
    }

    // 5: handled only when the provider accepts the value.
    private static void ValueRequestHandledOnlyOnSuccess()
    {
        var accepting = new ValueProvider("p", accept: true, Feature("mode", allowed: new[] { "A", "B" }));
        var rejecting = new ValueProvider("q", accept: false, Feature("mode", allowed: new[] { "A", "B" }));
        var providers = new Dictionary<string, IModernFeatureProvider> { ["p"] = accepting, ["q"] = rejecting };
        List<FeatureToggleRequest> unhandled = FeatureProviderDispatch.ApplyRequests(
            new[] { Request("p", "mode", false, "B"), Request("q", "mode", false, "B") },
            r => FeatureProviderDispatch.SetEnabled(providers[r.ProviderId], r.FeatureId, r.Enabled),
            r => FeatureProviderDispatch.SetValue(providers[r.ProviderId], r.FeatureId, r.Value));
        Check(unhandled.Count == 1 && unhandled[0].ProviderId == "q", "only the rejected request must remain");
        Check(accepting.EnabledCalls.Count == 0 && rejecting.EnabledCalls.Count == 0, "no Enabled change on value requests");
    }

    // 6: provider, feature and value arrive exactly.
    private static void SetValueReachesProviderWithExactArguments()
    {
        var provider = new ValueProvider("p", accept: true,
            Feature("speed", allowed: new[] { "1.0", "1.5", "2.0" }), Feature("mode", allowed: new[] { "A" }));
        Check(FeatureProviderDispatch.SetValue(provider, "speed", "1.5"), "allowed value must succeed");
        Check(provider.ValueCalls.Count == 1 && provider.ValueCalls[0] == ("speed", "1.5"),
            "SetFeatureValue must receive the requested feature and value");
    }

    private static void SetValueRejectsWhatItShould()
    {
        var provider = new ValueProvider("p", accept: true,
            Feature("mode", allowed: new[] { "A", "B" }),
            Feature("locked", allowed: new[] { "A" }, readOnly: true),
            Feature("free"));
        Check(!FeatureProviderDispatch.SetValue(provider, "mode", "C"), "value outside AllowedValues must be rejected");
        Check(!FeatureProviderDispatch.SetValue(provider, "mode", "a"), "AllowedValues match is case-sensitive");
        Check(!FeatureProviderDispatch.SetValue(provider, "locked", "A"), "read-only feature must be rejected");
        Check(!FeatureProviderDispatch.SetValue(provider, "missing", "A"), "unknown feature must be rejected");
        Check(!FeatureProviderDispatch.SetValue(provider, "mode", ""), "empty value must be rejected");
        Check(provider.ValueCalls.Count == 0, "rejected values must not reach the provider");
        Check(FeatureProviderDispatch.SetValue(provider, "free", "anything"),
            "a feature without AllowedValues leaves validation to the provider");
    }

    // 7
    private static void UnknownProviderIsFalse()
    {
        Check(!FeatureProviderDispatch.SetValue(null, "mode", "A"), "unknown provider (value) must be false");
        Check(!FeatureProviderDispatch.SetEnabled(null, "mode", true), "unknown provider (enabled) must be false");
    }

    // 8: a throwing provider or callback only makes its own request unhandled.
    private static void ThrowingProviderDoesNotBreakLoop()
    {
        var throwing = new ValueProvider("t", accept: true, Feature("mode", allowed: new[] { "A" })) { Throw = true };
        Check(!FeatureProviderDispatch.SetValue(throwing, "mode", "A"), "throwing SetFeatureValue must be false");
        Check(!FeatureProviderDispatch.SetEnabled(throwing, "mode", true), "throwing SetFeatureEnabled must be false");

        int after = 0;
        List<FeatureToggleRequest> unhandled = FeatureProviderDispatch.ApplyRequests(
            new FeatureToggleRequest?[] { Request("t", "a", true), null, Request("p", "b", true) },
            r => r.ProviderId == "t" ? throw new InvalidOperationException("boom") : ++after > 0,
            _ => true);
        Check(after == 1, "requests after a throwing one must still be applied");
        Check(unhandled.Count == 1 && unhandled[0].ProviderId == "t", "throwing request must remain unhandled");
    }

    // Existing bool behaviour through the new dispatcher.
    private static void SetEnabledUnchanged()
    {
        var provider = new BoolOnlyProvider("p", Feature("dash"), Feature("fixed", readOnly: true));
        Check(FeatureProviderDispatch.SetEnabled(provider, "DASH", false), "feature id match is case-insensitive as before");
        Check(provider.EnabledCalls.SequenceEqual(new[] { ("DASH", false) }), "SetFeatureEnabled must receive the arguments");
        Check(!FeatureProviderDispatch.SetEnabled(provider, "fixed", true), "read-only feature stays rejected");
        Check(!FeatureProviderDispatch.SetEnabled(provider, "missing", true), "unknown feature stays rejected");
    }

    // 9
    private static void LabelsUsedWhenPresent()
    {
        var labels = new Dictionary<string, string>
        {
            ["NormalAttackOnly"] = "Normal Attack Only",
            ["SkillPriority"] = "Skill Priority"
        };
        Check(FeatureValueLabels.Resolve("SkillPriority", labels, Fallback) == "Skill Priority",
            "provider label must be used");
    }

    // 10
    private static void LabelsFallBackWhenAbsent()
    {
        Check(FeatureValueLabels.Resolve("Always", null, Fallback) == "fallback:Always", "no labels must use fallback");
        Check(FeatureValueLabels.Resolve("Always", new Dictionary<string, string>(), Fallback) == "fallback:Always",
            "empty labels must use fallback");
    }

    // 11: missing, extra, blank or null entries never throw.
    private static void MismatchedLabelsDoNotCrash()
    {
        var labels = new Dictionary<string, string>
        {
            ["A"] = "Label A",
            ["Blank"] = "  ",
            ["Null"] = null!,
            ["NotAnAllowedValue"] = "Extra"
        };
        string[] allowed = { "A", "B", "Blank", "Null" };
        string[] shown = allowed.Select(value => FeatureValueLabels.Resolve(value, labels, Fallback)).ToArray();
        Check(shown.SequenceEqual(new[] { "Label A", "fallback:B", "fallback:Blank", "fallback:Null" }),
            "each value must get its own label or the fallback: " + string.Join(",", shown));
        Check(FeatureValueLabels.Resolve(null, labels, value => "fallback") == "fallback", "null raw value must not throw");

        var reordered = new[] { "B", "A" }.Select(value => FeatureValueLabels.Resolve(value, labels, Fallback)).ToArray();
        Check(reordered.SequenceEqual(new[] { "fallback:B", "Label A" }), "labels follow the value, not its position");
    }

    // Old snapshots (no AllowedValueLabels) still load; new ones round-trip.
    private static void MetadataJsonStaysCompatible()
    {
        FeatureMetadata? old = JsonSerializer.Deserialize<FeatureMetadata>(
            "{\"Id\":\"chance\",\"Name\":\"Chance\",\"AllowedValues\":[\"Disabled\",\"Native\",\"Always\"],\"Value\":\"Native\"}");
        Check(old != null && old.AllowedValueLabels == null && old.Value == "Native", "old snapshot must still load");

        var feature = Feature("mode", allowed: new[] { "A", "B" });
        feature.AllowedValueLabels = new Dictionary<string, string> { ["A"] = "Alpha" };
        FeatureMetadata? back = JsonSerializer.Deserialize<FeatureMetadata>(JsonSerializer.Serialize(feature));
        Check(back?.AllowedValueLabels != null && back.AllowedValueLabels["A"] == "Alpha", "labels must round-trip");

        List<FeatureToggleRequest>? requests = JsonSerializer.Deserialize<List<FeatureToggleRequest>>(
            "[{\"ProviderId\":\"p\",\"FeatureId\":\"mode\",\"Enabled\":false,\"Value\":\"B\"}," +
            "{\"ProviderId\":\"p\",\"FeatureId\":\"on\",\"Enabled\":true}]");
        Check(requests != null && FeatureProviderDispatch.IsValueRequest(requests[0]) &&
              !FeatureProviderDispatch.IsValueRequest(requests[1]),
            "Settings request file must classify value and bool requests");
    }

    private static string Fallback(string value) => "fallback:" + value;

    private static FeatureToggleRequest Request(string provider, string feature, bool enabled, string? value = null) =>
        new() { ProviderId = provider, FeatureId = feature, Enabled = enabled, Value = value };

    private static FeatureMetadata Feature(string id, string[]? allowed = null, bool readOnly = false) =>
        new() { Id = id, Name = id, AllowedValues = allowed, ReadOnly = readOnly };

    private class BoolOnlyProvider : IModernFeatureProvider
    {
        private readonly FeatureMetadata[] _features;
        internal readonly List<(string, bool)> EnabledCalls = new();
        internal bool Throw;

        internal BoolOnlyProvider(string id, params FeatureMetadata[] features)
        {
            ProviderId = id;
            _features = features;
        }

        public string ProviderId { get; }
        public string ProviderName => ProviderId;
        public string Version => "test";
        public IReadOnlyList<FeatureMetadata> GetFeatures() => _features;

        public bool SetFeatureEnabled(string featureId, bool enabled)
        {
            if (Throw) throw new InvalidOperationException("provider failure");
            EnabledCalls.Add((featureId, enabled));
            return true;
        }
    }

    private sealed class ValueProvider : BoolOnlyProvider, IModernFeatureValueProvider
    {
        private readonly bool _accept;
        internal readonly List<(string, string)> ValueCalls = new();

        internal ValueProvider(string id, bool accept, params FeatureMetadata[] features) : base(id, features)
        {
            _accept = accept;
        }

        public bool SetFeatureValue(string featureId, string value)
        {
            if (Throw) throw new InvalidOperationException("provider failure");
            ValueCalls.Add((featureId, value));
            return _accept;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("FeatureValueApi: " + message);
        }
    }
}
