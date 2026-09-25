using Colossal.AssetPipeline;
using Colossal.Core;
using Colossal.IO;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.Localization;
using Colossal.UI;
using ExtraAssetsImporter.ClassExtension;
using ExtraAssetsImporter.Database;
using ExtraLib;
using ExtraLib.Debugger;
using ExtraLib.Helpers;
using ExtraLib.Prefabs;
using Game.Prefabs;
using Game.SceneFlow;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using static Colossal.IO.AssetDatabase.ImageAsset;
using Hash128 = Colossal.Hash128;

namespace ExtraAssetsImporter.AssetImporter
{
    static class ImportersUtils
    {
        private static readonly object s_CreateDirectoryLock = new();

        /// <summary>
        /// Creates <paramref name="assetDataPath"/> in the database before anything is saved in it. Every asset Save
        /// goes through LongDirectory.CreateDirectory, which throws when another thread creates one of the missing
        /// parent folders at the same time (it only tolerates "already exists" on the last folder) — assets of the
        /// same category imported concurrently share their parent folder.
        /// </summary>
        internal static void EnsureAssetDirectory(ILocalAssetDatabase database, string assetDataPath)
        {
            string path = Path.Combine(database.rootPath, assetDataPath);
            if (LongDirectory.Exists(path)) return;
            lock (s_CreateDirectoryLock)
                if (!LongDirectory.Exists(path)) LongDirectory.CreateDirectory(path);
        }


        public static RenderPrefab GetRenderPrefab(PrefabImportData data)
        {
            TryGetCachedPrefab<RenderPrefab>(data, GetRenderPrefabName(data), GetRenderPrefabFileName(data), out RenderPrefab renderPrefab);
            return renderPrefab;
        }

        // Gives prefab an explicit .asset if it doesn't have one yet, so a later PrefabAsset.Save() elsewhere
        // in the reference graph can't trigger PrefabReferenceResolver's auto-create fallback for it
        // (prefabBase.asset ?? m_Database.AddAsset(...)) — that fallback runs inline on whatever background
        // thread happens to Save() first, with no synchronization, so two importers racing to auto-create
        // the SAME shared prefab (e.g. the UI category/menu chain, shared across every importer) can collide
        // writing the same .cid file. Must be called from the main thread — same requirement as AddAsset.
        public static void EnsurePrefabHasAsset(PrefabBase prefab)
        {
            if (prefab == null || prefab.asset != null) return;

            AssetDataPath assetDataPath = AssetDataPath.Create(EAI.kTempFolderName, $"{prefab.name}_{prefab.GetType().Name}", EscapeStrategy.None);
            EAIDatabaseManager.EAIAssetDatabase.AddAsset<PrefabAsset, ScriptableObject>(assetDataPath, prefab);
        }

        /// <summary>
        /// PrefabBase.AddComponent calls ScriptableObject.CreateInstance, a main-thread-only Unity API: an existing
        /// component is returned straight away, a missing one is created on the main thread.
        /// </summary>
        public static T AddOrGetComponentOnMainThread<T>(this PrefabBase prefab) where T : ComponentBase
        {
            if (prefab.TryGetExactly(out T component)) return component;

            using (PerformanceStats.MeasureCurrent("Blocking.AddComponent"))
                return MainThreadHelper.RunOnMainThread(() => prefab.AddOrGetComponent<T>()).Result;
        }

        /// <summary>
        /// Batched <see cref="AddOrGetComponentOnMainThread"/>: every missing component is created in a single
        /// main-thread round-trip (none when they all exist).
        /// </summary>
        public static void AddOrGetComponentsOnMainThread(this PrefabBase prefab, IEnumerable<Type> componentTypes)
        {
            List<Type> missing = new();
            foreach (Type type in componentTypes)
                if (!missing.Contains(type) && !prefab.TryGetExactly(type, out _)) missing.Add(type);

            if (missing.Count == 0) return;

            using (PerformanceStats.MeasureCurrent("Blocking.AddComponents"))
                MainThreadHelper.RunOnMainThread(() =>
                {
                    foreach (Type type in missing) prefab.AddOrGetComponent(type);
                }).Wait();
        }

