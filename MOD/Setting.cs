using Colossal.IO.AssetDatabase;
using ExtraAssetsImporter.AssetImporter;
using ExtraAssetsImporter.Database;
using Game.Modding;
using Game.Settings;
using Game.UI.Localization;
using Game.UI.Widgets;
using System;
using System.IO;
using UnityEngine;

namespace ExtraAssetsImporter
{
    //[FileLocation($"ModsSettings\\{nameof(ExtraAssetsImporter)}\\settings")]
    [FileLocation("ExtraAssetsImporter")]
    [SettingsUIGroupOrder(kNewImportersGroup, kOldImportersGroup, kMPHGroup)]
    [SettingsUIShowGroupName(kNewImportersGroup, kOldImportersGroup, kMPHGroup)]
    public class Setting : ModSetting
    {
        public Setting(IMod mod) : base(mod)
        {
            // Read here, on the main thread, for the Auto value of MaxConcurrentTextureImports.
            s_SystemMemoryMB = SystemInfo.systemMemorySize;
        }

        private static int s_SystemMemoryMB;

        public const string kMainSection = "Main";
        public const string kDataBaseSection = "DataBase";
        public const string kNewImportersGroup = "NewImporters";
        public const string kOldImportersGroup = "OldImporters";
        public const string kMPHGroup = "MissingPrefabHelper";
        internal bool DeleteDataBase { get; private set; } = false;

        [SettingsUISection(kMainSection, kNewImportersGroup)]
        public bool UseNewImporters { get; set; } = true;
        public bool DisableCondition_UseNewImporters => !UseNewImporters;

        [SettingsUISection(kMainSection, kNewImportersGroup)]
        public EAINewImportersCompatibility NewImportersCompatibilityDropDown { get; set; } = EAINewImportersCompatibility.None;

        // Saves the final built prefabs to disk so a later session can reload them instead of rebuilding
        // from source (ImportersUtils.TryGetCachedPrefab). Independent of the render prefab, which is
        // always saved regardless — see PrefabImporterBase/AssetPackImporter for the exact save points.
        // Disabled (and forced off, see CanSavePrefabs) when the database isn't in a ".Database" folder.
        [SettingsUISection(kMainSection, kNewImportersGroup)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(DisableCondition_SavePrefabs))]
        public bool SavePrefabs { get; set; } = false;
        public bool DisableCondition_SavePrefabs => DisableCondition_UseNewImporters || !EAIDatabaseManager.IsDatabaseInDatabaseFolder;
        internal bool CanSavePrefabs => SavePrefabs && !DisableCondition_SavePrefabs;

