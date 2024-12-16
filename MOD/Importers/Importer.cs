using Colossal.Localization;
using Colossal.PSI.Common;
using Extra.Lib;
using Extra.Lib.UI;
using Extra.Lib.mod.ClassExtension;
using ExtraAssetsImporter.DataBase;
using Game.Prefabs;
using Game.SceneFlow;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Colossal.IO.AssetDatabase;

namespace ExtraAssetsImporter.Importers
{
    internal abstract class Importer
    {
        internal List<string> folderToLoad = [];
        public bool isLoading = false;
        public bool isLoaded = false;

        public abstract string AssetNameID { get; }
        public abstract string AssetType { get; }

        public void AddFolder(string path)
        {
            if (folderToLoad.Contains(path)) return;
            folderToLoad.Add(path);
            Icons.LoadIcons(new DirectoryInfo(path).Parent.FullName);
        }

        public void RemoveFolder(string path)
        {
            if (!folderToLoad.Contains(path)) return;
            folderToLoad.Remove(path);
            Icons.UnLoadIcons(new DirectoryInfo(path).Parent.FullName);
        }


        internal IEnumerator CreateCustomAssets()
        {
            if (isLoading || folderToLoad.Count <= 0 || isLoaded) yield break;

            isLoading = true;

            int numberOfAsset = 0;
            int ammoutOfAssetloaded = 0;
            int failedAsset = 0;
            int notLoadedAsset = 0;

            var notificationInfo = ExtraLib.m_NotificationUISystem.AddOrUpdateNotification(
                $"{nameof(ExtraAssetsImporter)}.{nameof(EAI)}.{nameof(CreateCustomAssets)}.{AssetType}",
                title: $"EAI, Importing the custom assets : {AssetType}.",
                progressState: ProgressState.Indeterminate,
                thumbnail: $"{Icons.COUIBaseLocation}/Icons/NotificationInfo/{AssetType}.svg",
                progress: 0
            );

            foreach (string folder in folderToLoad)
                foreach (string catFolder in Directory.GetDirectories(folder))
                    foreach (string assetsFolder in Directory.GetDirectories(catFolder))
                        numberOfAsset++;

            ExtraAssetsMenu.AssetCat assetCat = ExtraAssetsMenu.GetOrCreateNewAssetCat(AssetType, $"{Icons.COUIBaseLocation}/Icons/UIAssetCategoryPrefab/{AssetType}.svg");

            Dictionary<string, string> csLocalisation = [];

            foreach (string folder in folderToLoad)
            {
                foreach (string catFolder in Directory.GetDirectories(folder))
                {
                    foreach (string assetsFolder in Directory.GetDirectories(catFolder))
                    {
                        string assetName = new DirectoryInfo(assetsFolder).Name;
                        notificationInfo.progressState = ProgressState.Progressing;
                        notificationInfo.progress = (int)(ammoutOfAssetloaded / (float)numberOfAsset * 100);
                        notificationInfo.text = $"Loading : {assetName}";
                        ExtraLib.m_NotificationUISystem.AddOrUpdateNotification(ref notificationInfo);

                        if (assetName.StartsWith("."))
                        {
                            notLoadedAsset++;
                            continue;
                        }

                        string catName = new DirectoryInfo(catFolder).Name;
                        FileInfo[] fileInfos = new DirectoryInfo(folder).Parent.GetFiles("*.dll");
                        string modName = fileInfos.Length > 0 ? Path.GetFileNameWithoutExtension(fileInfos[0].Name).Split('_')[0] : new DirectoryInfo(folder).Parent.Name.Split('_')[0];
                        string fullAssetName = $"{modName} {catName} {assetName} {AssetNameID}";
                        string assetCachePath = $"Custom{AssetType}\\{modName}\\{catName}\\{assetName}";

                        try
                        {
                            CreateCustomAsset(assetsFolder, assetName, catName, modName, fullAssetName, assetCachePath, assetCat);

                            if (!csLocalisation.ContainsKey($"Assets.NAME[{fullAssetName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.NAME[{fullAssetName}]")) csLocalisation.Add($"Assets.NAME[{fullAssetName}]", assetName);
                            if (!csLocalisation.ContainsKey($"Assets.DESCRIPTION[{fullAssetName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.DESCRIPTION[{fullAssetName}]")) csLocalisation.Add($"Assets.DESCRIPTION[{fullAssetName}]", assetName);
                        }
                        catch (Exception e)
                        {
                            failedAsset++;
                            Directory.Delete(Path.Combine(AssetDataBaseEAI.rootPath, assetCachePath));
                            EAI.Logger.Error($"Failed to load the custom asset at {assetsFolder} | ERROR : {e}");
                        }
                        ammoutOfAssetloaded++;
                        yield return null;
                    }
                }
            }

            foreach (string localeID in GameManager.instance.localizationManager.GetSupportedLocales())
            {
                GameManager.instance.localizationManager.AddSource(localeID, new MemorySource(csLocalisation));
            }

            ExtraLib.m_NotificationUISystem.RemoveNotification(
                identifier: notificationInfo.id,
                delay: 5f,
                text: $"Complete, {numberOfAsset - failedAsset} Loaded, {failedAsset} failed, {notLoadedAsset} skiped.",
                progressState: ProgressState.Complete,
                progress: 100
            );

            //LoadLocalization();
            isLoaded = true;
            isLoading = false;
        }

        internal abstract void CreateCustomAsset(string assetFolder, string assetName, string catName, string modName, string fullAssetName, string assetCachePath, ExtraAssetsMenu.AssetCat assetCat);

        internal SurfaceAsset CreateSurfaceAsset()
        {

        }


    }
}
