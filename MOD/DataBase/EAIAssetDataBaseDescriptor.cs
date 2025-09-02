using Colossal.IO.AssetDatabase;
using System;

namespace ExtraAssetsImporter.DataBase
{
	public readonly struct EAIAssetDataBaseDescriptor : IAssetDatabaseDescriptor<EAIAssetDataBaseDescriptor>, IEquatable<EAIAssetDataBaseDescriptor>
	{
		public static string kRootPath => EAIDataBaseManager.eaiDataBase.ActualDataBasePath;

		public bool canWriteSettings => false;

		public string name => "EAI";

		public IAssetFactory assetFactory => DefaultAssetFactory.instance;

		//public IDataSourceProvider dataSourceProvider => new CachedFileSystemDataSource(name, kRootPath, assetFactory);
		public IDataSourceProvider dataSourceProvider => new FileSystemDataSource(name, kRootPath, assetFactory);

		public bool Equals(EAIAssetDataBaseDescriptor other)
		{
			return true;
		}

		public override bool Equals(object obj)
		{
			return obj is EAIAssetDataBaseDescriptor;
		}

		public override int GetHashCode()
		{
			return GetType().GetHashCode();
		}

	}
}
