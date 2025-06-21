using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Colossal.AssetPipeline;
using Colossal.IO.AssetDatabase.VirtualTexturing;
using Colossal.IO.AssetDatabase;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.DataBase;

namespace ExtraAssetsImporter.AssetImporter.Utils
{
    public static class SurfaceImporterUtils
    {
        public const string MaterialJsonFileName = "Material.json";
        public const string BaseColorMap = "_BaseColorMap";
        public const string NormalMap = "_NormalMap";
        public const string MaskMap = "_MaskMap";

        public static Task<Surface> AsyncCreateSurface(ImportData data, string defaultMaterialName, bool importTextures = true)
        {
            return Task.Run(() => CreateSurface(data, defaultMaterialName, importTextures));
        }

        public static Task<Surface> AsyncCreateMaterial(ImportData data, MaterialJson materialJson, string defaultMaterialName, bool importTextures = true)
        {
            return Task.Run(() => CreateSurface(data, materialJson, defaultMaterialName, importTextures));
        }

        public static Surface CreateSurface(ImportData data, string defaultMaterialName, bool importTextures = true)
        {
            string path = Path.Combine(data.FolderPath, MaterialJsonFileName);
            MaterialJson materialJson = ImportersUtils.LoadJson<MaterialJson>(path);
            if (materialJson == null) throw new Exception("Material JSON is null, that maybe mean there is a syntax error in the file.");
            return CreateSurface(data, materialJson, defaultMaterialName, importTextures);
        }

        public static Surface CreateSurface(ImportData data, MaterialJson materialJson, string defaultMaterialName, bool importTextures = true)
        {

            string materialName = materialJson.MaterialName ?? defaultMaterialName;

            Surface surface = new(data.AssetName, materialJson.MaterialName);

            foreach (string key in materialJson.Float.Keys)     { surface.AddProperty(key, materialJson.Float[key]  );}
            foreach (string key in materialJson.Vector.Keys)    { surface.AddProperty(key, materialJson.Vector[key] );}

            if (importTextures)
            {
                TexturesImporterUtils.ImportTextures(data, surface);
            }

            return surface;
        }

        public static SurfaceAsset SetupSurfaceAsset(ImportData data, Surface surface, bool useVT = false)
        {
            AssetDataPath surfaceAssetDataPath = AssetDataPath.Create(data.AssetDataPath, $"{data.AssetName}_SurfaceAsset", EscapeStrategy.None);
            SurfaceAsset surfaceAsset = new()
            {
                id = new Identifier(Guid.NewGuid()),
                database = EAIDataBaseManager.assetDataBaseEAI
            };
            surfaceAsset.database.AddAsset<SurfaceAsset>(surfaceAssetDataPath, surfaceAsset.id.guid);
            surfaceAsset.SetData(surface);


            if (useVT)
            {
                //VT Stuff
                VirtualTexturingConfig virtualTexturingConfig = EAI.textureStreamingSystem.virtualTexturingConfig; //(VirtualTexturingConfig)ScriptableObject.CreateInstance("VirtualTexturingConfig");
                Dictionary<Colossal.IO.AssetDatabase.TextureAsset, List<SurfaceAsset>> textureReferencesMap = new();

                foreach (Colossal.IO.AssetDatabase.TextureAsset asset in surfaceAsset.textures.Values)
                {
                    asset.Save();
                    textureReferencesMap.Add(asset, new() { surfaceAsset });
                }

                surfaceAsset.Save(force: false, saveTextures: false, vt: true, virtualTexturingConfig: virtualTexturingConfig, textureReferencesMap: textureReferencesMap, tileSize: virtualTexturingConfig.tileSize, nbMidMipLevelsRequested: 0);

                //END OF VT Stuff.
            }
            else
            {
                surfaceAsset.Save(force: false, saveTextures: true, vt: false);
            }

            return surfaceAsset;
        }

    }
}
