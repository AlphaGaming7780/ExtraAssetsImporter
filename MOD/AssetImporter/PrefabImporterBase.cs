using Colossal.Core;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.Localization;
using Colossal.PSI.Common;
using ExtraAssetsImporter.AssetImporter.Importers;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.Database;
using ExtraLib;
using ExtraLib.ClassExtension;
using ExtraLib.Debugger;
using ExtraLib.Helpers;
using ExtraLib.Prefabs;
using Game.Prefabs;
using Game.SceneFlow;
using Game.UI.Menu;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

using Hash128 = Colossal.Hash128;

namespace ExtraAssetsImporter.AssetImporter
{
    abstract class PrefabImporterBase : FolderImporter
    {
        // static: DecalsImporterNew/SurfacesImporterNew/NetLanesDecalImporterNew are 3 separate instances
        // (AssetsImporterManager.AddImporter<T> does `new T()` once per importer), each running its own
        // LoadCustomAssetFolder on its own Task.Run thread. An instance field here would give each one its
        // own lock — protecting nothing, since the race is precisely between those 3 threads. Has to be one
        // lock shared by all of them.
        private static readonly object PrefabSaveLock = new object();
        public const string PrefabJsonName = "Prefab.json";
        private UIAssetParentCategoryPrefab assetCat = null;

        virtual public string CatName { get; } = null;

        protected override void PreLoadCustomAssetFolder(ImporterSettings importSettings)
        {
            base.PreLoadCustomAssetFolder(importSettings);
            using PerformanceStats.Scope blocking = Perf.Measure("Blocking.ParentCategory");
            assetCat = MainThreadHelper.RunOnMainThread(() =>
            {
                UIAssetParentCategoryPrefab category = PrefabsHelper.GetOrCreateUIAssetParentCategoryPrefab(CatName ?? ImporterId);
                ImportersUtils.EnsurePrefabHasAsset(category);
                ImportersUtils.EnsurePrefabHasAsset(category.parentCategoryOrMenu);
                return category;
            }).Result;
        }

