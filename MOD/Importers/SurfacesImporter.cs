using Game.Prefabs;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Extra.Lib;
using Extra.Lib.UI;
using Colossal.PSI.Common;
using System.Collections;
using Colossal.Json;
using Colossal.Localization;
using Game.SceneFlow;
using Game.Rendering;
using Colossal.IO.AssetDatabase;

namespace ExtraAssetsImporter.Importers;

public class JSONSurfacesMaterail
{
	public int UiPriority = 0;
	public float m_Roundness = 0.5f;
	public Dictionary<string, float> Float = [];
	public Dictionary<string, Vector4> Vector = [];
	public List<PrefabIdentifierInfo> prefabIdentifierInfos = [];
}

internal class SurfacesImporter
{
	internal static List<string> FolderToLoadSurface = [];

	private static bool SurfacesIsLoading = false;
	internal static bool SurfacesIsLoaded = false;

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
		if (SurfacesIsLoading || FolderToLoadSurface.Count <= 0) yield break;

		SurfacesIsLoading = true;

		int numberOfSurfaces = 0;
		int ammoutOfSurfacesloaded = 0;
		int failedSurfaces = 0;

		var notificationInfo = ExtraLib.m_NotificationUISystem.AddOrUpdateNotification(
			$"{nameof(ExtraAssetsImporter)}.{nameof(EAI)}.{nameof(CreateCustomSurfaces)}",
			title: "EAI, Importing the custom surfaces.",
			progressState: ProgressState.Indeterminate,
			thumbnail: $"{Icons.COUIBaseLocation}/Icons/NotificationInfo/Surfaces.svg",
			progress: 0
		);

		foreach (string folder in FolderToLoadSurface)
			foreach (string catFolder in Directory.GetDirectories(folder))
				foreach (string surfaceFolder in Directory.GetDirectories(catFolder))
					numberOfSurfaces++;

		ExtraAssetsMenu.AssetCat assetCat = ExtraAssetsMenu.GetOrCreateNewAssetCat("Surfaces", $"{Icons.COUIBaseLocation}/Icons/UIAssetCategoryPrefab/Surfaces.svg");

		Dictionary<string, string> csLocalisation = [];

