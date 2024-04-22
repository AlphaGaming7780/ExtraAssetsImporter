using Extra.Lib;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Colossal.Entities;
using Extra.Lib.UI;
namespace ExtraAssetsImporter;

internal static class EditEntities
{
	internal static void SetupEditEntities()
	{
		EntityQueryDesc surfaceEntityQueryDesc = new()
		{
			All = [ComponentType.ReadOnly<SurfaceData>()],
			None = [ComponentType.ReadOnly<PlaceholderObjectElement>()]
			
		};

		EntityQueryDesc decalsEntityQueryDesc = new()
		{
			All = [
				ComponentType.ReadOnly<StaticObjectData>(),
			],
			Any = [
				ComponentType.ReadOnly<SpawnableObjectData>(),
				ComponentType.ReadOnly<PlaceableObjectData>(),
			],
			None = [ComponentType.ReadOnly<PlaceholderObjectElement>()]
		};

            ExtraLib.AddOnEditEnities(new(OnEditSurfacesEntities, surfaceEntityQueryDesc));
		ExtraLib.AddOnEditEnities(new(OnEditDecalsEntities, decalsEntityQueryDesc));
	}

        private static void OnEditSurfacesEntities(NativeArray<Entity> entities)
	{

		if (entities.Length == 0) return;

            ExtraAssetsMenu.AssetCat assetCat =  ExtraAssetsMenu.GetOrCreateNewAssetCat("Surfaces", $"{Icons.COUIBaseLocation}/Icons/UIAssetCategoryPrefab/Surfaces.svg");

		foreach (Entity entity in entities)
		{
			if (ExtraLib.m_PrefabSystem.TryGetPrefab(entity, out SurfacePrefab prefab))
			{
                    if (!prefab.builtin) continue;

                    //bool isCustom = false;
                    //foreach (ComponentBase componentBase in prefab.components)
                    //{
                    //    if (componentBase.name == "CustomSurface")
                    //    {
                    //        isCustom = true;
                    //        break;
                    //    }
                    //}
                    //if (isCustom)
                    //{
                    //    EDT.Logger.Info(prefab);
                    //    continue;
                    //}

                    var prefabUI = prefab.GetComponent<UIObject>();
				if (prefabUI == null)
				{
					prefabUI = prefab.AddComponent<UIObject>();
					prefabUI.active = true;
					prefabUI.m_IsDebugObject = false;
					prefabUI.m_Icon = Icons.GetIcon(prefab);
					prefabUI.m_Priority = 1;
				}

				prefabUI.m_Group?.RemoveElement(entity);
				prefabUI.m_Group = ExtraAssetsMenu.GetOrCreateNewUIAssetCategoryPrefab(Surfaces.GetCatByRendererPriority(prefab.GetComponent<RenderedArea>() is null ? 0 : prefab.GetComponent<RenderedArea>().m_RendererPriority), Icons.GetIcon, assetCat);
                prefabUI.m_Group.AddElement(entity);

                ExtraLib.m_EntityManager.AddOrSetComponentData(entity, prefabUI.ToComponentData());
			}
		}
	}

	private static void OnEditDecalsEntities(NativeArray<Entity> entities)
	{
		if (entities.Length == 0) return;
            ExtraAssetsMenu.AssetCat assetCat = ExtraAssetsMenu.GetOrCreateNewAssetCat("Decals", $"{Icons.COUIBaseLocation}/Icons/UIAssetCategoryPrefab/Decals.svg");

            foreach (Entity entity in entities)
		{
			if (ExtraLib.m_PrefabSystem.TryGetPrefab(entity, out StaticObjectPrefab prefab))
			{

				if (!prefab.builtin) continue;

				//bool isCustom = false;
				//foreach(ComponentBase componentBase in prefab.components)
				//{
				//	if (componentBase.name == "CustomDecal")
				//	{
				//		isCustom = true;
				//		break;
				//	}
				//}
				//if (isCustom)
				//{
				//	EDT.Logger.Info(prefab);
				//	continue;
				//}

				DynamicBuffer<SubMesh> subMeshes =  ExtraLib.m_EntityManager.GetBuffer<SubMesh>(entity);
				if (!ExtraLib.m_EntityManager.TryGetComponent(subMeshes.ElementAt(0).m_SubMesh, out MeshData component)) continue;
				else if (component.m_State != MeshFlags.Decal) continue;

				//if (ExtraLib.m_EntityManager.TryGetComponent(entity, out ObjectGeometryData objectGeometryData))
				//{
				//	objectGeometryData.m_Flags &= ~GeometryFlags.Overridable;
				//	ExtraLib.m_EntityManager.SetComponentData(entity, objectGeometryData);
				//}

				var prefabUI = prefab.GetComponent<UIObject>();
				if (prefabUI == null)
				{
					prefabUI = prefab.AddComponent<UIObject>();
					prefabUI.active = true;
					prefabUI.m_IsDebugObject = false;
					prefabUI.m_Icon = Icons.GetIcon(prefab);
					prefabUI.m_Priority = 1;
				}

				prefabUI.m_Group?.RemoveElement(entity);
				prefabUI.m_Group = ExtraAssetsMenu.GetOrCreateNewUIAssetCategoryPrefab(Decals.GetCatByDecalName(prefab.name), Icons.GetIcon, assetCat);
                prefabUI.m_Group.AddElement(entity);

				ExtraLib.m_EntityManager.AddOrSetComponentData(entity, prefabUI.ToComponentData());
			}
		}
	}
}
