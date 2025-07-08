using Colossal.IO;
using ExtraAssetsImporter.AssetImporter.Importers;
using ExtraAssetsImporter.DataBase;
using ExtraAssetsImporter.Importers;
using ExtraLib;
using Game.Prefabs;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter
{
    static class AssetsImporterManager
    {

        private static readonly Dictionary<Type, ImporterBase> s_Importers = new();
        private static readonly Dictionary<Type, ImporterBase> s_PreImporters = new();
        private static readonly List<string> s_AddAssetFolder = new();

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

        public static bool AddAssetFolder(string path)
        {
            path = PathUtils.Normalize(path);
            if (s_AddAssetFolder.Contains(path)) return false;
            s_AddAssetFolder.Add(path);

            foreach ( ImporterBase importer in s_PreImporters.Values.Concat(s_Importers.Values) )
            {
                importer.AddCustomAssetsFolder(path);
            }

            return true;
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

    }
}
