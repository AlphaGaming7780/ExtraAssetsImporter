using Colossal.AssetPipeline.Importers;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.Components;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.AssetImporter.JSONs.Prefabs;
using ExtraAssetsImporter.AssetImporter.Utils;
using ExtraAssetsImporter.ClassExtension;
using ExtraAssetsImporter.DataBase;
using ExtraAssetsImporter.Importers;
using Game.Prefabs;
using Game.Rendering;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using static Colossal.AssetPipeline.Importers.DefaultTextureImporter;

namespace ExtraAssetsImporter.AssetImporter.Importers
{
    class SurfacesImporterNew : PrefabImporterBase
    {
        public const string k_DefaultShaderName = "Shader Graphs/AreaDecalShader";

        public override string ImporterId => "Surfaces";

        public override string AssetEndName => "Surface";

        protected override IEnumerator<PrefabBase> Import(PrefabImportData data)
        {
            SurfacePrefab surfacePrefab = ScriptableObject.CreateInstance<SurfacePrefab>();
            surfacePrefab.m_Color = new(255f, 255f, 255f, 0.05f);

            if (data.PrefabJson != null)
            {
                AreaPrefabJson areaPrefabJson = data.PrefabJson.Make<AreaPrefabJson>();
                areaPrefabJson.Process(surfacePrefab);
            }

            Material material = GetMaterial(data);

            if(material == null)
            {
                // If the material is not found, we create it asynchronously.
                IEnumerator<Material> materialEnumerator = AsyncCreateMaterial(data);
                while (materialEnumerator.Current == null && materialEnumerator.MoveNext())
                {
                    yield return null;
                }
                material = materialEnumerator.Current;
            }

            RenderedArea renderedArea = surfacePrefab.AddComponent<RenderedArea>();
            renderedArea.m_RendererPriority = (int)material.GetFloat(ShaderPropertiesIDs.DrawOrder);
            renderedArea.m_Material = material;
            renderedArea.m_DecalLayerMask = (DecalLayers)material.GetFloat(ShaderPropertiesIDs.colossal_DecalLayerMask);

            ImportersUtils.SetupUIObject(this, data, surfacePrefab);

            yield return surfacePrefab;
        }

        private Material GetMaterial(PrefabImportData data)
        {
            if(data.NeedToUpdateAsset)
            {
                return null;
            }
            string materialName = GetMaterialName(data);
            //Material[] mats = Resources.FindObjectsOfTypeAll<Material>().Where( material => material.name == materialName).ToArray();

            //if(mats.Length > 0)
            //{
            //    EAI.Logger.Info("Found existing material for surface: " + materialName);
            //}

            AssetDataPath materialAssetPath = AssetDataPath.Create(data.AssetDataPath, GetMaterialFileName(data), true, EscapeStrategy.None);
            if (data.ImportSettings.dataBase.TryGetOrAddAsset<SurfaceAsset>(materialAssetPath, out SurfaceAsset surfaceAsset))
            {
                return surfaceAsset.Load();
            }

            return null;

        }

