using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Il2Cpp;

namespace NocturneModernController
{
    [Flags]
    public enum ControllerContext
    {
        None = 0,
        Field = 1,
        Battle = 2,
        Puzzle = 4,
        WorldMap = 8,
        Menu = 16,
        All = Field | Battle | Puzzle | WorldMap | Menu
    }

    public enum ControllerButton
    {
        None,
        A,
        B,
        X,
        Y,
        LB,
        RB,
        LT,
        RT,
        L3,
        R3,
        Start,
        Select,
        DPadUp,
        DPadDown,
        DPadLeft,
        DPadRight
    }

    public enum ControllerActionBehavior
    {
        Press,
        Hold,
        LongPress,
        Toggle,
        DoublePress
    }

    public sealed class ControllerActionDefinition
    {
        public string ModId { get; set; } = string.Empty;
        public string ActionId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ControllerContext Contexts { get; set; }
        public ControllerActionBehavior Behavior { get; set; }
        public List<ControllerDefaultBinding> DefaultBindings { get; set; } = new();
    }

    public sealed class ControllerDefaultBinding
    {
        public ControllerContext Context { get; set; }
        public List<ControllerButton> Buttons { get; set; } = new();
    }

    public sealed class ControllerBindingEntry
    {
        public ControllerContext Context { get; set; }
        public List<ControllerButton> Buttons { get; set; } = new();
        public string ActionId { get; set; } = string.Empty;
        public string Source { get; set; } = "Default";
    }

