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
using Colossal.PSI.Environment;
using Extra.Lib.mod.ClassExtension;
using ExtraAssetsImporter.DataBase;
using static Colossal.AssetPipeline.Importers.DefaultTextureImporter;
using Extra.Lib.Helper;

namespace ExtraAssetsImporter.Importers;

public class JSONDecalsMaterail
{
	public int UiPriority = 0;
	public Dictionary<string, float> Float = [];
	public Dictionary<string, Vector4> Vector = [];
	public List<PrefabIdentifierInfo> prefabIdentifierInfos = [];
}

internal class DecalsImporter
{
	internal static List<string> FolderToLoadDecals = [];
	private static bool DecalsLoading = false;
	internal static bool DecalsLoaded = false;

	//private static readonly List<string> validName = ["_BaseColorMap.png", "_NormalMap.png", "_MaskMap.png"];

	//internal static void SearchForCustomDecalsFolder(string ModsFolderPath)
	//{
	//    foreach (DirectoryInfo directory in new DirectoryInfo(ModsFolderPath).GetDirectories())
	//    {
	//        if (File.Exists($"{directory.FullName}\\CustomDecals.zip"))
	//        {
	//            if (Directory.Exists($"{directory.FullName}\\CustomDecals")) Directory.Delete($"{directory.FullName}\\CustomDecals", true);
	//            ZipFile.ExtractToDirectory($"{directory.FullName}\\CustomDecals.zip", directory.FullName);
	//            File.Delete($"{directory.FullName}\\CustomDecals.zip");
	//        }
	//        if (Directory.Exists($"{directory.FullName}\\CustomDecals")) AddCustomDecalsFolder($"{directory.FullName}\\CustomDecals");
	//    }
	//}

	public static void AddCustomDecalsFolder(string path)
	{
		if (FolderToLoadDecals.Contains(path)) return;
		FolderToLoadDecals.Add(path);
		Icons.LoadIcons(new DirectoryInfo(path).Parent.FullName);
	}

	public static void RemoveCustomDecalsFolder(string path)
	{
		if (!FolderToLoadDecals.Contains(path)) return;
		FolderToLoadDecals.Remove(path);
		Icons.UnLoadIcons(new DirectoryInfo(path).Parent.FullName);
	}

