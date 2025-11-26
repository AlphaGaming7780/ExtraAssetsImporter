using Colossal.IO.AssetDatabase;
using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.Components;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.AssetImporter.JSONs.Prefabs;
using ExtraAssetsImporter.AssetImporter.Utils;
using ExtraAssetsImporter.ClassExtension;
using Game.Prefabs;
using Game.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

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
            RenderedArea renderedArea = surfacePrefab.AddComponent<RenderedArea>();

            if (data.PrefabJson != null)
            {
                AreaPrefabJson areaPrefabJson = data.PrefabJson.Make<AreaPrefabJson>();
                areaPrefabJson.Process(surfacePrefab);
            }

            if(data.ImportSettings.isAssetPack)
            {

                Task<Dictionary<string, TextureAsset>> textureAssetsTask = Task.Run(() => GetTextures(data));

                while (!textureAssetsTask.IsCompleted)
                {
                    yield return null;
                }

                Dictionary<string, TextureAsset> textureAssets = textureAssetsTask.Result;

                renderedArea.m_BaseColorMap = textureAssets[TextureAssetImporterUtils.BaseColorMapName];
                renderedArea.m_NormalMap = textureAssets[TextureAssetImporterUtils.NormalMapName];
                renderedArea.m_MaskMap = textureAssets[TextureAssetImporterUtils.MaskMapName];


            } else
            {
                Material material = GetMaterial(data);

                if (material == null)
                {
                    // If the material is not found, we create it asynchronously.
                    IEnumerator<Material> materialEnumerator = AsyncCreateMaterial(data);
                    while (materialEnumerator.Current == null && materialEnumerator.MoveNext())
                    {
                        yield return null;
                    }
                    material = materialEnumerator.Current;
                    materialEnumerator.Dispose();
                }

                renderedArea.m_RendererPriority = (int)material.GetFloat(ShaderPropertiesIDs.DrawOrder);
                renderedArea.m_Material = material;
                renderedArea.m_DecalLayerMask = (DecalLayers)material.GetFloat(ShaderPropertiesIDs.colossal_DecalLayerMask);
            }


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
            Task<Dictionary<string, TextureAsset>> textureAssetsTask = Task.Run(() => GetTextures(data));

            MaterialJson materialJson = SurfaceAssetImporterUtils.LoadMaterialJson(data);

            Material newMaterial = GetDefaultSurfaceMaterial(materialJson?.ShaderName);
            newMaterial.name = $"{data.FullAssetName}_Material";

            newMaterial.SetFloat(ShaderPropertiesIDs.DrawOrder, GetRendererPriorityByCat(data.CatName));

            if (materialJson != null)
            {
                foreach (string key in materialJson.Float.Keys) { if (newMaterial.HasFloat(key)) newMaterial.SetFloat(key, materialJson.Float[key]); }
                foreach (string key in materialJson.Vector.Keys) { if (newMaterial.HasVector(key)) newMaterial.SetVector(key, materialJson.Vector[key]); }
            }

            //while (!baseColorMapTask.IsCompleted || !normalMapTask.IsCompleted || !maskMapTask.IsCompleted) yield return null;
            while (!textureAssetsTask.IsCompleted) yield return null;
            var textureAssets = textureAssetsTask.Result;

            TextureAsset baseColorMap = textureAssets[TextureAssetImporterUtils.BaseColorMapName]; // textureAssets.ContainsKey(TextureAssetImporterUtils.BaseColorMapName) ? textureAssets[TextureAssetImporterUtils.BaseColorMapName] : null;
            TextureAsset normalMap = textureAssets[TextureAssetImporterUtils.NormalMapName]; // textureAssets.ContainsKey(TextureAssetImporterUtils.NormalMapName) ? textureAssets[TextureAssetImporterUtils.NormalMapName] : null;
            TextureAsset maskMap = textureAssets[TextureAssetImporterUtils.MaskMapName]; // textureAssets.ContainsKey(TextureAssetImporterUtils.MaskMapName) ? textureAssets[TextureAssetImporterUtils.MaskMapName] : null;


            if (baseColorMap != null)
            {
                IEnumerator<Material> mat = LoadTexture(newMaterial, baseColorMap, ShaderPropertiesIDs.BaseColorMap);
                while (mat.Current == null && mat.MoveNext())
                {
                    yield return null;
                }
                newMaterial = mat.Current;
                mat.Dispose();
            }

            if (normalMap != null)
            {

                IEnumerator<Material> mat = LoadTexture(newMaterial, normalMap, ShaderPropertiesIDs.NormalMap);
                while (mat.Current == null && mat.MoveNext())
                {
                    yield return null;
                }
                newMaterial = mat.Current;
                mat.Dispose();
            }

            if (maskMap != null)
            {

                IEnumerator<Material> mat = LoadTexture(newMaterial, maskMap, ShaderPropertiesIDs.MaskMap);
                while (mat.Current == null && mat.MoveNext())
                {
                    yield return null;
                }
                newMaterial = mat.Current;
                mat.Dispose();
            }

            // Doesn't work, we get the error "No material were mapped for {m_MaterialTemplateHash}", because the material Surface use doesn't have a template in MaterialLibrary.
            //AssetDataPath surfaceAssetPath = AssetDataPath.Create(data.AssetDataPath, GetMaterialFileName(data), EscapeStrategy.None);
            //SurfaceAsset surfaceAsset = data.ImportSettings.dataBase.AddAsset<SurfaceAsset, Material>(surfaceAssetPath, newMaterial); //Colossal.Hash128.CreateGuid(renderPrefab.name)
            //surfaceAsset.Save();

            yield return newMaterial;

        }

        private IEnumerator<Material> LoadTexture(Material material, TextureAsset textureAsset, int shaderPropertiesIDs)
        {
            // Évite les fichiers déjà utilisés ou autres problèmes.
            Texture2D texture = null;
            int i = 0;

            while (texture == null && i <= 10)
            {
                if (File.Exists(textureAsset.path))
                {
                    try
                    {
                        texture = textureAsset.Load<Texture2D>();
                    }
                    catch (Exception e)
                    {
                        EAI.Logger.Warn(e);
                    }
                }
                i++;
                yield return null;
            }

            if (texture != null)
            {
                texture.wrapMode = TextureWrapMode.Repeat;
                material.SetTexture(shaderPropertiesIDs, texture);
                yield return material;
            }

            EAI.Logger.Error($"Failed to load the texture {textureAsset} at path {textureAsset.path}");
            yield return material;
        }

        private Dictionary<string, TextureAsset> GetTextures(PrefabImportData data)
        {
            Dictionary<string, TextureAsset> textures = new();
            TextureAsset baseColorMap = TextureAssetImporterUtils.ImportTexture_BaseColorMap(data);
            //if (baseColorMap != null) 
                textures.Add(TextureAssetImporterUtils.BaseColorMapName, baseColorMap);
            TextureAsset normalMap = TextureAssetImporterUtils.ImportTexture_NormalMap(data);
            //if (normalMap != null) 
                textures.Add(TextureAssetImporterUtils.NormalMapName, normalMap);
            TextureAsset maskMap = TextureAssetImporterUtils.ImportTexture_MaskMap(data);
            //if (maskMap != null) 
                textures.Add(TextureAssetImporterUtils.MaskMapName, maskMap);
            return textures;
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
