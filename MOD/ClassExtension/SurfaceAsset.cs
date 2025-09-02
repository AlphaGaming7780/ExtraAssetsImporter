using Colossal.IO.AssetDatabase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ExtraAssetsImporter.ClassExtension
{
    public static class SurfaceAssetExtension
    {
        private static readonly FieldInfo FloatsField = typeof(SurfaceAsset)
            .GetField("m_Floats", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo IntsField = typeof(SurfaceAsset)
            .GetField("m_Ints", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo VectorsField = typeof(SurfaceAsset)
            .GetField("m_Vectors", BindingFlags.NonPublic | BindingFlags.Instance);

        public static void AddProperty_Reflection(this SurfaceAsset surfaceAsset, string name, float value)
        {
            if (FloatsField == null)
            {
                throw new MissingFieldException("SurfaceAsset", "m_Floats");
            }

            if (FloatsField.GetValue(surfaceAsset) is not Dictionary<string, float> floats)
            {
                throw new InvalidOperationException("m_Floats is not a Dictionary<string, float>");
            }

            floats[name] = value;
        }

        public static void AddProperty_Reflection(this SurfaceAsset surfaceAsset, string name, int value)
        {
            if (IntsField == null)
            {
                throw new MissingFieldException("SurfaceAsset", "IntsField");
            }

            if (IntsField.GetValue(surfaceAsset) is not Dictionary<string, int> ints)
            {
                throw new InvalidOperationException("m_Ints is not a Dictionary<string, int>");
            }

            ints[name] = value;
        }

        public static void AddProperty_Reflection(this SurfaceAsset surfaceAsset, string name, Vector4 value)
        {
            if (VectorsField == null)
            {
                throw new MissingFieldException("SurfaceAsset", "m_Vectors");
            }

            if (VectorsField.GetValue(surfaceAsset) is not Dictionary<string, Vector4> vectors)
            {
                throw new InvalidOperationException("m_Vectors is not a Dictionary<string, Vector4>");
            }

            vectors[name] = value;
        }

        public static void AddProperty(this SurfaceAsset surfaceAsset, string name, float value)
        {
            if (surfaceAsset == null) throw new ArgumentNullException(nameof(surfaceAsset));
            if (name == null) throw new ArgumentNullException(nameof(name));

            Dictionary<string, float> floats = new(surfaceAsset.floats);
            floats[name] = value;
            surfaceAsset.UpdateFloats(floats);
        }

        public static void AddProperty(this SurfaceAsset surfaceAsset, string name, int value)
        {
            if (surfaceAsset == null) throw new ArgumentNullException(nameof(surfaceAsset));
            if (name == null) throw new ArgumentNullException(nameof(name));

            Dictionary<string, int> ints = new(surfaceAsset.ints);
            ints[name] = value;
            surfaceAsset.UpdateInts(ints);
        }

        public static void AddProperty(this SurfaceAsset surfaceAsset, string name, Vector4 value)
        {
            if (surfaceAsset == null) throw new ArgumentNullException(nameof(surfaceAsset));
            if (name == null) throw new ArgumentNullException(nameof(name));

            Dictionary<string, Vector4> vectors = new(surfaceAsset.vectors);
            vectors[name] = value;
            surfaceAsset.UpdateVectors(vectors);
        }
    }
}
