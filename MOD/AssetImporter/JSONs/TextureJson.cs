using System;
using System.IO;

namespace ExtraAssetsImporter.AssetImporter.JSONs
{
    public class TextureJson
    {
        public string path = null;

        public string GetAssetName()
        {
            //Decals\Graffiti\CRIME
            return new DirectoryInfo(path).Name;
        }

        public string GetAssetDataPath(string modName)
        {
            //Decals\Graffiti\CRIME

            DirectoryInfo directoryInfo = new DirectoryInfo(path);
            string assetName = directoryInfo.Name; // CRIME
            string catName = directoryInfo.Parent.Name; // Graffiti
            string importerFolderName = directoryInfo.Parent.Parent.Name; // Decals

            return Path.Combine(importerFolderName, modName, catName, assetName); ;
        }

        public string GetFullAssetName(string modName)
        {
            //Decals\Graffiti\CRIME
            DirectoryInfo directoryInfo = new DirectoryInfo(path);
            string assetName = directoryInfo.Name; // CRIME
            string catName = directoryInfo.Parent.Name; // Graffiti
            string importerFolderName = directoryInfo.Parent.Parent.Name; // Decals
            string assetEndName = null;

            foreach (var importer in AssetsImporterManager.Importers.Values)
            {
                if(importer is not FolderImporter folderImporter) continue;

                if(folderImporter.FolderName == importerFolderName)
                {
                    assetEndName = folderImporter.AssetEndName;
                    break;
                }

            }

            if(assetEndName == null)
            {
                throw new Exception($"The importer folder name {importerFolderName} does not exist in any FolderImporter.");
            }

            return ImportersUtils.GetFullAssetName(modName, catName, assetName, assetEndName);
        }

    }
}
