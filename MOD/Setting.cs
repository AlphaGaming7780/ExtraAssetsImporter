﻿using Colossal.IO.AssetDatabase;
using ExtraAssetsImporter.DataBase;
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
        public bool DisableCondition_UseNewImporters => UseNewImporters;

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

        [SettingsUISection(kMainSection, kMPHGroup)]
        public EAICompatibility CompatibilityDropDown { get; set; } = EAICompatibility.None;

        [SettingsUISection(kDataBaseSection, "")]
        [SettingsUIDirectoryPicker]
        public string DatabasePath
        {
            get
            {
                if (EAIDataBaseManager.eaiDataBase == null || EAIDataBaseManager.eaiDataBase.ActualDataBasePath == null)
                    return "";
                return EAIDataBaseManager.eaiDataBase.ActualDataBasePath;
            }
            set { SavedDatabasePath = Path.GetDirectoryName(value); }
        }
        public string SavedDatabasePath = null;

        [SettingsUISection(kDataBaseSection, "")]
        public bool DeleteNotLoadedAssets { get; set; } = true;

        [SettingsUISection(kDataBaseSection, "")]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(DeleteDataBase))]
        public bool DeleteDataBaseOnClose { set { DeleteDataBase = true; } }

        public override void SetDefaults()
        {
            Decals = true;
            Surfaces = true;
            NetLanes = true;
            CompatibilityDropDown = EAICompatibility.None;
        }

        internal void ResetCompatibility()
        {
            CompatibilityDropDown = EAICompatibility.None;
            ApplyAndSave();
        }
    }

    public enum EAICompatibility
    {
        None,
        ELT2,
        ELT3,
        LocalAsset,
    }

}

