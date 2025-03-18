using Colossal.AssetPipeline;
using Colossal.AssetPipeline.Importers;
using Colossal.IO.AssetDatabase;
using Colossal.IO.AssetDatabase.VirtualTexturing;
using ExtraAssetsImporter.DataBase;
using ExtraLib;
using ExtraLib.Helpers;
using ExtraLib.Prefabs;
using Game.Prefabs;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static Colossal.AssetPipeline.Importers.DefaultTextureImporter;

namespace ExtraAssetsImporter.AssetImporter
{
    static class ImportersUtils
    {

        public static RenderPrefabBase GetRenderPrefab(ImportData data)
        {
            if (EAIDataBaseManager.TryGetEAIAsset(data.FullAssetName, out EAIAsset asset) && asset.AssetHash == EAIDataBaseManager.GetAssetHash(data.FolderPath))
            {
                try
                {
                    EAI.Logger.Info($"Cached data for {data.FullAssetName}, loading the cache.");
                    List<PrefabBase> loadedObject = EAIDataBaseManager.LoadAsset(data.FullAssetName);
                    foreach (PrefabBase prefabBase in loadedObject)
                    {
                        if (prefabBase is not RenderPrefabBase renderPrefab) continue;
                        
                        return renderPrefab;
                        
                    }
                }
                catch (Exception e) 
                {
                    EAI.Logger.Warn($"Failed to load the cached data for {data.FullAssetName}.");
                }
            }

            EAI.Logger.Info($"No cached data for {data.FullAssetName}.");

            return null;

        }

        public static RenderPrefab CreateRenderPrefab(ImportData data, Surface surface, Mesh[] meshes, Action<ImportData, RenderPrefab, Surface, Mesh[]> setupRenderPrefab, bool useVT = false)
        {
            EAI.Logger.Info($"Creating RenderPrefab for {data.FullAssetName}.");
            SurfaceAsset surfaceAsset = SetupSurfaceAsset(data, surface, useVT);

            Vector4 MeshSize = surface.GetVectorProperty("colossal_MeshSize");

            AssetDataPath geometryAssetDataPath = AssetDataPath.Create(data.AssetDataPath, "GeometryAsset", EscapeStrategy.None);
            GeometryAsset geometryAsset = new()
            {
                id = new Identifier(Guid.NewGuid()),
                database = EAIDataBaseManager.assetDataBaseEAI
            };
            geometryAsset.database.AddAsset<GeometryAsset>(geometryAssetDataPath, geometryAsset.id.guid);
            geometryAsset.SetData(meshes);
            geometryAsset.Save(false);

            //RenderPrefab renderPrefab = (RenderPrefab)ScriptableObject.CreateInstance("RenderPrefab");
            PrefabID prefabID = new PrefabID(typeof(RenderPrefab).Name, $"{data.FullAssetName}_RenderPrefab");

            if (!EL.m_PrefabSystem.TryGetPrefab(prefabID, out PrefabBase prefabBase) || prefabBase is not RenderPrefab renderPrefab)
            {
                renderPrefab = (RenderPrefab)ScriptableObject.CreateInstance("RenderPrefab");
            }

            renderPrefab.name = $"{data.FullAssetName}_RenderPrefab";
            renderPrefab.geometryAsset = geometryAsset;//new AssetReference<GeometryAsset>(geometryAsset.guid);
            renderPrefab.surfaceAssets = [surfaceAsset];
            renderPrefab.bounds = new(new(-MeshSize.x * 0.5f, -MeshSize.y * 0.5f, -MeshSize.z * 0.5f), new(MeshSize.x * 0.5f, MeshSize.y * 0.5f, MeshSize.z * 0.5f));
            renderPrefab.meshCount = geometryAsset.data.meshCount;
            renderPrefab.vertexCount = geometryAsset.GetVertexCount(0);
            renderPrefab.indexCount = 1;
            renderPrefab.manualVTRequired = false;

            setupRenderPrefab(data, renderPrefab, surface, meshes);

            AssetDataPath renderPrefabAssetPath = AssetDataPath.Create(data.AssetDataPath, $"{data.AssetName}_RenderPrefab", EscapeStrategy.None);
            PrefabAsset renderPrefabAsset = EAIDataBaseManager.assetDataBaseEAI.AddAsset<PrefabAsset, ScriptableObject>(renderPrefabAssetPath, renderPrefab); // Colossal.Hash128.CreateGuid(fullAssetName)
            renderPrefabAsset.Save();
            //EAI.Logger.Info($"render prefab path: {renderPrefabAsset.path}\nrender prefab id: {renderPrefabAsset.id}");

            surface.Dispose();
            geometryAsset.Unload();
            surfaceAsset.Unload();

            EAIAsset asset = new(data.FullAssetName, EAIDataBaseManager.GetAssetHash(data.FolderPath), data.AssetDataPath);
            EAIDataBaseManager.AddAssets(asset);

            return renderPrefab;
        }

