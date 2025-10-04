using Colossal.IO;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.Components;
using ExtraAssetsImporter.AssetImporter.Importers;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.AssetImporter.JSONs.Prefabs;
using ExtraAssetsImporter.DataBase;
using ExtraAssetsImporter.OldImporters;
using ExtraLib;
using Game.Prefabs;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter
{
    static class AssetsImporterManager
    {
        private static readonly Dictionary<Type, ImporterBase> s_PreImporters = new();
        private static readonly Dictionary<Type, ImporterBase> s_Importers = new();
        private static readonly Dictionary<Type, ComponentImporter> s_ComponentImporters = new();
        private static readonly List<string> s_AddAssetFolder = new();

        public static IReadOnlyDictionary<Type, ImporterBase> PreImporters => s_PreImporters;
        public static IReadOnlyDictionary<Type, ImporterBase> Importers => s_Importers;
        public static IReadOnlyDictionary<Type, ComponentImporter> ComponentImporters => s_ComponentImporters;

        public const string k_AssetPacksFolderName = "_AssetPacks";
        public const string k_CompiledAssetPacksFolderName = "_CompiledAssetPacks";
        public const string k_TemplateFolderName = "_DefaultJson";

#if DEBUG
        private static bool s_firstTimeLoad = true;
#endif

        public static bool AddImporter<T>() where T : ImporterBase, new()
        {
            if (s_Importers.ContainsKey(typeof(T))) return false;

            T importer = new T();

            if(importer.PreImporter) s_PreImporters.Add(typeof(T), importer);
            else s_Importers.Add(typeof(T), importer);

            foreach(string path in s_AddAssetFolder)
            {
                importer.AddCustomAssetsFolder(path);
            }

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
            if (s_AddAssetFolder.Contains(path)) return false;
            // Ignore path that start with "."
            if (Path.GetDirectoryName(path).StartsWith(".")) return false;

            s_AddAssetFolder.Add(path);
            
            foreach( ImporterBase importer in s_PreImporters.Values.Concat(s_Importers.Values) )
            {
                importer.AddCustomAssetsFolder(path);
            }

            return true;
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
                if (importer.PrefabType == prefabType || FindAllDerivedTypes(importer.PrefabType).Contains(prefabType))
                {
                    importers.Add(importer);
                }
            }
            return importers;
        }

        internal static void ProcessComponentImporters(PrefabImportData data, Variant prefabJson, PrefabBase prefabBase)
        {
            if(prefabJson == null)
            {
                EAI.Logger.Warn($"The asset {data.FullAssetName} doesn't have any JSON data to process.");
                return;
            }

            Variant componentsVariant = prefabJson.TryGet(nameof(PrefabJson.Components));

            if(componentsVariant == null)
            {
                EAI.Logger.Warn($"The prefab {prefabBase.name} doesn't have any components to process.");
                return;
            }

            foreach (ComponentImporter importer in s_ComponentImporters.Values)
            {
                if (componentsVariant.TryGetValue(importer.ComponentType.FullName, out Variant componentJson))
                {
                    if (importer.PrefabType != prefabBase.GetType() && !FindAllDerivedTypes(importer.PrefabType).Contains(prefabBase.GetType()))
                    {
                        EAI.Logger.Warn($"The component importer {importer.ComponentType.FullName} is not compatible with the prefab type {prefabBase.GetType().FullName} for the asset {prefabBase.name}.");
                        continue;
                    }
                    importer.Process(data, componentJson, prefabBase);
                }
            }
        }

        public static void BuildAllAssetPacks()
        {
            List<string> assetPacksToBuild = new List<string>();

            string path = Path.Combine(EAI.pathModsData, k_AssetPacksFolderName);

            if (!Directory.Exists(path)) return;

            foreach (DirectoryInfo directoryInfo in new DirectoryInfo(path).GetDirectories())
            {
                assetPacksToBuild.Add(directoryInfo.Name);
            }
            BuildAssetPack(assetPacksToBuild);
        }

        public static void BuildAssetPack(string assetPackName)
        {
            string[] strings = { assetPackName };
            BuildAssetPack(strings);
        }

        public static void BuildAssetPack(IEnumerable<string> assetPacksName)
        {
            ImporterSettings importerSettings = new ImporterSettings
            {
                dataBase = AssetDatabase.user,
                savePrefabs = true,
                isAssetPack = true,
                outputFolderOffset = Path.Combine(EAI.pathModsData.Replace(AssetDatabase.user.rootPath + Path.DirectorySeparatorChar, ""), k_CompiledAssetPacksFolderName)
            };

            foreach (string assetPackName in assetPacksName)
            {
                string path = Path.Combine(EAI.pathModsData, k_AssetPacksFolderName, assetPackName);

                path = PathUtils.Normalize(path);

                // Ignore path that start with "."
                //if (Path.GetDirectoryName(path).StartsWith(".")) return;

                foreach (ImporterBase importer in s_PreImporters.Values.Concat(s_Importers.Values))
                {
                    importer.AddCustomAssetsFolder(path);
                }
            }

            LoadCustomAssets(importerSettings);
        }

        public static void LoadCustomAssets(ImporterSettings importerSettings)
        {

            CreateEAILocalAssetPackPrefab();

            foreach (ImporterBase importer in s_PreImporters.Values)
            {
                EL.extraLibMonoScript.StartCoroutine(importer.LoadCustomAssets(importerSettings));
            }

            EL.extraLibMonoScript.StartCoroutine(WaitForPreImportersToFinish(importerSettings));

        }

        private static IEnumerator WaitForPreImportersToFinish(ImporterSettings importerSettings)
        {
            while (!HasImporterFinished(s_PreImporters.Values))
            {
                yield return null;
            }

            EAI.Logger.Info("The loading of pre importers as finished.");

            foreach (ImporterBase importer in s_Importers.Values)
            {
                EL.extraLibMonoScript.StartCoroutine(importer.LoadCustomAssets(importerSettings));
            }

            EL.extraLibMonoScript.StartCoroutine(WaitForImportersToFinish(importerSettings));
        }

        internal static IEnumerator WaitForImportersToFinish(ImporterSettings importerSettings)
        {

            while (
                ( EAI.m_Setting.UseOldImporters && EAI.m_Setting.Decals && !DecalsImporter.DecalsLoaded) ||
                ( EAI.m_Setting.UseOldImporters && EAI.m_Setting.Surfaces && !SurfacesImporter.SurfacesIsLoaded) ||
                ( EAI.m_Setting.UseOldImporters && EAI.m_Setting.NetLanes && !NetLanesDecalImporter.NetLanesLoaded) ||
                ( EAI.m_Setting.UseNewImporters && !HasImporterFinished(s_Importers.Values.ToArray()) )
            )
            {
                yield return null;
            }

            EAI.Logger.Info("The loading of importers as finished.");
            EAI.m_Setting.ResetCompatibility();
            EAIDataBaseManager.SaveValidateDataBase();
            EAIDataBaseManager.ClearNotLoadedAssetsFromFiles(importerSettings);
            //EAIDataBaseManager.UnloadDataBase();

#if DEBUG
            if(s_firstTimeLoad)
            {
                s_firstTimeLoad = false;
                BuildAllAssetPacks();
            }
#endif
            yield break;
        }

        private static bool HasImporterFinished(IEnumerable<ImporterBase> importers )
        {
            bool areDone = true;
            foreach (ImporterBase importer in importers)
            {
                if (importer.AssetsLoaded) continue;
                areDone = false;
                break;
            }
            return areDone;
        }

        private static void CreateEAILocalAssetPackPrefab()
        {
            string packName = $"ExtraAssetsImporter {AssetPackImporter.kAssetEndName}";
            if (EL.m_PrefabSystem.TryGetPrefab(new PrefabID(typeof(AssetPackPrefab).Name, packName), out var existingPrefab))
                return;

            AssetPackPrefab assetPackPrefab = ScriptableObject.CreateInstance<AssetPackPrefab>();
            assetPackPrefab.name = packName;

            UIObject assetPackUI = assetPackPrefab.AddComponent<UIObject>();
            assetPackUI.m_Icon = Icons.GetIcon(assetPackPrefab);

            EL.m_PrefabSystem.AddPrefab(assetPackPrefab);
        }

        public static void ExportImportersTemplate()
        {
            string path = Path.Combine(EAI.pathModsData, k_TemplateFolderName);
            Directory.CreateDirectory(path);
            foreach (ImporterBase importer in s_PreImporters.Values.Concat(s_Importers.Values))
            {
                importer.ExportTemplate(path);
            }
            //string textureSharing = Path.Combine(path, "TextureSharing");
            //Directory.CreateDirectory(textureSharing);

            TextureJson textureJson = new TextureJson()
            {
                path = "ImporterID\\Category\\AssetName",
                CID = new()
            };
            File.WriteAllText(Path.Combine(path, "TextureSharing.json"), Encoder.Encode(textureJson, EncodeOptions.None));

        }

        public static List<Type> FindAllDerivedTypes(Type baseType)
        {
            return FindAllDerivedTypes(baseType, Assembly.GetAssembly(baseType));
        }

        public static List<Type> FindAllDerivedTypes(Type baseType,Assembly assembly)
        {
            return assembly
                .GetTypes()
                .Where(t =>
                    t != baseType &&
                    baseType.IsAssignableFrom(t)
                    ).ToList();

        }

    }
}
