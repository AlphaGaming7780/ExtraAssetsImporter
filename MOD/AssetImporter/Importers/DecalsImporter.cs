using Colossal.AssetPipeline;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.Components;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.AssetImporter.JSONs.Prefabs;
using ExtraAssetsImporter.AssetImporter.Utils;
using ExtraAssetsImporter.ClassExtension;
using ExtraAssetsImporter.Importers;
using Game.Prefabs;
using Game.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter.Importers
{
    class DecalsImporterNew : PrefabImporterBase
    {
        public const string k_DefaultMaterialName = "DefaultDecal";
        public override string ImporterId => "Decals";
        public override string AssetEndName => "Decal";

        protected override IEnumerator<PrefabBase> Import(PrefabImportData data)
        {
            StaticObjectPrefab decalPrefab = ScriptableObject.CreateInstance<StaticObjectPrefab>();

            ImportersUtils.SetupUIObject(this, data, decalPrefab);

            RenderPrefabBase renderPrefab = ImportersUtils.GetRenderPrefab(data);
            if (renderPrefab == null)
            {

                IEnumerator<SurfaceAsset> enumerator = AsyncCreateSurface(data);

                while (enumerator.Current == null && enumerator.MoveNext())
                {
                    yield return null;
                }

                SurfaceAsset surface = enumerator.Current;
                Mesh[] meshes = CreateMeshes(surface);

                renderPrefab = ImportersUtils.CreateRenderPrefab(data, surface, meshes, SetupDecalRenderPrefab);
            }

            decalPrefab.AddObjectMeshInfo(renderPrefab);

            // Fixe for 1.3.3f1, have to remove that or intgrate it a bit better.
            PlaceableObject placeableObject = decalPrefab.AddComponent<PlaceableObject>();
            placeableObject.m_ConstructionCost = 0;
            placeableObject.m_XPReward = 0;

            yield return decalPrefab;
        }

        public static void SetupDecalRenderPrefab(PrefabImportData data, RenderPrefab renderPrefab, SurfaceAsset surface, Mesh[] meshes)
        {
            Vector4 TextureArea = surface.vectors.ContainsKey("colossal_TextureArea") ? surface.vectors["colossal_TextureArea"] : new Vector4(0, 0, 1, 1);
            DecalProperties decalProperties = renderPrefab.AddOrGetComponent<DecalProperties>();
            decalProperties.m_TextureArea = new(new(TextureArea.x, TextureArea.y), new(TextureArea.z, TextureArea.w));
            decalProperties.m_LayerMask = (DecalLayers)surface.floats["colossal_DecalLayerMask"];
            decalProperties.m_RendererPriority = (int)(surface.HasProperty("_DrawOrder") ? surface.floats["_DrawOrder"] : 0);
            decalProperties.m_EnableInfoviewColor = false;
        } 


        public static Mesh[] CreateMeshes(SurfaceAsset surface)
        {
            if(!surface.vectors.ContainsKey("colossal_MeshSize"))
            {
                surface.AddProperty("colossal_MeshSize", new Vector4(1f, 1f, 1f, 0f));
            }
            Vector4 MeshSize = surface.vectors["colossal_MeshSize"];
            return new[] { ImportersUtils.CreateBoxMesh(MeshSize.x, MeshSize.y, MeshSize.z) };
        }

        public static SurfaceAsset CreateSurface(PrefabImportData data, string materialName = k_DefaultMaterialName)
        {
            SurfaceAsset decalSurface = SurfaceAssetImporterUtils.CreateSurface(data, materialName);

            if(decalSurface.floats["colossal_DecalLayerMask"] == 0) decalSurface.AddProperty("colossal_DecalLayerMask", 1f);

            //decalSurface.AddProperty("colossal_DecalLayerMask", 1);

            //foreach (string key in decalsMaterail.Float.Keys)
            //{
            //    if (key == "UiPriority") continue;
            //    decalSurface.AddProperty(key, decalsMaterail.Float[key]);
            //}
            //foreach (string key in decalsMaterail.Vector.Keys) { decalSurface.AddProperty(key, decalsMaterail.Vector[key]); }

            return decalSurface;
        }

        public static IEnumerator<SurfaceAsset> AsyncCreateSurface(PrefabImportData data, string materialName = k_DefaultMaterialName)
        {

            //IEnumerator<Surface> enumerator = TexturesImporterUtils.AsyncCreateSurface(data, materialName);s
            Task<SurfaceAsset> task = SurfaceAssetImporterUtils.AsyncCreateSurface(data, materialName);

            //bool value = true;
            //while (enumerator.Current == null && value)
            //{
            //    yield return null;
            //    value = enumerator.MoveNext();
            //}

            //Surface decalSurface = enumerator.Current;

            while (!task.IsCompleted) yield return null;

            SurfaceAsset decalSurface = task.Result;

            if (!decalSurface.floats.ContainsKey("colossal_DecalLayerMask")) decalSurface.AddProperty("colossal_DecalLayerMask", 1f);
            if (!decalSurface.vectors.ContainsKey("colossal_TextureArea")) decalSurface.AddProperty("colossal_TextureArea", new Vector4(0, 0, 1, 1));

            yield return decalSurface;
        }

        private static void VersionCompatiblity(JSONDecalsMaterail jSONDecalsMaterail, string catName, string decalName)
        {
            if (EAI.m_Setting.CompatibilityDropDown == EAICompatibility.LocalAsset)
            {
                PrefabIdentifierInfo prefabIdentifierInfo = new()
                {
                    m_Name = $"ExtraAssetsImporter {catName} {decalName} Decal",
                    m_Type = "StaticObjectPrefab"
                };
                jSONDecalsMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
            }
            if (EAI.m_Setting.CompatibilityDropDown == EAICompatibility.ELT3)
            {
                PrefabIdentifierInfo prefabIdentifierInfo = new()
                {
                    m_Name = $"ExtraLandscapingTools_mods_{catName}_{decalName}",
                    m_Type = "StaticObjectPrefab"
                };
                jSONDecalsMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
            }
        }

        public static IEnumerator<JSONDecalsMaterail> AsyncLoadJSON(PrefabImportData data)
        {
            JSONDecalsMaterail decalsMaterail = new();
            string jsonDecalPath = Path.Combine(data.FolderPath, "decal.json");
            if (File.Exists(jsonDecalPath))
            {
                Task<JSONDecalsMaterail> task = ImportersUtils.AsyncLoadJson<JSONDecalsMaterail>(jsonDecalPath);

                while (!task.IsCompleted) yield return null;

                decalsMaterail = task.Result;

                if (decalsMaterail.Float.ContainsKey("UiPriority")) decalsMaterail.UiPriority = (int)decalsMaterail.Float["UiPriority"];
            }

            yield return decalsMaterail;

        }

        public static JSONDecalsMaterail LoadJSON(PrefabImportData data)
        {

            JSONDecalsMaterail decalsMaterail = new();
            string jsonDecalPath = Path.Combine(data.FolderPath, "decal.json");
            if (File.Exists(jsonDecalPath))
            {
                decalsMaterail = ImportersUtils.LoadJson<JSONDecalsMaterail>(jsonDecalPath);

                if (decalsMaterail.Float.ContainsKey("UiPriority")) decalsMaterail.UiPriority = (int)decalsMaterail.Float["UiPriority"];
            }
            return decalsMaterail;
        }

        public override void ExportTemplate(string path)
        {
            PrefabJson prefabJson = new PrefabJson();

            foreach (ComponentImporter component in AssetsImporterManager.GetComponentImportersForPrefab<StaticObjectPrefab>())
            {
                prefabJson.Components.Add(component.ComponentType.FullName, component.GetDefaultJson());
            }

            path = Path.Combine(path, FolderName);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, PrefabJsonName), Encoder.Encode(prefabJson, EncodeOptions.None));
            SurfaceAssetImporterUtils.ExportTemplateMaterialJson(k_DefaultMaterialName, path);
        }
    }
}
