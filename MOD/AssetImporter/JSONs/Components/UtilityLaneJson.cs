using Game.Prefabs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExtraAssetsImporter.AssetImporter.JSONs.Components
{
    public class UtilityLaneJson
    {
        public Game.Net.UtilityTypes UtilityType = Game.Net.UtilityTypes.WaterPipe;
        public string LocalConnectionLane;
        public string LocalConnectionLane2;
        public string NodeObject;
        public float Width;
        public float VisualCapacity;
        public float Hanging;
        public bool Underground;
    }
}
