using Colossal.Json;
using ExtraAssetsImporter.AssetImporter.JSONs;
using Game.Prefabs;
using Game.UI.Editor;
using Game.UI.Widgets;
using System;

namespace ExtraAssetsImporter.AssetImporter.Components
{
    public class UIObjectComponentImporter : ComponentImporter
    {
        public override Type ComponentType => typeof(UIObject);

        public override Type PrefabType => typeof(PrefabBase);

        public override void Process(Variant componentJson, PrefabBase prefab)
        {
            UIObjectJson uiObjectJson = componentJson.Make<UIObjectJson>();
            EAI.Logger.Info($"Processing UIObject component for prefab {prefab.name}.");
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

    public class UIObjectJson : ComponentJson
    {
        public string m_Group = null;

        public int m_Priority = -1;

        public string m_Icon = null;

        public bool m_IsDebugObject = false;
    }
}
