using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.JSONs;
using ExtraAssetsImporter.AssetImporter.JSONs.Components;
using Game.Prefabs;
using System;

namespace ExtraAssetsImporter.AssetImporter.Components
{
    public class UIObjectComponent : ComponentImporter
    {
        public override Type ComponentType => typeof(UIObject);

        public override Type PrefabType => typeof(PrefabBase);

        public override ComponentJson GetDefaultJson()
        {
            return new UIObjectJson() 
            {
                m_Group = new PrefabIDJson("Prefab Type", "Prefab Name"),
            };
        }

        public override void Process(ImportData data, Variant componentJson, PrefabBase prefab)
        {
            UIObjectJson uiObjectJson = componentJson.Make<UIObjectJson>();
            if (uiObjectJson is null)
            {
                EAI.Logger.Error($"UIObject component JSON is null for prefab {prefab.name}.");
                return;
            }
            UIObject uiObject = prefab.AddOrGetComponent<UIObject>();
            uiObject.m_Priority = uiObjectJson.m_Priority;
            uiObject.m_IsDebugObject = uiObjectJson.m_IsDebugObject;
            uiObject.m_Icon = uiObjectJson.m_Icon ?? uiObject.m_Icon;
        }
    }
}
