using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace ExtraAssetsImporter;

[FileLocation($"ModSettings\\{nameof(ExtraAssetsImporter)}\\settings")]
[SettingsUIGroupOrder(kImportersGroup, kMPHGroup)]
[SettingsUIShowGroupName(kImportersGroup, kMPHGroup)]
public class Setting(IMod mod) : ModSetting(mod)
{
    public const string kSection = "Main";
    public const string kImportersGroup = "Importers";
    public const string kMPHGroup = "Missing Prefab Helper";

    [SettingsUISection(kSection, kImportersGroup)]
    public bool Surfaces { get; set; } = true;

    [SettingsUISection(kSection, kImportersGroup)]
    public bool Decals { get; set; } = true;

    [SettingsUISection(kSection, kMPHGroup)]
    public bool ELT2Compatibility { get; set; } = false;

    [SettingsUISection(kSection, kMPHGroup)]
    public bool ELT3Compatibility { get; set; } = false;

    [SettingsUISection(kSection, kMPHGroup)]
    public bool LocalAssetCompatibility { get; set; } = false;

    public bool dummySettingsToAvoidSettingsBugThanksCO = false;

    public override void SetDefaults()
    {
        Decals = true;
        Surfaces = true;
        ELT2Compatibility = false;
        ELT3Compatibility = false;
        LocalAssetCompatibility = false;
        dummySettingsToAvoidSettingsBugThanksCO = false;
    }

    internal void ResetCompatibility()
    {
        ELT2Compatibility = false;
        ELT3Compatibility = false;
        LocalAssetCompatibility = false;
        ApplyAndSave();
    }
}
