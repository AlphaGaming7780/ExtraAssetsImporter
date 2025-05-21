using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Colossal.AssetPipeline;
using Colossal.AssetPipeline.Importers;
using UnityEngine;
using static Colossal.AssetPipeline.Importers.DefaultTextureImporter;

namespace ExtraAssetsImporter.AssetImporter.Utils
{
    public static class TexturesImporterUtils
    {
        private static DefaultTextureImporter defaultTextureImporter = ImporterCache.GetImporter(".png") as DefaultTextureImporter;
        //private static DefaultTextureImporter defaultTextureImporter = ImporterCache.GetInstance<DefaultTextureImporter>();

        public static TextureImporter.Texture ImportTexture_BaseColorMap(ImportData data)
        {
            ImportSettings importSettings = ImportSettings.GetDefault();
            importSettings.compressBC = true;
            importSettings.wrapMode = TextureWrapMode.Repeat;
            return ImportTexture_BaseColorMap(data, importSettings);
        }

        public static TextureImporter.Texture ImportTexture_BaseColorMap(ImportData data, ImportSettings importSettings)
        {
            return ImportTexture(data, "_BaseColorMap.png", importSettings);
        }

        public static Task<TextureImporter.Texture> AsyncImportTexture_BaseColorMap(ImportData data, ImportSettings importSettings)
        {
            return Task.Run<TextureImporter.Texture>(() => ImportTexture_BaseColorMap(data, importSettings));
        }

        public static TextureImporter.Texture ImportTexture_NormalMap(ImportData data)
        {
            ImportSettings importSettings = ImportSettings.GetDefault();
            importSettings.overrideCompressionFormat = Colossal.AssetPipeline.Native.NativeTextures.BlockCompressionFormat.BC7;
            importSettings.wrapMode = TextureWrapMode.Repeat;
            return ImportTexture_NormalMap(data, importSettings);
        }

        public static TextureImporter.Texture ImportTexture_NormalMap(ImportData data, ImportSettings importSettings)
        {
            importSettings.normalMap = true;
            importSettings.alphaIsTransparency = false;
            return ImportTexture(data, "_NormalMap.png", importSettings);
        }

        public static Task<TextureImporter.Texture> AsyncImportTexture_NormalMap(ImportData data, ImportSettings importSettings)
        {
            return Task.Run<TextureImporter.Texture>(() => ImportTexture_NormalMap(data, importSettings));
        }

        public static TextureImporter.Texture ImportTexture_MaskMap(ImportData data)
        {
            ImportSettings importSettings = ImportSettings.GetDefault();
            importSettings.wrapMode = TextureWrapMode.Repeat;
            return ImportTexture_MaskMap(data, importSettings);
        }

        public static TextureImporter.Texture ImportTexture_MaskMap(ImportData data, ImportSettings importSettings)
        {
            importSettings.alphaIsTransparency = false;
            return ImportTexture(data, "_MaskMap.png", importSettings);
        }

        public static Task<TextureImporter.Texture> AsyncImportTexture_MaskMap(ImportData data, ImportSettings importSettings)
        {
            return Task.Run<TextureImporter.Texture>(() => ImportTexture_MaskMap(data, importSettings));
        }

        public static TextureImporter.Texture ImportTexture(ImportData data, string TextureName, ImportSettings importSettings)
        {
            string path = Path.Combine(data.FolderPath, TextureName);
            if (!File.Exists(path)) return null;

            return defaultTextureImporter.Import(importSettings, path);
        }

    }
}
