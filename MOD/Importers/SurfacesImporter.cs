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
using Extra;
using Extra.Lib.Helper;

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
		if (SurfacesIsLoading) yield break;

		if(FolderToLoadSurface.Count <= 0)
		{
			SurfacesIsLoaded = true;
			yield break;
		}

		SurfacesIsLoading = true;
		SurfacesIsLoaded = false;

		int numberOfSurfaces = 0;
		int ammoutOfSurfacesloaded = 0;
		int failedSurfaces = 0;
		int skippedSurface = 0;

		var notificationInfo = ExtraLib.m_NotificationUISystem.AddOrUpdateNotification(
			$"{nameof(ExtraAssetsImporter)}.{nameof(EAI)}.{nameof(CreateCustomSurfaces)}",
			title: "EAI, Importing the custom surfaces.",
			progressState: ProgressState.Indeterminate,
			thumbnail: $"{Icons.COUIBaseLocation}/Icons/NotificationInfo/Surfaces.svg",
			progress: 0
		);

		foreach (string folder in FolderToLoadSurface)
		{
			if (!Directory.Exists(folder)) continue;
			foreach (string catFolder in Directory.GetDirectories(folder))
				foreach (string surfaceFolder in Directory.GetDirectories(catFolder))
					numberOfSurfaces++;
		}


		ExtraAssetsMenu.AssetCat assetCat = ExtraAssetsMenu.GetOrCreateNewAssetCat("Surfaces", $"{Icons.COUIBaseLocation}/Icons/UIAssetCategoryPrefab/Surfaces.svg");

		Dictionary<string, string> csLocalisation = [];

		foreach (string folder in FolderToLoadSurface)
		{
			if (!Directory.Exists(folder)) continue;
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
						skippedSurface++;
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
			text: $"Complete, {numberOfSurfaces - failedSurfaces} Loaded, {failedSurfaces} failed, {skippedSurface} skipped.",
			progressState: ProgressState.Complete,
			progress: 100
		);

		//LoadLocalization();
		SurfacesIsLoaded = true;
		SurfacesIsLoading = false;
	}

	private static void CreateCustomSurface(string folderPath, string surfaceName, string catName, string modName, string fullSurfaceName, ExtraAssetsMenu.AssetCat assetCat)
	{

		string baseColorMapPath = Path.Combine(folderPath, "_BaseColorMap.png");

		if (!File.Exists(baseColorMapPath)) { EAI.Logger.Error($"No _BaseColorMap.png file for the {new DirectoryInfo(folderPath).Name} surface in {catName} category in {modName}."); return; }

		SurfacePrefab surfacePrefab = ScriptableObject.CreateInstance<SurfacePrefab>();
		surfacePrefab.name = fullSurfaceName;
		surfacePrefab.m_Color = new(255f, 255f, 255f, 0.05f);

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
			foreach (string key in jSONMaterail.Vector.Keys) { if(newMaterial.HasVector(key)) newMaterial.SetVector(key, jSONMaterail.Vector[key]); }

			VersionCompatiblity(jSONMaterail, catName, surfaceName);
			if (jSONMaterail.prefabIdentifierInfos.Count > 0)
			{
				ObsoleteIdentifiers obsoleteIdentifiers = surfacePrefab.AddComponent<ObsoleteIdentifiers>();
				obsoleteIdentifiers.m_PrefabIdentifiers = [.. jSONMaterail.prefabIdentifierInfos];
			}
		}

		byte[] fileData;

		fileData = File.ReadAllBytes(baseColorMapPath);
		Texture2D texture2D_BaseColorMap = new(1, 1);
		if (!texture2D_BaseColorMap.LoadImage(fileData)) {
			EAI.Logger.Error($"[ELT] Failed to Load the BaseColorMap image for the {surfacePrefab.name} surface.");
			UnityEngine.Object.Destroy(texture2D_BaseColorMap);
			UnityEngine.Object.Destroy(newMaterial);
			return; 
		}

		newMaterial.SetTexture(ShaderPropertiesIDs.BaseColorMap, texture2D_BaseColorMap);


		string normalMapPath = Path.Combine(folderPath, "_NormalMap.png");
		if (File.Exists(normalMapPath))
		{
			fileData = File.ReadAllBytes(normalMapPath);
			Texture2D texture2D_NormalMap = new(1, 1);
			if (texture2D_NormalMap.LoadImage(fileData))
			{
				newMaterial.SetTexture(ShaderPropertiesIDs.NormalMap, texture2D_NormalMap);
			} else
			{
				UnityEngine.Object.Destroy(texture2D_NormalMap);
				EAI.Logger.Warn($"Failed to load the NormalMap texture data for the {fullSurfaceName}");
			}
		}

		string maskMapPath = Path.Combine(folderPath, "_MaskMap.png");
		if (File.Exists(maskMapPath))
		{
			fileData = File.ReadAllBytes(maskMapPath);
			Texture2D texture2D_MaskMap = new(1, 1);
			if (texture2D_MaskMap.LoadImage(fileData))
			{
				newMaterial.SetTexture(ShaderPropertiesIDs.MaskMap, texture2D_MaskMap);
			} else
			{
				UnityEngine.Object.Destroy(texture2D_MaskMap);
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
		} else
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
		surfacePrefabUI.m_Group = ExtraAssetsMenu.GetOrCreateNewUIAssetCategoryPrefab(catName, Icons.GetIcon, assetCat);

		AssetDataPath prefabAssetPath = AssetDataPath.Create("TempAssetsFolder", fullSurfaceName + PrefabAsset.kExtension, EscapeStrategy.None);
		EAIDataBaseManager.assetDataBaseEAI.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, surfacePrefab, forceGuid: Colossal.Hash128.CreateGuid(fullSurfaceName));

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
		material.SetFloat(ShaderPropertiesIDs.DecalColorMask0, 15);
		material.SetFloat(ShaderPropertiesIDs.DecalColorMask1, 15);
		material.SetFloat(ShaderPropertiesIDs.DecalColorMask2, 11);
		material.SetFloat(ShaderPropertiesIDs.DecalColorMask3, 8);
		material.SetFloat(ShaderPropertiesIDs.DecalStencilRef, 16);
		material.SetFloat(ShaderPropertiesIDs.DecalStencilWriteMask, 16);
		material.SetFloat(ShaderPropertiesIDs.colossal_DecalLayerMask, 1);
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