        /// <param name="components">Components added (if missing) in the same main-thread round-trip.</param>
        public static T GetOrCreatePrefabInstance<T>(string prefabName, params Type[] components) where T : PrefabBase
        {
            using PerformanceStats.Scope blocking = PerformanceStats.MeasureCurrent("Blocking.GetOrCreatePrefabInstance");
            return MainThreadHelper.RunOnMainThread(() =>
            {
                PrefabID prefabID = new PrefabID(typeof(T).Name, prefabName, Hash128.CreateGuid(prefabName));

                T prefab = EL.m_PrefabSystem.TryGetPrefab(prefabID, out PrefabBase prefabBase) && prefabBase is T existing
                    ? existing
                    : (T)ScriptableObject.CreateInstance(typeof(T));

                foreach (Type component in components) prefab.AddOrGetComponent(component);

                return prefab;
            }).Result;
        }

        public static bool TryParsePrefabID(string value, out PrefabID prefabID)
        {
            prefabID = default;
            if (string.IsNullOrEmpty(value)) return false;

            int typeSeparator = value.IndexOf(':');
            if (typeSeparator < 0) return false;

            string type = value.Substring(0, typeSeparator);
            string rest = value.Substring(typeSeparator + 1);

            int hashStart = rest.LastIndexOf(" (", StringComparison.Ordinal);
            if (hashStart >= 0 && rest[rest.Length - 1] == ')')
            {
                string hashText = rest.Substring(hashStart + 2, rest.Length - hashStart - 3);
                if (Hash128.TryParse(hashText, out Hash128 hash))
                {
                    prefabID = new PrefabID(type, rest.Substring(0, hashStart), hash);
                    return true;
                }
            }

            prefabID = new PrefabID(type, rest);
            return true;
        }

        public static void UnloadPrefab(PrefabID prefabID)
        {
            MainThreadHelper.RunOnMainThread(() =>
            {
                if (!EL.m_PrefabSystem.TryGetPrefab(prefabID, out PrefabBase prefab))
                    return;

                EL.m_PrefabSystem.RemovePrefab(prefab);
                prefab.asset?.Unload();
            }).Wait();
        }

        public static bool TryGetCachedPrefab<T>(PrefabImportData data, string prefabName, string fileName, out T prefab) where T : PrefabBase
        {
            return TryGetCachedPrefab(data.NeedToUpdateAsset, data.ImportSettings, data.AssetDataPath, prefabName, fileName, out prefab);
        }

        public static bool TryGetCachedPrefab<T>(bool needToUpdateAsset, ImporterSettings importerSettings, string assetDataPath, string prefabName, string fileName, out T prefab) where T : PrefabBase
        {
            PrefabID prefabID = new PrefabID(typeof(T).Name, prefabName, Hash128.CreateGuid(prefabName));
            bool found = TryGetCachedPrefab(prefabID, needToUpdateAsset, importerSettings, assetDataPath, fileName, out PrefabBase prefabBase);
            prefab = prefabBase as T;
            return found && prefab != null;
        }

        public static bool TryGetCachedPrefab<T>(ImporterSettings importerSettings, string assetDataPath, string prefabName, string fileName, out T prefab) where T : PrefabBase
        {
            PrefabID prefabID = new PrefabID(typeof(T).Name, prefabName, Hash128.CreateGuid(prefabName));
            bool found = TryGetCachedPrefab(prefabID, importerSettings, assetDataPath, fileName, out PrefabBase prefabBase);
            prefab = prefabBase as T;
            return found && prefab != null;
        }

        // The asset's main prefab isn't added to the prefab system here: PrefabImporterBase adds it with
        // AddOrUpdatePrefab once it's set up. Adding it here too made that AddOrUpdatePrefab an UpdatePrefab, which
        // replaces the entity, possibly in the very frame it was created (crash in AreaInitializeSystem).
        public static bool TryGetCachedPrefab(PrefabImportData data, out PrefabBase prefab)
        {
            if (TryParsePrefabID(data.EAIAsset.PrefabID, out PrefabID prefabID) && TryGetCachedPrefab(prefabID, data.NeedToUpdateAsset, data.ImportSettings, data.AssetDataPath, GetPrefabFileName(data), out prefab, addToPrefabSystem: false))
            {
                return true;
            }
            prefab = null;
            return false;
        }

