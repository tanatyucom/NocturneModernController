using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace NocturneModernController
{
    // Il2Cpp implementation of INativeGameBindingPort. Only the native APIs
    // proven by the 2026-09-26 round-trip PoC are used:
    //   dds3ConfigGamePadSteam.ChangeKeyDuplicate / GetConfigGamePad / GamePadDoneChk
    //   FsSaveData.SetConfigLocal(4, ...) / GetConfigLocal(4) / SteamConfigLocalCopy(4, false)
    // Never: ChangeKey, GamePadDuplicateWrite, cfgSetBit, DateSave,
    // SteamConfigLocalCopy(-1), ChangeTexEXE, or direct config_data/SelText writes.
    internal sealed class NativeGameBindingPort : INativeGameBindingPort
    {
        private const int GamepadTab = 4;

        // The build the native write path was analysed on
        // (GhidraProjects/SMT3HD_GameAssembly_METADATA.md).
        private const string AnalysedGameAssemblySha256 =
            "59ADBB5B18AAEDC7DF6C1672790CA48BE99B5E8559C81DB27132C436FB0A9FC4";

        private bool? _supportedGameBuild;

        public bool IsReady =>
            FieldDashPatch.IsExplorationActive && ModernControllerApi.GameActionBindingsReady;

        // Hashed once per session, on the first request that gets this far.
        public bool IsSupportedGameBuild
        {
            get
            {
                if (_supportedGameBuild == null)
                {
                    string path = Path.Combine(
                        Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName) ?? string.Empty,
                        "GameAssembly.dll");
                    using FileStream stream = File.OpenRead(path);
                    using SHA256 sha = SHA256.Create();
                    _supportedGameBuild = string.Equals(
                        Convert.ToHexString(sha.ComputeHash(stream)),
                        AnalysedGameAssemblySha256,
                        StringComparison.OrdinalIgnoreCase);
                }
                return _supportedGameBuild.Value;
            }
        }

        public int GetConfigGamePad(int index) => dds3ConfigGamePadSteam.GetConfigGamePad(index);

        public int[] ReadConfigData() => ToArray(ConfigDataTab());

        public int[] ReadSaveLocal() =>
            ToArray(SaveData().GetConfigLocal(GamepadTab)
                ?? throw new InvalidOperationException("FsSaveData.GetConfigLocal(4) is null"));

        public bool GamePadDoneChk() => dds3ConfigGamePadSteam.GamePadDoneChk();

        public bool ChangeKeyDuplicate(int configType, int raw)
        {
            bool duplicate = false;
            dds3ConfigGamePadSteam.ChangeKeyDuplicate((uint)configType, 0, 0, raw, ref duplicate);
            return duplicate;
        }

        public int DuplicateDestinationType => dds3ConfigGamePadSteam.g_dstidx;

        public void PersistFromConfigData() => SaveData().SetConfigLocal(GamepadTab, ConfigDataTab());

        public void PersistFromArray(int[] values)
        {
            var copy = new Il2CppStructArray<int>(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                copy[i] = values[i];
            }
            SaveData().SetConfigLocal(GamepadTab, copy);
        }

        public void RuntimeApply() => SaveData().SteamConfigLocalCopy(GamepadTab, false);

        private static FsSaveData SaveData() =>
            GlobalData.saveData ?? throw new InvalidOperationException("GlobalData.saveData is null");

        private static Il2CppStructArray<int> ConfigDataTab()
        {
            Il2Cppdds3GlobalWork_H.dds3GlobalWork_t global = dds3GlobalWork.DDS3_GBWK
                ?? throw new InvalidOperationException("dds3GlobalWork.DDS3_GBWK is null");
            Il2CppReferenceArray<Il2CppStructArray<int>> configData = global.config_data
                ?? throw new InvalidOperationException("config_data is null");
            if (configData.Length <= GamepadTab)
            {
                throw new InvalidOperationException($"config_data length {configData.Length}");
            }
            return configData[GamepadTab] ?? throw new InvalidOperationException("config_data[4] is null");
        }

        private static int[] ToArray(Il2CppStructArray<int> source)
        {
            var values = new int[source.Length];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = source[i];
            }
            return values;
        }
    }
}
