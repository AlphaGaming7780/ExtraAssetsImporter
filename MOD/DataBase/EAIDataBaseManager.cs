using Colossal;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.PSI.Common;
using ExtraAssetsImporter.AssetImporter;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraLib;
using ExtraLib.ClassExtension;
using ExtraLib.Helpers;
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
        private static string LegacyDatabasePath => Path.GetFullPath(Path.Combine(EAI.pathModsData, "Database"));

        // True when the database lives in a folder named ".Database" (the default layout, also used by relocation).
        internal static bool IsDatabaseInDatabaseFolder
        {
            get
            {
                string path = eaiDatabase?.ActualDatabasePath;
                if (string.IsNullOrEmpty(path)) return false;
                return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) == databaseFolderName;
            }
        }

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

            EAI.ClearTempFolder();

            return RegisterEaiAssetDatabase();
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

            const string notifId = "EAI.PopulatingDatabase";
            EL.m_NotificationUISystem.AddOrUpdateNotificationOnMainThread(notifId, "Extra Assets Importer", "Populating Extra Assets Importer Asset Database...", null, ProgressState.Indeterminate);

            // The progress callback and the continuation below run on background threads: notifications are dispatched
            // without waiting on them (nothing here needs their result, and a caller blocking the main thread on the
            // returned Task would otherwise deadlock). Dispatched actions run in no guaranteed order, so this flag (only
            // touched on the main thread) stops a late progress update from showing the notification again after removal.
            bool notifRemoved = false;
            void action(ProgressTracker tracker)
            {
                int progress = (int)math.round(tracker.progress * 100);
                MainThreadHelper.RunOnMainThread(() =>
                {
                    if (!notifRemoved) EL.m_NotificationUISystem.AddOrUpdateNotification(notifId, progress: progress);
                });
            }
            void removeNotif(string text, ProgressState progressState)
            {
                MainThreadHelper.RunOnMainThread(() =>
                {
                    notifRemoved = true;
                    EL.m_NotificationUISystem.RemoveNotification(notifId, 0.5f, text: text, progressState: progressState, progress: progressState == ProgressState.Complete ? 100 : null);
                });
            }
            TaskProgress taskProgress = new(action);

            Task task = AssetDatabase.global.RegisterDatabase(EAIAssetDatabase).ContinueWith((t) =>
            {
                if (t.IsFaulted)
                {
                    EAI.Logger.Error($"Failed to register the Extra Assets Importer Asset Database : {t.Exception}");
                    removeNotif("Failed to register the Extra Assets Importer Asset Database.", ProgressState.Failed);
                }

                else
                {
                    Task task2 = EAIAssetDatabase.PopulateFromDataSource(false, cancellationToken, taskProgress);

                    task2.Wait();

                    if (task2.IsFaulted)
                    {
                        EAI.Logger.Error($"Failed to populate the Extra Assets Importer Asset Database : {task2.Exception}");
                        removeNotif("Failed to populate the Extra Assets Importer Asset Database.", ProgressState.Failed);
                        return;
                    }

                    removeNotif("Extra Assets Importer Asset Database populated.", ProgressState.Complete);
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
        // mods present but disabled in the current playset (contrast with
        // EAIDatabase.ClearNotImportedAssets, which deletes for a mod that's genuinely gone).
        // Re-enabling a mod needs no separate "re-add" path:
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


        // Hash of a mod's source asset folder. Includes the last write time so an edit that keeps the file size
        // (retouched texture, JSON value changed to one of the same length) still triggers a rebuild.
        internal static int GetAssetHash(string assetFolder)
        {
            return ComputeFolderHash(assetFolder, includeLastWriteTime: true);
        }

        // Hash of the files EAI generated in the database. Name + size only: the write time changes every time EAI
        // itself rebuilds or saves the asset, which would make this hash useless for detecting outside tampering.
        internal static int GetBuildAssetHash(string assetFolder)
        {
            return ComputeFolderHash(assetFolder, includeLastWriteTime: false);
        }

        private static int ComputeFolderHash(string assetFolder, bool includeLastWriteTime)
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
                if (includeLastWriteTime) hash = unchecked(hash * 31 + file.LastWriteTimeUtc.Ticks.GetHashCode());
            }
            return hash;
        }

        internal static void CheckIfDatabaseNeedToBeRelocated()
        {
            // Nothing loaded yet (e.g. OnDispose before Initialize): LoadDatabase runs this check itself when it does
            // load, so loading and registering the database here would be wasted work at best.
            if (eaiDatabase == null) return;

            if(string.IsNullOrEmpty(EAI.m_Setting.SavedDatabasePath) || EAI.m_Setting.SavedDatabasePath == eaiDatabase.ActualDatabasePath) return;

            // The settings file syncs between computers, so this path can come from another one.
            if(!Directory.Exists(EAI.m_Setting.SavedDatabasePath))
            {
                EAI.Logger.Warn($"The selected directory for database relocation doesn't exist on this computer, keeping the database where it is: {EAI.m_Setting.SavedDatabasePath}");
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

        // Every mod of the playset has loaded, the deferred ones included, so every pack had its
        // chance to register its folder.
        internal static bool IsPlaysetLoaded()
        {
            return AssetDatabase<ParadoxMods>.instance?.isCached == true;
        }

        internal static string ComparablePath(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        // EAI.pathModsData mixes '/' and '\': Path.Combine adds '\' to EnvPath.kUserDataPath,
        // Unity's persistentDataPath, which uses '/'. A legacy path saved from it does too, so
        // only normalized paths can match.
        private static bool IsLegacyDatabasePath(string path)
        {
            return !string.IsNullOrEmpty(path) && string.Equals(ComparablePath(path), LegacyDatabasePath, StringComparison.OrdinalIgnoreCase);
        }

        // Before 1.7.4 the database folder was "Database". The game scans that folder at boot as
        // user content and loads every cached prefab a second time.
        internal static void MoveLegacyDatabaseFolder()
        {
            if (eaiDatabase == null) return;

            string legacyPath = LegacyDatabasePath;

            if (!IsLegacyDatabasePath(eaiDatabase.ActualDatabasePath)) return;

            if (!RelocateAssetDatabase(defaultDatabasePath)) return;

            EAI.Logger.Info($"Moved the database from {legacyPath} to {defaultDatabasePath}.");

            if (IsLegacyDatabasePath(EAI.m_Setting.SavedDatabasePath))
            {
                EAI.m_Setting.SavedDatabasePath = defaultDatabasePath;
                EAI.m_Setting.Apply();
            }
        }

        // At quit, the game saves the settings before the mods dispose, so this quit doesn't save
        // the change the move makes to the setting. Once the legacy folder is gone, the next load
        // makes the change again, and that session's quit saves it.
        internal static void ForgetMovedLegacyDatabasePath()
        {
            if (IsLegacyDatabasePath(EAI.m_Setting.SavedDatabasePath) && !Directory.Exists(LegacyDatabasePath))
                EAI.m_Setting.SavedDatabasePath = defaultDatabasePath;
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
        // Databases saved before version 4 name this key ActualDataBasePath. Reading it keeps their
        // location, which the legacy folder move and the relocation start from. Their other keys
        // stay unread: their assets were stored in another shape and hashed another way, so they
        // rebuild anyway.
        [DecodeAlias("ActualDataBasePath")]
        public string ActualDatabasePath = EAIDatabaseManager.defaultDatabasePath;
        public Dictionary<string, EAIAsset> AssetsDatabase = new Dictionary<string, EAIAsset>();
        private readonly Dictionary<string, EAIAsset> _ValidateAssetsDatabase = new Dictionary<string, EAIAsset>();
        // The assets an import validated this session, which ClearNotImportedAssets keeps.
        private readonly HashSet<string> _ImportedAssetIDs = new HashSet<string>();
        // The importer folders an import went through this session, or found missing, as
        // ComparablePath gives them.
        private readonly HashSet<string> _ScannedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal string _DatabasePath = null;

        private readonly object _lock = new object();


        internal void SaveValidateDatabase(ImporterSettings importerSettings)
        {
            lock (_lock)
            {
                // A pass may not see every pack: a late one imports only the new folders, and the
                // first one runs before the packs of deferred mods register. The assets it didn't
                // see stay, and ClearNotImportedAssets deletes at quit those whose source is gone.
                foreach (KeyValuePair<string, EAIAsset> entry in AssetsDatabase) _ValidateAssetsDatabase[entry.Key] = entry.Value;
                AssetsDatabase.Clear();

                // Copy, never alias: if both fields pointed at the same dictionary, the next pass (ReloadDatabase)
                // would see freshly validated assets as "not loaded" and the AssetsDatabase.Clear() above would wipe both.
                AssetsDatabase = new Dictionary<string, EAIAsset>(_ValidateAssetsDatabase);
                _ValidateAssetsDatabase.Clear();
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


        // Deletes the cache of each asset no import saw this session whose source is gone, as
        // IsRemoved decides. Runs when the game quits, after the packs of deferred mods had their
        // own pass.
        internal void ClearNotImportedAssets()
        {
            lock (_lock)
            {
                if (!EAI.m_Setting.DeleteNotLoadedAssets) return;

                List<EAIAsset> notImported = AssetsDatabase.Values.Where(asset => !_ImportedAssetIDs.Contains(asset.AssetID) && IsRemoved(asset)).ToList();

                if (notImported.Count == 0) return;

                foreach (EAIAsset asset in notImported)
                {
                    AssetsDatabase.Remove(asset.AssetID);

                    if (asset.AssetPath == null) continue;

                    string path = Path.Combine(ActualDatabasePath, asset.AssetPath);
                    if (Directory.Exists(path)) DeleteAssetFolder(ActualDatabasePath, path);
                }

                EAI.Logger.Info($"Removed {notImported.Count} assets no import of this session saw from the database.");
                SaveDatabase();
            }
        }

        // An asset no import saw is gone when its mod is, or when its importer folder is missing or
        // an import went through it without it.
        // A mod disabled in the playset keeps its folder, and a disabled importer skips it, so
        // their assets keep their cache and come back without a rebuild.
        private bool IsRemoved(EAIAsset asset)
        {
            if (string.IsNullOrEmpty(asset.SourceModPath) || !Directory.Exists(asset.SourceModPath)) return true;
            if (string.IsNullOrEmpty(asset.AssetPath)) return false;

            // An asset's path starts with the name of its importer folder.
            string importerFolder = Path.Combine(asset.SourceModPath, asset.AssetPath.Split('\\', '/')[0]);
            return _ScannedFolders.Contains(EAIDatabaseManager.ComparablePath(importerFolder));
        }

        // Records an importer folder an import went through without a throw, or one its mod lacks,
        // so that the quit cleanup deletes the assets the folder no longer holds.
        internal void AddScannedFolder(string folder)
        {
            lock (_lock)
            {
                _ScannedFolders.Add(EAIDatabaseManager.ComparablePath(folder));
            }
        }

        // Deletes an asset's folder, then its pack and category folders once they are empty.
        private static void DeleteAssetFolder(string rootPath, string path)
        {
            Directory.Delete(path, true);

            string root = EAIDatabaseManager.ComparablePath(rootPath);
            DirectoryInfo parent = new DirectoryInfo(path).Parent;

            while (parent != null && parent.FullName.Length > root.Length && parent.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase) && !parent.EnumerateFileSystemInfos().Any())
            {
                parent.Delete();
                parent = parent.Parent;
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
                _ImportedAssetIDs.Add(asset.AssetID);
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