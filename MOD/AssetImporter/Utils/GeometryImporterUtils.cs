using Colossal.AssetPipeline;
using Colossal.AssetPipeline.Collectors;
using Colossal.AssetPipeline.Diagnostic;
using Colossal.AssetPipeline.Importers;
using Colossal.IO.AssetDatabase;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.ClassExtension;
using Game.Prefabs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using static Colossal.AssetPipeline.Importers.FBXImporter;
using Hash128 = Colossal.Hash128;
using MainThreadDispatcher = Colossal.Core.MainThreadDispatcher;

namespace ExtraAssetsImporter.AssetImporter.Utils
{
    internal static class GeometryImporterUtils
    {

        private static FBXImporter fbxImporter = ImporterCache.GetImporter(".fbx") as FBXImporter;

        public const string LOD0_Name = "LOD0.fbx";
        public const string LOD1_Name = "LOD1.fbx";
        public const string LOD2_Name = "LOD2.fbx";


        private static readonly List<string> s_GeometryPaths = new List<string>();

        private static object _lock = new object();

        public static void SetupRenderPrefab( RenderPrefab renderPrefab, Geometry geometry, PrefabImportData data, int lodLevel)
        {
            string geometryName = $"_LOD{lodLevel}";
            AssetDataPath geometryDataPath = AssetDataPath.Create(data.AssetDataPath, GetGeometryFullFileName(data, geometryName), true, EscapeStrategy.None);
            string fullAssetGeometryName = GetFullAssetGeometryName(data, geometryName);
            using (GeometryAsset geometryAsset = data.ImportSettings.dataBase.AddAsset<GeometryAsset, Geometry>(geometryDataPath, geometry, Hash128.CreateGuid(fullAssetGeometryName)))
            {
                geometryAsset.Save();
                renderPrefab.geometryAsset = geometryAsset;
                renderPrefab.bounds = geometry.CalcBounds();
                renderPrefab.surfaceArea = geometry.CalcSurfaceArea();
                renderPrefab.indexCount = geometry.CalcTotalIndices();
                renderPrefab.vertexCount = geometry.CalcTotalVertices();
                renderPrefab.meshCount = geometry.models.Length;
            }
        }

        public static IEnumerable<Geometry> ImportGeometryAssets(PrefabImportData data)
        {
            List<Geometry> assets = new();

            Geometry LOD0 = ImportLOD0(data);
            Geometry LOD1 = ImportLOD1(data);
            Geometry LOD2 = ImportLOD2(data);

            if(LOD0 != null) assets.Add(LOD0);
            if(LOD1 != null) assets.Add(LOD1);
            if(LOD2 != null) assets.Add(LOD2);

            return assets;
        }

        public static Geometry ImportLOD(PrefabImportData data, int lodLevel)
        {
            ImportSettings settings = ImportSettings.GetDefault();
            return ImportGeometry(data, $"LOD{lodLevel}.fbx", settings);
        }

        public static Geometry ImportLOD0(PrefabImportData data)
        {
            ImportSettings settings = ImportSettings.GetDefault();
            return ImportGeometry(data, LOD0_Name, settings);
        }

        public static Geometry ImportLOD1(PrefabImportData data)
        {
            ImportSettings settings = ImportSettings.GetDefault();
            return ImportGeometry(data, LOD1_Name, settings);
        }

        public static Geometry ImportLOD2(PrefabImportData data)
        {
            ImportSettings settings = ImportSettings.GetDefault();
            return ImportGeometry(data, LOD2_Name, settings);
        }

        public static Geometry ImportGeometry(PrefabImportData data, string GeometryFileName, ImportSettings importSettings)
        {
            string path = Path.Combine(data.FolderPath, GeometryFileName);

            if (!File.Exists(path)) return null;

            string GeometryName = Path.GetFileNameWithoutExtension(path);

            //if (!File.Exists(path))
            //{
            //    string jsonPath = Path.Combine(data.FolderPath, $"{GeometryName}.json");

            //    if (!File.Exists(jsonPath))
            //        return null;

            //    // Read and process Geometry referencing between multiple assets

            //    return ImportersUtils.LoadJson<GeometryJson>(jsonPath).LoadGeometry(importSettings, data, GeometryFileName, GeometryName);
            //}

            AssetDataPath GeometryDataPath = AssetDataPath.Create(data.AssetDataPath, GetGeometryFullFileName(data, GeometryName), true, EscapeStrategy.None);

            return ImportGeometry_Impl(importSettings, data, path, GeometryDataPath, GetFullAssetGeometryName(data, GeometryName));
        }



