using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.JSONs.Components;
using Game.Prefabs;
using System;

namespace ExtraAssetsImporter.AssetImporter.Components
{
    internal class ObsoleteIdentifiersComponent : ComponentImporter
    {
        public override Type ComponentType => typeof(ObsoleteIdentifiers);

        public override Type PrefabType => typeof(PrefabBase);

        public override void Process(ImportData data, Variant componentJson, PrefabBase prefab)
        {
            ObsoleteIdentifiersJson obsoleteIdentifiersJson = componentJson.Make<ObsoleteIdentifiersJson>();
            if (obsoleteIdentifiersJson is null)
            {
                EAI.Logger.Error($"ObsoleteIdentifiers component JSON is null for prefab {prefab.name}.");
                return;
            }
            ObsoleteIdentifiers obsoleteIdentifiers = prefab.AddOrGetComponent<ObsoleteIdentifiers>();
            obsoleteIdentifiers.m_PrefabIdentifiers = obsoleteIdentifiersJson.PrefabIdentifiers;
        }
    }
}
