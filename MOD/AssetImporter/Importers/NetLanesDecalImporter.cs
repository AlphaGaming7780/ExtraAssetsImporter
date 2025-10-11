using Colossal.IO.AssetDatabase;
using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.Components;
using ExtraAssetsImporter.AssetImporter.JSONs.Prefabs;
using ExtraAssetsImporter.AssetImporter.Utils;
using ExtraAssetsImporter.ClassExtension;
using Game.Prefabs;
using System;
using System.Collections.Generic;
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

        protected override IEnumerator<PrefabBase> Import(PrefabImportData data)
        {
            NetLaneGeometryPrefab netLanesPrefab = ScriptableObject.CreateInstance<NetLaneGeometryPrefab>();

            if(data.PrefabJson != null)
            {
                NetLanePrefabJson netLanePrefabJson = data.PrefabJson.Make<NetLanePrefabJson>();
                netLanePrefabJson.Process(netLanesPrefab);
            }

            //ImportersUtils.SetupUIObject(this, data, netLanesPrefab);

            RenderPrefab renderPrefab = (RenderPrefab)ImportersUtils.GetRenderPrefab(data);
            if (renderPrefab == null)
            {

                IEnumerator<SurfaceAsset> enumerator = DecalsImporterNew.AsyncCreateSurface(data, k_DefaultMaterialName);

                while (enumerator.Current == null && enumerator.MoveNext())
                {
                    yield return null;
                }

                SurfaceAsset surfaceAsset = enumerator.Current;
                enumerator.Dispose();

                //Surface surface = DecalsImporterNew.CreateSurface(data, decalsMaterail, k_DefaultMaterialName);
                Mesh[] meshes = DecalsImporterNew.CreateMeshes(surfaceAsset);

                renderPrefab = ImportersUtils.CreateRenderPrefab(data, surfaceAsset, meshes, DecalsImporterNew.SetupDecalRenderPrefab);
            }

            netLanesPrefab.AddNetLaneMeshInfo(renderPrefab);

            yield return netLanesPrefab;
        }

        protected override void VersionCompatiblity(PrefabBase prefabBase, PrefabImportData data)
        {
            base.VersionCompatiblity(prefabBase, data);

            ObsoleteIdentifiers obsoleteIdentifiers = prefabBase.AddOrGetComponent<ObsoleteIdentifiers>();

            obsoleteIdentifiers.m_PrefabIdentifiers ??= new PrefabIdentifierInfo[0];

            PrefabIdentifierInfo prefabIdentifierInfo = new()
            {
                m_Name = $"{data.ModName} {data.CatName} {data.AssetName} NetLane",
                m_Type = prefabBase.GetType().Name
            };

            obsoleteIdentifiers.m_PrefabIdentifiers = obsoleteIdentifiers.m_PrefabIdentifiers.Prepend(prefabIdentifierInfo).ToArray();

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
            SurfaceAssetImporterUtils.ExportTemplateMaterialJson(k_DefaultMaterialName, path);
        }
    }
}
