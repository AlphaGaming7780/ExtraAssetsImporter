using Colossal.IO.AssetDatabase;
using System;

namespace ExtraAssetsImporter.DataBase
{
	public readonly struct AssetDataBaseEAI : IAssetDatabaseDescriptor<AssetDataBaseEAI>, IEquatable<AssetDataBaseEAI>
	{
		public static string kRootPath => EAIDataBaseManager.eaiDataBase.ActualDataBasePath;

		public bool canWriteSettings => false;

		public string name => "EAI";

		public IAssetFactory assetFactory => DefaultAssetFactory.instance;

		//public IDataSourceProvider dataSourceProvider => new CachedFileSystemDataSource(name, kRootPath, assetFactory);
		public IDataSourceProvider dataSourceProvider => new FileSystemDataSource(name, kRootPath, assetFactory);

		public bool Equals(AssetDataBaseEAI other)
		{
			return true;
		}

		public override bool Equals(object obj)
		{
			return obj is AssetDataBaseEAI;
		}

		public override int GetHashCode()
		{
			return GetType().GetHashCode();
		}

	}
}