        internal static Geometry ImportGeometry_Impl(ImportSettings importSettings, PrefabImportData data, string geometryFilePath, AssetDataPath geometryDataPath, string fullAssetGeometryName)
        {

            //while (IsGeometryBeingImported(geometryFilePath))
            //{
            //    EAI.Logger.Info($"{data.FullAssetName} is waiting for {geometryFilePath}.");
            //    Thread.Sleep(500);
            //}

            //if (!data.ImportSettings.dataBase.TryGetOrAddAsset(geometryDataPath, out GeometryAsset geometryAsset ))
            //{

            //bool value = false;
            //lock (_lock)
            //{
            //    value = s_GeometryPaths.Contains(geometryFilePath);
            //    if (!value) s_GeometryPaths.Add(geometryFilePath);
            //}

            //if (value) return ImportGeometry_Impl(importSettings, data, geometryFilePath, geometryDataPath, fullAssetGeometryName); // Go back waiting for your turn.

            //var assetGroupe = data.AssetsCollector.First(v => v.First(t => t.path == geometryFilePath) != null);
            //var asset = assetGroupe.First(t => t.path == geometryFilePath);

            //Report.FileReport report = new(new ReportFile(asset));

            //if (!fbxImporter.Import<Geometry>(importSettings, geometryFilePath, report, out Geometry geometry))
            //{
            //    EAI.Logger.Error($"Error occured during importation of FBX: {report}");
            //}

            //geometryAsset = data.ImportSettings.dataBase.AddAsset<GeometryAsset, Geometry>(geometryDataPath, geometry, Hash128.CreateGuid(fullAssetGeometryName));
            //geometryAsset.Save();
            //geometryAsset.Unload();
            //geometry.Dispose();

            //lock (_lock)
            //{
            //    s_GeometryPaths.Remove(geometryFilePath);
            //}
            //}

            //var assetGroupe = data.AssetsCollector.First(v => v.First(t => t.path == geometryFilePath) != null);
            //var asset = assetGroupe.First(t => t.path == geometryFilePath);

            SourceAssetCollector.Asset asset = new();

            Report.FileReport report = new(new ReportFile(asset));

            if (!fbxImporter.Import<ModelImporter.ModelList>(importSettings, geometryFilePath, report, out var modelList))
            {
                EAI.Logger.Error($"Error occured during importation of FBX: {report}");
            }

            if(report.hasErrors)
            {
                EAI.Logger.Error($"Error occured during importation of FBX: {report}");
            }


            if (report.hasWarnings)
            {
                EAI.Logger.Warn($"{report}");
            }

            if (modelList.models is null) EAI.Logger.Error("Modellist is null.");

            Geometry geometry = new Geometry(modelList.models);

            return geometry;
        }

        private static bool IsGeometryBeingImported(string path)
        {
            lock (_lock)
            {
                return s_GeometryPaths.Contains(path);
            }
        }

        public static string GetFullAssetGeometryName(PrefabImportData data, string GeometryName)
        {
            return $"{data.FullAssetName}{GeometryName}";
        }

        public static string GetFullAssetGeometryName(string FullAssetName, string GeometryName)
        {
            return $"{FullAssetName}{GeometryName}";
        }

        public static string GetGeometryFullFileName(PrefabImportData data, string GeometryName)
        {
            return $"{data.AssetName}{GeometryName}{GeometryAsset.kExtension}";
        }

        public static string GetGeometryFullFileName(string assetName, string GeometryName)
        {
            return $"{assetName}{GeometryName}{GeometryAsset.kExtension}";
        }

        public static GeometryAsset CreateBoxGeometryAsset(PrefabImportData data, SurfaceAsset surface)
        {
            if (!surface.vectors.ContainsKey("colossal_MeshSize"))
            {
                surface.AddProperty("colossal_MeshSize", new Vector4(1f, 1f, 1f, 0f));
            }
            Vector4 MeshSize = surface.vectors["colossal_MeshSize"];

            Task<Mesh> task = CreateBoxMeshAsyncOnMainThread(MeshSize);

            task.Wait();

            Mesh[] meshes = new[] { task.Result };

            AssetDataPath geometryAssetDataPath = AssetDataPath.Create(data.AssetDataPath, "GeometryAsset", EscapeStrategy.None);
            GeometryAsset geometryAsset = new()
            {
                id = new Identifier(Guid.NewGuid()),
                database = data.ImportSettings.dataBase
            };
            geometryAsset.database.AddAsset<GeometryAsset>(geometryAssetDataPath, geometryAsset.id.guid);
            geometryAsset.SetData(meshes);
            geometryAsset.Save(false);

            return geometryAsset;
        }

