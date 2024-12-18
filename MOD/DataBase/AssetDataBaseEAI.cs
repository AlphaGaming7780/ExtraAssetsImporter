using Colossal.IO.AssetDatabase;
using Colossal.PSI.Environment;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExtraAssetsImporter.DataBase
{
	public readonly struct AssetDataBaseEAI : IAssetDatabaseDescriptor<AssetDataBaseEAI>, IEquatable<AssetDataBaseEAI>
	{
		private static readonly string kRootPath = Path.Combine(EnvPath.kContentPath, "Mods", "EAI");
		public static readonly string rootPath = kRootPath;

		public bool canWriteSettings => true;

		public string uiUri => null;

		public string uiPath => null;

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