        protected override void LoadCustomAssetFolder(ImporterSettings importSettings, string importerFolder, string modName, NotificationUISystem.NotificationInfo notificationInfo)
        {
            string sourceModPath = Directory.GetParent(importerFolder)?.FullName;

            if (string.Equals(sourceModPath, EAI.pathModsData, StringComparison.OrdinalIgnoreCase))
                sourceModPath = null;

            foreach (string catFolder in Directory.GetDirectories(importerFolder))
            {
                string catName = new DirectoryInfo(catFolder).Name;
                if (catName.StartsWith("."))
                {
                    int num = Directory.GetDirectories(catFolder).Length;
                    skipedAsset += num;
                    ammoutOfAssetsloaded += num;
                    continue;
                }

                foreach (string assetFolder in Directory.GetDirectories(catFolder))
                {
                    using PerformanceStats.Scope assetScope = Perf.Measure("Asset.Total");

                    string assetName = new DirectoryInfo(assetFolder).Name;
                    UpdateNotificationOnMainThread(notificationInfo, $"Loading : {assetName}", (int)(ammoutOfAssetsloaded / (float)numberOfAssets * 100));

                    if (assetName.StartsWith("."))
                    {
                        skipedAsset++;
                        ammoutOfAssetsloaded++;
                        continue;
                    }

                    string fullAssetName = $"{modName} {catName} {assetName} {AssetEndName}";

                    string assetDataPath = Path.Combine(FolderName, modName, catName, assetName);

                    string fullAssetDataPath = Path.Combine(importSettings.dataBase.rootPath, assetDataPath);

                    string prefabJsonPath = Path.Combine(assetFolder, PrefabJsonName);
                    Variant prefabJson = null;
                    if (File.Exists(prefabJsonPath))
                        using (Perf.Measure("Asset.ReadPrefabJson"))
                            prefabJson = ImportersUtils.LoadJson(prefabJsonPath);

                    int sourceAssetFolderHash;
                    using (Perf.Measure("Asset.SourceHash"))
                        sourceAssetFolderHash = EAIDatabaseManager.GetAssetHash(assetFolder);
                    bool needToUpdateAsset = false;

                    if (importSettings.eaiDatabase.TryGetEAIAsset(fullAssetName, out EAIAsset eaiAsset))
                    {
                        eaiAsset.SourceModPath = sourceModPath;

                        if(eaiAsset.AssetPath != assetDataPath)
                        {
                            EAI.Logger.Warn($"EAI asset {eaiAsset.AssetID} doesn't have the right path, old path {eaiAsset.AssetPath}, new path {assetDataPath}");

                            string fullPath = Path.Combine(importSettings.dataBase.rootPath, eaiAsset.AssetPath);
                            if (Directory.Exists(fullPath))
                            {
                                Directory.Delete(fullPath, true);
                            }

                            needToUpdateAsset = true;
                            eaiAsset.AssetPath = assetDataPath;
                        }

                        if (eaiAsset.SourceAssetHash != sourceAssetFolderHash)
                        {
                            EAI.Logger.Info($"The asset {fullAssetName} source files have changed, updating it. Old hash {eaiAsset.SourceAssetHash}, new hash {sourceAssetFolderHash}.");
                            needToUpdateAsset = true;
                            eaiAsset.SourceAssetHash = sourceAssetFolderHash;
                        }

                        if (!needToUpdateAsset)
                        {
                            int hash;
                            using (Perf.Measure("Asset.BuildHashCheck"))
                                hash = EAIDatabaseManager.GetBuildAssetHash(fullAssetDataPath);
                            if (eaiAsset.BuildAssetHash != hash)
                            {
                                EAI.Logger.Info($"The asset {fullAssetName} built files have changed, updating it. old hash: {eaiAsset.BuildAssetHash}, new hash: {hash}");
                                needToUpdateAsset = true;
                            }
                        }

                    }
                    else
                    {
                        EAI.Logger.Info($"The asset {fullAssetName} is new, adding it.");
                        eaiAsset = new(fullAssetName, sourceAssetFolderHash, assetDataPath, sourceModPath: sourceModPath);
                        needToUpdateAsset = true;
                    }

                    if (needToUpdateAsset && Directory.Exists(fullAssetDataPath))
                    {
                        using (Perf.Measure("Asset.DeleteOldBuild"))
                            Directory.Delete(fullAssetDataPath, true);
                    }

                    PrefabImportData importData = new(
                        importSettings,
                        eaiAsset,
                        needToUpdateAsset,
                        assetFolder,
                        assetName,
                        catName,
                        modName,
                        fullAssetName,
                        assetDataPath,
                        prefabJson,
                        assetCat
                    );


                    PrefabBase prefab;
                    bool cachedPrefab = true;
                    try
                    {
                        bool foundInCache;
                        using (Perf.Measure("Asset.CacheLookup"))
                            foundInCache = ImportersUtils.TryGetCachedPrefab(importData, out prefab);

                        if (!foundInCache)
                        {
                            using (Perf.Measure("Asset.Import"))
                                prefab = Import(importData);
                            cachedPrefab = false;
                        }

                        Perf.Increment(cachedPrefab ? "Assets.FromCache" : "Assets.Built");
                    }
                    catch (Exception e)
                    {
                        ImportFailed(assetFolder, assetDataPath, e);
                        continue;
                    }

                    if (prefab == null)
                    {
                        ImportFailed(assetFolder, assetDataPath, new NullReferenceException("Importer return prefab is null."));
                        continue;
                    }

                    try
                    {
                        if(!cachedPrefab)
                        {
                            PerformanceStats.Scope setupScope = Perf.Measure("Asset.Setup");

                            prefab.name = importData.FullAssetName;

                            // Should see this prefab has a pre-editor prefab.
                            prefab.version = 0;

                            EditorAssetCategoryOverride categoryOverride = prefab.AddOrGetComponentOnMainThread<EditorAssetCategoryOverride>();
                            categoryOverride.m_IncludeCategories = new[] { $"EAI/{ImporterId}/{importData.ModName}/{importData.CatName}" };

                            if (AssetPackImporter.TryGetAssetPackPrefab(importData, out AssetPackPrefab assetPackPrefab))
                            {
                                AssetPackItem assetPackItem = prefab.AddOrGetComponentOnMainThread<AssetPackItem>();
                                assetPackItem.m_Packs = new[] { assetPackPrefab };
                            }

                            AssetsImporterManager.ProcessComponentImporters(importData, importData.PrefabJson, prefab);

                            setupScope.Dispose();

                            AssetDataPath prefabAssetPath = importSettings.savePrefabs
                                ? AssetDataPath.Create(importData.AssetDataPath, ImportersUtils.GetPrefabFileName(importData), true, EscapeStrategy.None)
                                : AssetDataPath.Create(EAI.kTempFolderName, ImportersUtils.GetPrefabFileName(importData), true, EscapeStrategy.None);

                            PrefabAsset prefabAsset;
                            using (Perf.Measure("Asset.AddToDatabase"))
                                prefabAsset = importSettings.dataBase.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, prefab, Hash128.CreateGuid(importData.FullAssetName));

                            eaiAsset.PrefabID = prefab.GetPrefabID().ToString();

                            //MainThreadHelper.RunOnMainThread(() => prefabAsset.Save()).Wait();
                            // Includes the wait on PrefabSaveLock (shared by the importers running in parallel).
                            if (importSettings.savePrefabs)
                                using (Perf.Measure("Asset.SavePrefab"))
                                    lock (PrefabSaveLock) prefabAsset.Save();

                            using (Perf.Measure("Asset.BuildHashAfterSave"))
                                eaiAsset.BuildAssetHash = EAIDatabaseManager.GetBuildAssetHash(fullAssetDataPath);
                        }

                        using (Perf.Measure("Asset.VersionCompatibility"))
                            VersionCompatiblity(prefab, importData);

                        using (Perf.Measure("Asset.SetupUIObject"))
                            ImportersUtils.SetupUIObject(this, importData, prefab);

                        TrackMainThreadTask(MainThreadHelper.RunOnMainThread(() => EL.m_PrefabSystem.AddOrUpdatePrefab(prefab)));

                        Dictionary<string, string> localisation = new()
                        {
                            { $"Assets.NAME[{fullAssetName}]", assetName },
                            { $"Assets.DESCRIPTION[{fullAssetName}]", assetName }
                        };

                        TrackMainThreadTask(ImportersUtils.SetupLocalisationForPrefab(localisation));

                        using (Perf.Measure("Asset.RegisterInEAIDatabase"))
                            importSettings.eaiDatabase.AddOrValidateAsset(eaiAsset);

                    }
                    catch (Exception e)
                    {
                        ImportFailed(assetFolder, assetDataPath, e);
                        continue;
                    }

                    ammoutOfAssetsloaded++;
                }
            }
        }
        protected abstract PrefabBase Import(PrefabImportData data);

