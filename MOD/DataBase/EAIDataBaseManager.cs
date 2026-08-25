using Colossal;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.PSI.Common;
using ExtraAssetsImporter.AssetImporter;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraLib;
using ExtraLib.ClassExtension;
using Game.Prefabs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Mathematics;

namespace ExtraAssetsImporter.Database
{
    internal static class EAIDatabaseManager
    {
        public const int DatabaseVersion = 4;
        internal const string databaseFolderName = ".Database";
        internal static readonly string pathToAssetsDatabase = Path.Combine(EAI.pathModsData, "AssetsDatabase.json");
        internal static readonly string defaultDatabasePath = Path.GetFullPath(Path.Combine(EAI.pathModsData, databaseFolderName));
        public static EAIDatabase eaiDatabase;
        public static AssetDatabase<EAIAssetDatabaseDescriptor> EAIAssetDatabase => AssetDatabase<EAIAssetDatabaseDescriptor>.instance;

        internal static Task LoadDatabase()
        {
            if(!File.Exists(pathToAssetsDatabase))
            {
                eaiDatabase = new();
            } else
            {
                try
                {
                    eaiDatabase = Decoder.Decode(File.ReadAllText(pathToAssetsDatabase)).Make<EAIDatabase>();
                }
                catch
                {
                    eaiDatabase = new();
                }
            }

            eaiDatabase._DatabasePath = pathToAssetsDatabase;

            if (eaiDatabase.DatabaseVersion != DatabaseVersion)
            {
                EAI.Logger.Warn($"The database version is incorrect, expected {DatabaseVersion}, got {eaiDatabase.DatabaseVersion}. The database will be reset.");
                eaiDatabase.ClearDatabase();
            }

            CheckIfDatabaseNeedToBeRelocated();

            return RegisterEaiAssetDatabase();
        }

        internal static EAIDatabase LoadDatabase(string path, string databasePath = null)
        {

            EAIDatabase database;

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

            if (database.DatabaseVersion != DatabaseVersion)
            {
                EAI.Logger.Warn($"The database version is incorrect, expected {DatabaseVersion}, got {database.DatabaseVersion}. The database will be reset.");
                database = new();
            }

            database._DatabasePath = path;
            database.ActualDatabasePath = databasePath;

            return database;
        }

        internal static void SaveDatabase()
        {

            eaiDatabase.SaveDatabase();
        }
        internal static void DeleteDatabase()
        {

            eaiDatabase.ClearDatabase();
        }

        internal static Task RegisterEaiAssetDatabase()
        {
            CancellationToken cancellationToken = new();
            ProgressTracker progressTracker = new("EAI_PopulateDatabase", true);

            var notif = EL.m_NotificationUISystem.AddOrUpdateNotification("EAI.PopulatingDatabase", "Extra Assets Importer", "Populating Extra Assets Importer Asset Database...", null, ProgressState.Indeterminate);
            void action(ProgressTracker tracker)
            {
                notif.progress = (int)math.round(tracker.progress * 100);
                notif.Update();
            }
            TaskProgress taskProgress = new(action);

            Task task = AssetDatabase.global.RegisterDatabase(EAIAssetDatabase).ContinueWith((t) =>
            {
                if (t.IsFaulted)
                {
                    EAI.Logger.Error($"Failed to register the Extra Assets Importer Asset Database : {t.Exception}");
                    notif.text = "Failed to register the Extra Assets Importer Asset Database.";
                    notif.progressState = ProgressState.Failed;
                    notif.Update();
                    EL.m_NotificationUISystem.RemoveNotification(notif, 0.5f);
                }

                else
                {
                    Task task2 = EAIAssetDatabase.PopulateFromDataSource(false, cancellationToken, taskProgress);

                    task2.Wait();

                    if (task2.IsFaulted)
                    {
                        EAI.Logger.Error($"Failed to populate the Extra Assets Importer Asset Database : {task2.Exception}");
                        notif.text = "Failed to populate the Extra Assets Importer Asset Database.";
                        notif.progressState = ProgressState.Failed;
                        notif.Update();
                        EL.m_NotificationUISystem.RemoveNotification(notif, 0.5f);
                        return;
                    }

                    notif.text = "Extra Assets Importer Asset Database populated.";
                    notif.progressState = ProgressState.Complete;
                    notif.progress = 100;
                    notif.Update();
                    EL.m_NotificationUISystem.RemoveNotification(notif, 0.5f);
                    return;
                }

            });

            EAI.Logger.Info($"Database Location : {EAIAssetDatabase.rootPath}.");

            return task;
        }