        public static bool TryGetCachedPrefab(PrefabID prefabID, bool needToUpdateAsset, ImporterSettings importerSettings, string assetDataPath, string fileName, out PrefabBase prefab, bool addToPrefabSystem = true)
        {
            if (needToUpdateAsset)
            {
                UnloadPrefab(prefabID);

                prefab = null;
                return false;
            }

            return TryGetCachedPrefab(prefabID, importerSettings, assetDataPath, fileName, out prefab, addToPrefabSystem);
        }

        // A single main-thread round-trip: the cached asset is loaded off-thread first, then the prefab system is
        // checked and, if the prefab isn't there yet, the loaded instance is added in the same call (when
        // addToPrefabSystem is set).
        public static bool TryGetCachedPrefab(PrefabID prefabID, ImporterSettings importerSettings, string assetDataPath, string fileName, out PrefabBase prefab, bool addToPrefabSystem = true)
        {
            try
            {
                AssetDataPath prefabAssetPath = AssetDataPath.Create(assetDataPath, fileName, true, EscapeStrategy.None);
                if (!importerSettings.dataBase.TryGetOrAddAsset(prefabAssetPath, out PrefabAsset prefabAsset))
                {
                    EAI.Logger.Info($"No cached data for {prefabID}.");
                    return TryGetPrefab(prefabID, out prefab);
                }

                // Already loaded (e.g. database reload): reuse the instance without taking another reference on it.
                PrefabBase loaded = prefabAsset.state == LoadState.Object ? prefabAsset.GetInstance<PrefabBase>() : prefabAsset.Load() as PrefabBase;

                using PerformanceStats.Scope blocking = PerformanceStats.MeasureCurrent("Blocking.CachedPrefab");
                prefab = MainThreadHelper.RunOnMainThread(() =>
                {
                    if (EL.m_PrefabSystem.TryGetPrefab(prefabID, out PrefabBase existing)) return existing;
                    if (loaded == null) return null;
                    if (EL.m_PrefabSystem.TryGetPrefab(loaded.GetPrefabID(), out existing)) return existing;

                    if (addToPrefabSystem) EL.m_PrefabSystem.AddPrefab(loaded);
                    return loaded;
                }).Result;

                if (prefab != null)
                {
                    EAI.Logger.Info(prefab == loaded ? $"Cached data for {prefabID}, loading the cache." : $"{prefabID} was already loaded and in the prefab system.");
                    return true;
                }

                EAI.Logger.Info($"No cached data for {prefabID}.");
            }
            catch (Exception e)
            {
                EAI.Logger.Warn($"Failed to load the cached prefab for {prefabID}.\nException:{e}.");
            }

            prefab = null;
            return false;
        }

        public static string GetPrefabFileName(PrefabImportData data)
        {
            return $"{data.AssetName}{PrefabAsset.kExtension}";
        }

