using Game.Prefabs;
using System.Collections.Generic;
using System.Linq;

namespace ExtraAssetsImporter.ClassExtension
{
    public static class NetLaneGeometryPrefabExtension
    {
        public static void AddNetLaneMeshInfo(this NetLaneGeometryPrefab prefab, RenderPrefab renderPrefab)
        {
            prefab.m_Meshes ??= [];

            NetLaneMeshInfo netLaneMeshInfo = new()
            {
                m_Mesh = renderPrefab,
            };

            List<NetLaneMeshInfo> netLaneMeshInfos = prefab.m_Meshes.ToList();
            netLaneMeshInfos.Add(netLaneMeshInfo);
            prefab.m_Meshes = netLaneMeshInfos.ToArray();

        }
    }
}
