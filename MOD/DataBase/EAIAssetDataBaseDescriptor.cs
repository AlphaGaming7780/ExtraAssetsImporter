using Colossal.IO.AssetDatabase;
using Colossal.PSI.Common;
using System;

namespace ExtraAssetsImporter.Database
{
	public readonly struct EAIAssetDatabaseDescriptor : IAssetDatabaseDescriptor<EAIAssetDatabaseDescriptor>, IEquatable<EAIAssetDatabaseDescriptor>
	{
		public static string kRootPath => EAIDatabaseManager.eaiDatabase.ActualDatabasePath;

		public bool canWriteSettings => false;

		public string name => "EAI";

		public IAssetFactory assetFactory => DefaultAssetFactory.instance;

		//public IDataSourceProvider dataSourceProvider => new CachedFileSystemDataSource(name, kRootPath, assetFactory);
		public IDataSourceProvider dataSourceProvider => new FileSystemDataSource(name, kRootPath, assetFactory);

        public DlcId dlcId => DlcId.Virtual;

        public bool Equals(EAIAssetDatabaseDescriptor other)
		{
			return true;
		}

		public override bool Equals(object obj)
		{
			return obj is EAIAssetDatabaseDescriptor;
		}

		public override int GetHashCode()
		{
			return GetType().GetHashCode();
		}

	}
}