        internal static void AddOrValidateAsset(EAIAsset asset)
        {

            eaiDatabase.AddOrValidateAsset(asset);
            
        }

        internal static bool TryGetEAIAsset(string AssetID, out EAIAsset asset)
        {
            return eaiDatabase.TryGetEAIAsset(AssetID, out asset);
        }

        internal static List<EAIAsset> GetAllAssets()
        {
            return eaiDatabase.GetAllAssets();
        }

        // Unregisters (from PrefabSystem) and unloads (from the asset database) every known asset whose
        // source mod's path isn't in activeModPaths, without touching anything generated on disk — for
        // mods present but disabled in the current playset (contrast with EAIDatabase.ClearNotLoadedAssetsFromFiles,
        // which deletes for a mod that's genuinely gone). Re-enabling a mod needs no separate "re-add" path:
        // a normal AddAssetFolder + import pass finds the mod's cached prefabs again via
        // ImportersUtils.TryGetCachedPrefab instead of rebuilding them.
        //
        // Not wired to any trigger yet — no reliable signal exists today for "the playset just changed"
        // (mods' OnLoad isn't re-invoked on a playset switch). Call manually, or from a future trigger.
        internal static void UnloadAssetsForInactiveMods(IEnumerable<string> activeModPaths)
        {
            HashSet<string> active = new(activeModPaths, StringComparer.OrdinalIgnoreCase);

            foreach (EAIAsset asset in GetAllAssets())
            {
                if (string.IsNullOrEmpty(asset.SourceModPath) || active.Contains(asset.SourceModPath)) continue;
                if (!ImportersUtils.TryParsePrefabID(asset.PrefabID, out PrefabID prefabID)) continue;

                ImportersUtils.UnloadPrefab(prefabID);
            }
        }


        internal static int GetAssetHash(string assetFolder)
        {
            if(!Directory.Exists(assetFolder))
            {
                return 0;
            }

            DirectoryInfo directoryInfo = new(assetFolder);
            int hash = 17;
            foreach (FileInfo file in directoryInfo.GetFiles())
            {
                if (file.Extension == ".cid") continue;

                hash = unchecked(hash * 31 + file.Name.GetHashCode());
                hash = unchecked(hash * 31 + file.Length.GetHashCode());
            }
            return hash;
        }

        public static void RemoveAllPrefab()
        {
            IEnumerable<IAssetData> assetsData = EAIAssetDatabase.AllAssets();

            foreach (IAssetData assetData in assetsData)
            {
                if (assetData is not PrefabAsset prefabAsset) continue;

                PrefabBase prefabBase = prefabAsset.Load<PrefabBase>();

                if (EL.m_PrefabSystem.RemovePrefab(prefabBase)) continue;

                EAI.Logger.Warn($"Failed to remove prefab {assetData.name} from prefab system.");

            }
        }

        internal static void CheckIfDatabaseNeedToBeRelocated()
        {
            if (eaiDatabase == null) LoadDatabase();

            if(string.IsNullOrEmpty(EAI.m_Setting.SavedDatabasePath) || EAI.m_Setting.SavedDatabasePath == eaiDatabase.ActualDatabasePath) return;

            if(!Directory.Exists(EAI.m_Setting.SavedDatabasePath))
            {
                EAI.Logger.Error($"The selected directory for database relocation doesn't exist: {EAI.m_Setting.SavedDatabasePath}");
                return;
            }

            string newPath = Path.Combine(EAI.m_Setting.SavedDatabasePath, databaseFolderName);

            if (newPath != eaiDatabase.ActualDatabasePath)
            {
                if (RelocateAssetDatabase(newPath))
                {
                    EAI.m_Setting.SavedDatabasePath = eaiDatabase.ActualDatabasePath;
                    EAI.m_Setting.Apply();
                }
            }
        }

