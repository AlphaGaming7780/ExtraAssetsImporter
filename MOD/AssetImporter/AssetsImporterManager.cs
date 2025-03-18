using ExtraAssetsImporter.DataBase;
using ExtraLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace ExtraAssetsImporter.AssetImporter
{
    static class AssetsImporterManager
    {

        private static readonly Dictionary<Type, ImporterBase> s_Importers = [];
        private static readonly List<string> s_AddAssetFolder = [];


        public static bool AddImporter<T>() where T : ImporterBase, new()
        {
            if (s_Importers.ContainsKey(typeof(T))) return false;

            T importer = new T();

            s_Importers.Add(typeof(T), importer);

            foreach(string path in s_AddAssetFolder)
            {
                string folder = Path.Combine(path, importer.FolderName);
                if (!Directory.Exists(folder)) continue;
                importer.AddCustomAssetsFolder(folder);
            }

            return true;
        }

        public static bool AddAssetFolder(string path)
        {
            if (s_AddAssetFolder.Contains(path)) return false;

            foreach( ImporterBase importer in s_Importers.Values )
            {
                string folder = Path.Combine(path, importer.FolderName);
                if (!Directory.Exists(folder)) continue;
                importer.AddCustomAssetsFolder(folder);
            }

            return true;
        }


        public static void LoadCustomAssets()
        {
            EAIDataBaseManager.LoadDataBase();

            foreach(ImporterBase importer in s_Importers.Values)
            {
               EL.extraLibMonoScript.StartCoroutine(importer.CreateCustomAssets());
            }

            EL.extraLibMonoScript.StartCoroutine(WaitForCustomStuffToFinish());
        }

        private static IEnumerator WaitForCustomStuffToFinish()
        {
            bool areDone = false;
            while(!areDone)
            {
                areDone = true;
                foreach (ImporterBase importer in s_Importers.Values)
                {
                    if (importer.AssetsLoaded) continue;
                    areDone = false;
                }
                yield return null;
            }

            EAI.Logger.Info("The loading of custom stuff as finished.");
            EAI.m_Setting.ResetCompatibility();
            EAIDataBaseManager.SaveValidateDataBase();
            EAIDataBaseManager.ClearNotLoadedAssetsFromFiles();

            yield break;
        }

    }
}
