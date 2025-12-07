using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.Importers;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.AssetImporter.JSONs.Components;
using ExtraAssetsImporter.AssetImporter.Utils;
using Game.Prefabs;
using Game.Rendering;
using System;
using Unity.Mathematics;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter.Components
{
    internal class RenderedAreaComponent : ComponentImporter
    {
        public override Type ComponentType => typeof(RenderedArea);

        public override Type PrefabType => typeof(SurfacePrefab);

        public override ComponentJson GetDefaultJson()
        {
            return new RenderedAreaJson();
        }

        public override void Process(PrefabImportData data, Variant componentJson, PrefabBase prefab)
        {
            RenderedAreaJson json = componentJson.Make<RenderedAreaJson>();
            if (json is null)
            {
                EAI.Logger.Error($"RenderedArea component JSON is null for prefab {prefab.name}.");
                return;
            }
            RenderedArea renderedArea = prefab.AddOrGetComponent<RenderedArea>();
            renderedArea.m_Material = null;
            renderedArea.m_Roundness = componentJson.TryGetValue("Roundness", out Variant variant) ? variant : json.m_Roundness;
            renderedArea.m_LodBias = componentJson.TryGetValue("LodBias", out Variant variant2) ? variant2 : json.m_LodBias;
            renderedArea.m_BaseColor = json.m_BaseColor;
            renderedArea.m_DecalLayerMask = json.m_DecalLayerMask;
            renderedArea.m_EdgeFadeRange = json.m_EdgeFadeRange;
            renderedArea.m_EdgeNoise = json.m_EdgeNoise;
            renderedArea.m_EdgeNormal = json.m_EdgeNormal;
            renderedArea.m_EdgeNormalRange = json.m_EdgeNormalRange;
            renderedArea.m_Metallic = json.m_Metallic;
            renderedArea.m_MetallicAlphaSource = json.m_MetallicAlphaSource;
            renderedArea.m_MetallicOpacity = json.m_MetallicOpacity;
            renderedArea.m_NormalAlphaSource = json.m_NormalAlphaSource;
            renderedArea.m_NormalOpacity = json.m_NormalOpacity;
            renderedArea.m_RendererPriority = json.m_RendererPriority != 0 ? json.m_RendererPriority : GetRendererPriorityByCat(data.CatName); ;
            renderedArea.m_Smoothness = json.m_Smoothness;
            renderedArea.m_UVScale = json.m_UVScale;

            // Do it again, because the value might have been set back to default one.
            SurfacesImporterNew.ProcessMaterialJSON(prefab, data);

        }

        private int GetRendererPriorityByCat(string cat)

        {
            return cat switch
            {
                "Ground" => -100,
                "Grass" => -99,
                "Sand" => -98,
                "Concrete" => -97,
                "Wood" => -97,
                "Pavement" => -96,
                "Tiles" => -95,
                _ => -100
            };
        }

    }
}