        private static Task<Mesh> CreateBoxMeshAsyncOnMainThread(Vector3 size)
        {
            var tcs = new TaskCompletionSource<Mesh>();

            MainThreadDispatcher.RunOnMainThread(() =>
            {
                try
                {
                    var mesh = CreateBoxMesh(size.x, size.y, size.z);
                    tcs.SetResult(mesh);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });

            return tcs.Task;
        }

        private static Mesh CreateBoxMesh(float length, float height, float width)
        {
            Mesh mesh = new();
            mesh.name = $"Box_{length}_{height}_{width}";

            Vector3[] c = new[]
            {
                new Vector3(-length * .5f, -height * .5f, width * .5f),
                new Vector3(length * .5f, -height * .5f, width * .5f),
                new Vector3(length * .5f, -height * .5f, -width * .5f),
                new Vector3(-length * .5f, -height * .5f, -width * .5f),
                new Vector3(-length * .5f, height * .5f, width * .5f),
                new Vector3(length * .5f, height * .5f, width * .5f),
                new Vector3(length * .5f, height * .5f, -width * .5f),
                new Vector3(-length * .5f, height * .5f, -width * .5f),
            };


            //4) Define the vertices that the cube is composed of:
            //I have used 16 vertices (4 vertices per side). 
            //This is because I want the vertices of each side to have separate normals.
            //(so the object renders light/shade correctly) 
            Vector3[] vertices = new[]
            {
                c[0], c[1], c[2], c[3], // Bottom
			    c[7], c[4], c[0], c[3], // Left
			    c[4], c[5], c[1], c[0], // Front
			    c[6], c[7], c[3], c[2], // Back
			    c[5], c[6], c[2], c[1], // Right
			    c[7], c[6], c[5], c[4]  // Top
            };


            //5) Define each vertex's Normal
            Vector3 up = Vector3.up;
            Vector3 down = Vector3.down;
            Vector3 forward = Vector3.forward;
            Vector3 back = Vector3.back;
            Vector3 left = Vector3.left;
            Vector3 right = Vector3.right;


            Vector3[] normals = new[]
            {
                down, down, down, down,             // Bottom
			    left, left, left, left,             // Left
			    forward, forward, forward, forward,	// Front
			    back, back, back, back,             // Back
			    right, right, right, right,         // Right
			    up, up, up, up                      // Top
            };

            //6) Define each vertex's UV co-ordinates
            Vector2 uv00 = new(0f, 0f);
            Vector2 uv10 = new(1f, 0f);
            Vector2 uv01 = new(0f, 1f);
            Vector2 uv11 = new(1f, 1f);

            Vector2[] uvs = new[]
            {
                uv11, uv01, uv00, uv10, // Bottom
			    uv11, uv01, uv00, uv10, // Left
			    uv11, uv01, uv00, uv10, // Front
			    uv11, uv01, uv00, uv10, // Back	        
			    uv11, uv01, uv00, uv10, // Right 
			    uv11, uv01, uv00, uv10  // Top
            };


            //7) Define the Polygons (triangles) that make up the our Mesh (cube)
            //IMPORTANT: Unity uses a 'Clockwise Winding Order' for determining front-facing polygons.
            //This means that a polygon's vertices must be defined in 
            //a clockwise order (relative to the camera) in order to be rendered/visible.
            int[] triangles = new[]
            {
                3, 1, 0,        3, 2, 1,        // Bottom	
			    7, 5, 4,        7, 6, 5,        // Left
			    11, 9, 8,       11, 10, 9,      // Front
			    15, 13, 12,     15, 14, 13,     // Back
			    19, 17, 16,     19, 18, 17,	    // Right
			    23, 21, 20,     23, 22, 21,     // Top
            };


            //8) Build the Mesh
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.Optimize();
            // mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            return mesh;
        }

    }
}
