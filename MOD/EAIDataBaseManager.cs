using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.PSI.Environment;
using Extra.Lib;
using ExtraAssetsImporter.DataBase;
using Game.Prefabs;
using System;
using System.Collections.Generic;
using System.IO;

namespace ExtraAssetsImporter;

internal static class EAIDataBaseManager
{
	const int DataBaseVersion = 2;
	private static readonly string pathToAssetsDatabase = EAI.pathModsData + "\\AssetsDataBase.json";
	private static readonly List<EAIAsset> ValidateAssetsDataBase = [];
	private static List<EAIAsset> AssetsDataBase = [];
    public static ILocalAssetDatabase assetDataBaseEAI => AssetDatabase<AssetDataBaseEAI>.instance;

    internal static void LoadDataBase()
	{
		if (!File.Exists(pathToAssetsDatabase)) return;
		try
		{
            EAIDataBase dataBase = Decoder.Decode(File.ReadAllText(pathToAssetsDatabase)).Make<EAIDataBase>();
            if (dataBase.DataBaseVersion != DataBaseVersion) return;
            AssetsDataBase = dataBase.AssetsDataBase;
        }
		catch
		{

		}
    }

    internal static void SaveValidateDataBase() 
	{
		if (!EAI.m_Setting.DeleteNotLoadedAssets)
		{
			ValidateAssetsDataBase.AddRange(AssetsDataBase);
            AssetsDataBase.Clear();
        }

        EAIDataBase dataBase = new()
		{
			DataBaseVersion = DataBaseVersion,
			AssetsDataBase =  ValidateAssetsDataBase,
        };
        string directoryPath = Path.GetDirectoryName(pathToAssetsDatabase);
        if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);
        File.WriteAllText(pathToAssetsDatabase, Encoder.Encode(dataBase, EncodeOptions.None));
	}

	internal static void ClearNotLoadedAssetsFromFiles()
	{
		List<EAIAsset> tempDataBase = new(AssetsDataBase);
		EAI.Logger.Info($"Going to remove unused asset from database, number of asset : {AssetsDataBase.Count}");
		foreach(EAIAsset asset in tempDataBase)
		{
			string path = Path.Combine(AssetDataBaseEAI.rootPath, asset.AssetPath);
			if (Directory.Exists(path))
			{
				if (!AssetsDataBase.Remove(asset))
				{
					EAI.Logger.Warn($"Failed to remove a none loaded asset at path {path} from the data base.");
					continue;
				}
                Directory.Delete(path, true);
            }
			else EAI.Logger.Warn($"Trying to delete a none loaded asset at path {path}, but this path doesn't exist.");
		}
		EAI.Logger.Info($"Removed unused asset from database, number of asset in database now : {AssetsDataBase.Count}.");
		ValidateAssetsDataBase.AddRange(AssetsDataBase);
		AssetsDataBase.Clear();
	}

	internal static void DeleteDatabase()
	{
		if(File.Exists(pathToAssetsDatabase)) File.Delete(pathToAssetsDatabase);
		if(Directory.Exists(AssetDataBaseEAI.rootPath)) Directory.Delete(AssetDataBaseEAI.rootPath, true);
	}

	private static void ValidateAssets(string AssetID)
	{
		if (AssetID == null) { EAI.Logger.Warn("Try to validate an assets with a null AssetID."); return; }
		ValidateAssets(GetEAIAsset(AssetID));
	}

    private static void ValidateAssets(EAIAsset asset)
    {
        if (asset == EAIAsset.Null) return;
        AssetsDataBase.Remove(asset);
        ValidateAssetsDataBase.Add(asset);
    }

    //   internal static void AddAssets(string AssetID, int Hash, string AssetPath)
    //{
    //	EAIAsset asset = new()
    //	{
    //		AssetID = AssetID,
    //		AssetHash = Hash
    //	};
    //	AddAssets(asset);
    //}

    internal static void AddAssets(EAIAsset asset)
	{
		if(IsAssetsInDataBase(asset)) { EAI.Logger.Warn($"Try to add {asset.AssetID} in the data base, it is already in the data base."); return; }
		ValidateAssetsDataBase.Add(asset);
	}

	internal static bool IsAssetsInDataBase(EAIAsset asset)
	{
		return IsAssetsInDataBase(asset.AssetID);
	}

	internal static bool IsAssetsInDataBase(string AssetID)
	{
		foreach(var asset in AssetsDataBase)
		{
			if(asset.AssetID == AssetID) return true;
		}

		foreach (var asset in ValidateAssetsDataBase)
		{
			if (asset.AssetID == AssetID) return true;
		}

		return false;
	}

	internal static EAIAsset GetEAIAsset(string AssetID)
	{
		foreach (var asset in AssetsDataBase)
		{
			if (asset.AssetID == AssetID) return asset;
		}
		EAI.Logger.Warn($"Try to get an asset with this ID '{AssetID}', but it's not in the dataBase" );
		return EAIAsset.Null;
	}

	internal static bool TryGetEAIAsset(string AssetID, out EAIAsset asset)
	{
		asset = AssetsDataBase.Find(asset => asset.AssetID == AssetID);
		return asset != EAIAsset.Null;
    }


    internal static int GetAssetHash(string assetFolder)
	{
		DirectoryInfo directoryInfo = new (assetFolder);
		int hash = 0;
		foreach (FileInfo file in directoryInfo.GetFiles())
		{
			hash += file.LastWriteTimeUtc.GetHashCode();
		}
		return hash;
	}

    internal static List<object> LoadAsset(string AssetID)
    {
		return LoadAsset(GetEAIAsset(AssetID));
    }

    internal static List<object> LoadAsset(EAIAsset asset)
	{
		List<object> output = [];

		List<PrefabAsset> prefabAssets = [];

		foreach(string s in DefaultAssetFactory.instance.GetSupportedExtensions())
		{
			foreach(string file in Directory.GetFiles(Path.Combine(AssetDataBaseEAI.rootPath, asset.AssetPath), $"*{s}"))
			{
				string assetPath = file.Replace(AssetDataBaseEAI.rootPath + "\\", "");
				//EAI.Logger.Info(assetPath);
				AssetDataPath assetDataPath = AssetDataPath.Create(assetPath, EscapeStrategy.None);
                try
				{
                    IAssetData assetData = assetDataBaseEAI.AddAsset(assetDataPath);
					if (assetData is PrefabAsset prefabAsset) prefabAssets.Add(prefabAsset);
					//if (assetData is TextureAsset textureAsset) output.Add(textureAsset.Load());
					//if (assetData is SurfaceAsset surfaceAsset) output.Add(surfaceAsset.Load());
                } catch (Exception e)
				{
					EAI.Logger.Warn(e);
				}
			}
		}

		foreach(PrefabAsset prefabAsset in prefabAssets)
		{
            PrefabBase prefabBase = (PrefabBase)prefabAsset.Load();
			output.Add(prefabBase);
            ExtraLib.m_PrefabSystem.AddPrefab(prefabBase);
        }

        ValidateAssets(asset);
		return output;
    }
}

internal class EAIDataBase()
{
	public int DataBaseVersion = 0;
    public List<EAIAsset> AssetsDataBase = [];
}

public struct EAIAsset(string AssetID, int AssetHash, string AssetPath)
{

    public static EAIAsset Null => default;
    public string AssetID = AssetID;
    public int AssetHash = AssetHash;
    public string AssetPath = AssetPath;

    public static bool operator ==(EAIAsset lhs, EAIAsset rhs)
    {
        return lhs.AssetID == rhs.AssetID;
    }

    public static bool operator !=(EAIAsset lhs, EAIAsset rhs)
    {
        return !(lhs == rhs);
    }

    public override readonly bool Equals(object compare)
    {
        if (compare is EAIAsset asset)
        {
            return Equals(asset);
        }

        return false;
    }

    public readonly bool Equals(EAIAsset asset)
    {
        return asset.AssetID == AssetID;
    }

    public override readonly int GetHashCode()
    {
        return AssetHash;
    }
}
