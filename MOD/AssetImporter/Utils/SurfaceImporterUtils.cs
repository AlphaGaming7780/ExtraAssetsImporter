using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Colossal.AssetPipeline;
using ExtraAssetsImporter.AssetImporter.JSONs;
using UnityEngine;

namespace ExtraAssetsImporter.AssetImporter.Utils
{
    public static class SurfaceImporterUtils
    {

        public static Task<Surface> AsyncCreateSurface(ImportData data, bool importTextures = true)
        {
            return Task.Run(() => CreateSurface(data, importTextures));
        }

        public static Task<Surface> AsyncCreateMaterial(ImportData data, MaterialJson materialJson, bool importTextures = true)
        {
            return Task.Run(() => CreateSurface(data, materialJson, importTextures));
        }

        public static Surface CreateSurface(ImportData data, bool importTextures = true)
        {
            string path = Path.Combine(data.FolderPath, "Material.json");
            MaterialJson materialJson = ImportersUtils.LoadJson<MaterialJson>(path);
            if (materialJson == null) throw new Exception("Material JSON is null, that maybe mean there is a sytaxe error in the file.");
            return CreateSurface(data, materialJson, importTextures);
        }

        public static Surface CreateSurface(ImportData data, MaterialJson materialJson, bool importTextures = true)
        {
            Surface surface = new(data.AssetName, materialJson.MaterialName);

            foreach (string key in materialJson.Float.Keys)     { surface.AddProperty(key, materialJson.Float[key]  );}
            foreach (string key in materialJson.Vector.Keys)    { surface.AddProperty(key, materialJson.Vector[key] );}

            if (importTextures)
            {
                var baseColorMap = TexturesImporterUtils.ImportTexture_BaseColorMap(data);
                if (baseColorMap != null) surface.AddProperty("_BaseColorMap", baseColorMap);

                var normalMap = TexturesImporterUtils.ImportTexture_NormalMap(data);
                if (normalMap != null) surface.AddProperty("_NormalMap", normalMap);

                var maskMap = TexturesImporterUtils.ImportTexture_MaskMap(data);
                if (maskMap != null) surface.AddProperty("_MaskMap", maskMap);
            }

            return surface;
        }
    }
}
