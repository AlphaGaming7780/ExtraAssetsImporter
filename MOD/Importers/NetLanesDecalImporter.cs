using Colossal.AssetPipeline.Importers;
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

                        if (!EAIDataBaseManager.IsAssetsInDataBase(fullNetLaneName))
						{
                            renderPrefab = CreateRenderPrefab(netLanesFolder, netLanesName, catName, modName, fullNetLaneName, assetDataPath);
                            EAIAsset asset = new(fullNetLaneName, EAIDataBaseManager.GetAssetHash(netLanesFolder), assetDataPath);
                            EAIDataBaseManager.AddAssets(asset);
                        }
						else
						{
							List<object> loadedObject = EAIDataBaseManager.LoadAsset(fullNetLaneName);
							foreach (object obj in loadedObject)
							{
								if(obj is RenderPrefab renderPrefab1) renderPrefab = renderPrefab1;
							}
						}

                        ExtraAssetsMenu.GetOrCreateNewUIAssetCategoryPrefab(catName, Icons.GetIcon, assetCat);
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

        //NetPiecePrefab netPiecePrefab = ScriptableObject.CreateInstance<NetPiecePrefab>();
        //      netPiecePrefab.name = renderPrefab.name;
        //netPiecePrefab.surfaceAssets = renderPrefab.surfaceAssets;
        //netPiecePrefab.geometryAsset = renderPrefab.geometryAsset;
        //netPiecePrefab.bounds = renderPrefab.bounds;
        //netPiecePrefab.meshCount = renderPrefab.meshCount;
        //netPiecePrefab.vertexCount = renderPrefab.vertexCount;
        //netPiecePrefab.indexCount = renderPrefab.indexCount;
        //netPiecePrefab.manualVTRequired = renderPrefab.manualVTRequired;

        //netPiecePrefab.m_Layer = NetPieceLayer.Top | NetPieceLayer.Bottom | NetPieceLayer.Surface | NetPieceLayer.Side;
        //netPiecePrefab.m_Length = 1f;
        //netPiecePrefab.m_Width = 1f;
        //netPiecePrefab.m_WidthOffset = 1f;
        //netPiecePrefab.m_NodeOffset = 1f;

  //      UnityEngine.Hash128 hash = UnityEngine.Hash128.Parse("cdfda2f5ecbb82640ad2a6fe7a89c5c7");
  //      IAssetData assetData = AssetDatabase..GetAsset()
  //      if (assetData is null) EAI.Logger.Warn("assetData is NULL");
		//EAI.Logger.Info(assetData.ToString());
  //      PrefabAsset prefabAsset = assetData as PrefabAsset;
		//if (prefabAsset is null) EAI.Logger.Warn("prefabAsset is NULL");
		//ScriptableObject scriptableObject = prefabAsset.Load();
		//EAI.Logger.Info(scriptableObject.GetType());

		CurveProperties curveProperties = renderPrefab.AddComponent<CurveProperties>();
		curveProperties.m_TilingCount = 2;
		curveProperties.m_SmoothingDistance = 0;
		curveProperties.m_OverrideLength = 0;
		curveProperties.m_GeometryTiling = false;

        //ObjectMeshInfo objectMeshInfo = new()
        //{
        //    m_Mesh = renderPrefab,
        //    m_Position = float3.zero,
        //    m_RequireState = Game.Objects.ObjectState.None
        //};

        NetLaneMeshInfo objectMeshInfo = new()
		{
			m_Mesh = renderPrefab,
		};

		netLanesPrefab.m_Meshes = [objectMeshInfo];

        //StaticObjectPrefab placeholder = (StaticObjectPrefab)ScriptableObject.CreateInstance("StaticObjectPrefab");
        //placeholder.name = $"{fullNetLaneName}_Placeholder";
        //placeholder.m_Meshes = [objectMeshInfo];
        //placeholder.AddComponent<PlaceholderObject>();

        NetLaneGeometryPrefab placeholder = (NetLaneGeometryPrefab)ScriptableObject.CreateInstance("NetLaneGeometryPrefab");
        placeholder.name = $"{fullNetLaneName}_Placeholder";
        placeholder.m_Meshes = [objectMeshInfo];
        placeholder.AddComponent<PlaceholderObject>();

		//SpawnableObject spawnableObject = netLanesPrefab.AddComponent<SpawnableObject>();
		//spawnableObject.m_Placeholders = [placeholder];

		SpawnableLane spawnableObject = netLanesPrefab.AddComponent<SpawnableLane>();
		spawnableObject.m_Placeholders = [placeholder];

		UtilityLane utilityLane = netLanesPrefab.AddComponent<UtilityLane>();
		utilityLane.m_UtilityType = Game.Net.UtilityTypes.Fence;
		utilityLane.m_VisualCapacity = 2;
		utilityLane.m_Width = 0;

		UIObject netLanesPrefabUI = netLanesPrefab.AddComponent<UIObject>();
        netLanesPrefabUI.m_IsDebugObject = false;
        netLanesPrefabUI.m_Icon = File.Exists(folderPath + "\\icon.png") ? $"{Icons.COUIBaseLocation}/CustomNetLanes/{catName}/{netLanesName}/icon.png" : Icons.NetLanesPlaceholder;
        netLanesPrefabUI.m_Priority = jSONMaterail.UiPriority;
        netLanesPrefabUI.m_Group = ExtraAssetsMenu.GetOrCreateNewUIAssetCategoryPrefab(catName, Icons.GetIcon, assetCat);

        AssetDataPath prefabAssetPath = AssetDataPath.Create("Mods\\EAI\\TempAssetsFolder", fullNetLaneName+PrefabAsset.kExtension, EscapeStrategy.None);
		AssetDatabase.game.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, netLanesPrefab, forceGuid: Colossal.Hash128.CreateGuid(fullNetLaneName));

        ExtraLib.m_PrefabSystem.AddPrefab(netLanesPrefab);
    }

	private static RenderPrefab CreateRenderPrefab(string folderPath, string netLanesName, string catName, string modName, string fullNetLaneName, string assetDataPath)
	{

        if (Directory.Exists($"{AssetDataBaseEAI.rootPath}\\{assetDataPath}")) { Directory.Delete($"{AssetDataBaseEAI.rootPath}\\{assetDataPath}", true); }

        //EAIAsset asset = new(fullDecalName, EAIDataBaseManager.GetAssetHash(folderPath));
        //EAIAsset asset = new(fullDecalName, EAIDataBaseManager.GetAssetHash(decalsFolder), assetDataPath);

        Surface netLaneSurface = new(netLanesName, "DefaultDecal");
        netLaneSurface.AddProperty("colossal_DecalLayerMask", 1);

        if (File.Exists(folderPath + "\\decal.json"))
        {
            JSONDecalsMaterail jSONMaterail = Decoder.Decode(File.ReadAllText(folderPath + "\\decal.json")).Make<JSONDecalsMaterail>();
            foreach (string key in jSONMaterail.Float.Keys)
            {
                if (key == "UiPriority") continue;
                netLaneSurface.AddProperty(key, jSONMaterail.Float[key]);
            }
            foreach (string key in jSONMaterail.Vector.Keys) { netLaneSurface.AddProperty(key, jSONMaterail.Vector[key]); }
        }

        // if(!netLaneSurface.floats.ContainsKey("_DrawOrder")) netLaneSurface.AddProperty("_DrawOrder", 0f);

        byte[] fileData;

        fileData = File.ReadAllBytes(folderPath + "\\_BaseColorMap.png");
        Texture2D texture2D_BaseColorMap_Temp = new(1, 1);
        if (!texture2D_BaseColorMap_Temp.LoadImage(fileData)) { EAI.Logger.Error($"[EAI] Failed to Load the BaseColorMap image for the {netLanesName} decal."); return null; }

        Texture2D texture2D_BaseColorMap = new(texture2D_BaseColorMap_Temp.width, texture2D_BaseColorMap_Temp.height, GraphicsFormat.R8G8B8A8_SRGB, texture2D_BaseColorMap_Temp.mipmapCount, TextureCreationFlags.MipChain)
        {
            name = $"{netLanesName}_BaseColorMap"
        };

        for (int i = 0; i < texture2D_BaseColorMap_Temp.mipmapCount; i++)
        {
            texture2D_BaseColorMap.SetPixels(texture2D_BaseColorMap_Temp.GetPixels(i), i);
        }
        texture2D_BaseColorMap.Apply();
        if (!File.Exists(folderPath + "\\icon.png")) texture2D_BaseColorMap.ResizeTexture(128).SaveTextureAsPNG(folderPath + "\\icon.png");//ELT.ResizeTexture(texture2D_BaseColorMap_Temp, 128, folderPath + "\\icon.png");
        TextureImporter.Texture textureImporterBaseColorMap = new($"{netLanesName}_BaseColorMap", folderPath + "\\" + "_BaseColorMap.png", texture2D_BaseColorMap);
        netLaneSurface.AddProperty("_BaseColorMap", textureImporterBaseColorMap);

        //AssetDataPath pathBaseColorName = AssetDataPath.Create(assetDataPath, "BaseColorMap", EscapeStrategy.None);
        //TextureAsset textureAssetBaseColorMap = AssetDatabase.game.AddAsset<TextureAsset>(pathBaseColorName);
        //textureAssetBaseColorMap.SetData(textureImporterBaseColorMap);
        //textureAssetBaseColorMap.Save();
        //asset.subAssetsDataPath.Add(pathBaseColorName);

        if (File.Exists(folderPath + "\\_NormalMap.png"))
        {
            fileData = File.ReadAllBytes(folderPath + "\\_NormalMap.png");
            Texture2D texture2D_NormalMap_temp = new(1, 1)
            {
                name = $"{netLanesName}_NormalMap"
            };
            if (texture2D_NormalMap_temp.LoadImage(fileData))
            {
                Texture2D texture2D_NormalMap = new(texture2D_NormalMap_temp.width, texture2D_NormalMap_temp.height, GraphicsFormat.R8G8B8A8_SRGB, texture2D_NormalMap_temp.mipmapCount, TextureCreationFlags.None)
                {
                    name = $"{netLanesName}_NormalMap"
                };

                for (int i = 0; i < texture2D_NormalMap_temp.mipmapCount; i++)
                {
                    texture2D_NormalMap.SetPixels(texture2D_NormalMap_temp.GetPixels(i), i);
                }
                texture2D_NormalMap.Apply();
                TextureImporter.Texture textureImporterNormalMap = new($"{netLanesName}_NormalMap", folderPath + "\\" + "_NormalMap.png", texture2D_NormalMap);
                textureImporterNormalMap.CompressBC(1, Colossal.AssetPipeline.Native.NativeTextures.BlockCompressionFormat.BC5);
                netLaneSurface.AddProperty("_NormalMap", textureImporterNormalMap);

                //AssetDataPath NormalMapPath = AssetDataPath.Create(assetDataPath, "NormalMap", EscapeStrategy.None);
                //TextureAsset textureAsset = AssetDatabase.game.AddAsset<TextureAsset>(NormalMapPath);
                //textureAsset.SetData(textureImporterNormalMap);
                //textureAsset.Save();
                //asset.subAssetsDataPath.Add(NormalMapPath);

            };
        }

        if (File.Exists(folderPath + "\\_MaskMap.png"))
        {
            fileData = File.ReadAllBytes(folderPath + "\\_MaskMap.png");
            Texture2D texture2D_MaskMap_temp = new(1, 1);
            if (texture2D_MaskMap_temp.LoadImage(fileData))
            {
                Texture2D texture2D_MaskMap = new(texture2D_MaskMap_temp.width, texture2D_MaskMap_temp.height, GraphicsFormat.R8G8B8A8_SRGB, texture2D_MaskMap_temp.mipmapCount, TextureCreationFlags.None)
                {
                    name = $"{netLanesName}_MaskMap"
                };

                for (int i = 0; i < texture2D_MaskMap_temp.mipmapCount; i++)
                {
                    texture2D_MaskMap.SetPixels(texture2D_MaskMap_temp.GetPixels(i), i);
                }
                texture2D_MaskMap.Apply();
                TextureImporter.Texture textureImporterMaskMap = new($"{netLanesName}_MaskMap", folderPath + "\\" + "_MaskMap.png", texture2D_MaskMap);
                //textureImporterMaskMap.CompressBC(1);
                netLaneSurface.AddProperty("_MaskMap", textureImporterMaskMap);

                //AssetDataPath MaskMapPath = AssetDataPath.Create(assetDataPath, "MaskMap", EscapeStrategy.None);
                //TextureAsset textureAsset = AssetDatabase.game.AddAsset<TextureAsset>(MaskMapPath);
                //textureAsset.SetData(textureImporterMaskMap);
                //textureAsset.Save();
                //asset.subAssetsDataPath.Add(MaskMapPath);
            };
        }

        AssetDataPath surfaceAssetDataPath = AssetDataPath.Create(assetDataPath, "SurfaceAsset", EscapeStrategy.None);
        SurfaceAsset surfaceAsset = new()
        {
            guid = Guid.NewGuid(), //DecalRenderPrefab.surfaceAssets.ToArray()[0].guid, //
                                   //database = AssetDatabase.game //DecalRenderPrefab.surfaceAssets.ToArray()[0].database,
            database = EAIDataBaseManager.assetDataBaseEAI //DecalRenderPrefab.surfaceAssets.ToArray()[0].database,
        };
        surfaceAsset.database.AddAsset<SurfaceAsset>(surfaceAssetDataPath, surfaceAsset.guid);
        surfaceAsset.SetData(netLaneSurface);
        surfaceAsset.Save(force: false, saveTextures: true, vt: false);
        //asset.subAssetsDataPath.Add(surfaceAssetDataPath);

        Vector4 MeshSize = netLaneSurface.GetVectorProperty("colossal_MeshSize");
        Vector4 TextureArea = netLaneSurface.GetVectorProperty("colossal_TextureArea");
        Mesh[] meshes = [ConstructMesh(MeshSize.x, MeshSize.y, MeshSize.z)];

        AssetDataPath geometryAssetDataPath = AssetDataPath.Create(assetDataPath, "GeometryAsset", EscapeStrategy.None);
        GeometryAsset geometryAsset = new()
        {
            guid = Guid.NewGuid(),
            //database = AssetDatabase.game //DecalRenderPrefab.geometryAsset.database
            database = EAIDataBaseManager.assetDataBaseEAI //DecalRenderPrefab.surfaceAssets.ToArray()[0].database,
        };
        geometryAsset.database.AddAsset<GeometryAsset>(geometryAssetDataPath, geometryAsset.guid);
        geometryAsset.SetData(meshes);
        geometryAsset.Save(false);
        //asset.subAssetsDataPath.Add(geometryAssetDataPath);

        RenderPrefab renderPrefab = (RenderPrefab)ScriptableObject.CreateInstance("RenderPrefab");
        renderPrefab.name = $"{fullNetLaneName}_RenderPrefab";
        renderPrefab.geometryAsset = geometryAsset;//new AssetReference<GeometryAsset>(geometryAsset.guid);
        renderPrefab.surfaceAssets = [surfaceAsset];
        renderPrefab.bounds = new(new(-MeshSize.x * 0.5f, -MeshSize.y * 0.5f, -MeshSize.z * 0.5f), new(MeshSize.x * 0.5f, MeshSize.y * 0.5f, MeshSize.z * 0.5f));
        renderPrefab.meshCount = 1;
        renderPrefab.vertexCount = geometryAsset.GetVertexCount(0);
        renderPrefab.indexCount = 1;
        renderPrefab.manualVTRequired = false;

        DecalProperties decalProperties = renderPrefab.AddComponent<DecalProperties>();
        decalProperties.m_TextureArea = new(new(TextureArea.x, TextureArea.y), new(TextureArea.z, TextureArea.w));
        decalProperties.m_LayerMask = (DecalLayers)netLaneSurface.GetFloatProperty("colossal_DecalLayerMask");
        decalProperties.m_RendererPriority = (int)(netLaneSurface.HasProperty("_DrawOrder") ? netLaneSurface.GetFloatProperty("_DrawOrder") : 0);
        decalProperties.m_EnableInfoviewColor = false;//DecalPropertiesPrefab.m_EnableInfoviewColor;

        //AssetDataPath renderPrefabAssetPath = AssetDataPath.Create($"Mods/EAI/CustomNetLanes/{modName}/{catName}/{netLanesName}", $"{netLanesName}_RenderPrefab", EscapeStrategy.None);
        AssetDataPath renderPrefabAssetPath = AssetDataPath.Create($"CustomNetLanes/{modName}/{catName}/{netLanesName}", $"{netLanesName}_RenderPrefab", EscapeStrategy.None);
        //PrefabAsset renderPrefabAsset = AssetDatabase.game.AddAsset(renderPrefabAssetPath, renderPrefab);
        PrefabAsset renderPrefabAsset = EAIDataBaseManager.assetDataBaseEAI.AddAsset(renderPrefabAssetPath, renderPrefab);
        renderPrefabAsset.Save();

        netLaneSurface.Dispose();
        geometryAsset.Unload();
        surfaceAsset.Unload();

        return renderPrefab;

        /*

		ObjectMeshInfo objectMeshInfo = new()
		{
			m_Mesh = renderPrefab,
			m_Position = float3.zero,
			m_RequireState = Game.Objects.ObjectState.None
		};

		netLanesPrefab.m_Meshes = [objectMeshInfo];

		StaticObjectPrefab placeholder = (StaticObjectPrefab)ScriptableObject.CreateInstance("StaticObjectPrefab");
		placeholder.name = $"{fullNetLaneName}_Placeholder";
		placeholder.m_Meshes = [objectMeshInfo];
		placeholder.AddComponent<PlaceholderObject>();

        AssetDataPath placeholderPrefabAssetPath = AssetDataPath.Create($"Mods/EAI/CustomNetLanes/{modName}/{catName}/{netLanesName}", $"{netLanesName}_Placeholder", EscapeStrategy.None);
        PrefabAsset placeholderPrefabAsset = AssetDatabase.game.AddAsset(placeholderPrefabAssetPath, placeholder);
		placeholderPrefabAsset.Save();

        SpawnableObject spawnableObject = netLanesPrefab.AddComponent<SpawnableObject>();
		spawnableObject.m_Placeholders = [placeholder];

		UIObject netLanesPrefabUI = netLanesPrefab.AddComponent<UIObject>();
		netLanesPrefabUI.m_IsDebugObject = false;
		netLanesPrefabUI.m_Icon = File.Exists(folderPath + "\\icon.png") ? $"{Icons.COUIBaseLocation}/CustomNetLanes/{catName}/{netLanesName}/icon.png" : Icons.NetLanePlaceholder;
		netLanesPrefabUI.m_Priority = (int)(netLanesSurface.HasProperty("UiPriority") ? netLanesSurface.GetFloatProperty("UiPriority") : -1);
		netLanesPrefabUI.m_Group = ExtraAssetsMenu.GetOrCreateNewUIAssetCategoryPrefab(catName, Icons.GetIcon, assetCat);

		AssetDataPath prefabAssetPath = AssetDataPath.Create(assetDataPath, netLanesName, EscapeStrategy.None);
		PrefabAsset prefabAsset = AssetDatabase.game.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, netLanesPrefab, forceGuid: Colossal.Hash128.CreateGuid(fullNetLaneName));
		prefabAsset.Save(false, false);
        //asset.assetDataPath = prefabAssetPath;

		EAIDataBaseManager.AddAssets(asset);

		ExtraLib.m_PrefabSystem.AddPrefab(netLanesPrefab);
		*/

    }

	internal static Mesh ConstructMesh(float length, float height, float width)
	{
		Mesh mesh = new();

		//3) Define the co-ordinates of each Corner of the cube 
		Vector3[] c =
		[
			new Vector3(-length * .5f, -height * .5f, width * .5f),
			new Vector3(length * .5f, -height * .5f, width * .5f),
			new Vector3(length * .5f, -height * .5f, -width * .5f),
			new Vector3(-length * .5f, -height * .5f, -width * .5f),
			new Vector3(-length * .5f, height * .5f, width * .5f),
			new Vector3(length * .5f, height * .5f, width * .5f),
			new Vector3(length * .5f, height * .5f, -width * .5f),
			new Vector3(-length * .5f, height * .5f, -width * .5f),
		];


		//4) Define the vertices that the cube is composed of:
		//I have used 16 vertices (4 vertices per side). 
		//This is because I want the vertices of each side to have separate normals.
		//(so the object renders light/shade correctly) 
		Vector3[] vertices =
		[
			c[0], c[1], c[2], c[3], // Bottom
			c[7], c[4], c[0], c[3], // Left
			c[4], c[5], c[1], c[0], // Front
			c[6], c[7], c[3], c[2], // Back
			c[5], c[6], c[2], c[1], // Right
			c[7], c[6], c[5], c[4]  // Top
		];


		//5) Define each vertex's Normal
		Vector3 up = Vector3.up;
		Vector3 down = Vector3.down;
		Vector3 forward = Vector3.forward;
		Vector3 back = Vector3.back;
		Vector3 left = Vector3.left;
		Vector3 right = Vector3.right;


		Vector3[] normals =
		[
			down, down, down, down,             // Bottom
			left, left, left, left,             // Left
			forward, forward, forward, forward,	// Front
			back, back, back, back,             // Back
			right, right, right, right,         // Right
			up, up, up, up                      // Top
		];

		//6) Define each vertex's UV co-ordinates
		Vector2 uv00 = new(0f, 0f);
		Vector2 uv10 = new(1f, 0f);
		Vector2 uv01 = new(0f, 1f);
		Vector2 uv11 = new(1f, 1f);

		Vector2[] uvs =
		[
			uv11, uv01, uv00, uv10, // Bottom
			uv11, uv01, uv00, uv10, // Left
			uv11, uv01, uv00, uv10, // Front
			uv11, uv01, uv00, uv10, // Back	        
			uv11, uv01, uv00, uv10, // Right 
			uv11, uv01, uv00, uv10  // Top
		];


		//7) Define the Polygons (triangles) that make up the our Mesh (cube)
		//IMPORTANT: Unity uses a 'Clockwise Winding Order' for determining front-facing polygons.
		//This means that a polygon's vertices must be defined in 
		//a clockwise order (relative to the camera) in order to be rendered/visible.
		int[] triangles =
		[
			3, 1, 0,        3, 2, 1,        // Bottom	
			7, 5, 4,        7, 6, 5,        // Left
			11, 9, 8,       11, 10, 9,      // Front
			15, 13, 12,     15, 14, 13,     // Back
			19, 17, 16,     19, 18, 17,	    // Right
			23, 21, 20,     23, 22, 21,	    // Top
		];


		//8) Build the Mesh
		mesh.Clear();
		mesh.vertices = vertices;
		mesh.triangles = triangles;
		mesh.normals = normals;
		mesh.uv = uvs;
		mesh.Optimize();
		// mesh.RecalculateNormals();
		mesh.RecalculateTangents();
		mesh.RecalculateBounds();

		return mesh;
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
internal class CustomNetLane : ComponentBase
{
	public override void GetArchetypeComponents(HashSet<ComponentType> components) { }
	public override void GetPrefabComponents(HashSet<ComponentType> components) { }
}
