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

        public override Type PrefabType => typeof(NetLanePrefab);

        public override ComponentJson GetDefaultJson()
        {
            return new CurvePropertiesJson();
        }

        public override void Process(ImportData data, Variant componentJson, PrefabBase prefab)
        {
            CurvePropertiesJson curvePropertiesJson = componentJson.Make<CurvePropertiesJson>();
            if (curvePropertiesJson is null)
            {
                EAI.Logger.Error($"curveProperties component JSON is null for prefab {prefab.name}.");
                return;
            }
            CurveProperties curveProperties = prefab.AddOrGetComponent<CurveProperties>();
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
