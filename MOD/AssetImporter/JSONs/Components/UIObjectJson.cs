using Game.Prefabs;

namespace ExtraAssetsImporter.AssetImporter.JSONs.Components
{
    public class UIObjectJson : ComponentJson
    {
        public PrefabIDJson m_Group = null;

        public int m_Priority = -1;

        public string m_Icon = null;

        public bool m_IsDebugObject = false;
    }
}
