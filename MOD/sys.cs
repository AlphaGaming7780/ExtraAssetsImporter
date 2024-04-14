using Colossal.Entities;
using Colossal.Serialization.Entities;
using Extra.Lib;
using Game;
using Game.Prefabs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

                if (ExtraLib.m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(InfoviewPrefab), "None"), out PrefabBase p1) && p1 is InfoviewPrefab infoviewPrefab)
                {
                    Entity entity1 = ExtraLib.m_PrefabSystem.GetEntity(infoviewPrefab);
                    foreach (Entity entity in entityQuery.ToEntityArray(Allocator.Temp))
                    {
                        DynamicBuffer<PlaceableInfoviewItem> stuff;

                        stuff = ExtraLib.m_EntityManager.TryGetBuffer(entity, false, out stuff) ? stuff : ExtraLib.m_EntityManager.AddBuffer<PlaceableInfoviewItem>(entity);
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
