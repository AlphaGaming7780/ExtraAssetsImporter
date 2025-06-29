using Colossal.AssetPipeline.Importers;
using Colossal.Json;
using ExtraAssetsImporter.AssetImporter;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.AssetImporter.JSONs.Prefabs;
using ExtraAssetsImporter.AssetImporter.Utils;
using ExtraAssetsImporter.Importers;
using Game.Prefabs;
using Game.Rendering;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using static Colossal.AssetPipeline.Importers.DefaultTextureImporter;

namespace ExtraAssetsImporter.MOD.AssetImporter.Importers
{
    class SurfacesImporterNew : PrefabImporterBase
    {
        public override string ImporterId => "Surfaces";

        public override string AssetEndName => "Surface";

        protected override IEnumerator<PrefabBase> Import(ImportData data)
        {
            ImportSettings importSettings = default;
            importSettings.compressBC = false;

            Task<TextureImporter.Texture> baseColorMapTask = TexturesImporterUtils.AsyncImportTexture_BaseColorMap(data, importSettings);
            Task<TextureImporter.Texture> normalMapTask = TexturesImporterUtils.AsyncImportTexture_NormalMap(data, importSettings);
            Task<TextureImporter.Texture> maskMapTask = TexturesImporterUtils.AsyncImportTexture_MaskMap(data, importSettings);

            SurfacePrefab surfacePrefab = ScriptableObject.CreateInstance<SurfacePrefab>();
            surfacePrefab.m_Color = new(255f, 255f, 255f, 0.05f);

            if (data.PrefabJson != null)
            {
                AreaPrefabJson areaPrefabJson = data.PrefabJson.Make<AreaPrefabJson>();
                areaPrefabJson.Process(surfacePrefab);
            }

            Material newMaterial = GetDefaultSurfaceMaterial();
            newMaterial.name = data.FullAssetName + " Material";

            newMaterial.SetFloat(ShaderPropertiesIDs.DrawOrder, GetRendererPriorityByCat(data.CatName));

            MaterialJson materialJson = SurfaceImporterUtils.LoadMaterialJson(data);

            if(materialJson != null)
            {
                foreach (string key in materialJson.Float.Keys) { if (newMaterial.HasFloat(key)) newMaterial.SetFloat(key, materialJson.Float[key]); }
                foreach (string key in materialJson.Vector.Keys) { if (newMaterial.HasVector(key)) newMaterial.SetVector(key, materialJson.Vector[key]); }
            }

            while (!baseColorMapTask.IsCompleted || !normalMapTask.IsCompleted || !maskMapTask.IsCompleted) yield return null;

            var baseColorMap = baseColorMapTask.Result;
            var normalMap = normalMapTask.Result;
            var maskMap = maskMapTask.Result;

            //var baseColorMap = ImportersUtils.ImportTexture_BaseColorMap(data, importSettings);
            if (baseColorMap != null)
            {
                Texture2D texture = (Texture2D)baseColorMap.ToUnityTexture(false);
                baseColorMap.Dispose();
                texture.wrapMode = TextureWrapMode.Repeat;
                newMaterial.SetTexture(ShaderPropertiesIDs.BaseColorMap, texture);
            }

            //var normalMap = ImportersUtils.ImportTexture_NormalMap(data, importSettings);
            if (normalMap != null)
            {
                Texture2D texture = (Texture2D)normalMap.ToUnityTexture(false);
                normalMap.Dispose();
                texture.wrapMode = TextureWrapMode.Repeat;
                newMaterial.SetTexture(ShaderPropertiesIDs.NormalMap, texture);
            }

            //var maskMap = ImportersUtils.ImportTexture_MaskMap(data, importSettings);
            if (maskMap != null)
            {
                Texture2D texture = (Texture2D)maskMap.ToUnityTexture(false);
                maskMap.Dispose();
                texture.wrapMode = TextureWrapMode.Repeat;
                newMaterial.SetTexture(ShaderPropertiesIDs.MaskMap, texture);
            }

            RenderedArea renderedArea = surfacePrefab.AddComponent<RenderedArea>();
            renderedArea.m_RendererPriority = (int)newMaterial.GetFloat(ShaderPropertiesIDs.DrawOrder);
            renderedArea.m_Material = newMaterial;
            renderedArea.m_DecalLayerMask = (DecalLayers)newMaterial.GetFloat(ShaderPropertiesIDs.colossal_DecalLayerMask);

            ImportersUtils.SetupUIObject(this, data, surfacePrefab);

            yield return surfacePrefab;
        }

