using Colossal.AssetPipeline;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.Utils;
using ExtraAssetsImporter.ClassExtension;
using ExtraAssetsImporter.DataBase;
using ExtraLib;
using ExtraLib.Helpers;
using ExtraLib.Prefabs;
using Game.Prefabs;
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;


namespace ExtraAssetsImporter.AssetImporter
{
    static class ImportersUtils
    {
        public static RenderPrefabBase GetRenderPrefab(PrefabImportData data)
        {

            if (data.NeedToUpdateAsset)
            {
                EAI.Logger.Info($"Need to update the cached data for {data.FullAssetName}.");
                return null;
            }

            PrefabID prefabID = new PrefabID(nameof(RenderPrefabBase), GetRenderPrefabName(data));
            if (EL.m_PrefabSystem.TryGetPrefab(prefabID, out PrefabBase prefabBase) && prefabBase is RenderPrefabBase renderPrefab)
            {
                EAI.Logger.Info($"RenderPrefab for {data.FullAssetName} was already loaded and in the prefab system.");
                return renderPrefab;
            }

            try
            {
                EAI.Logger.Info($"Cached data for {data.FullAssetName}, loading the cache.");

                AssetDataPath renderPrefabAssetPath = AssetDataPath.Create(data.AssetDataPath, GetRenderPrefabFileName(data), true, EscapeStrategy.None);
                if (data.ImportSettings.dataBase.TryLoadPrefab<RenderPrefabBase>(renderPrefabAssetPath, out renderPrefab))
                {
                    EL.m_PrefabSystem.AddPrefab(renderPrefab);
                    return renderPrefab;
                }

                //if (EAIDataBaseManager.TryLoadPrefab<RenderPrefabBase>(data.EAIAsset, GetRenderPrefabFileName(data), out renderPrefab))
                //{
                //    EL.m_PrefabSystem.AddPrefab(renderPrefab);
                //    return renderPrefab;
                //}

                EAI.Logger.Info($"No cached data for {data.FullAssetName}.");
                return null;

            }
            catch (Exception e)
            {
                EAI.Logger.Warn($"Failed to load the cached data for {data.FullAssetName}.\nException:{e}.");
            }

            return null;

            //if (EAIDataBaseManager.IsAssetsInDataBase(data.EAIAsset))
            //{
            //    if(data.EAIAsset.AssetHash != EAIDataBaseManager.GetAssetHash(data.FolderPath))
            //    {
            //        EAI.Logger.Info($"Need to update the cached data for {data.FullAssetName}.");
            //        return null;
            //    }

            //    try
            //    {
            //        EAI.Logger.Info($"Cached data for {data.FullAssetName}, loading the cache.");
            //        List<PrefabBase> loadedObject = EAIDataBaseManager.LoadAsset(data.FullAssetName);
            //        foreach (PrefabBase prefabBase in loadedObject)
            //        {
            //            if (prefabBase is not RenderPrefabBase renderPrefab) continue;

            //            return renderPrefab;

            //        }
            //    }
            //    catch (Exception e) 
            //    {
            //        EAI.Logger.Warn($"Failed to load the cached data for {data.FullAssetName}.\nException:{e}.");
            //    }

            //}

            //EAI.Logger.Info($"No cached data for {data.FullAssetName}.");

            //return null;

        }

        public static RenderPrefab CreateRenderPrefab(PrefabImportData data, SurfaceAsset surfaceAsset, Mesh[] meshes, Action<PrefabImportData, RenderPrefab, SurfaceAsset, Mesh[]> setupRenderPrefab, bool useVT = false)
        {
            EAI.Logger.Info($"Creating RenderPrefab for {data.FullAssetName}.");
            //SurfaceAsset surfaceAsset =  SurfaceImporterUtils.SetupSurfaceAsset(data, surface, useVT);
            surfaceAsset.Save(false);

            string pathToRenderPrefabJson = Path.Combine(data.FolderPath, "RenderPrefab.json");
            Variant renderPrefabVariant = File.Exists(pathToRenderPrefabJson) ? ImportersUtils.LoadJson(pathToRenderPrefabJson) : null;

            Vector4 MeshSize = surfaceAsset.vectors["colossal_MeshSize"];

            AssetDataPath geometryAssetDataPath = AssetDataPath.Create(data.AssetDataPath, "GeometryAsset", EscapeStrategy.None);
            GeometryAsset geometryAsset = new()
            {
                id = new Identifier(Guid.NewGuid()),
                database = EAIDataBaseManager.EAIAssetDataBase
            };
            geometryAsset.database.AddAsset<GeometryAsset>(geometryAssetDataPath, geometryAsset.id.guid);
            geometryAsset.SetData(meshes);
            geometryAsset.Save(false);

            //RenderPrefab renderPrefab = (RenderPrefab)ScriptableObject.CreateInstance("RenderPrefab");
            PrefabID prefabID = new PrefabID(typeof(RenderPrefab).Name, GetRenderPrefabName(data));

            if (!EL.m_PrefabSystem.TryGetPrefab(prefabID, out PrefabBase prefabBase) || prefabBase is not RenderPrefab renderPrefab)
            {
                renderPrefab = (RenderPrefab)ScriptableObject.CreateInstance("RenderPrefab");
            }

            renderPrefab.name = GetRenderPrefabName(data); // $"{data.FullAssetName}_RenderPrefab";
            renderPrefab.geometryAsset = geometryAsset;//new AssetReference<GeometryAsset>(geometryAsset.guid);
            renderPrefab.surfaceAssets = new[] { surfaceAsset };
            renderPrefab.bounds = new(new(-MeshSize.x * 0.5f, -MeshSize.y * 0.5f, -MeshSize.z * 0.5f), new(MeshSize.x * 0.5f, MeshSize.y * 0.5f, MeshSize.z * 0.5f));
            renderPrefab.meshCount = geometryAsset.data.meshCount;
            renderPrefab.vertexCount = geometryAsset.GetVertexCount(0);
            renderPrefab.indexCount = 1;
            renderPrefab.manualVTRequired = false;

            setupRenderPrefab(data, renderPrefab, surfaceAsset, meshes);

            if(renderPrefabVariant != null) AssetsImporterManager.ProcessComponentImporters(data, renderPrefabVariant, renderPrefab);

            AssetDataPath renderPrefabAssetPath = AssetDataPath.Create(data.AssetDataPath, GetRenderPrefabFileName(data), true, EscapeStrategy.None);
            PrefabAsset renderPrefabAsset = EAIDataBaseManager.EAIAssetDataBase.AddAsset<PrefabAsset, ScriptableObject>(renderPrefabAssetPath, renderPrefab); //Colossal.Hash128.CreateGuid(renderPrefab.name)
            renderPrefabAsset.Save();
            //EAI.Logger.Info($"render prefab path: {renderPrefabAsset.path}\nrender prefab id: {renderPrefabAsset.id}");

            geometryAsset.Unload();
            surfaceAsset.Unload();

            //EAIAsset asset = new(data.FullAssetName, EAIDataBaseManager.GetAssetHash(data.FolderPath), data.AssetDataPath);
            //EAIDataBaseManager.AddAssets(asset);

            return renderPrefab;
        }

