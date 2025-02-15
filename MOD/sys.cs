using Colossal.Entities;
using Colossal.Serialization.Entities;
using ExtraLib;
using Game;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace ExtraAssetsImporter
{
    internal partial class sys : GameSystemBase
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            Enabled = false;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (purpose == Purpose.LoadGame || purpose == Purpose.NewGame)
            {

                EntityQueryDesc surfaceEntityQueryDesc = new()
                {
                    All = [ComponentType.ReadOnly<SurfaceData>()],
                    None = [ComponentType.ReadOnly<PlaceholderObjectElement>()]

                };

                EntityQuery entityQuery = GetEntityQuery(surfaceEntityQueryDesc);

                if (EL.m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(InfoviewPrefab), "None"), out PrefabBase p1) && p1 is InfoviewPrefab infoviewPrefab)
                {
                    Entity entity1 = EL.m_PrefabSystem.GetEntity(infoviewPrefab);
                    foreach (Entity entity in entityQuery.ToEntityArray(Allocator.Temp))
                    {
                        DynamicBuffer<PlaceableInfoviewItem> stuff;

                        stuff = EL.m_EntityManager.TryGetBuffer(entity, false, out stuff) ? stuff : EL.m_EntityManager.AddBuffer<PlaceableInfoviewItem>(entity);
                        stuff.Clear();
                        PlaceableInfoviewItem placeableInfoviewItem = new()
                        {
                            m_Item = entity1,
                            m_Priority = 0
                        };
                        stuff.Add(placeableInfoviewItem);
                    }
                }
            }
        }

        protected override void OnUpdate()
        {
            
        }
    }
}
