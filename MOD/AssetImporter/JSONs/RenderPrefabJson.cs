using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Game.Prefabs;

namespace ExtraAssetsImporter.AssetImporter.JSONs
{
    public class RenderPrefabJson
    {
        public Dictionary<string, IRenderPrefabComponentJson> Components;
    }

    public interface IRenderPrefabComponentJson
    {
        public void Process(ImportData data, RenderPrefab renderPrefab, RenderPrefabJson renderPrefabJson);
    }
}
