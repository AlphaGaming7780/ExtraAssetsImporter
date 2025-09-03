using Colossal.AssetPipeline.Importers;
using Colossal.IO.AssetDatabase;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.ClassExtension;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using static Colossal.AssetPipeline.Importers.DefaultTextureImporter;
using Hash128 = Colossal.Hash128;
using TextureAsset = Colossal.IO.AssetDatabase.TextureAsset;


namespace ExtraAssetsImporter.AssetImporter.Utils
{
    public static class TextureAssetImporterUtils
    {

        private static DefaultTextureImporter defaultTextureImporter = ImporterCache.GetImporter(".png") as DefaultTextureImporter;
        //private static DefaultTextureImporter defaultTextureImporter = ImporterCache.GetInstance<DefaultTextureImporter>();

        public const string BaseColorMapName = "_BaseColorMap.png";
        public const string NormalMapName = "_NormalMap.png";
        public const string MaskMapName = "_MaskMap.png";

        public static TextureAsset ImportTexture_BaseColorMap(PrefabImportData data)
        {
            ImportSettings importSettings = ImportSettings.GetDefault();
            return ImportTexture_BaseColorMap(data, importSettings);
        }

        public static TextureAsset ImportTexture_BaseColorMap(PrefabImportData data, ImportSettings importSettings)
        {
            importSettings.wrapMode = TextureWrapMode.Repeat;
            return ImportTexture(data, BaseColorMapName, importSettings);
        }

        public static Task<TextureAsset> AsyncImportTexture_BaseColorMap(PrefabImportData data)
        {
            return Task.Run<TextureAsset>(() => ImportTexture_BaseColorMap(data));
        }

        public static Task<TextureAsset> AsyncImportTexture_BaseColorMap(PrefabImportData data, ImportSettings importSettings)
        {
            return Task.Run<TextureAsset>(() => ImportTexture_BaseColorMap(data, importSettings));
        }

        public static TextureAsset ImportTexture_NormalMap(PrefabImportData data)
        {
            ImportSettings importSettings = ImportSettings.GetDefault();
            return ImportTexture_NormalMap(data, importSettings);
        }

        public static TextureAsset ImportTexture_NormalMap(PrefabImportData data, ImportSettings importSettings)
        {
            importSettings.normalMap = true;
            importSettings.alphaIsTransparency = false;
            importSettings.overrideCompressionFormat = Colossal.AssetPipeline.Native.NativeTextures.BlockCompressionFormat.BC7;
            importSettings.wrapMode = TextureWrapMode.Repeat;
            return ImportTexture(data, NormalMapName, importSettings);
        }

        public static Task<TextureAsset> AsyncImportTexture_NormalMap(PrefabImportData data)
        {
            return Task.Run<TextureAsset>(() => ImportTexture_NormalMap(data));
        }

        public static Task<TextureAsset> AsyncImportTexture_NormalMap(PrefabImportData data, ImportSettings importSettings)
        {
            return Task.Run<TextureAsset>(() => ImportTexture_NormalMap(data, importSettings));
        }

        public static TextureAsset ImportTexture_MaskMap(PrefabImportData data)
        {
            ImportSettings importSettings = ImportSettings.GetDefault();
            return ImportTexture_MaskMap(data, importSettings);
        }

        public static TextureAsset ImportTexture_MaskMap(PrefabImportData data, ImportSettings importSettings)
        {
            importSettings.wrapMode = TextureWrapMode.Repeat;
            importSettings.alphaIsTransparency = false;
            return ImportTexture(data, MaskMapName, importSettings);
        }

        public static Task<TextureAsset> AsyncImportTexture_MaskMap(PrefabImportData data)
        {
            return Task.Run<TextureAsset>(() => ImportTexture_MaskMap(data));
        }

        public static Task<TextureAsset> AsyncImportTexture_MaskMap(PrefabImportData data, ImportSettings importSettings)
        {
            return Task.Run<TextureAsset>(() => ImportTexture_MaskMap(data, importSettings));
        }

        public static TextureAsset ImportTexture(PrefabImportData data, string textureFileName, ImportSettings importSettings)
        {
            string path = Path.Combine(data.FolderPath, textureFileName);
            string textureName = Path.GetFileNameWithoutExtension(path);

            string textureFullFileName = GetTextureFullFileName(data, textureName);
            string fullAssetTextureName = GetFullAssetTextureName(data, textureName);
            string assetDataPath = data.AssetDataPath;

            if (!File.Exists(path))
            {
                string jsonPath = Path.Combine(data.FolderPath, $"{textureName}.json");

                if (!File.Exists(jsonPath))
                    return null;

                // Read and process Texture referencing between multiple assets

                TextureJson textureJson = ImportersUtils.LoadJson<TextureJson>(jsonPath);

                string modPath = ImportersUtils.GetModPath(data);

                EAI.Logger.Info($"Mod path : {modPath}");

                path = Path.Combine(modPath, textureJson.path, textureFileName);

                if(!File.Exists(path))
                    return null;

                textureFullFileName = GetTextureFullFileName(textureJson.GetAssetName(), textureName);
                fullAssetTextureName = GetFullAssetTextureName(textureJson.GetFullAssetName(data.ModName), textureName);
                assetDataPath = textureJson.GetAssetDataPath(data.ModName);
            }

            AssetDataPath textureDataPath = AssetDataPath.Create(assetDataPath, textureFullFileName, true, EscapeStrategy.None);

            if (!data.ImportSettings.dataBase.TryGetOrAddAsset(textureDataPath, out TextureAsset textureAsset)) {
                var texture = defaultTextureImporter.Import(importSettings, path);
                textureAsset = data.ImportSettings.dataBase.AddAsset<TextureAsset, TextureImporter.ITexture>(textureDataPath, texture, Hash128.CreateGuid(fullAssetTextureName));
                textureAsset.Save();
                textureAsset.Unload();
                texture.Dispose();
            }

            return textureAsset;
        }

        public static string GetFullAssetTextureName(PrefabImportData data, string textureName)
        {
            return $"{data.FullAssetName}{textureName}";
        }

        public static string GetFullAssetTextureName(string FullAssetName, string textureName)
        {
            return $"{FullAssetName}{textureName}";
        }

        public static string GetTextureFullFileName(PrefabImportData data, string textureName)
        {
            return $"{data.AssetName}{textureName}{TextureAsset.kExtension}";
        }

        public static string GetTextureFullFileName(string assetName, string textureName)
        {
            return $"{assetName}{textureName}{TextureAsset.kExtension}";
        }

    }
}
