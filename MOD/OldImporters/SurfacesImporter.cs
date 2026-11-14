using Colossal.AssetPipeline.Importers;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.Localization;
using Colossal.PSI.Common;
using ExtraAssetsImporter.AssetImporter;
using ExtraAssetsImporter.Database;
using ExtraLib;
using ExtraLib.Helpers;
using ExtraLib.Prefabs;
using Game.Prefabs;
using Game.Rendering;
using Game.SceneFlow;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Entities;
using UnityEngine;

namespace ExtraAssetsImporter.OldImporters 
{
    public class JSONSurfacesMaterail
    {
        public int UiPriority = 0;
        public float m_Roundness = 0.5f;
        public Dictionary<string, float> Float = new();
        public Dictionary<string, Vector4> Vector = new();
        public List<PrefabIdentifierInfo> prefabIdentifierInfos = new();
    }

    internal class SurfacesImporter
    {
        internal static List<string> FolderToLoadSurface = new();

        private static bool SurfacesIsLoading = false;
        internal static bool SurfacesIsLoaded = false;

        // Large maps fill memory before the importer reaches the count limit: a decoded 4K map
        // takes 64 MB.
        private const long MaxPendingBytes = 256L * 1024 * 1024;

        public static void AddCustomSurfacesFolder(string path)
        {
            if (FolderToLoadSurface.Contains(path)) return;
            FolderToLoadSurface.Add(path);
            Icons.LoadIcons(new DirectoryInfo(path).Parent.FullName);
        }

        public static void RemoveCustomSurfacesFolder(string folder)
        {
            if (!FolderToLoadSurface.Contains(folder)) return;
            FolderToLoadSurface.Remove(folder);
            Icons.UnLoadIcons(new DirectoryInfo(folder).Parent.FullName);
        }

        internal static IEnumerator CreateCustomSurfaces()
        {
            if (SurfacesIsLoading) yield break;

            if (FolderToLoadSurface.Count <= 0)
            {
                SurfacesIsLoaded = true;
                yield break;
            }

            SurfacesIsLoading = true;
            SurfacesIsLoaded = false;

            // A pack can register its folder while this runs (deferred mod loading). That folder
            // waits for the next pass, which starts when this one ends.
            List<string> folders = new(FolderToLoadSurface);

            int numberOfSurfaces = 0;
            int ammoutOfSurfacesloaded = 0;
            int failedSurfaces = 0;
            int skippedSurface = 0;

            var notificationInfo = EL.m_NotificationUISystem.AddOrUpdateNotification(
                $"{nameof(ExtraAssetsImporter)}.{nameof(EAI)}.{nameof(CreateCustomSurfaces)}",
                title: "EAI, Importing the custom surfaces.",
                progressState: ProgressState.Indeterminate,
                thumbnail: $"{Icons.COUIBaseLocation}/Icons/NotificationInfo/Surfaces.svg",
                progress: 0
            );

            foreach (string folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string catFolder in Directory.GetDirectories(folder))
                    foreach (string surfaceFolder in Directory.GetDirectories(catFolder))
                        if (Directory.GetFiles(surfaceFolder).Length > 0)
                            numberOfSurfaces++;
                        else
                            ImportersUtils.TryDeleteDirectory(surfaceFolder, recursive: false);
            }


            UIAssetParentCategoryPrefab assetCat = PrefabsHelper.GetOrCreateUIAssetParentCategoryPrefab("Surfaces");

            Dictionary<string, string> csLocalisation = new();
            List<(Task<DecodedSurface> DecodeTask, Action<Task<DecodedSurface>> Finish)> pending = new();
            long pendingBytes = 0;