        public static Surface CreateSurface(ImportData data, string materialName)
        {
            Surface surface = new(data.AssetName, materialName);

            DefaultTextureImporter defaultTextureImporter = ImporterCache.GetImporter(".png") as DefaultTextureImporter;

            var baseColorMap = ImportTexture_BaseColorMap(data);
            if(baseColorMap != null) surface.AddProperty("_BaseColorMap", baseColorMap);

            var normalMap = ImportTexture_NormalMap(data);
            if(normalMap != null) surface.AddProperty("_NormalMap", normalMap);

            var maskMap = ImportTexture_MaskMap(data);
            if(maskMap != null) surface.AddProperty("_MaskMap", maskMap);

            return surface;
        }

        public static TextureImporter.Texture ImportTexture_BaseColorMap(ImportData data)
        {
            ImportSettings importSettings = ImportSettings.GetDefault();
            importSettings.compressBC = true;
            importSettings.wrapMode = TextureWrapMode.Repeat;
            return ImportTexture_BaseColorMap(data, importSettings);
        }

        public static TextureImporter.Texture ImportTexture_BaseColorMap(ImportData data, ImportSettings importSettings)
        {
            return ImportTexture(data, "_BaseColorMap.png", importSettings);
        }

        public static TextureImporter.Texture ImportTexture_NormalMap(ImportData data)
        {
            ImportSettings importSettings = ImportSettings.GetDefault();
            importSettings.overrideCompressionFormat = Colossal.AssetPipeline.Native.NativeTextures.BlockCompressionFormat.BC7;
            importSettings.wrapMode = TextureWrapMode.Repeat;
            return ImportTexture_NormalMap(data, importSettings);
        }

        public static TextureImporter.Texture ImportTexture_NormalMap(ImportData data, ImportSettings importSettings)
        {
            importSettings.normalMap = true;
            importSettings.alphaIsTransparency = false;
            return ImportTexture(data, "_NormalMap.png", importSettings);
        }

        public static TextureImporter.Texture ImportTexture_MaskMap(ImportData data)
        {
            ImportSettings importSettings = ImportSettings.GetDefault();
            importSettings.wrapMode = TextureWrapMode.Repeat;
            return ImportTexture_MaskMap(data, importSettings);
        }

        public static TextureImporter.Texture ImportTexture_MaskMap(ImportData data, ImportSettings importSettings)
        {
            importSettings.alphaIsTransparency = false;
            return ImportTexture(data, "_MaskMap.png", importSettings);
        }

        public static TextureImporter.Texture ImportTexture(ImportData data, string TextureName, ImportSettings importSettings)
        {
            string path = Path.Combine(data.FolderPath, TextureName);
            if (!File.Exists(path)) return null;

            DefaultTextureImporter defaultTextureImporter = ImporterCache.GetImporter(".png") as DefaultTextureImporter;
            return defaultTextureImporter.Import(importSettings, path);
        }

        public static SurfaceAsset SetupSurfaceAsset(ImportData data, Surface surface, bool useVT = false)
        {
            AssetDataPath surfaceAssetDataPath = AssetDataPath.Create(data.AssetDataPath, $"{data.AssetName}_SurfaceAsset", EscapeStrategy.None);
            SurfaceAsset surfaceAsset = new()
            {
                id = new Identifier(Guid.NewGuid()),
                database = EAIDataBaseManager.assetDataBaseEAI
            };
            surfaceAsset.database.AddAsset<SurfaceAsset>(surfaceAssetDataPath, surfaceAsset.id.guid);
            surfaceAsset.SetData(surface);


            if (useVT)
            {
                //VT Stuff
                VirtualTexturingConfig virtualTexturingConfig = EAI.textureStreamingSystem.virtualTexturingConfig; //(VirtualTexturingConfig)ScriptableObject.CreateInstance("VirtualTexturingConfig");
                Dictionary<Colossal.IO.AssetDatabase.TextureAsset, List<SurfaceAsset>> textureReferencesMap = [];

                foreach (Colossal.IO.AssetDatabase.TextureAsset asset in surfaceAsset.textures.Values)
                {
                    asset.Save();
                    textureReferencesMap.Add(asset, [surfaceAsset]);
                }

                surfaceAsset.Save(force: false, saveTextures: false, vt: true, virtualTexturingConfig: virtualTexturingConfig, textureReferencesMap: textureReferencesMap, tileSize: virtualTexturingConfig.tileSize, nbMidMipLevelsRequested: 0);

                //END OF VT Stuff.
            }
            else
            {
                surfaceAsset.Save(force: false, saveTextures: true, vt: false);
            }

            return surfaceAsset;
        }

