using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Colossal.Localization;
using Colossal.PSI.Common;
using ExtraLib;
using ExtraLib.Prefabs;
using Game.SceneFlow;
using Game.UI.Menu;

namespace ExtraAssetsImporter.AssetImporter
{
    abstract class ImporterBase
    {

        public abstract string ImporterId { get; }

        public abstract string AssetEndName { get; }
        public virtual bool PreImporter { get; } = false;

        internal List<string> _FolderToLoadAssets = new();
        private bool AssetsLoading = false;
        internal bool AssetsLoaded = false;

        protected int numberOfAssets;
        protected int ammoutOfAssetsloaded;
        protected int failedAssets;
        protected int skipedAsset;
        
        public virtual void AddCustomAssetsFolder(string path)
        {
            if (_FolderToLoadAssets.Contains(path)) return;
            _FolderToLoadAssets.Add(path);
            Icons.LoadIcons(new DirectoryInfo(path).Parent.FullName);
        }

        public virtual void RemoveCustomAssetsFolder(string path)
        {
            if (!_FolderToLoadAssets.Contains(path)) return;
            _FolderToLoadAssets.Remove(path);
            Icons.UnLoadIcons(new DirectoryInfo(path).Parent.FullName);
        }

        internal virtual IEnumerator LoadCustomAssets()
        {
            if (AssetsLoading) yield break;

            if (_FolderToLoadAssets.Count <= 0)
            {
                AssetsLoaded = true;
                yield break;
            }

            AssetsLoading = true;
            AssetsLoaded = false;

            EAI.Logger.Info($"The {ImporterId} importer start to load is custom assets.");

            numberOfAssets = 0;
            ammoutOfAssetsloaded = 0;
            failedAssets = 0;
            skipedAsset = 0;

            var notificationInfo = EL.m_NotificationUISystem.AddOrUpdateNotification(
                $"{nameof(ExtraAssetsImporter)}.{nameof(EAI)}.{nameof(LoadCustomAssets)}.{ImporterId}",
                title: $"EAI, Importing {ImporterId}.",
                progressState: ProgressState.Indeterminate,
                thumbnail: $"{Icons.COUIBaseLocation}/Icons/NotificationInfo/{ImporterId}.svg",
                progress: 0
            );

            foreach (string folder in _FolderToLoadAssets)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string catFolder in Directory.GetDirectories(folder))
                    foreach (string assetsFolder in Directory.GetDirectories(catFolder))
                        numberOfAssets++;
            }

            PreLoadCustomAssetFolder();

            Dictionary<string, string> csLocalisation = new();

            foreach (string folder in _FolderToLoadAssets)
            {

                FileInfo[] fileInfos = new FileInfo[0];

                // Note: This is a workaround for the fact that the FolderImporter and FileImporter are not compatible with each other.
                if (this is FolderImporter)
                {
                    if(!Directory.Exists(folder)) continue;
                    fileInfos = new DirectoryInfo(folder).Parent.GetFiles("*.dll");

                } else if (this is FileImporter)
                {
                    if(!File.Exists(folder)) continue;
                    fileInfos = new FileInfo(folder).Directory.GetFiles("*.dll");
                }

                string modName = fileInfos.Length > 0 ? Path.GetFileNameWithoutExtension(fileInfos[0].Name).Split('_')[0] : new DirectoryInfo(folder).Parent.Name.Split('_')[0];

                yield return LoadCustomAssetFolder(folder, modName, csLocalisation, notificationInfo);
            }

            AfterLoadCustomAssetFolder();

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

        protected virtual void PreLoadCustomAssetFolder() { }
        protected abstract IEnumerator LoadCustomAssetFolder(string folder, string modName, Dictionary<string, string> localisation, NotificationUISystem.NotificationInfo notificationInfo);
        protected virtual void AfterLoadCustomAssetFolder() { }

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