        // 0 = Auto, see GetMaxConcurrentTextureImports. The new and old importers share it.
        [SettingsUISection(kMainSection, kNewImportersGroup)]
        [SettingsUIDropdown(typeof(Setting), nameof(GetMaxConcurrentTextureImportsValues))]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(DisableCondition_MaxConcurrentTextureImports))]
        public int MaxConcurrentTextureImports { get; set; } = 0;
        public bool DisableCondition_MaxConcurrentTextureImports => !UseNewImporters && !UseOldImporters;

        public static DropdownItem<int>[] GetMaxConcurrentTextureImportsValues()
        {
            int limit = Environment.ProcessorCount;
            DropdownItem<int>[] items = new DropdownItem<int>[limit + 1];
            items[0] = new DropdownItem<int> { value = 0, displayName = LocalizedString.Value($"Auto ({GetAutoMaxConcurrentTextureImports(out _)})") };
            for (int i = 1; i <= limit; i++)
                items[i] = new DropdownItem<int> { value = i, displayName = LocalizedString.Value(i.ToString()) };
            return items;
        }

        /// <summary>
        /// How many textures all the importers import at once. <paramref name="reason"/> explains
        /// the value, for the log.
        /// </summary>
        internal int GetMaxConcurrentTextureImports(out string reason)
        {
            if (MaxConcurrentTextureImports > 0)
            {
                reason = "set by the user";
                return Math.Min(MaxConcurrentTextureImports, Environment.ProcessorCount);
            }
            return GetAutoMaxConcurrentTextureImports(out reason);
        }

        // An import keeps one core busy, so Auto leaves two to the game's own threads, or one on a
        // CPU of four cores or fewer, where two would idle half of it. Each import holds its
        // image's buffers, so a machine short on RAM gets fewer.
        private static int GetAutoMaxConcurrentTextureImports(out string reason)
        {
            int cores = Environment.ProcessorCount;
            int value = Math.Max(1, cores <= 4 ? cores - 1 : cores - 2);
            if (s_SystemMemoryMB > 0 && s_SystemMemoryMB < 12 * 1024) value = Math.Min(value, 4);

            reason = $"auto: {cores} logical cores, {s_SystemMemoryMB / 1024f:F1} GB RAM";
            return value;
        }

        [SettingsUISection(kMainSection, kNewImportersGroup)]
        public bool ExportDefaultJson { set { AssetsImporterManager.ExportImportersTemplate(); } }

        [SettingsUISection(kMainSection, kNewImportersGroup)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(DisableCondition_ReloadDatabase))]
        public bool ReloadDatabase { set { if (!DisableCondition_ReloadDatabase) AssetsImporterManager.ReloadCustomAssets(); } }
        public bool DisableCondition_ReloadDatabase => DisableCondition_IsImporting || !EAIDatabaseManager.IsDatabaseInDatabaseFolder;

        [SettingsUISection(kMainSection, kOldImportersGroup)]
        public bool UseOldImporters { get; set; } = true;
        public bool DisableCondition_UseOldImporters => !UseOldImporters;

        [SettingsUISection(kMainSection, kOldImportersGroup)]
        [SettingsUIDisableByConditionAttribute(typeof(Setting), nameof(DisableCondition_UseOldImporters))]
        public bool Surfaces { get; set; } = true;

        [SettingsUISection(kMainSection, kOldImportersGroup)]
        [SettingsUIDisableByConditionAttribute(typeof(Setting), nameof(DisableCondition_UseOldImporters))]
        public bool Decals { get; set; } = true;

        [SettingsUISection(kMainSection, kOldImportersGroup)]
        [SettingsUIDisableByConditionAttribute(typeof(Setting), nameof(DisableCondition_UseOldImporters))]
        public bool NetLanes { get; set; } = true;

        [SettingsUISection(kMainSection, kOldImportersGroup)]
        public EAIOldImportersCompatibility OldImportersCompatibilityDropDown { get; set; } = EAIOldImportersCompatibility.None;

        [SettingsUISection(kDataBaseSection, "")]
        [SettingsUIDirectoryPicker]
        public string DatabasePath
        {
            get
            {
                if (EAIDatabaseManager.eaiDatabase == null || EAIDatabaseManager.eaiDatabase.ActualDatabasePath == null)
                    return "";
                return EAIDatabaseManager.eaiDatabase.ActualDatabasePath;
            }
            set { try { SavedDatabasePath = Path.GetFullPath(value); } catch { EAI.Logger.Warn($"Invalid database path: {value}"); } }
        }
        public string SavedDatabasePath = null;

        [SettingsUISection(kDataBaseSection, "")]
        public bool DeleteNotLoadedAssets { get; set; } = true;

        public bool DisableCondition_IsImporting => AssetsImporterManager.IsImporting;

        [SettingsUISection(kDataBaseSection, "")]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(DeleteDataBase))]
        public bool DeleteDataBaseOnClose { set { DeleteDataBase = true; } }

        public override void SetDefaults()
        {
            UseNewImporters = true;
            SavePrefabs = false;
            MaxConcurrentTextureImports = 0;
            UseOldImporters = true;
            DeleteNotLoadedAssets = true;
            Decals = true;
            Surfaces = true;
            NetLanes = true;
            OldImportersCompatibilityDropDown = EAIOldImportersCompatibility.None;
            NewImportersCompatibilityDropDown = EAINewImportersCompatibility.None;
        }

        // A pick that differs from the held value stays for a later pass to take.
        internal void ResetCompatibility(EAIOldImportersCompatibility appliedOld, EAINewImportersCompatibility appliedNew)
        {
            if (OldImportersCompatibilityDropDown == appliedOld) OldImportersCompatibilityDropDown = EAIOldImportersCompatibility.None;
            if (NewImportersCompatibilityDropDown == appliedNew) NewImportersCompatibilityDropDown = EAINewImportersCompatibility.None;
            Apply();
        }
    }

    public enum EAIOldImportersCompatibility
    {
        None,
        ELT2,
        ELT3,
        PreEditor,
        LocalAsset,
    }

    public enum EAINewImportersCompatibility
    {
        None,
        LocalAsset,
        PreEditor,
    }

}

