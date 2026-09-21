using Colossal;
using Colossal.IO.AssetDatabase;
using Colossal.Localization;
using Colossal.PSI.Common;
using ExtraAssetsImporter.Database;
using ExtraLib;
using ExtraLib.ClassExtension;
using Game.SceneFlow;
using Game.UI.Menu;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ExtraAssetsImporter.AssetImporter
{

    public struct ImporterSettings
    {
        internal EAIDatabase eaiDatabase;
        public ILocalAssetDatabase dataBase;
        public bool savePrefabs;

        public static ImporterSettings GetDefault()
        {
            ImporterSettings result = default(ImporterSettings);
            result.eaiDatabase = EAIDatabaseManager.eaiDatabase;
            result.dataBase = EAIDatabaseManager.EAIAssetDatabase;
            result.savePrefabs = EAI.m_Setting.CanSavePrefabs;
            return result;
        }
    }

    abstract class ImporterBase
    {
        public abstract string ImporterId { get; }

        public abstract string AssetEndName { get; }
        public virtual bool PreImporter { get; } = false;

        internal List<string> _FolderToLoadAssets = new();
        // Written by the import Task, read by the main thread (settings UI / AreImportersFinished).
        private volatile bool AssetsLoading = false;
        internal volatile bool AssetsLoaded = false;

        // Main-thread work queued during the import without blocking on it (AddOrUpdatePrefab, AddSource,
        // notification updates). MainThreadDispatcher runs them in no guaranteed order, so they are all
        // awaited before the importer reports itself as loaded — this also surfaces their exceptions.
        private readonly ConcurrentQueue<Task> _PendingMainThreadTasks = new();

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

        internal void LoadCustomAssets(ImporterSettings importSettings)
        {
            if (AssetsLoading) return; // yield break;

            if (_FolderToLoadAssets.Count <= 0)
            {
                AssetsLoaded = true;
                return;
            }

            AssetsLoading = true;
            AssetsLoaded = false;

            EAI.Logger.Info($"The {ImporterId} importer starts loading its custom assets.");

            PerformanceCounter performanceCounter = new();

            numberOfAssets = 0;
            ammoutOfAssetsloaded = 0;
            failedAssets = 0;
            skipedAsset = 0;

            // NotificationUISystem uses a plain Dictionary/List, so it must only be touched on the main thread.
            var notificationInfo = EL.m_NotificationUISystem.AddOrUpdateNotificationOnMainThread(
                $"{nameof(ExtraAssetsImporter)}.{nameof(LoadCustomAssets)}.{ImporterId}",
                title: $"EAI - Importing {ImporterId}.",
                progressState: ProgressState.Indeterminate,
                thumbnail: $"{Icons.COUIBaseLocation}/Icons/NotificationInfo/{ImporterId}.svg",
                progress: 0
            ).Result;

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

            foreach (string importerFolder in _FolderToLoadAssets)
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

                string modName = fileInfos.Length > 0 ? Path.GetFileNameWithoutExtension(fileInfos[0].Name).Split('_')[0] : new DirectoryInfo(importerFolder).Parent.Name.Split('_')[0];

                LoadCustomAssetFolder(importSettings, importerFolder, modName, notificationInfo);
            }

            AfterLoadCustomAssetFolder(importSettings);

            WaitPendingMainThreadTasks();

            TimeSpan elapsed = performanceCounter.result;
            double msPerAsset = numberOfAssets > 0 ? elapsed.TotalMilliseconds / numberOfAssets : 0;

            EL.m_NotificationUISystem.RemoveNotificationOnMainThread(
                identifier: notificationInfo.id,
                delay: 5f,
                text: $"Complete, {numberOfAssets - failedAssets - skipedAsset} Loaded, {failedAssets} failed, {skipedAsset} skipped. Took {elapsed.TotalSeconds:F2}s ({msPerAsset:F1}ms/asset).",
                progressState: ProgressState.Complete,
                progress: 100
            ).Wait();

            EAI.Logger.Info($"The {ImporterId} importer finished loading all its assets. {numberOfAssets - failedAssets - skipedAsset} Loaded, {failedAssets} failed, {skipedAsset} skipped. Took {elapsed.TotalSeconds:F2}s ({msPerAsset:F1}ms/asset).");

            AssetsLoaded = true;
            AssetsLoading = false;
            Reset();
        }

        public virtual void Reset()
        {
            _FolderToLoadAssets.Clear();
        }

        /// <summary>
        /// Queues main-thread work without blocking the import on it. Everything queued here is awaited
        /// before this importer is marked as loaded.
        /// </summary>
        protected void TrackMainThreadTask(Task task)
        {
            _PendingMainThreadTasks.Enqueue(task);
        }

        protected void UpdateNotificationOnMainThread(NotificationUISystem.NotificationInfo notificationInfo, string text, int progress)
        {
            TrackMainThreadTask(EL.m_NotificationUISystem.AddOrUpdateNotificationOnMainThread(notificationInfo.id, text: text, progressState: ProgressState.Progressing, progress: progress));
        }

        private void WaitPendingMainThreadTasks()
        {
            while (_PendingMainThreadTasks.TryDequeue(out Task task))
            {
                try
                {
                    task.Wait();
                }
                catch (AggregateException e)
                {
                    EAI.Logger.Error($"The {ImporterId} importer had a main thread task fail: {e.Flatten().InnerException}");
                }
            }
        }

        protected virtual void PreLoadCustomAssetFolder(ImporterSettings importSettings) { }
        //IEnumerator
        protected abstract void LoadCustomAssetFolder(ImporterSettings importSettings, string importerFolder, string modName, NotificationUISystem.NotificationInfo notificationInfo);
        protected virtual void AfterLoadCustomAssetFolder(ImporterSettings importSettings) { }
        public abstract void ExportTemplate(string path);

    }
}