	internal static IEnumerator CreateCustomDecals()
	{
		if (DecalsLoading) yield break;

		if (FolderToLoadDecals.Count <= 0)
		{
			DecalsLoaded = true;
			yield break;
		}

		DecalsLoading = true;
		DecalsLoaded = false;

		int numberOfDecals = 0;
		int ammoutOfDecalsloaded = 0;
		int failedDecals = 0;
		int skipedDecal = 0;

		var notificationInfo = ExtraLib.m_NotificationUISystem.AddOrUpdateNotification(
			$"{nameof(ExtraAssetsImporter)}.{nameof(EAI)}.{nameof(CreateCustomDecals)}",
			title: "EAI, Importing the custom decals.",
			progressState: ProgressState.Indeterminate,
			thumbnail: $"{Icons.COUIBaseLocation}/Icons/NotificationInfo/Decals.svg",
			progress: 0
		);

		foreach (string folder in FolderToLoadDecals)
		{
            if (!Directory.Exists(folder)) continue;
            foreach (string catFolder in Directory.GetDirectories(folder))
                foreach (string decalsFolder in Directory.GetDirectories(catFolder))
                    numberOfDecals++;
        }

		ExtraAssetsMenu.AssetCat assetCat = ExtraAssetsMenu.GetOrCreateNewAssetCat("Decals", $"{Icons.COUIBaseLocation}/Icons/UIAssetCategoryPrefab/Decals.svg");

		Dictionary<string, string> csLocalisation = [];

		foreach (string folder in FolderToLoadDecals)
		{
            if (!Directory.Exists(folder)) continue;
            foreach (string catFolder in Directory.GetDirectories(folder))
			{
				foreach (string decalsFolder in Directory.GetDirectories(catFolder))
				{
					string decalName = new DirectoryInfo(decalsFolder).Name;
					notificationInfo.progressState = ProgressState.Progressing;
					notificationInfo.progress = (int)(ammoutOfDecalsloaded / (float)numberOfDecals * 100);
					notificationInfo.text = $"Loading : {decalName}";
					ExtraLib.m_NotificationUISystem.AddOrUpdateNotification(ref notificationInfo);

					if(decalName.StartsWith(".")) {
						skipedDecal++;
						continue;
					}

					string catName = new DirectoryInfo(catFolder).Name;
					FileInfo[] fileInfos = new DirectoryInfo(folder).Parent.GetFiles("*.dll");
					string modName = fileInfos.Length > 0 ? Path.GetFileNameWithoutExtension(fileInfos[0].Name).Split('_')[0] : new DirectoryInfo(folder).Parent.Name.Split('_')[0];
					string fullDecalName = $"{modName} {catName} {decalName} Decal";
					string assetDataPath = Path.Combine("CustomDecals", modName, catName, decalName);

					try
					{
						RenderPrefab renderPrefab = null;

						if (!EAIDataBaseManager.TryGetEAIAsset(fullDecalName, out EAIAsset asset) || asset.AssetHash != EAIDataBaseManager.GetAssetHash(decalsFolder))
						{
							EAI.Logger.Info($"No cahed data for {fullDecalName}, creating the cache.");
							renderPrefab = CreateRenderPrefab(decalsFolder, decalName, catName, modName, fullDecalName, assetDataPath);
							asset = new(fullDecalName, EAIDataBaseManager.GetAssetHash(decalsFolder), assetDataPath);
							EAIDataBaseManager.AddAssets(asset);
						}
						else
						{
                            EAI.Logger.Info($"Cahed data for {fullDecalName}, loading the cache.");
                            List<PrefabBase> loadedObject = EAIDataBaseManager.LoadAsset(fullDecalName);
							foreach (PrefabBase prefabBase in loadedObject)
							{
								if (prefabBase is RenderPrefab renderPrefab1)
								{
									renderPrefab = renderPrefab1;
									break;
								}
							}

							if(renderPrefab == null)
							{
								EAI.Logger.Warn($"EAI failed to load the cached data for {fullDecalName}");
								renderPrefab = CreateRenderPrefab(decalsFolder, decalName, catName, modName, fullDecalName, assetDataPath);
								asset = new(fullDecalName, EAIDataBaseManager.GetAssetHash(decalsFolder), assetDataPath);
								EAIDataBaseManager.AddAssets(asset);
							}
						}

						ExtraAssetsMenu.GetOrCreateNewUIAssetCategoryPrefab(catName, Icons.GetIcon, assetCat);
						CreateCustomDecal(decalsFolder, decalName, catName, modName, fullDecalName, assetDataPath, assetCat, renderPrefab);

						if (!csLocalisation.ContainsKey($"Assets.NAME[{fullDecalName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.NAME[{fullDecalName}]")) csLocalisation.Add($"Assets.NAME[{fullDecalName}]", decalName);
						if (!csLocalisation.ContainsKey($"Assets.DESCRIPTION[{fullDecalName}]") && !GameManager.instance.localizationManager.activeDictionary.ContainsID($"Assets.DESCRIPTION[{fullDecalName}]")) csLocalisation.Add($"Assets.DESCRIPTION[{fullDecalName}]", decalName);
					}
					catch (Exception e)
					{
						failedDecals++;
						EAI.Logger.Error($"Failed to load the custom decal at {decalsFolder} | ERROR : {e}");
						string pathToAssetInDatabase = Path.Combine(AssetDataBaseEAI.kRootPath, assetDataPath);
						if(Directory.Exists(pathToAssetInDatabase)) Directory.Delete(pathToAssetInDatabase, true);
					}
					ammoutOfDecalsloaded++;
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
			text: $"Complete, {numberOfDecals - failedDecals} Loaded, {failedDecals} failed, {skipedDecal} skipped.",
			progressState: ProgressState.Complete,
			progress: 100
		);

		//LoadLocalization();
		DecalsLoaded = true;
		DecalsLoading = false;
	}

	private static void CreateCustomDecal(string folderPath, string decalName, string catName, string modName, string fullDecalName, string assetDataPath, ExtraAssetsMenu.AssetCat assetCat, RenderPrefab renderPrefab)
	{
		if(renderPrefab == null) throw new NullReferenceException("RenderPrefab is NULL.");

		ObjectMeshInfo objectMeshInfo = new()
		{
			m_Mesh = renderPrefab,
			m_Position = float3.zero,
			m_RequireState = Game.Objects.ObjectState.None
		};

		StaticObjectPrefab decalPrefab = ScriptableObject.CreateInstance<StaticObjectPrefab>();
		decalPrefab.name = fullDecalName;

		JSONDecalsMaterail jSONMaterail = new();

		string jsonDecalPath = Path.Combine(folderPath, "decal.json");
		if (File.Exists(jsonDecalPath))
		{
			jSONMaterail = Decoder.Decode(File.ReadAllText(jsonDecalPath)).Make<JSONDecalsMaterail>();

			if (jSONMaterail.Float.ContainsKey("UiPriority")) jSONMaterail.UiPriority = (int)jSONMaterail.Float["UiPriority"];

			VersionCompatiblity(jSONMaterail, catName, decalName);
			if (jSONMaterail.prefabIdentifierInfos.Count > 0)
			{
				ObsoleteIdentifiers obsoleteIdentifiers = decalPrefab.AddComponent<ObsoleteIdentifiers>();
				obsoleteIdentifiers.m_PrefabIdentifiers = [.. jSONMaterail.prefabIdentifierInfos];
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

		decalPrefab.m_Meshes = [objectMeshInfo];

		StaticObjectPrefab placeholder = ScriptableObject.CreateInstance<StaticObjectPrefab>();
		placeholder.name = $"{fullDecalName}_Placeholder";
		placeholder.m_Meshes = [objectMeshInfo];
		placeholder.AddComponent<PlaceholderObject>();

		SpawnableObject spawnableObject = decalPrefab.AddComponent<SpawnableObject>();
		spawnableObject.m_Placeholders = [placeholder];

		UIObject decalPrefabUI = decalPrefab.AddComponent<UIObject>();
		decalPrefabUI.m_IsDebugObject = false;
		decalPrefabUI.m_Icon = File.Exists(iconPath) ? $"{Icons.COUIBaseLocation}/CustomDecals/{catName}/{decalName}/icon.png" : Icons.DecalPlaceholder;
		decalPrefabUI.m_Priority = jSONMaterail.UiPriority;
		decalPrefabUI.m_Group = ExtraAssetsMenu.GetOrCreateNewUIAssetCategoryPrefab(catName, Icons.GetIcon, assetCat);

		AssetDataPath prefabAssetPath = AssetDataPath.Create("TempAssetsFolder", fullDecalName+PrefabAsset.kExtension, EscapeStrategy.None);
		EAIDataBaseManager.assetDataBaseEAI.AddAsset<PrefabAsset, ScriptableObject>(prefabAssetPath, decalPrefab, forceGuid: Colossal.Hash128.CreateGuid(fullDecalName));

		ExtraLib.m_PrefabSystem.AddPrefab(decalPrefab);
	}

	internal static RenderPrefab CreateRenderPrefab(string folderPath, string decalName, string catName, string modName, string fullDecalName, string assetDataPath, string materialName = "DefaultDecal")
	{
		string fullAssetDataPath = Path.Combine(AssetDataBaseEAI.kRootPath, assetDataPath);
		if (Directory.Exists(fullAssetDataPath)) { Directory.Delete(fullAssetDataPath, true); }

		Surface decalSurface = new(decalName, materialName);
		decalSurface.AddProperty("colossal_DecalLayerMask", 1);

		string jsonDecalPath = Path.Combine(folderPath, "decal.json");
		if (File.Exists(jsonDecalPath))
		{
			JSONDecalsMaterail jSONMaterail = Decoder.Decode(File.ReadAllText(jsonDecalPath)).Make<JSONDecalsMaterail>();
			foreach (string key in jSONMaterail.Float.Keys) {
				if (key == "UiPriority")  continue;
				decalSurface.AddProperty(key, jSONMaterail.Float[key]);
			}
			foreach (string key in jSONMaterail.Vector.Keys) { decalSurface.AddProperty(key, jSONMaterail.Vector[key]); }
		}


		DefaultTextureImporter defaultTextureImporter = ImporterCache.GetImporter(".png") as DefaultTextureImporter;

		ImportSettings importSettings = ImportSettings.GetDefault();
		importSettings.compressBC = true;
		importSettings.wrapMode = TextureWrapMode.Repeat;
		TextureImporter.Texture textureImporterBaseColorMap = defaultTextureImporter.Import(importSettings, Path.Combine(folderPath, "_BaseColorMap.png"));
		decalSurface.AddProperty("_BaseColorMap", textureImporterBaseColorMap);

		string normalMapPath = Path.Combine(folderPath, "_NormalMap.png");
		if (File.Exists(normalMapPath))
		{
			importSettings = ImportSettings.GetDefault();
			importSettings.normalMap = true;
			importSettings.alphaIsTransparency = false;
			importSettings.overrideCompressionFormat = Colossal.AssetPipeline.Native.NativeTextures.BlockCompressionFormat.BC7;
			importSettings.wrapMode = TextureWrapMode.Repeat;
            TextureImporter.Texture textureImporterNormalMap = defaultTextureImporter.Import(importSettings, normalMapPath);
			decalSurface.AddProperty("_NormalMap", textureImporterNormalMap);
		}

		string maskMapPath = Path.Combine(folderPath, "_MaskMap.png");
		if (File.Exists(maskMapPath))
		{
			importSettings = ImportSettings.GetDefault();
			importSettings.normalMap = false;
			importSettings.alphaIsTransparency = false;
			importSettings.wrapMode = TextureWrapMode.Repeat;
            TextureImporter.Texture textureImporterMaskMap = defaultTextureImporter.Import(importSettings, maskMapPath);
			decalSurface.AddProperty("_MaskMap", textureImporterMaskMap);
		}

		AssetDataPath surfaceAssetDataPath = AssetDataPath.Create(assetDataPath, $"{decalName}_SurfaceAsset", EscapeStrategy.None);
		SurfaceAsset surfaceAsset = new()
		{
			guid = Guid.NewGuid(),
			database = EAIDataBaseManager.assetDataBaseEAI
		};
		surfaceAsset.database.AddAsset<SurfaceAsset>(surfaceAssetDataPath, surfaceAsset.guid);
		surfaceAsset.SetData(decalSurface);
		surfaceAsset.Save(force: false, saveTextures: true, vt: false);


		//VT Stuff
		//VirtualTexturingConfig virtualTexturingConfig = EAI.textureStreamingSystem.virtualTexturingConfig; //(VirtualTexturingConfig)ScriptableObject.CreateInstance("VirtualTexturingConfig");
		//Dictionary<Colossal.IO.AssetDatabase.TextureAsset, List<SurfaceAsset>> textureReferencesMap = [];

		//foreach (Colossal.IO.AssetDatabase.TextureAsset asset in surfaceAsset.textures.Values)
		//{
		//	asset.Save();
		//	textureReferencesMap.Add(asset, [surfaceAsset]);
		//}

		//surfaceAsset.Save(force: false, saveTextures: false, vt: true, virtualTexturingConfig: virtualTexturingConfig, textureReferencesMap: textureReferencesMap, tileSize: virtualTexturingConfig.tileSize, nbMidMipLevelsRequested: 0);

		// END OF VT Stuff.

		Vector4 MeshSize = decalSurface.GetVectorProperty("colossal_MeshSize");
		Vector4 TextureArea = decalSurface.GetVectorProperty("colossal_TextureArea");
		Mesh[] meshes = [ConstructMesh(MeshSize.x, MeshSize.y, MeshSize.z)];

		AssetDataPath geometryAssetDataPath = AssetDataPath.Create(assetDataPath, "GeometryAsset", EscapeStrategy.None);
		GeometryAsset geometryAsset = new()
		{
			guid = Guid.NewGuid(),
			database = EAIDataBaseManager.assetDataBaseEAI
		};
		geometryAsset.database.AddAsset<GeometryAsset>(geometryAssetDataPath, geometryAsset.guid);
		geometryAsset.SetData(meshes);
		geometryAsset.Save(false);

		//RenderPrefab renderPrefab = (RenderPrefab)ScriptableObject.CreateInstance("RenderPrefab");
		PrefabID prefabID = new PrefabID(typeof(RenderPrefab).Name, $"{fullDecalName}_RenderPrefab");

        if (!ExtraLib.m_PrefabSystem.TryGetPrefab(prefabID, out PrefabBase prefabBase) || prefabBase is not RenderPrefab renderPrefab)
		{
            renderPrefab = (RenderPrefab)ScriptableObject.CreateInstance("RenderPrefab");
        }

        renderPrefab.name = $"{fullDecalName}_RenderPrefab";
		renderPrefab.geometryAsset = geometryAsset;//new AssetReference<GeometryAsset>(geometryAsset.guid);
		renderPrefab.surfaceAssets = [surfaceAsset];
		renderPrefab.bounds = new(new(-MeshSize.x * 0.5f, -MeshSize.y * 0.5f, -MeshSize.z * 0.5f), new(MeshSize.x * 0.5f, MeshSize.y * 0.5f, MeshSize.z * 0.5f));
		renderPrefab.meshCount = 1;
		renderPrefab.vertexCount = geometryAsset.GetVertexCount(0);
		renderPrefab.indexCount = 1;
		renderPrefab.manualVTRequired = false;

		DecalProperties decalProperties = renderPrefab.AddOrGetComponent<DecalProperties>();
		decalProperties.m_TextureArea = new(new(TextureArea.x, TextureArea.y), new(TextureArea.z, TextureArea.w));
		decalProperties.m_LayerMask = (DecalLayers)decalSurface.GetFloatProperty("colossal_DecalLayerMask");
		decalProperties.m_RendererPriority = (int)(decalSurface.HasProperty("_DrawOrder") ? decalSurface.GetFloatProperty("_DrawOrder") : 0);
		decalProperties.m_EnableInfoviewColor = false;

		AssetDataPath renderPrefabAssetPath = AssetDataPath.Create(assetDataPath, $"{decalName}_RenderPrefab", EscapeStrategy.None);
		PrefabAsset renderPrefabAsset = EAIDataBaseManager.assetDataBaseEAI.AddAsset(renderPrefabAssetPath, renderPrefab);
		renderPrefabAsset.Save();

		decalSurface.Dispose();
		geometryAsset.Unload();
		surfaceAsset.Unload();

		return renderPrefab;

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

	private static void VersionCompatiblity(JSONDecalsMaterail jSONDecalsMaterail, string catName, string decalName)
	{
		if (EAI.m_Setting.CompatibilityDropDown == EAICompatibility.LocalAsset)
		{
			PrefabIdentifierInfo prefabIdentifierInfo = new()
			{
				m_Name = $"ExtraAssetsImporter {catName} {decalName} Decal",
				m_Type = "StaticObjectPrefab"
			};
			jSONDecalsMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
		}
		if (EAI.m_Setting.CompatibilityDropDown == EAICompatibility.ELT3)
		{
			PrefabIdentifierInfo prefabIdentifierInfo = new()
			{
				m_Name = $"ExtraLandscapingTools_mods_{catName}_{decalName}",
				m_Type = "StaticObjectPrefab"
			};
			jSONDecalsMaterail.prefabIdentifierInfos.Insert(0, prefabIdentifierInfo);
		}
	}
}
