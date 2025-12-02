using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.Localization;
using Game.SceneFlow;
using Game.UI.Menu;
using System.Collections.Generic;
using System.IO;

namespace ExtraAssetsImporter.AssetImporter.Importers
{
    class LocalizationImporter : FolderImporter
    {
        public override string ImporterId => "Localization";

        public override string AssetEndName => "local";

        public override bool PreImporter => true;

        public override void ExportTemplate(string path)
        {
            path = Path.Combine(path, FolderName);
            Directory.CreateDirectory(path);
            foreach (string localeID in GameManager.instance.localizationManager.GetSupportedLocales())
            {
                string newPath = Path.Combine(path, $"{localeID}.json");
                Dictionary<string, string> keyValuePairs = new()
                {
                    { "key1", "value1" },
                    { "key2", "value2" },
                    { "key3", "value3" }
                };
                File.WriteAllText(newPath, Encoder.Encode(keyValuePairs, EncodeOptions.None));
            }
        }

        protected override void LoadCustomAssetFolder(ImporterSettings importSettings, string folder, string modName, Dictionary<string, string> cslocalisation, NotificationUISystem.NotificationInfo notificationInfo)
        {
            LocalizationManager localizationManager = GameManager.instance.localizationManager;

            Dictionary<string, Dictionary<string, string>> local = LoadLocalization(folder);

            foreach (string localeID in GameManager.instance.localizationManager.GetSupportedLocales())
            {
                if(!local.ContainsKey(localeID)) continue;
                GameManager.instance.localizationManager.AddSource(localeID, new MemorySource(local[localeID]));

                if(importSettings.isAssetPack)
                {
                    LocaleData localeData = new LocaleData(localeID, local[localeID], new());
                    AssetDataPath assetDataPath = AssetDataPath.Create(Path.Combine(importSettings.outputFolderOffset, modName, "Localization"), $"{localeID}", EscapeStrategy.None);
                    LocaleAsset localeAsset = importSettings.dataBase.AddAsset<LocaleAsset>(assetDataPath);
                    localeAsset.SetData(localeData, localizationManager.LocaleIdToSystemLanguage(localeID), localizationManager.GetLocalizedName(localeID));
                    localeAsset.Save();
                }
            }
        }

        private Dictionary<string, Dictionary<string, string>> LoadLocalization(string folder)
        {
            Dictionary<string, Dictionary<string, string>> local = new();

            foreach (string localeID in GameManager.instance.localizationManager.GetSupportedLocales())
            {
                string path = Path.Combine(folder, $"{localeID}.json");
                if (!File.Exists(path)) continue;
                local.Add(localeID, ImportersUtils.LoadJson<Dictionary<string, string>>(path));
            }
            return local;
        }
    }
}
