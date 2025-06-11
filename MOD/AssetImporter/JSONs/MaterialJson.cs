using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter.JSONs
{
    public class MaterialJson
    {
        public string MaterialName = null;
        public string ShaderName = null;
        public Dictionary<string, float> Float = new();
        public Dictionary<string, Vector4> Vector = new();
    }
}
