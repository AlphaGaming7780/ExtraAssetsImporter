using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraLib;
using Game.Prefabs;
using Game.SceneFlow;
using Game.UI.Menu;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter.Importers
{
    class AssetPackImporter : FileImporter
    {
        public const string kAssetEndName = "AssetPack";

        public override string ImporterId => "AssetPackPrefab";

        public override string FileName => "AssetPack.json";

        public override string AssetEndName => kAssetEndName;

        public override bool PreImporter => true;

        private AssetPackJson LoadJSON(string path)
        {

            AssetPackJson assetPackJson = new();
            //string jsonPath = Path.Combine(path, "decal.json");
            if (File.Exists(path))
            {
                assetPackJson = Decoder.Decode(File.ReadAllText(path)).Make<AssetPackJson>();
            }
            return assetPackJson;
        }

        public static bool TryGetAssetPackPrefab( ImportData data, out AssetPackPrefab assetPackPrefab )
        {
            assetPackPrefab = null;

            if (EL.m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(AssetPackPrefab), $"{data.ModName} {kAssetEndName}"), out var p1)
                && p1 is AssetPackPrefab assetPack)
            {
                assetPackPrefab = assetPack;
                return true;
            }

            return false;

        }

        protected override IEnumerator LoadCustomAssetFolder(string folder, string modName, Dictionary<string, string> localisation, NotificationUISystem.NotificationInfo notificationInfo)
        {
            EAI.Logger.Info($"{modName} {AssetEndName}");
            
            AssetPackJson assetPackJson = LoadJSON(folder);

            AssetPackPrefab assetPackPrefab = ScriptableObject.CreateInstance<AssetPackPrefab>();

            string fullAssetName = $"{modName} {AssetEndName}";
            assetPackPrefab.name = fullAssetName;

            UIObject assetPackUI = assetPackPrefab.AddComponent<UIObject>();
            assetPackUI.m_Icon = File.Exists(Path.Combine( Path.GetDirectoryName(folder), $"{modName}.svg" )) ? $"{Icons.COUIBaseLocation}/{modName}.svg" : Icons.GetIcon(assetPackPrefab);

            EL.m_PrefabSystem.AddPrefab(assetPackPrefab);

            if (!localisation.ContainsKey($"Assets.NAME[{fullAssetName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.NAME[{fullAssetName}]")) localisation.Add($"Assets.NAME[{fullAssetName}]", assetPackJson.PackName);
            if (!localisation.ContainsKey($"Assets.DESCRIPTION[{fullAssetName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.DESCRIPTION[{fullAssetName}]")) localisation.Add($"Assets.DESCRIPTION[{fullAssetName}]", assetPackJson.PackName);

            yield return null;
        }
    }
}
