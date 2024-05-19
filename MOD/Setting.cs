using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using System.CodeDom;

namespace ExtraAssetsImporter;

[FileLocation($"ModSettings\\{nameof(ExtraAssetsImporter)}\\settings")]
[SettingsUIGroupOrder(kImportersGroup, kMPHGroup)]
[SettingsUIShowGroupName(kImportersGroup, kMPHGroup)]
public class Setting(IMod mod) : ModSetting(mod)
{
    public const string kSection = "Main";
    public const string kImportersGroup = "Importers";
    public const string kImportersSurfacesGroup = "Surfaces";
    public const string kImportersDecalsGroup = "Decals";
    public const string kMPHGroup = "Missing Prefab Helper";

    [SettingsUISection(kSection, kImportersGroup, kImportersSurfacesGroup)]
    public bool Surfaces { get; set; } = true;

    [SettingsUISection(kSection, kImportersGroup, kImportersDecalsGroup)]
    public bool Decals { get; set; } = true;
    public bool DeleteDecalsCache { set { } }

    [SettingsUISection(kSection, kMPHGroup)]
    public EAICompatibility CompatibilityDropDown { get; set; } = EAICompatibility.None;


    public bool dummySettingsToAvoidSettingsBugThanksCO = false;

    public override void SetDefaults()
    {
        Decals = true;
        Surfaces = true;
        CompatibilityDropDown = EAICompatibility.None;
        dummySettingsToAvoidSettingsBugThanksCO = false;
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
