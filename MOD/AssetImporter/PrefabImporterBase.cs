using Colossal.IO.AssetDatabase;
using Colossal.PSI.Common;
using ExtraAssetsImporter.AssetImporter.Importers;
using ExtraAssetsImporter.DataBase;
using ExtraLib;
using ExtraLib.ClassExtension;
using ExtraLib.Helpers;
using ExtraLib.Prefabs;
using Game.Prefabs;
using Game.SceneFlow;
using Game.UI.Menu;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter
{
    abstract class PrefabImporterBase : ImporterBase
    {
        public override string ImporterId => throw new NotImplementedException();

        public override string FolderName => throw new NotImplementedException();

        public override string AssetEndName => throw new NotImplementedException();

        private UIAssetParentCategoryPrefab assetCat = null;

        protected override void PreLoadCustomAssetFolder()
        {
            base.PreLoadCustomAssetFolder();
            assetCat = PrefabsHelper.GetOrCreateUIAssetParentCategoryPrefab(ImporterId);
        }

        protected override IEnumerator LoadCustomAssetFolder(string folder, string modName, Dictionary<string, string> localisation, NotificationUISystem.NotificationInfo notificationInfo)
        {
            foreach (string catFolder in Directory.GetDirectories(folder))
            {
                foreach (string assetFolder in Directory.GetDirectories(catFolder))
                {
                    string assetName = new DirectoryInfo(assetFolder).Name;
                    notificationInfo.progressState = ProgressState.Progressing;
                    notificationInfo.progress = (int)(ammoutOfAssetsloaded / (float)numberOfAssets * 100);
                    notificationInfo.text = $"Loading : {assetName}";
                    EL.m_NotificationUISystem.AddOrUpdateNotification(ref notificationInfo);

                    if (assetName.StartsWith("."))
                    {
                        skipedAsset++;
                        continue;
                    }

                    string catName = new DirectoryInfo(catFolder).Name;
                    string fullAssetName = $"{modName} {catName} {assetName} {AssetEndName}";
                    string assetDataPath = Path.Combine(FolderName, modName, catName, assetName);

                    try
                    {

                        ImportData importData = new(assetFolder, assetName, catName, modName, fullAssetName, assetDataPath, assetCat);

                        PrefabBase prefab = Import(importData);
                        prefab.name = importData.FullAssetName;

                        EditorAssetCategoryOverride categoryOverride = prefab.AddComponent<EditorAssetCategoryOverride>();
                        categoryOverride.m_IncludeCategories = [$"EAI/{ImporterId}/{importData.ModName}/{importData.CatName}"];

                        if (AssetPackImporter.TryGetAssetPackPrefab(importData, out AssetPackPrefab assetPackPrefab))
                        {
                            AssetPackItem assetPackItem = prefab.AddComponent<AssetPackItem>();
                            assetPackItem.m_Packs = [assetPackPrefab];
                        }

                        AssetDataPath prefabAssetPath = AssetDataPath.Create("TempAssetsFolder", importData.FullAssetName + PrefabAsset.kExtension, EscapeStrategy.None);
                        EAIDataBaseManager.assetDataBaseEAI.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, prefab, forceGuid: Colossal.Hash128.CreateGuid(importData.FullAssetName));

                        EL.m_PrefabSystem.AddPrefab(prefab);

                        if (!localisation.ContainsKey($"Assets.NAME[{fullAssetName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.NAME[{fullAssetName}]")) localisation.Add($"Assets.NAME[{fullAssetName}]", assetName);
                        if (!localisation.ContainsKey($"Assets.DESCRIPTION[{fullAssetName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.DESCRIPTION[{fullAssetName}]")) localisation.Add($"Assets.DESCRIPTION[{fullAssetName}]", assetName);
                    }
                    catch (Exception e)
                    {
                        failedAssets++;
                        EAI.Logger.Error($"Failed to load the custom asset at {assetFolder} | ERROR : {e}");
                        string pathToAssetInDatabase = Path.Combine(AssetDataBaseEAI.kRootPath, assetDataPath);
                        if (Directory.Exists(pathToAssetInDatabase)) Directory.Delete(pathToAssetInDatabase, true);
                    }
                    ammoutOfAssetsloaded++;
                    yield return null;
                }
            }
        }

        protected abstract PrefabBase Import(ImportData data);

    }
}