        /// <param name="components">Components setupRenderPrefab needs, created with the render prefab in one main-thread round-trip.</param>
        public static RenderPrefab CreateRenderPrefab(PrefabImportData data, SurfaceAsset surfaceAsset, Mesh[] meshes, Action<PrefabImportData, RenderPrefab, SurfaceAsset> setupRenderPrefab, params Type[] components)
        {
            EAI.Logger.Info($"Creating RenderPrefab for {data.FullAssetName}.");
            surfaceAsset.Save(false);

            string pathToRenderPrefabJson = Path.Combine(data.FolderPath, "RenderPrefab.json");
            Variant renderPrefabVariant = File.Exists(pathToRenderPrefabJson) ? LoadJson(pathToRenderPrefabJson) : null;

            Vector4 MeshSize = surfaceAsset.vectors["colossal_MeshSize"];

            AssetDataPath geometryAssetDataPath = AssetDataPath.Create(data.AssetDataPath, "GeometryAsset", EscapeStrategy.None);
            GeometryAsset geometryAsset = new()
            {
                id = new Identifier(Guid.NewGuid()),
                database = data.ImportSettings.dataBase
            };
            geometryAsset.database.AddAsset<GeometryAsset>(geometryAssetDataPath, geometryAsset.id.guid);
            geometryAsset.SetData(meshes);
            geometryAsset.Save(false);

            RenderPrefab renderPrefab = GetOrCreatePrefabInstance<RenderPrefab>(GetRenderPrefabName(data), components);

            renderPrefab.name = GetRenderPrefabName(data);
            renderPrefab.geometryAsset = geometryAsset;
            renderPrefab.surfaceAssets = new[] { surfaceAsset };
            renderPrefab.bounds = new(new(-MeshSize.x * 0.5f, -MeshSize.y * 0.5f, -MeshSize.z * 0.5f), new(MeshSize.x * 0.5f, MeshSize.y * 0.5f, MeshSize.z * 0.5f));
            renderPrefab.meshCount = geometryAsset.data.meshCount;
            renderPrefab.vertexCount = geometryAsset.GetVertexCount(0);
            renderPrefab.indexCount = 1;
            renderPrefab.manualVTRequired = false;

            setupRenderPrefab(data, renderPrefab, surfaceAsset);

            if(renderPrefabVariant != null) AssetsImporterManager.ProcessComponentImporters(data, renderPrefabVariant, renderPrefab);

            AssetDataPath renderPrefabAssetPath = AssetDataPath.Create(data.AssetDataPath, GetRenderPrefabFileName(data), true, EscapeStrategy.None);
            PrefabAsset renderPrefabAsset = data.ImportSettings.dataBase.AddAsset<PrefabAsset, ScriptableObject>(renderPrefabAssetPath, renderPrefab, Hash128.CreateGuid(renderPrefab.name));
            renderPrefabAsset.Save(); // No UIObject on the render prefab — can't reach the shared UI chain, see PrefabImporterBase.PrefabSaveLock.

            geometryAsset.Unload();
            surfaceAsset.Unload();

            return renderPrefab;
        }

        public static Task SetupLocalisationForPrefab(Dictionary<string, string> localisation)
        {
            LocalizationManager localizationManager = GameManager.instance.localizationManager;

            string localeID = localizationManager.fallbackLocaleId;

            return MainThreadHelper.RunOnMainThread(() => localizationManager.AddSource(localeID, new MemorySource(localisation)));
        }

        /// <summary>
        /// PrefabSystem's lookup tables are plain Dictionary/List mutated by the main thread, so lookups from
        /// the import Tasks must go through the main thread too.
        /// </summary>
        public static bool TryGetPrefab(PrefabID prefabID, out PrefabBase prefab)
        {
            using (PerformanceStats.MeasureCurrent("Blocking.TryGetPrefab"))
                prefab = MainThreadHelper.RunOnMainThread(() =>
                    EL.m_PrefabSystem.TryGetPrefab(prefabID, out PrefabBase prefabBase) ? prefabBase : null).Result;
            return prefab != null;
        }

        public static string GetRenderPrefabName(PrefabImportData data)
        {
            return $"{data.FullAssetName}_RenderPrefab";
        }

        public static string GetRenderPrefabFileName(PrefabImportData data)
        {
            return $"{data.AssetName}_RenderPrefab{PrefabAsset.kExtension}";
        }

        public static T LoadJson<T>(string path) where T : class
        {
            return LoadJson(path).Make<T>();
        }

        public static Variant LoadJson(string path)
        {
            return Decoder.Decode(File.ReadAllText(path));
        }

        public static void ProcessIcon(PrefabImportData data)
        {
            string iconPath = Path.Combine(data.FolderPath, "icon.png");
            string baseColorMapPath = Path.Combine(data.FolderPath, "_BaseColorMap.png");

            Texture2D texture2D_Icon = new(1, 1);

            if (File.Exists(iconPath))
                LoadAndCacheIcon(ref texture2D_Icon, iconPath, iconPath);
            else if (File.Exists(baseColorMapPath))
                LoadAndCacheIcon(ref texture2D_Icon, baseColorMapPath, iconPath);

            UnityEngine.Object.Destroy(texture2D_Icon);
        }

