using Colossal.Json;
using Game.Prefabs;
using System;

namespace ExtraAssetsImporter.AssetImporter.Components
{
    abstract public class ComponentImporter
    {
        abstract public Type ComponentType { get; }
        abstract public Type PrefabType { get; }
        abstract public void Process(ImportData data, Variant componentJson, PrefabBase prefab);


    }
}
