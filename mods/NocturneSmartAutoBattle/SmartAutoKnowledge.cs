using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace NocturneSmartAutoBattle
{
    // Which enemy affinities (demon id x attribute 0..6) Smart Auto has seen,
    // so only those count as "known" in the decision. Stored in
    // Mods\NocturneSmartAutoBattle.knowledge.json. Without that file, the
    // knowledge Controller's built-in version collected
    // (NocturneModernController.smart-auto-knowledge.json, same format) is
    // copied once; the Controller file is left as it is.
    internal sealed class SmartAutoKnowledge
    {
        internal const string FileName = "NocturneSmartAutoBattle.knowledge.json";
        private const string ControllerFileName = "NocturneModernController.smart-auto-knowledge.json";

        private readonly Dictionary<int, HashSet<int>> _known = new();
        private readonly string _path;
        private readonly Action<string> _log;

        internal SmartAutoKnowledge(string directory, Action<string> log)
        {
            _path = Path.Combine(directory, FileName);
            _log = log;
        }

        internal static string ModDirectory =>
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;

        internal int DemonCount => _known.Count;

        internal void Load()
        {
            _known.Clear();
            string directory = Path.GetDirectoryName(_path) ?? string.Empty;
            string legacyPath = Path.Combine(directory, ControllerFileName);
            bool migrate = !File.Exists(_path) && File.Exists(legacyPath);
            string? source = File.Exists(_path) ? _path : migrate ? legacyPath : null;
            if (source == null)
            {
                return;
            }

            try
            {
                Dictionary<int, int[]>? data = JsonSerializer.Deserialize<Dictionary<int, int[]>>(
                    File.ReadAllText(source));
                if (data != null)
                {
                    foreach (KeyValuePair<int, int[]> entry in data)
                    {
                        _known[entry.Key] = new HashSet<int>(entry.Value ?? Array.Empty<int>());
                    }
                }
                if (migrate)
                {
                    Save();
                    _log($"knowledge copied from {ControllerFileName}: demons={_known.Count}.");
                }
                else
                {
                    _log($"knowledge loaded: demons={_known.Count}.");
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or
                                              UnauthorizedAccessException or NotSupportedException)
            {
                _known.Clear();
                _log("knowledge load failed: " + exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal bool IsKnown(int demonId, int attribute) =>
            _known.TryGetValue(demonId, out HashSet<int>? attributes) && attributes.Contains(attribute);

        internal void Learn(int demonId, int attribute)
        {
            if (demonId <= 0 || attribute < 0 || attribute >= 7)
            {
                return;
            }

            if (!_known.TryGetValue(demonId, out HashSet<int>? attributes))
            {
                attributes = new HashSet<int>();
                _known[demonId] = attributes;
            }
            if (!attributes.Add(attribute))
            {
                return;
            }

            _log($"learned: demon={demonId} attr={attribute}.");
            Save();
        }

        internal void LearnAll(int demonId)
        {
            if (demonId <= 0)
            {
                return;
            }

            if (!_known.TryGetValue(demonId, out HashSet<int>? attributes))
            {
                attributes = new HashSet<int>();
                _known[demonId] = attributes;
            }

            bool changed = false;
            for (int attribute = 0; attribute < 7; attribute++)
            {
                changed |= attributes.Add(attribute);
            }
            if (!changed)
            {
                return;
            }

            _log($"fully learned: demon={demonId}.");
            Save();
        }

        private void Save()
        {
            try
            {
                Dictionary<int, int[]> data = _known.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value.OrderBy(value => value).ToArray());
                string temporaryPath = _path + ".tmp";
                File.WriteAllText(
                    temporaryPath,
                    JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporaryPath, _path, overwrite: true);
            }
            catch (Exception exception)
            {
                _log("knowledge save failed: " + exception.GetType().Name + ": " + exception.Message);
            }
        }
    }
}
