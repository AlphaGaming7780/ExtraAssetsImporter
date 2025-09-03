using Colossal.IO.AssetDatabase;
using ExtraAssetsImporter.DataBase;
using Game.Prefabs;
using System;
using System.IO;

namespace ExtraAssetsImporter.ClassExtension
{
    public static class ILocalAssetDatabaseExtension
    {
        public static IAssetData GetAsset(this ILocalAssetDatabase assetDatabase, AssetDataPath assetDataPath)
        {
            return assetDatabase.Exists(assetDataPath, out IAssetData assetData) ? assetData : null;
        }

        public static IAssetData GetOrAddAsset(this ILocalAssetDatabase assetDatabase, AssetDataPath assetDataPath)
        {
            if (assetDataPath == null) throw new ArgumentNullException(nameof(assetDataPath));

            if(string.IsNullOrEmpty(assetDataPath.extension)) throw new ArgumentException("The assetDataPath must have a valid extension.", nameof(assetDataPath));

            string assetPath = Path.Combine(assetDatabase.rootPath, assetDataPath.subPath);
            if (!Directory.Exists(assetPath)) return null;

            string filePath = Path.Combine(assetPath, assetDataPath.assetName+assetDataPath.extension);
            if (!File.Exists(filePath)) return null;

            if (assetDatabase.Exists(assetDataPath, out IAssetData assetData))
            {
                return assetData;
            }

            return assetDatabase.AddAsset(assetDataPath);
        }

        public static bool TryGetAsset(this ILocalAssetDatabase assetDatabase, AssetDataPath assetDataPath, out IAssetData assetData )
        {
            return assetDatabase.Exists(assetDataPath, out assetData);
        }

        public static bool TryGetAsset<TAssetData>(
            this ILocalAssetDatabase assetDatabase,
            AssetDataPath assetDataPath,
            out TAssetData assetData
        )
            where TAssetData : class, IAssetData
        {
            if (assetDatabase.Exists(assetDataPath, out IAssetData rawData) && rawData is TAssetData typedData)
            {
                assetData = typedData;
                return true;
            }

            assetData = null;
            return false;
        }

        public static bool TryGetOrAddAsset<TAsset>(this ILocalAssetDatabase assetDatabase, AssetDataPath assetDataPath, out TAsset assetData ) where TAsset : class, IAssetData
        {
            if (GetOrAddAsset(assetDatabase, assetDataPath) is TAsset typedData)
            {
                assetData = typedData;
                return true;
            }
            assetData = null;
            return false;

        }

        public static T LoadPrefab<T>(this ILocalAssetDatabase assetDatabase, AssetDataPath assetDataPath) where T : PrefabBase
        {
            return TryGetOrAddAsset<PrefabAsset>(assetDatabase, assetDataPath, out PrefabAsset prefabAsset) ? prefabAsset.Load<T>() : null;  
        }

        public static bool TryLoadPrefab<T>(this ILocalAssetDatabase assetDatabase, AssetDataPath assetDataPath, out T prefab) where T : PrefabBase
        {
            if (TryGetOrAddAsset<PrefabAsset>(assetDatabase, assetDataPath, out PrefabAsset prefabAsset))
            {
                prefab = prefabAsset.Load<T>();
                return prefab != null;
            }
            prefab = null;
            return false;
        }
    }
}
