using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter.JSONs
{
    public class MaterialJson
    {
        public string MaterialName = null;
        public string ShaderName = null;
        public Dictionary<string, float> Float = new();
        public Dictionary<string, Vector4> Vector = new();

        public float TryGetValue(string key, float defaultValue)
        {
            if (Float != null && Float.TryGetValue(key, out float value))
            {
                return value;
            }
            return defaultValue;
        }

        public Vector4 TryGetValue(string key, Vector4 defaultValue)
        {
            if (Vector != null && Vector.TryGetValue(key, out Vector4 value))
            {
                return value;
            }
            return defaultValue;
        }

        public float2 TryGetValue(string key, float2 defaultValue)
        {
            if (Vector != null && Vector.TryGetValue(key, out Vector4 value))
            {
                return new float2(value.x, value.y);
            }
            return defaultValue;
        }

    }
}
