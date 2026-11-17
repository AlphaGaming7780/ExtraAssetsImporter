using Colossal.IO.AssetDatabase;
using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.Components;
using ExtraAssetsImporter.AssetImporter.JSONs.Prefabs;
using ExtraAssetsImporter.AssetImporter.Utils;
using ExtraAssetsImporter.ClassExtension;
using Game.Prefabs;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter.Importers
{
    class NetLanesDecalImporterNew : PrefabImporterBase
    {
        public const string k_DefaultMaterialName = "CurvedDecal";

        public override string ImporterId => "NetLanesDecal";

        public override string AssetEndName => "NetLaneDecal";

        public override string CatName => "NetLanes";

        protected override PrefabBase Import(PrefabImportData data)
        {
            NetLaneGeometryPrefab netLanesPrefab = ImportersUtils.GetOrCreatePrefabInstance<NetLaneGeometryPrefab>(data.FullAssetName);

            if(data.PrefabJson != null)
            {
                NetLanePrefabJson netLanePrefabJson = data.PrefabJson.MakeInvariant<NetLanePrefabJson>();
                netLanePrefabJson.Process(netLanesPrefab);
            }

            RenderPrefab renderPrefab = (RenderPrefab)ImportersUtils.GetRenderPrefab(data);
            if (renderPrefab == null)
            {
                SurfaceAsset surfaceAsset = DecalsImporterNew.CreateSurface(data, k_DefaultMaterialName);
                Mesh[] meshes = DecalsImporterNew.CreateMeshes(surfaceAsset);

                renderPrefab = ImportersUtils.CreateRenderPrefab(data, surfaceAsset, meshes, DecalsImporterNew.SetupDecalRenderPrefab, typeof(DecalProperties));
            }

            netLanesPrefab.AddNetLaneMeshInfo(renderPrefab);

            return netLanesPrefab;
        }

        public override void ExportTemplate(string path)
        {
            path = Path.Combine(path, FolderName);
            Directory.CreateDirectory(path);
            NetLanePrefabJson netLanePrefabJson = new();

            foreach (ComponentImporter component in AssetsImporterManager.GetComponentImportersForPrefab<NetLaneGeometryPrefab>())
            {
                netLanePrefabJson.Components.Add(component.ComponentType.FullName, component.GetDefaultJson());
            }

            File.WriteAllText(Path.Combine(path, PrefabJsonName), Encoder.Encode(netLanePrefabJson, EncodeOptions.None));

            // Components of the render prefab (the mesh), such as CurveProperties.
            RenderPrefabJson renderPrefabJson = new();
            foreach (ComponentImporter component in AssetsImporterManager.GetComponentImportersForPrefab<RenderPrefab>())
            {
                renderPrefabJson.Components.Add(component.ComponentType.FullName, component.GetDefaultJson());
            }

            File.WriteAllText(Path.Combine(path, ImportersUtils.RenderPrefabJsonName), Encoder.Encode(renderPrefabJson, EncodeOptions.None));
            SurfaceAssetImporterUtils.ExportTemplateMaterialJson(k_DefaultMaterialName, path);
        }
    }
}
