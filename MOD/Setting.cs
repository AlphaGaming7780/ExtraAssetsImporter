using Colossal.IO.AssetDatabase;
using ExtraAssetsImporter.AssetImporter;
using ExtraAssetsImporter.Database;
using Game.Modding;
using Game.Settings;
using System.IO;

namespace ExtraAssetsImporter
{
    //[FileLocation($"ModsSettings\\{nameof(ExtraAssetsImporter)}\\settings")]
    [FileLocation("ExtraAssetsImporter")]
    [SettingsUIGroupOrder(kNewImportersGroup, kOldImportersGroup, kMPHGroup)]
    [SettingsUIShowGroupName(kNewImportersGroup, kOldImportersGroup, kMPHGroup)]
    public class Setting : ModSetting
    {
        public Setting(IMod mod) : base(mod) { }

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

