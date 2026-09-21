using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.Localization;
using ExtraLib.Helpers;
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

        private readonly List<string> LoadedLocales = new();

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

        protected override void LoadCustomAssetFolder(ImporterSettings importSettings, string folder, string modName, NotificationUISystem.NotificationInfo notificationInfo)
        {
            LocalizationManager localizationManager = GameManager.instance.localizationManager;

            Dictionary<string, Dictionary<string, string>> local = LoadLocalization(folder);

            if (local.TryGetValue(localizationManager.fallbackLocaleId, out Dictionary<string, string> fallbackLocale))
                LoadedLocales.AddRange(fallbackLocale.Keys);
            else
                EAI.Logger.Warn($"{modName} has no {localizationManager.fallbackLocaleId} localization file in {folder}.");

            foreach (KeyValuePair<string, Dictionary<string, string>> locale in local)
            {
                // Awaited before the pre-import pass ends (ImporterBase), so the strings are registered
                // before the importers run — no need to wait frames here.
                TrackMainThreadTask(MainThreadHelper.RunOnMainThread(() => localizationManager.AddSource(locale.Key, new MemorySource(locale.Value))));
            }
        }

        public override void Reset()
        {
            base.Reset();
            LoadedLocales.Clear();
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