        public static bool RelocateAssetDatabase(string newDirectory)
        {
            if (newDirectory == eaiDatabase.ActualDatabasePath) return false;

            bool sourceExists = Directory.Exists(eaiDatabase.ActualDatabasePath);

            if (Directory.Exists(newDirectory))
            {
                if (sourceExists)
                {
                    // Genuine conflict: both the currently tracked path and the target already hold data.
                    EAI.Logger.Error($"Failed to relocate database, the directory already exist: {newDirectory}");
                    return false;
                }

                // Nothing to move — a database already lives at the target (e.g. the tracked path was lost
                // after AssetsDatabase.json got reset while the real data still lives there). Adopt it.
                eaiDatabase.ActualDatabasePath = newDirectory;
                SaveDatabase();
                return true;
            }

            if (!sourceExists)
            {
                eaiDatabase.ActualDatabasePath = newDirectory;
                SaveDatabase();
                return true;
            }

            AssetDatabase.global.UnregisterDatabase(EAIAssetDatabase).Wait();
            EAIAssetDatabase.Dispose();

            try
            {
                Directory.Move(eaiDatabase.ActualDatabasePath, newDirectory);
                eaiDatabase.ActualDatabasePath = newDirectory;
                SaveDatabase();
            }
            catch (Exception ex)
            {
                EAI.Logger.Warn(
                    $"Failed to relocate database.\n" +
                    $"From: {eaiDatabase.ActualDatabasePath}\n" +
                    $"To: {newDirectory}\n" +
                    $"Error: {ex}"
                );
                return false;
            }

            return true;
        }
    }

    internal class EAIDatabase
    {

        public EAIDatabase() {}

        public int DatabaseVersion = EAIDatabaseManager.DatabaseVersion;
        public string ActualDatabasePath = EAIDatabaseManager.defaultDatabasePath;
        public Dictionary<string, EAIAsset> AssetsDatabase = new Dictionary<string, EAIAsset>();
        private readonly Dictionary<string, EAIAsset> _ValidateAssetsDatabase = new Dictionary<string, EAIAsset>();
        internal string _DatabasePath = null;

        private readonly object _lock = new object();


        internal void SaveValidateDatabase(ImporterSettings importerSettings)
        {
            lock (_lock)
            {
                if (!EAI.m_Setting.DeleteNotLoadedAssets)
                {
                    foreach (KeyValuePair<string, EAIAsset> entry in AssetsDatabase) _ValidateAssetsDatabase[entry.Key] = entry.Value;
                    AssetsDatabase.Clear();
                }
                else
                {
                    ClearNotLoadedAssetsFromFiles(importerSettings);
                }

                AssetsDatabase = _ValidateAssetsDatabase;
                SaveDatabase();
            }
        }

