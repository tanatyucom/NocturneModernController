using MelonLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace NocturneModernController
{
    internal static class SmartAutoKnowledgeStore
    {
        private static readonly Dictionary<int, HashSet<int>> Known = new();

        private static string FilePath => Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
            "NocturneModernController.smart-auto-knowledge.json");

        internal static void Load()
        {
            Known.Clear();
            try
            {
                if (!File.Exists(FilePath))
                {
                    return;
                }

                Dictionary<int, int[]>? data = JsonSerializer.Deserialize<Dictionary<int, int[]>>(
                    File.ReadAllText(FilePath));
                if (data == null)
                {
                    return;
                }

                foreach (KeyValuePair<int, int[]> entry in data)
                {
                    Known[entry.Key] = new HashSet<int>(entry.Value);
                }
                MelonLogger.Msg(
                    $"[NocturneModernController] SMART-AUTO knowledge loaded: demons={Known.Count}.");
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[NocturneModernController] SMART-AUTO knowledge load failed: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal static bool IsKnown(int demonId, int attribute)
        {
            return Known.TryGetValue(demonId, out HashSet<int>? attributes) &&
                   attributes.Contains(attribute);
        }

        internal static void Learn(int demonId, int attribute)
        {
            if (demonId <= 0 || attribute < 0 || attribute >= 7)
            {
                return;
            }

            if (!Known.TryGetValue(demonId, out HashSet<int>? attributes))
            {
                attributes = new HashSet<int>();
                Known[demonId] = attributes;
            }
            if (!attributes.Add(attribute))
            {
                return;
            }

            MelonLogger.Msg(
                $"[NocturneModernController] SMART-AUTO learned: demon={demonId} attr={attribute}.");
            Save();
        }

        internal static void LearnAll(int demonId)
        {
            if (demonId <= 0)
            {
                return;
            }

            if (!Known.TryGetValue(demonId, out HashSet<int>? attributes))
            {
                attributes = new HashSet<int>();
                Known[demonId] = attributes;
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

            MelonLogger.Msg(
                $"[NocturneModernController] SMART-AUTO fully learned: demon={demonId}.");
            Save();
        }

        private static void Save()
        {
            try
            {
                Dictionary<int, int[]> data = Known.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value.OrderBy(value => value).ToArray());
                File.WriteAllText(
                    FilePath,
                    JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[NocturneModernController] SMART-AUTO knowledge save failed: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }
    }
}
