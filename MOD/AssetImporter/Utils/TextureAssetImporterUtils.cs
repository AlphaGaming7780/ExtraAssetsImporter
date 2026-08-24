using Colossal.AssetPipeline;
using Colossal.AssetPipeline.Importers;
using Colossal.AssetPipeline.Native;
using Colossal.IO.AssetDatabase;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.ClassExtension;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using UnityEngine;
using static Colossal.AssetPipeline.Importers.DefaultTextureImporter;
using Hash128 = Colossal.Hash128;
using TextureAsset = Colossal.IO.AssetDatabase.TextureAsset;


namespace ExtraAssetsImporter.AssetImporter.Utils
{
    public static class TextureAssetImporterUtils
    {

        private static readonly DefaultTextureImporter defaultTextureImporter = ImporterCache.GetImporter(".png") as DefaultTextureImporter;

        public const string BaseColorMapName = "_BaseColorMap.png";
        public const string NormalMapName = "_NormalMap.png";
        public const string MaskMapName = "_MaskMap.png";

        private static readonly ConcurrentDictionary<string, Lazy<TextureAsset>> s_PendingImports = new();

        public static TextureAsset ImportTexture_BaseColorMap(PrefabImportData data)
        {
            ImportSettings importSettings = Settings.Defaults.Texture.GetDefault(TextureWrapMode.Repeat);
            return ImportTexture_BaseColorMap(data, importSettings);
        }

        public static TextureAsset ImportTexture_BaseColorMap(PrefabImportData data, ImportSettings importSettings)
        {
            importSettings.wrapMode = TextureWrapMode.Repeat;
            importSettings.alphaIsTransparency = true;
            return ImportTexture(data, BaseColorMapName, importSettings);
        }

        public static TextureAsset ImportTexture_NormalMap(PrefabImportData data)
        {
            ImportSettings importSettings = Settings.Defaults.Texture.GetNormal(TextureWrapMode.Repeat);
            return ImportTexture_NormalMap(data, importSettings);
        }

        public static TextureAsset ImportTexture_NormalMap(PrefabImportData data, ImportSettings importSettings)
        {
            importSettings.normalMap = true;
            importSettings.alphaIsTransparency = false;
            importSettings.wrapMode = TextureWrapMode.Repeat;
            importSettings.overrideCompressionFormat = NativeTextures.BlockCompressionFormat.BC7;
            return ImportTexture(data, NormalMapName, importSettings);
        }

        public static TextureAsset ImportTexture_MaskMap(PrefabImportData data)
        {
            ImportSettings importSettings = Settings.Defaults.Texture.GetLinear(TextureWrapMode.Repeat);
            return ImportTexture_MaskMap(data, importSettings);
        }

        public static TextureAsset ImportTexture_MaskMap(PrefabImportData data, ImportSettings importSettings)
        {
            importSettings.wrapMode = TextureWrapMode.Repeat;
            importSettings.alphaIsTransparency = false;
            importSettings.linearTexture = true;
            return ImportTexture(data, MaskMapName, importSettings);
        }

        public static TextureAsset ImportTexture(PrefabImportData data, string textureFileName, ImportSettings importSettings)
        {
            string path = Path.Combine(data.FolderPath, textureFileName);
            string textureName = Path.GetFileNameWithoutExtension(path);

            if (!File.Exists(path))
            {
                string jsonPath = Path.Combine(data.FolderPath, $"{textureName}.json");

                if (!File.Exists(jsonPath))
                    return null;

                // Read and process Texture referencing between multiple assets

                return ImportersUtils.LoadJson<TextureJson>(jsonPath).LoadTexture(importSettings, data, textureFileName, textureName);
            }

            AssetDataPath textureDataPath = AssetDataPath.Create(data.AssetDataPath, GetTextureFullFileName(data, textureName), true, EscapeStrategy.None);

            return ImportTexture_Impl(importSettings, data, path, textureDataPath, GetFullAssetTextureName(data, textureName));
        }

        internal static TextureAsset ImportTexture_Impl(ImportSettings importSettings, PrefabImportData data, string textureFilePath, AssetDataPath textureDataPath, string fullAssetTextureName)
        {
            if (data.ImportSettings.dataBase.TryGetOrAddAsset(textureDataPath, out TextureAsset textureAsset))
                return textureAsset;

            Lazy<TextureAsset> pendingImport = s_PendingImports.GetOrAdd(textureFilePath, _ => new Lazy<TextureAsset>(
                () => ImportAndSaveTexture(importSettings, data, textureFilePath, textureDataPath, fullAssetTextureName),
                LazyThreadSafetyMode.ExecutionAndPublication));

            textureAsset = pendingImport.Value;
            s_PendingImports.TryRemove(textureFilePath, out _);

            return textureAsset;
        }

        private static TextureAsset ImportAndSaveTexture(ImportSettings importSettings, PrefabImportData data, string textureFilePath, AssetDataPath textureDataPath, string fullAssetTextureName)
        {
            var texture = defaultTextureImporter.Import(importSettings, textureFilePath);

            TextureAsset textureAsset = data.ImportSettings.dataBase.AddAsset<TextureAsset, TextureImporter.ITexture>(textureDataPath, texture, Hash128.CreateGuid(fullAssetTextureName));
            textureAsset.Save();
            textureAsset.Unload();
            texture.Dispose();

            return textureAsset;
        }

        public static string GetFullAssetTextureName(PrefabImportData data, string textureName)
        {
            return GetFullAssetTextureName(data.FullAssetName, textureName);
        }

        public static string GetFullAssetTextureName(string fullAssetName, string textureName)
        {
            return $"{fullAssetName}{textureName}";
        }

        public static string GetTextureFullFileName(PrefabImportData data, string textureName)
        {
            return GetTextureFullFileName(data.AssetName, textureName);
        }

        public static string GetTextureFullFileName(string assetName, string textureName)
        {
            return $"{assetName}{textureName}{TextureAsset.kExtension}";
        }

    }
}
