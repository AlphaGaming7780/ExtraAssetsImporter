using ExtraAssetsImporter.DataBase;
using ExtraLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExtraAssetsImporter.AssetImporter
{
    static class AssetsImporterManager
    {

        private static readonly Dictionary<Type, ImporterBase> s_Importers = [];
        private static readonly Dictionary<Type, ImporterBase> s_PreImporters = [];
        private static readonly List<string> s_AddAssetFolder = [];


        public static bool AddImporter<T>() where T : ImporterBase, new()
        {
            if (s_Importers.ContainsKey(typeof(T))) return false;

            T importer = new T();

            if(importer.PreImporter) s_PreImporters.Add(typeof(T), importer);
            else s_Importers.Add(typeof(T), importer);

            foreach(string path in s_AddAssetFolder)
            {
                string folder = Path.Combine(path, importer.FolderName);
                if (!Directory.Exists(folder) && !( importer.IsFileName && File.Exists(folder) ) ) continue;
                importer.AddCustomAssetsFolder(folder);
            }

            return true;
        }

        public static bool AddAssetFolder(string path)
        {
            if (s_AddAssetFolder.Contains(path)) return false;

            foreach( ImporterBase importer in s_PreImporters.Values.Concat(s_Importers.Values) )
            {
                string folder = Path.Combine(path, importer.FolderName);
                if (!Directory.Exists(folder) && !( importer.IsFileName && File.Exists(folder) ) ) continue;
                importer.AddCustomAssetsFolder(folder);
            }

            return true;
        }


        public static void LoadCustomAssets()
        {

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

            EL.extraLibMonoScript.StartCoroutine(WaitForImportersToFinish());
        }

        private static IEnumerator WaitForImportersToFinish()
        {
            //bool areDone = false;
            while(!HasImporterFinished(s_Importers.Values.ToArray()))
            {
                //areDone = true;
                //foreach (ImporterBase importer in s_Importers.Values)
                //{
                //    if (importer.AssetsLoaded) continue;
                //    areDone = false;
                //}
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

    }
}
