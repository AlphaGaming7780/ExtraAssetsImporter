using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.UI;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.ClassExtension;
using ExtraAssetsImporter.Database;
using ExtraLib;
using ExtraLib.Helpers;
using Game.Prefabs;
using Game.SceneFlow;
using Game.UI.Menu;
using System.Collections.Concurrent;
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

        // Filled during the pre-import pass so the importers (running on their own Tasks afterwards) can
        // resolve their mod's pack without a main-thread round trip per asset.
        private static readonly ConcurrentDictionary<string, AssetPackPrefab> s_AssetPacks = new();

        private AssetPackJson LoadJSON(string path)
        {

            AssetPackJson assetPackJson = new();
            if (File.Exists(path))
            {
                assetPackJson = Decoder.Decode(File.ReadAllText(path)).Make<AssetPackJson>();
            }
            return assetPackJson;
        }

        // A pack's AssetPath starts with ImporterId, and its mod's folder holds AssetPack.json.
        protected override string GetScannedFolder(string importerFolder)
        {
            return Path.Combine(Path.GetDirectoryName(importerFolder), ImporterId);
        }

        protected override void PreLoadCustomAssetFolder(ImporterSettings importSettings)
        {
            // A pack whose mod was removed since the last pass must not be handed out anymore.
            s_AssetPacks.Clear();
        }

        public static bool TryGetAssetPackPrefab( PrefabImportData data, out AssetPackPrefab assetPackPrefab )
        {
            if (s_AssetPacks.TryGetValue(data.ModName, out assetPackPrefab)) return true;

            string fullAssetName = $"{data.ModName} {kAssetEndName}";
            if (ImportersUtils.TryGetPrefab(new PrefabID(nameof(AssetPackPrefab), fullAssetName, Colossal.Hash128.CreateGuid(fullAssetName)), out PrefabBase prefab)
                && prefab is AssetPackPrefab assetPack)
            {
                assetPackPrefab = s_AssetPacks.GetOrAdd(data.ModName, assetPack);
                return true;
            }

            assetPackPrefab = null;
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

            string assetDataPath = Path.Combine(ImporterId, modName);

            int sourceAssetFolderHash = EAIDatabaseManager.GetAssetHash(sourceFolder);
            bool needToUpdateAsset;

            if (importSettings.eaiDatabase.TryGetEAIAsset(fullAssetName, out EAIAsset eaiAsset))
            {
                needToUpdateAsset = eaiAsset.SourceAssetHash != sourceAssetFolderHash || eaiAsset.AssetPath != assetDataPath;
                eaiAsset.SourceAssetHash = sourceAssetFolderHash;
                eaiAsset.AssetPath = assetDataPath;
                eaiAsset.SourceModPath = sourceFolder;
            }
            else
            {
                eaiAsset = new(fullAssetName, sourceAssetFolderHash, assetDataPath, sourceModPath: sourceFolder);
                needToUpdateAsset = true;
            }

            string prefabFileName = $"{fullAssetName}{PrefabAsset.kExtension}";

            if (!needToUpdateAsset && ImportersUtils.TryGetCachedPrefab(importSettings, assetDataPath, fullAssetName, prefabFileName, out AssetPackPrefab cachedAssetPackPrefab))
            {
                // A prefab ID holds the prefab's name, which only the main thread may read.
                eaiAsset.PrefabID = MainThreadHelper.RunOnMainThread(() => cachedAssetPackPrefab.GetPrefabID().ToString()).Result;
                s_AssetPacks[modName] = cachedAssetPackPrefab;
                importSettings.eaiDatabase.AddOrValidateAsset(eaiAsset);
                ammoutOfAssetsloaded++;
                return;
            }

            AssetPackJson assetPackJson = LoadJSON(folder);

            AssetPackPrefab assetPackPrefab = ImportersUtils.GetOrCreatePrefabInstance<AssetPackPrefab>(fullAssetName, typeof(UIObject));

            UIObject assetPackUI = assetPackPrefab.AddOrGetComponentOnMainThread<UIObject>();

            string svgIconPath = Path.Combine(sourceFolder, $"{modName}.svg");

            AssetDataPath prefabAssetPath = AssetDataPath.Create(assetDataPath, prefabFileName, true, EscapeStrategy.None);
            PrefabAsset prefabAsset = importSettings.dataBase.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, assetPackPrefab, Colossal.Hash128.CreateGuid(fullAssetName));

            // The default icon, the save, and the prefab ID read the prefab's name, which only the
            // main thread may read.
            eaiAsset.PrefabID = MainThreadHelper.RunOnMainThread(() =>
            {
                assetPackUI.m_Icon = File.Exists(svgIconPath) ? $"{Icons.COUIBaseLocation}/{modName}.svg" : Icons.GetIcon(assetPackPrefab);
                if (importSettings.savePrefabs) prefabAsset.Save();
                return assetPackPrefab.GetPrefabID().ToString();
            }).Result;

            TrackMainThreadTask(MainThreadHelper.RunOnMainThread(() => EL.m_PrefabSystem.AddOrUpdatePrefab(assetPackPrefab)));
            s_AssetPacks[modName] = assetPackPrefab;

            Dictionary<string, string> localisation = new()
            {
                { $"Assets.NAME[{fullAssetName}]", assetPackJson.PackName },
                { $"Assets.DESCRIPTION[{fullAssetName}]", assetPackJson.PackName }
            };

            TrackMainThreadTask(ImportersUtils.SetupLocalisationForPrefab(localisation));

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