        private int GetRendererPriorityByCat(string cat)

        {
            return cat switch
            {
                "Ground" => -100,
                "Grass" => -99,
                "Sand" => -98,
                "Concrete" => -97,
                "Wood" => -97,
                "Pavement" => -96,
                "Tiles" => -95,
                _ => -100
            };
        }

        private Material GetDefaultSurfaceMaterial()
        {
            Material material = new(Shader.Find("Shader Graphs/AreaDecalShader"));
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

        public static IEnumerator<JSONSurfacesMaterail> AsyncLoadJSON(ImportData data)
        {
            JSONSurfacesMaterail surfacesMaterail = new();

            string jsonSurfacePath = Path.Combine(data.FolderPath, "surface.json");

            if (File.Exists(jsonSurfacePath))
            {
                Task<JSONSurfacesMaterail> task = ImportersUtils.AsyncLoadJson<JSONSurfacesMaterail>(jsonSurfacePath);

                while (!task.IsCompleted) yield return null;

                surfacesMaterail = task.Result;

            }
            yield return surfacesMaterail;
        }

        public static JSONSurfacesMaterail LoadJSON(ImportData data)
        {
            JSONSurfacesMaterail surfacesMaterail = new();

            string jsonSurfacePath = Path.Combine(data.FolderPath, "surface.json");

            if (File.Exists(jsonSurfacePath))
            {
                surfacesMaterail = ImportersUtils.LoadJson<JSONSurfacesMaterail>(jsonSurfacePath);
            }
            return surfacesMaterail;
        }

        private void VersionCompatiblity(JSONSurfacesMaterail jSONSurfacesMaterail, string catName, string surfaceName)
        {
            if (EAI.m_Setting.CompatibilityDropDown == EAICompatibility.LocalAsset)
            {
                PrefabIdentifierInfo prefabIdentifierInfo = new()
                {
                    m_Name = $"ExtraAssetsImporter {catName} {surfaceName} Surface",
                    m_Type = "StaticObjectPrefab"
                };
                jSONSurfacesMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
            }
            if (EAI.m_Setting.CompatibilityDropDown == EAICompatibility.ELT2)
            {
                PrefabIdentifierInfo prefabIdentifierInfo = new()
                {
                    m_Name = $"{surfaceName}",
                    m_Type = "SurfacePrefab"
                };
                jSONSurfacesMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
            }
            if (EAI.m_Setting.CompatibilityDropDown == EAICompatibility.ELT3)
            {
                PrefabIdentifierInfo prefabIdentifierInfo = new()
                {
                    m_Name = $"ExtraLandscapingTools_mods_{catName}_{surfaceName}",
                    m_Type = "SurfacePrefab"
                };
                jSONSurfacesMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
            }
        }

        public override void ExportTemplate(string path)
        {
            path = Path.Combine(path, FolderName);
            Directory.CreateDirectory(path);
            AreaPrefabJson areaPrefabJson = new();
            File.WriteAllText(Path.Combine(path, PrefabJsonName), Encoder.Encode(areaPrefabJson, EncodeOptions.None));

            Material material = GetDefaultSurfaceMaterial();
            MaterialJson materialJson = new MaterialJson
            {
                Float = new Dictionary<string, float>(),
                Vector = new Dictionary<string, Vector4>()
            };

            foreach (string key in material.GetPropertyNames(MaterialPropertyType.Float))
            {
                if(materialJson.Float.ContainsKey(key))
                    materialJson.Float[key] = material.GetFloat(key);
                else
                    materialJson.Float.Add(key, material.GetFloat(key));
            }

            foreach (string key in material.GetPropertyNames(MaterialPropertyType.Vector))
            {
                if(materialJson.Vector.ContainsKey(key))
                    materialJson.Vector[key] = material.GetVector(key);
                else
                    materialJson.Vector.Add(key, material.GetVector(key));
            }

            UnityEngine.Object.Destroy(material);

            File.WriteAllText(Path.Combine(path, SurfaceImporterUtils.MaterialJsonFileName), Encoder.Encode(materialJson, EncodeOptions.None));

        }
    }
}
