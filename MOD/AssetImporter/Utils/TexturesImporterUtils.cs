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

        public const string BaseColorMapName = "_BaseColorMap.png";
        public const string NormalMapName = "_NormalMap.png";
        public const string MaskMapName = "_MaskMap.png";

        public static void ImportTextures(PrefabImportData data, Surface surface)
        {
            var baseColorMap = TexturesImporterUtils.ImportTexture_BaseColorMap(data);
            if (baseColorMap != null) surface.AddProperty(SurfaceImporterUtils.BaseColorMap, baseColorMap);

            var normalMap = TexturesImporterUtils.ImportTexture_NormalMap(data);
            if (normalMap != null) surface.AddProperty(SurfaceImporterUtils.NormalMap, normalMap);

            var maskMap = TexturesImporterUtils.ImportTexture_MaskMap(data);
            if (maskMap != null) surface.AddProperty(SurfaceImporterUtils.MaskMap, maskMap);
        }

        public static Task ImportTexturesAsync(PrefabImportData data, Surface surface)
        {
            return Task.Run(() => ImportTextures(data, surface));
        }

        public static TextureImporter.Texture ImportTexture_BaseColorMap(PrefabImportData data)
        {
            ImportSettings importSettings = ImportSettings.GetDefault();
            return ImportTexture_BaseColorMap(data, importSettings);
        }

        public static TextureImporter.Texture ImportTexture_BaseColorMap(PrefabImportData data, ImportSettings importSettings)
        {
            importSettings.wrapMode = TextureWrapMode.Repeat;
            return ImportTexture(data, BaseColorMapName, importSettings);
        }

        public static Task<TextureImporter.Texture> AsyncImportTexture_BaseColorMap(PrefabImportData data)
        {
            return Task.Run<TextureImporter.Texture>(() => ImportTexture_BaseColorMap(data));
        }

        public static Task<TextureImporter.Texture> AsyncImportTexture_BaseColorMap(PrefabImportData data, ImportSettings importSettings)
        {
            return Task.Run<TextureImporter.Texture>(() => ImportTexture_BaseColorMap(data, importSettings));
        }

        public static TextureImporter.Texture ImportTexture_NormalMap(PrefabImportData data)
        {
            ImportSettings importSettings = ImportSettings.GetDefault();
            return ImportTexture_NormalMap(data, importSettings);
        }

        public static TextureImporter.Texture ImportTexture_NormalMap(PrefabImportData data, ImportSettings importSettings)
        {
            importSettings.normalMap = true;
            importSettings.alphaIsTransparency = false;
            importSettings.overrideCompressionFormat = Colossal.AssetPipeline.Native.NativeTextures.BlockCompressionFormat.BC7;
            importSettings.wrapMode = TextureWrapMode.Repeat;
            return ImportTexture(data, NormalMapName, importSettings);
        }

        public static Task<TextureImporter.Texture> AsyncImportTexture_NormalMap(PrefabImportData data)
        {
            return Task.Run<TextureImporter.Texture>(() => ImportTexture_NormalMap(data));
        }

        public static Task<TextureImporter.Texture> AsyncImportTexture_NormalMap(PrefabImportData data, ImportSettings importSettings)
        {
            return Task.Run<TextureImporter.Texture>(() => ImportTexture_NormalMap(data, importSettings));
        }

        public static TextureImporter.Texture ImportTexture_MaskMap(PrefabImportData data)
        {
            ImportSettings importSettings = ImportSettings.GetDefault();
            return ImportTexture_MaskMap(data, importSettings);
        }

        public static TextureImporter.Texture ImportTexture_MaskMap(PrefabImportData data, ImportSettings importSettings)
        {
            importSettings.wrapMode = TextureWrapMode.Repeat;
            importSettings.alphaIsTransparency = false;
            return ImportTexture(data, MaskMapName, importSettings);
        }

        public static Task<TextureImporter.Texture> AsyncImportTexture_MaskMap(PrefabImportData data)
        {
            return Task.Run<TextureImporter.Texture>(() => ImportTexture_MaskMap(data));
        }

        public static Task<TextureImporter.Texture> AsyncImportTexture_MaskMap(PrefabImportData data, ImportSettings importSettings)
        {
            return Task.Run<TextureImporter.Texture>(() => ImportTexture_MaskMap(data, importSettings));
        }

        public static TextureImporter.Texture ImportTexture(PrefabImportData data, string TextureName, ImportSettings importSettings)
        {
            string path = Path.Combine(data.FolderPath, TextureName);
            if (!File.Exists(path)) return null;

            return defaultTextureImporter.Import(importSettings, path);
        }

    }
}
