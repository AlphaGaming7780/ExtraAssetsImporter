using Colossal;
using Colossal.AssetPipeline;
using Colossal.AssetPipeline.Importers;
using Colossal.IO;
using Colossal.IO.AssetDatabase;
using Colossal.IO.AssetDatabase.Internal;
using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.Components;
using ExtraAssetsImporter.AssetImporter.Importers;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.AssetImporter.JSONs.Prefabs;
using ExtraAssetsImporter.AssetImporter.Utils;
using ExtraAssetsImporter.Database;
using ExtraAssetsImporter.OldImporters;
using ExtraLib;
using ExtraLib.Debugger;
using ExtraLib.Helpers;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tutorials;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter
{
    static class AssetsImporterManager
    {
        private static readonly Dictionary<Type, ImporterBase> s_PreImporters = new();
        private static readonly Dictionary<Type, ImporterBase> s_Importers = new();
        private static readonly Dictionary<Type, ComponentImporter> s_ComponentImporters = new();
        // Every mod path ever registered via AddAssetFolder, kept for the lifetime of the session.
        // RunImportPass reads straight from this so ReloadCustomAssets can re-run a full import
        // pass over every known mod path without needing a mod's OnLoad to run again.
        private static readonly HashSet<string> s_RegisteredModPaths = new();
        // The mod paths registered since the last pass started, which a late pass imports.
        private static readonly List<string> s_PendingModPaths = new();

        public static IReadOnlyDictionary<Type, ImporterBase> PreImporters => s_PreImporters;
        public static IReadOnlyDictionary<Type, ImporterBase> Importers => s_Importers;
        public static IReadOnlyDictionary<Type, ComponentImporter> ComponentImporters => s_ComponentImporters;

        public const string k_TemplateFolderName = "_DefaultJson";

        private static readonly ConcurrentDictionary<(Type baseType, Assembly assembly), List<Type>> s_DerivedTypesCache = new();

        // 1 while a new-importers pass is queued or running. Set before the pass starts so a second
        // ReloadDatabase click can't slip in between the button press and the importers flipping
        // their own flags.
        private static int s_NewImportersPassRunning;

        private static bool s_ImportStarted;
        private static bool s_LatePassPending;

        public static bool IsImporting => IsPassRunning || s_LatePassPending;

        // Every import of the session so far has ended, late passes included.
        internal static bool IsImportFinished => s_ImportStarted && !IsImporting;

        private static bool IsPassRunning => Volatile.Read(ref s_NewImportersPassRunning) != 0 || !AreImportersFinished();

        // A compatibility a pass takes holds for every later pass of the session, late ones
        // included, until a pass takes another. The setting goes back to None once a pass takes it.
        internal static EAIOldImportersCompatibility OldImportersCompatibility { get; private set; }
        internal static EAINewImportersCompatibility NewImportersCompatibility { get; private set; }

        // Set for the rest of the session once an importer or a pass fails. What failed never
        // validated its assets, so the quit cleanup deletes nothing.
        private static volatile bool s_ImportFailed;

        internal static bool HasImportFailed => s_ImportFailed;

        internal static void MarkImportFailed()
        {
            s_ImportFailed = true;
        }

        public static bool AddImporter<T>() where T : ImporterBase, new()
        {
            if (s_Importers.ContainsKey(typeof(T))) return false;

            T importer = new T();

            if(importer.PreImporter) s_PreImporters.Add(typeof(T), importer);
            else s_Importers.Add(typeof(T), importer);

            return true;
        }

        public static bool AddComponentImporter<T>() where T : ComponentImporter, new()
        {
            if (s_ComponentImporters.ContainsKey(typeof(T))) return false;
            T importer = new T();
            s_ComponentImporters.Add(typeof(T), importer);
            return true;
        }

        public static bool AddAssetFolder(string path)
        {
            path = PathUtils.Normalize(path);
            // Ignore path that start with "."
            if (Path.GetDirectoryName(path).StartsWith(".")) return false;

            if (!s_RegisteredModPaths.Add(path)) return false;

            s_PendingModPaths.Add(path);

            // The pack of a mod that loads after the import started (deferred mod loading).
            if (s_ImportStarted) ScheduleLatePass();

            return true;
        }

        private static void ScheduleLatePass()
        {
            if (s_LatePassPending) return;

            s_LatePassPending = true;
            GameManager.instance.StartCoroutine(RunLatePass());
        }

        // Starts a frame later, so that the packs of mods loading together share it, and once the
        // running pass ends, since the importers read their folder lists while a pass runs.
        private static IEnumerator RunLatePass()
        {
            yield return null;

            while (IsPassRunning) yield return null;

            s_LatePassPending = false;

            // The pass that just ended may have taken them.
            if (s_PendingModPaths.Count == 0) yield break;

            EAI.Logger.Info($"Importing the {s_PendingModPaths.Count} mod folders registered since the last pass.");
            RunImportPass(includeOldImporters: true, isLatePass: true);
        }

        public static List<ComponentImporter> GetComponentImportersForPrefab<T>()
        {
            return GetComponentImportersForPrefab(typeof(T));
        }

        public static List<ComponentImporter> GetComponentImportersForPrefab(Type prefabType)
        {
            List<ComponentImporter> importers = new List<ComponentImporter>();
            foreach (ComponentImporter importer in s_ComponentImporters.Values)
            {
                if (importer.PrefabType == prefabType || GetOrCacheDerivedTypes(importer.PrefabType).Contains(prefabType))
                {
                    importers.Add(importer);
                }
            }
            return importers;
        }

        /// <summary>
        /// Component types ProcessComponentImporters will add to prefabBase, so they can be created up front in a
        /// single main-thread round-trip.
        /// </summary>
        internal static IEnumerable<Type> GetComponentTypesToProcess(Variant prefabJson, PrefabBase prefabBase)
        {
            Variant componentsVariant = prefabJson?.TryGet(nameof(PrefabJson.Components));
            if (componentsVariant == null) yield break;

            foreach (ComponentImporter importer in s_ComponentImporters.Values)
            {
                if (componentsVariant.TryGetValue(importer.ComponentType.FullName, out _)
                    && (importer.PrefabType == prefabBase.GetType() || GetOrCacheDerivedTypes(importer.PrefabType).Contains(prefabBase.GetType())))
                    yield return importer.ComponentType;
            }
        }

        internal static void ProcessComponentImporters(PrefabImportData data, Variant prefabJson, PrefabBase prefabBase)
        {
            if(prefabJson == null)
            {
                EAI.Logger.Info($"The asset {data.FullAssetName} doesn't have any JSON data to process.");
                return;
            }

            Variant componentsVariant = prefabJson.TryGet(nameof(PrefabJson.Components));

            if(componentsVariant == null)
            {
                EAI.Logger.Info($"The {prefabBase.GetType().Name} of {data.FullAssetName} doesn't have any components to process.");
                return;
            }

            foreach (ComponentImporter importer in s_ComponentImporters.Values)
            {
                if (componentsVariant.TryGetValue(importer.ComponentType.FullName, out Variant componentJson))
                {
                    if (importer.PrefabType != prefabBase.GetType() && !GetOrCacheDerivedTypes(importer.PrefabType).Contains(prefabBase.GetType()))
                    {
                        EAI.Logger.Warn($"The component importer {importer.ComponentType.FullName} is not compatible with the prefab type {prefabBase.GetType().FullName} for the asset {data.FullAssetName}.");
                        continue;
                    }
                    importer.Process(data, componentJson, prefabBase);
                }
            }
        }

        public static void ImportCustomAssets()
        {
            s_ImportStarted = true;

            // Registering + populating the asset database can take a while: don't freeze the main thread on it.
            // The import pass then goes back to the main thread, since it starts the old importers' coroutines.
            // IsImporting is already true meanwhile (no importer is marked loaded yet), so ReloadDatabase stays greyed out.
            PerformanceCounter databaseCounter = new();

            EAIDatabaseManager.LoadDatabase()
                .ContinueWith(_ =>
                {
                    EAI.Logger.Info($"[Perf] EAI asset database loaded (register + populate) in {databaseCounter.result.TotalSeconds:F2}s.");
                    return MainThreadHelper.RunOnMainThread(() => RunImportPass(includeOldImporters: true, isLatePass: false));
                })
                .Unwrap()
                .ContinueWith(
                    t => EAI.Logger.Error($"Error while starting the custom assets import: {t.Exception}"),
                    TaskContinuationOptions.OnlyOnFaulted
                );
        }

        public static void ReloadCustomAssets()
        {
            EAI.Logger.Info("Reloading all custom assets (new importers only).");

            RunImportPass(includeOldImporters: false, isLatePass: false);
        }

        private static void RunImportPass(bool includeOldImporters, bool isLatePass)
        {
            bool runNewImporters = EAI.m_Setting.UseNewImporters;
            if (runNewImporters && Interlocked.CompareExchange(ref s_NewImportersPassRunning, 1, 0) != 0)
            {
                EAI.Logger.Warn("An import pass is already running, ignoring this one.");
                return;
            }

            // A pass that fails before its task starts must still end, or IsImporting stays true
            // and no later pass or Reload can run.
            try
            {
                StartImportPass(includeOldImporters, isLatePass, runNewImporters);
            }
            catch (Exception e)
            {
                MarkImportFailed();
                EAI.Logger.Error($"The import pass failed to start.\n{e}");
                if (runNewImporters) Interlocked.Exchange(ref s_NewImportersPassRunning, 0);
            }
        }

        private static void StartImportPass(bool includeOldImporters, bool isLatePass, bool runNewImporters)
        {
            bool runOldImporters = includeOldImporters && EAI.m_Setting.UseOldImporters;

            // A choice still pending in the settings replaces the stored one, for the importers the
            // pass runs. A late pass imports only the late mods, so it leaves both choices for a
            // pass over every mod: the next boot, or a Reload for the new pick.
            if (runOldImporters && !isLatePass && EAI.m_Setting.OldImportersCompatibilityDropDown != EAIOldImportersCompatibility.None) OldImportersCompatibility = EAI.m_Setting.OldImportersCompatibilityDropDown;
            if (runNewImporters && !isLatePass && EAI.m_Setting.NewImportersCompatibilityDropDown != EAINewImportersCompatibility.None) NewImportersCompatibility = EAI.m_Setting.NewImportersCompatibilityDropDown;

            // A late pass takes only the folders registered since the last pass started.
            // AddCustomAssetsFolder skips a path an importer already has.
            IEnumerable<string> paths = isLatePass ? s_PendingModPaths : s_RegisteredModPaths;

            foreach (string path in paths)
            {
                foreach (ImporterBase importer in s_PreImporters.Values.Concat(s_Importers.Values))
                {
                    importer.AddCustomAssetsFolder(path);
                }

                // A mod without one of these folders no longer holds any of that old importer's
                // assets, so the quit cleanup deletes their cache whichever importers run.
                // The old surfaces cache none.
                if (!Directory.Exists(Path.Combine(path, "CustomDecals"))) EAIDatabaseManager.eaiDatabase.AddScannedFolder(Path.Combine(path, "CustomDecals"));
                if (!Directory.Exists(Path.Combine(path, "CustomNetLanes"))) EAIDatabaseManager.eaiDatabase.AddScannedFolder(Path.Combine(path, "CustomNetLanes"));
            }

            s_PendingModPaths.Clear();

            // The game's ImporterCache fills its importer list on first use without a lock, so a
            // worker thread that reaches it while another fills it finds no PNG importer.
            // Filling it here, on the main thread, keeps the importers' workers from racing on it.
            ImporterCache.CacheSupportedExtensions();

            TextureImportSlots.SetCount(EAI.m_Setting.GetMaxConcurrentTextureImports(out string concurrencyReason));
            EAI.Logger.Info($"Importing up to {TextureImportSlots.Count} textures at once ({concurrencyReason}).");

            EAI.Logger.Info("Start loading custom stuff.");

            if (runOldImporters)
            {
                if (EAI.m_Setting.Decals) GameManager.instance.StartCoroutine(DecalsImporter.CreateCustomDecals());
                if (EAI.m_Setting.Surfaces) GameManager.instance.StartCoroutine(SurfacesImporter.CreateCustomSurfaces());
                if (EAI.m_Setting.NetLanes) GameManager.instance.StartCoroutine(NetLanesDecalImporter.CreateCustomNetLanes());
            }

            // Load the custom assets with the new importers
            if (runNewImporters)
            {
                // Auto load custom assets into new importer if they have the correct folder names
                // !!!!!!!!!!!!!!! Have to rework that, it laoding all Localization in any mod, if there is a Localization folder !!!!!!!!!!!!!!!
                //AutoImportCustomAssets();
                ImporterSettings importerSettings = ImporterSettings.GetDefault();

                Task task = AssetsImporterManager.LoadCustomAssetsAsync(importerSettings);

                task.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        MarkImportFailed();
                        EAI.Logger.Error($"Error while loading custom assets with new importers: {t.Exception}");
                    }

                    Interlocked.Exchange(ref s_NewImportersPassRunning, 0);
                });
            }
            else if (includeOldImporters)
            {
                GameManager.instance.StartCoroutine(AssetsImporterManager.WaitForOldImportersOnlyToFinish(ImporterSettings.GetDefault()));
            }
        }

        // The pass waits for its importers to end, so it runs on its own thread, off the .NET pool.
        public static Task LoadCustomAssetsAsync(ImporterSettings importerSettings)
        {
            return Task.Factory.StartNew(() => LoadCustomAssetsAsync_Impl(importerSettings), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private static void LoadCustomAssetsAsync_Impl(ImporterSettings importerSettings)
        {
            PerformanceCounter totalPerformanceCounter = new();

            // Aggregates every importer's stats (they are its children), logged at the end of the pass.
            PerformanceStats passStats = new("Import pass");
            using PerformanceStats.CurrentScope currentStats = passStats.MakeCurrent();
            using MemorySampler passMemory = new();

            ImportersUtils.ClearCaches();

            // Frames elapsed during the pass: each blocking main-thread call costs up to one frame.
            int startFrame = MainThreadHelper.RunOnMainThread(() => Time.frameCount).Result;

            using (passStats.Measure("Pass.CreateLocalAssetPack"))
                CreateEAILocalAssetPackPrefab();

            EAI.Logger.Info("Starting the loading of pre importers.");

            PerformanceCounter preImportersPerformanceCounter = new();

            // In registration order and one at a time, so that the packs' localization files fill
            // PackFallbackKeys before the asset packs' default names check it.
            // An importer returns only after its main-thread calls have run.
            foreach (ImporterBase importer in s_PreImporters.Values)
                importer.LoadCustomAssets(importerSettings, passStats);

            EAI.Logger.Info($"The loading of pre importers has finished. Took {preImportersPerformanceCounter.result.TotalSeconds:F2}s.");
            EAI.Logger.Info("Starting the loading of importers.");

            PerformanceCounter importersPerformanceCounter = new();

            // Each importer gets its own thread too: Parallel.ForEach runs its first worker on the
            // thread that calls it (TaskReplicator in the game's mscorlib), and an asset blocks on
            // texture slots and main-thread calls.
            var tasks = s_Importers.Values
                .Select(importer => Task.Factory.StartNew(() => importer.LoadCustomAssets(importerSettings, passStats), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))
                .ToArray();

            Task.WaitAll(tasks);

            // At this point the pre-importers above have returned and every importer Task has
            // completed, so this only waits on the legacy (old importers) coroutines, which have no
            // completion event to await.
            while (!AreImportersFinished())
            {
                Thread.Sleep(50);
            }

            EAI.Logger.Info($"The loading of importers has finished. Took {importersPerformanceCounter.result.TotalSeconds:F2}s.");

            EAI.m_Setting.ResetCompatibility(OldImportersCompatibility, NewImportersCompatibility);

            using (passStats.Measure("Pass.SaveDatabase"))
                importerSettings.eaiDatabase.SaveValidateDatabase(importerSettings);

            int frames = MainThreadHelper.RunOnMainThread(() => Time.frameCount).Result - startFrame;
            double seconds = totalPerformanceCounter.result.TotalSeconds;

            EAI.Logger.Info($"All custom assets have been loaded. Took {seconds:F2}s, {frames} frames ({(seconds > 0 ? frames / seconds : 0):F1} fps), savePrefabs={importerSettings.savePrefabs}.");
            EAI.Logger.Info($"{passStats.BuildReport()}\n{passMemory.Stop()}");

        }

        internal static IEnumerator WaitForOldImportersOnlyToFinish(ImporterSettings importerSettings)
        {

            while (
                (EAI.m_Setting.UseOldImporters && EAI.m_Setting.Decals && !DecalsImporter.DecalsLoaded) ||
                (EAI.m_Setting.UseOldImporters && EAI.m_Setting.Surfaces && !SurfacesImporter.SurfacesIsLoaded) ||
                (EAI.m_Setting.UseOldImporters && EAI.m_Setting.NetLanes && !NetLanesDecalImporter.NetLanesLoaded)
            )
            {
                yield return null;
            }

            EAI.Logger.Info("The loading of the old importers has finished.");
            EAI.m_Setting.ResetCompatibility(OldImportersCompatibility, NewImportersCompatibility);
            EAIDatabaseManager.eaiDatabase.SaveValidateDatabase(importerSettings);

            yield break;
        }

        private static void CreateEAILocalAssetPackPrefab()
        {
            string packName = $"ExtraAssetsImporter {AssetPackImporter.kAssetEndName}";

            // All in one main-thread call: AddPrefab registers the prefab synchronously, so it can be looked up
            // as soon as this returns — no frame waiting needed (WaitXFrames isn't thread-safe anyway).
            MainThreadHelper.RunOnMainThread(() =>
            {
                if (EL.m_PrefabSystem.TryGetPrefab(new PrefabID(typeof(AssetPackPrefab).Name, packName), out _))
                    return;

                AssetPackPrefab assetPackPrefab = ScriptableObject.CreateInstance<AssetPackPrefab>();
                assetPackPrefab.name = packName;

                UIObject assetPackUI = assetPackPrefab.AddComponent<UIObject>();
                assetPackUI.m_Icon = Icons.GetIcon(assetPackPrefab);

                EL.m_PrefabSystem.AddPrefab(assetPackPrefab);
            }).Wait();
        }

        public static void ExportImportersTemplate()
        {
            string path = Path.Combine(EAI.pathModsData, k_TemplateFolderName);

            if (Directory.Exists(path)) Directory.Delete(path, true);

            Directory.CreateDirectory(path);
            foreach (ImporterBase importer in s_PreImporters.Values.Concat(s_Importers.Values))
            {
                importer.ExportTemplate(path);
            }
            TextureJson textureJson = new TextureJson()
            {
                path = "ImporterID\\Category\\AssetName",
                CID = new()
            };
            File.WriteAllText(Path.Combine(path, "TextureSharing.json"), Encoder.Encode(textureJson, EncodeOptions.None));

//#if DEBUG
            Settings settings = Settings.GetDefault("TEST_SETTING");
            File.WriteAllText(Path.Combine(path, "settings.json"), Encoder.Encode(settings, EncodeOptions.None));

            string matPath = Path.Combine(path, $"MaterialProperties");
            Directory.CreateDirectory(matPath);


            static object GetMaterialValue(Material material,  string key, UnityEngine.MaterialPropertyType type)
            {
                return type switch
                {
                    UnityEngine.MaterialPropertyType.Vector => material.GetVector(key),
                    UnityEngine.MaterialPropertyType.Float => material.GetFloat(key),
                    UnityEngine.MaterialPropertyType.Int => material.GetInt(key),
                    _ => null,
                };
            }

            foreach (MaterialLibrary.MaterialDescription material in AssetDatabase.global.resources.materialLibrary.m_Materials)
            {
                EAI.Logger.Info(material.m_Material.name);

                EAI.Logger.Info($"{material.m_Material.name} | Shader name : {material.m_Material.shader.name}");

                Dictionary<UnityEngine.MaterialPropertyType, Dictionary<string, object>> properties = new();

                foreach (UnityEngine.MaterialPropertyType type
                         in Enum.GetValues(typeof(UnityEngine.MaterialPropertyType)))
                {
                    properties.Add(type, new Dictionary<string, object>());
                    foreach (string s in material.m_Material.GetPropertyNames(type))
                        properties[type].Add(s, GetMaterialValue(material.m_Material, s, type));
                }
                File.WriteAllText(Path.Combine(matPath, $"{material.m_Material.name}.json"), Encoder.Encode(properties, EncodeOptions.None));

            }
//#endif
        }

        private static List<Type> GetOrCacheDerivedTypes(Type baseType)
        {
            return GetOrCacheDerivedTypes(baseType, Assembly.GetAssembly(baseType));
        }

        // Assembly.GetTypes() + the LINQ filter below is a slow reflection scan whose result never changes
        // for a given (baseType, assembly) pair, but GetComponentImportersForPrefab/ProcessComponentImporters
        // call it per component importer for every prefab processed during an import — cache it.
        private static List<Type> GetOrCacheDerivedTypes(Type baseType, Assembly assembly)
        {
            return s_DerivedTypesCache.GetOrAdd((baseType, assembly), key => key.assembly
                .GetTypes()
                .Where(t => t != key.baseType && key.baseType.IsAssignableFrom(t))
                .ToList());
        }

        public static bool AreImportersFinished()
        {
            // Conditions des anciens importers
            if (EAI.m_Setting.UseOldImporters)
            {
                if (EAI.m_Setting.Decals && !DecalsImporter.DecalsLoaded) return false;
                if (EAI.m_Setting.Surfaces && !SurfacesImporter.SurfacesIsLoaded) return false;
                if (EAI.m_Setting.NetLanes && !NetLanesDecalImporter.NetLanesLoaded) return false;
            }

            if(EAI.m_Setting.UseNewImporters)
            {
                // Pré-importers
                if (s_PreImporters.Values.Any(importer => !importer.AssetsLoaded))
                    return false;

                // Importers
                if (s_Importers.Values.Any(importer => !importer.AssetsLoaded))
                    return false;
            }

            return true;
        }
    }
}
