using Colossal.Json;
using Game.Prefabs;
using System.Collections.Generic;

namespace ExtraAssetsImporter.AssetImporter.JSONs.Prefabs
{
    public class PrefabJson
    {
        public Dictionary<string, ComponentJson> Components = new();


        public void Process(PrefabBase prefabBase)
        {
        }

        //virtual protected void Process(ImportData data, Variant prefabJson, PrefabBase prefabBase)
        //{
        //    AssetsImporterManager.ProcessComponentImporters(data, prefabJson, prefabBase);
        //}
    }
}
