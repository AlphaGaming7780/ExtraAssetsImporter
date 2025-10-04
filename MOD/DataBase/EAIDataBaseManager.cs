using Colossal.IO;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using ExtraAssetsImporter.AssetImporter;
using ExtraLib;
using ExtraLib.Debugger;
using Game.Prefabs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExtraAssetsImporter.DataBase
{
    internal static class EAIDataBaseManager
    {
        const int DataBaseVersion = 2;
        private static readonly string pathToAssetsDatabase = Path.Combine(EAI.pathModsData, "AssetsDataBase.json");
        public static EAIDataBase eaiDataBase;
        private static readonly List<EAIAsset> ValidateAssetsDataBase = new List<EAIAsset>();
        private static List<EAIAsset> AssetsDataBase = new List<EAIAsset>();
        //public static ILocalAssetDatabase assetDataBaseEAI { get; private set; } = AssetDatabase<AssetDataBaseEAI>.instance;
        public static AssetDatabase<EAIAssetDataBaseDescriptor> EAIAssetDataBase => AssetDatabase<EAIAssetDataBaseDescriptor>.instance;

        internal static void LoadDataBase()
        {
            if(!File.Exists(pathToAssetsDatabase))
            {
                eaiDataBase = new();
            } else
            {
                try
                {
                    eaiDataBase = Decoder.Decode(File.ReadAllText(pathToAssetsDatabase)).Make<EAIDataBase>();
                }
                catch
                {
                    eaiDataBase = new();
                }
            }

            if(eaiDataBase.DataBaseVersion != DataBaseVersion)
            {
                EAI.Logger.Warn($"The database version is not the good one, expected {DataBaseVersion}, got {eaiDataBase.DataBaseVersion}. The database will be reseted.");
                eaiDataBase = new();
            }

            AssetsDataBase = eaiDataBase.AssetsDataBase;

            CheckIfDataBaseNeedToBeRelocated();

            AssetDatabase.global.RegisterDatabase(EAIAssetDataBase).Wait();

            EAI.Logger.Info($"DataBase Location : {EAIAssetDataBase.rootPath}.");
        }

        internal static bool LoadDataBase(string path)
        {

            if(eaiDataBase != null && eaiDataBase.ActualDataBasePath == path)
            {
                EAI.Logger.Info($"The database is already loaded at this path {path}, doing nothing.");
                return true;
            } else if(eaiDataBase != null)
            {
                EAI.Logger.Warn($"Another database is already loaded at this path {eaiDataBase.ActualDataBasePath}.");
                return false;
            }

            if (!File.Exists(pathToAssetsDatabase))
            {
                eaiDataBase = new();
            }
            else
            {
                try
                {
                    eaiDataBase = Decoder.Decode(File.ReadAllText(pathToAssetsDatabase)).Make<EAIDataBase>();
                }
                catch
                {
                    eaiDataBase = new();
                    eaiDataBase.ActualDataBasePath = path;
                }
            }

            if (eaiDataBase.DataBaseVersion != DataBaseVersion)
            {
                EAI.Logger.Warn($"The database version is not the good one, expected {DataBaseVersion}, got {eaiDataBase.DataBaseVersion}. The database will be reseted.");
                eaiDataBase = new();
                eaiDataBase.ActualDataBasePath = path;
            }

            AssetsDataBase = eaiDataBase.AssetsDataBase;

            return true;
        }

        internal static void UnloadDataBase()
        {
            AssetsDataBase.Clear();
            ValidateAssetsDataBase.Clear();
            eaiDataBase = null;
        }

        internal static void SaveValidateDataBase()
        {
            if (!EAI.m_Setting.DeleteNotLoadedAssets)
            {
                ValidateAssetsDataBase.AddRange(AssetsDataBase);
                AssetsDataBase.Clear();
            }

            eaiDataBase.DataBaseVersion = DataBaseVersion;
            eaiDataBase.AssetsDataBase = ValidateAssetsDataBase;
            SaveDataBase();

            //assetDataBaseEAI.ResaveCache().Wait();
        }

        internal static void SaveDataBase()
        {
            EAI.Logger.Info($"Saving the database at {pathToAssetsDatabase}, saving {eaiDataBase.AssetsDataBase.Count} assets.");
            string directoryPath = Path.GetDirectoryName(pathToAssetsDatabase);
            if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);
            File.WriteAllText(pathToAssetsDatabase, Encoder.Encode(eaiDataBase, EncodeOptions.None));
        }

        internal static void ClearNotLoadedAssetsFromFiles(ImporterSettings importerSettings)
        {
            List<EAIAsset> tempDataBase = new(AssetsDataBase);
            EAI.Logger.Info($"Going to remove unused asset from database, number of asset : {AssetsDataBase.Count}");
            foreach (EAIAsset asset in tempDataBase)
            {
                string path = Path.Combine(importerSettings.dataBase.rootPath, asset.AssetPath);
                if (Directory.Exists(path))
                {
                    if (!AssetsDataBase.Remove(asset))
                    {
                        EAI.Logger.Warn($"Failed to remove a none loaded asset at path {path} from the data base.");
                        continue;
                    }

                    // Making sure that if a prefab is their and loaded in the prefab system, it is removed from it.
                    SearchFilter<PrefabAsset> searchFilter = SearchFilter<PrefabAsset>.ByCondition(a => { 
                        string pathA = a.subPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                        string pathB = Path.DirectorySeparatorChar + asset.AssetPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);  
                        return pathA.Contains(pathB);
                    });

                    IEnumerable<PrefabAsset> prefabAssets = AssetDatabase.user.GetAssets<PrefabAsset>(searchFilter);
                    prefabAssets.Concat(importerSettings.dataBase.GetAssets<PrefabAsset>(searchFilter));

                    foreach (PrefabAsset prefabAsset in prefabAssets)
                    {
                        EAI.Logger.Info($"Removing prefab asset {prefabAsset.name} at {prefabAsset.subPath} from prefab system and unloading it.");

                        PrefabBase prefabBase = prefabAsset.Load<PrefabBase>();

                        if(EL.m_PrefabSystem.TryGetPrefab(prefabBase.GetPrefabID(), out PrefabBase existingPrefab))
                        {
                            if (EL.m_PrefabSystem.RemovePrefab(existingPrefab)) continue;
                            EAI.Logger.Warn($"Failed to remove prefab {prefabAsset.name} from prefab system.");
                        } else
                        {
                            EAI.Logger.Info($"Prefab {prefabAsset.name} was not in the prefab system.");
                        }

                        prefabAsset.Unload();
                        prefabAsset.database.DeleteAsset(prefabAsset);
                    }

                    Directory.Delete(path, true);
                }
                else EAI.Logger.Warn($"Trying to delete a none loaded asset at path {path}, but this path doesn't exist.");
            }
            EAI.Logger.Info($"Removed unused asset from database, number of asset in database now : {AssetsDataBase.Count}.");
            ValidateAssetsDataBase.AddRange(AssetsDataBase);
            AssetsDataBase.Clear();
        }

        internal static void DeleteDatabase()
        {
            EAI.Logger.Info("Deleting the database.");
            //if(File.Exists(pathToAssetsDatabase)) File.Delete(pathToAssetsDatabase);
            //if(Directory.Exists(AssetDataBaseEAI.kRootPath)) Directory.Delete(AssetDataBaseEAI.kRootPath, true);
            eaiDataBase.AssetsDataBase = new();
            ValidateAssetsDataBase.Clear();
            AssetsDataBase.Clear();
            SaveDataBase();
        }

        internal static void ValidateAsset(string AssetID)
        {
            if (AssetID == null) { EAI.Logger.Warn("Try to validate an assets with a null AssetID."); return; }
            ValidateAsset(GetEAIAsset(AssetID));
        }

        internal static void ValidateAsset(EAIAsset asset)
        {
            if (asset == EAIAsset.Null) return;
            AssetsDataBase.Remove(asset);
            ValidateAssetsDataBase.Add(asset);
        }

        //   internal static void AddAssets(string AssetID, int Hash, string AssetPath)
        //{
        //	EAIAsset asset = new()
        //	{
        //		AssetID = AssetID,
        //		AssetHash = Hash
        //	};
        //	AddAssets(asset);
        //}

        internal static void AddOrValidateAsset(EAIAsset asset)
        {
            if(IsAssetsInDataBase(asset))
            {
                ValidateAsset(asset);
            }
            else
            {
                ValidateAssetsDataBase.Add(asset);
            }
        }

        internal static void AddAsset(EAIAsset asset)
        {
            if (IsAssetsInDataBase(asset))
            {
                EAI.Logger.Info($"Try to add {asset.AssetID} in the data base, it is already in the data base. Validating this asset instead.");
                ValidateAsset(asset);
                return;
            }
            ValidateAssetsDataBase.Add(asset);
        }

        internal static bool IsAssetsInDataBase(EAIAsset asset)
        {
            return IsAssetsInDataBase(asset.AssetID);
        }

        internal static bool IsAssetsInDataBase(string AssetID)
        {
            foreach (var asset in AssetsDataBase)
            {
                if (asset.AssetID == AssetID) return true;
            }

            foreach (var asset in ValidateAssetsDataBase)
            {
                if (asset.AssetID == AssetID) return true;
            }

            return false;
        }

        internal static EAIAsset GetEAIAsset(string AssetID)
        {
            foreach (var asset in AssetsDataBase)
            {
                if (asset.AssetID == AssetID) return asset;
            }
            EAI.Logger.Warn($"Try to get an asset with this ID '{AssetID}', but it's not in the dataBase");
            return EAIAsset.Null;
        }

        internal static bool TryGetEAIAsset(string AssetID, out EAIAsset asset)
        {
            asset = AssetsDataBase.Find(asset => asset.AssetID == AssetID);
            return asset != EAIAsset.Null;
        }


        internal static int GetAssetHash(string assetFolder)
        {
            DirectoryInfo directoryInfo = new(assetFolder);
            int hash = 0;
            foreach (FileInfo file in directoryInfo.GetFiles())
            {
                // Maybe get the hash of the file instead of the last write time ?
                hash += file.LastWriteTimeUtc.GetHashCode();
            }
            return hash;
        }

        //internal static PrefabBase LoadPrefab(string AssetID, string prefabFileName)
        //{
        //    return LoadPrefab<PrefabBase>(GetEAIAsset(AssetID), prefabFileName);
        //}

        //internal static PrefabBase LoadPrefab(EAIAsset asset, string prefabFileName)
        //{
        //    return LoadPrefab<PrefabBase>(asset, prefabFileName);
        //}

        //internal static T LoadPrefab<T>(string AssetID, string prefabFileName) where T : PrefabBase
        //{
        //    return LoadPrefab<T>(GetEAIAsset(AssetID), prefabFileName);
        //}

        //internal static bool TryLoadPrefab<T>( string AssetID, string prefabFileName, out T prefab ) where T : PrefabBase
        //{
        //    prefab = LoadPrefab<T>(AssetID, prefabFileName);
        //    return prefab != null;
        //}

        //internal static bool TryLoadPrefab<T>(EAIAsset asset, string prefabFileName, out T prefab) where T : PrefabBase
        //{
        //    prefab = LoadPrefab<T>(asset, prefabFileName);
        //    return prefab != null;
        //}

        //internal static T LoadPrefab<T>(EAIAsset asset, string prefabFileName) where T : PrefabBase
        //{

        //    IAssetData assetData =
        //    (asset, $"{prefabFileName}{PrefabAsset.kExtension}");

        //    if(assetData == null)
        //    {
        //        EAI.Logger.Warn($"Failed to load prefab {prefabFileName} from asset {asset.AssetID}.");
        //        return null;
        //    }

        //    if (assetData is not PrefabAsset prefabAsset) return null;

        //    T prefab = prefabAsset.Load<T>();
        //    return prefab;
        //}

        //internal static IAssetData GetAsset(string AssetID, string fileName)
        //{
        //    return GetAsset(GetEAIAsset(AssetID), fileName);
        //}

        //internal static bool TryGetAsset(string AssetID, string fileName, out IAssetData assetData)
        //{
        //    assetData = GetAsset(AssetID, fileName);
        //    return assetData != null;
        //}

        //internal static bool TryGetAsset(EAIAsset asset, string fileName, out IAssetData assetData)
        //{
        //    assetData = GetAsset(asset, fileName);
        //    return assetData != null;
        //}

        //internal static bool TryGetAsset<T>(EAIAsset asset, string fileName, out T assetData) where T : IAssetData
        //{
        //    assetData = default;
        //    IAssetData iAssetData = GetAsset(asset, fileName);
        //    if (iAssetData == null) return false;
        //    if (iAssetData is not T t) return false;
        //    assetData = t;
        //    return true;
        //}

        //internal static IAssetData GetAsset(EAIAsset asset, string fileName)
        //{
        //    if (asset == EAIAsset.Null) return null;
        //    string assetPath = Path.Combine(DataBase.EAIAssetDataBaseDescriptor.kRootPath, asset.AssetPath);

        //    if (!Directory.Exists(assetPath)) { EAI.Logger.Warn("Diretory doesn't exist"); return null; }
        //    string filePath = Path.Combine(assetPath, fileName);
        //    EAI.Logger.Info($"Trying to get asset at path {filePath}.");
        //    if (!File.Exists(filePath)) { EAI.Logger.Warn("File doesn't exist"); return null; }

        //    string assetSubPath = assetPath.Replace(DataBase.EAIAssetDataBaseDescriptor.kRootPath + Path.DirectorySeparatorChar, "");
        //    AssetDataPath assetDataPath = AssetDataPath.Create(assetSubPath, fileName, true, EscapeStrategy.None);

        //    try
        //    {
        //        if (EAIAssetDataBase.Exists(assetDataPath, out IAssetData assetData))
        //        {
        //            EAI.Logger.Info($"Asset {assetDataPath} already exists in the database, returning existing asset.");
        //            return assetData;
        //        }
                
        //        return EAIAssetDataBase.AddAsset(assetDataPath);
        //    }
        //    catch (Exception e)
        //    {
        //        EAI.Logger.Warn(e);
        //        return null;
        //    }

        //}

        //internal static List<IAssetData> GetAssets(string AssetID)
        //{
        //    return GetAssets(GetEAIAsset(AssetID));
        //}

        //internal static List<IAssetData> GetAssets(EAIAsset asset)
        //{
        //    List<IAssetData> output = new();
        //    string assetPath = Path.Combine(DataBase.EAIAssetDataBaseDescriptor.kRootPath, asset.AssetPath);
        //    if (!Directory.Exists(assetPath)) return output;
        //    foreach (string s in DefaultAssetFactory.instance.GetSupportedExtensions())
        //    {
        //        foreach (string file in Directory.GetFiles(assetPath, $"*{s}"))
        //        {
        //            if(!TryGetAsset(asset, Path.GetFileName(file), out IAssetData assetData)) continue;
                    
        //            output.Add(assetData);

        //        }
        //    }
        //    return output;
        //}

        //internal static List<PrefabBase> LoadAsset(string AssetID)
        //{
        //    return LoadAsset(GetEAIAsset(AssetID));
        //}

        //internal static List<PrefabBase> LoadAsset(EAIAsset asset)
        //{
        //    List<PrefabBase> output = new();

        //    List<PrefabAsset> prefabAssets = new();

        //    string assetPath = Path.Combine(AssetDataBaseEAI.kRootPath, asset.AssetPath);

        //    if (!Directory.Exists(assetPath)) return output;

        //    foreach (string s in DefaultAssetFactory.instance.GetSupportedExtensions())
        //    {
        //        foreach (string file in Directory.GetFiles(assetPath, $"*{s}"))
        //        {
        //            string assetSubPath = assetPath.Replace(AssetDataBaseEAI.kRootPath + Path.DirectorySeparatorChar, "");
        //            AssetDataPath assetDataPath = AssetDataPath.Create(assetSubPath, Path.GetFileName(file), true, EscapeStrategy.None); //Path.GetDirectoryName(file)
        //            //EAI.Logger.Info($"Loading asset {assetDataPath}.");
        //            try
        //            {
        //                //if (!assetDataBaseEAI.Exists(assetDataPath, out IAssetData assetData)) continue;
        //                IAssetData assetData = assetDataBaseEAI.AddAsset(assetDataPath);
        //                if (assetData is PrefabAsset prefabAsset) prefabAssets.Add(prefabAsset);
        //            }
        //            catch (Exception e)
        //            {
        //                EAI.Logger.Warn(e);
        //            }
        //        }
        //    }

        //    foreach (PrefabAsset prefabAsset in prefabAssets)
        //    {
        //        if (!prefabAsset.isValid)
        //        {
        //            EAI.Logger.Info($"Prefab Asset wasn't valid, prefab asset path: {prefabAsset.path}, prefab asset id: {prefabAsset.id} ");
        //            continue;
        //        }
        //        //EAI.Logger.Info($"Loading prefab {prefabAsset.path} subPath {prefabAsset.subPath}.");
        //        PrefabBase prefabBase = prefabAsset.Load<PrefabBase>();
        //        if (EL.m_PrefabSystem.TryGetPrefab(prefabBase.GetPrefabID(), out PrefabBase prefabBase1))
        //        {
        //            prefabBase = prefabBase1;
        //        }
        //        else
        //        {
        //            EL.m_PrefabSystem.AddPrefab(prefabBase);
        //        }
        //        output.Add(prefabBase);
        //    }

        //    ValidateAssets(asset);
        //    return output;
        //}

        public static void RemoveAllPrefab()
        {
            IEnumerable<IAssetData> assetsData = EAIAssetDataBase.AllAssets();

            foreach (IAssetData assetData in assetsData)
            {
                if (assetData is not PrefabAsset prefabAsset) continue;

                PrefabBase prefabBase = prefabAsset.Load<PrefabBase>();

                if (EL.m_PrefabSystem.RemovePrefab(prefabBase)) continue;

                EAI.Logger.Warn($"Failed to remove prefab {assetData.name} from prefab system.");

            }
        }

        internal static void CheckIfDataBaseNeedToBeRelocated(bool saveSettings = true)
        {
            string newPath = EAI.m_Setting.SavedDatabasePath ?? eaiDataBase.ActualDataBasePath;

            if (EAI.m_Setting.SavedDatabasePath == null)
            {
                EAI.m_Setting.SavedDatabasePath = newPath;
                if (saveSettings) EAI.m_Setting.ApplyAndSave();
            }

            if (newPath != eaiDataBase.ActualDataBasePath)
            {
                if (!RelocateAssetDataBase(newPath))
                {
                    EAI.m_Setting.SavedDatabasePath = eaiDataBase.ActualDataBasePath;
                    if (saveSettings) EAI.m_Setting.ApplyAndSave();
                }
            }
        }

        public static bool RelocateAssetDataBase(string newDirectory)
        {
            if (!Directory.Exists(newDirectory)) return false;

            if (newDirectory == eaiDataBase.ActualDataBasePath) return false;

            if (!Directory.Exists(eaiDataBase.ActualDataBasePath))
            {
                eaiDataBase.ActualDataBasePath = newDirectory;
                SaveDataBase();
                return true;
            }

            //RemoveAllPrefab();
            //AssetDatabase.global.UnregisterDatabase(assetDataBaseEAI).Wait();
            //assetDataBaseEAI.Dispose();

            try
            {
                Directory.Delete(newDirectory, false);
                Directory.Move(eaiDataBase.ActualDataBasePath, newDirectory);
                eaiDataBase.ActualDataBasePath = newDirectory;
                SaveDataBase();
            }
            catch (Exception ex)
            {
                EAI.Logger.Warn($"Failed to relocate the asset database, this could be because you try to move the database in a non empty folder.\nActual path : {eaiDataBase.ActualDataBasePath},\nthe target new path : {newDirectory}. \nHere is the error {ex.ToString()}");
                return false;
            }

            //AssetDatabase.global.RegisterDatabase(assetDataBaseEAI).Wait();

            //EAI.Initialize();

            return true;
        }
    }

    internal class EAIDataBase
    {
        public int DataBaseVersion = 0;
        public string ActualDataBasePath = Path.Combine(EAI.pathModsData, "Database");
        public List<EAIAsset> AssetsDataBase = new List<EAIAsset>();
    }

    public struct EAIAsset
    {
        public static EAIAsset Null => default;
        public string AssetID { get; set; }
        public int SourceAssetHash { get; set; }
        public int BuildAssetHash { get; set; }
        public string AssetPath { get; set; }

        public EAIAsset(string assetID, int assetHash, string assetPath)
        {
            AssetID = assetID;
            SourceAssetHash = assetHash;
            AssetPath = assetPath;
            BuildAssetHash = 0;
        }

        public static bool operator ==(EAIAsset lhs, EAIAsset rhs)
        {
            return lhs.AssetID == rhs.AssetID;
        }

        public static bool operator !=(EAIAsset lhs, EAIAsset rhs)
        {
            return !(lhs == rhs);
        }

        public override readonly bool Equals(object compare)
        {
            if (compare is EAIAsset asset)
            {
                return Equals(asset);
            }

            return false;
        }

        public readonly bool Equals(EAIAsset asset)
        {
            return asset.AssetID == AssetID;
        }

        public override readonly int GetHashCode()
        {
            return SourceAssetHash;
        }
    }
}