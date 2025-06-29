using Game.Prefabs;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter.JSONs.Prefabs
{
    public class AreaPrefabJson : PrefabJson
    {
        public Color m_Color = Color.white;
        public Color m_EdgeColor = Color.white;
        public Color m_SelectionColor = Color.white;
        public Color m_SelectionEdgeColor = Color.white;

        public void Process(AreaPrefab areaPrefab)
        {
            base.Process(areaPrefab);
            areaPrefab.m_Color = m_Color;
            areaPrefab.m_EdgeColor = m_EdgeColor;
            areaPrefab.m_SelectionColor = m_SelectionColor;
            areaPrefab.m_SelectionEdgeColor = m_SelectionEdgeColor;
        }
    }
}
