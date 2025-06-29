using Colossal.AssetPipeline;
using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.JSONs.Prefabs;
using ExtraAssetsImporter.AssetImporter.Utils;
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
        public const string k_DefaultMaterialName = "CurvedDecal";

        public override string ImporterId => "NetLanesDecal";

        public override string AssetEndName => "NetLaneDecal";

        public override string CatName => "NetLanes";

        protected override IEnumerator<PrefabBase> Import(ImportData data)
        {
            NetLaneGeometryPrefab netLanesPrefab = ScriptableObject.CreateInstance<NetLaneGeometryPrefab>();

            if(data.PrefabJson != null)
            {
                NetLanePrefabJson netLanePrefabJson = data.PrefabJson.Make<NetLanePrefabJson>();
                netLanePrefabJson.Process(netLanesPrefab);
            }

            ImportersUtils.SetupUIObject(this, data, netLanesPrefab);

            RenderPrefab renderPrefab = (RenderPrefab)ImportersUtils.GetRenderPrefab(data);
            if (renderPrefab == null)
            {

                IEnumerator<Surface> enumerator = DecalsImporterNew.AsyncCreateSurface(data, k_DefaultMaterialName);

                bool value = true;
                while (enumerator.Current == null && value)
                {
                    yield return null;
                    value = enumerator.MoveNext();
                }

                Surface surface = enumerator.Current;

                //Surface surface = DecalsImporterNew.CreateSurface(data, decalsMaterail, k_DefaultMaterialName);
                Mesh[] meshes = DecalsImporterNew.CreateMeshes(surface);

                renderPrefab = ImportersUtils.CreateRenderPrefab(data, surface, meshes, DecalsImporterNew.SetupDecalRenderPrefab);
            }

            netLanesPrefab.AddNetLaneMeshInfo(renderPrefab);

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

        public override void ExportTemplate(string path)
        {
            path = Path.Combine(path, FolderName);
            Directory.CreateDirectory(path);
            NetLanePrefabJson netLanePrefabJson = new();
            File.WriteAllText(Path.Combine(path, PrefabJsonName), Encoder.Encode(netLanePrefabJson, EncodeOptions.None));
            SurfaceImporterUtils.ExportTemplateMaterialJson(k_DefaultMaterialName, path);
        }
    }
}
