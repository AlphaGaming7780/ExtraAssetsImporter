using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace ExtraAssetsImporter;

[FileLocation($"ModsSettings\\{nameof(ExtraAssetsImporter)}\\settings")]
[SettingsUIGroupOrder(kImportersGroup, kMPHGroup)]
[SettingsUIShowGroupName(kImportersGroup, kMPHGroup)]
public class Setting(IMod mod) : ModSetting(mod)
{
    public const string kMainSection = "Main";
    public const string kDataBaseSection = "DataBase";
    public const string kImportersGroup = "Importers";
    public const string kMPHGroup = "Missing Prefab Helper";
    internal bool DeleteDataBase { get; private set; } = false;

    [SettingsUISection(kMainSection, kImportersGroup)]
    public bool Surfaces { get; set; } = true;

    [SettingsUISection(kMainSection, kImportersGroup)]
    public bool Decals { get; set; } = true;

    [SettingsUISection(kMainSection, kImportersGroup)]
    public bool NetLanes { get; set; } = true;

    [SettingsUISection(kMainSection, kMPHGroup)]
    public EAICompatibility CompatibilityDropDown { get; set; } = EAICompatibility.None;

    [SettingsUISection(kDataBaseSection, "")]
    [SettingsUIDirectoryPicker]
    //public string DataBasePath { get { return EAIDataBaseManager.eaiDataBase.ActualDataBasePath; } set { EAIDataBaseManager.RelocateAssetDataBase(value); } }// = "C:/";
    public string DatabasePath { get { return EAIDataBaseManager.eaiDataBase.ActualDataBasePath; } set { SavedDatabasePath = value; } }
    public string SavedDatabasePath = null;

    [SettingsUISection(kDataBaseSection, "")]
    public bool DeleteNotLoadedAssets { get; set; } = true;

    [SettingsUISection(kDataBaseSection, "")]
    [SettingsUIDisableByCondition(typeof(Setting), nameof(DeleteDataBase))]
    public bool DeleteDataBaseOnClose { set { DeleteDataBase = true; } }
    //public bool DisableCondition => DeleteDataBase;

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
