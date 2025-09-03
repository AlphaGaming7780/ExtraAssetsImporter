using ExtraLib;
using Game.Prefabs;
using System;
using System.IO;

namespace ExtraAssetsImporter
{
    internal class Icons
    {
        internal const string IconsResourceKey = "extraassetsimporter";
        internal static readonly string COUIBaseLocation = $"coui://{IconsResourceKey}";

        public static readonly string DecalPlaceholder = $"{COUIBaseLocation}/Icons/Placeholder/Decals.svg";
        public static readonly string NetLanesPlaceholder = $"{COUIBaseLocation}/Icons/Placeholder/NetLanes.svg";

        internal static void LoadIcons(string path)
        {
            ExtraLib.Helpers.Icons.LoadIconsFolder(IconsResourceKey, path);
        }

        internal static void UnLoadIcons(string path)
        {
            ExtraLib.Helpers.Icons.UnLoadIconsFolder(IconsResourceKey, path);
        }

        public static string GetIcon(PrefabBase prefab)
        {

            if (prefab is null) return ExtraLib.Helpers.Icons.Placeholder;

            EAI.Logger.Info($"GetIcon: {prefab.GetType().Name} | {prefab.name}");
            if (File.Exists(Path.Combine(EAI.ResourcesIcons, prefab.GetType().Name, $"{prefab.name}.svg"))) return $"{COUIBaseLocation}/Icons/{prefab.GetType().Name}/{prefab.name}.svg";

            if (prefab is SurfacePrefab)
            {
                return "Media/Game/Icons/LotTool.svg";
            }
            else if (prefab is UIAssetCategoryPrefab)
            {
                return ExtraLib.Helpers.Icons.Placeholder;
            }
            else if (prefab is UIAssetMenuPrefab)
            {
                return ExtraLib.Helpers.Icons.Placeholder;
            }

            return ExtraLib.Helpers.Icons.Placeholder;
        }

    }
}
