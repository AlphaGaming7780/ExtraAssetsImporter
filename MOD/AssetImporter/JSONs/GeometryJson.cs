using Colossal;
using Colossal.IO.AssetDatabase;
using ExtraAssetsImporter.AssetImporter.Utils;
using System;
using System.IO;
using static Colossal.AssetPipeline.Importers.FBXImporter;
using GeometryAsset = Colossal.IO.AssetDatabase.GeometryAsset;

namespace ExtraAssetsImporter.AssetImporter.JSONs
{
    public class GeometryJson
    {
        public string path = null;
        public Hash128 CID = Hash128.Empty;

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

        public GeometryAsset LoadGeometry(ImportSettings importSettings, PrefabImportData data, string geometryFileName, string geometryName)
        {

            if (CID != Hash128.Empty)
            {
                if(AssetDatabase.global.TryGetAsset<GeometryAsset>(CID, out GeometryAsset geometryAsset))
                {
                    return geometryAsset;
                }
            }

            if(path == null)
                return null;

            string modPath = ImportersUtils.GetModPath(data);

            string filePath = Path.Combine(modPath, path, geometryFileName);

            if (!File.Exists(filePath))
                return null;

            string geometryFullFileName = GeometryImporterUtils.GetGeometryFullFileName(GetAssetName(), geometryName);
            string fullAssetGeometryName = GeometryImporterUtils.GetFullAssetGeometryName(GetFullAssetName(data.ModName), geometryName);
            string assetDataPath = GetAssetDataPath(data.ModName);

            AssetDataPath geometryDataPath = AssetDataPath.Create(assetDataPath, geometryFullFileName, true, EscapeStrategy.None);

            return null; //GeometryImporterUtils.ImportGeometry_Impl(importSettings, data, filePath, geometryDataPath, fullAssetGeometryName);

        }

    }
}
