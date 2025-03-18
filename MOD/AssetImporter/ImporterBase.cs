using Colossal.IO.AssetDatabase;
using Colossal.Localization;
using Colossal.PSI.Common;
using ExtraAssetsImporter.DataBase;
using ExtraLib;
using ExtraLib.ClassExtension;
using ExtraLib.Helpers;
using ExtraLib.Prefabs;
using Game.Prefabs;
using Game.SceneFlow;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter
{
    abstract class ImporterBase
    {

        public abstract string ImporterId { get; }
        public abstract string FolderName { get; }
        public abstract string AssetEndName { get; }

        internal List<string> FolderToLoadAssets = [];
        private bool AssetsLoading = false;
        internal bool AssetsLoaded = false;

        public void AddCustomAssetsFolder(string path)
        {
            if (FolderToLoadAssets.Contains(path)) return;
            FolderToLoadAssets.Add(path);
            Icons.LoadIcons(new DirectoryInfo(path).Parent.FullName);
        }

        public void RemoveCustomAssetsFolder(string path)
        {
            if (!FolderToLoadAssets.Contains(path)) return;
            FolderToLoadAssets.Remove(path);
            Icons.UnLoadIcons(new DirectoryInfo(path).Parent.FullName);
        }

        internal IEnumerator CreateCustomAssets()
        {
            if (AssetsLoading) yield break;

            if (FolderToLoadAssets.Count <= 0)
            {
                AssetsLoaded = true;
                yield break;
            }

            AssetsLoading = true;
            AssetsLoaded = false;

            EAI.Logger.Info($"The {ImporterId} importer start to load is custom assets.");

            int numberOfAssets = 0;
            int ammoutOfAssetsloaded = 0;
            int failedAssets = 0;
            int skipedAsset = 0;

            var notificationInfo = EL.m_NotificationUISystem.AddOrUpdateNotification(
                $"{nameof(ExtraAssetsImporter)}.{nameof(EAI)}.{nameof(CreateCustomAssets)}.{ImporterId}",
                title: $"EAI, Importing the custom {ImporterId}.",
                progressState: ProgressState.Indeterminate,
                thumbnail: $"{Icons.COUIBaseLocation}/Icons/NotificationInfo/{ImporterId}.svg",
                progress: 0
            );

            foreach (string folder in FolderToLoadAssets)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string catFolder in Directory.GetDirectories(folder))
                    foreach (string assetsFolder in Directory.GetDirectories(catFolder))
                        numberOfAssets++;
            }

            UIAssetParentCategoryPrefab assetCat = PrefabsHelper.GetOrCreateUIAssetParentCategoryPrefab(ImporterId);

            Dictionary<string, string> csLocalisation = [];

            foreach (string folder in FolderToLoadAssets)
            {
                if (!Directory.Exists(folder)) continue;
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
                        FileInfo[] fileInfos = new DirectoryInfo(folder).Parent.GetFiles("*.dll");
                        string modName = fileInfos.Length > 0 ? Path.GetFileNameWithoutExtension(fileInfos[0].Name).Split('_')[0] : new DirectoryInfo(folder).Parent.Name.Split('_')[0];
                        string fullAssetName = $"{modName} {catName} {assetName} {AssetEndName}";
                        string assetDataPath = Path.Combine(FolderName, modName, catName, assetName);

                        try
                        {

                            ImportData importData = new(assetFolder, assetName, catName, modName, fullAssetName, assetDataPath, assetCat);

                            PrefabBase prefab = Import(importData);
                            prefab.name = importData.FullAssetName;

                            AssetDataPath prefabAssetPath = AssetDataPath.Create("TempAssetsFolder", importData.FullAssetName + PrefabAsset.kExtension, EscapeStrategy.None);
                            EAIDataBaseManager.assetDataBaseEAI.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, prefab, forceGuid: Colossal.Hash128.CreateGuid(importData.FullAssetName));

                            EL.m_PrefabSystem.AddPrefab(prefab);

                            if (!csLocalisation.ContainsKey($"Assets.NAME[{fullAssetName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.NAME[{fullAssetName}]")) csLocalisation.Add($"Assets.NAME[{fullAssetName}]", assetName);
                            if (!csLocalisation.ContainsKey($"Assets.DESCRIPTION[{fullAssetName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.DESCRIPTION[{fullAssetName}]")) csLocalisation.Add($"Assets.DESCRIPTION[{fullAssetName}]", assetName);
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

            foreach (string localeID in GameManager.instance.localizationManager.GetSupportedLocales())
            {
                GameManager.instance.localizationManager.AddSource(localeID, new MemorySource(csLocalisation));
            }

            EL.m_NotificationUISystem.RemoveNotification(
                identifier: notificationInfo.id,
                delay: 5f,
                text: $"Complete, {numberOfAssets - failedAssets} Loaded, {failedAssets} failed, {skipedAsset} skipped.",
                progressState: ProgressState.Complete,
                progress: 100
            );

            EAI.Logger.Info($"The {ImporterId} importer finish to load all is assets. {numberOfAssets - failedAssets} Loaded, {failedAssets} failed, {skipedAsset} skipped.");

            AssetsLoaded = true;
            AssetsLoading = false;
        }

        protected abstract PrefabBase Import(ImportData data);

    }


    struct ImportData
    {

        public ImportData(string folderPath, string assetName, string catName, string modName, string fullAssetName, string assetDataPath, UIAssetParentCategoryPrefab assetCat)
        {
            this.FolderPath = folderPath;
            this.AssetName = assetName;
            this.CatName = catName;
            this.ModName = modName;
            this.FullAssetName = fullAssetName;
            this.AssetDataPath = assetDataPath;
            this.AssetCat = assetCat;
        }

        public string FolderPath { get; private set; }
        public string AssetName { get; private set; }
        public string CatName { get; private set; }
        public string ModName { get; private set; }
        public string FullAssetName { get; private set; }
        public string AssetDataPath { get; private set; }
        public UIAssetParentCategoryPrefab AssetCat { get; private set; }

        //public Mesh[] meshes;
        //public Surface surface;
    }
}
