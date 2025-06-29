using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.AssetImporter.JSONs.Components;
using Game.Prefabs;
using System;

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

        public override void Process(ImportData data, Variant componentJson, PrefabBase prefab)
        {
            RenderedAreaJson json = componentJson.Make<RenderedAreaJson>();
            if (json is null)
            {
                EAI.Logger.Error($"RenderedArea component JSON is null for prefab {prefab.name}.");
                return;
            }
            RenderedArea renderedArea = prefab.AddOrGetComponent<RenderedArea>();
            renderedArea.m_Roundness = json.Roundness;
            renderedArea.m_LodBias = json.LodBias;
        }
    }
}
