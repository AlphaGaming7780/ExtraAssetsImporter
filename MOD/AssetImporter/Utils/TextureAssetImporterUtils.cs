using Colossal.AssetPipeline;
using Colossal.AssetPipeline.Importers;
using Colossal.AssetPipeline.Native;
using Colossal.IO.AssetDatabase;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.ClassExtension;
using ExtraLib.Debugger;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

        /// <summary>
        /// Imports the base color, normal and mask maps at the same time: each import (decode, mips, BC compression)
        /// is CPU-bound and independent — the game's AssetImportPipeline imports textures in parallel too.
        /// </summary>
        public static (TextureAsset baseColorMap, TextureAsset normalMap, TextureAsset maskMap) ImportSurfaceTextures(PrefabImportData data)
        {
            Task<TextureAsset> normalMapTask = RunWithCurrentStats(() => ImportTexture_NormalMap(data));
            Task<TextureAsset> maskMapTask = RunWithCurrentStats(() => ImportTexture_MaskMap(data));

            // The base color map is imported on this thread rather than waiting idle.
            TextureAsset baseColorMap;
            try
            {
                baseColorMap = ImportTexture_BaseColorMap(data);
            }
            finally
            {
                // Don't leave imports running in the background when the base color map fails.
                Task.WaitAny(Task.WhenAll(normalMapTask, maskMapTask));
            }

            // GetResult rethrows the import's own exception instead of an AggregateException.
            return (baseColorMap, normalMapTask.GetAwaiter().GetResult(), maskMapTask.GetAwaiter().GetResult());
        }

        // PerformanceStats.Current is per thread: carry it over so Texture.Import/Save are still recorded.
        private static Task<T> RunWithCurrentStats<T>(Func<T> func)
        {
            PerformanceStats stats = PerformanceStats.Current;
            return Task.Run(() =>
            {
                if (stats == null) return func();
                using (stats.MakeCurrent()) return func();
            });
        }

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

            // Keyed on the destination: TextureJson can map several source files (from other mods) to different
            // destinations, and the same source to the same destination must only be imported once.
            string key = $"{textureDataPath.subPath}/{textureDataPath.assetName}{textureDataPath.extension}";

            Lazy<TextureAsset> pendingImport = s_PendingImports.GetOrAdd(key, _ => new Lazy<TextureAsset>(
                // Checked again inside the Lazy: a thread that missed the database check above may get here after
                // the previous import finished and its entry was removed.
                () => data.ImportSettings.dataBase.TryGetOrAddAsset(textureDataPath, out TextureAsset existingAsset)
                    ? existingAsset
                    : ImportAndSaveTexture(importSettings, data, textureFilePath, textureDataPath, fullAssetTextureName),
                LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                return pendingImport.Value;
            }
            finally
            {
                // Only remove our own entry (also clears a faulted Lazy so a later call can retry).
                ((ICollection<KeyValuePair<string, Lazy<TextureAsset>>>)s_PendingImports).Remove(new KeyValuePair<string, Lazy<TextureAsset>>(key, pendingImport));
            }
        }

        private static TextureAsset ImportAndSaveTexture(ImportSettings importSettings, PrefabImportData data, string textureFilePath, AssetDataPath textureDataPath, string fullAssetTextureName)
        {
            PerformanceStats.Scope importScope = PerformanceStats.MeasureCurrent("Texture.Import");
            var texture = defaultTextureImporter.Import(importSettings, textureFilePath);
            importScope.Dispose();

            TextureAsset textureAsset = data.ImportSettings.dataBase.AddAsset<TextureAsset, TextureImporter.ITexture>(textureDataPath, texture, Hash128.CreateGuid(fullAssetTextureName));
            using (PerformanceStats.MeasureCurrent("Texture.Save"))
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
