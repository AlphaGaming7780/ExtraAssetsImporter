﻿using Colossal.AssetPipeline;
using ExtraAssetsImporter.ClassExtension;
using ExtraAssetsImporter.Importers;
using Game.Prefabs;
using Game.Rendering;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter.Importers
{
    class DecalsImporterNew : PrefabImporterBase
    {
        public override string ImporterId => "Decals";
        public override string FolderName => "CustomDecals";
        public override string AssetEndName => "Decal";

        protected override IEnumerator<PrefabBase> Import(ImportData data)
        {
            StaticObjectPrefab decalPrefab = ScriptableObject.CreateInstance<StaticObjectPrefab>();

            //IEnumerator<JSONDecalsMaterail> enumerator = AsyncLoadJSON(data);

            //bool value = true;
            //while (enumerator.Current == null && value)
            //{
            //    yield return null;
            //    value = enumerator.MoveNext();
            //}

            //JSONDecalsMaterail decalsMaterail = enumerator.Current;
            JSONDecalsMaterail decalsMaterail = LoadJSON(data);

            VersionCompatiblity(decalsMaterail, data.CatName, data.AssetName);
            if (decalsMaterail.prefabIdentifierInfos.Count > 0)
            {
                ObsoleteIdentifiers obsoleteIdentifiers = decalPrefab.AddComponent<ObsoleteIdentifiers>();
                obsoleteIdentifiers.m_PrefabIdentifiers = [.. decalsMaterail.prefabIdentifierInfos];
            }

            ImportersUtils.SetupUIObject(this, data, decalPrefab, decalsMaterail.UiPriority);

            RenderPrefabBase renderPrefab = ImportersUtils.GetRenderPrefab(data);
            if (renderPrefab == null)
            {

                IEnumerator<Surface> enumerator = AsyncCreateSurface(data, decalsMaterail);

                bool value = true;
                while (enumerator.Current == null && value)
                {
                    yield return null;
                    value = enumerator.MoveNext();
                }

                Surface surface = enumerator.Current;
                //Surface surface = CreateSurface(data, decalsMaterail);
                Mesh[] meshes = CreateMeshes(surface);

                renderPrefab = ImportersUtils.CreateRenderPrefab(data, surface, meshes, SetupDecalRenderPrefab);
            }

            decalPrefab.AddObjectMeshInfo(renderPrefab);

            //AssetDataPath prefabAssetPath = AssetDataPath.Create("TempAssetsFolder", data.FullAssetName + PrefabAsset.kExtension, EscapeStrategy.None);
            //EAIDataBaseManager.assetDataBaseEAI.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, decalPrefab, forceGuid: Colossal.Hash128.CreateGuid(data.FullAssetName));

            yield return decalPrefab;
        }

        public static void SetupDecalRenderPrefab(ImportData data, RenderPrefab renderPrefab, Surface surface, Mesh[] meshes)
        {
            Vector4 TextureArea = surface.GetVectorProperty("colossal_TextureArea");
            DecalProperties decalProperties = renderPrefab.AddOrGetComponent<DecalProperties>();
            decalProperties.m_TextureArea = new(new(TextureArea.x, TextureArea.y), new(TextureArea.z, TextureArea.w));
            decalProperties.m_LayerMask = (DecalLayers)surface.GetFloatProperty("colossal_DecalLayerMask");
            decalProperties.m_RendererPriority = (int)(surface.HasProperty("_DrawOrder") ? surface.GetFloatProperty("_DrawOrder") : 0);
            decalProperties.m_EnableInfoviewColor = false;
        } 


        public static Mesh[] CreateMeshes(Surface surface)
        {
            Vector4 MeshSize = surface.GetVectorProperty("colossal_MeshSize");
            return [ImportersUtils.CreateBoxMesh(MeshSize.x, MeshSize.y, MeshSize.z)];
        }

        public static Surface CreateSurface(ImportData data, JSONDecalsMaterail decalsMaterail, string materialName = "DefaultDecal")
        {
            Surface decalSurface = ImportersUtils.CreateSurface(data, materialName);
            decalSurface.AddProperty("colossal_DecalLayerMask", 1);

            foreach (string key in decalsMaterail.Float.Keys)
            {
                if (key == "UiPriority") continue;
                decalSurface.AddProperty(key, decalsMaterail.Float[key]);
            }
            foreach (string key in decalsMaterail.Vector.Keys) { decalSurface.AddProperty(key, decalsMaterail.Vector[key]); }

            return decalSurface;
        }

        public static IEnumerator<Surface> AsyncCreateSurface(ImportData data, JSONDecalsMaterail decalsMaterail, string materialName = "DefaultDecal")
        {

            IEnumerator<Surface> enumerator = ImportersUtils.AsyncCreateSurface(data, materialName);

            bool value = true;
            while (enumerator.Current == null && value)
            {
                yield return null;
                value = enumerator.MoveNext();
            }

            Surface decalSurface = enumerator.Current;

            decalSurface.AddProperty("colossal_DecalLayerMask", 1);

            foreach (string key in decalsMaterail.Float.Keys)
            {
                if (key == "UiPriority") continue;
                decalSurface.AddProperty(key, decalsMaterail.Float[key]);
            }
            foreach (string key in decalsMaterail.Vector.Keys) { decalSurface.AddProperty(key, decalsMaterail.Vector[key]); }

            yield return decalSurface;
        }

        private static void VersionCompatiblity(JSONDecalsMaterail jSONDecalsMaterail, string catName, string decalName)
        {
            if (EAI.m_Setting.CompatibilityDropDown == EAICompatibility.LocalAsset)
            {
                PrefabIdentifierInfo prefabIdentifierInfo = new()
                {
                    m_Name = $"ExtraAssetsImporter {catName} {decalName} Decal",
                    m_Type = "StaticObjectPrefab"
                };
                jSONDecalsMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
            }
            if (EAI.m_Setting.CompatibilityDropDown == EAICompatibility.ELT3)
            {
                PrefabIdentifierInfo prefabIdentifierInfo = new()
                {
                    m_Name = $"ExtraLandscapingTools_mods_{catName}_{decalName}",
                    m_Type = "StaticObjectPrefab"
                };
                jSONDecalsMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
            }
        }

        public static IEnumerator<JSONDecalsMaterail> AsyncLoadJSON(ImportData data)
        {
            JSONDecalsMaterail decalsMaterail = new();
            string jsonDecalPath = Path.Combine(data.FolderPath, "decal.json");
            if (File.Exists(jsonDecalPath))
            {
                Task<JSONDecalsMaterail> task = ImportersUtils.AsyncLoadJson<JSONDecalsMaterail>(jsonDecalPath);

                while (!task.IsCompleted) yield return null;

                decalsMaterail = task.Result;

                if (decalsMaterail.Float.ContainsKey("UiPriority")) decalsMaterail.UiPriority = (int)decalsMaterail.Float["UiPriority"];
            }

            yield return decalsMaterail;

        }

        public static JSONDecalsMaterail LoadJSON(ImportData data)
        {

            JSONDecalsMaterail decalsMaterail = new();
            string jsonDecalPath = Path.Combine(data.FolderPath, "decal.json");
            if (File.Exists(jsonDecalPath))
            {
                decalsMaterail = ImportersUtils.LoadJson<JSONDecalsMaterail>(jsonDecalPath);

                if (decalsMaterail.Float.ContainsKey("UiPriority")) decalsMaterail.UiPriority = (int)decalsMaterail.Float["UiPriority"];
            }
            return decalsMaterail;
        }

    }
}
