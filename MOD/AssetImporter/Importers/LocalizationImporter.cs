using Colossal.Collections.Generic;
using Colossal.Localization;
using Game.SceneFlow;
using Game.UI.Menu;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ExtraAssetsImporter.AssetImporter.Importers
{
    class LocalizationImporter : FolderImporter
    {
        public override string ImporterId => "Localization";

        public override string AssetEndName => "local";

        public override bool PreImporter => true;

        protected override IEnumerator LoadCustomAssetFolder(string folder, string modName, Dictionary<string, string> cslocalisation, NotificationUISystem.NotificationInfo notificationInfo)
        {

            Task<Dictionary<string, Dictionary<string, string>>> task = Task.Run( () =>LoadLocalization(folder));

            while (!task.IsCompleted) yield return null;

            Dictionary<string, Dictionary<string, string>> local = task.Result;

            foreach (string localeID in GameManager.instance.localizationManager.GetSupportedLocales())
            {
                if(!local.ContainsKey(localeID)) continue;
                GameManager.instance.localizationManager.AddSource(localeID, new MemorySource(local[localeID]));
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
