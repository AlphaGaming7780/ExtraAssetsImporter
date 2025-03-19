using Colossal.Json;
using ExtraAssetsImporter.Importers;
using Game.Prefabs;
using Game.UI.Menu;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ExtraAssetsImporter.AssetImporter.Importers
{
    class LocalizationImporter : ImporterBase
    {
        public override string ImporterId => "Localization";

        public override string FolderName => "Localization";

        public override string AssetEndName => ".local";

        protected override IEnumerator LoadCustomAssetFolder(string folder, string modName, Dictionary<string, string> localisation, NotificationUISystem.NotificationInfo notificationInfo)
        {
            throw new NotImplementedException();
        }


        protected Task<T> LoadJson<T>(string path) where T : class
        {
            return new Task<T>(() => Decoder.Decode(File.ReadAllText(path)).Make<T>());            
        }

    }
}
