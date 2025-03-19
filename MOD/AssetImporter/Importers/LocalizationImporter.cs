using Colossal.Json;
using Colossal.Localization;
using Game.SceneFlow;
using Game.UI.Menu;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ExtraAssetsImporter.AssetImporter.Importers
{
    class LocalizationImporter : ImporterBase
    {
        public override string ImporterId => "Localization";

        public override string FolderName => "Localization";

        public override string AssetEndName => "local";

        public override bool PreImporter => true;

        protected override IEnumerator LoadCustomAssetFolder(string folder, string modName, Dictionary<string, string> cslocalisation, NotificationUISystem.NotificationInfo notificationInfo)
        {

            foreach (string localeID in GameManager.instance.localizationManager.GetSupportedLocales())
            {
                string path = Path.Combine(folder, $"{localeID}.json");
                Task<Dictionary<string, string>> task = LoadJson<Dictionary<string, string>>(path);

                while (!task.IsCompleted) yield return null;

                GameManager.instance.localizationManager.AddSource(localeID, new MemorySource(task.Result));
            }

        }


        protected Task<T> LoadJson<T>(string path) where T : class
        {
            Task<T> task = new Task<T>(() => Decoder.Decode(File.ReadAllText(path)).Make<T>());
            task.Start();
            return task;
        }

    }
}
