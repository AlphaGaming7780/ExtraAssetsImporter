﻿using Colossal.AssetPipeline.Importers;
using Colossal.IO.AssetDatabase;
using Game.Prefabs;
using Game.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Experimental.Rendering;
using UnityEngine;
using Colossal.AssetPipeline;
using Unity.Mathematics;
using Extra.Lib;
using Extra.Lib.UI;
using System.Collections;
using Colossal.PSI.Common;
using Colossal.Json;
using Unity.Entities;
using Game.SceneFlow;
using Colossal.Localization;
using TextureAsset = Colossal.IO.AssetDatabase.TextureAsset;
using Colossal.PSI.Environment;
using static Extra.Lib.UI.ExtraAssetsMenu;
using ExtraAssetsImporter.DataBase;

namespace ExtraAssetsImporter.Importers;

public class JSONNetLanesMaterail
{
	public int UiPriority = 0;
    public Dictionary<string, float> Float = [];
	public Dictionary<string, Vector4> Vector = [];
	public List<PrefabIdentifierInfo> prefabIdentifierInfos = [];
}

internal class NetLanesDecalImporter
{
	internal static List<string> FolderToLoadNetLanes = [];
	private static bool NetLanesLoading = false;
	internal static bool NetLanesLoaded = false;

	internal static void LoadLocalization()
	{

		Dictionary<string, string> csLocalisation = [];

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
		if (NetLanesLoading || FolderToLoadNetLanes.Count <= 0) yield break;

		NetLanesLoading = true;

		int numberOfNetLanes = 0;
		int ammoutOfNetLanesloaded = 0;
		int failedNetLanes = 0;

		var notificationInfo = ExtraLib.m_NotificationUISystem.AddOrUpdateNotification(
			$"{nameof(ExtraAssetsImporter)}.{nameof(EAI)}.{nameof(CreateCustomNetLanes)}",
			title: "EAI, Importing the custom net lanes.",
			progressState: ProgressState.Indeterminate,
			thumbnail: $"{Icons.COUIBaseLocation}/Icons/NotificationInfo/NetLanes.svg",
			progress: 0
		);

		foreach (string folder in FolderToLoadNetLanes)
			foreach (string catFolder in Directory.GetDirectories(folder))
				foreach (string netLanesFolder in Directory.GetDirectories(catFolder))
					numberOfNetLanes++;

		ExtraAssetsMenu.AssetCat assetCat = ExtraAssetsMenu.GetOrCreateNewAssetCat("NetLanes", $"{Icons.COUIBaseLocation}/Icons/UIAssetCategoryPrefab/NetLanes.svg");

		Dictionary<string, string> csLocalisation = [];

		foreach (string folder in FolderToLoadNetLanes)
		{
			foreach (string catFolder in Directory.GetDirectories(folder))
			{
				foreach (string netLanesFolder in Directory.GetDirectories(catFolder))
				{
					string netLanesName = new DirectoryInfo(netLanesFolder).Name;
					notificationInfo.progressState = ProgressState.Progressing;
					notificationInfo.progress = (int)(ammoutOfNetLanesloaded / (float)numberOfNetLanes * 100);
					notificationInfo.text = $"Loading : {netLanesName}";

                    if (netLanesName.StartsWith("."))
                    {
                        failedNetLanes++;
                        continue;
                    }

                    try
					{
						string catName = new DirectoryInfo(catFolder).Name;
						FileInfo[] fileInfos = new DirectoryInfo(folder).Parent.GetFiles("*.dll");
						string modName = fileInfos.Length > 0 ? Path.GetFileNameWithoutExtension(fileInfos[0].Name).Split('_')[0] : new DirectoryInfo(folder).Parent.Name.Split('_')[0];
						string fullNetLaneName = $"{modName} {catName} {netLanesName} NetLane";
                        string assetDataPath = $"CustomNetLanes\\{modName}\\{catName}\\{netLanesName}";


                        RenderPrefab renderPrefab = null;

                        if (!EAIDataBaseManager.TryGetEAIAsset(fullNetLaneName, out EAIAsset asset) || asset.AssetHash != EAIDataBaseManager.GetAssetHash(netLanesFolder))
                        {
                            //renderPrefab = CreateRenderPrefab(netLanesFolder, netLanesName, catName, modName, fullNetLaneName, assetDataPath);
                            renderPrefab = DecalsImporter.CreateRenderPrefab(netLanesFolder, netLanesName, catName, modName, fullNetLaneName, assetDataPath, "CurvedDecal");
                            asset = new(fullNetLaneName, EAIDataBaseManager.GetAssetHash(netLanesFolder), assetDataPath);
                            EAIDataBaseManager.AddAssets(asset);
                        }
                        else
                        {
                            List<object> loadedObject = EAIDataBaseManager.LoadAsset(fullNetLaneName);
                            foreach (object obj in loadedObject)
                            {
                                if (obj is RenderPrefab renderPrefab1)
                                {
                                    renderPrefab = renderPrefab1;
                                    break;
                                }
                            }

                            if (renderPrefab == null)
                            {
								EAI.Logger.Warn($"EAI failed to load the cached data for {fullNetLaneName}");
                                renderPrefab = DecalsImporter.CreateRenderPrefab(netLanesFolder, netLanesName, catName, modName, fullNetLaneName, assetDataPath, "CurvedDecal");
                                asset = new(fullNetLaneName, EAIDataBaseManager.GetAssetHash(netLanesFolder), assetDataPath);
                                EAIDataBaseManager.AddAssets(asset);
                            }
                        }

                        CreateCustomNetLane(netLanesFolder, netLanesName, catName, modName, fullNetLaneName, assetDataPath, assetCat, renderPrefab);

						if (!csLocalisation.ContainsKey($"Assets.NAME[{fullNetLaneName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.NAME[{fullNetLaneName}]")) csLocalisation.Add($"Assets.NAME[{fullNetLaneName}]", netLanesName);
						if (!csLocalisation.ContainsKey($"Assets.DESCRIPTION[{fullNetLaneName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.DESCRIPTION[{fullNetLaneName}]")) csLocalisation.Add($"Assets.DESCRIPTION[{fullNetLaneName}]", netLanesName);
					}
					catch (Exception e)
					{
						failedNetLanes++;
						EAI.Logger.Error($"Failed to load the custom netLanes at {netLanesFolder} | ERROR : {e}");
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

		ExtraLib.m_NotificationUISystem.RemoveNotification(
			identifier: notificationInfo.id,
			delay: 5f,
			text: $"Complete, {numberOfNetLanes - failedNetLanes} Loaded, {failedNetLanes} failed.",
			progressState: ProgressState.Complete,
			progress: 100
		);

		//LoadLocalization();
		NetLanesLoaded = true;
	}

	private static void CreateCustomNetLane(string folderPath, string netLanesName, string catName, string modName, string fullNetLaneName, string assetDataPath, ExtraAssetsMenu.AssetCat assetCat, RenderPrefab renderPrefab)
    {
		if(renderPrefab == null) throw new NullReferenceException("RenderPrefab is NULL.");

        // StaticObjectPrefab netLanesPrefab = (StaticObjectPrefab)ScriptableObject.CreateInstance("StaticObjectPrefab");
        NetLaneGeometryPrefab netLanesPrefab = (NetLaneGeometryPrefab)ScriptableObject.CreateInstance("NetLaneGeometryPrefab");
		//NetLanePrefab netLanesPrefab = (NetLanePrefab)ScriptableObject.CreateInstance("NetLanePrefab");
        netLanesPrefab.name = fullNetLaneName;

		JSONNetLanesMaterail jSONMaterail = new();

        if (File.Exists(folderPath + "\\decal.json"))
        {
            jSONMaterail = Decoder.Decode(File.ReadAllText(folderPath + "\\decal.json")).Make<JSONNetLanesMaterail>();

			if (jSONMaterail.Float.ContainsKey("UiPriority")) jSONMaterail.UiPriority = (int)jSONMaterail.Float["UiPriority"];

            VersionCompatiblity(jSONMaterail, catName, netLanesName);
            if (jSONMaterail.prefabIdentifierInfos.Count > 0)
            {
                ObsoleteIdentifiers obsoleteIdentifiers = netLanesPrefab.AddComponent<ObsoleteIdentifiers>();
                obsoleteIdentifiers.m_PrefabIdentifiers = [.. jSONMaterail.prefabIdentifierInfos];
            }
        }

        if (File.Exists(folderPath + "\\icon.png"))
        {
            byte[] fileData = File.ReadAllBytes(folderPath + "\\icon.png");
            Texture2D texture2D_Icon = new(1, 1);
            if (texture2D_Icon.LoadImage(fileData))
            {
                if (texture2D_Icon.width > 128 || texture2D_Icon.height > 128)
                {
                    //ELT.ResizeTexture(texture2D_Icon, 128, folderPath + "\\icon.png");
                    texture2D_Icon.ResizeTexture(128).SaveTextureAsPNG(folderPath + "\\icon.png");
                }
            }
        }

		CurveProperties curveProperties = renderPrefab.AddComponent<CurveProperties>();
		curveProperties.m_TilingCount = 0;
		curveProperties.m_SmoothingDistance = 0.1f;
		curveProperties.m_OverrideLength = 0;
		curveProperties.m_GeometryTiling = false;
		curveProperties.m_StraightTiling = false;
		curveProperties.m_SubFlow = false;
		curveProperties.m_InvertCurve = false;

        NetLaneMeshInfo objectMeshInfo = new()
		{
			m_Mesh = renderPrefab,
		};

        netLanesPrefab.m_Meshes = [objectMeshInfo];

		//NetLaneGeometryPrefab placeholder = (NetLaneGeometryPrefab)ScriptableObject.CreateInstance("NetLaneGeometryPrefab");
  //      placeholder.name = $"{fullNetLaneName}_Placeholder";
  //      placeholder.m_Meshes = [objectMeshInfo];
  //      placeholder.AddComponent<PlaceholderObject>();

		//SpawnableLane spawnableObject = netLanesPrefab.AddComponent<SpawnableLane>();
		//spawnableObject.m_Placeholders = [placeholder];

		//UtilityLane utilityLane = netLanesPrefab.AddComponent<UtilityLane>();
		//utilityLane.m_UtilityType = Game.Net.UtilityTypes.Fence;
		//utilityLane.m_VisualCapacity = 2;
		//utilityLane.m_Width = 0;

		//SecondaryLane secondaryLane = netLanesPrefab.AddComponent<SecondaryLane>();

		UIObject netLanesPrefabUI = netLanesPrefab.AddComponent<UIObject>();
        netLanesPrefabUI.m_IsDebugObject = false;
        netLanesPrefabUI.m_Icon = File.Exists(folderPath + "\\icon.png") ? $"{Icons.COUIBaseLocation}/CustomNetLanes/{catName}/{netLanesName}/icon.png" : Icons.NetLanesPlaceholder;
        netLanesPrefabUI.m_Priority = jSONMaterail.UiPriority;
        netLanesPrefabUI.m_Group = ExtraAssetsMenu.GetOrCreateNewUIAssetCategoryPrefab(catName, Icons.GetIcon, assetCat);

        AssetDataPath prefabAssetPath = AssetDataPath.Create("TempAssetsFolder", fullNetLaneName+PrefabAsset.kExtension, EscapeStrategy.None);
		AssetDatabase.game.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, netLanesPrefab, forceGuid: Colossal.Hash128.CreateGuid(fullNetLaneName));

        ExtraLib.m_PrefabSystem.AddPrefab(netLanesPrefab);
    }

	private static void VersionCompatiblity(JSONNetLanesMaterail jSONNetLanesMaterail, string catName, string netLanesName)
	{
		if (EAI.m_Setting.CompatibilityDropDown == EAICompatibility.LocalAsset)
		{
			PrefabIdentifierInfo prefabIdentifierInfo = new()
			{
				m_Name = $"ExtraAssetsImporter {catName} {netLanesName} NetLane",
				m_Type = "StaticObjectPrefab"
			};
			jSONNetLanesMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
		}
		if (EAI.m_Setting.CompatibilityDropDown == EAICompatibility.ELT3)
		{
			PrefabIdentifierInfo prefabIdentifierInfo = new()
			{
				m_Name = $"ExtraLandscapingTools_mods_{catName}_{netLanesName}",
				m_Type = "StaticObjectPrefab"
			};
			jSONNetLanesMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
		}
	}
}