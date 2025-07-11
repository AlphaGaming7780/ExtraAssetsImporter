using Game.Prefabs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExtraAssetsImporter.AssetImporter.JSONs.Components
{
    internal class EnclosedAreaJson : ComponentJson
    {
        public PrefabIDJson m_BorderLaneType;
        public bool m_CounterClockWise;
    }
}
