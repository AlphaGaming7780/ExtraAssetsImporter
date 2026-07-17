using Colossal.IO.AssetDatabase;
using Colossal.Json;
using ExtraAssetsImporter.AssetImporter;
using ExtraLib;
using Game.Prefabs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExtraAssetsImporter.DataBase
{
    internal static class EAIDataBaseManager
    {
        public const int DataBaseVersion = 3;
        internal static readonly string pathToAssetsDatabase = Path.Combine(EAI.pathModsData, "AssetsDataBase.json");
        public static EAIDatabase eaiDataBase;
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
                    eaiDataBase = Decoder.Decode(File.ReadAllText(pathToAssetsDatabase)).Make<EAIDatabase>();
                }
                catch
                {
                    eaiDataBase = new();
                }
            }

            eaiDataBase._DatabasePath = pathToAssetsDatabase;

            if (eaiDataBase.DataBaseVersion != DataBaseVersion)
            {
                EAI.Logger.Warn($"The database version is not the good one, expected {DataBaseVersion}, got {eaiDataBase.DataBaseVersion}. The database will be reseted.");
                eaiDataBase.ClearDatabase();
            }

            CheckIfDataBaseNeedToBeRelocated();

            AssetDatabase.global.RegisterDatabase(EAIAssetDataBase).Wait();

            EAI.Logger.Info($"DataBase Location : {EAIAssetDataBase.rootPath}.");
        }

        internal static EAIDatabase LoadDataBase(string path, string databasePath = null)
        {

            EAIDatabase database = null;

            if (!File.Exists(path))
            {
                database = new();
            }
            else
            {
                try
                {
                    database = Decoder.Decode(File.ReadAllText(path)).Make<EAIDatabase>();
                }
                catch
                {
                    database = new();
                }
            }

            if (database.DataBaseVersion != DataBaseVersion)
            {
                EAI.Logger.Warn($"The database version is not the good one, expected {DataBaseVersion}, got {database.DataBaseVersion}. The database will be reseted.");
                database = new();
            }

            database._DatabasePath = path;
            database.ActualDataBasePath = databasePath;

            return database;
        }

        internal static void SaveDataBase()
        {

            eaiDataBase.SaveDataBase();
        }
        internal static void DeleteDatabase()
        {

            eaiDataBase.ClearDatabase();
        }

        internal static void AddOrValidateAsset(EAIAsset asset)
        {

            eaiDataBase.AddOrValidateAsset(asset);
            
        }

        internal static bool TryGetEAIAsset(string AssetID, out EAIAsset asset)
        {
            return eaiDataBase.TryGetEAIAsset(AssetID, out asset);
        }


        internal static int GetAssetHash(string assetFolder)
        {
            if(!Directory.Exists(assetFolder))
            {
                return 0; 
            }

            DirectoryInfo directoryInfo = new(assetFolder);
            int hash = 0;
            foreach (FileInfo file in directoryInfo.GetFiles())
            {
                hash += file.LastWriteTimeUtc.GetHashCode();
            }
            return hash;
        }

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

        internal static void CheckIfDataBaseNeedToBeRelocated()
        {
            if (eaiDataBase == null) LoadDataBase();

            string newPath = EAI.m_Setting.SavedDatabasePath ?? eaiDataBase.ActualDataBasePath;

            if (EAI.m_Setting.SavedDatabasePath == null)
            {
                EAI.m_Setting.SavedDatabasePath = newPath;
                EAI.m_Setting.Apply();
            }

            if (newPath != eaiDataBase.ActualDataBasePath)
            {
                if (!RelocateAssetDataBase(newPath))
                {
                    EAI.m_Setting.SavedDatabasePath = eaiDataBase.ActualDataBasePath;
                    EAI.m_Setting.Apply();
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

    internal class EAIDatabase
    {

        public EAIDatabase() {}

        public int DataBaseVersion = EAIDataBaseManager.DataBaseVersion;
        public string ActualDataBasePath = Path.Combine(EAI.pathModsData, "Database");
        public List<EAIAsset> AssetsDataBase = new List<EAIAsset>();
        private readonly List<EAIAsset> _ValidateAssetsDataBase = new List<EAIAsset>();
        internal string _DatabasePath = null;

        private readonly object _lock = new object();


        internal void SaveValidateDataBase(ImporterSettings importerSettings)
        {
            lock (_lock)
            {
                if (!EAI.m_Setting.DeleteNotLoadedAssets)
                {
                    _ValidateAssetsDataBase.AddRange(AssetsDataBase);
                    AssetsDataBase.Clear();
                }
                else
                {
                    ClearNotLoadedAssetsFromFiles(importerSettings);
                }

                AssetsDataBase = _ValidateAssetsDataBase;
                SaveDataBase();
            }
        }

        internal void SaveDataBase()
        {
            lock (_lock)
            {
                DataBaseVersion = EAIDataBaseManager.DataBaseVersion;
                EAI.Logger.Info($"Saving the database at {_DatabasePath}, saving {AssetsDataBase.Count} assets.");
                string directoryPath = Path.GetDirectoryName(_DatabasePath);
                if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);
                File.WriteAllText(_DatabasePath, Encoder.Encode(this, EncodeOptions.None));
            }
        }


        private void ClearNotLoadedAssetsFromFiles(ImporterSettings importerSettings)
        {
            lock (_lock)
            {
                if (EAI.m_Setting.DeleteNotLoadedAssets == false)
                {
                    EAI.Logger.Info("Skipping clearing not loaded assets from files because the setting is disabled.");
                    return;
                }
                List<EAIAsset> tempDataBase = new(AssetsDataBase);
                EAI.Logger.Info($"Going to remove unused asset from database, number of asset : {AssetsDataBase.Count}");
                foreach (EAIAsset asset in tempDataBase)
                {
                    if (asset.AssetPath == null) continue;

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

                            if (EL.m_PrefabSystem.TryGetPrefab(prefabBase.GetPrefabID(), out PrefabBase existingPrefab))
                            {
                                if (EL.m_PrefabSystem.RemovePrefab(existingPrefab)) continue;
                                EAI.Logger.Warn($"Failed to remove prefab {prefabAsset.name} from prefab system.");
                            }
                            else
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
                _ValidateAssetsDataBase.AddRange(AssetsDataBase);
                AssetsDataBase.Clear();
            }
        }

        internal void AddOrValidateAsset(EAIAsset asset)
        {
            lock (_lock) 
            {
                if (AssetsDataBase.Contains(asset))
                {
                    AssetsDataBase.RemoveAll((Asset2) => Asset2 == asset);
                }

                if (_ValidateAssetsDataBase.Contains(asset))
                {
                    EAI.Logger.Warn($"Validating an already validated asset {asset.AssetID}, removing the old one and adding the new one.");
                    _ValidateAssetsDataBase.RemoveAll((asset2) => asset2 == asset);
                }

                _ValidateAssetsDataBase.Add(asset);
            }
        }

        internal EAIAsset GetEAIAsset(string AssetID)
        {
            EAIAsset asset = AssetsDataBase.Find((asset) => asset.AssetID == AssetID);

            if (asset != EAIAsset.Null) return asset;

            EAI.Logger.Warn($"Try to get an asset with this ID '{AssetID}', but it's not in the dataBase");
            return asset;
        }

        internal bool TryGetEAIAsset(string AssetID, out EAIAsset asset)
        {
            asset = AssetsDataBase.Find(asset => asset.AssetID == AssetID);
            return asset != EAIAsset.Null;
        }

        internal void ClearDatabase()
        {
            EAI.Logger.Info($"Clearing database at {this._DatabasePath}.");
            _ValidateAssetsDataBase.Clear();
            AssetsDataBase.Clear();
            SaveDataBase();
            Directory.Delete(ActualDataBasePath, true);
        }

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
            return AssetID.GetHashCode();
        }
    }
}