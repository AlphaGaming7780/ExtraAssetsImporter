using Game.Objects;
using Game.Prefabs;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ExtraAssetsImporter.ClassExtension
{
    public static class ObjectGeometryPrefabExtension
    {
        public static void AddObjectMeshInfo(this ObjectGeometryPrefab prefab, RenderPrefabBase renderPrefab, float3 position = new(), ObjectState objectState = ObjectState.None)
        {
            prefab.m_Meshes ??= new ObjectMeshInfo[0];

            ObjectMeshInfo objectMeshInfo = new()
            {
                m_Mesh = renderPrefab,
                m_Position = float3.zero,
                m_RequireState = ObjectState.None
            };

            List<ObjectMeshInfo> objectMeshInfos = prefab.m_Meshes.ToList();
            objectMeshInfos.Add(objectMeshInfo);
            prefab.m_Meshes = objectMeshInfos.ToArray();

        }
    }
}
