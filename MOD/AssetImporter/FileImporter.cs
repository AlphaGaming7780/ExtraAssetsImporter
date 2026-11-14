using ExtraAssetsImporter.Database;
using System.IO;

namespace ExtraAssetsImporter.AssetImporter
{
    abstract class FileImporter : ImporterBase
    {
        public abstract string FileName { get; }

        public override bool AddCustomAssetsFolder(string path)
        {
            string folder = Path.Combine(path, FileName);

            // A mod without this file no longer holds any of this importer's assets, so the quit
            // cleanup deletes their cache.
            if (!File.Exists(folder))
            {
                EAIDatabaseManager.eaiDatabase.AddScannedFolder(GetScannedFolder(folder));
                return false;
            }

            return base.AddCustomAssetsFolder(folder);
        }
    }
}
