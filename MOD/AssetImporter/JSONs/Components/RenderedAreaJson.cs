using Colossal.IO.AssetDatabase;
using Game.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter.JSONs.Components
{
    internal class RenderedAreaJson : ComponentJson
    {
        //public float Roundness = 0.5f;
        //public float LodBias = 0;

        public float m_Roundness = 0.5f;

        public float m_LodBias;

        public int m_RendererPriority;

        public DecalLayers m_DecalLayerMask = DecalLayers.Terrain;

        public Color m_BaseColor = Color.white;

        public float m_Metallic = 1f;

        public float m_Smoothness = 1f;

        public float m_NormalOpacity = 1f;

        public float m_MetallicOpacity = 1f;

        public float m_NormalAlphaSource;

        public float m_MetallicAlphaSource;

        public float m_UVScale = 0.2f;

        public float m_EdgeNormal = 0.5f;

        public float2 m_EdgeFadeRange = new float2(0.75f, 0.25f);

        public float2 m_EdgeNormalRange = new float2(0.7f, 0.5f);

        public float2 m_EdgeNoise = new float2(0.5f, 0.5f);
    }
}
