using ExtraAssetsImporter.Database;
using System.IO;

namespace ExtraAssetsImporter.AssetImporter
{
    abstract class FolderImporter : ImporterBase
    {
        public virtual string FolderName { get; private set; } = null;

        public override bool AddCustomAssetsFolder(string path)
        {
            if(string.IsNullOrEmpty(FolderName)) FolderName = ImporterId;

            string folder = Path.Combine(path, FolderName);

            // A mod without this folder no longer holds any of this importer's assets, so the quit
            // cleanup deletes their cache.
            if (!Directory.Exists(folder))
            {
                EAIDatabaseManager.eaiDatabase.AddScannedFolder(GetScannedFolder(folder));
                return false;
            }

            return base.AddCustomAssetsFolder(folder);
        }

    }
}
