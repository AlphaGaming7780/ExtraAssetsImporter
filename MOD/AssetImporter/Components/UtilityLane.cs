using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.JSONs.Components;
using ExtraLib;
using ExtraLib.Helpers;
using Game.Prefabs;
using System;

namespace ExtraAssetsImporter.AssetImporter.Components
{
    internal class UtilityLaneComponent : ComponentImporter
    {
        public override Type ComponentType => typeof(UtilityLane);

        public override Type PrefabType => typeof(NetLanePrefab);

        public override void Process(ImportData data, Variant componentJson, PrefabBase prefab)
        {
            UtilityLaneJson utilityLaneJson = componentJson.Make<UtilityLaneJson>();
            if (utilityLaneJson is null)
            {
                EAI.Logger.Error($"UtilityLane component JSON is null for prefab {prefab.name}.");
                return;
            }
            UtilityLane utilityLane = prefab.AddOrGetComponent<UtilityLane>();
            utilityLane.m_UtilityType = utilityLaneJson.UtilityType;
            utilityLane.m_VisualCapacity = utilityLaneJson.VisualCapacity;
            utilityLane.m_Width = utilityLaneJson.Width;
            utilityLane.m_Hanging = utilityLaneJson.Hanging;
            utilityLane.m_Underground = utilityLaneJson.Underground;

            if(utilityLaneJson.LocalConnectionLane != null && EL.m_PrefabSystem.TryGetPrefab(utilityLaneJson.LocalConnectionLane, out PrefabBase prefab1) && prefab1 is NetLanePrefab localConnectionLane)
            {
                utilityLane.m_LocalConnectionLane = localConnectionLane;
            }
            else
            {
                EAI.Logger.Warn($"Failed to get the NetLanePrefab for LocalConnectionLane with the name of {utilityLaneJson.LocalConnectionLane} for the {data.FullAssetName} asset.");
            }

            if (utilityLaneJson.LocalConnectionLane2 != null && EL.m_PrefabSystem.TryGetPrefab(utilityLaneJson.LocalConnectionLane2, out PrefabBase prefab2) && prefab2 is NetLanePrefab localConnectionLane2)
            {
                utilityLane.m_LocalConnectionLane2 = localConnectionLane2;
            }
            else
            {
                EAI.Logger.Warn($"Failed to get the NetLanePrefab for LocalConnectionLane2 with the name of {utilityLaneJson.LocalConnectionLane2} for the {data.FullAssetName} asset.");
            }

            if(utilityLaneJson.NodeObject != null && EL.m_PrefabSystem.TryGetPrefab(utilityLaneJson.NodeObject, out PrefabBase prefab3) && prefab3 is ObjectPrefab nodeObject)
            {
                utilityLane.m_NodeObject = nodeObject;
            }
            else
            {
                EAI.Logger.Warn($"Failed to get the ObjectPrefab for NodeObject with the name of {utilityLaneJson.NodeObject} for the {data.FullAssetName} asset.");
            }
        }
    }
}