        public static UIObject SetupUIObject( ImporterBase importer, ImportData data, PrefabBase prefab, int UiPriority)
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

            UIAssetChildCategoryPrefab categoryPrefab = PrefabsHelper.GetOrCreateUIAssetChildCategoryPrefab(data.AssetCat, $"{data.CatName} {data.AssetCat.name}");
            UIObject uiObject = categoryPrefab.GetComponent<UIObject>();
            if(uiObject.m_Icon == ExtraLib.Helpers.Icons.Placeholder && File.Exists(catIconPath))
            {
                uiObject.m_Icon = $"{Icons.COUIBaseLocation}/{importer.FolderName}/{data.CatName}/icon.svg";
            }

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

            //3) Define the co-ordinates of each Corner of the cube 
            Vector3[] c =
            [
                new Vector3(-length * .5f, -height * .5f, width * .5f),
                new Vector3(length * .5f, -height * .5f, width * .5f),
                new Vector3(length * .5f, -height * .5f, -width * .5f),
                new Vector3(-length * .5f, -height * .5f, -width * .5f),
                new Vector3(-length * .5f, height * .5f, width * .5f),
                new Vector3(length * .5f, height * .5f, width * .5f),
                new Vector3(length * .5f, height * .5f, -width * .5f),
                new Vector3(-length * .5f, height * .5f, -width * .5f),
            ];


            //4) Define the vertices that the cube is composed of:
            //I have used 16 vertices (4 vertices per side). 
            //This is because I want the vertices of each side to have separate normals.
            //(so the object renders light/shade correctly) 
            Vector3[] vertices =
            [
                c[0], c[1], c[2], c[3], // Bottom
			    c[7], c[4], c[0], c[3], // Left
			    c[4], c[5], c[1], c[0], // Front
			    c[6], c[7], c[3], c[2], // Back
			    c[5], c[6], c[2], c[1], // Right
			    c[7], c[6], c[5], c[4]  // Top
            ];


            //5) Define each vertex's Normal
            Vector3 up = Vector3.up;
            Vector3 down = Vector3.down;
            Vector3 forward = Vector3.forward;
            Vector3 back = Vector3.back;
            Vector3 left = Vector3.left;
            Vector3 right = Vector3.right;


            Vector3[] normals =
            [
                down, down, down, down,             // Bottom
			    left, left, left, left,             // Left
			    forward, forward, forward, forward,	// Front
			    back, back, back, back,             // Back
			    right, right, right, right,         // Right
			    up, up, up, up                      // Top
            ];

            //6) Define each vertex's UV co-ordinates
            Vector2 uv00 = new(0f, 0f);
            Vector2 uv10 = new(1f, 0f);
            Vector2 uv01 = new(0f, 1f);
            Vector2 uv11 = new(1f, 1f);

            Vector2[] uvs =
            [
                uv11, uv01, uv00, uv10, // Bottom
			    uv11, uv01, uv00, uv10, // Left
			    uv11, uv01, uv00, uv10, // Front
			    uv11, uv01, uv00, uv10, // Back	        
			    uv11, uv01, uv00, uv10, // Right 
			    uv11, uv01, uv00, uv10  // Top
            ];


            //7) Define the Polygons (triangles) that make up the our Mesh (cube)
            //IMPORTANT: Unity uses a 'Clockwise Winding Order' for determining front-facing polygons.
            //This means that a polygon's vertices must be defined in 
            //a clockwise order (relative to the camera) in order to be rendered/visible.
            int[] triangles =
            [
                3, 1, 0,        3, 2, 1,        // Bottom	
			    7, 5, 4,        7, 6, 5,        // Left
			    11, 9, 8,       11, 10, 9,      // Front
			    15, 13, 12,     15, 14, 13,     // Back
			    19, 17, 16,     19, 18, 17,	    // Right
			    23, 21, 20,     23, 22, 21,	    // Top
		    ];


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
