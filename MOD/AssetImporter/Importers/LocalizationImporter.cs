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

            Task task = Task.Run(() => LoadLocalization(folder));

            while (!task.IsCompleted) yield return null;

        }

        private void LoadLocalization(string folder)
        {
            foreach (string localeID in GameManager.instance.localizationManager.GetSupportedLocales())
            {
                string path = Path.Combine(folder, $"{localeID}.json");
                if(!File.Exists(path)) continue;
                Dictionary<string, string> local = ImportersUtils.LoadJson<Dictionary<string, string>>(path);

                GameManager.instance.localizationManager.AddSource(localeID, new MemorySource(local));
            }
        }

    }
}