        // Loads sourcePath into texture and, if it's bigger than the icon size, downscales it and
        // caches the result at iconSavePath (always icon.png, even when the source is _BaseColorMap.png).
        // texture is passed by ref because ResizeTexture destroys the original and swaps in a new instance.
        private static void LoadAndCacheIcon(ref Texture2D texture, string sourcePath, string iconSavePath)
        {
            byte[] fileData = File.ReadAllBytes(sourcePath);

            if (texture.LoadImage(fileData) && (texture.width > 128 || texture.height > 128))
            {
                TextureHelper.ResizeTexture(ref texture, 128, iconSavePath);
            }
        }

        // Child categories are shared by every asset of a category folder: created once per import pass.
        private static readonly ConcurrentDictionary<string, UIAssetChildCategoryPrefab> s_ChildCategories = new();

        /// <summary>
        /// Clears the per-pass caches, called at the start of each import pass (the prefabs they hold may have been
        /// removed since the previous one).
        /// </summary>
        public static void ClearCaches()
        {
            s_ChildCategories.Clear();
        }

        // ProcessIcon only does something when the icon must be (re)generated: icon.png missing (made from
        // _BaseColorMap.png) or bigger than 128px. Reads the PNG header, so it can run off the main thread.
        private static bool IconNeedsProcessing(PrefabImportData data)
        {
            string iconPath = Path.Combine(data.FolderPath, "icon.png");
            if (!File.Exists(iconPath)) return File.Exists(Path.Combine(data.FolderPath, "_BaseColorMap.png"));

            try
            {
                // PNG signature (8 bytes), IHDR length + type (8 bytes), then width and height as big-endian ints.
                byte[] header = new byte[24];
                using (FileStream stream = File.OpenRead(iconPath))
                    if (stream.Read(header, 0, header.Length) < header.Length) return true;

                int width = header[16] << 24 | header[17] << 16 | header[18] << 8 | header[19];
                int height = header[20] << 24 | header[21] << 16 | header[22] << 8 | header[23];
                return width > 128 || height > 128;
            }
            catch (Exception)
            {
                return true;
            }
        }

        // Icon, UIObject and child category need the main thread: everything still missing is done in one round-trip.
        public static void SetupUIObject( FolderImporter importer, PrefabImportData data, PrefabBase prefab)
        {
            string iconPath = Path.Combine(data.FolderPath, "icon.png");

            string catIconPath = Path.Combine(Directory.GetParent(data.FolderPath).FullName, "icon.svg");
            string catIcon = File.Exists(catIconPath) ? $"{Icons.COUIBaseLocation}/{importer.FolderName}/{data.CatName}/icon.svg" : null;
            string childCategoryName = $"{data.CatName} {data.AssetCat.name}";
            string childCategoryKey = $"{data.AssetCat.name}|{childCategoryName}|{catIcon}";

            bool processIcon = IconNeedsProcessing(data);
            prefab.TryGetExactly(out UIObject prefabUI);
            s_ChildCategories.TryGetValue(childCategoryKey, out UIAssetChildCategoryPrefab categoryPrefab);

            if (processIcon || prefabUI == null || categoryPrefab == null)
            {
                using PerformanceStats.Scope blocking = PerformanceStats.MeasureCurrent("Blocking.SetupUIObject");
                bool success = MainThreadHelper.RunOnMainThread(() =>
                {
                    if (processIcon)
                    {
                        try
                        {
                            ProcessIcon(data);
                        }
                        catch (Exception e)
                        {
                            EAI.Logger.Warn($"Failed to process icon for {data.FullAssetName}, skipping its UI setup.\nException:{e}.");
                            return false;
                        }
                    }

                    prefabUI ??= prefab.AddOrGetComponent<UIObject>();

                    if (categoryPrefab == null)
                    {
                        categoryPrefab = s_ChildCategories.GetOrAdd(childCategoryKey, _ =>
                        {
                            UIAssetChildCategoryPrefab child = PrefabsHelper.GetOrCreateUIAssetChildCategoryPrefab(data.AssetCat, childCategoryName, catIcon);
                            EnsurePrefabHasAsset(child);
                            return child;
                        });
                    }

                    return true;
                }).Result;

                if (!success) return;
            }

            prefabUI.m_IsDebugObject = false;
            prefabUI.m_Icon = File.Exists(iconPath) ? $"{Icons.COUIBaseLocation}/{importer.FolderName}/{data.CatName}/{data.AssetName}/icon.png" : Icons.DecalPlaceholder;
            prefabUI.m_Group = categoryPrefab;
        }

