using Colossal;
using Colossal.Json;
using Game.Prefabs;

namespace ExtraAssetsImporter.AssetImporter.JSONs
{
    public class PrefabIDJson
    {
        public PrefabIDJson(PrefabBase prefab, Hash128 overrideHash = default(Hash128))
        {
            m_Type = prefab.GetType().Name;
            m_Name = prefab.name;
            Hash128 hash = default(Hash128);
            if (overrideHash.isValid)
            {
                hash = overrideHash;
            }
            else if (prefab.asset != null)
            {
                int data = prefab.asset.GetMeta().platformID;
                if (data > 0)
                {
                    hash.Calculate(in data);
                }
                else
                {
                    hash = prefab.asset.id.guid;
                }
            }
            m_Hash = hash.ToJSONString();
        }

        public PrefabIDJson(string type, string name) 
        {
            m_Type = type;
            m_Name = name;
        }

        public PrefabIDJson()
        {
        }

        public string m_Type;
        public string m_Name;
        public string m_Hash;

        public static implicit operator PrefabID(PrefabIDJson prefabIDJson)
        {
            return new PrefabID(prefabIDJson.m_Type, prefabIDJson.m_Name, Hash128.Parse(prefabIDJson.m_Hash));
        }
    }
}
