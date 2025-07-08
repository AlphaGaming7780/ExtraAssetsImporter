using System.IO;

namespace ExtraAssetsImporter.AssetImporter
{
    abstract class FileImporter : ImporterBase
    {
        public abstract string FileName { get; }

        public override bool AddCustomAssetsFolder(string path)
        {
            string folder = Path.Combine(path, FileName);
            if (!File.Exists(folder)) return false;
            return base.AddCustomAssetsFolder(folder);
        }
    }
}