        public static string GetRenderPrefabName(PrefabImportData data)
        {
            return $"{data.FullAssetName}_RenderPrefab";
        }

        public static string GetRenderPrefabFileName(PrefabImportData data)
        {
            return $"{data.AssetName}_RenderPrefab{PrefabAsset.kExtension}";
        }

        public static Task<T> AsyncLoadJson<T>(string path) where T : class
        {
            return Task.Run(() => LoadJson<T>(path));
        }

        public static T LoadJson<T>(string path) where T : class
        {
            return Decoder.Decode(File.ReadAllText(path)).Make<T>();
        }

        public static Variant LoadJson(string path)
        {
            return Decoder.Decode(File.ReadAllText(path));
        }

        public static UIObject SetupUIObject( FolderImporter importer, PrefabImportData data, PrefabBase prefab, int UiPriority = 0)
        {

            string iconPath = Path.Combine(data.FolderPath, "icon.png");
            string baseColorMapPath = Path.Combine(data.FolderPath, "_BaseColorMap.png");
            Texture2D texture2D_Icon = new(1, 1);
            if (File.Exists(iconPath))
            {
                byte[] fileData = File.ReadAllBytes(iconPath);

                if (texture2D_Icon.LoadImage(fileData))
                {
                    if (texture2D_Icon.width > 128 || texture2D_Icon.height > 128)
                    {
                        TextureHelper.ResizeTexture(ref texture2D_Icon, 128, iconPath);
                    }
                }
            }
            else if (File.Exists(baseColorMapPath))
            {
                byte[] fileData = File.ReadAllBytes(baseColorMapPath);
                if (texture2D_Icon.LoadImage(fileData))
                {
                    if (texture2D_Icon.width > 128 || texture2D_Icon.height > 128)
                    {
                        TextureHelper.ResizeTexture(ref texture2D_Icon, 128, iconPath);
                    }
                }
            }
            UnityEngine.Object.Destroy(texture2D_Icon);


            string catIconPath = Path.Combine(Directory.GetParent(data.FolderPath).FullName, "icon.svg");

            UIAssetChildCategoryPrefab categoryPrefab = PrefabsHelper.GetOrCreateUIAssetChildCategoryPrefab(data.AssetCat, $"{data.CatName} {data.AssetCat.name}", File.Exists(catIconPath) ? $"{Icons.COUIBaseLocation}/{importer.FolderName}/{data.CatName}/icon.svg": null);
            //UIObject uiObject = categoryPrefab.GetComponent<UIObject>();
            //if(uiObject.m_Icon == ExtraLib.Helpers.Icons.Placeholder && File.Exists(catIconPath))
            //{
            //    uiObject.m_Icon = $"{Icons.COUIBaseLocation}/{importer.FolderName}/{data.CatName}/icon.svg";
            //}

            UIObject prefabUI = prefab.AddComponent<UIObject>();
            prefabUI.m_IsDebugObject = false;
            prefabUI.m_Icon = File.Exists(iconPath) ? $"{Icons.COUIBaseLocation}/{importer.FolderName}/{data.CatName}/{data.AssetName}/icon.png" : Icons.DecalPlaceholder;
            prefabUI.m_Priority = UiPriority;
            prefabUI.m_Group = categoryPrefab;
            return prefabUI;
        }

        public static Mesh CreateBoxMesh(float length, float height, float width)
        {
            Mesh mesh = new();

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
