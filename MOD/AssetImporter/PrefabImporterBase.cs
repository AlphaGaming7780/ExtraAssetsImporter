using Colossal.IO.AssetDatabase;
using Colossal.Json;
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
                    string prefabJsonPath = Path.Combine(assetFolder, PrefabJsonName);
                    Variant prefabJson = null;
                    if (File.Exists(prefabJsonPath)) prefabJson = ImportersUtils.LoadJson(Path.Combine(assetFolder, PrefabJsonName));

                    int folderHash = EAIDataBaseManager.GetAssetHash(assetFolder);
                    bool needToUpdateAsset = false;

                    if (EAIDataBaseManager.TryGetEAIAsset(fullAssetName, out EAIAsset eaiAsset))
                    {
                        if (eaiAsset.AssetHash == folderHash && importSettings.isAssetPack)
                        {
                            EAIDataBaseManager.AddOrValidateAsset(eaiAsset);
                            continue;
                        }

                        if(eaiAsset.AssetHash != folderHash) 
                        {
                            EAI.Logger.Info($"The asset {fullAssetName} has changed, updating it. Old hash {eaiAsset.AssetHash}, new hash {folderHash}.");
                            needToUpdateAsset = true;
                            eaiAsset.AssetHash = folderHash;
                        }

                    } 
                    else
                    {
                        EAI.Logger.Info($"The asset {fullAssetName} is new, adding it.");
                        eaiAsset = new(fullAssetName, folderHash, assetDataPath);
                        needToUpdateAsset = true;
                    }

                    if (needToUpdateAsset && Directory.Exists(Path.Combine(EAIAssetDataBaseDescriptor.kRootPath, assetDataPath)))
                    {
                        Directory.Delete(Path.Combine(EAIAssetDataBaseDescriptor.kRootPath, assetDataPath), true);
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

                        //CreateEditorAssetCategories(importData);

                        EditorAssetCategoryOverride categoryOverride = prefab.AddComponent<EditorAssetCategoryOverride>();
                        categoryOverride.m_IncludeCategories = new[] { $"EAI/{ImporterId}/{importData.ModName}/{importData.CatName}" };

                        if (AssetPackImporter.TryGetAssetPackPrefab(importData, out AssetPackPrefab assetPackPrefab))
                        {
                            AssetPackItem assetPackItem = prefab.AddComponent<AssetPackItem>();
                            assetPackItem.m_Packs = new[] { assetPackPrefab };
                        }

                        AssetsImporterManager.ProcessComponentImporters(importData, importData.PrefabJson, prefab);
                        AssetDataPath prefabAssetPath;
                        if (importSettings.isAssetPack)
                        {
                            //prefabAssetPath = AssetDataPath.Create(importData.AssetDataPath, $"{importData.AssetName}{PrefabAsset.kExtension}", EscapeStrategy.None);
                            prefabAssetPath = AssetDataPath.Create(importData.AssetDataPath, importData.AssetName, EscapeStrategy.None);
                        }
                        else
                        {
                            prefabAssetPath = AssetDataPath.Create("TempAssetsFolder", importData.FullAssetName + PrefabAsset.kExtension, EscapeStrategy.None);
                        }

                        PrefabAsset prefabAsset = EAIDataBaseManager.EAIAssetDataBase.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, prefab, forceGuid: Colossal.Hash128.CreateGuid(importData.FullAssetName));
                        
                        if(importSettings.savePrefab) prefabAsset.Save();

                        if(EL.m_PrefabSystem.TryGetPrefab(prefab.GetPrefabID(), out var existingPrefab)) {
                            EAI.Logger.Warn($"Prefab {importData.FullAssetName} already exist, removing the old one and adding the new one.");
                            EL.m_PrefabSystem.RemovePrefab(existingPrefab);
                        }

                        EL.m_PrefabSystem.AddPrefab(prefab);

                        EAIDataBaseManager.AddOrValidateAsset(eaiAsset);

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

        protected abstract IEnumerator<PrefabBase> Import(PrefabImportData data);

        internal void ImportFailed(string assetFolder, string assetDataPath, Exception e)
        {
            failedAssets++;
            EAI.Logger.Error($"Failed to load the custom asset at {assetFolder} | ERROR : {e}");
            string pathToAssetInDatabase = Path.Combine(EAIAssetDataBaseDescriptor.kRootPath, assetDataPath);
            if (Directory.Exists(pathToAssetInDatabase)) Directory.Delete(pathToAssetInDatabase, true);
        }

        //private void CreateEditorAssetCategories(PrefabImportData importData)
        //{

        //    //$"EAI/{ImporterId}/{importData.ModName}/{importData.CatName}"

        //    if (!EL.m_EditorAssetCategorySystem.TryGetCategory("EAI", out var eaiCat))
        //    {
        //        eaiCat = new()
        //        {
        //            id = "EAI",
        //            path = "EAI"
        //        };
        //        EL.m_EditorAssetCategorySystem.AddCategory(eaiCat);
        //    }

        //    if (!EL.m_EditorAssetCategorySystem.TryGetCategory($"{ImporterId}", out var importerCat))
        //    {
        //        importerCat = new()
        //        {
        //            id = $"{ImporterId}",
        //            path = $"EAI/{ImporterId}",
        //            icon = $"{Icons.COUIBaseLocation}/Icons/NotificationInfo/{ImporterId}.svg",
        //        };
        //        EL.m_EditorAssetCategorySystem.AddCategory(importerCat, eaiCat);
        //    }

        //    if (!EL.m_EditorAssetCategorySystem.TryGetCategory($"{importData.ModName}", out var modCat))
        //    {
        //        modCat = new()
        //        {
        //            id = $"{importData.ModName}",
        //            path = $"EAI/{ImporterId}/{importData.ModName}",
        //            icon = null,
        //        };

        //        if (AssetPackImporter.TryGetAssetPackPrefab(importData, out AssetPackPrefab assetPackPrefab))
        //        {
        //            modCat.icon = assetPackPrefab.GetComponent<UIObject>().m_Icon;
        //        }

        //        EL.m_EditorAssetCategorySystem.AddCategory(modCat, importerCat);
        //    }

        //    if (!EL.m_EditorAssetCategorySystem.TryGetCategory($"{importData.CatName}", out var cat))
        //    {
        //        cat = new()
        //        {
        //            id = $"{importData.CatName}",
        //            path = $"EAI/{ImporterId}/{importData.ModName}/{importData.CatName}",
        //            icon = File.Exists($"{EL.ResourcesIcons}/UIAssetChildCategoryPrefab/{importData.CatName} {ImporterId}.svg") ? $"{ExtraLib.Helpers.Icons.COUIBaseLocation}/Icons/UIAssetChildCategoryPrefab/{importData.CatName} {ImporterId}.svg" : "",
        //        };
        //        EL.m_EditorAssetCategorySystem.AddCategory(cat, modCat);
        //    }

        //}
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
