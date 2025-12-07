using Colossal.AssetPipeline;
using Colossal.AssetPipeline.Importers;
using Colossal.AssetPipeline.Native;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.Components;
using ExtraAssetsImporter.AssetImporter.JSONs.Prefabs;
using ExtraAssetsImporter.AssetImporter.Utils;
using ExtraAssetsImporter.ClassExtension;
using Game.Prefabs;
using Game.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter.Importers
{
    class DecalsImporterNew : PrefabImporterBase
    {
        public const string k_DefaultMaterialName = "DefaultDecal";
        public override string ImporterId => "Decals";
        public override string AssetEndName => "Decal";

        protected override PrefabBase Import(PrefabImportData data)
        {
            StaticObjectPrefab decalPrefab = ScriptableObject.CreateInstance<StaticObjectPrefab>();

            RenderPrefabBase renderPrefab = RenderPrefabUtils.GetRenderPrefab(data);
            if (renderPrefab == null)
            {
                //SurfaceAsset surface = CreateSurface(data);

                //GeometryAsset geometryAsset = GeometryImporterUtils.CreateBoxGeometryAsset(data, surface);

                renderPrefab = RenderPrefabUtils.CreateRenderPrefab(data, k_DefaultMaterialName, SetupDecalRenderPrefab);
            }

            decalPrefab.AddObjectMeshInfo(renderPrefab);

            return decalPrefab;
        }

        public static void SetupDecalRenderPrefab(PrefabImportData data, RenderPrefab renderPrefab, IEnumerable<SurfaceAsset> surfaceAssets)
        {
            SurfaceAsset surface = surfaceAssets.ElementAt(0);
            Vector4 TextureArea = surface.vectors.ContainsKey("colossal_TextureArea") ? surface.vectors["colossal_TextureArea"] : new Vector4(0, 0, 1, 1);
            DecalProperties decalProperties = renderPrefab.AddOrGetComponent<DecalProperties>();
            decalProperties.m_TextureArea = new(new(TextureArea.x, TextureArea.y), new(TextureArea.z, TextureArea.w));
            decalProperties.m_LayerMask = (DecalLayers) (surface.HasProperty("colossal_DecalLayerMask") ? surface.floats["colossal_DecalLayerMask"] : 1);
            decalProperties.m_RendererPriority = (int)(surface.HasProperty("_DrawOrder") ? surface.floats["_DrawOrder"] : 0);
            decalProperties.m_EnableInfoviewColor = false;
        }




        //public ModelImporter.Model ConvertUnityMeshToModel(Mesh mesh) {

        //    int[] i = mesh.GetIndices(0);

        //    NativeArray<int> indices = new NativeArray<int>(i);

        //    mesh.te

        //    return new ModelImporter.Model(mesh.name, Matrix4x4.zero, mesh.vertexCount, i, )
        //}

        public static SurfaceAsset CreateSurface(PrefabImportData data, string materialName = k_DefaultMaterialName)
        {
            SurfaceAsset decalSurface = SurfaceAssetImporterUtils.CreateSurface(data, materialName);
            if (!decalSurface.floats.ContainsKey("colossal_DecalLayerMask")) decalSurface.AddProperty("colossal_DecalLayerMask", 1f);
            if (!decalSurface.vectors.ContainsKey("colossal_TextureArea")) decalSurface.AddProperty("colossal_TextureArea", new Vector4(0, 0, 1, 1));

            return decalSurface;
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
