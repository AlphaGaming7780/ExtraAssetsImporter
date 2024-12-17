using Game.Prefabs;
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
            Extra.Lib.UI.Icons.LoadIconsFolder(IconsResourceKey, path);
        }

        internal static void UnLoadIcons(string path)
        {
            Extra.Lib.UI.Icons.UnLoadIconsFolder(IconsResourceKey, path);
        }

        public static string GetIcon(PrefabBase prefab)
        {

            if (prefab is null) return Extra.Lib.UI.Icons.Placeholder;

            
            if (File.Exists(Path.Combine(EAI.ResourcesIcons, prefab.GetType().Name, $"{prefab.name}.svg"))) return $"{COUIBaseLocation}/Icons/{prefab.GetType().Name}/{prefab.name}.svg";

            if (prefab is SurfacePrefab)
            {
                return "Media/Game/Icons/LotTool.svg";
            }
            else if (prefab is UIAssetCategoryPrefab)
            {

                return Extra.Lib.UI.Icons.Placeholder;
            }
            else if (prefab is UIAssetMenuPrefab)
            {
                return Extra.Lib.UI.Icons.Placeholder;
            }

            return Extra.Lib.UI.Icons.Placeholder;
        }

    }
}
