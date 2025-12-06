using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.Localization;
using Colossal.PSI.Common;
using ExtraAssetsImporter.DataBase;
using ExtraLib;
using ExtraLib.Helpers;
using ExtraLib.Prefabs;
using Game.Prefabs;
using Game.SceneFlow;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ExtraAssetsImporter.OldImporters
{
    internal class NetLanesDecalImporter
    {
        internal static List<string> FolderToLoadNetLanes = new();
        private static bool NetLanesLoading = false;
        internal static bool NetLanesLoaded = false;

        internal static void LoadLocalization()
        {

            Dictionary<string, string> csLocalisation = new();

            foreach (string folder in FolderToLoadNetLanes)
            {
                foreach (string netLanesCat in Directory.GetDirectories(folder))
                {
                    foreach (string filePath in Directory.GetDirectories(netLanesCat))
                    {
                        FileInfo[] fileInfos = new DirectoryInfo(folder).Parent.GetFiles(".dll");
                        string modName = fileInfos.Length > 0 ? fileInfos[0].Name.Split('_')[0] : new DirectoryInfo(folder).Parent.Name.Split('_')[0];
                        string netLanesName = $"{modName} {new DirectoryInfo(netLanesCat).Name} {new DirectoryInfo(filePath).Name} Net Lanes";

                        if (!csLocalisation.ContainsKey($"Assets.NAME[{netLanesName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.NAME[{netLanesName}]")) csLocalisation.Add($"Assets.NAME[{netLanesName}]", new DirectoryInfo(filePath).Name);
                        if (!csLocalisation.ContainsKey($"Assets.DESCRIPTION[{netLanesName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.DESCRIPTION[{netLanesName}]")) csLocalisation.Add($"Assets.DESCRIPTION[{netLanesName}]", new DirectoryInfo(filePath).Name);
                    }
                }
            }

            foreach (string localeID in GameManager.instance.localizationManager.GetSupportedLocales())
            {
                GameManager.instance.localizationManager.AddSource(localeID, new MemorySource(csLocalisation));
            }
        }

        public static void AddCustomNetLanesFolder(string path)
        {
            if (FolderToLoadNetLanes.Contains(path)) return;
            FolderToLoadNetLanes.Add(path);
            Icons.LoadIcons(new DirectoryInfo(path).Parent.FullName);
        }

        public static void RemoveCustomNetLanesFolder(string path)
        {
            if (!FolderToLoadNetLanes.Contains(path)) return;
            FolderToLoadNetLanes.Remove(path);
            Icons.UnLoadIcons(new DirectoryInfo(path).Parent.FullName);
        }

        internal static IEnumerator CreateCustomNetLanes()
        {
            if (NetLanesLoading) yield break;

            if (FolderToLoadNetLanes.Count <= 0)
            {
                NetLanesLoaded = true;
                yield break;
            }

            NetLanesLoading = true;
            NetLanesLoaded = false;

            int numberOfNetLanes = 0;
            int ammoutOfNetLanesloaded = 0;
            int failedNetLanes = 0;
            int skipedNetLane = 0;

            var notificationInfo = EL.m_NotificationUISystem.AddOrUpdateNotification(
                $"{nameof(ExtraAssetsImporter)}.{nameof(EAI)}.{nameof(CreateCustomNetLanes)}",
                title: "EAI, Importing the custom net lanes.",
                progressState: ProgressState.Indeterminate,
                thumbnail: $"{Icons.COUIBaseLocation}/Icons/NotificationInfo/NetLanes.svg",
                progress: 0
            );

            foreach (string folder in FolderToLoadNetLanes)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string catFolder in Directory.GetDirectories(folder))
                    foreach (string netLanesFolder in Directory.GetDirectories(catFolder))
                        if (Directory.GetFiles(netLanesFolder).Length > 0)
                            numberOfNetLanes++;
                        else
                            Directory.Delete(netLanesFolder, false);
            }


            UIAssetParentCategoryPrefab assetCat = PrefabsHelper.GetOrCreateUIAssetParentCategoryPrefab("NetLanes");

            Dictionary<string, string> csLocalisation = new();

            foreach (string folder in FolderToLoadNetLanes)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string catFolder in Directory.GetDirectories(folder))
                {
                    string catName = new DirectoryInfo(catFolder).Name;
                    if (catName.StartsWith("."))
                    {
                        int num = Directory.GetDirectories(catFolder).Length;
                        skipedNetLane += num;
                        ammoutOfNetLanesloaded += num;
                        continue;
                    }

                    foreach (string netLanesFolder in Directory.GetDirectories(catFolder))
                    {

                        string netLanesName = new DirectoryInfo(netLanesFolder).Name;
                        notificationInfo.progressState = ProgressState.Progressing;
                        notificationInfo.progress = (int)(ammoutOfNetLanesloaded / (float)numberOfNetLanes * 100);
                        notificationInfo.text = $"Loading : {netLanesName}";

                        if (netLanesName.StartsWith("."))
                        {
                            skipedNetLane++;
                            ammoutOfNetLanesloaded++;
                            continue;
                        }

                        FileInfo[] fileInfos = new DirectoryInfo(folder).Parent.GetFiles("*.dll");
                        string modName = fileInfos.Length > 0 ? Path.GetFileNameWithoutExtension(fileInfos[0].Name).Split('_')[0] : new DirectoryInfo(folder).Parent.Name.Split('_')[0];
                        string fullNetLaneName = $"{modName} {catName} {netLanesName} NetLane";
                        string assetDataPath = Path.Combine("CustomNetLanes", modName, catName, netLanesName);

                        try
                        {
                            RenderPrefab renderPrefab = null;

                            if (!EAIDataBaseManager.TryGetEAIAsset(fullNetLaneName, out EAIAsset asset) || asset.SourceAssetHash != EAIDataBaseManager.GetAssetHash(netLanesFolder))
                            {
                                //renderPrefab = CreateRenderPrefab(netLanesFolder, netLanesName, catName, modName, fullNetLaneName, assetDataPath);
                                renderPrefab = DecalsImporter.CreateRenderPrefab(netLanesFolder, netLanesName, catName, modName, fullNetLaneName, assetDataPath, "CurvedDecal");
                                asset = new(fullNetLaneName, EAIDataBaseManager.GetAssetHash(netLanesFolder), assetDataPath);
                            }
                            else
                            {
                                try
                                {

                                    renderPrefab = DecalsImporter.GetRenderPrefab(fullNetLaneName, netLanesName);

                                }
                                catch (Exception e) { 
                                
                                    EAI.Logger.Error($"EAI failed to load the cached RenderPrefab for {fullNetLaneName} | ERROR : {e}");
                                    renderPrefab = null;

                                }

                                if (renderPrefab == null)
                                {
                                    //EAI.Logger.Warn($"EAI failed to load the cached data for {fullNetLaneName}");
                                    renderPrefab = DecalsImporter.CreateRenderPrefab(netLanesFolder, netLanesName, catName, modName, fullNetLaneName, assetDataPath, "CurvedDecal");
                                    asset = new(fullNetLaneName, EAIDataBaseManager.GetAssetHash(netLanesFolder), assetDataPath);
                                }
                            }

                            EAIDataBaseManager.AddOrValidateAsset(asset);

                            CreateCustomNetLane(netLanesFolder, netLanesName, catName, modName, fullNetLaneName, assetDataPath, assetCat, renderPrefab);

                            if (!csLocalisation.ContainsKey($"Assets.NAME[{fullNetLaneName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.NAME[{fullNetLaneName}]")) csLocalisation.Add($"Assets.NAME[{fullNetLaneName}]", netLanesName);
                            if (!csLocalisation.ContainsKey($"Assets.DESCRIPTION[{fullNetLaneName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.DESCRIPTION[{fullNetLaneName}]")) csLocalisation.Add($"Assets.DESCRIPTION[{fullNetLaneName}]", netLanesName);

                        }
                        catch (Exception e)
                        {
                            failedNetLanes++;
                            EAI.Logger.Error($"Failed to load the custom netLanes at {netLanesFolder} | ERROR : {e}");
                            string pathToAssetInDatabase = Path.Combine(EAIAssetDataBaseDescriptor.kRootPath, assetDataPath);
                            if (Directory.Exists(pathToAssetInDatabase)) Directory.Delete(pathToAssetInDatabase, true);
                        }
                        ammoutOfNetLanesloaded++;
                        yield return null;
                    }
                }
            }

            foreach (string localeID in GameManager.instance.localizationManager.GetSupportedLocales())
            {
                GameManager.instance.localizationManager.AddSource(localeID, new MemorySource(csLocalisation));
            }

            EL.m_NotificationUISystem.RemoveNotification(
                identifier: notificationInfo.id,
                delay: 5f,
                text: $"Complete, {numberOfNetLanes - failedNetLanes} Loaded, {failedNetLanes} failed, {skipedNetLane} skipped.",
                progressState: ProgressState.Complete,
                progress: 100
            );

            //LoadLocalization();
            NetLanesLoaded = true;
            NetLanesLoading = false;
        }

        private static void CreateCustomNetLane(string folderPath, string netLanesName, string catName, string modName, string fullNetLaneName, string assetDataPath, UIAssetParentCategoryPrefab assetCat, RenderPrefab renderPrefab)
        {
            if (renderPrefab == null) throw new NullReferenceException("RenderPrefab is NULL.");

            NetLaneGeometryPrefab netLanesPrefab = (NetLaneGeometryPrefab)ScriptableObject.CreateInstance("NetLaneGeometryPrefab");
            netLanesPrefab.name = fullNetLaneName;

            JsonNetLanes jsonNetLane = new();

            string jsonNetLanesPath = Path.Combine(folderPath, "netLane.json");
            if (File.Exists(jsonNetLanesPath))
            {
                jsonNetLane = Decoder.Decode(File.ReadAllText(jsonNetLanesPath)).Make<JsonNetLanes>();

                VersionCompatiblity(jsonNetLane, catName, netLanesName, fullNetLaneName);
                if (jsonNetLane.prefabIdentifierInfos.Count > 0)
                {
                    ObsoleteIdentifiers obsoleteIdentifiers = netLanesPrefab.AddComponent<ObsoleteIdentifiers>();
                    obsoleteIdentifiers.m_PrefabIdentifiers = jsonNetLane.prefabIdentifierInfos.ToArray();
                }
            }

            string iconPath = Path.Combine(folderPath, "icon.png");
            string baseColorMapPath = Path.Combine(folderPath, "_BaseColorMap.png");
            Texture2D texture2D_Icon = new(1, 1);
            if (File.Exists(iconPath))
            {
                byte[] fileData = File.ReadAllBytes(iconPath);

                if (texture2D_Icon.LoadImage(fileData))
                {
                    if (texture2D_Icon.width > 128 || texture2D_Icon.height > 128)
                    {
                        TextureHelper.ResizeTexture(ref texture2D_Icon, 128, iconPath);
                    }
                }
            }
            else if (File.Exists(baseColorMapPath))
            {
                byte[] fileData = File.ReadAllBytes(baseColorMapPath);
                if (texture2D_Icon.LoadImage(fileData))
                {
                    if (texture2D_Icon.width > 128 || texture2D_Icon.height > 128)
                    {
                        TextureHelper.ResizeTexture(ref texture2D_Icon, 128, iconPath);
                    }
                }
            }
            UnityEngine.Object.Destroy(texture2D_Icon);

            if (jsonNetLane.curveProperties != null)
            {
                CurveProperties curveProperties = renderPrefab.AddComponent<CurveProperties>();
                curveProperties.m_TilingCount = jsonNetLane.curveProperties.TilingCount;
                curveProperties.m_SmoothingDistance = jsonNetLane.curveProperties.SmoothingDistance;
                curveProperties.m_OverrideLength = jsonNetLane.curveProperties.OverrideLength;
                curveProperties.m_GeometryTiling = jsonNetLane.curveProperties.GeometryTiling;
                curveProperties.m_StraightTiling = jsonNetLane.curveProperties.StraightTiling;
                curveProperties.m_SubFlow = jsonNetLane.curveProperties.SubFlow;
                curveProperties.m_InvertCurve = jsonNetLane.curveProperties.InvertCurve;
            }

            NetLaneMeshInfo objectMeshInfo = new()
            {
                m_Mesh = renderPrefab,
            };

            netLanesPrefab.m_Meshes = new[] { objectMeshInfo };

            if (jsonNetLane.PathfindPrefab != null)
            {
                if (EL.m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(PathfindPrefab), jsonNetLane.PathfindPrefab), out PrefabBase prefabBase) && prefabBase is PathfindPrefab pathfindPrefab)
                {
                    netLanesPrefab.m_PathfindPrefab = pathfindPrefab;
                }
                else
                {
                    EAI.Logger.Warn($"Failed to get the PathfindPrefab with the name of {jsonNetLane.PathfindPrefab} for the {fullNetLaneName} asset.");
                }
            }

            //NetLaneGeometryPrefab placeholder = (NetLaneGeometryPrefab)ScriptableObject.CreateInstance("NetLaneGeometryPrefab");
            //      placeholder.name = $"{fullNetLaneName}_Placeholder";
            //      placeholder.m_Meshes = [objectMeshInfo];
            //      placeholder.AddComponent<PlaceholderObject>();

            //SpawnableLane spawnableObject = netLanesPrefab.AddComponent<SpawnableLane>();
            //spawnableObject.m_Placeholders = [placeholder];

            if (jsonNetLane.utilityLane != null)
            {
                UtilityLane utilityLane = netLanesPrefab.AddComponent<UtilityLane>();
                utilityLane.m_UtilityType = jsonNetLane.utilityLane.UtilityType;
                utilityLane.m_VisualCapacity = jsonNetLane.utilityLane.VisualCapacity;
                utilityLane.m_Width = jsonNetLane.utilityLane.Width;
                utilityLane.m_Hanging = jsonNetLane.utilityLane.Hanging;
                utilityLane.m_Underground = jsonNetLane.utilityLane.Underground;
            }

            //SecondaryLane secondaryLane = netLanesPrefab.AddComponent<SecondaryLane>();

            UIObject netLanesPrefabUI = netLanesPrefab.AddComponent<UIObject>();
            netLanesPrefabUI.m_IsDebugObject = false;
            netLanesPrefabUI.m_Icon = File.Exists(iconPath) ? $"{Icons.COUIBaseLocation}/CustomNetLanes/{catName}/{netLanesName}/icon.png" : Icons.NetLanesPlaceholder;
            netLanesPrefabUI.m_Priority = jsonNetLane.UiPriority;
            netLanesPrefabUI.m_Group = PrefabsHelper.GetOrCreateUIAssetChildCategoryPrefab(assetCat, $"{catName} {assetCat.name}");

            AssetDataPath prefabAssetPath = AssetDataPath.Create("TempAssetsFolder", fullNetLaneName + PrefabAsset.kExtension, EscapeStrategy.None);
            EAIDataBaseManager.EAIAssetDataBase.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, netLanesPrefab, forceGuid: Colossal.Hash128.CreateGuid(fullNetLaneName));

            EL.m_PrefabSystem.AddPrefab(netLanesPrefab);
        }

        private static void VersionCompatiblity(JsonNetLanes jSONNetLanesMaterail, string catName, string netLanesName, string fullNetLaneName)
        {
            if (EAI.m_Setting.OldImportersCompatibilityDropDown == EAIOldImportersCompatibility.LocalAsset)
            {
                PrefabIdentifierInfo prefabIdentifierInfo = new()
                {
                    m_Name = $"ExtraAssetsImporter {catName} {netLanesName} NetLane",
                    m_Type = "NetLaneGeometryPrefab"
                };
                jSONNetLanesMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
            }
            if (EAI.m_Setting.OldImportersCompatibilityDropDown == EAIOldImportersCompatibility.ELT3)
            {
                PrefabIdentifierInfo prefabIdentifierInfo = new()
                {
                    m_Name = $"ExtraLandscapingTools_mods_{catName}_{netLanesName}",
                    m_Type = "NetLaneGeometryPrefab"
                };
                jSONNetLanesMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
            }
            if (EAI.m_Setting.OldImportersCompatibilityDropDown == EAIOldImportersCompatibility.PreEditor)
            {
                PrefabIdentifierInfo prefabIdentifierInfo = new()
                {
                    m_Name = fullNetLaneName,
                    m_Type = "NetLaneGeometryPrefab"
                };
                jSONNetLanesMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
            }
        }
    }

    public class JsonCurveProperties
    {
        public int TilingCount = 0;
        public float OverrideLength = 0;
        public float SmoothingDistance = 0;
        public bool GeometryTiling = false;
        public bool StraightTiling = false;
        public bool InvertCurve = false;
        public bool SubFlow = false;
        public bool HangingSwaying = false;
    }

    public class JsonUtilityLane
    {
        public Game.Net.UtilityTypes UtilityType = Game.Net.UtilityTypes.WaterPipe;
        public float Width;
        public float VisualCapacity;
        public float Hanging;
        public bool Underground;
    }

    public class JsonNetLanes
    {
        public int UiPriority = 0;
        public string PathfindPrefab = null;
        public JsonCurveProperties curveProperties = null;
        public JsonUtilityLane utilityLane = null;
        public List<PrefabIdentifierInfo> prefabIdentifierInfos = new();
    }

}