        private IEnumerator<Material> AsyncCreateMaterial(PrefabImportData data)
        {
            // Disble BC compression for surfaces.
            //ImportSettings importSettings = ImportSettings.GetDefault();
            //importSettings.compressBC = false;

            //Task<TextureAsset> baseColorMapTask = TextureAssetImporterUtils.AsyncImportTexture_BaseColorMap(data, importSettings);
            //Task<TextureAsset> normalMapTask = TextureAssetImporterUtils.AsyncImportTexture_NormalMap(data, importSettings);
            //Task<TextureAsset> maskMapTask = TextureAssetImporterUtils.AsyncImportTexture_MaskMap(data, importSettings);

            Task<TextureAsset> baseColorMapTask = TextureAssetImporterUtils.AsyncImportTexture_BaseColorMap(data);
            Task<TextureAsset> normalMapTask = TextureAssetImporterUtils.AsyncImportTexture_NormalMap(data);
            Task<TextureAsset> maskMapTask = TextureAssetImporterUtils.AsyncImportTexture_MaskMap(data);

            MaterialJson materialJson = SurfaceAssetImporterUtils.LoadMaterialJson(data);

            Material newMaterial = GetDefaultSurfaceMaterial(materialJson?.ShaderName);
            newMaterial.name = $"{data.FullAssetName}_Material";

            newMaterial.SetFloat(ShaderPropertiesIDs.DrawOrder, GetRendererPriorityByCat(data.CatName));

            if (materialJson != null)
            {
                foreach (string key in materialJson.Float.Keys) { if (newMaterial.HasFloat(key)) newMaterial.SetFloat(key, materialJson.Float[key]); }
                foreach (string key in materialJson.Vector.Keys) { if (newMaterial.HasVector(key)) newMaterial.SetVector(key, materialJson.Vector[key]); }
            }

            while (!baseColorMapTask.IsCompleted || !normalMapTask.IsCompleted || !maskMapTask.IsCompleted) yield return null;

            var baseColorMap = baseColorMapTask.Result;
            var normalMap = normalMapTask.Result;
            var maskMap = maskMapTask.Result;

            if (baseColorMap != null)
            {
                Texture2D texture = baseColorMap.Load<Texture2D>();
                texture.wrapMode = TextureWrapMode.Repeat;
                newMaterial.SetTexture(ShaderPropertiesIDs.BaseColorMap, texture);
            }

            if (normalMap != null)
            {
                Texture2D texture = normalMap.Load<Texture2D>();
                texture.wrapMode = TextureWrapMode.Repeat;
                newMaterial.SetTexture(ShaderPropertiesIDs.NormalMap, texture);
            }

            if (maskMap != null)
            {
                Texture2D texture = maskMap.Load<Texture2D>();
                texture.wrapMode = TextureWrapMode.Repeat;
                newMaterial.SetTexture(ShaderPropertiesIDs.MaskMap, texture);
            }

            // Doesn't work, we get the error "No material were mapped for {m_MaterialTemplateHash}", because the material Surface use doesn't have a template in MaterialLibrary.
            //AssetDataPath surfaceAssetPath = AssetDataPath.Create(data.AssetDataPath, GetMaterialFileName(data), EscapeStrategy.None);
            //SurfaceAsset surfaceAsset = EAIDataBaseManager.assetDataBaseEAI.AddAsset<SurfaceAsset, Material>(surfaceAssetPath, newMaterial); //Colossal.Hash128.CreateGuid(renderPrefab.name)
            //surfaceAsset.Save();

            yield return newMaterial;

        }

        private string GetMaterialName(PrefabImportData data)
        {
            return $"{data.FullAssetName}_Material";
        }

        private string GetMaterialFileName(PrefabImportData data)
        {
            return $"{data.AssetName}_Material{SurfaceAsset.kExtensions[1]}";
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

        public static IEnumerator<JSONSurfacesMaterail> AsyncLoadJSON(PrefabImportData data)
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

        public static JSONSurfacesMaterail LoadJSON(PrefabImportData data)
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

            foreach (ComponentImporter component in AssetsImporterManager.GetComponentImportersForPrefab<SurfacePrefab>())
            {
                areaPrefabJson.Components.Add(component.ComponentType.FullName, component.GetDefaultJson());
            }

            File.WriteAllText(Path.Combine(path, PrefabJsonName), Encoder.Encode(areaPrefabJson, EncodeOptions.None));

            Material material = GetDefaultSurfaceMaterial();
            MaterialJson materialJson = new MaterialJson
            {
                ShaderName = k_DefaultShaderName,
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

            File.WriteAllText(Path.Combine(path, SurfaceAssetImporterUtils.MaterialJsonFileName), Encoder.Encode(materialJson, EncodeOptions.None));

        }
    }
}
