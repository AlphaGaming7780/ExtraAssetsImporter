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
            // Read here, on the main thread, for the Auto value of MaxConcurrentAssets.
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

        // 0 = Auto, see GetMaxConcurrentAssets.
        [SettingsUISection(kMainSection, kNewImportersGroup)]
        [SettingsUIDropdown(typeof(Setting), nameof(GetMaxConcurrentAssetsValues))]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(DisableCondition_UseNewImporters))]
        public int MaxConcurrentAssets { get; set; } = 0;

        public const int kMaxConcurrentAssetsLimit = 8;

        public static DropdownItem<int>[] GetMaxConcurrentAssetsValues()
        {
            DropdownItem<int>[] items = new DropdownItem<int>[kMaxConcurrentAssetsLimit + 1];
            items[0] = new DropdownItem<int> { value = 0, displayName = LocalizedString.Value($"Auto ({GetAutoMaxConcurrentAssets(out _)})") };
            for (int i = 1; i <= kMaxConcurrentAssetsLimit; i++)
                items[i] = new DropdownItem<int> { value = i, displayName = LocalizedString.Value(i.ToString()) };
            return items;
        }

        /// <summary>
        /// How many assets each importer imports at once. <paramref name="reason"/> explains the value, for the log.
        /// </summary>
        internal int GetMaxConcurrentAssets(out string reason)
        {
            if (MaxConcurrentAssets > 0)
            {
                reason = "set by the user";
                return Math.Min(MaxConcurrentAssets, kMaxConcurrentAssetsLimit);
            }
            return GetAutoMaxConcurrentAssets(out reason);
        }

        // Each asset imports up to 3 textures at once (CPU bound), hence a third of the logical cores. Every texture
        // being imported holds its decoded image and compression buffers in memory, so the RAM caps it.
        private static int GetAutoMaxConcurrentAssets(out string reason)
        {
            int cores = Environment.ProcessorCount;
            int value = Math.Max(1, Math.Min(kMaxConcurrentAssetsLimit, cores / 3));

            int ramCap = s_SystemMemoryMB <= 0 ? kMaxConcurrentAssetsLimit
                : s_SystemMemoryMB < 12 * 1024 ? 1
                : s_SystemMemoryMB < 20 * 1024 ? 2
                : s_SystemMemoryMB < 28 * 1024 ? 4
                : kMaxConcurrentAssetsLimit;
            value = Math.Min(value, ramCap);

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
            MaxConcurrentAssets = 0;
            UseOldImporters = true;
            DeleteNotLoadedAssets = true;
            Decals = true;
            Surfaces = true;
            NetLanes = true;
            OldImportersCompatibilityDropDown = EAIOldImportersCompatibility.None;
            NewImportersCompatibilityDropDown = EAINewImportersCompatibility.None;
        }

        internal void ResetCompatibility()
        {
            OldImportersCompatibilityDropDown = EAIOldImportersCompatibility.None;
            NewImportersCompatibilityDropDown = EAINewImportersCompatibility.None;
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

