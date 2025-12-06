using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.Components;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.AssetImporter.JSONs.Prefabs;
using ExtraAssetsImporter.AssetImporter.Utils;
using Game.Prefabs;
using Game.Rendering;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter.Importers
{
    class SurfacesImporterNew : PrefabImporterBase
    {
        public const string k_DefaultShaderName = "Shader Graphs/AreaDecalShader";

        public override string ImporterId => "Surfaces";

        public override string AssetEndName => "Surface";

        protected override PrefabBase Import(PrefabImportData data)
        {
            SurfacePrefab surfacePrefab = ScriptableObject.CreateInstance<SurfacePrefab>();
            surfacePrefab.m_Color = new(255f, 255f, 255f, 0.05f);

            if (data.PrefabJson != null)
            {
                AreaPrefabJson areaPrefabJson = data.PrefabJson.Make<AreaPrefabJson>();
                areaPrefabJson.Process(surfacePrefab);
            }

            SetupRenderedArea(surfacePrefab, data);

            return surfacePrefab;
        }

        private void SetupRenderedArea(SurfacePrefab surfacePrefab, PrefabImportData data )
        {
            RenderedArea renderedArea = surfacePrefab.AddComponent<RenderedArea>();

            renderedArea.m_BaseColorMap = TextureAssetImporterUtils.ImportTexture_BaseColorMap(data);
            renderedArea.m_NormalMap = TextureAssetImporterUtils.ImportTexture_NormalMap(data);
            renderedArea.m_MaskMap = TextureAssetImporterUtils.ImportTexture_MaskMap(data);
        }

        private Material GetDefaultSurfaceMaterial(string shaderName = k_DefaultShaderName)
        {
            if (string.IsNullOrEmpty(shaderName)) shaderName = k_DefaultShaderName;

            Material material = new(Shader.Find(shaderName));
            material.SetFloat(ShaderPropertiesIDs.DecalColorMask0, 15);
            material.SetFloat(ShaderPropertiesIDs.DecalColorMask1, 15);
            material.SetFloat(ShaderPropertiesIDs.DecalColorMask2, 11);
            material.SetFloat(ShaderPropertiesIDs.DecalColorMask3, 8);
            material.SetFloat(ShaderPropertiesIDs.DecalStencilRef, 16);
            material.SetFloat(ShaderPropertiesIDs.DecalStencilWriteMask, 16);
            material.SetFloat(ShaderPropertiesIDs.colossal_DecalLayerMask, 1);
            material.enableInstancing = true;
            material.shaderKeywords = new[] { "_MATERIAL_AFFECTS_ALBEDO", "_MATERIAL_AFFECTS_MASKMAP", "_MATERIAL_AFFECTS_NORMAL" };
            return material;
        }
        public override void ExportTemplate(string path)
        {
            path = Path.Combine(path, FolderName);
            Directory.CreateDirectory(path);
            AreaPrefabJson areaPrefabJson = new();

            foreach (ComponentImporter component in AssetsImporterManager.GetComponentImportersForPrefab<SurfacePrefab>())
            {
                areaPrefabJson.Components.Add(component.ComponentType.FullName, component.GetDefaultJson());
            }

            File.WriteAllText(Path.Combine(path, PrefabJsonName), Encoder.Encode(areaPrefabJson, EncodeOptions.None));

            //Material material = GetDefaultSurfaceMaterial();
            //MaterialJson materialJson = new MaterialJson
            //{
            //    ShaderName = k_DefaultShaderName,
            //    Float = new Dictionary<string, float>(),
            //    Vector = new Dictionary<string, Vector4>()
            //};

            //foreach (string key in material.GetPropertyNames(MaterialPropertyType.Float))
            //{
            //    if(materialJson.Float.ContainsKey(key))
            //        materialJson.Float[key] = material.GetFloat(key);
            //    else
            //        materialJson.Float.Add(key, material.GetFloat(key));
            //}

            //foreach (string key in material.GetPropertyNames(MaterialPropertyType.Vector))
            //{
            //    if(materialJson.Vector.ContainsKey(key))
            //        materialJson.Vector[key] = material.GetVector(key);
            //    else
            //        materialJson.Vector.Add(key, material.GetVector(key));
            //}

            //UnityEngine.Object.Destroy(material);

            //File.WriteAllText(Path.Combine(path, SurfaceAssetImporterUtils.MaterialJsonFileName), Encoder.Encode(materialJson, EncodeOptions.None));

        }
    }
}
