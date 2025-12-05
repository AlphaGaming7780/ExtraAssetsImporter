using Colossal.Core;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.UI;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraLib;
using Game.Prefabs;
using Game.SceneFlow;
using Game.UI.Menu;
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
            if (File.Exists(path))
            {
                assetPackJson = Decoder.Decode(File.ReadAllText(path)).Make<AssetPackJson>();
            }
            return assetPackJson;
        }

        public static bool TryGetAssetPackPrefab( PrefabImportData data, out AssetPackPrefab assetPackPrefab )
        {
            assetPackPrefab = null;

            if (EL.m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(AssetPackPrefab), $"{data.ModName} {kAssetEndName}", Colossal.Hash128.CreateGuid($"{data.ModName} {kAssetEndName}")), out var p1)
                && p1 is AssetPackPrefab assetPack)
            {
                assetPackPrefab = assetPack;
                return true;
            }

            return false;

        }

        protected override void LoadCustomAssetFolder(ImporterSettings importSettings, string folder, string modName, NotificationUISystem.NotificationInfo notificationInfo)
        {
            EAI.Logger.Info($"{modName} {AssetEndName}");

            string assetDataPath = importSettings.isAssetPack ?
                        Path.Combine(importSettings.outputFolderOffset, modName) :
                        Path.Combine(importSettings.outputFolderOffset, ImporterId, modName);

            AssetPackJson assetPackJson = LoadJSON(folder);

            AssetPackPrefab assetPackPrefab = ScriptableObject.CreateInstance<AssetPackPrefab>();

            string fullAssetName = $"{modName} {AssetEndName}";
            assetPackPrefab.name = fullAssetName;

            UIObject assetPackUI = assetPackPrefab.AddComponent<UIObject>();

            string svgIconPath = Path.Combine(Path.GetDirectoryName(folder), $"{modName}.svg"); // Doesn't work with SVG, fuck.
            string pngIconPath = Path.Combine(Path.GetDirectoryName(folder), $"{modName}.png"); // Doesn't work with SVG, fuck.

            if (importSettings.isAssetPack)
            {
                ImageAsset imageAsset = ImportersUtils.ImportImageFromPath(pngIconPath, assetDataPath, importSettings, fullAssetName);

                if (imageAsset == null) EAI.Logger.Warn("Image asset is null.");

                assetPackUI.m_Icon = imageAsset != null ? imageAsset.ToGlobalUri() : Icons.GetIcon(assetPackPrefab);
            }
            else
            {
                assetPackUI.m_Icon = File.Exists(svgIconPath) ? $"{Icons.COUIBaseLocation}/{modName}.svg" : Icons.GetIcon(assetPackPrefab);
            }

            AssetDataPath prefabAssetPath;
            if (importSettings.isAssetPack)
            {
                prefabAssetPath = AssetDataPath.Create(assetDataPath, $"{fullAssetName}{PrefabAsset.kExtension}", EscapeStrategy.None);
            }
            else
            {
                prefabAssetPath = AssetDataPath.Create(EAI.kTempFolderName, fullAssetName + PrefabAsset.kExtension, EscapeStrategy.None);
            }

            PrefabAsset prefabAsset = importSettings.dataBase.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, assetPackPrefab, Colossal.Hash128.CreateGuid(fullAssetName));

            if (importSettings.savePrefabs) prefabAsset.Save();

            MainThreadDispatcher.RunOnMainThread(() => EL.m_PrefabSystem.AddPrefab(assetPackPrefab));

            Dictionary<string, string> localisation = new()
            {
                { $"Assets.NAME[{fullAssetName}]", assetPackJson.PackName },
                { $"Assets.DESCRIPTION[{fullAssetName}]", assetPackJson.PackName }
            };

            ImportersUtils.SetupLocalisationForPrefab(localisation, importSettings, assetDataPath, fullAssetName);

        }

        public override void ExportTemplate(string path)
        {
            AssetPackJson assetPackJson = new AssetPackJson
            {
                PackName = "Asset Pack Name",
            };

            File.WriteAllText(Path.Combine(path, FileName), Encoder.Encode(assetPackJson, EncodeOptions.None));
        }
    }
}
