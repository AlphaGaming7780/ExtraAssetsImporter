using Game.Prefabs;

namespace ExtraAssetsImporter.AssetImporter.JSONs
{
    public class PrefabIDJson
    {
        public PrefabIDJson(PrefabBase prefab)
        {
            Type = prefab.GetType().Name;
            Name = prefab.name;
        }

        public PrefabIDJson(string type, string name) 
        {
            Type = type;
            Name = name;
        }

        public PrefabIDJson()
        {
        }

        public string Type;

        public string Name;

        public static implicit operator PrefabID(PrefabIDJson prefabIDJson)
        {
            return new PrefabID(prefabIDJson.Type, prefabIDJson.Name);
        }
    }
}
