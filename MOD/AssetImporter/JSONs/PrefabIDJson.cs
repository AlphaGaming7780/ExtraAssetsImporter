using Game.Prefabs;

namespace ExtraAssetsImporter.AssetImporter.JSONs
{
    public class PrefabIDJson
    {
        public string Type;

        public string Name;

        public static implicit operator PrefabID(PrefabIDJson prefabIDJson)
        {
            return new PrefabID(prefabIDJson.Type, prefabIDJson.Name);
        }

    }
}
