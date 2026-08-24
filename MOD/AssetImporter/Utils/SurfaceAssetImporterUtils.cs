using Colossal.AssetPipeline;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.JSONs;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter.Utils
{
    internal static class SurfaceAssetImporterUtils
    {
        public const string MaterialJsonFileName = "Material.json";
        public const string BaseColorMap = "_BaseColorMap";
        public const string NormalMap = "_NormalMap";
        public const string MaskMap = "_MaskMap";

        public static MaterialJson LoadMaterialJson(PrefabImportData data)
        {
            string path = Path.Combine(data.FolderPath, MaterialJsonFileName);
            if (!File.Exists(path)) return null;
            MaterialJson materialJson = ImportersUtils.LoadJson<MaterialJson>(path);
            return materialJson;
        }

        public static SurfaceAsset CreateSurface(PrefabImportData data, string defaultMaterialName, bool importTextures = true)
        {
            MaterialJson materialJson = LoadMaterialJson(data);
            return CreateSurface(data, materialJson, defaultMaterialName, importTextures);
        }

        public static SurfaceAsset CreateSurface(PrefabImportData data, MaterialJson materialJson, string defaultMaterialName, bool importTextures = true)
        {
            string materialName = materialJson != null ? materialJson.MaterialName ?? defaultMaterialName : defaultMaterialName;

            Surface surface = new($"{data.AssetName}_Surface", materialName);
            if (materialJson != null)
            {
                foreach (string key in materialJson.Float.Keys) { surface.AddProperty(key, materialJson.Float[key]); }
                foreach (string key in materialJson.Vector.Keys) { surface.AddProperty(key, materialJson.Vector[key]); }
            }

            AssetDataPath surfaceAssetDataPath = AssetDataPath.Create(data.AssetDataPath, $"{data.AssetName}_SurfaceAsset", EscapeStrategy.None);

            SurfaceAsset surfaceAsset = new()
            {
                id = new Identifier(Guid.NewGuid()),
                database = data.ImportSettings.dataBase
            };

            surfaceAsset.database.AddAsset<SurfaceAsset>(surfaceAssetDataPath, surfaceAsset.id.guid);
            surfaceAsset.SetData(surface);

            if (importTextures)
            {
                EAI.Logger.Info($"Importing textures for surface asset {surfaceAssetDataPath}");

                var baseColorMap = TextureAssetImporterUtils.ImportTexture_BaseColorMap(data);
                if (baseColorMap != null) surfaceAsset.UpdateTexture(BaseColorMap, baseColorMap);

                var normalMap = TextureAssetImporterUtils.ImportTexture_NormalMap(data);
                if (normalMap != null) surfaceAsset.UpdateTexture(NormalMap, normalMap);

                var maskMap = TextureAssetImporterUtils.ImportTexture_MaskMap(data);
                if (maskMap != null) surfaceAsset.UpdateTexture(MaskMap, maskMap);
            }

            surfaceAsset.Save(force: false, saveTextures: false, vt: false);

            surface.Dispose();

            return surfaceAsset;
        }

        public static void ExportTemplateMaterialJson(string materialName, string path)
        {
            Surface surface = new Surface($"{materialName}_Template", materialName);
            Material material = surface.ToUnityMaterial();
            MaterialJson materialJson = new MaterialJson
            {
                MaterialName = materialName,
                Float = new Dictionary<string, float>(),
                Vector = new Dictionary<string, Vector4>()
            };

            foreach (string key in material.GetPropertyNames(MaterialPropertyType.Float))
            {
                materialJson.Float[key] = material.GetFloat(key);
            }

            foreach (string key in material.GetPropertyNames(MaterialPropertyType.Vector))
            {
                materialJson.Vector[key] = material.GetVector(key);
            }

            UnityEngine.Object.Destroy(material);

            File.WriteAllText(Path.Combine(path, MaterialJsonFileName), Encoder.Encode(materialJson, EncodeOptions.None));

        }

    }
}