        internal void SaveDatabase()
        {
            lock (_lock)
            {
                DatabaseVersion = EAIDatabaseManager.DatabaseVersion;
                EAI.Logger.Info($"Saving the database at {_DatabasePath}, saving {AssetsDatabase.Count} assets.");
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
                List<EAIAsset> tempAssets = new(AssetsDatabase.Values);
                EAI.Logger.Info($"Going to remove unused asset from database, number of asset : {AssetsDatabase.Count}");
                foreach (EAIAsset asset in tempAssets)
                {
                    if (asset.AssetPath == null) continue;

                    // The source mod is still installed but wasn't re-registered this session (most likely
                    // disabled in the playset, since a disabled mod's OnLoad never runs). Leave the generated
                    // asset and its database entry untouched so it comes back instantly if re-enabled, instead
                    // of treating it the same as a genuinely uninstalled mod. The trailing sweep below already
                    // carries anything left in AssetsDatabase forward into _ValidateAssetsDatabase.
                    if (!string.IsNullOrEmpty(asset.SourceModPath) && Directory.Exists(asset.SourceModPath))
                        continue;

                    string path = Path.Combine(importerSettings.dataBase.rootPath, asset.AssetPath);
                    if (Directory.Exists(path))
                    {
                        if (!AssetsDatabase.Remove(asset.AssetID))
                        {
                            EAI.Logger.Warn($"Failed to remove an unloaded asset at path {path} from the database.");
                            continue;
                        }

                        // Making sure that if a prefab is their and loaded in the prefab system, it is removed from it.
                        SearchFilter<PrefabAsset> searchFilter = SearchFilter<PrefabAsset>.ByCondition(a => {
                            try
                            {
                                if (a is null)
                                {
                                    EAI.Logger.Warn($"Found a null prefab asset when trying to remove unused assets from database.");
                                    return false;
                                }

                                if (a.subPath is null)
                                {
                                    EAI.Logger.Warn($"Found a prefab asset with a null subpath when trying to remove unused assets from database.{a}");
                                    return false;
                                }
                                string pathA = a.subPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                                string pathB = Path.DirectorySeparatorChar + asset.AssetPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                                return pathA.Contains(pathB) && !ActualDatabasePath.Contains(pathA);
                            }
                            catch (Exception ex)
                            {
                                EAI.Logger.Warn($"Exception when trying to compare prefab asset paths when removing unused assets from database. Exception : {ex}");
                                return false;
                            }
                        });

                        IEnumerable<PrefabAsset> prefabAssets = AssetDatabase.user.GetAssets<PrefabAsset>(searchFilter)
                            .Concat(importerSettings.dataBase.GetAssets<PrefabAsset>(searchFilter));

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
                    else EAI.Logger.Warn($"Trying to delete an unloaded asset at path {path}, but this path doesn't exist.");
                }
                EAI.Logger.Info($"Removed unused asset from database, number of asset in database now : {AssetsDatabase.Count}.");
                foreach (KeyValuePair<string, EAIAsset> entry in AssetsDatabase) _ValidateAssetsDatabase[entry.Key] = entry.Value;
                AssetsDatabase.Clear();
            }
        }

        internal void AddOrValidateAsset(EAIAsset asset)
        {
            lock (_lock)
            {
                AssetsDatabase.Remove(asset.AssetID);

                if (_ValidateAssetsDatabase.Remove(asset.AssetID))
                {
                    EAI.Logger.Warn($"Validating an already validated asset {asset.AssetID}, removing the old one and adding the new one.");
                }

                _ValidateAssetsDatabase[asset.AssetID] = asset;
            }
        }

        internal EAIAsset GetEAIAsset(string AssetID)
        {
            if (TryGetEAIAsset(AssetID, out EAIAsset asset)) return asset;

            EAI.Logger.Warn($"Tried to get an asset with this ID '{AssetID}', but it's not in the dataBase");
            return asset;
        }

        internal bool TryGetEAIAsset(string AssetID, out EAIAsset asset)
        {
            lock (_lock)
            {
                return AssetsDatabase.TryGetValue(AssetID, out asset);
            }
        }

        internal List<EAIAsset> GetAllAssets()
        {
            lock (_lock)
            {
                return new List<EAIAsset>(AssetsDatabase.Values);
            }
        }

        internal void ClearDatabase()
        {
            lock (_lock)
            {
                EAI.Logger.Info($"Clearing database at {this._DatabasePath}.");
                _ValidateAssetsDatabase.Clear();
                AssetsDatabase.Clear();
                SaveDatabase();
                Directory.Delete(ActualDatabasePath, true);
            }
        }

    }

    public struct EAIAsset
    {
        public static EAIAsset Null => default;
        public string AssetID { get; set; }
        public string PrefabID { get; set; }
        public int SourceAssetHash { get; set; }
        public int BuildAssetHash { get; set; }
        public string AssetPath { get; set; }
        public string SourceModPath { get; set; }

        public EAIAsset(string assetID, int assetHash, string assetPath, string prefabID = null, string sourceModPath = null)
        {
            AssetID = assetID;
            SourceAssetHash = assetHash;
            AssetPath = assetPath;
            BuildAssetHash = 0;
            PrefabID = prefabID;
            SourceModPath = sourceModPath;
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