        public static Task<Mesh> CreateBoxMeshAsyncOnMainThread(Vector3 size)
        {
            return MainThreadHelper.RunOnMainThread(() => CreateBoxMesh(size.x, size.y, size.z));
        }

        public static Mesh CreateBoxMesh(float length, float height, float width)
        {
            Mesh mesh = new();

            Vector3[] c = new[]
            {
                new Vector3(-length * .5f, -height * .5f, width * .5f),
                new Vector3(length * .5f, -height * .5f, width * .5f),
                new Vector3(length * .5f, -height * .5f, -width * .5f),
                new Vector3(-length * .5f, -height * .5f, -width * .5f),
                new Vector3(-length * .5f, height * .5f, width * .5f),
                new Vector3(length * .5f, height * .5f, width * .5f),
                new Vector3(length * .5f, height * .5f, -width * .5f),
                new Vector3(-length * .5f, height * .5f, -width * .5f),
            };


            //4) Define the vertices that the cube is composed of:
            //I have used 16 vertices (4 vertices per side). 
            //This is because I want the vertices of each side to have separate normals.
            //(so the object renders light/shade correctly) 
            Vector3[] vertices = new[]
            {
                c[0], c[1], c[2], c[3], // Bottom
			    c[7], c[4], c[0], c[3], // Left
			    c[4], c[5], c[1], c[0], // Front
			    c[6], c[7], c[3], c[2], // Back
			    c[5], c[6], c[2], c[1], // Right
			    c[7], c[6], c[5], c[4]  // Top
            };


            //5) Define each vertex's Normal
            Vector3 up = Vector3.up;
            Vector3 down = Vector3.down;
            Vector3 forward = Vector3.forward;
            Vector3 back = Vector3.back;
            Vector3 left = Vector3.left;
            Vector3 right = Vector3.right;


            Vector3[] normals = new[]
            {
                down, down, down, down,             // Bottom
			    left, left, left, left,             // Left
			    forward, forward, forward, forward,	// Front
			    back, back, back, back,             // Back
			    right, right, right, right,         // Right
			    up, up, up, up                      // Top
            };

            //6) Define each vertex's UV co-ordinates
            Vector2 uv00 = new(0f, 0f);
            Vector2 uv10 = new(1f, 0f);
            Vector2 uv01 = new(0f, 1f);
            Vector2 uv11 = new(1f, 1f);

            Vector2[] uvs = new[]
            {
                uv11, uv01, uv00, uv10, // Bottom
			    uv11, uv01, uv00, uv10, // Left
			    uv11, uv01, uv00, uv10, // Front
			    uv11, uv01, uv00, uv10, // Back	        
			    uv11, uv01, uv00, uv10, // Right 
			    uv11, uv01, uv00, uv10  // Top
            };


            //7) Define the Polygons (triangles) that make up the our Mesh (cube)
            //IMPORTANT: Unity uses a 'Clockwise Winding Order' for determining front-facing polygons.
            //This means that a polygon's vertices must be defined in 
            //a clockwise order (relative to the camera) in order to be rendered/visible.
            int[] triangles = new[]
            {
                3, 1, 0,        3, 2, 1,        // Bottom	
			    7, 5, 4,        7, 6, 5,        // Left
			    11, 9, 8,       11, 10, 9,      // Front
			    15, 13, 12,     15, 14, 13,     // Back
			    19, 17, 16,     19, 18, 17,	    // Right
			    23, 21, 20,     23, 22, 21,     // Top
            };


            //8) Build the Mesh
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.normals = normals;
            mesh.uv = uvs;
            // mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            mesh.OptimizeIndexBuffers();
            mesh.OptimizeReorderVertexBuffer();

            return mesh;
        }

        public static string GetModPath(PrefabImportData data)
        {
            return new DirectoryInfo(data.FolderPath).Parent.Parent.Parent.FullName;
        }

        public static string GetFullAssetName(string modName, string catName, string assetName, string assetEndName)
        {
            return $"{modName} {catName} {assetName} {assetEndName}";
        }

    }
}
