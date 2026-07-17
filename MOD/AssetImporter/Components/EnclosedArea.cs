using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.AssetImporter.JSONs.Components;
using ExtraLib;
using Game.Net;
using Game.Prefabs;
using System;

namespace ExtraAssetsImporter.AssetImporter.Components
{
    public class EnclosedAreaComponent : ComponentImporter
    {
        public override Type ComponentType => typeof(EnclosedArea);

        public override Type PrefabType => typeof(SurfacePrefab);

        public override ComponentJson GetDefaultJson()
        {
            return new EnclosedAreaJson()
            {
                m_BorderLaneType = new PrefabIDJson()
                {
                    Name = "Border Lane Name",
                    Type = "BorderLaneType, Can be NetLanePrefab NetLaneGeometryPrefab"
                },
                m_CounterClockWise = false,
            };
        }

        public override void Process(PrefabImportData data, Variant componentJson, PrefabBase prefab)
        {
            EnclosedAreaJson json = componentJson.Make<EnclosedAreaJson>();
            if (json is null)
            {
                EAI.Logger.Error($"EnclosedArea component JSON is null for prefab {prefab.name}.");
                return;
            }
            EnclosedArea enclosedArea = prefab.AddOrGetComponent<EnclosedArea>();
            if (json.m_BorderLaneType != null)
            {
                if(EL.m_PrefabSystem.TryGetPrefab(json.m_BorderLaneType, out PrefabBase prefab1) && prefab1 is NetLanePrefab localConnectionLane)
                    enclosedArea.m_BorderLaneType = localConnectionLane;
                else
                    EAI.Logger.Warn($"Failed to get the NetLanePrefab for m_BorderLaneType with the name of {json.m_BorderLaneType.Name} for the {data.FullAssetName} asset.");
                
            }
        }
    }
}
