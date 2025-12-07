using Colossal.IO;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.Localization;
using Colossal.UI;
using ExtraAssetsImporter.DataBase;
using ExtraLib.Helpers;
using ExtraLib.Prefabs;
using Game.Prefabs;
using Game.SceneFlow;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using MainThreadDispatcher = Colossal.Core.MainThreadDispatcher;

namespace ExtraAssetsImporter.AssetImporter
{

    static class ImportersUtils
    {
        public static Task<T> AsyncLoadJson<T>(string path) where T : class
        {
            return Task.Run(() => LoadJson<T>(path));
        }

        public static T LoadJson<T>(string path) where T : class
        {
            return Decoder.Decode(File.ReadAllText(path)).Make<T>();
        }

        public static Variant LoadJson(string path)
        {
            return Decoder.Decode(File.ReadAllText(path));
        }

        public static Task ProcessIconOnMainThread(PrefabImportData data) 
        {

            TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();

            MainThreadDispatcher.RunOnMainThread( () =>
            {
                try
                {
                    ProcessIcon(data);
                    tcs.SetResult(null);
                }
                catch (Exception e)
                {
                    EAI.Logger.Warn($"Failed to process icon.\nException:{e}.");
                    tcs.SetException(e);
                }
            });

            return tcs.Task;

        }

        public static void ProcessIcon(PrefabImportData data)
        {
            string iconPath = Path.Combine(data.FolderPath, "icon.png");
            string baseColorMapPath = Path.Combine(data.FolderPath, "_BaseColorMap.png");
            Texture2D texture2D_Icon = new(1, 1);
            if (File.Exists(iconPath))
            {
                byte[] fileData = File.ReadAllBytes(iconPath);

                if (texture2D_Icon.LoadImage(fileData))
                {
                    if (texture2D_Icon.width > 128 || texture2D_Icon.height > 128)
                    {
                        TextureHelper.ResizeTexture(ref texture2D_Icon, 128, iconPath);
                    }
                }
            }
            else if (File.Exists(baseColorMapPath))
            {
                byte[] fileData = File.ReadAllBytes(baseColorMapPath);
                if (texture2D_Icon.LoadImage(fileData))
                {
                    if (texture2D_Icon.width > 128 || texture2D_Icon.height > 128)
                    {
                        TextureHelper.ResizeTexture(ref texture2D_Icon, 128, iconPath);
                    }
                }
            }
            UnityEngine.Object.Destroy(texture2D_Icon);
        }

        public static ImageAsset ImportImageFromPath(string path, PrefabImportData data)
        {
            return ImportImageFromPath(path, data.AssetDataPath, data.ImportSettings, data.AssetName);
        }

        public static ImageAsset ImportImageFromPath(string inPath, string assetDataPath, ImporterSettings importerSettings, string assetName)
        {
            if (LongFile.Exists(inPath))
            {
                string extension = Path.GetExtension(inPath);
                string fullAssetName = $"{assetName}_Icon{extension}";
                ImageAsset imageAsset = importerSettings.dataBase.AddAsset<ImageAsset>(AssetDataPath.Create(assetDataPath, fullAssetName, hasExtension: true, EscapeStrategy.None));
                using (FileStream source = LongFile.OpenRead(inPath))
                {
                    using Stream destination = imageAsset.GetWriteStream();
                    IOUtils.CopyStream(source, destination);
                }

                imageAsset.Save();
                return imageAsset;
            }

            return null;
        }

        public static void SetupUIObject( FolderImporter importer, PrefabImportData data, PrefabBase prefab, int UiPriority = 0)
        {
            Task taskTexture = ProcessIconOnMainThread(data);

            taskTexture.Wait();

            if (taskTexture.IsFaulted) return;

            string iconPath = Path.Combine(data.FolderPath, "icon.png");

            string catIconPath = Path.Combine(Directory.GetParent(data.FolderPath).FullName, "icon.svg");

            string iconString = File.Exists(iconPath) ? $"{Icons.COUIBaseLocation}/{importer.FolderName}/{data.CatName}/{data.AssetName}/icon.png" : Icons.DecalPlaceholder;
            if (data.ImportSettings.isAssetPack)
            {
                ImageAsset imageAsset = ImportImageFromPath(iconPath, data);
                if (imageAsset != null)
                    iconString = imageAsset.ToGlobalUri();
            }

            UIObject prefabUI = prefab.AddComponent<UIObject>();
            prefabUI.m_IsDebugObject = false;
            prefabUI.m_Icon = iconString;
            prefabUI.m_Priority = UiPriority;

            if (!data.ImportSettings.isAssetPack) 
            {
                UIAssetChildCategoryPrefab categoryPrefab = PrefabsHelper.GetOrCreateUIAssetChildCategoryPrefab(data.AssetCat, $"{data.CatName} {data.AssetCat.name}", File.Exists(catIconPath) ? $"{Icons.COUIBaseLocation}/{importer.FolderName}/{data.CatName}/icon.svg" : null);
                AssetDataPath assetDataPath = AssetDataPath.Create(EAI.kTempFolderName, $"{categoryPrefab.name}_CategoryPrefab", EscapeStrategy.None);
                EAIDataBaseManager.EAIAssetDataBase.AddAsset<PrefabAsset, ScriptableObject>(assetDataPath, categoryPrefab);
                prefabUI.m_Group = categoryPrefab;
            }
        }

        public static void SetupLocalisationForPrefab(Dictionary<string, string> localisation, ImporterSettings importSettings, string assetDataPath, string assetName)
        {
            LocalizationManager localizationManager = GameManager.instance.localizationManager;

            string localeID = localizationManager.fallbackLocaleId;

            if (importSettings.isAssetPack)
            {
                LocaleData localeData = new LocaleData(localeID, localisation, new());
                AssetDataPath localAssetDataPath = AssetDataPath.Create(assetDataPath, $"{assetName}_{localeID}", EscapeStrategy.None);
                LocaleAsset localeAsset = importSettings.dataBase.AddAsset<LocaleAsset>(localAssetDataPath);
                localeAsset.SetData(localeData, localizationManager.LocaleIdToSystemLanguage(localeID), localizationManager.GetLocalizedName(localeID));
                localeAsset.Save();
                MainThreadDispatcher.RunOnMainThread(() => localizationManager.AddLocale(localeAsset));
            }
            else
            {

                MainThreadDispatcher.RunOnMainThread(() => localizationManager.AddSource(localeID, new MemorySource(localisation)));

                //foreach (string localeID in localizationManager.GetSupportedLocales())
                //{
                //    localizationManager.AddSource(localeID, new MemorySource(localisation));
                //}
            }
        }

        public static string GetModPath(PrefabImportData data)
        {
            return new DirectoryInfo(data.FolderPath).Parent.Parent.Parent.FullName; //Path.Combine(data.FolderPath, "..", "..", "..");
        }

        public static string GetFullAssetName(string modName, string catName, string assetName, string assetEndName)
        {
            return $"{modName} {catName} {assetName} {assetEndName}";
        }

    }
}
