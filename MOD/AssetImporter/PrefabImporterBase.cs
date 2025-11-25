using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.PSI.Common;
using ExtraAssetsImporter.AssetImporter.Importers;
using ExtraAssetsImporter.AssetImporter.JSONs.Prefabs;
using ExtraAssetsImporter.DataBase;
using ExtraAssetsImporter.OldImporters;
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
using System.Linq;
using UnityEngine;

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

        protected override IEnumerator LoadCustomAssetFolder(ImporterSettings importSettings, string importerFolder, string modName, Dictionary<string, string> localisation, NotificationUISystem.NotificationInfo notificationInfo)
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

                    if (assetName.StartsWith(".")) // || catName.StartsWith(".")
                    {
                        skipedAsset++;
                        ammoutOfAssetsloaded++;
                        //yield return null;
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

                    if (EAIDataBaseManager.TryGetEAIAsset(fullAssetName, out EAIAsset eaiAsset))
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
                            EAIDataBaseManager.AddOrValidateAsset(eaiAsset);
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

                    IEnumerator<PrefabBase> enumerator = null;

                    try
                    {

                        enumerator = Import(importData);

                    }
                    catch (Exception e)
                    {
                        ImportFailed(assetFolder, assetDataPath, e);
                        continue;
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
                            //ImportFailed(assetFolder, assetDataPath, e);
                            EAI.Logger.Error(e);
                            break;
                        }
                    }

                    if(enumerator.Current == null)
                    {
                        ImportFailed(assetFolder, assetDataPath, new NullReferenceException("enumerator.Current is null."));
                        continue;
                    }

                    try
                    {
                        if (enumerator.Current is not PrefabBase prefab) throw new Exception("Importer didn't return a PrefabBase.");

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

                        //PrefabAsset prefabAsset = EAIDataBaseManager.EAIAssetDataBase.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, prefab, forceGuid: Colossal.Hash128.CreateGuid(importData.FullAssetName));
                        PrefabAsset prefabAsset = importSettings.dataBase.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, prefab, forceGuid: Colossal.Hash128.CreateGuid(importData.FullAssetName));
                        
                        if(importSettings.savePrefabs) prefabAsset.Save();

                        if(EL.m_PrefabSystem.TryGetPrefab(prefab.GetPrefabID(), out var existingPrefab)) {
                            //EAI.Logger.Warn($"Prefab {importData.FullAssetName} already exist.");
                            EAI.Logger.Warn($"Prefab {importData.FullAssetName} already exist, removing the old one and adding the new one.");
                            EL.m_PrefabSystem.RemovePrefab(existingPrefab); // Maybe, this is crashing the game ?? YES if they where already loaded, doesn't cause issue if they are duplicate or render prefabs.
                            existingPrefab.asset.Dispose();
                        }

                        EL.m_PrefabSystem.AddPrefab(prefab);

                        //Fixe for surfaces that might not have a folder in the database is they share all their textures with other surfaces.
                        if(!Directory.Exists(fullAssetDataPath) && this is SurfacesImporterNew)
                        {
                            // By creating the directory, this allow the coede to calculate the hash.
                            Directory.CreateDirectory(fullAssetDataPath);
                        }

                        if ( needToUpdateAsset )
                        {
                            int buildAssetFolderHash = EAIDataBaseManager.GetAssetHash(fullAssetDataPath);
                            eaiAsset.BuildAssetHash = buildAssetFolderHash;
                        }

                        EAIDataBaseManager.AddOrValidateAsset(eaiAsset);

                        if (!localisation.ContainsKey($"Assets.NAME[{fullAssetName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.NAME[{fullAssetName}]")) localisation.Add($"Assets.NAME[{fullAssetName}]", assetName);
                        if (!localisation.ContainsKey($"Assets.DESCRIPTION[{fullAssetName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.DESCRIPTION[{fullAssetName}]")) localisation.Add($"Assets.DESCRIPTION[{fullAssetName}]", assetName);

                    }
                    catch (Exception e)
                    {
                        ImportFailed(assetFolder, assetDataPath, e);
                        continue;
                    }

                    ammoutOfAssetsloaded++;
                    yield return null;
                }
            }
        }

        protected abstract IEnumerator<PrefabBase> Import(PrefabImportData data);

        internal void ImportFailed(string assetFolder, string assetDataPath, Exception e)
        {
            failedAssets++;
            EAI.Logger.Error($"Failed to load the custom asset at {assetFolder} | ERROR : {e}");
            string pathToAssetInDatabase = Path.Combine(EAIAssetDataBaseDescriptor.kRootPath, assetDataPath);
            if (Directory.Exists(pathToAssetInDatabase)) Directory.Delete(pathToAssetInDatabase, true);
        }

        protected virtual void VersionCompatiblity(PrefabBase prefabBase, PrefabImportData data)
        {

            if (EAI.m_Setting.NewImportersCompatibilityDropDown == EAINewImportersCompatibility.None) return;

            ObsoleteIdentifiers obsoleteIdentifiers = prefabBase.AddOrGetComponent<ObsoleteIdentifiers>();

            obsoleteIdentifiers.m_PrefabIdentifiers ??= new PrefabIdentifierInfo[0];

            string name = "";

            switch (EAI.m_Setting.NewImportersCompatibilityDropDown)
            {
                case EAINewImportersCompatibility.LocalAsset:
                    name = $"ExtraAssetsImporter {data.CatName} {data.AssetName} {this.AssetEndName}";
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
