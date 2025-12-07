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

            ProcessMaterialJSON(surfacePrefab, data);

        }

        internal static void ProcessMaterialJSON(PrefabBase prefab, PrefabImportData data)
        {
            MaterialJson materialJson = SurfaceAssetImporterUtils.LoadMaterialJson(data);

            if (materialJson == null) return;

            RenderedArea renderedArea = prefab.AddOrGetComponent<RenderedArea>();
            renderedArea.m_RendererPriority = (int)materialJson.TryGetValue("_DrawOrder", renderedArea.m_RendererPriority);
            renderedArea.m_BaseColor = materialJson.TryGetValue("_BaseColor", renderedArea.m_BaseColor);
            renderedArea.m_DecalLayerMask = (DecalLayers)materialJson.TryGetValue("colossal_DecalLayerMask", (float)renderedArea.m_DecalLayerMask);

            renderedArea.m_Metallic = materialJson.TryGetValue("_Metallic", renderedArea.m_Metallic);
            renderedArea.m_Smoothness = materialJson.TryGetValue("_Smoothness", renderedArea.m_Smoothness);
            renderedArea.m_NormalOpacity = materialJson.TryGetValue("_NormalOpacity", renderedArea.m_NormalOpacity);
            renderedArea.m_MetallicOpacity = materialJson.TryGetValue("_MetallicOpacity", renderedArea.m_MetallicOpacity);
            renderedArea.m_NormalAlphaSource = materialJson.TryGetValue("_NormalAlphaSource", renderedArea.m_NormalAlphaSource);
            renderedArea.m_MetallicAlphaSource = materialJson.TryGetValue("_MetallicAlphaSource", renderedArea.m_MetallicAlphaSource);
            renderedArea.m_UVScale = materialJson.TryGetValue("colossal_UVScale", renderedArea.m_UVScale);
            renderedArea.m_EdgeNormal = materialJson.TryGetValue("colossal_EdgeNormal", renderedArea.m_EdgeNormal);
            renderedArea.m_EdgeFadeRange = materialJson.TryGetValue("colossal_EdgeFadeRange", renderedArea.m_EdgeFadeRange);
            renderedArea.m_EdgeNormalRange = materialJson.TryGetValue("colossal_EdgeNormalRange", renderedArea.m_EdgeNormalRange);
            renderedArea.m_EdgeNoise = materialJson.TryGetValue("colossal_EdgeNoise", renderedArea.m_EdgeNoise);

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