    public sealed class FeatureMetadata
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string Category { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool RequiresRestart { get; set; }
        public bool ReadOnly { get; set; }
        public string Version { get; set; } = string.Empty;
        public string Warning { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string[]? AllowedValues { get; set; }
        public string? Value { get; set; }
    }

    public sealed class FeatureProviderMetadata
    {
        public string ProviderId { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public List<FeatureMetadata> Features { get; set; } = new();
        public string Error { get; set; } = string.Empty;
    }

    public sealed class FeatureToggleRequest
    {
        public string ProviderId { get; set; } = string.Empty;
        public string FeatureId { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string? Value { get; set; }
    }

    public interface IModernFeatureProvider
    {
        string ProviderId { get; }
        string ProviderName { get; }
        string Version { get; }
        IReadOnlyList<FeatureMetadata> GetFeatures();
        bool SetFeatureEnabled(string featureId, bool enabled);
    }

    public static class ModernControllerApi
    {
        private static readonly Dictionary<string, ControllerActionDefinition> Actions =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<ControllerBindingEntry> Bindings = new();
        private static readonly List<BindingOverrideEntry> BindingOverrides = new();
        private static readonly List<SavedBindingConflict<ControllerBindingEntry>> SavedBindingConflicts = new();
        private static readonly Dictionary<string, IModernFeatureProvider> FeatureProviders =
            new(StringComparer.OrdinalIgnoreCase);
        private static bool _bindingsLoaded;
        private static bool _bindingsLoadedFromLegacy;

        // Read-only Core state for external mods. Read from the game's main
        // thread (MelonMod.OnUpdate, Harmony patches); values are live, not cached.

        // True while the player is in field exploration: the field update
        // (fldPlayer.fldPlayerCalc) ran within the last 100 ms. False in
        // battle, menus, events, loading and while the Settings window is
        // open (the game is minimized), and on the first frames after it closes.
        public static bool IsExplorationActive => ExplorationState.IsExplorationActive;

        // True from the moment Controller launches the Settings window until
        // that Settings process has exited. Mod actions should not run while
        // this is true. It does not cover the time after Settings closes until
        // exploration resumes; check IsExplorationActive for that.
        public static bool IsSettingsOpen => SettingsGuiController.IsOpen;

        // True when Controller's UI language resolves to Japanese (explicit
        // setting, or "Auto" on a Japanese Windows UI culture); false means
        // English. Updated when Settings closes.
        public static bool UseJapaneseUi => ControllerSettings.UseJapanese;

        public static void RegisterFeatureProvider(IModernFeatureProvider provider)
        {
            if (provider == null || string.IsNullOrWhiteSpace(provider.ProviderId) ||
                string.IsNullOrWhiteSpace(provider.ProviderName))
            {
                throw new ArgumentException("ProviderId and ProviderName are required.");
            }

            FeatureProviders[provider.ProviderId] = provider;
            SaveFeatureSnapshot();
        }

        public static bool UnregisterFeatureProvider(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId) || !FeatureProviders.Remove(providerId))
            {
                return false;
            }

            SaveFeatureSnapshot();
            return true;
        }

        public static IReadOnlyList<FeatureProviderMetadata> GetFeatureProviders()
        {
            var result = new List<FeatureProviderMetadata>();
            foreach (IModernFeatureProvider provider in FeatureProviders.Values
                         .OrderBy(item => item.ProviderName, StringComparer.OrdinalIgnoreCase))
            {
                var snapshot = new FeatureProviderMetadata
                {
                    ProviderId = provider.ProviderId,
                    ProviderName = provider.ProviderName,
                    Version = provider.Version
                };
                try
                {
                    IReadOnlyList<FeatureMetadata> features =
                        provider.GetFeatures() ?? Array.Empty<FeatureMetadata>();
                    snapshot.Features = features
                        .Where(IsValidFeature)
                        .GroupBy(feature => feature.Id, StringComparer.OrdinalIgnoreCase)
                        .Select(group => CloneFeature(group.First()))
                        .OrderBy(feature => feature.SortOrder)
                        .ThenBy(feature => feature.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch (Exception exception)
                {
                    snapshot.Error = exception.GetType().Name;
                }
                result.Add(snapshot);
            }
            LoadExternalFeatureSnapshots(result);
            return result;
        }

        public static bool SetFeatureEnabled(
            string providerId,
            string featureId,
            bool enabled)
        {
            if (!FeatureProviders.TryGetValue(providerId, out IModernFeatureProvider? provider))
            {
                return false;
            }

            try
            {
                FeatureMetadata? feature = provider.GetFeatures()
                    .FirstOrDefault(item => string.Equals(
                        item.Id, featureId, StringComparison.OrdinalIgnoreCase));
                if (feature == null || feature.ReadOnly ||
                    !provider.SetFeatureEnabled(featureId, enabled))
                {
                    return false;
                }

                SaveFeatureSnapshot();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void RegisterAction(ControllerActionDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.ModId) ||
                string.IsNullOrWhiteSpace(definition.ActionId) ||
                string.IsNullOrWhiteSpace(definition.DisplayName))
            {
                throw new ArgumentException("ModId, ActionId and DisplayName are required.");
            }

            Actions[definition.ActionId] = definition;
        }

        internal static void ResolveBindings()
        {
            EnsureBindingsLoaded();
            var registeredActionIds = new HashSet<string>(
                Actions.Keys,
                StringComparer.OrdinalIgnoreCase);
            var savedActionContexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var occupiedSlots = new HashSet<string>(StringComparer.Ordinal);
            foreach (ControllerBindingEntry binding in Bindings)
            {
                if (!registeredActionIds.Contains(binding.ActionId))
                {
                    continue;
                }

                savedActionContexts.Add(DefaultBindingResolver.ActionContextKey(
                    (int)binding.Context,
                    binding.ActionId));
                occupiedSlots.Add(DefaultBindingResolver.SlotKey(
                    (int)binding.Context,
                    NormalizeChord(binding.Buttons).Select(button => (int)button)));
            }
            foreach (ControllerBindingEntry binding in SavedBindingConflicts
                         .SelectMany(conflict => conflict.Bindings))
            {
                if (registeredActionIds.Contains(binding.ActionId))
                {
                    savedActionContexts.Add(DefaultBindingResolver.ActionContextKey(
                        (int)binding.Context,
                        binding.ActionId));
                }
            }

            var unassignedOverrides = new HashSet<string>(
                BindingOverrides
                    .Where(entry => entry.State == "Unassigned")
                    .Select(entry => DefaultBindingResolver.ActionContextKey(
                        entry.Context,
                        entry.ActionId)),
                StringComparer.OrdinalIgnoreCase);
            var candidates = Actions.Values.SelectMany(action =>
                action.DefaultBindings
                    .Select(binding => new
                    {
                        Binding = binding,
                        Buttons = NormalizeChord(binding.Buttons)
                    })
                    .Where(item =>
                        IsSingleContext(item.Binding.Context) &&
                        item.Buttons.Count > 0)
                    .Select(item => new DefaultBindingCandidate
                    {
                        Context = (int)item.Binding.Context,
                        Buttons = item.Buttons.Select(button => (int)button).ToArray(),
                        ActionId = action.ActionId
                    }));

            foreach (DefaultBindingCandidate resolved in DefaultBindingResolver.Resolve(
                         candidates,
                         savedActionContexts,
                         occupiedSlots,
                         unassignedOverrides))
            {
                Bindings.Add(new ControllerBindingEntry
                {
                    Context = (ControllerContext)resolved.Context,
                    Buttons = resolved.Buttons.Select(button => (ControllerButton)button).ToList(),
                    ActionId = resolved.ActionId,
                    Source = "Default"
                });
            }

            SaveSnapshots();
        }

        public static IReadOnlyList<ControllerActionDefinition> GetRegisteredActions() =>
            Actions.Values.OrderBy(action => action.ModId).ThenBy(action => action.DisplayName).ToArray();

        public static IReadOnlyList<ControllerBindingEntry> GetBindings() =>
            Bindings.ToArray();

        internal static void SetUnassignedOverride(
            ControllerContext context,
            string actionId)
        {
            EnsureBindingsLoaded();
            if (!IsSingleContext(context) || string.IsNullOrWhiteSpace(actionId))
            {
                throw new ArgumentException("A single context and ActionId are required.");
            }

            BindingEditOperations.SetUnassigned(
                Bindings,
                BindingOverrides,
                (int)context,
                actionId,
                binding => (int)binding.Context,
                binding => binding.ActionId);
        }

        internal static void ClearUnassignedOverride(
            ControllerContext context,
            string actionId)
        {
            EnsureBindingsLoaded();
            BindingEditOperations.ClearOverride(
                BindingOverrides,
                (int)context,
                actionId);
        }

        internal static bool HasUnassignedOverride(
            ControllerContext context,
            string actionId)
        {
            EnsureBindingsLoaded();
            return BindingEditOperations.HasOverride(
                BindingOverrides,
                (int)context,
                actionId);
        }

        public static bool IsHeld(string actionId, ControllerContext context, int padNumber = 0)
        {
            EnsureBindingsLoaded();
            foreach (ControllerBindingEntry binding in Bindings)
            {
                if (binding.Context != context ||
                    !string.Equals(binding.ActionId, actionId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool allHeld = binding.Buttons.Count > 0;
                foreach (ControllerButton button in binding.Buttons)
                {
                    Il2Cpplibsdf_H.SDF_PADMAP? map = ToPadMap(button);
                    bool held;
                    try
                    {
                        held = map.HasValue &&
                            dds3PadManager.DDS3_PADCHECK_PRESS(map.Value, padNumber);
                    }
                    catch
                    {
                        held = false;
                    }
                    if (!held)
                    {
                        allHeld = false;
                        break;
                    }
                }
                if (allHeld)
                {
                    return true;
                }
            }
            return false;
        }

        internal static void ReloadBindings()
        {
            _bindingsLoaded = false;
            EnsureBindingsLoaded();
        }

        internal static void SaveSnapshots()
        {
            string directory = ModDirectory;
            Directory.CreateDirectory(directory);
            var options = new JsonSerializerOptions { WriteIndented = true };
            WriteRegistrySnapshot(options);
            BindingFileMigration.CreateLegacyBackupOnce(
                BindingsPath,
                _bindingsLoadedFromLegacy);
            AtomicJsonFile.WriteJsonAtomic(
                BindingsPath,
                JsonSerializer.Serialize(
                    BindingFileMigration.CreateV1(
                        Bindings.Concat(SavedBindingConflicts.SelectMany(conflict => conflict.Bindings)),
                        BindingOverrides),
                    options));
            _bindingsLoadedFromLegacy = false;
            SaveFeatureSnapshot();
        }

        // index=7 is CONFIRMED as "Field/Dungeon Menu" (SESSION_RESUME_NOTES.md
        // §17), but "GetConfigGamePad succeeds without throwing" is NOT the
        // same as "the player's saved GAME binding is loaded" (§19,
        // REJECTED by real-hardware evidence: a startup-time 34/34-success
        // sweep, captured before dds3TitleInit had even run, returned a
        // stale/default value for index=7 instead of the player's actual
        // setting). The only readiness signal with real-hardware confirmation
        // is ExplorationState.IsExplorationActive == true (the same gate
        // GameBindingProbe used for the A/B/A that correctly tracked Y/X/Y).
        // SaveSnapshots() still writes whatever partial/default result is
        // available at startup so the file always exists; only a capture
        // taken after exploration becomes active is treated as authoritative
        // and allowed to mark readiness / refresh the registry file, without
        // touching bindings.json or the feature snapshot (no effect on the
        // binding resolver / user binding state).
        private static bool _gameActionBindingsReady;
        private static int _gameActionBindingsRetryFrame;
        private const int GameActionBindingsRetryIntervalFrames = 45;

        // Readiness gate for native GAME binding writes (NativeGameBindingPort).
        internal static bool GameActionBindingsReady => _gameActionBindingsReady;

        // Re-publishes the authoritative GAME binding snapshot after a native
        // binding write, so the next Settings session shows the new value. Only
        // replaces the snapshot with a complete sweep; otherwise leaves it as is.
        internal static void RefreshAuthoritativeGameActionBindings()
        {
            if (!_gameActionBindingsReady)
            {
                return;
            }
            try
            {
                GameActionBindingSnapshotResult gameActionBindings = CaptureGameActionBindingsRaw();
                if (!gameActionBindings.Available ||
                    gameActionBindings.Entries.Count != GameActionBindingSnapshotReader.SlotCount)
                {
                    return;
                }
                WriteRegistrySnapshot(new JsonSerializerOptions { WriteIndented = true }, gameActionBindings);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning(
                    $"[GameActionBindingSnapshot] refresh after GAME binding write failed ({ex.GetType().Name})");
            }
        }

        private static void WriteRegistrySnapshot(
            JsonSerializerOptions options,
            GameActionBindingSnapshotResult? gameActionBindingsOverride = null)
        {
            GameBindingSnapshotResult gameBindings = CaptureGameBindings();
            GameActionBindingSnapshotResult gameActionBindings =
                gameActionBindingsOverride ?? CaptureGameActionBindingsRaw();
            AtomicJsonFile.WriteJsonAtomic(
                RegistryPath,
                JsonSerializer.Serialize(new ActionRegistrySnapshot<ControllerActionDefinition>
                {
                    Actions = Actions.Values
                        .OrderBy(action => action.ActionId, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    GameBindingsAvailable = gameBindings.Available,
                    GameBindings = gameBindings.Bindings.ToList(),
                    GameActionBindingsAvailable = gameActionBindings.Available,
                    GameActionBindingsRaw = gameActionBindings.Entries.ToList(),
                    GameActionBindingsAuthoritative = gameActionBindingsOverride != null
                }, options));
            // NOTE: does not set _gameActionBindingsReady. A successful write
            // here (e.g. from startup SaveSnapshots()) is not proof the
            // player's saved binding was loaded -- see the block comment above.
        }

        // Read-only readiness retry, gated on the one condition with
        // real-hardware confirmation: ExplorationState.IsExplorationActive.
        // GetConfigGamePad is not even called while inactive. Frame-gated
        // once active, to avoid calling the native getter 34x every frame;
        // stops permanently after the first fully successful sweep taken
        // while exploring.
        internal static void RetryGameActionBindingsIfNeeded()
        {
            if (_gameActionBindingsReady)
            {
                return;
            }

            if (!ExplorationState.IsExplorationActive)
            {
                return;
            }

            _gameActionBindingsRetryFrame++;
            if (_gameActionBindingsRetryFrame % GameActionBindingsRetryIntervalFrames != 0)
            {
                return;
            }

            GameActionBindingSnapshotResult gameActionBindings = CaptureGameActionBindingsRaw();
            int failed = GameActionBindingSnapshotReader.SlotCount - gameActionBindings.Entries.Count;
            if (!gameActionBindings.Available || failed > 0)
            {
                MelonLoader.MelonLogger.Msg(
                    $"[GameActionBindingSnapshot] GAME binding state not ready yet after " +
                    $"exploration became active ({failed}/{GameActionBindingSnapshotReader.SlotCount} failed)");
                return;
            }

            _gameActionBindingsReady = true;
            MelonLoader.MelonLogger.Msg(
                $"[GameActionBindingSnapshot] GAME binding state ready after exploration " +
                $"became active ({gameActionBindings.Entries.Count}/{GameActionBindingSnapshotReader.SlotCount}); " +
                "snapshot refreshed");

            string directory = ModDirectory;
            Directory.CreateDirectory(directory);
            var options = new JsonSerializerOptions { WriteIndented = true };
            WriteRegistrySnapshot(options, gameActionBindings);
        }

        private static GameBindingSnapshotResult CaptureGameBindings() =>
            GameBindingSnapshotReader.Capture((controllerId, keyId) =>
            {
                InputAssign inputAssign = InputAssign.Instance;
                if (inputAssign == null)
                {
                    throw new InvalidOperationException("InputAssign.Instance is unavailable.");
                }
                return inputAssign.GetAssignCode(
                    (InputAssign.ControllerID)controllerId,
                    (InputAssign.KeyID)keyId).ToString();
            });

        // GAME binding SSoT candidate (confirmed 2026-09-21, SESSION_RESUME_NOTES.md
        // §17-23; 15 indices independently A/B/A-confirmed as of this writing,
        // see investigations/GAMEBINDING_INDEX_MAP_20260921.md). Distinct read
        // path from CaptureGameBindings/GetAssignCode above; kept separate
        // rather than replacing it, since the majority of the 34 index slots
        // still have no confirmed semantic mapping.
        private static GameActionBindingSnapshotResult CaptureGameActionBindingsRaw() =>
            GameActionBindingSnapshotReader.Capture(dds3ConfigGamePadSteam.GetConfigGamePad);

        internal static void SaveFeatureSnapshot()
        {
            string directory = ModDirectory;
            Directory.CreateDirectory(directory);
            var options = new JsonSerializerOptions { WriteIndented = true };
            AtomicJsonFile.WriteJsonAtomic(
                FeaturesPath,
                JsonSerializer.Serialize(GetFeatureProviders(), options));
        }

        internal static void ApplyFeatureToggleRequests()
        {
            if (!File.Exists(FeatureRequestsPath))
            {
                return;
            }

            try
            {
                List<FeatureToggleRequest>? requests =
                    JsonSerializer.Deserialize<List<FeatureToggleRequest>>(
                        File.ReadAllText(FeatureRequestsPath));
                if (requests != null)
                {
                    var unhandled = new List<FeatureToggleRequest>();
                    foreach (FeatureToggleRequest request in requests)
                    {
                        if (!SetFeatureEnabled(
                            request.ProviderId,
                            request.FeatureId,
                            request.Enabled))
                        {
                            unhandled.Add(request);
                        }
                    }
                    if (unhandled.Count > 0)
                    {
                        AtomicJsonFile.WriteJsonAtomic(
                            FeatureRequestsPath,
                            JsonSerializer.Serialize(
                                unhandled,
                                new JsonSerializerOptions { WriteIndented = true }));
                    }
                    else
                    {
                        File.Delete(FeatureRequestsPath);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                SaveFeatureSnapshot();
            }
        }

        private static void LoadExternalFeatureSnapshots(
            List<FeatureProviderMetadata> result)
        {
            try
            {
                foreach (string path in Directory.GetFiles(
                             ModDirectory,
                             "NocturneModern*.features.json"))
                {
                    if (string.Equals(path, FeaturesPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    try
                    {
                        List<FeatureProviderMetadata>? providers =
                            JsonSerializer.Deserialize<List<FeatureProviderMetadata>>(
                                File.ReadAllText(path));
                        if (providers == null)
                        {
                            continue;
                        }
                        foreach (FeatureProviderMetadata provider in providers)
                        {
                            if (string.IsNullOrWhiteSpace(provider.ProviderId) ||
                                result.Any(item => string.Equals(
                                    item.ProviderId,
                                    provider.ProviderId,
                                    StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }
                            provider.Features = (provider.Features ?? new List<FeatureMetadata>())
                                .Where(IsValidFeature)
                                .GroupBy(feature => feature.Id, StringComparer.OrdinalIgnoreCase)
                                .Select(group => CloneFeature(group.First()))
                                .OrderBy(feature => feature.SortOrder)
                                .ThenBy(feature => feature.Name, StringComparer.OrdinalIgnoreCase)
                                .ToList();
                            result.Add(provider);
                        }
                    }
                    catch
                    {
                        // One malformed optional provider must not break the GUI.
                    }
                }
            }
            catch
            {
            }
        }

        private static bool IsValidFeature(FeatureMetadata? feature) =>
            feature != null &&
            !string.IsNullOrWhiteSpace(feature.Id) &&
            !string.IsNullOrWhiteSpace(feature.Name);

        private static FeatureMetadata CloneFeature(FeatureMetadata feature) => new()
        {
            Id = feature.Id,
            Name = feature.Name,
            Description = feature.Description,
            Enabled = feature.Enabled,
            Category = feature.Category,
            SortOrder = feature.SortOrder,
            RequiresRestart = feature.RequiresRestart,
            ReadOnly = feature.ReadOnly,
            Version = feature.Version,
            Warning = feature.Warning,
            Notes = feature.Notes,
            AllowedValues = feature.AllowedValues,
            Value = feature.Value
        };

        private static void EnsureBindingsLoaded()
        {
            if (_bindingsLoaded)
            {
                return;
            }
            _bindingsLoaded = true;
            Bindings.Clear();
            BindingOverrides.Clear();
            SavedBindingConflicts.Clear();
            _bindingsLoadedFromLegacy = false;
            try
            {
                BindingLoadResult<ControllerBindingEntry> loaded =
                    BindingFileMigration.Read<ControllerBindingEntry>(
                        BindingsPath,
                        IsValidBinding,
                        GetBindingSlotKey,
                        binding => binding.Source = "Legacy");
                Bindings.AddRange(loaded.Bindings);
                BindingOverrides.AddRange(loaded.Overrides);
                SavedBindingConflicts.AddRange(loaded.SavedConflicts);
                foreach (SavedBindingConflict<ControllerBindingEntry> conflict in loaded.SavedConflicts)
                {
                    MelonLoader.MelonLogger.Warning(
                        "[NocturneModernController] SavedBindingConflict " +
                        conflict.SlotKey + ": " +
                        string.Join(", ", conflict.Bindings
                            .Select(binding => binding.ActionId)
                            .OrderBy(actionId => actionId, StringComparer.OrdinalIgnoreCase)));
                }
                _bindingsLoadedFromLegacy = loaded.IsLegacy;
            }
            catch
            {
            }
        }

        private static bool IsValidBinding(ControllerBindingEntry binding) =>
            IsSingleContext(binding.Context) &&
            !string.IsNullOrWhiteSpace(binding.ActionId) &&
            binding.Buttons != null &&
            binding.Buttons.Count is >= 1 and <= 3 &&
            binding.Buttons.All(button =>
                button is >= ControllerButton.A and <= ControllerButton.DPadRight) &&
            binding.Buttons.Distinct().Count() == binding.Buttons.Count &&
            binding.Source is "Legacy" or "Default" or "User";

        private static bool IsSingleContext(ControllerContext context) =>
            context is ControllerContext.Field or ControllerContext.Battle or
                ControllerContext.Puzzle or ControllerContext.WorldMap or ControllerContext.Menu;

        private static string GetBindingSlotKey(ControllerBindingEntry binding) =>
            ((int)binding.Context) + ":" + string.Join(",", NormalizeChord(binding.Buttons));

        private static List<ControllerButton> NormalizeChord(IEnumerable<ControllerButton> buttons) =>
            buttons.Where(button => button != ControllerButton.None)
                .Distinct()
                .Take(3)
                .OrderBy(button => button)
                .ToList();

        private static bool SameChord(
            IEnumerable<ControllerButton> left,
            IEnumerable<ControllerButton> right) =>
            NormalizeChord(left).SequenceEqual(NormalizeChord(right));

        private static Il2Cpplibsdf_H.SDF_PADMAP? ToPadMap(ControllerButton button) => button switch
        {
            ControllerButton.A => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_RD,
            ControllerButton.B => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_RR,
            ControllerButton.X => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_RL,
            ControllerButton.Y => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_RU,
            ControllerButton.LB => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_L1,
            ControllerButton.RB => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_R1,
            ControllerButton.LT => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_L2,
            ControllerButton.RT => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_R2,
            ControllerButton.L3 => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_L3,
            ControllerButton.R3 => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_R3,
            ControllerButton.Start => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_START,
            ControllerButton.Select => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_SELECT,
            ControllerButton.DPadUp => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_U,
            ControllerButton.DPadDown => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_D,
            ControllerButton.DPadLeft => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_L,
            ControllerButton.DPadRight => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_R,
            _ => null
        };

        private static string ModDirectory =>
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        internal static string RegistryPath => Path.Combine(
            ModDirectory, "NocturneModernController.actions.json");
        internal static string BindingsPath => Path.Combine(
            ModDirectory, "NocturneModernController.bindings.json");
        internal static string FeaturesPath => Path.Combine(
            ModDirectory, "NocturneModernController.features.json");
        internal static string FeatureRequestsPath => Path.Combine(
            ModDirectory, "NocturneModernController.feature-requests.json");
        internal static string GameBindingRequestPath => Path.Combine(
            ModDirectory, "NocturneModernController.game-binding-request.json");
        internal static string GameBindingResultPath => Path.Combine(
            ModDirectory, "NocturneModernController.game-binding-result.json");
    }
}