            foreach (string folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string surfacesCat in Directory.GetDirectories(folder))
                {

                    string catName = new DirectoryInfo(surfacesCat).Name;
                    if (catName.StartsWith("."))
                    {
                        int num = Directory.GetDirectories(surfacesCat).Length;
                        skippedSurface += num;
                        ammoutOfSurfacesloaded += num;
                        continue;
                    }

                    foreach (string surfaceFolder in Directory.GetDirectories(surfacesCat))
                    {

                        string surfaceName = new DirectoryInfo(surfaceFolder).Name;
                        notificationInfo.progressState = ProgressState.Progressing;
                        notificationInfo.progress = (int)(ammoutOfSurfacesloaded / (float)numberOfSurfaces * 100);
                        notificationInfo.text = $"Loading : {surfaceName}";

                        if (surfaceName.StartsWith("."))
                        {
                            skippedSurface++;
                            ammoutOfSurfacesloaded++;
                            continue;
                        }

                        long decodedBytes = DecodedSize(surfaceFolder);

                        // The rest of the import, once a worker has decoded the surface's textures.
                        Action<Task<DecodedSurface>> finish = decodeTask =>
                        {
                            try
                            {
                                FileInfo[] fileInfos = new DirectoryInfo(folder).Parent.GetFiles("*.dll");
                                string modName = fileInfos.Length > 0 ? Path.GetFileNameWithoutExtension(fileInfos[0].Name).Split('_')[0] : new DirectoryInfo(folder).Parent.Name.Split('_')[0];
                                string fullSurfaceName = $"{modName} {catName} {surfaceName} Surface";
                                CreateCustomSurface(surfaceFolder, surfaceName, catName, modName, fullSurfaceName, assetCat, decodeTask.Result);
                                if (!csLocalisation.ContainsKey($"Assets.NAME[{fullSurfaceName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.NAME[{fullSurfaceName}]")) csLocalisation.Add($"Assets.NAME[{fullSurfaceName}]", surfaceName);
                                if (!csLocalisation.ContainsKey($"Assets.DESCRIPTION[{fullSurfaceName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.DESCRIPTION[{fullSurfaceName}]")) csLocalisation.Add($"Assets.DESCRIPTION[{fullSurfaceName}]", surfaceName);
                            }
                            catch (Exception e)
                            {
                                failedSurfaces++;
                                EAI.Logger.Error($"Failed to load the custom surface at {surfaceFolder} | ERROR : {e}");
                            }
                            finally
                            {
                                if (decodeTask.Status == TaskStatus.RanToCompletion) decodeTask.Result.Dispose();
                                pendingBytes -= decodedBytes;
                            }

                            ammoutOfSurfacesloaded++;
                        };

                        // Decoding the PNGs is the slow part: several run at once on worker
                        // threads, and each surface finishes on the main thread once a worker has
                        // decoded its textures.
                        pending.Add((DecodeSurfaceAsync(surfaceFolder), finish));
                        pendingBytes += decodedBytes;
                        while (pending.Count >= DecalsImporter.MaxPendingAssets || pendingBytes > MaxPendingBytes)
                        {
                            yield return null;
                            DecalsImporter.FinishCompleted(pending);
                        }
                    }
                }
            }

            while (pending.Count > 0)
            {
                yield return null;
                DecalsImporter.FinishCompleted(pending);
            }

            foreach (string localeID in GameManager.instance.localizationManager.GetSupportedLocales())
            {
                GameManager.instance.localizationManager.AddSource(localeID, new MemorySource(csLocalisation));
            }

            EL.m_NotificationUISystem.RemoveNotification(
                identifier: notificationInfo.id,
                delay: 5f,
                text: $"Complete, {numberOfSurfaces - failedSurfaces} Loaded, {failedSurfaces} failed, {skippedSurface} skipped.",
                progressState: ProgressState.Complete,
                progress: 100
            );

            //LoadLocalization();
            FolderToLoadSurface.RemoveAll(folders.Contains);

