using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter
{
    abstract class PrefabImporterBase : FolderImporter
    {
        private UIAssetParentCategoryPrefab assetCat = null;

        virtual public string CatName { get; } = null;

        protected override void PreLoadCustomAssetFolder()
        {
            base.PreLoadCustomAssetFolder();
            assetCat = PrefabsHelper.GetOrCreateUIAssetParentCategoryPrefab( CatName ?? ImporterId );
        }

        protected override IEnumerator LoadCustomAssetFolder(string folder, string modName, Dictionary<string, string> localisation, NotificationUISystem.NotificationInfo notificationInfo)
        {
            foreach (string catFolder in Directory.GetDirectories(folder))
            {
                string catName = new DirectoryInfo(catFolder).Name;
                if (catName.StartsWith("."))
                {
                    continue;
                }

                foreach (string assetFolder in Directory.GetDirectories(catFolder))
                {
                    string assetName = new DirectoryInfo(assetFolder).Name;
                    notificationInfo.progressState = ProgressState.Progressing;
                    notificationInfo.progress = (int)(ammoutOfAssetsloaded / (float)numberOfAssets * 100);
                    notificationInfo.text = $"Loading : {assetName}";
                    EL.m_NotificationUISystem.AddOrUpdateNotification(ref notificationInfo);

                    if (assetName.StartsWith(".")) // || catName.StartsWith(".")
                    {
                        skipedAsset++;
                        //yield return null;
                        continue;
                    }

                    string fullAssetName = $"{modName} {catName} {assetName} {AssetEndName}";
                    string assetDataPath = Path.Combine(FolderName, modName, catName, assetName);

                    ImportData importData = new(assetFolder, assetName, catName, modName, fullAssetName, assetDataPath, assetCat);
                    IEnumerator<PrefabBase> enumerator = null;

                    try
                    {

                        enumerator = Import(importData);

                    }
                    catch (Exception e)
                    {
                        ImportFailed(assetFolder, assetDataPath, e);
                    }

                    bool value = true;
                    while(enumerator.Current == null && value)
                    {
                        yield return null;
                        try
                        {
                            value = enumerator.MoveNext();
                        }
                        catch (Exception e)
                        {
                            ImportFailed(assetFolder, assetDataPath, e);
                        }
                    }

                    if (enumerator.Current == null) yield return null;

                    try
                    {
                        if (enumerator.Current is not PrefabBase prefab) throw new Exception("Import didn't return a PrefabBase.");

                        //PrefabBase prefab = pre
                        prefab.name = importData.FullAssetName;

                        EditorAssetCategoryOverride categoryOverride = prefab.AddComponent<EditorAssetCategoryOverride>();
                        categoryOverride.m_IncludeCategories = new[] { $"EAI/{ImporterId}/{importData.ModName}/{importData.CatName}" };

                        if (AssetPackImporter.TryGetAssetPackPrefab(importData, out AssetPackPrefab assetPackPrefab))
                        {
                            AssetPackItem assetPackItem = prefab.AddComponent<AssetPackItem>();
                            assetPackItem.m_Packs = new[] { assetPackPrefab };
                        }

                        AssetDataPath prefabAssetPath = AssetDataPath.Create("TempAssetsFolder", importData.FullAssetName + PrefabAsset.kExtension, EscapeStrategy.None);
                        EAIDataBaseManager.assetDataBaseEAI.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, prefab, forceGuid: Colossal.Hash128.CreateGuid(importData.FullAssetName));

                        EL.m_PrefabSystem.AddPrefab(prefab);

                        if (!localisation.ContainsKey($"Assets.NAME[{fullAssetName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.NAME[{fullAssetName}]")) localisation.Add($"Assets.NAME[{fullAssetName}]", assetName);
                        if (!localisation.ContainsKey($"Assets.DESCRIPTION[{fullAssetName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.DESCRIPTION[{fullAssetName}]")) localisation.Add($"Assets.DESCRIPTION[{fullAssetName}]", assetName);

                    } 
                    catch (Exception e)
                    {
                        ImportFailed(assetFolder, assetDataPath, e);
                    }

                    ammoutOfAssetsloaded++;
                    yield return null;
                }
            }
        }

        protected abstract IEnumerator<PrefabBase> Import(ImportData data);

        internal void ImportFailed(string assetFolder, string assetDataPath, Exception e)
        {
            failedAssets++;
            EAI.Logger.Error($"Failed to load the custom asset at {assetFolder} | ERROR : {e}");
            string pathToAssetInDatabase = Path.Combine(AssetDataBaseEAI.kRootPath, assetDataPath);
            if (Directory.Exists(pathToAssetInDatabase)) Directory.Delete(pathToAssetInDatabase, true);
        }

    }
}
