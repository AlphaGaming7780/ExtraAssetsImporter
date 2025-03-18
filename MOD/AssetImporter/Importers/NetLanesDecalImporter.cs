using Colossal.AssetPipeline;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using ExtraAssetsImporter.ClassExtension;
using ExtraAssetsImporter.DataBase;
using ExtraAssetsImporter.Importers;
using ExtraLib;
using Game.Prefabs;
using System.IO;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter.Importers
{
    class NetLanesDecalImporterNew : ImporterBase
    {
        public override string ImporterId => "NetLanes";

        public override string FolderName => "CustomNetLanes";

        public override string AssetEndName => "NetLane";

        protected override PrefabBase Import(ImportData data)
        {
            NetLaneGeometryPrefab netLanesPrefab = ScriptableObject.CreateInstance<NetLaneGeometryPrefab>();

            JsonNetLanes jsonNetLanes = LoadJSON(data);
            JSONDecalsMaterail decalsMaterail = DecalsImporterNew.LoadJSON(data);

            VersionCompatiblity(jsonNetLanes, data.CatName, data.AssetName);
            if (jsonNetLanes.prefabIdentifierInfos.Count > 0)
            {
                ObsoleteIdentifiers obsoleteIdentifiers = netLanesPrefab.AddComponent<ObsoleteIdentifiers>();
                obsoleteIdentifiers.m_PrefabIdentifiers = [.. jsonNetLanes.prefabIdentifierInfos];
            }

            ImportersUtils.SetupUIObject(this, data, netLanesPrefab, jsonNetLanes.UiPriority );

            RenderPrefab renderPrefab = (RenderPrefab)ImportersUtils.GetRenderPrefab(data);
            if (renderPrefab == null)
            {

                Surface surface = DecalsImporterNew.CreateSurface(data, decalsMaterail, "CurvedDecal");
                Mesh[] meshes = DecalsImporterNew.CreateMeshes(surface);

                renderPrefab = ImportersUtils.CreateRenderPrefab(data, surface, meshes, DecalsImporterNew.SetupDecalRenderPrefab);
            }

            if (jsonNetLanes.curveProperties != null)
            {
                CurveProperties curveProperties = renderPrefab.AddComponent<CurveProperties>();
                curveProperties.m_TilingCount = jsonNetLanes.curveProperties.TilingCount;
                curveProperties.m_SmoothingDistance = jsonNetLanes.curveProperties.SmoothingDistance;
                curveProperties.m_OverrideLength = jsonNetLanes.curveProperties.OverrideLength;
                curveProperties.m_GeometryTiling = jsonNetLanes.curveProperties.GeometryTiling;
                curveProperties.m_StraightTiling = jsonNetLanes.curveProperties.StraightTiling;
                curveProperties.m_SubFlow = jsonNetLanes.curveProperties.SubFlow;
                curveProperties.m_InvertCurve = jsonNetLanes.curveProperties.InvertCurve;
            }

            netLanesPrefab.AddNetLaneMeshInfo(renderPrefab);


            if (jsonNetLanes.PathfindPrefab != null)
            {
                if (EL.m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(PathfindPrefab), jsonNetLanes.PathfindPrefab), out PrefabBase prefabBase) && prefabBase is PathfindPrefab pathfindPrefab)
                {
                    netLanesPrefab.m_PathfindPrefab = pathfindPrefab;
                }
                else
                {
                    EAI.Logger.Warn($"Failed to get the PathfindPrefab with the name of {jsonNetLanes.PathfindPrefab} for the {data.FullAssetName} asset.");
                }
            }

            if (jsonNetLanes.utilityLane != null)
            {
                UtilityLane utilityLane = netLanesPrefab.AddComponent<UtilityLane>();
                utilityLane.m_UtilityType = jsonNetLanes.utilityLane.UtilityType;
                utilityLane.m_VisualCapacity = jsonNetLanes.utilityLane.VisualCapacity;
                utilityLane.m_Width = jsonNetLanes.utilityLane.Width;
                utilityLane.m_Hanging = jsonNetLanes.utilityLane.Hanging;
                utilityLane.m_Underground = jsonNetLanes.utilityLane.Underground;
            }

            //AssetDataPath prefabAssetPath = AssetDataPath.Create("TempAssetsFolder", data.FullAssetName + PrefabAsset.kExtension, EscapeStrategy.None);
            //EAIDataBaseManager.assetDataBaseEAI.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, netLanesPrefab, forceGuid: Colossal.Hash128.CreateGuid(data.FullAssetName));

            return netLanesPrefab;
        }

        private JsonNetLanes LoadJSON(ImportData data)
        {
            JsonNetLanes jsonNetLane = new();

            string jsonNetLanesPath = Path.Combine(data.FolderPath, "netLane.json");
            if (File.Exists(jsonNetLanesPath))
            {
                jsonNetLane = Decoder.Decode(File.ReadAllText(jsonNetLanesPath)).Make<JsonNetLanes>();
            }

            return jsonNetLane;
        }

        private static void VersionCompatiblity(JsonNetLanes jSONNetLanesMaterail, string catName, string netLanesName)
        {
            if (EAI.m_Setting.CompatibilityDropDown == EAICompatibility.LocalAsset)
            {
                PrefabIdentifierInfo prefabIdentifierInfo = new()
                {
                    m_Name = $"ExtraAssetsImporter {catName} {netLanesName} NetLane",
                    m_Type = "StaticObjectPrefab"
                };
                jSONNetLanesMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
            }
            if (EAI.m_Setting.CompatibilityDropDown == EAICompatibility.ELT3)
            {
                PrefabIdentifierInfo prefabIdentifierInfo = new()
                {
                    m_Name = $"ExtraLandscapingTools_mods_{catName}_{netLanesName}",
                    m_Type = "StaticObjectPrefab"
                };
                jSONNetLanesMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
            }
        }
    }
}
