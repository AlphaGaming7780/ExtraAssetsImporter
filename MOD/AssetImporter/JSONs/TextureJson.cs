using Colossal;
using Colossal.AssetPipeline;
using Colossal.IO.AssetDatabase;
using ExtraAssetsImporter.AssetImporter.Utils;
using System;
using System.IO;
using static Colossal.AssetPipeline.Importers.DefaultTextureImporter;
using TextureAsset = Colossal.IO.AssetDatabase.TextureAsset;

namespace ExtraAssetsImporter.AssetImporter.JSONs
{
    public class TextureJson
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

        public TextureAsset LoadTexture(ImportSettings importSettings, PrefabImportData data, string textureFileName, string textureName)
        {

            if (CID != Hash128.Empty)
            {
                if(AssetDatabase.global.TryGetAsset<TextureAsset>(CID, out TextureAsset textureAsset))
                {
                    return textureAsset;
                }
            }

            if(path == null)
                return null;

            string modPath = ImportersUtils.GetModPath(data);

            path = Path.Combine(modPath, path, textureFileName);

            if (!File.Exists(path))
                return null;

            string textureFullFileName = TextureAssetImporterUtils.GetTextureFullFileName(GetAssetName(), textureName);
            string fullAssetTextureName = TextureAssetImporterUtils.GetFullAssetTextureName(GetFullAssetName(data.ModName), textureName);
            string assetDataPath = GetAssetDataPath(data.ModName);

            AssetDataPath textureDataPath = AssetDataPath.Create(assetDataPath, textureFullFileName, true, EscapeStrategy.None);

            return TextureAssetImporterUtils.ImportTexture_Impl(importSettings, data, path, textureDataPath, fullAssetTextureName);

        }

    }
}