		foreach (string folder in FolderToLoadSurface)
		{
			foreach (string surfacesCat in Directory.GetDirectories(folder))
			{
				foreach (string surfaceFolder in Directory.GetDirectories(surfacesCat))
				{
					string surfaceName = new DirectoryInfo(surfaceFolder).Name;
					notificationInfo.progressState = ProgressState.Progressing;
					notificationInfo.progress = (int)(ammoutOfSurfacesloaded / (float)numberOfSurfaces * 100);
					notificationInfo.text = $"Loading : {surfaceName}";
					if (surfaceName.StartsWith("."))
					{
						failedSurfaces++;
						continue;
					}
					try
					{
						string catName = new DirectoryInfo(surfacesCat).Name;
						FileInfo[] fileInfos = new DirectoryInfo(folder).Parent.GetFiles("*.dll");
						string modName = fileInfos.Length > 0 ? Path.GetFileNameWithoutExtension(fileInfos[0].Name).Split('_')[0] : new DirectoryInfo(folder).Parent.Name.Split('_')[0];
						string fullSurfaceName = $"{modName} {catName} {surfaceName} Surface";
						CreateCustomSurface(surfaceFolder, surfaceName, catName, modName, fullSurfaceName, assetCat);
						if (!csLocalisation.ContainsKey($"Assets.NAME[{fullSurfaceName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.NAME[{fullSurfaceName}]")) csLocalisation.Add($"Assets.NAME[{fullSurfaceName}]", surfaceName);
						if (!csLocalisation.ContainsKey($"Assets.DESCRIPTION[{fullSurfaceName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.DESCRIPTION[{fullSurfaceName}]")) csLocalisation.Add($"Assets.DESCRIPTION[{fullSurfaceName}]", surfaceName);
					}
					catch (Exception e)
					{
						failedSurfaces++;
						EAI.Logger.Error($"Failed to load the custom surface at {surfaceFolder} | ERROR : {e}");
					}
					ammoutOfSurfacesloaded++;
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
			text: $"Complete, {numberOfSurfaces - failedSurfaces} Loaded, {failedSurfaces} failed.",
			progressState: ProgressState.Complete,
			progress: 100
		);

		//LoadLocalization();
		SurfacesIsLoaded = true;
	}

	private static void CreateCustomSurface(string folderPath, string surfaceName, string catName, string modName, string fullSurfaceName, ExtraAssetsMenu.AssetCat assetCat)
	{

		if (!File.Exists(folderPath + "\\" + "_BaseColorMap.png")) { EAI.Logger.Error($"No _BaseColorMap.png file for the {new DirectoryInfo(folderPath).Name} surface in {catName} category in {modName}."); return; }

		//string fullSurfaceName = $"{modName} {catName} {surfaceName} Surface";

		JSONSurfacesMaterail jSONMaterail = new();

		SurfacePrefab surfacePrefab = (SurfacePrefab)ScriptableObject.CreateInstance("SurfacePrefab");
		surfacePrefab.name = fullSurfaceName;
		surfacePrefab.m_Color = new(255f, 255f, 255f, 0.05f);

		SurfacePrefab surfacePrefabPlaceHolder = (SurfacePrefab)ScriptableObject.CreateInstance("SurfacePrefab");
		surfacePrefabPlaceHolder.name = surfacePrefab.name + "_Placeholder";

		Material newMaterial = GetDefaultSurfaceMaterial();
		newMaterial.name = fullSurfaceName + " Material";

  //      JSONSurfacesMaterail jSONSurfacesMaterail = new JSONSurfacesMaterail();

  //      foreach ( string s  in newMaterial.GetPropertyNames(MaterialPropertyType.Float)) jSONSurfacesMaterail.Float.Add(s, newMaterial.GetFloat(s));
  //      foreach (string s in newMaterial.GetPropertyNames(MaterialPropertyType.Vector)) jSONSurfacesMaterail.Vector.Add(s, newMaterial.GetVector(s));

		//File.WriteAllText(folderPath + "\\test.json", Encoder.Encode(jSONSurfacesMaterail, EncodeOptions.None));

		//newMaterial.SetVector("_BaseColor", new Vector4(1, 1, 1, 1));
		//newMaterial.SetFloat("_Metallic", 0.5f);
		//newMaterial.SetFloat("_Smoothness", 0.5f);
		//newMaterial.SetFloat("colossal_UVScale", 0.5f);
		newMaterial.SetFloat("_DrawOrder", GetRendererPriorityByCat(catName));

		if (File.Exists(folderPath + "\\surface.json"))
		{
			jSONMaterail = Decoder.Decode(File.ReadAllText(folderPath + "\\surface.json")).Make<JSONSurfacesMaterail>();
			foreach (string key in jSONMaterail.Float.Keys)
			{
				if (newMaterial.HasProperty(key)) newMaterial.SetFloat(key, jSONMaterail.Float[key]);
				else
				{
					if (key == "UiPriority") jSONMaterail.UiPriority = (int)jSONMaterail.Float[key];
				}
			}
			foreach (string key in jSONMaterail.Vector.Keys) { newMaterial.SetVector(key, jSONMaterail.Vector[key]); }

			VersionCompatiblity(jSONMaterail, catName, surfaceName);
			if (jSONMaterail.prefabIdentifierInfos.Count > 0)
			{
				ObsoleteIdentifiers obsoleteIdentifiers = surfacePrefab.AddComponent<ObsoleteIdentifiers>();
				obsoleteIdentifiers.m_PrefabIdentifiers = [.. jSONMaterail.prefabIdentifierInfos];
			}
		}

		byte[] fileData;

		fileData = File.ReadAllBytes(folderPath + "\\_BaseColorMap.png");
		Texture2D texture2D_BaseColorMap = new(1, 1);
		if (!texture2D_BaseColorMap.LoadImage(fileData)) { EAI.Logger.Error($"[ELT] Failed to Load the BaseColorMap image for the {surfacePrefab.name} surface."); return; }

		if (!File.Exists(folderPath + "\\icon.png")) texture2D_BaseColorMap.ResizeTexture(128).SaveTextureAsPNG(folderPath + "\\icon.png"); //ELT.ResizeTexture(texture2D_BaseColorMap, 128, folderPath + "\\icon.png");
		// if(texture2D_BaseColorMap.width > 512 || texture2D_BaseColorMap.height > 512) texture2D_BaseColorMap = ELT.ResizeTexture(texture2D_BaseColorMap, 512, folderPath+"\\_BaseColorMap.png");

		newMaterial.SetTexture("_BaseColorMap", texture2D_BaseColorMap);

		if (File.Exists(folderPath + "\\_NormalMap.png"))
		{
			fileData = File.ReadAllBytes(folderPath + "\\_NormalMap.png");
			Texture2D texture2D_NormalMap = new(1, 1);
			if (texture2D_NormalMap.LoadImage(fileData))
			{
				// if(texture2D_NormalMap.width > 512 || texture2D_NormalMap.height > 512) texture2D_NormalMap = ELT.ResizeTexture(texture2D_NormalMap, 512, folderPath+"\\_NormalMap.png");
				newMaterial.SetTexture("_NormalMap", texture2D_NormalMap);
			}
		}

		if (File.Exists(folderPath + "\\_MaskMap.png"))
		{
			fileData = File.ReadAllBytes(folderPath + "\\_MaskMap.png");
			Texture2D texture2D_MaskMap = new(1, 1);
			if (texture2D_MaskMap.LoadImage(fileData))
			{
				// if(texture2D_MaskMap.width > 512 || texture2D_MaskMap.height > 512) texture2D_MaskMap = ELT.ResizeTexture(texture2D_MaskMap, 512, folderPath+"\\_MaskMap.png");

				newMaterial.SetTexture("_MaskMap", texture2D_MaskMap);
			}
		}

		// try {
		// 	Texture2D texture2D = (Texture2D)newMaterial.GetTexture("_BaseColorMap");
		// 	Plugin.Logger.LogMessage(texture2D);


		// 	newMaterial.SetTexture("_BaseColorMap", texture2D);
		// } catch (Exception e) {Plugin.Logger.LogError(e);}


		//SurfaceAsset surfaceAsset = new SurfaceAsset();
		//surfaceAsset.SetData(newMaterial);
		//surfaceAsset.Save();

		RenderedArea renderedArea = surfacePrefabPlaceHolder.AddComponent<RenderedArea>();
		renderedArea.m_RendererPriority = (int)newMaterial.GetFloat("_DrawOrder");
		renderedArea.m_LodBias = 0;
		renderedArea.m_Roundness = jSONMaterail.m_Roundness;
		renderedArea.m_Material = newMaterial;
		renderedArea.m_DecalLayerMask = (DecalLayers)newMaterial.GetFloat("colossal_DecalLayerMask");

		PlaceholderArea placeholderArea = surfacePrefabPlaceHolder.AddComponent<PlaceholderArea>();

		SpawnableArea spawnableArea = surfacePrefab.AddComponent<SpawnableArea>();
		spawnableArea.m_Placeholders = new AreaPrefab[1];
		spawnableArea.m_Placeholders[0] = surfacePrefabPlaceHolder;

		RenderedArea renderedArea1 = surfacePrefab.AddComponent<RenderedArea>();
		renderedArea1.m_RendererPriority = (int)newMaterial.GetFloat("_DrawOrder");
		renderedArea1.m_LodBias = 0;
		renderedArea1.m_Roundness = jSONMaterail.m_Roundness;
		renderedArea1.m_Material = newMaterial;
		renderedArea1.m_DecalLayerMask = (DecalLayers)newMaterial.GetFloat("colossal_DecalLayerMask");

		if (File.Exists(folderPath + "\\icon.png"))
		{
			fileData = File.ReadAllBytes(folderPath + "\\icon.png");
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

		UIObject surfacePrefabUI = surfacePrefab.AddComponent<UIObject>();
		surfacePrefabUI.m_IsDebugObject = false;
		surfacePrefabUI.m_Icon = File.Exists(folderPath + "\\icon.png") ? $"{Icons.COUIBaseLocation}/CustomSurfaces/{catName}/{new DirectoryInfo(folderPath).Name}/icon.png" : Icons.GetIcon(surfacePrefab);
		surfacePrefabUI.m_Priority = jSONMaterail.UiPriority;
		surfacePrefabUI.m_Group = ExtraAssetsMenu.GetOrCreateNewUIAssetCategoryPrefab(catName, Icons.GetIcon, assetCat);

		AssetDataPath prefabAssetPath = AssetDataPath.Create("Mods\\EAI\\TempAssetsFolder", fullSurfaceName + PrefabAsset.kExtension, EscapeStrategy.None);
		AssetDatabase.game.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, surfacePrefab, forceGuid: Colossal.Hash128.CreateGuid(fullSurfaceName));


		ExtraLib.m_PrefabSystem.AddPrefab(surfacePrefab);
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
		material.SetFloat("_DecalColorMask0", 15);
		material.SetFloat("_DecalColorMask1", 15);
		material.SetFloat("_DecalColorMask2", 11);
		material.SetFloat("_DecalColorMask3", 8);
		material.SetFloat("_DecalStencilRef", 16);
		material.SetFloat("_DecalStencilWriteMask", 16);
		material.SetFloat("colossal_DecalLayerMask", 1);
		material.enableInstancing = true;
		material.shaderKeywords = ["_MATERIAL_AFFECTS_ALBEDO", "_MATERIAL_AFFECTS_MASKMAP", "_MATERIAL_AFFECTS_NORMAL"];
		return material;
	}

	private static void VersionCompatiblity(JSONSurfacesMaterail jSONSurfacesMaterail, string catName, string surfaceName)
	{
		if (EAI.m_Setting.CompatibilityDropDown == EAICompatibility.LocalAsset)
		{
			PrefabIdentifierInfo prefabIdentifierInfo = new()
			{
				m_Name = $"ExtraAssetsImporter {catName} {surfaceName} Surface",
				m_Type = "StaticObjectPrefab"
			};
			jSONSurfacesMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
		}
		if (EAI.m_Setting.CompatibilityDropDown == EAICompatibility.ELT2)
		{
			PrefabIdentifierInfo prefabIdentifierInfo = new()
			{
				m_Name = $"{surfaceName}",
				m_Type = "SurfacePrefab"
			};
			jSONSurfacesMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
		}
		if (EAI.m_Setting.CompatibilityDropDown == EAICompatibility.ELT3)
		{
			PrefabIdentifierInfo prefabIdentifierInfo = new()
			{
				m_Name = $"ExtraLandscapingTools_mods_{catName}_{surfaceName}",
				m_Type = "SurfacePrefab"
			};
			jSONSurfacesMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
		}
	}
}
