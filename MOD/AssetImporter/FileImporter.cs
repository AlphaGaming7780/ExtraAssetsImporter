using System.IO;

namespace ExtraAssetsImporter.AssetImporter
{
    abstract class FileImporter : ImporterBase
    {
        public abstract string FileName { get; }

        public override void AddCustomAssetsFolder(string path)
        {
            string folder = Path.Combine(path, FileName);
            if (!File.Exists(folder)) return;
            base.AddCustomAssetsFolder(folder);
        }
    }
}
