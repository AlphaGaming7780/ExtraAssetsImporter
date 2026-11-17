using ExtraAssetsImporter.ClassExtension;
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

        public override void Process(PrefabImportData data, Variant componentJson, PrefabBase prefab)
        {
            UIObjectJson uiObjectJson = componentJson.MakeInvariant<UIObjectJson>();
            if (uiObjectJson is null)
            {
                EAI.Logger.Error($"UIObject component JSON is null for prefab {data.FullAssetName}.");
                return;
            }
            UIObject uiObject = prefab.AddOrGetComponentOnMainThread<UIObject>();
            uiObject.m_Priority = uiObjectJson.m_Priority;
            uiObject.m_IsDebugObject = uiObjectJson.m_IsDebugObject;
            uiObject.m_Icon = uiObjectJson.m_Icon ?? uiObject.m_Icon;

            // On the asset prefab, ImportersUtils.SetupUIObject runs after this and only sets the fields the JSON leaves out.
            if (uiObjectJson.m_Group != null)
            {
                if (ImportersUtils.TryGetPrefab(uiObjectJson.m_Group, out PrefabBase groupPrefab) && groupPrefab is UIGroupPrefab group)
                    uiObject.m_Group = group;
                else
                    EAI.Logger.Warn($"Failed to get the UIGroupPrefab for m_Group with the name of {uiObjectJson.m_Group.m_Name} for the {data.FullAssetName} asset. The asset stays in its EAI category.");
            }
        }
    }
}
