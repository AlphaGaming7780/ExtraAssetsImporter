using System.IO;

namespace ExtraAssetsImporter.AssetImporter
{
    abstract class FolderImporter : ImporterBase
    {
        public virtual string FolderName { get; private set; } = null;

        public override void AddCustomAssetsFolder(string path)
        {
            if(string.IsNullOrEmpty(FolderName)) FolderName = ImporterId;

            string folder = Path.Combine(path, FolderName);
            if (!Directory.Exists(folder)) return;
            base.AddCustomAssetsFolder(folder);
        }

    }
}