        internal void ImportFailed(string assetFolder, string assetDataPath, Exception e)
        {
            failedAssets++;
            ammoutOfAssetsloaded++;
            EAI.Logger.Error($"Failed to load the custom asset at {assetFolder} | ERROR : {e}");
            string pathToAssetInDatabase = Path.Combine(EAIAssetDatabaseDescriptor.kRootPath, assetDataPath);
            if (Directory.Exists(pathToAssetInDatabase)) Directory.Delete(pathToAssetInDatabase, true);
        }

        protected virtual void VersionCompatiblity(PrefabBase prefabBase, PrefabImportData data)
        {

            EAINewImportersCompatibility compatibility = EAI.m_Setting.NewImportersCompatibilityDropDown;

            if (compatibility == EAINewImportersCompatibility.None) return;

            ObsoleteIdentifiers obsoleteIdentifiers = prefabBase.AddOrGetComponentOnMainThread<ObsoleteIdentifiers>();

            obsoleteIdentifiers.m_PrefabIdentifiers ??= new PrefabIdentifierInfo[0];

            string name;

            switch (compatibility)
            {
                case EAINewImportersCompatibility.LocalAsset:
                    name = $"ExtraAssetsImporter {data.CatName} {data.AssetName} {this.AssetEndName}";
                    break;
                case EAINewImportersCompatibility.PreEditor:
                    name = data.FullAssetName;
                    break;
                case EAINewImportersCompatibility.None:
                    // You shouldn't be here because of the first if;
                    return;
                default:
                    throw new Exception("Unknown NewImportersCompatibilityDropDown");
            }

            PrefabIdentifierInfo prefabIdentifierInfo = new()
            {
                m_Name = name,
                m_Hash = null,
                m_Type = prefabBase.GetType().Name
            };

            obsoleteIdentifiers.m_PrefabIdentifiers = obsoleteIdentifiers.m_PrefabIdentifiers.Prepend(prefabIdentifierInfo).ToArray();
        }
    }
    public struct PrefabImportData
    {

        public PrefabImportData(ImporterSettings importSettings, EAIAsset eaiAsset, bool needToUpdateAsset, string folderPath, string assetName, string catName, string modName, string fullAssetName, string assetDataPath, Variant prefabJson, UIAssetParentCategoryPrefab assetCat)
        {
            this.ImportSettings = importSettings;
            this.EAIAsset = eaiAsset;
            this.NeedToUpdateAsset = needToUpdateAsset;
            this.FolderPath = folderPath;
            this.AssetName = assetName;
            this.CatName = catName;
            this.ModName = modName;
            this.FullAssetName = fullAssetName;
            this.AssetDataPath = assetDataPath;
            this.PrefabJson = prefabJson;
            this.AssetCat = assetCat;
        }
        public ImporterSettings ImportSettings { get; private set; }
        public EAIAsset EAIAsset { get; private set; }
        public bool NeedToUpdateAsset { get; private set; }
        public string FolderPath { get; private set; }
        public string AssetName { get; private set; }
        public string CatName { get; private set; }
        public string ModName { get; private set; }
        public string FullAssetName { get; private set; }
        public string AssetDataPath { get; private set; }
        public Variant PrefabJson { get; private set; }
        public UIAssetParentCategoryPrefab AssetCat { get; private set; }
    }

}
