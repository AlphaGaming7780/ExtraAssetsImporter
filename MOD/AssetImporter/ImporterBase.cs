using Colossal.IO.AssetDatabase;
using Colossal.Localization;
using Colossal.PSI.Common;
using ExtraAssetsImporter.DataBase;
using ExtraLib;
using Game.SceneFlow;
using Game.UI.Menu;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace ExtraAssetsImporter.AssetImporter
{

    public struct ImporterSettings
    {
        internal EAIDatabase eaiDatabase;
        public ILocalAssetDatabase dataBase;
        public bool savePrefabs;
        public bool isAssetPack;
        public string outputFolderOffset;
        public string assetPackName;

        public static ImporterSettings GetDefault()
        {
            ImporterSettings result = default(ImporterSettings);
            result.eaiDatabase = EAIDataBaseManager.eaiDataBase;
            result.dataBase = EAIDataBaseManager.EAIAssetDataBase;
            result.savePrefabs = false;
            result.isAssetPack = false;
            result.outputFolderOffset = "";
            return result;
        }
    }

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
        
        public virtual bool AddCustomAssetsFolder(string path)
        {
            if (_FolderToLoadAssets.Contains(path)) return false;
            _FolderToLoadAssets.Add(path);

            if( this is FolderImporter)
                Icons.LoadIcons(new DirectoryInfo(path).Parent.FullName);
            else if ( this is FileImporter )
                Icons.LoadIcons(new FileInfo(path).Directory.FullName);
            
            return true;
        }

        public virtual void AddCustomAssetsFolder( IEnumerable<string> paths)
        {
            foreach( string path in paths )
                AddCustomAssetsFolder(path);
        }

        //public virtual void RemoveCustomAssetsFolder(string path)
        //{
        //    if (!_FolderToLoadAssets.Contains(path)) return;
        //    _FolderToLoadAssets.Remove(path);
        //    Icons.UnLoadIcons(new DirectoryInfo(path).Parent.FullName);
        //}
        //IEnumerator
        internal virtual void LoadCustomAssets(ImporterSettings importSettings)
        {
            if (AssetsLoading) return; // yield break;

            if (_FolderToLoadAssets.Count <= 0)
            {
                AssetsLoaded = true;
                return;
            }

            AssetsLoading = true;
            AssetsLoaded = false;

            EAI.Logger.Info($"The {ImporterId} importer start to load is custom assets.");

            numberOfAssets = 0;
            ammoutOfAssetsloaded = 0;
            failedAssets = 0;
            skipedAsset = 0;

            var notificationInfo = EL.m_NotificationUISystem.AddOrUpdateNotification(
                importSettings.isAssetPack ? $"{nameof(ExtraAssetsImporter)}.{nameof(LoadCustomAssets)}.{ImporterId}.{importSettings.assetPackName}" : $"{nameof(ExtraAssetsImporter)}.{nameof(LoadCustomAssets)}.{ImporterId}",
                title: importSettings.isAssetPack ? $"{importSettings.assetPackName} - Importing {ImporterId}." : $"EAI - Importing {ImporterId}.",
                progressState: ProgressState.Indeterminate,
                thumbnail: $"{Icons.COUIBaseLocation}/Icons/NotificationInfo/{ImporterId}.svg",
                progress: 0
            );

            foreach (string folder in _FolderToLoadAssets)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string catFolder in Directory.GetDirectories(folder))
                    foreach (string assetsFolder in Directory.GetDirectories(catFolder))
                        if (Directory.GetFiles(assetsFolder).Length > 0)
                            numberOfAssets++;
                        else
                        {
                            try
                            {
                                Directory.Delete(assetsFolder, false);
                            }
                            catch (Exception ex) { EAI.Logger.Warn(ex); }
                        }
            }

            PreLoadCustomAssetFolder(importSettings);

            Dictionary<string, string> csLocalisation = new();

            foreach (string importerFolder in _FolderToLoadAssets)
            {

                string modName = "Unknown";
                if (importSettings.isAssetPack)
                {
                    modName = new DirectoryInfo(importerFolder).Parent.Name;
                } 
                else
                {

                    FileInfo[] fileInfos = new FileInfo[0];

                    // Note: This is a workaround for the fact that the FolderImporter and FileImporter are not compatible with each other.
                    if (this is FolderImporter)
                    {
                        if (!Directory.Exists(importerFolder)) continue;
                        fileInfos = new DirectoryInfo(importerFolder).Parent.GetFiles("*.dll");

                    }
                    else if (this is FileImporter)
                    {
                        if (!File.Exists(importerFolder)) continue;
                        fileInfos = new FileInfo(importerFolder).Directory.GetFiles("*.dll");
                    }

                    modName = fileInfos.Length > 0 ? Path.GetFileNameWithoutExtension(fileInfos[0].Name).Split('_')[0] : new DirectoryInfo(importerFolder).Parent.Name.Split('_')[0];
                }

                //yield return 
                LoadCustomAssetFolder(importSettings, importerFolder, modName, csLocalisation, notificationInfo);
            }

            AfterLoadCustomAssetFolder(importSettings);

            foreach (string localeID in GameManager.instance.localizationManager.GetSupportedLocales())
            {
                GameManager.instance.localizationManager.AddSource(localeID, new MemorySource(csLocalisation));
            }

            EL.m_NotificationUISystem.RemoveNotification(
                identifier: notificationInfo.id,
                delay: 5f,
                text: $"Complete, {numberOfAssets - failedAssets - skipedAsset} Loaded, {failedAssets} failed, {skipedAsset} skipped.",
                progressState: ProgressState.Complete,
                progress: 100
            );

            EAI.Logger.Info($"The {ImporterId} importer finish to load all is assets. {numberOfAssets - failedAssets - skipedAsset} Loaded, {failedAssets} failed, {skipedAsset} skipped.");

            AssetsLoaded = true;
            AssetsLoading = false;
            Reset();
        }

        public virtual void Reset()
        {
            _FolderToLoadAssets.Clear();
        }

        protected virtual void PreLoadCustomAssetFolder(ImporterSettings importSettings) { }
        //IEnumerator
        protected abstract void LoadCustomAssetFolder(ImporterSettings importSettings, string importerFolder, string modName, Dictionary<string, string> localisation, NotificationUISystem.NotificationInfo notificationInfo);
        protected virtual void AfterLoadCustomAssetFolder(ImporterSettings importSettings) { }
        public abstract void ExportTemplate(string path);

    }
}