            SurfacesIsLoaded = true;
            SurfacesIsLoading = false;
        }

        // The memory a surface's PNGs take once decoded, read from their headers: the decoder gives
        // 4 bytes per pixel.
        private static long DecodedSize(string folderPath)
        {
            long size = 0;

            foreach (string map in new[] { "_BaseColorMap.png", "_NormalMap.png", "_MaskMap.png" })
            {
                string path = Path.Combine(folderPath, map);
                if (!File.Exists(path)) continue;

                byte[] header = new byte[24];

                // An unreadable map adds nothing here, and its decode then fails the surface.
                try
                {
                    using (FileStream stream = File.OpenRead(path))
                    {
                        if (stream.Read(header, 0, header.Length) != header.Length) continue;
                    }
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                // Bytes 16 to 23 are the width and the height of the PNG's IHDR chunk, big-endian.
                long width = ((long)header[16] << 24) | ((long)header[17] << 16) | ((long)header[18] << 8) | header[19];
                long height = ((long)header[20] << 24) | ((long)header[21] << 16) | ((long)header[22] << 8) | header[23];
                size += width * height * 4;
            }

            return size;
        }

        // Decoding a surface's PNGs is the slow part of a surface, and it touches no Unity object,
        // so it runs on a worker thread while the importer's coroutine yields.
        internal static Task<DecodedSurface> DecodeSurfaceAsync(string folderPath)
        {
            return Task.Run(() => new DecodedSurface
            {
                BaseColorMap = DecodePng(Path.Combine(folderPath, "_BaseColorMap.png")),
                NormalMap = DecodePng(Path.Combine(folderPath, "_NormalMap.png")),
                MaskMap = DecodePng(Path.Combine(folderPath, "_MaskMap.png"))
            });
        }

        // Returns null for a missing file, a PNG that isn't 8 bits per channel or has fewer than
        // three channels, or a failed decode: CreateTexture then loads that file with LoadImage on
        // the main thread.
        // LoadImage copies a gray PNG into each color channel, which no one- or two-channel texture
        // format does.
        internal static TextureImporter.Texture DecodePng(string path)
        {
            TextureImporter.Texture texture = null;

            try
            {
                if (!File.Exists(path)) return null;

                byte[] header = new byte[26];
                using (FileStream stream = File.OpenRead(path))
                {
                    if (stream.Read(header, 0, header.Length) != header.Length) return null;
                }

                // Bytes 24 and 25 are the bit depth and the color type of the PNG's IHDR chunk.
                if (header[1] != 'P' || header[2] != 'N' || header[3] != 'G' || header[24] != 8) return null;

                DefaultTextureImporter.ImportSettings importSettings = DefaultTextureImporter.ImportSettings.GetDefault();
                importSettings.computeMips = false;
                importSettings.compressBC = false;

                texture = ((DefaultTextureImporter)ImporterCache.GetImporter(".png")).Import(importSettings, path);
                if (texture.info.channels >= 3) return texture;

                texture.Dispose();
                return null;
            }
            catch (Exception e)
            {
                texture?.Dispose();
                EAI.Logger.Warn($"Failed to decode {path} off the main thread, loading it on the main thread.\n{e}");
                return null;
            }
        }

        // Builds a texture with the pixels LoadImage gives for a PNG, in sRGB, with the mips Unity
        // generates. The decoder's RGBA or RGB rows go in as they are.
        // Only the GPU reads a surface's textures, so they keep no copy in RAM.
        private static Texture2D CreateTexture(TextureImporter.Texture image, string path)
        {
            if (image == null)
            {
                Texture2D loaded = new(1, 1);
                if (loaded.LoadImage(File.ReadAllBytes(path), markNonReadable: true)) return loaded;

                UnityEngine.Object.Destroy(loaded);
                return null;
            }

            TextureFormat format = image.info.channels == 4 ? TextureFormat.RGBA32 : TextureFormat.RGB24;
            Texture2D texture = new(image.width, image.height, format, true);
            texture.SetPixelData(image.GetRawMipData(0, 0), 0);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return texture;
        }

        private static void CreateCustomSurface(string folderPath, string surfaceName, string catName, string modName, string fullSurfaceName, UIAssetParentCategoryPrefab assetCat, DecodedSurface decoded)
        {

            string baseColorMapPath = Path.Combine(folderPath, "_BaseColorMap.png");

            if (!File.Exists(baseColorMapPath)) { EAI.Logger.Error($"No _BaseColorMap.png file for the {new DirectoryInfo(folderPath).Name} surface in {catName} category in {modName}."); return; }

            SurfacePrefab surfacePrefab = ScriptableObject.CreateInstance<SurfacePrefab>();
            surfacePrefab.name = fullSurfaceName;
            surfacePrefab.m_Color = new(255f, 255f, 255f, 0.05f);
            surfacePrefab.version = 0;

            SurfacePrefab surfacePrefabPlaceHolder = ScriptableObject.CreateInstance<SurfacePrefab>();
            surfacePrefabPlaceHolder.name = surfacePrefab.name + "_Placeholder";



            Material newMaterial = GetDefaultSurfaceMaterial();
            newMaterial.name = fullSurfaceName + " Material";

            newMaterial.SetFloat(ShaderPropertiesIDs.DrawOrder, GetRendererPriorityByCat(catName));



            string jsonSurfacePath = Path.Combine(folderPath, "surface.json");

            JSONSurfacesMaterail jSONMaterail = new();

            if (File.Exists(jsonSurfacePath))
            {
                jSONMaterail = Decoder.Decode(File.ReadAllText(jsonSurfacePath)).Make<JSONSurfacesMaterail>();
                foreach (string key in jSONMaterail.Float.Keys)
                {
                    if (newMaterial.HasFloat(key)) newMaterial.SetFloat(key, jSONMaterail.Float[key]);
                    else
                    {
                        if (key == "UiPriority") jSONMaterail.UiPriority = (int)jSONMaterail.Float[key];
                    }
                }
                foreach (string key in jSONMaterail.Vector.Keys) { if (newMaterial.HasVector(key)) newMaterial.SetVector(key, jSONMaterail.Vector[key]); }

                VersionCompatiblity(jSONMaterail, catName, surfaceName, fullSurfaceName);
                if (jSONMaterail.prefabIdentifierInfos.Count > 0)
                {
                    ObsoleteIdentifiers obsoleteIdentifiers = surfacePrefab.AddComponent<ObsoleteIdentifiers>();
                    obsoleteIdentifiers.m_PrefabIdentifiers = jSONMaterail.prefabIdentifierInfos.ToArray();
                }
            }

            byte[] fileData;

            Texture2D texture2D_BaseColorMap = CreateTexture(decoded.BaseColorMap, baseColorMapPath);
            if (texture2D_BaseColorMap == null)
            {
                EAI.Logger.Error($"Failed to Load the BaseColorMap image for the {surfacePrefab.name} surface.");
                UnityEngine.Object.Destroy(newMaterial);
                return;
            }

            newMaterial.SetTexture(ShaderPropertiesIDs.BaseColorMap, texture2D_BaseColorMap);


            string normalMapPath = Path.Combine(folderPath, "_NormalMap.png");
            if (File.Exists(normalMapPath))
            {
                Texture2D texture2D_NormalMap = CreateTexture(decoded.NormalMap, normalMapPath);
                if (texture2D_NormalMap != null)
                {
                    newMaterial.SetTexture(ShaderPropertiesIDs.NormalMap, texture2D_NormalMap);
                }
                else
                {
                    EAI.Logger.Warn($"Failed to load the NormalMap texture data for the {fullSurfaceName}");
                }
            }

            string maskMapPath = Path.Combine(folderPath, "_MaskMap.png");
            if (File.Exists(maskMapPath))
            {
                Texture2D texture2D_MaskMap = CreateTexture(decoded.MaskMap, maskMapPath);
                if (texture2D_MaskMap != null)
                {
                    newMaterial.SetTexture(ShaderPropertiesIDs.MaskMap, texture2D_MaskMap);
                }
                else
                {
                    EAI.Logger.Warn($"Failed to load the MaskMap texture data for the {fullSurfaceName}");
                }
            }

            RenderedArea renderedArea = surfacePrefabPlaceHolder.AddComponent<RenderedArea>();
            renderedArea.m_RendererPriority = (int)newMaterial.GetFloat(ShaderPropertiesIDs.DrawOrder);
            renderedArea.m_LodBias = 0;
            renderedArea.m_Roundness = jSONMaterail.m_Roundness;
            renderedArea.m_Material = newMaterial;
            renderedArea.m_DecalLayerMask = (DecalLayers)newMaterial.GetFloat(ShaderPropertiesIDs.colossal_DecalLayerMask);

            PlaceholderArea placeholderArea = surfacePrefabPlaceHolder.AddComponent<PlaceholderArea>();

            SpawnableArea spawnableArea = surfacePrefab.AddComponent<SpawnableArea>();
            spawnableArea.m_Placeholders = new AreaPrefab[1];
            spawnableArea.m_Placeholders[0] = surfacePrefabPlaceHolder;

            RenderedArea renderedArea1 = surfacePrefab.AddComponent<RenderedArea>();
            renderedArea1.m_RendererPriority = (int)newMaterial.GetFloat(ShaderPropertiesIDs.DrawOrder);
            renderedArea1.m_LodBias = 0;
            renderedArea1.m_Roundness = jSONMaterail.m_Roundness;
            renderedArea1.m_Material = newMaterial;
            renderedArea1.m_DecalLayerMask = (DecalLayers)newMaterial.GetFloat(ShaderPropertiesIDs.colossal_DecalLayerMask);

            string iconPath = Path.Combine(folderPath, "icon.png");
            Texture2D texture2D_Icon = new(1, 1);
            if (File.Exists(iconPath))
            {
                fileData = File.ReadAllBytes(iconPath);

                if (texture2D_Icon.LoadImage(fileData))
                {
                    if (texture2D_Icon.width > 128 || texture2D_Icon.height > 128)
                    {
                        TextureHelper.ResizeTexture(ref texture2D_Icon, 128, iconPath);
                    }
                }
            }
            else
            {
                fileData = File.ReadAllBytes(baseColorMapPath);
                texture2D_Icon.LoadImage(fileData);
                TextureHelper.ResizeTexture(ref texture2D_Icon, 128, iconPath);
            }
            UnityEngine.Object.Destroy(texture2D_Icon);

            UIObject surfacePrefabUI = surfacePrefab.AddComponent<UIObject>();
            surfacePrefabUI.m_IsDebugObject = false;
            surfacePrefabUI.m_Icon = File.Exists(iconPath) ? $"{Icons.COUIBaseLocation}/CustomSurfaces/{catName}/{new DirectoryInfo(folderPath).Name}/icon.png" : Icons.GetIcon(surfacePrefab);
            surfacePrefabUI.m_Priority = jSONMaterail.UiPriority;
            surfacePrefabUI.m_Group = PrefabsHelper.GetOrCreateUIAssetChildCategoryPrefab(assetCat, $"{catName} {assetCat.name}");

            AssetDataPath prefabAssetPath = AssetDataPath.Create("TempAssetsFolder", fullSurfaceName + PrefabAsset.kExtension, EscapeStrategy.None);
            EAIDatabaseManager.EAIAssetDatabase.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, surfacePrefab, forceGuid: Colossal.Hash128.CreateGuid(fullSurfaceName));

            EL.m_PrefabSystem.AddPrefab(surfacePrefab);
        }

        internal static int GetRendererPriorityByCat(string cat)

        {
            return cat switch
            {
                "Ground" => -100,
                "Grass" => -99,
                "Sand" => -98,
                "Concrete" => -97,
                "Wood" => -97,
                "Pavement" => -96,
                "Tiles" => -95,
                _ => -100
            };
        }

        private static Material GetDefaultSurfaceMaterial()
        {
            Material material = new(Shader.Find("Shader Graphs/AreaDecalShader"));
            material.SetFloat(ShaderPropertiesIDs.DecalColorMask0, 15);
            material.SetFloat(ShaderPropertiesIDs.DecalColorMask1, 15);
            material.SetFloat(ShaderPropertiesIDs.DecalColorMask2, 11);
            material.SetFloat(ShaderPropertiesIDs.DecalColorMask3, 8);
            material.SetFloat(ShaderPropertiesIDs.DecalStencilRef, 16);
            material.SetFloat(ShaderPropertiesIDs.DecalStencilWriteMask, 16);
            material.SetFloat(ShaderPropertiesIDs.colossal_DecalLayerMask, 1);
            material.enableInstancing = true;
            material.shaderKeywords = new[] { "_MATERIAL_AFFECTS_ALBEDO", "_MATERIAL_AFFECTS_MASKMAP", "_MATERIAL_AFFECTS_NORMAL" };
            return material;
        }

        private static void VersionCompatiblity(JSONSurfacesMaterail jSONSurfacesMaterail, string catName, string surfaceName, string fullSurfaceName)
        {
            if (AssetsImporterManager.OldImportersCompatibility == EAIOldImportersCompatibility.LocalAsset)
            {
                PrefabIdentifierInfo prefabIdentifierInfo = new()
                {
                    m_Name = $"ExtraAssetsImporter {catName} {surfaceName} Surface",
                    m_Type = "StaticObjectPrefab"
                };
                jSONSurfacesMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
            }
            if (AssetsImporterManager.OldImportersCompatibility == EAIOldImportersCompatibility.ELT2)
            {
                PrefabIdentifierInfo prefabIdentifierInfo = new()
                {
                    m_Name = $"{surfaceName}",
                    m_Type = "SurfacePrefab"
                };
                jSONSurfacesMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
            }
            if (AssetsImporterManager.OldImportersCompatibility == EAIOldImportersCompatibility.ELT3)
            {
                PrefabIdentifierInfo prefabIdentifierInfo = new()
                {
                    m_Name = $"ExtraLandscapingTools_mods_{catName}_{surfaceName}",
                    m_Type = "SurfacePrefab"
                };
                jSONSurfacesMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
            }
            if (AssetsImporterManager.OldImportersCompatibility == EAIOldImportersCompatibility.PreEditor)
            {
                PrefabIdentifierInfo prefabIdentifierInfo = new()
                {
                    m_Name = fullSurfaceName,
                    m_Type = "SurfacePrefab"
                };
                jSONSurfacesMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
            }
        }

        // A surface's PNGs, decoded by the game into native memory that Dispose frees.
        internal class DecodedSurface : IDisposable
        {
            public TextureImporter.Texture BaseColorMap;
            public TextureImporter.Texture NormalMap;
            public TextureImporter.Texture MaskMap;

            public void Dispose()
            {
                BaseColorMap?.Dispose();
                NormalMap?.Dispose();
                MaskMap?.Dispose();
            }
        }
    }

}

