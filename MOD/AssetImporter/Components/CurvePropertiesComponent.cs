using ExtraAssetsImporter.ClassExtension;
using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.AssetImporter.JSONs.Components;
using Game.Prefabs;
using System;

namespace ExtraAssetsImporter.AssetImporter.Components
{
    public class CurvePropertiesComponent : ComponentImporter
    {
        public override Type ComponentType => typeof(CurveProperties);

        // The game reads CurveProperties on the mesh (RenderPrefab), see MeshSystem and ManagedBatchSystem:
        // it goes in RenderPrefab.json, like the old importer that added it to the render prefab.
        public override Type PrefabType => typeof(RenderPrefab);

        public override ComponentJson GetDefaultJson()
        {
            return new CurvePropertiesJson();
        }

        public override void Process(PrefabImportData data, Variant componentJson, PrefabBase prefab)
        {
            CurvePropertiesJson curvePropertiesJson = componentJson.MakeInvariant<CurvePropertiesJson>();
            if (curvePropertiesJson is null)
            {
                EAI.Logger.Error($"curveProperties component JSON is null for prefab {data.FullAssetName}.");
                return;
            }
            CurveProperties curveProperties = prefab.AddOrGetComponentOnMainThread<CurveProperties>();
            curveProperties.m_TilingCount = curvePropertiesJson.TilingCount;
            curveProperties.m_OverrideLength = curvePropertiesJson.OverrideLength;
            curveProperties.m_SmoothingDistance = curvePropertiesJson.SmoothingDistance;
            curveProperties.m_GeometryTiling = curvePropertiesJson.GeometryTiling;
            curveProperties.m_StraightTiling = curvePropertiesJson.StraightTiling;
            curveProperties.m_SubFlow = curvePropertiesJson.SubFlow;
            curveProperties.m_InvertCurve = curvePropertiesJson.InvertCurve;
            curveProperties.m_HangingSwaying = curvePropertiesJson.HangingSwaying;
        }
    }
}
