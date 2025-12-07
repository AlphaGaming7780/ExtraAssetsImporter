using Colossal.AssetPipeline;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.Localization;
using ExtraAssetsImporter.ClassExtension;
using ExtraLib;
using Game.Prefabs;
using Game.Prefabs.Climate;
using Game.SceneFlow;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Hash128 = Colossal.Hash128;
using MainThreadDispatcher = Colossal.Core.MainThreadDispatcher;

namespace ExtraAssetsImporter.AssetImporter.Utils
{
    public class LOD
    {
        public Geometry geometry;
        public SurfaceAsset[] surfaces;
        public int level;

        public LOD(Geometry geometry, SurfaceAsset[] surfaces, int level)
        {
            this.geometry = geometry;
            this.surfaces = surfaces;
            this.level = level;
        }

    }

    public static class RenderPrefabUtils
    {
        public static RenderPrefab GetRenderPrefab(PrefabImportData data)
        {

            if (data.NeedToUpdateAsset)
            {
                EAI.Logger.Info($"Need to update the cached data for {data.FullAssetName}.");
                return null;
            }

            string renderPrefabName = GetRenderPrefabName(data);
            PrefabID prefabID = new PrefabID(nameof(RenderPrefab), renderPrefabName, Hash128.CreateGuid(renderPrefabName));
            if (EL.m_PrefabSystem.TryGetPrefab(prefabID, out PrefabBase prefabBase) && prefabBase is RenderPrefab renderPrefab)
            {
                EAI.Logger.Info($"RenderPrefab for {data.FullAssetName} was already loaded and in the prefab system.");
                return renderPrefab;
            }

            try
            {
                AssetDataPath renderPrefabAssetPath = AssetDataPath.Create(data.AssetDataPath, GetRenderPrefabFileName(data), true, EscapeStrategy.None);
                if (data.ImportSettings.dataBase.TryLoadPrefab<RenderPrefab>(renderPrefabAssetPath, out renderPrefab))
                {
                    EAI.Logger.Info($"Cached data for {data.FullAssetName}, loading the cache.");

                    if (!EL.m_PrefabSystem.TryGetPrefab(renderPrefab.GetPrefabID(), out _))
                    {
                        EAI.Logger.Info($"Adding {renderPrefab.name} to the prefab system.");
                        MainThreadDispatcher.RunOnMainThread(() => EL.m_PrefabSystem.AddPrefab(renderPrefab));
                    }

                    return renderPrefab;
                }

                EAI.Logger.Info($"No cached data for {data.FullAssetName}.");
                return null;

            }
            catch (Exception e)
            {
                EAI.Logger.Warn($"Failed to load the cached data for {data.FullAssetName}.\nException:{e}.");
            }

            return null;


        }

        public static RenderPrefab CreateRenderPrefab(PrefabImportData data, string defaultMaterialName, Action<PrefabImportData, RenderPrefab, IEnumerable<SurfaceAsset>> setupRenderPrefab, bool useVT = false)
        {
            List<LOD> lods = new List<LOD>();

            SurfaceAsset surfaceAsset = null;
            for (int i = 0; i < 3; i++)
            {
                Geometry geometry = GeometryImporterUtils.ImportLOD(data, i);

                if (geometry == null) break;

                if (TextureAssetImporterUtils.TexturesExist(data, i))
                {
                    surfaceAsset = SurfaceAssetImporterUtils.CreateSurface(data, defaultMaterialName, i);
                }

                LOD lod = new(geometry, new[] { surfaceAsset }, i);
                lods.Add(lod);
            }

            if (lods.Count > 0)
            {
                EAI.Logger.Info("Creating LOD render prefab.");
                return CreateRenderPrefabWithLOD(data, lods, setupRenderPrefab, useVT);
            }

            EAI.Logger.Info("Creating Non LOD render prefab.");

            SurfaceAsset surface = SurfaceAssetImporterUtils.CreateSurface(data, defaultMaterialName);

            GeometryAsset geometryAsset = GeometryImporterUtils.CreateBoxGeometryAsset(data, surface);

            if (surface is null) EAI.Logger.Error($"Surface is null {data.FullAssetName}");

            return CreateRenderPrefabNoLOD(data, new[] { surface }, geometryAsset, setupRenderPrefab, useVT);
        }

