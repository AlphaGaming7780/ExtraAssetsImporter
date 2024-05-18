using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.PSI.Environment;
using Extra.Lib;
using Game.Prefabs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Policy;
using UnityEngine;

namespace ExtraAssetsImporter;

internal static class EAIDataBaseManager
{
	const int DataBaseVersion = 1;
	private static readonly string pathToAssetsDataBase = EAI.pathModsData + "\\AssetsDataBase.json";
	private static readonly List<EAIAsset> ValidateAssetsDataBase = [];
	private static List<EAIAsset> AssetsDataBase = [];

	internal static void LoadDataBase()
	{
		if (!File.Exists(pathToAssetsDataBase)) return;
		try
		{
            EAIDataBase dataBase = Decoder.Decode(File.ReadAllText(pathToAssetsDataBase)).Make<EAIDataBase>();
            if (dataBase.DataBaseVersion != DataBaseVersion) return;
            AssetsDataBase = dataBase.AssetsDataBase;
        }
		catch
		{

		}
    }

    internal static void SaveValidateDataBase() 
	{
        EAIDataBase dataBase = new()
        {
            DataBaseVersion = DataBaseVersion,
            AssetsDataBase = ValidateAssetsDataBase
        };
        File.WriteAllText(pathToAssetsDataBase, Encoder.Encode(dataBase, EncodeOptions.None));
	}

	internal static void ClearNotLoadedAssetsFromFiles()
	{
		foreach(EAIAsset asset in AssetsDataBase)
		{
			if(Directory.Exists(asset.AssetPath)) Directory.Delete(asset.AssetPath, true);
		}
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
		//foreach(EAIAsset.EAIAssetDataPath dataPath in asset.subAssetsDataPath )
		//{
		//	IAssetData assetData = AssetDatabase.game.AddAsset(dataPath);
		//	//EAI.Logger.Info(assetData);
		//      }

		//PrefabAsset prefabAsset = AssetDatabase.game.AddAsset<PrefabAsset>(asset.assetDataPath);
		//PrefabBase prefabBase = (PrefabBase)prefabAsset.Load();
		//ExtraLib.m_PrefabSystem.AddPrefab(prefabBase);

		List<object> output = [];

		List<PrefabAsset> prefabAssets = [];

		foreach(string s in DefaultAssetFactory.instance.GetSupportedExtensions())
		{
			foreach(string file in Directory.GetFiles(Path.Combine(EnvPath.kStreamingDataPath, asset.AssetPath), $"*{s}"))
			{
				string assetPath = file.Replace(EnvPath.kStreamingDataPath + "\\", "");
                EAI.Logger.Info(assetPath);
                AssetDataPath assetDataPath = AssetDataPath.Create(assetPath, EscapeStrategy.None);
                try
				{
                    IAssetData assetData = AssetDatabase.game.AddAsset(assetDataPath);
					if (assetData is PrefabAsset prefabAsset) prefabAssets.Add(prefabAsset);
					if (assetData is TextureAsset textureAsset) output.Add(textureAsset.Load());
					if (assetData is SurfaceAsset surfaceAsset) output.Add(surfaceAsset.Load());
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

    //public struct EAIAssetDataPath()
    //{
    //       public static EAIAsset Null => default;
    //       public string subPath = null;
    //	public string assetName = null;

    //       public static implicit operator EAIAssetDataPath(AssetDataPath path)
    //       {

    //           EAIAssetDataPath assetDataPath = new()
    //           {
    //               subPath = path.subPath,
    //               assetName = path.assetName,
    //           };
    //           return assetDataPath;
    //       }

    //       public static implicit operator AssetDataPath(EAIAssetDataPath path)
    //       {
    //           return AssetDataPath.Create(path.subPath, path.assetName);
    //       }

    //   }

    public static EAIAsset Null => default;
    public string AssetID = AssetID;
    public int AssetHash = AssetHash;
    public string AssetPath = AssetPath;
    //   public EAIAssetDataPath assetDataPath = new();
    //public List<EAIAssetDataPath> subAssetsDataPath = [];

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
