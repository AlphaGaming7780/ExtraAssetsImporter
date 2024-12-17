using UnityEngine;

namespace ExtraAssetsImporter
{
    internal static class ShaderPropertiesIDs
    {
        internal static readonly int Metallic = Shader.PropertyToID("_Metallic");
        internal static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        internal static readonly int colossal_DecalLayerMask = Shader.PropertyToID("colossal_DecalLayerMask");
        internal static readonly int DecalColorMask0 = Shader.PropertyToID("_DecalColorMask0");
        internal static readonly int DecalColorMask1 = Shader.PropertyToID("_DecalColorMask1");
        internal static readonly int DecalColorMask2 = Shader.PropertyToID("DecalColorMask2");
        internal static readonly int DecalColorMask3 = Shader.PropertyToID("DecalColorMask3");
        internal static readonly int DrawOrder = Shader.PropertyToID("_DrawOrder");


        internal static readonly int DecalStencilRef = Shader.PropertyToID("_DecalStencilRef");
        internal static readonly int DecalStencilWriteMask = Shader.PropertyToID("_DecalStencilWriteMask");


        internal static readonly int BaseColorMap = Shader.PropertyToID("_BaseColorMap");
        internal static readonly int MaskMap = Shader.PropertyToID("_MaskMap");
        internal static readonly int NormalMap = Shader.PropertyToID("_NormalMap");
    }
}
