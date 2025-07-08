using Colossal.IO;
using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.Components;
using ExtraAssetsImporter.AssetImporter.Importers;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.AssetImporter.JSONs.Prefabs;
using ExtraAssetsImporter.DataBase;
using ExtraAssetsImporter.Importers;
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

        public const string k_AssetPacksFolderName = "_AssetPacks";
        public const string k_CompiledAssetPacksFolderName = "_CompiledAssetPacks";
        public const string k_TemplateFolderName = "_Templates";

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

        internal static void ProcessComponentImporters(ImportData data, Variant prefabJson, PrefabBase prefabBase)
        {
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

        public static void LoadCustomAssets()
        {

            CreateEAILocalAssetPackPrefab();

            foreach (ImporterBase importer in s_PreImporters.Values)
            {
                EL.extraLibMonoScript.StartCoroutine(importer.LoadCustomAssets());
            }

            EL.extraLibMonoScript.StartCoroutine(WaitForPreImportersToFinish());

        }

        private static IEnumerator WaitForPreImportersToFinish()
        {
            //bool areDone = false;
            while (!HasImporterFinished(s_PreImporters.Values.ToArray()))
            {
                //areDone = true;
                //foreach (ImporterBase importer in s_PreImporters.Values)
                //{
                //    if (importer.AssetsLoaded) continue;
                //    areDone = false;
                //}
                yield return null;
            }

            EAI.Logger.Info("The loading of pre importers as finished.");

            foreach (ImporterBase importer in s_Importers.Values)
            {
                EL.extraLibMonoScript.StartCoroutine(importer.LoadCustomAssets());
            }

            //EL.extraLibMonoScript.StartCoroutine(WaitForImportersToFinish());
        }

        internal static IEnumerator WaitForImportersToFinish()
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
            EAIDataBaseManager.ClearNotLoadedAssetsFromFiles();

            yield break;
        }

        private static bool HasImporterFinished(ImporterBase[] importers )
        {
            bool areDone = true;
            foreach (ImporterBase importer in importers)
            {
                if (importer.AssetsLoaded) continue;
                areDone = false;
            }
            return areDone;
        }

        private static void CreateEAILocalAssetPackPrefab()
        {
            AssetPackPrefab assetPackPrefab = ScriptableObject.CreateInstance<AssetPackPrefab>();
            assetPackPrefab.name = $"ExtraAssetsImporter {AssetPackImporter.kAssetEndName}";

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