        public static RenderPrefab CreateRenderPrefabWithLOD(PrefabImportData data, IReadOnlyList<LOD> LODs, Action<PrefabImportData, RenderPrefab, IEnumerable<SurfaceAsset>> setupRenderPrefab, bool useVT = false)
        {
            List<RenderPrefab> renderPrefabs = new List<RenderPrefab>();
            foreach (LOD lod in LODs)
            {
                RenderPrefab renderPrefab = PrefabBase.Create<RenderPrefab>(GetRenderPrefabName(data));

                renderPrefabs.Insert(lod.level, renderPrefab);

                Geometry geometry = lod.geometry;

                if (geometry is null) EAI.Logger.Error("Geometry is null, it shouldn't be null");

                GeometryImporterUtils.SetupRenderPrefab(renderPrefab, geometry, data, lod.level);
                renderPrefab.surfaceAssets = lod.surfaces;

                setupRenderPrefab(data, renderPrefab, lod.surfaces);

            }

            RenderPrefab mainRenderPrefab = renderPrefabs[0];

            //Setup LODS
            if (renderPrefabs.Count > 1)
            {
                ProceduralAnimationProperties component = mainRenderPrefab.GetComponent<ProceduralAnimationProperties>();
                ContentPrerequisite component2 = mainRenderPrefab.GetComponent<ContentPrerequisite>();
                LodProperties lodProperties = mainRenderPrefab.AddOrGetComponent<LodProperties>();
                lodProperties.m_LodMeshes = new RenderPrefab[renderPrefabs.Count - 1];
                for (int i = 1; i < renderPrefabs.Count; i++)
                {
                    if (component != null)
                    {
                        ProceduralAnimationProperties proceduralAnimationProperties = renderPrefabs[i].prefab.AddComponentFrom(component);
                        proceduralAnimationProperties.m_Animations = null;
                    }
                    if (component2 != null)
                    {
                        renderPrefabs[i].prefab.AddComponentFrom(component2);
                    }
                    lodProperties.m_LodMeshes[i - 1] = renderPrefabs[i];
                }
            }
            else if (renderPrefabs.Count == 1)
            {
                renderPrefabs[0].Remove<LodProperties>();
            }

            for (int i = 1; i < renderPrefabs.Count; i++)
            {
                RenderPrefab renderPrefab = renderPrefabs[i];
                AssetDataPath renderPrefabAssetPath = AssetDataPath.Create(data.AssetDataPath, GetRenderPrefabFileName(data, i), true, EscapeStrategy.None);
                PrefabAsset renderPrefabAsset = data.ImportSettings.dataBase.AddAsset<PrefabAsset, ScriptableObject>(renderPrefabAssetPath, renderPrefab, Hash128.CreateGuid(renderPrefab.name));
                renderPrefabAsset.Save();
            }

            return mainRenderPrefab;

        }

        private static RenderPrefab CreateRenderPrefabNoLOD(PrefabImportData data, IEnumerable<SurfaceAsset> surfaceAssets, GeometryAsset geometryAsset, Action<PrefabImportData, RenderPrefab, IEnumerable<SurfaceAsset>> setupRenderPrefab, bool useVT = false)
        {
            EAI.Logger.Info($"Creating RenderPrefab for {data.FullAssetName}.");

            string pathToRenderPrefabJson = Path.Combine(data.FolderPath, "RenderPrefab.json");
            Variant renderPrefabVariant = File.Exists(pathToRenderPrefabJson) ? ImportersUtils.LoadJson(pathToRenderPrefabJson) : null;

            SurfaceAsset surfaceAsset = surfaceAssets.ElementAt(0);
            Vector4 MeshSize = surfaceAsset.vectors["colossal_MeshSize"];

            RenderPrefab renderPrefab = PrefabBase.Create<RenderPrefab>(GetRenderPrefabName(data));
            renderPrefab.name = GetRenderPrefabName(data); // $"{data.FullAssetName}_RenderPrefab";
            renderPrefab.geometryAsset = geometryAsset;//new AssetReference<GeometryAsset>(geometryAsset.guid);
            renderPrefab.surfaceAssets = surfaceAssets;
            renderPrefab.bounds = new(new(-MeshSize.x * 0.5f, -MeshSize.y * 0.5f, -MeshSize.z * 0.5f), new(MeshSize.x * 0.5f, MeshSize.y * 0.5f, MeshSize.z * 0.5f));
            renderPrefab.meshCount = geometryAsset.meshCount; //renderPrefab.surfaceAssets.Count(); 
            renderPrefab.vertexCount = geometryAsset.GetVertexCount(0);
            renderPrefab.indexCount = 1;
            renderPrefab.manualVTRequired = false;

            setupRenderPrefab(data, renderPrefab, surfaceAssets);

            if (renderPrefabVariant != null) AssetsImporterManager.ProcessComponentImporters(data, renderPrefabVariant, renderPrefab);

            AssetDataPath renderPrefabAssetPath = AssetDataPath.Create(data.AssetDataPath, GetRenderPrefabFileName(data), true, EscapeStrategy.None);
            PrefabAsset renderPrefabAsset = data.ImportSettings.dataBase.AddAsset<PrefabAsset, ScriptableObject>(renderPrefabAssetPath, renderPrefab, Hash128.CreateGuid(renderPrefab.name));
            renderPrefabAsset.Save();
            //EAI.Logger.Info($"render prefab path: {renderPrefabAsset.path}\nrender prefab id: {renderPrefabAsset.id}");

            geometryAsset.Unload();
            surfaceAsset.Unload();

            //EAIAsset asset = new(data.FullAssetName, EAIDataBaseManager.GetAssetHash(data.FolderPath), data.AssetDataPath);
            //EAIDataBaseManager.AddAssets(asset);

            return renderPrefab;
        }

        public static string GetRenderPrefabName(PrefabImportData data, int lodLevel = -1)
        {
            return lodLevel < 1 ? $"{data.FullAssetName}_RenderPrefab" : $"{data.FullAssetName}_LOD{lodLevel}_RenderPrefab";
        }

        public static string GetRenderPrefabFileName(PrefabImportData data, int lodLevel = -1)
        {
            return lodLevel < 1 ? $"{data.AssetName}_RenderPrefab{PrefabAsset.kExtension}" : $"{data.AssetName}_LOD{lodLevel}_RenderPrefab{PrefabAsset.kExtension}";
        }
    }
}
