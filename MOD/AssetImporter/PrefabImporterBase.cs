using Colossal.Core;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.Localization;
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

using Hash128 = Colossal.Hash128;

namespace ExtraAssetsImporter.AssetImporter
{
    abstract class PrefabImporterBase : FolderImporter
    {
        public const string PrefabJsonName = "Prefab.json";
        private UIAssetParentCategoryPrefab assetCat = null;

        virtual public string CatName { get; } = null;

        protected override void PreLoadCustomAssetFolder(ImporterSettings importSettings)
        {
            base.PreLoadCustomAssetFolder(importSettings);
            assetCat = PrefabsHelper.GetOrCreateUIAssetParentCategoryPrefab( CatName ?? ImporterId );
        }

        protected override void LoadCustomAssetFolder(ImporterSettings importSettings, string importerFolder, string modName, NotificationUISystem.NotificationInfo notificationInfo)
        {
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
                    string assetName = new DirectoryInfo(assetFolder).Name;
                    notificationInfo.progressState = ProgressState.Progressing;
                    notificationInfo.progress = (int)(ammoutOfAssetsloaded / (float)numberOfAssets * 100);
                    notificationInfo.text = $"Loading : {assetName}";
                    EL.m_NotificationUISystem.AddOrUpdateNotification(ref notificationInfo);

                    if (assetName.StartsWith("."))
                    {
                        skipedAsset++;
                        ammoutOfAssetsloaded++;
                        continue;
                    }

                    string fullAssetName = $"{modName} {catName} {assetName} {AssetEndName}";

                    string assetDataPath = importSettings.isAssetPack ?  
                        Path.Combine(importSettings.outputFolderOffset, modName, FolderName, catName, assetName) :
                        Path.Combine(importSettings.outputFolderOffset, FolderName, modName, catName, assetName);

                    string fullAssetDataPath = Path.Combine(importSettings.dataBase.rootPath, assetDataPath);

                    string prefabJsonPath = Path.Combine(assetFolder, PrefabJsonName);
                    Variant prefabJson = null;
                    if (File.Exists(prefabJsonPath)) prefabJson = ImportersUtils.LoadJson(Path.Combine(assetFolder, PrefabJsonName));

                    int sourceAssetFolderHash = EAIDataBaseManager.GetAssetHash(assetFolder);
                    bool needToUpdateAsset = false;

                    if (importSettings.eaiDatabase.TryGetEAIAsset(fullAssetName, out EAIAsset eaiAsset))
                    {

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
                            EAI.Logger.Info($"The asset {fullAssetName} source files has changed, updating it. Old hash {eaiAsset.SourceAssetHash}, new hash {sourceAssetFolderHash}.");
                            needToUpdateAsset = true;
                            eaiAsset.SourceAssetHash = sourceAssetFolderHash;
                        }

                        if (!Directory.Exists(fullAssetDataPath) || eaiAsset.BuildAssetHash != EAIDataBaseManager.GetAssetHash(fullAssetDataPath))
                        {
                            EAI.Logger.Info($"The asset {fullAssetName} builded files has changed, updating it.");
                            needToUpdateAsset = true;
                        }

                        if (!needToUpdateAsset && importSettings.savePrefabs)
                        {
                            importSettings.eaiDatabase.AddOrValidateAsset(eaiAsset);
                            continue;
                        }
                    } 
                    else
                    {
                        EAI.Logger.Info($"The asset {fullAssetName} is new, adding it.");
                        eaiAsset = new(fullAssetName, sourceAssetFolderHash, assetDataPath);
                        needToUpdateAsset = true;
                    }

