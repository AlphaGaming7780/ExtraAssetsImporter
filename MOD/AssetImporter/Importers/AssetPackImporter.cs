using Colossal.Core;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.UI;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.ClassExtension;
using ExtraAssetsImporter.Database;
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
            numberOfAssets++;
            EAI.Logger.Info($"{modName} {AssetEndName}");

            string fullAssetName = $"{modName} {AssetEndName}";

            // AddCustomAssetsFolder (FileImporter) hands us the full path to AssetPack.json itself, so its
            // parent is the mod's own root — the same folder the icon (modName.svg/.png) lives in.
            string sourceFolder = Path.GetDirectoryName(folder);

            string assetDataPath = importSettings.isAssetPack ?
                        Path.Combine(importSettings.outputFolderOffset, modName) :
                        Path.Combine(importSettings.outputFolderOffset, ImporterId, modName);

            int sourceAssetFolderHash = EAIDatabaseManager.GetAssetHash(sourceFolder);
            bool needToUpdateAsset;

            if (importSettings.eaiDatabase.TryGetEAIAsset(fullAssetName, out EAIAsset eaiAsset))
            {
                needToUpdateAsset = eaiAsset.SourceAssetHash != sourceAssetFolderHash || eaiAsset.AssetPath != assetDataPath;
                eaiAsset.SourceAssetHash = sourceAssetFolderHash;
                eaiAsset.AssetPath = assetDataPath;
            }
            else
            {
                eaiAsset = new(fullAssetName, sourceAssetFolderHash, assetDataPath);
                needToUpdateAsset = true;
            }

            string prefabFileName = $"{fullAssetName}{PrefabAsset.kExtension}";

            if (!needToUpdateAsset && ImportersUtils.TryGetCachedPrefab(importSettings, assetDataPath, fullAssetName, prefabFileName, out AssetPackPrefab cachedAssetPackPrefab))
            {
                eaiAsset.PrefabID = cachedAssetPackPrefab.GetPrefabID().ToString();
                importSettings.eaiDatabase.AddOrValidateAsset(eaiAsset);
                ammoutOfAssetsloaded++;
                return;
            }

            AssetPackJson assetPackJson = LoadJSON(folder);

            AssetPackPrefab assetPackPrefab = ImportersUtils.GetOrCreatePrefabInstance<AssetPackPrefab>(fullAssetName);
            assetPackPrefab.name = fullAssetName;

            UIObject assetPackUI = assetPackPrefab.AddOrGetComponent<UIObject>();

            string svgIconPath = Path.Combine(sourceFolder, $"{modName}.svg"); // Doesn't work with SVG, fuck.
            string pngIconPath = Path.Combine(sourceFolder, $"{modName}.png"); // Doesn't work with SVG, fuck.

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


            AssetDataPath prefabAssetPath = AssetDataPath.Create(assetDataPath, prefabFileName, true, EscapeStrategy.None);
            PrefabAsset prefabAsset = importSettings.dataBase.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, assetPackPrefab, Colossal.Hash128.CreateGuid(fullAssetName));

            eaiAsset.PrefabID = assetPackPrefab.GetPrefabID().ToString();

            if(importSettings.savePrefabs) prefabAsset.Save();

            MainThreadDispatcher.RunOnMainThread(() => EL.m_PrefabSystem.AddOrUpdatePrefab(assetPackPrefab));

            Dictionary<string, string> localisation = new()
            {
                { $"Assets.NAME[{fullAssetName}]", assetPackJson.PackName },
                { $"Assets.DESCRIPTION[{fullAssetName}]", assetPackJson.PackName }
            };

            ImportersUtils.SetupLocalisationForPrefab(localisation, importSettings, assetDataPath, fullAssetName);

            importSettings.eaiDatabase.AddOrValidateAsset(eaiAsset);
            ammoutOfAssetsloaded++;
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
