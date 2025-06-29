using ExtraLib;
using Game.Prefabs;

namespace ExtraAssetsImporter.AssetImporter.JSONs.Prefabs
{
    public class NetLanePrefabJson : PrefabJson
    {
        public string m_PathfindPrefab = null;

        public void Process(NetLanePrefab netLanePrefab)
        {
            if (m_PathfindPrefab != null)
            {
                if (EL.m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(PathfindPrefab), m_PathfindPrefab), out PrefabBase prefabBase) && prefabBase is PathfindPrefab pathfindPrefab)
                {
                    netLanePrefab.m_PathfindPrefab = pathfindPrefab;
                }
                else
                {
                    EAI.Logger.Warn($"Failed to get the PathfindPrefab with the name of {m_PathfindPrefab}.");
                }
            }
            base.Process(netLanePrefab);
        }

    }
}