                    if (needToUpdateAsset && Directory.Exists(fullAssetDataPath))
                    {
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
                    try
                    {

                        prefab = Import(importData);

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

                        prefab.name = importData.FullAssetName;

                        //CreateEditorAssetCategories(importData);

                        EditorAssetCategoryOverride categoryOverride = prefab.AddComponent<EditorAssetCategoryOverride>();
                        categoryOverride.m_IncludeCategories = new[] { $"EAI/{ImporterId}/{importData.ModName}/{importData.CatName}" };

                        if (AssetPackImporter.TryGetAssetPackPrefab(importData, out AssetPackPrefab assetPackPrefab))
                        {
                            AssetPackItem assetPackItem = prefab.AddComponent<AssetPackItem>();
                            assetPackItem.m_Packs = new[] { assetPackPrefab };
                        }

                        ImportersUtils.SetupUIObject(this, importData, prefab);

                        AssetsImporterManager.ProcessComponentImporters(importData, importData.PrefabJson, prefab);
                        VersionCompatiblity(prefab, importData);

                        AssetDataPath prefabAssetPath;
                        if (importSettings.isAssetPack)
                        {
                            prefabAssetPath = AssetDataPath.Create(importData.AssetDataPath, $"{importData.AssetName}{PrefabAsset.kExtension}", EscapeStrategy.None);
                        }
                        else
                        {
                            prefabAssetPath = AssetDataPath.Create(EAI.kTempFolderName, importData.FullAssetName + PrefabAsset.kExtension, EscapeStrategy.None);
                        }

                        PrefabAsset prefabAsset = importSettings.dataBase.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, prefab, Hash128.CreateGuid(importData.FullAssetName));

                        if (importSettings.savePrefabs) prefabAsset.Save();

                        //if(EL.m_PrefabSystem.TryGetPrefab(prefab.GetPrefabID(), out var existingPrefab)) {
                        //    //EAI.Logger.Warn($"Prefab {importData.FullAssetName} already exist.");
                        //    EAI.Logger.Warn($"Prefab {importData.FullAssetName} already exist, removing the old one and adding the new one.");
                        //    EL.m_PrefabSystem.AddOrUpdatePrefab(prefab);
                        //    existingPrefab.asset.Dispose();
                        //}

                        MainThreadDispatcher.RunOnMainThread(() => EL.m_PrefabSystem.AddOrUpdatePrefab(prefab));

                        if ( needToUpdateAsset )
                        {
                            int buildAssetFolderHash = EAIDataBaseManager.GetAssetHash(fullAssetDataPath);
                            eaiAsset.BuildAssetHash = buildAssetFolderHash;
                        }

                        if(eaiAsset.AssetPath != assetDataPath)
                        {
                            eaiAsset.AssetPath = assetDataPath;
                            EAI.Logger.Warn($"EAI asset {eaiAsset.AssetID} path was incorrect, updated to {assetDataPath}");
                        }

                        importSettings.eaiDatabase.AddOrValidateAsset(eaiAsset);

                        Dictionary<string, string> localisation = new()
                        {
                            { $"Assets.NAME[{fullAssetName}]", assetName },
                            { $"Assets.DESCRIPTION[{fullAssetName}]", assetName }
                        };

                        ImportersUtils.SetupLocalisationForPrefab(localisation, importSettings, assetDataPath, assetName);

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
            string pathToAssetInDatabase = Path.Combine(EAIAssetDataBaseDescriptor.kRootPath, assetDataPath);
            if (Directory.Exists(pathToAssetInDatabase)) Directory.Delete(pathToAssetInDatabase, true);
        }

        protected virtual void VersionCompatiblity(PrefabBase prefabBase, PrefabImportData data)
        {

            if (EAI.m_Setting.NewImportersCompatibilityDropDown == EAINewImportersCompatibility.None) return;

            ObsoleteIdentifiers obsoleteIdentifiers = prefabBase.AddOrGetComponent<ObsoleteIdentifiers>();

            obsoleteIdentifiers.m_PrefabIdentifiers ??= new PrefabIdentifierInfo[0];

            EAI.Logger.Info("Doing version compatibility for prefab " + data.FullAssetName);

            string name;
            string hash = null;

            switch (EAI.m_Setting.NewImportersCompatibilityDropDown)
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
                m_Hash = hash,
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

        //public Mesh[] meshes;
        //public Surface surface;
    }

}
