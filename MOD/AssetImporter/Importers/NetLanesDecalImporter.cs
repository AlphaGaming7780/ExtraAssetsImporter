using Colossal.AssetPipeline;
using ExtraAssetsImporter.AssetImporter.JSONs.Prefabs;
using ExtraAssetsImporter.ClassExtension;
using ExtraAssetsImporter.Importers;
using ExtraLib;
using Game.Prefabs;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter.Importers
{
    class NetLanesDecalImporterNew : PrefabImporterBase
    {
        public override string ImporterId => "NetLanesDecal";

        public override string AssetEndName => "NetLaneDecal";

        public override string CatName => "NetLanes";

        protected override IEnumerator<PrefabBase> Import(ImportData data)
        {
            NetLaneGeometryPrefab netLanesPrefab = ScriptableObject.CreateInstance<NetLaneGeometryPrefab>();

            NetLanePrefabJson netLanePrefabJson = data.PrefabJson.Make<NetLanePrefabJson>();

            //IEnumerator<JsonNetLanes> enumeratorNetLanes = AsyncLoadJSON(data);
            //IEnumerator<JSONDecalsMaterail> enumeratorDecal = DecalsImporterNew.AsyncLoadJSON(data);

            //bool valueDecal = true;
            //bool valueNetLane = true;
            //while ((enumeratorDecal.Current == null && valueDecal) || ( enumeratorNetLanes.Current == null && valueNetLane))
            //{
            //    yield return null;
            //    valueDecal = enumeratorDecal.MoveNext();
            //    valueNetLane = enumeratorNetLanes.MoveNext();
            //}

            //JsonNetLanes jsonNetLanes = enumeratorNetLanes.Current;
            //JSONDecalsMaterail decalsMaterail = enumeratorDecal.Current;

            //JsonNetLanes jsonNetLanes = LoadJSON(data);

            //VersionCompatiblity(jsonNetLanes, data.CatName, data.AssetName);
            //if (jsonNetLanes.prefabIdentifierInfos.Count > 0)
            //{
            //    ObsoleteIdentifiers obsoleteIdentifiers = netLanesPrefab.AddComponent<ObsoleteIdentifiers>();
            //    obsoleteIdentifiers.m_PrefabIdentifiers = jsonNetLanes.prefabIdentifierInfos.ToArray();
            //}

            //ImportersUtils.SetupUIObject(this, data, netLanesPrefab, jsonNetLanes.UiPriority );

            RenderPrefab renderPrefab = (RenderPrefab)ImportersUtils.GetRenderPrefab(data);
            if (renderPrefab == null)
            {

                IEnumerator<Surface> enumerator = DecalsImporterNew.AsyncCreateSurface(data, "CurvedDecal");

                bool value = true;
                while (enumerator.Current == null && value)
                {
                    yield return null;
                    value = enumerator.MoveNext();
                }

                Surface surface = enumerator.Current;

                //Surface surface = DecalsImporterNew.CreateSurface(data, decalsMaterail, "CurvedDecal");
                Mesh[] meshes = DecalsImporterNew.CreateMeshes(surface);

                renderPrefab = ImportersUtils.CreateRenderPrefab(data, surface, meshes, DecalsImporterNew.SetupDecalRenderPrefab);
            }

            //if (jsonNetLanes.curveProperties != null)
            //{
            //    CurveProperties curveProperties = renderPrefab.AddComponent<CurveProperties>();
            //    curveProperties.m_TilingCount = jsonNetLanes.curveProperties.TilingCount;
            //    curveProperties.m_SmoothingDistance = jsonNetLanes.curveProperties.SmoothingDistance;
            //    curveProperties.m_OverrideLength = jsonNetLanes.curveProperties.OverrideLength;
            //    curveProperties.m_GeometryTiling = jsonNetLanes.curveProperties.GeometryTiling;
            //    curveProperties.m_StraightTiling = jsonNetLanes.curveProperties.StraightTiling;
            //    curveProperties.m_SubFlow = jsonNetLanes.curveProperties.SubFlow;
            //    curveProperties.m_InvertCurve = jsonNetLanes.curveProperties.InvertCurve;
            //}

            netLanesPrefab.AddNetLaneMeshInfo(renderPrefab);


            if (netLanePrefabJson.PathfindPrefab != null)
            {
                if (EL.m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(PathfindPrefab), netLanePrefabJson.PathfindPrefab), out PrefabBase prefabBase) && prefabBase is PathfindPrefab pathfindPrefab)
                {
                    netLanesPrefab.m_PathfindPrefab = pathfindPrefab;
                }
                else
                {
                    EAI.Logger.Warn($"Failed to get the PathfindPrefab with the name of {netLanePrefabJson.PathfindPrefab} for the {data.FullAssetName} asset.");
                }
            }

            //if (jsonNetLanes.utilityLane != null)
            //{
            //    UtilityLane utilityLane = netLanesPrefab.AddComponent<UtilityLane>();
            //    utilityLane.m_UtilityType = jsonNetLanes.utilityLane.UtilityType;
            //    utilityLane.m_VisualCapacity = jsonNetLanes.utilityLane.VisualCapacity;
            //    utilityLane.m_Width = jsonNetLanes.utilityLane.Width;
            //    utilityLane.m_Hanging = jsonNetLanes.utilityLane.Hanging;
            //    utilityLane.m_Underground = jsonNetLanes.utilityLane.Underground;
            //}

            //AssetDataPath prefabAssetPath = AssetDataPath.Create("TempAssetsFolder", data.FullAssetName + PrefabAsset.kExtension, EscapeStrategy.None);
            //EAIDataBaseManager.assetDataBaseEAI.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, netLanesPrefab, forceGuid: Colossal.Hash128.CreateGuid(data.FullAssetName));

            yield return netLanesPrefab;
        }

        private IEnumerator<JsonNetLanes> AsyncLoadJSON(ImportData data)
        {
            JsonNetLanes jsonNetLane = new();

            string jsonNetLanesPath = Path.Combine(data.FolderPath, "netLane.json");
            if (File.Exists(jsonNetLanesPath))
            {
                Task<JsonNetLanes> task = ImportersUtils.AsyncLoadJson<JsonNetLanes>(jsonNetLanesPath);

                while (!task.IsCompleted) yield return null;

                jsonNetLane = task.Result;
            }

            yield return jsonNetLane;
        }

        private JsonNetLanes LoadJSON(ImportData data)
        {
            JsonNetLanes jsonNetLane = new();

            string jsonNetLanesPath = Path.Combine(data.FolderPath, "netLane.json");
            if (File.Exists(jsonNetLanesPath))
            {
                jsonNetLane = ImportersUtils.LoadJson<JsonNetLanes>(jsonNetLanesPath);
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
